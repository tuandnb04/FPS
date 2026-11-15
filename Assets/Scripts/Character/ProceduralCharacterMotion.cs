using UnityEngine;

namespace Character
{
    /// <summary>
    /// Procedural character animation driving gait bob, limb swing, acceleration/turn lean,
    /// secondary lag motion, squash/stretch impacts, and smooth settle into idle breathing.
    /// </summary>
    public sealed class ProceduralCharacterMotion : MonoBehaviour
    {
        [Header("Rig Pivots")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform headPivot;
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField] private Transform secondaryPivot;

        [Header("Weapon Holding IK (Player)")]
        [SerializeField] private Transform leftElbowPivot;
        [SerializeField] private Transform rightElbowPivot;
        [SerializeField] private Transform weaponAnchor;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;
        [SerializeField] private float upperArmLength = 0.34f;
        [SerializeField] private float forearmLength = 0.35f;

        [Header("Motion Tuning")]
        [SerializeField] private float gaitFrequency = 2.5f;
        [SerializeField] private float bobAmplitude = 0.07f;
        [SerializeField] private float limbSwingDegrees = 34f;
        [SerializeField] private float forwardLeanDegrees = 8f;
        [SerializeField] private float bankRollDegrees = 5f;
        [SerializeField] private float idleBreathFrequency = 0.6f;
        [SerializeField] private float idleBreathAmplitude = 0.025f;
        [SerializeField] private float settleBlendDuration = 0.18f;

        private Vector3 _baseVisualLocalPos;
        private Vector3 _baseSecondaryLocalPos;
        private Vector3 _baseWeaponAnchorLocalPos;
        private Vector3 _currentWeaponRecoil;
        private float _gaitPhase;
        private float _idlePhase;
        private float _moveBlend;
        private float _smoothedBankRoll;

        private Vector3 _currentSquashScale = Vector3.one;
        private float _squashTimer;
        private float _squashDuration = 0.14f;
        private Vector3 _squashStartScale = Vector3.one;

        private Vector3 _smoothedSecondaryLag;
        private Vector3 _prevVelocity;

        public float CurrentBobOffset { get; private set; }

        public float CurrentLeanAngle { get; private set; }

        public float CurrentLegAngle { get; private set; }

        public float CurrentIdleOffset { get; private set; }

        public bool IsSettledIdle => _moveBlend < 0.08f;

        public void BindPivots(
            Transform root,
            Transform head,
            Transform leftArm,
            Transform rightArm,
            Transform leftLeg,
            Transform rightLeg,
            Transform secondary)
        {
            visualRoot = root;
            headPivot = head;
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            leftLegPivot = leftLeg;
            rightLegPivot = rightLeg;
            secondaryPivot = secondary;
            CacheBasePoses();
        }

        public void BindWeaponArmIk(
            Transform leftElbow,
            Transform rightElbow,
            Transform anchor,
            Transform leftHand = null,
            Transform rightHand = null,
            float upperLen = 0.34f,
            float foreLen = 0.35f)
        {
            leftElbowPivot = leftElbow;
            rightElbowPivot = rightElbow;
            weaponAnchor = anchor;
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
            upperArmLength = Mathf.Max(0.15f, upperLen);
            forearmLength = Mathf.Max(0.15f, foreLen);
            CacheBasePoses();
            SolveWeaponArmIkImmediate();
        }

        public void SetWeaponGripTargets(Transform leftHand, Transform rightHand)
        {
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
            SolveWeaponArmIkImmediate();
        }

        public void ConfigureWeaponIkPose(Transform leftHand, Transform rightHand, float upperLen, float foreLen)
        {
            leftHandTarget = leftHand;
            rightHandTarget = rightHand;
            upperArmLength = Mathf.Max(0.15f, upperLen);
            forearmLength = Mathf.Max(0.15f, foreLen);
            if (weaponAnchor != null)
            {
                _baseWeaponAnchorLocalPos = weaponAnchor.localPosition;
            }
            SolveWeaponArmIkImmediate();
        }

