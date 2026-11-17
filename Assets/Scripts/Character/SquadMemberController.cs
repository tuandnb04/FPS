using System;
using Audio;
using Core;
using Enemies;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;

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
    /// AI-controlled Vanguard-09 squadmate with two-hand weapon IK, cover line-of-sight awareness,
    /// player muzzle-lane avoidance, 20-second critical bleedout requiring manual stabilization or Trauma Kit,
    /// and irreversible K.I.A. permadeath synced to SaveManager.
    /// </summary>
    public sealed class SquadMemberController : MonoBehaviour
    {
        private CharacterController _controller;
        private ProceduralCharacterMotion _characterMotion;
        private MeshRenderer[] _bodyRenderers;
        private Transform _visualRoot;
        private Transform _muzzleTip;

        private Vector3 _formationOffset = new(-2.3f, 0f, -0.9f);
        private Vector3 _holdAnchorWorld;
        private bool _hasHoldAnchor;

        private float _timeSinceLastHit = 10f;
        private float _fireCooldown;
        private int _burstRemaining;
        private float _burstTimer;
        private float _hitFlashTimer;
        private float _prevYaw;

        private const float BleedOutMaxSeconds = 20.0f;
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

        public void InitializeRig(
            SquadRole assignedRole,
            CharacterController cc,
            ProceduralCharacterMotion motion,
            MeshRenderer[] renderers,
            Transform visual,
            Transform muzzle)
        {
            Role = assignedRole;
            _controller = cc;
            _characterMotion = motion;
            _bodyRenderers = renderers;
            _visualRoot = visual;
            _muzzleTip = muzzle;

            switch (assignedRole)
            {
                case SquadRole.SquadLeadHayes:
                    Callsign = "MAJ. ROURKE";
                    RoleTag = "IRONWOOD-1";
                    _formationOffset = new Vector3(-2.35f, 0f, -0.85f);
                    MaxHp = 175f;
                    break;
                case SquadRole.MarksmanBishop:
                    Callsign = "KOZLOWSKI";
                    RoleTag = "WRAITH-2";
                    _formationOffset = new Vector3(2.65f, 0f, -1.65f);
                    MaxHp = 145f;
                    break;
                case SquadRole.BreacherTorres:
                default:
                    Callsign = "CPL. REYES";
                    RoleTag = "SPARK-3";
                    _formationOffset = new Vector3(2.05f, 0f, 0.95f);
                    MaxHp = 165f;
                    break;
            }

            CurrentHp = MaxHp;
            ResetForMission(transform.position);
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
            _hitFlashTimer = 0f;
            _hasHoldAnchor = false;
            ShotsFiredCount = 0;
            gameObject.SetActive(true);
            SetVisualCrouch(false);
            ApplyHitFlash(0f);
            Teleport(spawnPos, Quaternion.identity);
            _characterMotion?.ResetMotionState();
        }

        public void ApplyPreexistingKiaState()
        {
            IsDowned = false;
            IsKia = true;
            BleedOutRemaining = 0f;
            StabilizeProgress = 0f;
            CurrentHp = 0f;
            _burstRemaining = 0;
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
            SetVisualCrouch(false);
            FeelManager.Instance?.SpawnFloatingText(transform.position + Vector3.up * 0.85f, Callsign + " STABILIZED", Palette.HealthGreen);
            AudioManager.Instance?.PlayPickupBlip();
            return true;
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (_controller != null)
            {
                _controller.enabled = false;
                transform.SetPositionAndRotation(pos, rot);
                _controller.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(pos, rot);
            }
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
                _characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: true);
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(_hitFlashTimer / 0.12f));
            }

            var player = PlayerController.Instance;
            if (IsDowned)
            {
                StepDownedState(player, dt);
                _characterMotion?.StepMotion(Vector3.zero, 0f, isAimingWeapon: false);
                return;
            }

            // Out-of-combat CoD health regeneration
            _timeSinceLastHit += dt;
            if (_timeSinceLastHit >= 3.6f && CurrentHp < MaxHp)
            {
                CurrentHp = Mathf.Min(MaxHp, CurrentHp + 18f * dt);
            }

            var targetEnemy = AcquireTargetEnemy();
            var desiredAnchor = ComputeDesiredAnchor(player);

            var toAnchor = desiredAnchor - transform.position;
            toAnchor.y = 0f;
            var velocity = Vector3.zero;
            var moveSpeed = Role == SquadRole.BreacherTorres ? 5.65f : (Role == SquadRole.SquadLeadHayes ? 5.45f : 5.15f);

            var anchorDist = toAnchor.magnitude;
            if (anchorDist > 0.65f)
            {
                var speedScale = Mathf.Clamp01((anchorDist - 0.55f) / 1.4f);
                if (anchorDist > 6.5f) speedScale = 1.15f;
                velocity = toAnchor.normalized * (moveSpeed * speedScale);
            }

            // Avoid lingering inside player's immediate line of fire
            if (player != null && !player.IsDead)
            {
                var fromPlayer = transform.position - player.transform.position;
                fromPlayer.y = 0f;
                var pDist = fromPlayer.magnitude;
                if (pDist is < 3.4f and > 0.05f)
                {
                    var aimFwd = player.transform.forward;
                    aimFwd.y = 0f;
                    aimFwd.Normalize();
                    var proj = Vector3.Dot(fromPlayer, aimFwd);
                    if (proj > 0.1f)
                    {
                        var side = Vector3.Dot(fromPlayer, player.transform.right);
                        if (Mathf.Abs(side) < 1.05f)
                        {
                            var dodgeSide = player.transform.right * (side >= 0f ? 1f : -1f);
                            velocity += dodgeSide * 3.4f;
                        }
                    }
                }
            }

            if (velocity.sqrMagnitude > 0.01f)
            {
                var clampedStep = ClampToUnlockedBounds(transform.position + velocity * dt) - transform.position;
                var safeVel = clampedStep / Mathf.Max(0.001f, dt);
                if (_controller != null && _controller.enabled)
                {
                    _controller.Move((safeVel + Vector3.down * 5f) * dt);
                }
                else
                {
                    transform.position += safeVel * dt;
                }
                velocity = safeVel;
            }

            // Aiming + Weapon Firing
            var lookDir = velocity.sqrMagnitude > 0.25f ? velocity.normalized : transform.forward;
            if (targetEnemy != null)
            {
                var toTarget = targetEnemy.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.02f)
                {
                    lookDir = toTarget.normalized;
                }
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
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-14f * dt));
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            _characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: true);
            StepCombatFire(targetEnemy, dt);
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
                    UIManager.Instance?.UpdateContextPrompt($"STABILIZING {Callsign} [{Mathf.RoundToInt(Mathf.Clamp01(StabilizeProgress) * 100f)}%] — HOLD [E]");
                    if (StabilizeProgress >= 1f)
                    {
                        UIManager.Instance?.UpdateContextPrompt(string.Empty);
                        ApplyEmergencyTraumaPatch();
                        return;
                    }
                }
            }

            if (stabilizing) return;
            StabilizeProgress = Mathf.Max(0f, StabilizeProgress - 0.15f * dt);
            BleedOutRemaining -= dt;
            if (BleedOutRemaining > 0f) return;

            MarkKilledInAction();
        }

        public void MarkKilledInAction(bool broadcastRadio = true)
        {
            if (IsKia) return;
            IsKia = true;
            IsDowned = false;
            BleedOutRemaining = 0f;
            StabilizeProgress = 0f;
            CurrentHp = 0f;

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

        private Vector3 ComputeDesiredAnchor(PlayerController player)
        {
            var order = SquadDirector.Instance != null ? SquadDirector.Instance.CurrentOrder : SquadOrder.Follow;
            var playerPos = player != null ? player.transform.position : Vector3.zero;

            switch (order)
            {
                case SquadOrder.Hold when _hasHoldAnchor:
                    return ClampToUnlockedBounds(_holdAnchorWorld);
                case SquadOrder.Breach when EnemySpawner.Instance != null:
                {
                    var objPos = EnemySpawner.Instance.GetActiveObjectiveWorldPoint();
                    var toObj = objPos - playerPos;
                    toObj.y = 0f;
                    if (toObj.sqrMagnitude > 1f)
                    {
                        var breachForward = Vector3.Lerp(playerPos, objPos, Role == SquadRole.BreacherTorres ? 0.72f : 0.45f);
                        var right = Vector3.Cross(Vector3.up, toObj.normalized).normalized;
                        var lateral = Role == SquadRole.SquadLeadHayes ? -2.0f : (Role == SquadRole.MarksmanBishop ? 2.4f : 0.75f);
                        return ClampToUnlockedBounds(breachForward + right * lateral);
                    }

                    break;
                }
                case SquadOrder.Follow:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Default Follow wedge formation oriented slightly by advance/aim
            var desired = playerPos + _formationOffset;
            if (EnemySpawner.Instance == null || Role != SquadRole.BreacherTorres)
                return ClampToUnlockedBounds(desired);
            {
                var objPos = EnemySpawner.Instance.GetActiveObjectiveWorldPoint();
                if (Vector3.Distance(playerPos, objPos) < 8.5f)
                {
                    desired = Vector3.Lerp(desired, objPos + new Vector3(1.1f, 0f, -0.6f), 0.55f);
                }
            }
            return ClampToUnlockedBounds(desired);
        }

        private EnemyController AcquireTargetEnemy()
        {
            if (EnemySpawner.Instance == null) return null;
            var maxRange = Role == SquadRole.MarksmanBishop ? 24.5f : (Role == SquadRole.SquadLeadHayes ? 18.5f : 16.5f);
            var maxRangeSq = maxRange * maxRange;

            EnemyController best = null;
            var bestScore = float.MaxValue;
            var enemies = EnemySpawner.Instance.ActiveEnemies;
            foreach (var e in enemies)
            {
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

                if (!(priorityWeight < bestScore)) continue;
                bestScore = priorityWeight;
                best = e;
            }
            return best;
        }

        private void StepCombatFire(EnemyController target, float dt)
        {
            if (target == null || SquadDirector.Instance == null)
            {
                _burstRemaining = 0;
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

        private static readonly RaycastHit[] LineOfFireBuffer = new RaycastHit[16];

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
            var origin = _muzzleTip != null ? _muzzleTip.position : transform.position + transform.forward * 0.65f + Vector3.up * 1.18f;
            var dest = target.transform.position + Vector3.up * 0.95f;
            var flatDir = dest - origin;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.01f) flatDir = transform.forward;
            flatDir.Normalize();

            var spread = Role == SquadRole.MarksmanBishop ? 0.6f : (Role == SquadRole.BreacherTorres ? 1.8f : 2.1f);
            var jitter = UnityEngine.Random.Range(-spread, spread);
            var shotDir = Quaternion.Euler(0f, jitter, 0f) * flatDir;

            var spd = Role == SquadRole.MarksmanBishop ? 50f : 38f;
            var dmg = Role == SquadRole.MarksmanBishop ? 42f : (Role == SquadRole.SquadLeadHayes ? 22f : 20f);
            var pierce = Role == SquadRole.MarksmanBishop ? 1 : 0;
            var visScale = Role == SquadRole.MarksmanBishop ? new Vector3(0.030f, 0.48f, 0.030f) : new Vector3(0.026f, 0.36f, 0.026f);

            SquadDirector.Instance.SpawnSquadBullet(origin, Quaternion.LookRotation(shotDir, Vector3.up), spd, dmg, pierce, visScale);
            ShotsFiredCount++;
            _characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.01f, -0.055f));
            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.RailgunCyan);
            AudioManager.Instance?.PlayEnemyShoot(Role == SquadRole.MarksmanBishop ? 0.78f : 1.08f, 0.16f);
        }

        private static Vector3 ClampToUnlockedBounds(Vector3 pos)
        {
            return EnemySpawner.Instance == null ? pos : EnemySpawner.Instance.ClampPositionToUnlockedSectors(pos);
        }

        private void SetVisualCrouch(bool crouched)
        {
            if (_visualRoot != null)
            {
                _visualRoot.localPosition = crouched ? new Vector3(0f, -0.36f, 0f) : Vector3.zero;
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
