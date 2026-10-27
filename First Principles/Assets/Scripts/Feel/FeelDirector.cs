using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// GameFeel + FeelDirector — juice layer (sound, screen shake, hit-stop, glyph bursts)
// -----------------------------------------------------------------------------
// Gameplay code raises GameFeel events; FeelDirector (persistent singleton, created
// on demand) turns them into feedback. Bursts use math glyphs (∂ ∫ Σ π Δ ∞ …) rendered
// with TextMesh Pro — on-theme and no geometric-shape decoration.
// -----------------------------------------------------------------------------

public static class GameFeel
{
    public static event Action<bool> Jumped;            // true = f′ air jump
    public static event Action<float> Landed;           // impact 0..1
    public static event Action<Vector3> Died;           // world position of the avatar
    public static event Action Grazed;
    public static event Action<Color> StagePopped;
    public static event Action<Vector3> Finished;

    public static void RaiseJump(bool air) => Jumped?.Invoke(air);
    public static void RaiseLand(float impact) => Landed?.Invoke(impact);
    public static void RaiseDeath(Vector3 worldPos) => Died?.Invoke(worldPos);
    public static void RaiseGraze() => Grazed?.Invoke();
    public static void RaiseStagePop(Color c) => StagePopped?.Invoke(c);
    public static void RaiseFinish(Vector3 worldPos) => Finished?.Invoke(worldPos);
}

public class FeelDirector : MonoBehaviour
{
    static FeelDirector _instance;

    const int VoiceCount = 6;
    readonly List<AudioSource> _voices = new List<AudioSource>();
    int _nextVoice;

    float _shakeTime;
    float _shakeDuration;
    float _shakeAmp;
    float _hitStopUntil;
    float _savedTimeScale = 1f;

    static readonly string[] BurstGlyphs = { "∂", "∫", "Σ", "π", "Δ", "∞", "+", "√", "dx", "θ" };

