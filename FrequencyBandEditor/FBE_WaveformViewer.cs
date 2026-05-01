using UnityEditor;
using UnityEngine;

public class FBE_WaveformViewer : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color lineColor = new Color(0.4f, 0.7f, 1f, 1f);
    Color bgColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    Color gridColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    int windowSamples = 128;

    public static FBE_WaveformViewer Open()
    { var w = GetWindow<FBE_WaveformViewer>("Waveform"); w.minSize = new Vector2(300, 150); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        float w = position.width, h = position.height - 20;
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        float[] mono = parentEditor.GetMonoCache();
        AudioClip clip = parentEditor.GetCurrentClip();
        if (mono == null || clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        float time = parentEditor.GetCurrentTime();
        int center = Mathf.Clamp((int)(mono.Length * (time / clip.length)), 0, mono.Length - 1);
        float midY = h / 2f;

        EditorGUI.DrawRect(new Rect(0, midY, w, 1), gridColor);
        EditorGUI.DrawRect(new Rect(w / 2f, 0, 1, h), new Color(1, 1, 1, 0.2f));

        int half = windowSamples / 2;
        int pixelW = (int)w;
        Vector3[] points = new Vector3[pixelW];
        for (int px = 0; px < pixelW; px++)
        {
            int si = center - half + (int)((float)px / w * windowSamples);
            si = Mathf.Clamp(si, 0, mono.Length - 1);
            float y = midY - mono[si] * (h * 0.45f);
            points[px] = new Vector3(px, y, 0);
        }
        Handles.BeginGUI();
        Handles.color = lineColor;
        Handles.DrawAAPolyLine(2f, points);
        Handles.EndGUI();

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label($"{windowSamples} smp", st, GUILayout.Width(55));
        if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Max(64, windowSamples / 2);
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Min(65536, windowSamples * 2);
        EditorGUILayout.EndHorizontal();
    }
}
