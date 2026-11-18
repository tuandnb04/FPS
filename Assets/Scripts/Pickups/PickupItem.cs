using System;
using Audio;
using Character;
using Core;
using Feel;
using UnityEngine;
using UnityEngine.Pool;

namespace Pickups
{
    public enum PickupKind
    {
        Medkit,
        CreditCoin,
        MunitionCache
    }

    /// <summary>
    /// Pooled Medkit, Cyber-Credit, or Munition Resupply Cache collectible. Features idle spin,
    /// scaled magnetic pull, and multi-mesh shape swapping per pickup kind.
    /// </summary>
    public sealed class PickupItem : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private ObjectPool<PickupItem> _ownerPool;
        private float _healValue;
        private int _creditValue;
        private float _spinAngle;
        private Vector3 _baseLocalVisualPos;
        private Transform _visualTransform;
        private GameObject _medkitVisual;
        private GameObject _creditVisual;
        private GameObject _munitionVisual;
        private MeshRenderer _medkitRenderer;
        private MeshRenderer _creditRenderer;
        private MeshRenderer _munitionRenderer;
        private MaterialPropertyBlock _mpb;
        private Action<PickupItem> _onReleasedCallback;
        private bool _collected;

        private PickupKind Kind { get; set; }

        public void ConfigureMultiVisuals(
            Transform visualRoot,
            GameObject medObj, MeshRenderer medMr,
            GameObject credObj, MeshRenderer credMr,
            GameObject munitionObj = null, MeshRenderer munitionMr = null)
        {
            _visualTransform = visualRoot;
            _medkitVisual = medObj;
            _medkitRenderer = medMr;
            _creditVisual = credObj;
            _creditRenderer = credMr;
            _munitionVisual = munitionObj;
            _munitionRenderer = munitionMr;
            if (_visualTransform != null)
            {
                _baseLocalVisualPos = _visualTransform.localPosition;
            }
        }

        public void Initialize(
            ObjectPool<PickupItem> pool,
            Vector3 worldPos,
            PickupKind pickupKind,
            float healAmount,
            Action<PickupItem> onReleased,
            int creditAmount = 15)
        {
            _ownerPool = pool;
            Kind = pickupKind;
            _healValue = healAmount;
            _creditValue = creditAmount;
            _onReleasedCallback = onReleased;
            _collected = false;

            transform.position = new Vector3(worldPos.x, 0.24f, worldPos.z);
            _spinAngle = UnityEngine.Random.Range(0f, 360f);

            var col = Kind switch
            {
                PickupKind.Medkit => Palette.HealthGreen,
                PickupKind.MunitionCache => Palette.NvgGreen,
                _ => Palette.CreditGold
            };

            if (_medkitVisual != null) _medkitVisual.SetActive(Kind == PickupKind.Medkit);
            if (_creditVisual != null) _creditVisual.SetActive(Kind == PickupKind.CreditCoin);
            if (_munitionVisual != null) _munitionVisual.SetActive(Kind == PickupKind.MunitionCache);

            var activeMr = Kind switch
            {
                PickupKind.Medkit => _medkitRenderer,
                PickupKind.MunitionCache => _munitionRenderer != null ? _munitionRenderer : _creditRenderer,
                _ => _creditRenderer
            };

            if (activeMr == null) return;
            _mpb ??= new MaterialPropertyBlock();
            activeMr.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, col);
            _mpb.SetColor(EmissionColor, col * 0.45f);
            activeMr.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            if (_collected) return;

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            _spinAngle += 32f * dt;
            if (_visualTransform != null)
            {
                _visualTransform.localPosition = _baseLocalVisualPos;
                _visualTransform.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);
            }

            var player = PlayerController.Instance;
            if (player == null || player.IsDead) return;
            var toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            var dist = toPlayer.magnitude;
            var pullRadius = 3.6f + player.PickupMagnetBonus;

            if (dist < pullRadius && dist > 0.01f)
            {
                var pullSpeed = Mathf.Lerp(15f, 5f, dist / pullRadius);
                transform.position += toPlayer.normalized * (pullSpeed * dt);
            }

            if (dist <= 0.95f)
            {
                Collect(player);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_collected || other == null) return;
            if (!other.CompareTag("Player") && other.gameObject.layer != 6) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player != null && !player.IsDead)
            {
                Collect(player);
            }
        }

        private void Collect(PlayerController player)
        {
            if (_collected) return;
            _collected = true;

            switch (Kind)
            {
                case PickupKind.Medkit:
                    player.Heal(_healValue);
                    player.AddArmorAndTrauma(25f, player.TraumaKits < 2 ? 1 : 0);
                    player.AddFragGrenades(1);
                    FeelManager.Instance?.OnPickupCollected(transform.position, 0, isHealth: true);
                    break;
                case PickupKind.CreditCoin:
                {
                    var gained = Mathf.Max(1, Mathf.RoundToInt(_creditValue * player.CreditMultiplier));
                    GameManager.Instance?.AddRunCredits(gained);
                    FeelManager.Instance?.OnCreditCollected(transform.position, gained);
                    break;
                }
                case PickupKind.MunitionCache:
                    player.Weapon?.InstantRefillCurrentMagazine();
                    player.AddFragGrenades(1);
                    player.AddArmorAndTrauma(15f);
                    AudioManager.Instance?.PlayPickupBlip();
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.7f, "RESUPPLY // +MAG +FRAG +ARMOR", Palette.NvgGreen);
                    break;
            }

            ForceReturnToPool();
        }

        public void ForceReturnToPool()
        {
            if (!gameObject.activeSelf && _collected)
            {
                return;
            }
            _collected = true;
            _onReleasedCallback?.Invoke(this);
            if (_ownerPool != null)
            {
                _ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
