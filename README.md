# KeyUtil — Frequency Band Editor

A Unity Editor tool that overlays audio waveforms and frequency band analysis onto the Animation window. Drop in an `AudioClip`, dock the window above or below the Animation timeline, and the waveform aligns to the dope sheet so you can scrub audio while keyframing. Includes per-band mute/solo, a keyframe copy/paste system that works across the Animation window, and nine real-time audio visualizers.

## Installation

Drop all twelve `.cs` files into an `Editor/` folder anywhere inside your Unity project's `Assets/`. The folder name doesn't matter — Unity picks them up because they live under `Editor/`.

```
Assets/
  Editor/
    FrequencyBandEditor.cs
    FBE_SettingsWindow.cs
    FBE_FFTUtil.cs
    FBE_Oscilloscope.cs
    FBE_WaveformViewer.cs
    FBE_SpectrumAnalyzer.cs
    FBE_Vectorscope.cs
    FBE_LoudnessMeter.cs
    FBE_StereoWidth.cs
    FBE_Spectrogram.cs
    FBE_Chromagram.cs
    FBE_EQCurve.cs
```

Requires Unity 2019.4+. Tested in 2022 LTS.

## Usage

1. Open the window via **Temmie → KeyUtil** in the Unity menu bar.
2. Drag an `AudioClip` from the project window onto the drop zone.
3. Dock the window directly above or below the Animation window so the waveform aligns with the dope sheet timeline.
4. Hit play in the Animation window — audio scrubs along with the playhead.
5. Click **Split** to switch from a single combined waveform to six per-band waveforms (Sub-Bass, Bass, Low-Mid, Mid, Upper-Mid, High). Splitting also unlocks per-band mute and solo.
6. Click **Settings** to open the tabbed settings popup.

## How it works

### Async waveform generation

All FFT and texture work happens on `Task.Run` background threads so the editor stays responsive on long clips. The main thread polls `done` flags in `Update()` and applies finished pixel buffers as `Texture2D` objects on the next frame. Combined waveforms come up almost instantly (no FFT — just peak amplitude); split-band waveforms take a few seconds because each band needs a full overlap-add FFT pass.

### Band splitting

Uses overlap-add FFT resynthesis. The audio is windowed (Hann), FFT'd, frequency-masked per band, inverse FFT'd, and summed back together with a Hann-squared envelope correction. The result is six isolated audio streams, one per band, each individually mutable and soloable. All streams are kept in memory.

### Filtered playback

When any band is muted or soloed, audible bands are summed in memory and played through an editor-only `AudioSource`. The mix is rebuilt on a background thread whenever audibility changes, so toggling mute/solo doesn't stutter playback.

### Animation window integration

Reads the Animation window's `shownArea`, `hierarchyWidth`, `translation`, and `currentTime` via reflection on `UnityEditorInternal.AnimationWindowState` and `UnityEditor.AnimEditor`. This is what lets the waveform align *exactly* with the dope sheet timeline regardless of zoom level or scroll offset.

### Global keybinds

The Easy Copy/Paste keybinds hook `EditorApplication.globalEventHandler` via reflection so shortcuts fire even when the Animation window has focus (Unity normally swallows key events when another window owns input). The hook is registered in `OnEnable` and removed in `OnDisable`.

## Settings tabs

| Tab | What it does |
|-----|--------------|
| **Bands** | Per-band visibility, mute, and solo. Mute/solo affect playback only — the filtered audio is rebuilt in the background and swapped in automatically. |
| **Colors** | Color pickers for the combined waveform, each band, and the background. Reset and Regenerate buttons rebuild all textures. |
| **Playback** | Instant Playback toggle (audio plays whenever the playhead moves, even when not actively playing) and a master volume slider applied via `AudioSource.volume` so changes are real-time. |
| **Visualizers** | Buttons that open each visualizer in its own dockable window. Multiple visualizers can be open at once; they all read from the same parent editor. |
| **KeyFrames** | Two utilities: **Fit Animation Clip to Audio** and **Easy Copy & Paste**. See below. |

### Fit Animation Clip to Audio

