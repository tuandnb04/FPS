using System.Collections;
using System.Collections.Generic;
using Audio;
using Core;
using TMPro;
using UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Pool;

namespace Feel
{
    /// <summary>
    /// Coordinates tactile game-feel feedback: hit-stop, Cinemachine impulse shakes,
    /// pooled floating combat/pickup text, player scale pops, CoD hitmarkers, and level-up sequences.
    /// </summary>
    public sealed class FeelManager : MonoBehaviour
    {
        public static FeelManager Instance { get; private set; }

        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private Transform floatingTextRoot;

        private Coroutine _hitStopRoutine;
        private Coroutine _playerPopRoutine;
        private Transform _cachedPlayerRoot;
        private UnityEngine.Camera _mainCamera;
        private ObjectPool<TextMeshPro> _floatingTextPool;
        private readonly List<TextMeshPro> _activeTexts = new(24);

        public void BindComponents(CinemachineImpulseSource impulse, Transform textRoot)
        {
            impulseSource = impulse;
            floatingTextRoot = textRoot;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (impulseSource == null)
            {
                impulseSource = GetComponent<CinemachineImpulseSource>();
                if (impulseSource == null)
                {
                    impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
                }
            }
            impulseSource.ImpulseDefinition.ImpulseDuration = 0.16f;
            impulseSource.DefaultVelocity = new Vector3(0.4f, -0.7f, 0.4f);

            if (floatingTextRoot == null)
            {
                var rootGo = new GameObject("FloatingTextRoot");
                rootGo.transform.SetParent(transform, false);
                floatingTextRoot = rootGo.transform;
            }
            _mainCamera = UnityEngine.Camera.main;
            InitializeFloatingTextPool();
        }

