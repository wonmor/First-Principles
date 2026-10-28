using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// CourseExtras — the "obstacle course" layer added on top of each generated graph world.
// -----------------------------------------------------------------------------
//   * ∂ tokens off the main line (+5 points each, capped at 100, so they repair f′ grazes).
//     The first one per level sits on the natural jump arc; the rest hang higher and need
//     an f′ double jump or a spring: the optional harder route.
//   * Spring pads (Hooke's law, k glyph) on the take-off column before big gaps / step-ups:
//     jumping from one gives ×1.32 launch speed. They only ever help the main path.
//   * Sentries (from the 5th level on): small hazards that patrol a sine path just above
//     some high tokens, so greed has a price. Never placed on the plain jump arc.
//   * A checkpoint flag halfway along longer levels; a fall respawns you there (the fall
//     still counts for stars, the clock keeps running).
//   * Flow combo: tokens, f′ air jumps and spring jumps chain while you stay airborne-ish;
//     the counter pops, the pitch climbs, and it resets after ~1 s on the ground or a fall.
// Placement is deterministic per level, so restarts and the daily challenge are fair.
// -----------------------------------------------------------------------------

public class CourseExtras : MonoBehaviour
{
    sealed class Token
    {
        public Vector2 Pos;
        public RectTransform Rt;
        public TextMeshProUGUI Glyph;
        public bool Taken;
        public float Phase;
    }

    sealed class Spring
    {
        public GridRect Pad;
        public RectTransform Rt;
        public Image Img;
        public float Kick;
    }

    sealed class Sentry
    {
        public Vector2 Center;
        public float Amp;
        public float Speed;
        public float Phase;
        public RectTransform Rt;
        public GraphObstacleUILayout Layout;
        public Vector2 Pos;
    }

    const float TokenSize = CourseLayout.TokenSize;
    const float SentrySize = CourseLayout.SentrySize;
    public const int TokenPoints = 5;
    public const float SpringBoost = CourseLayout.SpringBoost;

    LevelManager _lm;
    PlayerControllerUI2D _player;
    RectTransform _root;
    RectTransform _plane;
    Vector2Int _grid;
    readonly List<Token> _tokens = new List<Token>();
    readonly List<Spring> _springs = new List<Spring>();
    readonly List<Sentry> _sentries = new List<Sentry>();
    bool _hasCheckpoint;
    Vector2 _checkpoint; // x, platform top
    RectTransform _flagRt;
    Image _flagImg;
    bool _checkpointReached;

    int _flow;
    float _groundedFor;
    TextMeshProUGUI _hud;
    RectTransform _hudRt;

    public int TokensTaken { get; private set; }
    public int TokenCount => _tokens.Count;
    public bool CheckpointReached => _checkpointReached;
    public Vector2 CheckpointGrid => _checkpoint;

    public void Bind(LevelManager lm, PlayerControllerUI2D player)
    {
        if (_player != null)
        {
            _player.GroundJumped -= OnGroundJumped;
            _player.AirJumped -= OnAirJumped;
        }
        _lm = lm;
        _player = player;
        if (_player != null)
        {
            _player.GroundJumped += OnGroundJumped;
            _player.AirJumped += OnAirJumped;
        }
    }

    void OnDestroy()
    {
        if (_player != null)
        {
            _player.GroundJumped -= OnGroundJumped;
            _player.AirJumped -= OnAirJumped;
        }
    }

    /// <param name="keepCheckpoint">Restart after a fall: the flag stays raised.</param>
    public void Build(GraphWorld world, RectTransform obstaclesRoot, RectTransform plane, int levelIndex, bool keepCheckpoint)
    {
        bool hadCheckpoint = keepCheckpoint && _checkpointReached;
        Clear();
        if (world == null || obstaclesRoot == null || world.platforms.Count < 3)
            return;
        _plane = plane;
        _grid = world.gridSize;

        var go = new GameObject("CourseExtras");
        _root = go.AddComponent<RectTransform>();
        _root.SetParent(obstaclesRoot, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();

        // Placement is engine-free (CourseLayout) so the EditMode reachability audit checks the same spots.
        var layout = CourseLayout.Compute(world, levelIndex);
        foreach (var sp in layout.Springs)
            AddSpring(sp);
        foreach (var tk in layout.Tokens)
            AddToken(tk.Pos, tk.Order);
        foreach (var se in layout.Sentries)
            AddSentry(se.Center, se.Amp, se.Speed, se.Phase);
        if (layout.HasCheckpoint)
        {
            _hasCheckpoint = true;
            _checkpoint = layout.Checkpoint;
            AddFlag(layout.CheckpointPlatform);
            if (hadCheckpoint)
                RaiseFlag(false);
        }

        _flow = 0;
        TokensTaken = 0;
        EnsureHud();
        RefreshHud();
    }

    void Clear()
    {
        if (_root != null)
            Destroy(_root.gameObject);
        _root = null;
        _tokens.Clear();
        _springs.Clear();
        _sentries.Clear();
        _hasCheckpoint = false;
        _checkpointReached = false;
        _flagRt = null;
    }

    /// <summary>Forget the checkpoint (new attempt or a different level).</summary>
    public void ResetCheckpoint() => _checkpointReached = false;

    RectTransform Place(string name, GridRect r)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_root, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        var layout = go.AddComponent<GraphObstacleUILayout>();
        layout.gridRect = r;
        Units(out float uw, out float uh);
        layout.ApplyPixelLayout(uw, uh);
        return rt;
    }

