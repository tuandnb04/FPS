using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TopDownShooter.Audio;
using TopDownShooter.Core;

namespace TopDownShooter.UI
{
    public enum GameState
    {
        Title,
        Playing,
        Paused,
        Ended
    }

    /// <summary>
    /// Full 4-state UI & Game Shell controller (Title, Playing, Paused, Ended).
    /// Animates CanvasGroup fades and sprite-backed filled bars via unscaled Ease.Tween.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Screen Groups")]
        [SerializeField] private CanvasGroup titleGroup;
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private CanvasGroup pauseGroup;
        [SerializeField] private CanvasGroup endedGroup;

        [Header("HUD Elements")]
        [SerializeField] private Image hpFillImage;
        [SerializeField] private Image xpFillImage;
        [SerializeField] private Image dashFillImage;
        [SerializeField] private TextMeshProUGUI hpValueText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI waveText;
        [SerializeField] private TextMeshProUGUI enemiesText;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private RectTransform crosshairRect;

        [Header("Milestone Banner")]
        [SerializeField] private CanvasGroup milestoneBannerGroup;
        [SerializeField] private RectTransform milestoneBannerRect;
        [SerializeField] private TextMeshProUGUI milestoneBannerText;

        [Header("Title Elements")]
        [SerializeField] private TextMeshProUGUI titleHighScoreText;

        [Header("Pause Settings Elements")]
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private TextMeshProUGUI musicPctText;
        [SerializeField] private TextMeshProUGUI sfxPctText;

        [Header("Ended Elements")]
        [SerializeField] private TextMeshProUGUI endedTitleText;
        [SerializeField] private TextMeshProUGUI endedScoreText;
        [SerializeField] private TextMeshProUGUI endedBestText;
        [SerializeField] private TextMeshProUGUI endedStatsText;

        [Header("Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button hudPauseButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button pauseRestartButton;
        [SerializeField] private Button pauseTitleButton;
        [SerializeField] private Button endedReplayButton;
        [SerializeField] private Button endedTitleButton;

        private Coroutine hpTween;
        private Coroutine xpTween;
        private Coroutine transitionTween;
        private Coroutine milestoneRoutine;

        public float HpBarFill => hpFillImage != null ? hpFillImage.fillAmount : 0f;
        public float XpBarFill => xpFillImage != null ? xpFillImage.fillAmount : 0f;
        public float TitleAlpha => titleGroup != null ? titleGroup.alpha : 0f;
        public float HudAlpha => hudGroup != null ? hudGroup.alpha : 0f;
        public float PauseAlpha => pauseGroup != null ? pauseGroup.alpha : 0f;
        public float EndedAlpha => endedGroup != null ? endedGroup.alpha : 0f;

        public void BindAll(
            CanvasGroup title, CanvasGroup hud, CanvasGroup pause, CanvasGroup ended,
            Image hpFill, Image xpFill, Image dashFill,
            TextMeshProUGUI hpTxt, TextMeshProUGUI lvlTxt, TextMeshProUGUI wvTxt, TextMeshProUGUI enemTxt, TextMeshProUGUI scrTxt,
            RectTransform crosshair,
            CanvasGroup milestoneCg, RectTransform milestoneRt, TextMeshProUGUI milestoneTxt,
            TextMeshProUGUI titleBestTxt,
            Slider mSlider, Slider sSlider, TextMeshProUGUI mPct, TextMeshProUGUI sPct,
            TextMeshProUGUI endTitle, TextMeshProUGUI endScore, TextMeshProUGUI endBest, TextMeshProUGUI endStats,
            Button startBtn, Button hudPauseBtn, Button resumeBtn, Button pRestartBtn, Button pTitleBtn, Button eReplayBtn, Button eTitleBtn)
        {
            titleGroup = title;
            hudGroup = hud;
            pauseGroup = pause;
            endedGroup = ended;
            hpFillImage = hpFill;
            xpFillImage = xpFill;
            dashFillImage = dashFill;
            hpValueText = hpTxt;
            levelText = lvlTxt;
            waveText = wvTxt;
            enemiesText = enemTxt;
            scoreText = scrTxt;
            crosshairRect = crosshair;
            milestoneBannerGroup = milestoneCg;
            milestoneBannerRect = milestoneRt;
            milestoneBannerText = milestoneTxt;
            titleHighScoreText = titleBestTxt;
            musicSlider = mSlider;
            sfxSlider = sSlider;
            musicPctText = mPct;
            sfxPctText = sPct;
            endedTitleText = endTitle;
            endedScoreText = endScore;
            endedBestText = endBest;
            endedStatsText = endStats;
            startButton = startBtn;
            hudPauseButton = hudPauseBtn;
            resumeButton = resumeBtn;
            pauseRestartButton = pRestartBtn;
            pauseTitleButton = pTitleBtn;
            endedReplayButton = eReplayBtn;
            endedTitleButton = eTitleBtn;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            EnsureBarSprites();
            WireButtonsAndSliders();
            ApplyImmediateState(GameState.Title);
            RefreshVolumeLabels();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void EnsureBarSprites()
        {
            Sprite rectSprite = UISpriteFactory.GetRoundedRectSprite();
            if (hpFillImage != null && hpFillImage.sprite == null) hpFillImage.sprite = rectSprite;
            if (xpFillImage != null && xpFillImage.sprite == null) xpFillImage.sprite = rectSprite;
            if (dashFillImage != null && dashFillImage.sprite == null) dashFillImage.sprite = rectSprite;
        }

        private void WireButtonsAndSliders()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(() => GameManager.Instance?.StartMission());
            }
            if (hudPauseButton != null)
            {
                hudPauseButton.onClick.RemoveAllListeners();
                hudPauseButton.onClick.AddListener(() => GameManager.Instance?.SetPaused(true));
            }
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveAllListeners();
                resumeButton.onClick.AddListener(() => GameManager.Instance?.SetPaused(false));
            }
            if (pauseRestartButton != null)
            {
                pauseRestartButton.onClick.RemoveAllListeners();
                pauseRestartButton.onClick.AddListener(() => GameManager.Instance?.RestartMission());
            }
            if (pauseTitleButton != null)
            {
                pauseTitleButton.onClick.RemoveAllListeners();
                pauseTitleButton.onClick.AddListener(() => GameManager.Instance?.ReturnToTitle());
            }
            if (endedReplayButton != null)
            {
                endedReplayButton.onClick.RemoveAllListeners();
                endedReplayButton.onClick.AddListener(() => GameManager.Instance?.RestartMission());
            }
            if (endedTitleButton != null)
            {
                endedTitleButton.onClick.RemoveAllListeners();
                endedTitleButton.onClick.AddListener(() => GameManager.Instance?.ReturnToTitle());
            }

            if (musicSlider != null)
            {
                musicSlider.onValueChanged.RemoveAllListeners();
                if (AudioManager.Instance != null) musicSlider.SetValueWithoutNotify(AudioManager.Instance.MusicVol);
                musicSlider.onValueChanged.AddListener(v =>
                {
                    AudioManager.Instance?.SetMusicVolume(v);
                    RefreshVolumeLabels();
                });
            }

            if (sfxSlider != null)
            {
                sfxSlider.onValueChanged.RemoveAllListeners();
                if (AudioManager.Instance != null) sfxSlider.SetValueWithoutNotify(AudioManager.Instance.SFXVol);
                sfxSlider.onValueChanged.AddListener(v =>
                {
                    AudioManager.Instance?.SetSFXVolume(v);
                    RefreshVolumeLabels();
                });
            }
        }

