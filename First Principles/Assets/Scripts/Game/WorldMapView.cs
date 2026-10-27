using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// WorldMapView — the level select as a side-scrolling world map.
// -----------------------------------------------------------------------------
// * One themed region per topic (tint + a big math glyph), levels as tiles strung along a
//   path that is literally a sine curve; the path turns gold as levels are cleared.
// * "You are here": the player's own glyph bobs over the next level to play.
// * Stars under every tile, medal glyphs for Flawless / Time-trial wins.
// * Optional challenge content branches off the main path:
//     - a Trial node at the end of each region (needs stars from that region, then a
//       no-fall, no-hint, 90+ point run of the region's last level),
//     - the Grand trial after the final bosses (needs medals),
//     - Flawless and Time-trial runs on every cleared level (from the level sheet).
//   The main path itself is never gated by any of this (only the store unlock gates levels).
// * Returning from a level animates what changed: stars pop in, the path fills, the marker
//   hops to the next tile, newly opened trials announce themselves (sound + haptics on phones).
// -----------------------------------------------------------------------------

public class WorldMapView : MonoBehaviour
{
    sealed class Region
    {
        public LevelSelectCategory Cat;
        public string Key;
        public Color Tint;
        public string Glyph;
        public bool ComingSoon;
        public bool IsBoss;
        public readonly List<Node> Nodes = new List<Node>();
        public Node Trial;
        public RectTransform Band;
        public TextMeshProUGUI WatermarkTmp;
    }

    sealed class Node
    {
        public int Level;
        public ChallengeKind Kind;
        public Region Region;
        public Vector2 Pos;
        public RectTransform Rt;
        public Image Bg;
        public Button Button;
        public Image[] Stars;
        public Image MedalF, MedalT;
        public TextMeshProUGUI Sub;
        public Image Lock;
        public Bauhaus.Tone Tone = Bauhaus.Tone.Paper;
    }

    sealed class Segment
    {
        public Image Img;
        public Node From, To;
    }

    // Layout (canvas units; the canvas is ~1980 x 914 on a landscape phone).
    const float NodeSize = 100f;
    const float TrialSize = 112f;
    const float Spacing = 196f;
    const float RegionPad = 110f;
    const float Amp = 78f;
    const float BaseY = -40f;

    Transform _canvas;
    ScrollRect _scroll;
    RectTransform _content;
    RectTransform _pathLayer, _nodeLayer, _fxLayer;
    readonly List<Region> _regions = new List<Region>();
    readonly List<Node> _mainPath = new List<Node>();
    readonly List<Segment> _segments = new List<Segment>();
    Node _grand;
    Node _current;
    RectTransform _marker;
    Vector2 _markerBase;
    float _markerHopUntil;
    Action<int> _play;
    Action<ChallengeSpec> _playChallenge;
    Action _openPaywall;
    Action _comingSoon;
    float _bandHalfH;

    public RectTransform CurrentNodeRect => _current != null ? _current.Rt : null;
    public RectTransform MarkerRect => _marker;

    public RectTransform FirstTrialRect
    {
        get
        {
            foreach (var r in _regions)
                if (r.Trial != null)
                    return r.Trial.Rt;
            return null;
        }
    }

    /// <summary>A cleared tile with stars (for the "stars and medals" tour step), else the current one.</summary>
    public RectTransform StarsExampleRect
    {
        get
        {
            foreach (var n in _mainPath)
                if (ProgressStore.StarsFor(n.Level) > 0)
                    return n.Rt;
            return CurrentNodeRect;
        }
    }

    public static WorldMapView Create(RectTransform parent, Transform canvas, Action<int> play,
        Action<ChallengeSpec> playChallenge, Action openPaywall, Action comingSoon)
    {
        var go = new GameObject("WorldMap");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var view = go.AddComponent<WorldMapView>();
        view._canvas = canvas;
        view._play = play;
        view._playChallenge = playChallenge;
        view._openPaywall = openPaywall;
        view._comingSoon = comingSoon;
        view.BuildScroll(rt);
        return view;
    }

    void BuildScroll(RectTransform root)
    {
        _scroll = root.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = true;
        _scroll.vertical = false;
        _scroll.movementType = ScrollRect.MovementType.Elastic;
        _scroll.elasticity = 0.12f;
        _scroll.inertia = true;
        _scroll.decelerationRate = 0.12f;
        _scroll.scrollSensitivity = DeviceLayout.LevelSelectScrollSensitivity;

        var viewport = UiKit.Rect("Viewport", root, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vimg = viewport.gameObject.AddComponent<Image>();
        vimg.color = new Color(0f, 0f, 0f, 0f);
        vimg.raycastTarget = true;
        _scroll.viewport = viewport;

        _content = UiKit.Rect("Content", viewport, new Vector2(0f, 0f), new Vector2(0f, 1f));
        _content.pivot = new Vector2(0f, 0.5f);
        _scroll.content = _content;

        _pathLayer = UiKit.Rect("Paths", _content, Vector2.zero, Vector2.one);
        _nodeLayer = UiKit.Rect("Nodes", _content, Vector2.zero, Vector2.one);
        _fxLayer = UiKit.Rect("Fx", _content, Vector2.zero, Vector2.one);
    }

    // ------------------------------------------------------------------ build

    public void Rebuild()
    {
        StopAllCoroutines();
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            var c = _content.GetChild(i);
            if (c != _pathLayer && c != _nodeLayer && c != _fxLayer)
                Destroy(c.gameObject);
        }
        foreach (var layer in new[] { _pathLayer, _nodeLayer, _fxLayer })
            for (int i = layer.childCount - 1; i >= 0; i--)
                Destroy(layer.GetChild(i).gameObject);
        _regions.Clear();
        _mainPath.Clear();
        _segments.Clear();
        _grand = null;
        _current = null;
        _marker = null;

        StartCoroutine(BuildWhenSized());
    }