    void Units(out float uw, out float uh)
    {
        uw = _plane != null && _grid.x > 0 ? _plane.rect.width / _grid.x : 1f;
        uh = _plane != null && _grid.y > 0 ? _plane.rect.height / _grid.y : 1f;
    }

    void AddToken(Vector2 c, int k)
    {
        float h = TokenSize * 0.5f;
        var rt = Place("Token", new GridRect(c.x - h, c.x + h, c.y - h, c.y + h));
        // Bauhaus: a bold red ∂, no glow halo.
        var t = UiKit.Text(rt, "Glyph", Vector2.zero, Vector2.one, "∂", 40f, Bauhaus.Red, FontStyles.Bold);
        t.enableAutoSizing = true;
        t.fontSizeMin = 10f;
        t.fontSizeMax = 80f;
        _tokens.Add(new Token { Pos = c, Rt = rt, Glyph = t, Phase = k * 1.3f });
    }

    void AddSpring(CourseLayout.SpringSpot spot)
    {
        var pad = spot.Pad;
        // The coil sits in the top of the take-off slab and pokes just above it.
        var rt = Place("Spring", spot.Visual);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = StarGlyph.Spring;
        img.color = Bauhaus.Blue;
        img.preserveAspect = false;
        img.raycastTarget = false;
        _springs.Add(new Spring { Pad = pad, Rt = rt, Img = img });
    }

    void AddSentry(Vector2 center, float amp, float speed, float phase)
    {
        float h = SentrySize * 0.5f;
        var rt = Place("Sentry", new GridRect(center.x - h, center.x + h, center.y - h, center.y + h));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = StarGlyph.Flame;
        img.color = Bauhaus.Red;
        img.preserveAspect = true;
        img.raycastTarget = false;
        _sentries.Add(new Sentry { Center = center, Amp = amp, Speed = speed, Phase = phase, Rt = rt, Layout = rt.GetComponent<GraphObstacleUILayout>(), Pos = center });
    }

    void AddFlag(GridRect p)
    {
        float cx = (p.xMin + p.xMax) * 0.5f;
        _flagRt = Place("Checkpoint", new GridRect(cx - 0.45f, cx + 0.55f, p.yMax, p.yMax + 1.5f));
        _flagImg = _flagRt.gameObject.AddComponent<Image>();
        _flagImg.sprite = StarGlyph.Flag;
        _flagImg.color = Bauhaus.RuleStrong;
        _flagImg.preserveAspect = true;
        _flagImg.raycastTarget = false;
    }

    void RaiseFlag(bool celebrate)
    {
        _checkpointReached = true;
        if (_flagImg != null)
            _flagImg.color = Bauhaus.Red;
        if (!celebrate)
            return;
        FeelDirector.Play(SfxId.StagePop, 0.7f, 1.2f);
        if (_flagRt != null)
        {
            StartCoroutine(FeelDirector.PopScale(_flagRt, 0.4f, 1.4f));
            FeelDirector.Instance.Burst(_flagRt.position, Bauhaus.Red, 10, 300f);
        }
        Toast(LocalizationManager.Get("course.checkpoint", "Checkpoint"));
        Buzz();
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (_root == null || _player == null || _player.IsDeadOrFinished)
            return;
        float t = Time.time;
        Vector2 p = _player.PlayerCenterGrid;
        Vector2 half = _player.PlayerSizeGrid * 0.5f;

        // Tokens: bob + pickup.
        foreach (var tk in _tokens)
        {
            if (tk.Taken || tk.Rt == null) continue;
            tk.Glyph.rectTransform.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(t * 4f + tk.Phase));
            if (CourseLayout.TouchesToken(tk.Pos, p, half))
                Collect(tk);
        }

        // Springs: boost while standing on the pad.
        foreach (var s in _springs)
        {
            bool on = CourseLayout.IsOnSpring(new CourseLayout.SpringSpot { Pad = s.Pad }, _player.Grounded, p, half);
            if (on)
                _player.SetGroundJumpBoost(SpringBoost);
            s.Kick = Mathf.MoveTowards(s.Kick, 0f, Time.deltaTime * 4f);
            float squash = on ? 0.82f : 1f + 0.35f * s.Kick;
            s.Rt.localScale = new Vector3(1f, squash, 1f);
            s.Img.color = on ? Color.Lerp(Bauhaus.Blue, Bauhaus.Ink, 0.4f + 0.3f * Mathf.Sin(t * 10f)) : Bauhaus.Blue;
        }

