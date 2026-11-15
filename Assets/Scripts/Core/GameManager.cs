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
    /// Authoritative game state coordinator driving Title -> Playing -> Paused -> Ended
    /// lifecycle, score/kills tracking, PlayerPrefs persistence, and clean zero-leak restarts.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        private const string PrefHighScore = "Vanguard_HighScore";

        private GameState currentState = GameState.Title;
        private int score;
        private int kills;
        private int highScore;

        public GameState CurrentState => currentState;
        public int Score => score;
        public int Kills => kills;
        public int HighScore => highScore;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            highScore = PlayerPrefs.GetInt(PrefHighScore, 0);
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
            Time.timeScale = 1f;
            currentState = GameState.Title;
            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, highScore);
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
            }
        }

        public void StartMission()
        {
            AudioManager.Instance?.PlayUIClick();
            AudioManager.Instance?.SetPausedDucking(false);

            FeelManager.Instance?.CancelHitStop();
            FeelManager.Instance?.ClearAllFloatingText();
            EnemySpawner.Instance?.ResetAndClearAll();
            PlayerController.Instance?.ResetPlayer();

            score = 0;
            kills = 0;
            Time.timeScale = 1f;
            currentState = GameState.Playing;

            TopDownCameraRig.Instance?.SetTitleMode(false);
            UIManager.Instance?.UpdateScore(score, highScore);
            UIManager.Instance?.TransitionToState(GameState.Playing);
            EnemySpawner.Instance?.BeginWave(1);
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

            Time.timeScale = 0f;
            currentState = GameState.Ended;

            int waveReached = EnemySpawner.Instance != null ? EnemySpawner.Instance.CurrentWave : 1;
            UIManager.Instance?.UpdateScore(score, highScore);
            UIManager.Instance?.PopulateEndedSummary(victory, score, highScore, kills, waveReached, isNewRecord);
            UIManager.Instance?.TransitionToState(GameState.Ended);
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

            score = 0;
            kills = 0;
            Time.timeScale = 1f;
            currentState = GameState.Title;

            TopDownCameraRig.Instance?.SetTitleMode(true);
            UIManager.Instance?.UpdateScore(0, highScore);
            UIManager.Instance?.TransitionToState(GameState.Title);
        }

        public void AddScoreAndKill(int points)
        {
            score += Mathf.Max(0, points);
            kills++;
            if (score > highScore)
            {
                highScore = score;
                PlayerPrefs.SetInt(PrefHighScore, highScore);
            }
            UIManager.Instance?.UpdateScore(score, highScore);
        }
    }
}
