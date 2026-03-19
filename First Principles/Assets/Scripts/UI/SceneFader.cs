using System.Collections;
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
		Scene currentScene = SceneManager.GetActiveScene();

		if (currentScene.name == "Menu")
        {
			fadeOutUIImage = fadeOutUIImage1;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage2.gameObject.SetActive(false);

			ZoomIn();
			StartCoroutine(SpawnFaxasMenuButtonNextFrame());

			// FindObjectOfType<AudioManager>().PlayMusic("RainMusic");
		}
		else if (currentScene.name == "Game")
		{
			fadeOutUIImage = fadeOutUIImage2;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage1.gameObject.SetActive(false);

			// FindObjectOfType<AudioManager>().PlayMusic("ClassicalMusic");
		}
		else if (currentScene.name == "LevelSelect")
		{
			fadeOutUIImage = fadeOutUIImage2;
			fadeOutUIImage.gameObject.SetActive(true);
			fadeOutUIImage1.gameObject.SetActive(false);
		}

		StartCoroutine(Fade(FadeDirection.Out));
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
		}
		else
		{
			fadeOutUIImage.enabled = true;
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
		SceneManager.LoadScene(sceneToLoad);
	}

	private void SetColorImage(ref float alpha, FadeDirection fadeDirection)
	{
		fadeOutUIImage.color = new Color(fadeOutUIImage.color.r, fadeOutUIImage.color.g, fadeOutUIImage.color.b, alpha);
		alpha += Time.deltaTime * (1.0f / fadeSpeed) * ((fadeDirection == FadeDirection.Out) ? -1 : 1);
	}
    #endregion

    private void ZoomIn() => Debug.Log("Zoomed In");

    private IEnumerator SpawnFaxasMenuButtonNextFrame()
    {
        yield return null;
        TrySpawnFaxasGraphingMenuButton();
    }

    /// <summary>Runtime entry for free graphing mode (no extra scene objects required).</summary>
    private void TrySpawnFaxasGraphingMenuButton()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Menu", System.StringComparison.Ordinal))
            return;
        if (GameObject.Find("FaxasGraphingEntryButton") != null)
            return;

        var play = GameObject.FindGameObjectWithTag("PlayButton");
        if (play == null)
            return;

        var playRt = play.GetComponent<RectTransform>();
        if (playRt == null)
            return;

        var go = new GameObject("FaxasGraphingEntryButton");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(playRt.parent, false);
        rt.localScale = Vector3.one;
        rt.anchorMin = playRt.anchorMin;
        rt.anchorMax = playRt.anchorMax;
        rt.pivot = playRt.pivot;
        rt.sizeDelta = playRt.sizeDelta;
        rt.anchoredPosition = playRt.anchoredPosition + new Vector2(0f, 168f);

        var playImg = play.GetComponent<Image>();
        var img = go.AddComponent<Image>();
        if (playImg != null && playImg.sprite != null)
        {
            img.sprite = playImg.sprite;
            img.type = playImg.type;
        }

        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = new Color(0.16f, 0.48f, 0.40f, 0.98f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, img.color,
            Color.Lerp(img.color, Color.white, 0.2f),
            Color.Lerp(img.color, Color.black, 0.18f));
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(2f, -3f), 0.28f);
        btn.onClick.AddListener(LoadGraphCalculator);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 6f);
        trt.offsetMax = new Vector2(-10f, -6f);

        var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        bool tablet = DeviceLayout.IsTabletLike();
        tmp.text = "Faxas-style graphing";
        tmp.fontSize = tablet ? 26 : 22;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.92f, 0.98f, 1f, 1f);
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        var refTmp = play.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (refTmp != null && refTmp.font != null)
        {
            tmp.font = refTmp.font;
            if (refTmp.fontSharedMaterial != null)
                tmp.fontSharedMaterial = refTmp.fontSharedMaterial;
        }
        else if (TMPro.TMP_Settings.defaultFontAsset != null)
            tmp.font = TMPro.TMP_Settings.defaultFontAsset;
    }

    public void LoadGame()
    {
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);

		// Coroutine allows developers to run different tasks simultaneously (for multitasking)
		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Game"));
    }

	public void LoadLevelSelect()
	{
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);

		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "LevelSelect"));
	}

	/// <summary>Faxas Instruments–style free graph (same <c>Game</c> scene, platformer off).</summary>
	public void LoadGraphCalculator()
	{
		GraphCalculatorSession.RequestEnterFromMenu();
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage2;
		fadeOutUIImage.gameObject.SetActive(true);
		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Game"));
	}

	public void LoadMenu()
    {
		fadeOutUIImage.gameObject.SetActive(false);
		fadeOutUIImage = fadeOutUIImage1;
		fadeOutUIImage.gameObject.SetActive(true);

		StartCoroutine(FadeAndLoadScene(FadeDirection.In, "Menu"));
    }

	public void QuitGame()
    {
		Application.Quit();
    }
}