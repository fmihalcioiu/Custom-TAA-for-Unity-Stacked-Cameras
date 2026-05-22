using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using CameraData = UnityEngine.Rendering.Universal.CameraData;
using RenderingData = UnityEngine.Rendering.Universal.RenderingData;

namespace BroadcastDecay.Rendering
{
    public class CustomTaaRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class TaaSettings
        {
            [Range(0.01f, 1f)]
            public float jitterSpread = 0.75f;

            [Range(0f, 0.99f)]
            public float historyWeight = 0.9f;

            [Range(0f, 1f)]
            public float neighborhoodClamp = 0.85f;

            public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        private sealed class CameraState
        {
            public RTHandle history;
            public Matrix4x4 previousViewProjection;
            public Matrix4x4 nonJitteredProjection;
            public int frameIndex;
            public bool hasHistory;
        }

        private sealed class CustomTaaPass : ScriptableRenderPass
        {
            private static readonly int HistoryTexId = Shader.PropertyToID("_HistoryTex");
            private static readonly int PrevViewProjId = Shader.PropertyToID("_PrevViewProj");
            private static readonly int CurrInvViewProjId = Shader.PropertyToID("_CurrInvViewProj");
            private static readonly int HistoryWeightId = Shader.PropertyToID("_HistoryWeight");
            private static readonly int NeighborhoodClampId = Shader.PropertyToID("_NeighborhoodClamp");

            private readonly CustomTaaRendererFeature owner;
            private Camera activeCamera;

