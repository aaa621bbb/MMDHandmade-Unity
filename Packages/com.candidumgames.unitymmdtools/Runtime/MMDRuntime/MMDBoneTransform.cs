using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static UMT.PMXUtilities;

namespace UMT
{
    /// <summary>
    /// Per-bone runtime data for the MMD transform solver: transform order, initial pose, constraints, IK, and solved transform state. Declares the blittable <see cref="BoneSolverConfig"/>/<see cref="BoneSolverState"/> pair used by the Burst-compiled solver.
    /// </summary>
    public sealed class MMDBoneTransform : MonoBehaviour
    {
        /// <summary>Index of this bone within the PMX bone list.</summary>
        public int boneIndex;
        /// <summary>Original PMX bone name.</summary>
        public string boneName;
        /// <summary>Parent bone in the MMD hierarchy, or null for a root bone.</summary>
        public MMDBoneTransform parentBone;
        /// <summary>Initial bone position in model space.</summary>
        public Vector3 initialModelPosition;
        /// <summary>Initial bone position relative to its parent.</summary>
        public Vector3 initialLocalPosition;
        /// <summary>Initial bone rotation relative to its parent.</summary>
        public Quaternion initialLocalRotation = Quaternion.identity;
        /// <summary>MMD transform-order level used to sort solving passes.</summary>
        public int transformLevel;
        /// <summary>PMX bone flags describing transform/constraint/IK capabilities.</summary>
        public PMXBone.Flags flags;
        /// <summary>Bone supplying rotation/translation constraint values, or null.</summary>
        public MMDBoneTransform constraintTarget;
        /// <summary>Constraint influence weight; negative values invert the constraint.</summary>
        public float constraintInfluence ;
        /// <summary>Whether the constraint reads the target's local (model) transform.</summary>
        public bool localConstraint;
        /// <summary>Whether this bone inherits rotation from its constraint target.</summary>
        public bool rotationConstraint;
        /// <summary>Whether this bone inherits translation from its constraint target.</summary>
        public bool translationConstraint;
        /// <summary>Whether this bone is solved in the after-physics pass.</summary>
        public bool afterPhysics;
        /// <summary>Whether IK solving is enabled for this bone (when it is an IK controller).</summary>
        public bool ikEnabled;
        /// <summary>IK data for this bone when it acts as an IK controller.</summary>
        public MMDBoneIKData ik = new MMDBoneIKData();

        /// <summary>
        /// Set by external pose resets (<see cref="RefreshInitialTransform"/>, physics reset); consumed by the transform manager on its next sample pass to clear this bone's solved state in the native solver cache.
        /// </summary>
        [NonSerialized] public bool solverResetPending = true;

        /// <summary>
        /// Blittable read-only per-bone solver configuration: initial pose, constraint setup, and hierarchy indices. Built once per model and shared by every solve pass.
        /// </summary>
        public struct BoneSolverConfig
        {
            /// <summary>Initial bone position in model space.</summary>
            public readonly float3 initialModelPosition;
            /// <summary>Initial bone position relative to its parent.</summary>
            public readonly float3 initialLocalPosition;
            /// <summary>Initial bone rotation relative to its parent.</summary>
            public readonly quaternion initialLocalRotation;
            /// <summary>Index of the constraint target bone, or negative when there is none.</summary>
            public readonly int constraintTargetBoneIndex;
            /// <summary>Constraint influence weight; negative values invert the constraint.</summary>
            public readonly float constraintInfluence;
            /// <summary>Whether the constraint reads the target's local (model) transform.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool localConstraint;
            /// <summary>Whether this bone inherits rotation from its constraint target.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool rotationConstraint;
            /// <summary>Whether this bone inherits translation from its constraint target.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool translationConstraint;
            /// <summary>Whether this bone may be translated.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool translatable;
            /// <summary>Whether this bone may be rotated.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool rotatable;
            /// <summary>Index of the parent bone, or negative for a root bone.</summary>
            public readonly int parentBoneIndex;

