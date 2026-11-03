using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using NV2 = System.Numerics.Vector2;
using NV3 = System.Numerics.Vector3;

// -----------------------------------------------------------------------------
// Space3DStageView: 3D stage select and play.
// -----------------------------------------------------------------------------
// Edit phase: orbit the stage, move the sliders (or type the formula) and watch the ground, rail or
// ramp change; the panel reads off the function at the pads so the maths is visible. Go starts the
// run: third-person camera, walk with WASD / arrows, left stick or the left half of the screen,
// jump with Space / A / the jump button. Water, walls and undefined points end the run; the goal
// pad clears the stage (1 star + 1 per star pickup). Stages open in order; 6+ follow the Full Game.
// -----------------------------------------------------------------------------

public static class Space3DProgress
{
    public static int Stars(int i)
    {
        var a = ProgressStore.Data.stars3d;
        return a != null && i >= 0 && i < a.Length ? a[i] : 0;
    }

    public static bool Cleared(int i) => Stars(i) > 0;

    public static bool IsOpenInOrder(int i) => i <= 0 || Cleared(i - 1) || Cleared(i);

    public static bool IsPurchased(int i) => Stage3DCatalog.IsFree(i) || Entitlements.HasFullGame;

    public static int TotalStars
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Stage3DCatalog.Count; i++)
                n += Stars(i);
            return n;
        }
    }

    public static int ClearedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Stage3DCatalog.Count; i++)
                if (Cleared(i)) n++;
            return n;
        }
    }

    /// <returns>True when this beat the previous best.</returns>
    public static bool Record(int i, int stars)
    {
        var d = ProgressStore.Data;
        if (d.stars3d == null || d.stars3d.Length < Stage3DCatalog.Count)
            Array.Resize(ref d.stars3d, Stage3DCatalog.Count);
        bool better = stars > d.stars3d[i];
        if (better)
            d.stars3d[i] = stars;
        ProgressStore.Save();
        return better;
    }
}

public class Space3DStageView
{
    readonly Space3DScene host;
    readonly Transform world;
    Transform stageRoot;

    // Select screen.
    RectTransform selectRoot, tileGrid;
    TextMeshProUGUI selectSummary;

    // Play screen.
    RectTransform playRoot, panel, hud, card, joystickRoot, jumpRt, sliderBlock, inputBlock;
    TextMeshProUGUI stageTitle, stageConcept, storyText, formulaText, readText, hintText, hudStars, cardTitle, cardBody;
    TMP_InputField freeInput;
    Button goBtn, hintBtn, cardPrimary, cardSecondary;
    RectTransform cardStars;
    readonly List<Slider> sliders = new List<Slider>();
    readonly List<TextMeshProUGUI> sliderValues = new List<TextMeshProUGUI>();
    Image joyKnob, joyBase;
    float panelW;

    // Stage state.
    int index = -1;
    Stage3D stage;
    float[] values = new float[0];
    string freeText = "";
    Plot3DSpec spec;
    string specError;
    int hintsShown, fails;
    bool running;
    Stage3DMotor motor;
    Stage3DWorld runWorld;
    Stage3DSolver.Result autopilot;
    int autoWi;

    // 3D objects.
    MeshFilter groundMf, propsMf, waterMf, dropMf;
    MeshRenderer groundMr;
    TextMeshPro avatar;
    Transform avatarRoot;
    readonly List<SpriteRenderer> pickups = new List<SpriteRenderer>();
    SpriteRenderer flag;
    StageDriver driver;

    // Touch stick.
    int joyTouch = -1;
    Vector2 joyOrigin, joyVec;
    bool jumpQueued;

    public bool InStage => index >= 0;

    /// <summary>QA: steer along the solver path instead of reading input.</summary>
    public bool QaAutopilot;

