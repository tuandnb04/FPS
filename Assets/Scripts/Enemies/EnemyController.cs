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
    /// Pooled compound cyber-sentinel enemy and Dreadnought MK-IV Boss controller
    /// with archetype mesh switching, radial plasma barrages, and telegraphed hazard rings.
    /// </summary>
    public sealed class EnemyController : MonoBehaviour
    {
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

        private ObjectPool<EnemyController> _ownerPool;
        private float _moveSpeed;
        private float _contactDamage;
        private float _rangedDamage;
        private float _projectileSpeed;
        private int _scoreValue;
        private int _activeWaveNumber = 1;
        private string _bossDisplayTitle = "IRONCLAD SCOUT-01";
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

        private MaterialPropertyBlock _mpb;
        private Action<EnemyController, bool> _onFinishedCallback;

        public bool IsDead { get; private set; }

        public EnemyArchetype Archetype { get; private set; }

        public string DisplayCallsign => Archetype switch
        {
            EnemyArchetype.Boss => _bossDisplayTitle,
            EnemyArchetype.Heavy => "IRONWARD ENFORCER",
            _ => "IRONWARD RIFLEMAN"
        };

        private float CurrentHp { get; set; }

        private float MaxHp { get; set; }

        private int ShotsFiredCount { get; set; }

        private Transform ActiveWeaponMuzzle => Archetype switch
        {
            EnemyArchetype.Boss => bossMuzzleTip,
            EnemyArchetype.Heavy => pkmMuzzleTip,
            _ => ak74MuzzleTip
        };

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
            Transform bossMuzzle = null)
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
            _fireCooldown = UnityEngine.Random.Range(1.05f, 1.85f);
            _burstRemaining = 0;
            _burstTimer = 0f;
            _bossBarrageTimer = 2.1f;
            _bossHazardTimer = 3.6f;
            _bossCannonTimer = 1.35f;
            _bossCannonBurst = 0;
            _bossBarrageStep = 0;
            ShotsFiredCount = 0;
            _isAlerted = kind == EnemyArchetype.Boss;
            _aggroRadius = kind == EnemyArchetype.Boss ? 52f : 26f;

            var waveScale = 1f + (_activeWaveNumber - 1) * 0.14f;
            if (bipedalVisualRoot != null) bipedalVisualRoot.SetActive(kind != EnemyArchetype.Boss);
            if (chaserVisualRoot != null) chaserVisualRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (heavyVisualRoot != null) heavyVisualRoot.SetActive(kind == EnemyArchetype.Heavy);
            if (bossVisualRoot != null) bossVisualRoot.SetActive(kind == EnemyArchetype.Boss);
            if (ak74WeaponRoot != null) ak74WeaponRoot.SetActive(kind == EnemyArchetype.Chaser);
            if (pkmWeaponRoot != null) pkmWeaponRoot.SetActive(kind == EnemyArchetype.Heavy);

            switch (kind)
            {
                case EnemyArchetype.Boss:
                    MaxHp = _activeWaveNumber <= 2 ? 380f : (_activeWaveNumber <= 4 ? 540f : 780f);
                    _moveSpeed = 2.45f + Mathf.Min(0.8f, _activeWaveNumber * 0.08f);
                    _contactDamage = 28f;
                    _rangedDamage = 10f;
                    _projectileSpeed = 14.5f;
                    _scoreValue = 1500;
                    _bossDisplayTitle = StoryDirector.GetBossCallsign(_activeWaveNumber);
                    transform.localScale = Vector3.one * 1.15f;
                    UIManager.Instance?.UpdateBossHp(MaxHp, MaxHp, _bossDisplayTitle);
                    break;
                case EnemyArchetype.Heavy:
                {
                    MaxHp = 115f * waveScale;
                    _moveSpeed = 3.15f + Mathf.Min(1.2f, _activeWaveNumber * 0.12f);
                    _contactDamage = 20f;
                    _rangedDamage = 6f + Mathf.Min(3f, _activeWaveNumber * 0.35f);
                    _projectileSpeed = 11.5f + Mathf.Min(1.5f, _activeWaveNumber * 0.18f);
                    _scoreValue = 250;
                    transform.localScale = Vector3.one * 1.22f;
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
                    break;
                }
                default:
                {
                    MaxHp = 48f * waveScale;
                    _moveSpeed = 4.35f + Mathf.Min(1.6f, _activeWaveNumber * 0.18f);
                    _contactDamage = 12f;
                    _rangedDamage = 7f + Mathf.Min(3f, _activeWaveNumber * 0.30f);
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

        private void Update()
        {
            if (IsDead) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
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
                        EnemyArchetype.Boss => 4.4f,
                        EnemyArchetype.Heavy => 5.2f,
                        _ => 4.2f
                    };
                    if (dist > minStopDist)
                    {
                        velocity = dir * _moveSpeed;
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
                        if (targetPlayer != null) targetPlayer.TakeDamage(_contactDamage);
                        else if (targetSquad != null) targetSquad.TakeDamage(_contactDamage);
                    }
                }

                if (Archetype == EnemyArchetype.Boss)
                {
                    StepBossAttacks(player, targetTf, dt);
                }
                else
                {
                    StepInfantryFire(player, targetTf, dist, dt);
                }
            }

            var currentYaw = transform.eulerAngles.y;
            var yawRate = Mathf.DeltaAngle(_prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            _prevYaw = currentYaw;

            if (Archetype != EnemyArchetype.Boss)
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
            {
                var squad = SquadDirector.Instance.Members;
                foreach (var member in squad)
                {
                    if (member == null || member.IsDowned || !member.gameObject.activeInHierarchy) continue;
                    var d = member.transform.position - transform.position;
                    d.y = 0f;
                    var mDist = d.magnitude;
                    // Slight preference (0.85x) toward nearby squadmates so enemies naturally engage the whole fireteam
                    if (!(mDist * 0.88f < bestDist)) continue;
                    bestDist = mDist;
                    bestTf = member.transform;
                    hitPlayer = null;
                    hitSquad = member;
                }
            }
        }

        private void StepInfantryFire(PlayerController player, Transform targetTf, float distToTarget, float dt)
        {
            var maxRange = Archetype == EnemyArchetype.Heavy ? 15.5f : 14.2f;
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
                var muzzle = ActiveWeaponMuzzle;
                FireTracerFromMuzzle(
                    muzzle,
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
                _fireCooldown = UnityEngine.Random.Range(1.80f, 2.35f);
                var muzzle = ActiveWeaponMuzzle;
                FireTracerFromMuzzle(
                    muzzle,
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

        private void StepBossAttacks(PlayerController player, Transform targetTf, float dt)
        {
            if (targetTf == null || player == null) return;
            if (bossTurretPivot != null)
            {
                var aimDir = targetTf.position - bossTurretPivot.position;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.01f)
                {
                    var turretRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
                    bossTurretPivot.rotation = Quaternion.Slerp(bossTurretPivot.rotation, turretRot, 1f - Mathf.Exp(-6f * dt));
                }
            }

            // Coaxial cannon 2-round burst directly from turret barrel
            _bossCannonTimer -= dt;
            if (_bossCannonTimer <= 0f && player.Weapon != null)
            {
                if (_bossCannonBurst <= 0)
                {
                    _bossCannonBurst = 2;
                }
                _bossCannonBurst--;
                _bossCannonTimer = _bossCannonBurst > 0 ? 0.24f : 1.95f;
                FireTracerFromMuzzle(
                    bossMuzzleTip,
                    player,
                    targetTf.position,
                    _projectileSpeed,
                    _rangedDamage,
                    Palette.BossPurple,
                    new Vector3(0.09f, 0.42f, 0.09f),
                    1.5f,
                    0.66f);
            }

            _bossBarrageTimer -= dt;
            if (_bossBarrageTimer <= 0f)
            {
                _bossBarrageTimer = 3.2f;
                _bossBarrageStep++;
                var baseAngle = (_bossBarrageStep % 2 == 0) ? 0f : 22.5f;
                var origin = bossMuzzleTip != null ? bossMuzzleTip.position : transform.position + Vector3.up * 1.1f;

                if (player.Weapon != null)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var angleDeg = baseAngle + i * 45f;
                        var dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
                        player.Weapon.SpawnEnemyProjectile(origin + dir * 0.45f, dir, 12.2f, 14f, Palette.BossPurple, new Vector3(0.20f, 0.20f, 0.20f), 2.5f);
                    }
                }
                VFXManager.Instance?.Play(VfxKind.MuzzleFlash, origin, Palette.BossPurple);
                AudioManager.Instance?.PlayEnemyShoot(0.62f, 0.34f);
            }

            _bossHazardTimer -= dt;
            if (!(_bossHazardTimer <= 0f)) return;
            _bossHazardTimer = 4.8f;
            EnemySpawner.Instance?.SpawnHazardTelegraph(targetTf.position, 2.35f, 22f);
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
            if (Archetype != EnemyArchetype.Boss)
            {
                characterMotion?.TriggerWeaponRecoil(new Vector3(0f, 0.004f, -0.042f));
            }
            AudioManager.Instance?.PlayEnemyShoot(audioPitch, Archetype == EnemyArchetype.Boss ? 0.30f : 0.20f);

            ShotsFiredCount++;
        }

        public void ApplyDamage(float dmg, Vector3 hitPoint, Vector3 shotDir, bool isCrit = false)
        {
            if (IsDead) return;
            _isAlerted = true;

            CurrentHp = Mathf.Max(0f, CurrentHp - dmg);
            _hitFlashTimer = 0.10f;
            ApplyHitFlash(1f);

            if (Archetype != EnemyArchetype.Boss)
            {
                characterMotion?.TriggerSquash(new Vector3(1.22f, 0.80f, 1.22f), 0.12f);
            }

            var dmgLabel = isCrit ? "-" + Mathf.RoundToInt(dmg) + "!" : "-" + Mathf.RoundToInt(dmg);
            var txtCol = isCrit ? Palette.CreditGold : Palette.EnemySecondary;
            FeelManager.Instance?.SpawnFloatingText(hitPoint, dmgLabel, txtCol);

            if (controller != null && controller.enabled && Archetype != EnemyArchetype.Boss)
            {
                var knock = new Vector3(shotDir.x, 0f, shotDir.z).normalized * (Archetype == EnemyArchetype.Heavy ? 0.12f : 0.24f);
                controller.Move(knock);
            }

            if (Archetype == EnemyArchetype.Boss)
            {
                UIManager.Instance?.UpdateBossHp(CurrentHp, MaxHp, _bossDisplayTitle);
            }

            if (CurrentHp <= 0f)
            {
                Die();
            }
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;

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
            foreach (var t in bodyRenderers)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", flashEmission);
                t.SetPropertyBlock(_mpb);
            }
        }
    }
}
