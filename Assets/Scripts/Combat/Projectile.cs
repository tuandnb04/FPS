using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Enemies;
using TopDownShooter.Feel;

namespace TopDownShooter.Combat
{
    /// <summary>
    /// Pooled projectile supporting friendly ballistic/piercing/explosive rounds
    /// and hostile boss plasma orbs with swept raycast hit detection.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private ObjectPool<Projectile> ownerPool;
        private float speed;
        private float damage;
        private float remainingLifetime;
        private int remainingPierces;
        private float explosionRadius;
        private bool isEnemyProjectile;
        private bool isCrit;
        private Color projectileTint;
        private bool released;

        private MeshRenderer visualRenderer;
        private Transform visualTransform;
        private MaterialPropertyBlock mpb;
        private readonly HashSet<EnemyController> hitEnemies = new HashSet<EnemyController>();
        private static readonly HashSet<EnemyController> ExplosionBuffer = new HashSet<EnemyController>();

        public void Initialize(
            ObjectPool<Projectile> pool,
            Vector3 position,
            Quaternion rotation,
            float projectileSpeed,
            float projectileDamage,
            float lifetime = 1.6f,
            int pierceCount = 0,
            float aoeRadius = 0f,
            bool enemyProjectile = false,
            Color? tintColor = null,
            Vector3? visualScale = null,
            bool criticalHit = false)
        {
            ownerPool = pool;
            transform.SetPositionAndRotation(position, rotation);
            speed = projectileSpeed;
            damage = projectileDamage;
            remainingLifetime = lifetime;
            remainingPierces = Mathf.Max(0, pierceCount);
            explosionRadius = Mathf.Max(0f, aoeRadius);
            isEnemyProjectile = enemyProjectile;
            isCrit = criticalHit;
            projectileTint = tintColor ?? (enemyProjectile ? Palette.BossPurple : Palette.Accent);
            released = false;
            hitEnemies.Clear();

            CacheVisuals();
            if (visualTransform != null)
            {
                visualTransform.localScale = visualScale ?? new Vector3(0.13f, 0.28f, 0.13f);
            }
            ApplyTint(projectileTint);
        }

        private void CacheVisuals()
        {
            if (visualRenderer == null && transform.childCount > 0)
            {
                visualTransform = transform.GetChild(0);
                visualRenderer = visualTransform.GetComponent<MeshRenderer>();
            }
        }

        private void ApplyTint(Color tint)
        {
            if (visualRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_EmissionColor", tint * 1.45f);
            visualRenderer.SetPropertyBlock(mpb);
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
            if (dist > 0.0001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(transform.position, transform.forward, dist + 0.28f, ~0, QueryTriggerInteraction.Ignore);
                if (hits != null && hits.Length > 0)
                {
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    for (int i = 0; i < hits.Length; i++)
                    {
                        RaycastHit hit = hits[i];
                        if (hit.collider == null) continue;

                        if (isEnemyProjectile)
                        {
                            if (hit.collider.GetComponentInParent<EnemyController>() != null) continue;

                            PlayerController player = hit.collider.GetComponentInParent<PlayerController>();
                            if (player != null)
                            {
                                player.TakeDamage(damage);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, projectileTint);
                                Release();
                                return;
                            }

                            VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, projectileTint);
                            Release();
                            return;
                        }
                        else
                        {
                            if (hit.collider.CompareTag("Player")) continue;

                            EnemyController enemy = hit.collider.GetComponentInParent<EnemyController>();
                            if (enemy != null)
                            {
                                if (!hitEnemies.Add(enemy)) continue;

                                if (explosionRadius > 0.1f)
                                {
                                    DetonateAoE(hit.point);
                                    Release();
                                    return;
                                }

                                enemy.ApplyDamage(damage, hit.point, transform.forward, isCrit);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, projectileTint);
                                AudioManager.Instance?.PlayHit();

                                if (remainingPierces > 0)
                                {
                                    remainingPierces--;
                                    continue;
                                }

                                Release();
                                return;
                            }

                            // Hit solid wall or tactical cover
                            if (explosionRadius > 0.1f)
                            {
                                DetonateAoE(hit.point);
                            }
                            else
                            {
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.Accent);
                            }
                            Release();
                            return;
                        }
                    }
                }
            }

            transform.position += step;
        }

        private void DetonateAoE(Vector3 center)
        {
            ExplosionBuffer.Clear();
            Collider[] overlap = Physics.OverlapSphere(center, explosionRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlap.Length; i++)
            {
                if (overlap[i] == null) continue;
                EnemyController enemy = overlap[i].GetComponentInParent<EnemyController>();
                if (enemy != null && !enemy.IsDead)
                {
                    ExplosionBuffer.Add(enemy);
                }
            }

            foreach (EnemyController enemy in ExplosionBuffer)
            {
                Vector3 dir = enemy.transform.position - center;
                dir.y = 0f;
                enemy.ApplyDamage(damage, enemy.transform.position + Vector3.up * 0.8f, dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward, isCrit);
            }

            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center, Palette.RocketOrange);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center, Palette.Accent);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(0.28f);
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
