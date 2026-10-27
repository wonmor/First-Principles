using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// -----------------------------------------------------------------------------
// LevelCompleteOverlay — "Level cleared" card: stars pop in one by one with chimes,
// score / falls / time, new-best and daily-challenge badges, and Next / Replay / Levels.
// Star rules are spelled out on the card so players know how to master a level.
// -----------------------------------------------------------------------------

public static class LevelCompleteOverlay
{
    const string RootName = "LevelCompleteOverlayRoot";
    static GameObject _root;

    public static bool IsOpen => _root != null;

    public static void Open(Transform canvas, RunResult r, ProgressStore.RecordOutcome o, DailyChallengeSpec? daily,
        Action onNext, Action onReplay, Action onLevels, ChallengeOutcome challenge = null)
    {
        Close();
        if (canvas == null)
            return;

        bool tablet = DeviceLayout.IsTabletLike();
        bool landscape = Screen.width >= Screen.height;
        var rootRt = UiKit.FullScreenDim(RootName, canvas, 0.78f, null);
        _root = rootRt.gameObject;
        var group = _root.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var panel = UiKit.Panel(rootRt,
            landscape ? new Vector2(0.24f, 0.07f) : new Vector2(0.05f, 0.16f),
            landscape ? new Vector2(0.76f, 0.93f) : new Vector2(0.95f, 0.84f));

        // Yellow header block (Bauhaus band) across the top of the card.
        UiKit.Band(panel, Bauhaus.Yellow, 22f);

        string title = o.FirstClear
            ? LocalizationManager.Get("complete.title_first", "Level cleared!")
            : LocalizationManager.Get("complete.title", "Cleared again!");
        UiKit.Text(panel, "Title", new Vector2(0.06f, 0.83f), new Vector2(0.94f, 0.94f), title,
            UiTypography.Scale(tablet ? 46 : 42), Bauhaus.Ink, FontStyles.Bold);
        UiKit.Text(panel, "Level", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.83f),
            GameLevelCatalog.GetLocalizedDisplayName(r.LevelIndex),
            UiTypography.Scale(tablet ? 24 : 22), UiKit.Muted);

