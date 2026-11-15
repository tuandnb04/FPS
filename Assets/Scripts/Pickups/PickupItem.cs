using System;
using Character;
using Core;
using Feel;
using UnityEngine;
using UnityEngine.Pool;

namespace Pickups
{
    public enum PickupKind
    {
        XpShard,
        Medkit,
        CreditCoin
    }

    /// <summary>
    /// Pooled XP shard, Medkit, or Cyber-Credit collectible. Features idle spin,
    /// scaled magnetic pull, and multi-mesh shape swapping per pickup kind.
    /// </summary>
    public sealed class PickupItem : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private ObjectPool<PickupItem> _ownerPool;
        private int _xpValue;
        private float _healValue;
        private int _creditValue;
        private float _spinAngle;
        private Vector3 _baseLocalVisualPos;
        private Transform _visualTransform;
        private MeshRenderer _visualRenderer;
        private GameObject _xpVisual;
        private GameObject _medkitVisual;
        private GameObject _creditVisual;
        private MeshRenderer _xpRenderer;
        private MeshRenderer _medkitRenderer;
        private MeshRenderer _creditRenderer;
        private MaterialPropertyBlock _mpb;
        private Action<PickupItem> _onReleasedCallback;
        private bool _collected;

        public PickupKind Kind { get; private set; }

        public void ConfigureMultiVisuals(
            Transform visualRoot,
            GameObject xpObj, MeshRenderer xpMr,
            GameObject medObj, MeshRenderer medMr,
            GameObject credObj, MeshRenderer credMr)
        {
            _visualTransform = visualRoot;
            _xpVisual = xpObj;
            _xpRenderer = xpMr;
            _medkitVisual = medObj;
            _medkitRenderer = medMr;
            _creditVisual = credObj;
            _creditRenderer = credMr;
            if (_visualTransform != null)
            {
                _baseLocalVisualPos = _visualTransform.localPosition;
            }
        }

        public void Initialize(
            ObjectPool<PickupItem> pool,
            Vector3 worldPos,
            PickupKind pickupKind,
            int xpAmount,
            float healAmount,
            Action<PickupItem> onReleased,
            int creditAmount = 15)
        {
            _ownerPool = pool;
            Kind = pickupKind;
            _xpValue = xpAmount;
            _healValue = healAmount;
            _creditValue = creditAmount;
            _onReleasedCallback = onReleased;
            _collected = false;

            transform.position = new Vector3(worldPos.x, 0.24f, worldPos.z);
            _spinAngle = UnityEngine.Random.Range(0f, 360f);

            var col = Kind switch
            {
                PickupKind.Medkit => Palette.HealthGreen,
                PickupKind.CreditCoin => Palette.CreditGold,
                _ => Palette.Accent
            };

            if (_xpVisual != null) _xpVisual.SetActive(Kind == PickupKind.XpShard);
            if (_medkitVisual != null) _medkitVisual.SetActive(Kind == PickupKind.Medkit);
            if (_creditVisual != null) _creditVisual.SetActive(Kind == PickupKind.CreditCoin);

            var activeMr = Kind == PickupKind.Medkit
                ? (_medkitRenderer != null ? _medkitRenderer : _visualRenderer)
                : (Kind == PickupKind.CreditCoin ? (_creditRenderer != null ? _creditRenderer : _visualRenderer) : (_xpRenderer != null ? _xpRenderer : _visualRenderer));

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
            if (_collected) return;
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
                case PickupKind.XpShard:
                default:
                    player.AddXp(_xpValue);
                    FeelManager.Instance?.OnPickupCollected(transform.position, _xpValue, isHealth: false);
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
