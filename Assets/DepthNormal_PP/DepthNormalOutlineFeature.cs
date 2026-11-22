using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace DepthNormalPP
{
    public sealed class DepthNormalOutlineFeature : ScriptableRendererFeature
    {
        // Inspector order follows estimated cost. Values are the serialized shader pass indices.
        public enum EdgeOperator
        {
            RobertsCross = 0,
            Laplacian = 4,
            CrossMax = 2,
            Sobel = 1,
            Scharr = 3
        }

        public enum OutlineAAMode
        {
            // Keep existing asset values while displaying None first in the Inspector.
            None = 3,
            Coverage = 0,
            FXAA = 1,
            [InspectorName("SMAA 1x")] SMAA = 2
        }

        public enum BufferQuality
        {
            Low,
            Medium,
            High
        }

        [FormerlySerializedAs("algorithm")]
        public EdgeOperator m_eAlgorithm = EdgeOperator.Sobel;

        [FormerlySerializedAs("quality")]
        [Tooltip("Packed texture resolution: 50%, 75%, or 100% of camera width and height.")]
        public BufferQuality m_eQuality = BufferQuality.High;

        [FormerlySerializedAs("antiAliasing"), FormerlySerializedAs("m_bAntiAliasing")]
        [SerializeField, HideInInspector] private bool m_bLegacyAntiAliasing = true;

        [Tooltip("Anti-alias only the outline coverage before blending it with the camera target.")]
        public OutlineAAMode m_eAAMode = OutlineAAMode.FXAA;

        [FormerlySerializedAs("outlineColor")]
        public Color m_kOutlineColor = new Color(0.045f, 0.035f, 0.025f, 1f);

        [FormerlySerializedAs("widthPixels")]
        [Range(1, 8)]
        public int m_iWidthPixels = 2;

        [FormerlySerializedAs("depthThreshold")]
        [Tooltip("Depth change relative to the distance of the centre sample.")]
        [Range(0.001f, 0.2f)]
        public float m_fDepthThreshold = 0.025f;

        [FormerlySerializedAs("normalThreshold")]
        [Tooltip("Magnitude of the change between decoded unit normals.")]
        [Range(0.02f, 2f)]
        public float m_fNormalThreshold = 0.4f;

        [FormerlySerializedAs("captureShader")]
        [SerializeField, HideInInspector]
        private Shader m_kCaptureShader;

        [FormerlySerializedAs("compositeShader")]
        [SerializeField, HideInInspector]
        private Shader m_kCompositeShader;

        private static readonly int ms_iPackedTexture = Shader.PropertyToID("_OutlineDepthNormalTexture");
        private static readonly int ms_iSampleStep = Shader.PropertyToID("_OutlineSampleStep");
        private static readonly int ms_iThresholds = Shader.PropertyToID("_OutlineThresholds");
        private static readonly int ms_iLineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int ms_iCoverageTexture = Shader.PropertyToID("_OutlineCoverageTexture");
        private static readonly int ms_iCoverageStep = Shader.PropertyToID("_OutlineCoverageStep");
        private static readonly int ms_iCoverageGrid = Shader.PropertyToID("_OutlineCoverageGrid");

        private static readonly int ms_iSmaaEdges = Shader.PropertyToID("_OutlineSmaaEdges");
        private static readonly int ms_iSmaaWeights = Shader.PropertyToID("_OutlineSmaaWeights");
        private static readonly int ms_iSmaaArea = Shader.PropertyToID("_OutlineSmaaArea");
        private static readonly int ms_iSmaaSearch = Shader.PropertyToID("_OutlineSmaaSearch");

        [SerializeField, HideInInspector] private Texture2D m_kSmaaArea;
        [SerializeField, HideInInspector] private Texture2D m_kSmaaSearch;

        private Material m_matCapture;
        private Material m_matComposite;
        private CapturePass m_kCapture;
        private CoveragePass m_kCoverage;
        private CompositePass m_kComposite;
        private int m_iTargetMask;
        private int m_iCoverageFilter;
        private int m_iFxaaFilter;
        private int m_iSmaaFilter;
        private int m_iFilterPass;
        private MaskPass m_kSmaaEdges;
        private MaskPass m_kSmaaWeights;
        private GraphicsFormat m_eStencilFormat;
        private GraphicsFormat m_eCoverageFormat;
        private bool m_bUseCoverage;

        public override void Create()
        {
            if (!m_bLegacyAntiAliasing)
            {
                m_eAAMode = OutlineAAMode.None;
                m_bLegacyAntiAliasing = true;
            }

            DisposeMaterials();
            m_iTargetMask = LayerMask.GetMask("DepthNormalOutline");
            if (!m_kCaptureShader || !m_kCompositeShader)
            {
                return;
            }

            m_matCapture = CoreUtils.CreateEngineMaterial(m_kCaptureShader);
            m_matComposite = CoreUtils.CreateEngineMaterial(m_kCompositeShader);
            m_iCoverageFilter = m_matComposite.FindPass("FilterCoverage");
            m_iFxaaFilter = m_matComposite.FindPass("FilterFXAA");
            m_iSmaaFilter = m_matComposite.FindPass("FilterSMAA");
            m_matComposite.SetTexture(ms_iSmaaArea, m_kSmaaArea);
            m_matComposite.SetTexture(ms_iSmaaSearch, m_kSmaaSearch);
            m_eStencilFormat = GraphicsFormatUtility.GetDepthStencilFormat(24, 8);
            m_eCoverageFormat = GraphicsFormat.R8_UNorm;
            if (!SystemInfo.IsFormatSupported(m_eCoverageFormat, FormatUsage.Render) ||
                !SystemInfo.IsFormatSupported(m_eCoverageFormat, FormatUsage.Blend) ||
                !SystemInfo.IsFormatSupported(m_eCoverageFormat, FormatUsage.Linear))
            {
                m_eCoverageFormat = GraphicsFormat.R8G8B8A8_UNorm;
            }

            m_kCapture = new CapturePass(this);
            m_kCoverage = new CoveragePass(this);
            GraphicsFormat eEdgeFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8_UNorm, FormatUsage.Render)
                && SystemInfo.IsFormatSupported(GraphicsFormat.R8G8_UNorm, FormatUsage.Linear)
                ? GraphicsFormat.R8G8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
            m_kSmaaEdges = new MaskPass(this, "SmaaEdges", ms_iSmaaEdges, eEdgeFormat);
            m_kSmaaWeights = new MaskPass(this, "SmaaWeights", ms_iSmaaWeights, GraphicsFormat.R8G8B8A8_UNorm);
            m_kComposite = new CompositePass(this);
        }

        public override void AddRenderPasses(ScriptableRenderer kRenderer, ref RenderingData kRenderingData)
        {
            if (!m_matCapture || !m_matComposite || m_kOutlineColor.a <= 0f ||
                (kRenderingData.cameraData.camera.cullingMask & m_iTargetMask) == 0)
            {
                return;
            }

            bool bSmaa = m_eAAMode == OutlineAAMode.SMAA && m_kSmaaArea && m_kSmaaSearch
                && m_kSmaaEdges.IsAvailable && m_kSmaaWeights.IsAvailable && m_iSmaaFilter >= 0;
            m_iFilterPass = m_eAAMode == OutlineAAMode.Coverage ? m_iCoverageFilter
                : bSmaa ? m_iSmaaFilter : m_iFxaaFilter;
            m_bUseCoverage = m_eAAMode != OutlineAAMode.None && m_iFilterPass >= 0
                && m_eStencilFormat != GraphicsFormat.None;
            kRenderer.EnqueuePass(m_kCapture);
            if (m_bUseCoverage)
            {
                kRenderer.EnqueuePass(m_kCoverage);
                if (bSmaa)
                {
                    kRenderer.EnqueuePass(m_kSmaaEdges);
                    kRenderer.EnqueuePass(m_kSmaaWeights);
                }
            }

            kRenderer.EnqueuePass(m_kComposite);
        }

        protected override void Dispose(bool bDisposing) => DisposeMaterials();

        private void DisposeMaterials()
        {
            CoreUtils.Destroy(m_matCapture);
            CoreUtils.Destroy(m_matComposite);
            m_matCapture = null;
            m_matComposite = null;
        }

        private void DrawEdges(CommandBuffer kCommandBuffer, MaterialPropertyBlock kProperties, Color kColor)
        {
            kProperties.SetColor(ms_iLineColor, kColor);
            kProperties.SetVector(ms_iThresholds, new Vector4(m_fNormalThreshold, m_fDepthThreshold, 0, 0));
            CoreUtils.DrawFullScreen(kCommandBuffer, m_matComposite, kProperties, (int)m_eAlgorithm);
        }

        private sealed class CapturePass : ScriptableRenderPass
        {
            private readonly DepthNormalOutlineFeature m_kFeature;
            private readonly List<ShaderTagId> m_listShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
            private FilteringSettings m_kFilteringSettings;
            public RenderTextureDescriptor Descriptor { get; private set; }

            public CapturePass(DepthNormalOutlineFeature kFeature)
            {
                m_kFeature = kFeature;
                m_kFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, m_kFeature.m_iTargetMask);
                profilingSampler = new ProfilingSampler("Outline / Pack depth and normals");
                // The renderer draws the original target materials at AfterRenderingPrePasses.
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses - 2;
            }

            public override void OnCameraSetup(CommandBuffer kCommandBuffer, ref RenderingData kRenderingData)
            {
                RenderTextureDescriptor kDescriptor = kRenderingData.cameraData.cameraTargetDescriptor;
                float fScale = m_kFeature.m_eQuality == BufferQuality.Low ? 0.5f :
                    m_kFeature.m_eQuality == BufferQuality.Medium ? 0.75f : 1f;
                int iCameraWidth = kDescriptor.width;
                int iCameraHeight = kDescriptor.height;
                kDescriptor.width = Mathf.Max(1, Mathf.RoundToInt(iCameraWidth * fScale));
                kDescriptor.height = Mathf.Max(1, Mathf.RoundToInt(iCameraHeight * fScale));
                kDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                kDescriptor.depthBufferBits = 24;
                if (m_kFeature.m_bUseCoverage)
                {
                    kDescriptor.depthStencilFormat = m_kFeature.m_eStencilFormat;
                }

                kDescriptor.msaaSamples = 1;
                kDescriptor.bindMS = false;
                kDescriptor.useMipMap = false;
                kDescriptor.autoGenerateMips = false;
                kDescriptor.memoryless = RenderTextureMemoryless.None;
                Descriptor = kDescriptor;
                kCommandBuffer.GetTemporaryRT(ms_iPackedTexture, kDescriptor, FilterMode.Point);
                ConfigureTarget(ms_iPackedTexture, ms_iPackedTexture);
                ConfigureClear(ClearFlag.All, Color.clear);
                ConfigureDepthStoreAction(m_kFeature.m_bUseCoverage
                    ? RenderBufferStoreAction.Store
                    : RenderBufferStoreAction.DontCare);

                // Never interpolate packed bytes. Keep width in camera pixels, with a one-texel minimum.
                kCommandBuffer.SetGlobalVector(ms_iSampleStep, new Vector4(
                    Mathf.Max(1f / kDescriptor.width, (float)m_kFeature.m_iWidthPixels / iCameraWidth),
                    Mathf.Max(1f / kDescriptor.height, (float)m_kFeature.m_iWidthPixels / iCameraHeight), 0, 0));
            }

            public override void Execute(ScriptableRenderContext kContext, ref RenderingData kRenderingData)
            {
                DrawingSettings kDrawingSettings = CreateDrawingSettings(
                    m_listShaderTags, ref kRenderingData, kRenderingData.cameraData.defaultOpaqueSortFlags);
                kDrawingSettings.overrideMaterial = m_kFeature.m_matCapture;
                kDrawingSettings.perObjectData = PerObjectData.None;
                kContext.DrawRenderers(kRenderingData.cullResults, ref kDrawingSettings, ref m_kFilteringSettings);
            }

            public override void OnCameraCleanup(CommandBuffer kCommandBuffer)
            {
                kCommandBuffer.ReleaseTemporaryRT(ms_iPackedTexture);
            }
        }

        private sealed class CoveragePass : ScriptableRenderPass
        {
            private readonly DepthNormalOutlineFeature m_kFeature;
            private readonly MaterialPropertyBlock m_kProperties = new MaterialPropertyBlock();

            public CoveragePass(DepthNormalOutlineFeature kFeature)
            {
                m_kFeature = kFeature;
                profilingSampler = new ProfilingSampler("Outline / Generate coverage");
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses - 1;
            }

            public override void OnCameraSetup(CommandBuffer kCommandBuffer, ref RenderingData kRenderingData)
            {
                // Capture is enqueued first. Reuse its stencil to evaluate the detector only
                // on visible target samples, including when the mask is below camera resolution.
                RenderTextureDescriptor kDescriptor = m_kFeature.m_kCapture.Descriptor;
                kDescriptor.graphicsFormat = m_kFeature.m_eCoverageFormat;
                kDescriptor.depthStencilFormat = GraphicsFormat.None;
                FilterMode eCoverageFilter = m_kFeature.m_iFilterPass == m_kFeature.m_iFxaaFilter
                    ? FilterMode.Bilinear : FilterMode.Point;
                kCommandBuffer.GetTemporaryRT(ms_iCoverageTexture, kDescriptor, eCoverageFilter);
                RenderTextureDescriptor kCameraDescriptor = kRenderingData.cameraData.cameraTargetDescriptor;
                kCommandBuffer.SetGlobalVector(ms_iCoverageGrid,
                    new Vector4(kDescriptor.width, kDescriptor.height, 1f / kDescriptor.width, 1f / kDescriptor.height));
                kCommandBuffer.SetGlobalVector(ms_iCoverageStep,
                    new Vector4(1f / kCameraDescriptor.width, 1f / kCameraDescriptor.height, 0, 0));
                ConfigureTarget(ms_iCoverageTexture, ms_iPackedTexture);
                ConfigureClear(ClearFlag.Color, Color.clear);
                ConfigureDepthStoreAction(RenderBufferStoreAction.DontCare);
            }

            public override void Execute(ScriptableRenderContext kContext, ref RenderingData kRenderingData)
            {
                CommandBuffer kCommandBuffer = CommandBufferPool.Get();
                // White blended over the cleared mask stores the detector's coverage in R.
                m_kFeature.DrawEdges(kCommandBuffer, m_kProperties, Color.white);
                kContext.ExecuteCommandBuffer(kCommandBuffer);
                CommandBufferPool.Release(kCommandBuffer);
            }

            public override void OnCameraCleanup(CommandBuffer kCommandBuffer)
            {
                kCommandBuffer.ReleaseTemporaryRT(ms_iCoverageTexture);
            }
        }

        // Both SMAA intermediates are data textures at the coverage grid resolution.
        private sealed class MaskPass : ScriptableRenderPass
        {
            private readonly DepthNormalOutlineFeature m_kFeature;
            private readonly int m_iTexture;
            private readonly int m_iShaderPass;
            private readonly GraphicsFormat m_eFormat;

            public bool IsAvailable => m_iShaderPass >= 0;

            public MaskPass(DepthNormalOutlineFeature kFeature, string strPass, int iTexture,
                GraphicsFormat eFormat)
            {
                m_kFeature = kFeature;
                m_iTexture = iTexture;
                m_iShaderPass = kFeature.m_matComposite.FindPass(strPass);
                m_eFormat = eFormat;
                // URP's stable sort preserves coverage -> edges -> weights at this event.
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses - 1;
                profilingSampler = new ProfilingSampler("Outline / " + strPass);
            }

            public override void OnCameraSetup(CommandBuffer kCommandBuffer, ref RenderingData kRenderingData)
            {
                RenderTextureDescriptor kDescriptor = m_kFeature.m_kCapture.Descriptor;
                kDescriptor.depthStencilFormat = GraphicsFormat.None;
                kDescriptor.graphicsFormat = m_eFormat;
                kCommandBuffer.GetTemporaryRT(m_iTexture, kDescriptor, FilterMode.Bilinear);
                ConfigureTarget(m_iTexture);
                ConfigureClear(ClearFlag.Color, Color.clear);
            }

            public override void Execute(ScriptableRenderContext kContext, ref RenderingData kRenderingData)
            {
                CommandBuffer kCommandBuffer = CommandBufferPool.Get();
                CoreUtils.DrawFullScreen(kCommandBuffer, m_kFeature.m_matComposite, null, m_iShaderPass);
                kContext.ExecuteCommandBuffer(kCommandBuffer);
                CommandBufferPool.Release(kCommandBuffer);
            }

            public override void OnCameraCleanup(CommandBuffer kCommandBuffer)
            {
                kCommandBuffer.ReleaseTemporaryRT(m_iTexture);
            }
        }

        private sealed class CompositePass : ScriptableRenderPass
        {
            private readonly DepthNormalOutlineFeature m_kFeature;
            private readonly MaterialPropertyBlock m_kProperties = new MaterialPropertyBlock();

            public CompositePass(DepthNormalOutlineFeature kFeature)
            {
                m_kFeature = kFeature;
                profilingSampler = new ProfilingSampler("Outline / Screen-space edges");
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques - 1;
            }

            public override void Execute(ScriptableRenderContext kContext, ref RenderingData kRenderingData)
            {
                CommandBuffer kCommandBuffer = CommandBufferPool.Get();
                if (m_kFeature.m_bUseCoverage)
                {
                    m_kProperties.SetColor(ms_iLineColor, m_kFeature.m_kOutlineColor);
                    m_kProperties.SetVector(ms_iThresholds,
                        new Vector4(m_kFeature.m_fNormalThreshold, m_kFeature.m_fDepthThreshold, 0, 0));
                    CoreUtils.DrawFullScreen(kCommandBuffer, m_kFeature.m_matComposite, m_kProperties, m_kFeature.m_iFilterPass);
                }
                else
                {
                    m_kFeature.DrawEdges(kCommandBuffer, m_kProperties, m_kFeature.m_kOutlineColor);
                }

                kContext.ExecuteCommandBuffer(kCommandBuffer);
                CommandBufferPool.Release(kCommandBuffer);
            }
        }
    }
}
