using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// -----------------------------------------------------------------------------
// PaywallOverlay — upgrade sheet (runtime-built uGUI, RuntimeUiPolish style)
// -----------------------------------------------------------------------------
// Plans: First Principles Plus yearly / monthly (auto-renewable) and the one-time Full Game.
// Shows live store prices, Restore Purchases, auto-renew terms, Terms of Use (EULA) and
// Privacy Policy links (App Store guideline 3.1.2 / Google Play subscription policy).
// Players who already own the Full Game only see Plus (their levels stay unlocked forever).
// Opened from Level Select (locked level / unlock chip), after the last free level, and from
// the in-game Hint button when the free daily hints are used up.
// Development builds: tap the title 7× to toggle the debug unlock override.
// -----------------------------------------------------------------------------

public static class PaywallOverlay
{
    public enum Reason
    {
        Levels,
        Hints,
    }

    public const string PrivacyPolicyUrl = "https://github.com/rkaissi/First-Principles/blob/main/privacy-policy.md";
    public const string AppleEulaUrl = "https://www.apple.com/legal/internet-services/itunes/dev/stdeula/";
    public const string GooglePlayTermsUrl = "https://play.google.com/about/play-terms/";

    const string RootName = "PaywallOverlayRoot";

    static GameObject _root;
    static Action _onUnlocked;
    static Reason _reason;
    static TextMeshProUGUI _statusTmp;
    static Button _restoreBtn;
    static PlanRow[] _plans = new PlanRow[0];
    static bool _busy;
    static int _titleTaps;

    sealed class PlanRow
    {
        public string ProductId;
        public Button Button;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Detail;
    }

    public static bool IsOpen => _root != null;

