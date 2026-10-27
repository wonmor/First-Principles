using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// SceneFader — full-screen Image alpha fade between Menu / LevelSelect / Game
// -----------------------------------------------------------------------------
// OnEnable picks white vs black overlay by scene name, then fades Out (opaque→clear).
// Public API loads scenes after fading In (clear→opaque) via coroutines. Assign both
// Images in the inspector on Menu (or shared prefab).
// -----------------------------------------------------------------------------

public class SceneFader : MonoBehaviour
{

	#region FIELDS
	/// <summary>Typically used on Menu (lighter fade).</summary>
	public Image fadeOutUIImage1; // White background
	/// <summary>Typically used on Game / LevelSelect (darker fade).</summary>
	public Image fadeOutUIImage2; // Black background

	private Image fadeOutUIImage;

	private TextMeshProUGUI graphicCalculatorMenuButtonText;
	private TextMeshProUGUI menuLanguageButtonLabel;
	private Image playerIconMenuButtonImage; // kept for the picker refresh hook
	private TextMeshProUGUI playerIconMenuGlyphTmp;

	public float fadeSpeed = 0.8f;

	public enum FadeDirection
	{
		In, //Alpha = 1
		Out // Alpha = 0
	}
	#endregion

	#region MONOBEHAVIOR
	void OnEnable()
	{
        LocalizationManager.LanguageChanged += RefreshMenuLocalizedControls;

		Scene currentScene = SceneManager.GetActiveScene();

		if (currentScene.name == "Menu")
        {
			fadeOutUIImage = fadeOutUIImage1;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage.enabled = true;
			fadeOutUIImage.raycastTarget = true;
			fadeOutUIImage2.gameObject.SetActive(false);

            PlayerGlyphSettings.GlyphChanged += RefreshPlayerIconMenuButton;

			ZoomIn();
			StartCoroutine(SpawnMenuExtrasNextFrame());

			// FindObjectOfType<AudioManager>().PlayMusic("RainMusic");
		}
		else if (currentScene.name == "Game")
		{
			fadeOutUIImage = fadeOutUIImage2;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage.enabled = true;
			fadeOutUIImage.raycastTarget = true;
			fadeOutUIImage1.gameObject.SetActive(false);

			// FindObjectOfType<AudioManager>().PlayMusic("ClassicalMusic");
		}
		else if (currentScene.name == "LevelSelect")
		{
			fadeOutUIImage = fadeOutUIImage2;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage.enabled = true;
			fadeOutUIImage.raycastTarget = true;
			fadeOutUIImage1.gameObject.SetActive(false);
		}

		StartCoroutine(Fade(FadeDirection.Out));
	}

    void OnDisable()
    {
        LocalizationManager.LanguageChanged -= RefreshMenuLocalizedControls;
        PlayerGlyphSettings.GlyphChanged -= RefreshPlayerIconMenuButton;
    }
	#endregion

	#region FADE
	private IEnumerator Fade(FadeDirection fadeDirection)
	{
		float alpha = (fadeDirection == FadeDirection.Out) ? 1 : 0;
		float fadeEndValue = (fadeDirection == FadeDirection.Out) ? 0 : 1;
		if (fadeDirection == FadeDirection.Out)
		{
			while (alpha >= fadeEndValue)
			{
				SetColorImage(ref alpha, fadeDirection);
				yield return null;
			}
			fadeOutUIImage.enabled = false;
			// Full-screen fade must not eat clicks when invisible (alpha 0 + raycast off).
			fadeOutUIImage.raycastTarget = false;
		}
		else
		{
			fadeOutUIImage.enabled = true;
			fadeOutUIImage.raycastTarget = true;
			while (alpha <= fadeEndValue)
			{
				SetColorImage(ref alpha, fadeDirection);
				yield return null;
			}
		}
	}
	#endregion

	#region HELPERS
	public IEnumerator FadeAndLoadScene(FadeDirection fadeDirection, string sceneToLoad)
	{
		yield return Fade(fadeDirection);
		AttachLoadingLabelToFadeOverlay();
		yield return AsyncSceneLoader.LoadCoroutine(sceneToLoad);
	}

