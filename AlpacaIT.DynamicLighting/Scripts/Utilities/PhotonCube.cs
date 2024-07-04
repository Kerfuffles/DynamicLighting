﻿using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpacaIT.DynamicLighting
{
    /// <summary>
    /// Cubemap containing photon data of direct illumination used for a technique similar to photon mapping.
    /// </summary>
    internal class PhotonCube
    {
        /// <summary>One side of a <see cref="PhotonCube"/> containing pixel data.</summary>
        private class PhotonCubeFace
        {
            /// <summary>The packed shader data.</summary>
            public readonly Color[] colors;

            /// <summary>The width and height of each face of the photon cube in pixels.</summary>
            public readonly int size;

            private float byte_to_normalized_float(uint value)
            {
                return -1.0f + (value / 255.0f) * 2.0f;
            }

            private float byte_to_saturated_float(float value)
            {
                return value / 255.0f;
            }

            private float4 unpack_normalized_float4_from_float(float value)
            {
                uint bytes = math.asuint(value);
                float4 result;
                result.x = byte_to_normalized_float((bytes >> 24) & 0xFF);
                result.y = byte_to_normalized_float((bytes >> 16) & 0xFF);
                result.z = byte_to_normalized_float((bytes >> 8) & 0xFF);
                result.w = byte_to_normalized_float(bytes & 0xFF);
                return result;
            }

            private float4 unpack_saturated_float4_from_float(float value)
            {
                uint bytes = math.asuint(value);
                float4 result;
                result.x = byte_to_saturated_float((bytes >> 24) & 0xFF);
                result.y = byte_to_saturated_float((bytes >> 16) & 0xFF);
                result.z = byte_to_saturated_float((bytes >> 8) & 0xFF);
                result.w = byte_to_saturated_float(bytes & 0xFF);
                return result;
            }

            public PhotonCubeFace(Color[] colors, int size)
            {
                this.colors = colors;
                this.size = size;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int Index(float2 position)
            {
                position.x = 1.0f - position.x;
                var x = (int)math.floor(position.x * size);
                var y = (int)math.floor(position.y * size);
                x = math.max(0, math.min(x, size - 1));
                y = math.max(0, math.min(y, size - 1));
                return y * size + x;
            }

            public float SampleDistance(float2 position)
            {
                return colors[Index(position)].r;
            }

            public float3 SampleNormal(float2 position)
            {
                return unpack_normalized_float4_from_float(colors[Index(position)].g).xyz;
            }

            public float3 SampleDiffuse(float2 position)
            {
                return unpack_saturated_float4_from_float(colors[Index(position)].b).xyz;
            }
        }

        /// <summary>The six faces of the photon cube.</summary>
        private readonly PhotonCubeFace[] faces = new PhotonCubeFace[6];

        /// <summary>The width and height of each face of the photon cube in pixels.</summary>
        private readonly int size;

        /// <summary>
        /// Creates a new <see cref="PhotonCube"/> instance that copies the data from a cubemap <see
        /// cref="RenderTexture"/> into memory.
        /// </summary>
        /// <param name="cubemapRenderTexture">
        /// The cubemap <see cref="RenderTexture"/> with a <see cref="RenderTexture.volumeDepth"/>
        /// of 6 faces.
        /// </param>
        public PhotonCube(RenderTexture cubemapRenderTexture)
        {
            // validate the arguments to prevent any errors.
            if (cubemapRenderTexture == null) throw new System.ArgumentNullException(nameof(cubemapRenderTexture));
            if (cubemapRenderTexture.dimension != TextureDimension.Cube) throw new System.ArgumentException("The render texture must have the dimension set to cube.", nameof(cubemapRenderTexture));
            if (cubemapRenderTexture.volumeDepth != 6) throw new System.ArgumentException("The render texture for photon cubes must have 6 faces.", nameof(cubemapRenderTexture));

            // remember the size of the cubemap texture in pixels.
            size = cubemapRenderTexture.width;

#if UNITY_2021_3_OR_NEWER && !UNITY_2021_3_0 && !UNITY_2021_3_1 && !UNITY_2021_3_2 && !UNITY_2021_3_3 && !UNITY_2021_3_4 && !UNITY_2021_3_5 && !UNITY_2021_3_6 && !UNITY_2021_3_7 && !UNITY_2021_3_8 && !UNITY_2021_3_9 && !UNITY_2021_3_10 && !UNITY_2021_3_11 && !UNITY_2021_3_12 && !UNITY_2021_3_13 && !UNITY_2021_3_14 && !UNITY_2021_3_15 && !UNITY_2021_3_16 && !UNITY_2021_3_17 && !UNITY_2021_3_18 && !UNITY_2021_3_19 && !UNITY_2021_3_20 && !UNITY_2021_3_21 && !UNITY_2021_3_22 && !UNITY_2021_3_23 && !UNITY_2021_3_24 && !UNITY_2021_3_25 && !UNITY_2021_3_26 && !UNITY_2021_3_27
            var photonCameraRenderTextureDescriptor = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBFloat, 16, 0, RenderTextureReadWrite.Linear);
#else
            photonCameraRenderTextureDescriptor = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBFloat, 16, 0);
#endif
            photonCameraRenderTextureDescriptor.autoGenerateMips = false;

            // extract the 6 sides of the cubemap:
            var rt = RenderTexture.GetTemporary(photonCameraRenderTextureDescriptor);
            rt.filterMode = FilterMode.Point;
            var readableTexture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            readableTexture.filterMode = FilterMode.Point;
            for (int face = 0; face < 6; face++)
            {
                Graphics.CopyTexture(cubemapRenderTexture, face, 0, rt, 0, 0);
                RenderTexture.active = rt;
                readableTexture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                readableTexture.Apply();
                faces[face] = new PhotonCubeFace(readableTexture.GetPixels(), size);
                RenderTexture.active = null;
            }
            RenderTexture.ReleaseTemporary(rt);
        }

        /// <summary>
        /// Samples a cubemap like a shader would and returns the face index to sample with the coordinates.
        /// <para>https://www.gamedev.net/forums/topic/687535-implementing-a-cube-map-lookup-function/</para>
        /// </summary>
        /// <param name="direction">The direction to sample the cubemap in.</param>
        /// <param name="face">The face index to be sampled from.</param>
        /// <returns>The UV coordinates to sample the cubemap at.</returns>
        public static float2 GetFaceUvByDirection(float3 direction, out int face)
        {
            float3 vAbs = math.abs(direction);
            float ma;
            float2 uv;
            if (vAbs.z >= vAbs.x && vAbs.z >= vAbs.y)
            {
                face = direction.z < 0.0f ? 5 : 4;
                ma = 0.5f / vAbs.z;
                uv = new float2(direction.z < 0.0f ? -direction.x : direction.x, -direction.y);
            }
            else if (vAbs.y >= vAbs.x)
            {
                face = direction.y < 0.0f ? 3 : 2;
                ma = 0.5f / vAbs.y;
                uv = new float2(direction.x, direction.y < 0.0f ? -direction.z : direction.z);
            }
            else
            {
                face = direction.x < 0.0f ? 1 : 0;
                ma = 0.5f / vAbs.x;
                uv = new float2(direction.x < 0.0f ? direction.z : -direction.z, -direction.y);
            }
            return uv * ma + 0.5f;
        }

        /// <summary>
        /// Computes the direction vector from a given UV coordinate and face index of a cubemap.
        /// </summary>
        /// <param name="uv">The UV coordinates to sample the cubemap at.</param>
        /// <param name="face">The face index to be sampled from.</param>
        /// <returns>The direction vector corresponding to the UV and face index.</returns>
        public static float3 GetDirectionByFaceUv(float2 uv, int face)
        {
            // adjust uv coordinates from [0,1] to [-1,1].
            uv = 2.0f * uv - 1.0f;

            float3 direction = default;
            switch (face)
            {
                case 0: direction = new float3(1.0f, -uv.y, -uv.x); break;  // +X
                case 1: direction = new float3(-1.0f, -uv.y, uv.x); break;  // -X
                case 2: direction = new float3(uv.x, 1.0f, uv.y); break;    // +Y
                case 3: direction = new float3(uv.x, -1.0f, -uv.y); break;  // -Y
                case 4: direction = new float3(uv.x, -uv.y, 1.0f); break;   // +Z
                case 5: direction = new float3(-uv.x, -uv.y, -1.0f); break; // -Z
            }

            // normalize the direction vector to ensure it's a unit vector.
            return math.normalize(direction);
        }

        public float SampleDistance(float3 direction)
        {
            var uv = GetFaceUvByDirection(direction, out var face);
            var distance = faces[face].SampleDistance(uv);
            return distance < 0.5f ? 0.0f : distance; // account for skybox.
        }

        public Color SampleDiffuse(float3 direction)
        {
            var uv = GetFaceUvByDirection(direction, out var face);
            var color = faces[face].SampleDiffuse(uv);
            return new Color(color.x, color.y, color.z);
        }

        public float3 SampleNormal(float3 direction)
        {
            var uv = GetFaceUvByDirection(direction, out var face);
            return faces[face].SampleNormal(uv);
        }
    }
}