using System.Collections.Generic;
using System.Linq;
using Audio;
using Character;
using Core;
using Enemies;
using Feel;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Shootable red/amber tactical fuel drum inspired by Modern Warfare compound firefights.
    /// Detonates cleanly via non-alloc OverlapSphere, triggers VFX + camera shake + chained blasts,
    /// and resets cleanly between campaign runs without destroying scene instances.
    /// </summary>
    public sealed class ExplosiveBarrel : MonoBehaviour
    {
        private static readonly List<ExplosiveBarrel> AllBarrels = new(16);
        private static readonly Collider[] OverlapBuffer = new Collider[32];

        [SerializeField] private float maxHp = 45f;
        [SerializeField] private float explosionRadius = 5.5f;
        [SerializeField] private float enemyDamage = 195f;
        [SerializeField] private float friendlyDamage = 14f;

        private float _hp;
        private Collider _col;
        private Renderer[] _renderers;

        public bool IsDetonated { get; private set; }

        private void Awake()
        {
            _hp = maxHp;
            _col = GetComponent<Collider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnEnable()
        {
            if (!AllBarrels.Contains(this))
            {
                AllBarrels.Add(this);
            }
        }

        private void OnDisable()
        {
            AllBarrels.Remove(this);
        }

        public static void ResetAllBarrels()
        {
            foreach (var barrel in AllBarrels.Where(barrel => barrel != null))
            {
                barrel.ResetBarrel();
            }
        }

        private void ResetBarrel()
        {
            _hp = maxHp;
            IsDetonated = false;
            if (_col != null) _col.enabled = true;
            if (_renderers == null) return;
            foreach (var t in _renderers)
            {
                if (t != null) t.enabled = true;
            }
        }

        public bool ApplyDamage(float amount)
        {
            if (IsDetonated || amount <= 0f) return false;
            _hp -= amount;
            VFXManager.Instance?.Play(VfxKind.HitSpark, transform.position + Vector3.up * 0.6f, Palette.C4Amber);
            if (!(_hp <= 0f)) return false;
            Detonate();
            return true;
        }

        private void Detonate()
        {
            if (IsDetonated) return;
            IsDetonated = true;

            if (_col != null) _col.enabled = false;
            if (_renderers != null)
            {
                foreach (var t in _renderers)
                {
                    if (t != null) t.enabled = false;
                }
            }

            var center = transform.position + Vector3.up * 0.6f;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center, Palette.RocketOrange);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center, Palette.C4Amber);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(0.48f);
            FeelManager.Instance?.SpawnFloatingText(center + Vector3.up * 0.5f, "FUEL DETONATION!", Palette.RocketOrange);

            var hitCount = Physics.OverlapSphereNonAlloc(transform.position, explosionRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            var damagedEnemies = new HashSet<EnemyController>();
            for (var i = 0; i < hitCount; i++)
            {
                var col = OverlapBuffer[i];
                if (col == null) continue;

                var otherBarrel = col.GetComponentInParent<ExplosiveBarrel>();
                if (otherBarrel != null && otherBarrel != this && !otherBarrel.IsDetonated)
                {
                    otherBarrel.ApplyDamage(maxHp);
                    continue;
                }

                var enemy = col.GetComponentInParent<EnemyController>();
                if (enemy == null || enemy.IsDead || !damagedEnemies.Add(enemy)) continue;
                var pushDir = enemy.transform.position - transform.position;
                pushDir.y = 0f;
                var normalizedPush = pushDir.sqrMagnitude > 0.01f ? pushDir.normalized : Vector3.forward;
                var wasDead = enemy.IsDead;
                enemy.ApplyDamage(enemyDamage, enemy.transform.position + Vector3.up * 0.9f, normalizedPush, isCrit: true);
                if (!wasDead && enemy.IsDead)
                {
                    FeelManager.Instance?.TriggerKillFeed("VANCE", enemy.DisplayCallsign, "FUEL DRUM");
                }
            }

            var player = PlayerController.Instance;
            if (player == null || player.IsDead || !(friendlyDamage > 0f)) return;
            var d = player.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude <= explosionRadius * 0.72f)
            {
                player.TakeDamage(friendlyDamage);
            }
        }
    }
}
