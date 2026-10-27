using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Central place for **screen-dependent layout policy** used by HUD, level select, touch bar, CanvasScaler tuning.
/// <list type="bullet">
/// <item><description><see cref="PreferOnScreenGameControls"/> — use full-screen touch zones + guide (no virtual button strip).</description></item>
/// <item><description><see cref="IsTabletLike"/> — dp-based; avoids treating phones as tablets.</description></item>
/// <item><description><see cref="ApplySafeAreaAnchors"/> — normalized <see cref="Screen.safeArea"/> → RectTransform anchors.</description></item>
/// </list>
/// </summary>
public static class DeviceLayout
{
    private static Rect _lastSafeArea;
    private static Vector2 _lastScreen;

    // --- QA screenshots only ---------------------------------------------------------------
    // Development desktop builds launched with "-fpDevice phone|tablet" lay the UI out as on a
    // touch device (on-screen controls, tablet sizing, notch safe area) so batch screenshots
    // match iPhone / iPad. Release builds ignore the flag.
    private static int _qaDevice = -1; // 0 = off, 1 = phone, 2 = tablet

    public static int QaDevice
    {
        get
        {
            if (_qaDevice >= 0)
                return _qaDevice;
            _qaDevice = 0;
            if (!Debug.isDebugBuild || Application.isEditor)
                return _qaDevice;
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-fpDevice");
            if (i >= 0 && i + 1 < a.Length)
                _qaDevice = a[i + 1] == "tablet" ? 2 : a[i + 1] == "phone" ? 1 : 0;
            return _qaDevice;
        }
    }

    /// <summary><see cref="Screen.safeArea"/>, or a simulated landscape-iPhone notch inset under <c>-fpDevice phone</c>.</summary>
    public static Rect SafeArea
    {
        get
        {
            if (QaDevice != 1)
                return Screen.safeArea;
            // iPhone 16 Pro Max landscape: 62 pt each side, 21 pt home indicator (956 x 440 pt).
            float w = Screen.width, h = Screen.height;
            float side = w * (62f / 956f), bottom = h * (21f / 440f);
            return new Rect(side, bottom, w - side * 2f, h - bottom);
        }
    }

    /// <summary>True when we show on-screen move/jump controls in the Game scene.</summary>
    public static bool PreferOnScreenGameControls =>
        QaDevice > 0 ||
        Application.isMobilePlatform ||
        SystemInfo.deviceType == DeviceType.Handheld ||
        HasTouchscreenDevice();

    private static bool HasTouchscreenDevice()
    {
#if ENABLE_INPUT_SYSTEM
        foreach (var d in InputSystem.devices)
        {
            if (d is Touchscreen)
                return true;
        }
#endif
        return false;
    }

    /// <summary>
    /// iPad-class devices: shortest side in approximate dp (avoids mislabeling phones by pixel count alone).
    /// </summary>
    public static bool IsTabletLike()
    {
        if (!PreferOnScreenGameControls)
            return false;
        if (QaDevice > 0)
            return QaDevice == 2;

        float dpi = Screen.dpi > 1f ? Screen.dpi : 163f;
        float minPx = Mathf.Min(Screen.width, Screen.height);
        float minDp = minPx / dpi * 160f;
        return minDp >= 592f;
    }

    /// <summary>CanvasScaler <c>matchWidthOrHeight</c> tuned for phone vs ~4:3 tablet / split view.</summary>
    public static float RecommendedCanvasMatchWidthOrHeight()
    {
        float w = Screen.width;
        float h = Screen.height;
        float min = Mathf.Min(w, h);
        float max = Mathf.Max(w, h);
        float aspect = max / Mathf.Max(1f, min);

        if (IsTabletLike())
        {
            if (aspect < 1.42f)
                return 0.52f;
            if (aspect > 1.75f)
                return 0.48f;
            return 0.5f;
        }

        if (aspect > 1.85f)
            return 0.42f;
        if (aspect < 1.55f)
            return 0.48f;
        return 0.45f;
    }

    public static float TouchControlBarHeight => IsTabletLike() ? 188f : 168f;

    /// <summary>True in Game when we use left/right screen halves + second-finger jump (no ◀ ▶ Jump strip).</summary>
    public static bool GameplayUsesFullScreenTouchZones => PreferOnScreenGameControls;

    /// <summary>Bottom inset for game UI anchored above the safe area (no on-screen controls strip).</summary>
    public static float TouchHintVerticalOffset =>
        !PreferOnScreenGameControls
            ? 22f
            : GameplayUsesFullScreenTouchZones
                ? (IsTabletLike() ? 28f : 18f)
                : (IsTabletLike() ? 212f : 188f);

    // Leave vertical room for bottom “Math tips” / “How to play” band + back chip.
    public static Vector2 LevelSelectScrollAnchorMin =>
        IsTabletLike() ? new Vector2(0.1f, 0.32f) : new Vector2(0.04f, 0.34f);

    // Chips moved to bottom — scroll extends higher (below title).
    public static Vector2 LevelSelectScrollAnchorMax =>
        IsTabletLike() ? new Vector2(0.9f, 0.84f) : new Vector2(0.96f, 0.82f);

    public static float LevelSelectScrollSensitivity => IsTabletLike() ? 44f : 28f;

    /// <summary>Apply normalized Screen.safeArea to a full-bleed RectTransform (typical content root).</summary>
    public static void ApplySafeAreaAnchors(RectTransform rt)
    {
        if (rt == null)
            return;

        Rect sa = SafeArea;
        float w = Mathf.Max(1f, Screen.width);
        float h = Mathf.Max(1f, Screen.height);

        Vector2 min = new Vector2(sa.xMin / w, sa.yMin / h);
        Vector2 max = new Vector2(sa.xMax / w, sa.yMax / h);

        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static bool SafeAreaOrScreenChanged()
    {
        Rect sa = SafeArea;
        Vector2 s = new Vector2(Screen.width, Screen.height);
        if (sa != _lastSafeArea || s != _lastScreen)
        {
            _lastSafeArea = sa;
            _lastScreen = s;
            return true;
        }

        return false;
    }
}
