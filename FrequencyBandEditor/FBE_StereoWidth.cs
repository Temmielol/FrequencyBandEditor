using UnityEditor;
using UnityEngine;

public class FBE_StereoWidth : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color bgColor = new Color(0.06f, 0.06f, 0.06f, 1f);
    Color widthColor = new Color(0.4f, 0.8f, 1f, 1f);
    Color corrColor = new Color(0.3f, 1f, 0.5f, 1f);
    Color gridColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    Color monoColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    int windowSamples = 1024;

    const int HISTORY = 256;
    float[] widthHistory = new float[HISTORY];
    float[] corrHistory = new float[HISTORY];
    int histIdx;

    public static FBE_StereoWidth Open()
    { var w = GetWindow<FBE_StereoWidth>("Stereo Width"); w.minSize = new Vector2(250, 150); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        float w = position.width, h = position.height;
        EditorGUI.DrawRect(new Rect(0, 0, w, h), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        AudioClip clip = parentEditor.GetCurrentClip();
        if (clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }
        int channels = clip.channels;

        float time = parentEditor.GetCurrentTime();
        int centerSample = Mathf.Clamp((int)(clip.samples * (time / clip.length)), 0, clip.samples - 1);

        float width = 0f, corr = 0f;

        if (channels >= 2)
        {
            float[] raw = parentEditor.GetRawStereo();
            if (raw == null) return;
            int half = windowSamples / 2;
            float Lp = 0, Rp = 0, LR = 0, sPow = 0, mPow = 0;
            for (int i = 0; i < windowSamples; i++)
            {
                int s = Mathf.Clamp(centerSample - half + i, 0, clip.samples - 1);
                float L = raw[s * channels], R = raw[s * channels + 1];
                float mid = (L + R) * 0.5f, side = (L - R) * 0.5f;
                Lp += L * L; Rp += R * R; LR += L * R;
                sPow += side * side;
                mPow += mid * mid;
            }
            float totalE = mPow + sPow;
            width = totalE > 1e-7f ? sPow / totalE : 0f;

            float denom = Mathf.Sqrt(Lp * Rp);
            corr = denom > 1e-7f ? LR / denom : 0f;
        }

        widthHistory[histIdx % HISTORY] = width;
        corrHistory[histIdx % HISTORY] = corr;
        histIdx++;

        float graphH = h * 0.55f;
        float meterY = graphH + 4;

        EditorGUI.DrawRect(new Rect(0, graphH * 0.5f, w, 1), gridColor);
        EditorGUI.DrawRect(new Rect(0, graphH, w, 1), new Color(0.15f, 0.15f, 0.15f, 1));

        Handles.BeginGUI();
        Vector3[] wPts = new Vector3[HISTORY];
        Vector3[] cPts = new Vector3[HISTORY];
        for (int i = 0; i < HISTORY; i++)
        {
            int idx = (histIdx + i) % HISTORY;
            float x = (float)i / (HISTORY - 1) * w;
            wPts[i] = new Vector3(x, graphH * (1f - widthHistory[idx]), 0);
            cPts[i] = new Vector3(x, graphH * (1f - (corrHistory[idx] * 0.5f + 0.5f)), 0);
        }
        Handles.color = widthColor;
        Handles.DrawAAPolyLine(1.5f, wPts);
        Handles.color = new Color(corrColor.r, corrColor.g, corrColor.b, 0.5f);
        Handles.DrawAAPolyLine(1.5f, cPts);
        Handles.EndGUI();

        var gs = new GUIStyle(EditorStyles.miniLabel){fontSize=8, normal={textColor=new Color(0.45f,0.45f,0.45f)}};
        GUI.Label(new Rect(2, 1, 40, 12), "Wide", gs);
        GUI.Label(new Rect(2, graphH - 13, 40, 12), "Mono", gs);

        var ls = new GUIStyle(EditorStyles.miniLabel){fontSize=8};
        ls.normal.textColor = widthColor;
        GUI.Label(new Rect(w - 90, 1, 40, 12), "Width", ls);
        ls.normal.textColor = corrColor;
        GUI.Label(new Rect(w - 45, 1, 40, 12), "Corr", ls);

        float barH = 12f;
        float barMaxW = w - 80;

        float wBarY = meterY + 8;
        EditorGUI.DrawRect(new Rect(60, wBarY, barMaxW, barH), new Color(0.12f, 0.12f, 0.12f, 1));
        EditorGUI.DrawRect(new Rect(60, wBarY, barMaxW * Mathf.Clamp01(width), barH), widthColor);
        var ms = new GUIStyle(EditorStyles.miniLabel){fontSize=9, normal={textColor=widthColor}};
        GUI.Label(new Rect(4, wBarY - 1, 54, 14), $"W: {width*100:F0}%", ms);

        float cBarY = wBarY + barH + 6;
        EditorGUI.DrawRect(new Rect(60, cBarY, barMaxW, barH), new Color(0.12f, 0.12f, 0.12f, 1));
        float cMid = 60 + barMaxW * 0.5f;
        EditorGUI.DrawRect(new Rect(cMid, cBarY, 1, barH), gridColor);
        Color cCol = corr > 0.5f ? new Color(0.3f, 1f, 0.4f, 1) : corr > 0f ? new Color(1f, 0.9f, 0.3f, 1) : new Color(1f, 0.3f, 0.3f, 1);
        float cX = cMid + corr * (barMaxW * 0.5f);
        float barStart = Mathf.Min(cMid, cX), barEnd = Mathf.Max(cMid, cX);
        EditorGUI.DrawRect(new Rect(barStart, cBarY + 1, barEnd - barStart, barH - 2), cCol);
        ms.normal.textColor = cCol;
        GUI.Label(new Rect(4, cBarY - 1, 54, 14), $"r: {corr:F2}", ms);

        if (channels < 2)
        {
            var monoS = new GUIStyle(EditorStyles.boldLabel){alignment=TextAnchor.MiddleCenter, normal={textColor=monoColor}};
            GUI.Label(new Rect(0, h/2 - 10, w, 20), "Mono Source", monoS);
        }
    }
}
