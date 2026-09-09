using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using static UMT.PMXUtilities;

namespace UMT
{
    /// <summary>
    /// Coordinates native Bullet-backed MMD rigid-body physics: builds rigid bodies, joints, optional ground collision, and physics runtime data from the <see cref="PMXModel"/>, and drives the simulation each frame in concert with <see cref="MMDTransformManager"/>.
    /// </summary>
    [RequireComponent(typeof(MMDTransformManager))]
    public sealed class MMDPhysicsManager : MonoBehaviour
    {
        private const float k_MMDPhysicsGravity = 98.0f;
        private const byte k_MMDGroundCollisionGroup = 15;
        private const short k_MMDGroundCollisionMask = -1;
        private const int k_SolverIterations = 4;
        private const int k_MaxSubSteps = 120;
        private const float k_FixedTimeStep = 1.0f / 120.0f;
        /// <summary>MMD's reference solver iteration count; the spring damping compensation below is expressed relative to it.</summary>
        private const int k_MMDReferenceSolverIterations = 4;
        /// <summary>Bullet's default convex collision margin, in MMD units; scaled into Unity units for the native config.</summary>
        private const float k_BulletConvexMargin = 0.04f;
        /// <summary>Bullet spring damping for Spring6DOF translation rows: solverIterations / referenceIterations keeps the spring motor's target velocity matched to the MMD reference.</summary>
        private const float k_SpringTranslationDamping = (float)k_SolverIterations / k_MMDReferenceSolverIterations;
        /// <summary>Bullet spring damping for Spring6DOF rotation rows: solverIterations / (referenceIterations * scale^2). Bullet's motor-based spring drives targetVelocity = fps * (damping / iterations) * stiffness * angle with impulse clamp stiffness * angle / fps; the clamp scales correctly with the scale^2 stiffness factor (inertia grows with scale^2 at fixed mass) but the velocity term carries no inertia, so without this compensation the meter-scale simulation leaves MMD's operating regime - historically Unity's rotation springs were much SOFTER than MMD because the tiny meter-scale inertias fell into the motor's velocity-limited regime. With it, target velocity, impulse clamp, and the regime boundary all match MMD's unit-scale reference exactly.</summary>
        private const float k_SpringRotationDamping = (float)k_SolverIterations / (k_MMDReferenceSolverIterations * MMDConstants.k_MMDUnitToUnityUnit * MMDConstants.k_MMDUnitToUnityUnit);
        /// <summary>Redundant point-to-point constraint copies per fully-locked-translation joint (tLim min == max == 0). Each copy re-solves those equality rows once more per solver iteration, so spring-less locked hair chains survive fast motion at MMD's 4 iterations without firming up rotation limits, springs, or contacts; the converged pose is unchanged. Empirically the chains need ~12-iteration-equivalent convergence, hence 2 copies at 4 iterations.</summary>
        private const int k_LockedTranslationReinforceCount = 2;

        /// <summary>Rigid-body components managed by this physics manager.</summary>
        public MMDRigidBody[] rigidBodies = Array.Empty<MMDRigidBody>();
        /// <summary>Joint components managed by this physics manager.</summary>
        public MMDJoint[] joints = Array.Empty<MMDJoint>();
        /// <summary>Whether the static ground collider is enabled.</summary>
        public bool enableGroundCollision = true;
        /// <summary>Random seed used to reset the deterministic physics simulation.</summary>
        public uint physicsSeed = 0;

        /// <summary>
        /// Mutable native physics solver state: the Bullet context, transform/index buffers, rigid-body simulation data, and whether the initial pose has been seeded.
        /// </summary>
        internal struct PhysicsSolverContext
        {
            /// <summary>Native Bullet physics context wrapper.</summary>
            internal MMDBulletPhysics bulletPhysicsContext;
            /// <summary>Scratch buffer of rigid-body world transforms.</summary>
            internal NativeArray<float4x4> worldTransforms;
            /// <summary>Scratch buffer of rigid-body indices paired with <see cref="worldTransforms"/>, prefilled with the identity mapping at initialization.</summary>
            internal NativeArray<int> rigidBodyIndices;
            /// <summary>Indices of kinetic (bone-driven) rigid bodies, precomputed at initialization.</summary>
            internal NativeArray<int> kineticRigidBodyIndices;
            /// <summary>Indices of simulated rigid bodies with related bones, sorted by (bone transform level, related bone index), precomputed at initialization.</summary>
            internal NativeArray<int> sortedSimulatedRigidBodyIndices;
            /// <summary>Per-rigid-body simulation data mirrored to the native context.</summary>
            internal NativeArray<MMDRigidBody.RigidBodySimulationData> rigidBodySimulationData;
            /// <summary>Kinetic-body world targets of the previous physics frame (indexed like <see cref="kineticRigidBodyIndices"/>), interpolated toward the current targets across the fixed substeps so kinematic bodies move continuously with Bullet-derived velocities instead of teleporting once per frame.</summary>
            internal NativeArray<float4x4> previousKineticTargets;
            /// <summary>Scratch: current-frame kinetic-body world targets.</summary>
            internal NativeArray<float4x4> currentKineticTargets;
            /// <summary>Unconsumed simulation time carried between frames so variable frame times map onto whole fixed substeps without drifting.</summary>
            internal float timeAccumulator;
            /// <summary>Whether the initial bone-driven rigid-body pose has been applied.</summary>
            [MarshalAs(UnmanagedType.U1)]
            internal bool initialPoseApplied;
        }
        private PhysicsSolverContext m_PhysicsSolverContext;

