using System.Collections.Generic;
using Audio;
using Character;
using Core;
using Enemies;
using Feel;
using UnityEngine;
using UnityEngine.Pool;

namespace Combat
{
    /// <summary>
    /// Pooled projectile supporting friendly ballistic/piercing/explosive rounds
    /// and hostile boss plasma orbs with swept raycast hit detection.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private ObjectPool<Projectile> _ownerPool;
        private float _speed;
        private float _damage;
        private float _remainingLifetime;
        private int _remainingPierces;
        private float _explosionRadius;
        private bool _isEnemyProjectile;
        private bool _isCrit;
        private Color _projectileTint;
        private bool _released;

        private MeshRenderer _visualRenderer;
        private Transform _visualTransform;
        private MaterialPropertyBlock _mpb;
        private readonly HashSet<EnemyController> _hitEnemies = new();
        private static readonly HashSet<EnemyController> ExplosionBuffer = new();
        private static readonly RaycastHit[] RayHitBuffer = new RaycastHit[16];
        private static readonly Collider[] OverlapBuffer = new Collider[32];
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

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
            _ownerPool = pool;
            transform.SetPositionAndRotation(position, rotation);
            _speed = projectileSpeed;
            _damage = projectileDamage;
            _remainingLifetime = lifetime;
            _remainingPierces = Mathf.Max(0, pierceCount);
            _explosionRadius = Mathf.Max(0f, aoeRadius);
            _isEnemyProjectile = enemyProjectile;
            _isCrit = criticalHit;
            _projectileTint = tintColor ?? (enemyProjectile ? Palette.BossPurple : Palette.Accent);
            _released = false;
            _hitEnemies.Clear();

            CacheVisuals();
            if (_visualTransform != null)
            {
                _visualTransform.localScale = visualScale ?? new Vector3(0.13f, 0.28f, 0.13f);
            }
            ApplyTint(_projectileTint);
        }

        private void CacheVisuals()
        {
            if (_visualRenderer != null || transform.childCount <= 0) return;
            _visualTransform = transform.GetChild(0);
            _visualRenderer = _visualTransform.GetComponent<MeshRenderer>();
        }

        private void ApplyTint(Color tint)
        {
            if (_visualRenderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _visualRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, tint);
            _mpb.SetColor(EmissionColor, tint * 1.45f);
            _visualRenderer.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            if (_released) return;

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            _remainingLifetime -= dt;
            if (_remainingLifetime <= 0f)
            {
                Release();
                return;
            }

            var step = transform.forward * (_speed * dt);
            var dist = step.magnitude;
            if (dist > 0.0001f)
            {
                var hitCount = Physics.RaycastNonAlloc(transform.position, transform.forward, RayHitBuffer, dist + 0.28f, ~0, QueryTriggerInteraction.Ignore);
                if (hitCount > 0)
                {
                    // Insertion sort in-place: zero allocations, optimal for small hit counts
                    for (var i = 1; i < hitCount; i++)
                    {
                        var key = RayHitBuffer[i];
                        var j = i - 1;
                        while (j >= 0 && RayHitBuffer[j].distance > key.distance)
                        {
                            RayHitBuffer[j + 1] = RayHitBuffer[j];
                            j--;
                        }
                        RayHitBuffer[j + 1] = key;
                    }

                    for (var i = 0; i < hitCount; i++)
                    {
                        var hit = RayHitBuffer[i];
                        if (hit.collider == null) continue;

                        if (_isEnemyProjectile)
                        {
                            if (hit.collider.GetComponentInParent<EnemyController>() != null) continue;

                            var player = hit.collider.GetComponentInParent<PlayerController>();
                            if (player != null)
                            {
                                player.TakeDamage(_damage);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, _projectileTint);
                                Release();
                                return;
                            }

                            var ally = hit.collider.GetComponentInParent<SquadMemberController>();
                            if (ally != null)
                            {
                                if (ally.IsDowned) continue;
                                ally.TakeDamage(_damage);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, _projectileTint);
                                Release();
                                return;
                            }

                            VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, _projectileTint);
                        }
                        else
                        {
                            if (hit.collider.CompareTag("Player") || hit.collider.GetComponentInParent<SquadMemberController>() != null) continue;

                            var barrel = hit.collider.GetComponentInParent<ExplosiveBarrel>();
                            if (barrel != null && !barrel.IsDetonated)
                            {
                                if (_explosionRadius > 0.1f)
                                {
                                    DetonateAoE(hit.point);
                                }
                                else
                                {
                                    var exploded = barrel.ApplyDamage(_damage);
                                    FeelManager.Instance?.TriggerHitmarker(exploded);
                                }
                                Release();
                                return;
                            }

                            var enemy = hit.collider.GetComponentInParent<EnemyController>();
                            if (enemy != null)
                            {
                                if (!_hitEnemies.Add(enemy)) continue;

                                if (_explosionRadius > 0.1f)
                                {
                                    DetonateAoE(hit.point);
                                    Release();
                                    return;
                                }

                                var wasDead = enemy.IsDead;
                                enemy.ApplyDamage(_damage, hit.point, transform.forward, _isCrit);
                                var killed = !wasDead && enemy.IsDead;
                                FeelManager.Instance?.TriggerHitmarker(killed);
                                if (killed)
                                {
                                    FeelManager.Instance?.TriggerKillFeed("VANCE", enemy.DisplayCallsign, "5.56");
                                }
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, _projectileTint);
                                AudioManager.Instance?.PlayHit();

                                if (_remainingPierces > 0)
                                {
                                    _remainingPierces--;
                                    continue;
                                }

                                Release();
                                return;
                            }

                            // Hit solid wall or tactical cover
                            if (_explosionRadius > 0.1f)
                            {
                                DetonateAoE(hit.point);
                            }
                            else
                            {
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.Accent);
                            }
                        }

                        Release();
                        return;
                    }
                }
            }

            transform.position += step;
        }

        private void DetonateAoE(Vector3 center)
        {
            ExplosionBuffer.Clear();
            var overlapCount = Physics.OverlapSphereNonAlloc(center, _explosionRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            var anyHit = false;
            var anyKill = false;
            for (var i = 0; i < overlapCount; i++)
            {
                var t = OverlapBuffer[i];
                if (t == null) continue;

                var barrel = t.GetComponentInParent<ExplosiveBarrel>();
                if (barrel != null && !barrel.IsDetonated)
                {
                    if (barrel.ApplyDamage(_damage)) anyKill = true;
                    anyHit = true;
                    continue;
                }

                var enemy = t.GetComponentInParent<EnemyController>();
                if (enemy != null && !enemy.IsDead)
                {
                    ExplosionBuffer.Add(enemy);
                }
            }

            foreach (var enemy in ExplosionBuffer)
            {
                anyHit = true;
                var wasDead = enemy.IsDead;
                var dir = enemy.transform.position - center;
                dir.y = 0f;
                enemy.ApplyDamage(_damage, enemy.transform.position + Vector3.up * 0.8f, dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward, _isCrit);
                if (!wasDead && enemy.IsDead)
                {
                    anyKill = true;
                    FeelManager.Instance?.TriggerKillFeed("VANCE", enemy.DisplayCallsign, "HE");
                }
            }

            if (anyHit)
            {
                FeelManager.Instance?.TriggerHitmarker(anyKill);
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
            if (_released) return;
            _released = true;
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