            /// <summary>
            /// Initializes solver configuration from an <see cref="MMDBoneTransform"/> component.
            /// </summary>
            /// <param name="bone">Source bone component.</param>
            public BoneSolverConfig(MMDBoneTransform bone) : this(bone.initialModelPosition, bone.initialLocalPosition, bone.initialLocalRotation, bone.constraintInfluence, bone.constraintTarget != null ? bone.constraintTarget.boneIndex : -1, bone.localConstraint, bone.rotationConstraint, bone.translationConstraint, bone.flags.HasFlag(PMXBone.Flags.Translatable), bone.flags.HasFlag(PMXBone.Flags.Rotatable), bone.parentBone != null ? bone.parentBone.boneIndex : -1)
            {
            }

            /// <summary>
            /// Initializes solver configuration from explicit initial and constraint values.
            /// </summary>
            /// <param name="initialModelPosition">Initial bone position in model space.</param>
            /// <param name="initialLocalPosition">Initial bone position relative to its parent.</param>
            /// <param name="initialLocalRotation">Initial bone rotation relative to its parent.</param>
            /// <param name="constraintInfluence">Constraint influence weight; negative inverts the constraint.</param>
            /// <param name="constraintTargetBoneIndex">Index of the constraint target bone, or negative when none.</param>
            /// <param name="localConstraint">Whether the constraint reads the target's local transform.</param>
            /// <param name="rotationConstraint">Whether this bone inherits rotation from its constraint target.</param>
            /// <param name="translationConstraint">Whether this bone inherits translation from its constraint target.</param>
            /// <param name="translatable">Whether this bone may be translated.</param>
            /// <param name="rotatable">Whether this bone may be rotated.</param>
            /// <param name="parentBoneIndex">Index of the parent bone, or negative for a root bone.</param>
            public BoneSolverConfig(float3 initialModelPosition, float3 initialLocalPosition, quaternion initialLocalRotation, float constraintInfluence, int constraintTargetBoneIndex, bool localConstraint, bool rotationConstraint, bool translationConstraint, bool translatable, bool rotatable, int parentBoneIndex)
            {
                this.initialModelPosition = initialModelPosition;
                this.initialLocalPosition = initialLocalPosition;
                this.initialLocalRotation = initialLocalRotation;
                this.constraintTargetBoneIndex = constraintTargetBoneIndex;
                this.constraintInfluence = constraintInfluence;
                this.localConstraint = localConstraint;
                this.rotationConstraint = rotationConstraint;
                this.translationConstraint = translationConstraint;
                this.translatable = translatable;
                this.rotatable = rotatable;
                this.parentBoneIndex = parentBoneIndex;
            }
        };

        /// <summary>
        /// Blittable mutable per-bone solver state: the current local pose, constraint accumulators, IK rotation, world matrix, and solved transform, reset toward the initial pose each solve pass.
        /// </summary>
        public struct BoneSolverState
        {
            /// <summary>Current local position before solving.</summary>
            public float3 localPosition;
            /// <summary>Current local rotation before solving.</summary>
            public quaternion localRotation;
            /// <summary>Accumulated translation contributed to constraint targets.</summary>
            public float3 translationConstraintValue;
            /// <summary>Accumulated rotation contributed to constraint targets.</summary>
            public quaternion rotationConstraintValue;
            /// <summary>IK rotation delta applied on top of the local rotation.</summary>
            public quaternion ikRotation;
            /// <summary>World transform matrix derived from the parent world matrix and the solved local pose.</summary>
            public float4x4 worldMatrix;
            /// <summary>Local position used as the IK-link base before applying IK rotation.</summary>
            public float3 localPositionForIKLink;
            /// <summary>Local rotation used as the IK-link base before applying IK rotation.</summary>
            public quaternion localRotationForIKLink;
            /// <summary>Final solved local position flushed back to Unity.</summary>
            public float3 solvedLocalPosition;
            /// <summary>Final solved local rotation flushed back to Unity.</summary>
            public quaternion solvedLocalRotation;
            /// <summary>Whether this bone has a valid solved transform this pass.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public bool hasSolvedTransform;
            /// <summary>Whether the solved transform came from the IK solver.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public bool solvedByIK;
            /// <summary>Whether <see cref="worldMatrix"/> is already up to date for the current physics prepare pass; cleared at the start of each physics step and set only by the prepare pass so shared ancestor chains are computed once per step instead of once per rigid body.</summary>
            [MarshalAs(UnmanagedType.U1)]
            public bool worldMatrixValid;

