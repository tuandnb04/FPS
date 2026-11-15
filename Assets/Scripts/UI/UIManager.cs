using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;

namespace TopDownShooter.UI
{
    public enum GameState
    {
        Title,
        Playing,
        Paused,
        Ended,
        Armory,
        LevelUpDraft
    }

    /// <summary>
    /// Commercial-grade 6-state UI & Game Shell controller supporting Title, Armory Shop,
    /// HUD (with Boss Bar, 4-Weapon Loadout, & Twin-Stick Mobile Controls), LevelUpDraft, Paused, and Ended.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private static readonly string[] WeaponNames = { "PULSE RIFLE", "SHOTGUN", "RAILGUN", "ROCKET" };
        private static readonly int[] WeaponCosts = { 0, 150, 300, 450 };
        private static readonly string[] SkinNames = { "CYBER CYAN", "SOLAR GOLD", "CRIMSON OPS" };
        private static readonly int[] SkinCosts = { 0, 180, 280 };
        private static readonly string[] UpgradeKeys = { "armor", "firepower", "thruster", "greed" };
        private static readonly string[] UpgradeTitles = { "HULL ARMOR (+20 HP)", "WEAPON CORE (+12% DMG)", "THRUSTER DRIVE (+SPD)", "SCAVENGER (+18% CR)" };

        [Header("Screen Groups")]
        [SerializeField] private CanvasGroup titleGroup;
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private CanvasGroup pauseGroup;
        [SerializeField] private CanvasGroup endedGroup;
        [SerializeField] private CanvasGroup armoryGroup;
        [SerializeField] private CanvasGroup draftGroup;

        [Header("HUD Elements")]
        [SerializeField] private Image hpFillImage;
        [SerializeField] private Image xpFillImage;
        [SerializeField] private Image dashFillImage;
        [SerializeField] private TextMeshProUGUI hpValueText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI waveText;
        [SerializeField] private TextMeshProUGUI enemiesText;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI hudCreditsText;
        [SerializeField] private RectTransform crosshairRect;

        [Header("Boss Bar")]
        [SerializeField] private RectTransform bossBarRoot;
        [SerializeField] private Image bossHpFillImage;
        [SerializeField] private TextMeshProUGUI bossNameText;
        [SerializeField] private TextMeshProUGUI bossHpText;

        [Header("Weapon Loadout & Mobile Controls")]
        [SerializeField] private Button[] weaponSlotButtons;
        [SerializeField] private Image[] weaponSlotBgs;
        [SerializeField] private TextMeshProUGUI[] weaponSlotLabels;
        [SerializeField] private VirtualJoystick leftJoystick;
        [SerializeField] private VirtualJoystick rightJoystick;
        [SerializeField] private Button touchDashButton;

        [Header("Milestone Banner")]
        [SerializeField] private CanvasGroup milestoneBannerGroup;
        [SerializeField] private RectTransform milestoneBannerRect;
        [SerializeField] private TextMeshProUGUI milestoneBannerText;

        [Header("Title & Armory Elements")]
        [SerializeField] private TextMeshProUGUI titleHighScoreText;
        [SerializeField] private TextMeshProUGUI titleCreditsText;
        [SerializeField] private TextMeshProUGUI armoryCreditsText;
        [SerializeField] private Button titleArmoryButton;
        [SerializeField] private Button armoryCloseButton;
        [SerializeField] private Button[] upgradeButtons;
        [SerializeField] private TextMeshProUGUI[] upgradeRankLabels;
        [SerializeField] private TextMeshProUGUI[] upgradeCostLabels;
        [SerializeField] private Button[] shopWeaponButtons;
        [SerializeField] private TextMeshProUGUI[] shopWeaponLabels;
        [SerializeField] private Button[] shopSkinButtons;
        [SerializeField] private TextMeshProUGUI[] shopSkinLabels;

        [Header("Roguelite Draft Cards")]
        [SerializeField] private Button[] draftCardButtons;
        [SerializeField] private TextMeshProUGUI[] draftTitles;
        [SerializeField] private TextMeshProUGUI[] draftRanks;
        [SerializeField] private TextMeshProUGUI[] draftDescs;

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
        [SerializeField] private TextMeshProUGUI endedCreditsText;

