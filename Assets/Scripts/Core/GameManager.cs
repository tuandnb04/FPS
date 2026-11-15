using UnityEngine;
using UnityEngine.InputSystem;
using TopDownShooter.Audio;
using TopDownShooter.CameraRig;
using TopDownShooter.Character;
using TopDownShooter.Enemies;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Authoritative game state coordinator driving Title <-> Armory, Playing <-> LevelUpDraft,
    /// Paused, and Ended lifecycles, score/credits tracking, SaveManager persistence, and clean restarts.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private const string PrefHighScore = "Vanguard_HighScore";

        private GameState currentState = GameState.Title;
        private int score;
        private int kills;
        private int runCreditsEarned;
        private int highScore;

        public GameState CurrentState => currentState;
        public int Score => score;
        public int Kills => kills;
        public int RunCreditsEarned => runCreditsEarned;
        public int HighScore => highScore;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SaveManager save = SaveManager.EnsureInstance();
            highScore = Mathf.Max(save.Data.highScore, PlayerPrefs.GetInt(PrefHighScore, 0));
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
            SaveManager save = SaveManager.EnsureInstance();
            highScore = Mathf.Max(highScore, save.Data.highScore);

            Time.timeScale = 1f;
            currentState = GameState.Title;
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, highScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.ApplyImmediateState(GameState.Title);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (currentState == GameState.Playing)
                {
                    SetPaused(true);
                }
                else if (currentState == GameState.Paused)
                {
                    SetPaused(false);
                }
                else if (currentState == GameState.Armory)
                {
                    CloseArmory();
                }
            }
        }

        public void OpenArmory()
        {
            if (currentState != GameState.Title && currentState != GameState.Ended) return;
            AudioManager.Instance?.PlayUIClick();
            Time.timeScale = 1f;
            currentState = GameState.Armory;
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.TransitionToState(GameState.Armory);
        }

        public void CloseArmory()
        {
            if (currentState != GameState.Armory) return;
            AudioManager.Instance?.PlayUIClick();
            PlayerController.Instance?.ApplySaveLoadout();
            SaveManager save = SaveManager.EnsureInstance();
            UIManager.Instance?.UpdateScore(0, highScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            currentState = GameState.Title;
            UIManager.Instance?.TransitionToState(GameState.Title);
        }

        public void StartMission()
        {
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(false);

            FeelManager.Instance?.CancelHitStop();
            FeelManager.Instance?.ClearAllFloatingText();
            EnemySpawner.Instance?.ResetAndClearAll();
            PlayerController.Instance?.ResetPlayer();
            StoryDirector.ResetCampaignRun();
            UIManager.Instance?.HideRadioCommsImmediate();

            score = 0;
            kills = 0;
            runCreditsEarned = 0;
            Time.timeScale = 1f;
            currentState = GameState.Playing;

            SaveManager save = SaveManager.EnsureInstance();
            TopDownCameraRig.Instance?.SetTitleMode(false);
            UIManager.Instance?.UpdateScore(score, highScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshWeaponBar();
            UIManager.Instance?.TransitionToState(GameState.Playing);
            StoryDirector.NotifyMissionDeployed();
            EnemySpawner.Instance?.BeginWave(1);
        }

        public void OpenLevelUpDraft()
        {
            if (currentState != GameState.Playing) return;
            PlayerController player = PlayerController.Instance;
            if (player == null) return;

            var options = player.RollDraftOptions(3);
            if (options.Count == 0) return;

            AudioManager.Instance?.SetPausedDucking(true);
            FeelManager.Instance?.CancelHitStop();
            Time.timeScale = 0f;
            currentState = GameState.LevelUpDraft;
            UIManager.Instance?.PresentDraftOptions(options, player);
            UIManager.Instance?.TransitionToState(GameState.LevelUpDraft);
        }

        public void SelectDraftSkill(RogueliteSkillId skill)
        {
            if (currentState != GameState.LevelUpDraft) return;
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(false);

            PlayerController.Instance?.ApplyRogueliteSkill(skill);
            Time.timeScale = 1f;
            currentState = GameState.Playing;
            UIManager.Instance?.TransitionToState(GameState.Playing);
        }

        public void SetPaused(bool paused)
        {
            if (paused && currentState != GameState.Playing) return;
            if (!paused && currentState != GameState.Paused) return;

            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(paused);

            FeelManager.Instance?.CancelHitStop();
            Time.timeScale = paused ? 0f : 1f;
            currentState = paused ? GameState.Paused : GameState.Playing;
            UIManager.Instance?.TransitionToState(currentState);
        }

        public void EndMission(bool victory)
        {
            if (currentState == GameState.Ended) return;

            AudioManager.Instance?.SetPausedDucking(true);
            FeelManager.Instance?.CancelHitStop();

            bool isNewRecord = score > highScore;
            if (isNewRecord)
            {
                highScore = score;
                PlayerPrefs.SetInt(PrefHighScore, highScore);
                PlayerPrefs.Save();
            }

            int waveReached = EnemySpawner.Instance != null ? EnemySpawner.Instance.CurrentWave : 1;
            SaveManager save = SaveManager.EnsureInstance();
            save.RecordRunResult(score, kills, waveReached);
            highScore = Mathf.Max(highScore, save.Data.highScore);

            Time.timeScale = 0f;
            currentState = GameState.Ended;

            UIManager.Instance?.UpdateScore(score, highScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, runCreditsEarned);
            UIManager.Instance?.PopulateEndedSummary(victory, score, highScore, kills, waveReached, isNewRecord);
            UIManager.Instance?.TransitionToState(GameState.Ended);
            if (victory)
            {
                AudioManager.Instance?.PlayMilestoneSting();
            }
        }

        public void CompleteCampaignVictory()
        {
            if (currentState == GameState.Ended) return;
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
            EnemySpawner.Instance?.ResetAndClearAll();
            PlayerController.Instance?.ResetPlayer();
            StoryDirector.ResetCampaignRun();
            UIManager.Instance?.HideRadioCommsImmediate();

            score = 0;
            kills = 0;
            runCreditsEarned = 0;
            Time.timeScale = 1f;
            currentState = GameState.Title;

            SaveManager save = SaveManager.EnsureInstance();
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, highScore);
            UIManager.Instance?.UpdateCredits(save.Data.credits, 0);
            UIManager.Instance?.RefreshArmoryUI();
            UIManager.Instance?.TransitionToState(GameState.Title);
        }

        public void AddScoreAndKill(int points)
        {
            score += Mathf.Max(0, points);
            kills++;
            StoryDirector.NotifyKillCount(kills);
            if (score > highScore)
            {
                highScore = score;
                PlayerPrefs.SetInt(PrefHighScore, highScore);
            }
            UIManager.Instance?.UpdateScore(score, highScore);
        }

        public void AddRunCredits(int amount)
        {
            if (amount <= 0) return;
            runCreditsEarned += amount;
            SaveManager save = SaveManager.EnsureInstance();
            save.AddCredits(amount);
            UIManager.Instance?.UpdateCredits(save.Data.credits, runCreditsEarned);
        }
    }
}
