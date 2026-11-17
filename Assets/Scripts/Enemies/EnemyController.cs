using System;
using Audio;
using Character;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.Pool;

namespace Enemies
{
    public enum EnemyArchetype
    {
        Chaser,
        Heavy,
        Boss
    }

    /// <summary>
    /// Pooled Ironward soldier, heavy PKM enforcer, and mission-unique Boss controller
    /// supporting distinct per-act boss behaviors (Warden Barge, Goliath Railgun Laser,
    /// Volkov Reactive Riot Shield, and Radek 2-Phase Overcharge).
    /// </summary>
    public sealed class EnemyController : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

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
        [SerializeField] private GameObject bossShieldVisual;
        [SerializeField] private LineRenderer railLaserLine;

        private ObjectPool<EnemyController> _ownerPool;
        private float _moveSpeed;
        private float _contactDamage;
        private float _rangedDamage;
        private float _projectileSpeed;
        private int _scoreValue;
        private int _activeWaveNumber = 1;
        private string _bossDisplayTitle = "WARDEN-01";
        private BossBehaviorKind _bossBehavior = BossBehaviorKind.WardenBarge;
        private float _hitFlashTimer;
        private float _contactCooldown;
        private float _fireCooldown;
        private int _burstRemaining;
        private float _burstTimer;
        private float _bossBarrageTimer;
        private float _bossHazardTimer;
        private float _bossCannonTimer;
        private int _bossCannonBurst;
        private int _bossBarrageStep;
        private float _prevYaw;
        private bool _isAlerted;
        private float _aggroRadius = 26f;

        // Goliath Railgun laser telegraph
        private float _railChargeTimer;
        private bool _railLockActive;
        private float _railLockElapsed;
        private Vector3 _railLockedDir = Vector3.forward;

        // Volkov Reactive Shield
        private bool _volkovShieldActive;
        private float _volkovShieldTimer;

        // Radek Phase 2 Overcharge
        private bool _radekPhase2;
        private float _radekDashCooldown;
        private float _radekDashTimer;
        private Vector3 _radekDashDir;

        private MaterialPropertyBlock _mpb;
        private Action<EnemyController, bool> _onFinishedCallback;

        public bool IsDead { get; private set; }

        public EnemyArchetype Archetype { get; private set; }

        public string DisplayCallsign
        {
            get
            {
                if (Archetype == EnemyArchetype.Boss) return _bossDisplayTitle;
                var m = StoryDirector.ActiveMissionIndex;
                return (m, Archetype) switch
                {
                    (1, EnemyArchetype.Heavy) => "DOCKYARD ENFORCER",
                    (1, _) => "HARBOR RAIDER",
                    (2, EnemyArchetype.Heavy) => "GORGE JUGGERNAUT",
                    (2, _) => "RAIL SAPPER",
                    (3, EnemyArchetype.Heavy) => "KESTREL BREACHER",
                    (3, _) => "AEGIS DEFECTOR",
                    (4, EnemyArchetype.Heavy) => "APEX PRAETORIAN",
                    _ => "CITADEL GUARD"
                };
            }
        }

        private float CurrentHp { get; set; }

        private float MaxHp { get; set; }

        private int ShotsFiredCount { get; set; }