    public Space3DStageView(Space3DScene host, Transform world)
    {
        this.host = host;
        this.world = world;
        stageRoot = new GameObject("StageWorld").transform;
        stageRoot.SetParent(world, false);
        BuildSelect();
        BuildPlay();
        BuildWorldObjects();
        driver = stageRoot.gameObject.AddComponent<StageDriver>();
        driver.View = this;
        if (!EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Enable();
    }

    /// <summary>Per-frame hook (the view is a plain class; this component forwards Update).</summary>
    sealed class StageDriver : MonoBehaviour
    {
        public Space3DStageView View;
        void Update() => View?.Tick(Time.deltaTime);
        void LateUpdate() => View?.LateTick();
    }

    public void SetActive(bool on)
    {
        if (!on)
        {
            StopRun();
            selectRoot.gameObject.SetActive(false);
            playRoot.gameObject.SetActive(false);
            stageRoot.gameObject.SetActive(false);
            index = -1;
            return;
        }

        ShowSelect();
    }

    // ------------------------------------------------------------------ select

    void BuildSelect()
    {
        selectRoot = Space3DUi.Rect("StageSelect", host.Body, Vector2.zero, Vector2.one);
        var bg = selectRoot.gameObject.AddComponent<Image>();
        bg.color = Bauhaus.Paper;
        var head = Space3DUi.Rect("Head", selectRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -120f), new Vector2(-40f, -16f));
        var h1 = Space3DUi.Rect("H1", head, new Vector2(0f, 0.45f), new Vector2(1f, 1f));
        Space3DUi.Label(h1, "Text", LocalizationManager.Get("space3d.stages_title", "Build the path, then walk it"), 40f, Bauhaus.Ink, FontStyles.Bold);
        var h2 = Space3DUi.Rect("H2", head, new Vector2(0f, 0f), new Vector2(1f, 0.45f));
        selectSummary = Space3DUi.Label(h2, "Text", "", 24f, Bauhaus.InkSoft);

        var viewport = Space3DUi.Rect("Viewport", selectRoot, Vector2.zero, Vector2.one, new Vector2(30f, 20f), new Vector2(-30f, -130f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);
        tileGrid = Space3DUi.Rect("Tiles", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
        tileGrid.pivot = new Vector2(0.5f, 1f);
        var grid = tileGrid.gameObject.AddComponent<GridLayoutGroup>();
        var safe = DeviceLayout.SafeArea;
        float avail = host.CanvasWidth * (safe.width / Mathf.Max(1f, Screen.width)) - 80f;
        int cols = avail > 1700f ? 5 : avail > 1300f ? 4 : 3;
        float cw = (avail - (cols - 1) * 18f - 20f) / cols;
        grid.cellSize = new Vector2(cw, 250f);
        grid.spacing = new Vector2(18f, 18f);
        grid.padding = new RectOffset(10, 10, 10, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        tileGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = tileGrid;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
    }

    public void ShowSelect()
    {
        StopRun();
        index = -1;
        host.SetTitle(LocalizationManager.Get("space3d.stages_header", "3D stages"));
        host.LeftInset = 0f;
        host.Axes.SetLabelsVisible(false);
        stageRoot.gameObject.SetActive(false);
        playRoot.gameObject.SetActive(false);
        selectRoot.gameObject.SetActive(true);
        host.Axes.gameObject.SetActive(false);
        RebuildTiles();
        selectSummary.text = string.Format(LocalizationManager.Get("space3d.summary", "{0} of {1} stages cleared  |  {2} of {3} stars"),
            Space3DProgress.ClearedCount, Stage3DCatalog.Count, Space3DProgress.TotalStars, Stage3DCatalog.Count * 3);
        var first = tileGrid.childCount > 0 ? tileGrid.GetChild(Mathf.Min(NextStageIndex(), tileGrid.childCount - 1)) : null;
        if (GamepadSupport.ControllerActive && first != null)
            EventSystem.current?.SetSelectedGameObject(first.gameObject);
    }

    static int NextStageIndex()
    {
        for (int i = 0; i < Stage3DCatalog.Count; i++)
            if (!Space3DProgress.Cleared(i)) return i;
        return 0;
    }

    void RebuildTiles()
    {
        for (int i = tileGrid.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(tileGrid.GetChild(i).gameObject);
        int next = NextStageIndex();
        for (int i = 0; i < Stage3DCatalog.Count; i++)
        {
            var st = Stage3DCatalog.All[i];
            int idx = i;
            bool open = Space3DProgress.IsOpenInOrder(i);
            bool bought = Space3DProgress.IsPurchased(i);
            int stars = Space3DProgress.Stars(i);
            var tile = Space3DUi.Rect("Stage" + (i + 1), tileGrid, Vector2.zero, Vector2.one);
            var tone = !open ? Bauhaus.Tone.Paper : i == next ? Bauhaus.Tone.Yellow : Bauhaus.Tone.Paper;
            var btn = Space3DUi.Button(tile, "", tone, null, () => OnTile(idx), 20f);
            var face = tile;
            var num = Space3DUi.Rect("Num", face, new Vector2(0f, 0.62f), new Vector2(0.4f, 1f), new Vector2(18f, 0f), new Vector2(0f, -8f));
            Space3DUi.Label(num, "Text", (i + 1).ToString(), 48f, open ? Bauhaus.Ink : Bauhaus.RuleStrong, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            var ttl = Space3DUi.Rect("Title", face, new Vector2(0f, 0.36f), new Vector2(1f, 0.64f), new Vector2(18f, 0f), new Vector2(-14f, 0f));
            var tt = Space3DUi.Label(ttl, "Text", st.Title, 26f, open ? Bauhaus.Ink : Bauhaus.InkSoft, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            tt.enableAutoSizing = true;
            tt.fontSizeMin = 16f;
            var cpt = Space3DUi.Rect("Concept", face, new Vector2(0f, 0.04f), new Vector2(1f, 0.38f), new Vector2(18f, 0f), new Vector2(-14f, 0f));
            var ct = Space3DUi.Label(cpt, "Text", open ? st.Concept : string.Format(LocalizationManager.Get("space3d.clear_first", "Clear stage {0} first"), i),
                18f, Bauhaus.InkSoft, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            ct.enableAutoSizing = true;
            ct.fontSizeMin = 13f;
            if (stars > 0)
            {
                var s = UiKit.Stars(face, new Vector2(0.5f, 0.66f), new Vector2(1f, 0.98f), stars, 34f);
                s[0].transform.parent.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperRight;
                var srt = s[0].transform.parent as RectTransform;
                srt.offsetMax = new Vector2(-12f, -10f);
            }
            else if (!bought)
            {
                var lk = Space3DUi.Pinned("Lock", face, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(40f, 40f));
                var img = lk.gameObject.AddComponent<Image>();
                img.sprite = LockGlyph.Locked;
                img.color = Bauhaus.InkSoft;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            if (!open)
                btn.interactable = true; // still focusable; tapping explains why it is closed
        }
    }

    void OnTile(int i)
    {
        if (!Space3DProgress.IsOpenInOrder(i))
        {
            FeelDirector.Play(SfxId.Locked, 0.7f);
            return;
        }

        if (!Space3DProgress.IsPurchased(i))
        {
            PaywallOverlay.Open(host.Canvas.transform, () => OpenStage(i));
            return;
        }

        OpenStage(i);
    }

    // ------------------------------------------------------------------ play UI

    void BuildPlay()
    {
        playRoot = Space3DUi.Rect("StagePlay", host.Body, Vector2.zero, Vector2.one);
        panelW = Mathf.Min(640f, host.CanvasWidth * 0.40f);
        panel = Space3DUi.Rect("Panel", playRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(16f + panelW, -14f));
        Space3DUi.Card(panel);
        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 16, 18);
        vlg.spacing = 10f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        stageTitle = PanelText("Title", "", 32f, Bauhaus.Ink, FontStyles.Bold, 46f);
        stageConcept = PanelText("Concept", "", 22f, Bauhaus.Blue, FontStyles.Bold, 32f);
        storyText = PanelText("Story", "", 19f, Bauhaus.InkSoft, FontStyles.Normal, 0f, flexible: true);
        storyText.enableAutoSizing = true;
        storyText.fontSizeMin = 13f;
        formulaText = PanelText("Formula", "", 30f, Bauhaus.Ink, FontStyles.Bold, 52f);
        formulaText.enableAutoSizing = true;
        formulaText.fontSizeMin = 16f;

        sliderBlock = Space3DUi.Rect("Sliders", panel, Vector2.zero, Vector2.one);
        var sv = sliderBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        sv.spacing = 4f;
        sv.childControlHeight = true;
        sv.childControlWidth = true;
        sv.childForceExpandHeight = false;
        sliderBlock.gameObject.AddComponent<LayoutElement>().minHeight = 10f;

        inputBlock = Space3DUi.Rect("FreeInput", panel, Vector2.zero, Vector2.one);
        inputBlock.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
        var fi = Space3DUi.Rect("Field", inputBlock, Vector2.zero, Vector2.one);
        freeInput = Space3DUi.Input(fi, "", "3*exp(-x^2/8)", 26f, s => OnFreeText(s), null);

        readText = PanelText("Readout", "", 19f, Bauhaus.Ink, FontStyles.Normal, 78f);
        readText.enableAutoSizing = true;
        readText.fontSizeMin = 12f;

        var actions = Space3DUi.Rect("Actions", panel, Vector2.zero, Vector2.one);
        actions.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        var go = Space3DUi.Rect("Go", actions, new Vector2(0f, 0f), new Vector2(0.62f, 1f), Vector2.zero, new Vector2(-6f, 0f));
        goBtn = Space3DUi.Button(go, LocalizationManager.Get("space3d.go", "Go"), Bauhaus.Tone.Red, StarGlyph.ArrowRight, ToggleRun, 30f, 40f);
        var hb = Space3DUi.Rect("Hint", actions, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), Vector2.zero);
        hintBtn = Space3DUi.Button(hb, LocalizationManager.Get("space3d.hint", "Hint"), Bauhaus.Tone.Yellow, StarGlyph.Help, ShowNextHint, 24f, 32f);
        hintBtn.gameObject.name = "HintButton";

        hintText = PanelText("HintText", "", 19f, Bauhaus.Ink, FontStyles.Italic, 60f);
        hintText.enableAutoSizing = true;
        hintText.fontSizeMin = 12f;

        // HUD: stars and restart (top-right of the 3D view).
        hud = Space3DUi.Pinned("Hud", playRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(330f, 76f));
        var hs = Space3DUi.Rect("Stars", hud, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-86f, 0f));
        Space3DUi.Card(hs, Bauhaus.Card, Bauhaus.ButtonRule);
        var star = StarGlyph.AddStar(hs, "Star", true, new Vector2(46f, 46f));
        star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        star.rectTransform.pivot = new Vector2(0f, 0.5f);
        star.rectTransform.anchoredPosition = new Vector2(14f, 0f);
        var ht = Space3DUi.Rect("Count", hs, Vector2.zero, Vector2.one, new Vector2(70f, 0f), new Vector2(-10f, 0f));
        hudStars = Space3DUi.Label(ht, "Text", "", 28f, Bauhaus.Ink, FontStyles.Bold);
        var rs = Space3DUi.Rect("Restart", hud, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-76f, 0f), Vector2.zero);
        Space3DUi.Button(rs, null, Bauhaus.Tone.Paper, StarGlyph.Restart, Restart, 20f, 34f);

        // Touch controls.
        joystickRoot = Space3DUi.Rect("Stick", playRoot, Vector2.zero, Vector2.zero);
        joystickRoot.sizeDelta = new Vector2(220f, 220f);
        joyBase = joystickRoot.gameObject.AddComponent<Image>();
        joyBase.sprite = Bauhaus.Frame;
        joyBase.type = Image.Type.Sliced;
        joyBase.color = Bauhaus.WithAlpha(Bauhaus.Ink, 0.35f);
        joyBase.raycastTarget = false;
        var knob = Space3DUi.Pinned("Knob", joystickRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
        joyKnob = knob.gameObject.AddComponent<Image>();
        joyKnob.sprite = Bauhaus.Square;
        joyKnob.color = Bauhaus.WithAlpha(Bauhaus.Ink, 0.55f);
        joyKnob.raycastTarget = false;
        joystickRoot.gameObject.SetActive(false);

        jumpRt = Space3DUi.Pinned("Jump", playRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(180f, 180f));
        var jb = Space3DUi.Button(jumpRt, null, Bauhaus.Tone.Yellow, StarGlyph.ArrowRight, () => jumpQueued = true, 20f, 86f);
        var jg = jumpRt.Find("Glyph");
        if (jg != null)
            jg.localRotation = Quaternion.Euler(0f, 0f, 90f);
        jb.gameObject.name = "JumpButton";

        // Result card (centre of the 3D view).
        card = Space3DUi.Pinned("Card", playRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 420f));
        Space3DUi.Card(card);
        var band = UiKit.Band(card, Bauhaus.Red, 18f);
        band.name = "Band";
        var ctl = Space3DUi.Rect("Title", card, new Vector2(0f, 0.72f), new Vector2(1f, 0.94f), new Vector2(30f, 0f), new Vector2(-30f, 0f));
        cardTitle = Space3DUi.Label(ctl, "Text", "", 40f, Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        cardStars = Space3DUi.Rect("StarsHolder", card, new Vector2(0f, 0.52f), new Vector2(1f, 0.72f));
        var cbd = Space3DUi.Rect("Body", card, new Vector2(0f, 0.3f), new Vector2(1f, 0.56f), new Vector2(36f, 0f), new Vector2(-36f, 0f));
        cardBody = Space3DUi.Label(cbd, "Text", "", 22f, Bauhaus.InkSoft, FontStyles.Normal, TextAlignmentOptions.Center);
        cardBody.enableAutoSizing = true;
        cardBody.fontSizeMin = 14f;
        var cp = Space3DUi.Rect("Primary", card, new Vector2(0.5f, 0.06f), new Vector2(0.95f, 0.26f), new Vector2(8f, 0f), Vector2.zero);
        cardPrimary = Space3DUi.Button(cp, "Next", Bauhaus.Tone.Red, StarGlyph.ArrowRight, null, 24f, 32f);
        var cs = Space3DUi.Rect("Secondary", card, new Vector2(0.05f, 0.06f), new Vector2(0.5f, 0.26f), Vector2.zero, new Vector2(-8f, 0f));
        cardSecondary = Space3DUi.Button(cs, "", Bauhaus.Tone.Paper, null, null, 24f);
        card.gameObject.SetActive(false);

        playRoot.gameObject.SetActive(false);
    }

    TextMeshProUGUI PanelText(string name, string text, float size, Color color, FontStyles style, float height, bool flexible = false)
    {
        var rt = Space3DUi.Rect(name, panel, Vector2.zero, Vector2.one);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        if (flexible)
        {
            le.flexibleHeight = 1f;
            le.minHeight = 60f;
        }
        else
            le.preferredHeight = height;
        return Space3DUi.Label(rt, "Text", text, size, color, style, TextAlignmentOptions.TopLeft);
    }

    // ------------------------------------------------------------------ 3D objects

    void BuildWorldObjects()
    {
        Space3DRender.NewMeshObject("Ground", stageRoot, Space3DRender.LitWithContours(1f, 0.25f), out groundMf);
        groundMr = groundMf.GetComponent<MeshRenderer>();
        Space3DRender.NewMeshObject("Props", stageRoot, Space3DRender.Lit, out propsMf);
        Space3DRender.NewMeshObject("Water", stageRoot, Space3DRender.UnlitTranslucent, out waterMf);
        Space3DRender.NewMeshObject("Drop", stageRoot, Space3DRender.Lit, out dropMf);

        avatarRoot = new GameObject("Player").transform;
        avatarRoot.SetParent(stageRoot, false);
        var tgo = new GameObject("Glyph");
        tgo.transform.SetParent(avatarRoot, false);
        avatar = tgo.AddComponent<TextMeshPro>();
        var font = Resources.Load<TMP_FontAsset>("Fonts/Outfit SDF");
        if (font != null)
            avatar.font = font;
        avatar.text = PlayerGlyphSettings.GetSelectedGlyph();
        avatar.fontSize = 9f;
        avatar.fontStyle = FontStyles.Bold;
        avatar.alignment = TextAlignmentOptions.Center;
        avatar.rectTransform.sizeDelta = new Vector2(2f, 2f);
        avatar.textWrappingMode = TextWrappingModes.NoWrap;
        avatar.color = Bauhaus.Ink;

        flag = NewSprite("GoalFlag", StarGlyph.Flag, Bauhaus.Red, 1.1f);
    }

    SpriteRenderer NewSprite(string name, Sprite s, Color c, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(stageRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.color = c;
        float w = s != null ? Mathf.Max(0.01f, s.bounds.size.x) : 1f;
        go.transform.localScale = Vector3.one * (size / w);
        return sr;
    }

    // ------------------------------------------------------------------ open / edit

    public void OpenStage(int i)
    {
        if (i < 0 || i >= Stage3DCatalog.Count)
            return;
        StopRun();
        index = i;
        stage = Stage3DCatalog.All[i];
        hintsShown = 0;
        fails = 0;
        var init = stage.InitialParams();
        values = new float[stage.Params.Length];
        for (int k = 0; k < values.Length; k++)
            values[k] = init[Stage3D.ParamSlot(stage.Params[k].Name)];
        freeText = stage.FreeFormula ? stage.Formula : "";

        selectRoot.gameObject.SetActive(false);
        playRoot.gameObject.SetActive(true);
        stageRoot.gameObject.SetActive(true);
        host.Axes.gameObject.SetActive(true);
        host.SetTitle(string.Format(LocalizationManager.Get("space3d.stage_n", "Stage {0}"), i + 1) + "  |  " + stage.Title);
        stageTitle.text = stage.Title;
        stageConcept.text = stage.Concept;
        storyText.text = stage.Story;
        hintText.text = "";
        card.gameObject.SetActive(false);
        BuildSliders();
        inputBlock.gameObject.SetActive(stage.FreeFormula);
        sliderBlock.gameObject.SetActive(!stage.FreeFormula);
        if (stage.FreeFormula)
            freeInput.SetTextWithoutNotify(freeText);

        host.Axes.ShowTicks = true;
        host.Axes.Rebuild(stage.Box, showGrid: false);
        host.Axes.SetLabelsVisible(true);
        BuildProps();
        Rebuild();
        EnterEdit(frame: true);
    }

    void BuildSliders()
    {
        for (int i = sliderBlock.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(sliderBlock.GetChild(i).gameObject);
        sliders.Clear();
        sliderValues.Clear();
        for (int k = 0; k < stage.Params.Length; k++)
        {
            var p = stage.Params[k];
            int slot = k;
            var r = Space3DUi.Rect("Slider_" + p.Name, sliderBlock, Vector2.zero, Vector2.one);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            var name = Space3DUi.Rect("Name", r, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(50f, 0f));
            Space3DUi.Label(name, "Text", p.Name, 32f, Bauhaus.Ink, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
            var sr = Space3DUi.Rect("Slider", r, Vector2.zero, Vector2.one, new Vector2(58f, 8f), new Vector2(-112f, -8f));
            int steps = Mathf.Max(1, Mathf.RoundToInt((p.Max - p.Min) / Mathf.Max(1e-4f, p.Step)));
            var s = Space3DUi.Slider(sr, 0f, steps, Mathf.Round((values[k] - p.Min) / p.Step), v => OnSlider(slot, v));
            s.wholeNumbers = true;
            sliders.Add(s);
            var val = Space3DUi.Rect("Value", r, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-104f, 0f), Vector2.zero);
            sliderValues.Add(Space3DUi.Label(val, "Text", Space3DUi.Num(values[k]), 28f, Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight));
        }
    }

    void OnSlider(int k, float stepIndex)
    {
        if (running)
            return;
        var p = stage.Params[k];
        values[k] = p.Snap(p.Min + stepIndex * p.Step);
        sliderValues[k].text = Space3DUi.Num(values[k]);
        Rebuild();
    }

    void OnFreeText(string s)
    {
        if (running)
            return;
        freeText = s;
        Rebuild();
    }

    string Formula => stage.FreeFormula ? freeText : stage.Formula;

    float[] Slots => stage.FreeFormula ? stage.InitialParams() : stage.ToSlots(values);

    /// <summary>Formula with the slider values written in, e.g. z = 0.5 x + 0.75 y.</summary>
    string DisplayFormula()
    {
        string lhs = stage.Mode == Stage3DMode.Rail ? "r(t) = " : stage.Mode == Stage3DMode.ParamSurface ? "r(u, v) = " : "z = ";
        if (stage.FreeFormula)
            return lhs.TrimEnd();
        string f = stage.Formula;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < f.Length; i++)
        {
            char c = f[i];
            int pk = Array.FindIndex(stage.Params, p => p.Name.Length == 1 && p.Name[0] == c);
            bool isolated = (i == 0 || !char.IsLetter(f[i - 1])) && (i == f.Length - 1 || !char.IsLetter(f[i + 1]));
            if (pk >= 0 && isolated)
            {
                string v = Space3DUi.Num(values[pk]);
                sb.Append(values[pk] < 0f ? "(" + v + ")" : v);
            }
            else if (c == '*')
                sb.Append(' ');
            else if (c == '-')
                sb.Append('−');
            else
                sb.Append(c);
        }

        return lhs + sb.ToString().Replace("^2", "²");
    }

    void Rebuild()
    {
        specError = null;
        spec = null;
        if (!string.IsNullOrWhiteSpace(Formula) && !stage.TryBuildSpec(Formula, out spec, out specError))
            spec = null;
        else if (string.IsNullOrWhiteSpace(Formula))
            specError = LocalizationManager.Get("space3d.type_surface", "Type a surface z = f(x, y)");
        formulaText.text = DisplayFormula();
        goBtn.interactable = spec != null;
        RebuildGround();
        RefreshReadout();
    }

    void RebuildGround()
    {
        var box = stage.Box;
        var mesh = groundMf.sharedMesh;
        if (spec == null)
        {
            mesh.Clear();
            return;
        }

        var prm = Slots;
        int res = Space3DScene.Mobile ? 72 : 120;
        groundMr.sharedMaterial.SetFloat("_Contour", 1f);
        groundMr.sharedMaterial.SetFloat("_ContourAlpha", stage.Mode == Stage3DMode.Rail ? 0f : 0.25f);
        switch (stage.Mode)
        {
            case Stage3DMode.Rail:
            {
                var sampled = Plot3DSampler.SampleCurve(spec, new Box3 { XMin = -50, XMax = 50, YMin = -50, YMax = 50, ZMin = -50, ZMax = 50 }, prm, 600);
                Space3DRender.FillTubes(mesh, sampled.Polylines, 0.09f, Bauhaus.Blue, 10);
                break;
            }
            case Stage3DMode.ParamSurface:
            {
                var big = box;
                big.ZMin -= 5f;
                big.ZMax += 5f;
                var sampled = Plot3DSampler.SampleParamSurface(spec, big, prm, res / 2);
                Space3DRender.FillSurface(mesh, sampled, true, Color.white, stage.Water, stage.ZMax);
                break;
            }
            default:
            {
                var sampled = Plot3DSampler.SampleSurface(spec, box, prm, res);
                Space3DRender.FillSurface(mesh, sampled, true, Color.white, stage.Water, stage.ZMax);
                break;
            }
        }
    }

    void BuildProps()
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var c = new List<Color>();
        var t = new List<int>();
        float h = stage.PadHalf;
        const float thick = 0.28f;
        Space3DRender.AppendBox(new NV3(stage.Start.X - h, stage.Start.Y - h, stage.Start.Z - thick), new NV3(stage.Start.X + h, stage.Start.Y + h, stage.Start.Z), Bauhaus.Blue, v, n, c, t);
        Space3DRender.AppendBox(new NV3(stage.Goal.X - h, stage.Goal.Y - h, stage.Goal.Z - thick), new NV3(stage.Goal.X + h, stage.Goal.Y + h, stage.Goal.Z), Bauhaus.Red, v, n, c, t);
        foreach (var b in stage.Blocks)
        {
            var min = b.Min;
            min.Z = Mathf.Max(min.Z, stage.Water - 0.5f);
            Space3DRender.AppendBox(min, b.Max, Bauhaus.Hex("3A3833"), v, n, c, t);
        }

        Space3DRender.FillLists(propsMf.sharedMesh, v, n, c, t);
        Space3DRender.FillWater(waterMf.sharedMesh, stage.Box, stage.Water, 3f, Bauhaus.WithAlpha(Bauhaus.Blue, 0.26f));

        foreach (var p in pickups)
            UnityEngine.Object.Destroy(p.gameObject);
        pickups.Clear();
        foreach (var p in stage.Pickups)
        {
            var sr = NewSprite("Pickup", StarGlyph.Filled, Bauhaus.Yellow, 0.7f);
            sr.transform.position = Space3DRender.ToWorld(p);
            pickups.Add(sr);
        }

        flag.transform.position = Space3DRender.ToWorld(stage.Goal + new NV3(0f, 0f, 0.75f));
    }

    void RefreshReadout()
    {
        if (spec == null)
        {
            readText.text = $"<color=#{Bauhaus.HexOf(Bauhaus.Red)}>{specError}</color>";
            return;
        }

        var prm = Slots;
        string Ok(bool good, string s) => $"<color=#{Bauhaus.HexOf(good ? Bauhaus.Blue : Bauhaus.Red)}>{s}</color>";
        string P(NV3 q) => $"({Space3DUi.Num(q.X)}, {Space3DUi.Num(q.Y)}, {Space3DUi.Num(q.Z)})";
        switch (stage.Mode)
        {
            case Stage3DMode.Rail:
            {
                var a = spec.CurvePoint(stage.Range0[0], prm);
                var b = spec.CurvePoint(stage.Range0[1], prm);
                bool goodB = Stage3DWorld.Finite(b) && (b - stage.Goal).Length() <= Stage3DWorld.AttachDist;
                readText.text = $"r({Space3DUi.Num(stage.Range0[0])}) = {P(a)}\n" +
                                Ok(goodB, $"r({Tlabel(stage.Range0[1])}) = {P(b)}") + $"   goal {P(stage.Goal)}";
                break;
            }
            case Stage3DMode.ParamSurface:
            {
                var e = spec.ParamPoint(stage.Range0[1], stage.Range1[1], prm);
                bool good = Math.Abs(e.Z - stage.Goal.Z) <= Stage3DWorld.GoalTolerance;
                readText.text = Ok(good, $"top of the ramp: z = {Space3DUi.Num(e.Z)}") + $"   goal z = {Space3DUi.Num(stage.Goal.Z)}\n" +
                                $"start z = {Space3DUi.Num(spec.ParamPoint(stage.Range0[1], stage.Range1[0], prm).Z)}";
                break;
            }
            default:
            {
                float fs = spec.EvalSurface(stage.Start.X, stage.Start.Y, prm);
                float fg = spec.EvalSurface(stage.Goal.X, stage.Goal.Y, prm);
                bool gs = Math.Abs(fs - stage.Start.Z) <= Stage3DWorld.GoalTolerance;
                bool gg = Math.Abs(fg - stage.Goal.Z) <= Stage3DWorld.GoalTolerance;
                readText.text = Ok(gs, $"f({Space3DUi.Num(stage.Start.X)}, {Space3DUi.Num(stage.Start.Y)}) = {Space3DUi.Num(fs)}") +
                                $"   start pad z = {Space3DUi.Num(stage.Start.Z)}\n" +
                                Ok(gg, $"f({Space3DUi.Num(stage.Goal.X)}, {Space3DUi.Num(stage.Goal.Y)}) = {Space3DUi.Num(fg)}") +
                                $"   goal pad z = {Space3DUi.Num(stage.Goal.Z)}";
                break;
            }
        }
    }

    static string Tlabel(float t)
    {
        float k = t / Mathf.PI;
        if (Mathf.Abs(t) > 0.5f && Mathf.Abs(k - Mathf.Round(k)) < 1e-4f)
            return Mathf.Round(k) == 1f ? "π" : Space3DUi.Num(Mathf.Round(k)) + "π";
        return Space3DUi.Num(t);
    }

    void ShowNextHint()
    {
        if (stage == null || stage.Hints == null || stage.Hints.Length == 0)
            return;
        hintsShown = Mathf.Min(hintsShown + 1, stage.Hints.Length);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hintsShown; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(stage.Hints[i]);
        }

        hintText.text = sb.ToString();
        FeelDirector.Play(SfxId.Hint, 0.7f);
        ProgressStore.Data.hintsUsedTotal++;
    }

    void EnterEdit(bool frame)
    {
        running = false;
        motor = null;
        runWorld = null;
        card.gameObject.SetActive(false);
        Space3DUi.SetLabel(goBtn, LocalizationManager.Get("space3d.go", "Go"));
        Space3DUi.SetTone(goBtn, Bauhaus.Tone.Red);
        goBtn.interactable = spec != null;
        foreach (var s in sliders)
            s.interactable = true;
        freeInput.interactable = true;
        panel.gameObject.SetActive(true);
        host.LeftInset = panelW + 16f;
        host.Rig.FollowPoint = null;
        host.Rig.TouchOrbitRegion = null;
        host.Rig.AllowPan = true;
        host.Rig.MinPitch = -10f;
        host.Axes.gameObject.SetActive(true);
        if (frame)
        {
            host.Rig.Frame(stage.Box, -38f, 30f);
            host.Rig.Distance *= 0.82f;
        }
        jumpRt.gameObject.SetActive(false);
        joystickRoot.gameObject.SetActive(false);
        for (int i = 0; i < pickups.Count; i++)
            pickups[i].gameObject.SetActive(true);
        PlaceAvatar(stage.Start);
        dropMf.sharedMesh.Clear();
        UpdateHud(0);
    }

    void UpdateHud(int collected)
    {
        hudStars.text = $"{collected} / {stage.Pickups.Length}";
    }

    void ToggleRun()
    {
        if (running)
        {
            StopRun();
            EnterEdit(frame: false);
            return;
        }

        if (spec == null)
            return;
        running = true;
        runWorld = new Stage3DWorld(stage, spec, Slots);
        motor = new Stage3DMotor(runWorld);
        autopilot = QaAutopilot ? Stage3DSolver.Solve(stage, Formula, Slots) : null;
        autoWi = 0;
        Space3DUi.SetLabel(goBtn, LocalizationManager.Get("space3d.edit", "Edit"));
        Space3DUi.SetTone(goBtn, Bauhaus.Tone.Blue);
        foreach (var s in sliders)
            s.interactable = false;
        freeInput.interactable = false;
        bool touch = Space3DScene.Mobile;
        jumpRt.gameObject.SetActive(touch);
        host.Rig.FollowPoint = () => Space3DRender.ToWorld(motor != null ? motor.Center : stage.Start) + Vector3.up * 0.4f;
        host.Rig.TouchOrbitRegion = p => p.x > Screen.width * 0.5f;
        host.Rig.AllowPan = false;
        host.Rig.Distance = 8.5f;
        host.Rig.Pitch = 34f;
        host.Axes.gameObject.SetActive(false);
        host.Rig.MinPitch = -10f;
        // Start looking from the start toward the goal.
        var d = Space3DRender.ToWorld(stage.Goal) - Space3DRender.ToWorld(stage.Start);
        if (new Vector2(d.x, d.z).sqrMagnitude > 0.01f)
            host.Rig.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        // On phones the panel would hide half the run: fold it away (Edit is on the HUD row then).
        host.LeftInset = 0f;
        panel.gameObject.SetActive(false);
        ShowFloatingEdit(true);
        EventSystem.current?.SetSelectedGameObject(null);
        jumpQueued = false;
        FeelDirector.Play(SfxId.Click, 0.8f);
    }

    Button floatingEdit;

    void ShowFloatingEdit(bool on)
    {
        if (floatingEdit == null)
        {
            var rt = Space3DUi.Pinned("EditFloating", playRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(230f, 76f));
            floatingEdit = Space3DUi.Button(rt, LocalizationManager.Get("space3d.edit", "Edit"), Bauhaus.Tone.Blue, StarGlyph.Sliders, () =>
            {
                StopRun();
                EnterEdit(frame: false);
            }, 26f, 32f);
        }

        floatingEdit.gameObject.SetActive(on);
    }

    void StopRun()
    {
        running = false;
        motor = null;
        joyTouch = -1;
        if (floatingEdit != null)
            floatingEdit.gameObject.SetActive(false);
    }

    void Restart()
    {
        if (stage == null)
            return;
        if (running)
        {
            StopRun();
            ToggleRun();
        }
        else
            EnterEdit(frame: true);
    }

    /// <summary>Back from the top bar / Esc / B: leave a run first, then the stage.</summary>
    public bool HandleBack()
    {
        if (running || card.gameObject.activeSelf)
        {
            StopRun();
            EnterEdit(frame: false);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ run

    void Tick(float dt)
    {
        if (stage == null || index < 0)
            return;
        AnimateProps();
        if (!running || motor == null)
        {
            HandleEditShortcuts();
            return;
        }

        ReadInput(out var move, out bool jump, out float ride);
        dt = Mathf.Min(dt, 1f / 20f);
        // Fixed sub-steps keep the motor deterministic and close to the solver.
        int sub = Mathf.CeilToInt(dt / (1f / 120f));
        float h = dt / sub;
        bool landed = false, jumped = false, collected = false;
        for (int s = 0; s < sub && !motor.Finished; s++)
        {
            motor.Step(h, move, jump && s == 0, ride);
            landed |= motor.JustLanded;
            jumped |= motor.JustJumped;
            collected |= motor.JustCollected;
        }

        if (jumped) FeelDirector.Play(SfxId.Jump, 0.7f);
        if (landed) FeelDirector.Play(SfxId.Land, 0.5f);
        if (collected)
        {
            FeelDirector.Play(SfxId.Star1 + Mathf.Clamp(motor.CollectedCount - 1, 0, 2), 0.9f);
            UpdateHud(motor.CollectedCount);
        }

        for (int i = 0; i < pickups.Count && i < motor.Collected.Length; i++)
            pickups[i].gameObject.SetActive(!motor.Collected[i]);
        PlaceAvatar(motor.Feet);
        UpdateDropLine();

        KeepCameraAboveGround(dt);
        if (motor.State == Stage3DMotorState.Rail)
        {
            var tan = Space3DRender.ToWorld(motor.RailTangent());
            if (new Vector2(tan.x, tan.z).sqrMagnitude > 0.04f)
                host.Rig.Yaw = Mathf.LerpAngle(host.Rig.Yaw, Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 2.5f));
        }

        if (motor.State == Stage3DMotorState.Dead)
            OnDead();
        else if (motor.State == Stage3DMotorState.Won)
            OnWon();
    }

    /// <summary>Third-person camera: tilt up when the view would dip under the ground or into a wall.</summary>
    void KeepCameraAboveGround(float dt)
    {
        var cp = Space3DRender.ToMath(host.Cam.transform.position);
        float g = runWorld.SupportBelow(cp.X, cp.Y, float.PositiveInfinity, out _);
        bool inBlock = runWorld.InsideBlock(cp, 0.3f);
        if (inBlock || (!float.IsNaN(g) && cp.Z < g + 0.8f))
            host.Rig.Pitch = Mathf.Min(78f, host.Rig.Pitch + 90f * dt);
    }

    void HandleEditShortcuts()
    {
        var k = Keyboard.current;
        var gp = Gamepad.current;
        if (card.gameObject.activeSelf)
            return;
        bool go = (k != null && !Space3DCameraRig.TypingInField() && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
                  || (gp != null && gp.startButton.wasPressedThisFrame);
        if (go && spec != null)
            ToggleRun();
        if (gp != null && gp.buttonNorth.wasPressedThisFrame)
            ShowNextHint();
    }

    void ReadInput(out NV2 move, out bool jump, out float ride)
    {
        Vector2 input = Vector2.zero;
        jump = jumpQueued;
        jumpQueued = false;
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.wKey.isPressed || k.upArrowKey.isPressed) input.y += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) input.y -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) input.x += 1f;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) input.x -= 1f;
            jump |= k.spaceKey.wasPressedThisFrame;
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            var ls = gp.leftStick.ReadValue();
            if (ls.sqrMagnitude > 0.03f) input += ls;
            input += gp.dpad.ReadValue();
            jump |= gp.buttonSouth.wasPressedThisFrame;
            if (gp.startButton.wasPressedThisFrame)
            {
                StopRun();
                EnterEdit(frame: false);
            }
        }

        input += TouchStick();
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        // Camera-relative.
        var fwd = host.Rig.FlatForward;
        var right = Vector3.Cross(Vector3.up, fwd);
        var w = right * input.x + fwd * input.y;
        move = new NV2(w.x, w.z);
        ride = input.y;
        if (motor != null && motor.State == Stage3DMotorState.Rail)
        {
            var tan = motor.RailTangent();
            var th = new NV2(tan.X, tan.Y);
            if (th.Length() > 0.25f && move.Length() > 0.05f)
                ride = Mathf.Clamp(NV2.Dot(move, NV2.Normalize(th)) * 1.4f, -1f, 1f);
        }

        if (QaAutopilot && autopilot != null)
            Autopilot(ref move, ref ride, ref jump);
    }

    void Autopilot(ref NV2 move, ref float ride, ref bool jump)
    {
        jump = false;
        ride = 1f;
        if (autopilot.Path.Count == 0 || motor.State == Stage3DMotorState.Rail)
        {
            move = NV2.Zero;
            return;
        }

        var pos = new NV2(motor.Feet.X, motor.Feet.Y);
        var way = autopilot.Path;
        while (autoWi < way.Count - 1 && (new NV2(way[autoWi].X, way[autoWi].Y) - pos).Length() < 0.15f)
            autoWi++;
        var target = autoWi >= way.Count - 1 ? new NV2(stage.Goal.X, stage.Goal.Y) : new NV2(way[autoWi].X, way[autoWi].Y);
        var d = target - pos;
        move = d.Length() > 1e-4f ? NV2.Normalize(d) : NV2.Zero;
    }

    Vector2 TouchStick()
    {
        var touches = ETouch.activeTouches;
        Vector2 result = Vector2.zero;
        bool alive = false;
        foreach (var t in touches)
        {
            if (t.touchId == joyTouch)
            {
                alive = t.phase != UnityEngine.InputSystem.TouchPhase.Ended && t.phase != UnityEngine.InputSystem.TouchPhase.Canceled;
                if (alive)
                {
                    var d = t.screenPosition - joyOrigin;
                    float r = 90f * Mathf.Max(1f, Screen.height / 1080f);
                    joyVec = Vector2.ClampMagnitude(d / r, 1f);
                    result = joyVec;
                }
            }
            else if (joyTouch < 0 && t.phase == UnityEngine.InputSystem.TouchPhase.Began &&
                     t.screenPosition.x < Screen.width * 0.5f && !Space3DCameraRig.PointerOverUi(t.screenPosition))
            {
                joyTouch = t.touchId;
                joyOrigin = t.screenPosition;
                joyVec = Vector2.zero;
                alive = true;
            }
        }

        if (!alive)
        {
            joyTouch = -1;
            joyVec = Vector2.zero;
        }

        joystickRoot.gameObject.SetActive(alive);
        if (alive)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(playRoot, joyOrigin, null, out var local);
            joystickRoot.anchorMin = joystickRoot.anchorMax = new Vector2(0.5f, 0.5f);
            joystickRoot.anchoredPosition = local;
            joyKnob.rectTransform.anchoredPosition = joyVec * 70f;
        }

        return result;
    }