Adds end-of-clip keyframes for every curve in the active animation clip, copying values from each curve's first keyframe so the animation length matches the audio length. Useful for looping animations that need to end where they started.

### Easy Copy & Paste

Global keybinds for copying and pasting keyframes in the Animation window. Defaults are `Numpad 1` for copy and `Numpad 2` for paste — click a keybind button and press any key combination (with modifiers) to rebind.

Copy grabs whatever is selected in the Animation window via reflection on `AnimationWindowState.selectedKeys`. If nothing is selected, it falls back to grabbing all keyframes at the current playhead time. Paste stamps everything down at the current playhead and **preserves the relative time offsets between the copied keys**, so multi-key selections retain their shape.

## File layout

| File | Responsibility |
|------|----------------|
| `FrequencyBandEditor.cs` | `EditorWindow` — UI, audio loading, waveform generation, band splitting, animation window integration, audio playback, keyframe copy/paste |
| `FBE_SettingsWindow.cs` | Pop-out tabbed settings panel; calls back into the main window to draw each tab body |
| `FBE_FFTUtil.cs` | Shared static helpers used by every visualizer: forward FFT, additive color blending, Bresenham-style line plotter with 1px glow |
| `FBE_Oscilloscope.cs` | XY Lissajous (L on X, R on Y), texture-rendered with glowing connected lines |
| `FBE_WaveformViewer.cs` | Time-domain amplitude graph centered on the playhead, anti-aliased polylines |
| `FBE_SpectrumAnalyzer.cs` | Log-frequency FFT bar display with smoothing and grid markers from 100 Hz to 20 kHz |
| `FBE_Vectorscope.cs` | 45° rotated Mid/Side display with L/R correlation meter; auto-detects mono sources |
| `FBE_LoudnessMeter.cs` | Side-by-side RMS and Peak meters in dB with peak hold and clip-warning colors |
| `FBE_StereoWidth.cs` | Scrolling history graph plus current-value bars for stereo width and L/R correlation |
| `FBE_Spectrogram.cs` | Scrolling FFT heatmap (time × log-frequency × magnitude) with a black-blue-cyan-yellow-white color ramp |
| `FBE_Chromagram.cs` | 12 pitch class bars plus a scrolling note-history heatmap; folds all FFT energy into the 12 semitones |
| `FBE_EQCurve.cs` | FL Studio-style filled frequency response curve with band labels (SUB, BASS, LOW MID, MID, HIGH MID, PRS, TREBLE), C-note markers, and dB grid |

Only `FrequencyBandEditor` and the visualizer windows need to be `public` (Unity requires it for `EditorWindow` subclasses). Everything else stays free of the public API surface.

## Output

Nothing. The tool doesn't write to disk — all visualizer state, waveform textures, band audio, and the mute/solo mixdown live in memory for the lifetime of the window.

## Notes and caveats

- Settings persist across editor sessions via `EditorPrefs`. All keys are prefixed `FBE_` to avoid collisions (`FBE_Clip`, `FBE_C0R`/`G`/`B`, `FBE_Vol`, `FBE_CopyKey`, etc.).
- The main waveform texture is `16384 × 200` to keep detail when zoomed into long clips. Generation is one-time per clip load, so the cost is paid up front.
- Band-split audio generation is heavier — expect a few seconds on a 5-minute clip. The "Processing..." indicator stays up while background tasks run; the editor remains usable the whole time.
- Reflection into `AnimationWindowState` is best-effort. If a future Unity version renames or removes those internal types, the waveform will fall back to filling the whole window (no timeline alignment) but the tool will still function.
- The global keybind hook uses `EditorApplication.globalEventHandler`, which is internal. If that ever gets renamed, copy/paste shortcuts will silently stop firing outside the KeyUtil window — the in-window shortcuts will still work via the normal `Event.current` path.
- Filtered playback uses a custom `AudioSource` rather than `AudioUtil.PlayPreviewClip` so volume changes apply in real time. `AudioUtil.StopAllPreviewClips` is still called as a safety net in case something else started a preview.
- The tool does no asset modification — it never touches the source `AudioClip` import settings.