    IEnumerator BuildWhenSized()
    {
        // The viewport height is only known after the first layout pass.
        yield return null;
        float h = ((RectTransform)_scroll.viewport).rect.height;
        if (h < 100f) h = 620f;
        _bandHalfH = h * 0.5f - 6f;
        BuildRegions();
        LayoutAndDraw(h);
        PickCurrent();
        BuildMarker();
        bool celebrate = MapReturn.Pending;
        Node focus = celebrate ? FindMain(MapReturn.LevelIndex) ?? _current : _current;
        ScrollTo(focus, true);
        if (celebrate)
            StartCoroutine(Celebrate());
        Built?.Invoke();
    }

    /// <summary>Raised once the map has been laid out (tours wait for it).</summary>
    public event Action Built;

    void BuildRegions()
    {
        var cats = GameLevelCatalog.SelectCategories;
        var ordered = new List<LevelSelectCategory>();
        LevelSelectCategory? soon = null;
        foreach (var c in cats)
        {
            if (GameLevelCatalog.IsComingSoonLevel(c.FirstLevelIndex))
                soon = c;
            else
                ordered.Add(c);
        }
        if (soon.HasValue)
            ordered.Add(soon.Value);

        foreach (var c in ordered)
        {
            string key = c.TitleLocalizationKey;
            int dot = key.LastIndexOf('.');
            if (dot >= 0) key = key.Substring(dot + 1);
            var r = new Region
            {
                Cat = c,
                Key = key,
                ComingSoon = GameLevelCatalog.IsComingSoonLevel(c.FirstLevelIndex),
                IsBoss = key == "final_boss",
            };
            Theme(key, out r.Tint, out r.Glyph);
            for (int i = c.FirstLevelIndex; i <= c.LastLevelIndexInclusive; i++)
                r.Nodes.Add(new Node { Level = i, Kind = ChallengeKind.None, Region = r });
            if (!r.ComingSoon && !r.IsBoss && c.LevelCount >= 2)
                r.Trial = new Node { Level = c.LastLevelIndexInclusive, Kind = ChallengeKind.RegionTrial, Region = r };
            _regions.Add(r);
        }
    }

    // Bauhaus: regions cycle blue / red / yellow along the map (the final bosses are red).
    static void Theme(string key, out Color tint, out string glyph)
    {
        switch (key)
        {
            case "core": tint = Bauhaus.Blue; glyph = "f′"; break;
            case "integration": tint = Bauhaus.Red; glyph = "∫"; break;
            case "engineering": tint = Bauhaus.Yellow; glyph = "Δ"; break;
            case "ap_bc": tint = Bauhaus.Blue; glyph = "∂"; break;
            case "aerospace": tint = Bauhaus.Red; glyph = "α"; break;
            case "astrophysics": tint = Bauhaus.Yellow; glyph = "∞"; break;
            case "economics": tint = Bauhaus.Blue; glyph = "$"; break;
            case "transforms": tint = Bauhaus.Red; glyph = "ω"; break;
            case "spring_physics": tint = Bauhaus.Yellow; glyph = "k"; break;
            case "big_o": tint = Bauhaus.Blue; glyph = "O(n)"; break;
            case "final_boss": tint = Bauhaus.Red; glyph = "Σ"; break;
            default: tint = Bauhaus.Blue; glyph = "π"; break;
        }
    }

    void LayoutAndDraw(float h)
    {
        float x = 70f;
        int g = 0;
        float trialYMax = _bandHalfH - TrialSize * 0.5f - 96f;
        float trialYMin = -(_bandHalfH - TrialSize * 0.5f - 64f);
        foreach (var r in _regions)
        {
            float start = x;
            x += RegionPad;
            foreach (var n in r.Nodes)
            {
                n.Pos = new Vector2(x, Amp * Mathf.Sin(g * 0.9f + 0.6f) + BaseY);
                _mainPath.Add(n);
                x += Spacing;
                g++;
            }
            if (r.Trial != null)
            {
                var last = r.Nodes[r.Nodes.Count - 1];
                float ty = last.Pos.y > BaseY ? last.Pos.y - 210f : last.Pos.y + 210f;
                ty = Mathf.Clamp(ty, trialYMin, trialYMax - 40f);
                r.Trial.Pos = new Vector2(x - Spacing * 0.35f, ty);
                x += Spacing * 0.55f;
            }
            if (r.IsBoss)
            {
                // Grand trial: bonus node after the final bosses (medal-gated).
                var last = r.Nodes[r.Nodes.Count - 1];
                _grand = new Node { Level = 47, Kind = ChallengeKind.GrandTrial, Region = r };
                float gy = last.Pos.y > BaseY ? last.Pos.y - 210f : last.Pos.y + 210f;
                _grand.Pos = new Vector2(x - Spacing * 0.35f, Mathf.Clamp(gy, trialYMin, trialYMax - 40f));
                x += Spacing * 0.55f;
            }
            x += RegionPad * 0.3f;
            DrawBand(r, start, x);
            x += 26f;
        }
        _content.sizeDelta = new Vector2(x + 70f, 0f);

        // Main path (drawn first so tiles sit on top), then branches to trials.
        for (int i = 0; i + 1 < _mainPath.Count; i++)
            AddSegment(_mainPath[i], _mainPath[i + 1], false);
        foreach (var r in _regions)
            if (r.Trial != null)
                AddSegment(r.Nodes[r.Nodes.Count - 1], r.Trial, true);
        if (_grand != null)
        {
            var boss = _regions.Find(rr => rr.IsBoss);
            AddSegment(boss.Nodes[boss.Nodes.Count - 1], _grand, true);
        }

        foreach (var n in _mainPath)
            DrawNode(n);
        foreach (var r in _regions)
            if (r.Trial != null)
                DrawNode(r.Trial);
        if (_grand != null)
            DrawNode(_grand);
    }