    public static FeelDirector Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("FeelDirector");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<FeelDirector>();
            }
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => _ = Instance;

    void Awake()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            _voices.Add(src);
        }
    }

    void OnEnable()
    {
        GameFeel.Jumped += OnJump;
        GameFeel.Landed += OnLand;
        GameFeel.Died += OnDied;
        GameFeel.Grazed += OnGraze;
        GameFeel.StagePopped += OnPop;
        GameFeel.Finished += OnFinished;
    }

    void OnDisable()
    {
        GameFeel.Jumped -= OnJump;
        GameFeel.Landed -= OnLand;
        GameFeel.Died -= OnDied;
        GameFeel.Grazed -= OnGraze;
        GameFeel.StagePopped -= OnPop;
        GameFeel.Finished -= OnFinished;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
            ProgressStore.FlushIfDirty();
    }

    void OnApplicationQuit() => ProgressStore.FlushIfDirty();

    // ------------------------------------------------------------------ audio

    public static void Play(SfxId id, float volume = 1f, float pitch = 1f)
    {
        if (ProgressStore.SfxMuted)
            return;
        var d = Instance;
        var src = d._voices[d._nextVoice];
        d._nextVoice = (d._nextVoice + 1) % d._voices.Count;
        src.pitch = pitch;
        src.PlayOneShot(Sfx.Get(id), Mathf.Clamp01(volume) * 0.8f);
    }

    static float Jitter(float range) => 1f + UnityEngine.Random.Range(-range, range);

    void OnJump(bool air)
    {
        Play(air ? SfxId.AirJump : SfxId.Jump, air ? 0.8f : 0.55f, Jitter(0.05f));
        ProgressStore.AddJump(air);
        if (air)
            Shake(0.12f, 5f);
    }

    void OnLand(float impact)
    {
        if (impact < 0.2f)
            return;
        Play(SfxId.Land, 0.25f + 0.5f * impact, Jitter(0.08f));
        if (impact > 0.65f)
            Shake(0.1f, 4f * impact);
    }

    void OnDied(Vector3 worldPos)
    {
        Play(SfxId.Death, 0.85f, Jitter(0.03f));
        ProgressStore.AddFall();
        Shake(0.32f, 16f);
        HitStop(0.07f);
        Burst(worldPos, Bauhaus.Red, 14, 420f);
        Flash(Bauhaus.WithAlpha(Bauhaus.Red, 0.18f), 0.28f); // flat red flash, no glow
    }

    void OnGraze()
    {
        Play(SfxId.Graze, 0.5f, Jitter(0.1f));
        ProgressStore.AddGraze();
    }

    void OnPop(Color c)
    {
        Play(SfxId.StagePop, 0.7f);
        Shake(0.14f, 6f);
    }

    void OnFinished(Vector3 worldPos)
    {
        Burst(worldPos, Bauhaus.Blue, 18, 520f);
    }

    // ------------------------------------------------------------------ shake / hit-stop / flash

    /// <summary>Current shake offset in UI pixels; LevelManager adds it to the fitted Cartesian plane position.</summary>
    public static Vector2 ShakeOffset { get; private set; }

    /// <summary>Shake the gameplay plane for <paramref name="duration"/> seconds.</summary>
    public void Shake(float duration, float amplitudePx)
    {
        if (_shakeTime <= 0f)
        {
            _shakeDuration = duration;
            _shakeAmp = amplitudePx;
        }
        else
        {
            _shakeDuration = Mathf.Max(_shakeTime, duration);
            _shakeAmp = Mathf.Max(_shakeAmp * (_shakeTime / Mathf.Max(0.001f, _shakeDuration)), amplitudePx);
        }
        _shakeTime = _shakeDuration;
    }

    void Update()
    {
        if (_hitStopUntil > 0f && Time.unscaledTime >= _hitStopUntil)
        {
            _hitStopUntil = 0f;
            Time.timeScale = _savedTimeScale;
        }

        if (_shakeTime <= 0f)
        {
            ShakeOffset = Vector2.zero;
            return;
        }
        _shakeTime -= Time.unscaledDeltaTime;
        if (_shakeTime <= 0f)
        {
            _shakeTime = 0f;
            ShakeOffset = Vector2.zero;
            return;
        }
        float k = _shakeTime / Mathf.Max(0.001f, _shakeDuration);
        float amp = _shakeAmp * k * k;
        float t = Time.unscaledTime * 55f;
        ShakeOffset = new Vector2(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * amp;
    }

    public void HitStop(float seconds)
    {
        if (_hitStopUntil <= 0f)
            _savedTimeScale = Time.timeScale > 0.06f ? Time.timeScale : 1f;
        Time.timeScale = 0.05f;
        _hitStopUntil = Time.unscaledTime + seconds;
    }

    public void Flash(Color c, float seconds)
    {
        var canvas = FindTopCanvas();
        if (canvas == null)
            return;
        var go = new GameObject("FeelFlash");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        StartCoroutine(FadeAndDestroy(img, seconds));
    }

    static IEnumerator FadeAndDestroy(Graphic g, float seconds)
    {
        float t = 0f;
        Color c0 = g.color;
        while (t < seconds && g != null)
        {
            t += Time.unscaledDeltaTime;
            g.color = new Color(c0.r, c0.g, c0.b, c0.a * (1f - t / seconds));
            yield return null;
        }
        if (g != null)
            Destroy(g.gameObject);
    }

    static Canvas FindTopCanvas()
    {
        Canvas best = null;
        foreach (var c in FindObjectsByType<Canvas>())
        {
            if (!c.isRootCanvas || !c.isActiveAndEnabled)
                continue;
            if (best == null || c.sortingOrder > best.sortingOrder)
                best = c;
        }
        return best;
    }

    // ------------------------------------------------------------------ glyph bursts

    /// <summary>Math-glyph burst at a world position (UI space of the top canvas).</summary>
    public void Burst(Vector3 worldPos, Color color, int count, float speed)
    {
        var canvas = FindTopCanvas();
        if (canvas == null)
            return;
        var layer = new GameObject("FeelBurst");
        var lrt = layer.AddComponent<RectTransform>();
        lrt.SetParent(canvas.transform, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        lrt.SetAsLastSibling();

        var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(lrt, screen, cam, out var local);

        var parts = new List<(RectTransform rt, TextMeshProUGUI tmp, Vector2 vel, float spin)>();
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("g");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(lrt, false);
            rt.sizeDelta = new Vector2(80f, 80f);
            rt.anchoredPosition = local;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            UiTypography.ApplyDefaultFontAsset(tmp);
            tmp.text = BurstGlyphs[UnityEngine.Random.Range(0, BurstGlyphs.Length)];
            tmp.fontSize = UnityEngine.Random.Range(26f, 44f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color; // flat primary glyphs (no white wash on paper)
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.fontStyle = FontStyles.Bold;
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float sp = speed * UnityEngine.Random.Range(0.45f, 1.1f);
            parts.Add((rt, tmp, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f + 0.35f) * sp, UnityEngine.Random.Range(-260f, 260f)));
        }
        StartCoroutine(RunBurst(layer, parts));
    }

    static IEnumerator RunBurst(GameObject layer, List<(RectTransform rt, TextMeshProUGUI tmp, Vector2 vel, float spin)> parts)
    {
        const float life = 0.9f;
        float t = 0f;
        var vels = new Vector2[parts.Count];
        for (int i = 0; i < parts.Count; i++) vels[i] = parts[i].vel;
        while (t < life && layer != null)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float a = 1f - Mathf.SmoothStep(0f, 1f, t / life);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.rt == null) continue;
                vels[i] += new Vector2(0f, -900f) * dt;
                vels[i] *= 1f - 1.6f * dt;
                p.rt.anchoredPosition += vels[i] * dt;
                p.rt.localRotation = Quaternion.Euler(0f, 0f, p.spin * t);
                var c = p.tmp.color;
                p.tmp.color = new Color(c.r, c.g, c.b, a);
                float s = 1f + 0.4f * (1f - a);
                p.rt.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }
        if (layer != null)
            Destroy(layer);
    }

    /// <summary>Elastic "pop" scale animation for UI (stars, badges, buttons).</summary>
    public static IEnumerator PopScale(Transform t, float duration = 0.45f, float overshoot = 1.35f)
    {
        if (t == null)
            yield break;
        float e = 0f;
        while (e < duration && t != null)
        {
            e += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(e / duration);
            // Damped spring from 0 → 1 with overshoot.
            float s = 1f - Mathf.Exp(-6f * x) * Mathf.Cos(x * Mathf.PI * 2.4f) * (overshoot - 0.35f) / 1f;
            s = Mathf.Max(0f, s);
            t.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (t != null)
            t.localScale = Vector3.one;
    }
}
