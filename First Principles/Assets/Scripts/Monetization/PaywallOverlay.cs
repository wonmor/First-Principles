using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// -----------------------------------------------------------------------------
// PaywallOverlay — "Unlock the full game" sheet (runtime-built uGUI, RuntimeUiPolish style)
// -----------------------------------------------------------------------------
// Opened from Level Select (locked category/level, unlock chip) and when the player
// clears the last free level. Contains Buy (localized store price), Restore Purchases
// (App Review requirement for non-consumables) and Not now. Live-updates from
// Entitlements.Changed; closes itself and invokes onUnlocked once the game is owned.
// Development builds: tap the title 7× to toggle the debug unlock override.
// -----------------------------------------------------------------------------

public static class PaywallOverlay
{
    const string RootName = "PaywallOverlayRoot";

    static GameObject _root;
    static Action _onUnlocked;
    static TextMeshProUGUI _buyTmp;
    static Button _buyBtn;
    static Image _buyImg;
    static TextMeshProUGUI _statusTmp;
    static Button _restoreBtn;
    static bool _busy;
    static int _titleTaps;

    public static bool IsOpen => _root != null;

    /// <param name="onUnlocked">Called once if the player owns the game while the sheet is open (purchase or restore).</param>
    public static void Open(Transform canvasTransform, Action onUnlocked = null)
    {
        if (canvasTransform == null)
            return;
        Close();

        _onUnlocked = onUnlocked;
        _busy = false;
        _titleTaps = 0;
        Entitlements.RetryConnectIfUnavailable();

        bool tablet = DeviceLayout.IsTabletLike();

        _root = new GameObject(RootName);
        var rootRt = _root.AddComponent<RectTransform>();
        rootRt.SetParent(canvasTransform, false);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        rootRt.SetAsLastSibling();

        var dim = _root.AddComponent<Image>();
        dim.color = new Color(0.04f, 0.05f, 0.1f, 0.9f);
        dim.raycastTarget = true;
        var dimBtn = _root.AddComponent<Button>();
        dimBtn.targetGraphic = dim;
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => { if (!_busy) Close(); });

