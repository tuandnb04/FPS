using System.Collections;
using System.Collections.Generic;
using Audio;
using Camera;
using Character;
using Core;
using Enemies;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI
{
    public enum GameState
    {
        Title,
        Playing,
        Paused,
        Ended,
        Armory,
        LevelUpDraft,
        Cutscene
    }

    /// <summary>
    /// Commercial-grade 6-state UI & Game Shell controller supporting Title, Armory Shop,
    /// HUD (with Boss Bar, 4-Weapon Loadout, & Twin-Stick Mobile Controls), LevelUpDraft, Paused, and Ended.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private static readonly string[] WeaponNames = { "M4A1 CARBINE", "M870 SHOTGUN", "MK14 SNIPER", "RPG-7 LAUNCHER", "AN-94 ABAKAN", "VECTOR .45 SMG" };
        private static readonly string[] WeaponShortTags = { "5.56 AUTO", "12G PUMP", "AP PIERCE", "HE ROCKET", "2X BURST", "1100 RPM" };
        private static readonly int[] WeaponCosts = { 0, 150, 300, 450, 140, 200 };
        private static readonly string[] SkinNames = { "RANGER OLIVE", "DESERT TAN", "NIGHT OPS" };
        private static readonly int[] SkinCosts = { 0, 180, 280 };
        private static readonly string[] UpgradeKeys = { "armor", "firepower", "thruster", "greed" };
        private static readonly string[] UpgradeTitles = { "KEVLAR VEST (+20 HP)", "FMJ AMMO (+12% DMG)", "MARATHON PRO (+SPD)", "SCAVENGER (+18% CR)" };

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
        [SerializeField] private GameObject mobileTouchControlsRoot;
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
        [SerializeField] private TextMeshProUGUI endedNarrativeText;
        [SerializeField] private Image endedAccentStripe;

        [Header("Story Radio & Intel Dossier")]
        [SerializeField] private CanvasGroup radioCommsGroup;
        [SerializeField] private Image radioAccentStripe;
        [SerializeField] private TextMeshProUGUI radioCallsignText;
        [SerializeField] private TextMeshProUGUI radioBodyText;
        [SerializeField] private CanvasGroup intelGroup;
        [SerializeField] private TextMeshProUGUI intelBodyText;
        [SerializeField] private Button titleIntelButton;
        [SerializeField] private Button intelCloseButton;

        [Header("CoD Damage Vignette & Context Prompt")]
        [SerializeField] private Image damageVignetteImage;
        [SerializeField] private CanvasGroup contextPromptGroup;
        [SerializeField] private TextMeshProUGUI contextPromptText;

        [Header("Tactical Radar Minimap")]
        [SerializeField] private RectTransform radarContainerRect;
        [SerializeField] private RectTransform radarPlayerChevron;
        [SerializeField] private Image[] radarSquadDots;
        [SerializeField] private Image[] radarEnemyDots;
        [SerializeField] private Image[] radarObjectiveDots;
        [SerializeField] private TextMeshProUGUI radarUavStatusText;

        [Header("Fireteam Bravo-6 Squad Roster & Orders")]
        [SerializeField] private TextMeshProUGUI[] squadMemberNames;
        [SerializeField] private Image[] squadMemberBars;
        [SerializeField] private TextMeshProUGUI[] squadMemberStatusTexts;
        [SerializeField] private Button[] squadOrderButtons;
        [SerializeField] private Image[] squadOrderBgs;

        [Header("Tactical Equipment & Killstreaks")]
        [SerializeField] private TextMeshProUGUI fragCountText;
        [SerializeField] private TextMeshProUGUI strikeStatusText;
        [SerializeField] private TextMeshProUGUI streakText;
        [SerializeField] private Button fragButton;
        [SerializeField] private Button strikeButton;

        [Header("Cutscene Cinema Letterbox & Objective")]
        [SerializeField] private CanvasGroup cinemaOverlayGroup;
        [SerializeField] private TextMeshProUGUI cinemaStampText;
        [SerializeField] private TextMeshProUGUI cinemaSpeakerText;
        [SerializeField] private TextMeshProUGUI cinemaDialogueText;
        [SerializeField] private Button cinemaSkipButton;
        [SerializeField] private TextMeshProUGUI objectiveBannerText;

        [Header("Modern Warfare 4 Tactical Combat HUD")]
        [SerializeField] private CanvasGroup hitmarkerGroup;
        [SerializeField] private Image[] hitmarkerTicks;
        [SerializeField] private TextMeshProUGUI killFeedText;
        [SerializeField] private TextMeshProUGUI ammoHudText;
        [SerializeField] private Image reloadProgressFill;
        [SerializeField] private CanvasGroup nvgOverlayGroup;
        [SerializeField] private TextMeshProUGUI nvgStatusText;
        [SerializeField] private Button nvgToggleButton;

        [Header("Core Navigation Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button hudPauseButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button pauseRestartButton;
        [SerializeField] private Button pauseTitleButton;
        [SerializeField] private Button endedReplayButton;
        [SerializeField] private Button endedTitleButton;

        private Coroutine _hpTween;
        private Coroutine _xpTween;
        private Coroutine _transitionTween;
        private Coroutine _milestoneRoutine;
        private Coroutine _radioRoutine;
        private bool _touchDashQueued;
        private float _hitmarkerTimer;
        private float _hitmarkerDuration = 0.12f;
        private float _killFeedClearTimer;
        private readonly string[] _killFeedLines = new string[3];
        private int _killFeedCursor;
        private List<RogueliteSkillInfo> _currentDraftOptions = new();

        private int HitmarkerFlashCount { get; set; }
        private int KillFeedCount { get; set; }
        public string LastKillFeedLine { get; private set; } = string.Empty;
        public string CurrentAmmoText { get; private set; } = "30 // INF";
        public bool IsNvgHudActive { get; private set; }

        public float HpBarFill => hpFillImage != null ? hpFillImage.fillAmount : 0f;
        public float XpBarFill => xpFillImage != null ? xpFillImage.fillAmount : 0f;
        public float TitleAlpha => titleGroup != null ? titleGroup.alpha : 0f;
        public float HudAlpha => hudGroup != null ? hudGroup.alpha : 0f;
        public float PauseAlpha => pauseGroup != null ? pauseGroup.alpha : 0f;
        public float EndedAlpha => endedGroup != null ? endedGroup.alpha : 0f;
        public float ArmoryAlpha => armoryGroup != null ? armoryGroup.alpha : 0f;
        public float DraftAlpha => draftGroup != null ? draftGroup.alpha : 0f;
        public float IntelAlpha => intelGroup != null ? intelGroup.alpha : 0f;
        public bool IsIntelOpen => intelGroup != null && intelGroup.alpha > 0.5f;
        public bool IsRadioCommsVisible => radioCommsGroup != null && radioCommsGroup.alpha > 0.05f;
        public string LastDisplayedRadioCallsign { get; private set; } = string.Empty;
        public string LastDisplayedRadioBody { get; private set; } = string.Empty;
        public bool IsCinemaOverlayVisible => cinemaOverlayGroup != null && cinemaOverlayGroup.alpha > 0.5f;
        public string CurrentObjectiveText => objectiveBannerText != null ? objectiveBannerText.text : string.Empty;
        public bool IsBossBarVisible => bossBarRoot != null && bossBarRoot.gameObject.activeSelf;
        public int RadarEnemyActiveCount { get; private set; }
        public float DamageVignetteAlpha => damageVignetteImage != null ? damageVignetteImage.color.a : 0f;
        public SquadOrder CurrentOrderDisplayed { get; private set; } = SquadOrder.Follow;

        public static bool? ForceMobileControlsOverride { get; set; }
        public VirtualJoystick LeftJoystick => leftJoystick;
        public VirtualJoystick RightJoystick => rightJoystick;
        public bool IsMobileTouchControlsVisible => mobileTouchControlsRoot != null && mobileTouchControlsRoot.activeSelf;

        private static bool ShouldShowMobileControls()
        {
            if (ForceMobileControlsOverride.HasValue)
            {
                return ForceMobileControlsOverride.Value;
            }
            return Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;
        }

        public void RefreshPlatformControls()
        {
            if (mobileTouchControlsRoot == null && hudGroup != null)
            {
                var found = hudGroup.transform.Find("MobileTouchControls");
                if (found != null) mobileTouchControlsRoot = found.gameObject;
            }
            if (mobileTouchControlsRoot != null)
            {
                mobileTouchControlsRoot.SetActive(ShouldShowMobileControls());
            }
        }

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
            Button[] drftBtns, TextMeshProUGUI[] drftTitles, TextMeshProUGUI[] drftRanks, TextMeshProUGUI[] drftDescs,
            GameObject touchControlsRoot = null)
        {
            weaponSlotButtons = wButtons;
            weaponSlotBgs = wBgs;
            weaponSlotLabels = wLabels;
            mobileTouchControlsRoot = touchControlsRoot;
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
            RefreshPlatformControls();
        }

        public void BindStoryWidgets(
            CanvasGroup radioGroup,
            Image radioStripe,
            TextMeshProUGUI radioCallsign,
            TextMeshProUGUI radioBody,
            CanvasGroup intelModalGroup,
            TextMeshProUGUI intelText,
            Button intelOpenBtn,
            Button intelCloseBtn,
            TextMeshProUGUI endedNarrative,
            Image endedAccent = null)
        {
            radioCommsGroup = radioGroup;
            radioAccentStripe = radioStripe;
            radioCallsignText = radioCallsign;
            radioBodyText = radioBody;
            intelGroup = intelModalGroup;
            intelBodyText = intelText;
            titleIntelButton = intelOpenBtn;
            intelCloseButton = intelCloseBtn;
            endedNarrativeText = endedNarrative;
            endedAccentStripe = endedAccent;
        }

        public void BindCutsceneWidgets(
            CanvasGroup cinemaGroup,
            TextMeshProUGUI stampText,
            TextMeshProUGUI speakerText,
            TextMeshProUGUI dialogueText,
            Button skipButton,
            TextMeshProUGUI objectiveText)
        {
            cinemaOverlayGroup = cinemaGroup;
            cinemaStampText = stampText;
            cinemaSpeakerText = speakerText;
            cinemaDialogueText = dialogueText;
            cinemaSkipButton = skipButton;
            objectiveBannerText = objectiveText;
        }

        public void BindTacticalSquadWidgets(
            Image vignetteImg,
            CanvasGroup ctxGroup,
            TextMeshProUGUI ctxText,
            RectTransform radarRect,
            RectTransform playerChevron,
            Image[] squadDots,
            Image[] enemyDots,
            Image[] objectiveDots,
            TextMeshProUGUI uavStatusText,
            TextMeshProUGUI[] sqNames,
            Image[] sqBars,
            TextMeshProUGUI[] sqStatuses,
            Button[] orderBtns,
            Image[] orderBgs,
            TextMeshProUGUI fragTxt,
            TextMeshProUGUI strikeTxt,
            TextMeshProUGUI streakTxt,
            Button fragBtn,
            Button strikeBtn)
        {
            damageVignetteImage = vignetteImg;
            contextPromptGroup = ctxGroup;
            contextPromptText = ctxText;
            radarContainerRect = radarRect;
            radarPlayerChevron = playerChevron;
            radarSquadDots = squadDots;
            radarEnemyDots = enemyDots;
            radarObjectiveDots = objectiveDots;
            radarUavStatusText = uavStatusText;
            squadMemberNames = sqNames;
            squadMemberBars = sqBars;
            squadMemberStatusTexts = sqStatuses;
            squadOrderButtons = orderBtns;
            squadOrderBgs = orderBgs;
            fragCountText = fragTxt;
            strikeStatusText = strikeTxt;
            streakText = streakTxt;
            fragButton = fragBtn;
            strikeButton = strikeBtn;

            if (damageVignetteImage != null)
            {
                if (damageVignetteImage.sprite == null)
                {
                    damageVignetteImage.sprite = UISpriteFactory.GetDamageVignetteSprite();
                }
                damageVignetteImage.raycastTarget = false;
                damageVignetteImage.color = Palette.WithAlpha(Palette.DamageBloodRed, 0f);
            }
            if (radarPlayerChevron != null)
            {
                var chevImg = radarPlayerChevron.GetComponent<Image>();
                if (chevImg != null && chevImg.sprite == null)
                {
                    chevImg.sprite = UISpriteFactory.GetChevronIcon();
                }
            }
            if (contextPromptGroup != null)
            {
                contextPromptGroup.alpha = 0f;
                contextPromptGroup.blocksRaycasts = false;
            }
            UpdateSquadOrderUI(SquadOrder.Follow);
        }

        public void BindMw4HudWidgets(
            CanvasGroup hmGroup,
            Image[] hmTicks,
            TextMeshProUGUI killFeedTxt,
            TextMeshProUGUI ammoTxt,
            Image reloadFill,
            CanvasGroup nvgGroup,
            TextMeshProUGUI nvgTxt,
            Button nvgBtn)
        {
            hitmarkerGroup = hmGroup;
            hitmarkerTicks = hmTicks;
            killFeedText = killFeedTxt;
            ammoHudText = ammoTxt;
            reloadProgressFill = reloadFill;
            nvgOverlayGroup = nvgGroup;
            nvgStatusText = nvgTxt;
            nvgToggleButton = nvgBtn;

            if (hitmarkerGroup != null)
            {
                hitmarkerGroup.alpha = 0f;
                hitmarkerGroup.blocksRaycasts = false;
            }
            if (nvgOverlayGroup != null)
            {
                nvgOverlayGroup.alpha = 0f;
                nvgOverlayGroup.blocksRaycasts = false;
            }
            if (killFeedText != null) killFeedText.text = string.Empty;
            UpdateAmmoHud(30, 30, false, 1f);
            SetNvgActive(false);
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

        private void OnEnable()
        {
            StoryDirector.OnRadioTransmission -= HandleRadioTransmission;
            StoryDirector.OnRadioTransmission += HandleRadioTransmission;
        }

        private void OnDisable()
        {
            StoryDirector.OnRadioTransmission -= HandleRadioTransmission;
        }

        private void Start()
        {
            EnsureBarSprites();
            WireButtonsAndSliders();
            RefreshPlatformControls();
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
            var rectSprite = UISpriteFactory.GetRoundedRectSprite();
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
                startButton.onClick.AddListener(() => GameManager.Instance?.DeployCampaign());
            }
            if (cinemaSkipButton != null)
            {
                cinemaSkipButton.onClick.RemoveAllListeners();
                cinemaSkipButton.onClick.AddListener(() => CutsceneDirector.Instance?.SkipCurrentCutscene());
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
            if (nvgToggleButton != null)
            {
                nvgToggleButton.onClick.RemoveAllListeners();
                nvgToggleButton.onClick.AddListener(() => PlayerController.Instance?.ToggleNightVision());
            }
            if (titleIntelButton != null)
            {
                titleIntelButton.onClick.RemoveAllListeners();
                titleIntelButton.onClick.AddListener(OpenIntel);
            }
            if (intelCloseButton != null)
            {
                intelCloseButton.onClick.RemoveAllListeners();
                intelCloseButton.onClick.AddListener(CloseIntel);
            }

            if (touchDashButton != null)
            {
                touchDashButton.onClick.RemoveAllListeners();
                touchDashButton.onClick.AddListener(() => _touchDashQueued = true);
            }

            if (weaponSlotButtons != null)
            {
                for (var i = 0; i < weaponSlotButtons.Length; i++)
                {
                    var idx = i;
                    if (weaponSlotButtons[i] == null) continue;
                    weaponSlotButtons[i].onClick.RemoveAllListeners();
                    weaponSlotButtons[i].onClick.AddListener(() => PlayerController.Instance?.TrySwitchWeapon(idx));
                }
            }

            if (squadOrderButtons != null)
            {
                for (var i = 0; i < squadOrderButtons.Length; i++)
                {
                    var ord = (SquadOrder)i;
                    if (squadOrderButtons[i] == null) continue;
                    squadOrderButtons[i].onClick.RemoveAllListeners();
                    squadOrderButtons[i].onClick.AddListener(() => SquadDirector.Instance?.SetOrder(ord));
                }
            }

            if (fragButton != null)
            {
                fragButton.onClick.RemoveAllListeners();
                fragButton.onClick.AddListener(() => PlayerController.Instance?.TryThrowFragGrenade());
            }

            if (strikeButton != null)
            {
                strikeButton.onClick.RemoveAllListeners();
                strikeButton.onClick.AddListener(() => PlayerController.Instance?.TryCallMortarStrike());
            }

            if (upgradeButtons != null)
            {
                for (var i = 0; i < upgradeButtons.Length; i++)
                {
                    var idx = i;
                    if (upgradeButtons[i] == null) continue;
                    upgradeButtons[i].onClick.RemoveAllListeners();
                    upgradeButtons[i].onClick.AddListener(() => OnUpgradeClicked(idx));
                }
            }

            if (shopWeaponButtons != null)
            {
                for (var i = 0; i < shopWeaponButtons.Length; i++)
                {
                    var idx = i;
                    if (shopWeaponButtons[i] == null) continue;
                    shopWeaponButtons[i].onClick.RemoveAllListeners();
                    shopWeaponButtons[i].onClick.AddListener(() => OnShopWeaponClicked(idx));
                }
            }

            if (shopSkinButtons != null)
            {
                for (var i = 0; i < shopSkinButtons.Length; i++)
                {
                    var idx = i;
                    if (shopSkinButtons[i] == null) continue;
                    shopSkinButtons[i].onClick.RemoveAllListeners();
                    shopSkinButtons[i].onClick.AddListener(() => OnShopSkinClicked(idx));
                }
            }

            if (draftCardButtons != null)
            {
                for (var i = 0; i < draftCardButtons.Length; i++)
                {
                    var idx = i;
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

            if (sfxSlider == null) return;
            {
                sfxSlider.onValueChanged.RemoveAllListeners();
                if (AudioManager.Instance != null) sfxSlider.SetValueWithoutNotify(AudioManager.Instance.SfxVol);
                sfxSlider.onValueChanged.AddListener(v =>
                {
                    AudioManager.Instance?.SetSfxVolume(v);
                    RefreshVolumeLabels();
                });
            }
        }

        private void OnUpgradeClicked(int index)
        {
            if (index < 0 || index >= UpgradeKeys.Length) return;
            var save = SaveManager.EnsureInstance();
            if (!save.TryUpgradeStat(UpgradeKeys[index])) return;
            AudioManager.Instance?.PlayPickupBlip();
            RefreshArmoryUI();
        }

        private void OnShopWeaponClicked(int index)
        {
            if (index < 0 || index >= WeaponCosts.Length) return;
            var save = SaveManager.EnsureInstance();
            if (!save.TryUnlockOrEquipWeapon(index, WeaponCosts[index])) return;
            AudioManager.Instance?.PlayUIClick();
            RefreshArmoryUI();
            RefreshWeaponBar();
        }

        private void OnShopSkinClicked(int index)
        {
            if (index < 0 || index >= SkinCosts.Length) return;
            var save = SaveManager.EnsureInstance();
            if (!save.TryUnlockOrEquipSkin(index, SkinCosts[index])) return;
            AudioManager.Instance?.PlayUIClick();
            PlayerController.Instance?.ApplySaveLoadout();
            RefreshArmoryUI();
        }

        private void OnDraftCardClicked(int index)
        {
            if (index < 0 || index >= _currentDraftOptions.Count) return;
            GameManager.Instance?.SelectDraftSkill(_currentDraftOptions[index].Id);
        }

        public bool ConsumeTouchDashPressed()
        {
            var pressed = _touchDashQueued;
            _touchDashQueued = false;
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
            var unscaledDt = Time.unscaledDeltaTime;
            if (crosshairRect != null)
            {
                var showCrosshair = !ShouldShowMobileControls() && hudGroup != null && hudGroup.alpha > 0.5f && Mouse.current != null;
                if (crosshairRect.gameObject.activeSelf != showCrosshair)
                {
                    crosshairRect.gameObject.SetActive(showCrosshair);
                }
                if (showCrosshair)
                {
                    crosshairRect.position = Mouse.current.position.ReadValue();
                }
            }

            if (_hitmarkerTimer > 0f && hitmarkerGroup != null)
            {
                _hitmarkerTimer = Mathf.Max(0f, _hitmarkerTimer - unscaledDt);
                var t = _hitmarkerDuration > 0.01f ? _hitmarkerTimer / _hitmarkerDuration : 0f;
                hitmarkerGroup.alpha = t;
                if (crosshairRect != null && crosshairRect.gameObject.activeSelf)
                {
                    hitmarkerGroup.transform.position = crosshairRect.position;
                }
            }

            if (_killFeedClearTimer > 0f)
            {
                _killFeedClearTimer -= unscaledDt;
                if (_killFeedClearTimer <= 0f)
                {
                    ClearKillFeed();
                }
            }

            if (hudGroup == null || !(hudGroup.alpha > 0.1f)) return;
            UpdateSquadRosterHud();
            UpdateTacticalRadar();
        }

        public void ApplyImmediateState(GameState state)
        {
            RefreshPlatformControls();
            if (_transitionTween != null)
            {
                StopCoroutine(_transitionTween);
                _transitionTween = null;
            }
            SetGroupState(titleGroup, state == GameState.Title ? 1f : 0f);
            SetGroupState(hudGroup, state == GameState.Playing ? 1f : 0f);
            SetGroupState(pauseGroup, state == GameState.Paused ? 1f : 0f);
            SetGroupState(endedGroup, state == GameState.Ended ? 1f : 0f);
            SetGroupState(armoryGroup, state == GameState.Armory ? 1f : 0f);
            SetGroupState(draftGroup, state == GameState.LevelUpDraft ? 1f : 0f);
            SetGroupState(intelGroup, 0f);
            if (milestoneBannerGroup != null)
            {
                milestoneBannerGroup.alpha = 0f;
            }
            if (state != GameState.Playing)
            {
                HideRadioCommsImmediate();
                UpdateDamageVignette(0f);
                UpdateContextPrompt(string.Empty);
                RadarEnemyActiveCount = 0;
            }
            if (state != GameState.Cutscene)
            {
                HideCutsceneOverlayImmediate();
            }
            HideBossHp();
        }

        public void TransitionToState(GameState state)
        {
            SetGroupState(intelGroup, 0f);
            RefreshPlatformControls();
            if (_transitionTween != null)
            {
                StopCoroutine(_transitionTween);
            }
            _transitionTween = StartCoroutine(TransitionRoutine(state));
        }

        private IEnumerator TransitionRoutine(GameState target)
        {
            var sTitle = titleGroup != null ? titleGroup.alpha : 0f;
            var sHud = hudGroup != null ? hudGroup.alpha : 0f;
            var sPause = pauseGroup != null ? pauseGroup.alpha : 0f;
            var sEnded = endedGroup != null ? endedGroup.alpha : 0f;
            var sArmory = armoryGroup != null ? armoryGroup.alpha : 0f;
            var sDraft = draftGroup != null ? draftGroup.alpha : 0f;

            var eTitle = target == GameState.Title ? 1f : 0f;
            var eHud = target == GameState.Playing ? 1f : 0f;
            var ePause = target == GameState.Paused ? 1f : 0f;
            var eEnded = target == GameState.Ended ? 1f : 0f;
            var eArmory = target == GameState.Armory ? 1f : 0f;
            var eDraft = target == GameState.LevelUpDraft ? 1f : 0f;

            SetGroupInteractable(titleGroup, eTitle > 0.5f);
            SetGroupInteractable(hudGroup, eHud > 0.5f);
            SetGroupInteractable(pauseGroup, ePause > 0.5f);
            SetGroupInteractable(endedGroup, eEnded > 0.5f);
            SetGroupInteractable(armoryGroup, eArmory > 0.5f);
            SetGroupInteractable(draftGroup, eDraft > 0.5f);

            yield return Ease.Tween(0.25f, t =>
            {
                var e = Ease.OutCubic(t);
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
            _transitionTween = null;
        }

        private static void SetGroupState(CanvasGroup cg, float alpha)
        {
            if (cg == null) return;
            cg.alpha = alpha;
            var active = alpha > 0.5f;
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
            var targetFill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (hpValueText != null)
            {
                hpValueText.text = Mathf.CeilToInt(current) + " / " + Mathf.RoundToInt(max);
            }

            if (hpFillImage == null) return;
            if (_hpTween != null) StopCoroutine(_hpTween);
            var start = hpFillImage.fillAmount;
            _hpTween = StartCoroutine(TweenImageFill(hpFillImage, start, targetFill, 0.30f));
        }

        public void UpdateXp(int current, int max, int level)
        {
            var targetFill = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            if (levelText != null)
            {
                levelText.text = "RANK " + level.ToString("00");
            }

            if (xpFillImage == null) return;
            if (_xpTween != null) StopCoroutine(_xpTween);
            var start = xpFillImage.fillAmount;
            _xpTween = StartCoroutine(TweenImageFill(xpFillImage, start, targetFill, 0.30f));
        }

        public void UpdateDashCooldown(float readyNormalized)
        {
            if (dashFillImage != null)
            {
                dashFillImage.fillAmount = Mathf.Clamp01(readyNormalized);
            }
        }

        public void UpdateMissionStatus(string actTitle, int activeHostiles, string objectiveText)
        {
            if (waveText != null) waveText.text = actTitle;
            if (enemiesText != null)
            {
                enemiesText.text = activeHostiles > 0
                    ? (activeHostiles + " HOSTILES IN SECTOR")
                    : "SECTOR CLEAR // ADVANCE";
                enemiesText.color = activeHostiles > 0 ? Palette.EnemySecondary : Palette.HealthGreen;
            }
            UpdateObjective(objectiveText);
        }

        public void UpdateWave(int wave, int enemiesRemaining)
        {
            UpdateMissionStatus(
                StoryDirector.GetActLabel(wave),
                enemiesRemaining,
                StoryDirector.GetSectorObjective(wave));
        }

        public void UpdateContextPrompt(string text)
        {
            if (contextPromptGroup == null) return;
            var visible = !string.IsNullOrEmpty(text);
            contextPromptGroup.alpha = visible ? 1f : 0f;
            if (visible && contextPromptText != null)
            {
                contextPromptText.text = text;
            }
        }

        public void UpdateEquipmentHud(int fragCount, int mortarCharges, int streak, bool uavOnline, bool isRegen)
        {
            if (fragCountText != null)
            {
                fragCountText.text = "[G] FRAG x" + fragCount;
                fragCountText.color = fragCount > 0 ? Palette.Accent : Palette.MutedText;
            }
            if (strikeStatusText != null)
            {
                strikeStatusText.text = mortarCharges > 0
                    ? ("[F] MORTAR x" + mortarCharges + " RDY")
                    : ("[F] MORTAR (" + Mathf.Min(streak, 5) + "/5)");
                strikeStatusText.color = mortarCharges > 0 ? Palette.HealthGreen : Palette.MutedText;
            }
            if (streakText != null)
            {
                streakText.text = uavOnline
                    ? ("STREAK " + streak + " // UAV LIVE")
                    : ("STREAK " + streak + " // UAV 3K");
                streakText.color = uavOnline ? Palette.RadarGreen : Palette.CharacterSecondary;
            }
            if (levelText != null)
            {
                levelText.text = uavOnline
                    ? ("UAV RECON // " + streak + " KILLS")
                    : (mortarCharges > 0 ? ("STRIKE READY // " + streak + " KILLS") : ("KILLSTREAK // " + streak + " KILLS"));
                levelText.color = mortarCharges > 0 ? Palette.HealthGreen : (uavOnline ? Palette.RadarGreen : Palette.Accent);
            }
            if (xpFillImage != null)
            {
                if (_xpTween != null)
                {
                    StopCoroutine(_xpTween);
                    _xpTween = null;
                }
                xpFillImage.fillAmount = Mathf.Clamp01(streak / 5f);
                xpFillImage.color = uavOnline ? Palette.RadarGreen : Palette.Accent;
            }
            if (radarUavStatusText != null)
            {
                radarUavStatusText.text = uavOnline ? "UAV SWEEP // LIVE" : "TAC-RADAR // HAJIN DMZ";
                radarUavStatusText.color = uavOnline ? Palette.RadarGreen : Palette.MutedText;
            }
            if (hpFillImage != null && isRegen)
            {
                hpFillImage.color = Palette.HealthGreen;
            }
        }

        public void FlashHitmarker(bool isKill)
        {
            HitmarkerFlashCount++;
            _hitmarkerDuration = isKill ? 0.19f : 0.12f;
            _hitmarkerTimer = _hitmarkerDuration;
            if (hitmarkerGroup != null)
            {
                hitmarkerGroup.alpha = 1f;
                if (crosshairRect != null && crosshairRect.gameObject.activeSelf)
                {
                    hitmarkerGroup.transform.position = crosshairRect.position;
                }
                else
                {
                    ((RectTransform)hitmarkerGroup.transform).anchoredPosition = Vector2.zero;
                }
            }

            if (hitmarkerTicks == null) return;
            var col = isKill ? Palette.HitmarkerCrimson : Palette.HitmarkerWhite;
            foreach (var t in hitmarkerTicks)
            {
                if (t != null) t.color = col;
            }
        }

        public void PushKillFeed(string killerCallsign, string victimLabel, string weaponTag)
        {
            KillFeedCount++;
            var entry = $"[{weaponTag}]  {killerCallsign}  >>  {victimLabel}";
            LastKillFeedLine = entry;
            _killFeedLines[_killFeedCursor % _killFeedLines.Length] = entry;
            _killFeedCursor++;
            _killFeedClearTimer = 4.2f;

            if (killFeedText == null) return;
            var l0 = _killFeedLines[(_killFeedCursor - 1 + 3) % 3];
            var l1 = _killFeedLines[(_killFeedCursor - 2 + 3) % 3];
            var l2 = _killFeedLines[(_killFeedCursor - 3 + 3) % 3];
            killFeedText.text = string.IsNullOrEmpty(l1)
                ? l0
                : (string.IsNullOrEmpty(l2) ? (l1 + "\n" + l0) : (l2 + "\n" + l1 + "\n" + l0));
        }

        private void ClearKillFeed()
        {
            for (var i = 0; i < _killFeedLines.Length; i++)
            {
                _killFeedLines[i] = string.Empty;
            }
            if (killFeedText != null) killFeedText.text = string.Empty;
        }

        public void UpdateAmmoHud(int currentMag, int magCapacity, bool isReloading, float reloadProgress)
        {
            CurrentAmmoText = isReloading ? "RELOADING..." : (currentMag + " // INF");
            if (ammoHudText != null)
            {
                ammoHudText.text = isReloading ? ("RELOADING  " + Mathf.RoundToInt(reloadProgress * 100f) + "%") : (currentMag + " / " + magCapacity + "  [R]");
                ammoHudText.color = isReloading ? Palette.C4Amber : (currentMag <= Mathf.Max(2, magCapacity / 5) ? Palette.EnemySecondary : Palette.CharacterSecondary);
            }

            if (reloadProgressFill == null) return;
            reloadProgressFill.fillAmount = isReloading ? reloadProgress : (magCapacity > 0 ? (float)currentMag / magCapacity : 1f);
            reloadProgressFill.color = isReloading ? Palette.C4Amber : Palette.Accent;
        }

        public void SetNvgActive(bool active)
        {
            IsNvgHudActive = active;
            if (nvgOverlayGroup != null)
            {
                nvgOverlayGroup.alpha = active ? 0.85f : 0f;
            }

            if (nvgStatusText == null) return;
            nvgStatusText.text = active ? "[N] NVG: ON" : "[N] NVG: OFF";
            nvgStatusText.color = active ? Palette.NvgPhosphor : Palette.MutedText;
        }

        public void UpdateSquadOrderUI(SquadOrder order)
        {
            CurrentOrderDisplayed = order;
            if (squadOrderBgs == null) return;
            for (var i = 0; i < squadOrderBgs.Length; i++)
            {
                if (squadOrderBgs[i] == null) continue;
                var active = i == (int)order;
                squadOrderBgs[i].color = active
                    ? (order == SquadOrder.Breach ? Palette.C4Amber : Palette.SquadBlue)
                    : Palette.SecondaryButtonBg;
            }
        }

        public void UpdateDamageVignette(float alpha)
        {
            if (damageVignetteImage == null) return;
            if (damageVignetteImage.sprite == null)
            {
                damageVignetteImage.sprite = UISpriteFactory.GetDamageVignetteSprite();
            }
            damageVignetteImage.color = Palette.WithAlpha(Palette.DamageBloodRed, Mathf.Clamp01(alpha));
        }

        private void UpdateSquadRosterHud()
        {
            var squad = SquadDirector.Instance;
            if (squad == null || squadMemberBars == null) return;
            var members = squad.Members;
            for (var i = 0; i < squadMemberBars.Length; i++)
            {
                var hasMember = members != null && i < members.Count && members[i] != null;
                if (!hasMember) continue;

                var m = members[i];
                if (squadMemberNames != null && i < squadMemberNames.Length && squadMemberNames[i] != null)
                {
                    squadMemberNames[i].text = m.Callsign;
                    squadMemberNames[i].color = m.IsDowned ? Palette.C4Amber : Palette.CharacterSecondary;
                }
                if (squadMemberBars[i] != null)
                {
                    squadMemberBars[i].fillAmount = m.MaxHp > 0f ? Mathf.Clamp01(m.CurrentHp / m.MaxHp) : 0f;
                    squadMemberBars[i].color = m.IsDowned ? Palette.C4Amber : Palette.SquadBlue;
                }

                if (squadMemberStatusTexts == null || i >= squadMemberStatusTexts.Length ||
                    squadMemberStatusTexts[i] == null) continue;
                squadMemberStatusTexts[i].text = m.IsDowned
                    ? "DOWN"
                    : (Mathf.RoundToInt(100f * m.CurrentHp / Mathf.Max(1f, m.MaxHp)) + "%");
                squadMemberStatusTexts[i].color = m.IsDowned ? Palette.C4Amber : Palette.SquadBlue;
            }
        }

        private void UpdateTacticalRadar()
        {
            if (radarContainerRect == null || PlayerController.Instance == null) return;

            var player = PlayerController.Instance;
            var playerPos = player.transform.position;
            var radiusPx = Mathf.Min(radarContainerRect.rect.width, radarContainerRect.rect.height) * 0.5f - 10f;
            if (radiusPx < 24f) radiusPx = 56f;
            const float radarWorldRange = 36f;

            if (radarPlayerChevron != null)
            {
                radarPlayerChevron.anchoredPosition = Vector2.zero;
                radarPlayerChevron.localRotation = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
            }

            if (radarSquadDots != null)
            {
                var members = SquadDirector.Instance != null ? SquadDirector.Instance.Members : null;
                for (var i = 0; i < radarSquadDots.Length; i++)
                {
                    var dot = radarSquadDots[i];
                    if (dot == null) continue;
                    var show = members != null && i < members.Count && members[i] != null && members[i].gameObject.activeInHierarchy;
                    dot.gameObject.SetActive(show);
                    if (!show) continue;

                    var pos = MapWorldToRadar(members[i].transform.position, playerPos, radarWorldRange, radiusPx, true);
                    dot.rectTransform.anchoredPosition = pos;
                    dot.color = members[i].IsDowned ? Palette.C4Amber : Palette.SquadBlue;
                }
            }

            if (radarObjectiveDots != null)
            {
                var nodes = EnemySpawner.Instance != null ? EnemySpawner.Instance.ObjectiveNodes : null;
                for (var i = 0; i < radarObjectiveDots.Length; i++)
                {
                    var dot = radarObjectiveDots[i];
                    if (dot == null) continue;
                    var show = nodes != null && i < nodes.Count && nodes[i] != null && nodes[i].IsUnlocked && !nodes[i].IsCompleted;
                    dot.gameObject.SetActive(show);
                    if (!show) continue;

                    var pos = MapWorldToRadar(nodes[i].transform.position, playerPos, radarWorldRange, radiusPx, true);
                    dot.rectTransform.anchoredPosition = pos;
                    dot.color = nodes[i].Kind == ObjectiveNodeKind.HelipadEvacLZ ? Palette.RadarGreen : Palette.C4Amber;
                }
            }

            var activeEnemyBlips = 0;
            if (radarEnemyDots != null)
            {
                var enemies = EnemySpawner.Instance != null ? EnemySpawner.Instance.ActiveEnemies : null;
                var uavOnline = player.IsUavReconActive;
                var enemyIdx = 0;

                foreach (var dot in radarEnemyDots)
                {
                    if (dot == null) continue;

                    EnemyController target = null;
                    var mappedPos = Vector2.zero;
                    while (enemies != null && enemyIdx < enemies.Count)
                    {
                        var candidate = enemies[enemyIdx++];
                        if (candidate == null || !candidate.gameObject.activeInHierarchy || candidate.IsDead) continue;
                        var p = MapWorldToRadar(candidate.transform.position, playerPos, radarWorldRange, radiusPx, uavOnline);
                        if (float.IsNaN(p.x)) continue;
                        target = candidate;
                        mappedPos = p;
                        break;
                    }

                    var show = target != null;
                    dot.gameObject.SetActive(show);
                    if (!show) continue;
                    activeEnemyBlips++;
                    dot.rectTransform.anchoredPosition = mappedPos;
                    var isBoss = target.Archetype == EnemyArchetype.Boss;
                    dot.color = isBoss ? Palette.BossPurple : Palette.EnemySecondary;
                    dot.rectTransform.localScale = isBoss ? new Vector3(1.45f, 1.45f, 1f) : Vector3.one;
                }
            }
            RadarEnemyActiveCount = activeEnemyBlips;
        }

        private static Vector2 MapWorldToRadar(Vector3 worldPos, Vector3 origin, float worldRange, float radiusPx, bool clampToRim)
        {
            var delta = new Vector2(worldPos.x - origin.x, worldPos.z - origin.z) * (radiusPx / worldRange);
            var mag = delta.magnitude;
            if (!(mag > radiusPx)) return delta;
            if (!clampToRim) return new Vector2(float.NaN, float.NaN);
            delta *= (radiusPx / mag);
            return delta;
        }

        private void UpdateObjective(string objectiveText)
        {
            if (objectiveBannerText != null)
            {
                objectiveBannerText.text = objectiveText;
            }
        }

        public void ShowCutsceneOverlay()
        {
            if (cinemaOverlayGroup != null)
            {
                SetGroupState(cinemaOverlayGroup, 1f);
            }
            if (cinemaStampText != null) cinemaStampText.text = string.Empty;
            if (cinemaSpeakerText != null) cinemaSpeakerText.text = string.Empty;
            if (cinemaDialogueText != null) cinemaDialogueText.text = string.Empty;
        }

        public void HideCutsceneOverlayImmediate()
        {
            if (cinemaOverlayGroup != null)
            {
                SetGroupState(cinemaOverlayGroup, 0f);
            }
        }

        public void UpdateCutsceneText(string stamp, string speaker, string dialogue)
        {
            if (cinemaStampText != null) cinemaStampText.text = stamp;
            if (cinemaSpeakerText != null) cinemaSpeakerText.text = speaker;
            if (cinemaDialogueText != null) cinemaDialogueText.text = dialogue;
        }

        public void UpdateScore(int score, int highScore)
        {
            if (scoreText != null) scoreText.text = score.ToString("N0") + " PTS";
            if (titleHighScoreText != null) titleHighScoreText.text = "HIGH SCORE // " + highScore.ToString("N0");
        }

        public void UpdateCredits(int totalCredits, int runEarned)
        {
            if (titleCreditsText != null) titleCreditsText.text = "FUNDS // " + totalCredits.ToString("N0") + " CR";
            if (armoryCreditsText != null) armoryCreditsText.text = totalCredits.ToString("N0") + " CR";
            if (hudCreditsText != null) hudCreditsText.text = totalCredits.ToString("N0") + " CR";
            if (endedCreditsText != null) endedCreditsText.text = "EARNED +" + runEarned + " CR   |   FUNDS: " + totalCredits.ToString("N0") + " CR";
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
            var save = SaveManager.EnsureInstance();
            var selected = save.Data.selectedWeapon;

            for (var i = 0; i < weaponSlotButtons.Length && i < WeaponNames.Length; i++)
            {
                var unlocked = save.IsWeaponUnlocked(i);
                var active = i == selected;
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
            var save = SaveManager.EnsureInstance();
            var d = save.Data;

            if (armoryCreditsText != null) armoryCreditsText.text = d.credits.ToString("N0") + " CR";
            if (titleCreditsText != null) titleCreditsText.text = "FUNDS // " + d.credits.ToString("N0") + " CR";

            if (upgradeButtons != null)
            {
                for (var i = 0; i < upgradeButtons.Length && i < UpgradeKeys.Length; i++)
                {
                    var rank = save.GetUpgradeRank(UpgradeKeys[i]);
                    var cost = SaveManager.GetUpgradeCost(rank);
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
                for (var i = 0; i < shopWeaponButtons.Length && i < WeaponNames.Length; i++)
                {
                    var unlocked = save.IsWeaponUnlocked(i);
                    var equipped = d.selectedWeapon == i;
                    if (shopWeaponLabels != null && i < shopWeaponLabels.Length && shopWeaponLabels[i] != null)
                    {
                        var tag = i < WeaponShortTags.Length ? WeaponShortTags[i] : string.Empty;
                        var status = equipped ? "[EQUIPPED]" : (unlocked ? "[EQUIP]" : $"[{WeaponCosts[i]} CR]");
                        shopWeaponLabels[i].text = string.IsNullOrEmpty(tag)
                            ? $"{WeaponNames[i]}\n{status}"
                            : $"{WeaponNames[i]}\n{tag} · {status}";
                    }

                    if (shopWeaponButtons[i] == null) continue;
                    var bg = shopWeaponButtons[i].GetComponent<Image>();
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

            if (shopSkinButtons == null) return;
            {
                for (var i = 0; i < shopSkinButtons.Length && i < SkinNames.Length; i++)
                {
                    var unlocked = save.IsSkinUnlocked(i);
                    var equipped = d.selectedSkin == i;
                    if (shopSkinLabels != null && i < shopSkinLabels.Length && shopSkinLabels[i] != null)
                    {
                        shopSkinLabels[i].text = equipped
                            ? $"{SkinNames[i]}\n[EQUIPPED]"
                            : (unlocked ? $"{SkinNames[i]}\n[EQUIP]" : $"{SkinNames[i]}\n[{SkinCosts[i]} CR]");
                    }

                    if (shopSkinButtons[i] == null) continue;
                    var bg = shopSkinButtons[i].GetComponent<Image>();
                    if (bg != null)
                    {
                        bg.color = equipped ? Palette.Accent : (unlocked ? Palette.SecondaryButtonBg : Palette.Shadow);
                    }
                    if (shopSkinLabels != null && i < shopSkinLabels.Length && shopSkinLabels[i] != null)
                    {
                        shopSkinLabels[i].color = equipped ? Palette.Shadow : Palette.CharacterSecondary;
                    }
                    shopSkinButtons[i].interactable = unlocked || d.credits >= SkinCosts[i];
                }
            }
        }

        public void PresentDraftOptions(List<RogueliteSkillInfo> options, PlayerController player)
        {
            _currentDraftOptions = options ?? new List<RogueliteSkillInfo>();
            if (draftCardButtons == null) return;

            for (var i = 0; i < draftCardButtons.Length; i++)
            {
                var valid = i < _currentDraftOptions.Count;
                if (draftCardButtons[i] != null) draftCardButtons[i].gameObject.SetActive(valid);
                if (!valid) continue;

                var info = _currentDraftOptions[i];
                var currentRank = player != null ? player.GetSkillRank(info.Id) : 0;

                if (draftTitles != null && i < draftTitles.Length && draftTitles[i] != null)
                {
                    draftTitles[i].text = info.Title;
                }
                if (draftRanks != null && i < draftRanks.Length && draftRanks[i] != null)
                {
                    draftRanks[i].text = $"PERK TIER {currentRank + 1}";
                }
                if (draftDescs != null && i < draftDescs.Length && draftDescs[i] != null)
                {
                    draftDescs[i].text = info.Description;
                }
            }
        }

        public void PopulateEndedSummary(bool victory, int score, int highScore, int kills, int wave, bool isNewRecord)
        {
            if (endedTitleText != null)
            {
                endedTitleText.text = victory ? "CAMPAIGN VICTORY // SECTOR SECURED" : "K.I.A. // MISSION FAILED";
                endedTitleText.color = victory ? Palette.HealthGreen : Palette.EnemySecondary;
            }
            if (endedAccentStripe != null)
            {
                endedAccentStripe.color = victory ? Palette.HealthGreen : Palette.EnemySecondary;
            }
            if (endedScoreText != null) endedScoreText.text = score.ToString("N0") + " PTS";
            if (endedBestText != null) endedBestText.text = isNewRecord ? "NEW RECORD // " + highScore.ToString("N0") : "BEST // " + highScore.ToString("N0");
            if (endedStatsText != null) endedStatsText.text = "ELIMINATIONS: " + kills + "   |   STAGE REACHED: " + wave + " / " + StoryDirector.FinalCampaignStage;
            if (endedNarrativeText != null) endedNarrativeText.text = StoryDirector.GetEndedDebrief(victory, wave, kills);
        }

        private void OpenIntel()
        {
            AudioManager.Instance?.PlayUIClick();
            if (intelBodyText != null)
            {
                var bestWave = SaveManager.EnsureInstance().Data.highestWave;
                intelBodyText.text = StoryDirector.BuildIntelDossierText(bestWave);
            }
            SetGroupState(titleGroup, 0f);
            SetGroupState(intelGroup, 1f);
        }

        private void CloseIntel()
        {
            AudioManager.Instance?.PlayUIClick();
            SetGroupState(intelGroup, 0f);
            if (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Title)
            {
                SetGroupState(titleGroup, 1f);
            }
        }

        public void HideRadioCommsImmediate()
        {
            if (_radioRoutine != null)
            {
                StopCoroutine(_radioRoutine);
                _radioRoutine = null;
            }

            if (radioCommsGroup == null) return;
            radioCommsGroup.alpha = 0f;
            radioCommsGroup.interactable = false;
            radioCommsGroup.blocksRaycasts = false;
        }

        private void HandleRadioTransmission(string callsign, string channel, string body)
        {
            LastDisplayedRadioCallsign = callsign;
            LastDisplayedRadioBody = body;
            if (radioCommsGroup == null) return;

            if (radioCallsignText != null) radioCallsignText.text = callsign + " // " + channel;
            if (radioBodyText != null) radioBodyText.text = body;
            if (radioAccentStripe != null)
            {
                radioAccentStripe.color = callsign.Contains("VOLKOV") ? Palette.EnemySecondary : Palette.Accent;
            }

            if (_radioRoutine != null) StopCoroutine(_radioRoutine);
            _radioRoutine = StartCoroutine(RadioCommsRoutine());
        }

        private IEnumerator RadioCommsRoutine()
        {
            AudioManager.Instance?.PlayRadioChirp();
            var startAlpha = radioCommsGroup != null ? radioCommsGroup.alpha : 0f;
            yield return Ease.Tween(0.18f, t =>
            {
                if (radioCommsGroup != null) radioCommsGroup.alpha = Mathf.Lerp(startAlpha, 1f, Ease.OutCubic(t));
            }, Ease.Linear, unscaled: false);

            yield return Ease.GetWait(5.2f);

            yield return Ease.Tween(0.35f, t =>
            {
                if (radioCommsGroup != null) radioCommsGroup.alpha = Mathf.Lerp(1f, 0f, Ease.OutCubic(t));
            }, Ease.Linear, unscaled: false);

            if (radioCommsGroup != null) radioCommsGroup.alpha = 0f;
            _radioRoutine = null;
        }

        public void ShowMilestoneBanner(string message)
        {
            if (milestoneBannerGroup == null || milestoneBannerText == null) return;
            if (_milestoneRoutine != null) StopCoroutine(_milestoneRoutine);
            _milestoneRoutine = StartCoroutine(MilestoneBannerRoutine(message));
        }

        private IEnumerator MilestoneBannerRoutine(string message)
        {
            milestoneBannerText.text = message;
            if (milestoneBannerRect != null) milestoneBannerRect.localScale = Vector3.zero;
            milestoneBannerGroup.alpha = 1f;

            yield return Ease.Tween(0.22f, t =>
            {
                if (milestoneBannerRect == null) return;
                var s = Ease.OutBack(t);
                milestoneBannerRect.localScale = new Vector3(s, s, 1f);
            }, Ease.Linear, unscaled: true);

            yield return Ease.GetWaitRealtime(1.15f);

            yield return Ease.Tween(0.24f, t =>
            {
                milestoneBannerGroup.alpha = Mathf.Lerp(1f, 0f, Ease.OutCubic(t));
            }, Ease.Linear, unscaled: true);

            milestoneBannerGroup.alpha = 0f;
            _milestoneRoutine = null;
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
