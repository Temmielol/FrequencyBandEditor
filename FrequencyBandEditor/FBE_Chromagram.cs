using UnityEditor;
using UnityEngine;

public class FBE_Chromagram : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color bgColor = new Color(0.06f, 0.06f, 0.06f, 1f);
    Color gridColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    const int FFT = 4096;

    static readonly string[] NoteNames = {"C","C#","D","D#","E","F","F#","G","G#","A","A#","B"};
    static readonly Color[] NoteColors = {
        new Color(1f,0.35f,0.35f,1),
        new Color(1f,0.5f,0.3f,1),
        new Color(1f,0.7f,0.2f,1),
        new Color(1f,0.85f,0.2f,1),
        new Color(0.9f,1f,0.3f,1),
        new Color(0.4f,0.95f,0.4f,1),
        new Color(0.3f,0.9f,0.7f,1),
        new Color(0.3f,0.8f,1f,1),
        new Color(0.4f,0.55f,1f,1),
        new Color(0.55f,0.4f,1f,1),
        new Color(0.75f,0.35f,1f,1),
        new Color(1f,0.4f,0.85f,1),
    };

    float[] smoothChroma = new float[12];
    float smoothing = 0.6f;

    const int HIST_W = 256, HIST_H = 12;
    Texture2D histTex;
    Color[] histPixels;
    int histCol;

    public static FBE_Chromagram Open()
    { var w = GetWindow<FBE_Chromagram>("Chromagram"); w.minSize = new Vector2(300, 200); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        float ww = position.width, hh = position.height;
        EditorGUI.DrawRect(new Rect(0, 0, ww, hh), bgColor);
        if (parentEditor == null)
        { var e = Resources.FindObjectsOfTypeAll<FrequencyBandEditor>(); if (e.Length > 0) parentEditor = e[0]; }
        if (parentEditor == null) { GUI.Label(new Rect(10,10,200,20), "Open KeyUtil window"); return; }

        float[] mono = parentEditor.GetMonoCache();
        AudioClip clip = parentEditor.GetCurrentClip();
        if (mono == null || clip == null) { GUI.Label(new Rect(10,10,200,20), "No audio loaded"); return; }

        float time = parentEditor.GetCurrentTime();
        int center = Mathf.Clamp((int)(mono.Length * (time / clip.length)), 0, mono.Length - 1);
        float sr = clip.frequency;

        float[] re = new float[FFT], im = new float[FFT];
        int start = center - FFT / 2;
        for (int i = 0; i < FFT; i++)
        { int idx = Mathf.Clamp(start+i,0,mono.Length-1); float win=0.5f*(1f-Mathf.Cos(2f*Mathf.PI*i/(FFT-1))); re[i]=mono[idx]*win; im[i]=0; }
        FBE_FFTUtil.DoFFT(re, im);

        float[] chroma = new float[12];
        float binHz = sr / FFT;
        for (int k = 1; k < FFT / 2; k++)
        {
            float freq = k * binHz;
            if (freq < 16f || freq > 8000f) continue;
            float mag = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]);
            float semitones = 12f * Mathf.Log(freq / 16.3516f, 2f);
            int pitchClass = ((int)Mathf.Round(semitones) % 12 + 12) % 12;
            chroma[pitchClass] += mag;
        }

        float peak = 0;
        for (int i = 0; i < 12; i++) if (chroma[i] > peak) peak = chroma[i];
        if (peak > 0) for (int i = 0; i < 12; i++) chroma[i] /= peak;
        for (int i = 0; i < 12; i++)
            smoothChroma[i] = Mathf.Lerp(chroma[i], smoothChroma[i], smoothing);

        float labelW = 24f;
        float barAreaW = Mathf.Min(ww * 0.35f, 120f);
        float heatX = labelW + barAreaW + 8;
        float heatW = ww - heatX - 4;
        float rowH = (hh - 22) / 12f;

        for (int n = 0; n < 12; n++)
        {
            float y = n * rowH;
            bool isBlack = (n==1||n==3||n==6||n==8||n==10);

            Color lblC = isBlack ? new Color(0.5f,0.5f,0.5f) : NoteColors[n];
            var ns = new GUIStyle(EditorStyles.miniLabel){fontSize=9, alignment=TextAnchor.MiddleRight, normal={textColor=lblC}, fontStyle=isBlack?FontStyle.Normal:FontStyle.Bold};
            GUI.Label(new Rect(0, y, labelW - 2, rowH), NoteNames[n], ns);

            float barW = smoothChroma[n] * barAreaW;
            Color barCol = NoteColors[n];
            if (isBlack) barCol = new Color(barCol.r*0.7f, barCol.g*0.7f, barCol.b*0.7f, 0.8f);
            EditorGUI.DrawRect(new Rect(labelW, y + 1, barAreaW, rowH - 2), new Color(0.1f,0.1f,0.1f,1));
            EditorGUI.DrawRect(new Rect(labelW, y + 1, barW, rowH - 2), barCol);

            EditorGUI.DrawRect(new Rect(labelW, y, barAreaW, 1), gridColor);
        }

        if (histTex == null)
        {
            histTex = new Texture2D(HIST_W, HIST_H, TextureFormat.RGBA32, false);
            histTex.filterMode = FilterMode.Bilinear;
            histPixels = new Color[HIST_W * HIST_H];
            for (int i = 0; i < histPixels.Length; i++) histPixels[i] = Color.black;
            histTex.SetPixels(histPixels); histTex.Apply();
        }

        int col = histCol % HIST_W;
        for (int n = 0; n < 12; n++)
        {
            float v = smoothChroma[n];
            Color c = Color.Lerp(Color.black, NoteColors[n], v);
            histPixels[n * HIST_W + col] = c;
        }
        histCol++;
        int nextCol = histCol % HIST_W;
        for (int n = 0; n < 12; n++)
            histPixels[n * HIST_W + nextCol] = new Color(0.15f,0.15f,0.15f,1);

        histTex.SetPixels(histPixels); histTex.Apply();

        float splitFrac = (float)(histCol % HIST_W) / HIST_W;
        Rect heatRect = new Rect(heatX, 0, heatW, 12 * rowH);
        GUI.DrawTextureWithTexCoords(
            new Rect(heatRect.x, heatRect.y, heatRect.width * (1f - splitFrac), heatRect.height),
            histTex, new Rect(splitFrac, 0, 1f - splitFrac, 1), true);
        if (splitFrac > 0.001f)
            GUI.DrawTextureWithTexCoords(
                new Rect(heatRect.x + heatRect.width * (1f - splitFrac), heatRect.y, heatRect.width * splitFrac, heatRect.height),
                histTex, new Rect(0, 0, splitFrac, 1), true);

        for (int n = 0; n < 12; n++)
        {
            float y = n * rowH;
            var hs = new GUIStyle(EditorStyles.miniLabel){fontSize=7, alignment=TextAnchor.MiddleLeft, normal={textColor=new Color(0.5f,0.5f,0.5f,0.6f)}};
            GUI.Label(new Rect(heatX + 2, y, 16, rowH), NoteNames[n], hs);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label("Smooth:", st, GUILayout.Width(48));
        smoothing = EditorGUILayout.Slider(smoothing, 0f, 0.95f, GUILayout.Width(130));
        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
        {
            for (int i = 0; i < histPixels.Length; i++) histPixels[i] = Color.black;
            histTex.SetPixels(histPixels); histTex.Apply(); histCol = 0;
        }
        EditorGUILayout.EndHorizontal();
    }
}