    void DrawBand(Region r, float x0, float x1)
    {
        var band = UiKit.Rect("Region_" + r.Key, _content, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        band.SetSiblingIndex(0);
        band.pivot = new Vector2(0f, 0.5f);
        band.anchoredPosition = new Vector2(x0, 0f);
        band.sizeDelta = new Vector2(x1 - x0, _bandHalfH * 2f);
        // Bauhaus region: a card with a black rule and a solid colour strip along the top.
        var img = band.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, Bauhaus.Card, Bauhaus.PanelRule);
        img.raycastTarget = false;
        r.Band = band;
        UiKit.Band(band, r.Tint, 16f);

        // Region symbol set large as a static typographic mark (poster style).
        r.WatermarkTmp = UiKit.Text(band, "Watermark", new Vector2(0f, 0f), new Vector2(1f, 1f), r.Glyph,
            r.Glyph.Length > 2 ? 230f : 380f, Bauhaus.WithAlpha(r.Tint, 0.12f), FontStyles.Bold);
        r.WatermarkTmp.enableAutoSizing = false;

        // Header: glyph + region name + progress.
        var title = LocalizationManager.Get(r.Cat.TitleLocalizationKey, r.Cat.DefaultTitle);
        string progress;
        if (r.ComingSoon)
            progress = LocalizationManager.Get("map.coming_soon", "Coming soon");
        else
        {
            int stars = ProgressStore.StarsInRange(r.Cat.FirstLevelIndex, r.Cat.LastLevelIndexInclusive);
            progress = string.Format(LocalizationManager.Get("map.region_stars", "{0}/{1} stars"), stars, r.Cat.LevelCount * ProgressRules.MaxStars);
        }
        var head = UiKit.Text(band, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f),
            $"{r.Glyph}  {title}   <size=80%><color=#57534A>{progress}</color></size>",
            UiTypography.Scale(26), Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        head.rectTransform.pivot = new Vector2(0.5f, 1f);
        head.rectTransform.sizeDelta = new Vector2(-48f, 64f);
        head.rectTransform.anchoredPosition = new Vector2(0f, -22f);
        head.textWrappingMode = TextWrappingModes.NoWrap;
        head.overflowMode = TextOverflowModes.Ellipsis;
    }

    void AddSegment(Node a, Node b, bool branch)
    {
        if (branch)
        {
            // Dashed branch: short line pieces.
            Vector2 d = b.Pos - a.Pos;
            float len = d.magnitude;
            int dashes = Mathf.Max(3, Mathf.RoundToInt(len / 34f));
            for (int i = 0; i < dashes; i += 2)
            {
                Vector2 p0 = a.Pos + d * (i / (float)dashes);
                Vector2 p1 = a.Pos + d * ((i + 1) / (float)dashes);
                var seg = Line(p0, p1, 6f, Bauhaus.InkSoft);
                _segments.Add(new Segment { Img = seg, From = a, To = b });
            }
            return;
        }
        bool done = ProgressStore.StarsFor(a.Level) > 0 && ProgressStore.StarsFor(b.Level) > 0;
        var img = Line(a.Pos, b.Pos, 10f, done ? PathGold : PathIdle);
        _segments.Add(new Segment { Img = img, From = a, To = b });
    }

    // Cleared path: solid ink. Ahead: light rule.
    static readonly Color PathGold = Bauhaus.Ink;
    static readonly Color PathIdle = Bauhaus.RuleStrong;