    private void SetColorImage(ref float alpha, FadeDirection fadeDirection)
    {
        fadeOutUIImage.color = new Color(fadeOutUIImage.color.r, fadeOutUIImage.color.g, fadeOutUIImage.color.b, alpha);
        alpha += Time.deltaTime * (1.0f / fadeSpeed) * ((fadeDirection == FadeDirection.Out) ? -1 : 1);
    }

    /// <summary>Shown while <see cref="AsyncSceneLoader"/> finishes activating the next scene.</summary>
    private void AttachLoadingLabelToFadeOverlay()
    {
        if (fadeOutUIImage == null)
            return;
        if (fadeOutUIImage.transform.Find("LoadingLabel") != null)
            return;

        var go = new GameObject("LoadingLabel");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(fadeOutUIImage.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0f, 80f);
        rt.offsetMax = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = LocalizationManager.Get("ui.loading", "Loading…");
        tmp.fontSize = UiTypography.Scale(36);
        tmp.alignment = TextAlignmentOptions.Bottom;
        tmp.color = Bauhaus.Ink;
        tmp.fontStyle = FontStyles.Bold;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        UiTypography.ApplyDefaultFontAsset(tmp);
        LocalizationManager.ApplyTextDirection(tmp);
    }
    #endregion

    private void ZoomIn() { }

    private IEnumerator SpawnMenuExtrasNextFrame()
    {
        yield return null;
        BuildBauhausMenu();
        RefreshMenuLocalizedControls();
    }

    private TextMeshProUGUI menuTitleTmp;
    private TextMeshProUGUI menuTaglineTmp;
    private TextMeshProUGUI menuPlayTmp;

    private void RefreshMenuLocalizedControls()
    {
        if (graphicCalculatorMenuButtonText != null)
        {
            graphicCalculatorMenuButtonText.text = LocalizationManager.Get("ui.graphing_calculator_mode", "Graphing\ncalculator");
            LocalizationManager.ApplyTextDirection(graphicCalculatorMenuButtonText);
        }

        if (menuLanguageButtonLabel != null)
        {
            menuLanguageButtonLabel.text = LocalizationManager.GetLanguageChipDisplayText();
            LocalizationManager.ApplyLanguagePickerTextDirection(menuLanguageButtonLabel);
        }

        if (menuTaglineTmp != null)
        {
            menuTaglineTmp.text = LocalizationManager.Get("menu.tagline", "A calculus platformer");
            LocalizationManager.ApplyTextDirection(menuTaglineTmp);
        }

        if (menuPlayTmp != null)
        {
            menuPlayTmp.text = LocalizationManager.Get("menu.play", "Play");
            LocalizationManager.ApplyTextDirection(menuPlayTmp);
        }

        RefreshPlayerIconMenuButton();

        if (string.Equals(SceneManager.GetActiveScene().name, "Menu", System.StringComparison.Ordinal))
        {
            var creditsGo = GameObject.Find("MenuCreditsBlock");
            var creditsTmp = creditsGo != null ? creditsGo.GetComponent<TextMeshProUGUI>() : null;
            if (creditsTmp != null)
            {
                creditsTmp.text = SceneCreditsFooter.BuildMenuFooterCompact();
                LocalizationManager.ApplyTextDirection(creditsTmp);
            }
        }
    }

    // ------------------------------------------------------------------ Bauhaus main menu (1.4)
    // Paper page on a two-column grid. Left: a yellow panel holding the lim h→0 logo in a black-ruled
    // frame. Right: the title set large with a thick black rule, then the three actions as flat primary
    // blocks (Play red, Graphing calculator blue, Player icon paper), credits at the foot. Language chip
    // top-right. Portrait windows stack the two columns.

    private void BuildBauhausMenu()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Menu", System.StringComparison.Ordinal))
            return;
        if (GameObject.Find("BauhausMenu") != null)
            return;
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var play = GameObject.FindGameObjectWithTag("PlayButton");
        if (canvas == null || play == null)
            return;

