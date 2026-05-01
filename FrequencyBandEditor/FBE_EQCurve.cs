using UnityEditor;
using UnityEngine;

public class FBE_EQCurve : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color bgColor = new Color(0.08f, 0.10f, 0.12f, 1f);
    Color gridColor = new Color(0.15f, 0.18f, 0.22f, 1f);
    Color curveColor = new Color(0.55f, 0.75f, 0.85f, 1f);
    Color fillColor = new Color(0.35f, 0.55f, 0.65f, 0.25f);
    Color zeroLine = new Color(0.35f, 0.40f, 0.45f, 1f);
    const int FFT = 4096;
    float smoothing = 0.75f;
    float[] smoothBins;
    const int NUM_POINTS = 256;

    static readonly string[] BandLabels = {"SUB","BASS","LOW MID","MID","HIGH MID","PRS","TREBLE"};
    static readonly float[] BandEdges = {20,60,250,500,2000,6000,12000,20000};

    static readonly float[] CNotes = {32.7f,65.4f,130.8f,261.6f,523.3f,1046.5f,2093f,4186f,8372f};

    public static FBE_EQCurve Open()
    { var w = GetWindow<FBE_EQCurve>("EQ Curve"); w.minSize = new Vector2(400, 180); w.Show(); return w; }

    void Update() { if (parentEditor != null && parentEditor.GetIsPlaying()) Repaint(); }

    void OnGUI()
    {
        float w = position.width, h = position.height;
        EditorGUI.DrawRect(new Rect(0, 0, w, h), bgColor);
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

        if (smoothBins == null || smoothBins.Length != NUM_POINTS) smoothBins = new float[NUM_POINTS];
        float minLog = Mathf.Log10(20f), maxLog = Mathf.Log10(Mathf.Min(20000f, sr / 2f));
        float binHz = sr / FFT;

        for (int p = 0; p < NUM_POINTS; p++)
        {
            float normX = (float)p / (NUM_POINTS - 1);
            float freq = Mathf.Pow(10, Mathf.Lerp(minLog, maxLog, normX));
            int k = Mathf.Clamp(Mathf.RoundToInt(freq / binHz), 1, FFT / 2 - 1);

            float mag = 0; int count = 0;
            int spread = Mathf.Max(1, k / 16);
            for (int dk = -spread; dk <= spread; dk++)
            {
                int kk = Mathf.Clamp(k + dk, 1, FFT / 2 - 1);
                mag += Mathf.Sqrt(re[kk] * re[kk] + im[kk] * im[kk]);
                count++;
            }
            mag /= count;

            float db = 20f * Mathf.Log10(Mathf.Max(mag, 1e-7f));
            float norm = Mathf.InverseLerp(-72f, 6f, db);
            smoothBins[p] = Mathf.Lerp(norm, smoothBins[p], smoothing);
        }

        float topBar = 16f;
        float bandBar = 14f;
        float headerH = topBar + bandBar;
        float bottomCtrl = 22f;
        float graphH = h - headerH - bottomCtrl;
        float graphY = headerH;
        float graphBottom = graphY + graphH;

        var noteStyle = new GUIStyle(EditorStyles.miniLabel){fontSize=8, alignment=TextAnchor.MiddleCenter, normal={textColor=new Color(0.5f,0.55f,0.6f)}};
        for (int c = 0; c < CNotes.Length; c++)
        {
            if (CNotes[c] > sr / 2f) break;
            float nx = FreqToX(CNotes[c], minLog, maxLog, w);
            EditorGUI.DrawRect(new Rect(nx, topBar, 1, h - topBar - bottomCtrl), new Color(gridColor.r, gridColor.g, gridColor.b, 0.5f));
            GUI.Label(new Rect(nx - 10, 0, 22, topBar), $"C{c+1}", noteStyle);
        }

        var bandStyle = new GUIStyle(EditorStyles.miniLabel){fontSize=7, alignment=TextAnchor.MiddleCenter, normal={textColor=new Color(0.45f,0.50f,0.55f)}, fontStyle=FontStyle.Bold};
        for (int b = 0; b < BandLabels.Length; b++)
        {
            if (BandEdges[b] > sr / 2f) break;
            float x0 = FreqToX(BandEdges[b], minLog, maxLog, w);
            float x1 = FreqToX(Mathf.Min(BandEdges[b + 1], sr / 2f), minLog, maxLog, w);
            if (b % 2 == 0)
                EditorGUI.DrawRect(new Rect(x0, graphY, x1 - x0, graphH), new Color(0.08f, 0.10f, 0.13f, 0.5f));
            GUI.Label(new Rect(x0, topBar, x1 - x0, bandBar), BandLabels[b], bandStyle);
            EditorGUI.DrawRect(new Rect(x0, topBar, 1, bandBar), gridColor);
        }

        float[] dbLines = {6f, 0f, -6f, -12f, -24f, -36f, -48f, -60f, -72f};
        var dbStyle = new GUIStyle(EditorStyles.miniLabel){fontSize=7, alignment=TextAnchor.MiddleRight, normal={textColor=new Color(0.35f,0.38f,0.42f)}};
        for (int d = 0; d < dbLines.Length; d++)
        {
            float ny = Mathf.InverseLerp(-72f, 6f, dbLines[d]);
            float gy = graphBottom - ny * graphH;
            Color lc = dbLines[d] == 0 ? zeroLine : gridColor;
            EditorGUI.DrawRect(new Rect(0, gy, w, 1), lc);
            if (d % 2 == 0 || dbLines[d] == 0)
                GUI.Label(new Rect(w - 28, gy - 6, 26, 12), $"{dbLines[d]:F0}", dbStyle);
        }

        Handles.BeginGUI();

        Vector3[] curvePoints = new Vector3[NUM_POINTS];
        for (int p = 0; p < NUM_POINTS; p++)
        {
            float x = (float)p / (NUM_POINTS - 1) * w;
            float y = graphBottom - smoothBins[p] * graphH;
            curvePoints[p] = new Vector3(x, y, 0);
        }

        for (int p = 0; p < NUM_POINTS - 1; p++)
        {
            float x0 = curvePoints[p].x;
            float x1 = curvePoints[p + 1].x;
            float y0 = Mathf.Min(curvePoints[p].y, curvePoints[p + 1].y);
            float colW = Mathf.Max(1, x1 - x0);
            EditorGUI.DrawRect(new Rect(x0, y0, colW, graphBottom - y0), fillColor);
        }

        Handles.color = curveColor;
        Handles.DrawAAPolyLine(2f, curvePoints);
        Handles.EndGUI();

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label("Smooth:", st, GUILayout.Width(48));
        smoothing = EditorGUILayout.Slider(smoothing, 0f, 0.95f, GUILayout.Width(130));
        EditorGUILayout.EndHorizontal();
    }

    float FreqToX(float freq, float minLog, float maxLog, float width)
    {
        return (Mathf.Log10(freq) - minLog) / (maxLog - minLog) * width;
    }
}