        public void TriggerWeaponRecoil(Vector3 kickLocal)
        {
            _currentWeaponRecoil = Vector3.ClampMagnitude(_currentWeaponRecoil + kickLocal, 0.11f);
        }

        private void Awake()
        {
            CacheBasePoses();
        }

        private void CacheBasePoses()
        {
            if (visualRoot != null)
            {
                _baseVisualLocalPos = visualRoot.localPosition;
            }
            if (secondaryPivot != null)
            {
                _baseSecondaryLocalPos = secondaryPivot.localPosition;
            }
            if (weaponAnchor != null)
            {
                _baseWeaponAnchorLocalPos = weaponAnchor.localPosition;
            }
        }

        public void TriggerSquash(Vector3 impactScale, float duration = 0.14f)
        {
            _squashStartScale = impactScale;
            _squashDuration = Mathf.Max(0.05f, duration);
            _squashTimer = _squashDuration;
        }

        /// <summary>
        /// Clears runtime locomotion blend, banking, secondary lag, and squash timers across mission resets.
        /// </summary>
        public void ResetMotionState()
        {
            _moveBlend = 0f;
            CurrentBobOffset = 0f;
            CurrentLeanAngle = 0f;
            _smoothedBankRoll = 0f;
            CurrentLegAngle = 0f;
            CurrentIdleOffset = 0f;
            _prevVelocity = Vector3.zero;
            _smoothedSecondaryLag = Vector3.zero;
            _currentWeaponRecoil = Vector3.zero;
            _squashTimer = 0f;
            _currentSquashScale = Vector3.one;
            if (weaponAnchor != null)
            {
                weaponAnchor.localPosition = _baseWeaponAnchorLocalPos;
            }
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.one;
            }
            SolveWeaponArmIkImmediate();
        }