    Image Line(Vector2 a, Vector2 b, float width, Color c)
    {
        var rt = UiKit.Rect("Path", _pathLayer, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = a;
        Vector2 d = b - a;
        rt.sizeDelta = new Vector2(d.magnitude, width);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        var img = rt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    void DrawNode(Node n)
    {
        bool trial = n.Kind == ChallengeKind.RegionTrial || n.Kind == ChallengeKind.GrandTrial;
        float size = trial ? TrialSize : NodeSize;
        var rt = UiKit.Rect("Node_" + n.Level + "_" + n.Kind, _nodeLayer, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = n.Pos;
        rt.sizeDelta = new Vector2(size, size);
        n.Rt = rt;
        var img = rt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        n.Bg = img;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        n.Button = btn;
        btn.onClick.AddListener(() => OnNodeTapped(n));

        StyleNode(n);

        // Caption under the tile: level name (or the trial requirement).
        var sub = UiKit.Text(rt, "Caption", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), "",
            UiTypography.Scale(17), Bauhaus.Ink, FontStyles.Bold);
        sub.rectTransform.pivot = new Vector2(0.5f, 1f);
        sub.rectTransform.sizeDelta = new Vector2(Spacing - 14f, 46f);
        sub.rectTransform.anchoredPosition = new Vector2(0f, trial ? -8f : -40f);
        sub.overflowMode = TextOverflowModes.Ellipsis;
        sub.fontSizeMin = UiTypography.Scale(14);
        n.Sub = sub;
        RefreshCaption(n);

        if (!trial)
        {
            var row = UiKit.Rect("StarsRow", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(110f, 30f);
            row.anchoredPosition = new Vector2(0f, -6f);
            n.Stars = UiKit.Stars(row, Vector2.zero, Vector2.one, ProgressStore.StarsFor(n.Level), 28f);
            if (ProgressStore.StarsFor(n.Level) == 0)
                row.gameObject.SetActive(!GameLevelCatalog.IsComingSoonLevel(n.Level) && Entitlements.IsLevelUnlocked(n.Level));

            // Medals (top corners) once earned.
            n.MedalF = StarGlyph.Add(rt, "MedalFlawless", StarGlyph.Medal, Bauhaus.OnFill(n.Tone), new Vector2(34f, 34f));
            n.MedalF.rectTransform.anchorMin = n.MedalF.rectTransform.anchorMax = new Vector2(0f, 1f);
            n.MedalF.rectTransform.anchoredPosition = new Vector2(4f, -2f);
            n.MedalT = StarGlyph.Add(rt, "MedalTime", StarGlyph.Stopwatch, Bauhaus.OnFill(n.Tone), new Vector2(32f, 32f));
            n.MedalT.rectTransform.anchorMin = n.MedalT.rectTransform.anchorMax = new Vector2(1f, 1f);
            n.MedalT.rectTransform.anchoredPosition = new Vector2(-4f, -2f);
            RefreshMedals(n);
        }
    }

    void RefreshMedals(Node n)
    {
        if (n.MedalF == null)
            return;
        n.MedalF.gameObject.SetActive(ProgressStore.HasMedal(ChallengeRules.MedalId(ChallengeKind.Flawless, n.Level, null)));
        n.MedalT.gameObject.SetActive(ProgressStore.HasMedal(ChallengeRules.MedalId(ChallengeKind.TimeTrial, n.Level, null)));
    }

    void StyleNode(Node n)
    {
        for (int i = n.Rt.childCount - 1; i >= 0; i--)
        {
            var c = n.Rt.GetChild(i);
            if (c.name == "Face")
                Destroy(c.gameObject);
        }
        var face = UiKit.Rect("Face", n.Rt, Vector2.zero, Vector2.one);
        face.SetAsFirstSibling();
        Color bg;
        bool trial = n.Kind == ChallengeKind.RegionTrial || n.Kind == ChallengeKind.GrandTrial;
        if (trial)
        {
            bool open = TrialOpen(n, out _, out _);
            bool won = ProgressStore.HasMedal(ChallengeRules.MedalId(n.Kind, n.Level, n.Region.Key));
            // Won: yellow block. Open: ink block. Gated: paper with a lock.
            bg = won ? Bauhaus.Yellow : open ? Bauhaus.Ink : Bauhaus.Paper;
            var icon = StarGlyph.Add(face, "Glyph", won ? StarGlyph.Medal : StarGlyph.Trophy,
                Bauhaus.OnFill(Bauhaus.ToneFor(bg)), new Vector2(58f, 58f));
            icon.rectTransform.anchoredPosition = new Vector2(0f, open || won ? 0f : 8f);
            if (!open && !won)
            {
                var lk = StarGlyph.Add(face, "Lock", LockGlyph.Locked, Bauhaus.InkSoft, new Vector2(30f, 30f));
                lk.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            }
        }
        else if (GameLevelCatalog.IsComingSoonLevel(n.Level))
        {
            bg = Bauhaus.Paper;
            UiKit.Text(face, "Soon", Vector2.zero, Vector2.one, LocalizationManager.Get("map.soon", "Soon"),
                UiTypography.Scale(22), UiKit.Muted, FontStyles.Bold);
        }
        else if (!Entitlements.IsLevelUnlocked(n.Level))
        {
            bg = Bauhaus.Paper;
            var lk = StarGlyph.Add(face, "Lock", LockGlyph.Locked, Bauhaus.InkSoft, new Vector2(44f, 44f));
            lk.rectTransform.anchoredPosition = Vector2.zero;
        }
        else
        {
            int stars = ProgressStore.StarsFor(n.Level);
            // Cleared: solid region colour. Not yet cleared: card with the black rule.
            bg = stars > 0 ? n.Region.Tint : Bauhaus.Card;
            Color on = Bauhaus.OnFill(Bauhaus.ToneFor(bg));
            if (n.Region.IsBoss)
            {
                var fl = StarGlyph.Add(face, "Boss", StarGlyph.Flame, on, new Vector2(54f, 54f));
                fl.rectTransform.anchoredPosition = Vector2.zero;
            }
            else
            {
                int num = n.Level - n.Region.Cat.FirstLevelIndex + 1;
                UiKit.Text(face, "Num", Vector2.zero, Vector2.one, num.ToString(),
                    UiTypography.Scale(40), on, FontStyles.Bold);
            }
        }
        n.Tone = Bauhaus.ToneFor(bg);
        Bauhaus.Button(n.Button, n.Tone, Bauhaus.PanelRule, face);
        if (n.MedalF != null) n.MedalF.color = Bauhaus.OnFill(n.Tone);
        if (n.MedalT != null) n.MedalT.color = Bauhaus.OnFill(n.Tone);
    }

    void RefreshCaption(Node n)
    {
        if (n.Sub == null)
            return;
        string text;
        if (n.Kind == ChallengeKind.RegionTrial || n.Kind == ChallengeKind.GrandTrial)
        {
            bool open = TrialOpen(n, out int have, out int need);
            string name = ChallengeSession.Title(n.Kind);
            bool won = ProgressStore.HasMedal(ChallengeRules.MedalId(n.Kind, n.Level, n.Region.Key));
            string req = won ? LocalizationManager.Get("map.trial_won", "Medal won")
                : n.Kind == ChallengeKind.GrandTrial
                    ? string.Format(LocalizationManager.Get("map.need_medals", "Medals {0}/{1}"), Mathf.Min(have, need), need)
                    : string.Format(LocalizationManager.Get("map.need_stars", "Stars {0}/{1}"), Mathf.Min(have, need), need);
            text = $"{name}\n<size=86%><color=#{(open || won ? "D7261E" : "57534A")}>{req}</color></size>";
            n.Sub.rectTransform.sizeDelta = new Vector2(Spacing, 56f);
        }
        else
            text = ShortName(GameLevelCatalog.GetLocalizedDisplayName(n.Level));
        n.Sub.text = text;
        LocalizationManager.ApplyTextDirection(n.Sub);
    }

    /// <summary>Tile captions: drop the "BOSS:" tag and any " — subtitle" / long "(...)" tail.</summary>
    static string ShortName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (name.StartsWith("BOSS: "))
            name = name.Substring(6);
        int dash = name.IndexOf(" — ", StringComparison.Ordinal);
        if (dash > 0)
            name = name.Substring(0, dash);
        if (name.Length > 30)
        {
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 8)
                name = name.Substring(0, paren);
        }
        return name;
    }

    bool TrialOpen(Node n, out int have, out int need)
    {
        if (n.Kind == ChallengeKind.GrandTrial)
        {
            have = ProgressStore.MedalCount;
            need = ChallengeRules.GrandTrialMedalsNeeded;
            return have >= need && Entitlements.IsLevelUnlocked(n.Level);
        }
        var c = n.Region.Cat;
        have = ProgressStore.StarsInRange(c.FirstLevelIndex, c.LastLevelIndexInclusive);
        need = ChallengeRules.TrialStarsNeeded(c.LevelCount);
        return have >= need && Entitlements.IsLevelUnlocked(n.Level);
    }

    Node FindMain(int level)
    {
        foreach (var n in _mainPath)
            if (n.Level == level)
                return n;
        return null;
    }

    void PickCurrent()
    {
        _current = null;
        foreach (var n in _mainPath)
        {
            if (GameLevelCatalog.IsComingSoonLevel(n.Level))
                continue;
            if (ProgressStore.StarsFor(n.Level) == 0)
            {
                _current = n;
                break;
            }
        }
        if (_current == null)
        {
            foreach (var n in _mainPath)
                if (!GameLevelCatalog.IsComingSoonLevel(n.Level) && ProgressStore.StarsFor(n.Level) < ProgressRules.MaxStars)
                {
                    _current = n;
                    break;
                }
        }
        if (_current == null && _mainPath.Count > 0)
            _current = _mainPath[0];
    }

    void BuildMarker()
    {
        if (_current == null)
            return;
        // Player's avatar glyph on a chip, a map pin under it pointing at the tile, label beside.
        var rt = UiKit.Rect("YouAreHere", _fxLayer, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(64f, 100f);
        _marker = rt;

        int gi = PlayerGlyphSettings.GetSelectedIndex();
        var pin = StarGlyph.Add(rt, "Pin", StarGlyph.Pin, Bauhaus.Ink, new Vector2(56f, 56f));
        pin.rectTransform.anchorMin = pin.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        pin.rectTransform.pivot = new Vector2(0.5f, 0f);
        pin.rectTransform.anchoredPosition = Vector2.zero;
        var chip = UiKit.Rect("Avatar", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        chip.pivot = new Vector2(0.5f, 1f);
        chip.sizeDelta = new Vector2(52f, 52f);
        chip.anchoredPosition = Vector2.zero;
        // Avatar: the player's glyph on a solid ink block.
        var cimg = chip.gameObject.AddComponent<Image>();
        Bauhaus.Panel(cimg, Bauhaus.Ink, 0f);
        cimg.raycastTarget = false;
        string glyph = gi >= 0 && gi < PlayerGlyphSettings.Glyphs.Length ? PlayerGlyphSettings.Glyphs[gi] : "∑";
        UiKit.Text(chip, "Glyph", Vector2.zero, Vector2.one, glyph, UiTypography.Scale(34), Bauhaus.Card, FontStyles.Bold);
        var label = UiKit.Text(rt, "Label", new Vector2(1f, 1f), new Vector2(1f, 1f),
            LocalizationManager.Get("map.you_are_here", "You are here"), UiTypography.Scale(18), Bauhaus.Red, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        label.rectTransform.pivot = new Vector2(0f, 1f);
        label.rectTransform.sizeDelta = new Vector2(170f, 52f);
        label.rectTransform.anchoredPosition = new Vector2(6f, 0f);
        PlaceMarkerAt(_current);
    }

    void PlaceMarkerAt(Node n)
    {
        if (_marker == null || n == null)
            return;
        _markerBase = n.Pos + new Vector2(0f, NodeSize * 0.5f + 2f);
        _marker.anchoredPosition = _markerBase;
    }

    // ------------------------------------------------------------------ idle animation

    void Update()
    {
        float t = Time.unscaledTime;
        if (_marker != null && Time.unscaledTime > _markerHopUntil)
            _marker.anchoredPosition = _markerBase + new Vector2(0f, 7f + 7f * Mathf.Sin(t * 3.2f));
        if (_current != null && _current.Rt != null)
            _current.Rt.localScale = Vector3.one * (1f + 0.045f * Mathf.Sin(t * 4f));
        foreach (var r in _regions)
        {
            if (r.Trial == null || r.Trial.Rt == null) continue;
            if (TrialOpen(r.Trial, out _, out _) && !ProgressStore.HasMedal(ChallengeRules.MedalId(ChallengeKind.RegionTrial, r.Trial.Level, r.Key)))
                r.Trial.Rt.localRotation = Quaternion.Euler(0f, 0f, 3f * Mathf.Sin(t * 2.6f + r.Trial.Level));
        }
        KeepSelectionVisible();
    }

    /// <summary>Controller / keyboard navigation: scroll so the focused tile stays on screen.</summary>
    void KeepSelectionVisible()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null || !sel.transform.IsChildOf(_nodeLayer))
            return;
        var rt = sel.transform as RectTransform;
        float viewW = ((RectTransform)_scroll.viewport).rect.width;
        float contentW = _content.rect.width;
        if (contentW <= viewW)
            return;
        float x = rt.anchoredPosition.x;
        float left = -_content.anchoredPosition.x;
        float margin = 260f;
        float targetLeft = left;
        if (x < left + margin) targetLeft = x - margin;
        else if (x > left + viewW - margin) targetLeft = x - viewW + margin;
        targetLeft = Mathf.Clamp(targetLeft, 0f, contentW - viewW);
        if (Mathf.Abs(targetLeft - left) > 1f)
            _content.anchoredPosition = new Vector2(Mathf.Lerp(-left, -targetLeft, 0.25f), _content.anchoredPosition.y);
    }

    /// <summary>Scrolls so a level's tile (or, with trial=true, its region's trial) is in view.</summary>
    public void ScrollToLevel(int level, bool trial = false)
    {
        if (trial)
        {
            foreach (var r in _regions)
                if (r.Trial != null && r.Trial.Level == level) { ScrollTo(r.Trial, true); return; }
        }
        ScrollTo(FindMain(level), true);
    }

    public bool IsTrialOpenForRegionEnding(int level, out string regionKey, out string requirement)
    {
        regionKey = null;
        requirement = null;
        foreach (var r in _regions)
            if (r.Trial != null && r.Trial.Level == level)
            {
                regionKey = r.Key;
                requirement = TrialRequirementText(r.Trial);
                return TrialOpen(r.Trial, out _, out _);
            }
        return false;
    }

    public void ScrollTo(RectTransform nodeRt, bool instant)
    {
        foreach (var n in _mainPath)
            if (n.Rt == nodeRt) { ScrollTo(n, instant); return; }
        foreach (var r in _regions)
            if (r.Trial != null && r.Trial.Rt == nodeRt) { ScrollTo(r.Trial, instant); return; }
    }

    void ScrollTo(Node n, bool instant)
    {
        if (n == null)
            return;
        float viewW = ((RectTransform)_scroll.viewport).rect.width;
        float contentW = _content.sizeDelta.x;
        float left = Mathf.Clamp(n.Pos.x - viewW * 0.42f, 0f, Mathf.Max(0f, contentW - viewW));
        _scroll.StopMovement();
        if (instant)
            _content.anchoredPosition = new Vector2(-left, _content.anchoredPosition.y);
        else
            StartCoroutine(ScrollAnim(-left));
    }

    IEnumerator ScrollAnim(float targetX)
    {
        float from = _content.anchoredPosition.x;
        for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.45f);
            _content.anchoredPosition = new Vector2(Mathf.Lerp(from, targetX, k), _content.anchoredPosition.y);
            yield return null;
        }
        _content.anchoredPosition = new Vector2(targetX, _content.anchoredPosition.y);
    }

