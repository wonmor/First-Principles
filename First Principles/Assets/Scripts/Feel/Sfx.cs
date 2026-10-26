using System.Collections.Generic;
using UnityEngine;

// -----------------------------------------------------------------------------
// Sfx — procedurally synthesized sound effects (no audio assets, no licensing).
// -----------------------------------------------------------------------------
// Every clip is rendered once on first use into an AudioClip (44.1 kHz mono) and
// played through a small AudioSource pool owned by FeelDirector. Muted via
// ProgressStore.SfxMuted (toggle in the pause / settings chip).
// -----------------------------------------------------------------------------

public enum SfxId
{
    Jump,
    AirJump,
    Land,
    Death,
    StagePop,
    Graze,
    Star1,
    Star2,
    Star3,
    Complete,
    Click,
    Achievement,
    Hint,
    Locked,
}

public static class Sfx
{
    const int Rate = 44100;
    static readonly Dictionary<SfxId, AudioClip> Cache = new Dictionary<SfxId, AudioClip>();

    public static AudioClip Get(SfxId id)
    {
        if (Cache.TryGetValue(id, out var c) && c != null)
            return c;
        c = Render(id);
        Cache[id] = c;
        return c;
    }

    static AudioClip Render(SfxId id)
    {
        switch (id)
        {
            case SfxId.Jump: return Make("sfx_jump", 0.16f, (t, d) => Sweep(t, d, 330f, 700f) * Env(t, d, 0.005f, 2.6f) * 0.55f);
            case SfxId.AirJump:
                return Make("sfx_airjump", 0.22f, (t, d) =>
                    (Sweep(t, d, 620f, 1240f) * 0.6f + Sweep(t, d, 930f, 1860f) * 0.25f) * Env(t, d, 0.004f, 2.2f) * 0.5f);
            case SfxId.Land:
                return Make("sfx_land", 0.1f, (t, d) =>
                    (Sweep(t, d, 150f, 70f) * 0.8f + Noise(t) * 0.25f * Mathf.Exp(-t * 60f)) * Env(t, d, 0.002f, 4f) * 0.6f);
            case SfxId.Death:
                return Make("sfx_death", 0.42f, (t, d) =>
                {
                    float s = Sweep(t, d, 520f, 95f);
                    float sq = Mathf.Sign(s) * 0.25f + s * 0.55f;
                    return (sq + Noise(t) * 0.12f * Mathf.Exp(-t * 18f)) * Env(t, d, 0.004f, 1.8f) * 0.5f;
                });
            case SfxId.StagePop: return Make("sfx_pop", 0.55f, (t, d) => Bell(t, 880f) * 0.45f + Bell(t, 1320f) * 0.18f);
            case SfxId.Graze:
                return Make("sfx_graze", 0.07f, (t, d) =>
                    (Sin(t, 1560f) * 0.5f + Noise(t) * 0.35f) * Env(t, d, 0.001f, 6f) * 0.35f);
            case SfxId.Star1: return Make("sfx_star1", 0.6f, (t, d) => Bell(t, 1046.5f) * 0.5f + Bell(t, 2093f) * 0.12f);
            case SfxId.Star2: return Make("sfx_star2", 0.6f, (t, d) => Bell(t, 1318.5f) * 0.5f + Bell(t, 2637f) * 0.12f);
            case SfxId.Star3: return Make("sfx_star3", 0.8f, (t, d) => Bell(t, 1568f) * 0.5f + Bell(t, 3136f) * 0.12f + Bell(t, 784f) * 0.2f);
            case SfxId.Complete:
                return Make("sfx_complete", 1.1f, (t, d) =>
                {
                    float v = 0f;
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    for (int i = 0; i < notes.Length; i++)
                    {
                        float st = t - i * 0.09f;
                        if (st >= 0f)
                            v += Bell(st, notes[i]) * 0.3f + Pluck(st, notes[i] * 0.5f) * 0.12f;
                    }
                    return v;
                });
            case SfxId.Click: return Make("sfx_click", 0.04f, (t, d) => (Sin(t, 1900f) * 0.6f + Noise(t) * 0.2f) * Env(t, d, 0.0005f, 8f) * 0.3f);
            case SfxId.Achievement:
                return Make("sfx_ach", 0.9f, (t, d) =>
                {
                    float v = Bell(t, 783.99f) * 0.35f;
                    if (t > 0.12f) v += Bell(t - 0.12f, 1174.66f) * 0.4f;
                    return v;
                });
            case SfxId.Hint:
                return Make("sfx_hint", 0.5f, (t, d) =>
                {
                    float v = Bell(t, 659.25f) * 0.3f;
                    if (t > 0.08f) v += Bell(t - 0.08f, 987.77f) * 0.3f;
                    return v;
                });
            case SfxId.Locked: return Make("sfx_locked", 0.18f, (t, d) => (Sin(t, 220f) * 0.5f + Sin(t, 233f) * 0.5f) * Env(t, d, 0.003f, 3f) * 0.4f);
        }
        return Make("sfx_silence", 0.01f, (t, d) => 0f);
    }

    delegate float SampleFn(float t, float duration);

    static AudioClip Make(string name, float seconds, SampleFn fn)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
        var data = new float[n];
        _noiseSeed = 22222u;
        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float v = fn(t, seconds);
            // Short fade-out tail so nothing clicks.
            float tail = Mathf.Clamp01((seconds - t) / 0.012f);
            data[i] = v * tail;
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        if (peak > 0.95f)
        {
            float k = 0.95f / peak;
            for (int i = 0; i < n; i++) data[i] *= k;
        }
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ primitives

    static float Sin(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

    /// <summary>Exponential frequency sweep (phase-continuous).</summary>
    static float Sweep(float t, float d, float f0, float f1)
    {
        float k = Mathf.Log(f1 / f0) / Mathf.Max(0.001f, d);
        float phase = Mathf.Abs(k) < 1e-5f ? f0 * t : f0 * (Mathf.Exp(k * t) - 1f) / k;
        return Mathf.Sin(2f * Mathf.PI * phase);
    }

    static float Env(float t, float d, float attack, float decayPerDuration)
    {
        float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
        return a * Mathf.Exp(-decayPerDuration * t / Mathf.Max(0.001f, d));
    }

    /// <summary>Soft bell: fundamental + inharmonic partial, exponential decay.</summary>
    static float Bell(float t, float hz)
    {
        if (t < 0f) return 0f;
        float a = Mathf.Clamp01(t / 0.003f);
        return a * (Sin(t, hz) * Mathf.Exp(-t * 5.5f) + Sin(t, hz * 2.76f) * 0.25f * Mathf.Exp(-t * 11f));
    }

    static float Pluck(float t, float hz)
    {
        if (t < 0f) return 0f;
        float s = Sin(t, hz) + 0.5f * Sin(t, hz * 2f) + 0.25f * Sin(t, hz * 3f);
        return s * Mathf.Exp(-t * 7f) * Mathf.Clamp01(t / 0.002f);
    }

    static uint _noiseSeed = 22222u;

    static float Noise(float t)
    {
        _noiseSeed ^= _noiseSeed << 13;
        _noiseSeed ^= _noiseSeed >> 17;
        _noiseSeed ^= _noiseSeed << 5;
        return (_noiseSeed / (float)uint.MaxValue) * 2f - 1f;
    }
}