        // Panel ----------------------------------------------------------------
        var panelGo = new GameObject("Panel");
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.SetParent(_root.transform, false);
        // Fractional anchors so the sheet fits both the landscape mobile build and portrait/desktop windows.
        bool landscape = Screen.width >= Screen.height;
        panelRt.anchorMin = landscape ? new Vector2(0.2f, 0.05f) : new Vector2(0.06f, 0.17f);
        panelRt.anchorMax = landscape ? new Vector2(0.8f, 0.95f) : new Vector2(0.94f, 0.83f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = panelGo.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(panelImg);
        panelImg.color = RuntimeUiPolish.PanelMid;
        panelImg.raycastTarget = true;
        // A no-op click handler on the panel stops taps bubbling up to the dim layer's close button.
        var panelSwallow = panelGo.AddComponent<Button>();
        panelSwallow.targetGraphic = panelImg;
        panelSwallow.transition = Selectable.Transition.None;
        RuntimeUiPolish.ApplyDropShadow(panelRt, new Vector2(3f, -5f), 0.34f);

        // Coral accent bar
        var accent = NewRect("Accent", panelRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        accent.pivot = new Vector2(0.5f, 1f);
        accent.sizeDelta = new Vector2(-48f, 8f);
        accent.anchoredPosition = new Vector2(0f, -20f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(accentImg);
        accentImg.color = RuntimeUiPolish.AccentCoral;
        accentImg.raycastTarget = false;

        // Lock icon
        var iconRt = NewRect("Icon", panelRt, new Vector2(0.3f, 0.82f), new Vector2(0.7f, 0.95f));
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.sprite = Entitlements.HasFullGame ? LockGlyph.Unlocked : LockGlyph.Locked;
        icon.preserveAspect = true;
        icon.color = RuntimeUiPolish.AccentCoral;
        icon.raycastTarget = false;

        // Title (7-tap debug toggle in dev builds)
        var title = AddText(panelRt, "Title", new Vector2(0.06f, 0.70f), new Vector2(0.94f, 0.81f),
            LocalizationManager.Get("paywall.title", "Unlock the full game"),
            UiTypography.Scale(tablet ? 44 : 40), RuntimeUiPolish.TitleIvory, FontStyles.Bold);
        if (Application.isEditor || Debug.isDebugBuild)
        {
            title.raycastTarget = true;
            var tapBtn = title.gameObject.AddComponent<Button>();
            tapBtn.transition = Selectable.Transition.None;
            tapBtn.onClick.AddListener(OnTitleTapped);
        }

        int total = LevelManagerPlayableLevelCount();
        string bodyFmt = LocalizationManager.Get("paywall.body",
            "You've got {0} free levels. One purchase opens all {1}: AP Calculus BC & Physics C, aerospace, " +
            "economics, transforms, Big O, the four final bosses — and every level we add later.");
        AddText(panelRt, "Body", new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.70f),
            SafeFormat(bodyFmt, LevelAccessPolicy.FreeLevelCount, total),
            UiTypography.Scale(tablet ? 26 : 23), new Color(0.82f, 0.86f, 0.94f, 1f), FontStyles.Normal);

        AddText(panelRt, "Promise", new Vector2(0.08f, 0.43f), new Vector2(0.92f, 0.50f),
            LocalizationManager.Get("paywall.promise", "One-time purchase · No ads · No subscription"),
            UiTypography.Scale(tablet ? 22 : 20), new Color(0.55f, 0.85f, 0.82f, 1f), FontStyles.Bold);

        // Buy button
        var buyRt = NewRect("BuyButton", panelRt, new Vector2(0.08f, 0.29f), new Vector2(0.92f, 0.40f));
        _buyImg = buyRt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(_buyImg);
        _buyImg.color = RuntimeUiPolish.AccentCoral;
        _buyBtn = buyRt.gameObject.AddComponent<Button>();
        _buyBtn.targetGraphic = _buyImg;
        RuntimeUiPolish.ApplyButtonTransitions(_buyBtn, RuntimeUiPolish.AccentCoral,
            Color.Lerp(RuntimeUiPolish.AccentCoral, Color.white, 0.18f),
            Color.Lerp(RuntimeUiPolish.AccentCoral, Color.black, 0.22f));
        RuntimeUiPolish.ApplyDropShadow(buyRt, new Vector2(2f, -3f), 0.3f);
        _buyTmp = AddText(buyRt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), "",
            UiTypography.Scale(tablet ? 32 : 29), Color.white, FontStyles.Bold);
        _buyBtn.onClick.AddListener(OnBuy);

        // Restore + Not now
        var restoreRt = NewRect("RestoreButton", panelRt, new Vector2(0.08f, 0.17f), new Vector2(0.49f, 0.26f));
        var restoreImg = restoreRt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(restoreImg);
        restoreImg.color = RuntimeUiPolish.ButtonNeutral;
        _restoreBtn = restoreRt.gameObject.AddComponent<Button>();
        _restoreBtn.targetGraphic = restoreImg;
        RuntimeUiPolish.ApplyButtonTransitions(_restoreBtn, RuntimeUiPolish.ButtonNeutral,
            RuntimeUiPolish.ButtonNeutralHover, RuntimeUiPolish.PanelDeep);
        AddText(restoreRt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f),
            LocalizationManager.Get("paywall.restore", "Restore purchases"),
            UiTypography.Scale(tablet ? 24 : 21), RuntimeUiPolish.TitleIvory, FontStyles.Bold);
        _restoreBtn.onClick.AddListener(OnRestore);

