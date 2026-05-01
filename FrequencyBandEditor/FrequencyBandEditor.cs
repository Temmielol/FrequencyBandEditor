#pragma warning disable 0162

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class FrequencyBandEditor : EditorWindow
{
    AudioClip audioClip;
    AudioClip filteredClip;
    bool filteredReady;
    float currentTime, lastTime, lastLastTime;
    bool isPlaying, hasStopped;
    bool instantReplay;
    float playbackVolume = 1f;

    Texture2D combinedTexture;
    Texture2D[] bandTextures;
    bool combinedGenerated;
    bool bandsGenerated;

    bool splitMode = false;
    float[][] bandAudio;
    float[] monoCache;
    int cachedSamples;
    float cachedRate;
    bool bandAudioBuilt;

    public static readonly string[] BandNames = {
        "Sub-Bass", "Bass", "Low-Mid", "Mid", "Upper-Mid", "High"
    };
    public static readonly float[][] BandRanges = {
        new float[]{20f,60f}, new float[]{60f,250f}, new float[]{250f,500f},
        new float[]{500f,2000f}, new float[]{2000f,6000f}, new float[]{6000f,20000f}
    };
    public static readonly Color[] DefaultColors = {
        new Color(0.90f,0.20f,0.20f,1), new Color(0.95f,0.55f,0.15f,1),
        new Color(0.95f,0.90f,0.20f,1), new Color(0.30f,0.85f,0.35f,1),
        new Color(0.30f,0.60f,0.95f,1), new Color(0.70f,0.35f,0.90f,1)
    };

    Color[] bandColors;
    bool[] bandVisible, bandMuted, bandSolo;
    Color bgColor = new Color(0.15f,0.15f,0.15f,1f);
    Color waveColor = new Color(0.4f,0.7f,1f,1f);

    const int TEX_W = 16384, TEX_H = 200, FFT_SIZE = 2048;
    static string FilteredWavPath => "Assets/FBE_filtered_temp.wav";

    volatile bool _asyncCombinedRunning;
    Color[] _asyncCombinedPixels;
    volatile bool _asyncCombinedDone;

    volatile bool _asyncBandAudioRunning;
    float[][] _asyncBandAudioResult;
    float[] _asyncMonoResult;
    int _asyncCachedSamples;
    float _asyncCachedRate;
    volatile bool _asyncBandAudioDone;

    volatile bool _asyncBandTexRunning;
    Color[][] _asyncBandTexPixels;
    volatile bool _asyncBandTexDone;

    volatile bool _asyncFilteredRunning;
    float[] _asyncFilteredMixed;
    int _asyncFilteredRate;
    volatile bool _asyncFilteredDone;
    bool _pendingFilteredRebuild;

    bool easyCopyPaste;
    KeyCode copyKey = KeyCode.Keypad1;
    bool copyCtrl, copyShift, copyAlt;
    KeyCode pasteKey = KeyCode.Keypad2;
    bool pasteCtrl, pasteShift, pasteAlt;
    int listeningForBind = 0;

    struct CopiedKey
    {
        public EditorCurveBinding binding;
        public Keyframe keyframe;
        public float timeOffset;
    }
    System.Collections.Generic.List<CopiedKey> copiedKeys = new System.Collections.Generic.List<CopiedKey>();

    [MenuItem("Temmie/KeyUtil")]
    static void OpenWindow()
    {
        var w = GetWindow<FrequencyBandEditor>("KeyUtil");
        w.minSize = new Vector2(500, 100);
        w.Show();
    }

    void OnEnable()
    {
        int n = BandNames.Length;
        bandColors = new Color[n]; bandVisible = new bool[n];
        bandMuted = new bool[n]; bandSolo = new bool[n];
        for (int i = 0; i < n; i++)
        {
            bandColors[i] = new Color(
                EditorPrefs.GetFloat($"FBE_C{i}R", DefaultColors[i].r),
                EditorPrefs.GetFloat($"FBE_C{i}G", DefaultColors[i].g),
                EditorPrefs.GetFloat($"FBE_C{i}B", DefaultColors[i].b), 1f);
            bandVisible[i] = EditorPrefs.GetBool($"FBE_V{i}", true);
            bandMuted[i] = EditorPrefs.GetBool($"FBE_M{i}", false);
        }
        bgColor = new Color(
            EditorPrefs.GetFloat("FBE_BGR",0.15f),
            EditorPrefs.GetFloat("FBE_BGG",0.15f),
            EditorPrefs.GetFloat("FBE_BGB",0.15f), 1f);
        waveColor = new Color(
            EditorPrefs.GetFloat("FBE_WR",0.4f),
            EditorPrefs.GetFloat("FBE_WG",0.7f),
            EditorPrefs.GetFloat("FBE_WB",1f), 1f);
        playbackVolume = EditorPrefs.GetFloat("FBE_Vol", 1f);

        easyCopyPaste = EditorPrefs.GetBool("FBE_EasyCP", false);
        copyKey = (KeyCode)EditorPrefs.GetInt("FBE_CopyKey", (int)KeyCode.Keypad1);
        copyCtrl = EditorPrefs.GetBool("FBE_CopyCtrl", false);
        copyShift = EditorPrefs.GetBool("FBE_CopyShift", false);
        copyAlt = EditorPrefs.GetBool("FBE_CopyAlt", false);
        pasteKey = (KeyCode)EditorPrefs.GetInt("FBE_PasteKey", (int)KeyCode.Keypad2);
        pasteCtrl = EditorPrefs.GetBool("FBE_PasteCtrl", false);
        pasteShift = EditorPrefs.GetBool("FBE_PasteShift", false);
        pasteAlt = EditorPrefs.GetBool("FBE_PasteAlt", false);

        HookGlobalKeys(true);

        string p = EditorPrefs.GetString("FBE_Clip","");
        if (!string.IsNullOrEmpty(p))
        {
            audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            if (audioClip != null)
                GenerateCombinedWaveform();
        }
    }

    void OnDisable()
    {
        HookGlobalKeys(false);
        DestroyPreviewAudioSource();
        if (File.Exists(FilteredWavPath))
            AssetDatabase.DeleteAsset(FilteredWavPath);
    }

    static System.Reflection.FieldInfo s_globalEventField;
    static FrequencyBandEditor s_instance;

    void HookGlobalKeys(bool hook)
    {
        s_instance = hook ? this : null;
        if (s_globalEventField == null)
            s_globalEventField = typeof(EditorApplication).GetField("globalEventHandler", BindingFlags.Static | BindingFlags.NonPublic);
        if (s_globalEventField == null) return;

        var handler = (EditorApplication.CallbackFunction)s_globalEventField.GetValue(null);
        handler -= GlobalKeyHandler;
        if (hook) handler += GlobalKeyHandler;
        s_globalEventField.SetValue(null, handler);
    }

    static void GlobalKeyHandler()
    {
        if (s_instance == null) return;
        if (!s_instance.easyCopyPaste && s_instance.listeningForBind == 0) return;

        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;

        if (s_instance.listeningForBind > 0)
        {
            if (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl ||
                e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift ||
                e.keyCode == KeyCode.LeftAlt || e.keyCode == KeyCode.RightAlt ||
                e.keyCode == KeyCode.LeftCommand || e.keyCode == KeyCode.RightCommand)
                return;

            bool ctrl = e.control || e.command, shift = e.shift, alt = e.alt;

            if (s_instance.listeningForBind == 1)
            {
                s_instance.copyKey = e.keyCode; s_instance.copyCtrl = ctrl; s_instance.copyShift = shift; s_instance.copyAlt = alt;
                EditorPrefs.SetInt("FBE_CopyKey", (int)e.keyCode);
                EditorPrefs.SetBool("FBE_CopyCtrl", ctrl);
                EditorPrefs.SetBool("FBE_CopyShift", shift);
                EditorPrefs.SetBool("FBE_CopyAlt", alt);
            }
            else
            {
                s_instance.pasteKey = e.keyCode; s_instance.pasteCtrl = ctrl; s_instance.pasteShift = shift; s_instance.pasteAlt = alt;
                EditorPrefs.SetInt("FBE_PasteKey", (int)e.keyCode);
                EditorPrefs.SetBool("FBE_PasteCtrl", ctrl);
                EditorPrefs.SetBool("FBE_PasteShift", shift);
                EditorPrefs.SetBool("FBE_PasteAlt", alt);
            }

            s_instance.listeningForBind = 0;
            e.Use();
            var sw = Resources.FindObjectsOfTypeAll<FBE_SettingsWindow>();
            if (sw.Length > 0) sw[0].Repaint();
            s_instance.Repaint();
            return;
        }

        if (!s_instance.easyCopyPaste) return;
        bool c = e.control || e.command, s = e.shift, a = e.alt;

        if (e.keyCode == s_instance.copyKey && c == s_instance.copyCtrl && s == s_instance.copyShift && a == s_instance.copyAlt)
        {
            s_instance.EasyCopy(); e.Use();
        }
        else if (e.keyCode == s_instance.pasteKey && c == s_instance.pasteCtrl && s == s_instance.pasteShift && a == s_instance.pasteAlt)
        {
            s_instance.EasyPaste(); e.Use();
        }
    }

    bool IsBandAudible(int b)
    {
        bool anySolo = false;
        for (int i = 0; i < BandNames.Length; i++) if (bandSolo[i]) { anySolo = true; break; }
        return anySolo ? (bandSolo[b] && !bandMuted[b]) : !bandMuted[b];
    }
    bool AnyFiltered()
    {
        for (int b = 0; b < BandNames.Length; b++) if (!IsBandAudible(b)) return true;
        return false;
    }
    void OnMuteChanged()
    {
        StopAllClips(); isPlaying = false; hasStopped = true;
        if (bandAudio == null || _asyncBandAudioRunning)
        {
            _pendingFilteredRebuild = true;
            filteredReady = false;
        }
        else
        {
            RebuildFilteredClip();
        }
    }

    void GenerateCombinedWaveform()
    {
        if (audioClip == null || _asyncCombinedRunning) return;

        int ch = audioClip.channels, total = audioClip.samples;
        float[] raw = new float[total * ch];
        audioClip.GetData(raw, 0);
        Color wc = waveColor;

        _asyncCombinedRunning = true;
        _asyncCombinedDone = false;

        Task.Run(() =>
        {
            float[] mono = new float[total];
            for (int s = 0; s < total; s++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += raw[s * ch + c];
                mono[s] = sum / ch;
            }

            int step = (int)Math.Ceiling((double)total / TEX_W);
            Color[] pixels = new Color[TEX_W * TEX_H];
            Color clear = new Color(0,0,0,0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            for (int x = 0; x < TEX_W; x++)
            {
                int idx = Math.Min(x * step, total - 1);
                int barH = (int)Math.Ceiling(Math.Min(Math.Abs(mono[idx]) * TEX_H, TEX_H));
                int add = mono[idx] > 0 ? 1 : -1;
                int mid = TEX_H / 2;
                for (int j = 0; j < barH; j++)
                {
                    int y = mid - (barH / 2 * add) + (j * add);
                    if (y >= 0 && y < TEX_H)
                        pixels[y * TEX_W + x] = wc;
                }
            }

            _asyncCombinedPixels = pixels;
            _asyncCombinedDone = true;
        });
    }

    void EnsureSplitData()
    {
        if (!bandAudioBuilt && !_asyncBandAudioRunning) BuildBandAudio();
        if (bandAudioBuilt && !bandsGenerated && !_asyncBandTexRunning) GenerateBandTextures();
    }

    void BuildBandAudio()
    {
        if (audioClip == null || _asyncBandAudioRunning) return;

        int ch = audioClip.channels, total = audioClip.samples;
        float sr = audioClip.frequency;
        float[] raw = new float[total * ch];
        audioClip.GetData(raw, 0);

        _asyncBandAudioRunning = true;
        _asyncBandAudioDone = false;

        Task.Run(() =>
        {
            float[] mono = new float[total];
            for (int s = 0; s < total; s++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += raw[s * ch + c];
                mono[s] = sum / ch;
            }

            int bc = BandNames.Length;
            float[][] result = new float[bc][];
            float binHz = sr / FFT_SIZE;

            float[] win = new float[FFT_SIZE];
            for (int i = 0; i < FFT_SIZE; i++)
                win[i] = 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * i / (FFT_SIZE - 1)));
            int hop = FFT_SIZE / 4;

            float[] envelope = new float[total];
            for (int pos = 0; pos + FFT_SIZE <= total; pos += hop)
                for (int i = 0; i < FFT_SIZE; i++)
                    if (pos + i < total) envelope[pos + i] += win[i] * win[i];
            for (int i = 0; i < total; i++)
                if (envelope[i] < 1e-7f) envelope[i] = 1e-7f;

            for (int b = 0; b < bc; b++)
            {
                bool[] keep = new bool[FFT_SIZE / 2 + 1];
                for (int k = 0; k <= FFT_SIZE / 2; k++)
                    if (k * binHz >= BandRanges[b][0] && k * binHz <= BandRanges[b][1])
                        keep[k] = true;

                float[] output = new float[total];
                float[] re = new float[FFT_SIZE], im = new float[FFT_SIZE];

                for (int pos = 0; pos + FFT_SIZE <= total; pos += hop)
                {
                    for (int i = 0; i < FFT_SIZE; i++) { re[i] = mono[pos + i] * win[i]; im[i] = 0f; }
                    FFT(re, im, false);
                    for (int k = 0; k <= FFT_SIZE / 2; k++)
                        if (!keep[k]) { re[k] = 0; im[k] = 0; if (k > 0 && k < FFT_SIZE / 2) { re[FFT_SIZE - k] = 0; im[FFT_SIZE - k] = 0; } }
                    FFT(re, im, true);
                    for (int i = 0; i < FFT_SIZE; i++) if (pos + i < total) output[pos + i] += re[i] * win[i];
                }
                for (int i = 0; i < total; i++) output[i] /= envelope[i];
                result[b] = output;
            }

            _asyncBandAudioResult = result;
            _asyncMonoResult = mono;
            _asyncCachedSamples = total;
            _asyncCachedRate = sr;
            _asyncBandAudioDone = true;
        });
    }

    void GenerateBandTextures()
    {
        if (audioClip == null || _asyncBandTexRunning) return;
        int ch = audioClip.channels, total = audioClip.samples;
        float sr = audioClip.frequency;
        float[] raw = new float[total * ch];
        audioClip.GetData(raw, 0);
        Color[] colors = (Color[])bandColors.Clone();

        _asyncBandTexRunning = true;
        _asyncBandTexDone = false;

        Task.Run(() =>
        {
            float[] mono = new float[total];
            for (int s = 0; s < total; s++)
            { float sum = 0; for (int c = 0; c < ch; c++) sum += raw[s * ch + c]; mono[s] = sum / ch; }

            int bc = BandNames.Length;
            float binHz = sr / FFT_SIZE;
            float[] win = new float[FFT_SIZE];
            for (int i = 0; i < FFT_SIZE; i++)
                win[i] = 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * i / (FFT_SIZE - 1)));

            int[][] bins = new int[bc][];
            for (int b = 0; b < bc; b++)
            {
                int lo = Math.Max(1, (int)Math.Floor(BandRanges[b][0] / binHz));
                int hi = Math.Min(FFT_SIZE / 2 - 1, (int)Math.Ceiling(BandRanges[b][1] / binHz));
                bins[b] = new int[] { lo, hi };
            }

            float[][] energy = new float[bc][];
            for (int b = 0; b < bc; b++) energy[b] = new float[TEX_W];
            float[] peak = new float[bc];
            float[] re = new float[FFT_SIZE], im = new float[FFT_SIZE];

            for (int px = 0; px < TEX_W; px++)
            {
                int center = (int)((long)px * (total - 1) / (TEX_W - 1));
                int start = center - FFT_SIZE / 2;
                for (int i = 0; i < FFT_SIZE; i++)
                {
                    int idx = start + i;
                    if (idx < 0) idx = -idx;
                    if (idx >= total) idx = 2 * (total - 1) - idx;
                    idx = Math.Max(0, Math.Min(idx, total - 1));
                    re[i] = mono[idx] * win[i]; im[i] = 0f;
                }
                FFT(re, im, false);
                for (int b = 0; b < bc; b++)
                {
                    float e = 0;
                    for (int k = bins[b][0]; k <= bins[b][1]; k++)
                        e += (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    e /= Math.Max(1, bins[b][1] - bins[b][0] + 1);
                    energy[b][px] = e; if (e > peak[b]) peak[b] = e;
                }
            }

            Color[][] allPixels = new Color[bc][];
            for (int b = 0; b < bc; b++)
            {
                Color[] pixels = new Color[TEX_W * TEX_H];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0,0,0,0);
                float p = peak[b] > 0 ? peak[b] : 1f; Color col = colors[b];
                for (int x = 0; x < TEX_W; x++)
                {
                    int barH = Math.Max(0, Math.Min((int)Math.Ceiling(energy[b][x] / p * TEX_H), TEX_H));
                    int mid = TEX_H / 2;
                    for (int j = 0; j < barH / 2; j++)
                    {
                        if (mid + j < TEX_H) pixels[(mid + j) * TEX_W + x] = col;
                        if (mid - j >= 0) pixels[(mid - j) * TEX_W + x] = col;
                    }
                }
                allPixels[b] = pixels;
            }

            _asyncBandTexPixels = allPixels;
            _asyncBandTexDone = true;
        });
    }

    void RebuildFilteredClip()
    {
        if (audioClip == null || bandAudio == null) return;
        if (_asyncFilteredRunning)
        {
            _pendingFilteredRebuild = true;
            return;
        }
        filteredReady = false;
        if (!AnyFiltered()) { filteredClip = audioClip; filteredReady = true; return; }

        int samples = cachedSamples;
        int rate = (int)cachedRate;
        int bc = BandNames.Length;
        bool[] audible = new bool[bc];
        float[][] ba = bandAudio;
        for (int b = 0; b < bc; b++) audible[b] = IsBandAudible(b);

        _asyncFilteredRunning = true;
        _asyncFilteredDone = false;

        Task.Run(() =>
        {
            float[] mixed = new float[samples];
            for (int b = 0; b < bc; b++)
            {
                if (!audible[b]) continue;
                float[] src = ba[b];
                for (int i = 0; i < samples; i++) mixed[i] += src[i];
            }

            _asyncFilteredMixed = mixed;
            _asyncFilteredRate = rate;
            _asyncFilteredDone = true;
        });
    }

    static void WriteWav32(string path, float[] samples, int sampleRate)
    {
        int count = samples.Length, dataSize = count * 4;
        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(new char[]{'R','I','F','F'}); bw.Write(36 + dataSize);
            bw.Write(new char[]{'W','A','V','E'});
            bw.Write(new char[]{'f','m','t',' '}); bw.Write(16);
            bw.Write((short)3); bw.Write((short)1); bw.Write(sampleRate);
            bw.Write(sampleRate * 4); bw.Write((short)4); bw.Write((short)32);
            bw.Write(new char[]{'d','a','t','a'}); bw.Write(dataSize);
            for (int i = 0; i < count; i++) bw.Write(samples[i]);
        }
    }

    static void FFT(float[] real, float[] imag, bool inv)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
          if (i < j) { float t = real[i]; real[i] = real[j]; real[j] = t; t = imag[i]; imag[i] = imag[j]; imag[j] = t; } }
        for (int len = 2; len <= n; len <<= 1)
        { float a = 2f * Mathf.PI / len * (inv ? -1f : 1f); float wR = Mathf.Cos(a), wI = Mathf.Sin(a);
          for (int i = 0; i < n; i += len)
          { float cR = 1f, cI = 0f;
            for (int j = 0; j < len / 2; j++)
            { float uR = real[i+j], uI = imag[i+j];
              float vR = real[i+j+len/2]*cR - imag[i+j+len/2]*cI;
              float vI = real[i+j+len/2]*cI + imag[i+j+len/2]*cR;
              real[i+j]=uR+vR; imag[i+j]=uI+vI;
              real[i+j+len/2]=uR-vR; imag[i+j+len/2]=uI-vI;
              float nc=cR*wR-cI*wI; cI=cR*wI+cI*wR; cR=nc; } } }
        if (inv) for (int i = 0; i < n; i++) { real[i] /= n; imag[i] /= n; }
    }

    float controlsHeight = 0;

    void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bgColor);

        ProcessEasyCopyPasteKeys();

        if (audioClip != null)
        {
            bool anyAsync = _asyncCombinedRunning || _asyncBandAudioRunning || _asyncBandTexRunning || _asyncFilteredRunning;
            if (anyAsync)
            {
                var ls = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 0.9f) } };
                string dots = new string('.', (int)(EditorApplication.timeSinceStartup * 3) % 4);
                GUI.Label(new Rect(position.width / 2f - 80, position.height / 2f, 200, 30), "Processing" + dots, ls);
            }

            if (splitMode)
            {
                if (bandsGenerated && bandTextures != null) DrawSplitWaveforms();
            }
            else
            {
                if (combinedGenerated && combinedTexture != null) DrawCombinedWaveform();
            }
        }
        else
        {
            combinedGenerated = false; bandsGenerated = false;
            var s = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.gray } };
            GUI.Label(new Rect(position.width / 2f - 80, position.height / 2f, 200, 30), "Drop AudioClip Here", s);
        }

        EditorGUI.DrawRect(new Rect(0, 0, position.width, controlsHeight + 4), new Color(bgColor.r, bgColor.g, bgColor.b, 0.85f));

        float waveStartX = 200;
        {
            Rect ds = GetRect(); Vector2 tr = GetTranslation();
            if (ds.width > 0) waveStartX = ds.x + tr.x;
        }
        float btnAreaW = Mathf.Max(120, waveStartX - 8);

        EditorGUILayout.BeginHorizontal(GUILayout.MaxWidth(btnAreaW));
        Color oldBg = GUI.backgroundColor;

        float halfW = (btnAreaW - 4) / 2f;
        GUI.backgroundColor = splitMode ? new Color(0.9f,0.2f,0.2f,1) : new Color(0.35f,0.35f,0.35f,1);
        GUIStyle splitStyle = new GUIStyle(GUI.skin.button);
        splitStyle.fontStyle = splitMode ? FontStyle.Bold : FontStyle.Normal;
        splitStyle.normal.textColor = splitMode ? Color.white : new Color(0.9f,0.9f,0.9f,0.7f);
        bool newSplit = GUILayout.Toggle(splitMode, splitMode ? "Unsplit" : "Split", splitStyle, GUILayout.Width(halfW), GUILayout.Height(25));
        if (newSplit != splitMode)
        {
            splitMode = newSplit;
            if (splitMode && audioClip != null) EnsureSplitData();
        }

        GUI.backgroundColor = new Color(0.35f,0.35f,0.35f,1);
        GUIStyle settingsStyle = new GUIStyle(GUI.skin.button);
        settingsStyle.normal.textColor = new Color(0.9f,0.9f,0.9f,0.7f);
        if (GUILayout.Button("Settings", settingsStyle, GUILayout.Width(halfW), GUILayout.Height(25)))
        { var w = FBE_SettingsWindow.Open(); w.parentEditor = this; }

        GUI.backgroundColor = oldBg;
        EditorGUILayout.EndHorizontal();

        Rect lastRect = GUILayoutUtility.GetRect(0, 0);
        controlsHeight = lastRect.y;

        HandleDragAndDrop();
    }

    void DrawCombinedWaveform()
    {
        Rect shown = GetShownArea();
        Rect dsRect = GetRect();
        Vector2 trans = GetTranslation();
        float hierW = GetHierarchyWidth();
        bool hasAnim = shown.width > 0 && dsRect.width > 0;

        float top = controlsHeight + 4;
        Rect wr;
        if (hasAnim)
        {
            float ww = (audioClip.length * dsRect.width) / shown.width;
            wr = new Rect(dsRect.x + trans.x, top, ww, position.height - top - 2);
        }
        else
            wr = new Rect(2, top, position.width - 4, position.height - top - 2);

        GUI.DrawTexture(wr, combinedTexture);

        if (hasAnim && audioClip.length > 0)
        {
            float ww = (audioClip.length * dsRect.width) / shown.width;
            float cx = dsRect.x + trans.x + (currentTime / audioClip.length) * ww;
            EditorGUI.DrawRect(new Rect(cx, 0, 1, position.height), Color.white);
        }
        if (hasAnim)
        {
            Rect block = new Rect(hierW - dsRect.width, 0, dsRect.width, position.height);
            EditorGUI.DrawRect(block, bgColor);
        }
    }

    void DrawSplitWaveforms()
    {
        Rect shown = GetShownArea();
        Rect dsRect = GetRect();
        Vector2 trans = GetTranslation();
        float hierW = GetHierarchyWidth();
        bool hasAnim = shown.width > 0 && dsRect.width > 0;

        int visCount = 0;
        for (int b = 0; b < BandNames.Length; b++) if (bandVisible[b]) visCount++;
        if (visCount == 0) return;

        float top = controlsHeight + 4;
        float perBand = (position.height - top - 2) / visCount;
        float yOff = top;
        int drawn = 0;

        for (int b = 0; b < BandNames.Length; b++)
        {
            if (!bandVisible[b]) continue;
            Rect wr;
            if (hasAnim)
            {
                float ww = (audioClip.length * dsRect.width) / shown.width;
                wr = new Rect(dsRect.x + trans.x, yOff + drawn * perBand, ww, perBand - 2);
            }
            else
                wr = new Rect(2, yOff + drawn * perBand, position.width - 4, perBand - 2);

            bool aud = IsBandAudible(b);
            GUI.color = new Color(1,1,1, aud ? 1f : 0.2f);
            GUI.DrawTexture(wr, bandTextures[b]);
            GUI.color = Color.white;

            Rect lb = new Rect(wr.x + 2, wr.y + 1, 84, 14);
            EditorGUI.DrawRect(lb, new Color(0,0,0,0.6f));
            string tag = bandMuted[b] ? " [M]" : bandSolo[b] ? " [S]" : "";
            Color lc = aud ? bandColors[b] : new Color(bandColors[b].r,bandColors[b].g,bandColors[b].b,0.35f);
            GUI.Label(lb, " "+BandNames[b]+tag, new GUIStyle(EditorStyles.miniLabel){normal={textColor=lc}});
            drawn++;
        }

        if (hasAnim && audioClip.length > 0)
        {
            float ww = (audioClip.length * dsRect.width) / shown.width;
            float cx = dsRect.x + trans.x + (currentTime / audioClip.length) * ww;
            EditorGUI.DrawRect(new Rect(cx, yOff, 1, drawn * perBand), Color.white);
        }
        if (hasAnim)
        {
            Rect block = new Rect(hierW - dsRect.width, 0, dsRect.width, position.height);
            EditorGUI.DrawRect(block, bgColor);
        }
    }

    public void DrawBandsTab()
    {
        bool changed = false;

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Frequency Bands", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        for (int b = 0; b < BandNames.Length; b++)
        {
            bool audible = IsBandAudible(b);
            EditorGUILayout.BeginHorizontal();

            Rect sr = GUILayoutUtility.GetRect(14, 20, GUILayout.Width(14));
            EditorGUI.DrawRect(sr, audible ? bandColors[b] : new Color(bandColors[b].r,bandColors[b].g,bandColors[b].b,0.3f));

            var ns = new GUIStyle(EditorStyles.label){normal={textColor=audible?Color.white:new Color(1,1,1,0.3f)}, fontStyle=FontStyle.Bold};
            GUILayout.Label(BandNames[b], ns, GUILayout.Width(70));

            string lo = BandRanges[b][0] >= 1000 ? $"{BandRanges[b][0]/1000f:F0}k" : $"{BandRanges[b][0]:F0}";
            string hi = BandRanges[b][1] >= 1000 ? $"{BandRanges[b][1]/1000f:F0}k" : $"{BandRanges[b][1]:F0}";
            GUILayout.Label($"{lo}-{hi}Hz", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(65));

            GUILayout.FlexibleSpace();

            bool nv = GUILayout.Toggle(bandVisible[b], "Show", GUILayout.Width(48), GUILayout.Height(18));
            if (nv != bandVisible[b]) { bandVisible[b] = nv; EditorPrefs.SetBool($"FBE_V{b}", nv); }

            bool nm = GUILayout.Toggle(bandMuted[b], "M", GUILayout.Width(30), GUILayout.Height(18));
            if (nm != bandMuted[b]) { bandMuted[b] = nm; EditorPrefs.SetBool($"FBE_M{b}", nm); if (nm) bandSolo[b] = false; changed = true; }

            bool nsol = GUILayout.Toggle(bandSolo[b], "S", GUILayout.Width(26), GUILayout.Height(18));
            if (nsol != bandSolo[b]) { bandSolo[b] = nsol; if (nsol) bandMuted[b] = false; changed = true; }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Unmute All", GUILayout.Height(25)))
        { for(int b=0;b<BandNames.Length;b++){bandMuted[b]=false;bandSolo[b]=false;EditorPrefs.SetBool($"FBE_M{b}",false);} changed=true; }
        if (GUILayout.Button("Clear Solo", GUILayout.Height(25)))
        { for(int b=0;b<BandNames.Length;b++) bandSolo[b]=false; changed=true; }
        EditorGUILayout.EndHorizontal();

        if (changed) OnMuteChanged();
    }

    public void DrawColorsTab()
    {
        bool regen = false;

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Waveform Colors", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        if (!splitMode)
        {
            Color nw = EditorGUILayout.ColorField("Waveform", waveColor);
            if (nw != waveColor)
            {
                waveColor = nw;
                EditorPrefs.SetFloat("FBE_WR",nw.r); EditorPrefs.SetFloat("FBE_WG",nw.g); EditorPrefs.SetFloat("FBE_WB",nw.b);
                if (audioClip != null) GenerateCombinedWaveform();
            }
        }

        for (int b = 0; b < BandNames.Length; b++)
        {
            Color nc = EditorGUILayout.ColorField(BandNames[b], bandColors[b]);
            if (nc != bandColors[b])
            { bandColors[b]=nc; EditorPrefs.SetFloat($"FBE_C{b}R",nc.r); EditorPrefs.SetFloat($"FBE_C{b}G",nc.g); EditorPrefs.SetFloat($"FBE_C{b}B",nc.b); regen=true; }
        }

        Color nb = EditorGUILayout.ColorField("Background", bgColor);
        if (nb != bgColor) { bgColor=nb; EditorPrefs.SetFloat("FBE_BGR",bgColor.r); EditorPrefs.SetFloat("FBE_BGG",bgColor.g); EditorPrefs.SetFloat("FBE_BGB",bgColor.b); }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset", GUILayout.Height(25)))
        { for(int b=0;b<BandNames.Length;b++) bandColors[b]=DefaultColors[b]; bgColor=new Color(0.15f,0.15f,0.15f,1); waveColor=new Color(0.4f,0.7f,1f,1); regen=true;
          if(audioClip!=null) GenerateCombinedWaveform(); }
        if (GUILayout.Button("Regenerate", GUILayout.Height(25)))
        { regen = true; if (audioClip != null) GenerateCombinedWaveform(); }
        EditorGUILayout.EndHorizontal();

        if (regen && audioClip != null && bandsGenerated) GenerateBandTextures();
    }

    public void DrawPlaybackTab()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Playback Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        instantReplay = EditorGUILayout.Toggle("Instant Playback", instantReplay);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Volume", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        float newVol = EditorGUILayout.Slider(playbackVolume, 0f, 1f);
        GUILayout.Label($"{Mathf.RoundToInt(newVol * 100)}%", EditorStyles.miniLabel, GUILayout.Width(32));
        EditorGUILayout.EndHorizontal();
        if (Mathf.Abs(newVol - playbackVolume) > 0.001f)
        {
            playbackVolume = newVol;
            EditorPrefs.SetFloat("FBE_Vol", playbackVolume);
            if (_previewSrc != null) _previewSrc.volume = playbackVolume;
        }

        EditorGUILayout.EndVertical();
    }

    void HandleDragAndDrop()
    {
        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                        if (obj is AudioClip c)
                        {
                            audioClip = c;
                            EditorPrefs.SetString("FBE_Clip", AssetDatabase.GetAssetPath(c));

                            combinedGenerated = false; bandsGenerated = false; bandAudioBuilt = false;
                            rawStereoCache = null; rawStereoCacheSamples = 0;
                            monoCache = null;
                            _asyncCombinedRunning = false; _asyncCombinedDone = false;
                            _asyncBandAudioRunning = false; _asyncBandAudioDone = false;
                            _asyncBandTexRunning = false; _asyncBandTexDone = false;
                            _asyncFilteredRunning = false; _asyncFilteredDone = false;
                            _pendingFilteredRebuild = false;

                            GenerateCombinedWaveform();

                            if (splitMode) EnsureSplitData();
                        }
                }
                break;
        }
    }

    void Update()
    {
        if (audioClip == null) { isPlaying = false; return; }
        if (EditorApplication.isPlaying) return;

        if (_asyncCombinedDone)
        {
            var tex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false);
            tex.SetPixels(_asyncCombinedPixels);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            combinedTexture = tex;
            combinedGenerated = true;
            _asyncCombinedPixels = null;
            _asyncCombinedDone = false;
            _asyncCombinedRunning = false;
        }

        if (_asyncBandAudioDone)
        {
            bandAudio = _asyncBandAudioResult;
            monoCache = _asyncMonoResult;
            cachedSamples = _asyncCachedSamples;
            cachedRate = _asyncCachedRate;
            bandAudioBuilt = true;
            _asyncBandAudioResult = null;
            _asyncMonoResult = null;
            _asyncBandAudioDone = false;
            _asyncBandAudioRunning = false;
            if (splitMode && !bandsGenerated && !_asyncBandTexRunning) GenerateBandTextures();
            if (_pendingFilteredRebuild)
            {
                _pendingFilteredRebuild = false;
                RebuildFilteredClip();
            }
        }

        if (_asyncBandTexDone)
        {
            int bc = BandNames.Length;
            bandTextures = new Texture2D[bc];
            for (int b = 0; b < bc; b++)
            {
                var tex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false);
                tex.SetPixels(_asyncBandTexPixels[b]);
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply();
                bandTextures[b] = tex;
            }
            bandsGenerated = true;
            _asyncBandTexPixels = null;
            _asyncBandTexDone = false;
            _asyncBandTexRunning = false;
        }

        if (_asyncFilteredDone)
        {
            WriteWav32(FilteredWavPath, _asyncFilteredMixed, _asyncFilteredRate);
            AssetDatabase.ImportAsset(FilteredWavPath, ImportAssetOptions.ForceUpdate);
            filteredClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FilteredWavPath);
            filteredReady = (filteredClip != null);
            _asyncFilteredMixed = null;
            _asyncFilteredDone = false;
            _asyncFilteredRunning = false;
            if (_pendingFilteredRebuild)
            {
                _pendingFilteredRebuild = false;
                RebuildFilteredClip();
            }
        }

        bool hasWaveform = splitMode ? bandsGenerated : combinedGenerated;
        bool anyAsync = _asyncCombinedRunning || _asyncBandAudioRunning || _asyncBandTexRunning || _asyncFilteredRunning;
        if (hasWaveform || anyAsync) Repaint();

        AudioClip clip;
        if (splitMode && filteredReady && filteredClip != null)
            clip = filteredClip;
        else
            clip = audioClip;

        lastLastTime = lastTime; lastTime = currentTime; currentTime = AnimWindowGetTime();

        if (AnimWindowState("playing") && lastTime != currentTime && !isPlaying && currentTime <= audioClip.length)
        {
            isPlaying = true; hasStopped = false;
            int ss = (int)Math.Ceiling(clip.samples * (currentTime / audioClip.length));
            PlayClip(clip); SetClipSamplePosition(clip, ss); return;
        }
        else if (lastTime != currentTime && !AnimWindowState("playing") && currentTime <= audioClip.length && instantReplay)
        {
            if (!isPlaying) { hasStopped = false; PlayClip(clip); isPlaying = true; }
            int ss = (int)Math.Ceiling(clip.samples * (currentTime / audioClip.length));
            SetClipSamplePosition(clip, ss); return;
        }
        else if (!AnimWindowState("playing") && lastLastTime == currentTime || lastTime > currentTime)
        { if (!hasStopped) StopAllClips(); isPlaying = false; hasStopped = true; }
    }

    static System.Type _audioUtilType;
    static System.Type AudioUtilType => _audioUtilType ?? (_audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil"));

    GameObject _previewGO;
    AudioSource _previewSrc;

    AudioSource GetOrCreatePreviewSource()
    {
        if (_previewSrc != null) return _previewSrc;
        _previewGO = new GameObject("FBE_PreviewAudio") { hideFlags = HideFlags.HideAndDontSave };
        _previewSrc = _previewGO.AddComponent<AudioSource>();
        _previewSrc.playOnAwake = false;
        _previewSrc.spatialBlend = 0f;
        _previewSrc.volume = playbackVolume;
        return _previewSrc;
    }

    void DestroyPreviewAudioSource()
    {
        if (_previewSrc != null) { _previewSrc.Stop(); _previewSrc = null; }
        if (_previewGO != null) { DestroyImmediate(_previewGO); _previewGO = null; }
    }

    void PlayClip(AudioClip c)
    {
        try { AudioUtilType.GetMethod("StopAllPreviewClips",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,null); } catch {}
        var src = GetOrCreatePreviewSource();
        src.clip = c;
        src.volume = playbackVolume;
        src.Play();
    }

    void StopAllClips()
    {
        try { AudioUtilType.GetMethod("StopAllPreviewClips",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,null); } catch {}
        if (_previewSrc != null) _previewSrc.Stop();
    }

    void SetClipSamplePosition(AudioClip c, int p)
    {
        var src = GetOrCreatePreviewSource();
        if (src.clip == c || src.clip == null) src.timeSamples = Mathf.Clamp(p, 0, c.samples - 1);
    }

    public void DrawVisualizersTab()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Visualizers", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        string[] vizNames = {"Oscilloscope","Waveform Viewer","Spectrum Analyzer","Vectorscope","Loudness Meter","Stereo Width","Spectrogram","Chromagram","EQ Curve"};
        for (int i = 0; i < vizNames.Length; i++)
        {
            if (GUILayout.Button(vizNames[i], GUILayout.Height(25)))
                OpenVisualizer(i);
        }

        EditorGUILayout.EndVertical();
    }

    public void OpenVisualizer(int i)
    {
        switch(i)
        {
            case 0: { var w=FBE_Oscilloscope.Open(); w.parentEditor=this; } break;
            case 1: { var w=FBE_WaveformViewer.Open(); w.parentEditor=this; } break;
            case 2: { var w=FBE_SpectrumAnalyzer.Open(); w.parentEditor=this; } break;
            case 3: { var w=FBE_Vectorscope.Open(); w.parentEditor=this; } break;
            case 4: { var w=FBE_LoudnessMeter.Open(); w.parentEditor=this; } break;
            case 5: { var w=FBE_StereoWidth.Open(); w.parentEditor=this; } break;
            case 6: { var w=FBE_Spectrogram.Open(); w.parentEditor=this; } break;
            case 7: { var w=FBE_Chromagram.Open(); w.parentEditor=this; } break;
            case 8: { var w=FBE_EQCurve.Open(); w.parentEditor=this; } break;
        }
    }

    public void DrawKeyFramesTab()
    {
        if (audioClip == null)
        {
            EditorGUILayout.HelpBox("Load an audio clip first.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Keyframe Utilities", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        if (GUILayout.Button("Fit Animation Clip to Audio", GUILayout.Height(28)))
            FitAnimationClipToAudio();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Copies first-frame keyframes to the end of the audio clip.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Easy Copy & Paste", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        EditorGUI.BeginChangeCheck();
        easyCopyPaste = EditorGUILayout.Toggle("Enable", easyCopyPaste);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetBool("FBE_EasyCP", easyCopyPaste);

        if (easyCopyPaste)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Copy:", EditorStyles.boldLabel, GUILayout.Width(45));
            string copyLabel = listeningForBind == 1 ? "Press any key..." : FormatKeybind(copyCtrl, copyShift, copyAlt, copyKey);
            GUI.backgroundColor = listeningForBind == 1 ? new Color(0.8f, 0.4f, 0.2f) : new Color(0.25f, 0.25f, 0.25f);
            if (GUILayout.Button(copyLabel, GUILayout.Height(22)))
                listeningForBind = listeningForBind == 1 ? 0 : 1;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Paste:", EditorStyles.boldLabel, GUILayout.Width(45));
            string pasteLabel = listeningForBind == 2 ? "Press any key..." : FormatKeybind(pasteCtrl, pasteShift, pasteAlt, pasteKey);
            GUI.backgroundColor = listeningForBind == 2 ? new Color(0.8f, 0.4f, 0.2f) : new Color(0.25f, 0.25f, 0.25f);
            if (GUILayout.Button(pasteLabel, GUILayout.Height(22)))
                listeningForBind = listeningForBind == 2 ? 0 : 2;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    void EasyCopy()
    {
        AnimationClip clip = GetActiveAnimationClip();
        if (clip == null) return;

        copiedKeys.Clear();

        bool gotSelected = false;
        try
        {
            var stateType = typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowState");
            var states = Resources.FindObjectsOfTypeAll(stateType);
            if (states.Length > 0)
            {
                var state = states[0];

                var selKeysProp = stateType.GetProperty("selectedKeys", BindingFlags.Instance | BindingFlags.Public);
                if (selKeysProp != null)
                {
                    var selKeys = selKeysProp.GetValue(state, null) as System.Collections.IList;
                    if (selKeys != null && selKeys.Count > 0)
                    {
                        var awkType = selKeys[0].GetType();
                        var timeProp = awkType.GetProperty("time", BindingFlags.Instance | BindingFlags.Public);
                        var valueProp = awkType.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
                        var curveProp = awkType.GetProperty("curve", BindingFlags.Instance | BindingFlags.Public);
                        var inTanField = awkType.GetField("m_InTangent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var outTanField = awkType.GetField("m_OutTangent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        System.Type awcType = null;
                        System.Reflection.PropertyInfo bindingProp = null;

                        if (curveProp != null)
                        {
                            awcType = curveProp.PropertyType;
                            bindingProp = awcType.GetProperty("binding", BindingFlags.Instance | BindingFlags.Public);
                        }

                        if (timeProp != null && valueProp != null && bindingProp != null)
                        {
                            float minTime = float.MaxValue;

                            foreach (var sk in selKeys)
                            {
                                float t = (float)timeProp.GetValue(sk, null);
                                if (t < minTime) minTime = t;
                            }

                            foreach (var sk in selKeys)
                            {
                                float t = (float)timeProp.GetValue(sk, null);
                                float v = Convert.ToSingle(valueProp.GetValue(sk, null));
                                var curveObj = curveProp.GetValue(sk, null);
                                var binding = (EditorCurveBinding)bindingProp.GetValue(curveObj, null);

                                float inTan = 0f, outTan = 0f;
                                if (inTanField != null) inTan = Convert.ToSingle(inTanField.GetValue(sk));
                                if (outTanField != null) outTan = Convert.ToSingle(outTanField.GetValue(sk));

                                copiedKeys.Add(new CopiedKey
                                {
                                    binding = binding,
                                    keyframe = new Keyframe(t, v, inTan, outTan),
                                    timeOffset = t - minTime
                                });
                            }

                            gotSelected = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to read selected keys: " + ex.Message);
        }

        if (!gotSelected || copiedKeys.Count == 0)
        {
            copiedKeys.Clear();
            float time = AnimWindowGetTime();

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                for (int k = 0; k < curve.length; k++)
                {
                    if (Mathf.Abs(curve.keys[k].time - time) < 0.001f)
                    {
                        copiedKeys.Add(new CopiedKey
                        {
                            binding = binding,
                            keyframe = curve.keys[k],
                            timeOffset = 0f
                        });
                        break;
                    }
                }
            }
        }
    }

    void EasyPaste()
    {
        if (copiedKeys.Count == 0) return;

        AnimationClip clip = GetActiveAnimationClip();
        if (clip == null) return;

        float pasteTime = AnimWindowGetTime();
        Undo.RecordObject(clip, "Easy Paste Keyframes");

        foreach (var ck in copiedKeys)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, ck.binding);
            if (curve == null) continue;

            float targetTime = pasteTime + ck.timeOffset;

            for (int k = curve.length - 1; k >= 0; k--)
                if (Mathf.Abs(curve.keys[k].time - targetTime) < 0.0001f)
                    curve.RemoveKey(k);

            Keyframe newKey = ck.keyframe;
            newKey.time = targetTime;
            curve.AddKey(newKey);
            AnimationUtility.SetEditorCurve(clip, ck.binding, curve);
        }
    }

    void ProcessEasyCopyPasteKeys()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown) return;
        if (e.keyCode == KeyCode.None) return;

        if (listeningForBind > 0)
        {
            if (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl ||
                e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift ||
                e.keyCode == KeyCode.LeftAlt || e.keyCode == KeyCode.RightAlt ||
                e.keyCode == KeyCode.LeftCommand || e.keyCode == KeyCode.RightCommand)
                return;

            bool ctrl = e.control || e.command;
            bool shift = e.shift;
            bool alt = e.alt;

            if (listeningForBind == 1)
            {
                copyKey = e.keyCode; copyCtrl = ctrl; copyShift = shift; copyAlt = alt;
                EditorPrefs.SetInt("FBE_CopyKey", (int)copyKey);
                EditorPrefs.SetBool("FBE_CopyCtrl", copyCtrl);
                EditorPrefs.SetBool("FBE_CopyShift", copyShift);
                EditorPrefs.SetBool("FBE_CopyAlt", copyAlt);
            }
            else if (listeningForBind == 2)
            {
                pasteKey = e.keyCode; pasteCtrl = ctrl; pasteShift = shift; pasteAlt = alt;
                EditorPrefs.SetInt("FBE_PasteKey", (int)pasteKey);
                EditorPrefs.SetBool("FBE_PasteCtrl", pasteCtrl);
                EditorPrefs.SetBool("FBE_PasteShift", pasteShift);
                EditorPrefs.SetBool("FBE_PasteAlt", pasteAlt);
            }

            listeningForBind = 0;
            e.Use();
            Repaint();
            return;
        }

        if (!easyCopyPaste) return;

        bool c = e.control || e.command, s = e.shift, a = e.alt;

        if (e.keyCode == copyKey && c == copyCtrl && s == copyShift && a == copyAlt)
        {
            EasyCopy(); e.Use(); Repaint();
        }
        else if (e.keyCode == pasteKey && c == pasteCtrl && s == pasteShift && a == pasteAlt)
        {
            EasyPaste(); e.Use(); Repaint();
        }
    }

    static string FormatKeybind(bool ctrl, bool shift, bool alt, KeyCode key)
    {
        string s = "";
        if (ctrl) s += "Ctrl + ";
        if (shift) s += "Shift + ";
        if (alt) s += "Alt + ";
        s += key.ToString();
        return s;
    }

    void FitAnimationClipToAudio()
    {
        if (audioClip == null) return;
        AnimationClip clip = GetActiveAnimationClip();
        if (clip == null)
        {
            EditorUtility.DisplayDialog("No Clip", "No animation clip found.\nSelect one in the Animation window.", "OK");
            return;
        }

        float endTime = audioClip.length;
        Undo.RecordObject(clip, "Fit Animation Clip to Audio");

        var bindings = AnimationUtility.GetCurveBindings(clip);

        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0) continue;
            Keyframe firstKey = curve.keys[0];
            bool hasEndKey = false;
            for (int k = 0; k < curve.length; k++)
                if (Mathf.Abs(curve.keys[k].time - endTime) < 0.001f) { hasEndKey = true; break; }
            if (!hasEndKey)
            {
                Keyframe endKey = new Keyframe(endTime, firstKey.value, firstKey.inTangent, firstKey.outTangent);
                endKey.weightedMode = firstKey.weightedMode;
                curve.AddKey(endKey);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
        }

        var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        foreach (var binding in objBindings)
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys == null || keys.Length == 0) continue;
            var firstKey = keys[0];
            bool hasEndKey = false;
            for (int k = 0; k < keys.Length; k++)
                if (Mathf.Abs(keys[k].time - endTime) < 0.001f) { hasEndKey = true; break; }
            if (!hasEndKey)
            {
                var newKeys = new ObjectReferenceKeyframe[keys.Length + 1];
                System.Array.Copy(keys, newKeys, keys.Length);
                newKeys[keys.Length] = new ObjectReferenceKeyframe { time = endTime, value = firstKey.value };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, newKeys);
            }
        }
    }

    AnimationClip GetActiveAnimationClip()
    {
        try
        {
            var stateType = typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowState");
            var states = Resources.FindObjectsOfTypeAll(stateType);
            if (states.Length > 0)
            {
                var clipProp = stateType.GetProperty("activeAnimationClip", BindingFlags.Instance | BindingFlags.Public);
                if (clipProp != null) return clipProp.GetValue(states[0], null) as AnimationClip;
            }
        }
        catch {}
        if (Selection.activeObject is AnimationClip sel) return sel;
        return null;
    }

    public AudioClip GetCurrentClip() => audioClip;
    public float GetCurrentTime() => currentTime;
    public bool GetIsPlaying() => isPlaying || AnimWindowState("playing");
    public float[] GetMonoCache()
    {
        if (monoCache != null) return monoCache;
        if (audioClip == null) return null;
        int ch = audioClip.channels, total = audioClip.samples;
        float[] raw = new float[total * ch];
        audioClip.GetData(raw, 0);
        monoCache = new float[total];
        for (int s = 0; s < total; s++)
        { float sum = 0; for (int c = 0; c < ch; c++) sum += raw[s * ch + c]; monoCache[s] = sum / ch; }
        cachedSamples = total; cachedRate = audioClip.frequency;
        return monoCache;
    }
    float[] rawStereoCache;
    int rawStereoCacheSamples;
    public float[] GetRawStereo()
    {
        if (audioClip == null) return null;
        int totalFloats = audioClip.samples * audioClip.channels;
        if (rawStereoCache == null || rawStereoCache.Length != totalFloats || rawStereoCacheSamples != audioClip.samples)
        {
            rawStereoCache = new float[totalFloats];
            audioClip.GetData(rawStereoCache, 0);
            rawStereoCacheSamples = audioClip.samples;
        }
        return rawStereoCache;
    }

    bool AnimWindowState(string prop){try{var t=typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowState");var p=t.GetProperty(prop,BindingFlags.Instance|BindingFlags.Public);var i=Resources.FindObjectsOfTypeAll(t);if(i.Length>0)return(bool)p.GetValue(i[0],null);}catch{}return false;}
    float AnimWindowGetTime(){try{var t=typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowState");var p=t.GetProperty("currentTime",BindingFlags.Instance|BindingFlags.Public);var i=Resources.FindObjectsOfTypeAll(t);if(i.Length>0)return(float)p.GetValue(i[0],null);}catch{}return 0f;}
    object GetDSE(){try{var t=typeof(Editor).Assembly.GetType("UnityEditor.AnimEditor");var m=t.GetMethod("get_dopeSheetEditor",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public);var i=Resources.FindObjectsOfTypeAll(t);if(i.Length>0)return m.Invoke(i[0],null);}catch{}return null;}
    Rect GetShownArea(){try{var d=GetDSE();if(d==null)return new Rect();return(Rect)d.GetType().GetProperty("shownArea",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).GetValue(d,null);}catch{return new Rect();}}
    float GetHierarchyWidth(){try{var t=typeof(Editor).Assembly.GetType("UnityEditor.AnimEditor");var m=t.GetMethod("get_hierarchyWidth",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public);var i=Resources.FindObjectsOfTypeAll(t);if(i.Length>0)return(float)m.Invoke(i[0],null);}catch{}return 0f;}
    Vector2 GetTranslation(){try{var d=GetDSE();if(d==null)return Vector2.zero;return(Vector2)d.GetType().GetProperty("translation",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).GetValue(d,null);}catch{return Vector2.zero;}}
    Rect GetRect(){try{var d=GetDSE();if(d==null)return new Rect();return(Rect)d.GetType().GetProperty("rect",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).GetValue(d,null);}catch{return new Rect();}}
}
