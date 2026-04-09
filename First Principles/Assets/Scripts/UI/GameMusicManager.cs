using System.Collections;
using UnityEngine;

// =============================================================================
// GameMusicManager — per-category background music with cross-fade
// =============================================================================
// Singleton that lives across scenes. LevelManager calls PlayForCategory()
// on each level load; the manager cross-fades between tracks.
//
// HOW TO ADD TRACKS:
//   1. Place royalty-free .ogg/.mp3 files in Assets/Resources/Music/
//   2. Name them to match the TrackNames array entries below.
//   3. The manager loads them via Resources.Load<AudioClip> at startup.
//
// Recommended royalty-free sources (Geometry Dash style):
//   - Incompetech (Kevin MacLeod) — CC BY license
//   - Pixabay Music — free for commercial use
//   - FreePD.com — public domain
//   - Bensound.com — free tier with attribution
//   - OpenGameArt.org — CC0 / CC BY tracks
// =============================================================================

/// <summary>Category-keyed music index; order matches <see cref="GameMusicManager.TrackNames"/>.</summary>
public enum MusicCategory
{
    Menu,
    CoreAndSeries,
    Integration,
    Engineering,
    ApCalculusBC,
    Aerospace,
    Economics,
    Transforms,
    BossLevels,
    BigO,
    SpringPhysics,
    Astrophysics,
}

/// <summary>
/// Manages background music per level category with smooth cross-fading.
/// Attach to a persistent GameObject (DontDestroyOnLoad).
/// </summary>
public class GameMusicManager : MonoBehaviour
{
    public static GameMusicManager Instance { get; private set; }

    [Header("Cross-fade")]
    [SerializeField] private float crossFadeDuration = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 0.35f;

    /// <summary>
    /// Resource paths under Resources/Music/ (without extension).
    /// Add your royalty-free tracks here and place the files in that folder.
    /// Index must match <see cref="MusicCategory"/>.
    /// </summary>
    private static readonly string[] TrackNames =
    {
        "Menu_Theme",           // Menu
        "Upbeat_Synth_1",       // CoreAndSeries
        "Chill_Electronic_1",   // Integration
        "Driving_Bass_1",       // Engineering
        "Ambient_Piano_1",      // ApCalculusBC
        "Epic_Orchestral_1",    // Aerospace
        "Jazzy_Lofi_1",         // Economics
        "Glitch_Wave_1",        // Transforms
        "Intense_Boss_1",       // BossLevels
        "Retro_Chiptune_1",     // BigO
        "Bouncy_Synth_1",       // SpringPhysics
        "Space_Ambient_1",      // Astrophysics
    };

    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private bool _aIsActive = true;
    private MusicCategory _currentCategory = (MusicCategory)(-1);
    private AudioClip[] _clips;
    private Coroutine _fadeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _sourceA = gameObject.AddComponent<AudioSource>();
        _sourceB = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_sourceA);
        ConfigureSource(_sourceB);

        LoadClips();
    }

    private static void ConfigureSource(AudioSource src)
    {
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 0f;
    }

    private void LoadClips()
    {
        _clips = new AudioClip[TrackNames.Length];
        for (int i = 0; i < TrackNames.Length; i++)
        {
            _clips[i] = Resources.Load<AudioClip>($"Music/{TrackNames[i]}");
            // null is fine — the slot simply stays silent until you add the file.
        }
    }

    /// <summary>
    /// Maps a level index to a <see cref="MusicCategory"/> using the same grouping
    /// as <see cref="GameLevelCatalog.SelectCategories"/>.
    /// </summary>
    public static MusicCategory CategoryForLevel(int levelIndex)
    {
        if (levelIndex <= 8) return MusicCategory.CoreAndSeries;
        if (levelIndex <= 13) return MusicCategory.Integration;
        if (levelIndex <= 16) return MusicCategory.Engineering;
        if (levelIndex <= 34) return MusicCategory.ApCalculusBC;
        if (levelIndex <= 41) return MusicCategory.Aerospace;
        if (levelIndex <= 43) return MusicCategory.Economics;
        if (levelIndex <= 45) return MusicCategory.Transforms;
        if (levelIndex <= 49) return MusicCategory.BossLevels;
        if (levelIndex == 50) return MusicCategory.SpringPhysics;
        if (levelIndex <= 58) return MusicCategory.BigO;
        return MusicCategory.Astrophysics;
    }

    /// <summary>Start playing the track for <paramref name="category"/> with cross-fade.</summary>
    public void PlayForCategory(MusicCategory category)
    {
        if (category == _currentCategory)
            return;

        _currentCategory = category;
        int idx = (int)category;
        AudioClip clip = idx >= 0 && idx < _clips.Length ? _clips[idx] : null;

        if (clip == null)
        {
            // No track loaded for this category — fade out current.
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeOutActive());
            return;
        }

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CrossFadeTo(clip));
    }

    /// <summary>Play the menu track.</summary>
    public void PlayMenu()
    {
        PlayForCategory(MusicCategory.Menu);
    }

    /// <summary>Play the track matching a level index.</summary>
    public void PlayForLevel(int levelIndex)
    {
        PlayForCategory(CategoryForLevel(levelIndex));
    }

    public void SetVolume(float vol01)
    {
        masterVolume = Mathf.Clamp01(vol01);
        var active = _aIsActive ? _sourceA : _sourceB;
        if (active.isPlaying)
            active.volume = masterVolume;
    }

    private IEnumerator CrossFadeTo(AudioClip newClip)
    {
        var fadeOut = _aIsActive ? _sourceA : _sourceB;
        var fadeIn = _aIsActive ? _sourceB : _sourceA;
        _aIsActive = !_aIsActive;

        fadeIn.clip = newClip;
        fadeIn.volume = 0f;
        fadeIn.Play();

        float t = 0f;
        float dur = Mathf.Max(0.1f, crossFadeDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            fadeIn.volume = Mathf.Lerp(0f, masterVolume, p);
            fadeOut.volume = Mathf.Lerp(masterVolume, 0f, p);
            yield return null;
        }

        fadeIn.volume = masterVolume;
        fadeOut.volume = 0f;
        fadeOut.Stop();
    }

    private IEnumerator FadeOutActive()
    {
        var active = _aIsActive ? _sourceA : _sourceB;
        float startVol = active.volume;
        float t = 0f;
        float dur = Mathf.Max(0.1f, crossFadeDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            active.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(t / dur));
            yield return null;
        }
        active.volume = 0f;
        active.Stop();
    }
}