        var laterRt = NewRect("NotNowButton", panelRt, new Vector2(0.51f, 0.17f), new Vector2(0.92f, 0.26f));
        var laterImg = laterRt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(laterImg);
        laterImg.color = new Color(0.15f, 0.17f, 0.24f, 0.96f);
        var laterBtn = laterRt.gameObject.AddComponent<Button>();
        laterBtn.targetGraphic = laterImg;
        RuntimeUiPolish.ApplyButtonTransitions(laterBtn, laterImg.color,
            RuntimeUiPolish.ButtonNeutralHover, RuntimeUiPolish.PanelDeep);
        AddText(laterRt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f),
            LocalizationManager.Get("paywall.not_now", "Not now"),
            UiTypography.Scale(tablet ? 24 : 21), RuntimeUiPolish.TitleIvory, FontStyles.Bold);
        laterBtn.onClick.AddListener(() => { if (!_busy) Close(); });

        _statusTmp = AddText(panelRt, "Status", new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.15f), "",
            UiTypography.Scale(tablet ? 21 : 19), new Color(0.75f, 0.8f, 0.92f, 0.9f), FontStyles.Italic);

        Entitlements.Changed += Refresh;
        Entitlements.PurchaseFinished += OnPurchaseFinished;
        LocalizationManager.LanguageChanged += Refresh;
        Refresh();
    }

    public static void Close()
    {
        Entitlements.Changed -= Refresh;
        Entitlements.PurchaseFinished -= OnPurchaseFinished;
        LocalizationManager.LanguageChanged -= Refresh;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _buyTmp = null;
        _buyBtn = null;
        _buyImg = null;
        _statusTmp = null;
        _restoreBtn = null;
        _busy = false;
    }

    // ------------------------------------------------------------------ state

    static void Refresh()
    {
        if (_root == null)
            return;

        if (Entitlements.HasFullGame)
        {
            var cb = _onUnlocked;
            _onUnlocked = null;
            Close();
            cb?.Invoke();
            return;
        }

        string price = Entitlements.LocalizedPrice;
        bool ready = Entitlements.Availability == StoreAvailability.Ready;
        if (_buyTmp != null)
        {
            _buyTmp.text = !string.IsNullOrEmpty(price)
                ? SafeFormat(LocalizationManager.Get("paywall.buy_price", "Unlock all levels · {0}"), price)
                : LocalizationManager.Get("paywall.buy", "Unlock all levels");
            LocalizationManager.ApplyTextDirection(_buyTmp);
        }
        if (_buyBtn != null)
            _buyBtn.interactable = ready && !_busy;
        if (_restoreBtn != null)
            _restoreBtn.interactable = !_busy && Entitlements.Availability != StoreAvailability.NotRequired;

        if (!_busy && _statusTmp != null)
        {
            switch (Entitlements.Availability)
            {
                case StoreAvailability.Connecting:
                    SetStatus(LocalizationManager.Get("paywall.status.connecting", "Connecting to the store…"));
                    break;
                case StoreAvailability.Unavailable:
                    SetStatus(LocalizationManager.Get("paywall.status.unavailable",
                        "The store isn't available right now. Check your connection and try again."));
                    break;
                default:
                    SetStatus("");
                    break;
            }
        }
    }

    static void OnBuy()
    {
        if (_busy || !Entitlements.CanPurchase)
            return;
        _busy = true;
        SetStatus(LocalizationManager.Get("paywall.status.purchasing", "Waiting for the store…"));
        Refresh();
        Entitlements.BuyFullGame();
    }

    static void OnRestore()
    {
        if (_busy)
            return;
        _busy = true;
        SetStatus(LocalizationManager.Get("paywall.status.restoring", "Restoring purchases…"));
        Refresh();
        Entitlements.RestorePurchases((ok, err) =>
        {
            if (_root == null)
                return;
            _busy = false;
            if (Entitlements.HasFullGame)
            {
                Refresh(); // closes + callback
                return;
            }
            Refresh();
            SetStatus(ok
                ? LocalizationManager.Get("paywall.status.nothing_to_restore", "No previous purchase found for this account.")
                : LocalizationManager.Get("paywall.status.restore_failed", "Couldn't restore right now. Please try again."));
        });
    }

    static void OnPurchaseFinished(PurchaseOutcome outcome, string detail)
    {
        if (_root == null)
            return;
        _busy = false;
        if (Entitlements.HasFullGame)
        {
            Refresh();
            return;
        }
        Refresh();
        switch (outcome)
        {
            case PurchaseOutcome.Cancelled:
                SetStatus("");
                break;
            case PurchaseOutcome.Deferred:
                SetStatus(LocalizationManager.Get("paywall.status.deferred",
                    "Purchase is waiting for approval. Levels unlock automatically once it's approved."));
                break;
            case PurchaseOutcome.Failed:
                SetStatus(detail == "already_owned_restore"
                    ? LocalizationManager.Get("paywall.status.already_owned", "You already own this — tap Restore purchases.")
                    : LocalizationManager.Get("paywall.status.failed", "The purchase didn't go through. You haven't been charged."));
                break;
        }
    }

    static void OnTitleTapped()
    {
        if (++_titleTaps < 7)
            return;
        _titleTaps = 0;
        bool unlocked = Entitlements.DebugOverride == true;
        Entitlements.DebugOverride = unlocked ? (bool?)null : true;
        Debug.Log($"Entitlements: debug override {(unlocked ? "cleared" : "→ unlocked")}");
    }

    // ------------------------------------------------------------------ helpers

    static int LevelManagerPlayableLevelCount()
    {
        int n = 0;
        for (int i = 0; i < GameLevelCatalog.LevelCount; i++)
            if (!GameLevelCatalog.IsComingSoonLevel(i))
                n++;
        return n;
    }

    static string SafeFormat(string fmt, params object[] args)
    {
        try { return string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    static void SetStatus(string s)
    {
        if (_statusTmp == null)
            return;
        _statusTmp.text = s ?? "";
        LocalizationManager.ApplyTextDirection(_statusTmp);
    }

    static RectTransform NewRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TextMeshProUGUI AddText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string text, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent, anchorMin, anchorMax);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UiTypography.ApplyDefaultFontAsset(tmp);
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = size;
        tmp.fontSizeMin = Mathf.Max(12f, size * 0.55f);
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        LocalizationManager.ApplyTextDirection(tmp);
        return tmp;
    }
}
