using System;
using System.Collections;
using AI;
using Audio;
using Character;
using CleverCrow.Fluid.BTs.Tasks;
using CleverCrow.Fluid.BTs.Trees;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

namespace Enemies
{
    public enum EnemyArchetype
    {
        Chaser,
        Heavy,
        Boss,
        Sniper,
        Shielder
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

        [SerializeField] private NavMeshAgent navAgent;
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
        [SerializeField] private GameObject sniperWeaponRoot;
        [SerializeField] private Transform sniperLeftGrip;
        [SerializeField] private Transform sniperRightGrip;
        [SerializeField] private Transform sniperMuzzleTip;
        [SerializeField] private GameObject riotShieldVisual;

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

        // Radek & Chen Phase 2 Overcharge
        private bool _radekPhase2;
        private bool _chenPhase2;
        private float _radekDashCooldown;
        private float _radekDashTimer;
        private Vector3 _radekDashDir;

        // Fluid Behavior Tree & NavMesh tactical state
        private BehaviorTree _behaviorTree;
        private PlayerController _framePlayer;
        private Transform _frameTargetTf;
        private PlayerController _frameTargetPlayer;
        private SquadMemberController _frameTargetSquad;
        private float _frameDist;
        private float _frameDt;
        private Vector3 _frameVelocity;
        private Vector3 _activeDest;
        private Vector3 _lastIssuedDest;
        private Vector3 _cachedCoverPos;
        private bool _hasCachedCover;
        private float _coverRefreshTimer;
        private Vector3 _cachedFlankPos;
        private bool _hasCachedFlank;
        private float _flankRefreshTimer;
        private float _repathTimer;
        private float _timeSinceLastHit = 10f;
        private bool _riotShieldIntact;
        private float _sniperAimTimer;
        private bool _sniperLaserLocking;
        private Vector3 _sniperLockedWorldPoint;
        private Vector3 _sniperLockedDir = Vector3.forward;
        private float _grenadeLobCooldown;
        private Coroutine _deathCollapseRoutine;
        private static readonly RaycastHit[] SniperSightRayBuffer = new RaycastHit[8];

        public bool HasIntactRiotShield => Archetype == EnemyArchetype.Shielder && _riotShieldIntact;
        private float _flankSign = 1f;
        private float _stopDistanceJitter;
        private float _turnRate = 8f;
        private float _alertReactionDelay;
        private static int _nextFlankToggle;

        private MaterialPropertyBlock _mpb;
        private Action<EnemyController, bool> _onFinishedCallback;

        public bool IsDead { get; private set; }

        public EnemyArchetype Archetype { get; private set; }

        public string CurrentBehaviorState { get; private set; } = "IDLE";

        public NavMeshAgent NavAgent => navAgent;

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

        public float CurrentHp { get; private set; }