        [Header("Core Navigation Buttons")]
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
        private bool touchDashQueued;
        private List<RogueliteSkillInfo> currentDraftOptions = new List<RogueliteSkillInfo>();

        public float HpBarFill => hpFillImage != null ? hpFillImage.fillAmount : 0f;
        public float XpBarFill => xpFillImage != null ? xpFillImage.fillAmount : 0f;
        public float TitleAlpha => titleGroup != null ? titleGroup.alpha : 0f;
        public float HudAlpha => hudGroup != null ? hudGroup.alpha : 0f;
        public float PauseAlpha => pauseGroup != null ? pauseGroup.alpha : 0f;
        public float EndedAlpha => endedGroup != null ? endedGroup.alpha : 0f;
        public float ArmoryAlpha => armoryGroup != null ? armoryGroup.alpha : 0f;
        public float DraftAlpha => draftGroup != null ? draftGroup.alpha : 0f;
        public bool IsBossBarVisible => bossBarRoot != null && bossBarRoot.gameObject.activeSelf;
        public VirtualJoystick LeftJoystick => leftJoystick;
        public VirtualJoystick RightJoystick => rightJoystick;

        public void BindCore(
            CanvasGroup title, CanvasGroup hud, CanvasGroup pause, CanvasGroup ended, CanvasGroup armory, CanvasGroup draft,
            Image hpFill, Image xpFill, Image dashFill,
            TextMeshProUGUI hpTxt, TextMeshProUGUI lvlTxt, TextMeshProUGUI wvTxt, TextMeshProUGUI enemTxt, TextMeshProUGUI scrTxt, TextMeshProUGUI hudCrTxt,
            RectTransform crosshair,
            RectTransform bossRoot, Image bossFill, TextMeshProUGUI bossName, TextMeshProUGUI bossHpTxt,
            CanvasGroup milestoneCg, RectTransform milestoneRt, TextMeshProUGUI milestoneTxt,
            TextMeshProUGUI titleBestTxt, TextMeshProUGUI titleCrTxt,
            Slider mSlider, Slider sSlider, TextMeshProUGUI mPct, TextMeshProUGUI sPct,
            TextMeshProUGUI endTitle, TextMeshProUGUI endScore, TextMeshProUGUI endBest, TextMeshProUGUI endStats, TextMeshProUGUI endCr,
            Button startBtn, Button armoryBtn, Button hudPauseBtn, Button resumeBtn, Button pRestartBtn, Button pTitleBtn, Button eReplayBtn, Button eTitleBtn)
        {
            titleGroup = title;
            hudGroup = hud;
            pauseGroup = pause;
            endedGroup = ended;
            armoryGroup = armory;
            draftGroup = draft;
            hpFillImage = hpFill;
            xpFillImage = xpFill;
            dashFillImage = dashFill;
            hpValueText = hpTxt;
            levelText = lvlTxt;
            waveText = wvTxt;
            enemiesText = enemTxt;
            scoreText = scrTxt;
            hudCreditsText = hudCrTxt;
            crosshairRect = crosshair;
            bossBarRoot = bossRoot;
            bossHpFillImage = bossFill;
            bossNameText = bossName;
            bossHpText = bossHpTxt;
            milestoneBannerGroup = milestoneCg;
            milestoneBannerRect = milestoneRt;
            milestoneBannerText = milestoneTxt;
            titleHighScoreText = titleBestTxt;
            titleCreditsText = titleCrTxt;
            musicSlider = mSlider;
            sfxSlider = sSlider;
            musicPctText = mPct;
            sfxPctText = sPct;
            endedTitleText = endTitle;
            endedScoreText = endScore;
            endedBestText = endBest;
            endedStatsText = endStats;
            endedCreditsText = endCr;
            startButton = startBtn;
            titleArmoryButton = armoryBtn;
            hudPauseButton = hudPauseBtn;
            resumeButton = resumeBtn;
            pauseRestartButton = pRestartBtn;
            pauseTitleButton = pTitleBtn;
            endedReplayButton = eReplayBtn;
            endedTitleButton = eTitleBtn;
        }

