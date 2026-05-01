using UnityEditor;
using UnityEngine;

public class FBE_SpectrumAnalyzer : EditorWindow
{
    public FrequencyBandEditor parentEditor;
    Color barColor = new Color(0.3f, 0.6f, 1f, 1f);
    Color bgColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    Color gridColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    const int FFT = 2048;
    float[] smoothBins;
    float smoothing = 0.7f;

    public static FBE_SpectrumAnalyzer Open()
    { var w = GetWindow<FBE_SpectrumAnalyzer>("Spectrum"); w.minSize = new Vector2(300, 150); w.Show(); return w; }

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

        float[] re = new float[FFT], im = new float[FFT];
        int start = center - FFT / 2;
        for (int i = 0; i < FFT; i++)
        { int idx = Mathf.Clamp(start+i,0,mono.Length-1); float win=0.5f*(1f-Mathf.Cos(2f*Mathf.PI*i/(FFT-1))); re[i]=mono[idx]*win; im[i]=0; }
        FBE_FFTUtil.DoFFT(re, im);

        int numBars = Mathf.Min(96, (int)w / 4);
        if (smoothBins == null || smoothBins.Length != numBars) smoothBins = new float[numBars];
        float sr = clip.frequency;
        float minLog = Mathf.Log10(20f), maxLog = Mathf.Log10(sr / 2f);

        for (int b = 0; b < numBars; b++)
        { float f0=Mathf.Pow(10,Mathf.Lerp(minLog,maxLog,(float)b/numBars)); float f1=Mathf.Pow(10,Mathf.Lerp(minLog,maxLog,(float)(b+1)/numBars));
          int k0=Mathf.Max(1,Mathf.FloorToInt(f0/sr*FFT)); int k1=Mathf.Min(FFT/2-1,Mathf.CeilToInt(f1/sr*FFT));
          float mag=0;int count=0; for(int k=k0;k<=k1;k++){mag+=Mathf.Sqrt(re[k]*re[k]+im[k]*im[k]);count++;}
          if(count>0)mag/=count;
          float norm=Mathf.InverseLerp(-80f,0f,20f*Mathf.Log10(Mathf.Max(mag,1e-7f)));
          smoothBins[b]=Mathf.Lerp(norm,smoothBins[b],smoothing); }

        float barW = w / numBars;
        float[] gf={100,500,1000,5000,10000,20000}; string[] gl={"100","500","1k","5k","10k","20k"};
        for (int g=0;g<gf.Length;g++)
        { float gx=(Mathf.Log10(gf[g])-minLog)/(maxLog-minLog)*w;
          EditorGUI.DrawRect(new Rect(gx,0,1,h),gridColor);
          GUI.Label(new Rect(gx+2,h-14,30,14),gl[g],new GUIStyle(EditorStyles.miniLabel){normal={textColor=gridColor},fontSize=9}); }
        for (int b=0;b<numBars;b++)
        { float barH=smoothBins[b]*h; Color c=Color.Lerp(barColor,new Color(0.2f,1f,0.9f,1),smoothBins[b]);
          EditorGUI.DrawRect(new Rect(b*barW,h-barH,Mathf.Max(1,barW-1),barH),c); }

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        var st = new GUIStyle(EditorStyles.miniLabel){normal={textColor=Color.gray}};
        GUILayout.Label("Smooth:", st, GUILayout.Width(48));
        smoothing = EditorGUILayout.Slider(smoothing, 0f, 0.95f, GUILayout.Width(130));
        EditorGUILayout.EndHorizontal();
    }
}
