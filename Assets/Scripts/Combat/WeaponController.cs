using System.Collections.Generic;
using Audio;
using Character;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.Pool;

namespace Combat
{
    public enum WeaponType
    {
        PulseRifle = 0,       // [1] M4A1 Carbine
        ScatterShotgun = 1,   // [2] M870 Shotgun
        PlasmaRailgun = 2,    // [3] MK14 EBR Sniper
        HellfireLauncher = 3, // [4] RPG-7 Launcher
        An94Abakan = 4,       // [5] AN-94 Abakan (2-Round Hyperburst -> Auto)
        VectorSmg = 5         // [6] KRISS Vector .45 SMG (1100 RPM + Mobility)
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

        [Header("Ballistics")]
        [SerializeField] private float baseFireInterval = 0.13f;
        [SerializeField] private float projectileSpeed = 36f;
        [SerializeField] private float baseDamage = 26f;

        private ObjectPool<Projectile> _projectilePool;
        private readonly List<Projectile> _allCreatedProjectiles = new();
        private Material _boltMaterial;
        private float _fireCooldown;
        private int _weaponTier = 1;
        private static readonly int[] MagazineCapacities = { 30, 8, 12, 4, 30, 25 };
        private static readonly float[] ReloadDurations = { 1.15f, 1.35f, 1.25f, 1.55f, 1.20f, 0.95f };
        private readonly int[] _ammoInMag = { 30, 8, 12, 4, 30, 25 };
        private float _reloadTimer;
        private float _reloadDuration = 1.15f;
        [SerializeField] private GameObject[] weaponVisuals = new GameObject[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponLeftGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponRightGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponMuzzleTips = new Transform[SaveManager.WeaponCount];

        // AN-94 hyperburst state
        private bool _an94HyperburstReady = true;
        private float _queuedFollowupTimer;
        private float _queuedFollowupInterval = 0.036f;
        private float _queuedFollowupDamage;
        private bool _queuedFollowupCrit;
        private Vector3 _lastAimDirection = Vector3.forward;

        // Meta + Roguelite modifiers
        private float _permanentDamageMult = 1f;
        private float _skillDamageMult = 1f;
        private float _skillFireRateMult = 1f;
        private int _skillBonusProjectiles;
        private float _skillCritChance;

        public WeaponType CurrentWeapon { get; private set; } = WeaponType.PulseRifle;

        public bool IsReloading => _reloadTimer > 0f;
        public int CurrentMagAmmo => _ammoInMag[Mathf.Clamp((int)CurrentWeapon, 0, _ammoInMag.Length - 1)];
        public int CurrentMagCapacity => MagazineCapacities[Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1)];
        public float ReloadProgress => _reloadDuration > 0.001f ? Mathf.Clamp01(1f - _reloadTimer / _reloadDuration) : 1f;
        public Transform MuzzleTransform => GetActiveMuzzleTransform();
        private int QueuedFollowupShots { get; set; }

        public int TotalShotsFired { get; private set; }
        public Vector3 LastFiredOrigin { get; private set; }
        public Vector3 LastFiredDirection { get; private set; } = Vector3.forward;

        public float MoveSpeedMultiplier => CurrentWeapon switch
        {
            WeaponType.VectorSmg => 1.12f,
            WeaponType.HellfireLauncher => 0.94f,
            _ => 1.0f
        };

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
                var rootGo = new GameObject("ProjectilePoolRoot");
                projectilePoolRoot = rootGo.transform;
            }

