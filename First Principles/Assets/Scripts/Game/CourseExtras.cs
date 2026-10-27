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

    const float TokenSize = 0.95f;
    const float SentrySize = 0.72f;
    public const int TokenPoints = 5;
    public const float SpringBoost = 1.32f;

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

        var plats = new List<GridRect>(world.platforms);
        plats.Sort((a, b) => a.xMin.CompareTo(b.xMin));
        float yTopLimit = world.hasPlayBounds ? world.playBounds.YMax - 0.7f : world.gridSize.y - 0.7f;
        float xMinLimit = world.spawnXGrid + 1.5f;
        float xMaxLimit = world.finish.xMin - 0.8f;

        // Gaps (pits) and big steps along the main line.
        var gaps = new List<int>();
        var springAt = new HashSet<int>();
        for (int i = 0; i + 1 < plats.Count; i++)
        {
            var a = plats[i];
            var b = plats[i + 1];
            float gapW = b.xMin - a.xMax;
            float step = b.yMax - a.yMax;
            if (a.xMax < xMinLimit || b.xMin > xMaxLimit)
                continue;
            if (gapW > 0.5f)
                gaps.Add(i);
            bool big = gapW > 3.2f || step > 2.4f;
            if (levelIndex >= 1 && big && springAt.Count < 3 && (springAt.Count == 0 || i - Last(springAt) > 3))
                springAt.Add(i);
        }

        foreach (int i in springAt)
            AddSpring(plats[i]);

        // Tokens: evenly spread over the gaps; first one easy, the rest on the high route.
        int want = levelIndex == 0 ? 1 : Mathf.Clamp(2 + levelIndex / 12, 2, 5);
        want = Mathf.Min(want, gaps.Count);
        var used = new HashSet<int>();
        for (int k = 0; k < want; k++)
        {
            int gi = gaps[Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) * gaps.Count / want - 0.5f), 0, gaps.Count - 1)];
            if (!used.Add(gi))
                continue;
            var a = plats[gi];
            var b = plats[gi + 1];
            float top = Mathf.Max(a.yMax, b.yMax);
            bool high = k > 0;
            float y = top + (high ? 5.2f : 2.4f);
            if (y > yTopLimit)
                y = Mathf.Max(top + 2.2f, yTopLimit);
            var pos = new Vector2((a.xMax + b.xMin) * 0.5f, y);
            AddToken(pos, k);
            // Sentries guard every other high token from the 5th level on (not over spring gaps).
            if (high && levelIndex >= 4 && k % 2 == 1 && !springAt.Contains(gi) && y + 1.3f < yTopLimit + 0.6f)
                AddSentry(new Vector2(pos.x, y + 1.3f), 1.3f, 1.5f + 0.02f * levelIndex, k);
        }

        // Checkpoint halfway on longer levels.
        float span = xMaxLimit - world.spawnXGrid;
        if (span > 22f)
        {
            float targetX = world.spawnXGrid + span * 0.5f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 1; i < plats.Count - 1; i++)
            {
                if (springAt.Contains(i)) continue;
                var p = plats[i];
                float cx = (p.xMin + p.xMax) * 0.5f;
                float d = Mathf.Abs(cx - targetX);
                if (d < bestD && p.yMax + 2f < yTopLimit + 0.7f)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best >= 0)
            {
                var p = plats[best];
                _hasCheckpoint = true;
                _checkpoint = new Vector2((p.xMin + p.xMax) * 0.5f, p.yMax);
                AddFlag(p);
                if (hadCheckpoint)
                    RaiseFlag(false);
            }
        }

        _flow = 0;
        TokensTaken = 0;
        EnsureHud();
        RefreshHud();
    }

    static int Last(HashSet<int> set)
    {
        int m = int.MinValue;
        foreach (var v in set) if (v > m) m = v;
        return m;
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

    void AddSpring(GridRect a)
    {
        float w = Mathf.Min(1f, a.xMax - a.xMin);
        var pad = new GridRect(a.xMax - w, a.xMax, a.yMax, a.yMax);
        // The coil sits in the top of the take-off slab and pokes just above it.
        var rt = Place("Spring", new GridRect(a.xMax - w, a.xMax, a.yMax - 0.42f, a.yMax + 0.14f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = StarGlyph.Spring;
        img.color = Bauhaus.Blue;
        img.preserveAspect = false;
        img.raycastTarget = false;
        _springs.Add(new Spring { Pad = pad, Rt = rt, Img = img });
    }

    void AddSentry(Vector2 center, float amp, float speed, int k)
    {
        float h = SentrySize * 0.5f;
        var rt = Place("Sentry", new GridRect(center.x - h, center.x + h, center.y - h, center.y + h));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = StarGlyph.Flame;
        img.color = Bauhaus.Red;
        img.preserveAspect = true;
        img.raycastTarget = false;
        _sentries.Add(new Sentry { Center = center, Amp = amp, Speed = speed, Phase = k * 0.9f, Rt = rt, Layout = rt.GetComponent<GraphObstacleUILayout>(), Pos = center });
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
            if (Mathf.Abs(p.x - tk.Pos.x) < half.x + TokenSize * 0.4f && Mathf.Abs(p.y - tk.Pos.y) < half.y + TokenSize * 0.4f)
                Collect(tk);
        }

        // Springs: boost while standing on the pad.
        foreach (var s in _springs)
        {
            bool on = _player.Grounded && p.x + half.x * 0.6f > s.Pad.xMin && p.x - half.x * 0.6f < s.Pad.xMax
                      && Mathf.Abs((p.y - half.y) - s.Pad.yMin) < 0.2f;
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
        if (_hasCheckpoint && !_checkpointReached && _player.Grounded && p.x >= _checkpoint.x - 0.2f)
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