        private void RefreshVolumeLabels()
        {
            if (musicPctText != null && musicSlider != null)
            {
                musicPctText.text = Mathf.RoundToInt(musicSlider.value * 100f) + "%";
            }
            if (sfxPctText != null && sfxSlider != null)
            {
                sfxPctText.text = Mathf.RoundToInt(sfxSlider.value * 100f) + "%";
            }
        }

        private void Update()
        {
            if (crosshairRect != null && hudGroup != null && hudGroup.alpha > 0.5f && Mouse.current != null)
            {
                crosshairRect.position = Mouse.current.position.ReadValue();
            }
        }

        public void ApplyImmediateState(GameState state)
        {
            if (transitionTween != null)
            {
                StopCoroutine(transitionTween);
                transitionTween = null;
            }
            SetGroupState(titleGroup, state == GameState.Title ? 1f : 0f);
            SetGroupState(hudGroup, state == GameState.Playing ? 1f : 0f);
            SetGroupState(pauseGroup, state == GameState.Paused ? 1f : 0f);
            SetGroupState(endedGroup, state == GameState.Ended ? 1f : 0f);
            if (milestoneBannerGroup != null)
            {
                milestoneBannerGroup.alpha = 0f;
            }
        }

        public void TransitionToState(GameState state)
        {
            if (transitionTween != null)
            {
                StopCoroutine(transitionTween);
            }
            transitionTween = StartCoroutine(TransitionRoutine(state));
        }

        private IEnumerator TransitionRoutine(GameState target)
        {
            float startTitle = titleGroup != null ? titleGroup.alpha : 0f;
            float startHud = hudGroup != null ? hudGroup.alpha : 0f;
            float startPause = pauseGroup != null ? pauseGroup.alpha : 0f;
            float startEnded = endedGroup != null ? endedGroup.alpha : 0f;

            float endTitle = target == GameState.Title ? 1f : 0f;
            float endHud = target == GameState.Playing ? 1f : 0f;
            float endPause = target == GameState.Paused ? 1f : 0f;
            float endEnded = target == GameState.Ended ? 1f : 0f;

            SetGroupInteractable(titleGroup, endTitle > 0.5f);
            SetGroupInteractable(hudGroup, endHud > 0.5f);
            SetGroupInteractable(pauseGroup, endPause > 0.5f);
            SetGroupInteractable(endedGroup, endEnded > 0.5f);

            yield return Ease.Tween(0.30f, t =>
            {
                float e = Ease.OutCubic(t);
                if (titleGroup != null) titleGroup.alpha = Mathf.Lerp(startTitle, endTitle, e);
                if (hudGroup != null) hudGroup.alpha = Mathf.Lerp(startHud, endHud, e);
                if (pauseGroup != null) pauseGroup.alpha = Mathf.Lerp(startPause, endPause, e);
                if (endedGroup != null) endedGroup.alpha = Mathf.Lerp(startEnded, endEnded, e);
            }, Ease.Linear, unscaled: true);

            SetGroupState(titleGroup, endTitle);
            SetGroupState(hudGroup, endHud);
            SetGroupState(pauseGroup, endPause);
            SetGroupState(endedGroup, endEnded);
            transitionTween = null;
        }