        var safe = MobileUiRoots.GetSafeContentParent(canvas.transform);
        bool portrait = Screen.height > Screen.width;
        bool tablet = DeviceLayout.IsTabletLike();

        // Full-bleed paper behind everything (the safe area insets stay paper too).
        var page = UiKit.Rect("BauhausPage", canvas.transform, Vector2.zero, Vector2.one);
        page.SetAsFirstSibling();
        var pageImg = page.gameObject.AddComponent<Image>();
        pageImg.color = Bauhaus.Paper;
        pageImg.raycastTarget = false;

        var root = UiKit.Rect("BauhausMenu", safe, Vector2.zero, Vector2.one);
        root.SetAsLastSibling();

        // Left / top: yellow panel with the logo.
        var logoPanel = portrait
            ? UiKit.Rect("LogoPanel", root, new Vector2(0.06f, 0.56f), new Vector2(0.94f, 0.95f))
            : UiKit.Rect("LogoPanel", root, new Vector2(0.035f, 0.07f), new Vector2(0.42f, 0.93f));
        var lp = logoPanel.gameObject.AddComponent<Image>();
        Bauhaus.Panel(lp, Bauhaus.Yellow, Bauhaus.PanelRule);
        lp.raycastTarget = false;
        PlaceMenuLogo(logoPanel);