    void OnDead()
    {
        fails++;
        ProgressStore.Data.falls++;
        FeelDirector.Play(SfxId.Death, 0.8f);
        running = false;
        ShowFloatingEdit(false);
        jumpRt.gameObject.SetActive(false);
        ShowCard(false, motor.DeathReason, 0);
    }

    void OnWon()
    {
        int stars = 1 + motor.CollectedCount;
        running = false;
        ShowFloatingEdit(false);
        jumpRt.gameObject.SetActive(false);
        bool firstClear = !Space3DProgress.Cleared(index);
        Space3DProgress.Record(index, stars);
        FeelDirector.Play(SfxId.Complete, 0.9f);
        ShowCard(true, firstClear ? LocalizationManager.Get("space3d.first_clear", "First clear!") : "", stars);
    }

    void ShowCard(bool won, string reason, int stars)
    {
        card.gameObject.SetActive(true);
        card.Find("Band").GetComponent<Image>().color = won ? Bauhaus.Blue : Bauhaus.Red;
        for (int i = cardStars.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(cardStars.GetChild(i).gameObject);
        cardPrimary.onClick.RemoveAllListeners();
        cardSecondary.onClick.RemoveAllListeners();
        cardPrimary.onClick.AddListener(() => FeelDirector.Play(SfxId.Click, 0.6f));
        cardSecondary.onClick.AddListener(() => FeelDirector.Play(SfxId.Click, 0.6f));
        if (won)
        {
            cardTitle.text = LocalizationManager.Get("space3d.clear", "Stage clear");
            UiKit.Stars(cardStars, Vector2.zero, Vector2.one, stars, 64f);
            string more = stars < 3 ? LocalizationManager.Get("space3d.more_stars", "Stars floating above the stage add a star each: shape the path through them.") : "";
            cardBody.text = (string.IsNullOrEmpty(reason) ? "" : reason + "  ") + DisplayFormula() + (more.Length > 0 ? "\n" + more : "");
            bool hasNext = index + 1 < Stage3DCatalog.Count;
            Space3DUi.SetLabel(cardPrimary, hasNext ? LocalizationManager.Get("space3d.next", "Next stage") : LocalizationManager.Get("space3d.all_stages", "All stages"));
            cardPrimary.onClick.AddListener(() =>
            {
                if (hasNext && Space3DProgress.IsPurchased(index + 1))
                    OpenStage(index + 1);
                else if (hasNext)
                    PaywallOverlay.Open(host.Canvas.transform, () => OpenStage(index + 1));
                else
                    ShowSelect();
            });
            Space3DUi.SetLabel(cardSecondary, LocalizationManager.Get("space3d.replay", "Play again"));
            cardSecondary.onClick.AddListener(() => EnterEdit(frame: false));
        }
        else
        {
            cardTitle.text = reason;
            cardBody.text = fails >= 2 && hintsShown < (stage.Hints?.Length ?? 0)
                ? LocalizationManager.Get("space3d.try_hint", "Change the function and try again. Stuck? Tap Hint.")
                : LocalizationManager.Get("space3d.try_again", "Change the function and try again.");
            Space3DUi.SetLabel(cardPrimary, LocalizationManager.Get("space3d.edit_fn", "Edit function"));
            cardPrimary.onClick.AddListener(() => EnterEdit(frame: false));
            Space3DUi.SetLabel(cardSecondary, LocalizationManager.Get("space3d.retry", "Try again"));
            cardSecondary.onClick.AddListener(() =>
            {
                card.gameObject.SetActive(false);
                EnterEdit(frame: false);
                ToggleRun();
            });
        }

        if (GamepadSupport.ControllerActive)
            EventSystem.current?.SetSelectedGameObject(cardPrimary.gameObject);
        else
            EventSystem.current?.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------ visuals

    void PlaceAvatar(NV3 feet)
    {
        avatarRoot.position = Space3DRender.ToWorld(feet) + Vector3.up * Stage3DWorld.CenterHeight;
        string g = PlayerGlyphSettings.GetSelectedGlyph();
        if (avatar.text != g)
            avatar.text = g;
    }

    void UpdateDropLine()
    {
        if (motor == null || motor.State == Stage3DMotorState.Walking || motor.State == Stage3DMotorState.OnSurface)
        {
            dropMf.sharedMesh.Clear();
            return;
        }

        var f = motor.Feet;
        float g = runWorld.SupportBelow(f.X, f.Y, f.Z, out _);
        float bottom = float.IsNaN(g) ? stage.Water : Mathf.Max(g, stage.Water);
        if (f.Z - bottom < 0.15f)
        {
            dropMf.sharedMesh.Clear();
            return;
        }

        var lines = new List<List<NV3>>
        {
            new List<NV3> { f, new NV3(f.X, f.Y, bottom) },
            new List<NV3> { new NV3(f.X - 0.25f, f.Y, bottom), new NV3(f.X + 0.25f, f.Y, bottom) },
            new List<NV3> { new NV3(f.X, f.Y - 0.25f, bottom), new NV3(f.X, f.Y + 0.25f, bottom) },
        };
        Space3DRender.FillTubes(dropMf.sharedMesh, lines, 0.025f, Bauhaus.InkSoft, 5);
    }

    void AnimateProps()
    {
        float t = Time.time;
        for (int i = 0; i < pickups.Count && i < stage.Pickups.Length; i++)
            pickups[i].transform.position = Space3DRender.ToWorld(stage.Pickups[i]) + Vector3.up * (Mathf.Sin(t * 2.2f + i) * 0.08f);
    }

    void LateTick()
    {
        if (stage == null || index < 0)
            return;
        var cam = host.Cam.transform;
        void Face(Transform tr)
        {
            var d = tr.position - cam.position;
            if (d.sqrMagnitude > 1e-6f)
                tr.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        Face(avatarRoot);
        Face(flag.transform);
        foreach (var p in pickups)
            Face(p.transform);
    }

    // ------------------------------------------------------------------ QA

    public void QaApplySolution()
    {
        if (stage == null)
            return;
        if (stage.FreeFormula)
        {
            freeText = stage.SolutionFormula;
            freeInput.SetTextWithoutNotify(freeText);
        }
        else
        {
            for (int k = 0; k < values.Length && k < stage.SolutionParams.Length; k++)
            {
                values[k] = stage.SolutionParams[k];
                var p = stage.Params[k];
                sliders[k].SetValueWithoutNotify(Mathf.Round((values[k] - p.Min) / p.Step));
                sliderValues[k].text = Space3DUi.Num(values[k]);
            }
        }

        Rebuild();
    }

    public void QaGo() => ToggleRun();

    public bool QaRunning => running;
}