    // ------------------------------------------------------------------ return-from-level celebration

    IEnumerator Celebrate()
    {
        int level = MapReturn.LevelIndex;
        int prev = MapReturn.PreviousStars;
        int now = MapReturn.Stars;
        bool first = MapReturn.FirstClear;
        string medal = MapReturn.NewMedal;
        int prevMedals = MapReturn.PreviousMedalCount;
        MapReturn.Clear();

        var node = FindMain(level);
        if (node == null)
            yield break;

        // Show the old state first, then animate to the new one.
        if (node.Stars != null)
        {
            node.Stars[0].transform.parent.gameObject.SetActive(true);
            for (int i = 0; i < node.Stars.Length; i++)
            {
                StarGlyph.SetStar(node.Stars[i], i < prev);
            }
        }
        Node oldCurrent = first ? node : _current;
        if (first && _current != node)
            PlaceMarkerAt(node);
        var outgoing = new List<Segment>();
        if (first)
            foreach (var s in _segments)
                if ((s.From == node || s.To == node) && s.Img.color == PathGold)
                {
                    s.Img.color = PathIdle;
                    outgoing.Add(s);
                }

        yield return new WaitForSecondsRealtime(0.45f);

        for (int i = prev; i < now && node.Stars != null && i < node.Stars.Length; i++)
        {
            StarGlyph.SetStar(node.Stars[i], true);
            FeelDirector.Play(i == 0 ? SfxId.Star1 : i == 1 ? SfxId.Star2 : SfxId.Star3, 0.8f);
            StartCoroutine(FeelDirector.PopScale(node.Stars[i].transform, 0.45f, 1.5f));
            FeelDirector.Instance.Burst(node.Stars[i].transform.position, UiKit.Gold, 5, 260f);
            yield return new WaitForSecondsRealtime(0.22f);
        }

        if (!string.IsNullOrEmpty(medal))
        {
            RefreshMedals(node);
            foreach (var r in _regions)
                if (r.Trial != null) { StyleNode(r.Trial); RefreshCaption(r.Trial); }
            if (_grand != null) { StyleNode(_grand); RefreshCaption(_grand); }
            FeelDirector.Play(SfxId.Achievement, 0.8f);
            Buzz();
            var target = node.MedalF != null && node.MedalF.gameObject.activeSelf ? node.MedalF.transform
                : node.MedalT != null && node.MedalT.gameObject.activeSelf ? node.MedalT.transform : node.Rt;
            StartCoroutine(FeelDirector.PopScale(target, 0.5f, 1.6f));
            yield return new WaitForSecondsRealtime(0.3f);
        }

        if (first)
        {
            StyleNode(node);
            StartCoroutine(FeelDirector.PopScale(node.Rt, 0.4f, 1.2f));
            FeelDirector.Play(SfxId.StagePop, 0.6f);
            foreach (var s in outgoing)
                StartCoroutine(FillSegment(s.Img));
            yield return new WaitForSecondsRealtime(0.35f);
        }

        // Trials whose star gate just opened (and the Grand trial's medal gate).
        foreach (var r in _regions)
        {
            if (r.Trial == null || r.Trial.Level != r.Cat.LastLevelIndexInclusive) continue;
            if (level < r.Cat.FirstLevelIndex || level > r.Cat.LastLevelIndexInclusive) continue;
            int stars = ProgressStore.StarsInRange(r.Cat.FirstLevelIndex, r.Cat.LastLevelIndexInclusive);
            int need = ChallengeRules.TrialStarsNeeded(r.Cat.LevelCount);
            if (stars >= need && stars - (now - prev) < need)
                yield return AnnounceUnlock(r.Trial, LocalizationManager.Get("map.trial_open", "Trial unlocked!"));
        }
        if (_grand != null && prevMedals < ChallengeRules.GrandTrialMedalsNeeded && ProgressStore.MedalCount >= ChallengeRules.GrandTrialMedalsNeeded)
            yield return AnnounceUnlock(_grand, LocalizationManager.Get("map.grand_open", "Grand trial unlocked!"));

        // Marker hops to the next tile to play.
        var prevCurrent = _current;
        PickCurrent();
        if (_current != null && _current != oldCurrent)
        {
            if (prevCurrent != null && prevCurrent.Rt != null)
                prevCurrent.Rt.localScale = Vector3.one;
            ScrollTo(_current, false);
            yield return Hop(oldCurrent, _current);
            if (first)
                Buzz();
        }
    }