        private Transform ActiveWeaponMuzzle
        {
            get
            {
                if (Archetype == EnemyArchetype.Boss)
                {
                    if (_bossBehavior == BossBehaviorKind.VolkovJuggernaut || (_bossBehavior == BossBehaviorKind.RadekApex && _radekPhase2))
                    {
                        return pkmMuzzleTip != null ? pkmMuzzleTip : bossMuzzleTip;
                    }
                    return bossMuzzleTip;
                }
                return Archetype == EnemyArchetype.Heavy ? pkmMuzzleTip : ak74MuzzleTip;
            }
        }

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
            Transform bossMuzzle = null,
            GameObject shieldVisual = null,
            LineRenderer laserLine = null)
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
            bossShieldVisual = shieldVisual;
            railLaserLine = laserLine;
        }

        public void Initialize(
            ObjectPool<EnemyController> pool,
            EnemyArchetype kind,
            Vector3 spawnPos,
            int waveNumber,
            Action<EnemyController, bool> onFinished)
        {
            _ownerPool = pool;
            Archetype = kind;
            _activeWaveNumber = Mathf.Max(1, waveNumber);
            _onFinishedCallback = onFinished;
            IsDead = false;
            _hitFlashTimer = 0f;
            _contactCooldown = 0.25f;
            _fireCooldown = UnityEngine.Random.Range(0.95f, 1.75f);
            _burstRemaining = 0;
            _burstTimer = 0f;
            _bossBarrageTimer = 2.0f;
            _bossHazardTimer = 3.5f;
            _bossCannonTimer = 1.25f;
            _bossCannonBurst = 0;
            _bossBarrageStep = 0;
            _railChargeTimer = 3.0f;
            _railLockActive = false;
            _railLockElapsed = 0f;
            _volkovShieldActive = false;
            _volkovShieldTimer = 2.4f;
            _radekPhase2 = false;
            _radekDashCooldown = 3.2f;
            _radekDashTimer = 0f;
            ShotsFiredCount = 0;
            _isAlerted = kind == EnemyArchetype.Boss;
            _aggroRadius = kind == EnemyArchetype.Boss ? 54f : 26f;

            _bossBehavior = StoryDirector.GetMissionBossBehavior(StoryDirector.ActiveMissionIndex);
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);
            if (railLaserLine != null) railLaserLine.enabled = false;

            var missionScale = 1f + (StoryDirector.ActiveMissionIndex - 1) * 0.10f;
            var waveScale = (1f + (_activeWaveNumber - 1) * 0.11f) * missionScale;

            var bossUsesExoBody = kind == EnemyArchetype.Boss && _bossBehavior == BossBehaviorKind.VolkovJuggernaut;
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(kind != EnemyArchetype.Boss || bossUsesExoBody);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(kind == EnemyArchetype.Heavy || bossUsesExoBody);
            if (bossVisualRoot != null) bossVisualRoot.SetActive(kind == EnemyArchetype.Boss && !bossUsesExoBody);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(kind == EnemyArchetype.Heavy || bossUsesExoBody);

            switch (kind)
            {
                case EnemyArchetype.Boss:
                    MaxHp = _bossBehavior switch
                    {
                        BossBehaviorKind.WardenBarge => 480f,
                        BossBehaviorKind.GoliathRail => 620f,
                        BossBehaviorKind.VolkovJuggernaut => 740f,
                        _ => 860f
                    };
                    _moveSpeed = _bossBehavior switch
                    {
                        BossBehaviorKind.VolkovJuggernaut => 3.35f,
                        BossBehaviorKind.RadekApex => 2.95f,
                        _ => 2.45f
                    };
                    _contactDamage = 28f;
                    _rangedDamage = 10f + (StoryDirector.ActiveMissionIndex - 1) * 1.5f;
                    _projectileSpeed = 14.5f;
                    _scoreValue = 1800;
                    _bossDisplayTitle = StoryDirector.GetBossCallsign(_activeWaveNumber);
                    if (bossUsesExoBody)
                    {
                        ConfigureHeavyOperatorIk(1.46f);
                    }
                    else
                    {
                        transform.localScale = Vector3.one * (_bossBehavior == BossBehaviorKind.RadekApex ? 1.24f : 1.18f);
                    }
                    UIManager.Instance?.UpdateBossHp(MaxHp, MaxHp, _bossDisplayTitle);
                    break;

                case EnemyArchetype.Heavy:
                    MaxHp = 112f * waveScale;
                    _moveSpeed = 3.15f + Mathf.Min(1.0f, _activeWaveNumber * 0.12f);
                    _contactDamage = 20f;
                    _rangedDamage = 6f + Mathf.Min(3.5f, _activeWaveNumber * 0.35f);
                    _projectileSpeed = 11.8f + Mathf.Min(1.5f, _activeWaveNumber * 0.18f);
                    _scoreValue = 250;
                    ConfigureHeavyOperatorIk(1.22f);
                    break;

                default:
                    MaxHp = 46f * waveScale;
                    _moveSpeed = 4.35f + Mathf.Min(1.5f, _activeWaveNumber * 0.16f);
                    _contactDamage = 12f;
                    _rangedDamage = 6.5f + Mathf.Min(3.2f, _activeWaveNumber * 0.30f);
                    _projectileSpeed = 12.2f + Mathf.Min(2.0f, _activeWaveNumber * 0.22f);
                    _scoreValue = 100;
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
                    break;
            }

            CurrentHp = MaxHp;

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

            _prevYaw = transform.eulerAngles.y;
            ApplyHitFlash(0f);
        }

        private void ConfigureHeavyOperatorIk(float rootScale)
        {
            transform.localScale = Vector3.one * rootScale;
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

        private void Update()
        {
            if (IsDead) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                if (railLaserLine != null) railLaserLine.enabled = false;
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(_hitFlashTimer / 0.10f));
            }

            if (_contactCooldown > 0f)
            {
                _contactCooldown -= dt;
            }

            var player = PlayerController.Instance;
            var velocity = Vector3.zero;

            ResolveCombatTarget(player, out var targetTf, out var targetPlayer, out var targetSquad, out var dist);
            if (!_isAlerted && targetTf != null && dist <= _aggroRadius)
            {
                _isAlerted = true;
            }

            if (targetTf != null && _isAlerted)
            {
                var toTarget = targetTf.position - transform.position;
                toTarget.y = 0f;

                if (dist > 0.05f)
                {
                    var dir = toTarget / dist;
                    var targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-8f * dt));

                    var minStopDist = Archetype switch
                    {
                        EnemyArchetype.Boss => _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 ? 5.0f : 4.5f,
                        EnemyArchetype.Heavy => 5.2f,
                        _ => 4.2f
                    };

                    if (_radekDashTimer > 0f)
                    {
                        _radekDashTimer -= dt;
                        velocity = _radekDashDir * 14.0f;
                    }
                    else if (dist > minStopDist)
                    {
                        velocity = dir * _moveSpeed;
                    }

                    if (velocity.sqrMagnitude > 0.01f)
                    {
                        if (controller != null && controller.enabled)
                        {
                            controller.Move((velocity + Vector3.down * 4f) * dt);
                        }
                        else
                        {
                            transform.position += velocity * dt;
                        }
                    }

                    var hitDist = Archetype == EnemyArchetype.Boss ? 1.85f : 1.25f;
                    if (dist <= hitDist && _contactCooldown <= 0f)
                    {
                        _contactCooldown = 0.85f;
                        if (targetPlayer != null) targetPlayer.TakeDamage(_contactDamage, transform.position);
                        else if (targetSquad != null) targetSquad.TakeDamage(_contactDamage);
                    }
                }

                if (Archetype == EnemyArchetype.Boss)
                {
                    StepBossAttacks(player, targetTf, dist, dt);
                }
                else
                {
                    StepInfantryFire(player, targetTf, dist, dt);
                }
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            var useBipedalMotion = Archetype != EnemyArchetype.Boss
                || _bossBehavior == BossBehaviorKind.VolkovJuggernaut
                || (_bossBehavior == BossBehaviorKind.RadekApex && _radekPhase2);
            if (useBipedalMotion)
            {
                characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: true);
            }
        }

        public void SetGarrisonAlert(bool alertedImmediately, float customAggroRadius = 24f)
        {
            _isAlerted = alertedImmediately;
            _aggroRadius = Mathf.Max(4f, customAggroRadius);
        }

        private void ResolveCombatTarget(
            PlayerController player,
            out Transform bestTf,
            out PlayerController hitPlayer,
            out SquadMemberController hitSquad,
            out float bestDist)
        {
            bestTf = null;
            hitPlayer = null;
            hitSquad = null;
            bestDist = float.MaxValue;

            if (player != null && !player.IsDead)
            {
                bestTf = player.transform;
                hitPlayer = player;
                var d = player.transform.position - transform.position;
                d.y = 0f;
                bestDist = d.magnitude;
            }

            if (SquadDirector.Instance == null) return;
            var squad = SquadDirector.Instance.Members;
            foreach (var member in squad)
            {
                if (member == null || member.IsDowned || !member.gameObject.activeInHierarchy) continue;
                var d = member.transform.position - transform.position;
                d.y = 0f;
                var mDist = d.magnitude;
                if (!(mDist * 0.88f < bestDist)) continue;
                bestDist = mDist;
                bestTf = member.transform;
                hitPlayer = null;
                hitSquad = member;
            }
        }

        private void StepInfantryFire(PlayerController player, Transform targetTf, float distToTarget, float dt)
        {
            var maxRange = Archetype == EnemyArchetype.Heavy ? 15.8f : 14.5f;
            if (distToTarget > maxRange || player == null || player.Weapon == null || targetTf == null)
            {
                _burstRemaining = 0;
                return;
            }

            if (_burstRemaining > 0)
            {
                _burstTimer -= dt;
                if (!(_burstTimer <= 0f)) return;
                _burstRemaining--;
                _burstTimer = 0.18f;
                FireTracerFromMuzzle(
                    ActiveWeaponMuzzle,
                    player,
                    targetTf.position,
                    _projectileSpeed,
                    _rangedDamage,
                    Palette.EnemySecondary,
                    new Vector3(0.050f, 0.30f, 0.050f),
                    3.2f,
                    0.76f);
                return;
            }

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            if (Archetype == EnemyArchetype.Heavy)
            {
                _fireCooldown = UnityEngine.Random.Range(2.55f, 3.10f);
                _burstRemaining = 3;
                _burstTimer = 0f;
            }
            else
            {
                _fireCooldown = UnityEngine.Random.Range(1.75f, 2.30f);
                FireTracerFromMuzzle(
                    ActiveWeaponMuzzle,
                    player,
                    targetTf.position,
                    _projectileSpeed,
                    _rangedDamage,
                    Palette.EnemySecondary,
                    new Vector3(0.044f, 0.28f, 0.044f),
                    2.2f,
                    0.96f);
            }
        }

        private void StepBossAttacks(PlayerController player, Transform targetTf, float dist, float dt)
        {
            if (targetTf == null || player == null) return;
            if (bossTurretPivot != null && bossVisualRoot != null && bossVisualRoot.activeSelf)
            {
                var aimDir = targetTf.position - bossTurretPivot.position;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.01f)
                {
                    var turretRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
                    bossTurretPivot.rotation = Quaternion.Slerp(bossTurretPivot.rotation, turretRot, 1f - Mathf.Exp(-6f * dt));
                }
            }

            switch (_bossBehavior)
            {
                case BossBehaviorKind.WardenBarge:
                    StepWardenBargeBoss(player, targetTf, dt);
                    break;
                case BossBehaviorKind.GoliathRail:
                    StepGoliathRailBoss(player, targetTf, dist, dt);
                    break;
                case BossBehaviorKind.VolkovJuggernaut:
                    StepVolkovShieldBoss(player, targetTf, dt);
                    break;
                default:
                    StepRadekApexBoss(player, targetTf, dt);
                    break;
            }
        }

        private void StepWardenBargeBoss(PlayerController player, Transform targetTf, float dt)
        {
            // Twin coaxial cannon 2-round burst
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0) _bossCannonBurst = 2;
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.22f : 1.90f;
                FireTracerFromMuzzle(bossMuzzleTip, player, targetTf.position, _projectileSpeed, _rangedDamage,
                    Palette.BossPurple, new Vector3(0.09f, 0.42f, 0.09f), 1.5f, 0.66f);
            }

            // Sweeping 5-shot harbor arc
            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 3.1f;
                _bossBarrageStep++;
                var origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position + Vector3.up * 1.1f;
                var toPlayer = targetTf.position - origin;
                toPlayer.y = 0f;
                var baseDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
                for (var i = -2; i <= 2; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 14f, 0f) * baseDir;
                    player.Weapon.SpawnEnemyProjectile(origin + dir * 0.45f, dir, 13.0f, 12f, Palette.BossPurple, new Vector3(0.18f, 0.20f, 0.18f), 2.5f);
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.BossPurple);
                AudioManager.Instance?.PlayEnemyShoot(0.62f, 0.34f);
            }

            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 4.6f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.5f, 22f);
            }
        }

        private void StepGoliathRailBoss(PlayerController player, Transform targetTf, float dist, float dt)
        {
            var origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position + Vector3.up * 1.15f;
            if (_railLockActive)
            {
                _railLockElapsed += dt;
                if (_railLockElapsed < 0.95f)
                {
                    var aim = targetTf.position - origin;
                    aim.y = 0f;
                    if (aim.sqrMagnitude > 0.01f) _railLockedDir = aim.normalized;
                }

                if (railLaserLine != null)
                {
                    railLaserLine.enabled = true;
                    var lockedColor = _railLockElapsed >= 0.95f ? Palette.HitmarkerCrimson : Palette.C4Amber;
                    railLaserLine.startColor = lockedColor;
                    railLaserLine.endColor = lockedColor;
                    railLaserLine.SetPosition(0, origin);
                    railLaserLine.SetPosition(1, origin + _railLockedDir * 24f);
                }

                if (_railLockElapsed >= 1.25f)
                {
                    _railLockActive = false;
                    if (railLaserLine != null) railLaserLine.enabled = false;
                    if (player.Weapon != null)
                    {
                        for (var s = -1; s <= 1; s++)
                        {
                            var dir = Quaternion.Euler(0f, s * 4.5f, 0f) * _railLockedDir;
                            player.Weapon.SpawnEnemyProjectile(origin + dir * 0.5f, dir, 25f, 16f, Palette.C4Amber, new Vector3(0.11f, 0.65f, 0.11f), 2.0f);
                        }
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.C4Amber);
                    AudioManager.Instance?.PlayEnemyShoot(0.54f, 0.38f);
                    FeelManager.Instance?.TriggerCameraShake(0.24f);
                }
                return;
            }

            _railChargeTimer -= dt;
            if (_railChargeTimer <= 0f)
            {
                _railChargeTimer = 3.8f;
                _railLockActive = true;
                _railLockElapsed = 0f;
                var aim = targetTf.position - origin;
                aim.y = 0f;
                _railLockedDir = aim.sqrMagnitude > 0.01f ? aim.normalized : transform.forward;
                UIManager.Instance?.TriggerThreatWarning("!! RWR ALERT: GOLIATH RAILGUN LASER LOCK !!", 1.45f);
                return;
            }

            // Standard pressure fire between rail snipes
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                _bossCannonTimer = 1.65f;
                FireTracerFromMuzzle(bossMuzzleTip, player, targetTf.position, 14f, 11f, Palette.EnemySecondary, new Vector3(0.08f, 0.36f, 0.08f), 2.5f, 0.72f);
            }

            // Close-range shockwave pulse if player hugs Goliath
            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 4.4f;
                var zoneCenter = dist < 6.5f ? transform.position : targetTf.position;
                EnemySpawner.Instance?.SpawnHazardTelegraph(zoneCenter, dist < 6.5f ? 4.2f : 2.3f, 22f);
            }
        }

        private void StepVolkovShieldBoss(PlayerController player, Transform targetTf, float dt)
        {
            _volkovShieldTimer -= dt;
            if (_volkovShieldTimer <= 0f)
            {
                _volkovShieldActive = !_volkovShieldActive;
                _volkovShieldTimer = _volkovShieldActive ? 5.8f : 4.6f;
                if (bossShieldVisual != null) bossShieldVisual.SetActive(_volkovShieldActive);
                if (_volkovShieldActive)
                {
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 2.0f, "RIOT SHIELD UP!", Palette.C4Amber);
                }
            }

            if (_volkovShieldActive && bossShieldVisual != null)
            {
                bossShieldVisual.transform.Rotate(0f, 90f * dt, 0f, Space.Self);
            }

            // Heavy scatter burst
            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 1.85f;
                var muzzle = ActiveWeaponMuzzle;
                var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.25f;
                var toPlayer = targetTf.position - origin;
                toPlayer.y = 0f;
                var baseDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
                for (var i = -2; i <= 2; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 9f, 0f) * baseDir;
                    player.Weapon.SpawnEnemyProjectile(origin + dir * 0.4f, dir, 14.5f, 9f, Palette.C4Amber, new Vector3(0.08f, 0.34f, 0.08f), 2.2f);
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.C4Amber);
                AudioManager.Instance?.PlayEnemyShoot(0.68f, 0.30f);
            }

            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 4.5f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.45f, 20f);
            }
        }

        private void StepRadekApexBoss(PlayerController player, Transform targetTf, float dt)
        {
            if (_radekPhase2)
            {
                _radekDashCooldown -= dt;
                if (_radekDashCooldown <= 0f)
                {
                    _radekDashCooldown = 3.8f;
                    _radekDashTimer = 0.25f;
                    var toTarget = targetTf.position - transform.position;
                    toTarget.y = 0f;
                    var fwd = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
                    var side = Vector3.Cross(Vector3.up, fwd).normalized * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
                    _radekDashDir = (side * 0.85f + fwd * 0.35f).normalized;
                    VFXManager.Instance?.Play(VfxKind.HitSpark, transform.position + Vector3.up * 0.9f, Palette.M3HeliosCoreCyan);
                }

                // Rapid triple sniper burst in Phase 2
                _bossCannonTimer -= dt;
                if (_bossCannonTimer <= 0f && player.Weapon != null)
                {
                    _bossCannonTimer = 1.35f;
                    var origin = ActiveWeaponMuzzle != null ? ActiveWeaponMuzzle.position : transform.position + Vector3.up * 1.25f;
                    var toPlayer = targetTf.position - origin;
                    toPlayer.y = 0f;
                    var baseDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
                    for (var s = -1; s <= 1; s++)
                    {
                        var dir = Quaternion.Euler(0f, s * 10f, 0f) * baseDir;
                        player.Weapon.SpawnEnemyProjectile(origin + dir * 0.4f, dir, 18.5f, 12f, Palette.M3HeliosCoreCyan, new Vector3(0.09f, 0.42f, 0.09f), 2.3f);
                    }
                    VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.M3HeliosCoreCyan);
                    AudioManager.Instance?.PlayEnemyShoot(0.60f, 0.32f);
                }

                // Twin Helios orbital strikes
                _bossHazardTimer -= dt;
                if (_bossHazardTimer <= 0f)
                {
                    _bossHazardTimer = 3.6f;
                    EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.35f, 22f);
                    EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position + new Vector3(UnityEngine.Random.Range(-4.5f, 4.5f), 0f, UnityEngine.Random.Range(-4.5f, 4.5f)), 2.15f, 18f);
                }
                return;
            }

            // Phase 1: Command Walker rotating 8-way spiral + twin cannon
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0) _bossCannonBurst = 2;
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.22f : 1.85f;
                FireTracerFromMuzzle(bossMuzzleTip, player, targetTf.position, _projectileSpeed, _rangedDamage,
                    Palette.M3HeliosCoreCyan, new Vector3(0.09f, 0.42f, 0.09f), 1.5f, 0.65f);
            }

            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 3.0f;
                _bossBarrageStep++;
                var baseAngle = (_bossBarrageStep % 2 == 0) ? 0f : 22.5f;
                var origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position + Vector3.up * 1.1f;
                for (var i = 0; i < 8; i++)
                {
                    var dir = Quaternion.Euler(0f, baseAngle + i * 45f, 0f) * Vector3.forward;
                    player.Weapon.SpawnEnemyProjectile(origin + dir * 0.45f, dir, 13.0f, 13f, Palette.BossPurple, new Vector3(0.20f, 0.20f, 0.20f), 2.5f);
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.BossPurple);
                AudioManager.Instance?.PlayEnemyShoot(0.62f, 0.34f);
            }

            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 4.4f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.5f, 22f);
            }
        }

        private void FireTracerFromMuzzle(
            Transform muzzle,
            PlayerController player,
            Vector3 targetWorldPos,
            float speed,
            float damage,
            Color tint,
            Vector3 tracerScale,
            float spreadDeg,
            float audioPitch)
        {
            if (player == null || player.Weapon == null) return;

            var spawnPos = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.15f;
            var toTarget = targetWorldPos - spawnPos;
            toTarget.y = 0f;
            var aimDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = Vector3.forward;
            aimDir.Normalize();

            if (spreadDeg > 0.01f)
            {
                aimDir = Quaternion.Euler(0f, UnityEngine.Random.Range(-spreadDeg, spreadDeg), 0f) * aimDir;
            }

            player.Weapon.SpawnEnemyProjectile(spawnPos, aimDir, speed, damage, tint, tracerScale, 2.2f);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, tint);
            if (Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2)
            {
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.042f));
            }
            AudioManager.Instance?.PlayEnemyShoot(audioPitch, Archetype == EnemyArchetype.Boss ? 0.30f : 0.20f);

            ShotsFiredCount++;
        }

        public void ApplyDamage(float dmg, Vector3 hitPoint, Vector3 shotDir, bool isCrit = false, bool isExplosive = false)
        {
            if (IsDead) return;
            _isAlerted = true;

            var effectiveDmg = dmg;
            var labelPrefix = string.Empty;

            if (Archetype == EnemyArchetype.Boss && _bossBehavior == BossBehaviorKind.VolkovJuggernaut && _volkovShieldActive)
            {
                if (isExplosive)
                {
                    _volkovShieldActive = false;
                    _volkovShieldTimer = 6.2f;
                    if (bossShieldVisual != null) bossShieldVisual.SetActive(false);
                    effectiveDmg *= 1.35f;
                    labelPrefix = "SHATTER ";
                    VFXManager.Instance?.Play(VfxKind.LevelUpNova, transform.position + Vector3.up * 1.0f, Palette.C4Amber);
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 2.2f, "SHIELD SHATTERED!", Palette.C4Amber);
                }
                else
                {
                    var flatShot = new Vector3(shotDir.x, 0f, shotDir.z).normalized;
                    var dot = Vector3.Dot(flatShot, transform.forward);
                    // Shot traveling same direction as Volkov faces = hitting his exposed back/flank
                    if (dot > 0.15f)
                    {
                        effectiveDmg *= 1.25f;
                        labelPrefix = "FLANK ";
                    }
                    else
                    {
                        effectiveDmg *= 0.25f;
                        FeelManager.Instance?.SpawnFloatingText(hitPoint + Vector3.up * 0.4f, "SHIELD!", Palette.C4Amber);
                    }
                }
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - effectiveDmg);
            _hitFlashTimer = 0.10f;
            ApplyHitFlash(1f);

            var useBipedalHit = Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2;
            if (useBipedalHit)
            {
                characterMotion?.TriggerSquash(new Vector3(1.22f, 0.80f, 1.22f), 0.12f);
            }

            var dmgLabel = labelPrefix + "-" + Mathf.RoundToInt(effectiveDmg) + (isCrit ? "!" : string.Empty);
            var txtCol = isCrit ? Palette.CreditGold : Palette.EnemySecondary;
            FeelManager.Instance?.SpawnFloatingText(hitPoint, dmgLabel, txtCol);

            if (controller != null && controller.enabled && Archetype != EnemyArchetype.Boss)
            {
                var knock = new Vector3(shotDir.x, 0f, shotDir.z).normalized * (Archetype == EnemyArchetype.Heavy ? 0.12f : 0.24f);
                controller.Move(knock);
            }

            if (Archetype == EnemyArchetype.Boss)
            {
                if (_bossBehavior == BossBehaviorKind.RadekApex && !_radekPhase2 && CurrentHp > 0f && CurrentHp <= MaxHp * 0.52f)
                {
                    TriggerRadekPhase2();
                }
                var label = _radekPhase2 ? "COL. RADEK // APEX-01 OVERCHARGE" : _bossDisplayTitle;
                UIManager.Instance?.UpdateBossHp(CurrentHp, MaxHp, label);
            }

            if (CurrentHp <= 0f)
            {
                Die();
            }
        }

        private void TriggerRadekPhase2()
        {
            _radekPhase2 = true;
            _moveSpeed = 4.45f;
            _bossCannonTimer = 0.6f;
            _bossHazardTimer = 1.8f;
            _radekDashCooldown = 1.4f;

            if (bossVisualRoot != null) bossVisualRoot.SetActive(false);
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(true);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(false);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(true);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(false);
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(true);
            ConfigureHeavyOperatorIk(1.42f);

            var blastPos = transform.position + Vector3.up * 1.0f;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, blastPos, Palette.RocketOrange);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, blastPos, Palette.M3HeliosCoreCyan);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(0.6f);
            StoryDirector.NotifyRadekPhase2();
            UIManager.Instance?.TriggerThreatWarning("!! WARNING: APEX-01 EXO-ARMOR OVERCHARGE !!", 2.5f);
            UIManager.Instance?.ShowMilestoneBanner("PHASE 2 // APEX-01 OVERCHARGE");
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;
            if (railLaserLine != null) railLaserLine.enabled = false;
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);

            var deathPos = transform.position + Vector3.up * 0.9f;
            var vfxCol = Archetype == EnemyArchetype.Boss ? Palette.BossPurple : Palette.EnemySecondary;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, deathPos, vfxCol);
            if (Archetype == EnemyArchetype.Boss)
            {
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, deathPos, Palette.CreditGold);
                UIManager.Instance?.HideBossHp();
            }

            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(Archetype == EnemyArchetype.Boss ? 0.55f : (Archetype == EnemyArchetype.Heavy ? 0.36f : 0.20f));
            if (Archetype != EnemyArchetype.Chaser)
            {
                FeelManager.Instance?.TriggerHitStop(0.055f, 0.05f);
            }

            PlayerController.Instance?.OnEnemyKilled();
            GameManager.Instance?.AddScoreAndKill(_scoreValue);
            _onFinishedCallback?.Invoke(this, true);
            _ownerPool?.Release(this);
        }

        public void ForceReturnToPool()
        {
            if (!gameObject.activeSelf && IsDead) return;
            IsDead = true;
            if (railLaserLine != null) railLaserLine.enabled = false;
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);
            if (Archetype == EnemyArchetype.Boss)
            {
                UIManager.Instance?.HideBossHp();
            }
            _onFinishedCallback?.Invoke(this, false);
            if (_ownerPool != null)
            {
                _ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void ApplyHitFlash(float intensity)
        {
            if (bodyRenderers == null) return;
            _mpb ??= new MaterialPropertyBlock();

            var flashEmission = Palette.Lerp(Palette.Shadow, Palette.CharacterPrimary, intensity) * (intensity * 1.8f);
            if (_radekPhase2 && intensity < 0.05f)
            {
                flashEmission = Palette.M3HeliosCoreCyan * 0.35f;
            }
            foreach (var t in bodyRenderers)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, flashEmission);
                t.SetPropertyBlock(_mpb);
            }
        }
    }
}
