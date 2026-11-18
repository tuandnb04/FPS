using System.Collections.Generic;
using Audio;
using Character;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace Combat
{
    public enum WeaponType
    {
        PulseRifle = 0,       // [1] M4A1 Carbine (800 RPM 5.56mm)
        ScatterShotgun = 1,   // [2] M870 Shotgun (8-Pellet 00 Buckshot)
        PlasmaRailgun = 2,    // [3] MK14 EBR Sniper (7.62x51 AP)
        HellfireLauncher = 3, // [4] RPG-7V2 Launcher (2-Stage Rocket + Backblast)
        An94Abakan = 4,       // [5] AN-94 Abakan (1800 RPM 2-Rd Hyperburst -> 600 RPM Auto)
        VectorSmg = 5,        // [6] KRISS Vector .45 (1200 RPM Super-V Low Climb)
        M249Saw = 6,          // [7] M249 SAW (Open-Bolt Reverse Bloom + 4:1 Tracer Belt)
        M32Launcher = 7,      // [8] M32 MGL 40mm 6-Shot Rotary Launcher
        M16A4Burst = 8,       // [9] M16A4 5.56mm 3-Round Mechanical Burst-Only Rifle
        Xm25Airburst = 9      // [0] XM25 CDTE 25mm Smart Proximity Airburst & Ricochet Bullpup
    }

    public enum ReloadStyle
    {
        DetachableBoxMag = 0,
        ShellByShellShotgun = 1,
        FrontLoadedRocket = 2,
        BeltFedBoxLmg = 3,
        SwingOutCylinder = 4
    }

    [System.Serializable]
    public sealed class WeaponSlotConfig
    {
        public string weaponName;
        public WeaponType weaponType;
        public ReloadStyle reloadStyle;
        public int magazineCapacity = 30;
        public float reloadDuration = 1.15f;
        public float shotgunShellInterval = 0.28f;
        public float shotgunPumpFinishTime = 0.18f;
        public float fireInterval = 0.13f;
        public float damage = 26f;
        public int projectilesPerShot = 1;
        public float projectileSpeed = 36f;
        public float baseSpreadDeg = 1.45f;
        public float moveSpeedMultiplier = 1.0f;
        public GameObject weaponVisual;
        public Transform leftHandGrip;
        public Transform rightHandGrip;
        public Transform muzzleTip;
        public Transform ejectionPort;
        public Transform magVisual;
        public Mesh droppedMagMesh;
        public Material droppedMagMaterial;
    }

    /// <summary>
    /// Handles 10-weapon tactical arsenal switching, pooled projectiles,
    /// straight-from-muzzle parallax-aligned ballistics, AN-94 2-round hyperburst, M16A4 burst-only sear,
    /// XM25 proximity airburst/ricochet smart grenade, laser sight, physical magazine drops, and Roguelite scaling.
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
        private static readonly int[] MagazineCapacities = { 30, 8, 12, 4, 30, 25, 100, 6, 30, 6 };
        private static readonly float[] ReloadDurations = { 1.15f, 1.35f, 1.25f, 1.55f, 1.20f, 0.95f, 2.20f, 1.85f, 1.10f, 1.50f };
        private readonly int[] _ammoInMag = { 30, 8, 12, 4, 30, 25, 100, 6, 30, 6 };
        private float _reloadTimer;
        private float _reloadDuration = 1.15f;
        private float _shotgunShellTimer;
        private float _shotgunShellInterval = 0.28f;
        private bool _shotgunPumpPhase;
        [SerializeField] private GameObject[] weaponVisuals = new GameObject[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponLeftGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponRightGrips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponMuzzleTips = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponEjectionPorts = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Transform[] weaponMagVisuals = new Transform[SaveManager.WeaponCount];
        [SerializeField] private Mesh[] weaponMagMeshes = new Mesh[SaveManager.WeaponCount];
        [SerializeField] private Material[] weaponMagMaterials = new Material[SaveManager.WeaponCount];
        [SerializeField] private Mesh shotgunShellMesh;
        [SerializeField] private Material shotgunShellMaterial;
        [SerializeField] private WeaponSlotConfig[] weaponConfigs = new WeaponSlotConfig[SaveManager.WeaponCount];
        private readonly Vector3[] _baseMagLocalPos = new Vector3[SaveManager.WeaponCount];
        private readonly Quaternion[] _baseMagLocalRot = new Quaternion[SaveManager.WeaponCount];
        private bool _hasCachedMagPoses;

        // AN-94 hyperburst / M16A4 burst-only / M249 belt states
        private bool _an94HyperburstReady = true;
        private WeaponType _queuedFollowupWeapon = WeaponType.An94Abakan;
        private Vector3 _queuedFollowupDirection = Vector3.forward;
        private bool _burstTriggerLatched;
        private int _m249BeltCounter;
        private float _queuedFollowupTimer;
        private float _queuedFollowupInterval = 0.033f;
        private float _queuedFollowupDamage;
        private Vector3 _lastAimDirection = Vector3.forward;
        private Vector3? _lastExactAimWorldPoint;

        public bool IsBurstTriggerLatched => _burstTriggerLatched;
        public int QueuedBurstRounds => QueuedFollowupShots;

        // Armory tactical modifiers
        private float _permanentDamageMult = 1f;
        private float _reloadTimeMult = 1f;
        private float _magCapacityMult = 1f;
        private float _blastScaleMult = 1f;
        [FormerlySerializedAs("_droppedMagMesh")] [SerializeField] private Mesh droppedMagMesh;
        [FormerlySerializedAs("_droppedMagMat")] [SerializeField] private Material droppedMagMat;
        private LineRenderer _tacticalSightLine;
        private Material _sightLineMat;
        private static readonly RaycastHit[] SightRayBuffer = new RaycastHit[8];

        public WeaponType CurrentWeapon { get; private set; } = WeaponType.PulseRifle;

        public bool IsAimingDownSights { get; private set; }
        public float CurrentSpreadBloom { get; private set; }
        public bool IsSightLineVisible => _tacticalSightLine != null && _tacticalSightLine.enabled;

        public bool IsReloading => _reloadTimer > 0f;
        public System.Collections.Generic.IReadOnlyList<WeaponSlotConfig> WeaponConfigs => weaponConfigs;
        public ReloadStyle ActiveReloadStyle => GetWeaponReloadStyle(CurrentWeapon);
        public float ShotgunShellPhase => _shotgunShellInterval > 0.001f ? Mathf.Clamp01(1f - _shotgunShellTimer / _shotgunShellInterval) : 0f;
        public bool IsShotgunPumpPhase => _shotgunPumpPhase;

        public Mesh ActiveHeldReloadMesh
        {
            get
            {
                var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
                if (ActiveReloadStyle == ReloadStyle.ShellByShellShotgun && shotgunShellMesh != null)
                {
                    return shotgunShellMesh;
                }
                if (weaponConfigs != null && idx < weaponConfigs.Length && weaponConfigs[idx]?.droppedMagMesh != null)
                {
                    return weaponConfigs[idx].droppedMagMesh;
                }
                if (weaponMagMeshes != null && idx < weaponMagMeshes.Length && weaponMagMeshes[idx] != null)
                {
                    return weaponMagMeshes[idx];
                }
                return droppedMagMesh;
            }
        }

        public Material ActiveHeldReloadMaterial
        {
            get
            {
                var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
                if (ActiveReloadStyle == ReloadStyle.ShellByShellShotgun && shotgunShellMaterial != null)
                {
                    return shotgunShellMaterial;
                }
                var magTf = GetMagVisual(idx);
                if (magTf != null)
                {
                    var mr = magTf.GetComponentInChildren<MeshRenderer>(true);
                    if (mr != null && mr.sharedMaterial != null) return mr.sharedMaterial;
                }
                if (weaponConfigs != null && idx < weaponConfigs.Length && weaponConfigs[idx]?.droppedMagMaterial != null)
                {
                    return weaponConfigs[idx].droppedMagMaterial;
                }
                if (weaponMagMaterials != null && idx < weaponMagMaterials.Length && weaponMagMaterials[idx] != null)
                {
                    return weaponMagMaterials[idx];
                }
                return droppedMagMat;
            }
        }

        public Vector3 ActiveHeldReloadScale
        {
            get
            {
                if (ActiveReloadStyle == ReloadStyle.ShellByShellShotgun)
                {
                    return Vector3.one * 0.68f;
                }
                var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
                var magTf = GetMagVisual(idx);
                return magTf != null ? magTf.localScale : Vector3.one * 0.66f;
            }
        }

        private Transform GetMagVisual(int idx)
        {
            if (weaponConfigs != null && idx >= 0 && idx < weaponConfigs.Length && weaponConfigs[idx]?.magVisual != null)
            {
                return weaponConfigs[idx].magVisual;
            }
            if (weaponMagVisuals != null && idx >= 0 && idx < weaponMagVisuals.Length)
            {
                return weaponMagVisuals[idx];
            }
            return null;
        }

        public ReloadStyle GetWeaponReloadStyle(WeaponType weapon)
        {
            var idx = Mathf.Clamp((int)weapon, 0, SaveManager.WeaponCount - 1);
            if (weaponConfigs != null && idx < weaponConfigs.Length && weaponConfigs[idx] != null && !string.IsNullOrEmpty(weaponConfigs[idx].weaponName))
            {
                return weaponConfigs[idx].reloadStyle;
            }
            return weapon switch
            {
                WeaponType.ScatterShotgun => ReloadStyle.ShellByShellShotgun,
                WeaponType.HellfireLauncher => ReloadStyle.FrontLoadedRocket,
                WeaponType.M249Saw => ReloadStyle.BeltFedBoxLmg,
                WeaponType.M32Launcher => ReloadStyle.SwingOutCylinder,
                _ => ReloadStyle.DetachableBoxMag
            };
        }

        public int CurrentMagAmmo => _ammoInMag[Mathf.Clamp((int)CurrentWeapon, 0, _ammoInMag.Length - 1)];
        public int CurrentMagCapacity => GetEffectiveMagCapacity(Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1));
        public float ReloadProgress => _reloadDuration > 0.001f ? Mathf.Clamp01(1f - _reloadTimer / _reloadDuration) : 1f;
        public Transform MuzzleTransform => GetActiveMuzzleTransform();
        private int QueuedFollowupShots { get; set; }

        public float MoveSpeedMultiplier
        {
            get
            {
                var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
                if (weaponConfigs != null && idx < weaponConfigs.Length && weaponConfigs[idx] != null && weaponConfigs[idx].moveSpeedMultiplier > 0.1f)
                {
                    return weaponConfigs[idx].moveSpeedMultiplier;
                }
                return CurrentWeapon switch
                {
                    WeaponType.VectorSmg => 1.12f,
                    WeaponType.HellfireLauncher => 0.94f,
                    WeaponType.M249Saw => 0.90f,
                    WeaponType.M32Launcher => 0.95f,
                    WeaponType.Xm25Airburst => 0.96f,
                    _ => 1.0f
                };
            }
        }

        public int TotalShotsFired { get; private set; }
        public Vector3 LastFiredOrigin { get; private set; }
        public Vector3 LastFiredDirection { get; private set; } = Vector3.forward;

        public void BindDroppedMagazine(Mesh magMesh, Material magMat)
        {
            droppedMagMesh = magMesh;
            droppedMagMat = magMat;
        }

        public void BindWeaponMagazines(
            Transform[] magVisuals,
            Mesh[] magMeshes,
            Mesh shellMesh,
            Material[] magMaterials = null,
            Material shellMat = null)
        {
            if (magVisuals != null) weaponMagVisuals = magVisuals;
            if (magMeshes != null) weaponMagMeshes = magMeshes;
            if (magMaterials != null) weaponMagMaterials = magMaterials;
            if (shellMesh != null) shotgunShellMesh = shellMesh;
            if (shellMat != null) shotgunShellMaterial = shellMat;
            EnsureDefaultWeaponConfigs();
            SyncConfigsFromArrays();
            _hasCachedMagPoses = false;
            CacheMagBaseTransforms();
            UpdateActiveWeaponMagVisual();
        }

        private void EnsureDefaultWeaponConfigs()
        {
            if (weaponConfigs == null || weaponConfigs.Length != SaveManager.WeaponCount)
            {
                weaponConfigs = new WeaponSlotConfig[SaveManager.WeaponCount];
            }
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                if (weaponConfigs[i] != null && !string.IsNullOrEmpty(weaponConfigs[i].weaponName)) continue;
                var w = (WeaponType)i;
                weaponConfigs[i] = new WeaponSlotConfig
                {
                    weaponName = w.ToString(),
                    weaponType = w,
                    reloadStyle = w switch
                    {
                        WeaponType.ScatterShotgun => ReloadStyle.ShellByShellShotgun,
                        WeaponType.HellfireLauncher => ReloadStyle.FrontLoadedRocket,
                        WeaponType.M249Saw => ReloadStyle.BeltFedBoxLmg,
                        WeaponType.M32Launcher => ReloadStyle.SwingOutCylinder,
                        _ => ReloadStyle.DetachableBoxMag
                    },
                    magazineCapacity = MagazineCapacities[i],
                    reloadDuration = ReloadDurations[i],
                    shotgunShellInterval = 0.28f,
                    shotgunPumpFinishTime = 0.18f,
                    fireInterval = w switch
                    {
                        WeaponType.ScatterShotgun => 0.48f,
                        WeaponType.PlasmaRailgun => 0.52f,
                        WeaponType.HellfireLauncher => 0.68f,
                        WeaponType.An94Abakan => 0.133f,
                        WeaponType.VectorSmg => 0.050f,
                        WeaponType.M249Saw => 0.082f,
                        WeaponType.M32Launcher => 0.46f,
                        WeaponType.M16A4Burst => 0.24f,
                        WeaponType.Xm25Airburst => 0.42f,
                        _ => 0.078f
                    },
                    damage = w switch
                    {
                        WeaponType.ScatterShotgun => 19f,
                        WeaponType.PlasmaRailgun => 84f,
                        WeaponType.HellfireLauncher => 70f,
                        WeaponType.An94Abakan => 23f,
                        WeaponType.VectorSmg => 16f,
                        WeaponType.M249Saw => 24f,
                        WeaponType.M32Launcher => 62f,
                        WeaponType.M16A4Burst => 30f,
                        WeaponType.Xm25Airburst => 58f,
                        _ => baseDamage
                    },
                    projectilesPerShot = w == WeaponType.ScatterShotgun ? 8 : 1,
                    projectileSpeed = w switch
                    {
                        WeaponType.ScatterShotgun => 34f,
                        WeaponType.PlasmaRailgun => 62f,
                        WeaponType.HellfireLauncher => 18f,
                        WeaponType.An94Abakan => 46f,
                        WeaponType.VectorSmg => 35f,
                        WeaponType.M249Saw => 40f,
                        WeaponType.M32Launcher => 22f,
                        WeaponType.M16A4Burst => 52f,
                        WeaponType.Xm25Airburst => 32f,
                        _ => projectileSpeed
                    },
                    baseSpreadDeg = w switch
                    {
                        WeaponType.PlasmaRailgun => 0.25f,
                        WeaponType.Xm25Airburst => 0.35f,
                        WeaponType.M16A4Burst => 0.45f,
                        WeaponType.HellfireLauncher => 0.70f,
                        WeaponType.M32Launcher => 0.85f,
                        WeaponType.An94Abakan => 1.05f,
                        WeaponType.PulseRifle => 1.45f,
                        WeaponType.ScatterShotgun => 1.80f,
                        WeaponType.VectorSmg => 2.15f,
                        WeaponType.M249Saw => 2.55f,
                        _ => 1.45f
                    },
                    moveSpeedMultiplier = w switch
                    {
                        WeaponType.VectorSmg => 1.12f,
                        WeaponType.HellfireLauncher => 0.94f,
                        WeaponType.M249Saw => 0.90f,
                        WeaponType.M32Launcher => 0.95f,
                        WeaponType.Xm25Airburst => 0.96f,
                        _ => 1.0f
                    }
                };
            }
        }

        private void SyncConfigsFromArrays()
        {
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var cfg = weaponConfigs[i];
                if (cfg == null) continue;
                if (weaponVisuals != null && i < weaponVisuals.Length && weaponVisuals[i] != null) cfg.weaponVisual = weaponVisuals[i];
                if (weaponLeftGrips != null && i < weaponLeftGrips.Length && weaponLeftGrips[i] != null) cfg.leftHandGrip = weaponLeftGrips[i];
                if (weaponRightGrips != null && i < weaponRightGrips.Length && weaponRightGrips[i] != null) cfg.rightHandGrip = weaponRightGrips[i];
                if (weaponMuzzleTips != null && i < weaponMuzzleTips.Length && weaponMuzzleTips[i] != null) cfg.muzzleTip = weaponMuzzleTips[i];
                if (weaponMagVisuals != null && i < weaponMagVisuals.Length && weaponMagVisuals[i] != null) cfg.magVisual = weaponMagVisuals[i];
                if (weaponMagMeshes != null && i < weaponMagMeshes.Length && weaponMagMeshes[i] != null) cfg.droppedMagMesh = weaponMagMeshes[i];
                if (weaponMagMaterials != null && i < weaponMagMaterials.Length && weaponMagMaterials[i] != null) cfg.droppedMagMaterial = weaponMagMaterials[i];
            }
        }

        private void CacheMagBaseTransforms()
        {
            if (_hasCachedMagPoses) return;
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var magTf = GetMagVisual(i);
                if (magTf != null)
                {
                    _baseMagLocalPos[i] = magTf.localPosition;
                    _baseMagLocalRot[i] = magTf.localRotation;
                }
            }
            _hasCachedMagPoses = true;
        }

        private int GetEffectiveMagCapacity(int wIdx)
        {
            var baseCap = (weaponConfigs != null && wIdx < weaponConfigs.Length && weaponConfigs[wIdx] != null && weaponConfigs[wIdx].magazineCapacity > 0)
                ? weaponConfigs[wIdx].magazineCapacity
                : MagazineCapacities[wIdx];
            return Mathf.Max(1, Mathf.RoundToInt(baseCap * _magCapacityMult));
        }

        public void InstantRefillCurrentMagazine()
        {
            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            _reloadTimer = 0f;
            _shotgunShellTimer = 0f;
            _shotgunPumpPhase = false;
            QueuedFollowupShots = 0;
            _burstTriggerLatched = false;
            _ammoInMag[wIdx] = GetEffectiveMagCapacity(wIdx);
            UpdateActiveWeaponMagVisual();
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], GetEffectiveMagCapacity(wIdx), false, 1f);
        }

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

        public void BindWeaponGrips(Transform[] leftGrips, Transform[] rightGrips, Transform[] muzzleTips, Transform[] ejectionPorts = null)
        {
            if (leftGrips != null) weaponLeftGrips = leftGrips;
            if (rightGrips != null) weaponRightGrips = rightGrips;
            if (muzzleTips != null) weaponMuzzleTips = muzzleTips;
            if (ejectionPorts != null) weaponEjectionPorts = ejectionPorts;
            EnsureDefaultWeaponConfigs();
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                if (weaponConfigs[i] == null) continue;
                if (weaponLeftGrips != null && i < weaponLeftGrips.Length && weaponLeftGrips[i] != null) weaponConfigs[i].leftHandGrip = weaponLeftGrips[i];
                if (weaponRightGrips != null && i < weaponRightGrips.Length && weaponRightGrips[i] != null) weaponConfigs[i].rightHandGrip = weaponRightGrips[i];
                if (weaponMuzzleTips != null && i < weaponMuzzleTips.Length && weaponMuzzleTips[i] != null) weaponConfigs[i].muzzleTip = weaponMuzzleTips[i];
                if (weaponEjectionPorts != null && i < weaponEjectionPorts.Length && weaponEjectionPorts[i] != null) weaponConfigs[i].ejectionPort = weaponEjectionPorts[i];
            }
            RefreshWeaponVisuals();
        }

        public void GetActiveEjectionPort(out Vector3 worldPos, out Vector3 ejectRightDir)
        {
            var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
            var cfg = GetWeaponConfig(idx);
            var port = cfg?.ejectionPort;
            if (port == null && weaponEjectionPorts != null && idx < weaponEjectionPorts.Length)
            {
                port = weaponEjectionPorts[idx];
            }
            if (port == null)
            {
                var vis = cfg?.weaponVisual ?? (weaponVisuals != null && idx < weaponVisuals.Length ? weaponVisuals[idx] : null);
                if (vis != null) port = vis.transform.Find("EjectionPort");
            }

            if (port != null)
            {
                worldPos = port.position;
                ejectRightDir = port.right.sqrMagnitude > 0.001f ? port.right.normalized : transform.right;
                return;
            }

            var fwd = GetFlatBarrelForward();
            worldPos = GetMuzzlePosition(fwd) - fwd * 0.28f;
            ejectRightDir = transform.right;
        }

        private void Awake()
        {
            if (projectilePoolRoot == null)
            {
                var rootGo = new GameObject("ProjectilePoolRoot");
                projectilePoolRoot = rootGo.transform;
            }

            _boltMaterial = Palette.CreateLitMaterial("Mat_ProjectileBoltRuntime", Palette.Accent, 0.1f, 0.25f, true, 1.45f);
            _boltMaterial.enableInstancing = true;
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

            var prewarm = new Projectile[18];
            for (var i = 0; i < prewarm.Length; i++) prewarm[i] = _projectilePool.Get();
            for (var i = 0; i < prewarm.Length; i++) _projectilePool.Release(prewarm[i]);

            var sightGo = new GameObject("TacticalLaserSight");
            sightGo.transform.SetParent(transform, false);
            _tacticalSightLine = sightGo.AddComponent<LineRenderer>();
            _tacticalSightLine.positionCount = 2;
            _tacticalSightLine.startWidth = 0.018f;
            _tacticalSightLine.endWidth = 0.008f;
            _tacticalSightLine.useWorldSpace = true;
            _tacticalSightLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tacticalSightLine.receiveShadows = false;
            var spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                _sightLineMat = new Material(spriteShader) { name = "Mat_TacticalLaserSightRuntime" };
                _tacticalSightLine.sharedMaterial = _sightLineMat;
            }
            _tacticalSightLine.enabled = false;
            EnsureDefaultWeaponConfigs();
            CacheMagBaseTransforms();
            UpdateActiveWeaponMagVisual();
        }

        public void SetAimDownSights(bool aiming)
        {
            IsAimingDownSights = aiming;
        }

        public void SetAimInput(Vector3 flatAimDirection, Vector3? exactAimWorldPoint = null)
        {
            if (flatAimDirection.sqrMagnitude > 0.001f)
            {
                _lastAimDirection = new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized;
                transform.rotation = Quaternion.LookRotation(_lastAimDirection, Vector3.up);
                characterMotion?.AlignWeaponBarrelWithBodyYaw();
            }
            _lastExactAimWorldPoint = exactAimWorldPoint;
        }

        public float GetEffectiveSpreadDeg(WeaponType type)
        {
            if (IsAimingDownSights)
            {
                return 0f;
            }

            var idx = Mathf.Clamp((int)type, 0, SaveManager.WeaponCount - 1);
            var baseSpread = (weaponConfigs != null && idx < weaponConfigs.Length && weaponConfigs[idx] != null && weaponConfigs[idx].baseSpreadDeg > 0.01f)
                ? weaponConfigs[idx].baseSpreadDeg
                : type switch
                {
                    WeaponType.PlasmaRailgun => 0.25f,
                    WeaponType.Xm25Airburst => 0.35f,
                    WeaponType.M16A4Burst => 0.45f,
                    WeaponType.HellfireLauncher => 0.70f,
                    WeaponType.M32Launcher => 0.85f,
                    WeaponType.An94Abakan => 1.05f,
                    WeaponType.PulseRifle => 1.45f,
                    WeaponType.ScatterShotgun => 1.80f,
                    WeaponType.VectorSmg => 2.15f,
                    WeaponType.M249Saw => 2.55f,
                    _ => 1.45f
                };
            return baseSpread + CurrentSpreadBloom;
        }

        private Projectile CreateProjectileInstance()
        {
            var go = new GameObject("Projectile_Bolt");
            go.transform.SetParent(projectilePoolRoot, false);
            try { go.tag = "Projectile"; } catch (UnityException) { }
            var projLayer = LayerMask.NameToLayer("Projectile");
            if (projLayer >= 0) go.layer = projLayer;

            var meshGo = new GameObject("BoltVisual");
            meshGo.transform.SetParent(go.transform, false);
            if (projLayer >= 0) meshGo.layer = projLayer;
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localScale = new Vector3(0.06f, 0.28f, 0.06f);
            meshGo.AddComponent<MeshFilter>().sharedMesh = Projectile.GetOrCreateSharedBulletMesh();
            var mr = meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _boltMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

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

            if (dt > 0f && CurrentSpreadBloom > 0f)
            {
                CurrentSpreadBloom = Mathf.MoveTowards(CurrentSpreadBloom, 0f, dt * (IsAimingDownSights ? 14f : 9.5f));
            }

            UpdateTacticalSightLine();

            if (_reloadTimer > 0f)
            {
                _reloadTimer = Mathf.Max(0f, _reloadTimer - dt);
                var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
                var effectiveCap = GetEffectiveMagCapacity(wIdx);
                var style = GetWeaponReloadStyle(CurrentWeapon);
                if (style == ReloadStyle.ShellByShellShotgun)
                {
                    if (!_shotgunPumpPhase && _ammoInMag[wIdx] < effectiveCap)
                    {
                        _shotgunShellTimer -= dt;
                        while (_shotgunShellTimer <= 0f && _ammoInMag[wIdx] < effectiveCap)
                        {
                            _ammoInMag[wIdx] = Mathf.Min(effectiveCap, _ammoInMag[wIdx] + 1);
                            AudioManager.Instance?.PlayShotgunShellInsert();
                            if (_ammoInMag[wIdx] >= effectiveCap)
                            {
                                _shotgunPumpPhase = true;
                                _shotgunShellInterval = Mathf.Max(0.10f, _reloadTimer);
                                _shotgunShellTimer = _shotgunShellInterval;
                                AudioManager.Instance?.PlayReload();
                            }
                            else
                            {
                                _shotgunShellTimer += _shotgunShellInterval;
                            }
                        }
                    }
                    else if (_shotgunPumpPhase)
                    {
                        _shotgunShellTimer = Mathf.Max(0f, _reloadTimer);
                    }

                    if (_reloadTimer <= 0f && _ammoInMag[wIdx] < effectiveCap)
                    {
                        _ammoInMag[wIdx] = Mathf.Min(effectiveCap, _ammoInMag[wIdx] + 1);
                        AudioManager.Instance?.PlayShotgunShellInsert();
                    }
                }

                if (_reloadTimer <= 0f)
                {
                    _ammoInMag[wIdx] = effectiveCap;
                    _shotgunPumpPhase = false;
                    _shotgunShellTimer = 0f;
                    UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], effectiveCap, false, 1f);
                }
                else
                {
                    UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], effectiveCap, true, ReloadProgress);
                }
            }

            UpdateActiveWeaponMagVisual();

            if (QueuedFollowupShots <= 0 || !(dt > 0f)) return;
            _queuedFollowupTimer -= dt;
            while (QueuedFollowupShots > 0 && _queuedFollowupTimer <= 0f)
            {
                QueuedFollowupShots--;
                _queuedFollowupTimer += _queuedFollowupInterval;
                FireAn94FollowupRound();
            }
        }

        public void EquipWeapon(WeaponType weapon)
        {
            CurrentWeapon = weapon;
            _fireCooldown = Mathf.Min(_fireCooldown, 0.05f);
            QueuedFollowupShots = 0;
            _burstTriggerLatched = false;
            _reloadTimer = 0f;
            _shotgunShellTimer = 0f;
            _shotgunPumpPhase = false;
            CurrentSpreadBloom = 0f;
            _an94HyperburstReady = true;
            RefreshWeaponVisuals();
            UpdateActiveWeaponMagVisual();
            UIManager.Instance?.UpdateAmmoHud(CurrentMagAmmo, CurrentMagCapacity, false, 1f);
        }

        public bool StartReload(bool quiet = false)
        {
            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            var effectiveCap = GetEffectiveMagCapacity(wIdx);
            if (_reloadTimer > 0f || _ammoInMag[wIdx] >= effectiveCap)
            {
                return false;
            }
            QueuedFollowupShots = 0;
            _burstTriggerLatched = false;
            var style = GetWeaponReloadStyle(CurrentWeapon);

            if (style == ReloadStyle.ShellByShellShotgun)
            {
                var cfg = (weaponConfigs != null && wIdx < weaponConfigs.Length) ? weaponConfigs[wIdx] : null;
                var baseShellInterval = cfg != null && cfg.shotgunShellInterval > 0.05f ? cfg.shotgunShellInterval : 0.28f;
                var basePumpTail = cfg != null && cfg.shotgunPumpFinishTime > 0.05f ? cfg.shotgunPumpFinishTime : 0.18f;
                var missing = Mathf.Max(1, effectiveCap - _ammoInMag[wIdx]);
                _shotgunShellInterval = Mathf.Max(0.14f, baseShellInterval * _reloadTimeMult);
                _reloadDuration = missing * _shotgunShellInterval + basePumpTail;
                _reloadTimer = _reloadDuration;
                _shotgunShellTimer = _shotgunShellInterval;
                _shotgunPumpPhase = false;
            }
            else
            {
                _shotgunPumpPhase = false;
                var cfg = (weaponConfigs != null && wIdx < weaponConfigs.Length) ? weaponConfigs[wIdx] : null;
                var baseDur = (cfg != null && cfg.reloadDuration > 0.1f) ? cfg.reloadDuration : ReloadDurations[wIdx];
                _reloadDuration = Mathf.Max(0.35f, baseDur * _reloadTimeMult);
                _reloadTimer = _reloadDuration;
                if (!quiet)
                {
                    AudioManager.Instance?.PlayReload();
                }

                if (Application.isPlaying)
                {
                    var barrelDir = GetFlatBarrelForward();
                    var muzzlePos = GetMuzzlePosition(barrelDir);
                    var ejectPos = muzzlePos - barrelDir * 0.30f - Vector3.up * 0.10f;
                    var ejectVel = (-transform.right * 1.45f - barrelDir * 0.35f) + Vector3.up * 1.05f;

                    if (style == ReloadStyle.DetachableBoxMag || style == ReloadStyle.BeltFedBoxLmg)
                    {
                        var dropMesh = cfg?.droppedMagMesh != null
                            ? cfg.droppedMagMesh
                            : (weaponMagMeshes != null && wIdx < weaponMagMeshes.Length && weaponMagMeshes[wIdx] != null ? weaponMagMeshes[wIdx] : droppedMagMesh);
                        var dropMat = cfg?.droppedMagMaterial != null
                            ? cfg.droppedMagMaterial
                            : (weaponMagMaterials != null && wIdx < weaponMagMaterials.Length && weaponMagMaterials[wIdx] != null ? weaponMagMaterials[wIdx] : droppedMagMat);
                        DroppedMagazine.SpawnFromVisual(GetMagVisual(wIdx), dropMesh, dropMat, ejectPos, transform.rotation, ejectVel);
                    }
                    else if (style == ReloadStyle.SwingOutCylinder)
                    {
                        var drumPos = muzzlePos - barrelDir * 0.24f - transform.right * 0.08f - Vector3.up * 0.04f;
                        for (var s = 0; s < 4; s++)
                        {
                            DroppedMagazine.SpawnShellCasing(drumPos + barrelDir * (s * 0.02f), -transform.right);
                        }
                    }
                }
            }

            UpdateActiveWeaponMagVisual();
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], effectiveCap, true, 0f);
            return true;
        }

        public void SetStatModifiers(
            float permDmgMult,
            float reloadTimeMult = 1f,
            float magCapacityMult = 1f,
            float blastScaleMult = 1f)
        {
            var prevMagMult = _magCapacityMult;
            _permanentDamageMult = Mathf.Max(0.5f, permDmgMult);
            _reloadTimeMult = Mathf.Clamp(reloadTimeMult, 0.35f, 1.5f);
            _magCapacityMult = Mathf.Clamp(magCapacityMult, 0.75f, 2.5f);
            _blastScaleMult = Mathf.Clamp(blastScaleMult, 0.75f, 2.2f);

            if (_magCapacityMult > prevMagMult + 0.01f)
            {
                for (var i = 0; i < _ammoInMag.Length; i++)
                {
                    _ammoInMag[i] = GetEffectiveMagCapacity(i);
                }
                UIManager.Instance?.UpdateAmmoHud(CurrentMagAmmo, CurrentMagCapacity, _reloadTimer > 0f, ReloadProgress);
            }
        }

        private WeaponSlotConfig GetWeaponConfig(int idx)
        {
            return weaponConfigs != null && idx >= 0 && idx < weaponConfigs.Length ? weaponConfigs[idx] : null;
        }

        private Transform GetActiveMuzzleTransform()
        {
            var idx = (int)CurrentWeapon;
            var cfg = GetWeaponConfig(idx);
            if (cfg != null && cfg.muzzleTip != null) return cfg.muzzleTip;
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
            var cfg = GetWeaponConfig(idx);
            var muzzleTip = cfg?.muzzleTip != null
                ? cfg.muzzleTip
                : ((weaponMuzzleTips != null && idx >= 0 && idx < weaponMuzzleTips.Length) ? weaponMuzzleTips[idx] : null);
            if (muzzlePoint != null && muzzleTip != null)
            {
                muzzlePoint.SetPositionAndRotation(muzzleTip.position, muzzleTip.rotation);
            }
        }

        private void RefreshWeaponVisuals()
        {
            var idx = (int)CurrentWeapon;
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var vis = GetWeaponConfig(i)?.weaponVisual
                    ?? ((weaponVisuals != null && i < weaponVisuals.Length) ? weaponVisuals[i] : null);
                if (vis != null)
                {
                    vis.SetActive(i == idx);
                }
            }

            var activeCfg = GetWeaponConfig(idx);
            var leftGrip = activeCfg?.leftHandGrip
                ?? ((weaponLeftGrips != null && idx >= 0 && idx < weaponLeftGrips.Length) ? weaponLeftGrips[idx] : null);
            var rightGrip = activeCfg?.rightHandGrip
                ?? ((weaponRightGrips != null && idx >= 0 && idx < weaponRightGrips.Length) ? weaponRightGrips[idx] : null);
            characterMotion?.SetWeaponGripTargets(leftGrip, rightGrip);

            SyncMuzzlePointToActiveTip();
            UpdateActiveWeaponMagVisual();
        }

        private void UpdateActiveWeaponMagVisual()
        {
            CacheMagBaseTransforms();
            var idx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
            var magTf = GetMagVisual(idx);
            if (magTf == null) return;
            var style = GetWeaponReloadStyle(CurrentWeapon);
            switch (style)
            {
                case ReloadStyle.DetachableBoxMag:
                case ReloadStyle.BeltFedBoxLmg:
                {
                    var showAttached = !IsReloading || ReloadProgress >= 0.68f;
                    if (magTf.gameObject.activeSelf != showAttached)
                    {
                        magTf.gameObject.SetActive(showAttached);
                    }
                    break;
                }
                case ReloadStyle.FrontLoadedRocket:
                {
                    var showWarhead = (!IsReloading && _ammoInMag[idx] > 0 && _fireCooldown <= 0.40f)
                                      || (IsReloading && ReloadProgress >= 0.66f);
                    if (magTf.gameObject.activeSelf != showWarhead)
                    {
                        magTf.gameObject.SetActive(showWarhead);
                    }
                    break;
                }
                case ReloadStyle.SwingOutCylinder:
                {
                    if (!magTf.gameObject.activeSelf) magTf.gameObject.SetActive(true);
                    if (IsReloading && ReloadProgress >= 0.10f && ReloadProgress < 0.74f)
                    {
                        var openIn = Mathf.Clamp01((ReloadProgress - 0.10f) / 0.14f);
                        var closeOut = Mathf.Clamp01((0.74f - ReloadProgress) / 0.12f);
                        var swing = Mathf.Min(openIn, closeOut);
                        magTf.localPosition = _baseMagLocalPos[idx] + new Vector3(-0.072f, -0.016f, 0f) * swing;
                        magTf.localRotation = Quaternion.Slerp(
                            _baseMagLocalRot[idx],
                            _baseMagLocalRot[idx] * Quaternion.Euler(0f, 0f, -38f),
                            swing);
                    }
                    else
                    {
                        magTf.localPosition = _baseMagLocalPos[idx];
                        magTf.localRotation = _baseMagLocalRot[idx];
                    }
                    break;
                }
            }
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

        /// <summary>Keeps muzzlePoint synced with the active firearm barrel tip and updates the ADS laser sight line.</summary>
        public void SyncActiveMuzzle()
        {
            SyncMuzzlePointToActiveTip();
            UpdateTacticalSightLine();
        }

        private Vector3 ResolveMuzzleFireDirection(Vector3 spawnPos, Vector3 flatAimDirection, Vector3? exactAimWorldPoint)
        {
            var barrelFwd = GetFlatBarrelForward();
            if (barrelFwd.sqrMagnitude > 0.0001f)
            {
                return barrelFwd;
            }

            if (flatAimDirection.sqrMagnitude > 0.001f)
            {
                return new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized;
            }

            var bodyFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return bodyFwd.sqrMagnitude > 0.001f ? bodyFwd.normalized : Vector3.forward;
        }

        private void UpdateTacticalSightLine()
        {
            if (_tacticalSightLine == null) return;
            var playing = GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;
            if (!IsAimingDownSights || IsReloading || !playing)
            {
                _tacticalSightLine.enabled = false;
                return;
            }

            SyncMuzzlePointToActiveTip();
            var origin = GetMuzzlePosition(_lastAimDirection);
            var aimFwd = ResolveMuzzleFireDirection(origin, _lastAimDirection, _lastExactAimWorldPoint);
            var range = CurrentWeapon == WeaponType.PlasmaRailgun ? 24f : 12.5f;
            var endPoint = origin + aimFwd * range;

            var hitCount = Physics.RaycastNonAlloc(origin, aimFwd, SightRayBuffer, range, ~0, QueryTriggerInteraction.Ignore);
            var bestDist = range;
            for (var i = 0; i < hitCount; i++)
            {
                var h = SightRayBuffer[i];
                if (h.collider == null || h.collider.CompareTag("Player")) continue;
                if (h.collider.GetComponentInParent<SquadMemberController>() != null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    endPoint = h.point;
                }
            }

            var col = CurrentWeapon == WeaponType.PlasmaRailgun
                ? new Color(Palette.RailgunCyan.r, Palette.RailgunCyan.g, Palette.RailgunCyan.b, 0.72f)
                : new Color(1f, 0.28f, 0.22f, 0.42f);
            _tacticalSightLine.startColor = col;
            _tacticalSightLine.endColor = new Color(col.r, col.g, col.b, 0.12f);
            _tacticalSightLine.SetPosition(0, origin);
            _tacticalSightLine.SetPosition(1, endPoint);
            _tacticalSightLine.enabled = true;
        }

        private Vector3 ApplySpreadToDirection(Vector3 dir, WeaponType type)
        {
            // Keep projectile trajectories strictly coaxial along the weapon barrel forward vector.
            return dir;
        }

        public bool TryFire(bool triggerHeld, Vector3 flatAimDirection, Vector3? exactAimWorldPoint = null)
        {
            SetAimInput(flatAimDirection, exactAimWorldPoint);

            if (!triggerHeld)
            {
                _burstTriggerLatched = false;
                _an94HyperburstReady = true;
                if (CurrentWeapon == WeaponType.An94Abakan && QueuedFollowupShots == 0)
                {
                    _fireCooldown = Mathf.Min(_fireCooldown, 0.055f);
                }
                return false;
            }

            if (CurrentWeapon == WeaponType.M16A4Burst && (_burstTriggerLatched || QueuedFollowupShots > 0))
            {
                return false;
            }

            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, SaveManager.WeaponCount - 1);
            if (_reloadTimer > 0f)
            {
                if (GetWeaponReloadStyle(CurrentWeapon) == ReloadStyle.ShellByShellShotgun && _ammoInMag[wIdx] > 0)
                {
                    _reloadTimer = 0f;
                    _shotgunShellTimer = 0f;
                    _shotgunPumpPhase = false;
                    UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], GetEffectiveMagCapacity(wIdx), false, 1f);
                }
                else
                {
                    return false;
                }
            }

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
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], GetEffectiveMagCapacity(wIdx), false, 1f);

            SyncMuzzlePointToActiveTip();
            var spawnPos = GetMuzzlePosition(_lastAimDirection);
            var dir = ResolveMuzzleFireDirection(spawnPos, _lastAimDirection, exactAimWorldPoint);

            var dmgScale = _permanentDamageMult;
            var activeCfg = GetWeaponConfig(wIdx);
            float CfgInterval(float fallback) => activeCfg != null && activeCfg.fireInterval > 0.01f ? activeCfg.fireInterval : fallback;
            float CfgDmg(float fallback) => activeCfg != null && activeCfg.damage > 0.1f ? activeCfg.damage : fallback;
            float CfgSpd(float fallback) => activeCfg != null && activeCfg.projectileSpeed > 1f ? activeCfg.projectileSpeed : fallback;
            int CfgPellets(int fallback) => activeCfg != null && activeCfg.projectilesPerShot > 0 ? activeCfg.projectilesPerShot : fallback;

            switch (CurrentWeapon)
            {
                case WeaponType.ScatterShotgun:
                {
                    _fireCooldown = CfgInterval(0.48f);
                    var pellets = CfgPellets(8);
                    var totalArc = IsAimingDownSights ? 3.5f : 7.5f;
                    var pelletDmg = CfgDmg(19f) * dmgScale;
                    var spd = CfgSpd(34f);
                    for (var i = 0; i < pellets; i++)
                    {
                        var yaw = 0f;
                        if (i < pellets - 1 && pellets > 1)
                        {
                            var pairIdx = i / 2 + 1;
                            var sign = (i & 1) == 0 ? -1f : 1f;
                            yaw = sign * ((float)pairIdx / (pellets * 0.5f)) * (totalArc * 0.5f);
                        }
                        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir, Vector3.up);
                        SpawnBolt(spawnPos, rot, spd, pelletDmg, 0.62f, 0, 0f, Palette.Accent, new Vector3(0.024f, 0.28f, 0.024f), false);
                    }
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.9f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight, isShotgunShell: true, shotgunShellMesh, shotgunShellMaterial);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.16f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.09f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.014f, -0.092f), 11.5f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.ScatterShotgun, 0.98f, 0.68f);
                    break;
                }
                case WeaponType.PlasmaRailgun:
                {
                    _fireCooldown = CfgInterval(0.52f);
                    var slugDmg = CfgDmg(84f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.PlasmaRailgun);
                    SpawnBolt(spawnPos, Quaternion.LookRotation(shotDir, Vector3.up), CfgSpd(62f), slugDmg, 1.15f, 3, 0f, Palette.RailgunCyan, new Vector3(0.036f, 0.88f, 0.036f), IsAimingDownSights);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.55f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                    FeelManager.Instance?.TriggerCameraShake(0.20f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.09f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.012f, -0.088f), 10.5f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.PlasmaRailgun, 1.0f, 0.65f);
                    break;
                }
                case WeaponType.HellfireLauncher:
                {
                    _fireCooldown = CfgInterval(0.68f);
                    var rocketDmg = CfgDmg(70f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.HellfireLauncher);
                    var rot = Quaternion.LookRotation(shotDir, Vector3.up);
                    // PG-7VL 2-stage rocket: launches at 18 m/s with rear venturi backblast, accelerating in flight up to 44 m/s
                    SpawnBolt(spawnPos, rot, CfgSpd(18f), rocketDmg, 1.7f, 0, 2.95f * _blastScaleMult, Palette.RocketOrange, new Vector3(0.095f, 0.34f, 0.095f), false, 0f, true, acceleration: 32f, maxSpeed: 44f);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.65f);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RocketOrange);
                    VFXManager.Instance?.Play(VfxKind.RocketSmoke, spawnPos - shotDir * 0.90f, new Color(0.75f, 0.75f, 0.78f, 0.85f));
                    FeelManager.Instance?.TriggerCameraShake(0.22f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.10f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.015f, -0.105f), 12f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.HellfireLauncher, 0.88f, 0.70f);
                    break;
                }
                case WeaponType.An94Abakan:
                {
                    if (_an94HyperburstReady)
                    {
                        _an94HyperburstReady = false;
                        var burstRoundDmg = (CfgDmg(23f) + 5f) * dmgScale;
                        var shotDir = ApplySpreadToDirection(dir, WeaponType.An94Abakan);
                        var burstSpeed = CfgSpd(46f);
                        // Lock Round 1 vector so Round 2 leaves the reciprocating barrel 0.033s later on the exact same trajectory (before shoulder recoil acts)
                        _queuedFollowupWeapon = WeaponType.An94Abakan;
                        _queuedFollowupDirection = shotDir;
                        SpawnBolt(spawnPos, Quaternion.LookRotation(shotDir, Vector3.up), burstSpeed, burstRoundDmg, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.028f, 0.52f, 0.028f), false);
                        GetActiveEjectionPort(out var portPos, out var ejectRight);
                        DroppedMagazine.SpawnShellCasing(portPos, ejectRight);

                        QueuedFollowupShots = 1;
                        _queuedFollowupInterval = 0.033f;
                        _queuedFollowupTimer = _queuedFollowupInterval;
                        _queuedFollowupDamage = burstRoundDmg;
                        _fireCooldown = CfgInterval(0.133f);

                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                        characterMotion?.TriggerSquash(new Vector3(1.015f, 0.99f, 0.985f), 0.05f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.001f, -0.018f), 0f);
                        AudioManager.Instance?.PlayWeaponFire(WeaponType.An94Abakan, 1.04f, 0.56f);
                    }
                    else
                    {
                        var autoDmg = CfgDmg(23f) * dmgScale;
                        _fireCooldown = 0.100f;
                        var shotDir = ApplySpreadToDirection(dir, WeaponType.An94Abakan);
                        SpawnBolt(spawnPos, Quaternion.LookRotation(shotDir, Vector3.up), CfgSpd(42f), autoDmg, 1.35f, 0, 0f, Palette.Accent, new Vector3(0.028f, 0.48f, 0.028f), false);
                        CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.72f);
                        GetActiveEjectionPort(out var portPos, out var ejectRight);
                        DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                        VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                        FeelManager.Instance?.TriggerCameraShake(0.05f);
                        characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.98f), 0.06f);
                        characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.005f, -0.052f), 5.8f);
                        AudioManager.Instance?.PlayWeaponFire(WeaponType.An94Abakan, 0.96f, 0.50f);
                    }
                    break;
                }
                case WeaponType.VectorSmg:
                {
                    _fireCooldown = CfgInterval(0.050f);
                    var smgDmg = CfgDmg(16f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.VectorSmg);
                    var rot = Quaternion.LookRotation(shotDir, Vector3.up);
                    SpawnBolt(spawnPos, rot, CfgSpd(35f), smgDmg, 0.95f, 0, 0f, Palette.Accent, new Vector3(0.024f, 0.34f, 0.024f), false);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.52f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.025f);
                    characterMotion?.TriggerSquash(new Vector3(1.015f, 0.99f, 0.985f), 0.05f);
                    // KRISS Super V counter-mass drives bolt downward into grip -> minimal muzzle climb
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, -0.005f, -0.036f), 0.75f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.VectorSmg, 1.02f, 0.46f);
                    break;
                }
                case WeaponType.M249Saw:
                {
                    _fireCooldown = CfgInterval(0.082f);
                    _m249BeltCounter++;
                    var isTracer = (_m249BeltCounter % 5) == 0;
                    var sawDmg = CfgDmg(24f) * dmgScale * (isTracer ? 1.35f : 1.0f);
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.M249Saw);
                    var rot = Quaternion.LookRotation(shotDir, Vector3.up);
                    var tint = isTracer ? Palette.RocketOrange : Palette.Accent;
                    var visScale = isTracer ? new Vector3(0.042f, 0.62f, 0.042f) : new Vector3(0.032f, 0.46f, 0.032f);
                    SpawnBolt(spawnPos, rot, CfgSpd(40f), sawDmg, 1.45f, 1, 0f, tint, visScale, isTracer);
                    // Open-belt sustained bipod LMG stabilization: reverse bloom tightens as you walk fire on target
                    CurrentSpreadBloom = Mathf.Max(0.15f, CurrentSpreadBloom - 0.35f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, tint);
                    FeelManager.Instance?.TriggerCameraShake(0.065f);
                    characterMotion?.TriggerSquash(new Vector3(1.022f, 0.985f, 0.982f), 0.06f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.007f, -0.060f), 6.2f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.M249Saw, isTracer ? 1.04f : 0.96f, 0.58f);
                    break;
                }
                case WeaponType.M32Launcher:
                {
                    _fireCooldown = CfgInterval(0.46f);
                    var shellDmg = CfgDmg(62f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.M32Launcher);
                    var rot = Quaternion.LookRotation(shotDir, Vector3.up);
                    SpawnBolt(spawnPos, rot, CfgSpd(22f), shellDmg, 1.15f, 0, 3.35f * _blastScaleMult, Palette.C4Amber, new Vector3(0.09f, 0.24f, 0.09f), false, 8.8f, true);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.75f);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.C4Amber);
                    FeelManager.Instance?.TriggerCameraShake(0.18f);
                    characterMotion?.TriggerSquash(new Vector3(1.03f, 0.98f, 0.97f), 0.09f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.014f, -0.096f), 11.0f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.M32Launcher, 1.06f, 0.66f);
                    break;
                }
                case WeaponType.M16A4Burst:
                {
                    _burstTriggerLatched = true;
                    _fireCooldown = CfgInterval(0.24f);
                    var burstDmg = CfgDmg(30f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.M16A4Burst);
                    _queuedFollowupWeapon = WeaponType.M16A4Burst;
                    _queuedFollowupDirection = shotDir;
                    _queuedFollowupDamage = burstDmg;
                    QueuedFollowupShots = 2;
                    _queuedFollowupInterval = 0.044f;
                    _queuedFollowupTimer = _queuedFollowupInterval;

                    SpawnBolt(spawnPos, Quaternion.LookRotation(shotDir, Vector3.up), CfgSpd(52f), burstDmg, 1.50f, 0, 0f, Palette.Accent, new Vector3(0.028f, 0.54f, 0.028f), false);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.35f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.05f);
                    characterMotion?.TriggerSquash(new Vector3(1.02f, 0.99f, 0.985f), 0.05f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.045f), 3.8f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.M16A4Burst, 1.08f, 0.56f);
                    break;
                }
                case WeaponType.Xm25Airburst:
                {
                    _fireCooldown = CfgInterval(0.42f);
                    var smartDmg = CfgDmg(58f) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.Xm25Airburst);
                    var rot = Quaternion.LookRotation(shotDir, Vector3.up);
                    SpawnBolt(
                        spawnPos, rot, CfgSpd(32f), smartDmg, 1.65f, 0, 2.85f * _blastScaleMult,
                        Palette.RailgunCyan, new Vector3(0.068f, 0.30f, 0.068f), true,
                        gravityArc: 0f, hasSmokeTrail: true,
                        acceleration: 0f, maxSpeed: 0f,
                        ricochetCount: 1, proximityAirburstRadius: 2.15f);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.45f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                    FeelManager.Instance?.TriggerCameraShake(0.14f);
                    characterMotion?.TriggerSquash(new Vector3(1.025f, 0.985f, 0.98f), 0.08f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.010f, -0.082f), 8.2f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.Xm25Airburst, 1.18f, 0.64f);
                    break;
                }
                case WeaponType.PulseRifle:
                default: // [1] M4A1 Carbine (~800 RPM)
                {
                    _fireCooldown = CfgInterval(baseFireInterval);
                    var boltDmg = CfgDmg(baseDamage) * dmgScale;
                    var shotDir = ApplySpreadToDirection(dir, WeaponType.PulseRifle);
                    SpawnBolt(spawnPos, Quaternion.LookRotation(shotDir, Vector3.up), CfgSpd(projectileSpeed), boltDmg, 1.45f, 0, 0f, Palette.Accent, new Vector3(0.028f, 0.48f, 0.028f), false);
                    CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.70f);
                    GetActiveEjectionPort(out var portPos, out var ejectRight);
                    DroppedMagazine.SpawnShellCasing(portPos, ejectRight);
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
                    FeelManager.Instance?.TriggerCameraShake(0.07f);
                    characterMotion?.TriggerSquash(new Vector3(1.025f, 0.985f, 0.98f), 0.07f);
                    characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.006f, -0.058f), 6.2f);
                    AudioManager.Instance?.PlayWeaponFire(WeaponType.PulseRifle, 1.0f, 0.56f);
                    break;
                }
            }

            if (_ammoInMag[wIdx] <= 0 && QueuedFollowupShots == 0)
            {
                StartReload(quiet: false);
            }
            else
            {
                UpdateActiveWeaponMagVisual();
            }

            return true;
        }

        private void FireAn94FollowupRound()
        {
            var wIdx = Mathf.Clamp((int)CurrentWeapon, 0, MagazineCapacities.Length - 1);
            if (_ammoInMag[wIdx] <= 0)
            {
                QueuedFollowupShots = 0;
                StartReload(quiet: false);
                return;
            }
            _ammoInMag[wIdx]--;
            UIManager.Instance?.UpdateAmmoHud(_ammoInMag[wIdx], GetEffectiveMagCapacity(wIdx), _reloadTimer > 0f, ReloadProgress);

            SyncMuzzlePointToActiveTip();
            if (_queuedFollowupWeapon == WeaponType.M16A4Burst)
            {
                var roundDir = GetFlatBarrelForward();
                var spawnPos = GetMuzzlePosition(roundDir);
                var isFinalBurstRound = QueuedFollowupShots == 0;
                var roundDmg = isFinalBurstRound ? _queuedFollowupDamage * 1.25f : _queuedFollowupDamage;
                var roundPierce = isFinalBurstRound ? 1 : 0;
                var boltTint = isFinalBurstRound ? Palette.RailgunCyan : Palette.Accent;
                SpawnBolt(spawnPos, Quaternion.LookRotation(roundDir, Vector3.up), 52f, roundDmg, 1.50f, roundPierce, 0f, boltTint, new Vector3(0.028f, 0.54f, 0.028f), isFinalBurstRound);
                GetActiveEjectionPort(out var burstPort, out var burstRight);
                DroppedMagazine.SpawnShellCasing(burstPort, burstRight);
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, boltTint);
                FeelManager.Instance?.TriggerCameraShake(isFinalBurstRound ? 0.08f : 0.05f);
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.048f), isFinalBurstRound ? 5.5f : 3.5f);
                AudioManager.Instance?.PlayWeaponFire(WeaponType.M16A4Burst, isFinalBurstRound ? 1.14f : 1.09f, 0.54f);
            }
            else
            {
                // AN-94 2-round hyperburst: lock barrel & Round 2 onto Round 1's exact trajectory (0° deviation from barrel & Round 1)
                var lockedDir = _queuedFollowupDirection.sqrMagnitude > 0.001f ? _queuedFollowupDirection : GetFlatBarrelForward();
                transform.rotation = Quaternion.LookRotation(lockedDir, Vector3.up);
                characterMotion?.AlignWeaponBarrelWithBodyYaw();
                SyncMuzzlePointToActiveTip();
                var spawnPos = GetMuzzlePosition(lockedDir);
                SpawnBolt(spawnPos, Quaternion.LookRotation(lockedDir, Vector3.up), 46f, _queuedFollowupDamage, 1.35f, 0, 0f, Palette.RailgunCyan, new Vector3(0.028f, 0.52f, 0.028f), false);
                CurrentSpreadBloom = Mathf.Min(6.5f, CurrentSpreadBloom + 0.45f);
                GetActiveEjectionPort(out var followupPort, out var followupRight);
                DroppedMagazine.SpawnShellCasing(followupPort, followupRight);
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.RailgunCyan);
                FeelManager.Instance?.TriggerCameraShake(0.07f);
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.008f, -0.068f), 7.2f);
                AudioManager.Instance?.PlayWeaponFire(WeaponType.An94Abakan, 1.16f, 0.55f);
            }

            if (_ammoInMag[wIdx] <= 0 && QueuedFollowupShots == 0)
            {
                StartReload(quiet: false);
            }
        }

        private void SpawnBolt(
            Vector3 pos,
            Quaternion rot,
            float spd,
            float dmg,
            float lifetime,
            int pierce,
            float aoe,
            Color tint,
            Vector3 scale,
            bool crit,
            float gravityArc = 0f,
            bool hasSmokeTrail = false,
            float acceleration = 0f,
            float maxSpeed = 0f,
            int ricochetCount = 0,
            float proximityAirburstRadius = 0f)
        {
            TotalShotsFired++;
            LastFiredOrigin = pos;
            var fwd = rot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f) LastFiredDirection = fwd.normalized;
            var p = _projectilePool.Get();
            p.Initialize(_projectilePool, pos, rot, spd, dmg, lifetime, pierce, aoe, false, tint, scale, crit, gravityArc, hasSmokeTrail, acceleration, maxSpeed, ricochetCount, proximityAirburstRadius);
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
            _shotgunShellTimer = 0f;
            _shotgunPumpPhase = false;
            CurrentSpreadBloom = 0f;
            IsAimingDownSights = false;
            if (_tacticalSightLine != null) _tacticalSightLine.enabled = false;
            _an94HyperburstReady = true;
            _burstTriggerLatched = false;
            _reloadTimeMult = 1f;
            _magCapacityMult = 1f;
            _blastScaleMult = 1f;
            for (var i = 0; i < MagazineCapacities.Length; i++)
            {
                _ammoInMag[i] = GetEffectiveMagCapacity(i);
            }
            UpdateActiveWeaponMagVisual();
            DroppedMagazine.ReturnAllActive();
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