        public void BindStoreAndMobileWidgets(
            Button[] wButtons, Image[] wBgs, TextMeshProUGUI[] wLabels,
            VirtualJoystick leftStick, VirtualJoystick rightStick, Button dashBtn,
            TextMeshProUGUI armoryCr, Button armoryClose,
            Button[] upgBtns, TextMeshProUGUI[] upgRankLbls, TextMeshProUGUI[] upgCostLbls,
            Button[] shopWpnBtns, TextMeshProUGUI[] shopWpnLbls,
            Button[] shopSkinBtns, TextMeshProUGUI[] shopSkinLbls,
            Button[] drftBtns, TextMeshProUGUI[] drftTitles, TextMeshProUGUI[] drftRanks, TextMeshProUGUI[] drftDescs)
        {
            weaponSlotButtons = wButtons;
            weaponSlotBgs = wBgs;
            weaponSlotLabels = wLabels;
            leftJoystick = leftStick;
            rightJoystick = rightStick;
            touchDashButton = dashBtn;
            armoryCreditsText = armoryCr;
            armoryCloseButton = armoryClose;
            upgradeButtons = upgBtns;
            upgradeRankLabels = upgRankLbls;
            upgradeCostLabels = upgCostLbls;
            shopWeaponButtons = shopWpnBtns;
            shopWeaponLabels = shopWpnLbls;
            shopSkinButtons = shopSkinBtns;
            shopSkinLabels = shopSkinLbls;
            draftCardButtons = drftBtns;
            draftTitles = drftTitles;
            draftRanks = drftRanks;
            draftDescs = drftDescs;
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
            RefreshWeaponBar();
            RefreshArmoryUI();
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
            if (bossHpFillImage != null && bossHpFillImage.sprite == null) bossHpFillImage.sprite = rectSprite;
        }