    /// <param name="onUnlocked">Called once if the player gains what this sheet sells while it is open.</param>
    public static void Open(Transform canvasTransform, Action onUnlocked = null, Reason reason = Reason.Levels)
    {
        if (canvasTransform == null)
            return;
        Close();

        _onUnlocked = onUnlocked;
        _reason = reason;
        _busy = false;
        _titleTaps = 0;
        Entitlements.RetryConnectIfUnavailable();

        bool tablet = DeviceLayout.IsTabletLike();
        bool landscape = Screen.width >= Screen.height;

        var rootRt = UiKit.FullScreenDim(RootName, canvasTransform, 0.9f, () => { if (!_busy) Close(); });
        _root = rootRt.gameObject;

        var panel = UiKit.Panel(rootRt,
            landscape ? new Vector2(0.18f, 0.03f) : new Vector2(0.04f, 0.08f),
            landscape ? new Vector2(0.82f, 0.97f) : new Vector2(0.96f, 0.92f));

        var accent = UiKit.Rect("Accent", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
        accent.pivot = new Vector2(0.5f, 1f);
        accent.sizeDelta = new Vector2(-48f, 8f);
        accent.anchoredPosition = new Vector2(0f, -18f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(accentImg);
        accentImg.color = UiKit.PlusViolet;
        accentImg.raycastTarget = false;

        bool ownsFull = Entitlements.OwnsFullGameOutright;
        string titleText = reason == Reason.Hints
            ? LocalizationManager.Get("paywall.hints.title", "Out of hints for today")
            : LocalizationManager.Get("paywall.plus.title", "Go further with First Principles");
        var title = UiKit.Text(panel, "Title", new Vector2(0.05f, 0.875f), new Vector2(0.95f, 0.955f), titleText,
            UiTypography.Scale(tablet ? 40 : 36), RuntimeUiPolish.TitleIvory, FontStyles.Bold);
        if (Application.isEditor || Debug.isDebugBuild)
        {
            title.raycastTarget = true;
            var tapBtn = title.gameObject.AddComponent<Button>();
            tapBtn.transition = Selectable.Transition.None;
            tapBtn.onClick.AddListener(OnTitleTapped);
        }

        int total = PlayableLevelCount();
        string body;
        if (reason == Reason.Hints)
            body = string.Format(LocalizationManager.Get("paywall.hints.body",
                "You get {0} free hints every day; they refill tomorrow. Plus gives you unlimited hints, every level and a daily-streak shield."),
                ProgressRules.FreeHintsPerDay);
        else if (ownsFull)
            body = LocalizationManager.Get("paywall.owner.body",
                "You own the Full Game, so every level stays yours forever. Plus adds unlimited hints and a shield that saves your daily streak once a week.");
        else
            body = string.Format(LocalizationManager.Get("paywall.plus.body",
                "You've got {0} free levels, the daily challenge and {1} hints a day. Unlock all {2} levels: AP Calculus BC & Physics C, aerospace, economics, transforms, Big O and the four final bosses."),
                LevelAccessPolicy.FreeLevelCount, ProgressRules.FreeHintsPerDay, total);
        UiKit.Text(panel, "Body", new Vector2(0.07f, 0.74f), new Vector2(0.93f, 0.875f), body,
            UiTypography.Scale(tablet ? 22 : 20), new Color(0.82f, 0.86f, 0.94f, 1f));

        // Plans ------------------------------------------------------------------
        var rows = new System.Collections.Generic.List<PlanRow>();
        float top = 0.725f;
        float h = ownsFull ? 0.135f : 0.115f;
        float gap = 0.012f;
        rows.Add(AddPlan(panel, LevelAccessPolicy.PlusYearlyProductId, top, h, UiKit.PlusViolet, tablet, true));
        top -= h + gap;
        rows.Add(AddPlan(panel, LevelAccessPolicy.PlusMonthlyProductId, top, h, new Color(0.3f, 0.27f, 0.55f, 0.98f), tablet, false));
        top -= h + gap;
        if (!ownsFull)
        {
            rows.Add(AddPlan(panel, LevelAccessPolicy.FullGameProductId, top, h, RuntimeUiPolish.AccentCoral, tablet, false));
            top -= h + gap;
        }
        _plans = rows.ToArray();

        // Restore + Not now -------------------------------------------------------
        float by1 = top - 0.075f;
        var restoreBtn = UiKit.Button(panel, "RestoreButton", new Vector2(0.07f, by1), new Vector2(0.49f, top - 0.005f),
            LocalizationManager.Get("paywall.restore", "Restore purchases"), RuntimeUiPolish.ButtonNeutral, OnRestore, tablet ? 22 : 20);
        _restoreBtn = restoreBtn;
        var laterBtn = UiKit.Button(panel, "NotNowButton", new Vector2(0.51f, by1), new Vector2(0.93f, top - 0.005f),
            LocalizationManager.Get("paywall.not_now", "Not now"), new Color(0.15f, 0.17f, 0.24f, 0.96f),
            () => { if (!_busy) Close(); }, tablet ? 22 : 20);
        OverlayCancelHandler.Attach(_root, laterBtn, _plans.Length > 0 ? _plans[0].Button : laterBtn);

        // Status + legal ----------------------------------------------------------
        _statusTmp = UiKit.Text(panel, "Status", new Vector2(0.06f, by1 - 0.055f), new Vector2(0.94f, by1 - 0.005f), "",
            UiTypography.Scale(tablet ? 19 : 17), new Color(0.75f, 0.8f, 0.92f, 0.9f), FontStyles.Italic);

        bool apple = Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.OSXPlayer
                     || Application.platform == RuntimePlatform.VisionOS || Application.platform == RuntimePlatform.tvOS
                     || Application.platform == RuntimePlatform.OSXEditor;
        string account = apple ? "Apple Account" : "Google Play account";
        string legal = string.Format(LocalizationManager.Get("paywall.legal",
            "Plus is an auto-renewing subscription. Payment is charged to your {0} at confirmation. It renews automatically at the same price for the same period unless cancelled at least 24 hours before the end of the current period; manage or cancel any time in your account settings. The Full Game is a one-time purchase."),
            account);
        UiKit.Text(panel, "Legal", new Vector2(0.06f, 0.075f), new Vector2(0.94f, by1 - 0.06f), legal,
            UiTypography.Scale(tablet ? 15 : 13), new Color(0.62f, 0.67f, 0.78f, 0.95f));

        UiKit.Button(panel, "TermsLink", new Vector2(0.07f, 0.018f), new Vector2(0.49f, 0.068f),
            LocalizationManager.Get("paywall.terms", "Terms of Use (EULA)"), new Color(0.12f, 0.13f, 0.19f, 0.9f),
            () => UiKit.OpenUrl(apple ? AppleEulaUrl : GooglePlayTermsUrl), tablet ? 17 : 15, new Color(0.7f, 0.82f, 1f, 1f));
        UiKit.Button(panel, "PrivacyLink", new Vector2(0.51f, 0.018f), new Vector2(0.93f, 0.068f),
            LocalizationManager.Get("paywall.privacy", "Privacy Policy"), new Color(0.12f, 0.13f, 0.19f, 0.9f),
            () => UiKit.OpenUrl(PrivacyPolicyUrl), tablet ? 17 : 15, new Color(0.7f, 0.82f, 1f, 1f));

        Entitlements.Changed += Refresh;
        Entitlements.PurchaseFinished += OnPurchaseFinished;
        LocalizationManager.LanguageChanged += Refresh;
        Refresh();
    }

    static PlanRow AddPlan(RectTransform panel, string productId, float top, float h, Color bg, bool tablet, bool bestValue)
    {
        var row = new PlanRow { ProductId = productId };
        var btn = UiKit.Button(panel, "Plan_" + productId, new Vector2(0.07f, top - h), new Vector2(0.93f, top), "", bg,
            () => OnBuy(productId), tablet ? 26 : 24, Color.white);
        // Replace the default centered label with a title + detail pair.
        Object.Destroy(btn.transform.Find("Text").gameObject);
        row.Button = btn;
        row.Title = UiKit.Text(btn.transform, "PlanTitle", new Vector2(0.04f, 0.5f), new Vector2(0.96f, 0.95f), "",
            UiTypography.Scale(tablet ? 26 : 24), Color.white, FontStyles.Bold);
        row.Detail = UiKit.Text(btn.transform, "PlanDetail", new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.5f), "",
            UiTypography.Scale(tablet ? 18 : 16), new Color(1f, 1f, 1f, 0.86f));
        if (bestValue)
        {
            var tag = UiKit.Rect("BestValue", btn.transform, new Vector2(1f, 1f), new Vector2(1f, 1f));
            tag.pivot = new Vector2(1f, 0.5f);
            tag.sizeDelta = new Vector2(tablet ? 190f : 170f, 34f);
            tag.anchoredPosition = new Vector2(-14f, 0f);
            var timg = tag.gameObject.AddComponent<Image>();
            RuntimeUiPolish.UseRoundedSliced(timg);
            timg.color = UiKit.Gold;
            timg.raycastTarget = false;
            UiKit.Text(tag, "T", new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f),
                LocalizationManager.Get("paywall.best_value", "Best value"), UiTypography.Scale(16), new Color(0.2f, 0.14f, 0.02f, 1f), FontStyles.Bold);
        }
        return row;
    }

