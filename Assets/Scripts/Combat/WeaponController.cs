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
        PulseRifle = 0,       // [1] M4A1 Carbine
        ScatterShotgun = 1,   // [2] M870 Shotgun
        PlasmaRailgun = 2,    // [3] MK14 EBR Sniper
        HellfireLauncher = 3, // [4] RPG-7 Launcher
        AN94Abakan = 4,       // [5] AN-94 Abakan (2-Round Hyperburst -> Auto)
        VectorSMG = 5         // [6] KRISS Vector .45 SMG (1100 RPM + Mobility)
    }

    /// <summary>
    /// Handles 6-weapon Call-of-Duty-inspired tactical arsenal switching, pooled projectiles,
    /// straight-from-muzzle parallax-aligned ballistics, AN-94 2-round hyperburst, laser sight,
    /// muzzle VFX, recoil kickback, and Roguelite scaling.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private Transform projectilePoolRoot;
        [SerializeField] private LineRenderer laserSightLine;

        [Header("Ballistics")]
        [SerializeField] private float baseFireInterval = 0.13f;
        [SerializeField] private float projectileSpeed = 36f;
        [SerializeField] private float baseDamage = 26f;

        private ObjectPool<Projectile> projectilePool;
        private readonly List<Projectile> allCreatedProjectiles = new List<Projectile>();
        private Material boltMaterial;
        private Material laserMaterial;
        private float fireCooldown;
        private int weaponTier = 1;
        private WeaponType currentWeapon = WeaponType.PulseRifle;
        [SerializeField] private GameObject[] weaponVisuals = new GameObject[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponLeftGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponRightGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponMuzzleTips = new Transform[SaveManager.WeaponCount];

        // AN-94 hyperburst state
        private bool an94HyperburstReady = true;
        private int queuedFollowupShots;
        private float queuedFollowupTimer;
        private float queuedFollowupInterval = 0.036f;
        private float queuedFollowupDamage;
        private bool queuedFollowupCrit;
        private Vector3 lastAimDirection = Vector3.forward;
        private Vector3? lastAimWorldPoint;

        // Meta + Roguelite modifiers
        private float permanentDamageMult = 1f;
        private float skillDamageMult = 1f;
        private float skillFireRateMult = 1f;
        private int skillBonusProjectiles;
        private float skillCritChance;

        public WeaponType CurrentWeapon => currentWeapon;
        public Transform MuzzleTransform => GetActiveMuzzleTransform();
        public int QueuedFollowupShots => queuedFollowupShots;
        public int TotalShotsFired { get; private set; }
        public Vector3 LastFiredOrigin { get; private set; }
        public Vector3 LastFiredDirection { get; private set; }
        public float MoveSpeedMultiplier => currentWeapon == WeaponType.VectorSMG
            ? 1.12f
            : (currentWeapon == WeaponType.HellfireLauncher ? 0.94f : 1.0f);

        public void BindReferences(Transform muzzle, ProceduralCharacterMotion motion, Transform poolRoot)
        {
            muzzlePoint = muzzle;
            characterMotion = motion;
            projectilePoolRoot = poolRoot;
        }

        public void BindLaserSight(LineRenderer line)
        {
            laserSightLine = line;
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
            float dt = Time.deltaTime;
            if (fireCooldown > 0f)
            {
                fireCooldown = Mathf.Max(0f, fireCooldown - dt);
            }

            if (queuedFollowupShots > 0 && dt > 0f)
            {
                queuedFollowupTimer -= dt;
                while (queuedFollowupShots > 0 && queuedFollowupTimer <= 0f)
                {
                    queuedFollowupShots--;
                    queuedFollowupTimer += queuedFollowupInterval;
                    FireAN94FollowupRound();
                }
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
            queuedFollowupShots = 0;
            an94HyperburstReady = true;
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

        private Transform GetActiveMuzzleTransform()
        {
            int idx = (int)currentWeapon;
            if (weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length && weaponMuzzleTips[idx] != null)
            {
                return weaponMuzzleTips[idx];
            }
            return muzzlePoint;
        }

        private Vector3 GetMuzzlePosition(Vector3 fallbackDir)
        {
            Transform active = GetActiveMuzzleTransform();
            return active != null ? active.position : transform.position + Vector3.up * 1.18f + fallbackDir * 0.85f;
        }

        private void SyncMuzzlePointToActiveTip()
        {
            int idx = (int)currentWeapon;
            Transform muzzleTip = (weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length) ? weaponMuzzleTips[idx] : null;
            if (muzzlePoint != null && muzzleTip != null)
            {
                muzzlePoint.SetPositionAndRotation(muzzleTip.position, muzzleTip.rotation);
            }
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

            SyncMuzzlePointToActiveTip();
        }

        public Vector3 GetFlatBarrelForward()
        {
            Transform active = GetActiveMuzzleTransform();
            if (active != null)
            {
                Vector3 fwd = new Vector3(active.forward.x, 0f, active.forward.z);
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    return fwd.normalized;
                }
            }

            Vector3 bodyFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return bodyFwd.sqrMagnitude > 0.0001f ? bodyFwd.normalized : Vector3.forward;
        }

        public void UpdateAimVisuals(bool showLaser, Vector3? exactAimWorldPoint, Vector3 flatAimDirection)
        {
            SyncMuzzlePointToActiveTip();
            if (laserSightLine == null)
            {
                return;
            }

            if (!showLaser || currentWeapon == WeaponType.HellfireLauncher || Time.timeScale <= 0.01f)
            {
                laserSightLine.enabled = false;
                return;
            }

            if (laserSightLine.sharedMaterial == null)
            {
                if (laserMaterial == null)
                {
                    laserMaterial = Palette.CreateLitMaterial("Mat_LaserSightRuntime", Palette.EnemySecondary, 0f, 0f, true, Palette.EnemySecondary, 0.95f);
                }
                laserSightLine.sharedMaterial = laserMaterial;
            }

            Vector3 origin = GetMuzzlePosition(flatAimDirection);
            Vector3 dir = GetFlatBarrelForward();
            float maxDist = 24f;
            if (exactAimWorldPoint.HasValue)
            {
                Vector3 toTarget = exactAimWorldPoint.Value - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.25f)
                {
                    maxDist = Mathf.Clamp(toTarget.magnitude + 4f, 8f, 28f);
                }
            }

            float hitDist = maxDist;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null || col.CompareTag("Player")) continue;
                    hitDist = Mathf.Max(0.25f, hits[i].distance);
                    break;
                }
            }

            laserSightLine.enabled = true;
            laserSightLine.useWorldSpace = true;
            laserSightLine.startWidth = 0.006f;
            laserSightLine.endWidth = 0.003f;
            laserSightLine.positionCount = 2;
            laserSightLine.SetPosition(0, origin);
            laserSightLine.SetPosition(1, origin + dir * hitDist);
        }

        private Vector3 ResolveMuzzleFireDirection(Vector3 spawnPos, Vector3 flatAimDirection, Vector3? exactAimWorldPoint)
        {
            if (exactAimWorldPoint.HasValue)
            {
                Vector3 toTarget = exactAimWorldPoint.Value - spawnPos;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.25f)
                {
                    return toTarget.normalized;
                }
            }

            if (flatAimDirection.sqrMagnitude > 0.001f)
            {
                return new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized;
            }

            Transform active = GetActiveMuzzleTransform();
            if (active != null)
            {
                Vector3 fwd = new Vector3(active.forward.x, 0f, active.forward.z);
                if (fwd.sqrMagnitude > 0.001f)
                {
                    return fwd.normalized;
                }
            }

            return new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        }

        public bool TryFire(bool triggerHeld, Vector3 flatAimDirection, Vector3? exactAimWorldPoint = null)
        {
            lastAimDirection = flatAimDirection.sqrMagnitude > 0.001f
                ? new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized
                : transform.forward;
            lastAimWorldPoint = exactAimWorldPoint;

            if (!triggerHeld)
            {
                if (currentWeapon == WeaponType.AN94Abakan && queuedFollowupShots == 0)
                {
                    an94HyperburstReady = true;
                    fireCooldown = Mathf.Min(fireCooldown, 0.065f);
                }
                return false;
            }

            if (fireCooldown > 0f)
            {
                return false;
            }

            SyncMuzzlePointToActiveTip();
            Vector3 spawnPos = GetMuzzlePosition(lastAimDirection);
            Vector3 dir = ResolveMuzzleFireDirection(spawnPos, lastAimDirection, exactAimWorldPoint);

            float dmgScale = permanentDamageMult * skillDamageMult * (1f + (weaponTier - 1) * 0.10f);
            bool isCrit = UnityEngine.Random.value < skillCritChance;
            if (isCrit) dmgScale *= 1.65f;

            switch (currentWeapon)
            {
                case WeaponType.ScatterShotgun:
                {
                    fireCooldown = 0.48f / skillFireRateMult;
                    int pellets = 6 + skillBonusProjectiles;
                    float totalArc = 26f;
                    float pelletDmg = 19f * dmgScale;
                    for (int i = 0; i < pellets; i++)
                    {
                        float t = pellets == 1 ? 0.5f : (float)i / (pellets - 1);
                        float yaw = Mathf.Lerp(-totalArc * 0.5f, totalArc * 0.5f, t);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 34f, pelletDmg, 0.62f, 0, 0f, Palette.Accent, new Vector3(0.055f, 0.26f, 0.055f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.16f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.09f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.014f, -0.075f));
                    AudioManager.Instance?.PlayShoot(0.68f, 0.65f);
                    break;
                }
                case WeaponType.PlasmaRailgun:
                {
                    fireCooldown = 0.52f / skillFireRateMult;
                    float slugDmg = 84f * dmgScale;
                    int shots = 1 + Mathf.Min(1, skillBonusProjectiles);
                    Vector3 rightAxis = Vector3.Cross(Vector3.up, dir).normalized;
                    for (int i = 0; i < shots; i++)
                    {
                        Vector3 origin = shots == 1 ? spawnPos : spawnPos + rightAxis * ((i == 0 ? -0.045f : 0.045f));
                        SpawnBolt(origin, Quaternion.LookRotation(dir, Vector3.up), 62f, slugDmg, 1.15f, 3, 0f, Palette.RailgunCyan, new Vector3(0.065f, 0.82f, 0.065f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                    FeelManager.Instance?.TriggerCameraShake(0.20f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.09f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.012f, -0.068f));
                    AudioManager.Instance?.PlayShoot(0.80f, 0.62f);
                    break;
                }
                case WeaponType.HellfireLauncher:
                {
                    fireCooldown = 0.68f / skillFireRateMult;
                    float rocketDmg = 70f * dmgScale;
                    int rockets = 1 + Mathf.Min(1, skillBonusProjectiles);
                    for (int i = 0; i < rockets; i++)
                    {
                        float yaw = rockets == 1 ? 0f : (i == 0 ? -3f : 3f);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 26f, rocketDmg, 1.7f, 0, 2.95f, Palette.RocketOrange, new Vector3(0.15f, 0.36f, 0.15f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.22f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.10f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.015f, -0.090f));
                    AudioManager.Instance?.PlayShoot(0.55f, 0.68f);
                    break;
                }
                case WeaponType.AN94Abakan:
                {
                    if (an94HyperburstReady)
                    {
                        an94HyperburstReady = false;
                        float burstRoundDmg = 28f * dmgScale;
                        SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), 44f, burstRoundDmg, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.056f, 0.44f, 0.056f), isCrit);

                        queuedFollowupShots = 1 + Mathf.Min(2, skillBonusProjectiles);
                        queuedFollowupInterval = 0.036f / skillFireRateMult;
                        queuedFollowupTimer = queuedFollowupInterval;
                        queuedFollowupDamage = burstRoundDmg;
                        queuedFollowupCrit = isCrit;
                        fireCooldown = 0.175f / skillFireRateMult;

                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                        FeelManager.Instance?.TriggerCameraShake(0.06f);
                        characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.98f), 0.06f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.036f));
                        AudioManager.Instance?.PlayShoot(1.08f, 0.54f);
                    }
                    else
                    {
                        float autoDmg = 23f * dmgScale;
                        fireCooldown = 0.135f / skillFireRateMult;
                        SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), 42f, autoDmg, 1.35f, 0, 0f, Palette.Accent, new Vector3(0.052f, 0.38f, 0.052f), isCrit);
                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                        FeelManager.Instance?.TriggerCameraShake(0.05f);
                        characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.98f), 0.06f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.035f));
                        AudioManager.Instance?.PlayShoot(0.98f, 0.48f);
                    }
                    break;
                }
                case WeaponType.VectorSMG:
                {
                    fireCooldown = 0.056f / skillFireRateMult;
                    float smgDmg = 16f * dmgScale;
                    int rounds = 1 + Mathf.Min(1, skillBonusProjectiles);
                    for (int i = 0; i < rounds; i++)
                    {
                        float jitter = rounds == 1
                            ? UnityEngine.Random.Range(-1.4f, 1.4f)
                            : (i == 0 ? -1.1f : 1.1f);
                        Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, jitter, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 35f, smgDmg, 0.95f, 0, 0f, Palette.Accent, new Vector3(0.044f, 0.28f, 0.044f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.035f);
                    characterMotion?.TriggerSquash(new Vector3(1.015f, 0.99f, 0.985f), 0.05f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.003f, -0.024f));
                    AudioManager.Instance?.PlayShoot(1.28f, 0.44f);
                    break;
                }
                default: // [1] M4A1 Carbine
                {
                    float tierInterval = Mathf.Max(0.085f, baseFireInterval - (weaponTier - 1) * 0.006f);
                    fireCooldown = tierInterval / skillFireRateMult;
                    float boltDmg = baseDamage * dmgScale;
                    SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), projectileSpeed, boltDmg, 1.45f, 0, 0f, Palette.Accent, new Vector3(0.055f, 0.40f, 0.055f), isCrit);
                    if (skillBonusProjectiles > 0)
                    {
                        Vector3 rightAxis = Vector3.Cross(Vector3.up, dir).normalized;
                        for (int i = 0; i < skillBonusProjectiles; i++)
                        {
                            float side = (i % 2 == 0) ? -0.055f : 0.055f;
                            Vector3 offsetPos = spawnPos + rightAxis * side * (1 + i / 2);
                            SpawnBolt(offsetPos, Quaternion.LookRotation(dir, Vector3.up), projectileSpeed, boltDmg * 0.72f, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.048f, 0.34f, 0.048f), isCrit);
                        }
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.07f);
                    characterMotion?.TriggerSquash(new Vector3(1.025f, 0.985f, 0.98f), 0.07f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.042f));
                    AudioManager.Instance?.PlayShoot(1.0f, 0.55f);
                    break;
                }
            }

            LastFiredOrigin = spawnPos;
            LastFiredDirection = dir;
            return true;
        }

        private void FireAN94FollowupRound()
        {
            SyncMuzzlePointToActiveTip();
            Vector3 barrelDir = GetFlatBarrelForward();
            Vector3 spawnPos = GetMuzzlePosition(barrelDir);
            Vector3 dir = barrelDir;
            SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), 45f, queuedFollowupDamage, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.056f, 0.44f, 0.056f), queuedFollowupCrit);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
            FeelManager.Instance?.TriggerCameraShake(0.07f);
            characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.045f));
            AudioManager.Instance?.PlayShoot(1.22f, 0.54f);
            LastFiredOrigin = spawnPos;
            LastFiredDirection = dir;
        }

        private void SpawnBolt(Vector3 pos, Quaternion rot, float spd, float dmg, float lifetime, int pierce, float aoe, Color tint, Vector3 scale, bool crit)
        {
            TotalShotsFired++;
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
            queuedFollowupShots = 0;
            an94HyperburstReady = true;
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
