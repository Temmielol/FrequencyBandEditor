using UnityEditor;
using UnityEngine;

public class FBE_LoudnessMeter : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color bgColor = new Color(0.06f, 0.06f, 0.06f, 1f);
    Color rmsColor = new Color(0.3f, 0.75f, 1f, 1f);
    Color peakColor = new Color(1f, 0.85f, 0.2f, 1f);
    Color clipColor = new Color(1f, 0.2f, 0.2f, 1f);
    Color gridColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    int windowSamples = 2048;

    float smoothRMS, smoothPeak;
    float peakHold, peakHoldTimer;
    const float PEAK_HOLD_TIME = 1.5f;
    const float SMOOTH = 0.8f;

    public static FBE_LoudnessMeter Open()
    { var w = GetWindow<FBE_LoudnessMeter>("Loudness"); w.minSize = new Vector2(120, 200); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        float[] mono = parentEditor.GetMonoCache();
        AudioClip clip = parentEditor.GetCurrentClip();
        if (mono == null || clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        float time = parentEditor.GetCurrentTime();
        int center = Mathf.Clamp((int)(mono.Length * (time / clip.length)), 0, mono.Length - 1);

        int half = windowSamples / 2;
        float sumSq = 0, peak = 0;
        for (int i = 0; i < windowSamples; i++)
        {
            int s = Mathf.Clamp(center - half + i, 0, mono.Length - 1);
            float v = Mathf.Abs(mono[s]);
            sumSq += mono[s] * mono[s];
            if (v > peak) peak = v;
        }
        float rms = Mathf.Sqrt(sumSq / windowSamples);

        float rmsDB = 20f * Mathf.Log10(Mathf.Max(rms, 1e-7f));
        float peakDB = 20f * Mathf.Log10(Mathf.Max(peak, 1e-7f));

        float targetRMS = Mathf.InverseLerp(-60f, 0f, rmsDB);
        float targetPeak = Mathf.InverseLerp(-60f, 0f, peakDB);
        smoothRMS = Mathf.Lerp(targetRMS, smoothRMS, SMOOTH);
        smoothPeak = Mathf.Lerp(targetPeak, smoothPeak, SMOOTH);

        if (targetPeak >= peakHold) { peakHold = targetPeak; peakHoldTimer = PEAK_HOLD_TIME; }
        else { peakHoldTimer -= 0.016f; if (peakHoldTimer <= 0) peakHold = Mathf.Lerp(peakHold, targetPeak, 0.1f); }

        float w = position.width, h = position.height - 20;
        float margin = 30f;
        float barArea = w - margin;
        float barW = Mathf.Max(20f, barArea / 2f - 8f);
        float rmsX = margin + (barArea - barW * 2 - 8) / 2f;
        float peakX = rmsX + barW + 8;

        float[] dbMarks = { 0f, -6f, -12f, -24f, -36f, -48f, -60f };
        var gridStyle = new GUIStyle(EditorStyles.miniLabel){normal={textColor=new Color(0.4f,0.4f,0.4f)}, fontSize=8, alignment=TextAnchor.MiddleRight};
        for (int g = 0; g < dbMarks.Length; g++)
        {
            float ny = Mathf.InverseLerp(-60f, 0f, dbMarks[g]);
            float gy = h * (1f - ny);
            EditorGUI.DrawRect(new Rect(margin - 2, gy, barArea + 4, 1), gridColor);
            GUI.Label(new Rect(0, gy - 7, margin - 4, 14), $"{dbMarks[g]:F0}", gridStyle);
        }

        float rmsH = smoothRMS * h;
        Color rmsC = rmsDB > -6f ? clipColor : rmsColor;
        EditorGUI.DrawRect(new Rect(rmsX, h - rmsH, barW, rmsH), rmsC);

        float peakH = smoothPeak * h;
        Color peakC = peakDB > -1f ? clipColor : peakColor;
        EditorGUI.DrawRect(new Rect(peakX, h - peakH, barW, peakH), peakC);

        float holdY = h * (1f - peakHold);
        EditorGUI.DrawRect(new Rect(peakX, holdY, barW, 2), Color.white);

        var lblS = new GUIStyle(EditorStyles.miniLabel){alignment=TextAnchor.MiddleCenter, normal={textColor=Color.gray}, fontSize=9};
        GUI.Label(new Rect(rmsX, h + 2, barW, 14), "RMS", lblS);
        GUI.Label(new Rect(peakX, h + 2, barW, 14), "Peak", lblS);

        var dbS = new GUIStyle(EditorStyles.miniLabel){alignment=TextAnchor.UpperCenter, normal={textColor=rmsC}, fontSize=9};
        GUI.Label(new Rect(rmsX, 2, barW, 14), $"{rmsDB:F1}dB", dbS);
        dbS.normal.textColor = peakC;
        GUI.Label(new Rect(peakX, 2, barW, 14), $"{peakDB:F1}dB", dbS);
    }
}