        /// <summary>Transform access array over the rigid-body owner objects driving the flush transform job, rebuilt with the runtime data.</summary>
        private TransformAccessArray m_RigidBodyTransformAccess;

        /// <summary>
        /// Writes each rigid body's world transform onto its owner object's Unity transform.
        /// </summary>
        [BurstCompile]
        private struct FlushRigidBodyTransformsJob : IJobParallelForTransform
        {
            /// <summary>Rigid-body world transforms, indexed by rigid-body array position.</summary>
            [ReadOnly] internal NativeArray<float4x4> worldTransforms;

            public void Execute(int index, TransformAccess rigidBodyTransform)
            {
                float4x4 worldTransform = worldTransforms[index];
                rigidBodyTransform.SetPositionAndRotation(worldTransform.c3.xyz, new quaternion(worldTransform));
            }
        }

        /// <summary>
        /// Reference to this manager's mutable physics solver context.
        /// </summary>
        internal ref PhysicsSolverContext Context => ref m_PhysicsSolverContext;

        private void OnEnable()
        {
            RebuildRuntimeData();
        }

        private void OnDisable()
        {
            DisposePhysics();
        }

        /// <summary>
        /// (Re)creates the native physics context with MMD gravity and solver settings, rebuilds runtime data, and builds rigid bodies, joints, and ground collision.
        /// </summary>
        internal void Initialize()
        {
            DisposePhysics();
            m_PhysicsSolverContext.bulletPhysicsContext = CreateConfiguredBulletPhysics();

            RebuildRuntimeData();
            BuildRigidBodies();
            BuildJoints();
            BuildGround();
        }

        /// <summary>
        /// Resets the native simulation with <see cref="physicsSeed"/>, restores simulated bones to their initial pose, and clears the initial-pose-applied flag.
        /// </summary>
        internal void ResetPhysics()
        {
            m_PhysicsSolverContext.bulletPhysicsContext.Reset(physicsSeed);
            ResetSimulatedBoneTransformsToInitial();
            m_PhysicsSolverContext.initialPoseApplied = false;
        }

        /// <summary>
        /// Seeds or syncs rigid bodies from bone transforms, steps the simulation, and applies dynamic rigid-body results back onto their related bones.
        /// </summary>
        /// <param name="physicsElapsedTime">Elapsed time to simulate; zero or less skips stepping.</param>
        /// <param name="transformManagerContext">Transform solver context providing bone matrices.</param>
        /// <param name="runtimeContext">Physics solver context to advance.</param>
        internal static void TransformPhysics(float physicsElapsedTime, ref MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
        {
            PhysicsMath.TransformPhysicsInternal(physicsElapsedTime, ref transformManagerContext, ref runtimeContext);
        }

        /// <summary>
        /// Builds a standalone physics solver context from a model: creates the native context, allocates buffers, fills rigid-body simulation data, builds rigid bodies, joints, ground, and resets the simulation.
        /// </summary>
        /// <param name="model">PMX model providing rigid bodies and joints.</param>
        /// <param name="seed">Random seed used to reset the simulation.</param>
        /// <param name="enableGroundCollision">Whether to build the ground collider enabled.</param>
        /// <param name="runtimeContext">Physics solver context to initialize.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is null.</exception>
        internal static void InitializePhysicsContext(PMXModel model, uint seed, bool enableGroundCollision, ref PhysicsSolverContext runtimeContext)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            DisposePhysicsContext(ref runtimeContext);
            runtimeContext.bulletPhysicsContext = CreateConfiguredBulletPhysics();
            ResizePersistent(ref runtimeContext.rigidBodySimulationData, model.rigidBodies.Length);
            ResizePersistent(ref runtimeContext.worldTransforms, model.rigidBodies.Length);
            ResizePersistent(ref runtimeContext.rigidBodyIndices, model.rigidBodies.Length);

            for (int i = 0; i < model.rigidBodies.Length; ++i)
            {
                PMXRigidBody source = model.rigidBodies[i];
                bool hasRelatedBone = source.relatedBoneIndex >= 0 && source.relatedBoneIndex < model.bones.Length;
                runtimeContext.rigidBodySimulationData[i] = new MMDRigidBody.RigidBodySimulationData
                    {
                        rigidBodyIndex = i,
                        relatedBoneIndex = source.relatedBoneIndex,
                        groupIndex = source.groupIndex,
                        collisionGroupMask = source.collisionGroupMask,
                        shape = source.shape,
                        size = source.size,
                        position = source.position,
                        rotation = source.rotation,
                        mass = source.mass,
                        linearDamping = source.linearDamping,
                        angularDamping = source.angularDamping,
                        restitution = source.restitution,
                        friction = source.friction,
                        mode = source.mode,
                        initialTransform = float4x4.identity,
                        hasRelatedBone = hasRelatedBone,
                        boneLocalTransform = float4x4.identity,
                        boneModelPosition = hasRelatedBone ? model.bones[source.relatedBoneIndex].position : float3.zero,
                        initialWorldTransform = float4x4.identity,
                        boneTransformLevel = hasRelatedBone ? model.bones[source.relatedBoneIndex].transformLevel : -1,
                    };
            }

            PhysicsMath.ComputeRigidBodyTransforms(ref runtimeContext.rigidBodySimulationData);
            PhysicsMath.BuildStaticRigidBodyLists(ref runtimeContext);
            runtimeContext.bulletPhysicsContext.BuildRigidBodies(runtimeContext.rigidBodySimulationData);
            runtimeContext.bulletPhysicsContext.BuildJoints(BuildPMXJoints(model, runtimeContext.rigidBodySimulationData));
            runtimeContext.bulletPhysicsContext.BuildGround(enableGroundCollision, k_MMDGroundCollisionGroup, k_MMDGroundCollisionMask);
            ResetPhysicsContext(seed, ref runtimeContext);
        }

