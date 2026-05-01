using UnityEditor;
using UnityEngine;

public class FBE_Oscilloscope : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color lineColor = new Color(0.3f, 1f, 0.4f, 0.7f);
    Color glowColor = new Color(0.3f, 1f, 0.4f, 0.12f);
    Color bgColor = new Color(0.06f, 0.06f, 0.06f, 1f);
    Color axisColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    int windowSamples = 128;

    Texture2D scopeTex;
    Color[] scopePixels;
    int texSize;

    public static FBE_Oscilloscope Open()
    {
        var w = GetWindow<FBE_Oscilloscope>("Oscilloscope");
        w.minSize = new Vector2(200, 200);
        w.Show(); return w;
    }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        AudioClip clip = parentEditor.GetCurrentClip();
        if (clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        float sz = Mathf.Min(position.width, position.height - 20);
        float ox = (position.width - sz) / 2f;
        float cx = ox + sz / 2f, cy = sz / 2f, r = sz / 2f * 0.9f;

        Handles.BeginGUI();
        Handles.color = axisColor;
        Handles.DrawLine(new Vector3(cx, cy - r), new Vector3(cx, cy + r));
        Handles.DrawLine(new Vector3(cx - r, cy), new Vector3(cx + r, cy));
        Handles.color = new Color(axisColor.r, axisColor.g, axisColor.b, 0.3f);
        Handles.DrawLine(new Vector3(cx - r*0.707f, cy - r*0.707f), new Vector3(cx + r*0.707f, cy + r*0.707f));
        Handles.DrawLine(new Vector3(cx - r*0.707f, cy + r*0.707f), new Vector3(cx + r*0.707f, cy - r*0.707f));
        Handles.EndGUI();

        var ls = new GUIStyle(EditorStyles.miniLabel){normal={textColor=new Color(0.4f,0.4f,0.4f)}, fontSize=9, alignment=TextAnchor.MiddleCenter};
        GUI.Label(new Rect(cx + r + 2, cy - 7, 14, 14), "L", ls);
        GUI.Label(new Rect(cx - 6, cy - r - 14, 14, 14), "R", ls);

        int newSize = Mathf.Max(64, Mathf.CeilToInt(sz));
        if (scopeTex == null || texSize != newSize)
        {
            texSize = newSize;
            scopeTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            scopeTex.filterMode = FilterMode.Bilinear;
            scopePixels = new Color[texSize * texSize];
        }

        Color clear = new Color(0, 0, 0, 0);
        for (int i = 0; i < scopePixels.Length; i++) scopePixels[i] = clear;

        float time = parentEditor.GetCurrentTime();
        int centerSample = Mathf.Clamp((int)(clip.samples * (time / clip.length)), 0, clip.samples - 1);
        int channels = clip.channels;
        float texR = texSize * 0.45f;
        float texC = texSize * 0.5f;

        if (channels >= 2)
        {
            float[] raw = parentEditor.GetRawStereo();
            if (raw != null)
            {
                int half = windowSamples / 2;
                float prevPx = -1, prevPy = -1;
                for (int i = 0; i < windowSamples; i++)
                {
                    int s = Mathf.Clamp(centerSample - half + i, 0, clip.samples - 1);
                    float L = raw[s * channels];
                    float R = raw[s * channels + 1];
                    float px = texC + L * texR;
                    float py = texC + R * texR;
                    if (prevPx >= 0)
                        FBE_FFTUtil.DrawTexLine(scopePixels, texSize, prevPx, prevPy, px, py, lineColor, glowColor);
                    prevPx = px; prevPy = py;
                }
            }
        }
        else
        {
            float[] mono = parentEditor.GetMonoCache();
            if (mono != null)
            {
                int half = windowSamples / 2;
                float prevPx = -1, prevPy = -1;
                for (int i = 0; i < windowSamples; i++)
                {
                    int s = Mathf.Clamp(centerSample - half + i, 0, mono.Length - 1);
                    float v = mono[s];
                    float px = texC + v * texR;
                    float py = texC + v * texR;
                    if (prevPx >= 0)
                        FBE_FFTUtil.DrawTexLine(scopePixels, texSize, prevPx, prevPy, px, py, lineColor, glowColor);
                    prevPx = px; prevPy = py;
                }
            }
        }

        scopeTex.SetPixels(scopePixels);
        scopeTex.Apply();
        GUI.DrawTexture(new Rect(ox, 0, sz, sz), scopeTex);

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label($"{windowSamples} smp", st, GUILayout.Width(55));
        if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Max(128, windowSamples / 2);
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Min(8192, windowSamples * 2);
        EditorGUILayout.EndHorizontal();
    }
}