            /// <summary>
            /// Creates a default solver state with identity rotations and world matrix, zero positions, and no solved transform.
            /// </summary>
            /// <returns>A default-initialized <see cref="BoneSolverState"/>.</returns>
            public static BoneSolverState CreateDefault()
            {
                BoneSolverState state = default;
                state.localRotation = quaternion.identity;
                state.rotationConstraintValue = quaternion.identity;
                state.ikRotation = quaternion.identity;
                state.worldMatrix = float4x4.identity;
                state.localRotationForIKLink = quaternion.identity;
                state.solvedLocalRotation = quaternion.identity;
                return state;
            }
        };

        /// <summary>
        /// Captures the current Unity local transform as this bone's initial pose and requests a solver reset so the native cache re-seeds from it on the next sample pass.
        /// </summary>
        public void RefreshInitialTransform()
        {
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
            solverResetPending = true;
        }


        /// <summary>
        /// Solves a bone's local and world matrices, applying constraints, then writes the result back into the solver context.
        /// </summary>
        /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
        /// <param name="boneIndex">Index of the bone to update.</param>
        internal static void UpdateLocalMatrix(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex)
        {
            BoneMath.UpdateLocalMatrixInternal(ref transformManagerContext, boneIndex);
        }

        /// <summary>
        /// Recomputes a bone's world matrix from its sampled local transform without solving constraints, marking it as not yet solved.
        /// </summary>
        /// <param name="config">Bone solver configuration.</param>
        /// <param name="state">Bone solver state to update.</param>
        /// <param name="parentWorldMatrix">World matrix of the bone's parent.</param>
        internal static void ObserveLocalMatrix(in BoneSolverConfig config, ref BoneSolverState state, in float4x4 parentWorldMatrix)
        {
            BoneMath.ObserveLocalMatrixInternal(in config, ref state, parentWorldMatrix);
        }

        /// <summary>
        /// Recomputes a bone's world matrix from its IK-link base transform plus the current IK rotation, marking it solved by IK.
        /// </summary>
        /// <param name="state">Bone solver state to update.</param>
        /// <param name="parentWorldMatrix">World matrix of the bone's parent.</param>
        internal static void UpdateLocalMatrixIKLink(ref BoneSolverState state, in float4x4 parentWorldMatrix)
        {
            BoneMath.UpdateLocalMatrixIKLinkInternal(ref state, parentWorldMatrix);
        }

        /// <summary>
        /// Resets a bone's solver state to the start of a solve pass, restoring either the initial pose (when the current transform matches the last solved transform) or the sampled transform.
        /// </summary>
        /// <param name="config">Bone solver configuration.</param>
        /// <param name="state">Bone solver state to reset.</param>
        /// <param name="currentLocalPosition">Current sampled local position.</param>
        /// <param name="currentLocalRotation">Current sampled local rotation.</param>
        internal static void ResetRuntimeData(in BoneSolverConfig config, ref BoneSolverState state, in float3 currentLocalPosition, in quaternion currentLocalRotation)
        {
            BoneMath.ResetRuntimeDataInternal(in config, ref state, in currentLocalPosition, in currentLocalRotation);
        }

        /// <summary>
        /// Returns the world matrix of a bone's parent, or the root parent world matrix for a root bone.
        /// </summary>
        /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
        /// <param name="boneIndex">Index of the bone whose parent matrix is requested.</param>
        /// <returns>The parent (or root) world matrix.</returns>
        internal static float4x4 GetParentWorldMatrix(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex)
        {
            BoneMath.GetParentWorldMatrixInternal(ref transformManagerContext, boneIndex, out float4x4 parentWorldMatrix);
            return parentWorldMatrix;
        }

        /// <summary>
        /// Directly applies a local position and rotation to a bone, recomputing its world matrix and marking it solved (not by IK).
        /// </summary>
        /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
        /// <param name="boneIndex">Index of the bone to update.</param>
        /// <param name="localPosition">Local position to apply.</param>
        /// <param name="localRotation">Local rotation to apply.</param>
        internal static void ApplyLocalTransformToBone(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex, in float3 localPosition, in quaternion localRotation)
        {
            BoneMath.ApplyLocalTransformToBoneInternal(ref transformManagerContext, boneIndex, in localPosition, in localRotation);
        }

