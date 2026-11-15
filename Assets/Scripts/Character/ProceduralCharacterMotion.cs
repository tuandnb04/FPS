using UnityEngine;

namespace TopDownShooter.Character
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

        private Vector3 baseVisualLocalPos;
        private Vector3 baseSecondaryLocalPos;
        private Vector3 baseWeaponAnchorLocalPos;
        private Vector3 currentWeaponRecoil;
        private float gaitPhase;
        private float idlePhase;
        private float moveBlend;
        private float smoothedBobOffset;
        private float smoothedForwardLean;
        private float smoothedBankRoll;
        private float currentLegSwingAngle;
        private float currentIdleOffset;

        private Vector3 currentSquashScale = Vector3.one;
        private float squashTimer;
        private float squashDuration = 0.14f;
        private Vector3 squashStartScale = Vector3.one;

        private Vector3 smoothedSecondaryLag;
        private Vector3 prevVelocity;

        public float CurrentBobOffset => smoothedBobOffset;
        public float CurrentLeanAngle => smoothedForwardLean;
        public float CurrentLegAngle => currentLegSwingAngle;
        public float CurrentIdleOffset => currentIdleOffset;
        public bool IsSettledIdle => moveBlend < 0.08f;

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

        public void TriggerWeaponRecoil(Vector3 kickLocal)
        {
            currentWeaponRecoil = Vector3.ClampMagnitude(currentWeaponRecoil + kickLocal, 0.11f);
        }

        private void Awake()
        {
            CacheBasePoses();
        }

        private void CacheBasePoses()
        {
            if (visualRoot != null)
            {
                baseVisualLocalPos = visualRoot.localPosition;
            }
            if (secondaryPivot != null)
            {
                baseSecondaryLocalPos = secondaryPivot.localPosition;
            }
            if (weaponAnchor != null)
            {
                baseWeaponAnchorLocalPos = weaponAnchor.localPosition;
            }
        }

        public void TriggerSquash(Vector3 impactScale, float duration = 0.14f)
        {
            squashStartScale = impactScale;
            squashDuration = Mathf.Max(0.05f, duration);
            squashTimer = squashDuration;
        }

        /// <summary>
        /// Clears runtime locomotion blend, banking, secondary lag, and squash timers across mission resets.
        /// </summary>
        public void ResetMotionState()
        {
            moveBlend = 0f;
            smoothedBobOffset = 0f;
            smoothedForwardLean = 0f;
            smoothedBankRoll = 0f;
            currentLegSwingAngle = 0f;
            currentIdleOffset = 0f;
            prevVelocity = Vector3.zero;
            smoothedSecondaryLag = Vector3.zero;
            currentWeaponRecoil = Vector3.zero;
            squashTimer = 0f;
            currentSquashScale = Vector3.one;
            if (weaponAnchor != null)
            {
                weaponAnchor.localPosition = baseWeaponAnchorLocalPos;
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
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            Vector3 flatVel = new Vector3(worldVelocity.x, 0f, worldVelocity.z);
            float speed = flatVel.magnitude;
            float speedRatio = Mathf.Clamp01(speed / 6.5f);
            bool moving = speed > 0.12f;

            float blendStep = dt / Mathf.Max(0.05f, settleBlendDuration);
            moveBlend = Mathf.MoveTowards(moveBlend, moving ? speedRatio : 0f, blendStep);

            if (moving)
            {
                float stepRate = gaitFrequency * Mathf.Lerp(0.75f, 1.15f, speedRatio);
                gaitPhase += dt * stepRate * Mathf.PI * 2f;
            }
            else
            {
                idlePhase += dt * idleBreathFrequency * Mathf.PI * 2f;
            }

            // Gait bounce vs idle breathing settle
            float activeBob = Mathf.Abs(Mathf.Sin(gaitPhase)) * bobAmplitude * moveBlend;
            currentIdleOffset = Mathf.Sin(idlePhase) * idleBreathAmplitude * (1f - moveBlend);
            smoothedBobOffset = activeBob + currentIdleOffset;

            // Velocity-driven lean and turn banking
            Vector3 localVel = transform.InverseTransformDirection(flatVel);
            float forwardRatio = Mathf.Clamp(localVel.z / 7.0f, -1f, 1f);
            float strafeRatio = Mathf.Clamp(localVel.x / 7.0f, -1f, 1f);

            float targetPitch = forwardRatio * forwardLeanDegrees * moveBlend;
            float turnBank = Mathf.Clamp(-yawVelocityDegPerSec / 180f, -1f, 1f) * bankRollDegrees;
            float targetRoll = Mathf.Clamp(turnBank - strafeRatio * (bankRollDegrees * 0.6f), -bankRollDegrees, bankRollDegrees) * moveBlend;

            smoothedForwardLean = Mathf.Lerp(smoothedForwardLean, targetPitch, 1f - Mathf.Exp(-14f * dt));
            smoothedBankRoll = Mathf.Lerp(smoothedBankRoll, targetRoll, 1f - Mathf.Exp(-14f * dt));

            // Limb oscillation (opposite phase pi radians)
            float swingSin = Mathf.Sin(gaitPhase) * moveBlend;
            currentLegSwingAngle = swingSin * limbSwingDegrees;

            if (leftLegPivot != null)
            {
                leftLegPivot.localRotation = Quaternion.Euler(currentLegSwingAngle, 0f, 0f);
            }
            if (rightLegPivot != null)
            {
                rightLegPivot.localRotation = Quaternion.Euler(-currentLegSwingAngle, 0f, 0f);
            }

            bool useWeaponIk = isAimingWeapon && leftElbowPivot != null && rightElbowPivot != null
                && leftHandTarget != null && rightHandTarget != null;

            if (!useWeaponIk)
            {
                float armFactor = isAimingWeapon ? 0.35f : 0.72f;
                if (leftArmPivot != null)
                {
                    float pitch = isAimingWeapon ? -42f : -currentLegSwingAngle * armFactor;
                    leftArmPivot.localRotation = Quaternion.Euler(pitch, 0f, -4f);
                }
                if (rightArmPivot != null)
                {
                    float pitch = isAimingWeapon ? -55f : currentLegSwingAngle * armFactor;
                    rightArmPivot.localRotation = Quaternion.Euler(pitch, 0f, 4f);
                }
            }

            if (weaponAnchor != null && isAimingWeapon)
            {
                currentWeaponRecoil = Vector3.Lerp(currentWeaponRecoil, Vector3.zero, 1f - Mathf.Exp(-18f * dt));
                float weaponBobY = -Mathf.Abs(Mathf.Sin(gaitPhase)) * 0.018f * moveBlend + currentIdleOffset * 0.45f;
                float weaponSwayX = Mathf.Cos(gaitPhase) * 0.010f * moveBlend;
                weaponAnchor.localPosition = baseWeaponAnchorLocalPos + currentWeaponRecoil + new Vector3(weaponSwayX, weaponBobY, 0f);
            }

            // Head subtle breathing counter-sway when settled idle
            if (headPivot != null)
            {
                float headPitch = Mathf.Sin(idlePhase * 0.5f) * 2.0f * (1f - moveBlend) - smoothedForwardLean * 0.35f;
                float headYaw = Mathf.Cos(idlePhase * 0.5f) * 3.0f * (1f - moveBlend);
                headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
            }

            // Secondary motion (0.07s lag on field rucksack)
            if (secondaryPivot != null)
            {
                Vector3 accel = (flatVel - prevVelocity) / Mathf.Max(0.005f, dt);
                Vector3 localAccel = transform.InverseTransformDirection(Vector3.ClampMagnitude(accel, 25f));
                Vector3 targetLag = new Vector3(-localVel.x * 0.015f, -smoothedBobOffset * 0.3f, -localVel.z * 0.018f)
                                    - localAccel * 0.002f;
                smoothedSecondaryLag = Vector3.Lerp(smoothedSecondaryLag, Vector3.ClampMagnitude(targetLag, 0.12f), 1f - Mathf.Exp(-12f * dt));
                secondaryPivot.localPosition = baseSecondaryLocalPos + smoothedSecondaryLag;
                secondaryPivot.localRotation = Quaternion.Euler(14f * moveBlend + localVel.z * 1.4f, 0f, -smoothedBankRoll * 1.4f);
            }
            prevVelocity = flatVel;

            // Squash & stretch recovery toward Vector3.one baseline
            if (squashTimer > 0f)
            {
                squashTimer = Mathf.Max(0f, squashTimer - dt);
                float normalized = 1f - (squashTimer / squashDuration);
                currentSquashScale = Vector3.Lerp(squashStartScale, Vector3.one, normalized);
            }
            else
            {
                currentSquashScale = Vector3.one;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = baseVisualLocalPos + new Vector3(0f, smoothedBobOffset, 0f);
                visualRoot.localRotation = Quaternion.Euler(smoothedForwardLean, 0f, smoothedBankRoll);
                visualRoot.localScale = currentSquashScale;
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
            Vector3 s = visualRoot.InverseTransformPoint(shoulderPivot.position);
            Vector3 w = visualRoot.InverseTransformPoint(handTarget.position);
            Vector3 toTarget = w - s;
            float dist = toTarget.magnitude;
            if (dist < 0.04f) return;

            float maxReach = Mathf.Max(0.08f, upperLen + foreLen - 0.006f);
            float minReach = Mathf.Max(0.06f, Mathf.Abs(upperLen - foreLen) + 0.02f);
            float c = Mathf.Clamp(dist, minReach, maxReach);
            Vector3 dir = toTarget / dist;

            float cosAngle = Mathf.Clamp((upperLen * upperLen + c * c - foreLen * foreLen) / (2f * upperLen * c), -0.998f, 0.998f);
            float adj = upperLen * cosAngle;
            float perp = Mathf.Sqrt(Mathf.Max(0f, upperLen * upperLen - adj * adj));

            Vector3 adjustedHint = elbowHintLocal;
            if (w.y > 1.24f)
            {
                // Keep elbow angled cleanly downward/outward under over-the-shoulder launchers (RPG-7)
                adjustedHint.y += (w.y - 1.24f) * 0.6f;
                adjustedHint.x *= 1.15f;
                adjustedHint.z += 0.12f;
            }

            Vector3 bendDir = Vector3.ProjectOnPlane(adjustedHint - s, dir);
            if (bendDir.sqrMagnitude < 1e-4f)
            {
                bendDir = Vector3.ProjectOnPlane(Vector3.down + Vector3.back, dir);
            }
            bendDir.Normalize();

            Vector3 elbowPos = s + dir * adj + bendDir * perp;
            Vector3 uSE = (elbowPos - s).normalized;
            Vector3 uEW = (w - elbowPos).normalized;

            Vector3 upperFwd = Vector3.ProjectOnPlane(-bendDir, -uSE);
            if (upperFwd.sqrMagnitude < 1e-4f) upperFwd = Vector3.ProjectOnPlane(Vector3.forward, -uSE);
            Quaternion upperRotLocal = Quaternion.LookRotation(upperFwd.normalized, -uSE);
            shoulderPivot.localRotation = upperRotLocal;

            elbowPivot.localPosition = new Vector3(0f, -upperLen, 0f);
            Vector3 foreFwd = Vector3.ProjectOnPlane(-bendDir, -uEW);
            if (foreFwd.sqrMagnitude < 1e-4f) foreFwd = Vector3.ProjectOnPlane(Vector3.forward, -uEW);
            Quaternion foreRotInVisualRoot = Quaternion.LookRotation(foreFwd.normalized, -uEW);
            elbowPivot.localRotation = Quaternion.Inverse(upperRotLocal) * foreRotInVisualRoot;
        }
    }
}
