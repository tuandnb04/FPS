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
        Medkit
    }

    /// <summary>
    /// Pooled XP shard or Medkit collectible. Features idle spin, magnetic pull within radius,
    /// and trigger collection synced with FeelManager + Player progression.
    /// </summary>
    public sealed class PickupItem : MonoBehaviour
    {
        private ObjectPool<PickupItem> ownerPool;
        private PickupKind kind;
        private int xpValue;
        private float healValue;
        private float spinAngle;
        private Vector3 baseLocalVisualPos;
        private Transform visualTransform;
        private MeshRenderer visualRenderer;
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

        public void Initialize(
            ObjectPool<PickupItem> pool,
            Vector3 worldPos,
            PickupKind pickupKind,
            int xpAmount,
            float healAmount,
            Action<PickupItem> onReleased)
        {
            ownerPool = pool;
            kind = pickupKind;
            xpValue = xpAmount;
            healValue = healAmount;
            onReleasedCallback = onReleased;
            collected = false;

            transform.position = new Vector3(worldPos.x, 0.55f, worldPos.z);
            spinAngle = UnityEngine.Random.Range(0f, 360f);

            if (visualRenderer != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                Color col = kind == PickupKind.Medkit ? Palette.HealthGreen : Palette.Accent;
                visualRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", col);
                mpb.SetColor("_EmissionColor", col * 2.4f);
                visualRenderer.SetPropertyBlock(mpb);
            }
        }

        private void Update()
        {
            if (collected) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            spinAngle += 110f * dt;
            if (visualTransform != null)
            {
                float bob = Mathf.Sin(Time.time * 4f + spinAngle * 0.02f) * 0.12f;
                visualTransform.localPosition = baseLocalVisualPos + new Vector3(0f, bob, 0f);
                visualTransform.localRotation = Quaternion.Euler(28f, spinAngle, 35f);
            }

            PlayerController player = PlayerController.Instance;
            if (player != null && !player.IsDead)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;

                if (dist < 3.4f && dist > 0.01f)
                {
                    float pullSpeed = Mathf.Lerp(14f, 4.5f, dist / 3.4f);
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
