using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// -----------------------------------------------------------------------------
// AchievementsOverlay — progress sheet (stars, streak, achievements list, sound toggle)
// AchievementToaster  — slide-in banner whenever ProgressStore unlocks an achievement.
// -----------------------------------------------------------------------------

public static class AchievementsOverlay
{
    const string RootName = "AchievementsOverlayRoot";
    static GameObject _root;

    public static void Open(Transform canvas)
    {
        Close();
        if (canvas == null)
            return;
        bool tablet = DeviceLayout.IsTabletLike();
        bool landscape = Screen.width >= Screen.height;
        var rootRt = UiKit.FullScreenDim(RootName, canvas, 0.88f, Close);
        _root = rootRt.gameObject;
        var panel = UiKit.Panel(rootRt,
            landscape ? new Vector2(0.16f, 0.04f) : new Vector2(0.04f, 0.08f),
            landscape ? new Vector2(0.84f, 0.96f) : new Vector2(0.96f, 0.92f));

        UiKit.Band(panel, Bauhaus.Blue, 18f);
        UiKit.Text(panel, "Title", new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.955f),
            LocalizationManager.Get("ach.title", "Progress & achievements"),
            UiTypography.Scale(tablet ? 40 : 36), Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

        var d = ProgressStore.Data;
        int maxStars = ProgressStore.PlayableLevelCount * ProgressRules.MaxStars;
        string summary = string.Format(LocalizationManager.Get("ach.summary",
                "<color=#D7261E><b>{0}/{1}</b> stars</color>   |   <b>{6}</b> medals   |   <b>{2}</b> levels cleared   |   streak <b>{3}</b> (best {4})   |   <b>{5}</b> f′ double jumps"),
            ProgressStore.TotalStars, maxStars, ProgressStore.Snapshot().LevelsCleared,
            ProgressStore.CurrentDailyStreak, d.dailyBestStreak, d.airJumps, ProgressStore.MedalCount);
        UiKit.Text(panel, "Summary", new Vector2(0.05f, 0.815f), new Vector2(0.95f, 0.88f), summary,
            UiTypography.Scale(tablet ? 21 : 19), UiKit.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

        // Scroll list -------------------------------------------------------------
        var scrollRt = UiKit.Rect("Scroll", panel, new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.81f));
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = DeviceLayout.LevelSelectScrollSensitivity;
        var viewport = UiKit.Rect("Viewport", scrollRt, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vimg = viewport.gameObject.AddComponent<Image>();
        vimg.color = new Color(0f, 0f, 0f, 0f);
        scroll.viewport = viewport;
        var content = UiKit.Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
        content.pivot = new Vector2(0.5f, 1f);
        scroll.content = content;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var a in AchievementCatalog.All)
        {
            bool got = ProgressStore.HasAchievement(a.Id);
            var row = new GameObject("Ach_" + a.Id);
            var rrt = row.AddComponent<RectTransform>();
            rrt.SetParent(content, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = tablet ? 86f : 80f;
            // Unlocked: white row with a yellow block behind the trophy. Locked: paper row, grey glyph.
            var bg = row.AddComponent<Image>();
            Bauhaus.Panel(bg, got ? Color.white : Bauhaus.Paper, 2f);
            bg.raycastTarget = false;
            if (got)
            {
                var block = UiKit.Rect("Block", rrt, new Vector2(0f, 0f), new Vector2(0f, 1f));
                block.pivot = new Vector2(0f, 0.5f);
                block.sizeDelta = new Vector2(78f, 0f);
                var bimg = block.gameObject.AddComponent<Image>();
                bimg.sprite = Bauhaus.Square;
                bimg.color = Bauhaus.Yellow;
                bimg.raycastTarget = false;
            }
            var icon = StarGlyph.Add(rrt, "Icon", StarGlyph.Trophy, got ? Bauhaus.Ink : UiKit.GoldDim, new Vector2(50f, 50f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            var t = UiKit.Text(rrt, "Text", new Vector2(0f, 0.08f), new Vector2(1f, 0.92f),
                $"<b>{LocalizationManager.Get("ach." + a.Id + ".t", a.Title)}</b>\n<size=78%>{LocalizationManager.Get("ach." + a.Id + ".d", a.Description)}</size>",
                UiTypography.Scale(tablet ? 24 : 22), got ? RuntimeUiPolish.TitleIvory : UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            t.rectTransform.offsetMin = new Vector2(84f, 0f);
            t.rectTransform.offsetMax = new Vector2(-16f, 0f);
        }

        // Footer: sound toggle + close --------------------------------------------
        TextMeshProUGUI soundLabel = null;
        var soundBtn = UiKit.Button(panel, "Sound", new Vector2(0.04f, 0.03f), new Vector2(0.35f, 0.115f), "",
            Bauhaus.Tone.Paper, () =>
            {
                ProgressStore.SfxMuted = !ProgressStore.SfxMuted;
                SetSoundLabel(soundLabel);
            }, tablet ? 24 : 22);
        soundLabel = soundBtn.GetComponentInChildren<TextMeshProUGUI>();
        SetSoundLabel(soundLabel);
        UiKit.Button(panel, "ShowTutorial", new Vector2(0.37f, 0.03f), new Vector2(0.66f, 0.115f),
            LocalizationManager.Get("ui.show_tutorial", "Show tutorial"), Bauhaus.Tone.Blue, () =>
            {
                Close();
                LevelSelectController.ReplayTutorials();
            }, tablet ? 24 : 22);
        var close = UiKit.Button(panel, "Close", new Vector2(0.68f, 0.03f), new Vector2(0.96f, 0.115f),
            LocalizationManager.Get("ui.close", "Close"), Bauhaus.Tone.Ink, Close, tablet ? 24 : 22);
        OverlayCancelHandler.Attach(_root, close, close);
    }

    static void SetSoundLabel(TextMeshProUGUI t)
    {
        if (t == null) return;
        t.text = ProgressStore.SfxMuted
            ? LocalizationManager.Get("ui.sfx_off", "Sound effects: Off")
            : LocalizationManager.Get("ui.sfx_on", "Sound effects: On");
    }

    public static void Close()
    {
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
    }
}

public class AchievementToaster : MonoBehaviour
{
    static AchievementToaster _instance;
    readonly System.Collections.Generic.Queue<AchievementDef> _queue = new System.Collections.Generic.Queue<AchievementDef>();
    bool _showing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null)
            return;
        var go = new GameObject("AchievementToaster");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<AchievementToaster>();
    }

    void OnEnable() => ProgressStore.AchievementUnlocked += Enqueue;
    void OnDisable() => ProgressStore.AchievementUnlocked -= Enqueue;

    void Enqueue(AchievementDef def)
    {
        _queue.Enqueue(def);
        if (!_showing)
            StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        _showing = true;
        while (_queue.Count > 0)
        {
            var def = _queue.Dequeue();
            yield return Show(def);
        }
        _showing = false;
    }

    IEnumerator Show(AchievementDef def)
    {
        Canvas canvas = null;
        foreach (var c in FindObjectsByType<Canvas>())
            if (c.isRootCanvas && c.isActiveAndEnabled && (canvas == null || c.sortingOrder > canvas.sortingOrder))
                canvas = c;
        if (canvas == null)
            yield break;
        bool tablet = DeviceLayout.IsTabletLike();
        var rt = UiKit.Rect("AchievementToast", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(tablet ? 640f : 580f, tablet ? 104f : 96f);
        // Bauhaus toast: white card, black rule, yellow block holding the trophy.
        var img = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, Color.white, Bauhaus.ButtonRule);
        img.raycastTarget = false;
        var block = UiKit.Rect("Block", rt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        block.pivot = new Vector2(0f, 0.5f);
        block.sizeDelta = new Vector2(84f, 0f);
        var bimg = block.gameObject.AddComponent<Image>();
        bimg.sprite = Bauhaus.Square;
        bimg.color = Bauhaus.Yellow;
        bimg.raycastTarget = false;
        var icon = StarGlyph.Add(rt, "Icon", StarGlyph.Trophy, Bauhaus.Ink, new Vector2(52f, 52f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(18f, 0f);
        var t = UiKit.Text(rt, "Text", Vector2.zero, Vector2.one,
            $"<size=70%><color=#D7261E><b>{LocalizationManager.Get("ach.unlocked", "Achievement unlocked")}</b></color></size>\n<b>{LocalizationManager.Get("ach." + def.Id + ".t", def.Title)}</b>",
            UiTypography.Scale(tablet ? 28 : 26), Bauhaus.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        t.rectTransform.offsetMin = new Vector2(102f, 6f);
        t.rectTransform.offsetMax = new Vector2(-16f, -6f);
        FeelDirector.Play(SfxId.Achievement, 0.8f);

        float t0 = 0f;
        const float slide = 0.3f, hold = 2.4f;
        while (t0 < slide * 2f + hold && rt != null)
        {
            t0 += Time.unscaledDeltaTime;
            float y;
            if (t0 < slide) y = Mathf.Lerp(120f, -18f, Mathf.SmoothStep(0f, 1f, t0 / slide));
            else if (t0 < slide + hold) y = -18f;
            else y = Mathf.Lerp(-18f, 120f, Mathf.SmoothStep(0f, 1f, (t0 - slide - hold) / slide));
            rt.anchoredPosition = new Vector2(0f, y);
            rt.SetAsLastSibling();
            yield return null;
        }
        if (rt != null)
            Destroy(rt.gameObject);
    }
}
