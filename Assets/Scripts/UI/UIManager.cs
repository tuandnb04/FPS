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
        Cutscene,
        Decision
    }

    /// <summary>
    /// Modern JSOC tactical HUD & shell controller supporting C4ISR compass/bearing telemetry,
    /// RWR threat warnings, ballistic armor + biometrics, squad permadeath status,
    /// Act IV moral choice modal, NVG/FLIR overlay, and K.I.A. Memorial After-Action debrief.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private static readonly string[] WeaponNames = { "M4A1 CARBINE", "M870 SHOTGUN", "MK14 EBR", "RPG-7 HEAT", "AN-94 ABAKAN", "VECTOR .45" };
        private static readonly string[] WeaponBarLabels = { "M4A1", "M870", "MK14", "RPG7", "AN94", "VEC.45" };
        private static readonly string[] WeaponShortTags = { "5.56 AUTO", "12G PUMP", "7.62 SEMI", "85MM HEAT", "2X BURST", ".45 ACP" };
        private static readonly string[] WeaponCaliberModes = { "5.56x45 NATO · AUTO", "12 GAUGE · PUMP", "7.62x51 AP · SEMI", "85MM PG-7V · HEAT", "5.45x39 · 2X BURST", ".45 ACP · 1100 RPM" };
        private static readonly int[] WeaponCosts = { 0, 150, 300, 450, 140, 200 };
        private static readonly string[] SkinNames = { "RANGER OLIVE", "DESERT TAN", "NIGHT OPS" };
        private static readonly int[] SkinCosts = { 0, 180, 280 };
        private static readonly string[] UpgradeKeys = { "armor", "firepower", "thruster", "greed" };
        private static readonly string[] UpgradeTitles = { "BALLISTIC PLATES (+HP/AR)", "AP MATCH AMMO (+12% DMG)", "ENDURANCE (+SPD)", "LOGISTICS (+18% REQ)" };

        [Header("Screen Groups")]
        [SerializeField] private CanvasGroup titleGroup;
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private CanvasGroup pauseGroup;
        [SerializeField] private CanvasGroup endedGroup;
        [SerializeField] private CanvasGroup armoryGroup;
        [SerializeField] private CanvasGroup draftGroup;
        [SerializeField] private CanvasGroup coreChoiceGroup;

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
        [SerializeField] private Button[] intelTabButtons;
        [SerializeField] private Image[] intelTabBgs;
        [SerializeField] private TextMeshProUGUI[] intelTabLabels;
        [SerializeField] private TextMeshProUGUI titleSubtitleText;
        [SerializeField] private TextMeshProUGUI titleBriefText;

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

        [Header("Fireteam Vanguard-09 Squad Roster & Orders")]
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

        [Header("Tactical Combat HUD")]
        [SerializeField] private CanvasGroup hitmarkerGroup;
        [SerializeField] private Image[] hitmarkerTicks;
        [SerializeField] private TextMeshProUGUI killFeedText;
        [SerializeField] private TextMeshProUGUI ammoHudText;
        [SerializeField] private Image reloadProgressFill;
        [SerializeField] private CanvasGroup nvgOverlayGroup;
        [SerializeField] private TextMeshProUGUI nvgStatusText;
        [SerializeField] private Button nvgToggleButton;

        [Header("Core Navigation & Multi-Mission Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button hudPauseButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button pauseRestartButton;
        [SerializeField] private Button pauseTitleButton;
        [SerializeField] private Button endedReplayButton;
        [SerializeField] private Button endedTitleButton;
        [SerializeField] private Button endedNextMissionButton;
        [SerializeField] private TextMeshProUGUI endedNextMissionText;
        [SerializeField] private Button[] missionSelectButtons;
        [SerializeField] private Image[] missionSelectBgs;
        [SerializeField] private TextMeshProUGUI[] missionSelectLabels;

        [Header("Modern Warfare C4ISR & Biometrics")]
        [SerializeField] private TextMeshProUGUI compassBearingText;
        [SerializeField] private TextMeshProUGUI gridStampText;
        [SerializeField] private CanvasGroup rwrWarningGroup;
        [SerializeField] private TextMeshProUGUI rwrWarningText;
        [SerializeField] private Image armorFillImage;
        [SerializeField] private TextMeshProUGUI armorValueText;
        [SerializeField] private TextMeshProUGUI heartRateText;
        [SerializeField] private Button traumaKitButton;
        [SerializeField] private TextMeshProUGUI traumaKitText;
        [SerializeField] private TextMeshProUGUI weaponCaliberText;
        [SerializeField] private TextMeshProUGUI missionClockText;
        [SerializeField] private TextMeshProUGUI intelHudCountText;
        [SerializeField] private Image[] radioWaveformBars;
        [SerializeField] private TextMeshProUGUI endedMemorialText;
        [SerializeField] private TextMeshProUGUI[] titleRosterTexts;
        [SerializeField] private Button titleResetCampaignButton;
        [SerializeField] private Button coreChoiceBroadcastButton;
        [SerializeField] private Button coreChoiceSilentButton;

        [Header("Tactical HUD v2 Telemetry")]
        [SerializeField] private RectTransform radarFovCone;
        [SerializeField] private CanvasGroup hitArcGroup;
        [SerializeField] private RectTransform hitArcRect;
        [SerializeField] private TextMeshProUGUI crosshairReloadHintText;
        [SerializeField] private Image[] squadKiaIcons;
        [SerializeField] private Image[] titleActBorders;
        [SerializeField] private TextMeshProUGUI[] titleActSubTexts;
        [SerializeField] private Image[] titleRosterBorders;
        [SerializeField] private Image[] titleRosterSkulls;
        [SerializeField] private TextMeshProUGUI titleHeaderMetaText;
        [SerializeField] private TextMeshProUGUI titleLoadoutPreviewText;
        [SerializeField] private TextMeshProUGUI ammoWeaponNameText;

        private Coroutine _hpTween;
        private Coroutine _xpTween;
        private Coroutine _transitionTween;
        private Coroutine _milestoneRoutine;
        private Coroutine _radioRoutine;
        private readonly Queue<RadioLine> _radioQueue = new();
        private int _activeIntelTab;
        private bool _touchDashQueued;
        private float _hitmarkerTimer;
        private float _hitmarkerDuration = 0.12f;
        private float _hitArcTimer;
        private float _killFeedClearTimer;
        private float _rwrTimer;
        private float _missionElapsedSeconds;
        private float _reticleBloom;
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

        public void BindCampaignUi(
            Button[] missionBtns,
            Image[] missionBgs,
            TextMeshProUGUI[] missionLbls,
            Button nextMissionBtn,
            TextMeshProUGUI nextMissionTxt,
            TextMeshProUGUI subTxt = null,
            TextMeshProUGUI briefTxt = null)
        {
            missionSelectButtons = missionBtns;
            missionSelectBgs = missionBgs;
            missionSelectLabels = missionLbls;
            endedNextMissionButton = nextMissionBtn;
            endedNextMissionText = nextMissionTxt;
            titleSubtitleText = subTxt;
            titleBriefText = briefTxt;
        }

        public void BindIntelTabWidgets(Button[] tabButtons, Image[] tabBgs, TextMeshProUGUI[] tabLabels)
        {
            intelTabButtons = tabButtons;
            intelTabBgs = tabBgs;
            intelTabLabels = tabLabels;
        }

        public void RefreshMissionSelectUI(int activeMission, int clearedCount)
        {
            if (titleSubtitleText != null)
            {
                titleSubtitleText.text = StoryDirector.GetMissionTitle(activeMission) + "  //  " + StoryDirector.GetMissionSubtitle(activeMission);
            }
            if (titleBriefText != null)
            {
                titleBriefText.text = StoryDirector.GetMissionTitleBrief(activeMission);
            }
            if (gridStampText != null)
            {
                gridStampText.text = StoryDirector.GetMissionGridStamp(activeMission);
            }
            RefreshTitleRoster();
            RefreshTitleLoadoutSummary();
            if (missionSelectButtons == null) return;
            for (var i = 0; i < missionSelectButtons.Length; i++)
            {
                var idx = i + 1;
                var selected = idx == activeMission;
                var cleared = clearedCount >= idx;
                if (missionSelectBgs != null && i < missionSelectBgs.Length && missionSelectBgs[i] != null)
                {
                    missionSelectBgs[i].color = selected
                        ? Palette.WithAlpha(Palette.FromHex("#342412"), 0.95f)
                        : (cleared ? Palette.WithAlpha(Palette.FromHex("#142B1F"), 0.95f) : Palette.WithAlpha(Palette.SecondaryButtonBg, 0.95f));
                }
                if (titleActBorders != null && i < titleActBorders.Length && titleActBorders[i] != null)
                {
                    titleActBorders[i].color = selected
                        ? Palette.Accent
                        : (cleared ? Palette.HealthGreen : Palette.WithAlpha(Palette.CharacterSecondary, 0.28f));
                }
                if (missionSelectLabels != null && i < missionSelectLabels.Length && missionSelectLabels[i] != null)
                {
                    var shortTitle = StoryDirector.GetMissionShortTab(idx);
                    missionSelectLabels[i].text = selected ? shortTitle + "  //  SELECTED" : (cleared ? shortTitle + "  //  CLEARED" : shortTitle);
                    missionSelectLabels[i].color = selected ? Palette.Accent : (cleared ? Palette.HealthGreen : Palette.CharacterSecondary);
                }
                if (titleActSubTexts != null && i < titleActSubTexts.Length && titleActSubTexts[i] != null)
                {
                    titleActSubTexts[i].text = StoryDirector.GetMissionGridStamp(idx);
                    titleActSubTexts[i].color = selected ? Palette.CharacterSecondary : Palette.MutedText;
                }
            }
        }

        public void BindModernWarfareWidgets(
            TextMeshProUGUI compassTxt,
            TextMeshProUGUI gridTxt,
            CanvasGroup rwrGroup,
            TextMeshProUGUI rwrTxt,
            Image armorFill,
            TextMeshProUGUI armorTxt,
            TextMeshProUGUI bpmTxt,
            Button traumaBtn,
            TextMeshProUGUI traumaTxt,
            TextMeshProUGUI wpnCaliberTxt,
            TextMeshProUGUI clockTxt,
            TextMeshProUGUI intelCountTxt,
            Image[] waveBars,
            TextMeshProUGUI memorialTxt,
            TextMeshProUGUI[] rosterTxts,
            Button resetRosterBtn,
            CanvasGroup choiceGroup,
            Button choiceBroadcastBtn,
            Button choiceSilentBtn)
        {
            compassBearingText = compassTxt;
            gridStampText = gridTxt;
            rwrWarningGroup = rwrGroup;
            rwrWarningText = rwrTxt;
            armorFillImage = armorFill;
            armorValueText = armorTxt;
            heartRateText = bpmTxt;
            traumaKitButton = traumaBtn;
            traumaKitText = traumaTxt;
            weaponCaliberText = wpnCaliberTxt;
            missionClockText = clockTxt;
            intelHudCountText = intelCountTxt;
            radioWaveformBars = waveBars;
            endedMemorialText = memorialTxt;
            titleRosterTexts = rosterTxts;
            titleResetCampaignButton = resetRosterBtn;
            coreChoiceGroup = choiceGroup;
            coreChoiceBroadcastButton = choiceBroadcastBtn;
            coreChoiceSilentButton = choiceSilentBtn;

            if (rwrWarningGroup != null)
            {
                rwrWarningGroup.alpha = 0f;
                rwrWarningGroup.blocksRaycasts = false;
                rwrWarningGroup.interactable = false;
            }
            if (coreChoiceGroup != null)
            {
                SetGroupState(coreChoiceGroup, 0f);
            }
            RefreshTitleRoster();
        }

        public void BindTacticalV2Widgets(
            RectTransform radarCone,
            CanvasGroup directionalHitGroup,
            RectTransform directionalHitRect,
            TextMeshProUGUI reloadHintTxt,
            Image[] sqKiaIcons,
            Image[] actBorders,
            TextMeshProUGUI[] actSubTxts,
            Image[] rosterBorders,
            Image[] rosterSkulls,
            TextMeshProUGUI headerMetaTxt,
            TextMeshProUGUI loadoutPreviewTxt,
            TextMeshProUGUI ammoWpnNameTxt)
        {
            radarFovCone = radarCone;
            hitArcGroup = directionalHitGroup;
            hitArcRect = directionalHitRect;
            crosshairReloadHintText = reloadHintTxt;
            squadKiaIcons = sqKiaIcons;
            titleActBorders = actBorders;
            titleActSubTexts = actSubTxts;
            titleRosterBorders = rosterBorders;
            titleRosterSkulls = rosterSkulls;
            titleHeaderMetaText = headerMetaTxt;
            titleLoadoutPreviewText = loadoutPreviewTxt;
            ammoWeaponNameText = ammoWpnNameTxt;

            if (hitArcGroup != null)
            {
                hitArcGroup.alpha = 0f;
                hitArcGroup.interactable = false;
                hitArcGroup.blocksRaycasts = false;
            }
            if (crosshairReloadHintText != null)
            {
                crosshairReloadHintText.gameObject.SetActive(false);
            }
            RefreshTitleRoster();
            RefreshTitleLoadoutSummary();
        }

        public void RefreshTitleRoster()
        {
            if (titleRosterTexts == null || titleRosterTexts.Length < 4) return;
            var save = SaveManager.EnsureInstance();
            var act = GameManager.Instance != null ? GameManager.Instance.CurrentMissionIndex : StoryDirector.ActiveMissionIndex;

            bool IsDeadForDisplay(string tag, int minCanonicalAct) => save.IsOperatorKia(tag) || act >= minCanonicalAct;

            var entries = new[]
            {
                ("[NOMAD-1] VANCE", "FIRETEAM LEAD · ASSAULT", false),
                ("[IRONWOOD-1] ROURKE", "PLATOON NCO · HEAVY", IsDeadForDisplay(SquadCallsigns.Officer, 4)),
                ("[WRAITH-2] KOZLOWSKI", "RECON / OVERWATCH", IsDeadForDisplay(SquadCallsigns.Recon, 2)),
                ("[SPARK-3] REYES", "BREACHER / EOD", IsDeadForDisplay(SquadCallsigns.Breacher, 3))
            };
            for (var i = 0; i < 4; i++)
            {
                var dead = entries[i].Item3;
                var accent = dead ? Palette.EnemySecondary : (i == 0 ? Palette.HealthGreen : Palette.SquadBlue);
                if (titleRosterTexts[i] != null)
                {
                    titleRosterTexts[i].text = dead
                        ? $"{entries[i].Item1}\n{entries[i].Item2}  //  K.I.A."
                        : $"{entries[i].Item1}\n{entries[i].Item2}  //  ACTIVE";
                    titleRosterTexts[i].color = dead ? Palette.EnemySecondary : Palette.CharacterSecondary;
                }
                if (titleRosterBorders != null && i < titleRosterBorders.Length && titleRosterBorders[i] != null)
                {
                    titleRosterBorders[i].color = Palette.WithAlpha(accent, dead ? 0.85f : 0.65f);
                }
                if (titleRosterSkulls != null && i < titleRosterSkulls.Length && titleRosterSkulls[i] != null)
                {
                    titleRosterSkulls[i].enabled = dead;
                    titleRosterSkulls[i].color = Palette.EnemySecondary;
                }
            }
        }

        private void RefreshTitleLoadoutSummary()
        {
            var save = SaveManager.EnsureInstance();
            var d = save.Data;
            var wpnIdx = Mathf.Clamp(d.selectedWeapon, 0, WeaponNames.Length - 1);
            var skinIdx = Mathf.Clamp(d.selectedSkin, 0, SkinNames.Length - 1);
            if (titleLoadoutPreviewText != null)
            {
                titleLoadoutPreviewText.text = $"LOADOUT: {WeaponNames[wpnIdx]} ({WeaponCaliberModes[wpnIdx]})   |   RIG: {SkinNames[skinIdx]}";
            }
            if (titleHeaderMetaText != null)
            {
                var act = GameManager.Instance != null ? GameManager.Instance.CurrentMissionIndex : StoryDirector.ActiveMissionIndex;
                var kia = save.KiaCount;
                if (act >= 2 && !save.IsOperatorKia(SquadCallsigns.Recon)) kia++;
                if (act >= 3 && !save.IsOperatorKia(SquadCallsigns.Breacher)) kia++;
                if (act >= 4 && !save.IsOperatorKia(SquadCallsigns.Officer)) kia++;
                titleHeaderMetaText.text = $"BLACKBOX: {save.DiscoveredIntelCount}/{StoryDirector.IntelCatalog.Length}   ·   KIA: {Mathf.Clamp(kia, 0, 3)}/3";
            }
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
            StoryDirector.OnRadioConversation -= HandleRadioConversation;
            StoryDirector.OnRadioConversation += HandleRadioConversation;
        }

        private void OnDisable()
        {
            StoryDirector.OnRadioTransmission -= HandleRadioTransmission;
            StoryDirector.OnRadioConversation -= HandleRadioConversation;
        }

        private void Start()
        {
            EnsureBarSprites();
            WireButtonsAndSliders();
            RefreshPlatformControls();
            var initialState = GameManager.Instance != null
                ? GameManager.Instance.CurrentState
                : (SaveManager.PendingAutoDeploy ? GameState.Cutscene : GameState.Title);
            ApplyImmediateState(initialState);
            RefreshVolumeLabels();
            RefreshWeaponBar();
            RefreshArmoryUI();
            var save = SaveManager.EnsureInstance();
            var activeM = GameManager.Instance != null ? GameManager.Instance.CurrentMissionIndex : StoryDirector.ActiveMissionIndex;
            RefreshMissionSelectUI(activeM, save.Data.highestMissionCompleted);
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
            if (endedNextMissionButton != null)
            {
                endedNextMissionButton.onClick.RemoveAllListeners();
                endedNextMissionButton.onClick.AddListener(() => GameManager.Instance?.LoadNextMission());
            }
            if (missionSelectButtons != null)
            {
                for (var i = 0; i < missionSelectButtons.Length; i++)
                {
                    var mIdx = i + 1;
                    if (missionSelectButtons[i] == null) continue;
                    missionSelectButtons[i].onClick.RemoveAllListeners();
                    missionSelectButtons[i].onClick.AddListener(() => GameManager.Instance?.SelectTitleMission(mIdx));
                }
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
            if (intelTabButtons != null)
            {
                for (var i = 0; i < intelTabButtons.Length; i++)
                {
                    var tabIdx = i;
                    if (intelTabButtons[i] == null) continue;
                    intelTabButtons[i].onClick.RemoveAllListeners();
                    intelTabButtons[i].onClick.AddListener(() => SelectIntelTab(tabIdx));
                }
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

            if (traumaKitButton != null)
            {
                traumaKitButton.onClick.RemoveAllListeners();
                traumaKitButton.onClick.AddListener(() => PlayerController.Instance?.TryUseTraumaKit());
            }

            if (titleResetCampaignButton != null)
            {
                titleResetCampaignButton.onClick.RemoveAllListeners();
                titleResetCampaignButton.onClick.AddListener(() => GameManager.Instance?.ResetFullCampaignRoster());
            }

            if (coreChoiceBroadcastButton != null)
            {
                coreChoiceBroadcastButton.onClick.RemoveAllListeners();
                coreChoiceBroadcastButton.onClick.AddListener(() => GameManager.Instance?.ResolveAct4CoreChoice(CampaignEndingKind.BroadcastBlackout));
            }

            if (coreChoiceSilentButton != null)
            {
                coreChoiceSilentButton.onClick.RemoveAllListeners();
                coreChoiceSilentButton.onClick.AddListener(() => GameManager.Instance?.ResolveAct4CoreChoice(CampaignEndingKind.SilentKillSwitch));
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
            _reticleBloom = Mathf.MoveTowards(_reticleBloom, 0f, unscaledDt * 2.8f);
            if (crosshairRect != null)
            {
                var showCrosshair = !ShouldShowMobileControls() && hudGroup != null && hudGroup.alpha > 0.5f && Mouse.current != null &&
                                    (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing);
                if (crosshairRect.gameObject.activeSelf != showCrosshair)
                {
                    crosshairRect.gameObject.SetActive(showCrosshair);
                }
                if (showCrosshair)
                {
                    crosshairRect.position = Mouse.current.position.ReadValue();
                    var s = 1f + _reticleBloom * 0.45f;
                    crosshairRect.localScale = new Vector3(s, s, 1f);
                    if (crosshairReloadHintText != null)
                    {
                        crosshairReloadHintText.rectTransform.position = crosshairRect.position + new Vector3(0f, -34f, 0f);
                    }
                }
                else if (crosshairReloadHintText != null && crosshairReloadHintText.gameObject.activeSelf)
                {
                    crosshairReloadHintText.gameObject.SetActive(false);
                }
            }

            if (_hitArcTimer > 0f && hitArcGroup != null)
            {
                _hitArcTimer = Mathf.Max(0f, _hitArcTimer - unscaledDt);
                var t = Mathf.Clamp01(_hitArcTimer / 0.72f);
                hitArcGroup.alpha = t * (0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 20f));
            }
            else if (hitArcGroup != null && hitArcGroup.alpha > 0.001f)
            {
                hitArcGroup.alpha = 0f;
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

            if (_rwrTimer > 0f && rwrWarningGroup != null)
            {
                _rwrTimer = Mathf.Max(0f, _rwrTimer - unscaledDt);
                var pulse = 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 16f);
                rwrWarningGroup.alpha = _rwrTimer > 0.01f ? Mathf.Clamp01(_rwrTimer / 0.35f) * pulse : 0f;
            }

            if (radioWaveformBars != null && radioCommsGroup != null && radioCommsGroup.alpha > 0.15f)
            {
                for (var i = 0; i < radioWaveformBars.Length; i++)
                {
                    if (radioWaveformBars[i] == null) continue;
                    var wave = 0.28f + 0.72f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * (7.5f + i * 2.1f) + i * 1.1f));
                    radioWaveformBars[i].rectTransform.localScale = new Vector3(1f, wave, 1f);
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
            if (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing)
            {
                _missionElapsedSeconds += Time.deltaTime;
                UpdateCompassAndClock();
            }
            UpdateSquadRosterHud();
            UpdateTacticalRadar();
        }

        public void ResetMissionClock()
        {
            _missionElapsedSeconds = 0f;
            _rwrTimer = 0f;
            _hitArcTimer = 0f;
            if (rwrWarningGroup != null) rwrWarningGroup.alpha = 0f;
            if (hitArcGroup != null) hitArcGroup.alpha = 0f;
        }

        public void ShowDirectionalHit(Vector3 sourceWorldPos)
        {
            if (hitArcGroup == null || hitArcRect == null || PlayerController.Instance == null) return;
            var delta = sourceWorldPos - PlayerController.Instance.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.05f) return;

            var cam = UnityEngine.Camera.main;
            var camForward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            var camRight = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
            if (camForward.sqrMagnitude < 0.01f) camForward = Vector3.forward;
            if (camRight.sqrMagnitude < 0.01f) camRight = Vector3.right;

            var x = Vector3.Dot(delta.normalized, camRight);
            var y = Vector3.Dot(delta.normalized, camForward);
            var uiZDeg = -Mathf.Atan2(x, y) * Mathf.Rad2Deg;
            hitArcRect.localRotation = Quaternion.Euler(0f, 0f, uiZDeg);
            _hitArcTimer = 0.72f;
            hitArcGroup.alpha = 1f;
        }

        public void AddReticleBloom(float amount)
        {
            _reticleBloom = Mathf.Clamp(_reticleBloom + amount, 0f, 1f);
        }

        public void TriggerThreatWarning(string alertMessage, float holdSeconds = 2.4f)
        {
            if (rwrWarningGroup == null || rwrWarningText == null) return;
            rwrWarningText.text = alertMessage;
            _rwrTimer = Mathf.Max(_rwrTimer, holdSeconds);
            rwrWarningGroup.alpha = 1f;
        }

        private void UpdateCompassAndClock()
        {
            var player = PlayerController.Instance;
            if (compassBearingText != null && player != null)
            {
                var heading = Mathf.Repeat(player.transform.eulerAngles.y, 360f);
                var hInt = Mathf.RoundToInt(heading) % 360;
                var card = HeadingCardinal(hInt);
                var left = (hInt + 345) % 360;
                var right = (hInt + 15) % 360;
                if (Enemies.EnemySpawner.Instance != null)
                {
                    var obj = Enemies.EnemySpawner.Instance.GetActiveObjectiveWorldPoint();
                    var delta = obj - player.transform.position;
                    delta.y = 0f;
                    var distM = Mathf.RoundToInt(delta.magnitude);
                    var objAngle = Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 360f);
                    compassBearingText.text = $"{left:000}°  ·  [ {hInt:000}° {card} ]  ·  {right:000}°   |   WP {Mathf.RoundToInt(objAngle) % 360:000}° ({distM}M)";
                }
                else
                {
                    compassBearingText.text = $"{left:000}°  ·  [ {hInt:000}° {card} ]  ·  {right:000}°";
                }
            }

            if (missionClockText != null)
            {
                var secTotal = Mathf.FloorToInt(_missionElapsedSeconds);
                var mins = secTotal / 60;
                var secs = secTotal % 60;
                missionClockText.text = $"T+{mins:00}:{secs:00}";
            }

            if (intelHudCountText != null)
            {
                var save = SaveManager.EnsureInstance();
                intelHudCountText.text = $"BLACKBOX // {save.DiscoveredIntelCount}/{StoryDirector.IntelCatalog.Length}";
            }
        }

        private static string HeadingCardinal(int deg)
        {
            if (deg < 23 || deg >= 338) return "N";
            if (deg < 68) return "NE";
            if (deg < 113) return "E";
            if (deg < 158) return "SE";
            if (deg < 203) return "S";
            if (deg < 248) return "SW";
            if (deg < 293) return "W";
            return "NW";
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
            SetGroupState(coreChoiceGroup, state == GameState.Decision ? 1f : 0f);
            SetGroupState(intelGroup, 0f);
            if (milestoneBannerGroup != null)
            {
                milestoneBannerGroup.alpha = 0f;
            }
            if (rwrWarningGroup != null && state != GameState.Playing)
            {
                rwrWarningGroup.alpha = 0f;
            }
            if (state != GameState.Playing)
            {
                HideRadioCommsImmediate();
                UpdateDamageVignette(0f);
                UpdateContextPrompt(string.Empty);
                RadarEnemyActiveCount = 0;
                _hitArcTimer = 0f;
                if (hitArcGroup != null) hitArcGroup.alpha = 0f;
                if (crosshairReloadHintText != null) crosshairReloadHintText.gameObject.SetActive(false);
            }
            if (state != GameState.Cutscene)
            {
                HideCutsceneOverlayImmediate();
            }
            if (state == GameState.Title)
            {
                RefreshTitleRoster();
                RefreshTitleLoadoutSummary();
            }
            if (state == GameState.Title || state == GameState.Ended)
            {
                HideBossHp();
            }
        }

        public void TransitionToState(GameState state)
        {
            SetGroupState(intelGroup, 0f);
            RefreshPlatformControls();
            if (state == GameState.Title)
            {
                RefreshTitleRoster();
                RefreshTitleLoadoutSummary();
            }
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
            var sChoice = coreChoiceGroup != null ? coreChoiceGroup.alpha : 0f;

            var eTitle = target == GameState.Title ? 1f : 0f;
            var eHud = target == GameState.Playing ? 1f : 0f;
            var ePause = target == GameState.Paused ? 1f : 0f;
            var eEnded = target == GameState.Ended ? 1f : 0f;
            var eArmory = target == GameState.Armory ? 1f : 0f;
            var eDraft = target == GameState.LevelUpDraft ? 1f : 0f;
            var eChoice = target == GameState.Decision ? 1f : 0f;

            SetGroupInteractable(titleGroup, eTitle > 0.5f);
            SetGroupInteractable(hudGroup, eHud > 0.5f);
            SetGroupInteractable(pauseGroup, ePause > 0.5f);
            SetGroupInteractable(endedGroup, eEnded > 0.5f);
            SetGroupInteractable(armoryGroup, eArmory > 0.5f);
            SetGroupInteractable(draftGroup, eDraft > 0.5f);
            SetGroupInteractable(coreChoiceGroup, eChoice > 0.5f);

            yield return Ease.Tween(0.25f, t =>
            {
                var e = Ease.OutCubic(t);
                if (titleGroup != null) titleGroup.alpha = Mathf.Lerp(sTitle, eTitle, e);
                if (hudGroup != null) hudGroup.alpha = Mathf.Lerp(sHud, eHud, e);
                if (pauseGroup != null) pauseGroup.alpha = Mathf.Lerp(sPause, ePause, e);
                if (endedGroup != null) endedGroup.alpha = Mathf.Lerp(sEnded, eEnded, e);
                if (armoryGroup != null) armoryGroup.alpha = Mathf.Lerp(sArmory, eArmory, e);
                if (draftGroup != null) draftGroup.alpha = Mathf.Lerp(sDraft, eDraft, e);
                if (coreChoiceGroup != null) coreChoiceGroup.alpha = Mathf.Lerp(sChoice, eChoice, e);
            }, Ease.Linear, unscaled: true);

            SetGroupState(titleGroup, eTitle);
            SetGroupState(hudGroup, eHud);
            SetGroupState(pauseGroup, ePause);
            SetGroupState(endedGroup, eEnded);
            SetGroupState(armoryGroup, eArmory);
            SetGroupState(draftGroup, eDraft);
            SetGroupState(coreChoiceGroup, eChoice);
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

        public void UpdateEquipmentHud(int fragCount, int mortarCharges, int streak, bool uavOnline, bool isRegen, int traumaKits = -1)
        {
            if (fragCountText != null)
            {
                fragCountText.text = "[G] FRAG x" + fragCount;
                fragCountText.color = fragCount > 0 ? Palette.Accent : Palette.MutedText;
            }
            if (strikeStatusText != null)
            {
                strikeStatusText.text = mortarCharges > 0
                    ? ("[F] 155MM x" + mortarCharges)
                    : ("[F] 155MM (" + Mathf.Min(streak, 5) + "/5)");
                strikeStatusText.color = mortarCharges > 0 ? Palette.HealthGreen : Palette.MutedText;
            }
            if (traumaKits >= 0 && traumaKitText != null)
            {
                traumaKitText.text = "[Q] PLATE x" + traumaKits;
                traumaKitText.color = traumaKits > 0 ? Palette.M3HeliosCoreCyan : Palette.MutedText;
            }
            if (streakText != null)
            {
                streakText.text = uavOnline
                    ? "REAPER-04 // ALT 4,200M"
                    : ("ISR TASKING // " + Mathf.Min(streak, 3) + "/3 KIA");
                streakText.color = uavOnline ? Palette.RadarGreen : Palette.CharacterSecondary;
            }
            if (levelText != null)
            {
                levelText.text = uavOnline
                    ? ("ISR OVERWATCH // " + streak + " KIA")
                    : (mortarCharges > 0 ? ("155MM CAS READY // " + streak + " KIA") : ("TAC-RECON // " + streak + " KIA"));
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
                radarUavStatusText.text = uavOnline ? "ISR LOCK // LIVE" : "ATAK // GRID 38T";
                radarUavStatusText.color = uavOnline ? Palette.RadarGreen : Palette.MutedText;
            }
            if (hpFillImage != null && isRegen)
            {
                hpFillImage.color = Palette.HealthGreen;
            }
        }

        public void UpdateOperatorVitals(float currentHp, float maxHp, float currentArmor, float maxArmor, int bpm, int traumaKits)
        {
            if (armorFillImage != null)
            {
                armorFillImage.fillAmount = maxArmor > 0f ? Mathf.Clamp01(currentArmor / maxArmor) : 0f;
                armorFillImage.color = currentArmor <= 0.1f ? Palette.EnemySecondary : Palette.M3HeliosCoreCyan;
            }
            if (armorValueText != null)
            {
                armorValueText.text = currentArmor > 0.1f
                    ? $"PLATES {Mathf.CeilToInt(currentArmor)}/{Mathf.RoundToInt(maxArmor)}"
                    : "PLATES CRACKED [Q]";
                armorValueText.color = currentArmor > 0.1f ? Palette.M3HeliosCoreCyan : Palette.EnemySecondary;
            }
            if (heartRateText != null)
            {
                var status = bpm >= 130 ? "STRESS" : (bpm >= 102 ? "ELEVATED" : "NOMINAL");
                heartRateText.text = $"{bpm} BPM // {status}";
                heartRateText.color = bpm >= 130 ? Palette.EnemySecondary : (bpm >= 102 ? Palette.C4Amber : Palette.HealthGreen);
            }
            if (traumaKitText != null)
            {
                traumaKitText.text = "[Q] PLATE x" + traumaKits;
                traumaKitText.color = traumaKits > 0 ? Palette.M3HeliosCoreCyan : Palette.MutedText;
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
            var lowMag = !isReloading && currentMag <= Mathf.Max(2, magCapacity / 4);
            if (ammoHudText != null)
            {
                ammoHudText.text = isReloading
                    ? ("RELOADING  " + Mathf.RoundToInt(reloadProgress * 100f) + "%")
                    : ($"{currentMag:00} / {magCapacity:00}  [R]");
                ammoHudText.color = isReloading ? Palette.C4Amber : (lowMag ? Palette.EnemySecondary : Palette.CharacterSecondary);
            }

            if (ammoWeaponNameText != null)
            {
                var save = SaveManager.EnsureInstance();
                var wIdx = Mathf.Clamp(save.Data.selectedWeapon, 0, WeaponNames.Length - 1);
                ammoWeaponNameText.text = WeaponNames[wIdx];
            }

            if (crosshairReloadHintText != null)
            {
                var showHint = (isReloading || lowMag) && crosshairRect != null && crosshairRect.gameObject.activeInHierarchy;
                crosshairReloadHintText.gameObject.SetActive(showHint);
                if (showHint)
                {
                    crosshairReloadHintText.text = isReloading ? $"RELOADING {Mathf.RoundToInt(reloadProgress * 100f)}%" : "[R] RELOAD";
                    crosshairReloadHintText.color = isReloading ? Palette.C4Amber : Palette.EnemySecondary;
                }
            }

            if (reloadProgressFill == null) return;
            reloadProgressFill.fillAmount = isReloading ? reloadProgress : (magCapacity > 0 ? (float)currentMag / magCapacity : 1f);
            reloadProgressFill.color = isReloading ? Palette.C4Amber : (lowMag ? Palette.EnemySecondary : Palette.Accent);
        }

        public void SetNvgActive(bool active)
        {
            IsNvgHudActive = active;
            if (nvgOverlayGroup != null)
            {
                nvgOverlayGroup.alpha = active ? 0.85f : 0f;
            }

            if (nvgStatusText == null) return;
            nvgStatusText.text = active ? "[N] FLIR: ON" : "[N] FLIR: OFF";
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
                if (!hasMember)
                {
                    if (squadKiaIcons != null && i < squadKiaIcons.Length && squadKiaIcons[i] != null)
                    {
                        squadKiaIcons[i].enabled = false;
                    }
                    continue;
                }

                var m = members[i];
                if (squadKiaIcons != null && i < squadKiaIcons.Length && squadKiaIcons[i] != null)
                {
                    squadKiaIcons[i].enabled = m.IsKia;
                    squadKiaIcons[i].color = Palette.EnemySecondary;
                }
                if (m.IsKia)
                {
                    if (squadMemberNames != null && i < squadMemberNames.Length && squadMemberNames[i] != null)
                    {
                        squadMemberNames[i].text = m.Callsign;
                        squadMemberNames[i].color = Palette.WithAlpha(Palette.EnemySecondary, 0.78f);
                    }
                    if (squadMemberBars[i] != null)
                    {
                        squadMemberBars[i].fillAmount = 0f;
                        squadMemberBars[i].color = Palette.DamageBloodRed;
                    }
                    if (squadMemberStatusTexts != null && i < squadMemberStatusTexts.Length && squadMemberStatusTexts[i] != null)
                    {
                        squadMemberStatusTexts[i].text = "K.I.A.";
                        squadMemberStatusTexts[i].color = Palette.EnemySecondary;
                    }
                    continue;
                }

                if (squadMemberNames != null && i < squadMemberNames.Length && squadMemberNames[i] != null)
                {
                    squadMemberNames[i].text = m.Callsign;
                    squadMemberNames[i].color = m.IsDowned ? Palette.C4Amber : Palette.CharacterSecondary;
                }
                if (squadMemberBars[i] != null)
                {
                    squadMemberBars[i].fillAmount = m.IsDowned
                        ? Mathf.Clamp01(m.BleedOutNormalized)
                        : (m.MaxHp > 0f ? Mathf.Clamp01(m.CurrentHp / m.MaxHp) : 0f);
                    squadMemberBars[i].color = m.IsDowned ? Palette.EnemySecondary : Palette.SquadBlue;
                }

                if (squadMemberStatusTexts == null || i >= squadMemberStatusTexts.Length ||
                    squadMemberStatusTexts[i] == null) continue;
                squadMemberStatusTexts[i].text = m.IsDowned
                    ? $"BLEED {Mathf.CeilToInt(m.BleedOutRemaining)}s"
                    : (Mathf.RoundToInt(100f * m.CurrentHp / Mathf.Max(1f, m.MaxHp)) + "%");
                squadMemberStatusTexts[i].color = m.IsDowned ? Palette.EnemySecondary : Palette.SquadBlue;
            }
        }

        private void UpdateTacticalRadar()
        {
            if (radarContainerRect == null || PlayerController.Instance == null) return;

            var player = PlayerController.Instance;
            var playerPos = player.transform.position;
            var radiusPx = Mathf.Min(radarContainerRect.rect.width, radarContainerRect.rect.height) * 0.5f - 12f;
            if (radiusPx < 24f) radiusPx = 56f;
            const float radarWorldRange = 36f;

            var facingEuler = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
            if (radarPlayerChevron != null)
            {
                radarPlayerChevron.anchoredPosition = Vector2.zero;
                radarPlayerChevron.localRotation = facingEuler;
            }
            if (radarFovCone != null)
            {
                radarFovCone.anchoredPosition = Vector2.zero;
                radarFovCone.localRotation = facingEuler;
            }

            if (radarSquadDots != null)
            {
                var members = SquadDirector.Instance != null ? SquadDirector.Instance.Members : null;
                for (var i = 0; i < radarSquadDots.Length; i++)
                {
                    var dot = radarSquadDots[i];
                    if (dot == null) continue;
                    var show = members != null && i < members.Count && members[i] != null && !members[i].IsKia && members[i].gameObject.activeInHierarchy;
                    dot.gameObject.SetActive(show);
                    if (!show) continue;

                    var pos = MapWorldToRadar(members[i].transform.position, playerPos, radarWorldRange, radiusPx, true);
                    dot.rectTransform.anchoredPosition = pos;
                    dot.color = members[i].IsDowned ? Palette.EnemySecondary : Palette.SquadBlue;
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
                    dot.color = nodes[i].Kind switch
                    {
                        ObjectiveNodeKind.HelipadEvacLZ or ObjectiveNodeKind.BoatExfilDock or ObjectiveNodeKind.TunnelBreachExit or ObjectiveNodeKind.FreightLiftExit => Palette.RadarGreen,
                        ObjectiveNodeKind.IntelTerminal => Palette.M3HeliosCoreCyan,
                        _ => Palette.C4Amber
                    };
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
            if (scoreText != null)
            {
                var kills = GameManager.Instance != null ? GameManager.Instance.ConfirmedKillsCount : 0;
                scoreText.text = kills > 0 ? $"CONFIRMED // {kills} KIA" : "OP-SCORE // " + score.ToString("N0");
            }
            if (titleHighScoreText != null) titleHighScoreText.text = "PEAK RATING // " + highScore.ToString("N0");
            RefreshTitleLoadoutSummary();
        }

        public void UpdateCredits(int totalCredits, int runEarned)
        {
            if (titleCreditsText != null) titleCreditsText.text = "REQUISITION // " + totalCredits.ToString("N0") + " CR";
            if (armoryCreditsText != null) armoryCreditsText.text = totalCredits.ToString("N0") + " CR";
            if (hudCreditsText != null) hudCreditsText.text = "REQ // " + totalCredits.ToString("N0") + " CR";
            if (endedCreditsText != null) endedCreditsText.text = "REQUISITION +" + runEarned + " CR   |   FUNDS: " + totalCredits.ToString("N0") + " CR";
            RefreshTitleLoadoutSummary();
        }

        public void UpdateBossHp(float current, float max, string label)
        {
            if (bossBarRoot == null) return;
            bossBarRoot.gameObject.SetActive(true);
            if (bossHpFillImage != null) bossHpFillImage.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (bossNameText != null) bossNameText.text = "HVT LOCK // " + label;
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

            if (weaponCaliberText != null && selected >= 0 && selected < WeaponCaliberModes.Length)
            {
                weaponCaliberText.text = WeaponCaliberModes[selected];
            }
            if (ammoWeaponNameText != null && selected >= 0 && selected < WeaponNames.Length)
            {
                ammoWeaponNameText.text = WeaponNames[selected];
            }

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
                    var code = i < WeaponBarLabels.Length ? WeaponBarLabels[i] : WeaponNames[i];
                    weaponSlotLabels[i].text = unlocked ? $"[{i + 1}]\n{code}" : $"[{i + 1}]\nLOCK";
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
            if (titleCreditsText != null) titleCreditsText.text = "REQUISITION // " + d.credits.ToString("N0") + " CR";
            RefreshTitleLoadoutSummary();

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
            var mission = StoryDirector.ActiveMissionIndex;
            var save = SaveManager.EnsureInstance();
            var hasNext = victory && (GameManager.Instance != null ? GameManager.Instance.HasNextMission : mission < StoryDirector.CampaignMissionCount);
            if (endedTitleText != null)
            {
                if (!victory)
                {
                    endedTitleText.text = $"UNIT WIPED // ACT 0{mission} M.I.A.";
                    endedTitleText.color = Palette.EnemySecondary;
                }
                else if (hasNext)
                {
                    endedTitleText.text = $"ACT 0{mission} CLEARED // EXTRACTION AT COST";
                    endedTitleText.color = Palette.C4Amber;
                }
                else
                {
                    endedTitleText.text = save.LastEndingChoice == CampaignEndingKind.BroadcastBlackout
                        ? "ENDING A // SCORCHED BLACKOUT BROADCAST"
                        : "ENDING B // SILENT PROTOCOL SEALED";
                    endedTitleText.color = save.LastEndingChoice == CampaignEndingKind.BroadcastBlackout ? Palette.HealthGreen : Palette.C4Amber;
                }
            }
            if (endedAccentStripe != null)
            {
                endedAccentStripe.color = victory ? Palette.C4Amber : Palette.EnemySecondary;
            }
            if (endedScoreText != null) endedScoreText.text = score.ToString("N0") + " PTS";
            if (endedBestText != null) endedBestText.text = isNewRecord ? "PEAK RECORD // " + highScore.ToString("N0") : "PEAK RECORD // " + highScore.ToString("N0");
            if (endedStatsText != null) endedStatsText.text = $"ACT 0{mission}/{StoryDirector.CampaignMissionCount}   |   HOSTILES KIA: {kills}   |   VANGUARD KIA: {save.KiaCount}/3";
            if (endedNarrativeText != null) endedNarrativeText.text = StoryDirector.GetEndedDebrief(victory, wave, kills);
            if (endedMemorialText != null) endedMemorialText.text = StoryDirector.BuildMemorialRollText(victory);
            if (endedNextMissionButton != null)
            {
                endedNextMissionButton.gameObject.SetActive(hasNext);
                if (hasNext && endedNextMissionText != null)
                {
                    endedNextMissionText.text = mission switch
                    {
                        1 => "DEPLOY: ACT II ->",
                        2 => "DEPLOY: ACT III ->",
                        _ => "DEPLOY: ACT IV ->"
                    };
                }
            }
        }

        private void OpenIntel()
        {
            AudioManager.Instance?.PlayUIClick();
            SelectIntelTab(_activeIntelTab, quiet: true);
            SetGroupState(titleGroup, 0f);
            SetGroupState(intelGroup, 1f);
        }

        public void SelectIntelTab(int tabIndex, bool quiet = false)
        {
            _activeIntelTab = Mathf.Clamp(tabIndex, 0, 2);
            if (!quiet) AudioManager.Instance?.PlayUIClick();
            if (intelBodyText != null)
            {
                var bestWave = SaveManager.EnsureInstance().Data.highestWave;
                intelBodyText.text = StoryDirector.BuildIntelDossierTabText(_activeIntelTab, bestWave);
            }
            if (intelTabBgs != null)
            {
                for (var i = 0; i < intelTabBgs.Length; i++)
                {
                    if (intelTabBgs[i] != null)
                    {
                        intelTabBgs[i].color = i == _activeIntelTab ? Palette.Accent : Palette.SecondaryButtonBg;
                    }
                    if (intelTabLabels != null && i < intelTabLabels.Length && intelTabLabels[i] != null)
                    {
                        intelTabLabels[i].color = i == _activeIntelTab ? Palette.Shadow : Palette.CharacterSecondary;
                    }
                }
            }
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
            _radioQueue.Clear();
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
            var faction = StoryDirector.LastFaction;
            if (callsign.Contains("DRAGIC") || callsign.Contains("KASTNER")) faction = SpeakerFaction.Hostile;
            else if (callsign.Contains("VOLKOV")) faction = SpeakerFaction.Traitor;
            else if (callsign.Contains("RADEK")) faction = SpeakerFaction.ApexBoss;
            else if (callsign.Contains("CHEN")) faction = SpeakerFaction.Command;

            _radioQueue.Clear();
            _radioQueue.Enqueue(new RadioLine(callsign, channel, body, faction, 4.6f));
            StartRadioQueueRunner();
        }

        private void HandleRadioConversation(RadioLine[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            _radioQueue.Clear();
            foreach (var line in lines)
            {
                _radioQueue.Enqueue(line);
            }
            StartRadioQueueRunner();
        }

        private void StartRadioQueueRunner()
        {
            if (radioCommsGroup == null)
            {
                if (_radioQueue.Count > 0)
                {
                    var head = _radioQueue.Peek();
                    LastDisplayedRadioCallsign = head.Callsign;
                    LastDisplayedRadioBody = head.Body;
                }
                return;
            }
            if (_radioRoutine != null) StopCoroutine(_radioRoutine);
            _radioRoutine = StartCoroutine(RadioCommsQueueRoutine());
        }

        private static Color GetFactionColor(SpeakerFaction faction)
        {
            return faction switch
            {
                SpeakerFaction.Command => Palette.RadarGreen,
                SpeakerFaction.Hostile => Palette.EnemySecondary,
                SpeakerFaction.Traitor => Palette.C4Amber,
                SpeakerFaction.ApexBoss => Palette.BossPurple,
                _ => Palette.SquadBlue
            };
        }

        private IEnumerator RadioCommsQueueRoutine()
        {
            while (_radioQueue.Count > 0)
            {
                var line = _radioQueue.Dequeue();
                LastDisplayedRadioCallsign = line.Callsign;
                LastDisplayedRadioBody = line.Body;

                var col = GetFactionColor(line.Faction);
                if (radioCallsignText != null)
                {
                    radioCallsignText.text = line.Callsign + " // " + line.Channel;
                    radioCallsignText.color = col;
                }
                if (radioBodyText != null) radioBodyText.text = line.Body;
                if (radioAccentStripe != null) radioAccentStripe.color = col;

                AudioManager.Instance?.PlayRadioChirp();
                var startAlpha = radioCommsGroup != null ? radioCommsGroup.alpha : 0f;
                if (startAlpha < 0.95f)
                {
                    yield return Ease.Tween(0.18f, t =>
                    {
                        if (radioCommsGroup != null) radioCommsGroup.alpha = Mathf.Lerp(startAlpha, 1f, Ease.OutCubic(t));
                    }, Ease.Linear, unscaled: false);
                }

                yield return Ease.GetWait(line.HoldSeconds);
            }

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
