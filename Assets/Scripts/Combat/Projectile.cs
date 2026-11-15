using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Core;
using TopDownShooter.Enemies;
using TopDownShooter.Feel;

namespace TopDownShooter.Combat
{
    /// <summary>
    /// Pooled neon pulse projectile with swept raycast collision detection so fast rounds
    /// never tunnel through thin cover obstacles or agile enemies.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private ObjectPool<Projectile> ownerPool;
        private float speed;
        private float damage;
        private float remainingLifetime;
        private bool released;

        public void Initialize(
            ObjectPool<Projectile> pool,
            Vector3 position,
            Quaternion rotation,
            float projectileSpeed,
            float projectileDamage,
            float lifetime = 1.6f)
        {
            ownerPool = pool;
            transform.SetPositionAndRotation(position, rotation);
            speed = projectileSpeed;
            damage = projectileDamage;
            remainingLifetime = lifetime;
            released = false;
        }

        private void Update()
        {
            if (released) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            remainingLifetime -= dt;
            if (remainingLifetime <= 0f)
            {
                Release();
                return;
            }

            Vector3 step = transform.forward * (speed * dt);
            float dist = step.magnitude;
            if (dist > 0.0001f && Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, dist + 0.25f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.CompareTag("Player"))
                {
                    EnemyController enemy = hit.collider.GetComponentInParent<EnemyController>();
                    if (enemy != null)
                    {
                        enemy.ApplyDamage(damage, hit.point, transform.forward);
                        VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.CyanGlow);
                        AudioManager.Instance?.PlayHit();
                        Release();
                        return;
                    }

                    // Hit wall or tactical cover
                    VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.Accent);
                    Release();
                    return;
                }
            }

            transform.position += step;
        }

        public void ForceReturnToPool()
        {
            Release();
        }

        private void Release()
        {
            if (released) return;
            released = true;
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