        [BurstCompile]
        private static class BoneMath
        {
            /// <summary>
            /// Burst implementation that recomputes a bone's solved local pose and world matrix, applying any MMD rotation/translation constraint from its constraint target bone before composing the final transform.
            /// </summary>
            /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
            /// <param name="boneIndex">Index of the bone whose matrix is recomputed.</param>
            [BurstCompile]
            internal static void UpdateLocalMatrixInternal(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex)
            {
                ref BoneSolverConfig config = ref ElementAt(transformManagerContext.boneConfigData, boneIndex);
                ref BoneSolverState state = ref ElementAt(transformManagerContext.boneStateData, boneIndex);
                float3 nextPosition = state.localPosition;
                quaternion nextRotation = state.localRotation;
                float3 localPositionDelta = LocalPositionDelta(state.localPosition, config.initialLocalPosition);
                quaternion localRotationDelta = LocalRotationDelta(state.localRotation, config.initialLocalRotation);
                float3 translationConstraintValue = localPositionDelta;
                quaternion rotationConstraintValue = localRotationDelta;

                if (config.constraintTargetBoneIndex != -1 && !Approximately(config.constraintInfluence, 0.0f) && transformManagerContext.solveConstraints)
                {
                    // Snapshot the constraint target by value before any writes so a degenerate self-targeting constraint still reads the pre-update values.
                    BoneSolverState constraintState = config.constraintTargetBoneIndex >= 0 ? transformManagerContext.boneStateData[config.constraintTargetBoneIndex] : BoneSolverState.CreateDefault();
                    float3 constraintInitialModelPosition = config.constraintTargetBoneIndex >= 0 ? transformManagerContext.boneConfigData[config.constraintTargetBoneIndex].initialModelPosition : float3.zero;
                    if (config.rotationConstraint)
                    {
                        quaternion sourceRotation = config.localConstraint ? ModelRotation(constraintState.worldMatrix) : math.mul(constraintState.rotationConstraintValue, constraintState.ikRotation);
                        quaternion weightedRotation = config.constraintInfluence >= 0.0f ? math.slerp(quaternion.identity, sourceRotation, config.constraintInfluence) : math.slerp(quaternion.identity, math.inverse(sourceRotation), -config.constraintInfluence);
                        nextRotation = math.mul(weightedRotation, nextRotation);
                        rotationConstraintValue = math.mul(weightedRotation, localRotationDelta);
                    }

                    if (config.translationConstraint)
                    {
                        float3 sourceTranslation = config.localConstraint ? math.transform(constraintState.worldMatrix, float3.zero)- constraintInitialModelPosition : constraintState.translationConstraintValue;
                        float3 weightedTranslation = sourceTranslation * config.constraintInfluence;
                        nextPosition += weightedTranslation;
                        translationConstraintValue = weightedTranslation + localPositionDelta;
                    }
                }

                GetParentWorldMatrixInternal(ref transformManagerContext, boneIndex, out float4x4 parentWorldMatrix);
                state.rotationConstraintValue = rotationConstraintValue;
                state.translationConstraintValue = translationConstraintValue;
                state.localPositionForIKLink = nextPosition;
                state.localRotationForIKLink = nextRotation;
                state.solvedLocalPosition = nextPosition;
                state.solvedLocalRotation = math.normalize(math.mul(nextRotation, state.ikRotation));
                float4x4 localMatrix = new float4x4(state.solvedLocalRotation, nextPosition);
                state.worldMatrix = math.mul(parentWorldMatrix, localMatrix);
                state.hasSolvedTransform = true;
            }

            /// <summary>
            /// Burst implementation that resets a bone's solver state toward its initial pose, restoring the initial local transform when the sampled transform still matches the previously solved value.
            /// </summary>
            /// <param name="config">Bone solver configuration.</param>
            /// <param name="state">Bone solver state to reset, updated in place.</param>
            /// <param name="currentLocalPosition">Local position currently sampled from the Unity transform.</param>
            /// <param name="currentLocalRotation">Local rotation currently sampled from the Unity transform.</param>
            [BurstCompile]
            internal static void ResetRuntimeDataInternal(in BoneSolverConfig config, ref BoneSolverState state, in float3 currentLocalPosition, in quaternion currentLocalRotation)
            {
                bool currentSolvedTransform = IsCurrentSolvedTransform(state.hasSolvedTransform, currentLocalPosition, currentLocalRotation, state.solvedLocalPosition, state.solvedLocalRotation);
                state = BoneSolverState.CreateDefault();
                state.localPosition = currentSolvedTransform ? config.initialLocalPosition : currentLocalPosition;
                state.localRotation = currentSolvedTransform ? config.initialLocalRotation : currentLocalRotation;
                state.localPositionForIKLink = state.localPosition;
                state.localRotationForIKLink = state.localRotation;
            }

