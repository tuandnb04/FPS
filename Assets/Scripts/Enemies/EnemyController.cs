using System;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.Enemies
{
    public enum EnemyArchetype
    {
        Chaser,
        Heavy,
        Boss
    }

    /// <summary>
    /// Pooled compound cyber-sentinel enemy and Dreadnought MK-IV Boss controller
    /// with archetype mesh switching, radial plasma barrages, and telegraphed hazard rings.
    /// </summary>
    public sealed class EnemyController : MonoBehaviour
    {
        [SerializeField] private CharacterController controller;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private MeshRenderer[] bodyRenderers;
        [SerializeField] private GameObject bipedalVisualRoot;
        [SerializeField] private GameObject chaserVisualRoot;
        [SerializeField] private GameObject heavyVisualRoot;
        [SerializeField] private GameObject bossVisualRoot;
        [SerializeField] private Transform bossTurretPivot;

        private ObjectPool<EnemyController> ownerPool;
        private EnemyArchetype archetype;
        private float maxHp;
        private float currentHp;
        private float moveSpeed;
        private float contactDamage;
        private int scoreValue;
        private float hitFlashTimer;
        private float contactCooldown;
        private float bossBarrageTimer;
        private float bossHazardTimer;
        private int bossBarrageStep;
        private float prevYaw;
        private bool isDead;

        private MaterialPropertyBlock mpb;
        private Action<EnemyController, bool> onFinishedCallback;

        public bool IsDead => isDead;
        public EnemyArchetype Archetype => archetype;
        public float CurrentHp => currentHp;
        public float MaxHp => maxHp;

        public void BindComponents(
            CharacterController cc,
            ProceduralCharacterMotion motion,
            MeshRenderer[] renderers,
            GameObject bipedalGroup,
            GameObject chaserGroup,
            GameObject heavyGroup,
            GameObject bossGroup,
            Transform turretPivot)
        {
            controller = cc;
            characterMotion = motion;
            bodyRenderers = renderers;
            bipedalVisualRoot = bipedalGroup;
            chaserVisualRoot = chaserGroup;
            heavyVisualRoot = heavyGroup;
            bossVisualRoot = bossGroup;
            bossTurretPivot = turretPivot;
        }

        public void Initialize(
            ObjectPool<EnemyController> pool,
            EnemyArchetype kind,
            Vector3 spawnPos,
            int waveNumber,
            Action<EnemyController, bool> onFinished)
        {
            ownerPool = pool;
            archetype = kind;
            onFinishedCallback = onFinished;
            isDead = false;
            hitFlashTimer = 0f;
            contactCooldown = 0.25f;
            bossBarrageTimer = 2.0f;
            bossHazardTimer = 3.6f;
            bossBarrageStep = 0;

            float waveScale = 1f + (waveNumber - 1) * 0.16f;
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(kind != EnemyArchetype.Boss);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(kind == EnemyArchetype.Heavy);
            if (bossVisualRoot != null) bossVisualRoot.SetActive(kind == EnemyArchetype.Boss);

            if (kind == EnemyArchetype.Boss)
            {
                maxHp = 650f * waveScale;
                moveSpeed = 2.45f + Mathf.Min(0.8f, waveNumber * 0.06f);
                contactDamage = 28f;
                scoreValue = 1500;
                transform.localScale = Vector3.one * 1.15f;
                UIManager.Instance?.UpdateBossHp(maxHp, maxHp, "ARMORED IFV // IRONCLAD");
            }
            else if (kind == EnemyArchetype.Heavy)
            {
                maxHp = 115f * waveScale;
                moveSpeed = 3.15f + Mathf.Min(1.2f, waveNumber * 0.12f);
                contactDamage = 20f;
                scoreValue = 250;
                transform.localScale = Vector3.one * 1.22f;
            }
            else
            {
                maxHp = 48f * waveScale;
                moveSpeed = 4.35f + Mathf.Min(1.6f, waveNumber * 0.18f);
                contactDamage = 12f;
                scoreValue = 100;
                transform.localScale = Vector3.one;
            }

            currentHp = maxHp;

            if (controller != null)
            {
                controller.enabled = false;
                transform.position = new Vector3(spawnPos.x, 0.02f, spawnPos.z);
                controller.enabled = true;
            }
            else
            {
                transform.position = new Vector3(spawnPos.x, 0.02f, spawnPos.z);
            }

            prevYaw = transform.eulerAngles.y;
            ApplyHitFlash(0f);
        }

        private void Update()
        {
            if (isDead) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (hitFlashTimer > 0f)
            {
                hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(hitFlashTimer / 0.10f));
            }

            if (contactCooldown > 0f)
            {
                contactCooldown -= dt;
            }

            PlayerController player = PlayerController.Instance;
            Vector3 velocity = Vector3.zero;

            if (player != null && !player.IsDead)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;

                if (dist > 0.05f)
                {
                    Vector3 dir = toPlayer / dist;
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-8f * dt));

                    float minStopDist = archetype == EnemyArchetype.Boss ? 4.2f : 0.85f;
                    if (dist > minStopDist)
                    {
                        velocity = dir * moveSpeed;
                        if (controller != null && controller.enabled)
                        {
                            controller.Move((velocity + Vector3.down * 4f) * dt);
                        }
                        else
                        {
                            transform.position += velocity * dt;
                        }
                    }

                    float hitDist = archetype == EnemyArchetype.Boss ? 1.85f : 1.25f;
                    if (dist <= hitDist && contactCooldown <= 0f)
                    {
                        contactCooldown = 0.85f;
                        player.TakeDamage(contactDamage);
                    }
                }

                if (archetype == EnemyArchetype.Boss)
                {
                    StepBossAttacks(player, dt);
                }
            }

            float currentYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            prevYaw = currentYaw;

            if (archetype != EnemyArchetype.Boss)
            {
                characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: false);
            }
        }

        private void StepBossAttacks(PlayerController player, float dt)
        {
            if (bossTurretPivot != null)
            {
                Vector3 aimDir = player.transform.position - bossTurretPivot.position;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.01f)
                {
                    Quaternion turretRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
                    bossTurretPivot.rotation = Quaternion.Slerp(bossTurretPivot.rotation, turretRot, 1f - Mathf.Exp(-6f * dt));
                }
            }

            bossBarrageTimer -= dt;
            if (bossBarrageTimer <= 0f)
            {
                bossBarrageTimer = 3.2f;
                bossBarrageStep++;
                float baseAngle = (bossBarrageStep % 2 == 0) ? 0f : 22.5f;
                Vector3 origin = transform.position + Vector3.up * 1.1f;

                if (player.Weapon != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float angleDeg = baseAngle + i * 45f;
                        Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
                        player.Weapon.SpawnEnemyProjectile(origin + dir * 1.2f, dir, 12.5f, 16f, Palette.BossPurple);
                    }
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.BossPurple);
                AudioManager.Instance?.PlayShoot();
            }

            bossHazardTimer -= dt;
            if (bossHazardTimer <= 0f)
            {
                bossHazardTimer = 4.8f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(player.transform.position, 2.35f, 22f);
            }
        }

        public void ApplyDamage(float dmg, Vector3 hitPoint, Vector3 shotDir, bool isCrit = false)
        {
            if (isDead) return;

            currentHp = Mathf.Max(0f, currentHp - dmg);
            hitFlashTimer = 0.10f;
            ApplyHitFlash(1f);

            if (archetype != EnemyArchetype.Boss)
            {
                characterMotion?.TriggerSquash(new Vector3(1.22f, 0.80f, 1.22f), 0.12f);
            }

            string dmgLabel = isCrit ? "-" + Mathf.RoundToInt(dmg) + "!" : "-" + Mathf.RoundToInt(dmg);
            Color txtCol = isCrit ? Palette.CreditGold : Palette.EnemySecondary;
            FeelManager.Instance?.SpawnFloatingText(hitPoint, dmgLabel, txtCol);

            if (controller != null && controller.enabled && archetype != EnemyArchetype.Boss)
            {
                Vector3 knock = new Vector3(shotDir.x, 0f, shotDir.z).normalized * (archetype == EnemyArchetype.Heavy ? 0.12f : 0.24f);
                controller.Move(knock);
            }

            if (archetype == EnemyArchetype.Boss)
            {
                UIManager.Instance?.UpdateBossHp(currentHp, maxHp, "ARMORED IFV // IRONCLAD");
            }

            if (currentHp <= 0f)
            {
                Die();
            }
        }

        private void Die()
        {
            if (isDead) return;
            isDead = true;

            Vector3 deathPos = transform.position + Vector3.up * 0.9f;
            Color vfxCol = archetype == EnemyArchetype.Boss ? Palette.BossPurple : Palette.EnemySecondary;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, deathPos, vfxCol);
            if (archetype == EnemyArchetype.Boss)
            {
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, deathPos, Palette.CreditGold);
                UIManager.Instance?.HideBossHp();
            }

            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(archetype == EnemyArchetype.Boss ? 0.55f : (archetype == EnemyArchetype.Heavy ? 0.36f : 0.20f));
            if (archetype != EnemyArchetype.Chaser)
            {
                FeelManager.Instance?.TriggerHitStop(0.055f, 0.05f);
            }

            PlayerController.Instance?.OnEnemyKilled();
            GameManager.Instance?.AddScoreAndKill(scoreValue);
            onFinishedCallback?.Invoke(this, true);
            ownerPool?.Release(this);
        }

        public void ForceReturnToPool()
        {
            if (!gameObject.activeSelf && isDead) return;
            isDead = true;
            if (archetype == EnemyArchetype.Boss)
            {
                UIManager.Instance?.HideBossHp();
            }
            onFinishedCallback?.Invoke(this, false);
            if (ownerPool != null)
            {
                ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void ApplyHitFlash(float intensity)
        {
            if (bodyRenderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            Color flashEmission = Palette.Lerp(Palette.Shadow, Palette.CharacterPrimary, intensity) * (intensity * 1.8f);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                bodyRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", flashEmission);
                bodyRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
