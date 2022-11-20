﻿using System.Collections.Generic;
using UnityEngine;

namespace AlpacaIT.DynamicLighting
{
    [ExecuteInEditMode]
    public class DynamicLightManager : MonoBehaviour
    {
        public const int realtimeLightChannel = 32;

        private static DynamicLightManager s_Instance;

        /// <summary>Gets the singleton dynamic lighting manager instance or creates it.</summary>
        public static DynamicLightManager Instance
        {
            get
            {
                // if known, immediately return the instance.
                if (s_Instance) return s_Instance;

                // C# hot reloading support: try finding an existing instance in the scene.
                s_Instance = FindObjectOfType<DynamicLightManager>();

                // otherwise create a new instance in scene.
                if (!s_Instance)
                    s_Instance = new GameObject("[Dynamic Light Manager]").AddComponent<DynamicLightManager>();

                return s_Instance;
            }
        }

        // the NVIDIA Quadro K1000M (2012) can handle 25 lights at 30fps.
        // the NVIDIA GeForce GTX 1050 Ti (2016) can handle 125 lights between 53-68fps.
        // the NVIDIA GeForce RTX 3080 (2020) can handle 2000 lights at 70fps.

        [Min(0)]
        public int dynamicLightBudget = 64;
        [Min(0)]
        public int realtimeLightBudget = 32;
        [Min(0f)]
        public float budgetLightFadingTime = 10f;

        /// <summary>The memory size in bytes of the <see cref="ShaderDynamicLight"/> struct.</summary>
        private int dynamicLightStride;
        private Lightmap[] lightmaps;
        private List<DynamicLight> sceneDynamicLights;
        private List<DynamicLight> sceneRealtimeLights;

        private List<DynamicLight> activeDynamicLights;
        private List<DynamicLight> activeRealtimeLights;
        private ShaderDynamicLight[] shaderDynamicLights;
        private ComputeBuffer dynamicLightsBuffer;

        private Vector3 lastCameraMetricGridPosition = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

#if UNITY_EDITOR

        private bool useContinuousPreview = false;

        private void OnEnable()
        {
            // handle C# reloads in the editor.
            if (!Application.isPlaying)
                Awake();
        }

        private void OnDisable()
        {
            // handle C# reloads in the editor.
            if (!Application.isPlaying)
                OnDestroy();
        }

        [UnityEditor.MenuItem("Dynamic Lighting/Toggle Continuous Preview", false, 21)]
        public static void EnableContinuousPreview()
        {
            Instance.useContinuousPreview = !Instance.useContinuousPreview;
        }

        [UnityEditor.MenuItem("Dynamic Lighting/Toggle Unlit Surfaces", false, 22)]
        public static void ToggleUnlitSurfaces()
        {
            if (Shader.IsKeywordEnabled("DYNAMIC_LIGHTING_UNLIT"))
                Shader.DisableKeyword("DYNAMIC_LIGHTING_UNLIT");
            else
                Shader.EnableKeyword("DYNAMIC_LIGHTING_UNLIT");
        }

        [UnityEditor.MenuItem("Dynamic Lighting/PayPal Donation", false, 41)]
        public static void PayPalDonation()
        {
            Application.OpenURL("https://paypal.me/henrydejongh");
        }

#endif

        /// <summary>Finds all of the dynamic lights in the scene that are not realtime.</summary>
        /// <returns>The collection of dynamic lights in the scene.</returns>
        public static List<DynamicLight> FindDynamicLightsInScene()
        {
            var dynamicPointLights = new List<DynamicLight>(FindObjectsOfType<DynamicLight>());

            // remove all of the realtime lights from our collection.
            var dynamicPointLightsCount = dynamicPointLights.Count;
            for (int i = dynamicPointLightsCount; i-- > 0;)
                if (dynamicPointLights[i].realtime)
                    dynamicPointLights.RemoveAt(i);

            return dynamicPointLights;
        }

        /// <summary>Immediately reloads the lighting.</summary>
        public void Reload()
        {
            OnDestroy();
            Awake();
        }

        private void Awake()
        {
            dynamicLightStride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ShaderDynamicLight));

