using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

[CustomEditor(typeof(Readme))]
[InitializeOnLoad]
public class ReadmeEditor : Editor
{
    private const string ms_strShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";
    private const string ms_strReadmeSourceDirectory = "Assets/TutorialInfo";
    private const float ms_fSpace = 16f;

    static ReadmeEditor()
    {
        EditorApplication.delayCall += SelectReadmeAutomatically;
    }

    private static void RemoveTutorial()
    {
        if (EditorUtility.DisplayDialog("Remove Readme Assets",
            $"All contents under {ms_strReadmeSourceDirectory} will be removed, are you sure you want to proceed?",
            "Proceed",
            "Cancel"))
        {
            if (Directory.Exists(ms_strReadmeSourceDirectory))
            {
                FileUtil.DeleteFileOrDirectory(ms_strReadmeSourceDirectory);
                FileUtil.DeleteFileOrDirectory(ms_strReadmeSourceDirectory + ".meta");
            }
            else
            {
                Debug.Log($"Could not find the Readme folder at {ms_strReadmeSourceDirectory}");
            }

            Readme kReadmeAsset = SelectReadme();
            if (kReadmeAsset != null)
            {
                string strPath = AssetDatabase.GetAssetPath(kReadmeAsset);
                FileUtil.DeleteFileOrDirectory(strPath + ".meta");
                FileUtil.DeleteFileOrDirectory(strPath);
            }

            AssetDatabase.Refresh();
        }
    }

    private static void SelectReadmeAutomatically()
    {
        if (!SessionState.GetBool(ms_strShowedReadmeSessionStateName, false))
        {
            Readme kReadme = SelectReadme();
            SessionState.SetBool(ms_strShowedReadmeSessionStateName, true);

            if (kReadme && !kReadme.m_bLoadedLayout)
            {
                LoadLayout();
                kReadme.m_bLoadedLayout = true;
            }
        }
    }

    private static void LoadLayout()
    {
        Assembly kAssembly = typeof(EditorApplication).Assembly;
        Type kWindowLayoutType = kAssembly.GetType("UnityEditor.WindowLayout", true);
        MethodInfo kMethod = kWindowLayoutType.GetMethod("LoadWindowLayout", BindingFlags.Public | BindingFlags.Static);
        kMethod.Invoke(null, new object[] { Path.Combine(Application.dataPath, "TutorialInfo/Layout.wlt"), false });
    }

    private static Readme SelectReadme()
    {
        string[] arrIds = AssetDatabase.FindAssets("Readme t:Readme");
        if (arrIds.Length == 1)
        {
            UnityEngine.Object kReadmeObject = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(arrIds[0]));

            Selection.objects = new UnityEngine.Object[] { kReadmeObject };

            return (Readme)kReadmeObject;
        }
        else
        {
            Debug.Log("Couldn't find a readme");
            return null;
        }
    }

    protected override void OnHeaderGUI()
    {
        Readme kReadme = (Readme)target;
        Init();

        float fIconWidth = Mathf.Min(EditorGUIUtility.currentViewWidth / 3f - 20f, 128f);

        GUILayout.BeginHorizontal("In BigTitle");
        {
            if (kReadme.m_kIcon != null)
            {
                GUILayout.Space(ms_fSpace);
                GUILayout.Label(kReadme.m_kIcon, GUILayout.Width(fIconWidth), GUILayout.Height(fIconWidth));
            }
            GUILayout.Space(ms_fSpace);
            GUILayout.BeginVertical();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(kReadme.m_strTitle, TitleStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();
    }

    public override void OnInspectorGUI()
    {
        Readme kReadme = (Readme)target;
        Init();

        foreach (Readme.Section kSection in kReadme.m_arrSections)
        {
            if (!string.IsNullOrEmpty(kSection.m_strHeading))
            {
                GUILayout.Label(kSection.m_strHeading, HeadingStyle);
            }

            if (!string.IsNullOrEmpty(kSection.m_strText))
            {
                GUILayout.Label(kSection.m_strText, BodyStyle);
            }

            if (!string.IsNullOrEmpty(kSection.m_strLinkText))
            {
                if (LinkLabel(new GUIContent(kSection.m_strLinkText)))
                {
                    Application.OpenURL(kSection.m_strUrl);
                }
            }

            GUILayout.Space(ms_fSpace);
        }

        if (GUILayout.Button("Remove Readme Assets", ButtonStyle))
        {
            RemoveTutorial();
        }
    }

    private bool m_bInitialized;

    [FormerlySerializedAs("m_LinkStyle")]
    [SerializeField]
    private GUIStyle m_kLinkStyle;

    private GUIStyle LinkStyle => m_kLinkStyle;

    [FormerlySerializedAs("m_TitleStyle")]
    [SerializeField]
    private GUIStyle m_kTitleStyle;

    private GUIStyle TitleStyle => m_kTitleStyle;

    [FormerlySerializedAs("m_HeadingStyle")]
    [SerializeField]
    private GUIStyle m_kHeadingStyle;

    private GUIStyle HeadingStyle => m_kHeadingStyle;

    [FormerlySerializedAs("m_BodyStyle")]
    [SerializeField]
    private GUIStyle m_kBodyStyle;

    private GUIStyle BodyStyle => m_kBodyStyle;

    [FormerlySerializedAs("m_ButtonStyle")]
    [SerializeField]
    private GUIStyle m_kButtonStyle;

    private GUIStyle ButtonStyle => m_kButtonStyle;

    private void Init()
    {
        if (m_bInitialized)
        {
            return;
        }

        m_kBodyStyle = new GUIStyle(EditorStyles.label);
        m_kBodyStyle.wordWrap = true;
        m_kBodyStyle.fontSize = 14;
        m_kBodyStyle.richText = true;

        m_kTitleStyle = new GUIStyle(m_kBodyStyle);
        m_kTitleStyle.fontSize = 26;

        m_kHeadingStyle = new GUIStyle(m_kBodyStyle);
        m_kHeadingStyle.fontStyle = FontStyle.Bold;
        m_kHeadingStyle.fontSize = 18;

        m_kLinkStyle = new GUIStyle(m_kBodyStyle);
        m_kLinkStyle.wordWrap = false;

        // Match selection color which works nicely for both light and dark skins
        m_kLinkStyle.normal.textColor = new Color(0x00 / 255f, 0x78 / 255f, 0xDA / 255f, 1f);
        m_kLinkStyle.stretchWidth = false;

        m_kButtonStyle = new GUIStyle(EditorStyles.miniButton);
        m_kButtonStyle.fontStyle = FontStyle.Bold;

        m_bInitialized = true;
    }

    private bool LinkLabel(GUIContent kLabel, params GUILayoutOption[] arrOptions)
    {
        Rect kPosition = GUILayoutUtility.GetRect(kLabel, LinkStyle, arrOptions);

        Handles.BeginGUI();
        Handles.color = LinkStyle.normal.textColor;
        Handles.DrawLine(new Vector3(kPosition.xMin, kPosition.yMax), new Vector3(kPosition.xMax, kPosition.yMax));
        Handles.color = Color.white;
        Handles.EndGUI();

        EditorGUIUtility.AddCursorRect(kPosition, MouseCursor.Link);

        return GUI.Button(kPosition, kLabel, LinkStyle);
    }
}