    IEnumerator FillSegment(Image img)
    {
        var rt = img.rectTransform;
        float w = rt.sizeDelta.x;
        img.color = PathGold;
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            rt.sizeDelta = new Vector2(Mathf.Lerp(0f, w, t / 0.4f), rt.sizeDelta.y);
            yield return null;
        }
        rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
    }

    IEnumerator Hop(Node from, Node to)
    {
        if (_marker == null || from == null || to == null)
            yield break;
        Vector2 a = from.Pos + new Vector2(0f, NodeSize * 0.5f + 2f);
        Vector2 b = to.Pos + new Vector2(0f, NodeSize * 0.5f + 2f);
        const float dur = 0.55f;
        _markerHopUntil = Time.unscaledTime + dur + 0.05f;
        FeelDirector.Play(SfxId.Jump, 0.7f);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float k = t / dur;
            _marker.anchoredPosition = Vector2.Lerp(a, b, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 120f);
            yield return null;
        }
        _markerBase = b;
        _marker.anchoredPosition = b;
        FeelDirector.Play(SfxId.Land, 0.7f);
        StartCoroutine(FeelDirector.PopScale(to.Rt, 0.35f, 1.15f));
    }

    IEnumerator AnnounceUnlock(Node n, string text)
    {
        StyleNode(n);
        RefreshCaption(n);
        ScrollTo(n, false);
        yield return new WaitForSecondsRealtime(0.5f);
        FeelDirector.Play(SfxId.Achievement, 0.8f);
        Buzz();
        StartCoroutine(FeelDirector.PopScale(n.Rt, 0.5f, 1.35f));
        FeelDirector.Instance.Burst(n.Rt.position, UiKit.Gold, 12, 360f);
        var toast = UiKit.Text(_fxLayer, "Toast", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), text,
            UiTypography.Scale(26), Bauhaus.Red, FontStyles.Bold);
        toast.alignment = TextAlignmentOptions.MidlineLeft;
        toast.rectTransform.pivot = new Vector2(0f, 0.5f);
        toast.rectTransform.sizeDelta = new Vector2(340f, 44f);
        toast.rectTransform.anchoredPosition = n.Pos + new Vector2(TrialSize * 0.5f + 16f, 0f);
        yield return new WaitForSecondsRealtime(1.6f);
        if (toast != null)
            Destroy(toast.gameObject);
    }

    static void Buzz()
    {
#if UNITY_IOS || UNITY_ANDROID
        if (Application.isMobilePlatform && !ProgressStore.SfxMuted)
            Handheld.Vibrate();
#endif
    }

    // ------------------------------------------------------------------ taps → level sheet

    void OnNodeTapped(Node n)
    {
        FeelDirector.Play(SfxId.Click, 0.6f);
        if (n.Kind == ChallengeKind.None && GameLevelCatalog.IsComingSoonLevel(n.Level))
        {
            _comingSoon?.Invoke();
            return;
        }
        if (!Entitlements.IsLevelUnlocked(n.Level))
        {
            FeelDirector.Play(SfxId.Locked, 0.6f);
            _openPaywall?.Invoke();
            return;
        }
        MapLevelSheet.Open(_canvas, n.Level, n.Kind, n.Region.Key, n.Region.Tint,
            () => TrialOpen(n, out _, out _), TrialRequirementText(n), _play, _playChallenge);
    }

    string TrialRequirementText(Node n)
    {
        if (n.Kind == ChallengeKind.None)
            return null;
        TrialOpen(n, out int have, out int need);
        if (n.Kind == ChallengeKind.GrandTrial)
            return string.Format(LocalizationManager.Get("map.grand_req",
                "Opens with {0} medals (you have {1}). Earn medals with Flawless and Time-trial runs."), need, have);
        string region = LocalizationManager.Get(n.Region.Cat.TitleLocalizationKey, n.Region.Cat.DefaultTitle);
        return string.Format(LocalizationManager.Get("map.trial_req",
            "Opens with {0} stars in {1} (you have {2})."), need, region, have);
    }
}