            /// <summary>
            /// Burst implementation that recomputes a bone's world matrix and constraint deltas from its current local transform without marking it solved (used to observe externally driven bones).
            /// </summary>
            /// <param name="config">Bone solver configuration.</param>
            /// <param name="state">Bone solver state to update in place.</param>
            /// <param name="parentWorldMatrix">World matrix of the bone's parent.</param>
            [BurstCompile]
            internal static void ObserveLocalMatrixInternal(in BoneSolverConfig config, ref BoneSolverState state, in float4x4 parentWorldMatrix)
            {
                state.translationConstraintValue = LocalPositionDelta(state.localPosition, config.initialLocalPosition);
                state.rotationConstraintValue = LocalRotationDelta(state.localRotation, config.initialLocalRotation);
                state.localPositionForIKLink = state.localPosition;
                state.localRotationForIKLink = state.localRotation;
                float4x4 localMatrix = new float4x4(state.localRotation, state.localPosition);
                state.worldMatrix = math.mul(parentWorldMatrix, localMatrix);
                state.hasSolvedTransform = false;
            }

            /// <summary>
            /// Burst implementation that rebuilds an IK link bone's world matrix from its IK-link local transform and IK rotation, marking the bone as solved by IK.
            /// </summary>
            /// <param name="state">Bone solver state to update in place.</param>
            /// <param name="parentWorldMatrix">World matrix of the bone's parent.</param>
            [BurstCompile]
            internal static void UpdateLocalMatrixIKLinkInternal(ref BoneSolverState state, in float4x4 parentWorldMatrix)
            {
                quaternion nextRotation = math.normalize(math.mul(state.localRotationForIKLink, state.ikRotation));
                float4x4 localMatrix = new float4x4(nextRotation, state.localPositionForIKLink);
                state.worldMatrix = math.mul(parentWorldMatrix, localMatrix);
                state.solvedLocalPosition = state.localPositionForIKLink;
                state.solvedLocalRotation = nextRotation;
                state.hasSolvedTransform = true;
                state.solvedByIK = true;
            }

            private static quaternion ModelRotation(float4x4 worldMatrix)
            {
                return math.normalize(new quaternion(worldMatrix));
            }

            private static float3 LocalPositionDelta(float3 localPosition, float3 initialLocalPosition)
            {
                return localPosition - initialLocalPosition;
            }

            private static quaternion LocalRotationDelta(quaternion localRotation, quaternion initialLocalRotation)
            {
                return math.normalize(math.mul(localRotation, math.inverse(initialLocalRotation)));
            }

            private static bool IsCurrentSolvedTransform(bool hasSolvedTransform, float3 currentLocalPosition, quaternion currentLocalRotation, float3 solvedLocalPosition, quaternion solvedLocalRotation)
            {
                return hasSolvedTransform &&
                    math.lengthsq(currentLocalPosition - solvedLocalPosition) == 0.0f &&
                    math.abs(math.dot(currentLocalRotation, solvedLocalRotation) - 1.0f) <= 0.000001f;
            }

            private static bool Approximately(float a, float b)
            {
                return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), math.EPSILON * 8.0f);
            }

            /// <summary>
            /// Burst implementation that resolves a bone's parent world matrix, falling back to the solver's root parent world matrix when the bone has no parent.
            /// </summary>
            /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
            /// <param name="boneIndex">Index of the bone whose parent matrix is resolved.</param>
            /// <param name="parentWorldMatrix">Receives the parent's world matrix.</param>
            [BurstCompile]
            internal static void GetParentWorldMatrixInternal(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex, out float4x4 parentWorldMatrix)
            {
                int parentBoneIndex = ElementAt(transformManagerContext.boneConfigData, boneIndex).parentBoneIndex;
                parentWorldMatrix = parentBoneIndex >= 0 ? ElementAt(transformManagerContext.boneStateData, parentBoneIndex).worldMatrix : transformManagerContext.rootParentWorldMatrix;
            }

