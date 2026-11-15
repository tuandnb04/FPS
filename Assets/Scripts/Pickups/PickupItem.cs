using System;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;

namespace TopDownShooter.Pickups
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
        private ObjectPool<PickupItem> ownerPool;
        private PickupKind kind;
        private int xpValue;
        private float healValue;
        private int creditValue;
        private float spinAngle;
        private Vector3 baseLocalVisualPos;
        private Transform visualTransform;
        private MeshRenderer visualRenderer;
        private GameObject xpVisual;
        private GameObject medkitVisual;
        private GameObject creditVisual;
        private MeshRenderer xpRenderer;
        private MeshRenderer medkitRenderer;
        private MeshRenderer creditRenderer;
        private MaterialPropertyBlock mpb;
        private Action<PickupItem> onReleasedCallback;
        private bool collected;

        public PickupKind Kind => kind;

        public void ConfigureVisual(Transform visual, MeshRenderer rendererComp)
        {
            visualTransform = visual;
            visualRenderer = rendererComp;
            if (visualTransform != null)
            {
                baseLocalVisualPos = visualTransform.localPosition;
            }
        }

        public void ConfigureMultiVisuals(
            Transform visualRoot,
            GameObject xpObj, MeshRenderer xpMr,
            GameObject medObj, MeshRenderer medMr,
            GameObject credObj, MeshRenderer credMr)
        {
            visualTransform = visualRoot;
            xpVisual = xpObj;
            xpRenderer = xpMr;
            medkitVisual = medObj;
            medkitRenderer = medMr;
            creditVisual = credObj;
            creditRenderer = credMr;
            if (visualTransform != null)
            {
                baseLocalVisualPos = visualTransform.localPosition;
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
            ownerPool = pool;
            kind = pickupKind;
            xpValue = xpAmount;
            healValue = healAmount;
            creditValue = creditAmount;
            onReleasedCallback = onReleased;
            collected = false;

            transform.position = new Vector3(worldPos.x, 0.55f, worldPos.z);
            spinAngle = UnityEngine.Random.Range(0f, 360f);

            Color col = kind == PickupKind.Medkit
                ? Palette.HealthGreen
                : (kind == PickupKind.CreditCoin ? Palette.CreditGold : Palette.Accent);

            if (xpVisual != null) xpVisual.SetActive(kind == PickupKind.XpShard);
            if (medkitVisual != null) medkitVisual.SetActive(kind == PickupKind.Medkit);
            if (creditVisual != null) creditVisual.SetActive(kind == PickupKind.CreditCoin);

            MeshRenderer activeMr = kind == PickupKind.Medkit
                ? (medkitRenderer != null ? medkitRenderer : visualRenderer)
                : (kind == PickupKind.CreditCoin ? (creditRenderer != null ? creditRenderer : visualRenderer) : (xpRenderer != null ? xpRenderer : visualRenderer));

            if (activeMr != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                activeMr.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", col);
                mpb.SetColor("_EmissionColor", col * 2.4f);
                activeMr.SetPropertyBlock(mpb);
            }
        }

        private void Update()
        {
            if (collected) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            spinAngle += 120f * dt;
            if (visualTransform != null)
            {
                float bob = Mathf.Sin(Time.time * 4f + spinAngle * 0.02f) * 0.12f;
                visualTransform.localPosition = baseLocalVisualPos + new Vector3(0f, bob, 0f);
                visualTransform.localRotation = Quaternion.Euler(kind == PickupKind.CreditCoin ? 75f : 24f, spinAngle, kind == PickupKind.CreditCoin ? 0f : 28f);
            }

            PlayerController player = PlayerController.Instance;
            if (player != null && !player.IsDead)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                float pullRadius = 3.6f + player.PickupMagnetBonus;

                if (dist < pullRadius && dist > 0.01f)
                {
                    float pullSpeed = Mathf.Lerp(15f, 5f, dist / pullRadius);
                    transform.position += toPlayer.normalized * (pullSpeed * dt);
                }

                if (dist <= 0.95f)
                {
                    Collect(player);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (collected) return;
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && !player.IsDead)
            {
                Collect(player);
            }
        }

        private void Collect(PlayerController player)
        {
            if (collected) return;
            collected = true;

            if (kind == PickupKind.Medkit)
            {
                player.Heal(healValue);
                FeelManager.Instance?.OnPickupCollected(transform.position, 0, isHealth: true);
            }
            else if (kind == PickupKind.CreditCoin)
            {
                int gained = Mathf.Max(1, Mathf.RoundToInt(creditValue * player.CreditMultiplier));
                GameManager.Instance?.AddRunCredits(gained);
                FeelManager.Instance?.OnCreditCollected(transform.position, gained);
            }
            else
            {
                player.AddXp(xpValue);
                FeelManager.Instance?.OnPickupCollected(transform.position, xpValue, isHealth: false);
            }

            ForceReturnToPool();
        }

        public void ForceReturnToPool()
        {
            if (!gameObject.activeSelf && collected)
            {
                return;
            }
            collected = true;
            onReleasedCallback?.Invoke(this);
            if (ownerPool != null)
            {
                ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
