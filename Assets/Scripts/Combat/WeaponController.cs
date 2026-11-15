using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;

namespace TopDownShooter.Combat
{
    public enum WeaponType
    {
        PulseRifle = 0,
        ScatterShotgun = 1,
        PlasmaRailgun = 2,
        HellfireLauncher = 3
    }

    /// <summary>
    /// Handles 4-weapon arsenal switching, cooldowns, pooled projectiles (spread/pierce/AoE),
    /// hostile projectile spawning, muzzle VFX, recoil squash, and Roguelite scaling.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private Transform projectilePoolRoot;

        [Header("Ballistics")]
        [SerializeField] private float baseFireInterval = 0.14f;
        [SerializeField] private float projectileSpeed = 32f;
        [SerializeField] private float baseDamage = 26f;

        private ObjectPool<Projectile> projectilePool;
        private readonly List<Projectile> allCreatedProjectiles = new List<Projectile>();
        private Material boltMaterial;
        private float fireCooldown;
        private int weaponTier = 1;
        private WeaponType currentWeapon = WeaponType.PulseRifle;
        [SerializeField] private GameObject[] weaponVisuals = new GameObject[4];
        [SerializeField] private Transform[] weaponLeftGrips = new Transform[4];
        [SerializeField] private Transform[] weaponRightGrips = new Transform[4];
        [SerializeField] private Transform[] weaponMuzzleTips = new Transform[4];

        // Meta + Roguelite modifiers
        private float permanentDamageMult = 1f;
        private float skillDamageMult = 1f;
        private float skillFireRateMult = 1f;
        private int skillBonusProjectiles;
        private float skillCritChance;

        public WeaponType CurrentWeapon => currentWeapon;

        public void BindReferences(Transform muzzle, ProceduralCharacterMotion motion, Transform poolRoot)
        {
            muzzlePoint = muzzle;
            characterMotion = motion;
            projectilePoolRoot = poolRoot;
        }

        public void BindWeaponVisuals(GameObject[] visuals)
        {
            if (visuals == null) return;
            weaponVisuals = visuals;
            RefreshWeaponVisuals();
        }

        public void BindWeaponGrips(Transform[] leftGrips, Transform[] rightGrips, Transform[] muzzleTips)
        {
            if (leftGrips != null) weaponLeftGrips = leftGrips;
            if (rightGrips != null) weaponRightGrips = rightGrips;
            if (muzzleTips != null) weaponMuzzleTips = muzzleTips;
            RefreshWeaponVisuals();
        }