        /// <summary>
        /// Creates the native Bullet context with MMD gravity/solver settings and applies the tunable configuration: spring damping compensation (so the motor-based 6DOF springs reproduce MMD's unit-scale reference at Unity's meter scale) and locked-translation reinforcement (so spring-less locked chains survive fast motion at MMD's 4 iterations). Ground plane, unit scale, and convex margin restate the historical defaults.
        /// </summary>
        /// <returns>The configured native physics context wrapper.</returns>
        private static MMDBulletPhysics CreateConfiguredBulletPhysics()
        {
            MMDBulletPhysics bulletPhysics = new MMDBulletPhysics(new float3(0.0f, -k_MMDPhysicsGravity * MMDConstants.k_MMDUnitToUnityUnit, 0.0f), k_SolverIterations, k_MaxSubSteps, k_FixedTimeStep);
            bulletPhysics.SetConfig(new MMDBulletPhysics.NativeConfig
            {
                groundNormal = new float3(0.0f, 1.0f, 0.0f),
                groundConstant = 0.0f,
                mmdUnitToUnityUnit = MMDConstants.k_MMDUnitToUnityUnit,
                convexMargin = k_BulletConvexMargin * MMDConstants.k_MMDUnitToUnityUnit,
                springTranslationDamping = k_SpringTranslationDamping,
                springRotationDamping = k_SpringRotationDamping,
                lockedTranslationReinforce = k_LockedTranslationReinforceCount,
            });
            return bulletPhysics;
        }

        /// <summary>
        /// Resets a physics solver context's native simulation with the given seed and clears its initial-pose-applied flag, if the context is valid.
        /// </summary>
        /// <param name="seed">Random seed used to reset the simulation.</param>
        /// <param name="runtimeContext">Physics solver context to reset.</param>
        internal static void ResetPhysicsContext(uint seed, ref PhysicsSolverContext runtimeContext)
        {
            if (!runtimeContext.bulletPhysicsContext.isValid)
            {
                return;
            }

            runtimeContext.bulletPhysicsContext.Reset(seed);
            runtimeContext.initialPoseApplied = false;
        }

        /// <summary>
        /// Disposes a physics solver context's native context and native arrays, resetting it to default.
        /// </summary>
        /// <param name="runtimeContext">Physics solver context to dispose.</param>
        internal static void DisposePhysicsContext(ref PhysicsSolverContext runtimeContext)
        {
            if (runtimeContext.bulletPhysicsContext.isValid)
            {
                runtimeContext.bulletPhysicsContext.Dispose();
            }

            DisposeNativeArray(ref runtimeContext.worldTransforms);
            DisposeNativeArray(ref runtimeContext.rigidBodyIndices);
            DisposeNativeArray(ref runtimeContext.kineticRigidBodyIndices);
            DisposeNativeArray(ref runtimeContext.sortedSimulatedRigidBodyIndices);
            DisposeNativeArray(ref runtimeContext.rigidBodySimulationData);
            DisposeNativeArray(ref runtimeContext.previousKineticTargets);
            DisposeNativeArray(ref runtimeContext.currentKineticTargets);
            runtimeContext = default;
        }

        /// <summary>
        /// Fills a per-bone boolean mask marking bones driven by non-kinetic (physics-controlled) rigid bodies, used to select bones for physics baking.
        /// </summary>
        /// <param name="runtimeContext">Physics solver context with rigid-body simulation data.</param>
        /// <param name="result">Per-bone mask, indexed by bone index, set true for physics-controlled bones.</param>
        internal static void BuildPhysicsControlledBoneSelection(in PhysicsSolverContext runtimeContext, ref NativeArray<bool> result)
        {
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = false;
            }

            for (int i = 0; i < runtimeContext.rigidBodySimulationData.Length; ++i)
            {
                MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[i];
                if (rigidBody.hasRelatedBone && rigidBody.relatedBoneIndex >= 0 && rigidBody.relatedBoneIndex < result.Length && rigidBody.mode != PMXRigidBody.Mode.Kinetic)
                {
                    result[rigidBody.relatedBoneIndex] = true;
                }
            }
        }

        /// <summary>
        /// Disposes this manager's native physics context and native arrays and clears its solver state.
        /// </summary>
        public void DisposePhysics()
        {
            if (m_PhysicsSolverContext.bulletPhysicsContext.isValid)
            {
                m_PhysicsSolverContext.bulletPhysicsContext.Dispose();
                m_PhysicsSolverContext.bulletPhysicsContext = default;
            }

            m_PhysicsSolverContext.initialPoseApplied = false;
            if (m_RigidBodyTransformAccess.isCreated)
            {
                m_RigidBodyTransformAccess.Dispose();
            }
            DisposeNativeArray(ref m_PhysicsSolverContext.worldTransforms);
            DisposeNativeArray(ref m_PhysicsSolverContext.rigidBodyIndices);
            DisposeNativeArray(ref m_PhysicsSolverContext.kineticRigidBodyIndices);
            DisposeNativeArray(ref m_PhysicsSolverContext.sortedSimulatedRigidBodyIndices);
            DisposeNativeArray(ref m_PhysicsSolverContext.rigidBodySimulationData);
            DisposeNativeArray(ref m_PhysicsSolverContext.previousKineticTargets);
            DisposeNativeArray(ref m_PhysicsSolverContext.currentKineticTargets);
        }

