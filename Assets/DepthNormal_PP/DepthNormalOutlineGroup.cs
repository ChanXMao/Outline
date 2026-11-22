using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace DepthNormalPP
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class DepthNormalOutlineGroup : MonoBehaviour
    {
        [FormerlySerializedAs("targetCamera")]
        [Tooltip("Camera using DepthNormalRenderer.")]
        public Camera _kTargetCamera;

        // Route only this camera's draw calls through the reserved layer. The scene,
        // prefabs and gameplay keep their authored layers between camera renders.
        private readonly List<Renderer> m_listRenderers = new List<Renderer>();
        private readonly List<(GameObject goTarget, int iLayer)> m_listSavedLayers = new List<(GameObject, int)>();
        private int m_iOutlineLayer;

        private void OnEnable()
        {
            m_iOutlineLayer = LayerMask.NameToLayer("DepthNormalOutline");
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            RestoreLayers();
        }

        private void BeforeCamera(ScriptableRenderContext kContext, Camera kCamera)
        {
            if (kCamera != _kTargetCamera || m_iOutlineLayer < 0 ||
                (kCamera.cullingMask & (1 << m_iOutlineLayer)) == 0)
            {
                return;
            }

            GetComponentsInChildren(false, m_listRenderers);
            foreach (Renderer kRenderer in m_listRenderers)
            {
                GameObject goTarget = kRenderer.gameObject;
                int iLayer = goTarget.layer;
                if (!kRenderer.enabled || iLayer == m_iOutlineLayer || (kCamera.cullingMask & (1 << iLayer)) == 0)
                {
                    continue;
                }

                m_listSavedLayers.Add((goTarget, iLayer));
                goTarget.layer = m_iOutlineLayer;
            }
        }

        private void AfterCamera(ScriptableRenderContext kContext, Camera kCamera)
        {
            if (kCamera == _kTargetCamera)
            {
                RestoreLayers();
            }
        }

        private void RestoreLayers()
        {
            foreach ((GameObject goTarget, int iLayer) in m_listSavedLayers)
            {
                if (goTarget)
                {
                    goTarget.layer = iLayer;
                }
            }

            m_listSavedLayers.Clear();
        }
    }
}