        private void Awake()
        {
            if (projectilePoolRoot == null)
            {
                GameObject rootGo = new GameObject("ProjectilePoolRoot");
                projectilePoolRoot = rootGo.transform;
            }

            boltMaterial = Palette.CreateLitMaterial("Mat_ProjectileBoltRuntime", Palette.Accent, 0.1f, 0.25f, true, 1.45f);
            projectilePool = new ObjectPool<Projectile>(
                createFunc: CreateProjectileInstance,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p =>
                {
                    if (p != null) Destroy(p.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: 32,
                maxSize: 120
            );
        }

        private Projectile CreateProjectileInstance()
        {
            GameObject go = new GameObject("Projectile_Bolt");
            go.transform.SetParent(projectilePoolRoot, false);

            GameObject meshGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshGo.name = "BoltVisual";
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localScale = new Vector3(0.13f, 0.28f, 0.13f);

            Collider c = meshGo.GetComponent<Collider>();
            if (c != null) Destroy(c);

            MeshRenderer mr = meshGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = boltMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Projectile proj = go.AddComponent<Projectile>();
            go.SetActive(false);
            allCreatedProjectiles.Add(proj);
            return proj;
        }

        private void Update()
        {
            if (fireCooldown > 0f)
            {
                fireCooldown -= Time.deltaTime;
            }
        }

        public void SetWeaponTier(int level)
        {
            weaponTier = Mathf.Max(1, level);
        }

        public void EquipWeapon(WeaponType weapon)
        {
            currentWeapon = weapon;
            fireCooldown = Mathf.Min(fireCooldown, 0.05f);
            RefreshWeaponVisuals();
        }

        public void SetStatModifiers(float permDmgMult, float dmgMult, float rateMult, int bonusProj, float critChance)
        {
            permanentDamageMult = Mathf.Max(0.5f, permDmgMult);
            skillDamageMult = Mathf.Max(0.5f, dmgMult);
            skillFireRateMult = Mathf.Max(0.5f, rateMult);
            skillBonusProjectiles = Mathf.Max(0, bonusProj);
            skillCritChance = Mathf.Clamp01(critChance);
        }

        private void RefreshWeaponVisuals()
        {
            if (weaponVisuals == null) return;
            int idx = (int)currentWeapon;
            for (int i = 0; i < weaponVisuals.Length; i++)
            {
                if (weaponVisuals[i] != null)
                {
                    weaponVisuals[i].SetActive(i == idx);
                }
            }

            Transform leftGrip = (weaponLeftGrips != null && idx >= 0 && idx < weaponLeftGrips.Length) ? weaponLeftGrips[idx] : null;
            Transform rightGrip = (weaponRightGrips != null && idx >= 0 && idx < weaponRightGrips.Length) ? weaponRightGrips[idx] : null;
            characterMotion?.SetWeaponGripTargets(leftGrip, rightGrip);

            Transform muzzleTip = (weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length) ? weaponMuzzleTips[idx] : null;
            if (muzzlePoint != null && muzzleTip != null)
            {
                muzzlePoint.SetPositionAndRotation(muzzleTip.position, muzzleTip.rotation);
            }
        }

        public bool TryFire(bool triggerHeld, Vector3 flatAimDirection)
        {
            if (!triggerHeld || fireCooldown > 0f)
            {
                return false;
            }

            Vector3 dir = flatAimDirection.sqrMagnitude > 0.001f
                ? new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized
                : transform.forward;
            Vector3 spawnPos = muzzlePoint != null
                ? muzzlePoint.position
                : transform.position + Vector3.up * 0.95f + dir * 0.85f;

            float dmgScale = permanentDamageMult * skillDamageMult * (1f + (weaponTier - 1) * 0.10f);
            bool isCrit = UnityEngine.Random.value < skillCritChance;
            if (isCrit) dmgScale *= 1.65f;

            switch (currentWeapon)
            {
                case WeaponType.ScatterShotgun:
                {
                    fireCooldown = 0.48f / skillFireRateMult;
                    int pellets = 5 + skillBonusProjectiles;
                    float totalArc = 32f;
                    float pelletDmg = 18f * dmgScale;
                    for (int i = 0; i < pellets; i++)
                    {
                        float t = pellets == 1 ? 0.5f : (float)i / (pellets - 1);
                        float yaw = Mathf.Lerp(-totalArc * 0.5f, totalArc * 0.5f, t);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 32f, pelletDmg, 0.65f, 0, 0f, Palette.Accent, new Vector3(0.055f, 0.26f, 0.055f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.16f);
                    characterMotion?.TriggerSquash(new Vector3(1.16f, 0.88f, 0.86f), 0.13f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.014f, -0.075f));
                    break;
                }
                case WeaponType.PlasmaRailgun:
                {
                    fireCooldown = 0.54f / skillFireRateMult;
                    float slugDmg = 78f * dmgScale;
                    int shots = 1 + Mathf.Min(1, skillBonusProjectiles);
                    for (int i = 0; i < shots; i++)
                    {
                        float yaw = shots == 1 ? 0f : (i == 0 ? -3f : 3f);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 58f, slugDmg, 1.15f, 3, 0f, Palette.RailgunCyan, new Vector3(0.065f, 0.82f, 0.065f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                    FeelManager.Instance?.TriggerCameraShake(0.20f);
                    characterMotion?.TriggerSquash(new Vector3(1.14f, 0.90f, 0.88f), 0.14f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.012f, -0.068f));
                    break;
                }
                case WeaponType.HellfireLauncher:
                {
                    fireCooldown = 0.68f / skillFireRateMult;
                    float rocketDmg = 66f * dmgScale;
                    int rockets = 1 + Mathf.Min(1, skillBonusProjectiles);
                    for (int i = 0; i < rockets; i++)
                    {
                        float yaw = rockets == 1 ? 0f : (i == 0 ? -5f : 5f);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 24f, rocketDmg, 1.7f, 0, 2.85f, Palette.RocketOrange, new Vector3(0.15f, 0.36f, 0.15f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.22f);
                    characterMotion?.TriggerSquash(new Vector3(1.18f, 0.86f, 0.86f), 0.16f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.015f, -0.090f));
                    break;
                }
                default: // M4A1 Carbine
                {
                    float tierInterval = Mathf.Max(0.08f, baseFireInterval - (weaponTier - 1) * 0.008f);
                    fireCooldown = tierInterval / skillFireRateMult;
                    float boltDmg = baseDamage * dmgScale;
                    int extraVolley = (weaponTier >= 3 ? 2 : 0) + skillBonusProjectiles;
                    SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), projectileSpeed * 1.15f, boltDmg, 1.45f, 0, 0f, Palette.Accent, new Vector3(0.055f, 0.40f, 0.055f), isCrit);
                    for (int i = 0; i < extraVolley; i++)
                    {
                        float sign = (i % 2 == 0) ? -1f : 1f;
                        float stepDeg = 5.5f * (1 + i / 2);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, sign * stepDeg, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, projectileSpeed * 1.15f, boltDmg * 0.72f, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.050f, 0.34f, 0.050f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.07f);
                    characterMotion?.TriggerSquash(new Vector3(1.10f, 0.92f, 0.90f), 0.10f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.042f));
                    break;
                }
            }

            AudioManager.Instance?.PlayShoot();
            return true;
        }

        private void SpawnBolt(Vector3 pos, Quaternion rot, float spd, float dmg, float lifetime, int pierce, float aoe, Color tint, Vector3 scale, bool crit)
        {
            Projectile p = projectilePool.Get();
            p.Initialize(projectilePool, pos, rot, spd, dmg, lifetime, pierce, aoe, false, tint, scale, crit);
        }

        public void SpawnEnemyProjectile(Vector3 pos, Vector3 flatDir, float spd, float dmg, Color tintColor)
        {
            if (projectilePool == null) return;
            Vector3 d = new Vector3(flatDir.x, 0f, flatDir.z);
            if (d.sqrMagnitude < 0.001f) d = Vector3.forward;
            Projectile p = projectilePool.Get();
            p.Initialize(projectilePool, pos, Quaternion.LookRotation(d.normalized, Vector3.up), spd, dmg, 2.8f, 0, 0f, true, tintColor, new Vector3(0.24f, 0.24f, 0.24f), false);
        }

        public void ReturnAllActive()
        {
            fireCooldown = 0f;
            weaponTier = 1;
            for (int i = 0; i < allCreatedProjectiles.Count; i++)
            {
                if (allCreatedProjectiles[i] != null && allCreatedProjectiles[i].gameObject.activeSelf)
                {
                    allCreatedProjectiles[i].ForceReturnToPool();
                }
            }
        }
    }
}
