using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EdgeOperator = DepthNormalPP.DepthNormalOutlineFeature.EdgeOperator;

namespace DepthNormalPP.Editor
{
    [CustomPropertyDrawer(typeof(EdgeOperator))]
    [CustomPropertyDrawer(typeof(DepthNormalOutlineFeature.OutlineAAMode))]
    public sealed class EdgeOperatorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect kPosition, SerializedProperty kProperty, GUIContent kLabel)
        {
            using (EditorGUI.PropertyScope kScope = new EditorGUI.PropertyScope(kPosition, kLabel, kProperty))
            {
                // The display order and serialized values of these menus differ.
                EditorGUI.BeginChangeCheck();
                Enum eCurrent = (Enum)Enum.ToObject(fieldInfo.FieldType, kProperty.intValue);
                Enum eSelected = EditorGUI.EnumPopup(kPosition, kScope.content, eCurrent);
                if (EditorGUI.EndChangeCheck())
                {
                    kProperty.intValue = Convert.ToInt32(eSelected);
                }
            }
        }
    }

    [CustomEditor(typeof(DepthNormalOutlineFeature)), CanEditMultipleObjects]
    internal sealed class DepthNormalOutlineFeatureEditor : UnityEditor.Editor
    {
        private static readonly Dictionary<string, string> ms_mapFieldLabels = new Dictionary<string, string>
        {
            { nameof(DepthNormalOutlineFeature.m_eAlgorithm), "Algorithm" },
            { nameof(DepthNormalOutlineFeature.m_eQuality), "Quality" },
            { nameof(DepthNormalOutlineFeature.m_eAAMode), "AA Mode" },
            { nameof(DepthNormalOutlineFeature.m_kOutlineColor), "Outline Color" },
            { nameof(DepthNormalOutlineFeature.m_fWidthPixels), "Width Pixels" },
            { nameof(DepthNormalOutlineFeature.m_fDepthThreshold), "Depth Threshold" },
            { nameof(DepthNormalOutlineFeature.m_fNormalThreshold), "Normal Threshold" }
        };

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();
            using (SerializedProperty kProperty = serializedObject.GetIterator())
            {
                bool bEnterChildren = true;
                while (kProperty.NextVisible(bEnterChildren))
                {
                    bEnterChildren = false;
                    GUIContent kLabel = ms_mapFieldLabels.TryGetValue(kProperty.name, out string strLabel)
                        ? new GUIContent(strLabel, kProperty.tooltip)
                        : null;
                    using (new EditorGUI.DisabledScope(kProperty.propertyPath == "m_Script"))
                    {
                        EditorGUILayout.PropertyField(kProperty, kLabel, true);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