            _boltMaterial = Palette.CreateLitMaterial("Mat_ProjectileBoltRuntime", Palette.Accent, 0.1f, 0.25f, true, 1.45f);
            _projectilePool = new ObjectPool<Projectile>(
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
            var go = new GameObject("Projectile_Bolt");
            go.transform.SetParent(projectilePoolRoot, false);

            var meshGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshGo.name = "BoltVisual";
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localScale = new Vector3(0.13f, 0.28f, 0.13f);

            var c = meshGo.GetComponent<Collider>();
            if (c != null) Destroy(c);

            var mr = meshGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _boltMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var proj = go.AddComponent<Projectile>();
            go.SetActive(false);
            _allCreatedProjectiles.Add(proj);
            return proj;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (_fireCooldown > 0f)
            {
                _fireCooldown = Mathf.Max(0f, _fireCooldown - dt);
            }

            if (_reloadTimer > 0f)
            {
                _reloadTimer = Mathf.Max(0f, _reloadTimer - dt);
                var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
                if (_reloadTimer <= 0f)
                {
                    _ammoInMag[wIdx] = MagazineCapacities[wIdx];
                    UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], MagazineCapacities[wIdx], false, 1f);
                }
                else
                {
                    UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], MagazineCapacities[wIdx], true, ReloadProgress);
                }
            }

            if (QueuedFollowupShots <= 0 || !(dt > 0f)) return;
            _queuedFollowupTimer -= dt;
            while (QueuedFollowupShots > 0 && _queuedFollowupTimer <= 0f)
            {
                QueuedFollowupShots--;
                _queuedFollowupTimer += _queuedFollowupInterval;
                FireAn94FollowupRound();
            }
        }

        public void SetWeaponTier(int level)
        {
            _weaponTier = Mathf.Max(1, level);
        }

        public void EquipWeapon(WeaponType weapon)
        {
            CurrentWeapon = weapon;
            _fireCooldown = Mathf.Min(_fireCooldown, 0.05f);
            QueuedFollowupShots = 0;
            _reloadTimer = 0f;
            _an94HyperburstReady = true;
            RefreshWeaponVisuals();
            UIManager.Instance?.UpdateAmmoHud(CurrentMagAmmo, CurrentMagCapacity, false, 1f);
        }

        public bool StartReload(bool quiet = false)
        {
            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            if (_reloadTimer > 0f || _ammoInMag[wIdx] >= MagazineCapacities[wIdx])
            {
                return false;
            }
            QueuedFollowupShots = 0;
            _reloadDuration = ReloadDurations[wIdx];
            _reloadTimer = _reloadDuration;
            if (!quiet)
            {
                AudioManager.Instance?.PlayReload();
            }
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], MagazineCapacities[wIdx], true, 0f);
            return true;
        }

        public void SetStatModifiers(float permDmgMult, float dmgMult, float rateMult, int bonusProj, float critChance)
        {
            _permanentDamageMult = Mathf.Max(0.5f, permDmgMult);
            _skillDamageMult = Mathf.Max(0.5f, dmgMult);
            _skillFireRateMult = Mathf.Max(0.5f, rateMult);
            _skillBonusProjectiles = Mathf.Max(0, bonusProj);
            _skillCritChance = Mathf.Clamp01(critChance);
        }

        private Transform GetActiveMuzzleTransform()
        {
            var idx = (int)CurrentWeapon;
            if (weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length && weaponMuzzleTips[idx] != null)
            {
                return weaponMuzzleTips[idx];
            }
            return muzzlePoint;
        }

        private Vector3 GetMuzzlePosition(Vector3 fallbackDir)
        {
            var active = GetActiveMuzzleTransform();
            return active != null ? active.position : transform.position + Vector3.up * 1.18f + fallbackDir * 0.85f;
        }

        private void SyncMuzzlePointToActiveTip()
        {
            var idx = (int)CurrentWeapon;
            var muzzleTip = (weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length) ? weaponMuzzleTips[idx] : null;
            if (muzzlePoint != null && muzzleTip != null)
            {
                muzzlePoint.SetPositionAndRotation(muzzleTip.position, muzzleTip.rotation);
            }
        }

        private void RefreshWeaponVisuals()
        {
            if (weaponVisuals == null) return;
            var idx = (int)CurrentWeapon;
            for (var i = 0; i < weaponVisuals.Length; i++)
            {
                if (weaponVisuals[i] != null)
                {
                    weaponVisuals[i].SetActive(i == idx);
                }
            }

            var leftGrip = (weaponLeftGrips != null && idx >= 0 && idx < weaponLeftGrips.Length) ? weaponLeftGrips[idx] : null;
            var rightGrip = (weaponRightGrips != null && idx >= 0 && idx < weaponRightGrips.Length) ? weaponRightGrips[idx] : null;
            characterMotion?.SetWeaponGripTargets(leftGrip, rightGrip);

            SyncMuzzlePointToActiveTip();
        }

        private Vector3 GetFlatBarrelForward()
        {
            var active = GetActiveMuzzleTransform();
            if (active != null)
            {
                var fwd = new Vector3(active.forward.x, 0f, active.forward.z);
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    return fwd.normalized;
                }
            }

            var bodyFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return bodyFwd.sqrMagnitude > 0.0001f ? bodyFwd.normalized : Vector3.forward;
        }

        /// <summary>Keeps muzzlePoint synced with the active firearm barrel tip each frame.</summary>
        public void SyncActiveMuzzle()
        {
            SyncMuzzlePointToActiveTip();
        }

        private Vector3 ResolveMuzzleFireDirection(Vector3 spawnPos, Vector3 flatAimDirection, Vector3? exactAimWorldPoint)
        {
            if (exactAimWorldPoint.HasValue)
            {
                var toTarget = exactAimWorldPoint.Value - spawnPos;
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

            var active = GetActiveMuzzleTransform();
            if (active == null) return new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            var fwd = new Vector3(active.forward.x, 0f, active.forward.z);
            return fwd.sqrMagnitude > 0.001f ? fwd.normalized : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        }

        public bool TryFire(bool triggerHeld, Vector3 flatAimDirection, Vector3? exactAimWorldPoint = null)
        {
            _lastAimDirection = flatAimDirection.sqrMagnitude > 0.001f
                ? new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized
                : transform.forward;

            if (!triggerHeld)
            {
                if (CurrentWeapon != WeaponType.An94Abakan || QueuedFollowupShots != 0) return false;
                _an94HyperburstReady = true;
                _fireCooldown = Mathf.Min(_fireCooldown, 0.065f);
                return false;
            }

            if (_reloadTimer > 0f)
            {
                return false;
            }

            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            if (_ammoInMag[wIdx] <= 0)
            {
                StartReload();
                return false;
            }

            if (_fireCooldown > 0f)
            {
                return false;
            }

            _ammoInMag[wIdx] = Mathf.Max(0, _ammoInMag[wIdx] - 1);
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], MagazineCapacities[wIdx], false, 1f);

            SyncMuzzlePointToActiveTip();
            var spawnPos = GetMuzzlePosition(_lastAimDirection);
            var dir = ResolveMuzzleFireDirection(spawnPos, _lastAimDirection, exactAimWorldPoint);

            var dmgScale = _permanentDamageMult * _skillDamageMult * (1f + (_weaponTier - 1) * 0.10f);
            var isCrit = Random.value < _skillCritChance;
            if (isCrit) dmgScale *= 1.65f;

            switch (CurrentWeapon)
            {
                case WeaponType.ScatterShotgun:
                {
                    _fireCooldown = 0.48f / _skillFireRateMult;
                    var pellets = 6 + _skillBonusProjectiles;
                    const float totalArc = 26f;
                    var pelletDmg = 19f * dmgScale;
                    for (var i = 0; i < pellets; i++)
                    {
                        var t = pellets == 1 ? 0.5f : (float)i / (pellets - 1);
                        var yaw = Mathf.Lerp(-totalArc * 0.5f, totalArc * 0.5f, t);
                        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 34f, pelletDmg, 0.62f, 0, 0f, Palette.Accent, new Vector3(0.024f, 0.28f, 0.024f), isCrit);
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
                    _fireCooldown = 0.52f / _skillFireRateMult;
                    var slugDmg = 84f * dmgScale;
                    var shots = 1 + Mathf.Min(1, _skillBonusProjectiles);
                    var rightAxis = Vector3.Cross(Vector3.up, dir).normalized;
                    for (var i = 0; i < shots; i++)
                    {
                        var origin = shots == 1 ? spawnPos : spawnPos + rightAxis * ((i == 0 ? -0.045f : 0.045f));
                        SpawnBolt(origin, Quaternion.LookRotation(dir, Vector3.up), 62f, slugDmg, 1.15f, 3, 0f, Palette.RailgunCyan, new Vector3(0.036f, 0.88f, 0.036f), isCrit);
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
                    _fireCooldown = 0.68f / _skillFireRateMult;
                    var rocketDmg = 70f * dmgScale;
                    var rockets = 1 + Mathf.Min(1, _skillBonusProjectiles);
                    for (var i = 0; i < rockets; i++)
                    {
                        var yaw = rockets == 1 ? 0f : (i == 0 ? -3f : 3f);
                        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 26f, rocketDmg, 1.7f, 0, 2.95f, Palette.RocketOrange, new Vector3(0.095f, 0.34f, 0.095f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.22f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.10f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.015f, -0.090f));
                    AudioManager.Instance?.PlayShoot(0.55f, 0.68f);
                    break;
                }
                case WeaponType.An94Abakan:
                {
                    if (_an94HyperburstReady)
                    {
                        _an94HyperburstReady = false;
                        var burstRoundDmg = 28f * dmgScale;
                        SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), 44f, burstRoundDmg, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.028f, 0.52f, 0.028f), isCrit);

                        QueuedFollowupShots = 1 + Mathf.Min(2, _skillBonusProjectiles);
                        _queuedFollowupInterval = 0.036f / _skillFireRateMult;
                        _queuedFollowupTimer = _queuedFollowupInterval;
                        _queuedFollowupDamage = burstRoundDmg;
                        _queuedFollowupCrit = isCrit;
                        _fireCooldown = 0.175f / _skillFireRateMult;

                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                        FeelManager.Instance?.TriggerCameraShake(0.06f);
                        characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.98f), 0.06f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.036f));
                        AudioManager.Instance?.PlayShoot(1.08f, 0.54f);
                    }
                    else
                    {
                        var autoDmg = 23f * dmgScale;
                        _fireCooldown = 0.135f / _skillFireRateMult;
                        SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), 42f, autoDmg, 1.35f, 0, 0f, Palette.Accent, new Vector3(0.028f, 0.48f, 0.028f), isCrit);
                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                        FeelManager.Instance?.TriggerCameraShake(0.05f);
                        characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.98f), 0.06f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.035f));
                        AudioManager.Instance?.PlayShoot(0.98f, 0.48f);
                    }
                    break;
                }
                case WeaponType.VectorSmg:
                {
                    _fireCooldown = 0.056f / _skillFireRateMult;
                    var smgDmg = 16f * dmgScale;
                    var rounds = 1 + Mathf.Min(1, _skillBonusProjectiles);
                    for (var i = 0; i < rounds; i++)
                    {
                        var jitter = rounds == 1
                            ? Random.Range(-1.4f, 1.4f)
                            : (i == 0 ? -1.1f : 1.1f);
                        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, jitter, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, 35f, smgDmg, 0.95f, 0, 0f, Palette.Accent, new Vector3(0.024f, 0.34f, 0.024f), isCrit);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.035f);
                    characterMotion?.TriggerSquash(new Vector3(1.015f, 0.99f, 0.985f), 0.05f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.003f, -0.024f));
                    AudioManager.Instance?.PlayShoot(1.28f, 0.44f);
                    break;
                }
                case WeaponType.PulseRifle:
                default: // [1] M4A1 Carbine
                {
                    var tierInterval = Mathf.Max(0.085f, baseFireInterval - (_weaponTier - 1) * 0.006f);
                    _fireCooldown = tierInterval / _skillFireRateMult;
                    var boltDmg = baseDamage * dmgScale;
                    SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), projectileSpeed, boltDmg, 1.45f, 0, 0f, Palette.Accent, new Vector3(0.028f, 0.48f, 0.028f), isCrit);
                    if (_skillBonusProjectiles > 0)
                    {
                        var rightAxis = Vector3.Cross(Vector3.up, dir).normalized;
                        for (var i = 0; i < _skillBonusProjectiles; i++)
                        {
                            var side = (i % 2 == 0) ? -0.055f : 0.055f;
                            var offsetPos = spawnPos + rightAxis * (side * (1 + i / 2));
                            SpawnBolt(offsetPos, Quaternion.LookRotation(dir, Vector3.up), projectileSpeed, boltDmg * 0.72f, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.024f, 0.38f, 0.024f), isCrit);
                        }
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.07f);
                    characterMotion?.TriggerSquash(new Vector3(1.025f, 0.985f, 0.98f), 0.07f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.042f));
                    AudioManager.Instance?.PlayShoot();
                    break;
                }
            }

            if (_ammoInMag[wIdx] <= 0 && QueuedFollowupShots == 0)
            {
                StartReload(quiet: false);
            }

            return true;
        }

        private void FireAn94FollowupRound()
        {
            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            if (_ammoInMag[wIdx] > 0)
            {
                _ammoInMag[wIdx]--;
            }
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], MagazineCapacities[wIdx], _reloadTimer > 0f, ReloadProgress);

            SyncMuzzlePointToActiveTip();
            var barrelDir = GetFlatBarrelForward();
            var spawnPos = GetMuzzlePosition(barrelDir);
            SpawnBolt(spawnPos, Quaternion.LookRotation(barrelDir, Vector3.up), 45f, _queuedFollowupDamage, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.028f, 0.52f, 0.028f), _queuedFollowupCrit);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
            FeelManager.Instance?.TriggerCameraShake(0.07f);
            characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.045f));
            AudioManager.Instance?.PlayShoot(1.22f, 0.54f);

            if (_ammoInMag[wIdx] <= 0 && QueuedFollowupShots == 0)
            {
                StartReload(quiet: false);
            }
        }

        private void SpawnBolt(Vector3 pos, Quaternion rot, float spd, float dmg, float lifetime, int pierce, float aoe, Color tint, Vector3 scale, bool crit)
        {
            TotalShotsFired++;
            LastFiredOrigin = pos;
            var fwd = rot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f) LastFiredDirection = fwd.normalized;
            var p = _projectilePool.Get();
            p.Initialize(_projectilePool, pos, rot, spd, dmg, lifetime, pierce, aoe, false, tint, scale, crit);
        }

        public void SpawnEnemyProjectile(Vector3 pos, Vector3 flatDir, float spd, float dmg, Color tintColor, Vector3? visualScale = null, float lifetime = 2.8f)
        {
            if (_projectilePool == null) return;
            var d = new Vector3(flatDir.x, 0f, flatDir.z);
            if (d.sqrMagnitude < 0.001f) d = Vector3.forward;
            var scale = visualScale ?? new Vector3(0.032f, 0.34f, 0.032f);
            var p = _projectilePool.Get();
            p.Initialize(_projectilePool, pos, Quaternion.LookRotation(d.normalized, Vector3.up), spd, dmg, lifetime, 0, 0f, true, tintColor, scale);
        }

        public void ReturnAllActive()
        {
            _fireCooldown = 0f;
            QueuedFollowupShots = 0;
            _reloadTimer = 0f;
            _an94HyperburstReady = true;
            _weaponTier = 1;
            for (var i = 0; i < MagazineCapacities.Length; i++)
            {
                _ammoInMag[i] = MagazineCapacities[i];
            }
            for (var i = 0; i < _allCreatedProjectiles.Count; i++)
            {
                var t = _allCreatedProjectiles[i];
                if (t != null && t.gameObject.activeSelf)
                {
                    t.ForceReturnToPool();
                }
            }
            UIManager.Instance?.UpdateAmmoHud(CurrentMagAmmo, CurrentMagCapacity, false, 1f);
        }
    }
}