        private void OnDestroy()
        {
            DisposePhysics();
        }

        /// <summary>
        /// Reinitializes runtime data for all rigid bodies and joints, recomputes rigid-body transforms, and reallocates scratch buffers as needed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a rigid-body or joint array element is null.</exception>
        internal void RebuildRuntimeData()
        {
            ResizePersistent(ref m_PhysicsSolverContext.rigidBodySimulationData, rigidBodies.Length);

            for (int i = 0; i < rigidBodies.Length; ++i)
            {
                MMDRigidBody rigidBody = rigidBodies[i];
                if (rigidBody == null)
                {
                    throw new InvalidOperationException($"MMD rigid body array element {i} is null.");
                }

                rigidBody.InitializeRuntimeData();
                m_PhysicsSolverContext.rigidBodySimulationData[i] = rigidBody.runtimeData;
            }

            PhysicsMath.ComputeRigidBodyTransforms(ref m_PhysicsSolverContext.rigidBodySimulationData);
            for (int i = 0; i < rigidBodies.Length; ++i)
            {
                rigidBodies[i].runtimeData = m_PhysicsSolverContext.rigidBodySimulationData[i];
            }

            for (int i = 0; i < joints.Length; ++i)
            {
                MMDJoint joint = joints[i];
                if (joint == null)
                {
                    throw new InvalidOperationException($"MMD joint array element {i} is null.");
                }

                joint.InitializeRuntimeData();
            }

            ReallocateArraysIfNeeded(rigidBodies.Length);
            PhysicsMath.BuildStaticRigidBodyLists(ref m_PhysicsSolverContext);

            if (m_RigidBodyTransformAccess.isCreated)
            {
                m_RigidBodyTransformAccess.Dispose();
            }
            m_RigidBodyTransformAccess = new TransformAccessArray(rigidBodies.Length);
            for (int i = 0; i < rigidBodies.Length; ++i)
            {
                m_RigidBodyTransformAccess.Add(rigidBodies[i].transform);
            }
        }

        private void BuildRigidBodies()
        {
            m_PhysicsSolverContext.bulletPhysicsContext.BuildRigidBodies(m_PhysicsSolverContext.rigidBodySimulationData);
        }

        private void BuildGround()
        {
            m_PhysicsSolverContext.bulletPhysicsContext.BuildGround(enableGroundCollision, k_MMDGroundCollisionGroup, k_MMDGroundCollisionMask);
        }

        /// <summary>
        /// Toggles ground collision both on this manager and in the native context.
        /// </summary>
        /// <param name="enabled">Whether ground collision is active.</param>
        public void SetGroundCollisionEnabled(bool enabled)
        {
            enableGroundCollision = enabled;
            m_PhysicsSolverContext.bulletPhysicsContext.SetGroundCollisionEnabled(enabled);
        }

        private void BuildJoints()
        {
            MMDBulletPhysics.NativeJointData[] nativeJoints = new MMDBulletPhysics.NativeJointData[joints.Length];
            for (int i = 0; i < joints.Length; ++i)
            {
                nativeJoints[i] = joints[i].runtimeData;
            }

            m_PhysicsSolverContext.bulletPhysicsContext.BuildJoints(nativeJoints);
        }

        /// <summary>
        /// Pushes solved rigid-body and joint transforms onto their Unity transforms, reading them from the native simulation when <paramref name="usePhysicsTransforms"/> is set, otherwise computing them from bone matrices.
        /// </summary>
        /// <param name="transformManagerContext">Transform solver context providing bone matrices.</param>
        /// <param name="usePhysicsTransforms">Whether to read rigid-body transforms from the native simulation.</param>
        internal void UpdateTransforms(ref MMDTransformManager.SolverContext transformManagerContext, bool usePhysicsTransforms)
        {
            int rigidBodyCount = m_PhysicsSolverContext.rigidBodySimulationData.Length;

            if (usePhysicsTransforms && m_PhysicsSolverContext.bulletPhysicsContext.isValid)
            {
                m_PhysicsSolverContext.bulletPhysicsContext.GetRigidBodyMotionTransforms(rigidBodyCount, m_PhysicsSolverContext.rigidBodyIndices, ref m_PhysicsSolverContext.worldTransforms);
            }
            else
            {
                for (int i = 0; i < rigidBodyCount; ++i)
                {
                    m_PhysicsSolverContext.worldTransforms[i] = PhysicsMath.ComputeRigidBodyWorldTransform(m_PhysicsSolverContext.rigidBodySimulationData[i], in transformManagerContext.boneStateData);
                }
            }

            new FlushRigidBodyTransformsJob { worldTransforms = m_PhysicsSolverContext.worldTransforms, }.Schedule(m_RigidBodyTransformAccess).Complete();

            // Joints are rigidly parented to rigid body A, so their local transform is fixed at build time and tracks rigid body A through the hierarchy. Do not overwrite it here: the native frameInA uses rigid body A's bone-offset rest frame, a different basis than the build-time local placement, so writing it displaces the joint object and that displacement persists after exiting play mode.
        }