        // Right / bottom column.
        var col = portrait
            ? UiKit.Rect("Column", root, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.53f))
            : UiKit.Rect("Column", root, new Vector2(0.47f, 0.07f), new Vector2(0.965f, 0.93f));

        menuTitleTmp = UiKit.Text(col, "Title", new Vector2(0f, 0.82f), new Vector2(1f, 1f), "First Principles",
            UiTypography.Scale(tablet ? 92 : 84), Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.BottomLeft);
        menuTitleTmp.textWrappingMode = TextWrappingModes.NoWrap;
        menuTitleTmp.characterSpacing = -2f;
        menuTitleTmp.fontSizeMin = 28f;
        menuTaglineTmp = UiKit.Text(col, "Tagline", new Vector2(0f, 0.745f), new Vector2(1f, 0.82f),
            LocalizationManager.Get("menu.tagline", "A calculus platformer"),
            UiTypography.Scale(tablet ? 30 : 28), Bauhaus.InkSoft, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        var rule = UiKit.Rect("Rule", col, new Vector2(0f, 0.715f), new Vector2(1f, 0.715f));
        rule.sizeDelta = new Vector2(0f, 8f);
        var ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.color = Bauhaus.Ink;
        ruleImg.raycastTarget = false;

        // Play: the scene button keeps its onClick, re-skinned as a red block with a label + arrow glyph.
        var playRt = play.GetComponent<RectTransform>();
        playRt.SetParent(col, false);
        playRt.anchorMin = new Vector2(0f, 0.48f);
        playRt.anchorMax = new Vector2(1f, 0.675f);
        playRt.pivot = new Vector2(0.5f, 0.5f);
        playRt.offsetMin = Vector2.zero;
        playRt.offsetMax = Vector2.zero;
        playRt.localScale = Vector3.one;
        foreach (Transform c in playRt)
            c.gameObject.SetActive(false);
        menuPlayTmp = UiKit.Text(playRt, "Label", new Vector2(0.06f, 0.08f), new Vector2(0.8f, 0.92f),
            LocalizationManager.Get("menu.play", "Play"), UiTypography.Scale(tablet ? 54 : 50), Bauhaus.Card,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var arrow = StarGlyph.Add(playRt, "Glyph", StarGlyph.ArrowRight, Bauhaus.Card, new Vector2(64f, 64f));
        arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = new Vector2(-28f, 0f);
        var playBtn = play.GetComponent<Button>();
        playBtn.transition = Selectable.Transition.ColorTint;
        Bauhaus.Button(playBtn, Bauhaus.Tone.Red, Bauhaus.PanelRule);

        // Second row: graphing calculator (blue) + player icon (paper).
        var calc = MenuBlockButton(col, "GraphicCalculatorEntryButton", new Vector2(0f, 0.255f), new Vector2(0.49f, 0.45f),
            Bauhaus.Tone.Blue, StarGlyph.Calculator, out graphicCalculatorMenuButtonText, LoadGraphCalculator, tablet);
        graphicCalculatorMenuButtonText.text = LocalizationManager.Get("ui.graphing_calculator_mode", "Graphing\ncalculator");
        var iconBtn = MenuBlockButton(col, "MenuPlayerIconButton", new Vector2(0.51f, 0.255f), new Vector2(1f, 0.45f),
            Bauhaus.Tone.Paper, null, out playerIconMenuGlyphTmp, OpenMenuPlayerGlyphPickerOverlay, tablet);
        playerIconMenuButtonImage = iconBtn.GetComponent<Image>();

        // Credits at the foot of the column.
        var credits = GameObject.Find("MenuCreditsBlock");
        if (credits != null)
        {
            var crt = credits.GetComponent<RectTransform>();
            crt.SetParent(col, false);
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0.215f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var ctmp = credits.GetComponent<TextMeshProUGUI>();
            if (ctmp != null)
            {
                Bauhaus.Label(ctmp, Bauhaus.Ink);
                ctmp.enableAutoSizing = true;
                ctmp.fontSizeMax = UiTypography.Scale(22);
                ctmp.fontSizeMin = 14f;
                ctmp.alignment = TextAlignmentOptions.BottomLeft;
                ctmp.textWrappingMode = TextWrappingModes.Normal;
                ctmp.text = SceneCreditsFooter.BuildMenuFooterCompact();
            }
        }

        // Language chip, top-right of the page.
        TrySpawnLanguagePicker();

        // The old centred container (logo bevels, offsets) is no longer used for layout.
        var container = GameObject.Find("MainUIContainer");
        if (container != null)
            container.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
    }

    /// <summary>Re-homes the scene logo (video in a frame) into the panel: black-ruled frame, no grey bevel or offset shadow.</summary>
    private static void PlaceMenuLogo(RectTransform panel)
    {
        var logo = GameObject.Find("Logo");
        if (logo == null)
            return;
        var rt = logo.GetComponent<RectTransform>();
        rt.SetParent(panel, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        float side = Mathf.Max(200f, Mathf.Min(panel.rect.width, panel.rect.height) * 0.82f);
        if (side < 1f || float.IsNaN(side))
            side = 400f;
        rt.localScale = Vector3.one * (side / 400f);
        BauhausSceneSkin.SkinLogoFrame(logo.transform);
    }

    private Button MenuBlockButton(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Bauhaus.Tone tone,
        Sprite glyph, out TextMeshProUGUI label, UnityEngine.Events.UnityAction onClick, bool tablet)
    {
        var existing = GameObject.Find(name);
        if (existing != null)
            Destroy(existing);
        var rt = UiKit.Rect(name, parent, aMin, aMax);
        var img = rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => FeelDirector.Play(SfxId.Click, 0.6f));
        btn.onClick.AddListener(onClick);
        float pad = glyph != null ? 96f : 20f;
        label = UiKit.Text(rt, "Text", new Vector2(0f, 0f), new Vector2(1f, 1f), "",
            UiTypography.Scale(tablet ? 30 : 27), Bauhaus.OnFill(tone), FontStyles.Bold,
            glyph != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
        label.rectTransform.offsetMin = new Vector2(pad, 10f);
        label.rectTransform.offsetMax = new Vector2(-16f, -10f);
        if (glyph != null)
        {
            var g = StarGlyph.Add(rt, "Glyph", glyph, Bauhaus.OnFill(tone), new Vector2(56f, 56f));
            g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            g.rectTransform.pivot = new Vector2(0f, 0.5f);
            g.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        }
        Bauhaus.Button(btn, tone, Bauhaus.PanelRule);
        return btn;
    }

    private void RefreshPlayerIconMenuButton()
    {
        if (playerIconMenuGlyphTmp != null)
        {
            string g = PlayerGlyphSettings.GetSelectedGlyph();
            string setLabel = LocalizationManager.Get("menu.player_icon_set", "Player icon");
            playerIconMenuGlyphTmp.text = $"<size=170%><b>{g}</b></size>\n<size=86%><b>{setLabel}</b></size>";
            playerIconMenuGlyphTmp.lineSpacing = -18f;
            LocalizationManager.ApplyTextDirection(playerIconMenuGlyphTmp);
        }
    }

    private static void OpenMenuPlayerGlyphPickerOverlay()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;
        MenuPlayerGlyphPickerOverlay.Open(canvas.transform);
    }

    /// <summary>Language cycle chip: paper block, globe glyph, top-right of the safe area.</summary>
    private void TrySpawnLanguagePicker()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Menu", System.StringComparison.Ordinal))
            return;
        if (GameObject.Find("MenuLanguagePicker") != null)
            return;

        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        var parentRt = MobileUiRoots.GetSafeContentParent(canvas.transform) as RectTransform ?? canvas.transform as RectTransform;
        if (parentRt == null)
            return;

        bool tablet = DeviceLayout.IsTabletLike();
        var go = new GameObject("MenuLanguagePicker");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parentRt, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(tablet ? -22f : -18f, tablet ? -20f : -16f);
        rt.sizeDelta = new Vector2(tablet ? 400f : 370f, tablet ? 68f : 60f);

        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(LocalizationManager.CycleNext);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(58f, 6f);
        trt.offsetMax = new Vector2(-14f, -6f);

        menuLanguageButtonLabel = textGo.AddComponent<TextMeshProUGUI>();
        UiTypography.ApplyDefaultFontAsset(menuLanguageButtonLabel);
        menuLanguageButtonLabel.enableAutoSizing = true;
        menuLanguageButtonLabel.fontSizeMin = Mathf.Max(14, UiTypography.Scale(14));
        menuLanguageButtonLabel.fontSizeMax = UiTypography.Scale(tablet ? 26 : 23);
        menuLanguageButtonLabel.fontSize = menuLanguageButtonLabel.fontSizeMax;
        menuLanguageButtonLabel.alignment = TextAlignmentOptions.MidlineLeft;
        menuLanguageButtonLabel.textWrappingMode = TextWrappingModes.NoWrap;
        menuLanguageButtonLabel.overflowMode = TextOverflowModes.Ellipsis;
        menuLanguageButtonLabel.fontStyle = FontStyles.Bold;

        var globe = StarGlyph.Add(rt, "Glyph", StarGlyph.Globe, Bauhaus.Ink, new Vector2(34f, 34f));
        globe.rectTransform.anchorMin = globe.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        globe.rectTransform.pivot = new Vector2(0f, 0.5f);
        globe.rectTransform.anchoredPosition = new Vector2(14f, 0f);

        Bauhaus.Button(btn, Bauhaus.Tone.Paper);
    }

    public void LoadGame()
    {
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);
		fadeOutUIImage.enabled = true;
		fadeOutUIImage.raycastTarget = true;

		// Coroutine allows developers to run different tasks simultaneously (for multitasking)
		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Game"));
    }

	public void LoadLevelSelect()
	{
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);
		fadeOutUIImage.enabled = true;
		fadeOutUIImage.raycastTarget = true;

		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "LevelSelect"));
	}

	/// <summary>Graphing calculator mode (same <c>Game</c> scene, platformer off).</summary>
	public void LoadGraphCalculator()
	{
		GraphCalculatorSession.RequestEnterFromMenu();
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);
		fadeOutUIImage.enabled = true;
		fadeOutUIImage.raycastTarget = true;
		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Game"));
	}

	public void LoadMenu()
    {
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage1;
		fadeOutUIImage.gameObject.SetActive(true);
		fadeOutUIImage.enabled = true;
		fadeOutUIImage.raycastTarget = true;

		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Menu"));
    }

	public void QuitGame()
    {
		Application.Quit();
    }
}