// -----------------------------------------------------------------------------
// MapLevelSheet — what tapping a tile opens: stars, best run, medals, and the play /
// challenge buttons. Trials show their requirement and only start once it is met.
// -----------------------------------------------------------------------------

public static class MapLevelSheet
{
    const string RootName = "MapLevelSheetRoot";
    static GameObject _root;

    public static bool IsOpen => _root != null;

    public static void Open(Transform canvas, int level, ChallengeKind kind, string regionKey, Color tint,
        Func<bool> trialOpen, string requirement, Action<int> play, Action<ChallengeSpec> playChallenge)
    {
        Close();
        if (canvas == null)
            return;
        bool tablet = DeviceLayout.IsTabletLike();
        bool landscape = Screen.width >= Screen.height;
        var rootRt = UiKit.FullScreenDim(RootName, canvas, 0.72f, Close);
        _root = rootRt.gameObject;
        var panel = UiKit.Panel(rootRt,
            landscape ? new Vector2(0.25f, 0.06f) : new Vector2(0.05f, 0.18f),
            landscape ? new Vector2(0.75f, 0.94f) : new Vector2(0.95f, 0.82f));
        UiKit.Band(panel, tint, 18f);

        bool trial = kind == ChallengeKind.RegionTrial || kind == ChallengeKind.GrandTrial;
        string title = trial
            ? $"{ChallengeSession.Title(kind)}: {GameLevelCatalog.GetLocalizedDisplayName(level)}"
            : GameLevelCatalog.GetLocalizedDisplayName(level);
        UiKit.Text(panel, "Title", new Vector2(0.06f, 0.8f), new Vector2(0.94f, 0.93f), title,
            UiTypography.Scale(tablet ? 36 : 34), RuntimeUiPolish.TitleIvory, FontStyles.Bold);

        Button close = null;
        Selectable focus = null;
        int stars = ProgressStore.StarsFor(level);
        if (!trial)
        {
            UiKit.Stars(panel, new Vector2(0.2f, 0.63f), new Vector2(0.8f, 0.79f), stars, tablet ? 64f : 58f);
            float best = ProgressStore.BestSeconds(level);
            string stat = stars > 0
                ? string.Format(LocalizationManager.Get("sheet.best", "Best: {0} points   |   {1}"),
                    ProgressStore.BestScore(level), LevelCompleteOverlay.FormatTime(best))
                : LocalizationManager.Get("sheet.new", "Not cleared yet. Reach the exit to earn your first star.");
            UiKit.Text(panel, "Stat", new Vector2(0.06f, 0.55f), new Vector2(0.94f, 0.63f), stat,
                UiTypography.Scale(22), UiKit.Muted);

            var playBtn = UiKit.Button(panel, "Play", new Vector2(0.08f, 0.4f), new Vector2(0.92f, 0.53f),
                LocalizationManager.Get("sheet.play", "Play"), Bauhaus.Tone.Red,
                () => { Close(); play?.Invoke(level); }, 32);
            focus = playBtn;

            bool cleared = stars > 0;
            int par = ChallengeRules.ParSeconds(best);
            bool hasF = ProgressStore.HasMedal(ChallengeRules.MedalId(ChallengeKind.Flawless, level, null));
            bool hasT = ProgressStore.HasMedal(ChallengeRules.MedalId(ChallengeKind.TimeTrial, level, null));
            ChallengeButton(panel, new Vector2(0.08f, 0.2f), new Vector2(0.49f, 0.37f), StarGlyph.Medal, hasF,
                LocalizationManager.Get("challenge.flawless", "Flawless run"),
                string.Format(LocalizationManager.Get("challenge.flawless.goal", "No falls, no hints, {0}+ points"), ChallengeRules.FlawlessMinScore),
                cleared, () => { Close(); playChallenge?.Invoke(new ChallengeSpec(ChallengeKind.Flawless, level)); });
            ChallengeButton(panel, new Vector2(0.51f, 0.2f), new Vector2(0.92f, 0.37f), StarGlyph.Stopwatch, hasT,
                LocalizationManager.Get("challenge.time", "Time trial"),
                cleared ? string.Format(LocalizationManager.Get("challenge.time.goal", "Reach the exit in {0}s or less"), par)
                    : LocalizationManager.Get("challenge.time.locked", "Beat 90% of your best time"),
                cleared, () => { Close(); playChallenge?.Invoke(new ChallengeSpec(ChallengeKind.TimeTrial, level, par)); });
            if (!cleared)
                UiKit.Text(panel, "Hint", new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.2f),
                    LocalizationManager.Get("sheet.challenges_locked", "Clear the level once to open its challenges."),
                    UiTypography.Scale(17), UiKit.Muted, FontStyles.Normal);
        }
        else
        {
            bool won = ProgressStore.HasMedal(ChallengeRules.MedalId(kind, level, regionKey));
            bool open = trialOpen != null && trialOpen();
            var icon = StarGlyph.Add(panel, "Glyph", won ? StarGlyph.Medal : StarGlyph.Trophy, won || open ? Bauhaus.Ink : UiKit.GoldDim,
                new Vector2(96f, 96f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.69f);
            int min = kind == ChallengeKind.RegionTrial ? ChallengeRules.TrialMinScore : ChallengeRules.FlawlessMinScore;
            string goal = string.Format(LocalizationManager.Get("sheet.trial_goal",
                "Boss run: clear it with no falls, no hints and {0}+ points to win the medal."), min);
            UiKit.Text(panel, "Goal", new Vector2(0.06f, 0.46f), new Vector2(0.94f, 0.6f), goal,
                UiTypography.Scale(23), RuntimeUiPolish.TitleIvory);
            UiKit.Text(panel, "Req", new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.46f),
                won ? LocalizationManager.Get("sheet.trial_won", "You won this medal. Run it again any time.") : requirement ?? "",
                UiTypography.Scale(20), open || won ? Bauhaus.Blue : Bauhaus.Red, FontStyles.Bold);
            var go = UiKit.Button(panel, "Start", new Vector2(0.08f, 0.2f), new Vector2(0.92f, 0.33f),
                open ? LocalizationManager.Get("sheet.trial_start", "Start trial") : LocalizationManager.Get("sheet.trial_locked", "Locked"),
                open ? Bauhaus.Blue : Bauhaus.Card,
                () =>
                {
                    if (trialOpen == null || !trialOpen())
                    {
                        FeelDirector.Play(SfxId.Locked, 0.7f);
                        return;
                    }
                    Close();
                    playChallenge?.Invoke(new ChallengeSpec(kind, level, 0, regionKey));
                }, 30, Color.white);
            focus = go;
        }

