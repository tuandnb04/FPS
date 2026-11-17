using System;
using Audio;
using Camera;
using Character;
using Enemies;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Core
{
    /// <summary>
    /// Authoritative game state coordinator driving Title <-> Armory, Playing <-> LevelUpDraft,
    /// Cutscenes, Paused, Ended, and Multi-Scene Mission Transitions across all 3 campaign acts.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private const string PrefHighScore = "Vanguard_HighScore";

        public GameState CurrentState { get; private set; } = GameState.Title;

        public int CurrentMissionIndex { get; private set; } = 1;

        public int ConfirmedKillsCount => Kills;

        public CampaignEndingKind ActiveEndingChoice { get; private set; } = CampaignEndingKind.None;

        private static bool _preserveRosterOnNextPrepare;

        private int Score { get; set; }

        private int Kills { get; set; }

        private int RunCreditsEarned { get; set; }

        private int HighScore { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CurrentMissionIndex = ResolveActiveMissionIndex();
            StoryDirector.ActiveMissionIndex = CurrentMissionIndex;
            SaveManager.SelectedMissionIndex = CurrentMissionIndex;

            var save = SaveManager.EnsureInstance();
            HighScore = Mathf.Max(save.Data.highScore, PlayerPrefs.GetInt(PrefHighScore, 0));
        }

        private static int ResolveActiveMissionIndex()
        {
            if (EnemySpawner.Instance != null)
            {
                return Mathf.Clamp(EnemySpawner.Instance.MissionIndex, 1, StoryDirector.CampaignMissionCount);
            }
            var spawner = FindAnyObjectByType<EnemySpawner>();
            if (spawner != null)
            {
                return Mathf.Clamp(spawner.MissionIndex, 1, StoryDirector.CampaignMissionCount);
            }
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName.Contains("Mission04", StringComparison.OrdinalIgnoreCase)) return 4;
            if (sceneName.Contains("Mission03", StringComparison.OrdinalIgnoreCase)) return 3;
            if (sceneName.Contains("Mission02", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            CurrentMissionIndex = ResolveActiveMissionIndex();
            StoryDirector.ActiveMissionIndex = CurrentMissionIndex;
            SaveManager.SelectedMissionIndex = CurrentMissionIndex;

            var save = SaveManager.EnsureInstance();
            HighScore = Mathf.Max(HighScore, save.Data.highScore);
            Time.timeScale = 1f;

            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.RefreshMissionSelectUI(CurrentMissionIndex, save.Data.highestMissionCompleted);

            if (SaveManager.PendingAutoDeploy)
            {
                SaveManager.PendingAutoDeploy = false;
                var carryScore = SaveManager.PendingCarryScore;
                var carryKills = SaveManager.PendingCarryKills;
                var carryCredits = SaveManager.PendingCarryCredits;
                SaveManager.PendingCarryScore = 0;
                SaveManager.PendingCarryKills = 0;
                SaveManager.PendingCarryCredits = 0;

                DeployCampaign(carryScore, carryKills, carryCredits);
                return;
            }

            CurrentState = GameState.Title;
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, HighScore);
            UIManager.Instance?.ApplyImmediateState(GameState.Title);
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            switch (CurrentState)
            {
                case GameState.Playing:
                    SetPaused(true);
                    break;
                case GameState.Paused:
                    SetPaused(false);
                    break;
                case GameState.Armory:
                    CloseArmory();
                    break;
                case GameState.Cutscene:
                    CutsceneDirector.Instance?.SkipCurrentCutscene();
                    break;
                case GameState.Title:
                case GameState.Ended:
                case GameState.LevelUpDraft:
                case GameState.Decision:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void OpenArmory()
        {
            if (CurrentState != GameState.Title && CurrentState != GameState.Ended) return;
            AudioManager.Instance?.PlayUIClick();
            Time.timeScale = 1f;
            CurrentState = GameState.Armory;
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.TransitionToState(GameState.Armory);
        }

        public void CloseArmory()
        {
            if (CurrentState != GameState.Armory) return;
            AudioManager.Instance?.PlayUIClick();
            PlayerController.Instance?.ApplySaveLoadout();
            var save = SaveManager.EnsureInstance();
            UIManager.Instance?.UpdateScore(0, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            CurrentState = GameState.Title;
            UIManager.Instance?.TransitionToState(GameState.Title);
        }

        public bool HasNextMission => CurrentMissionIndex < StoryDirector.CampaignMissionCount;

        public void ResetFullCampaignRoster()
        {
            AudioManager.Instance?.PlayUIClick();
            var save = SaveManager.EnsureInstance();
            save.ResetCampaignRoster();
            UIManager.Instance?.RefreshMissionSelectUI(CurrentMissionIndex, save.Data.highestMissionCompleted);
        }

        public void SelectTitleMission(int missionIndex)
        {
            var target = Mathf.Clamp(missionIndex, 1, StoryDirector.CampaignMissionCount);
            AudioManager.Instance?.PlayUIClick();
            SaveManager.SelectedMissionIndex = target;

            var save = SaveManager.EnsureInstance();
            save.SyncRosterForActEntry(target, preserveExistingLosses: false);

            if (target == CurrentMissionIndex)
            {
                StoryDirector.ActiveMissionIndex = target;
                UIManager.Instance?.RefreshMissionSelectUI(target, save.Data.highestMissionCompleted);
                return;
            }

            SaveManager.PendingAutoDeploy = false;
            SaveManager.PendingCarryScore = 0;
            SaveManager.PendingCarryKills = 0;
            SaveManager.PendingCarryCredits = 0;
            _preserveRosterOnNextPrepare = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(StoryDirector.GetMissionSceneName(target));
        }

        public void LoadNextMission()
        {
            if (!HasNextMission)
            {
                ReturnToTitle();
                return;
            }

            AudioManager.Instance?.PlayUIClick();
            var nextIndex = CurrentMissionIndex + 1;
            _preserveRosterOnNextPrepare = true;
            SaveManager.SelectedMissionIndex = nextIndex;
            SaveManager.PendingAutoDeploy = true;
            SaveManager.PendingCarryScore = Score;
            SaveManager.PendingCarryKills = Kills;
            SaveManager.PendingCarryCredits = RunCreditsEarned;
            Time.timeScale = 1f;
            SceneManager.LoadScene(StoryDirector.GetMissionSceneName(nextIndex));
        }

        public void DeployCampaign()
        {
            DeployCampaign(0, 0, 0);
        }

        private void DeployCampaign(int carryScore, int carryKills, int carryCredits)
        {
            AudioManager.Instance?.PlayUIClick();
            PrepareFreshMissionState(carryScore, carryKills, carryCredits);

            if (CutsceneDirector.Instance != null)
            {
                CurrentState = GameState.Cutscene;
                TopDownCameraRig.Instance?.SetTitleMode(false);
                UIManager.Instance?.ApplyImmediateState(GameState.Cutscene);
                CutsceneDirector.Instance.PlayCutscene(CutsceneType.IntroInfil, EnterCombatWaveOneAfterIntro);
            }
            else
            {
                EnterCombatWaveOneAfterIntro();
            }
        }

        public void StartMission()
        {
            AudioManager.Instance?.PlayUIClick();
            PrepareFreshMissionState(0, 0, 0);
            EnterCombatWaveOneAfterIntro();
        }

        private void PrepareFreshMissionState(int carryScore, int carryKills, int carryCredits)
        {
            StoryDirector.ActiveMissionIndex = CurrentMissionIndex;
            ActiveEndingChoice = CampaignEndingKind.None;

            var save = SaveManager.EnsureInstance();
            save.SyncRosterForActEntry(CurrentMissionIndex, _preserveRosterOnNextPrepare);
            _preserveRosterOnNextPrepare = false;

            AudioManager.Instance?.SetPausedDucking(false);
            FeelManager.Instance?.CancelHitStop();
            FeelManager.Instance?.ClearAllFloatingText();
            CutsceneDirector.Instance?.ResetCampaignProps();
            EnemySpawner.Instance?.ResetAndClearAll();
            PlayerController.Instance?.ResetPlayer();
            SquadDirector.Instance?.ResetSquadForMission();
            StoryDirector.ResetCampaignRun();
            UIManager.Instance?.HideRadioCommsImmediate();
            UIManager.Instance?.ResetMissionClock();

            Score = Mathf.Max(0, carryScore);
            Kills = Mathf.Max(0, carryKills);
            RunCreditsEarned = Mathf.Max(0, carryCredits);
            Time.timeScale = 1f;

            UIManager.Instance?.UpdateScore(Score, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, RunCreditsEarned);
            UIManager.Instance?.RefreshWeaponBar();
        }

        private void EnterCombatWaveOneAfterIntro()
        {
            CurrentState = GameState.Playing;
            TopDownCameraRig.Instance?.SetTitleMode(false);
            UIManager.Instance?.TransitionToState(GameState.Playing);
            StoryDirector.NotifyMissionDeployed();
            EnemySpawner.Instance?.BeginWave(1);
        }

        public void TriggerCutscene(CutsceneType cutsceneType, Action onComplete = null)
        {
            if (CurrentState == GameState.Ended) return;
            if (CutsceneDirector.Instance == null)
            {
                onComplete?.Invoke();
                return;
            }

            FeelManager.Instance?.CancelHitStop();
            CurrentState = GameState.Cutscene;
            UIManager.Instance?.ApplyImmediateState(GameState.Cutscene);
            CutsceneDirector.Instance.PlayCutscene(cutsceneType, () =>
            {
                if (CurrentState != GameState.Ended)
                {
                    CurrentState = GameState.Playing;
                    UIManager.Instance?.TransitionToState(GameState.Playing);
                }
                onComplete?.Invoke();
            });
        }

        public void SelectDraftSkill(RogueliteSkillId skill)
        {
            if (CurrentState != GameState.LevelUpDraft) return;
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(false);

            PlayerController.Instance?.ApplyRogueliteSkill(skill);
            Time.timeScale = 1f;
            CurrentState = GameState.Playing;
            UIManager.Instance?.TransitionToState(GameState.Playing);
        }

        public void SetPaused(bool paused)
        {
            switch (paused)
            {
                case true when CurrentState != GameState.Playing:
                case false when CurrentState != GameState.Paused:
                    return;
            }

            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(paused);

            FeelManager.Instance?.CancelHitStop();
            Time.timeScale = paused ? 0f : 1f;
            CurrentState = paused ? GameState.Paused : GameState.Playing;
            UIManager.Instance?.TransitionToState(CurrentState);
        }

        public void EndMission(bool victory)
        {
            if (CurrentState == GameState.Ended) return;

            AudioManager.Instance?.SetPausedDucking(true);
            FeelManager.Instance?.CancelHitStop();

            var isNewRecord = Score > HighScore;
            if (isNewRecord)
            {
                HighScore = Score;
                PlayerPrefs.SetInt(PrefHighScore, HighScore);
                PlayerPrefs.Save();
            }

            var waveReached = EnemySpawner.Instance != null ? EnemySpawner.Instance.CurrentWave : 1;
            var save = SaveManager.EnsureInstance();
            save.RecordRunResult(Score, Kills, waveReached);
            if (victory)
            {
                save.RecordMissionCompleted(CurrentMissionIndex);
                UIManager.Instance?.RefreshMissionSelectUI(CurrentMissionIndex, save.Data.highestMissionCompleted);
            }
            HighScore = Mathf.Max(HighScore, save.Data.highScore);

            Time.timeScale = 0f;
            CurrentState = GameState.Ended;

            UIManager.Instance?.UpdateScore(Score, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, RunCreditsEarned);
            UIManager.Instance?.PopulateEndedSummary(victory, Score, HighScore, Kills, waveReached, isNewRecord);
            UIManager.Instance?.TransitionToState(GameState.Ended);
            if (victory)
            {
                AudioManager.Instance?.PlayMilestoneSting();
            }
        }

        public void PresentAct4CoreChoice()
        {
            if (CurrentState == GameState.Ended || CurrentState == GameState.Decision) return;
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(true);
            FeelManager.Instance?.CancelHitStop();
            Time.timeScale = 0f;
            CurrentState = GameState.Decision;
            UIManager.Instance?.TransitionToState(GameState.Decision);
        }

        public void ResolveAct4CoreChoice(CampaignEndingKind choice)
        {
            if (CurrentState != GameState.Decision && CurrentState != GameState.Playing) return;
            ActiveEndingChoice = choice;
            SaveManager.EnsureInstance().RecordEndingChoice(choice);
            AudioManager.Instance?.PlayMilestoneSting();
            AudioManager.Instance?.SetPausedDucking(false);

            Time.timeScale = 1f;
            CurrentState = GameState.Playing;
            UIManager.Instance?.TransitionToState(GameState.Playing);
            StoryDirector.NotifyEndingChoiceMade(choice);
            EnemySpawner.Instance?.CompleteAct4CoreDecision(choice);
        }

        public void CompleteCampaignVictory()
        {
            if (CurrentState == GameState.Ended) return;
            ApplyActEndScriptedSacrifice();
            if (CutsceneDirector.Instance != null)
            {
                CurrentState = GameState.Cutscene;
                UIManager.Instance?.ApplyImmediateState(GameState.Cutscene);
                CutsceneDirector.Instance.PlayCutscene(CutsceneType.VictoryEvac, FinishCampaignVictoryImmediate);
            }
            else
            {
                FinishCampaignVictoryImmediate();
            }
        }

        private void ApplyActEndScriptedSacrifice()
        {
            switch (CurrentMissionIndex)
            {
                case 1:
                    SaveManager.EnsureInstance().MarkOperatorKia(SquadCallsigns.Recon);
                    SquadDirector.Instance?.MarkMemberKiaByTag(SquadCallsigns.Recon, broadcastRadio: false);
                    break;
                case 2:
                    SaveManager.EnsureInstance().MarkOperatorKia(SquadCallsigns.Breacher);
                    SquadDirector.Instance?.MarkMemberKiaByTag(SquadCallsigns.Breacher, broadcastRadio: false);
                    break;
                case 3:
                    SaveManager.EnsureInstance().MarkOperatorKia(SquadCallsigns.Officer);
                    SquadDirector.Instance?.MarkMemberKiaByTag(SquadCallsigns.Officer, broadcastRadio: false);
                    break;
            }
        }

        private void FinishCampaignVictoryImmediate()
        {
            if (CurrentState == GameState.Ended) return;
            ApplyActEndScriptedSacrifice();
            AddRunCredits(StoryDirector.CampaignVictoryBonusCredits);
            EndMission(true);
        }

        public void RestartMission()
        {
            StartMission();
        }

        public void ReturnToTitle()
        {
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(false);

            FeelManager.Instance?.CancelHitStop();
            FeelManager.Instance?.ClearAllFloatingText();
            CutsceneDirector.Instance?.ResetCampaignProps();
            EnemySpawner.Instance?.ResetAndClearAll();
            PlayerController.Instance?.ResetPlayer();
            SquadDirector.Instance?.ResetSquadForMission();
            StoryDirector.ResetCampaignRun();
            UIManager.Instance?.HideRadioCommsImmediate();

            Score = 0;
            Kills = 0;
            RunCreditsEarned = 0;
            Time.timeScale = 1f;
            CurrentState = GameState.Title;

            var save = SaveManager.EnsureInstance();
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.TransitionToState(GameState.Title);
        }

        public void AddScoreAndKill(int points)
        {
            Score += Mathf.Max(0, points);
            Kills++;
            StoryDirector.NotifyKillCount(Kills);
            if (Score > HighScore)
            {
                HighScore = Score;
                PlayerPrefs.SetInt(PrefHighScore, HighScore);
            }
            UIManager.Instance?.UpdateScore(Score, HighScore);
        }

        public void AddRunCredits(int amount)
        {
            if (amount <= 0) return;
            RunCreditsEarned += amount;
            var save = SaveManager.EnsureInstance();
            save.AddCredits(amount);
            UIManager.Instance?.UpdateCredits(save.Data.credits, RunCreditsEarned);
        }
    }
}