        private void ReallocateArraysIfNeeded(int count)
        {
            if (!m_PhysicsSolverContext.worldTransforms.IsCreated || m_PhysicsSolverContext.worldTransforms.Length < count)
            {
                DisposeNativeArray(ref m_PhysicsSolverContext.worldTransforms);
                DisposeNativeArray(ref m_PhysicsSolverContext.rigidBodyIndices);
                m_PhysicsSolverContext.worldTransforms = new NativeArray<float4x4>(count, Allocator.Persistent);
                m_PhysicsSolverContext.rigidBodyIndices = new NativeArray<int>(count, Allocator.Persistent);
            }
        }

        private static void DisposeNativeArray<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private static MMDBulletPhysics.NativeJointData[] BuildPMXJoints(PMXModel model, NativeArray<MMDRigidBody.RigidBodySimulationData> rigidBodies)
        {
            MMDBulletPhysics.NativeJointData[] result = new MMDBulletPhysics.NativeJointData[model.joints.Length];
            for (int i = 0; i < model.joints.Length; ++i)
            {
                PMXJoint joint = model.joints[i];
                if (joint.type != PMXJoint.Type.Spring6DOF && joint.type != PMXJoint.Type.Generic6DOF)
                {
                    throw new NotSupportedException($"PMX joint type {joint.type} is not supported by MMD Bullet physics.");
                }
                if (joint.rigidBodyAIndex < 0 || joint.rigidBodyAIndex >= rigidBodies.Length || joint.rigidBodyBIndex < 0 || joint.rigidBodyBIndex >= rigidBodies.Length)
                {
                    throw new InvalidOperationException($"PMX joint {i} has invalid rigid body indices.");
                }

                float4x4 jointWorld = float4x4.TRS(joint.position, quaternion.EulerZXY(joint.rotation), new float3(1.0f, 1.0f, 1.0f));
                result[i] = new MMDBulletPhysics.NativeJointData
                {
                    type = joint.type,
                    rigidBodyAIndex = joint.rigidBodyAIndex,
                    rigidBodyBIndex = joint.rigidBodyBIndex,
                    frameInA = math.mul(math.inverse(rigidBodies[joint.rigidBodyAIndex].initialWorldTransform), jointWorld),
                    frameInB = math.mul(math.inverse(rigidBodies[joint.rigidBodyBIndex].initialWorldTransform), jointWorld),
                    translationLimitMin = joint.translationLimitMin,
                    translationLimitMax = joint.translationLimitMax,
                    rotationLimitMin = joint.rotationLimitMin,
                    rotationLimitMax = joint.rotationLimitMax,
                    springTranslation = joint.springTranslation,
                    springRotation = joint.springRotation * MMDConstants.k_MMDUnitToUnityUnit * MMDConstants.k_MMDUnitToUnityUnit,
                };
            }

            return result;
        }

        private void ResetSimulatedBoneTransformsToInitial()
        {
            for (int i = 0; i < rigidBodies.Length; ++i)
            {
                MMDRigidBody rigidBody = rigidBodies[i];
                if (!IsSimulated(rigidBody.runtimeData) || rigidBody.relatedBone == null)
                {
                    continue;
                }

                ResetBoneTransformToInitial(rigidBody.relatedBone);
            }
        }

        private static bool IsSimulated(MMDRigidBody.RigidBodySimulationData rigidBodySimulationData)
        {
            return rigidBodySimulationData.mode == PMXRigidBody.Mode.Dynamic || rigidBodySimulationData.mode == PMXRigidBody.Mode.DynamicBoneAligned;
        }

        private static void ResetBoneTransformToInitial(MMDBoneTransform bone)
        {
            bone.transform.localPosition = bone.initialLocalPosition;
            bone.transform.localRotation = bone.initialLocalRotation;
            bone.solverResetPending = true;
        }

        [BurstCompile]
        private static class PhysicsMath
        {
            /// <summary>
            /// Burst implementation that precomputes each rigid body's initial, bone-local, and rest world transforms from its PMX position/rotation and owning bone model position.
            /// </summary>
            /// <param name="runtimeDataArray">Rigid-body simulation data to update in place.</param>
            [BurstCompile]
            internal static void ComputeRigidBodyTransforms(ref NativeArray<MMDRigidBody.RigidBodySimulationData> runtimeDataArray)
            {
                for (int i = 0; i < runtimeDataArray.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeDataArray[i];
                    rigidBody.initialTransform = ComputeRigidBodyInitialTransform(rigidBody.position, rigidBody.rotation, rigidBody.boneModelPosition);
                    rigidBody.boneLocalTransform = math.inverse(rigidBody.initialTransform);
                    rigidBody.initialWorldTransform = ComputeRigidBodyRestWorldTransform(rigidBody);
                    runtimeDataArray[i] = rigidBody;
                }
            }

