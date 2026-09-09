using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UMT
{
    /// <summary>
    /// Burst per-vertex SDEF/LBS skinning job that replicates the <c>CSSkin</c> kernel in <c>MMDSDEFSkinning.compute</c> on the CPU. It reads the bind-pose source geometry, applies vertex-morph blend shapes in bind-pose space, then skins each vertex (the SDEF formula for SDEF vertices, linear blend skinning for BDEF vertices), writing rootBone-space positions and normals for a runtime pass-through mesh. Scheduled by <see cref="MMDSDEFSkinner"/> and completed by <see cref="MMDTransformManager"/>.
    /// </summary>
    [BurstCompile]
    public struct MMDSDEFSkinJob : IJobParallelFor
    {
        /// <summary>Bind-pose source positions, in mesh-vertex order.</summary>
        [ReadOnly] public NativeArray<float3> sourcePositions;
        /// <summary>Bind-pose source normals, in mesh-vertex order.</summary>
        [ReadOnly] public NativeArray<float3> sourceNormals;
        /// <summary>Per-vertex SDEF skinning data.</summary>
        [ReadOnly] public NativeArray<SDEFVertexData> vertexData;
        /// <summary>Per-bone skinning matrices (<c>worldToObject * boneWorld * bindpose</c>).</summary>
        [ReadOnly] public NativeArray<float4x4> boneMatrices;
        /// <summary>Per-bone rotation quaternions extracted from the skinning matrices.</summary>
        [ReadOnly] public NativeArray<float4> boneQuaternions;
        /// <summary>Per-morph-slot weights in the 0..1 range.</summary>
        [ReadOnly] public NativeArray<float> morphWeights;
        /// <summary>Flattened morph contributions; <c>.xyz</c> is the delta as raw bits, <c>.w</c> is the morph slot.</summary>
        [ReadOnly] public NativeArray<uint4> morphOffsets;
        /// <summary>Per-vertex <c>(start, count)</c> range into <see cref="morphOffsets"/>.</summary>
        [ReadOnly] public NativeArray<int2> morphRanges;
        /// <summary>Output skinned positions, in rootBone space.</summary>
        [WriteOnly] public NativeArray<float3> outPositions;
        /// <summary>Output skinned normals, in rootBone space.</summary>
        [WriteOnly] public NativeArray<float3> outNormals;

        /// <summary>
        /// Skins vertex <paramref name="vertexIndex"/>: applies vertex morphs in bind-pose space, then the SDEF or linear-blend-skinning deformation matching the compute kernel.
        /// </summary>
        /// <param name="vertexIndex">Mesh vertex index.</param>
        public void Execute(int vertexIndex)
        {
            float3 position = sourcePositions[vertexIndex];
            float3 normal = sourceNormals[vertexIndex];

            int2 range = morphRanges[vertexIndex];
            for (int k = 0; k < range.y; ++k)
            {
                uint4 entry = morphOffsets[range.x + k];
                float3 delta = math.asfloat(entry.xyz);
                int morphSlot = (int)entry.w;
                position += morphWeights[morphSlot] * delta;
            }

            SDEFVertexData data = vertexData[vertexIndex];
            float3 outPosition;
            float3 outNormal;

            if (data.sdefC.w > 0.5f)
            {
                float weight0 = data.boneWeights.x;
                float weight1 = data.boneWeights.y;
                int boneIndex0 = data.boneIndices.x;
                int boneIndex1 = data.boneIndices.y;
                float4x4 boneMatrix0 = boneMatrices[boneIndex0];
                float4x4 boneMatrix1 = boneMatrices[boneIndex1];
                float3 center = data.sdefC.xyz;
                float3 weightedRotation0 = math.mul(boneMatrix0, new float4(data.sdefR0.xyz, 1.0f)).xyz;
                float3 weightedRotation1 = math.mul(boneMatrix1, new float4(data.sdefR1.xyz, 1.0f)).xyz;
                float3 weightedRotation = weightedRotation0 * weight0 + weightedRotation1 * weight1;
                float3 weightedPosition0 = math.mul(boneMatrix0, new float4(center, 1.0f)).xyz;
                float3 weightedPosition1 = math.mul(boneMatrix1, new float4(center, 1.0f)).xyz;
                float3 translation = (weightedPosition0 - center) * weight0 + (weightedPosition1 - center) * weight1;
                float3 weightedPosition = center + translation;
                float3 finalPosition = (weightedPosition + weightedRotation) * 0.5f;
                float4 deformQuaternion = Slerp(boneQuaternions[boneIndex0], boneQuaternions[boneIndex1], weight1);
                outPosition = Rotate(deformQuaternion, position - center) + finalPosition;
                outNormal = Rotate(deformQuaternion, normal);
            }
            else
            {
                float3 skinnedPosition = float3.zero;
                float3 skinnedNormal = float3.zero;
                for (int influence = 0; influence < 4; ++influence)
                {
                    int boneIndex = data.boneIndices[influence];
                    float w = data.boneWeights[influence];
                    if (w <= 0.0f || boneIndex < 0)
                    {
                        continue;
                    }
                    skinnedPosition += w * math.mul(boneMatrices[boneIndex], new float4(position, 1.0f)).xyz;
                    skinnedNormal += w * Rotate(boneQuaternions[boneIndex], normal);
                }
                outPosition = skinnedPosition;
                outNormal = skinnedNormal;
            }

            outPositions[vertexIndex] = outPosition;
            outNormals[vertexIndex] = math.normalize(outNormal);
        }

        /// <summary>
        /// Rotates a vector by a unit quaternion (<c>q.xyz = axis*sin</c>, <c>q.w = cos</c>), matching the compute shader's <c>Rotate</c>.
        /// </summary>
        /// <param name="q">Unit quaternion as <c>float4</c>.</param>
        /// <param name="v">Vector to rotate.</param>
        /// <returns>The rotated vector.</returns>
        private static float3 Rotate(float4 q, float3 v)
        {
            return v + 2.0f * math.cross(q.xyz, math.cross(q.xyz, v) + q.w * v);
        }

        /// <summary>
        /// Spherical linear interpolation of two unit quaternions with shortest-path sign flip and an nlerp fallback for near-parallel inputs, matching the compute shader's <c>Slerp</c>.
        /// </summary>
        /// <param name="a">First unit quaternion.</param>
        /// <param name="b">Second unit quaternion.</param>
        /// <param name="t">Interpolation factor.</param>
        /// <returns>The interpolated unit quaternion.</returns>
        private static float4 Slerp(float4 a, float4 b, float t)
        {
            float d = math.dot(a, b);
            if (d < 0.0f)
            {
                b = -b;
                d = -d;
            }

            if (d > 0.9995f)
            {
                return math.normalize(math.lerp(a, b, t));
            }

            float theta = math.acos(math.clamp(d, -1.0f, 1.0f));
            float sinTheta = math.sin(theta);
            float wa = math.sin((1.0f - t) * theta) / sinTheta;
            float wb = math.sin(t * theta) / sinTheta;
            return math.normalize(a * wa + b * wb);
        }
    }
}
