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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Character
{
    /// <summary>
    /// Top-down player controller integrating PC controls (WASD/Mouse/Hotkeys 1-8),
    /// tactical ordnance/med-plate systems, and Armory progression scaling.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

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

        private float _blastRadiusMult = 1f;
        private float _blastDamageMult = 1f;
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

        private Vector3? _latestAimWorldTarget;

        public float CurrentHp { get; private set; } = 100f;

        public float MaxHp { get; private set; } = 100f;

        public float CurrentArmor { get; private set; } = 50f;

        public float MaxArmor { get; private set; } = 50f;

        public int TraumaKits { get; private set; } = 2;
        private const int MaxTraumaKits = 4;

        private float _armorCooldownTimer;
        private float _footstepDistanceAccum;
        private bool _simAimDownSights;

        public bool IsDead { get; private set; }
        public bool IsAimingDownSights { get; private set; }

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

        public void ApplySaveLoadout()
        {
            var save = SaveManager.EnsureInstance();
            var d = save.Data;

            MaxHp = baseMaxHp + d.armorRank * 20f;
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

            MaxArmor = 50f + d.armorRank * 15f;
            CurrentArmor = Mathf.Min(CurrentArmor, MaxArmor);

            var weaponSpeedMult = weaponController != null ? weaponController.MoveSpeedMultiplier : 1f;
            _currentMoveSpeed = (baseMoveSpeed + d.thrusterRank * 0.35f) * weaponSpeedMult;
            var permCooldownMult = Mathf.Max(0.55f, 1f - d.thrusterRank * 0.08f);
            _currentDashCooldown = Mathf.Max(0.45f, baseDashCooldown * permCooldownMult);

            PickupMagnetBonus = d.greedRank * 0.35f;
            CreditMultiplier = 1f + d.greedRank * 0.18f;

            var permDmgMult = 1f + d.firepowerRank * 0.12f;
            var magCapMult = 1f + d.firepowerRank * 0.08f;
            _blastRadiusMult = 1f + d.firepowerRank * 0.06f;
            _blastDamageMult = 1f + d.firepowerRank * 0.08f;

            weaponController?.SetStatModifiers(permDmgMult, permCooldownMult, magCapMult, _blastRadiusMult);
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

        public void OnEnemyKilled()
        {
            KillStreakCount++;
            switch (KillStreakCount)
            {
                case 3 when !IsUavReconActive:
                    IsUavReconActive = true;
                    StoryDirector.NotifyKillstreakReady("Reaper-04 ISR Drone now sweeping hostile transponders on ATAK grid.");
                    UIManager.Instance?.ShowMilestoneBanner("ISR LINK // REAPER-04 OVERWATCH ACTIVE");
                    AudioManager.Instance?.PlayMilestoneSting();
                    break;
                case >= 5 when KillStreakCount % 5 == 0 && MortarStrikeCharges < 2:
                    MortarStrikeCharges++;
                    StoryDirector.NotifyKillstreakReady($"{KillStreakCount} Confirmed KIA: 155mm Battery cleared for Fire Mission [F]!");
                    UIManager.Instance?.ShowMilestoneBanner("CAS 155MM // FIRE MISSION AUTHORIZED [F]");
                    AudioManager.Instance?.PlayMilestoneSting();
                    break;
            }

            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth, TraumaKits);
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                characterMotion?.SetReloadState(false, 0f);
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
            if (_armorCooldownTimer > 0f) _armorCooldownTimer = Mathf.Max(0f, _armorCooldownTimer - dt);
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

            UIManager.Instance?.UpdateOperatorVitals(CurrentHp, MaxHp, CurrentArmor, MaxArmor, TraumaKits);
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth, TraumaKits);

            if (Keyboard.current != null)
            {
                if (Keyboard.current.gKey.wasPressedThisFrame) TryThrowFragGrenade();
                if (Keyboard.current.fKey.wasPressedThisFrame) TryCallMortarStrike();
                if (Keyboard.current.qKey.wasPressedThisFrame) TryUseTraumaKit();
                if (Keyboard.current.rKey.wasPressedThisFrame) weaponController?.StartReload();
                if (Keyboard.current.nKey.wasPressedThisFrame) ToggleNightVision();
            }

            ReadInputs(out var moveVec, out var aimDir, out var fireHeld, out var dashPressed, out var aimWorldTarget);
            _latestAimWorldTarget = aimWorldTarget;

            var rmbHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
            var adsActive = (_simAimDownSights || rmbHeld) && (weaponController == null || !weaponController.IsReloading);
            IsAimingDownSights = adsActive;
            weaponController?.SetAimDownSights(adsActive);
            characterMotion?.SetAimDownSightsState(adsActive);

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
                UIManager.Instance?.AddReticleBloom(0.45f);
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
                var adsSpeedFactor = IsAimingDownSights ? 0.82f : 1.0f;
                horizontalVelocity = moveDir * (_currentMoveSpeed * adsSpeedFactor);
            }

            var planarSpeed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
            if (_dashTimer <= 0f && planarSpeed > 0.55f)
            {
                _footstepDistanceAccum += planarSpeed * dt;
                if (_footstepDistanceAccum >= 1.95f)
                {
                    _footstepDistanceAccum -= 1.95f;
                    AudioManager.Instance?.PlayFootstep();
                }
            }
            else if (planarSpeed <= 0.25f)
            {
                _footstepDistanceAccum = 0.85f;
            }

            if (characterController != null && characterController.enabled)
            {
                characterController.Move((horizontalVelocity + Vector3.down * 6f) * dt);
                if (EnemySpawner.Instance != null)
                {
                    var clamped = EnemySpawner.Instance.ClampPositionToUnlockedSectors(transform.position);
                    if ((clamped - transform.position).sqrMagnitude > 0.0004f)
                    {
                        characterController.enabled = false;
                        transform.position = new Vector3(clamped.x, transform.position.y, clamped.z);
                        characterController.enabled = true;
                    }
                }
            }
            else
            {
                transform.position += horizontalVelocity * dt;
            }

            // Aim orientation & camera look-ahead (keep body & gun barrel 1:1 aligned toward cursor)
            if (aimDir.sqrMagnitude > 0.001f)
            {
                var targetRot = Quaternion.LookRotation(aimDir, Vector3.up);
                transform.rotation = (fireHeld || IsAimingDownSights || aimWorldTarget.HasValue)
                    ? targetRot
                    : Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-45f * dt));
                TopDownCameraRig.Instance?.UpdateAimLookAhead(aimDir);
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            if (weaponController != null)
            {
                characterMotion?.SetReloadState(
                    weaponController.IsReloading,
                    weaponController.ReloadProgress,
                    weaponController.ActiveReloadStyle,
                    weaponController.ActiveHeldReloadMesh,
                    weaponController.IsShotgunPumpPhase,
                    weaponController.ShotgunShellPhase,
                    weaponController.ActiveHeldReloadMaterial,
                    weaponController.ActiveHeldReloadScale);
            }
            else
            {
                characterMotion?.SetReloadState(false, 0f);
            }
            characterMotion?.StepMotion(horizontalVelocity, yawRate, isAimingWeapon: true);
            if (aimWorldTarget.HasValue)
            {
                aimDir = ComputeAimDirectionToWorldTarget(aimWorldTarget.Value, weaponController != null ? weaponController.MuzzleTransform : null);
            }
            if (aimDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
                characterMotion?.AlignWeaponBarrelWithBodyYaw();
            }
            weaponController?.SetAimInput(aimDir, aimWorldTarget);
            weaponController?.TryFire(fireHeld, aimDir, aimWorldTarget);
            if (fireHeld && weaponController != null && !weaponController.IsReloading)
            {
                UIManager.Instance?.AddReticleBloom(0.18f * dt * 12f);
            }
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
            else if (Keyboard.current.digit7Key.wasPressedThisFrame) TrySwitchWeapon(6);
            else if (Keyboard.current.digit8Key.wasPressedThisFrame) TrySwitchWeapon(7);
            else if (Keyboard.current.digit9Key.wasPressedThisFrame) TrySwitchWeapon(8);
            else if (Keyboard.current.digit0Key.wasPressedThisFrame) TrySwitchWeapon(9);
        }

        private Vector3 ComputeAimDirectionToWorldTarget(Vector3 worldTarget, Transform muzzleTf)
        {
            var centerToTarget = worldTarget - transform.position;
            centerToTarget.y = 0f;
            var dist = centerToTarget.magnitude;
            if (dist < 0.05f) return transform.forward;

            var lateralOffset = muzzleTf != null ? transform.InverseTransformPoint(muzzleTf.position).x : 0.11f;
            var bearingRad = Mathf.Atan2(centerToTarget.x, centerToTarget.z);
            if (!(dist > Mathf.Abs(lateralOffset) + 0.06f)) return centerToTarget / dist;
            var parallaxRad = Mathf.Asin(Mathf.Clamp(lateralOffset / dist, -0.92f, 0.92f));
            var correctedRad = bearingRad - parallaxRad;
            return new Vector3(Mathf.Sin(correctedRad), 0f, Mathf.Cos(correctedRad));
        }

        public void SetSimulatedAimDownSights(bool active)
        {
            _simAimDownSights = active;
            IsAimingDownSights = active;
            weaponController?.SetAimDownSights(IsAimingDownSights);
        }

        private void ReadInputs(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed, out Vector3? aimWorldTarget)
        {
            aimWorldTarget = null;
            var muzzleTf = weaponController != null ? weaponController.MuzzleTransform : null;

            moveVec = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
            dashPressed = _sprintAction != null && _sprintAction.WasPressedThisFrame();
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

            DetonateTacticalBlast(target, 4.8f * _blastRadiusMult, 145f * _blastDamageMult, Palette.RocketOrange, "M67 DETONATION");
        }

        public bool TryCallMortarStrike()
        {
            if (IsDead || MortarStrikeCharges <= 0) return false;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return false;

            MortarStrikeCharges--;
            var center = _latestAimWorldTarget ?? (transform.position + transform.forward * 12f);
            center.y = 0f;
            StartCoroutine(MortarBarrageRoutine(center));
            UIManager.Instance?.TriggerThreatWarning("FRIENDLY CAS // 155MM ARTILLERY INBOUND", 2.6f);
            UIManager.Instance?.ShowMilestoneBanner("DANGER CLOSE // 155MM BARRAGE INBOUND");
            AudioManager.Instance?.PlayMilestoneSting();
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth, TraumaKits);
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
                DetonateTacticalBlast(impact, 5.6f * _blastRadiusMult, 185f * _blastDamageMult, Palette.RocketOrange, "155MM IMPACT");
            }
        }

        private void DetonateTacticalBlast(Vector3 center, float radius, float dmg, Color vfxColor, string label)
        {
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center + Vector3.up * 0.6f, vfxColor);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, center + Vector3.up * 0.4f, Palette.C4Amber);
            VFXManager.Instance?.SpawnScorchMark(center, radius * 0.72f);
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
                enemy.ApplyDamage(dmg, enemy.transform.position + Vector3.up * 0.9f, pushDir.sqrMagnitude > 0.01f ? pushDir.normalized : Vector3.forward, isCrit: true, isExplosive: true);
                if (!wasDead && enemy.IsDead)
                {
                    anyKill = true;
                    FeelManager.Instance?.TriggerKillFeed("VANCE", enemy.DisplayCallsign, "ORDNANCE");
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

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
        }

        public bool TryUseTraumaKit()
        {
            if (IsDead || TraumaKits <= 0 || _armorCooldownTimer > 0f) return false;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return false;

            var patchedSquad = SquadDirector.Instance != null && SquadDirector.Instance.TryPatchNearestDownedMember(transform.position, 8.0f);
            if (!patchedSquad && CurrentArmor >= MaxArmor * 0.98f && CurrentHp >= MaxHp * 0.98f)
            {
                return false;
            }

            TraumaKits--;
            _armorCooldownTimer = 0.65f;
            CurrentArmor = MaxArmor;
            Heal(45f);
            AudioManager.Instance?.PlayPickupBlip();
            VFXManager.Instance?.Play(VfxKind.HitSpark, transform.position + Vector3.up * 0.9f, Palette.M3HeliosCoreCyan);
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 1.1f,
                patchedSquad ? "TRAUMA PATCH + PLATES" : "PLATES LOCKED // +45 HP", Palette.M3HeliosCoreCyan);
            UIManager.Instance?.UpdateOperatorVitals(CurrentHp, MaxHp, CurrentArmor, MaxArmor, TraumaKits);
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth, TraumaKits);
            return true;
        }

        public void AddArmorAndTrauma(float armorGain, int kitGain = 0)
        {
            if (armorGain > 0f)
            {
                CurrentArmor = Mathf.Min(MaxArmor, CurrentArmor + armorGain);
            }
            if (kitGain > 0)
            {
                TraumaKits = Mathf.Clamp(TraumaKits + kitGain, 0, MaxTraumaKits);
            }
            UIManager.Instance?.UpdateOperatorVitals(CurrentHp, MaxHp, CurrentArmor, MaxArmor, TraumaKits);
        }

        public void TakeDamage(float dmg, Vector3? sourceWorldPos = null)
        {
            if (IsDead || _invulnTimer > 0f || dmg <= 0f) return;

            _timeSinceLastDamage = 0f;
            IsRegeneratingHealth = false;
            if (sourceWorldPos.HasValue)
            {
                UIManager.Instance?.ShowDirectionalHit(sourceWorldPos.Value);
            }

            var remainingDmg = dmg;
            if (CurrentArmor > 0f)
            {
                var beforeArmor = CurrentArmor;
                var absorb = Mathf.Min(CurrentArmor, dmg * 0.70f);
                CurrentArmor = Mathf.Max(0f, CurrentArmor - absorb);
                remainingDmg = Mathf.Max(1f, dmg - absorb);
                if (beforeArmor > 0.1f && CurrentArmor <= 0.1f)
                {
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 1.1f, "PLATES SHATTERED!", Palette.C4Amber);
                    UIManager.Instance?.TriggerThreatWarning("!! BALLISTIC PLATES SHATTERED — PRESS [Q] TO RE-PLATE !!");
                }
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - remainingDmg);
            _invulnTimer = 0.28f;
            _damageFlashTimer = 0.14f;
            ApplyDamageFlash(1f);

            characterMotion?.TriggerSquash(new Vector3(1.24f, 0.78f, 1.24f));
            FeelManager.Instance?.TriggerCameraShake(0.38f);
            FeelManager.Instance?.SpawnFloatingText(transform.position, "-" + Mathf.RoundToInt(remainingDmg), Palette.EnemySecondary);
            AudioManager.Instance?.PlayHit();
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
            UIManager.Instance?.UpdateOperatorVitals(CurrentHp, MaxHp, CurrentArmor, MaxArmor, TraumaKits);

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
            _dashTimer = 0f;
            _dashCooldownTimer = 0f;
            _invulnTimer = 0f;
            _damageFlashTimer = 0f;
            _armorCooldownTimer = 0f;
            _timeSinceLastDamage = 10f;
            IsRegeneratingHealth = false;
            FragGrenades = 3;
            TraumaKits = 2;
            KillStreakCount = 0;
            IsUavReconActive = false;
            MortarStrikeCharges = 0;
            _grenadeCooldownTimer = 0f;
            transform.localScale = Vector3.one;

            var save = SaveManager.EnsureInstance();
            MaxHp = baseMaxHp + save.Data.armorRank * 20f;
            CurrentHp = MaxHp;
            MaxArmor = 50f + save.Data.armorRank * 15f;
            CurrentArmor = MaxArmor;

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
            characterMotion?.ResetMotionState();
            SyncHud();
        }

        private void SyncHud()
        {
            UIManager.Instance?.UpdateHealth(CurrentHp, MaxHp);
            UIManager.Instance?.UpdateOperatorVitals(CurrentHp, MaxHp, CurrentArmor, MaxArmor, TraumaKits);
            UIManager.Instance?.UpdateDashCooldown(1f);
            UIManager.Instance?.RefreshWeaponBar();
            UIManager.Instance?.UpdateEquipmentHud(FragGrenades, MortarStrikeCharges, KillStreakCount, IsUavReconActive, IsRegeneratingHealth, TraumaKits);
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