        private void WireButtonsAndSliders()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(() => GameManager.Instance?.StartMission());
            }
            if (titleArmoryButton != null)
            {
                titleArmoryButton.onClick.RemoveAllListeners();
                titleArmoryButton.onClick.AddListener(() => GameManager.Instance?.OpenArmory());
            }
            if (armoryCloseButton != null)
            {
                armoryCloseButton.onClick.RemoveAllListeners();
                armoryCloseButton.onClick.AddListener(() => GameManager.Instance?.CloseArmory());
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

            if (touchDashButton != null)
            {
                touchDashButton.onClick.RemoveAllListeners();
                touchDashButton.onClick.AddListener(() => touchDashQueued = true);
            }

            if (weaponSlotButtons != null)
            {
                for (int i = 0; i < weaponSlotButtons.Length; i++)
                {
                    int idx = i;
                    if (weaponSlotButtons[i] == null) continue;
                    weaponSlotButtons[i].onClick.RemoveAllListeners();
                    weaponSlotButtons[i].onClick.AddListener(() => PlayerController.Instance?.TrySwitchWeapon(idx));
                }
            }

            if (upgradeButtons != null)
            {
                for (int i = 0; i < upgradeButtons.Length; i++)
                {
                    int idx = i;
                    if (upgradeButtons[i] == null) continue;
                    upgradeButtons[i].onClick.RemoveAllListeners();
                    upgradeButtons[i].onClick.AddListener(() => OnUpgradeClicked(idx));
                }
            }

            if (shopWeaponButtons != null)
            {
                for (int i = 0; i < shopWeaponButtons.Length; i++)
                {
                    int idx = i;
                    if (shopWeaponButtons[i] == null) continue;
                    shopWeaponButtons[i].onClick.RemoveAllListeners();
                    shopWeaponButtons[i].onClick.AddListener(() => OnShopWeaponClicked(idx));
                }
            }

            if (shopSkinButtons != null)
            {
                for (int i = 0; i < shopSkinButtons.Length; i++)
                {
                    int idx = i;
                    if (shopSkinButtons[i] == null) continue;
                    shopSkinButtons[i].onClick.RemoveAllListeners();
                    shopSkinButtons[i].onClick.AddListener(() => OnShopSkinClicked(idx));
                }
            }

            if (draftCardButtons != null)
            {
                for (int i = 0; i < draftCardButtons.Length; i++)
                {
                    int idx = i;
                    if (draftCardButtons[i] == null) continue;
                    draftCardButtons[i].onClick.RemoveAllListeners();
                    draftCardButtons[i].onClick.AddListener(() => OnDraftCardClicked(idx));
                }
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

        private void OnUpgradeClicked(int index)
        {
            if (index < 0 || index >= UpgradeKeys.Length) return;
            SaveManager save = SaveManager.EnsureInstance();
            if (save.TryUpgradeStat(UpgradeKeys[index]))
            {
                AudioManager.Instance?.PlayPickupBlip();
                RefreshArmoryUI();
            }
        }

        private void OnShopWeaponClicked(int index)
        {
            if (index < 0 || index >= WeaponCosts.Length) return;
            SaveManager save = SaveManager.EnsureInstance();
            if (save.TryUnlockOrEquipWeapon(index, WeaponCosts[index]))
            {
                AudioManager.Instance?.PlayUIClick();
                RefreshArmoryUI();
                RefreshWeaponBar();
            }
        }

        private void OnShopSkinClicked(int index)
        {
            if (index < 0 || index >= SkinCosts.Length) return;
            SaveManager save = SaveManager.EnsureInstance();
            if (save.TryUnlockOrEquipSkin(index, SkinCosts[index]))
            {
                AudioManager.Instance?.PlayUIClick();
                PlayerController.Instance?.ApplySaveLoadout();
                RefreshArmoryUI();
            }
        }

        private void OnDraftCardClicked(int index)
        {
            if (index < 0 || index >= currentDraftOptions.Count) return;
            GameManager.Instance?.SelectDraftSkill(currentDraftOptions[index].Id);
        }

        public bool ConsumeTouchDashPressed()
        {
            bool pressed = touchDashQueued;
            touchDashQueued = false;
            return pressed;
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
            SetGroupState(armoryGroup, state == GameState.Armory ? 1f : 0f);
            SetGroupState(draftGroup, state == GameState.LevelUpDraft ? 1f : 0f);
            if (milestoneBannerGroup != null)
            {
                milestoneBannerGroup.alpha = 0f;
            }
            HideBossHp();
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
            float sTitle = titleGroup != null ? titleGroup.alpha : 0f;
            float sHud = hudGroup != null ? hudGroup.alpha : 0f;
            float sPause = pauseGroup != null ? pauseGroup.alpha : 0f;
            float sEnded = endedGroup != null ? endedGroup.alpha : 0f;
            float sArmory = armoryGroup != null ? armoryGroup.alpha : 0f;
            float sDraft = draftGroup != null ? draftGroup.alpha : 0f;

            float eTitle = target == GameState.Title ? 1f : 0f;
            float eHud = target == GameState.Playing ? 1f : 0f;
            float ePause = target == GameState.Paused ? 1f : 0f;
            float eEnded = target == GameState.Ended ? 1f : 0f;
            float eArmory = target == GameState.Armory ? 1f : 0f;
            float eDraft = target == GameState.LevelUpDraft ? 1f : 0f;

            SetGroupInteractable(titleGroup, eTitle > 0.5f);
            SetGroupInteractable(hudGroup, eHud > 0.5f);
            SetGroupInteractable(pauseGroup, ePause > 0.5f);
            SetGroupInteractable(endedGroup, eEnded > 0.5f);
            SetGroupInteractable(armoryGroup, eArmory > 0.5f);
            SetGroupInteractable(draftGroup, eDraft > 0.5f);

            yield return Ease.Tween(0.25f, t =>
            {
                float e = Ease.OutCubic(t);
                if (titleGroup != null) titleGroup.alpha = Mathf.Lerp(sTitle, eTitle, e);
                if (hudGroup != null) hudGroup.alpha = Mathf.Lerp(sHud, eHud, e);
                if (pauseGroup != null) pauseGroup.alpha = Mathf.Lerp(sPause, ePause, e);
                if (endedGroup != null) endedGroup.alpha = Mathf.Lerp(sEnded, eEnded, e);
                if (armoryGroup != null) armoryGroup.alpha = Mathf.Lerp(sArmory, eArmory, e);
                if (draftGroup != null) draftGroup.alpha = Mathf.Lerp(sDraft, eDraft, e);
            }, Ease.Linear, unscaled: true);

            SetGroupState(titleGroup, eTitle);
            SetGroupState(hudGroup, eHud);
            SetGroupState(pauseGroup, ePause);
            SetGroupState(endedGroup, eEnded);
            SetGroupState(armoryGroup, eArmory);
            SetGroupState(draftGroup, eDraft);
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

        public void UpdateCredits(int totalCredits, int runEarned)
        {
            if (titleCreditsText != null) titleCreditsText.text = "CREDITS // " + totalCredits.ToString("N0") + " CR";
            if (armoryCreditsText != null) armoryCreditsText.text = totalCredits.ToString("N0") + " CR";
            if (hudCreditsText != null) hudCreditsText.text = totalCredits.ToString("N0") + " CR";
            if (endedCreditsText != null) endedCreditsText.text = "EARNED +" + runEarned + " CR   |   BANK: " + totalCredits.ToString("N0") + " CR";
        }

        public void UpdateBossHp(float current, float max, string label)
        {
            if (bossBarRoot == null) return;
            bossBarRoot.gameObject.SetActive(true);
            if (bossHpFillImage != null) bossHpFillImage.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (bossNameText != null) bossNameText.text = label;
            if (bossHpText != null) bossHpText.text = Mathf.CeilToInt(current) + " / " + Mathf.RoundToInt(max);
        }

        public void HideBossHp()
        {
            if (bossBarRoot != null) bossBarRoot.gameObject.SetActive(false);
        }

        public void RefreshWeaponBar()
        {
            if (weaponSlotButtons == null) return;
            SaveManager save = SaveManager.EnsureInstance();
            int selected = save.Data.selectedWeapon;

            for (int i = 0; i < weaponSlotButtons.Length && i < WeaponNames.Length; i++)
            {
                bool unlocked = save.IsWeaponUnlocked(i);
                bool active = i == selected;
                if (weaponSlotBgs != null && i < weaponSlotBgs.Length && weaponSlotBgs[i] != null)
                {
                    weaponSlotBgs[i].color = active ? Palette.Accent : (unlocked ? Palette.SecondaryButtonBg : Palette.Shadow);
                }
                if (weaponSlotLabels != null && i < weaponSlotLabels.Length && weaponSlotLabels[i] != null)
                {
                    weaponSlotLabels[i].color = active ? Palette.Shadow : (unlocked ? Palette.CharacterSecondary : Palette.MutedText);
                    weaponSlotLabels[i].text = unlocked ? $"[{i + 1}] {WeaponNames[i]}" : $"[{i + 1}] LOCKED";
                }
                if (weaponSlotButtons[i] != null)
                {
                    weaponSlotButtons[i].interactable = unlocked;
                }
            }
        }

        public void RefreshArmoryUI()
        {
            SaveManager save = SaveManager.EnsureInstance();
            GameSaveData d = save.Data;

            if (armoryCreditsText != null) armoryCreditsText.text = d.credits.ToString("N0") + " CR";
            if (titleCreditsText != null) titleCreditsText.text = "CREDITS // " + d.credits.ToString("N0") + " CR";

            if (upgradeButtons != null)
            {
                for (int i = 0; i < upgradeButtons.Length && i < UpgradeKeys.Length; i++)
                {
                    int rank = save.GetUpgradeRank(UpgradeKeys[i]);
                    int cost = save.GetUpgradeCost(rank);
                    if (upgradeRankLabels != null && i < upgradeRankLabels.Length && upgradeRankLabels[i] != null)
                    {
                        upgradeRankLabels[i].text = $"{UpgradeTitles[i]}   [LV {rank}/{SaveManager.MaxUpgradeRank}]";
                    }
                    if (upgradeCostLabels != null && i < upgradeCostLabels.Length && upgradeCostLabels[i] != null)
                    {
                        upgradeCostLabels[i].text = rank >= SaveManager.MaxUpgradeRank ? "MAXED" : $"{cost} CR";
                    }
                    if (upgradeButtons[i] != null)
                    {
                        upgradeButtons[i].interactable = rank < SaveManager.MaxUpgradeRank && d.credits >= cost;
                    }
                }
            }

            if (shopWeaponButtons != null)
            {
                for (int i = 0; i < shopWeaponButtons.Length && i < WeaponNames.Length; i++)
                {
                    bool unlocked = save.IsWeaponUnlocked(i);
                    bool equipped = d.selectedWeapon == i;
                    if (shopWeaponLabels != null && i < shopWeaponLabels.Length && shopWeaponLabels[i] != null)
                    {
                        shopWeaponLabels[i].text = equipped
                            ? $"{WeaponNames[i]}\n[EQUIPPED]"
                            : (unlocked ? $"{WeaponNames[i]}\n[EQUIP]" : $"{WeaponNames[i]}\n[{WeaponCosts[i]} CR]");
                    }
                    if (shopWeaponButtons[i] != null)
                    {
                        Image bg = shopWeaponButtons[i].GetComponent<Image>();
                        if (bg != null)
                        {
                            bg.color = equipped ? Palette.Accent : (unlocked ? Palette.SecondaryButtonBg : Palette.Shadow);
                        }
                        if (shopWeaponLabels != null && i < shopWeaponLabels.Length && shopWeaponLabels[i] != null)
                        {
                            shopWeaponLabels[i].color = equipped ? Palette.Shadow : Palette.CharacterSecondary;
                        }
                        shopWeaponButtons[i].interactable = unlocked || d.credits >= WeaponCosts[i];
                    }
                }
            }

            if (shopSkinButtons != null)
            {
                for (int i = 0; i < shopSkinButtons.Length && i < SkinNames.Length; i++)
                {
                    bool unlocked = save.IsSkinUnlocked(i);
                    bool equipped = d.selectedSkin == i;
                    if (shopSkinLabels != null && i < shopSkinLabels.Length && shopSkinLabels[i] != null)
                    {
                        shopSkinLabels[i].text = equipped
                            ? $"{SkinNames[i]} [ON]"
                            : (unlocked ? $"{SkinNames[i]} [USE]" : $"{SkinNames[i]} [{SkinCosts[i]} CR]");
                    }
                    if (shopSkinButtons[i] != null)
                    {
                        Image bg = shopSkinButtons[i].GetComponent<Image>();
                        if (bg != null)
                        {
                            bg.color = equipped ? Palette.CyanGlow : (unlocked ? Palette.SecondaryButtonBg : Palette.Shadow);
                        }
                        if (shopSkinLabels != null && i < shopSkinLabels.Length && shopSkinLabels[i] != null)
                        {
                            shopSkinLabels[i].color = equipped ? Palette.Shadow : Palette.CharacterSecondary;
                        }
                        shopSkinButtons[i].interactable = unlocked || d.credits >= SkinCosts[i];
                    }
                }
            }
        }

        public void PresentDraftOptions(List<RogueliteSkillInfo> options, PlayerController player)
        {
            currentDraftOptions = options ?? new List<RogueliteSkillInfo>();
            if (draftCardButtons == null) return;

            for (int i = 0; i < draftCardButtons.Length; i++)
            {
                bool valid = i < currentDraftOptions.Count;
                if (draftCardButtons[i] != null) draftCardButtons[i].gameObject.SetActive(valid);
                if (!valid) continue;

                RogueliteSkillInfo info = currentDraftOptions[i];
                int currentRank = player != null ? player.GetSkillRank(info.Id) : 0;

                if (draftTitles != null && i < draftTitles.Length && draftTitles[i] != null)
                {
                    draftTitles[i].text = info.Title;
                }
                if (draftRanks != null && i < draftRanks.Length && draftRanks[i] != null)
                {
                    draftRanks[i].text = $"MODULE LV {currentRank + 1}";
                }
                if (draftDescs != null && i < draftDescs.Length && draftDescs[i] != null)
                {
                    draftDescs[i].text = info.Description;
                }
            }
        }

        public void PopulateEndedSummary(bool victory, int score, int highScore, int kills, int wave, bool isNewRecord)
        {
            if (endedTitleText != null) endedTitleText.text = victory ? "ARENA SECURED" : "SIGNAL LOST";
            if (endedScoreText != null) endedScoreText.text = score.ToString("N0") + " PTS";
            if (endedBestText != null) endedBestText.text = isNewRecord ? "NEW RECORD // " + highScore.ToString("N0") : "BEST // " + highScore.ToString("N0");
            if (endedStatsText != null) endedStatsText.text = "ELIMINATIONS: " + kills + "   |   WAVE REACHED: " + wave;
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
