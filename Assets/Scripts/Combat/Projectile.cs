using System.Collections.Generic;
using Audio;
using Character;
using Core;
using Enemies;
using Feel;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace Combat
{
    /// <summary>
    /// Pooled aerodynamic projectile with shared low-poly spitzer mesh, GPU instancing compatibility,
    /// O(1) collider target cache, and swept raycast hit detection.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private enum TargetKind : byte
        {
            Environment = 0,
            Enemy = 1,
            Player = 2,
            SquadAlly = 3,
            Barrel = 4
        }

        private readonly struct CachedColliderTarget
        {
            public readonly TargetKind Kind;
            public readonly EnemyController Enemy;
            public readonly PlayerController Player;
            public readonly SquadMemberController Ally;
            public readonly ExplosiveBarrel Barrel;

            public CachedColliderTarget(EnemyController enemy)
            {
                Kind = TargetKind.Enemy;
                Enemy = enemy;
                Player = null;
                Ally = null;
                Barrel = null;
            }

            public CachedColliderTarget(PlayerController player)
            {
                Kind = TargetKind.Player;
                Enemy = null;
                Player = player;
                Ally = null;
                Barrel = null;
            }

            public CachedColliderTarget(SquadMemberController ally)
            {
                Kind = TargetKind.SquadAlly;
                Enemy = null;
                Player = null;
                Ally = ally;
                Barrel = null;
            }

            public CachedColliderTarget(ExplosiveBarrel barrel)
            {
                Kind = TargetKind.Barrel;
                Enemy = null;
                Player = null;
                Ally = null;
                Barrel = barrel;
            }

            public CachedColliderTarget(TargetKind kind)
            {
                Kind = kind;
                Enemy = null;
                Player = null;
                Ally = null;
                Barrel = null;
            }
        }

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
        private float _gravityArc;
        private float _verticalVelocity;
        private bool _hasSmokeTrail;
        private float _smokeEmitTimer;
        private float _acceleration;
        private float _maxSpeed;
        private int _remainingRicochets;
        private float _proximityAirburstRadius;
        private Vector3 _flatForward = Vector3.forward;

        private MeshRenderer _visualRenderer;
        private Transform _visualTransform;
        private float _visualHalfExtentY = 1f;
        private MaterialPropertyBlock _mpb;
        private bool _hasAppliedVisualState;
        private Vector3 _lastAppliedScale;
        private Color _lastAppliedTint;
        private readonly HashSet<EnemyController> _hitEnemies = new();

        private static Mesh _sharedBulletMesh;
        private static bool _sceneHookRegistered;
        private static readonly Dictionary<EntityId, CachedColliderTarget> ColliderCache = new(256);
        private static readonly HashSet<EnemyController> ExplosionBuffer = new();
        private static readonly RaycastHit[] RayHitBuffer = new RaycastHit[16];
        private static readonly Collider[] OverlapBuffer = new Collider[32];
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color ConcreteDustColor = new(0.71f, 0.65f, 0.57f, 1f);

        /// <summary>
        /// Builds and caches a lightweight 26-vertex aerodynamic spitzer bullet mesh (bounds extents.y == 1.0)
        /// oriented along local +Y so instances share one GPU buffer without per-bullet primitive colliders.
        /// </summary>
        public static Mesh GetOrCreateSharedBulletMesh()
        {
            if (_sharedBulletMesh != null) return _sharedBulletMesh;

            const int segs = 8;
            // 3 rings (tail -1.0, shoulder -0.68, ogive +0.24) + 1 nose tip (+1.0) + 1 tail center (-1.0) = 26 verts, 44 tris
            const float yTail = -1.0f;
            const float yShoulder = -0.68f;
            const float yOgive = 0.24f;
            const float yTip = 1.0f;

            const float rTail = 0.30f;
            const float rBody = 0.50f;
            const float rOgive = 0.44f;

            var verts = new Vector3[segs * 3 + 2];
            for (var i = 0; i < segs; i++)
            {
                var a = i * (Mathf.PI * 2f / segs);
                var cos = Mathf.Cos(a);
                var sin = Mathf.Sin(a);
                verts[i] = new Vector3(cos * rTail, yTail, sin * rTail);
                verts[segs + i] = new Vector3(cos * rBody, yShoulder, sin * rBody);
                verts[segs * 2 + i] = new Vector3(cos * rOgive, yOgive, sin * rOgive);
            }
            var tipIdx = segs * 3;
            var botCenterIdx = tipIdx + 1;
            verts[tipIdx] = new Vector3(0f, yTip, 0f);
            verts[botCenterIdx] = new Vector3(0f, yTail, 0f);

            var tris = new int[segs * 18];
            var ti = 0;
            for (var i = 0; i < segs; i++)
            {
                var next = (i + 1) % segs;

                // Tail end-cap
                tris[ti++] = botCenterIdx;
                tris[ti++] = i;
                tris[ti++] = next;

                // Boat-tail bevel ring -> Shoulder
                var s0 = segs + i;
                var s1 = segs + next;
                tris[ti++] = i;
                tris[ti++] = s0;
                tris[ti++] = next;
                tris[ti++] = next;
                tris[ti++] = s0;
                tris[ti++] = s1;

                // Shoulder -> Ogive band
                var o0 = segs * 2 + i;
                var o1 = segs * 2 + next;
                tris[ti++] = s0;
                tris[ti++] = o0;
                tris[ti++] = s1;
                tris[ti++] = s1;
                tris[ti++] = o0;
                tris[ti++] = o1;

                // Ogive -> Sharp spitzer tip
                tris[ti++] = o0;
                tris[ti++] = tipIdx;
                tris[ti++] = o1;
            }

            var mesh = new Mesh
            {
                name = "SharedSpitzerBulletMesh",
                vertices = verts,
                triangles = tris
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _sharedBulletMesh = mesh;
            return _sharedBulletMesh;
        }

        private static CachedColliderTarget ResolveColliderTarget(Collider col)
        {
            if (!_sceneHookRegistered)
            {
                _sceneHookRegistered = true;
                SceneManager.activeSceneChanged += OnActiveSceneChanged;
            }

            var id = col.GetEntityId();
            if (ColliderCache.TryGetValue(id, out var cached))
            {
                var valid = cached.Kind switch
                {
                    TargetKind.Environment => true,
                    TargetKind.Enemy => cached.Enemy != null,
                    TargetKind.Player => cached.Player != null,
                    TargetKind.SquadAlly => cached.Ally != null,
                    TargetKind.Barrel => cached.Barrel != null,
                    _ => true
                };
                if (valid) return cached;
            }

            if (ColliderCache.Count > 2048) ColliderCache.Clear();

            CachedColliderTarget resolved;
            var enemy = col.GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                resolved = new CachedColliderTarget(enemy);
            }
            else
            {
                var player = col.GetComponentInParent<PlayerController>();
                if (player != null)
                {
                    resolved = new CachedColliderTarget(player);
                }
                else
                {
                    var ally = col.GetComponentInParent<SquadMemberController>();
                    if (ally != null)
                    {
                        resolved = new CachedColliderTarget(ally);
                    }
                    else
                    {
                        var barrel = col.GetComponentInParent<ExplosiveBarrel>();
                        resolved = barrel != null
                            ? new CachedColliderTarget(barrel)
                            : new CachedColliderTarget(TargetKind.Environment);
                    }
                }
            }

            ColliderCache[id] = resolved;
            return resolved;
        }

        private static void OnActiveSceneChanged(Scene _, Scene __)
        {
            ColliderCache.Clear();
        }

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
            bool criticalHit = false,
            float gravityArc = 0f,
            bool hasSmokeTrail = false,
            float acceleration = 0f,
            float maxSpeed = 0f,
            int ricochetCount = 0,
            float proximityAirburstRadius = 0f)
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
            _gravityArc = Mathf.Max(0f, gravityArc);
            _verticalVelocity = _gravityArc > 0.01f ? _gravityArc * 0.25f : 0f;
            _hasSmokeTrail = hasSmokeTrail;
            _smokeEmitTimer = 0.012f;
            _acceleration = Mathf.Max(0f, acceleration);
            _maxSpeed = maxSpeed > _speed ? maxSpeed : _speed;
            _remainingRicochets = Mathf.Max(0, ricochetCount);
            _proximityAirburstRadius = Mathf.Max(0f, proximityAirburstRadius);
            var fwd = rotation * Vector3.forward;
            fwd.y = 0f;
            _flatForward = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            _hitEnemies.Clear();

            CacheVisuals();
            var scale = visualScale ?? new Vector3(0.06f, 0.28f, 0.06f);
            if (_visualTransform != null && (!_hasAppliedVisualState || _lastAppliedScale != scale))
            {
                _visualTransform.localScale = scale;
                // Offset the tracer half-length forward along local +Z so its tail exits the barrel tip cleanly instead of poking back into the handguard.
                _visualTransform.localPosition = new Vector3(0f, 0f, scale.y * _visualHalfExtentY + 0.03f);
                _lastAppliedScale = scale;
            }
            if (!_hasAppliedVisualState || _lastAppliedTint != _projectileTint)
            {
                ApplyTint(_projectileTint);
                _lastAppliedTint = _projectileTint;
            }
            _hasAppliedVisualState = true;
        }

        private void CacheVisuals()
        {
            if (_visualRenderer != null || transform.childCount <= 0) return;
            _visualTransform = transform.GetChild(0);
            _visualRenderer = _visualTransform.GetComponent<MeshRenderer>();
            var meshFilter = _visualTransform.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                _visualHalfExtentY = Mathf.Max(0.1f, meshFilter.sharedMesh.bounds.extents.y);
            }
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
                if (_gravityArc > 0.01f && _explosionRadius > 0.1f)
                {
                    DetonateAoE(new Vector3(transform.position.x, 0.08f, transform.position.z));
                }
                Release();
                return;
            }

            if (_hasSmokeTrail)
            {
                _smokeEmitTimer -= dt;
                if (_smokeEmitTimer <= 0f)
                {
                    _smokeEmitTimer = 0.028f;
                    VFXManager.Instance?.Play(VfxKind.RocketSmoke, transform.position - transform.forward * 0.22f, new Color(0.76f, 0.78f, 0.82f, 0.82f));
                }
            }

            if (_acceleration > 0.01f && _speed < _maxSpeed)
            {
                _speed = Mathf.Min(_maxSpeed, _speed + _acceleration * dt);
            }

            Vector3 step;
            if (_gravityArc > 0.01f)
            {
                _verticalVelocity -= _gravityArc * dt;
                step = _flatForward * (_speed * dt) + Vector3.up * (_verticalVelocity * dt);
            }
            else
            {
                step = transform.forward * (_speed * dt);
            }

            var dist = step.magnitude;
            if (dist > 0.0001f)
            {
                var rayDir = step / dist;
                var hitCount = Physics.RaycastNonAlloc(transform.position, rayDir, RayHitBuffer, dist + 0.28f, GetCombatHitLayerMask(), QueryTriggerInteraction.Ignore);
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
                        var target = ResolveColliderTarget(hit.collider);

                        if (_isEnemyProjectile)
                        {
                            if (target.Kind == TargetKind.Enemy) continue;

                            if (target.Kind == TargetKind.Player && target.Player != null)
                            {
                                target.Player.TakeDamage(_damage, hit.point - transform.forward * 6f);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.DamageBloodRed);
                                Release();
                                return;
                            }

                            if (target.Kind == TargetKind.SquadAlly && target.Ally != null)
                            {
                                if (target.Ally.IsDowned) continue;
                                target.Ally.TakeDamage(_damage);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.DamageBloodRed);
                                Release();
                                return;
                            }

                            VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, ConcreteDustColor);
                        }
                        else
                        {
                            if (target.Kind is TargetKind.Player or TargetKind.SquadAlly) continue;

                            if (target.Kind == TargetKind.Barrel && target.Barrel != null && !target.Barrel.IsDetonated)
                            {
                                if (_explosionRadius > 0.1f)
                                {
                                    DetonateAoE(hit.point);
                                }
                                else
                                {
                                    var exploded = target.Barrel.ApplyDamage(_damage);
                                    FeelManager.Instance?.TriggerHitmarker(exploded);
                                }
                                Release();
                                return;
                            }

                            if (target.Kind == TargetKind.Enemy && target.Enemy != null)
                            {
                                var enemy = target.Enemy;
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
                                var impactColor = enemy.Archetype == EnemyArchetype.Chaser
                                    ? Palette.DamageBloodRed
                                    : Palette.Accent;
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, impactColor);
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
                            if (!_isEnemyProjectile && _remainingRicochets > 0)
                            {
                                _remainingRicochets--;
                                var refl = Vector3.Reflect(rayDir, hit.normal);
                                refl.y = 0f;
                                if (refl.sqrMagnitude < 0.001f) refl = -rayDir;
                                refl.Normalize();
                                _flatForward = refl;
                                transform.position = hit.point + refl * 0.08f;
                                transform.rotation = Quaternion.LookRotation(refl, Vector3.up);
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, Palette.RailgunCyan);
                                AudioManager.Instance?.PlayHit();
                                return;
                            }

                            if (_explosionRadius > 0.1f)
                            {
                                DetonateAoE(hit.point);
                            }
                            else
                            {
                                VFXManager.Instance?.Play(VfxKind.HitSpark, hit.point, ConcreteDustColor);
                            }
                        }

                        Release();
                        return;
                    }
                }
            }

            transform.position += step;
            if (!_isEnemyProjectile && _proximityAirburstRadius > 0.1f && _explosionRadius > 0.1f)
            {
                var proxCount = Physics.OverlapSphereNonAlloc(transform.position, _proximityAirburstRadius, OverlapBuffer, GetCombatHitLayerMask(), QueryTriggerInteraction.Ignore);
                for (var i = 0; i < proxCount; i++)
                {
                    var col = OverlapBuffer[i];
                    if (col == null) continue;
                    var proxTarget = ResolveColliderTarget(col);
                    if (proxTarget.Kind == TargetKind.Enemy && proxTarget.Enemy != null && !proxTarget.Enemy.IsDead)
                    {
                        DetonateAoE(transform.position);
                        Release();
                        return;
                    }
                }
            }
            if (_gravityArc > 0.01f)
            {
                if (step.sqrMagnitude > 0.00001f)
                {
                    transform.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
                }
                if (transform.position.y <= 0.08f)
                {
                    if (_explosionRadius > 0.1f)
                    {
                        DetonateAoE(new Vector3(transform.position.x, 0.08f, transform.position.z));
                    }
                    Release();
                    return;
                }
            }
        }

        private static int _combatHitLayerMask;

        private static int GetCombatHitLayerMask()
        {
            if (_combatHitLayerMask != 0) return _combatHitLayerMask;
            // Exclude Ignore Raycast (2), Pickup (10), Projectile (12), and WeaponProp (13) at PhysX level
            _combatHitLayerMask = ~((1 << 2) | (1 << 10) | (1 << 12) | (1 << 13));
            return _combatHitLayerMask;
        }

        private void DetonateAoE(Vector3 center)
        {
            ExplosionBuffer.Clear();
            var overlapCount = Physics.OverlapSphereNonAlloc(center, _explosionRadius, OverlapBuffer, GetCombatHitLayerMask(), QueryTriggerInteraction.Ignore);
            var anyHit = false;
            var anyKill = false;
            for (var i = 0; i < overlapCount; i++)
            {
                var t = OverlapBuffer[i];
                if (t == null) continue;

                var target = ResolveColliderTarget(t);
                if (target.Kind == TargetKind.Barrel && target.Barrel != null && !target.Barrel.IsDetonated)
                {
                    if (target.Barrel.ApplyDamage(_damage)) anyKill = true;
                    anyHit = true;
                    continue;
                }

                if (target.Kind == TargetKind.Enemy && target.Enemy != null && !target.Enemy.IsDead)
                {
                    ExplosionBuffer.Add(target.Enemy);
                }
            }

            foreach (var enemy in ExplosionBuffer)
            {
                anyHit = true;
                var wasDead = enemy.IsDead;
                var dir = enemy.transform.position - center;
                dir.y = 0f;
                enemy.ApplyDamage(_damage, enemy.transform.position + Vector3.up * 0.8f, dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward, _isCrit, isExplosive: true);
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
            VFXManager.Instance?.SpawnScorchMark(center, Mathf.Max(1.6f, _explosionRadius * 0.68f));
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
