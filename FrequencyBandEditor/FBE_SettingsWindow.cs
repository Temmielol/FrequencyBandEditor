using UnityEditor;
using UnityEngine;

public class FBE_SettingsWindow : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    int selectedTab = 0;
    Vector2 scroll;
    static readonly string[] TabNames = { "Bands", "Colors", "Playback", "Visualizers", "KeyFrames" };

    static readonly Color accent = new Color(0.9f, 0.2f, 0.2f);
    static readonly Color btnTint = new Color(0.35f, 0.35f, 0.35f);
    static readonly Color textHL = new Color(0.9f, 0.9f, 0.9f);

    public static FBE_SettingsWindow Open()
    {
        var w = GetWindow<FBE_SettingsWindow>("KeyUtil Settings");
        w.minSize = new Vector2(300, 250);
        w.Show();
        return w;
    }

    void OnGUI()
    {
        if (parentEditor == null)
        {
            var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>();
            if (e.Length > 0) parentEditor = e[0];
        }
        if (parentEditor == null)
        {
            EditorGUILayout.HelpBox("Open the KeyUtil window first.", MessageType.Warning);
            return;
        }

        Color origBg = GUI.backgroundColor;
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool isActive = (selectedTab == i);
            GUI.backgroundColor = isActive ? accent : btnTint;

            GUIStyle tabStyle = new GUIStyle(GUI.skin.button);
            tabStyle.normal.textColor = isActive ? textHL : new Color(textHL.r, textHL.g, textHL.b, 0.6f);
            tabStyle.fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal;
            tabStyle.fontSize = 10;

            if (GUILayout.Button(TabNames[i], tabStyle, GUILayout.Height(25)))
                selectedTab = i;
        }
        EditorGUILayout.EndHorizontal();
        GUI.backgroundColor = origBg;

        EditorGUILayout.Space(5);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        switch (selectedTab)
        {
            case 0: parentEditor.DrawBandsTab(); break;
            case 1: parentEditor.DrawColorsTab(); break;
            case 2: parentEditor.DrawPlaybackTab(); break;
            case 3: parentEditor.DrawVisualizersTab(); break;
            case 4: parentEditor.DrawKeyFramesTab(); break;
        }

        EditorGUILayout.EndScrollView();
    }

    void Update()
    {
        if (parentEditor != null) Repaint();
    }
}