        /// <summary>
        /// Steps locomotion pose from current world velocity and angular yaw velocity.
        /// Call once per frame from the character or enemy driver.
        /// </summary>
        public void StepMotion(Vector3 worldVelocity, float yawVelocityDegPerSec, bool isAimingWeapon)
        {
            var dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            var flatVel = new Vector3(worldVelocity.x, 0f, worldVelocity.z);
            var speed = flatVel.magnitude;
            var speedRatio = Mathf.Clamp01(speed / 6.5f);
            var moving = speed > 0.12f;

            var blendStep = dt / Mathf.Max(0.05f, settleBlendDuration);
            _moveBlend = Mathf.MoveTowards(_moveBlend, moving ? speedRatio : 0f, blendStep);

            if (moving)
            {
                var stepRate = gaitFrequency * Mathf.Lerp(0.75f, 1.15f, speedRatio);
                _gaitPhase += dt * stepRate * Mathf.PI * 2f;
            }
            else
            {
                _idlePhase += dt * idleBreathFrequency * Mathf.PI * 2f;
            }

            // Gait bounce vs idle breathing settle
            var activeBob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * bobAmplitude * _moveBlend;
            CurrentIdleOffset = Mathf.Sin(_idlePhase) * idleBreathAmplitude * (1f - _moveBlend);
            CurrentBobOffset = activeBob + CurrentIdleOffset;

            // Velocity-driven lean and turn banking
            var localVel = transform.InverseTransformDirection(flatVel);
            var forwardRatio = Mathf.Clamp(localVel.z / 7.0f, -1f, 1f);
            var strafeRatio = Mathf.Clamp(localVel.x / 7.0f, -1f, 1f);

            var targetPitch = forwardRatio * forwardLeanDegrees * _moveBlend;
            var turnBank = Mathf.Clamp(-yawVelocityDegPerSec / 180f, -1f, 1f) * bankRollDegrees;
            var targetRoll = Mathf.Clamp(turnBank - strafeRatio * (bankRollDegrees * 0.6f), -bankRollDegrees, bankRollDegrees) * _moveBlend;

            CurrentLeanAngle = Mathf.Lerp(CurrentLeanAngle, targetPitch, 1f - Mathf.Exp(-14f * dt));
            _smoothedBankRoll = Mathf.Lerp(_smoothedBankRoll, targetRoll, 1f - Mathf.Exp(-14f * dt));

            // Limb oscillation (opposite phase pi radians)
            var swingSin = Mathf.Sin(_gaitPhase) * _moveBlend;
            CurrentLegAngle = swingSin * limbSwingDegrees;

            if (leftLegPivot != null)
            {
                leftLegPivot.localRotation = Quaternion.Euler(CurrentLegAngle, 0f, 0f);
            }
            if (rightLegPivot != null)
            {
                rightLegPivot.localRotation = Quaternion.Euler(-CurrentLegAngle, 0f, 0f);
            }

            var useWeaponIk = isAimingWeapon && leftElbowPivot != null && rightElbowPivot != null
                              && leftHandTarget != null && rightHandTarget != null;

            if (!useWeaponIk)
            {
                var armFactor = isAimingWeapon ? 0.35f : 0.72f;
                if (leftArmPivot != null)
                {
                    var pitch = isAimingWeapon ? -42f : -CurrentLegAngle * armFactor;
                    leftArmPivot.localRotation = Quaternion.Euler(pitch, 0f, -4f);
                }
                if (rightArmPivot != null)
                {
                    var pitch = isAimingWeapon ? -55f : CurrentLegAngle * armFactor;
                    rightArmPivot.localRotation = Quaternion.Euler(pitch, 0f, 4f);
                }
            }

            if (weaponAnchor != null && isAimingWeapon)
            {
                _currentWeaponRecoil = Vector3.Lerp(_currentWeaponRecoil, Vector3.zero, 1f - Mathf.Exp(-18f * dt));
                var weaponBobY = -Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.018f * _moveBlend + CurrentIdleOffset * 0.45f;
                var weaponSwayX = Mathf.Cos(_gaitPhase) * 0.010f * _moveBlend;
                weaponAnchor.localPosition = _baseWeaponAnchorLocalPos + _currentWeaponRecoil + new Vector3(weaponSwayX, weaponBobY, 0f);
            }

            // Head subtle breathing counter-sway when settled idle
            if (headPivot != null)
            {
                var headPitch = Mathf.Sin(_idlePhase * 0.5f) * 2.0f * (1f - _moveBlend) - CurrentLeanAngle * 0.35f;
                var headYaw = Mathf.Cos(_idlePhase * 0.5f) * 3.0f * (1f - _moveBlend);
                headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
            }

            // Secondary motion (0.07s lag on field rucksack)
            if (secondaryPivot != null)
            {
                var accel = (flatVel - _prevVelocity) / Mathf.Max(0.005f, dt);
                var localAccel = transform.InverseTransformDirection(Vector3.ClampMagnitude(accel, 25f));
                var targetLag = new Vector3(-localVel.x * 0.015f, -CurrentBobOffset * 0.3f, -localVel.z * 0.018f)
                                - localAccel * 0.002f;
                _smoothedSecondaryLag = Vector3.Lerp(_smoothedSecondaryLag, Vector3.ClampMagnitude(targetLag, 0.12f), 1f - Mathf.Exp(-12f * dt));
                secondaryPivot.localPosition = _baseSecondaryLocalPos + _smoothedSecondaryLag;
                secondaryPivot.localRotation = Quaternion.Euler(14f * _moveBlend + localVel.z * 1.4f, 0f, -_smoothedBankRoll * 1.4f);
            }
            _prevVelocity = flatVel;

            // Squash & stretch recovery toward Vector3.one baseline
            if (_squashTimer > 0f)
            {
                _squashTimer = Mathf.Max(0f, _squashTimer - dt);
                var normalized = 1f - (_squashTimer / _squashDuration);
                _currentSquashScale = Vector3.Lerp(_squashStartScale, Vector3.one, normalized);
            }
            else
            {
                _currentSquashScale = Vector3.one;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = _baseVisualLocalPos + new Vector3(0f, CurrentBobOffset, 0f);
                visualRoot.localRotation = Quaternion.Euler(CurrentLeanAngle, 0f, _smoothedBankRoll);
                visualRoot.localScale = _currentSquashScale;
            }

            if (useWeaponIk)
            {
                SolveWeaponArmIkImmediate();
            }
        }

