using UnityEditor;
using UnityEngine;

public class FBE_Spectrogram : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color bgColor = new Color(0.04f, 0.04f, 0.04f, 1f);
    Color gridColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    const int FFT = 2048;
    const int TEX_W = 512;
    const int TEX_H = 256;

    Texture2D spectroTex;
    Color[] spectroPixels;
    int writeCol;
    int lastCenterSample = -1;

    static readonly Color[] Ramp = {
        new Color(0,0,0,1),
        new Color(0.05f,0.05f,0.3f,1),
        new Color(0.1f,0.2f,0.7f,1),
        new Color(0.1f,0.6f,0.8f,1),
        new Color(0.3f,0.9f,0.5f,1),
        new Color(0.9f,0.9f,0.2f,1),
        new Color(1f,0.6f,0.1f,1),
        new Color(1f,1f,1f,1)
    };

    public static FBE_Spectrogram Open()
    { var w = GetWindow<FBE_Spectrogram>("Spectrogram"); w.minSize = new Vector2(300, 180); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    static Color SampleRamp(float t)
    {
        t = Mathf.Clamp01(t);
        float idx = t * (Ramp.Length - 1);
        int lo = Mathf.FloorToInt(idx);
        int hi = Mathf.Min(lo + 1, Ramp.Length - 1);
        return Color.Lerp(Ramp[lo], Ramp[hi], idx - lo);
    }

    void OnGUI()
    {
        float w = position.width, h = position.height - 18;
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        float[] mono = parentEditor.GetMonoCache();
        AudioClip clip = parentEditor.GetCurrentClip();
        if (mono == null || clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        if (spectroTex == null)
        {
            spectroTex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false);
            spectroTex.filterMode = FilterMode.Bilinear;
            spectroPixels = new Color[TEX_W * TEX_H];
            for (int i = 0; i < spectroPixels.Length; i++) spectroPixels[i] = Color.black;
            spectroTex.SetPixels(spectroPixels);
            spectroTex.Apply();
            writeCol = 0;
        }

        float time = parentEditor.GetCurrentTime();
        int center = Mathf.Clamp((int)(mono.Length * (time / clip.length)), 0, mono.Length - 1);
        float sr = clip.frequency;

        int hop = FFT / 8;
        if (Mathf.Abs(center - lastCenterSample) >= hop)
        {
            lastCenterSample = center;

            float[] re = new float[FFT], im = new float[FFT];
            int start = center - FFT / 2;
            for (int i = 0; i < FFT; i++)
            {
                int idx = Mathf.Clamp(start + i, 0, mono.Length - 1);
                float win = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (FFT - 1)));
                re[i] = mono[idx] * win; im[i] = 0;
            }
            FBE_FFTUtil.DoFFT(re, im);

            float minLog = Mathf.Log10(20f), maxLog = Mathf.Log10(sr / 2f);
            int col = writeCol % TEX_W;
            for (int y = 0; y < TEX_H; y++)
            {
                float normY = (float)y / (TEX_H - 1);
                float freq = Mathf.Pow(10, Mathf.Lerp(minLog, maxLog, normY));
                int k = Mathf.Clamp(Mathf.RoundToInt(freq / sr * FFT), 1, FFT / 2 - 1);

                float mag = 0; int count = 0;
                for (int dk = -1; dk <= 1; dk++)
                {
                    int kk = Mathf.Clamp(k + dk, 1, FFT / 2 - 1);
                    mag += Mathf.Sqrt(re[kk] * re[kk] + im[kk] * im[kk]);
                    count++;
                }
                mag /= count;

                float db = 20f * Mathf.Log10(Mathf.Max(mag, 1e-7f));
                float norm = Mathf.InverseLerp(-80f, 0f, db);
                spectroPixels[y * TEX_W + col] = SampleRamp(norm);
            }
            writeCol++;

            int nextCol = writeCol % TEX_W;
            for (int y = 0; y < TEX_H; y++)
                spectroPixels[y * TEX_W + nextCol] = Color.black;

            spectroTex.SetPixels(spectroPixels);
            spectroTex.Apply();
        }

        float margin = 32f;
        Rect texRect = new Rect(margin, 0, w - margin, h);

        int wc = writeCol % TEX_W;
        float splitFrac = (float)wc / TEX_W;

        GUI.DrawTextureWithTexCoords(
            new Rect(texRect.x, texRect.y, texRect.width * (1f - splitFrac), texRect.height),
            spectroTex,
            new Rect(splitFrac, 0, 1f - splitFrac, 1), true);

        if (splitFrac > 0.001f)
            GUI.DrawTextureWithTexCoords(
                new Rect(texRect.x + texRect.width * (1f - splitFrac), texRect.y, texRect.width * splitFrac, texRect.height),
                spectroTex,
                new Rect(0, 0, splitFrac, 1), true);

        float[] freqMarks = { 100, 500, 1000, 2000, 5000, 10000, 20000 };
        string[] freqLabels = { "100", "500", "1k", "2k", "5k", "10k", "20k" };
        float minLogF = Mathf.Log10(20f), maxLogF = Mathf.Log10(sr / 2f);
        var fs = new GUIStyle(EditorStyles.miniLabel){fontSize=8, alignment=TextAnchor.MiddleRight, normal={textColor=new Color(0.45f,0.45f,0.45f)}};
        for (int f = 0; f < freqMarks.Length; f++)
        {
            if (freqMarks[f] > sr / 2f) break;
            float normF = (Mathf.Log10(freqMarks[f]) - minLogF) / (maxLogF - minLogF);
            float fy = h * (1f - normF);
            GUI.Label(new Rect(0, fy - 6, margin - 2, 12), freqLabels[f], fs);
            EditorGUI.DrawRect(new Rect(margin, fy, w - margin, 1), gridColor);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label("FFT: " + FFT, st, GUILayout.Width(60));
        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
        {
            for (int i = 0; i < spectroPixels.Length; i++) spectroPixels[i] = Color.black;
            spectroTex.SetPixels(spectroPixels); spectroTex.Apply();
            writeCol = 0; lastCenterSample = -1;
        }
        EditorGUILayout.EndHorizontal();
    }
}