    public static void Close()
    {
        Entitlements.Changed -= Refresh;
        Entitlements.PurchaseFinished -= OnPurchaseFinished;
        LocalizationManager.LanguageChanged -= Refresh;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _statusTmp = null;
        _restoreBtn = null;
        _plans = new PlanRow[0];
        _busy = false;
    }

    // ------------------------------------------------------------------ state

    static bool Satisfied() => _reason == Reason.Hints ? Entitlements.HasPlus : Entitlements.HasFullGame;

    static void Refresh()
    {
        if (_root == null)
            return;

        if (Satisfied())
        {
            var cb = _onUnlocked;
            _onUnlocked = null;
            Close();
            FeelDirector.Play(SfxId.Achievement, 0.8f);
            cb?.Invoke();
            return;
        }

        bool ready = Entitlements.Availability == StoreAvailability.Ready;
        foreach (var p in _plans)
        {
            string price = Entitlements.PriceFor(p.ProductId);
            bool has = !string.IsNullOrEmpty(price);
            switch (p.ProductId)
            {
                case LevelAccessPolicy.PlusYearlyProductId:
                    p.Title.text = has
                        ? string.Format(LocalizationManager.Get("paywall.plan.yearly", "Plus yearly   {0} / year"), price)
                        : LocalizationManager.Get("paywall.plan.yearly_np", "Plus yearly");
                    p.Detail.text = LocalizationManager.Get("paywall.plan.plus_detail",
                        "Every level and new chapter, unlimited hints, daily-streak shield. Auto-renews.");
                    break;
                case LevelAccessPolicy.PlusMonthlyProductId:
                    p.Title.text = has
                        ? string.Format(LocalizationManager.Get("paywall.plan.monthly", "Plus monthly   {0} / month"), price)
                        : LocalizationManager.Get("paywall.plan.monthly_np", "Plus monthly");
                    p.Detail.text = LocalizationManager.Get("paywall.plan.plus_detail_m",
                        "Everything in Plus, billed monthly. Auto-renews; cancel any time.");
                    break;
                default:
                    p.Title.text = has
                        ? string.Format(LocalizationManager.Get("paywall.plan.full", "Full Game   {0} once"), price)
                        : LocalizationManager.Get("paywall.plan.full_np", "Full Game (one-time)");
                    p.Detail.text = LocalizationManager.Get("paywall.plan.full_detail",
                        "All levels forever, including chapters we add later. No subscription.");
                    break;
            }
            LocalizationManager.ApplyTextDirection(p.Title);
            LocalizationManager.ApplyTextDirection(p.Detail);
            p.Button.interactable = ready && !_busy && Entitlements.IsProductAvailable(p.ProductId);
        }
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

    static void OnBuy(string productId)
    {
        if (_busy || Entitlements.Availability != StoreAvailability.Ready)
            return;
        _busy = true;
        SetStatus(LocalizationManager.Get("paywall.status.purchasing", "Waiting for the store…"));
        Refresh();
        Entitlements.Buy(productId);
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
            if (Satisfied())
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
        if (Satisfied())
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
                    "Purchase is waiting for approval. It unlocks automatically once it's approved."));
                break;
            case PurchaseOutcome.Failed:
                SetStatus(detail == "already_owned_restore"
                    ? LocalizationManager.Get("paywall.status.already_owned", "You already own this. Tap Restore purchases.")
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
        Debug.Log($"Entitlements: debug override {(unlocked ? "cleared" : "-> unlocked")}");
    }

    // ------------------------------------------------------------------ helpers

    static int PlayableLevelCount()
    {
        int n = 0;
        for (int i = 0; i < GameLevelCatalog.LevelCount; i++)
            if (!GameLevelCatalog.IsComingSoonLevel(i))
                n++;
        return n;
    }

    static void SetStatus(string s)
    {
        if (_statusTmp == null)
            return;
        _statusTmp.text = s ?? "";
        LocalizationManager.ApplyTextDirection(_statusTmp);
    }
}