        private void InitializeFloatingTextPool()
        {
            if (_floatingTextPool != null) return;
            _floatingTextPool = new ObjectPool<TextMeshPro>(
                createFunc: CreatePooledFloatingText,
                actionOnGet: tmp =>
                {
                    if (tmp != null) tmp.gameObject.SetActive(true);
                },
                actionOnRelease: tmp =>
                {
                    if (tmp != null) tmp.gameObject.SetActive(false);
                },
                actionOnDestroy: tmp =>
                {
                    if (tmp != null) Destroy(tmp.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: 12,
                maxSize: 28
            );

            var prewarm = new List<TextMeshPro>(10);
            for (var i = 0; i < 10; i++)
            {
                prewarm.Add(_floatingTextPool.Get());
            }
            foreach (var t in prewarm)
            {
                _floatingTextPool.Release(t);
            }
        }

        private TextMeshPro CreatePooledFloatingText()
        {
            var go = new GameObject("FloatingText");
            go.transform.SetParent(floatingTextRoot, false);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = 4.2f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = Palette.Shadow;
            tmp.sortingOrder = 15;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
            go.SetActive(false);
            return tmp;
        }

        public void TriggerHitmarker(bool isKill)
        {
            UIManager.Instance?.FlashHitmarker(isKill);
            AudioManager.Instance?.PlayHitmarker(isKill);
        }

        public void TriggerKillFeed(string killerCallsign, string victimLabel, string weaponTag)
        {
            UIManager.Instance?.PushKillFeed(killerCallsign, victimLabel, weaponTag);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetPlayerRoot(Transform player)
        {
            _cachedPlayerRoot = player;
        }

        public void TriggerCameraShake(float force = 0.25f)
        {
            if (impulseSource != null)
            {
                impulseSource.GenerateImpulseWithForce(force);
            }
        }

        public void TriggerHitStop(float duration = 0.055f, float timeScale = 0.05f)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            if (_hitStopRoutine != null)
            {
                StopCoroutine(_hitStopRoutine);
            }
            _hitStopRoutine = StartCoroutine(HitStopRoutine(duration, timeScale));
        }

        private IEnumerator HitStopRoutine(float duration, float slowScale)
        {
            Time.timeScale = Mathf.Clamp(slowScale, 0.02f, 1f);
            yield return Ease.GetWaitRealtime(duration);
            if (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing)
            {
                Time.timeScale = 1f;
            }
            _hitStopRoutine = null;
        }

        public void CancelHitStop()
        {
            if (_hitStopRoutine == null) return;
            StopCoroutine(_hitStopRoutine);
            _hitStopRoutine = null;
        }

        private void TriggerPlayerScalePop(float peakScale = 1.25f, float duration = 0.16f)
        {
            if (_cachedPlayerRoot == null) return;

            if (_playerPopRoutine != null)
            {
                StopCoroutine(_playerPopRoutine);
                _cachedPlayerRoot.localScale = Vector3.one;
            }
            _playerPopRoutine = StartCoroutine(PlayerScalePopRoutine(_cachedPlayerRoot, peakScale, duration));
        }

        private IEnumerator PlayerScalePopRoutine(Transform target, float peakScale, float duration)
        {
            var half = duration * 0.45f;
            yield return Ease.Tween(half, t =>
            {
                if (target == null) return;
                var s = Mathf.LerpUnclamped(1f, peakScale, Ease.OutBack(t));
                target.localScale = new Vector3(s, s, s);
            }, Ease.Linear, unscaled: true);

            yield return Ease.Tween(duration - half, t =>
            {
                if (target == null) return;
                var s = Mathf.LerpUnclamped(peakScale, 1f, Ease.OutCubic(t));
                target.localScale = new Vector3(s, s, s);
            }, Ease.Linear, unscaled: true);

            if (target != null)
            {
                target.localScale = Vector3.one;
            }
            _playerPopRoutine = null;
        }

        public void SpawnFloatingText(Vector3 worldPos, string text, Color color)
        {
            StartCoroutine(FloatingTextRoutine(worldPos, text, color));
        }

        private IEnumerator FloatingTextRoutine(Vector3 worldPos, string label, Color baseColor)
        {
            if (floatingTextRoot == null) yield break;
            InitializeFloatingTextPool();

            var tmp = _floatingTextPool.Get();
            if (tmp == null) yield break;

            var go = tmp.gameObject;
            go.transform.position = worldPos + new Vector3(Random.Range(-0.2f, 0.2f), 1.35f, Random.Range(-0.15f, 0.15f));
            tmp.text = label;
            tmp.color = baseColor;
            _activeTexts.Add(tmp);

            var startPos = go.transform.position;
            var endPos = startPos + Vector3.up * 0.85f;
            const float lifetime = 0.62f;

            yield return Ease.Tween(lifetime, t =>
            {
                if (tmp == null || !go.activeSelf) return;
                if (_mainCamera == null) _mainCamera = UnityEngine.Camera.main;
                if (_mainCamera != null)
                {
                    go.transform.rotation = _mainCamera.transform.rotation;
                }
                var moveT = Ease.OutCubic(t);
                go.transform.position = Vector3.Lerp(startPos, endPos, moveT);
                var scalePop = t < 0.2f ? Mathf.Lerp(0.75f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 0.95f, (t - 0.2f) / 0.8f);
                go.transform.localScale = Vector3.one * scalePop;
                var alpha = t < 0.55f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
                tmp.color = Palette.WithAlpha(baseColor, alpha);
            }, Ease.Linear, unscaled: true);

            if (_activeTexts.Remove(tmp))
            {
                _floatingTextPool.Release(tmp);
            }
        }

        public void OnPickupCollected(Vector3 worldPos, int xpGain, bool isHealth)
        {
            if (isHealth)
            {
                VFXManager.Instance?.Play(VfxKind.PickupBurst, worldPos, Palette.HealthGreen);
                SpawnFloatingText(worldPos, "+25 HP", Palette.HealthGreen);
            }
            else
            {
                VFXManager.Instance?.Play(VfxKind.PickupBurst, worldPos, Palette.Accent);
                SpawnFloatingText(worldPos, "+" + xpGain + " XP", Palette.Accent);
            }

            AudioManager.Instance?.PlayPickupBlip();
            TriggerPlayerScalePop(1.24f);
        }

        public void OnCreditCollected(Vector3 worldPos, int creditAmount)
        {
            VFXManager.Instance?.Play(VfxKind.PickupBurst, worldPos, Palette.CreditGold);
            SpawnFloatingText(worldPos, "+" + creditAmount + " CR", Palette.CreditGold);
            AudioManager.Instance?.PlayPickupBlip();
            TriggerPlayerScalePop(1.18f, 0.14f);
        }

        public void TriggerLevelUpSequence(int newLevel, Transform playerTransform)
        {
            if (playerTransform != null)
            {
                _cachedPlayerRoot = playerTransform;
            }
            StartCoroutine(LevelUpSequenceRoutine(newLevel));
        }

        private IEnumerator LevelUpSequenceRoutine(int newLevel)
        {
            // t = 0.00s: Shockwave VFX + Triad Audio + Hitstop + Camera Shake
            var center = _cachedPlayerRoot != null ? _cachedPlayerRoot.position + Vector3.up * 0.4f : Vector3.zero;
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center, Palette.Accent);
            AudioManager.Instance?.PlayMilestoneSting();
            TriggerCameraShake(0.45f);
            TriggerHitStop(0.06f, 0.12f);

            // t = 0.05s: Player celebratory punch
            yield return Ease.GetWaitRealtime(0.05f);
            TriggerPlayerScalePop(1.32f, 0.25f);

            // t = 0.10s: HUD milestone banner pop
            yield return Ease.GetWaitRealtime(0.05f);
            UIManager.Instance?.ShowMilestoneBanner("RANK " + newLevel + " // SUPPLY DROP");
        }

        public void ClearAllFloatingText()
        {
            for (var i = _activeTexts.Count - 1; i >= 0; i--)
            {
                var tmp = _activeTexts[i];
                if (tmp != null && _floatingTextPool != null)
                {
                    _floatingTextPool.Release(tmp);
                }
            }
            _activeTexts.Clear();
        }
    }
}
