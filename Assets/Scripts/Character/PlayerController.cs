using System.Collections;
using System.Collections.Generic;
using Audio;
using Camera;
using Combat;
using Core;
using Enemies;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Character
{
    public enum RogueliteSkillId
    {
        DamageBoost = 0,
        FireRateBoost = 1,
        MultiShot = 2,
        MaxHpUp = 3,
        MagnetSpeed = 4,
        DashCool = 5,
        VampiricSiphon = 6,
        CritPayload = 7
    }

    public readonly struct RogueliteSkillInfo
    {
        public readonly RogueliteSkillId Id;
        public readonly string Title;
        public readonly string Description;

        public RogueliteSkillInfo(RogueliteSkillId id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    /// <summary>
    /// Top-down player controller integrating hybrid PC (WASD/Mouse/Keys 1-4) and Mobile Twin-Stick
    /// Virtual Joystick controls, Meta Save upgrades, Armor Skins, and Roguelite Skill Draft scaling.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        private static readonly RogueliteSkillInfo[] SkillCatalog =
        {
            new(RogueliteSkillId.DamageBoost, "STOPPING POWER", "+18% All Ballistic Weapon Damage"),
            new(RogueliteSkillId.FireRateBoost, "DOUBLE TAP", "+16% Weapon Fire Rate"),
            new(RogueliteSkillId.MultiShot, "MULTI-BURST", "+1 Bonus Round Per Trigger Pull"),
            new(RogueliteSkillId.MaxHpUp, "JUGGERNAUT", "+25 Max Armor HP & Heal +25 HP"),
            new(RogueliteSkillId.MagnetSpeed, "LIGHTWEIGHT", "+0.45 Sprint Speed & +1.4m Pickup Radius"),
            new(RogueliteSkillId.DashCool, "SLEIGHT OF HAND", "-15% Tactical Sprint Cooldown"),
            new(RogueliteSkillId.VampiricSiphon, "QUICK FIX", "Restore +4 HP On Every Confirmed Kill"),
            new(RogueliteSkillId.CritPayload, "DEADSHOT", "+15% Chance For 1.65x Headshot Crit")
        };

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [Header("References")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private WeaponController weaponController;
        [SerializeField] private MeshRenderer[] bodyRenderers;
        [SerializeField] private MeshRenderer[] primarySkinRenderers;

        [Header("Stats")]
        [SerializeField] private Vector3 campaignSpawnPosition = new(0f, 0.02f, -43f);
        [SerializeField] private float baseMoveSpeed = 7.4f;
        [SerializeField] private float dashSpeed = 20f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float baseDashCooldown = 1.6f;
        [SerializeField] private float baseMaxHp = 100f;

        private InputAction _moveAction;
        private InputAction _attackAction;
        private InputAction _sprintAction;

        private float _currentMoveSpeed = 7.4f;
        private float _currentDashCooldown = 1.6f;
        private float _dashTimer;
        private float _dashCooldownTimer;
        private Vector3 _dashDirection;
        private float _invulnTimer;
        private float _damageFlashTimer;
        private float _timeSinceLastDamage = 10f;
        private const int MaxFragGrenades = 4;
        private float _grenadeCooldownTimer;
        private float _prevYaw;

        private readonly int[] _skillRanks = new int[8];
        private MaterialPropertyBlock _mpb;
        private UnityEngine.Camera _mainCamera;
        private UnityEngine.Pool.ObjectPool<GameObject> _fragGrenadePool;
        private Material _fragGrenadeMat;
        private ColorAdjustments _colorAdjustments;
        private float _basePostExposure;
        private float _baseContrast;
        private float _baseSaturation;
        private Color _baseColorFilter = Palette.ButtonTintIdle;
        private bool _hasCapturedBaseColorGrading;

        private static readonly Collider[] BlastOverlapBuffer = new Collider[32];
        private static readonly HashSet<EnemyController> BlastDamagedBuffer = new(16);

        // Deterministic verification & automated demo hooks
        private bool _hasSimulatedInput;
        private Vector2 _simulatedMove;
        private Vector3? _simulatedAimPoint;
        private bool _simulatedFire;
        private bool _simulatedDash;
        private bool _autoPlayBot;
        private float _botAngle;
        private Vector3? _latestAimWorldTarget;

        public float CurrentHp { get; private set; } = 100f;

        public float MaxHp { get; private set; } = 100f;

        private int CurrentXp { get; set; }

        private int XpToNextLevel { get; set; } = 100;

        private int CurrentLevel { get; set; } = 1;

        public bool IsDead { get; private set; }

        public int FragGrenades { get; private set; } = 3;

        public int KillStreakCount { get; private set; }

        public bool IsUavReconActive { get; private set; }

        public int MortarStrikeCharges { get; private set; }

        public bool IsRegeneratingHealth { get; private set; }

        public bool IsNvgActive { get; private set; }

        public ProceduralCharacterMotion Motion => characterMotion;
        public WeaponController Weapon => weaponController;
        public float PickupMagnetBonus { get; private set; }
        public float CreditMultiplier { get; private set; } = 1f;

        public void BindComponents(
            InputActionAsset actions,
            CharacterController cc,
            ProceduralCharacterMotion motion,
            WeaponController weapon,
            MeshRenderer[] renderers,
            MeshRenderer[] primaryArmorParts = null)
        {
            inputActions = actions;
            characterController = cc;
            characterMotion = motion;
            weaponController = weapon;
            bodyRenderers = renderers;
            primarySkinRenderers = primaryArmorParts;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (characterMotion == null) characterMotion = GetComponent<ProceduralCharacterMotion>();
            if (weaponController == null) weaponController = GetComponent<WeaponController>();

            EnsurePhysicsTriggers();
            BindInputActions();
            _mainCamera = UnityEngine.Camera.main;
            InitializeGrenadePool();
            CacheColorAdjustments();

            MaxHp = baseMaxHp;
            CurrentHp = MaxHp;
            _prevYaw = transform.eulerAngles.y;
        }

        private void InitializeGrenadePool()
        {
            if (_fragGrenadePool != null) return;
            _fragGrenadeMat = Palette.CreateLitMaterial("Mat_FragGrenadeRuntime", Palette.NvgGreen, 0.25f, 0.45f);
            _fragGrenadePool = new UnityEngine.Pool.ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "FragGrenade_Pooled";
                    go.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);
                    var c = go.GetComponent<Collider>();
                    if (c != null) Destroy(c);
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = _fragGrenadeMat;
                    go.SetActive(false);
                    return go;
                },
                actionOnGet: go => { if (go != null) go.SetActive(true); },
                actionOnRelease: go => { if (go != null) go.SetActive(false); },
                actionOnDestroy: go => { if (go != null) Destroy(go); },
                collectionCheck: false,
                defaultCapacity: 4,
                maxSize: 10
            );
        }

        private void CacheColorAdjustments()
        {
            if (_colorAdjustments != null) return;
            var volume = FindAnyObjectByType<Volume>();
            if (volume == null || volume.profile == null) return;
            if (!volume.profile.TryGet(out _colorAdjustments) || _hasCapturedBaseColorGrading) return;
            _basePostExposure = _colorAdjustments.postExposure.value;
            _baseContrast = _colorAdjustments.contrast.value;
            _baseSaturation = _colorAdjustments.saturation.value;
            _baseColorFilter = _colorAdjustments.colorFilter.value;
            _hasCapturedBaseColorGrading = true;
        }

        public void ToggleNightVision(bool? forceState = null, bool quiet = false)
        {
            var next = forceState ?? !IsNvgActive;
            if (next == IsNvgActive) return;

            IsNvgActive = next;
            CacheColorAdjustments();
            if (_colorAdjustments != null)
            {
                if (IsNvgActive)
                {
                    _colorAdjustments.colorFilter.Override(Palette.NvgPhosphor);
                    _colorAdjustments.postExposure.Override(_basePostExposure + 0.52f);
                    _colorAdjustments.contrast.Override(_baseContrast + 16f);
                    _colorAdjustments.saturation.Override(-45f);
                }
                else
                {
                    _colorAdjustments.colorFilter.Override(_baseColorFilter);
                    _colorAdjustments.postExposure.Override(_basePostExposure);
                    _colorAdjustments.contrast.Override(_baseContrast);
                    _colorAdjustments.saturation.Override(_baseSaturation);
                }
            }
            UIManager.Instance?.SetNvgActive(IsNvgActive);
            if (!quiet)
            {
                AudioManager.Instance?.PlayNvgToggle(IsNvgActive);
            }
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _attackAction?.Enable();
            _sprintAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _attackAction?.Disable();
            _sprintAction?.Disable();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            FeelManager.Instance?.SetPlayerRoot(transform);
            TopDownCameraRig.Instance?.SetPlayerTarget(transform);
            ApplySaveLoadout();
            SyncHud();
        }

        private void EnsurePhysicsTriggers()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            var pickupTrigger = GetComponent<SphereCollider>();
            if (pickupTrigger == null)
            {
                pickupTrigger = gameObject.AddComponent<SphereCollider>();
            }
            pickupTrigger.isTrigger = true;
            pickupTrigger.radius = 0.88f;
            pickupTrigger.center = new Vector3(0f, 0.88f, 0f);
        }

        private void BindInputActions()
        {
            if (inputActions != null)
            {
                var playerMap = inputActions.FindActionMap("Player");
                if (playerMap != null)
                {
                    _moveAction = playerMap.FindAction("Move");
                    _attackAction = playerMap.FindAction("Attack");
                    _sprintAction = playerMap.FindAction("Sprint");
                }
            }

            if (_moveAction == null)
            {
                _moveAction = new InputAction("Move");
                _moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
            }
            _attackAction ??= new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            _sprintAction ??= new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
        }

        public void SetSimulatedInput(Vector2 move, Vector3? aimWorldPoint, bool fire, bool dash = false)
        {
            _hasSimulatedInput = true;
            _simulatedMove = Vector2.ClampMagnitude(move, 1f);
            _simulatedAimPoint = aimWorldPoint;
            _simulatedFire = fire;
            _simulatedDash = dash;
        }

        public void ClearSimulatedInput()
        {
            _hasSimulatedInput = false;
            _simulatedMove = Vector2.zero;
            _simulatedAimPoint = null;
            _simulatedFire = false;
            _simulatedDash = false;
        }

        public void SetAutoPlayBot(bool enabled)
        {
            _autoPlayBot = enabled;
        }

        public void ApplySaveLoadout()
        {
            var save = SaveManager.EnsureInstance();
            var d = save.Data;

            var permMaxHp = baseMaxHp + d.armorRank * 20f;
            var skillHp = _skillRanks[(int)RogueliteSkillId.MaxHpUp] * 25f;
            MaxHp = permMaxHp + skillHp;
            CurrentHp = Mathf.Min(CurrentHp > 0f ? CurrentHp : MaxHp, MaxHp);

            if (weaponController != null)
            {
                weaponController.EquipWeapon((WeaponType)Mathf.Clamp(d.selectedWeapon, 0, SaveManager.WeaponCount - 1));
            }
            RecalculateDerivedStats();
            ApplySkinTint(d.selectedSkin);
            UIManager.Instance?.RefreshWeaponBar();
        }

        private void RecalculateDerivedStats()
        {
            var save = SaveManager.EnsureInstance();
            var d = save.Data;

            var speedRank = _skillRanks[(int)RogueliteSkillId.MagnetSpeed];
            var dashRank = _skillRanks[(int)RogueliteSkillId.DashCool];
            var dmgRank = _skillRanks[(int)RogueliteSkillId.DamageBoost];
            var rateRank = _skillRanks[(int)RogueliteSkillId.FireRateBoost];
            var multiRank = _skillRanks[(int)RogueliteSkillId.MultiShot];
            var critRank = _skillRanks[(int)RogueliteSkillId.CritPayload];

            var weaponSpeedMult = weaponController != null ? weaponController.MoveSpeedMultiplier : 1f;
            _currentMoveSpeed = (baseMoveSpeed + d.thrusterRank * 0.35f + speedRank * 0.45f) * weaponSpeedMult;
            var permCooldownMult = Mathf.Max(0.55f, 1f - d.thrusterRank * 0.08f);
            var skillCooldownMult = Mathf.Pow(0.85f, dashRank);
            _currentDashCooldown = Mathf.Max(0.45f, baseDashCooldown * permCooldownMult * skillCooldownMult);

            PickupMagnetBonus = d.greedRank * 0.35f + speedRank * 1.4f;
            CreditMultiplier = 1f + d.greedRank * 0.18f;

            var permDmgMult = 1f + d.firepowerRank * 0.12f;
            var skillDmgMult = 1f + dmgRank * 0.18f;
            var skillRateMult = 1f + rateRank * 0.16f;
            var critChance = Mathf.Min(0.75f, critRank * 0.15f);

            weaponController?.SetStatModifiers(permDmgMult, skillDmgMult, skillRateMult, multiRank, critChance);
        }

        private void ApplySkinTint(int skinId)
        {
            var skinCol = skinId switch
            {
                1 => Palette.SkinGoldPrimary,
                2 => Palette.SkinCrimsonPrimary,
                _ => Palette.CharacterPrimary
            };

            if (primarySkinRenderers == null) return;
            _mpb ??= new MaterialPropertyBlock();

            foreach (var t in primarySkinRenderers)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, skinCol);
                t.SetPropertyBlock(_mpb);
            }
        }

        public bool TrySwitchWeapon(int weaponIndex)
        {
            if (weaponIndex is < 0 or >= SaveManager.WeaponCount) return false;
            var save = SaveManager.EnsureInstance();
            if (!save.IsWeaponUnlocked(weaponIndex)) return false;

            save.Data.selectedWeapon = weaponIndex;
            save.Save();
            weaponController?.EquipWeapon((WeaponType)weaponIndex);
            RecalculateDerivedStats();
            AudioManager.Instance?.PlayUIClick();
            UIManager.Instance?.RefreshWeaponBar();
            return true;
        }

        public int GetSkillRank(RogueliteSkillId skill)
        {
            return _skillRanks[(int)skill];
        }

        public void ApplyRogueliteSkill(RogueliteSkillId skill)
        {
            var idx = (int)skill;
            _skillRanks[idx]++;

            if (skill == RogueliteSkillId.MaxHpUp)
            {
                MaxHp += 25f;
                CurrentHp = Mathf.Min(MaxHp, CurrentHp + 25f);
            }

            RecalculateDerivedStats();
            SyncHud();
            AudioManager.Instance?.PlayMilestoneSting();
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.6f, SkillCatalog[idx].Title, Palette.Accent);
        }

        public List<RogueliteSkillInfo> RollDraftOptions(int count = 3)
        {
            var pool = new List<int>(SkillCatalog.Length);
            for (var i = 0; i < SkillCatalog.Length; i++) pool.Add(i);

            var picks = new List<RogueliteSkillInfo>(count);
            for (var i = 0; i < count && pool.Count > 0; i++)
            {
                var randIdx = Random.Range(0, pool.Count);
                picks.Add(SkillCatalog[pool[randIdx]]);
                pool.RemoveAt(randIdx);
            }
            return picks;
        }

        public void OnEnemyKilled()
        {
            KillStreakCount++;
            switch (KillStreakCount)
            {
                case 3 when !IsUavReconActive:
                    IsUavReconActive = true;
                    StoryDirector.NotifyKillstreakReady("3-Kill Streak: UAV Recon Online — all sector hostiles sweeping on tactical radar!");
                    UIManager.Instance?.ShowMilestoneBanner("KILLSTREAK // UAV RECON ONLINE");
                    AudioManager.Instance?.PlayMilestoneSting();
                    break;
                case >= 5 when KillStreakCount % 5 == 0 && MortarStrikeCharges < 2:
                    MortarStrikeCharges++;
                    StoryDirector.NotifyKillstreakReady($"{KillStreakCount}-Kill Streak: Mortar Strike ready — press [F] for danger-close barrage!");
                    UIManager.Instance?.ShowMilestoneBanner("KILLSTREAK // MORTAR STRIKE [F]");
                    AudioManager.Instance?.PlayMilestoneSting();
                    break;
            }

            var siphonRank = _skillRanks[(int)RogueliteSkillId.VampiricSiphon];
            if (siphonRank > 0 && !IsDead)
            {
                Heal(4f * siphonRank);
            }
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: true);
                weaponController?.SyncActiveMuzzle();
                UIManager.Instance?.UpdateDamageVignette(0f);
                return;
            }
            if (IsDead)
            {
                weaponController?.SyncActiveMuzzle();
                UIManager.Instance?.UpdateDamageVignette(0f);
                return;
            }

            CheckWeaponHotkeys();

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_invulnTimer > 0f) _invulnTimer -= dt;
            if (_grenadeCooldownTimer > 0f) _grenadeCooldownTimer = Mathf.Max(0f, _grenadeCooldownTimer - dt);
            if (_dashCooldownTimer > 0f)
            {
                _dashCooldownTimer = Mathf.Max(0f, _dashCooldownTimer - dt);
                UIManager.Instance?.UpdateDashCooldown(1f - (_dashCooldownTimer / _currentDashCooldown));
            }

            if (_damageFlashTimer > 0f)
            {
                _damageFlashTimer -= dt;
                ApplyDamageFlash(Mathf.Clamp01(_damageFlashTimer / 0.14f));
            }

            // CoD Regenerating Health after 3.5s out of enemy fire + Crimson edge vignette
            _timeSinceLastDamage += dt;
            if (_timeSinceLastDamage >= 3.5f && CurrentHp < MaxHp)
            {
                IsRegeneratingHealth = true;
                CurrentHp = Mathf.Min(MaxHp, CurrentHp + 22f * dt);
                UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
            }
            else
            {
                IsRegeneratingHealth = false;
            }

            var lowHpFactor = MaxHp > 0f ? Mathf.Clamp01((0.68f - CurrentHp / MaxHp) / 0.68f) : 0f;
            var hitPulse = Mathf.Clamp01(_damageFlashTimer / 0.14f);
            var vignetteAlpha = Mathf.Clamp01(Mathf.Max(hitPulse * 0.72f, lowHpFactor * (0.60f + 0.18f * Mathf.Sin(Time.time * 5.5f))));
            UIManager.Instance?.UpdateDamageVignette(vignetteAlpha);
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);

            if (Keyboard.current != null)
            {
                if (Keyboard.current.gKey.wasPressedThisFrame) TryThrowFragGrenade();
                if (Keyboard.current.fKey.wasPressedThisFrame) TryCallMortarStrike();
                if (Keyboard.current.rKey.wasPressedThisFrame) weaponController?.StartReload();
                if (Keyboard.current.nKey.wasPressedThisFrame) ToggleNightVision();
            }

            ReadInputs(out var moveVec, out var aimDir, out var fireHeld, out var dashPressed, out var aimWorldTarget);
            _latestAimWorldTarget = aimWorldTarget;

            // Dash activation
            if (dashPressed && _dashTimer <= 0f && _dashCooldownTimer <= 0f)
            {
                var wishMove = new Vector3(moveVec.x, 0f, moveVec.y);
                _dashDirection = wishMove.sqrMagnitude > 0.01f ? wishMove.normalized : aimDir;
                _dashTimer = dashDuration;
                _dashCooldownTimer = _currentDashCooldown;
                _invulnTimer = dashDuration + 0.08f;
                AudioManager.Instance?.PlayDash();
                characterMotion?.TriggerSquash(new Vector3(0.84f, 1.18f, 0.84f), 0.16f);
                UIManager.Instance?.UpdateDashCooldown(0f);
            }

            // Velocity computation
            Vector3 horizontalVelocity;
            if (_dashTimer > 0f)
            {
                _dashTimer -= dt;
                horizontalVelocity = _dashDirection * dashSpeed;
            }
            else
            {
                var moveDir = new Vector3(moveVec.x, 0f, moveVec.y);
                if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
                horizontalVelocity = moveDir * _currentMoveSpeed;
            }

            if (characterController != null && characterController.enabled)
            {
                characterController.Move((horizontalVelocity + Vector3.down * 6f) * dt);
            }
            else
            {
                transform.position += horizontalVelocity * dt;
            }

            // Aim orientation & camera look-ahead (snap body yaw on fire so gun barrel aligns 1:1 with bullet)
            if (aimDir.sqrMagnitude > 0.001f)
            {
                var targetRot = Quaternion.LookRotation(aimDir, Vector3.up);
                transform.rotation = fireHeld
                    ? targetRot
                    : Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-45f * dt));
                TopDownCameraRig.Instance?.UpdateAimLookAhead(aimDir);
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            characterMotion?.StepMotion(horizontalVelocity, yawRate, isAimingWeapon: true);
            weaponController?.TryFire(fireHeld, aimDir, aimWorldTarget);
        }

        private void LateUpdate()
        {
            weaponController?.SyncActiveMuzzle();
        }

        private void CheckWeaponHotkeys()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) TrySwitchWeapon(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) TrySwitchWeapon(1);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) TrySwitchWeapon(2);
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) TrySwitchWeapon(3);
            else if (Keyboard.current.digit5Key.wasPressedThisFrame) TrySwitchWeapon(4);
            else if (Keyboard.current.digit6Key.wasPressedThisFrame) TrySwitchWeapon(5);
        }

        private Vector3 ComputeAimDirectionToWorldTarget(Vector3 worldTarget, Transform muzzleTf)
        {
            var centerToTarget = worldTarget - transform.position;
            centerToTarget.y = 0f;
            var dist = centerToTarget.magnitude;
            if (dist < 0.05f) return transform.forward;

            var lateralOffset = muzzleTf != null ? transform.InverseTransformPoint(muzzleTf.position).x : 0.11f;
            var bearingRad = Mathf.Atan2(centerToTarget.x, centerToTarget.z);
            if (!(dist > Mathf.Abs(lateralOffset) + 0.55f)) return centerToTarget / dist;
            var parallaxRad = Mathf.Asin(Mathf.Clamp(lateralOffset / dist, -0.55f, 0.55f));
            var correctedRad = bearingRad - parallaxRad;
            return new Vector3(Mathf.Sin(correctedRad), 0f, Mathf.Cos(correctedRad));
        }

        private void ReadInputs(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed, out Vector3? aimWorldTarget)
        {
            aimWorldTarget = null;
            var muzzleTf = weaponController != null ? weaponController.MuzzleTransform : null;

            if (_hasSimulatedInput)
            {
                moveVec = _simulatedMove;
                fireHeld = _simulatedFire;
                dashPressed = _simulatedDash;
                _simulatedDash = false;
                if (_simulatedAimPoint.HasValue)
                {
                    aimWorldTarget = _simulatedAimPoint.Value;
                    aimDir = ComputeAimDirectionToWorldTarget(_simulatedAimPoint.Value, muzzleTf);
                }
                else
                {
                    aimDir = transform.forward;
                }
                return;
            }

            if (_autoPlayBot)
            {
                StepAutoBotInput(out moveVec, out aimDir, out fireHeld, out dashPressed);
                return;
            }

            // Hybrid PC (WASD + Mouse) + Mobile Twin-Stick Touch Joysticks
            moveVec = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
            var leftStick = UIManager.Instance?.LeftJoystick;
            if (leftStick != null && (leftStick.IsHeld || leftStick.Value.sqrMagnitude > 0.01f))
            {
                moveVec = leftStick.Value;
            }

            var touchDash = UIManager.Instance != null && UIManager.Instance.ConsumeTouchDashPressed();
            dashPressed = touchDash || (_sprintAction != null && _sprintAction.WasPressedThisFrame());

            var rightStick = UIManager.Instance?.RightJoystick;
            if (rightStick != null && (rightStick.IsHeld || rightStick.Value.sqrMagnitude > 0.04f))
            {
                var aim2D = rightStick.Value;
                aimDir = aim2D.sqrMagnitude > 0.01f
                    ? new Vector3(aim2D.x, 0f, aim2D.y).normalized
                    : transform.forward;
                fireHeld = true;
                return;
            }

            fireHeld = _attackAction != null && _attackAction.IsPressed();

            // Raycast PC mouse cursor onto exact active muzzle elevation plane so crosshair and bullet trajectory align 1:1
            aimDir = transform.forward;
            if (_mainCamera == null) _mainCamera = UnityEngine.Camera.main;
            var cam = _mainCamera;
            if (cam != null && Mouse.current != null)
            {
                var screenPos = Mouse.current.position.ReadValue();
                var ray = cam.ScreenPointToRay(screenPos);
                var muzzleY = muzzleTf != null ? muzzleTf.position.y : (transform.position.y + 1.18f);
                var muzzlePlane = new Plane(Vector3.up, new Vector3(0f, muzzleY, 0f));
                if (!muzzlePlane.Raycast(ray, out var enter)) return;
                var hitWorld = ray.GetPoint(enter);
                aimWorldTarget = hitWorld;
                aimDir = ComputeAimDirectionToWorldTarget(hitWorld, muzzleTf);
            }
            else if (moveVec.sqrMagnitude > 0.01f)
            {
                aimDir = new Vector3(moveVec.x, 0f, moveVec.y).normalized;
            }
        }

        private void StepAutoBotInput(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed)
        {
            _botAngle += Time.deltaTime * 1.1f;
            var desiredOrbit = new Vector3(Mathf.Cos(_botAngle) * 4.5f, 0f, Mathf.Sin(_botAngle) * 4.5f);
            var toOrbit = desiredOrbit - new Vector3(transform.position.x, 0f, transform.position.z);
            moveVec = toOrbit.sqrMagnitude > 0.25f ? new Vector2(toOrbit.x, toOrbit.z).normalized : Vector2.zero;

            var activeEnemies = EnemySpawner.Instance != null ? EnemySpawner.Instance.ActiveEnemies : null;
            if (activeEnemies is { Count: > 0 } && activeEnemies[0] != null && !activeEnemies[0].IsDead)
            {
                var toEnemy = activeEnemies[0].transform.position - transform.position;
                toEnemy.y = 0f;
                aimDir = toEnemy.sqrMagnitude > 0.01f ? toEnemy.normalized : transform.forward;
                fireHeld = true;
            }
            else
            {
                aimDir = toOrbit.sqrMagnitude > 0.05f ? toOrbit.normalized : transform.forward;
                fireHeld = false;
            }
            dashPressed = false;
        }

        public void AddFragGrenades(int count)
        {
            if (count <= 0) return;
            FragGrenades = Mathf.Clamp(FragGrenades + count, 0, MaxFragGrenades);
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);
        }

        public bool TryThrowFragGrenade()
        {
            if (IsDead || FragGrenades <= 0 || _grenadeCooldownTimer > 0f) return false;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return false;

            FragGrenades--;
            _grenadeCooldownTimer = 0.75f;
            var start = transform.position + Vector3.up * 1.25f + transform.forward * 0.45f;
            var aimTarget = _latestAimWorldTarget ?? (transform.position + transform.forward * 10.5f);
            var flat = aimTarget - transform.position;
            flat.y = 0f;
            var dist = Mathf.Clamp(flat.magnitude, 3.2f, 15.5f);
            var dir = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
            var end = new Vector3(transform.position.x + dir.x * dist, 0.14f, transform.position.z + dir.z * dist);

            StartCoroutine(FragGrenadeArcRoutine(start, end));
            AudioManager.Instance?.PlayDash();
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);
            return true;
        }

        private IEnumerator FragGrenadeArcRoutine(Vector3 start, Vector3 target)
        {
            InitializeGrenadePool();
            var gren = _fragGrenadePool.Get();
            if (gren == null) yield break;

            const float flightTime = 0.62f;
            var elapsed = 0f;
            while (elapsed < flightTime)
            {
                if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing && GameManager.Instance.CurrentState != GameState.Cutscene)
                {
                    yield return null;
                    continue;
                }
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / flightTime);
                var pos = Vector3.Lerp(start, target, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * 2.7f;
                gren.transform.position = pos;
                yield return null;
            }

            gren.transform.position = target;
            yield return Ease.GetWait(0.24f);
            _fragGrenadePool.Release(gren);

            DetonateTacticalBlast(target, 4.8f, 145f, Palette.RocketOrange, "FRAG OUT!");
        }

        public bool TryCallMortarStrike()
        {
            if (IsDead || MortarStrikeCharges <= 0) return false;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return false;

            MortarStrikeCharges--;
            var center = _latestAimWorldTarget ?? (transform.position + transform.forward * 12f);
            center.y = 0f;
            StartCoroutine(MortarBarrageRoutine(center));
            UIManager.Instance?.ShowMilestoneBanner("DANGER CLOSE // MORTAR BARRAGE INBOUND");
            AudioManager.Instance?.PlayMilestoneSting();
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);
            return true;
        }

        private IEnumerator MortarBarrageRoutine(Vector3 center)
        {
            Vector3[] offsets = { Vector3.zero, new(-3.2f, 0f, 2.4f), new(3.2f, 0f, -2.0f) };
            foreach (var t in offsets)
            {
                var impact = center + t;
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, impact + Vector3.up * 0.4f, Palette.C4Amber);
                yield return Ease.GetWait(0.32f);
                DetonateTacticalBlast(impact, 5.6f, 185f, Palette.RocketOrange, "SPLASH!");
            }
        }

        private void DetonateTacticalBlast(Vector3 center, float radius, float dmg, Color vfxColor, string label)
        {
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center + Vector3.up * 0.6f, vfxColor);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center + Vector3.up * 0.4f, Palette.C4Amber);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(0.42f);

            var hitCount = Physics.OverlapSphereNonAlloc(center, radius, BlastOverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            BlastDamagedBuffer.Clear();
            var anyHit = false;
            var anyKill = false;
            for (var i = 0; i < hitCount; i++)
            {
                var t = BlastOverlapBuffer[i];
                if (t == null) continue;

                var barrel = t.GetComponentInParent<ExplosiveBarrel>();
                if (barrel != null && !barrel.IsDetonated)
                {
                    if (barrel.ApplyDamage(dmg)) anyKill = true;
                    anyHit = true;
                    continue;
                }

                var enemy = t.GetComponentInParent<EnemyController>();
                if (enemy == null || enemy.IsDead || !BlastDamagedBuffer.Add(enemy)) continue;

                anyHit = true;
                var wasDead = enemy.IsDead;
                var pushDir = enemy.transform.position - center;
                pushDir.y = 0f;
                enemy.ApplyDamage(dmg, enemy.transform.position + Vector3.up * 0.9f, pushDir.sqrMagnitude > 0.01f ? pushDir.normalized : Vector3.forward, isCrit: true);
                if (!wasDead && enemy.IsDead)
                {
                    anyKill = true;
                    FeelManager.Instance?.TriggerKillFeed("PRICE", enemy.DisplayCallsign, "ORDNANCE");
                }
            }
            if (anyHit)
            {
                FeelManager.Instance?.TriggerHitmarker(anyKill);
            }
            if (!string.IsNullOrEmpty(label))
            {
                FeelManager.Instance?.SpawnFloatingText(center + Vector3.up * 0.8f, label, Palette.C4Amber);
            }
        }

        public void AddXp(int amount)
        {
            if (IsDead || amount <= 0) return;
            CurrentXp += amount;

            while (CurrentXp >= XpToNextLevel)
            {
                CurrentXp -= XpToNextLevel;
                CurrentLevel++;
                XpToNextLevel = Mathf.RoundToInt(XpToNextLevel * 1.35f);
                weaponController?.SetWeaponTier(CurrentLevel);
                FeelManager.Instance?.TriggerLevelUpSequence(CurrentLevel, transform);
            }

            UIManager.Instance?.UpdateXp(CurrentXp, XpToNextLevel, CurrentLevel);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
        }

        public void TakeDamage(float dmg)
        {
            if (IsDead || _invulnTimer > 0f || dmg <= 0f) return;

            _timeSinceLastDamage = 0f;
            IsRegeneratingHealth = false;
            CurrentHp = Mathf.Max(0f, CurrentHp - dmg);
            _invulnTimer = 0.28f;
            _damageFlashTimer = 0.14f;
            ApplyDamageFlash(1f);

            characterMotion?.TriggerSquash(new Vector3(1.24f, 0.78f, 1.24f), 0.14f);
            FeelManager.Instance?.TriggerCameraShake(0.38f);
            FeelManager.Instance?.SpawnFloatingText(transform.position, "-" + Mathf.RoundToInt(dmg), Palette.EnemySecondary);
            AudioManager.Instance?.PlayHit();
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);

            if (!(CurrentHp <= 0f)) return;
            IsDead = true;
            IsUavReconActive = false;
            KillStreakCount = 0;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, transform.position + Vector3.up * 0.9f, Palette.EnemySecondary);
            AudioManager.Instance?.PlayEnemyExplode();
            UIManager.Instance?.UpdateDamageVignette(0f);
            GameManager.Instance?.EndMission(false);
        }

        public void SetSpawnPosition(Vector3 pos)
        {
            campaignSpawnPosition = pos;
        }

        public void TeleportTo(Vector3 pos)
        {
            if (characterController != null)
            {
                characterController.enabled = false;
                transform.position = pos;
                characterController.enabled = true;
            }
            else
            {
                transform.position = pos;
            }
            TopDownCameraRig.Instance?.SnapToPlayerImmediate();
        }

        public void ResetPlayer()
        {
            IsDead = false;
            CurrentXp = 0;
            XpToNextLevel = 100;
            CurrentLevel = 1;
            _dashTimer = 0f;
            _dashCooldownTimer = 0f;
            _invulnTimer = 0f;
            _damageFlashTimer = 0f;
            _timeSinceLastDamage = 10f;
            IsRegeneratingHealth = false;
            FragGrenades = 3;
            KillStreakCount = 0;
            IsUavReconActive = false;
            MortarStrikeCharges = 0;
            _grenadeCooldownTimer = 0f;
            transform.localScale = Vector3.one;

            for (var i = 0; i < _skillRanks.Length; i++)
            {
                _skillRanks[i] = 0;
            }

            var save = SaveManager.EnsureInstance();
            MaxHp = baseMaxHp + save.Data.armorRank * 20f;
            CurrentHp = MaxHp;

            if (characterController != null)
            {
                characterController.enabled = false;
                transform.SetPositionAndRotation(campaignSpawnPosition, Quaternion.identity);
                characterController.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(campaignSpawnPosition, Quaternion.identity);
            }

            ToggleNightVision(false, quiet: true);
            ExplosiveBarrel.ResetAllBarrels();
            weaponController?.ReturnAllActive();
            ApplySaveLoadout();
            ApplyDamageFlash(0f);
            ClearSimulatedInput();
            characterMotion?.ResetMotionState();
            SyncHud();
        }

        private void SyncHud()
        {
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
            UIManager.Instance?.UpdateXp(CurrentXp, XpToNextLevel, CurrentLevel);
            UIManager.Instance?.UpdateDashCooldown(1f);
            UIManager.Instance?.RefreshWeaponBar();
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth);
            UIManager.Instance?.UpdateDamageVignette(0f);
        }

        private void ApplyDamageFlash(float intensity)
        {
            if (bodyRenderers == null) return;
            _mpb ??= new MaterialPropertyBlock();

            var flash = Palette.Lerp(Palette.Shadow, Palette.EnemySecondary, intensity) * (intensity * 1.6f);
            foreach (var t in bodyRenderers)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, flash);
                t.SetPropertyBlock(_mpb);
            }
        }
    }
}