            /// <summary>
            /// Precomputes the static rigid-body index lists consumed each physics step: the identity index mapping, the kinetic (bone-driven) body list, and the simulated body list sorted by (bone transform level, related bone index). The filters and sort keys are immutable per model, so this runs once at initialization instead of re-filtering and re-sorting every step.
            /// </summary>
            /// <param name="runtimeContext">Physics solver context with populated rigid-body simulation data and an allocated index scratch buffer.</param>
            internal static void BuildStaticRigidBodyLists(ref PhysicsSolverContext runtimeContext)
            {
                int kineticCount = 0;
                int simulatedCount = 0;
                for (int i = 0; i < runtimeContext.rigidBodySimulationData.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[i];
                    runtimeContext.rigidBodyIndices[i] = rigidBody.rigidBodyIndex;
                    if (rigidBody.mode == PMXRigidBody.Mode.Kinetic)
                    {
                        ++kineticCount;
                    }
                    if (IsSimulated(rigidBody) && rigidBody.hasRelatedBone)
                    {
                        ++simulatedCount;
                    }
                }

                ResizePersistent(ref runtimeContext.kineticRigidBodyIndices, kineticCount);
                ResizePersistent(ref runtimeContext.sortedSimulatedRigidBodyIndices, simulatedCount);
                ResizePersistent(ref runtimeContext.previousKineticTargets, kineticCount);
                ResizePersistent(ref runtimeContext.currentKineticTargets, kineticCount);
                kineticCount = 0;
                simulatedCount = 0;
                for (int i = 0; i < runtimeContext.rigidBodySimulationData.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[i];
                    if (rigidBody.mode == PMXRigidBody.Mode.Kinetic)
                    {
                        runtimeContext.kineticRigidBodyIndices[kineticCount] = i;
                        ++kineticCount;
                    }
                    if (IsSimulated(rigidBody) && rigidBody.hasRelatedBone)
                    {
                        runtimeContext.sortedSimulatedRigidBodyIndices[simulatedCount] = i;
                        ++simulatedCount;
                    }
                }

                runtimeContext.sortedSimulatedRigidBodyIndices.Sort(new RigidBodyTransformLevelComparer { rigidBodies = runtimeContext.rigidBodySimulationData, });
            }

            /// <summary>
            /// Burst implementation that advances one MMD physics step: syncs or seeds bone-driven rigid bodies, steps the native Bullet simulation when time elapses, and writes simulated transforms back to bones.
            /// </summary>
            /// <param name="elapsedTime">Elapsed time for the step; values at or below zero skip the simulation advance.</param>
            /// <param name="transformManagerContext">Bone solver context supplying and receiving bone transforms.</param>
            /// <param name="runtimeContext">Physics solver context holding rigid-body data and the native Bullet context.</param>
            [BurstCompile]
            internal static void TransformPhysicsInternal(float elapsedTime, ref MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                if (!runtimeContext.bulletPhysicsContext.isValid)
                {
                    return;
                }

                // The prepare pass below memoizes observed bone world matrices per physics step; clear the per-step flags so no matrix computed in an earlier step (or rewritten by IK since) is trusted.
                for (int i = 0; i < transformManagerContext.boneStateData.Length; ++i)
                {
                    PMXUtilities.ElementAt(transformManagerContext.boneStateData, i).worldMatrixValid = false;
                }

                PrepareRigidBodyRelatedBoneMatrices(ref transformManagerContext, ref runtimeContext);
                if (!runtimeContext.initialPoseApplied)
                {
                    SeedRigidBodiesFromBones(in transformManagerContext, ref runtimeContext);
                    if (elapsedTime <= 0.0f)
                    {
                        return;
                    }
                }
                else if (elapsedTime <= 0.0f)
                {
                    SyncBoneDrivenRigidBodies(in transformManagerContext, ref runtimeContext);
                    return;
                }

                if (StepSimulationWithKineticInterpolation(elapsedTime, in transformManagerContext, ref runtimeContext))
                {
                    ApplyDynamicRigidBodiesToBones(ref transformManagerContext, ref runtimeContext);
                }
            }

            /// <summary>
            /// Advances the simulation in fixed substeps, moving the kinetic bodies along an interpolated path from their previous-frame targets to the current ones and pushing each waypoint WITHOUT clearing velocity, so Bullet derives proper kinematic velocities from the motion. This gives the constraint solver continuously-moving kinematic anchors it can track at MMD's reference iteration count, instead of the old once-per-frame teleport with zeroed velocity whose full-frame position error made translation-locked chains (spring-less hair) blow apart during fast motion. Variable frame times are carried in a time accumulator so they map onto whole substeps without drifting.
            /// </summary>
            /// <param name="elapsedTime">Elapsed time to simulate.</param>
            /// <param name="transformManagerContext">Bone solver context supplying bone matrices for the kinetic targets.</param>
            /// <param name="runtimeContext">Physics solver context to advance.</param>
            /// <returns>Whether at least one substep was simulated.</returns>
            private static bool StepSimulationWithKineticInterpolation(float elapsedTime, in MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                ComputeKineticTargets(in transformManagerContext, ref runtimeContext, ref runtimeContext.currentKineticTargets);
                runtimeContext.timeAccumulator += elapsedTime;
                int substepCount = (int)(runtimeContext.timeAccumulator / k_FixedTimeStep);
                if (substepCount <= 0)
                {
                    return false;
                }

                substepCount = math.min(substepCount, k_MaxSubSteps);
                runtimeContext.timeAccumulator = math.min(runtimeContext.timeAccumulator - substepCount * k_FixedTimeStep, k_FixedTimeStep);

                int kineticCount = runtimeContext.kineticRigidBodyIndices.Length;
                for (int step = 0; step < substepCount; ++step)
                {
                    float alpha = (float)(step + 1) / substepCount;
                    for (int i = 0; i < kineticCount; ++i)
                    {
                        float4x4 previous = runtimeContext.previousKineticTargets[i];
                        float4x4 current = runtimeContext.currentKineticTargets[i];
                        float3 position = math.lerp(previous.c3.xyz, current.c3.xyz, alpha);
                        quaternion rotation = math.slerp(new quaternion(previous), new quaternion(current), alpha);
                        runtimeContext.worldTransforms[i] = new float4x4(rotation, position);
                    }
                    runtimeContext.bulletPhysicsContext.SetRigidBodyTransforms(kineticCount, runtimeContext.worldTransforms, runtimeContext.kineticRigidBodyIndices, false);
                    runtimeContext.bulletPhysicsContext.StepSimulation(k_FixedTimeStep);
                }

                NativeArray<float4x4> swap = runtimeContext.previousKineticTargets;
                runtimeContext.previousKineticTargets = runtimeContext.currentKineticTargets;
                runtimeContext.currentKineticTargets = swap;
                return true;
            }

