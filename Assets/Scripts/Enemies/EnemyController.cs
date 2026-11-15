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
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform weaponAnchor;
        [SerializeField] private GameObject ak74WeaponRoot;
        [SerializeField] private Transform ak74LeftGrip;
        [SerializeField] private Transform ak74RightGrip;
        [SerializeField] private Transform ak74MuzzleTip;
        [SerializeField] private GameObject pkmWeaponRoot;
        [SerializeField] private Transform pkmLeftGrip;
        [SerializeField] private Transform pkmRightGrip;
        [SerializeField] private Transform pkmMuzzleTip;
        [SerializeField] private Transform bossMuzzleTip;

        private ObjectPool<EnemyController> ownerPool;
        private EnemyArchetype archetype;
        private float maxHp;
        private float currentHp;
        private float moveSpeed;
        private float contactDamage;
        private float rangedDamage;
        private float projectileSpeed;
        private int scoreValue;
        private int activeWaveNumber = 1;
        private string bossDisplayTitle = "IRONCLAD SCOUT-01";
        private float hitFlashTimer;
        private float contactCooldown;
        private float fireCooldown;
        private int burstRemaining;
        private float burstTimer;
        private float bossBarrageTimer;
        private float bossHazardTimer;
        private float bossCannonTimer;
        private int bossCannonBurst;
        private int bossBarrageStep;
        private float prevYaw;
        private bool isDead;

        private MaterialPropertyBlock mpb;
        private Action<EnemyController, bool> onFinishedCallback;

        public bool IsDead => isDead;
        public EnemyArchetype Archetype => archetype;
        public float CurrentHp => currentHp;
        public float MaxHp => maxHp;
        public int ShotsFiredCount { get; private set; }
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastShotDirection { get; private set; }
        public Transform ActiveWeaponMuzzle => archetype == EnemyArchetype.Boss
            ? bossMuzzleTip
            : (archetype == EnemyArchetype.Heavy ? pkmMuzzleTip : ak74MuzzleTip);

        public void BindComponents(
            CharacterController cc,
            ProceduralCharacterMotion motion,
            MeshRenderer[] renderers,
            GameObject bipedalGroup,
            GameObject chaserGroup,
            GameObject heavyGroup,
            GameObject bossGroup,
            Transform turretPivot,
            Transform leftArm = null,
            Transform rightArm = null,
            Transform wpnAnchor = null,
            GameObject ak74Root = null,
            Transform ak74Left = null,
            Transform ak74Right = null,
            Transform ak74Muzzle = null,
            GameObject pkmRoot = null,
            Transform pkmLeft = null,
            Transform pkmRight = null,
            Transform pkmMuzzle = null,
            Transform bossMuzzle = null)
        {
            controller = cc;
            characterMotion = motion;
            bodyRenderers = renderers;
            bipedalVisualRoot = bipedalGroup;
            chaserVisualRoot = chaserGroup;
            heavyVisualRoot = heavyGroup;
            bossVisualRoot = bossGroup;
            bossTurretPivot = turretPivot;
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            weaponAnchor = wpnAnchor;
            ak74WeaponRoot = ak74Root;
            ak74LeftGrip = ak74Left;
            ak74RightGrip = ak74Right;
            ak74MuzzleTip = ak74Muzzle;
            pkmWeaponRoot = pkmRoot;
            pkmLeftGrip = pkmLeft;
            pkmRightGrip = pkmRight;
            pkmMuzzleTip = pkmMuzzle;
            bossMuzzleTip = bossMuzzle;
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
            activeWaveNumber = Mathf.Max(1, waveNumber);
            onFinishedCallback = onFinished;
            isDead = false;
            hitFlashTimer = 0f;
            contactCooldown = 0.25f;
            fireCooldown = UnityEngine.Random.Range(1.05f, 1.85f);
            burstRemaining = 0;
            burstTimer = 0f;
            bossBarrageTimer = 2.1f;
            bossHazardTimer = 3.6f;
            bossCannonTimer = 1.35f;
            bossCannonBurst = 0;
            bossBarrageStep = 0;
            ShotsFiredCount = 0;

            float waveScale = 1f + (activeWaveNumber - 1) * 0.16f;
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(kind != EnemyArchetype.Boss);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(kind == EnemyArchetype.Heavy);
            if (bossVisualRoot != null) bossVisualRoot.SetActive(kind == EnemyArchetype.Boss);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(kind == EnemyArchetype.Heavy);

            if (kind == EnemyArchetype.Boss)
            {
                maxHp = 650f * waveScale;
                moveSpeed = 2.45f + Mathf.Min(0.8f, activeWaveNumber * 0.06f);
                contactDamage = 28f;
                rangedDamage = 10f;
                projectileSpeed = 14.5f;
                scoreValue = 1500;
                bossDisplayTitle = StoryDirector.GetBossCallsign(activeWaveNumber);
                transform.localScale = Vector3.one * 1.15f;
                UIManager.Instance?.UpdateBossHp(maxHp, maxHp, bossDisplayTitle);
            }
            else if (kind == EnemyArchetype.Heavy)
            {
                maxHp = 115f * waveScale;
                moveSpeed = 3.15f + Mathf.Min(1.2f, activeWaveNumber * 0.12f);
                contactDamage = 20f;
                rangedDamage = 6f + Mathf.Min(3f, activeWaveNumber * 0.35f);
                projectileSpeed = 11.5f + Mathf.Min(1.5f, activeWaveNumber * 0.18f);
                scoreValue = 250;
                transform.localScale = Vector3.one * 1.22f;
                characterMotion?.ResetMotionState();
                if (leftArmPivot != null)
                {
                    leftArmPivot.localPosition = new Vector3(-0.42f, 1.22f, 0.18f);
                    leftArmPivot.localScale = Vector3.one * 1.18f;
                }
                if (rightArmPivot != null)
                {
                    rightArmPivot.localPosition = new Vector3(0.43f, 1.22f, 0.06f);
                    rightArmPivot.localScale = Vector3.one * 1.18f;
                }
                if (weaponAnchor != null)
                {
                    weaponAnchor.localPosition = new Vector3(0.14f, 1.15f, 0.50f);
                }
                characterMotion?.ConfigureWeaponIkPose(pkmLeftGrip, pkmRightGrip, 0.34f * 1.18f, 0.35f * 1.18f);
            }
            else
            {
                maxHp = 48f * waveScale;
                moveSpeed = 4.35f + Mathf.Min(1.6f, activeWaveNumber * 0.18f);
                contactDamage = 12f;
                rangedDamage = 7f + Mathf.Min(3f, activeWaveNumber * 0.30f);
                projectileSpeed = 12.2f + Mathf.Min(2.0f, activeWaveNumber * 0.22f);
                scoreValue = 100;
                transform.localScale = Vector3.one;
                characterMotion?.ResetMotionState();
                if (leftArmPivot != null)
                {
                    leftArmPivot.localPosition = new Vector3(-0.30f, 1.20f, 0.07f);
                    leftArmPivot.localScale = Vector3.one;
                }
                if (rightArmPivot != null)
                {
                    rightArmPivot.localPosition = new Vector3(0.32f, 1.20f, -0.02f);
                    rightArmPivot.localScale = Vector3.one;
                }
                if (weaponAnchor != null)
                {
                    weaponAnchor.localPosition = new Vector3(0.12f, 1.12f, 0.35f);
                }
                characterMotion?.ConfigureWeaponIkPose(ak74LeftGrip, ak74RightGrip, 0.34f, 0.35f);
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

                    float minStopDist = archetype == EnemyArchetype.Boss
                        ? 4.4f
                        : (archetype == EnemyArchetype.Heavy ? 4.5f : 3.7f);
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
                else
                {
                    StepInfantryFire(player, dist, dt);
                }
            }

            float currentYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            prevYaw = currentYaw;

            if (archetype != EnemyArchetype.Boss)
            {
                characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: true);
            }
        }

        private void StepInfantryFire(PlayerController player, float distToPlayer, float dt)
        {
            float maxRange = archetype == EnemyArchetype.Heavy ? 14.5f : 13.2f;
            if (distToPlayer > maxRange || player.Weapon == null)
            {
                burstRemaining = 0;
                return;
            }

            if (burstRemaining > 0)
            {
                burstTimer -= dt;
                if (burstTimer <= 0f)
                {
                    burstRemaining--;
                    burstTimer = 0.18f;
                    Transform muzzle = ActiveWeaponMuzzle;
                    FireTracerFromMuzzle(
                        muzzle,
                        player,
                        projectileSpeed,
                        rangedDamage,
                        Palette.EnemySecondary,
                        new Vector3(0.050f, 0.30f, 0.050f),
                        3.2f,
                        0.76f);
                }
                return;
            }

            fireCooldown -= dt;
            if (fireCooldown > 0f) return;

            if (archetype == EnemyArchetype.Heavy)
            {
                fireCooldown = UnityEngine.Random.Range(2.55f, 3.10f);
                burstRemaining = 3;
                burstTimer = 0f;
            }
            else
            {
                fireCooldown = UnityEngine.Random.Range(1.80f, 2.35f);
                Transform muzzle = ActiveWeaponMuzzle;
                FireTracerFromMuzzle(
                    muzzle,
                    player,
                    projectileSpeed,
                    rangedDamage,
                    Palette.EnemySecondary,
                    new Vector3(0.044f, 0.28f, 0.044f),
                    2.2f,
                    0.96f);
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

            // Coaxial cannon 2-round burst directly from turret barrel
            bossCannonTimer -= dt;
            if (bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (bossCannonBurst <= 0)
                {
                    bossCannonBurst = 2;
                }
                bossCannonBurst--;
                bossCannonTimer = bossCannonBurst > 0 ? 0.24f : 1.95f;
                FireTracerFromMuzzle(
                    bossMuzzleTip,
                    player,
                    projectileSpeed,
                    rangedDamage,
                    Palette.BossPurple,
                    new Vector3(0.09f, 0.42f, 0.09f),
                    1.5f,
                    0.66f);
            }

            bossBarrageTimer -= dt;
            if (bossBarrageTimer <= 0f)
            {
                bossBarrageTimer = 3.2f;
                bossBarrageStep++;
                float baseAngle = (bossBarrageStep % 2 == 0) ? 0f : 22.5f;
                Vector3 origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position + Vector3.up * 1.1f;

                if (player.Weapon != null)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float angleDeg = baseAngle + i * 45f;
                        Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
                        player.Weapon.SpawnEnemyProjectile(origin + dir * 0.45f, dir, 12.2f, 14f, Palette.BossPurple, new Vector3(0.20f, 0.20f, 0.20f), 2.5f);
                    }
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.BossPurple);
                AudioManager.Instance?.PlayEnemyShoot(0.62f, 0.34f);
            }

            bossHazardTimer -= dt;
            if (bossHazardTimer <= 0f)
            {
                bossHazardTimer = 4.8f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(player.transform.position, 2.35f, 22f);
            }
        }

        private void FireTracerFromMuzzle(
            Transform muzzle,
            PlayerController player,
            float speed,
            float damage,
            Color tint,
            Vector3 tracerScale,
            float spreadDeg,
            float audioPitch)
        {
            if (player == null || player.Weapon == null) return;

            Vector3 spawnPos = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.15f;
            Vector3 toTarget = player.transform.position - spawnPos;
            toTarget.y = 0f;
            Vector3 aimDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = Vector3.forward;
            aimDir.Normalize();

            if (spreadDeg > 0.01f)
            {
                aimDir = Quaternion.Euler(0f, UnityEngine.Random.Range(-spreadDeg, spreadDeg), 0f) * aimDir;
            }

            player.Weapon.SpawnEnemyProjectile(spawnPos, aimDir, speed, damage, tint, tracerScale, 2.2f);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, tint);
            if (archetype != EnemyArchetype.Boss)
            {
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.042f));
            }
            AudioManager.Instance?.PlayEnemyShoot(audioPitch, archetype == EnemyArchetype.Boss ? 0.30f : 0.20f);

            ShotsFiredCount++;
            LastShotOrigin = spawnPos;
            LastShotDirection = aimDir;
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
                UIManager.Instance?.UpdateBossHp(currentHp, maxHp, bossDisplayTitle);
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