            public CustomTaaPass(CustomTaaRendererFeature ownerFeature)
            {
                owner = ownerFeature;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(Camera camera)
            {
                activeCamera = camera;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (owner.material == null || activeCamera == null)
                    return;

                UniversalAdditionalCameraData additionalCameraData = activeCamera.GetUniversalAdditionalCameraData();
                if (!owner.ShouldProcessCamera(activeCamera, additionalCameraData))
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData universalCameraData = frameData.Get<UniversalCameraData>();

                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle sourceColor = resourceData.activeColorTexture;
                if (!sourceColor.IsValid())
                    return;

                CameraState state = owner.GetCameraState(activeCamera);
                if (state == null)
                    return;

                RenderTextureDescriptor historyDesc = universalCameraData.cameraTargetDescriptor;
                historyDesc.msaaSamples = 1;
                historyDesc.depthStencilFormat = GraphicsFormat.None;

                ReallocateHistoryIfNeeded(ref state.history, historyDesc, "_CustomTaaHistory");

                TextureHandle historyTexture = renderGraph.ImportTexture(state.history);

                Matrix4x4 currViewProj = GetGpuViewProjection(
                    activeCamera.worldToCameraMatrix,
                    state.nonJitteredProjection);

                if (!state.hasHistory)
                {
                    renderGraph.AddBlitPass(
                        sourceColor,
                        historyTexture,
                        Vector2.one,
                        Vector2.zero,
                        passName: "Custom TAA Init History");

                    state.previousViewProjection = currViewProj;
                    state.hasHistory = true;
                    return;
                }

                TextureDesc tempDesc = sourceColor.GetDescriptor(renderGraph);
                tempDesc.name = "_CustomTaaTemp";
                tempDesc.clearBuffer = false;
                tempDesc.msaaSamples = MSAASamples.None;

                TextureHandle tempColor = renderGraph.CreateTexture(tempDesc);

                owner.material.SetTexture(HistoryTexId, state.history.rt);
                owner.material.SetMatrix(PrevViewProjId, state.previousViewProjection);
                owner.material.SetMatrix(CurrInvViewProjId, currViewProj.inverse);
                owner.material.SetFloat(HistoryWeightId, owner.settings.historyWeight);
                owner.material.SetFloat(NeighborhoodClampId, owner.settings.neighborhoodClamp);

                RenderGraphUtils.BlitMaterialParameters taaParams =
                    new RenderGraphUtils.BlitMaterialParameters(sourceColor, tempColor, owner.material, 0);

                renderGraph.AddBlitPass(taaParams, "Custom TAA Resolve");

                renderGraph.AddBlitPass(
                    tempColor,
                    sourceColor,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Custom TAA Composite");

                renderGraph.AddBlitPass(
                    tempColor,
                    historyTexture,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Custom TAA Update History");

                state.previousViewProjection = currViewProj;
            }

            private static Matrix4x4 GetGpuViewProjection(Matrix4x4 worldToCamera, Matrix4x4 projection)
            {
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(projection, true);
                return gpuProj * worldToCamera;
            }
        }

        [SerializeField] private Shader taaShader;
        [SerializeField] private TaaSettings settings = new TaaSettings();

        private readonly Dictionary<EntityId, CameraState> cameraStates = new Dictionary<EntityId, CameraState>();
        private CustomTaaPass pass;
        private Material material;

        public override void Create()
        {
            if (taaShader == null)
                taaShader = Shader.Find("Custom/CustomTAA");

            if (taaShader != null)
                material = CoreUtils.CreateEngineMaterial(taaShader);

            pass = new CustomTaaPass(this)
            {
                renderPassEvent = settings.passEvent
            };

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            if (!ShouldProcess(renderingData.cameraData))
                return;

            pass.renderPassEvent = settings.passEvent;
            pass.Setup(renderingData.cameraData.camera);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            if (pass != null)
                pass = null;

            foreach (CameraState state in cameraStates.Values)
            {
                if (state.history != null)
                    state.history.Release();
            }

            cameraStates.Clear();

            if (material != null)
            {
                CoreUtils.Destroy(material);
                material = null;
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null)
                return;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (!ShouldProcessCamera(camera, cameraData))
                return;

            CameraState state = GetCameraState(camera);

            state.nonJitteredProjection = camera.projectionMatrix;

            Vector2 jitter = GetHaltonJitter(state.frameIndex);
            state.frameIndex++;

            float jitterX = ((jitter.x * 2f) - 1f) * settings.jitterSpread / Mathf.Max(1, camera.pixelWidth);
            float jitterY = ((jitter.y * 2f) - 1f) * settings.jitterSpread / Mathf.Max(1, camera.pixelHeight);

            Matrix4x4 jitteredProjection = state.nonJitteredProjection;
            jitteredProjection.m02 += jitterX;
            jitteredProjection.m12 += jitterY;

            camera.nonJitteredProjectionMatrix = state.nonJitteredProjection;
            camera.projectionMatrix = jitteredProjection;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null)
                return;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (!ShouldProcessCamera(camera, cameraData))
                return;

            camera.ResetProjectionMatrix();
        }

        private CameraState GetCameraState(Camera camera)
        {
            EntityId id = camera.GetEntityId();
            if (!cameraStates.TryGetValue(id, out CameraState state))
            {
                state = new CameraState();
                cameraStates.Add(id, state);
            }

            return state;
        }

        private bool ShouldProcess(in CameraData cameraData)
        {
            return ShouldProcessCamera(cameraData.camera, cameraData.camera.GetUniversalAdditionalCameraData());
        }

        private bool ShouldProcessCamera(Camera camera, UniversalAdditionalCameraData cameraData)
        {
            if (camera == null || cameraData == null)
                return false;

            if (camera.cameraType != CameraType.Game)
                return false;

            // Base world camera only. Overlay/UI camera is skipped.
            if (cameraData.renderType != CameraRenderType.Base)
                return false;

            return true;
        }

        private static Vector2 GetHaltonJitter(int frameIndex)
        {
            int index = (frameIndex % 1024) + 1;
            return new Vector2(Halton(index, 2), Halton(index, 3));
        }

        private static float Halton(int index, int b)
        {
            float result = 0f;
            float f = 1f / b;
            int i = index;

            while (i > 0)
            {
                result += f * (i % b);
                i /= b;
                f /= b;
            }

            return result;
        }
        private static void ReallocateHistoryIfNeeded(ref RTHandle handle, in RenderTextureDescriptor descriptor, string name)
        {
            bool needsAlloc =
                handle == null
                || handle.rt == null
                || handle.rt.width != descriptor.width
                || handle.rt.height != descriptor.height
                || handle.rt.descriptor.graphicsFormat != descriptor.graphicsFormat
                || handle.rt.descriptor.depthStencilFormat != descriptor.depthStencilFormat
                || handle.rt.descriptor.msaaSamples != descriptor.msaaSamples;

            if (!needsAlloc)
                return;

            if (handle != null)
                RTHandles.Release(handle);

            GraphicsFormat format = descriptor.depthStencilFormat != GraphicsFormat.None
                ? descriptor.depthStencilFormat
                : descriptor.graphicsFormat;

            RTHandleAllocInfo allocInfo = new RTHandleAllocInfo(name: name)
            {
                format = format,
                filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp,
                msaaSamples = (MSAASamples)descriptor.msaaSamples,
                dimension = descriptor.dimension,
                useDynamicScale = descriptor.useDynamicScale,
                useDynamicScaleExplicit = descriptor.useDynamicScaleExplicit
            };

            handle = RTHandles.Alloc(descriptor.width, descriptor.height, allocInfo);
        }
    }
}