            /// <summary>
            /// Computes the world target transform of every kinetic (bone-driven) rigid body from the current bone matrices.
            /// </summary>
            /// <param name="transformManagerContext">Bone solver context supplying bone matrices.</param>
            /// <param name="runtimeContext">Physics solver context providing the kinetic index list and simulation data.</param>
            /// <param name="targets">Output array, indexed like the kinetic index list.</param>
            private static void ComputeKineticTargets(in MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext, ref NativeArray<float4x4> targets)
            {
                for (int i = 0; i < runtimeContext.kineticRigidBodyIndices.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[runtimeContext.kineticRigidBodyIndices[i]];
                    targets[i] = ComputeRigidBodyWorldTransform(rigidBody, in transformManagerContext.boneStateData);
                }
            }

            private static void SeedRigidBodiesFromBones(in MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                for (int i = 0; i < runtimeContext.rigidBodySimulationData.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[i];
                    runtimeContext.worldTransforms[i] = ComputeRigidBodyWorldTransform(rigidBody, in transformManagerContext.boneStateData);
                }

                runtimeContext.bulletPhysicsContext.SetRigidBodyTransforms(runtimeContext.rigidBodySimulationData.Length, runtimeContext.worldTransforms, runtimeContext.rigidBodyIndices, true);

                ComputeKineticTargets(in transformManagerContext, ref runtimeContext, ref runtimeContext.previousKineticTargets);
                runtimeContext.timeAccumulator = 0.0f;
                runtimeContext.initialPoseApplied = true;
            }

            // Hard-syncs kinetic bodies to the current pose (teleport, velocities cleared) for zero-elapsed updates, keeping the interpolation baseline in step.
            private static void SyncBoneDrivenRigidBodies(in MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                ComputeKineticTargets(in transformManagerContext, ref runtimeContext, ref runtimeContext.previousKineticTargets);
                for (int i = 0; i < runtimeContext.previousKineticTargets.Length; ++i)
                {
                    runtimeContext.worldTransforms[i] = runtimeContext.previousKineticTargets[i];
                }
                runtimeContext.bulletPhysicsContext.SetRigidBodyTransforms(runtimeContext.kineticRigidBodyIndices.Length, runtimeContext.worldTransforms, runtimeContext.kineticRigidBodyIndices, true);
            }

            private static void PrepareRigidBodyRelatedBoneMatrices(ref MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                for (int i = 0; i < runtimeContext.rigidBodySimulationData.Length; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[i];
                    if (rigidBody.hasRelatedBone)
                    {
                        PrepareBoneWorldMatrix(ref transformManagerContext, rigidBody.relatedBoneIndex);
                    }
                }
            }

            private static void PrepareBoneWorldMatrix(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex)
            {
                ref MMDBoneTransform.BoneSolverState state = ref PMXUtilities.ElementAt(transformManagerContext.boneStateData, boneIndex);
                if (state.hasSolvedTransform || state.worldMatrixValid)
                {
                    return;
                }

                ref MMDBoneTransform.BoneSolverConfig config = ref PMXUtilities.ElementAt(transformManagerContext.boneConfigData, boneIndex);
                if (config.parentBoneIndex >= 0)
                {
                    PrepareBoneWorldMatrix(ref transformManagerContext, config.parentBoneIndex);
                }

                if ((config.rotationConstraint || config.translationConstraint) && transformManagerContext.solveConstraints)
                {
                    MMDBoneTransform.UpdateLocalMatrix(ref transformManagerContext, boneIndex);
                }
                else
                {
                    float4x4 parentWorldMatrix = MMDBoneTransform.GetParentWorldMatrix(ref transformManagerContext, boneIndex);
                    MMDBoneTransform.ObserveLocalMatrix(in config, ref state, parentWorldMatrix);
                }

                state.worldMatrixValid = true;
            }

            private struct RigidBodyTransformLevelComparer : IComparer<int>
            {
                public NativeArray<MMDRigidBody.RigidBodySimulationData> rigidBodies;

                public int Compare(int x, int y)
                {
                    int boneIndexX = rigidBodies[x].relatedBoneIndex;
                    int boneIndexY = rigidBodies[y].relatedBoneIndex;
                    int transformLevelComparison = rigidBodies[x].boneTransformLevel.CompareTo(rigidBodies[y].boneTransformLevel);
                    return transformLevelComparison != 0 ? transformLevelComparison : boneIndexX.CompareTo(boneIndexY);
                }
            }