        private static void SetGroupState(CanvasGroup cg, float alpha)
        {
            if (cg == null) return;
            cg.alpha = alpha;
            bool active = alpha > 0.5f;
            cg.interactable = active;
            cg.blocksRaycasts = active;
        }

        private static void SetGroupInteractable(CanvasGroup cg, bool interactable)
        {
            if (cg == null) return;
            cg.interactable = interactable;
            cg.blocksRaycasts = interactable;
        }

        public void UpdateHealth(float current, float max)
        {
            float targetFill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (hpValueText != null)
            {
                hpValueText.text = Mathf.CeilToInt(current) + " / " + Mathf.RoundToInt(max);
            }
            if (hpFillImage != null)
            {
                if (hpTween != null) StopCoroutine(hpTween);
                float start = hpFillImage.fillAmount;
                hpTween = StartCoroutine(TweenImageFill(hpFillImage, start, targetFill, 0.30f));
            }
        }

        public void UpdateXp(int current, int max, int level)
        {
            float targetFill = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            if (levelText != null)
            {
                levelText.text = "LVL " + level.ToString("00");
            }
            if (xpFillImage != null)
            {
                if (xpTween != null) StopCoroutine(xpTween);
                float start = xpFillImage.fillAmount;
                xpTween = StartCoroutine(TweenImageFill(xpFillImage, start, targetFill, 0.30f));
            }
        }

        public void UpdateDashCooldown(float readyNormalized)
        {
            if (dashFillImage != null)
            {
                dashFillImage.fillAmount = Mathf.Clamp01(readyNormalized);
            }
        }

        public void UpdateWave(int wave, int enemiesRemaining)
        {
            if (waveText != null) waveText.text = "WAVE " + wave.ToString("00");
            if (enemiesText != null) enemiesText.text = enemiesRemaining + " HOSTILES";
        }

        public void UpdateScore(int score, int highScore)
        {
            if (scoreText != null) scoreText.text = score.ToString("N0") + " PTS";
            if (titleHighScoreText != null) titleHighScoreText.text = "HIGH SCORE // " + highScore.ToString("N0");
        }

        public void PopulateEndedSummary(bool victory, int score, int highScore, int kills, int wave, bool isNewRecord)
        {
            if (endedTitleText != null)
            {
                endedTitleText.text = victory ? "ARENA SECURED" : "SIGNAL LOST";
            }
            if (endedScoreText != null)
            {
                endedScoreText.text = score.ToString("N0") + " PTS";
            }
            if (endedBestText != null)
            {
                endedBestText.text = isNewRecord ? "NEW RECORD // " + highScore.ToString("N0") : "BEST // " + highScore.ToString("N0");
            }
            if (endedStatsText != null)
            {
                endedStatsText.text = "ELIMINATIONS: " + kills + "   |   WAVE REACHED: " + wave;
            }
        }

        public void ShowMilestoneBanner(string message)
        {
            if (milestoneBannerGroup == null || milestoneBannerText == null) return;
            if (milestoneRoutine != null) StopCoroutine(milestoneRoutine);
            milestoneRoutine = StartCoroutine(MilestoneBannerRoutine(message));
        }

        private IEnumerator MilestoneBannerRoutine(string message)
        {
            milestoneBannerText.text = message;
            if (milestoneBannerRect != null) milestoneBannerRect.localScale = Vector3.zero;
            milestoneBannerGroup.alpha = 1f;

            yield return Ease.Tween(0.22f, t =>
            {
                if (milestoneBannerRect != null)
                {
                    float s = Ease.OutBack(t);
                    milestoneBannerRect.localScale = new Vector3(s, s, 1f);
                }
            }, Ease.Linear, unscaled: true);

            yield return new WaitForSecondsRealtime(1.15f);

            yield return Ease.Tween(0.24f, t =>
            {
                milestoneBannerGroup.alpha = Mathf.Lerp(1f, 0f, Ease.OutCubic(t));
            }, Ease.Linear, unscaled: true);

            milestoneBannerGroup.alpha = 0f;
            milestoneRoutine = null;
        }

        private static IEnumerator TweenImageFill(Image img, float start, float target, float duration)
        {
            yield return Ease.Tween(duration, t =>
            {
                if (img != null)
                {
                    img.fillAmount = Mathf.Lerp(start, target, t);
                }
            }, Ease.OutCubic, unscaled: true);
        }
    }
}