            /// <summary>
            /// Burst implementation that writes a local position and rotation directly onto a bone, recomputing its world matrix and marking it solved but not driven by IK.
            /// </summary>
            /// <param name="transformManagerContext">Solver context holding all bone solver data.</param>
            /// <param name="boneIndex">Index of the bone to update.</param>
            /// <param name="localPosition">Local position to apply.</param>
            /// <param name="localRotation">Local rotation to apply.</param>
            [BurstCompile]
            internal static void ApplyLocalTransformToBoneInternal(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex, in float3 localPosition, in quaternion localRotation)
            {
                GetParentWorldMatrixInternal(ref transformManagerContext, boneIndex, out float4x4 parentWorldMatrix);
                ref BoneSolverState state = ref ElementAt(transformManagerContext.boneStateData, boneIndex);
                state.localPosition = localPosition;
                state.localRotation = localRotation;
                state.localPositionForIKLink = localPosition;
                state.localRotationForIKLink = localRotation;
                float4x4 localMatrix = new float4x4(localRotation, localPosition);
                state.worldMatrix = math.mul(parentWorldMatrix, localMatrix);
                state.solvedLocalPosition = localPosition;
                state.solvedLocalRotation = localRotation;
                state.hasSolvedTransform = true;
                state.solvedByIK = false;
            }
        }
    }

    /// <summary>
    /// IK controller data for a bone: the IK target, iteration count, per-iteration angle limit, and the ordered IK link chain.
    /// </summary>
    [Serializable]
    public sealed class MMDBoneIKData
    {
        /// <summary>End-effector bone the IK solver drives toward the controller.</summary>
        public MMDBoneTransform target;
        /// <summary>Maximum number of IK solver iterations.</summary>
        public int iterations;
        /// <summary>Maximum rotation angle applied per IK step.</summary>
        public float angleLimit;
        /// <summary>Ordered chain of IK links from the effector toward the root.</summary>
        public MMDBoneIKLinkData[] links = Array.Empty<MMDBoneIKLinkData>();
    }

    /// <summary>
    /// A single IK link in an IK chain, with its bone, optional per-axis angle limits, and the derived fix-axis and Euler decomposition order used while solving.
    /// </summary>
    [Serializable]
    public sealed class MMDBoneIKLinkData
    {
        /// <summary>Bone rotated by this IK link.</summary>
        public MMDBoneTransform bone;
        /// <summary>Whether this link has per-axis angle limits.</summary>
        public bool hasAngleLimit;
        /// <summary>Lower per-axis rotation limit, in radians.</summary>
        public float3 lowerLimit;
        /// <summary>Upper per-axis rotation limit, in radians.</summary>
        public float3 upperLimit;
        /// <summary>Axis the IK rotation is constrained to, derived from the angle limits.</summary>
        public MMDIKFixAxis fixAxis;
        /// <summary>Euler decomposition order used when clamping the IK rotation.</summary>
        public MMDIKEulerOrder eulerOrder;
    }

    /// <summary>
    /// Axis constraint applied to an IK link's rotation, derived from its angle limits.
    /// </summary>
    public enum MMDIKFixAxis : byte
    {
        /// <summary>
        /// No axis constraint.
        /// </summary>
        None,
        /// <summary>
        /// The link is fully fixed (no rotation).
        /// </summary>
        Fix,
        /// <summary>
        /// Rotation is constrained to the X axis.
        /// </summary>
        X,
        /// <summary>
        /// Rotation is constrained to the Y axis.
        /// </summary>
        Y,
        /// <summary>
        /// Rotation is constrained to the Z axis.
        /// </summary>
        Z,
    }

    /// <summary>
    /// Euler decomposition order used when clamping an IK link's rotation to its angle limits.
    /// </summary>
    public enum MMDIKEulerOrder : byte
    {
        /// <summary>
        /// Z-X-Y decomposition order.
        /// </summary>
        ZXY,
        /// <summary>
        /// X-Y-Z decomposition order.
        /// </summary>
        XYZ,
        /// <summary>
        /// Y-Z-X decomposition order.
        /// </summary>
        YZX,
    }
}
