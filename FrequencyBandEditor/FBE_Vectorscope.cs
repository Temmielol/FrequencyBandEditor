using UnityEditor;
using UnityEngine;

public class FBE_Vectorscope : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color lineColor = new Color(0.3f, 0.8f, 1f, 0.6f);
    Color glowColor = new Color(0.3f, 0.8f, 1f, 0.08f);
    Color bgColor = new Color(0.06f, 0.06f, 0.06f, 1f);
    Color axisColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    int windowSamples = 128;

    Texture2D scopeTex;
    Color[] scopePixels;
    int texSize;

    public static FBE_Vectorscope Open()
    { var w = GetWindow<FBE_Vectorscope>("Vectorscope"); w.minSize = new Vector2(200, 220); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        AudioClip clip = parentEditor.GetCurrentClip();
        if (clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        float meterH = 16;
        float sz = Mathf.Min(position.width, position.height - 40 - meterH);
        float ox = (position.width - sz) / 2f;
        float cx = ox + sz/2f, cy = sz/2f, r = sz/2f * 0.9f;

        Handles.BeginGUI();
        Handles.color = axisColor;
        Handles.DrawLine(new Vector3(cx, cy-r), new Vector3(cx, cy+r));
        Handles.DrawLine(new Vector3(cx-r, cy), new Vector3(cx+r, cy));
        Handles.color = new Color(axisColor.r,axisColor.g,axisColor.b,0.4f);
        Handles.DrawLine(new Vector3(cx, cy-r), new Vector3(cx+r, cy));
        Handles.DrawLine(new Vector3(cx, cy-r), new Vector3(cx-r, cy));
        Handles.DrawLine(new Vector3(cx, cy+r), new Vector3(cx+r, cy));
        Handles.DrawLine(new Vector3(cx, cy+r), new Vector3(cx-r, cy));
        Handles.EndGUI();

        var ls = new GUIStyle(EditorStyles.miniLabel){normal={textColor=new Color(0.45f,0.45f,0.45f)},fontSize=9,alignment=TextAnchor.MiddleCenter};
        GUI.Label(new Rect(cx-8,cy-r-14,18,14),"+M",ls);
        GUI.Label(new Rect(cx+r+2,cy-7,14,14),"+S",ls);
        GUI.Label(new Rect(cx-r-18,cy-7,14,14),"-S",ls);
        GUI.Label(new Rect(cx-r-4,cy-r+2,14,14),"L",ls);
        GUI.Label(new Rect(cx+r-8,cy-r+2,14,14),"R",ls);

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
        int centerSample = Mathf.Clamp((int)(clip.samples*(time/clip.length)),0,clip.samples-1);
        int channels = clip.channels;
        float corrSum=0, Lpower=0, Rpower=0;
        float texR = texSize * 0.45f;
        float texC = texSize * 0.5f;

        if (channels >= 2)
        {
            float[] raw = parentEditor.GetRawStereo();
            if (raw == null) return;
            int half = windowSamples / 2;
            float prevPx = -1, prevPy = -1;
            for (int i = 0; i < windowSamples; i++)
            {
                int s = Mathf.Clamp(centerSample-half+i, 0, clip.samples-1);
                float L = raw[s*channels], R = raw[s*channels+1];
                float M = (L+R)*0.5f, S = (L-R)*0.5f;
                float px = texC + S * texR;
                float py = texC + M * texR;
                if (prevPx >= 0)
                    FBE_FFTUtil.DrawTexLine(scopePixels, texSize, prevPx, prevPy, px, py, lineColor, glowColor);
                prevPx = px; prevPy = py;
                corrSum += L*R; Lpower += L*L; Rpower += R*R;
            }
        }
        else
        {
            float[] mono = parentEditor.GetMonoCache();
            if (mono == null) return;
            int half = windowSamples / 2;
            float prevPy = -1;
            for (int i = 0; i < windowSamples; i++)
            {
                int s=Mathf.Clamp(centerSample-half+i,0,mono.Length-1);
                float py = texC + mono[s] * texR;
                if (prevPy >= 0)
                    FBE_FFTUtil.DrawTexLine(scopePixels, texSize, texC, prevPy, texC, py, lineColor, glowColor);
                prevPy = py;
            }
        }

        scopeTex.SetPixels(scopePixels);
        scopeTex.Apply();
        GUI.DrawTexture(new Rect(ox, 0, sz, sz), scopeTex);

        if (channels >= 2)
        {
            float corr = 0;
            float denom = Mathf.Sqrt(Lpower * Rpower);
            if (denom > 1e-7f) corr = corrSum / denom;

            float meterY = sz + 8, meterW = sz * 0.8f, meterX = cx - meterW/2f;
            EditorGUI.DrawRect(new Rect(meterX, meterY, meterW, meterH), new Color(0.12f,0.12f,0.12f,1));
            float centerX = meterX + meterW/2f;
            EditorGUI.DrawRect(new Rect(centerX, meterY, 1, meterH), axisColor);

            Color corrColor = corr > 0.5f ? new Color(0.3f,1f,0.4f,1) : corr > 0f ? new Color(1f,0.9f,0.3f,1) : new Color(1f,0.3f,0.3f,1);
            float indicatorX = centerX + corr*(meterW/2f);
            float barStart = Mathf.Min(centerX, indicatorX), barEnd = Mathf.Max(centerX, indicatorX);
            EditorGUI.DrawRect(new Rect(barStart, meterY+2, barEnd-barStart, meterH-4), corrColor);

            var ms = new GUIStyle(EditorStyles.miniLabel){fontSize=8,alignment=TextAnchor.MiddleCenter,normal={textColor=Color.gray}};
            GUI.Label(new Rect(meterX-14, meterY, 14, meterH), "-1", ms);
            GUI.Label(new Rect(meterX+meterW+2, meterY, 14, meterH), "+1", ms);
            GUI.Label(new Rect(centerX-20, meterY+meterH, 40, 12), $"r={corr:F2}", ms);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label($"{windowSamples} smp", st, GUILayout.Width(55));
        if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Max(128, windowSamples / 2);
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20))) windowSamples = Mathf.Min(8192, windowSamples * 2);
        EditorGUILayout.EndHorizontal();
    }
}
