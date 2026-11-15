using System;
using Audio;
using Camera;
using Character;
using Enemies;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Authoritative game state coordinator driving Title <-> Armory, Playing <-> LevelUpDraft,
    /// Paused, and Ended lifecycles, score/credits tracking, SaveManager persistence, and clean restarts.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private const string PrefHighScore = "Vanguard_HighScore";

        public GameState CurrentState { get; private set; } = GameState.Title;

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

            var save = SaveManager.EnsureInstance();
            HighScore = Mathf.Max(save.Data.highScore, PlayerPrefs.GetInt(PrefHighScore, 0));
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
            var save = SaveManager.EnsureInstance();
            HighScore = Mathf.Max(HighScore, save.Data.highScore);

            Time.timeScale = 1f;
            CurrentState = GameState.Title;
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshArmoryUI();
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

        public void DeployCampaign()
        {
            AudioManager.Instance?.PlayUIClick();
            PrepareFreshMissionState();

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
            PrepareFreshMissionState();
            EnterCombatWaveOneAfterIntro();
        }

        private void PrepareFreshMissionState()
        {
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

            var save = SaveManager.EnsureInstance();
            UIManager.Instance?.UpdateScore(Score, HighScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
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

        public void CompleteCampaignVictory()
        {
            if (CurrentState == GameState.Ended) return;
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

        private void FinishCampaignVictoryImmediate()
        {
            if (CurrentState == GameState.Ended) return;
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