            // more dynamic point lights will never be instantiated during play so we fetch them here once.
            sceneDynamicLights = FindDynamicLightsInScene();

            // prepare to store realtime lights that get created during gameplay.
            sceneRealtimeLights = new List<DynamicLight>(realtimeLightBudget);

            // allocate the required arrays and buffers according to our budget.
            shaderDynamicLights = new ShaderDynamicLight[dynamicLightBudget + realtimeLightBudget];
            activeDynamicLights = new List<DynamicLight>(shaderDynamicLights.Length);
            activeRealtimeLights = new List<DynamicLight>(shaderDynamicLights.Length);
            dynamicLightsBuffer = new ComputeBuffer(shaderDynamicLights.Length, dynamicLightStride, ComputeBufferType.Default);
            Shader.SetGlobalBuffer("dynamic_lights", dynamicLightsBuffer);
            Shader.SetGlobalInt("dynamic_lights_count", 0);

            // prepare the scene for dynamic lighting.
            lightmaps = FindObjectsOfType<Lightmap>();
            for (int i = 0; i < lightmaps.Length; i++)
            {
                // for every game object that requires a lightmap:
                var lightmap = lightmaps[i];

                // fetch the active material on the mesh renderer.
                var meshRenderer = lightmap.GetComponent<MeshRenderer>();
                var materialPropertyBlock = new MaterialPropertyBlock();

                // assign the lightmap data to the material property block.
                if (RuntimeUtilities.ReadLightmapData(lightmap.identifier, out uint[] pixels))
                {
                    lightmap.buffer = new ComputeBuffer(pixels.Length, 4);
                    lightmap.buffer.SetData(pixels);
                    materialPropertyBlock.SetBuffer("lightmap", lightmap.buffer);
                    materialPropertyBlock.SetInt("lightmap_resolution", lightmap.resolution);
                    meshRenderer.SetPropertyBlock(materialPropertyBlock);
                }
                else Debug.LogError("Unable to read the lightmap " + lightmap.identifier + " data file!");
            }
        }

        private void OnDestroy()
        {
            dynamicLightsBuffer.Release();

            for (int i = 0; i < lightmaps.Length; i++)
                lightmaps[i].buffer.Release();
        }

        public void RegisterRealtimeLight(DynamicLight light)
        {
            sceneRealtimeLights.Add(light);
        }

        public void UnregisterRealtimeLight(DynamicLight light)
        {
            sceneRealtimeLights.Remove(light);
        }

        /// <summary>
        /// Whenever the dynamic or realtime light budgets change we must update the shader buffer.
        /// </summary>
        private void ReallocateShaderLightBuffer()
        {
            // properly release any old buffer.
            if (dynamicLightsBuffer != null && dynamicLightsBuffer.IsValid())
                dynamicLightsBuffer.Release();

            var totalLightBudget = dynamicLightBudget + realtimeLightBudget;
            shaderDynamicLights = new ShaderDynamicLight[totalLightBudget];
            dynamicLightsBuffer = new ComputeBuffer(shaderDynamicLights.Length, dynamicLightStride, ComputeBufferType.Default);
            Shader.SetGlobalBuffer("dynamic_lights", dynamicLightsBuffer);
        }

        /// <summary>This handles the CPU side lighting effects.</summary>
        private void Update()
        {
            var camera = Camera.main;

#if UNITY_EDITOR
            // editor scene view support.
            if (!Application.isPlaying)
            {
                var sceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (sceneView)
                {
                    camera = sceneView.camera;
                }
                else
                {
                    var current = Camera.current;
                    if (current)
                    {
                        camera = current;
                    }
                }
            }
            else
            {
                Debug.Assert(camera != null, "Could not find a camera that is tagged \"MainCamera\" for lighting calculations.");
            }
#endif

            // if the budget changed we must recreate the shader buffers.
            var totalLightBudget = dynamicLightBudget + realtimeLightBudget;
            if (totalLightBudget == 0) return; // sanity check.
            if (shaderDynamicLights.Length != totalLightBudget)
                ReallocateShaderLightBuffer();

            // if we exceed the dynamic light budget then whenever the camera moves more than a
            // meter in the scene we sort all dynamic lights by distance from the camera and the
            // closest lights will appear first in the lists.
            var cameraPosition = camera.transform.position;
            if (sceneDynamicLights.Count > dynamicLightBudget && Vector3.Distance(lastCameraMetricGridPosition, cameraPosition) > 1f)
            {
                lastCameraMetricGridPosition = cameraPosition;
                SortSceneDynamicLights(lastCameraMetricGridPosition);
            }

            // if we exceed the realtime light budget we sort the realtime lights by distance every
            // frame, as we will assume they are moving around.
            if (sceneRealtimeLights.Count > realtimeLightBudget)
            {
                SortSceneRealtimeLights(cameraPosition);
            }

            // clear as many active lights as possible.

            var activeDynamicLightsCount = activeDynamicLights.Count;
            for (int i = activeDynamicLightsCount; i-- > 0;)
                activeDynamicLights.RemoveAt(i);

            var activeRealtimeLightsCount = activeRealtimeLights.Count;
            for (int i = activeRealtimeLightsCount; i-- > 0;)
                activeRealtimeLights.RemoveAt(i);

            // fill the active lights back up with the closest lights.

            var sceneDynamicLightsCount = sceneDynamicLights.Count;
            for (int i = 0; i < sceneDynamicLightsCount; i++)
                if (activeDynamicLights.Count < dynamicLightBudget)
                    activeDynamicLights.Add(sceneDynamicLights[i]);

            var sceneRealtimeLightsCount = sceneRealtimeLights.Count;
            for (int i = 0; i < sceneRealtimeLightsCount; i++)
                if (activeRealtimeLights.Count < realtimeLightBudget)
                    activeRealtimeLights.Add(sceneRealtimeLights[i]);

            // write the active lights into the shader data.

            var idx = 0;
            activeDynamicLightsCount = activeDynamicLights.Count;
            for (int i = 0; i < activeDynamicLightsCount; i++)
            {
                var light = activeDynamicLights[i];
                SetShaderDynamicLight(idx, light);
                UpdateLightEffects(idx, light);
                idx++;
            }

            activeRealtimeLightsCount = activeRealtimeLights.Count;
            for (int i = 0; i < activeRealtimeLightsCount; i++)
            {
                var light = activeRealtimeLights[i];
                SetShaderDynamicLight(idx, light);
                UpdateLightEffects(idx, light);
                idx++;
            }

            /*
            dynamicLights.Sort((a, b) => a.dlmBusy == b.dlmBusy ? (camera.transform.position - a.transform.position).sqrMagnitude
                         .CompareTo((camera.transform.position - b.transform.position).sqrMagnitude) : (a.dlmBusy ? -1 : 1));

            realtimeLights.Sort((a, b) => a.dlmBusy == b.dlmBusy ? (camera.transform.position - a.transform.position).sqrMagnitude
                          .CompareTo((camera.transform.position - b.transform.position).sqrMagnitude) : (a.dlmBusy ? -1 : 1));

            var dynamicLightsCount = dynamicLights.Count;
            for (int i = 0; i < dynamicLightsCount; i++)
            {
                var light = dynamicLights[i];
                if (i > dynamicLightBudget)
                {
                    UpdateLightOutsideBudget(i, light);
                }
                else
                {
                    UpdateLightWithinBudget(i, light);
                }
            }

            dynamicLightsCount = dynamicLightBudget < dynamicLightsCount ? dynamicLightBudget : dynamicLightsCount;
            for (int i = 0; i < dynamicLightsCount; i++)
            {
                var light = dynamicLights[i];
                SetShaderDynamicLight(idx, light);
                UpdateLightEffects(idx, light);
                idx++;
            }
            /*
            var realtimeLightsCount = realtimeLights.Count;
            realtimeLightsCount = realtimeLightBudget < realtimeLightsCount ? realtimeLightBudget : realtimeLightsCount;
            for (int i = 0; i < realtimeLightsCount; i++)
            {
                var light = realtimeLights[i];
                SetShaderDynamicLight(idx, light);
                UpdateLightEffects(idx, light);
                idx++;
            }*/

            // upload the active light data to the graphics card.
            dynamicLightsBuffer.SetData(shaderDynamicLights);
            Shader.SetGlobalInt("dynamic_lights_count", activeDynamicLightsCount + activeRealtimeLightsCount);
        }

        /// <summary>
        /// Sorts the scene dynamic light lists by the distance from the specified origin. The
        /// closests lights will appear first in the list.
        /// </summary>
        /// <param name="origin">The origin (usually the camera world position).</param>
        private void SortSceneDynamicLights(Vector3 origin)
        {
            sceneDynamicLights.Sort((a, b) => (origin - a.transform.position).sqrMagnitude
            .CompareTo((origin - b.transform.position).sqrMagnitude));
        }

        /// <summary>
        /// Sorts the scene realtime light lists by the distance from the specified origin. The
        /// closests lights will appear first in the list.
        /// </summary>
        /// <param name="origin">The origin (usually the camera world position).</param>
        private void SortSceneRealtimeLights(Vector3 origin)
        {
            sceneRealtimeLights.Sort((a, b) => (origin - a.transform.position).sqrMagnitude
            .CompareTo((origin - b.transform.position).sqrMagnitude));
        }

        private void SetShaderDynamicLight(int idx, DynamicLight light)
        {
            shaderDynamicLights[idx].position = light.transform.position;
            shaderDynamicLights[idx].color = new Vector3(light.lightColor.r, light.lightColor.g, light.lightColor.b);
            shaderDynamicLights[idx].intensity = light.lightIntensity;
            shaderDynamicLights[idx].radius = light.lightRadius;
            shaderDynamicLights[idx].channel = light.lightChannel;

            switch (light.lightType)
            {
                case DynamicLightType.Point:
                    shaderDynamicLights[idx].channel &= ~((uint)1 << 6);
                    break;

                case DynamicLightType.Spot:
                    shaderDynamicLights[idx].channel |= (uint)1 << 6;
                    break;
            }

            shaderDynamicLights[idx].forward = light.transform.forward;
            shaderDynamicLights[idx].cutoff = Mathf.Cos(light.lightCutoff * Mathf.Deg2Rad);
            shaderDynamicLights[idx].outerCutoff = Mathf.Cos(light.lightOuterCutoff * Mathf.Deg2Rad);
        }

        private void UpdateLightEffects(int idx, DynamicLight light)
        {
            switch (light.lightEffect)
            {
                case DynamicLightEffect.Steady:
                    break;

                case DynamicLightEffect.Pulse:
                    shaderDynamicLights[idx].intensity *= Mathf.Lerp(light.lightEffectPulseModifier, 1.0f, (1f + Mathf.Sin(Time.time * light.lightEffectPulseSpeed)) * 0.5f);
                    break;

                case DynamicLightEffect.Flicker:
                    shaderDynamicLights[idx].intensity *= Random.value;
                    break;

                case DynamicLightEffect.Strobe:
                    break;
            }
        }

        /// <summary>Called before the light gets drawn. It's within the rendering budget.</summary>

        private void UpdateLightWithinBudget(int idx, DynamicLight light)
        {
            light.dlmFadeoutTime = Time.time + budgetLightFadingTime;
        }

        /// <summary>Called before the light gets drawn. It's outside the rendering budget.</summary>
        private void UpdateLightOutsideBudget(int idx, DynamicLight light)
        {
            var fadeoutInterpolant = Mathf.InverseLerp(1.0f, 0.0f, (light.dlmFadeoutTime - Time.time) / budgetLightFadingTime);
            shaderDynamicLights[idx].intensity *= fadeoutInterpolant;
        }

        private bool LightBudgetBusy(DynamicLight light)
        {
            if ((light.dlmFadeoutTime - Time.time) / budgetLightFadingTime > 0f)
            {
                return true;
            }
            return false;
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (useContinuousPreview)
            {
                // ensure continuous update calls.
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditor.SceneView.RepaintAll();
                }
            }
#endif
        }
    }
}