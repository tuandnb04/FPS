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

        [Header("Motion Tuning")]
        [SerializeField] private float gaitFrequency = 2.5f;
        [SerializeField] private float bobAmplitude = 0.07f;
        [SerializeField] private float limbSwingDegrees = 34f;
        [SerializeField] private float forwardLeanDegrees = 8f;
        [SerializeField] private float bankRollDegrees = 5f;
        [SerializeField] private float idleBreathFrequency = 0.6f;
        [SerializeField] private float idleBreathAmplitude = 0.025f;
        [SerializeField] private float settleBlendDuration = 0.30f;

        private Vector3 baseVisualLocalPos;
        private Vector3 baseSecondaryLocalPos;
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
        }

        public void TriggerSquash(Vector3 impactScale, float duration = 0.14f)
        {
            squashStartScale = impactScale;
            squashDuration = Mathf.Max(0.05f, duration);
            squashTimer = squashDuration;
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

            float armFactor = isAimingWeapon ? 0.35f : 0.72f;
            if (leftArmPivot != null)
            {
                float pitch = isAimingWeapon ? -68f - currentLegSwingAngle * 0.15f : -currentLegSwingAngle * armFactor;
                leftArmPivot.localRotation = Quaternion.Euler(pitch, 0f, -8f);
            }
            if (rightArmPivot != null)
            {
                float pitch = isAimingWeapon ? -84f + currentLegSwingAngle * 0.1f : currentLegSwingAngle * armFactor;
                rightArmPivot.localRotation = Quaternion.Euler(pitch, 0f, 8f);
            }

            // Head subtle breathing counter-sway when settled idle
            if (headPivot != null)
            {
                float headPitch = Mathf.Sin(idlePhase * 0.5f) * 2.5f * (1f - moveBlend) - smoothedForwardLean * 0.35f;
                float headYaw = Mathf.Cos(idlePhase * 0.5f) * 4.0f * (1f - moveBlend);
                headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
            }

            // Secondary motion (0.07s lag on backpack/visor cape)
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
        }
    }
}