        /// <summary>
        /// Solves 2-bone analytical IK for both operator arms so gloved hands remain locked onto active firearm grips.
        /// Safe to call in both Edit Mode and runtime.
        /// </summary>
        public void SolveWeaponArmIkImmediate()
        {
            if (visualRoot == null) return;
            if (leftArmPivot != null && leftElbowPivot != null && leftHandTarget != null)
            {
                SolveTwoBoneArm(
                    leftArmPivot,
                    leftElbowPivot,
                    leftHandTarget,
                    new Vector3(-0.36f, 0.86f, 0.20f),
                    upperArmLength,
                    forearmLength);
            }
            if (rightArmPivot != null && rightElbowPivot != null && rightHandTarget != null)
            {
                SolveTwoBoneArm(
                    rightArmPivot,
                    rightElbowPivot,
                    rightHandTarget,
                    new Vector3(0.45f, 0.90f, -0.05f),
                    upperArmLength,
                    forearmLength);
            }
        }

        private void SolveTwoBoneArm(
            Transform shoulderPivot,
            Transform elbowPivot,
            Transform handTarget,
            Vector3 elbowHintLocal,
            float upperLen,
            float foreLen)
        {
            var s = visualRoot.InverseTransformPoint(shoulderPivot.position);
            var w = visualRoot.InverseTransformPoint(handTarget.position);
            var toTarget = w - s;
            var dist = toTarget.magnitude;
            if (dist < 0.04f) return;

            var maxReach = Mathf.Max(0.08f, upperLen + foreLen - 0.006f);
            var minReach = Mathf.Max(0.06f, Mathf.Abs(upperLen - foreLen) + 0.02f);
            var c = Mathf.Clamp(dist, minReach, maxReach);
            var dir = toTarget / dist;

            var cosAngle = Mathf.Clamp((upperLen * upperLen + c * c - foreLen * foreLen) / (2f * upperLen * c), -0.998f, 0.998f);
            var adj = upperLen * cosAngle;
            var perp = Mathf.Sqrt(Mathf.Max(0f, upperLen * upperLen - adj * adj));

            var adjustedHint = elbowHintLocal;
            if (w.y > 1.24f)
            {
                // Keep elbow angled cleanly downward/outward under over-the-shoulder launchers (RPG-7)
                adjustedHint.y += (w.y - 1.24f) * 0.6f;
                adjustedHint.x *= 1.15f;
                adjustedHint.z += 0.12f;
            }

            var bendDir = Vector3.ProjectOnPlane(adjustedHint - s, dir);
            if (bendDir.sqrMagnitude < 1e-4f)
            {
                bendDir = Vector3.ProjectOnPlane(Vector3.down + Vector3.back, dir);
            }
            bendDir.Normalize();

            var elbowPos = s + dir * adj + bendDir * perp;
            var uSe = (elbowPos - s).normalized;
            var uEw = (w - elbowPos).normalized;

            var upperFwd = Vector3.ProjectOnPlane(-bendDir, -uSe);
            if (upperFwd.sqrMagnitude < 1e-4f) upperFwd = Vector3.ProjectOnPlane(Vector3.forward, -uSe);
            var upperRotLocal = Quaternion.LookRotation(upperFwd.normalized, -uSe);
            shoulderPivot.localRotation = upperRotLocal;

            elbowPivot.localPosition = new Vector3(0f, -upperLen, 0f);
            var foreFwd = Vector3.ProjectOnPlane(-bendDir, -uEw);
            if (foreFwd.sqrMagnitude < 1e-4f) foreFwd = Vector3.ProjectOnPlane(Vector3.forward, -uEw);
            var foreRotInVisualRoot = Quaternion.LookRotation(foreFwd.normalized, -uEw);
            elbowPivot.localRotation = Quaternion.Inverse(upperRotLocal) * foreRotInVisualRoot;
        }
    }
}