        // Stars start as outlines; the coroutine fills them.
        var stars = UiKit.Stars(panel, new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.76f), 0, tablet ? 112f : 100f);

        string stats = string.Format(
            LocalizationManager.Get("complete.stats", "<b>{0}</b> points   |   <b>{1}</b> falls   |   <b>{2}</b>"),
            r.Score, r.Falls, FormatTime(r.Seconds));
        UiKit.Text(panel, "Stats", new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.55f), stats,
            UiTypography.Scale(tablet ? 26 : 23), Bauhaus.Ink);

        string rules = string.Format(
            LocalizationManager.Get("complete.rules",
                "1 star: reach the exit   |   2: at most {0} fall   |   3: no falls and {1}+ points"),
            ProgressRules.TwoStarMaxFalls, ProgressRules.ThreeStarMinScore);
        UiKit.Text(panel, "Rules", new Vector2(0.06f, 0.41f), new Vector2(0.94f, 0.47f), rules,
            UiTypography.Scale(tablet ? 20 : 19), UiKit.Muted, FontStyles.Normal);

        // Badge line (new best / daily / streak).
        string badge = "";
        bool goldBadge = o.DailyCompletedNow;
        if (challenge != null && challenge.Kind != ChallengeKind.None)
        {
            string name = ChallengeSession.Title(challenge.Kind);
            if (challenge.Met)
            {
                badge = string.IsNullOrEmpty(challenge.NewMedal)
                    ? string.Format(LocalizationManager.Get("complete.challenge_again", "{0} passed again"), name)
                    : string.Format(LocalizationManager.Get("complete.challenge_medal", "{0} passed! Medal earned"), name);
                goldBadge = true;
            }
            else
            {
                badge = string.Format(LocalizationManager.Get("complete.challenge_missed", "{0} not passed: {1}"), name,
                    ChallengeSession.GoalText(new ChallengeSpec(challenge.Kind, r.LevelIndex, challenge.ParSeconds)));
            }
        }
        else if (o.DailyCompletedNow)
            badge = string.Format(LocalizationManager.Get("complete.daily_done",
                "Daily challenge complete!   Streak: {0} day(s)"), o.DailyStreak);
        else if (daily.HasValue && daily.Value.LevelIndex == r.LevelIndex && !ProgressStore.DailyDoneToday)
            badge = string.Format(LocalizationManager.Get("complete.daily_missed", "Daily goal not met yet: {0}"),
                DailyChallengeSession.GoalText(daily.Value.Goal));
        else if (o.Stars > o.PreviousStars && !o.FirstClear)
            badge = LocalizationManager.Get("complete.more_stars", "New star record!");
        else if (o.NewBestScore)
            badge = LocalizationManager.Get("complete.best", "New best score!");
        var badgeTmp = UiKit.Text(panel, "Badge", new Vector2(0.06f, 0.33f), new Vector2(0.94f, 0.41f), badge,
            UiTypography.Scale(tablet ? 26 : 23), goldBadge ? Bauhaus.Red : Bauhaus.Blue, FontStyles.Bold);

        // Buttons.
        bool nextLocked = !Entitlements.IsLevelUnlocked(NextIndex(r.LevelIndex));
        string nextLabel = nextLocked
            ? LocalizationManager.Get("complete.next_locked", "Unlock more levels")
            : LocalizationManager.Get("complete.next", "Next level");
        var next = UiKit.Button(panel, "Next", new Vector2(0.08f, 0.17f), new Vector2(0.92f, 0.3f), nextLabel,
            Bauhaus.Tone.Red, () => { Close(); onNext?.Invoke(); }, tablet ? 32 : 30);
        var replay = UiKit.Button(panel, "Replay", new Vector2(0.08f, 0.05f), new Vector2(0.49f, 0.15f),
            LocalizationManager.Get("complete.replay", "Replay"), Bauhaus.Tone.Paper,
            () => { Close(); onReplay?.Invoke(); }, tablet ? 25 : 23);
        var levels = UiKit.Button(panel, "Levels", new Vector2(0.51f, 0.05f), new Vector2(0.92f, 0.15f),
            LocalizationManager.Get("complete.levels", "Levels"), Bauhaus.Tone.Ink,
            () => { Close(); onLevels?.Invoke(); }, tablet ? 25 : 23);
        OverlayCancelHandler.Attach(_root, levels, next);

        FeelDirector.Instance.StartCoroutine(Animate(group, panel, stars, o.Stars, badgeTmp));
    }

    static int NextIndex(int i)
    {
        int n = GameLevelCatalog.LevelCount;
        int next = i + 1;
        if (next >= n) next = 0;
        if (GameLevelCatalog.IsComingSoonLevel(next)) next = 0;
        return next;
    }

    static IEnumerator Animate(CanvasGroup group, RectTransform panel, Image[] stars, int earned, TextMeshProUGUI badge)
    {
        FeelDirector.Play(SfxId.Complete, 0.8f);
        float t = 0f;
        panel.localScale = Vector3.one * 0.86f;
        while (t < 0.22f && group != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / 0.22f);
            group.alpha = k;
            panel.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, k);
            yield return null;
        }
        if (group == null)
            yield break;
        group.alpha = 1f;
        panel.localScale = Vector3.one;

        for (int i = 0; i < earned && i < stars.Length; i++)
        {
            yield return new WaitForSecondsRealtime(0.28f);
            if (stars[i] == null)
                yield break;
            StarGlyph.SetStar(stars[i], true);
            FeelDirector.Play(i == 0 ? SfxId.Star1 : i == 1 ? SfxId.Star2 : SfxId.Star3, 0.85f);
            FeelDirector.Instance.StartCoroutine(FeelDirector.PopScale(stars[i].transform, 0.5f));
            FeelDirector.Instance.Burst(stars[i].transform.position, Bauhaus.Ink, 6 + i * 3, 360f);
        }

        if (badge != null && !string.IsNullOrEmpty(badge.text))
            FeelDirector.Instance.StartCoroutine(FeelDirector.PopScale(badge.transform, 0.4f, 1.2f));
    }

    public static void Close()
    {
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
    }

    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int m = Mathf.FloorToInt(seconds / 60f);
        float s = seconds - m * 60f;
        return m > 0 ? $"{m}:{s:00.0}" : $"{s:0.0}s";
    }
}