        public float MaxHp { get; private set; }

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
                if (Archetype == EnemyArchetype.Sniper && sniperMuzzleTip != null) return sniperMuzzleTip;
                return Archetype == EnemyArchetype.Heavy ? pkmMuzzleTip : ak74MuzzleTip;
            }
        }

        public void BindExpandedEquipment(
            GameObject sniperRoot,
            Transform sniperLeft,
            Transform sniperRight,
            Transform sniperMuzzle,
            GameObject shieldVisual)
        {
            sniperWeaponRoot = sniperRoot;
            sniperLeftGrip = sniperLeft;
            sniperRightGrip = sniperRight;
            sniperMuzzleTip = sniperMuzzle;
            riotShieldVisual = shieldVisual;
        }

        public void BindComponents(
            NavMeshAgent agent,
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
            navAgent = agent;
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
            if (_deathCollapseRoutine != null)
            {
                StopCoroutine(_deathCollapseRoutine);
                _deathCollapseRoutine = null;
            }
            var rootCol = GetComponent<Collider>();
            if (rootCol != null) rootCol.enabled = true;
            if (bipedalVisualRoot != null)
            {
                bipedalVisualRoot.transform.localPosition = Vector3.zero;
                bipedalVisualRoot.transform.localRotation = Quaternion.identity;
            }
            Archetype = kind;
            _activeWaveNumber = Mathf.Max(1, waveNumber);
            _onFinishedCallback = onFinished;
            IsDead = false;
            _hitFlashTimer = 0f;
            _contactCooldown = 0.25f;
            _fireCooldown = UnityEngine.Random.Range(0.95f, 1.75f);
            _grenadeLobCooldown = UnityEngine.Random.Range(5.5f, 8.5f);
            _burstRemaining = 0;
            _burstTimer = 0f;
            _bossBarrageTimer = 2.0f;
            _bossHazardTimer = 3.5f;
            _bossCannonTimer = 1.25f;
            _bossCannonBurst = 0;
            _railChargeTimer = 3.0f;
            _railLockActive = false;
            _railLockElapsed = 0f;
            _volkovShieldActive = false;
            _volkovShieldTimer = 2.4f;
            _radekPhase2 = false;
            _chenPhase2 = false;
            _radekDashCooldown = 3.2f;
            _radekDashTimer = 0f;
            ShotsFiredCount = 0;
            _isAlerted = kind == EnemyArchetype.Boss;
            _aggroRadius = kind == EnemyArchetype.Boss ? 54f : 26f;
            _repathTimer = UnityEngine.Random.Range(0f, 0.22f);
            _coverRefreshTimer = UnityEngine.Random.Range(0f, 0.35f);
            _hasCachedCover = false;
            _flankRefreshTimer = UnityEngine.Random.Range(0f, 0.30f);
            _hasCachedFlank = false;
            _timeSinceLastHit = 10f;
            _flankSign = (_nextFlankToggle++ & 1) == 0 ? 1f : -1f;
            _stopDistanceJitter = kind == EnemyArchetype.Boss ? 0f : UnityEngine.Random.Range(-0.55f, 0.85f);
            _turnRate = kind == EnemyArchetype.Heavy ? 6.2f : UnityEngine.Random.Range(7.4f, 9.6f);
            _alertReactionDelay = 0f;
            CurrentBehaviorState = _isAlerted ? "BOSS_COMBAT" : "PATROL_STANDBY";
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
            if (_behaviorTree == null) BuildBehaviorTree();

            _bossBehavior = StoryDirector.GetMissionBossBehavior(StoryDirector.ActiveMissionIndex);
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);
            if (railLaserLine != null) railLaserLine.enabled = false;

            var missionScale = 1f + (StoryDirector.ActiveMissionIndex - 1) * 0.10f;
            var waveScale = (1f + (_activeWaveNumber - 1) * 0.11f) * missionScale;

            var bossUsesExoBody = kind == EnemyArchetype.Boss && _bossBehavior == BossBehaviorKind.VolkovJuggernaut;
            _riotShieldIntact = kind == EnemyArchetype.Shielder;
            _sniperLaserLocking = false;
            _sniperAimTimer = 0f;
            if (riotShieldVisual != null) riotShieldVisual.SetActive(kind == EnemyArchetype.Shielder);
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(kind != EnemyArchetype.Boss || bossUsesExoBody);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(kind == EnemyArchetype.Chaser || kind == EnemyArchetype.Sniper);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(kind == EnemyArchetype.Heavy || kind == EnemyArchetype.Shielder || bossUsesExoBody);
            if (bossVisualRoot != null) bossVisualRoot.SetActive(kind == EnemyArchetype.Boss && !bossUsesExoBody);
            if (sniperWeaponRoot != null) sniperWeaponRoot.SetActive(kind == EnemyArchetype.Sniper);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(kind == EnemyArchetype.Chaser || kind == EnemyArchetype.Shielder || (kind == EnemyArchetype.Sniper && sniperWeaponRoot == null));
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(kind == EnemyArchetype.Heavy || bossUsesExoBody);

            switch (kind)
            {
                case EnemyArchetype.Boss:
                    MaxHp = _bossBehavior switch
                    {
                        BossBehaviorKind.WardenBarge => 520f,
                        BossBehaviorKind.GoliathRail => 660f,
                        BossBehaviorKind.VolkovJuggernaut => 790f,
                        BossBehaviorKind.RadekApex => 890f,
                        _ => 1040f
                    };
                    _moveSpeed = _bossBehavior switch
                    {
                        BossBehaviorKind.VolkovJuggernaut => 3.35f,
                        BossBehaviorKind.RadekApex => 3.05f,
                        BossBehaviorKind.ChenOverwatch => 3.15f,
                        _ => 2.45f
                    };
                    _contactDamage = 28f;
                    _rangedDamage = 10f + (StoryDirector.ActiveMissionIndex - 1) * 1.5f;
                    _projectileSpeed = 25f;
                    _scoreValue = 1800;
                    _bossDisplayTitle = StoryDirector.GetBossCallsign(_activeWaveNumber);
                    if (bossUsesExoBody)
                    {
                        ConfigureHeavyOperatorIk(1.46f);
                    }
                    else
                    {
                        transform.localScale = Vector3.one * (_bossBehavior is BossBehaviorKind.RadekApex or BossBehaviorKind.ChenOverwatch ? 1.26f : 1.18f);
                    }
                    UIManager.Instance?.UpdateBossHp(MaxHp, MaxHp, _bossDisplayTitle);
                    break;

                case EnemyArchetype.Heavy:
                    MaxHp = 112f * waveScale;
                    _moveSpeed = 3.15f + Mathf.Min(1.0f, _activeWaveNumber * 0.12f);
                    _contactDamage = 20f;
                    _rangedDamage = 6f + Mathf.Min(3.5f, _activeWaveNumber * 0.35f);
                    _projectileSpeed = 26f + Mathf.Min(3.5f, _activeWaveNumber * 0.40f);
                    _scoreValue = 250;
                    ConfigureHeavyOperatorIk(1.22f);
                    break;

                case EnemyArchetype.Sniper:
                    MaxHp = 38f * waveScale;
                    _moveSpeed = 3.95f + Mathf.Min(1.0f, _activeWaveNumber * 0.10f);
                    _contactDamage = 8f;
                    _rangedDamage = 24f + Mathf.Min(8f, _activeWaveNumber * 1.4f);
                    _projectileSpeed = 62f;
                    _scoreValue = 220;
                    _aggroRadius = Mathf.Max(_aggroRadius, 32f);
                    transform.localScale = Vector3.one * 0.96f;
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
                        weaponAnchor.localPosition = new Vector3(0.12f, 1.14f, 0.36f);
                    }
                    characterMotion?.ConfigureWeaponIkPose(
                        sniperLeftGrip != null ? sniperLeftGrip : ak74LeftGrip,
                        sniperRightGrip != null ? sniperRightGrip : ak74RightGrip,
                        0.34f, 0.35f);
                    break;

                case EnemyArchetype.Shielder:
                    MaxHp = 132f * waveScale;
                    _moveSpeed = 3.35f + Mathf.Min(0.8f, _activeWaveNumber * 0.10f);
                    _contactDamage = 18f;
                    _rangedDamage = 5.5f + Mathf.Min(2.5f, _activeWaveNumber * 0.25f);
                    _projectileSpeed = 23f;
                    _scoreValue = 260;
                    ConfigureHeavyOperatorIk(1.16f);
                    characterMotion?.ConfigureWeaponIkPose(ak74LeftGrip, ak74RightGrip, 0.34f * 1.16f, 0.35f * 1.16f);
                    break;

                default:
                    MaxHp = 46f * waveScale;
                    _moveSpeed = 4.35f + Mathf.Min(1.5f, _activeWaveNumber * 0.16f);
                    _contactDamage = 12f;
                    _rangedDamage = 6.5f + Mathf.Min(3.2f, _activeWaveNumber * 0.30f);
                    _projectileSpeed = 24f + Mathf.Min(3.5f, _activeWaveNumber * 0.40f);
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

            var targetSpawn = new Vector3(spawnPos.x, 0.02f, spawnPos.z);
            if (navAgent != null && navAgent.enabled)
            {
                navAgent.speed = _moveSpeed;
                navAgent.radius = kind == EnemyArchetype.Boss ? 0.65f : (kind == EnemyArchetype.Heavy ? 0.52f : 0.44f);
                if (!TacticalNavigationUtility.EnsureAgentOnNavMesh(navAgent, targetSpawn, 8f))
                {
                    transform.position = targetSpawn;
                }
                else if (navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = false;
                    navAgent.ResetPath();
                    navAgent.velocity = Vector3.zero;
                }
            }
            else
            {
                transform.position = targetSpawn;
            }

            _lastIssuedDest = transform.position;
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

        private void Awake()
        {
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
            if (_behaviorTree == null) BuildBehaviorTree();
        }

        private void BuildBehaviorTree()
        {
            _behaviorTree = new BehaviorTreeBuilder(gameObject)
                .Selector("EnemyTacticalRoot")
                    .Sequence("StandbyPatrolUnalerted")
                        .Condition("IsUnalertedOrNoTarget", () => _frameTargetTf == null || !_isAlerted)
                        .Do("HoldWatch", StepIdleSentryState)
                    .End()
                    .Sequence("BossApexCombat")
                        .Condition("IsBossArchetype", () => Archetype == EnemyArchetype.Boss)
                        .Do("StepBossCombat", StepBossTacticalNode)
                    .End()
                    .Sequence("SeekCoverWhenWounded")
                        .Condition("NeedsTacticalCover", ShouldSeekCover)
                        .Do("RetreatToCover", () => ExecuteInfantryBehaviorNode(_activeDest, 1.12f, allowFire: false, "TAKE_COVER"))
                    .End()
                    .Sequence("FlankBlockedLineOfSight")
                        .Condition("ShouldFlankObstacle", ShouldFlankTarget)
                        .Do("ExecuteFlank", () => ExecuteInfantryBehaviorNode(_activeDest, 1.06f, allowFire: true, "FLANK_LOS"))
                    .End()
                    .Sequence("SuppressiveFireFromRange")
                        .Condition("InRangeWithClearLoS", () => _frameDist <= GetMinStopDistance() && HasClearLineOfSightToTarget())
                        .Do("HoldAndSuppress", () => ExecuteInfantryBehaviorNode(transform.position, 0f, allowFire: true, "SUPPRESS_FIRE"))
                    .End()
                    .Sequence("AdvanceAlongNavMeshCorridor")
                        .Do("AdvanceCorridor", () => ExecuteInfantryBehaviorNode(_frameTargetTf.position, 1.0f, allowFire: true, "ADVANCE_CORRIDOR"))
                    .End()
                .End()
                .Build();
        }

        private void Update()
        {
            if (IsDead) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                if (railLaserLine != null) railLaserLine.enabled = false;
                HaltNavAgent();
                characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: true);
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            _frameDt = dt;
            _repathTimer -= dt;
            _coverRefreshTimer -= dt;
            _flankRefreshTimer -= dt;
            _timeSinceLastHit += dt;

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(_hitFlashTimer / 0.10f));
            }

            if (_contactCooldown > 0f)
            {
                _contactCooldown -= dt;
            }

            _framePlayer = PlayerController.Instance;
            _frameVelocity = Vector3.zero;
            ResolveCombatTarget(_framePlayer, out _frameTargetTf, out _frameTargetPlayer, out _frameTargetSquad, out _frameDist);
            if (_alertReactionDelay > 0f)
            {
                _alertReactionDelay -= dt;
                if (_alertReactionDelay <= 0f)
                {
                    _isAlerted = true;
                }
            }
            else if (!_isAlerted && _frameTargetTf != null && _frameDist <= _aggroRadius)
            {
                _alertReactionDelay = UnityEngine.Random.Range(0.10f, 0.30f);
                AlertNearbyAllies(11.5f);
            }

            _behaviorTree?.Tick();

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            var useBipedalMotion = Archetype != EnemyArchetype.Boss
                || _bossBehavior == BossBehaviorKind.VolkovJuggernaut
                || (_bossBehavior == BossBehaviorKind.RadekApex && _radekPhase2)
                || (_bossBehavior == BossBehaviorKind.ChenOverwatch && _chenPhase2);
            if (useBipedalMotion)
            {
                characterMotion?.StepMotion(_frameVelocity, yawRate, isAimingWeapon: true);
            }
        }

        private TaskStatus StepIdleSentryState()
        {
            HaltNavAgent();
            CurrentBehaviorState = "PATROL_STANDBY";
            return TaskStatus.Success;
        }

        private TaskStatus StepBossTacticalNode()
        {
            CurrentBehaviorState = "BOSS_COMBAT";
            if (_frameTargetTf == null)
            {
                HaltNavAgent();
                return TaskStatus.Success;
            }

            FaceTarget(_frameTargetTf.position, 8f, _frameDt);
            var minStopDist = GetMinStopDistance();

            if (_radekDashTimer > 0f)
            {
                _radekDashTimer -= _frameDt;
                var dashVel = _radekDashDir * 14.0f;
                if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = true;
                    navAgent.Move(dashVel * _frameDt);
                }
                else
                {
                    transform.position += dashVel * _frameDt;
                }
                _frameVelocity = dashVel;
            }
            else if (_frameDist > minStopDist)
            {
                _frameVelocity = DriveNavMovement(_frameTargetTf.position, _moveSpeed, _frameDt);
            }
            else
            {
                HaltNavAgent();
            }

            StepContactMeleeDamage();
            StepBossAttacks(_framePlayer, _frameTargetTf, _frameDist, _frameDt);
            return TaskStatus.Success;
        }

        private TaskStatus ExecuteInfantryBehaviorNode(Vector3 destination, float speedMultiplier, bool allowFire, string stateName)
        {
            CurrentBehaviorState = stateName;
            if (_frameTargetTf == null)
            {
                HaltNavAgent();
                return TaskStatus.Success;
            }

            FaceTarget(_frameTargetTf.position, _turnRate, _frameDt);
            if (speedMultiplier > 0.01f && Vector3.Distance(transform.position, destination) > 0.45f)
            {
                _frameVelocity = DriveNavMovement(destination, _moveSpeed * speedMultiplier, _frameDt);
            }
            else
            {
                HaltNavAgent();
            }

            StepContactMeleeDamage();
            if (allowFire)
            {
                StepInfantryFire(_framePlayer, _frameTargetTf, _frameDist, _frameDt);
            }
            return TaskStatus.Success;
        }

        private float GetMinStopDistance()
        {
            return Archetype switch
            {
                EnemyArchetype.Boss => _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 ? 5.0f : 4.5f,
                EnemyArchetype.Heavy => 5.2f + _stopDistanceJitter,
                EnemyArchetype.Sniper => 13.5f + _stopDistanceJitter,
                EnemyArchetype.Shielder => 3.1f + _stopDistanceJitter,
                _ => 4.2f + _stopDistanceJitter
            };
        }

        private bool ShouldSeekCover()
        {
            if (Archetype == EnemyArchetype.Boss || Archetype == EnemyArchetype.Shielder || _sniperLaserLocking || _frameTargetTf == null) return false;
            if (CurrentHp > MaxHp * 0.45f || _burstRemaining > 0) return false;
            if (_fireCooldown <= 0.35f && _timeSinceLastHit > 2.2f) return false;

            if (_hasCachedCover && _coverRefreshTimer > 0f)
            {
                _activeDest = _cachedCoverPos;
                return true;
            }

            if (TacticalNavigationUtility.FindTacticalCoverPosition(transform.position, _frameTargetTf.position, 9.0f, 5.0f, out var coverPos))
            {
                _cachedCoverPos = coverPos;
                _hasCachedCover = true;
                _coverRefreshTimer = 1.5f;
                _activeDest = coverPos;
                return true;
            }

            _hasCachedCover = false;
            return false;
        }

        private bool ShouldFlankTarget()
        {
            if (_frameTargetTf == null) return false;
            var maxEngageRange = Archetype == EnemyArchetype.Heavy ? 17.5f : 16.0f;
            if (_frameDist > maxEngageRange || _frameDist <= 2.4f) return false;
            if (HasClearLineOfSightToTarget()) return false;

            if (_hasCachedFlank && _flankRefreshTimer > 0f && Vector3.Distance(transform.position, _cachedFlankPos) > 0.9f)
            {
                _activeDest = _cachedFlankPos;
                return true;
            }

            if (TacticalNavigationUtility.FindFlankPosition(transform.position, _frameTargetTf.position, _flankSign, 6.0f, out var flankPos)
                || TacticalNavigationUtility.FindFlankPosition(transform.position, _frameTargetTf.position, -_flankSign, 6.0f, out flankPos))
            {
                _cachedFlankPos = flankPos;
                _hasCachedFlank = true;
                _flankRefreshTimer = 1.1f;
                _activeDest = flankPos;
                return true;
            }

            _hasCachedFlank = false;
            return false;
        }

        private bool HasClearLineOfSightToTarget()
        {
            if (_frameTargetTf == null) return false;
            var origin = ActiveWeaponMuzzle != null ? ActiveWeaponMuzzle.position : transform.position + Vector3.up * 1.15f;
            return TacticalNavigationUtility.HasClearLineOfSight(origin, _frameTargetTf.position + Vector3.up * 0.9f);
        }

        private Vector3 DriveNavMovement(Vector3 destination, float speed, float dt)
        {
            if (navAgent != null && navAgent.enabled)
            {
                if (!navAgent.isOnNavMesh)
                {
                    TacticalNavigationUtility.EnsureAgentOnNavMesh(navAgent, transform.position, 6f);
                }

                if (navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = false;
                    navAgent.speed = speed;
                    if (_repathTimer <= 0f || Vector3.Distance(_lastIssuedDest, destination) > 0.65f || !navAgent.hasPath)
                    {
                        var targetPos = TacticalNavigationUtility.TryGetReachablePoint(transform.position, destination, out var reachable, 4.5f)
                            ? reachable
                            : destination;
                        navAgent.SetDestination(targetPos);
                        _lastIssuedDest = destination;
                        _repathTimer = 0.16f;
                    }
                    return navAgent.velocity;
                }
            }

            var toDest = destination - transform.position;
            toDest.y = 0f;
            if (toDest.sqrMagnitude <= 0.04f) return Vector3.zero;
            var vel = toDest.normalized * speed;
            transform.position += vel * dt;
            return vel;
        }

        private void HaltNavAgent()
        {
            if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh) return;
            navAgent.isStopped = true;
            if (navAgent.hasPath) navAgent.ResetPath();
            navAgent.velocity = Vector3.zero;
        }

        private Vector3 ComputeBarrelAimDirection(Vector3 targetWorldPos, Transform muzzle)
        {
            var centerToTarget = targetWorldPos - transform.position;
            centerToTarget.y = 0f;
            var dist = centerToTarget.magnitude;
            if (dist < 0.05f) return transform.forward;

            var lateralOffset = muzzle != null ? transform.InverseTransformPoint(muzzle.position).x : 0.11f;
            var bearingRad = Mathf.Atan2(centerToTarget.x, centerToTarget.z);
            if (!(dist > Mathf.Abs(lateralOffset) + 0.06f)) return centerToTarget / dist;
            var parallaxRad = Mathf.Asin(Mathf.Clamp(lateralOffset / dist, -0.92f, 0.92f));
            var correctedRad = bearingRad - parallaxRad;
            return new Vector3(Mathf.Sin(correctedRad), 0f, Mathf.Cos(correctedRad));
        }

        private Vector3 GetFlatMuzzleForward(Transform muzzle)
        {
            if (muzzle != null)
            {
                var fwd = new Vector3(muzzle.forward.x, 0f, muzzle.forward.z);
                if (fwd.sqrMagnitude > 0.0001f) return fwd.normalized;
            }
            var bodyFwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return bodyFwd.sqrMagnitude > 0.0001f ? bodyFwd.normalized : Vector3.forward;
        }

        private void FaceTarget(Vector3 targetPos, float turnRate, float dt)
        {
            var muzzle = ActiveWeaponMuzzle;
            var dir = (muzzle != null && (Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 || _chenPhase2))
                ? ComputeBarrelAimDirection(targetPos, muzzle)
                : (targetPos - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.002f) return;
            var targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnRate * dt));
        }

        private void StepContactMeleeDamage()
        {
            var hitDist = Archetype == EnemyArchetype.Boss ? 1.85f : 1.25f;
            if (_frameDist > hitDist || _contactCooldown > 0f) return;
            _contactCooldown = 0.85f;
            if (_frameTargetPlayer != null) _frameTargetPlayer.TakeDamage(_contactDamage, transform.position);
            else if (_frameTargetSquad != null) _frameTargetSquad.TakeDamage(_contactDamage);
        }

        public void SetGarrisonAlert(bool alertedImmediately, float customAggroRadius = 24f)
        {
            _isAlerted = alertedImmediately;
            _alertReactionDelay = 0f;
            _aggroRadius = Archetype == EnemyArchetype.Sniper ? Mathf.Max(32f, customAggroRadius) : Mathf.Max(4f, customAggroRadius);
        }

        private void AlertNearbyAllies(float radius)
        {
            if (EnemySpawner.Instance == null) return;
            var enemies = EnemySpawner.Instance.ActiveEnemies;
            var rSq = radius * radius;
            for (var i = 0; i < enemies.Count; i++)
            {
                var ally = enemies[i];
                if (ally == null || ally == this || ally.IsDead || ally._isAlerted || ally._alertReactionDelay > 0f) continue;
                var d = ally.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= rSq)
                {
                    ally._alertReactionDelay = UnityEngine.Random.Range(0.18f, 0.45f);
                }
            }
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
            for (var i = 0; i < squad.Count; i++)
            {
                var member = squad[i];
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
            if (Archetype == EnemyArchetype.Sniper)
            {
                StepSniperFire(player, targetTf, distToTarget, dt);
                return;
            }

            var maxRange = Archetype == EnemyArchetype.Heavy ? 15.8f : 14.5f;
            if (distToTarget > maxRange || player == null || player.Weapon == null || targetTf == null)
            {
                _burstRemaining = 0;
                return;
            }

            if (Archetype == EnemyArchetype.Heavy && distToTarget is >= 5.5f and <= 14.5f)
            {
                _grenadeLobCooldown -= dt;
                if (_grenadeLobCooldown <= 0f)
                {
                    _grenadeLobCooldown = UnityEngine.Random.Range(7.5f, 10.5f);
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 1.9f, "FRAG OUT!", Palette.C4Amber);
                    EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.15f, 18f);
                }
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
                    new Vector3(0.036f, 0.36f, 0.036f),
                    3.0f,
                    0.76f);
                return;
            }

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            var muzzlePos = ActiveWeaponMuzzle != null ? ActiveWeaponMuzzle.position : transform.position + Vector3.up * 1.15f;
            if (!TacticalNavigationUtility.HasClearLineOfSight(muzzlePos, targetTf.position + Vector3.up * 0.9f))
            {
                _fireCooldown = 0.25f;
                return;
            }

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
                    new Vector3(0.030f, 0.34f, 0.030f),
                    2.0f,
                    0.96f);
            }
        }

        private void StepSniperFire(PlayerController player, Transform targetTf, float distToTarget, float dt)
        {
            if (distToTarget > 25.5f || player == null || player.Weapon == null || targetTf == null)
            {
                _sniperLaserLocking = false;
                if (railLaserLine != null) railLaserLine.enabled = false;
                return;
            }

            var muzzlePos = ActiveWeaponMuzzle != null ? ActiveWeaponMuzzle.position : transform.position + Vector3.up * 1.18f;
            if (_sniperLaserLocking)
            {
                _sniperAimTimer -= dt;
                if (_sniperAimTimer > 0.18f)
                {
                    _sniperLockedWorldPoint = targetTf.position + Vector3.up * 0.92f;
                }
                var desiredAim = ComputeBarrelAimDirection(_sniperLockedWorldPoint, ActiveWeaponMuzzle);
                if (desiredAim.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(desiredAim, Vector3.up);
                    characterMotion?.AlignWeaponBarrelWithBodyYaw();
                }
                muzzlePos = ActiveWeaponMuzzle != null ? ActiveWeaponMuzzle.position : transform.position + Vector3.up * 1.18f;
                _sniperLockedDir = GetFlatMuzzleForward(ActiveWeaponMuzzle);

                if (railLaserLine != null)
                {
                    const float maxSightRange = 25.5f;
                    var beamEnd = muzzlePos + _sniperLockedDir * maxSightRange;
                    var hitCount = Physics.RaycastNonAlloc(muzzlePos, _sniperLockedDir, SniperSightRayBuffer, maxSightRange, ~0, QueryTriggerInteraction.Ignore);
                    var bestDist = maxSightRange;
                    for (var i = 0; i < hitCount; i++)
                    {
                        var h = SniperSightRayBuffer[i];
                        if (h.collider == null || h.collider.GetComponentInParent<EnemyController>() != null) continue;
                        if (h.distance < bestDist)
                        {
                            bestDist = h.distance;
                            beamEnd = new Vector3(h.point.x, muzzlePos.y, h.point.z);
                        }
                    }

                    var col = Palette.EnemySecondary;
                    railLaserLine.enabled = true;
                    railLaserLine.startWidth = 0.018f;
                    railLaserLine.endWidth = 0.008f;
                    railLaserLine.startColor = new Color(col.r, col.g, col.b, 0.82f);
                    railLaserLine.endColor = new Color(col.r, col.g, col.b, 0.16f);
                    railLaserLine.SetPosition(0, muzzlePos);
                    railLaserLine.SetPosition(1, beamEnd);
                }

                if (_sniperAimTimer <= 0f)
                {
                    _sniperLaserLocking = false;
                    if (railLaserLine != null) railLaserLine.enabled = false;
                    FireTracerFromMuzzle(
                        ActiveWeaponMuzzle,
                        player,
                        _sniperLockedWorldPoint,
                        _projectileSpeed,
                        _rangedDamage,
                        Palette.EnemySecondary,
                        new Vector3(0.036f, 0.88f, 0.036f),
                        0f,
                        0.72f);
                    _fireCooldown = UnityEngine.Random.Range(2.45f, 3.05f);
                }
                return;
            }

            if (railLaserLine != null) railLaserLine.enabled = false;
            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            if (!TacticalNavigationUtility.HasClearLineOfSight(muzzlePos, targetTf.position + Vector3.up * 0.9f))
            {
                _fireCooldown = 0.25f;
                return;
            }

            _sniperLaserLocking = true;
            _sniperAimTimer = 0.90f;
            _sniperLockedWorldPoint = targetTf.position + Vector3.up * 0.92f;
            var initAim = ComputeBarrelAimDirection(_sniperLockedWorldPoint, ActiveWeaponMuzzle);
            if (initAim.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(initAim, Vector3.up);
                characterMotion?.AlignWeaponBarrelWithBodyYaw();
            }
            _sniperLockedDir = GetFlatMuzzleForward(ActiveWeaponMuzzle);
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
                case BossBehaviorKind.ChenOverwatch:
                    StepChenOverwatchBoss(player, targetTf, dt);
                    break;
                default:
                    StepRadekApexBoss(player, targetTf, dt);
                    break;
            }
        }

        private Vector3 GetTurretForwardOrTarget(Transform targetTf)
        {
            if (bossTurretPivot != null && bossVisualRoot != null && bossVisualRoot.activeSelf)
            {
                var fwd = bossTurretPivot.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.001f) return fwd.normalized;
            }
            if (targetTf != null)
            {
                var origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position;
                var toTarget = targetTf.position - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.001f) return toTarget.normalized;
            }
            return transform.forward;
        }

        private void FireDirectionalSalvo(
            Transform muzzle,
            Vector3 forwardDir,
            int count,
            float spreadArcDeg,
            PlayerController player,
            float speed,
            float damage,
            Color tint,
            Vector3 tracerScale,
            float audioPitch)
        {
            if (player == null || player.Weapon == null || count <= 0) return;
            var wishDir = new Vector3(forwardDir.x, 0f, forwardDir.z);
            wishDir = wishDir.sqrMagnitude > 0.001f ? wishDir.normalized : transform.forward;
            if (muzzle != bossMuzzleTip && (Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 || _chenPhase2))
            {
                var originPreview = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.15f;
                var barrelAim = ComputeBarrelAimDirection(originPreview + wishDir * 12f, muzzle);
                if (barrelAim.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(barrelAim, Vector3.up);
                    characterMotion?.AlignWeaponBarrelWithBodyYaw();
                }
            }

            var origin = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.15f;
            var baseDir = muzzle != null ? GetFlatMuzzleForward(muzzle) : wishDir;

            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0f : (i / (float)(count - 1)) - 0.5f;
                var angle = t * spreadArcDeg;
                var dir = Quaternion.Euler(0f, angle, 0f) * baseDir;
                player.Weapon.SpawnEnemyProjectile(origin + dir * 0.42f, dir, speed, damage, tint, tracerScale, 2.3f);
            }
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, tint);
            AudioManager.Instance?.PlayEnemyShoot(audioPitch, 0.32f);
            ShotsFiredCount++;
        }

        private void StepWardenBargeBoss(PlayerController player, Transform targetTf, float dt)
        {
            var turretDir = GetTurretForwardOrTarget(targetTf);

            // Coaxial 20mm 3-round turret burst along barrel bore
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0) _bossCannonBurst = 3;
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.18f : 1.75f;
                FireTracerFromMuzzle(bossMuzzleTip, player, bossMuzzleTip != null ? bossMuzzleTip.position + turretDir * 10f : targetTf.position,
                    _projectileSpeed, _rangedDamage, Palette.C4Amber, new Vector3(0.08f, 0.40f, 0.08f), 2.2f, 0.66f);
            }

            // Focused forward 3-round AP turret salvo along barrel bore
            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 3.1f;
                FireDirectionalSalvo(bossMuzzleTip, turretDir, 3, 15f, player, 14.5f, 12f,
                    Palette.EnemySecondary, new Vector3(0.09f, 0.42f, 0.09f), 0.60f);
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
                    railLaserLine.startWidth = 0.10f;
                    railLaserLine.endWidth = 0.05f;
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

            // Suppressive 3-round PKM burst aimed downrange
            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 1.65f;
                var muzzle = ActiveWeaponMuzzle;
                var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.25f;
                FireDirectionalSalvo(muzzle, targetTf.position - origin, 3, 9f, player, 15.5f, 9.5f,
                    Palette.C4Amber, new Vector3(0.08f, 0.36f, 0.08f), 0.68f);
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

                // Tight 3-round precision burst in Phase 2
                _bossCannonTimer -= dt;
                if (_bossCannonTimer <= 0f && player.Weapon != null)
                {
                    _bossCannonTimer = 1.28f;
                    var muzzle = ActiveWeaponMuzzle;
                    var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.25f;
                    FireDirectionalSalvo(muzzle, targetTf.position - origin, 3, 11f, player, 18.5f, 11.5f,
                        Palette.M3HeliosCoreCyan, new Vector3(0.08f, 0.42f, 0.08f), 0.60f);
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

            var turretDir = GetTurretForwardOrTarget(targetTf);

            // Phase 1: IFV 30mm autocannon bursts + tight frontal rocket salvo along turret barrel
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0) _bossCannonBurst = 3;
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.18f : 1.70f;
                FireTracerFromMuzzle(bossMuzzleTip, player, bossMuzzleTip != null ? bossMuzzleTip.position + turretDir * 10f : targetTf.position,
                    _projectileSpeed, _rangedDamage, Palette.M3HeliosCoreCyan, new Vector3(0.08f, 0.42f, 0.08f), 1.8f, 0.65f);
            }

            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 2.85f;
                FireDirectionalSalvo(bossMuzzleTip, turretDir, 3, 14f, player, 16.0f, 12.5f,
                    Palette.C4Amber, new Vector3(0.09f, 0.44f, 0.09f), 0.58f);
            }

            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 4.4f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.5f, 22f);
            }
        }

        private void StepChenOverwatchBoss(PlayerController player, Transform targetTf, float dt)
        {
            _volkovShieldTimer -= dt;
            if (_volkovShieldTimer <= 0f)
            {
                _volkovShieldActive = !_volkovShieldActive;
                _volkovShieldTimer = _volkovShieldActive ? (_chenPhase2 ? 3.8f : 4.6f) : (_chenPhase2 ? 4.4f : 5.2f);
                if (bossShieldVisual != null) bossShieldVisual.SetActive(_volkovShieldActive);
                if (_volkovShieldActive)
                {
                    FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 2.1f, "AEON KINETIC BARRIER!", Palette.EnemySecondary);
                }
            }

            if (_volkovShieldActive && bossShieldVisual != null)
            {
                bossShieldVisual.transform.Rotate(0f, -110f * dt, 0f, Space.Self);
            }

            if (_chenPhase2)
            {
                _radekDashCooldown -= dt;
                if (_radekDashCooldown <= 0f)
                {
                    _radekDashCooldown = 3.2f;
                    _radekDashTimer = 0.28f;
                    var toTarget = targetTf.position - transform.position;
                    toTarget.y = 0f;
                    var fwd = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
                    var side = Vector3.Cross(Vector3.up, fwd).normalized * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
                    _radekDashDir = (side * 0.90f + fwd * 0.30f).normalized;
                    VFXManager.Instance?.Play(VfxKind.HitSpark, transform.position + Vector3.up * 0.9f, Palette.EnemySecondary);
                }

                _bossCannonTimer -= dt;
                if (_bossCannonTimer <= 0f && player.Weapon != null)
                {
                    _bossCannonTimer = 1.15f;
                    var muzzle = ActiveWeaponMuzzle;
                    var origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.25f;
                    FireDirectionalSalvo(muzzle, targetTf.position - origin, 3, 10f, player, 19.5f, 11.5f,
                        Palette.EnemySecondary, new Vector3(0.09f, 0.44f, 0.09f), 0.56f);
                }

                _bossHazardTimer -= dt;
                if (_bossHazardTimer <= 0f)
                {
                    _bossHazardTimer = 3.2f;
                    EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.45f, 22f);
                    EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position + new Vector3(UnityEngine.Random.Range(-5f, 5f), 0f, UnityEngine.Random.Range(-5f, 5f)), 2.20f, 18f);
                }
                return;
            }

            var turretDir = GetTurretForwardOrTarget(targetTf);

            // Phase 1: Command APC 30mm autocannon bursts + focused 4-round frontal salvo strictly along turret barrel
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0) _bossCannonBurst = 3;
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.17f : 1.65f;
                FireTracerFromMuzzle(bossMuzzleTip, player, bossMuzzleTip != null ? bossMuzzleTip.position + turretDir * 10f : targetTf.position,
                    _projectileSpeed, _rangedDamage, Palette.EnemySecondary, new Vector3(0.09f, 0.42f, 0.09f), 1.6f, 0.62f);
            }

            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f && player.Weapon != null)
            {
                _bossBarrageTimer = 2.75f;
                FireDirectionalSalvo(bossMuzzleTip, turretDir, 4, 14f, player, 16.5f, 11.5f,
                    Palette.C4Amber, new Vector3(0.09f, 0.46f, 0.09f), 0.56f);
            }

            _bossHazardTimer -= dt;
            if (_bossHazardTimer <= 0f)
            {
                _bossHazardTimer = 3.9f;
                EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.55f, 22f);
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

            if (Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 || _chenPhase2)
            {
                var barrelAim = ComputeBarrelAimDirection(targetWorldPos, muzzle);
                if (barrelAim.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(barrelAim, Vector3.up);
                    characterMotion?.AlignWeaponBarrelWithBodyYaw();
                }
            }
            else if (bossTurretPivot != null && bossVisualRoot != null && bossVisualRoot.activeSelf && muzzle == bossMuzzleTip)
            {
                var turretAim = targetWorldPos - bossTurretPivot.position;
                turretAim.y = 0f;
                if (turretAim.sqrMagnitude > 0.001f)
                {
                    bossTurretPivot.rotation = Quaternion.LookRotation(turretAim.normalized, Vector3.up);
                }
            }

            var spawnPos = muzzle != null ? muzzle.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.15f;
            var aimDir = GetFlatMuzzleForward(muzzle);

            player.Weapon.SpawnEnemyProjectile(spawnPos, aimDir, speed, damage, tint, tracerScale, 2.2f);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, tint);
            if (Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 || _chenPhase2)
            {
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.042f));
            }
            AudioManager.Instance?.PlayEnemyShoot(audioPitch, Archetype == EnemyArchetype.Boss ? 0.30f : 0.20f);

            ShotsFiredCount++;
        }

        public void ApplyDamage(float dmg, Vector3 hitPoint, Vector3 shotDir, bool isCrit = false, bool isExplosive = false)
        {
            if (IsDead) return;
            if (!_isAlerted)
            {
                _isAlerted = true;
                _alertReactionDelay = 0f;
                AlertNearbyAllies(11.5f);
            }
            _timeSinceLastHit = 0f;
            _hasCachedCover = false;

            var effectiveDmg = dmg;
            var labelPrefix = string.Empty;

            if (Archetype == EnemyArchetype.Boss && _bossBehavior == BossBehaviorKind.VolkovJuggernaut && _volkovShieldActive)
            {
                EvaluateDirectionalShield(ref effectiveDmg, ref labelPrefix, ref _volkovShieldActive, bossShieldVisual, shotDir, hitPoint, isExplosive);
                if (!_volkovShieldActive)
                {
                    _volkovShieldTimer = 6.2f;
                }
            }
            else if (Archetype == EnemyArchetype.Boss && _bossBehavior == BossBehaviorKind.ChenOverwatch && _volkovShieldActive)
            {
                EvaluateDirectionalShield(ref effectiveDmg, ref labelPrefix, ref _volkovShieldActive, bossShieldVisual, shotDir, hitPoint, isExplosive);
                if (!_volkovShieldActive)
                {
                    _volkovShieldTimer = 6.5f;
                }
            }
            else if (Archetype == EnemyArchetype.Shielder && _riotShieldIntact)
            {
                EvaluateDirectionalShield(ref effectiveDmg, ref labelPrefix, ref _riotShieldIntact, riotShieldVisual, shotDir, hitPoint, isExplosive);
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - effectiveDmg);
            _hitFlashTimer = 0.10f;
            ApplyHitFlash(1f);

            var useBipedalHit = Archetype != EnemyArchetype.Boss || _bossBehavior == BossBehaviorKind.VolkovJuggernaut || _radekPhase2 || _chenPhase2;
            if (useBipedalHit)
            {
                characterMotion?.TriggerSquash(new Vector3(1.22f, 0.80f, 1.22f), 0.12f);
            }

            var dmgLabel = labelPrefix + "-" + Mathf.RoundToInt(effectiveDmg) + (isCrit ? "!" : string.Empty);
            var txtCol = isCrit ? Palette.CreditGold : Palette.EnemySecondary;
            FeelManager.Instance?.SpawnFloatingText(hitPoint, dmgLabel, txtCol);

            if (Archetype != EnemyArchetype.Boss && navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
            {
                var knock = new Vector3(shotDir.x, 0f, shotDir.z).normalized * (Archetype == EnemyArchetype.Heavy ? 0.12f : 0.24f);
                navAgent.Move(knock);
            }

            if (Archetype == EnemyArchetype.Boss)
            {
                if (_bossBehavior == BossBehaviorKind.RadekApex && !_radekPhase2 && CurrentHp > 0f && CurrentHp <= MaxHp * 0.52f)
                {
                    TriggerRadekPhase2();
                }
                else if (_bossBehavior == BossBehaviorKind.ChenOverwatch && !_chenPhase2 && CurrentHp > 0f && CurrentHp <= MaxHp * 0.52f)
                {
                    TriggerChenPhase2();
                }
                var label = _radekPhase2
                    ? "RADEK // APEX OVERCHARGE"
                    : (_chenPhase2 ? "CHEN // AEON OVERDRIVE" : _bossDisplayTitle);
                UIManager.Instance?.UpdateBossHp(CurrentHp, MaxHp, label);
            }

            if (CurrentHp <= 0f)
            {
                Die();
            }
        }

        private void EvaluateDirectionalShield(
            ref float effectiveDmg,
            ref string labelPrefix,
            ref bool shieldActiveFlag,
            GameObject shieldVisual,
            Vector3 shotDir,
            Vector3 hitPoint,
            bool isExplosive)
        {
            if (isExplosive)
            {
                shieldActiveFlag = false;
                if (shieldVisual != null) shieldVisual.SetActive(false);
                effectiveDmg *= 1.35f;
                labelPrefix = "SHATTER ";
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, transform.position + Vector3.up * 1.0f, Palette.C4Amber);
                FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 2.1f, "SHIELD SHATTERED!", Palette.C4Amber);
                return;
            }

            var flatShot = new Vector3(shotDir.x, 0f, shotDir.z).normalized;
            var dot = Vector3.Dot(flatShot, transform.forward);
            if (dot > 0.15f)
            {
                effectiveDmg *= 1.28f;
                labelPrefix = "FLANK ";
            }
            else
            {
                effectiveDmg *= 0.22f;
                VFXManager.Instance?.Play(VfxKind.HitSpark, hitPoint, Palette.C4Amber);
                FeelManager.Instance?.SpawnFloatingText(hitPoint + Vector3.up * 0.4f, "SHIELD!", Palette.C4Amber);
            }
        }

        private void SwitchBossToExoRig(float newSpeed, float ikScale, Color novaTint)
        {
            _moveSpeed = newSpeed;
            _bossCannonTimer = 0.6f;
            _bossHazardTimer = 1.8f;
            _radekDashCooldown = 1.4f;

            if (bossVisualRoot != null) bossVisualRoot.SetActive(false);
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(true);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(false);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(true);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(false);
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(true);
            ConfigureHeavyOperatorIk(ikScale);

            var blastPos = transform.position + Vector3.up * 1.0f;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, blastPos, Palette.RocketOrange);
            VFXManager.Instance?.Play(VfxKind.LevelUpNova, blastPos, novaTint);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(0.6f);
        }

        private void TriggerRadekPhase2()
        {
            _radekPhase2 = true;
            SwitchBossToExoRig(4.45f, 1.42f, Palette.M3HeliosCoreCyan);
            StoryDirector.NotifyRadekPhase2();
            UIManager.Instance?.TriggerThreatWarning("!! WARNING: APEX-01 EXO-ARMOR OVERCHARGE !!", 2.5f);
            UIManager.Instance?.ShowMilestoneBanner("PHASE 2 // APEX-01 OVERCHARGE");
        }

        private void TriggerChenPhase2()
        {
            _chenPhase2 = true;
            SwitchBossToExoRig(4.60f, 1.48f, Palette.EnemySecondary);
            StoryDirector.NotifyChenPhase2();
            UIManager.Instance?.TriggerThreatWarning("!! CRITICAL: AEON OVERWATCH OVERDRIVE ACTIVE !!", 2.6f);
            UIManager.Instance?.ShowMilestoneBanner("FINAL PHASE // DIR. CHEN OVERDRIVE");
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;
            HaltNavAgent();
            CurrentBehaviorState = "DEAD";
            var rootCol = GetComponent<Collider>();
            if (rootCol != null) rootCol.enabled = false;
            if (railLaserLine != null) railLaserLine.enabled = false;
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);

            var deathPos = transform.position + Vector3.up * 0.9f;
            var vfxCol = Archetype == EnemyArchetype.Boss ? Palette.BossPurple : Palette.EnemySecondary;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, deathPos, vfxCol);
            if (Archetype == EnemyArchetype.Boss)
            {
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, deathPos, Palette.CreditGold);
                VFXManager.Instance?.SpawnScorchMark(transform.position, 3.2f);
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
            var cb = _onFinishedCallback;
            _onFinishedCallback = null;
            cb?.Invoke(this, true);

            if (bipedalVisualRoot != null && bipedalVisualRoot.activeSelf && gameObject.activeInHierarchy)
            {
                _deathCollapseRoutine = StartCoroutine(DeathCollapseRoutine());
            }
            else
            {
                _ownerPool?.Release(this);
            }
        }

        private IEnumerator DeathCollapseRoutine()
        {
            const float duration = 0.26f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var ease = 1f - (1f - t) * (1f - t);
                if (bipedalVisualRoot != null)
                {
                    bipedalVisualRoot.transform.localRotation = Quaternion.Euler(-78f * ease, 0f, 12f * ease);
                    bipedalVisualRoot.transform.localPosition = new Vector3(0f, -0.35f * ease, -0.22f * ease);
                }
                yield return null;
            }

            _deathCollapseRoutine = null;
            if (bipedalVisualRoot != null)
            {
                bipedalVisualRoot.transform.localRotation = Quaternion.identity;
                bipedalVisualRoot.transform.localPosition = Vector3.zero;
            }
            if (_ownerPool != null)
            {
                _ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        public void ForceReturnToPool()
        {
            if (_deathCollapseRoutine != null)
            {
                StopCoroutine(_deathCollapseRoutine);
                _deathCollapseRoutine = null;
            }
            if (!gameObject.activeSelf && IsDead) return;
            IsDead = true;
            _sniperLaserLocking = false;
            HaltNavAgent();
            CurrentBehaviorState = "POOLED";
            var rootCol = GetComponent<Collider>();
            if (rootCol != null) rootCol.enabled = true;
            if (bipedalVisualRoot != null)
            {
                bipedalVisualRoot.transform.localRotation = Quaternion.identity;
                bipedalVisualRoot.transform.localPosition = Vector3.zero;
            }
            if (railLaserLine != null) railLaserLine.enabled = false;
            if (riotShieldVisual != null) riotShieldVisual.SetActive(false);
            if (bossShieldVisual != null) bossShieldVisual.SetActive(false);
            if (Archetype == EnemyArchetype.Boss)
            {
                UIManager.Instance?.HideBossHp();
            }
            var cb = _onFinishedCallback;
            _onFinishedCallback = null;
            cb?.Invoke(this, false);
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