        // Sentries: sine patrol, touch = fall.
        if (_sentries.Count > 0)
        {
            Units(out float uw, out float uh);
            foreach (var se in _sentries)
            {
                se.Pos = se.Center + new Vector2(Mathf.Sin(t * se.Speed + se.Phase) * se.Amp, 0f);
                float h = SentrySize * 0.5f;
                se.Layout.gridRect = new GridRect(se.Pos.x - h, se.Pos.x + h, se.Pos.y - h, se.Pos.y + h);
                se.Layout.ApplyPixelLayout(uw, uh);
                if (Mathf.Abs(p.x - se.Pos.x) < half.x + h * 0.8f && Mathf.Abs(p.y - se.Pos.y) < half.y + h * 0.8f)
                {
                    _player.Kill();
                    return;
                }
            }
        }

        // Checkpoint.
        if (_hasCheckpoint && !_checkpointReached && _player.Grounded && p.x >= _checkpoint.x - CourseLayout.CheckpointTriggerLead)
            RaiseFlag(true);

        // Flow combo decays after a beat on the ground.
        if (_player.Grounded)
        {
            _groundedFor += Time.deltaTime;
            if (_groundedFor > 0.9f && _flow > 0)
            {
                _flow = 0;
                RefreshHud();
            }
        }
        else
            _groundedFor = 0f;
    }

    void Collect(Token tk)
    {
        tk.Taken = true;
        TokensTaken++;
        AddFlow();
        FeelDirector.Play(SfxId.Star1, 0.75f, 1f + 0.08f * Mathf.Min(_flow, 8));
        FeelDirector.Instance.Burst(tk.Rt.position, Bauhaus.Red, 10, 320f);
        if (_lm != null)
            _lm.AddScoreBonus(TokenPoints);
        StartCoroutine(Vanish(tk.Glyph.rectTransform, tk.Rt));
    }

    System.Collections.IEnumerator Vanish(RectTransform glyph, RectTransform root)
    {
        var glow = root != null ? root.GetComponent<Image>() : null;
        if (glow != null) glow.enabled = false;
        for (float t = 0f; t < 0.25f && glyph != null; t += Time.deltaTime)
        {
            glyph.localScale = Vector3.one * Mathf.Lerp(1.6f, 0f, t / 0.25f);
            yield return null;
        }
        if (root != null)
            root.gameObject.SetActive(false);
    }

    void OnGroundJumped(float boost)
    {
        if (boost > 1.01f)
        {
            AddFlow();
            FeelDirector.Play(SfxId.AirJump, 0.8f, 0.8f);
            FeelDirector.Instance.Shake(0.1f, 4f);
            foreach (var s in _springs)
                s.Kick = 1f;
        }
    }

    void OnAirJumped() => AddFlow();

    void AddFlow()
    {
        _flow++;
        _groundedFor = 0f;
        RefreshHud();
        if (_flow >= 2 && _hudRt != null)
            StartCoroutine(FeelDirector.PopScale(_hudRt, 0.3f, 1.2f));
    }

    // ------------------------------------------------------------------ HUD

    void EnsureHud()
    {
        if (_hud != null)
            return;
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;
        var safe = MobileUiRoots.GetSafeContentParent(canvas.transform);
        var parent = safe != null ? (Transform)safe : canvas.transform;
        _hudRt = UiKit.Rect("CourseHud", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        _hudRt.pivot = new Vector2(0.5f, 1f);
        _hudRt.sizeDelta = new Vector2(420f, 52f);
        _hudRt.anchoredPosition = new Vector2(0f, DeviceLayout.PreferOnScreenGameControls ? -14f : -22f);
        _hud = UiKit.Text(_hudRt, "Text", Vector2.zero, Vector2.one, "", UiTypography.Scale(26), Bauhaus.Ink, FontStyles.Bold);
    }

    void RefreshHud()
    {
        if (_hud == null)
            return;
        string tokens = _tokens.Count > 0 ? $"∂ {TokensTaken}/{_tokens.Count}" : "";
        string flow = _flow >= 2 ? $"   <color=#1F4FA0>{LocalizationManager.Get("course.flow", "Flow")} ×{_flow}</color>" : "";
        _hud.text = tokens + flow;
    }

    void Toast(string text)
    {
        if (_hudRt == null)
            return;
        var t = UiKit.Text(_hudRt.parent, "CourseToast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), text,
            UiTypography.Scale(30), Bauhaus.Red, FontStyles.Bold);
        t.rectTransform.pivot = new Vector2(0.5f, 1f);
        t.rectTransform.sizeDelta = new Vector2(500f, 56f);
        t.rectTransform.anchoredPosition = new Vector2(0f, -72f);
        Destroy(t.gameObject, 1.3f);
    }

    static void Buzz()
    {
#if UNITY_IOS || UNITY_ANDROID
        if (Application.isMobilePlatform && !ProgressStore.SfxMuted)
            Handheld.Vibrate();
#endif
    }
}