            private static void ApplyDynamicRigidBodiesToBones(ref MMDTransformManager.SolverContext transformManagerContext, ref PhysicsSolverContext runtimeContext)
            {
                int count = runtimeContext.sortedSimulatedRigidBodyIndices.Length;
                runtimeContext.bulletPhysicsContext.GetRigidBodyMotionTransforms(count, runtimeContext.sortedSimulatedRigidBodyIndices, ref runtimeContext.worldTransforms);

                for (int i = 0; i < count; ++i)
                {
                    MMDRigidBody.RigidBodySimulationData rigidBody = runtimeContext.rigidBodySimulationData[runtimeContext.sortedSimulatedRigidBodyIndices[i]];
                    float4x4 boneWorldMatrix = math.mul(runtimeContext.worldTransforms[i], rigidBody.boneLocalTransform);
                    if (rigidBody.mode == PMXRigidBody.Mode.DynamicBoneAligned)
                    {
                        float3 modelTranslationDelta = ApplyKineticBoneAlignedWorldMatrixToBone(ref transformManagerContext, rigidBody.relatedBoneIndex, boneWorldMatrix);
                        ShiftKineticBoneAlignedBodyPosition(ref runtimeContext.bulletPhysicsContext, rigidBody, modelTranslationDelta);
                        continue;
                    }

                    ApplyKineticWorldMatrixToBone(ref transformManagerContext, rigidBody.relatedBoneIndex, boneWorldMatrix);
                }
            }

            /// <summary>
            /// Computes a rigid body's world transform, composing its related bone's world matrix with its initial transform when bone-related, or returning the initial transform otherwise.
            /// </summary>
            /// <param name="rigidBody">Rigid-body simulation data to transform.</param>
            /// <param name="boneStateData">Bone solver state used to resolve the related bone's world matrix.</param>
            /// <returns>The rigid body's world transform matrix.</returns>
            internal static float4x4 ComputeRigidBodyWorldTransform(MMDRigidBody.RigidBodySimulationData rigidBody, in NativeArray<MMDBoneTransform.BoneSolverState> boneStateData)
            {
                return rigidBody.hasRelatedBone ? math.mul(PMXUtilities.ElementAt(boneStateData, rigidBody.relatedBoneIndex).worldMatrix, rigidBody.initialTransform) : rigidBody.initialTransform;
            }

            private static void ShiftKineticBoneAlignedBodyPosition(ref MMDBulletPhysics context, MMDRigidBody.RigidBodySimulationData rigidBody, float3 modelTranslationDelta)
            {
                context.ShiftRigidBodyPosition(rigidBody.rigidBodyIndex, modelTranslationDelta);
            }

            private static bool IsSimulated(MMDRigidBody.RigidBodySimulationData rigidBodySimulationData)
            {
                return rigidBodySimulationData.mode == PMXRigidBody.Mode.Dynamic || rigidBodySimulationData.mode == PMXRigidBody.Mode.DynamicBoneAligned;
            }

            private static void ApplyKineticWorldMatrixToBone(ref MMDTransformManager.SolverContext transformManagerContext, int boneIndex, float4x4 worldMatrix)
            {
                float4x4 parentWorldMatrix = MMDBoneTransform.GetParentWorldMatrix(ref transformManagerContext, boneIndex);
                float4x4 localMatrix = math.mul(math.inverse(parentWorldMatrix), worldMatrix);
                float3 localPosition = localMatrix.c3.xyz;
                quaternion localRotation = new quaternion(localMatrix);
                MMDBoneTransform.ApplyLocalTransformToBone(ref transformManagerContext, boneIndex, localPosition, localRotation);
            }

            private static float3 ApplyKineticBoneAlignedWorldMatrixToBone(ref MMDTransformManager.SolverContext transformManagerData, int boneIndex, float4x4 worldMatrix)
            {
                float3 boneLocalPosition = PMXUtilities.ElementAt(transformManagerData.boneStateData, boneIndex).localPosition;
                float4x4 parentWorldMatrix = MMDBoneTransform.GetParentWorldMatrix(ref transformManagerData, boneIndex);
                float4x4 localMatrix = math.mul(math.inverse(parentWorldMatrix), worldMatrix);
                float3 localPosition = localMatrix.c3.xyz;
                float3 localTranslationDelta = localPosition - boneLocalPosition;
                MMDBoneTransform.ApplyLocalTransformToBone(ref transformManagerData, boneIndex, boneLocalPosition, new quaternion(localMatrix));
                return math.mul(parentWorldMatrix, new float4(localTranslationDelta, 0)).xyz;
            }

            private static float4x4 ComputeRigidBodyRestWorldTransform(MMDRigidBody.RigidBodySimulationData rigidBody)
            {
                if (!rigidBody.hasRelatedBone)
                {
                    return rigidBody.initialTransform;
                }

                return math.mul(float4x4.Translate(rigidBody.boneModelPosition), rigidBody.initialTransform);
            }

            private static float4x4 ComputeRigidBodyInitialTransform(float3 position, float3 rotation, float3 bonePosition)
            {
                float3 relatedPosition = position - bonePosition;
                float4x4 initialTransform = math.mul(float4x4.Translate(relatedPosition), float4x4.EulerZXY(rotation));
                return initialTransform;
            }
        }
    }
}
