using System;
using UnityEngine;
using UnityEngine.Serialization;

public class Readme : ScriptableObject
{
    [FormerlySerializedAs("icon")]
    public Texture2D m_kIcon;

    [FormerlySerializedAs("title")]
    public string m_strTitle;

    [FormerlySerializedAs("sections")]
    public Section[] m_arrSections;

    [FormerlySerializedAs("loadedLayout")]
    public bool m_bLoadedLayout;

    [Serializable]
    public class Section
    {
        [FormerlySerializedAs("heading")]
        public string m_strHeading;

        [FormerlySerializedAs("text")]
        public string m_strText;

        [FormerlySerializedAs("linkText")]
        public string m_strLinkText;

        [FormerlySerializedAs("url")]
        public string m_strUrl;
    }
}
