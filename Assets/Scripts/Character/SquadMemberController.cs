using AI;
using Audio;
using CleverCrow.Fluid.BTs.Tasks;
using CleverCrow.Fluid.BTs.Trees;
using Core;
using Enemies;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Character
{
    public enum SquadRole
    {
        SquadLeadHayes = 0,
        MarksmanBishop = 1,
        BreacherTorres = 2
    }

    public enum SquadOrder
    {
        Follow = 0,
        Hold = 1,
        Breach = 2
    }

    /// <summary>
    /// AI-controlled Vanguard-09 squadmate driven by NavMeshAgent pathfinding and Fluid Behavior Tree:
    /// two-hand weapon IK, tactical cover seeking on critical trauma, LoS flanking around cover,
    /// player muzzle-lane avoidance, 20s bleedout stabilization, and irreversible K.I.A. permadeath.
    /// </summary>
    public sealed class SquadMemberController : MonoBehaviour
    {
        private ProceduralCharacterMotion _characterMotion;
        private MeshRenderer[] _bodyRenderers;
        private Transform _visualRoot;
        private Transform _muzzleTip;
        [FormerlySerializedAs("_ejectionPort")] [SerializeField] private Transform ejectionPort;

        private BehaviorTree _behaviorTree;
        private EnemyController _targetEnemy;
        private Vector3 _activeDestination;
        private Vector3 _lastIssuedDest;
        private Vector3 _cachedCoverPos;
        private bool _hasCachedCover;
        private float _coverRefreshTimer;
        private float _repathTimer;
        private float _frameDt;
        private bool _isSettledAtAnchor;

        private Vector3 _formationOffset = new(-2.3f, 0f, -0.9f);
        private Vector3 _holdAnchorWorld;
        private bool _hasHoldAnchor;
        private bool _showingStabilizePrompt;

        private float _timeSinceLastHit = 10f;
        private float _fireCooldown;
        private int _burstRemaining;
        private float _burstTimer;
        private int _clipCapacity = 18;
        private int _ammoInClip = 18;
        private float _reloadDuration = 1.20f;
        private float _reloadTimer;
        [FormerlySerializedAs("_droppedMagMesh")] [SerializeField] private Mesh droppedMagMesh;
        [FormerlySerializedAs("_droppedMagMat")] [SerializeField] private Material droppedMagMat;
        [FormerlySerializedAs("_attachedMagVisual")] [SerializeField] private Transform attachedMagVisual;
        private float _hitFlashTimer;
        private float _prevYaw;

        public bool IsReloading => _reloadTimer > 0f;
        private float ReloadProgress => _reloadDuration > 0.001f ? Mathf.Clamp01(1f - (_reloadTimer / _reloadDuration)) : 1f;
        public int AmmoInClip => _ammoInClip;

        private const float BleedOutMaxSeconds = 20.0f;
        private static readonly RaycastHit[] LineOfFireBuffer = new RaycastHit[16];
        private MaterialPropertyBlock _mpb;

        public SquadRole Role { get; private set; }

        public string Callsign { get; private set; } = "MAJ. ROURKE";

        public string RoleTag { get; private set; } = "IRONWOOD-1";

        public float CurrentHp { get; private set; } = 150f;

        public float MaxHp { get; private set; } = 150f;

        public bool IsDowned { get; private set; }

        public bool IsKia { get; private set; }

        public float BleedOutRemaining { get; private set; }

        public float BleedOutNormalized => BleedOutRemaining / BleedOutMaxSeconds;

        public float StabilizeProgress { get; private set; }

        public int ShotsFiredCount { get; private set; }

        public string CurrentBehaviorState { get; private set; } = "INIT";

        public NavMeshAgent NavAgent { get; private set; }

        public void InitializeRig(
            SquadRole assignedRole,
            NavMeshAgent agent,
            ProceduralCharacterMotion motion,
            MeshRenderer[] renderers,
            Transform visual,
            Transform muzzle,
            Transform ejectionPort = null)
        {
            Role = assignedRole;
            NavAgent = agent != null ? agent : GetComponent<NavMeshAgent>();
            _characterMotion = motion;
            _bodyRenderers = renderers;
            _visualRoot = visual;
            _muzzleTip = muzzle;
            this.ejectionPort = ejectionPort;

            switch (assignedRole)
            {
                case SquadRole.SquadLeadHayes:
                    Callsign = "MAJ. ROURKE";
                    RoleTag = "IRONWOOD-1";
                    _formationOffset = new Vector3(-2.35f, 0f, -0.85f);
                    MaxHp = 175f;
                    _clipCapacity = 18;
                    _reloadDuration = 1.20f;
                    break;
                case SquadRole.MarksmanBishop:
                    Callsign = "KOZLOWSKI";
                    RoleTag = "WRAITH-2";
                    _formationOffset = new Vector3(2.65f, 0f, -1.65f);
                    MaxHp = 145f;
                    _clipCapacity = 8;
                    _reloadDuration = 1.35f;
                    break;
                case SquadRole.BreacherTorres:
                default:
                    Callsign = "CPL. REYES";
                    RoleTag = "SPARK-3";
                    _formationOffset = new Vector3(2.05f, 0f, 0.95f);
                    MaxHp = 165f;
                    _clipCapacity = 16;
                    _reloadDuration = 1.15f;
                    break;
            }

            CurrentHp = MaxHp;
            _ammoInClip = _clipCapacity;
            BuildBehaviorTree();
            ResetForMission(transform.position);
        }

        public void BindDroppedMagazine(Mesh magMesh, Material magMat, Transform attachedMagVisual = null)
        {
            droppedMagMesh = magMesh;
            droppedMagMat = magMat;
            if (attachedMagVisual != null) this.attachedMagVisual = attachedMagVisual;
        }

        private void Awake()
        {
            if (NavAgent == null) NavAgent = GetComponent<NavMeshAgent>();
            if (_behaviorTree == null) BuildBehaviorTree();
        }

        private void BuildBehaviorTree()
        {
            _behaviorTree = new BehaviorTreeBuilder(gameObject)
                .Selector("SquadTacticalRoot")
                    .Sequence("BleedOut")
                        .Condition("IsDowned", () => IsDowned)
                        .Do("StepBleedOut", () =>
                        {
                            HaltNavAgent();
                            _reloadTimer = 0f;
                            _characterMotion?.SetReloadState(false, 0f);
                            StepDownedState(PlayerController.Instance, _frameDt);
                            _characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: false);
                            CurrentBehaviorState = "BLEEDOUT";
                            return TaskStatus.Success;
                        })
                    .End()
                    .Sequence("YieldPlayerMuzzleLane")
                        .Condition("BlockingPlayerMuzzle", () => TryComputeMuzzleAvoidancePoint(PlayerController.Instance, out _activeDestination))
                        .Do("SideStepLane", () => ExecuteTacticalMove(_activeDestination, 1.1f, "CLEAR_LANE"))
                    .End()
                    .Sequence("TakeCoverCriticalHp")
                        .Condition("CriticalHpNeedsCover", () => CurrentHp <= MaxHp * 0.42f && _timeSinceLastHit < 3.6f && AcquireCoverCandidate(out _activeDestination))
                        .Do("SeekTacticalCover", () => ExecuteTacticalMove(_activeDestination, 1.18f, "SEEK_COVER"))
                    .End()
                    .Sequence("OrderHoldOverwatch")
                        .Condition("OrderIsHold", () => GetCurrentOrder() == SquadOrder.Hold && _hasHoldAnchor)
                        .Do("HoldAnchorOverwatch", () => ExecuteTacticalMove(ClampToUnlockedBounds(_holdAnchorWorld), 1.0f, "HOLD_OVERWATCH"))
                    .End()
                    .Sequence("OrderBreachObjective")
                        .Condition("OrderIsBreach", () => GetCurrentOrder() == SquadOrder.Breach && EnemySpawner.Instance != null)
                        .Do("BreachPushDestination", () => ExecuteTacticalMove(ComputeBreachDestination(PlayerController.Instance), 1.08f, "BREACH_ASSAULT"))
                    .End()
                    .Sequence("DefaultFollowSupport")
                        .Do("DiamondSupportAndFlank", () => ExecuteTacticalMove(ComputeFollowOrFlankDestination(PlayerController.Instance), 1.0f, "FOLLOW_SUPPORT"))
                    .End()
                .End()
                .Build();
        }

        public void ResetForMission(Vector3 spawnPos)
        {
            IsDowned = false;
            IsKia = false;
            BleedOutRemaining = BleedOutMaxSeconds;
            StabilizeProgress = 0f;
            CurrentHp = MaxHp;
            _timeSinceLastHit = 10f;
            _fireCooldown = 0.25f + (int)Role * 0.14f;
            _burstRemaining = 0;
            _burstTimer = 0f;
            _ammoInClip = _clipCapacity;
            _reloadTimer = 0f;
            _characterMotion?.SetReloadState(false, 0f);
            _hitFlashTimer = 0f;
            _hasHoldAnchor = false;
            _hasCachedCover = false;
            _coverRefreshTimer = UnityEngine.Random.Range(0f, 0.25f);
            _repathTimer = UnityEngine.Random.Range(0f, 0.14f);
            _isSettledAtAnchor = false;
            ShotsFiredCount = 0;
            CurrentBehaviorState = "READY";
            gameObject.SetActive(true);
            SetVisualCrouch(false);
            ApplyHitFlash(0f);
            Teleport(spawnPos, Quaternion.identity);
            _characterMotion?.ResetMotionState();
        }

        public void ApplyPreexistingKiaState()
        {
            HaltNavAgent();
            IsDowned = false;
            IsKia = true;
            BleedOutRemaining = 0f;
            StabilizeProgress = 0f;
            CurrentHp = 0f;
            _burstRemaining = 0;
            CurrentBehaviorState = "KIA";
            gameObject.SetActive(false);
        }

        public bool ApplyEmergencyTraumaPatch()
        {
            if (IsKia || !IsDowned) return false;
            IsDowned = false;
            BleedOutRemaining = BleedOutMaxSeconds;
            StabilizeProgress = 0f;
            CurrentHp = MaxHp * 0.65f;
            _timeSinceLastHit = 4f;
            _hasCachedCover = false;
            SetVisualCrouch(false);
            ResumeNavAgent();
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.85f, Callsign + " STABILIZED", Palette.HealthGreen);
            AudioManager.Instance?.PlayPickupBlip();
            return true;
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            transform.rotation = rot;
            if (NavAgent != null && NavAgent.enabled)
            {
                if (!TacticalNavigationUtility.EnsureAgentOnNavMesh(NavAgent, pos, 8f))
                {
                    transform.SetPositionAndRotation(pos, rot);
                }
                else if (NavAgent.isOnNavMesh)
                {
                    NavAgent.ResetPath();
                    NavAgent.velocity = Vector3.zero;
                }
            }
            else
            {
                transform.SetPositionAndRotation(pos, rot);
            }
            _lastIssuedDest = transform.position;
            _prevYaw = transform.eulerAngles.y;
        }

        public void CaptureHoldAnchor()
        {
            _holdAnchorWorld = transform.position;
            _hasHoldAnchor = true;
        }

        public void ClearHoldAnchor()
        {
            _hasHoldAnchor = false;
        }

        public void TakeDamage(float dmg)
        {
            if (IsKia || IsDowned || dmg <= 0f) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - dmg);
            _timeSinceLastHit = 0f;
            _hitFlashTimer = 0.12f;
            ApplyHitFlash(1f);

            if (!(CurrentHp <= 0f)) return;
            IsDowned = true;
            BleedOutRemaining = BleedOutMaxSeconds;
            StabilizeProgress = 0f;
            _burstRemaining = 0;
            HaltNavAgent();
            SetVisualCrouch(true);
            StoryDirector.NotifySquadDowned(Callsign);
            UIManager.Instance?.TriggerThreatWarning($"!! TRAUMA: {RoleTag} BLEEDING OUT — STABILIZE ({Mathf.RoundToInt(BleedOutMaxSeconds)}S) !!", 2.5f);
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.7f, Callsign + " BLEEDOUT!", Palette.EnemySecondary);
            VFXManager.Instance?.Play(VfxKind.HitSpark, transform.position + Vector3.up * 0.6f, Palette.EnemySecondary);
        }

        private void Update()
        {
            if (IsKia) return;
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
            {
                HaltNavAgent();
                _characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: true);
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            _frameDt = dt;
            _repathTimer -= dt;
            _coverRefreshTimer -= dt;

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(_hitFlashTimer / 0.12f));
            }

            if (!IsDowned)
            {
                // Out-of-combat CoD health regeneration
                _timeSinceLastHit += dt;
                if (_timeSinceLastHit >= 3.6f && CurrentHp < MaxHp)
                {
                    CurrentHp = Mathf.Min(MaxHp, CurrentHp + 18f * dt);
                }

                _targetEnemy = AcquireTargetEnemy();
            }

            _behaviorTree?.Tick();
        }

        private TaskStatus ExecuteTacticalMove(Vector3 desiredDest, float speedMultiplier, string stateName)
        {
            CurrentBehaviorState = stateName;
            var baseSpeed = GetBaseMoveSpeed();
            var toDest = desiredDest - transform.position;
            toDest.y = 0f;
            var dist = toDest.magnitude;
            if (dist > 6.5f) speedMultiplier = Mathf.Max(speedMultiplier, 1.15f);

            var velocity = DriveNavigationTo(desiredDest, baseSpeed * speedMultiplier, _frameDt);
            StepFacingAndWeapon(_targetEnemy, PlayerController.Instance, velocity, _frameDt);
            return TaskStatus.Success;
        }

        private float GetBaseMoveSpeed()
        {
            return Role == SquadRole.BreacherTorres ? 5.65f : (Role == SquadRole.SquadLeadHayes ? 5.45f : 5.15f);
        }

        private Vector3 DriveNavigationTo(Vector3 destination, float targetSpeed, float dt)
        {
            destination = ClampToUnlockedBounds(destination);

            if (NavAgent != null && NavAgent.enabled)
            {
                if (!NavAgent.isOnNavMesh)
                {
                    TacticalNavigationUtility.EnsureAgentOnNavMesh(NavAgent, transform.position, 6f);
                }

                if (NavAgent.isOnNavMesh)
                {
                    NavAgent.isStopped = false;
                    NavAgent.speed = targetSpeed;

                    var delta = destination - transform.position;
                    delta.y = 0f;
                    var distToGoal = delta.magnitude;
                    var stopThreshold = _isSettledAtAnchor ? 0.72f : 0.45f;
                    if (distToGoal <= stopThreshold)
                    {
                        _isSettledAtAnchor = true;
                        if (NavAgent.hasPath) NavAgent.ResetPath();
                        return Vector3.zero;
                    }
                    _isSettledAtAnchor = false;

                    if (_repathTimer <= 0f || Vector3.Distance(_lastIssuedDest, destination) > 0.55f || !NavAgent.hasPath)
                    {
                        var navTarget = TacticalNavigationUtility.TryGetReachablePoint(transform.position, destination, out var reachable, 4.5f)
                            ? reachable
                            : destination;
                        NavAgent.SetDestination(navTarget);
                        _lastIssuedDest = destination;
                        _repathTimer = 0.14f;
                    }

                    return NavAgent.velocity;
                }
            }

            // Direct kinematic fallback for unbaked synthetic test scenes
            var toAnchor = destination - transform.position;
            toAnchor.y = 0f;
            if (toAnchor.magnitude <= 0.55f) return Vector3.zero;

            var vel = toAnchor.normalized * targetSpeed;
            var clamped = ClampToUnlockedBounds(transform.position + vel * dt);
            var safeVel = (clamped - transform.position) / Mathf.Max(0.001f, dt);
            transform.position = clamped;
            return safeVel;
        }

        private void HaltNavAgent()
        {
            if (NavAgent == null || !NavAgent.enabled || !NavAgent.isOnNavMesh) return;
            NavAgent.isStopped = true;
            if (NavAgent.hasPath) NavAgent.ResetPath();
            NavAgent.velocity = Vector3.zero;
        }

        private void ResumeNavAgent()
        {
            if (NavAgent != null && NavAgent.enabled && NavAgent.isOnNavMesh)
            {
                NavAgent.isStopped = false;
            }
        }

        private Vector3 ComputeBarrelAimDirection(Vector3 targetWorldPos)
        {
            var centerToTarget = targetWorldPos - transform.position;
            centerToTarget.y = 0f;
            var dist = centerToTarget.magnitude;
            if (dist < 0.05f) return transform.forward;

            var lateralOffset = _muzzleTip != null ? transform.InverseTransformPoint(_muzzleTip.position).x : 0.11f;
            var bearingRad = Mathf.Atan2(centerToTarget.x, centerToTarget.z);
            if (!(dist > Mathf.Abs(lateralOffset) + 0.06f)) return centerToTarget / dist;
            var parallaxRad = Mathf.Asin(Mathf.Clamp(lateralOffset / dist, -0.92f, 0.92f));
            var correctedRad = bearingRad - parallaxRad;
            return new Vector3(Mathf.Sin(correctedRad), 0f, Mathf.Cos(correctedRad));
        }

        private void StepFacingAndWeapon(EnemyController targetEnemy, PlayerController player, Vector3 velocity, float dt)
        {
            var lookDir = velocity.sqrMagnitude > 0.25f ? velocity.normalized : transform.forward;
            if (targetEnemy != null)
            {
                lookDir = ComputeBarrelAimDirection(targetEnemy.transform.position);
            }
            else if (player != null)
            {
                var pFwd = player.transform.forward;
                pFwd.y = 0f;
                if (pFwd.sqrMagnitude > 0.02f) lookDir = pFwd.normalized;
            }

            if (lookDir.sqrMagnitude > 0.001f)
            {
                var targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = targetEnemy != null
                    ? targetRot
                    : Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-14f * dt));
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            if (_reloadTimer > 0f)
            {
                _reloadTimer = Mathf.Max(0f, _reloadTimer - dt);
                if (_reloadTimer <= 0f)
                {
                    _ammoInClip = _clipCapacity;
                    _characterMotion?.SetReloadState(false, 0f);
                }
                else
                {
                    _characterMotion?.SetReloadState(true, ReloadProgress);
                }
            }

            var isAimingDownSights = targetEnemy != null && velocity.sqrMagnitude < 1.25f;
            _characterMotion?.SetAimDownSightsState(isAimingDownSights);
            _characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: true);
            StepCombatFire(targetEnemy, dt);
        }

        private bool TryComputeMuzzleAvoidancePoint(PlayerController player, out Vector3 escapePoint)
        {
            escapePoint = transform.position;
            if (player == null || player.IsDead) return false;

            var fromPlayer = transform.position - player.transform.position;
            fromPlayer.y = 0f;
            var pDist = fromPlayer.magnitude;
            if (pDist is >= 3.4f or <= 0.05f) return false;

            var aimFwd = player.transform.forward;
            aimFwd.y = 0f;
            if (aimFwd.sqrMagnitude < 0.001f) return false;
            aimFwd.Normalize();

            if (Vector3.Dot(fromPlayer, aimFwd) <= 0.1f) return false;
            var side = Vector3.Dot(fromPlayer, player.transform.right);
            if (Mathf.Abs(side) >= 1.05f) return false;

            var dodgeSide = player.transform.right * (side >= 0f ? 1f : -1f);
            escapePoint = ClampToUnlockedBounds(transform.position + dodgeSide * 2.4f);
            return true;
        }

        private bool AcquireCoverCandidate(out Vector3 coverPoint)
        {
            coverPoint = _cachedCoverPos;
            if (_hasCachedCover && _coverRefreshTimer > 0f)
            {
                return true;
            }

            var threatPos = _targetEnemy != null
                ? _targetEnemy.transform.position
                : (PlayerController.Instance != null ? PlayerController.Instance.transform.position + PlayerController.Instance.transform.forward * 10f : transform.position + transform.forward * 10f);

            if (TacticalNavigationUtility.FindTacticalCoverPosition(transform.position, threatPos, 9.5f, 4.5f, out var foundCover))
            {
                _cachedCoverPos = ClampToUnlockedBounds(foundCover);
                _hasCachedCover = true;
                _coverRefreshTimer = 1.4f;
                coverPoint = _cachedCoverPos;
                return true;
            }

            _hasCachedCover = false;
            return false;
        }

        private Vector3 ComputeBreachDestination(PlayerController player)
        {
            var playerPos = player != null ? player.transform.position : transform.position;
            var objPos = EnemySpawner.Instance.GetActiveObjectiveWorldPoint();
            var toObj = objPos - playerPos;
            toObj.y = 0f;

            var candidate = playerPos + _formationOffset;
            if (toObj.sqrMagnitude > 1f)
            {
                var breachForward = Vector3.Lerp(playerPos, objPos, Role == SquadRole.BreacherTorres ? 0.72f : 0.45f);
                var right = Vector3.Cross(Vector3.up, toObj.normalized).normalized;
                var lateral = Role == SquadRole.SquadLeadHayes ? -2.0f : (Role == SquadRole.MarksmanBishop ? 2.4f : 0.75f);
                candidate = breachForward + right * lateral;
            }

            return ClampToUnlockedBounds(candidate);
        }

        private Vector3 ComputeFollowOrFlankDestination(PlayerController player)
        {
            var playerPos = player != null ? player.transform.position : Vector3.zero;
            var offset = player != null
                ? Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f) * _formationOffset
                : _formationOffset;
            var desired = playerPos + offset;

            if (EnemySpawner.Instance != null && Role == SquadRole.BreacherTorres)
            {
                var objPos = EnemySpawner.Instance.GetActiveObjectiveWorldPoint();
                if (Vector3.Distance(playerPos, objPos) < 8.5f)
                {
                    desired = Vector3.Lerp(desired, objPos + new Vector3(1.1f, 0f, -0.6f), 0.55f);
                }
            }

            // If stationary behind cover relative to current target, step sideways onto an unobstructed firing lane
            if (_targetEnemy != null &&
                Vector3.Distance(transform.position, desired) < 3.6f &&
                !TacticalNavigationUtility.HasClearLineOfSight(transform.position + Vector3.up * 1.15f, _targetEnemy.transform.position + Vector3.up * 0.9f))
            {
                var flankSign = Role == SquadRole.MarksmanBishop ? 1f : -1f;
                if (TacticalNavigationUtility.FindFlankPosition(transform.position, _targetEnemy.transform.position, flankSign, 5.8f, out var flankPos) &&
                    Vector3.Distance(flankPos, playerPos) <= 11.5f)
                {
                    desired = flankPos;
                }
            }

            return ClampToUnlockedBounds(desired);
        }

        private static SquadOrder GetCurrentOrder()
        {
            return SquadDirector.Instance != null ? SquadDirector.Instance.CurrentOrder : SquadOrder.Follow;
        }

        private void StepDownedState(PlayerController player, float dt)
        {
            var stabilizing = false;
            if (player != null && !player.IsDead)
            {
                var d = player.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude <= 2.8f)
                {
                    stabilizing = true;
                    var eHeld = Keyboard.current != null && Keyboard.current.eKey.isPressed;
                    StabilizeProgress += (eHeld ? 0.95f : 0.48f) * dt;
                    _showingStabilizePrompt = true;
                    UIManager.Instance?.UpdateContextPrompt($"STABILIZING {Callsign} [{Mathf.RoundToInt(Mathf.Clamp01(StabilizeProgress) * 100f)}%] — HOLD [E]");
                    if (StabilizeProgress >= 1f)
                    {
                        _showingStabilizePrompt = false;
                        UIManager.Instance?.UpdateContextPrompt(string.Empty);
                        ApplyEmergencyTraumaPatch();
                        return;
                    }
                }
            }

            if (stabilizing) return;

            if (_showingStabilizePrompt)
            {
                _showingStabilizePrompt = false;
                UIManager.Instance?.UpdateContextPrompt(string.Empty);
            }
            StabilizeProgress = Mathf.Max(0f, StabilizeProgress - 0.15f * dt);
            BleedOutRemaining -= dt;
            if (BleedOutRemaining > 0f) return;

            MarkKilledInAction();
        }

        public void MarkKilledInAction(bool broadcastRadio = true)
        {
            if (IsKia) return;
            HaltNavAgent();
            IsKia = true;
            if (_showingStabilizePrompt)
            {
                _showingStabilizePrompt = false;
                UIManager.Instance?.UpdateContextPrompt(string.Empty);
            }
            IsDowned = false;
            BleedOutRemaining = 0f;
            StabilizeProgress = 0f;
            CurrentHp = 0f;
            CurrentBehaviorState = "KIA";

            SaveManager.EnsureInstance().MarkOperatorKia(RoleTag);
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.95f, Callsign + " K.I.A.", Palette.HitmarkerCrimson);
            FeelManager.Instance?.TriggerKillFeed("TRAUMA-NET", Callsign, "K.I.A.");
            UIManager.Instance?.TriggerThreatWarning($"!! FLATLINE: {RoleTag} ({Callsign}) K.I.A. — SIGNAL LOST !!", 3.5f);
            UIManager.Instance?.ShowMilestoneBanner($"{RoleTag} // {Callsign} — KILLED IN ACTION");
            AudioManager.Instance?.PlayEnemyExplode();
            if (broadcastRadio)
            {
                StoryDirector.NotifyOperatorKia(Callsign, RoleTag);
            }
            gameObject.SetActive(false);
        }

        private EnemyController AcquireTargetEnemy()
        {
            if (EnemySpawner.Instance == null) return null;
            var maxRange = Role == SquadRole.MarksmanBishop ? 24.5f : (Role == SquadRole.SquadLeadHayes ? 18.5f : 16.5f);
            var maxRangeSq = maxRange * maxRange;

            EnemyController best = null;
            var bestScore = float.MaxValue;
            var enemies = EnemySpawner.Instance.ActiveEnemies;
            var designated = SquadDirector.Instance != null ? SquadDirector.Instance.PriorityTarget : null;
            for (var i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.IsDead || !e.gameObject.activeInHierarchy) continue;

                var diff = e.transform.position - transform.position;
                diff.y = 0f;
                var distSq = diff.sqrMagnitude;
                if (distSq > maxRangeSq) continue;

                var priorityWeight = distSq;
                if (Role == SquadRole.MarksmanBishop && (e.Archetype == EnemyArchetype.Heavy || e.Archetype == EnemyArchetype.Boss))
                {
                    priorityWeight *= 0.55f;
                }
                if (e == designated)
                {
                    priorityWeight *= 0.25f;
                }

                if (!(priorityWeight < bestScore)) continue;
                bestScore = priorityWeight;
                best = e;
            }
            return best;
        }

        private void StepCombatFire(EnemyController target, float dt)
        {
            if (_reloadTimer > 0f)
            {
                _burstRemaining = 0;
                CurrentBehaviorState = "RELOADING";
                return;
            }

            if (target == null || SquadDirector.Instance == null)
            {
                _burstRemaining = 0;
                if (_ammoInClip > 0 && _ammoInClip <= Mathf.Max(2, _clipCapacity / 3))
                {
                    BeginSquadReload();
                }
                return;
            }

            if (_burstRemaining > 0)
            {
                _burstTimer -= dt;
                if (!(_burstTimer <= 0f)) return;
                _burstRemaining--;
                _burstTimer = Role == SquadRole.BreacherTorres ? 0.042f : 0.125f;
                FireRoundAt(target);
                return;
            }

            _fireCooldown -= dt;
            if (_fireCooldown > 0f) return;

            if (!HasClearLineOfFire(target))
            {
                _fireCooldown = 0.22f;
                return;
            }

            switch (Role)
            {
                case SquadRole.MarksmanBishop:
                    _fireCooldown = 0.78f;
                    break;
                case SquadRole.BreacherTorres:
                    _fireCooldown = 0.68f;
                    _burstRemaining = 1; // AN-94 2-round hyperburst
                    _burstTimer = 0.042f;
                    break;
                case SquadRole.SquadLeadHayes:
                default:
                    _fireCooldown = 0.65f;
                    _burstRemaining = 2; // M4A1 3-round tactical burst
                    _burstTimer = 0.12f;
                    break;
            }

            FireRoundAt(target);
        }

        private bool HasClearLineOfFire(EnemyController target)
        {
            var origin = _muzzleTip != null ? _muzzleTip.position : transform.position + Vector3.up * 1.18f;
            var dest = target.transform.position + Vector3.up * 0.9f;
            var delta = dest - origin;
            var dist = delta.magnitude;
            if (dist < 0.5f) return true;

            var hitCount = Physics.RaycastNonAlloc(origin, delta / dist, LineOfFireBuffer, dist - 0.35f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hitCount; i++)
            {
                var col = LineOfFireBuffer[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<SquadMemberController>() != null)
                {
                    continue;
                }
                if (col.GetComponentInParent<EnemyController>() == null)
                {
                    return false;
                }
            }
            return true;
        }

        private void FireRoundAt(EnemyController target)
        {
            if (target != null)
            {
                var barrelAim = ComputeBarrelAimDirection(target.transform.position);
                if (barrelAim.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(barrelAim, Vector3.up);
                    _characterMotion?.AlignWeaponBarrelWithBodyYaw();
                }
            }

            var origin = _muzzleTip != null ? _muzzleTip.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.18f;
            var shotDir = _muzzleTip != null
                ? new Vector3(_muzzleTip.forward.x, 0f, _muzzleTip.forward.z)
                : new Vector3(transform.forward.x, 0f, transform.forward.z);
            shotDir = shotDir.sqrMagnitude > 0.0001f ? shotDir.normalized : transform.forward;

            var spd = Role == SquadRole.MarksmanBishop ? 50f : 38f;
            var dmg = Role == SquadRole.MarksmanBishop ? 42f : (Role == SquadRole.SquadLeadHayes ? 22f : 20f);
            var pierce = Role == SquadRole.MarksmanBishop ? 1 : 0;
            var visScale = Role == SquadRole.MarksmanBishop ? new Vector3(0.030f, 0.48f, 0.030f) : new Vector3(0.026f, 0.36f, 0.026f);

            SquadDirector.Instance.SpawnSquadBullet(origin, Quaternion.LookRotation(shotDir, Vector3.up), spd, dmg, pierce, visScale);
            ShotsFiredCount++;
            _ammoInClip = Mathf.Max(0, _ammoInClip - 1);

            if (ejectionPort != null)
            {
                Combat.DroppedMagazine.SpawnShellCasing(ejectionPort.position, ejectionPort.right);
            }
            else
            {
                var fallbackPort = transform.position + transform.right * 0.16f + Vector3.up * 1.18f + transform.forward * 0.25f;
                Combat.DroppedMagazine.SpawnShellCasing(fallbackPort, transform.right);
            }

            _characterMotion?.TriggerWeaponRecoil(
                new Vector3(0f, 0.014f, -0.082f),
                Role == SquadRole.MarksmanBishop ? 10.5f : 7.0f);
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.RailgunCyan);
            AudioManager.Instance?.PlayWeaponFire(
                GetSquadWeaponType(),
                Role == SquadRole.MarksmanBishop ? 0.94f : (Role == SquadRole.BreacherTorres ? 1.05f : 1.0f),
                Role == SquadRole.MarksmanBishop ? 0.26f : 0.22f);

            if (_ammoInClip <= 0)
            {
                BeginSquadReload();
            }
        }

        private Combat.WeaponType GetSquadWeaponType()
        {
            return Role switch
            {
                SquadRole.MarksmanBishop => Combat.WeaponType.PlasmaRailgun,
                SquadRole.BreacherTorres => Combat.WeaponType.An94Abakan,
                _ => Combat.WeaponType.PulseRifle
            };
        }

        private void BeginSquadReload()
        {
            if (_reloadTimer > 0f) return;
            _reloadTimer = _reloadDuration;
            _burstRemaining = 0;
            CurrentBehaviorState = "RELOADING";
            _characterMotion?.SetReloadState(true, 0f);

            var ejectPos = _muzzleTip != null
                ? _muzzleTip.position - transform.forward * 0.28f - Vector3.up * 0.09f
                : transform.position + transform.forward * 0.28f + Vector3.up * 1.06f;
            var ejectVel = (-transform.right * 1.1f - transform.forward * 0.45f) + Vector3.up * 0.95f;
            Combat.DroppedMagazine.SpawnFromVisual(attachedMagVisual, droppedMagMesh, droppedMagMat, ejectPos, transform.rotation, ejectVel);
        }

        private void OnDisable()
        {
            if (!_showingStabilizePrompt) return;
            _showingStabilizePrompt = false;
            UIManager.Instance?.UpdateContextPrompt(string.Empty);
        }

        private static Vector3 ClampToUnlockedBounds(Vector3 pos)
        {
            return EnemySpawner.Instance == null ? pos : EnemySpawner.Instance.ClampPositionToUnlockedSectors(pos);
        }

        private void SetVisualCrouch(bool crouched)
        {
            if (_visualRoot == null) return;
            if (crouched)
            {
                _visualRoot.localPosition = new Vector3(0f, -0.48f, -0.14f);
                _visualRoot.localRotation = Quaternion.Euler(46f, -14f, -20f);
            }
            else
            {
                _visualRoot.localPosition = Vector3.zero;
                _visualRoot.localRotation = Quaternion.identity;
            }
        }

        private void ApplyHitFlash(float intensity)
        {
            if (_bodyRenderers == null) return;
            _mpb ??= new MaterialPropertyBlock();

            var flash = Palette.Lerp(Palette.Shadow, Palette.EnemySecondary, intensity) * (intensity * 1.4f);
            foreach (var t in _bodyRenderers)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", flash);
                t.SetPropertyBlock(_mpb);
            }
        }
    }
}
