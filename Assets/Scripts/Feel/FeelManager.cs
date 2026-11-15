using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using TopDownShooter.Audio;
using TopDownShooter.Core;
using TopDownShooter.UI;

namespace TopDownShooter.Feel
{
    /// <summary>
    /// Coordinates tactile game-feel feedback: hit-stop, Cinemachine impulse shakes,
    /// stylized floating combat/pickup text, player scale pops, and worked level-up sequences.
    /// </summary>
    public sealed class FeelManager : MonoBehaviour
    {
        public static FeelManager Instance { get; private set; }

        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private Transform floatingTextRoot;

        private Coroutine hitStopRoutine;
        private Coroutine playerPopRoutine;
        private Transform cachedPlayerRoot;
        private int activeFloatingTextCount;

        public int ActiveFloatingTextCount => activeFloatingTextCount;

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
                GameObject rootGo = new GameObject("FloatingTextRoot");
                rootGo.transform.SetParent(transform, false);
                floatingTextRoot = rootGo.transform;
            }
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
            cachedPlayerRoot = player;
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

            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
            }
            hitStopRoutine = StartCoroutine(HitStopRoutine(duration, timeScale));
        }

        private IEnumerator HitStopRoutine(float duration, float slowScale)
        {
            Time.timeScale = Mathf.Clamp(slowScale, 0.02f, 1f);
            yield return new WaitForSecondsRealtime(duration);
            if (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing)
            {
                Time.timeScale = 1f;
            }
            hitStopRoutine = null;
        }

        public void CancelHitStop()
        {
            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
                hitStopRoutine = null;
            }
        }

        public void TriggerPlayerScalePop(float peakScale = 1.25f, float duration = 0.16f)
        {
            if (cachedPlayerRoot == null) return;

            if (playerPopRoutine != null)
            {
                StopCoroutine(playerPopRoutine);
                cachedPlayerRoot.localScale = Vector3.one;
            }
            playerPopRoutine = StartCoroutine(PlayerScalePopRoutine(cachedPlayerRoot, peakScale, duration));
        }

        private IEnumerator PlayerScalePopRoutine(Transform target, float peakScale, float duration)
        {
            float half = duration * 0.45f;
            yield return Ease.Tween(half, t =>
            {
                if (target != null)
                {
                    float s = Mathf.LerpUnclamped(1f, peakScale, Ease.OutBack(t));
                    target.localScale = new Vector3(s, s, s);
                }
            }, Ease.Linear, unscaled: true);

            yield return Ease.Tween(duration - half, t =>
            {
                if (target != null)
                {
                    float s = Mathf.LerpUnclamped(peakScale, 1f, Ease.OutCubic(t));
                    target.localScale = new Vector3(s, s, s);
                }
            }, Ease.Linear, unscaled: true);

            if (target != null)
            {
                target.localScale = Vector3.one;
            }
            playerPopRoutine = null;
        }

        public void SpawnFloatingText(Vector3 worldPos, string text, Color color)
        {
            StartCoroutine(FloatingTextRoutine(worldPos, text, color));
        }

        private IEnumerator FloatingTextRoutine(Vector3 worldPos, string label, Color baseColor)
        {
            if (floatingTextRoot == null) yield break;

            GameObject go = new GameObject("FloatingText");
            go.transform.SetParent(floatingTextRoot, false);
            go.transform.position = worldPos + new Vector3(UnityEngine.Random.Range(-0.2f, 0.2f), 1.35f, UnityEngine.Random.Range(-0.15f, 0.15f));

            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = 4.2f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = baseColor;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = Palette.Shadow;
            tmp.sortingOrder = 15;

            RectTransform rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(4f, 1.5f);

            activeFloatingTextCount++;

            Vector3 startPos = go.transform.position;
            Vector3 endPos = startPos + Vector3.up * 0.85f;
            const float lifetime = 0.62f;

            yield return Ease.Tween(lifetime, t =>
            {
                if (go == null) return;
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    go.transform.rotation = cam.transform.rotation;
                }
                float moveT = Ease.OutCubic(t);
                go.transform.position = Vector3.Lerp(startPos, endPos, moveT);
                float scalePop = t < 0.2f ? Mathf.Lerp(0.75f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 0.95f, (t - 0.2f) / 0.8f);
                go.transform.localScale = Vector3.one * scalePop;
                float alpha = t < 0.55f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
                tmp.color = Palette.WithAlpha(baseColor, alpha);
            }, Ease.Linear, unscaled: true);

            activeFloatingTextCount = Mathf.Max(0, activeFloatingTextCount - 1);
            if (go != null)
            {
                Destroy(go);
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
            TriggerPlayerScalePop(1.24f, 0.16f);
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
                cachedPlayerRoot = playerTransform;
            }
            StartCoroutine(LevelUpSequenceRoutine(newLevel));
        }

        private IEnumerator LevelUpSequenceRoutine(int newLevel)
        {
            // t = 0.00s: Shockwave VFX + Triad Audio + Hitstop + Camera Shake
            Vector3 center = cachedPlayerRoot != null ? cachedPlayerRoot.position + Vector3.up * 0.4f : Vector3.zero;
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center, Palette.Accent);
            AudioManager.Instance?.PlayMilestoneSting();
            TriggerCameraShake(0.45f);
            TriggerHitStop(0.06f, 0.12f);

            // t = 0.05s: Player celebratory punch
            yield return new WaitForSecondsRealtime(0.05f);
            TriggerPlayerScalePop(1.32f, 0.25f);

            // t = 0.10s: HUD milestone banner pop
            yield return new WaitForSecondsRealtime(0.05f);
            UIManager.Instance?.ShowMilestoneBanner("LEVEL " + newLevel + " // OVERDRIVE");
        }

        public void ClearAllFloatingText()
        {
            if (floatingTextRoot == null) return;
            for (int i = floatingTextRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(floatingTextRoot.GetChild(i).gameObject);
            }
            activeFloatingTextCount = 0;
        }
    }
}