        close = UiKit.Button(panel, "Close", new Vector2(0.3f, 0.04f), new Vector2(0.7f, 0.13f),
            LocalizationManager.Get("sheet.close", "Close"), Bauhaus.Tone.Paper, Close, 24);
        OverlayCancelHandler.Attach(_root, close, focus);
        FeelDirector.Instance.StartCoroutine(FeelDirector.PopScale(panel, 0.3f, 1.06f));
    }

    static void ChallengeButton(RectTransform panel, Vector2 min, Vector2 max, Sprite glyph, bool won, string title,
        string goal, bool enabled, Action onClick)
    {
        var btn = UiKit.Button(panel, title, min, max, "", Bauhaus.Tone.Paper, () =>
        {
            if (!enabled)
            {
                FeelDirector.Play(SfxId.Locked, 0.6f);
                return;
            }
            onClick?.Invoke();
        });
        var rt = (RectTransform)btn.transform;
        var icon = StarGlyph.Add(rt, "Glyph", glyph, won ? Bauhaus.Red : enabled ? Bauhaus.Ink : UiKit.GoldDim, new Vector2(46f, 46f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(14f, 0f);
        string state = won ? "  <color=#D7261E>" + LocalizationManager.Get("sheet.medal_won", "Medal won") + "</color>" : "";
        UiKit.Text(rt, "Label", new Vector2(0.22f, 0.05f), new Vector2(0.97f, 0.95f),
            $"<b>{title}</b>{state}\n<size=78%><color=#1F4FA0>{goal}</color></size>",
            UiTypography.Scale(22), enabled ? Bauhaus.Ink : UiKit.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        // Keep the dimmed / medal colours set above (the block stays a paper button either way).
        Bauhaus.Button(btn, Bauhaus.Tone.Paper, Bauhaus.ButtonRule, null, false);
    }

    public static void Close()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
    }
}
