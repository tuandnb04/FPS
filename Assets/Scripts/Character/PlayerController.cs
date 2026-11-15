using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TopDownShooter.Audio;
using TopDownShooter.CameraRig;
using TopDownShooter.Combat;
using TopDownShooter.Core;
using TopDownShooter.Enemies;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.Character
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

        public static readonly RogueliteSkillInfo[] SkillCatalog =
        {
            new RogueliteSkillInfo(RogueliteSkillId.DamageBoost, "STOPPING POWER", "+18% All Ballistic Weapon Damage"),
            new RogueliteSkillInfo(RogueliteSkillId.FireRateBoost, "DOUBLE TAP", "+16% Weapon Fire Rate"),
            new RogueliteSkillInfo(RogueliteSkillId.MultiShot, "MULTI-BURST", "+1 Bonus Round Per Trigger Pull"),
            new RogueliteSkillInfo(RogueliteSkillId.MaxHpUp, "JUGGERNAUT", "+25 Max Armor HP & Heal +25 HP"),
            new RogueliteSkillInfo(RogueliteSkillId.MagnetSpeed, "LIGHTWEIGHT", "+0.45 Sprint Speed & +1.4m Pickup Radius"),
            new RogueliteSkillInfo(RogueliteSkillId.DashCool, "SLEIGHT OF HAND", "-15% Tactical Sprint Cooldown"),
            new RogueliteSkillInfo(RogueliteSkillId.VampiricSiphon, "QUICK FIX", "Restore +4 HP On Every Confirmed Kill"),
            new RogueliteSkillInfo(RogueliteSkillId.CritPayload, "DEADSHOT", "+15% Chance For 1.65x Headshot Crit")
        };

        [Header("References")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private WeaponController weaponController;
        [SerializeField] private MeshRenderer[] bodyRenderers;
        [SerializeField] private MeshRenderer[] primarySkinRenderers;

        [Header("Stats")]
        [SerializeField] private float baseMoveSpeed = 7.4f;
        [SerializeField] private float dashSpeed = 20f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float baseDashCooldown = 1.6f;
        [SerializeField] private float baseMaxHp = 100f;

        private InputAction moveAction;
        private InputAction attackAction;
        private InputAction sprintAction;

        private float currentMaxHp = 100f;
        private float currentHp = 100f;
        private float currentMoveSpeed = 7.4f;
        private float currentDashCooldown = 1.6f;
        private int currentXp;
        private int xpToNextLevel = 100;
        private int currentLevel = 1;
        private float dashTimer;
        private float dashCooldownTimer;
        private Vector3 dashDirection;
        private float invulnTimer;
        private float damageFlashTimer;
        private float prevYaw;
        private bool isDead;

        private readonly int[] skillRanks = new int[8];
        private MaterialPropertyBlock mpb;

        // Deterministic verification & automated demo hooks
        private bool hasSimulatedInput;
        private Vector2 simulatedMove;
        private Vector3? simulatedAimPoint;
        private bool simulatedFire;
        private bool simulatedDash;
        private bool autoPlayBot;
        private float botAngle;

        public float CurrentHp => currentHp;
        public float MaxHp => currentMaxHp;
        public int CurrentXp => currentXp;
        public int XpToNextLevel => xpToNextLevel;
        public int CurrentLevel => currentLevel;
        public bool IsDead => isDead;
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

            currentMaxHp = baseMaxHp;
            currentHp = currentMaxHp;
            prevYaw = transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            moveAction?.Enable();
            attackAction?.Enable();
            sprintAction?.Enable();
        }

        private void OnDisable()
        {
            moveAction?.Disable();
            attackAction?.Disable();
            sprintAction?.Disable();
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
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            SphereCollider pickupTrigger = GetComponent<SphereCollider>();
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
                InputActionMap playerMap = inputActions.FindActionMap("Player", false);
                if (playerMap != null)
                {
                    moveAction = playerMap.FindAction("Move", false);
                    attackAction = playerMap.FindAction("Attack", false);
                    sprintAction = playerMap.FindAction("Sprint", false);
                }
            }

            if (moveAction == null)
            {
                moveAction = new InputAction("Move", InputActionType.Value);
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
            }
            if (attackAction == null)
            {
                attackAction = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            }
            if (sprintAction == null)
            {
                sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            }
        }

        public void SetSimulatedInput(Vector2 move, Vector3? aimWorldPoint, bool fire, bool dash = false)
        {
            hasSimulatedInput = true;
            simulatedMove = Vector2.ClampMagnitude(move, 1f);
            simulatedAimPoint = aimWorldPoint;
            simulatedFire = fire;
            simulatedDash = dash;
        }

        public void ClearSimulatedInput()
        {
            hasSimulatedInput = false;
            simulatedMove = Vector2.zero;
            simulatedAimPoint = null;
            simulatedFire = false;
            simulatedDash = false;
        }

        public void SetAutoPlayBot(bool enabled)
        {
            autoPlayBot = enabled;
        }

        public void ApplySaveLoadout()
        {
            SaveManager save = SaveManager.EnsureInstance();
            GameSaveData d = save.Data;

            float permMaxHp = baseMaxHp + d.armorRank * 20f;
            float skillHp = skillRanks[(int)RogueliteSkillId.MaxHpUp] * 25f;
            currentMaxHp = permMaxHp + skillHp;
            currentHp = Mathf.Min(currentHp > 0f ? currentHp : currentMaxHp, currentMaxHp);

            RecalculateDerivedStats();

            if (weaponController != null)
            {
                weaponController.EquipWeapon((WeaponType)Mathf.Clamp(d.selectedWeapon, 0, 3));
            }
            ApplySkinTint(d.selectedSkin);
            UIManager.Instance?.RefreshWeaponBar();
        }

        private void RecalculateDerivedStats()
        {
            SaveManager save = SaveManager.EnsureInstance();
            GameSaveData d = save.Data;

            int speedRank = skillRanks[(int)RogueliteSkillId.MagnetSpeed];
            int dashRank = skillRanks[(int)RogueliteSkillId.DashCool];
            int dmgRank = skillRanks[(int)RogueliteSkillId.DamageBoost];
            int rateRank = skillRanks[(int)RogueliteSkillId.FireRateBoost];
            int multiRank = skillRanks[(int)RogueliteSkillId.MultiShot];
            int critRank = skillRanks[(int)RogueliteSkillId.CritPayload];

            currentMoveSpeed = baseMoveSpeed + d.thrusterRank * 0.35f + speedRank * 0.45f;
            float permCooldownMult = Mathf.Max(0.55f, 1f - d.thrusterRank * 0.08f);
            float skillCooldownMult = Mathf.Pow(0.85f, dashRank);
            currentDashCooldown = Mathf.Max(0.45f, baseDashCooldown * permCooldownMult * skillCooldownMult);

            PickupMagnetBonus = d.greedRank * 0.35f + speedRank * 1.4f;
            CreditMultiplier = 1f + d.greedRank * 0.18f;

            float permDmgMult = 1f + d.firepowerRank * 0.12f;
            float skillDmgMult = 1f + dmgRank * 0.18f;
            float skillRateMult = 1f + rateRank * 0.16f;
            float critChance = Mathf.Min(0.75f, critRank * 0.15f);

            weaponController?.SetStatModifiers(permDmgMult, skillDmgMult, skillRateMult, multiRank, critChance);
        }

        private void ApplySkinTint(int skinId)
        {
            Color skinCol = skinId == 1
                ? Palette.SkinGoldPrimary
                : (skinId == 2 ? Palette.SkinCrimsonPrimary : Palette.CharacterPrimary);

            if (primarySkinRenderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            for (int i = 0; i < primarySkinRenderers.Length; i++)
            {
                if (primarySkinRenderers[i] == null) continue;
                primarySkinRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", skinCol);
                primarySkinRenderers[i].SetPropertyBlock(mpb);
            }
        }

        public bool TrySwitchWeapon(int weaponIndex)
        {
            if (weaponIndex < 0 || weaponIndex > 3) return false;
            SaveManager save = SaveManager.EnsureInstance();
            if (!save.IsWeaponUnlocked(weaponIndex)) return false;

            save.Data.selectedWeapon = weaponIndex;
            save.Save();
            weaponController?.EquipWeapon((WeaponType)weaponIndex);
            AudioManager.Instance?.PlayUIClick();
            UIManager.Instance?.RefreshWeaponBar();
            return true;
        }

        public int GetSkillRank(RogueliteSkillId skill)
        {
            return skillRanks[(int)skill];
        }

        public void ApplyRogueliteSkill(RogueliteSkillId skill)
        {
            int idx = (int)skill;
            skillRanks[idx]++;

            if (skill == RogueliteSkillId.MaxHpUp)
            {
                currentMaxHp += 25f;
                currentHp = Mathf.Min(currentMaxHp, currentHp + 25f);
            }

            RecalculateDerivedStats();
            SyncHud();
            AudioManager.Instance?.PlayMilestoneSting();
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.6f, SkillCatalog[idx].Title, Palette.Accent);
        }

        public List<RogueliteSkillInfo> RollDraftOptions(int count = 3)
        {
            List<int> pool = new List<int>(SkillCatalog.Length);
            for (int i = 0; i < SkillCatalog.Length; i++) pool.Add(i);

            List<RogueliteSkillInfo> picks = new List<RogueliteSkillInfo>(count);
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int randIdx = UnityEngine.Random.Range(0, pool.Count);
                picks.Add(SkillCatalog[pool[randIdx]]);
                pool.RemoveAt(randIdx);
            }
            return picks;
        }

        public void OnEnemyKilled()
        {
            int siphonRank = skillRanks[(int)RogueliteSkillId.VampiricSiphon];
            if (siphonRank > 0 && !isDead)
            {
                Heal(4f * siphonRank);
            }
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: true);
                return;
            }
            if (isDead) return;

            CheckWeaponHotkeys();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (invulnTimer > 0f) invulnTimer -= dt;
            if (dashCooldownTimer > 0f)
            {
                dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - dt);
                UIManager.Instance?.UpdateDashCooldown(1f - (dashCooldownTimer / currentDashCooldown));
            }

            if (damageFlashTimer > 0f)
            {
                damageFlashTimer -= dt;
                ApplyDamageFlash(Mathf.Clamp01(damageFlashTimer / 0.14f));
            }

            ReadInputs(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed);

            // Dash activation
            if (dashPressed && dashTimer <= 0f && dashCooldownTimer <= 0f)
            {
                Vector3 wishMove = new Vector3(moveVec.x, 0f, moveVec.y);
                dashDirection = wishMove.sqrMagnitude > 0.01f ? wishMove.normalized : aimDir;
                dashTimer = dashDuration;
                dashCooldownTimer = currentDashCooldown;
                invulnTimer = dashDuration + 0.08f;
                AudioManager.Instance?.PlayDash();
                characterMotion?.TriggerSquash(new Vector3(0.84f, 1.18f, 0.84f), 0.16f);
                UIManager.Instance?.UpdateDashCooldown(0f);
            }

            // Velocity computation
            Vector3 horizontalVelocity;
            if (dashTimer > 0f)
            {
                dashTimer -= dt;
                horizontalVelocity = dashDirection * dashSpeed;
            }
            else
            {
                Vector3 moveDir = new Vector3(moveVec.x, 0f, moveVec.y);
                if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
                horizontalVelocity = moveDir * currentMoveSpeed;
            }

            if (characterController != null && characterController.enabled)
            {
                characterController.Move((horizontalVelocity + Vector3.down * 6f) * dt);
            }
            else
            {
                transform.position += horizontalVelocity * dt;
            }

            // Aim orientation & camera look-ahead
            if (aimDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(aimDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-20f * dt));
                TopDownCameraRig.Instance?.UpdateAimLookAhead(aimDir);
            }

            float currentYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            prevYaw = currentYaw;

            characterMotion?.StepMotion(horizontalVelocity, yawRate, isAimingWeapon: true);
            weaponController?.TryFire(fireHeld, aimDir);
        }

        private void CheckWeaponHotkeys()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) TrySwitchWeapon(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) TrySwitchWeapon(1);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) TrySwitchWeapon(2);
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) TrySwitchWeapon(3);
        }

        private void ReadInputs(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed)
        {
            if (hasSimulatedInput)
            {
                moveVec = simulatedMove;
                fireHeld = simulatedFire;
                dashPressed = simulatedDash;
                simulatedDash = false;
                if (simulatedAimPoint.HasValue)
                {
                    Vector3 diff = simulatedAimPoint.Value - transform.position;
                    diff.y = 0f;
                    aimDir = diff.sqrMagnitude > 0.001f ? diff.normalized : transform.forward;
                }
                else
                {
                    aimDir = transform.forward;
                }
                return;
            }

            if (autoPlayBot)
            {
                StepAutoBotInput(out moveVec, out aimDir, out fireHeld, out dashPressed);
                return;
            }

            // Hybrid PC (WASD + Mouse) + Mobile Twin-Stick Touch Joysticks
            moveVec = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            VirtualJoystick leftStick = UIManager.Instance?.LeftJoystick;
            if (leftStick != null && (leftStick.IsHeld || leftStick.Value.sqrMagnitude > 0.01f))
            {
                moveVec = leftStick.Value;
            }

            bool touchDash = UIManager.Instance != null && UIManager.Instance.ConsumeTouchDashPressed();
            dashPressed = touchDash || (sprintAction != null && sprintAction.WasPressedThisFrame());

            VirtualJoystick rightStick = UIManager.Instance?.RightJoystick;
            if (rightStick != null && (rightStick.IsHeld || rightStick.Value.sqrMagnitude > 0.04f))
            {
                Vector2 aim2D = rightStick.Value;
                aimDir = aim2D.sqrMagnitude > 0.01f
                    ? new Vector3(aim2D.x, 0f, aim2D.y).normalized
                    : transform.forward;
                fireHeld = true;
                return;
            }

            fireHeld = attackAction != null && attackAction.IsPressed();

            // Raycast PC mouse cursor onto player elevation plane
            aimDir = transform.forward;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null && Mouse.current != null)
            {
                Vector2 screenPos = Mouse.current.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(screenPos);
                Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y + 0.9f, 0f));
                if (groundPlane.Raycast(ray, out float enter))
                {
                    Vector3 hitWorld = ray.GetPoint(enter);
                    Vector3 flat = hitWorld - transform.position;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.01f)
                    {
                        aimDir = flat.normalized;
                    }
                }
            }
            else if (moveVec.sqrMagnitude > 0.01f)
            {
                aimDir = new Vector3(moveVec.x, 0f, moveVec.y).normalized;
            }
        }

        private void StepAutoBotInput(out Vector2 moveVec, out Vector3 aimDir, out bool fireHeld, out bool dashPressed)
        {
            botAngle += Time.deltaTime * 1.1f;
            Vector3 desiredOrbit = new Vector3(Mathf.Cos(botAngle) * 4.5f, 0f, Mathf.Sin(botAngle) * 4.5f);
            Vector3 toOrbit = desiredOrbit - new Vector3(transform.position.x, 0f, transform.position.z);
            moveVec = toOrbit.sqrMagnitude > 0.25f ? new Vector2(toOrbit.x, toOrbit.z).normalized : Vector2.zero;

            EnemyController nearest = null;
            float bestDistSq = float.MaxValue;
            var enemies = FindObjectsByType<EnemyController>();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] == null || enemies[i].IsDead) continue;
                float dSq = (enemies[i].transform.position - transform.position).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    nearest = enemies[i];
                }
            }

            if (nearest != null)
            {
                Vector3 diff = nearest.transform.position - transform.position;
                diff.y = 0f;
                aimDir = diff.sqrMagnitude > 0.001f ? diff.normalized : transform.forward;
                fireHeld = bestDistSq < 280f;
            }
            else
            {
                aimDir = toOrbit.sqrMagnitude > 0.05f ? toOrbit.normalized : transform.forward;
                fireHeld = false;
            }
            dashPressed = false;
        }

        public void AddXp(int amount)
        {
            if (isDead || amount <= 0) return;
            currentXp += amount;

            while (currentXp >= xpToNextLevel)
            {
                currentXp -= xpToNextLevel;
                currentLevel++;
                xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.35f);
                weaponController?.SetWeaponTier(currentLevel);
                FeelManager.Instance?.TriggerLevelUpSequence(currentLevel, transform);
                if (!autoPlayBot)
                {
                    GameManager.Instance?.OpenLevelUpDraft();
                }
                else
                {
                    ApplyRogueliteSkill((RogueliteSkillId)UnityEngine.Random.Range(0, skillRanks.Length));
                }
            }

            UIManager.Instance?.UpdateXp(currentXp, xpToNextLevel, currentLevel);
        }

        public void Heal(float amount)
        {
            if (isDead || amount <= 0f) return;
            currentHp = Mathf.Min(currentMaxHp, currentHp + amount);
            UIManager.Instance?.UpdateHealth(currentHp, currentMaxHp);
        }

        public void TakeDamage(float dmg)
        {
            if (isDead || invulnTimer > 0f || dmg <= 0f) return;

            currentHp = Mathf.Max(0f, currentHp - dmg);
            invulnTimer = 0.28f;
            damageFlashTimer = 0.14f;
            ApplyDamageFlash(1f);

            characterMotion?.TriggerSquash(new Vector3(1.24f, 0.78f, 1.24f), 0.14f);
            FeelManager.Instance?.TriggerCameraShake(0.38f);
            FeelManager.Instance?.SpawnFloatingText(transform.position, "-" + Mathf.RoundToInt(dmg), Palette.EnemySecondary);
            AudioManager.Instance?.PlayHit();
            UIManager.Instance?.UpdateHealth(currentHp, currentMaxHp);

            if (currentHp <= 0f)
            {
                isDead = true;
                VFXManager.Instance?.Play(VfxKind.EnemyDeath, transform.position + Vector3.up * 0.9f, Palette.EnemySecondary);
                AudioManager.Instance?.PlayEnemyExplode();
                GameManager.Instance?.EndMission(false);
            }
        }

        public void ResetPlayer()
        {
            isDead = false;
            currentXp = 0;
            xpToNextLevel = 100;
            currentLevel = 1;
            dashTimer = 0f;
            dashCooldownTimer = 0f;
            invulnTimer = 0f;
            damageFlashTimer = 0f;
            transform.localScale = Vector3.one;

            for (int i = 0; i < skillRanks.Length; i++)
            {
                skillRanks[i] = 0;
            }

            SaveManager save = SaveManager.EnsureInstance();
            currentMaxHp = baseMaxHp + save.Data.armorRank * 20f;
            currentHp = currentMaxHp;

            if (characterController != null)
            {
                characterController.enabled = false;
                transform.SetPositionAndRotation(new Vector3(0f, 0.02f, 0f), Quaternion.identity);
                characterController.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(new Vector3(0f, 0.02f, 0f), Quaternion.identity);
            }

            weaponController?.ReturnAllActive();
            ApplySaveLoadout();
            ApplyDamageFlash(0f);
            ClearSimulatedInput();
            characterMotion?.ResetMotionState();
            SyncHud();
        }

        private void SyncHud()
        {
            UIManager.Instance?.UpdateHealth(currentHp, currentMaxHp);
            UIManager.Instance?.UpdateXp(currentXp, xpToNextLevel, currentLevel);
            UIManager.Instance?.UpdateDashCooldown(1f);
            UIManager.Instance?.RefreshWeaponBar();
        }

        private void ApplyDamageFlash(float intensity)
        {
            if (bodyRenderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            Color flash = Palette.Lerp(Palette.Shadow, Palette.EnemySecondary, intensity) * (intensity * 1.6f);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                bodyRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", flash);
                bodyRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
