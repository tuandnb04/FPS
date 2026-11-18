using Combat;
using UnityEngine;
using UnityEngine.Serialization;

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
        [SerializeField] private Transform leftKneePivot;
        [SerializeField] private Transform rightKneePivot;
        [SerializeField] private Transform leftBootPivot;
        [SerializeField] private Transform rightBootPivot;
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
        private float _currentRecoilPitchDeg;
        private float _currentRecoilYawDeg;
        private float _currentTorsoRecoilPitch;
        private bool _isAimingDownSights;
        private float _adsBlend;
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
        private bool _isReloadingPose;
        private float _reloadPoseBlend;
        private float _reloadNormalizedProgress;
        private ReloadStyle _reloadStyle = ReloadStyle.DetachableBoxMag;
        private bool _shotgunPumpPhase;
        private float _shotgunShellPhase;
        private Mesh _heldPropOverrideMesh;
        private Material _heldPropOverrideMat;
        private Vector3 _heldPropOverrideScale = Vector3.one * 0.68f;
        [FormerlySerializedAs("_reloadMagMesh")] [SerializeField, HideInInspector] private Mesh reloadMagMesh;
        [FormerlySerializedAs("_reloadMagMaterial")] [SerializeField, HideInInspector] private Material reloadMagMaterial;
        [FormerlySerializedAs("_heldMagVisual")] [SerializeField, HideInInspector] private GameObject heldMagVisual;
        [FormerlySerializedAs("_attachedMagVisual")] [SerializeField, HideInInspector] private Transform attachedMagVisual;
        private MeshFilter _heldMagMeshFilter;
        private MeshRenderer _heldMagMeshRenderer;

        public bool IsInReloadPose => _isReloadingPose;
        public float ReloadNormalizedProgress => _reloadNormalizedProgress;
        public ReloadStyle ActiveReloadStyle => _reloadStyle;
        public float AdsBlend => _adsBlend;
        public Vector3 CurrentWeaponRecoilOffset => _currentWeaponRecoil;
        public float CurrentRecoilPitchDeg => _currentRecoilPitchDeg;

        public float CurrentBobOffset { get; private set; }

        public float CurrentLeanAngle { get; private set; }

        public float CurrentLegAngle { get; private set; }

        public float CurrentLeftKneeAngle { get; private set; }

        public float CurrentRightKneeAngle { get; private set; }

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

        public void SetAimDownSightsState(bool ads)
        {
            _isAimingDownSights = ads;
        }

        public void TriggerWeaponRecoil(Vector3 kickLocal, float pitchRiseDeg = 8.5f)
        {
            var isAds = _isAimingDownSights || _adsBlend >= 0.5f;
            var linearScale = isAds ? 0.85f : 1.35f;
            var climbScale = isAds ? 0.55f : 1.40f;

            // Always kick straight BACKWARD (-Z) opposite the weapon barrel
            var backZ = -Mathf.Max(0.035f, Mathf.Abs(kickLocal.z)) * linearScale;
            var rawKick = new Vector3(kickLocal.x, Mathf.Abs(kickLocal.y) * 0.6f, backZ);
            _currentWeaponRecoil = Vector3.ClampMagnitude(_currentWeaponRecoil + rawKick, 0.145f);
            _currentWeaponRecoil.z = Mathf.Min(-0.025f, _currentWeaponRecoil.z);

            _currentRecoilPitchDeg = Mathf.Clamp(_currentRecoilPitchDeg + Mathf.Abs(pitchRiseDeg) * climbScale, 0f, isAds ? 10.5f : 19f);
            _currentRecoilYawDeg = 0f;
            _currentTorsoRecoilPitch = Mathf.Clamp(_currentTorsoRecoilPitch + (isAds ? 2.2f : 4.2f), 0f, 8.5f);
        }

        public void SetReloadState(bool reloading, float normalizedProgress)
        {
            SetReloadState(reloading, normalizedProgress, ReloadStyle.DetachableBoxMag, null, false, 0f);
        }

        public void SetReloadState(
            bool reloading,
            float normalizedProgress,
            ReloadStyle style,
            Mesh heldPropMesh = null,
            bool shotgunPumpPhase = false,
            float shotgunShellPhase = 0f,
            Material heldPropMat = null,
            Vector3? heldPropScale = null)
        {
            _isReloadingPose = reloading;
            _reloadNormalizedProgress = reloading ? Mathf.Clamp01(normalizedProgress) : 0f;
            _reloadStyle = style;
            _heldPropOverrideMesh = heldPropMesh;
            _heldPropOverrideMat = heldPropMat;
            _heldPropOverrideScale = heldPropScale ?? Vector3.one * 0.68f;
            _shotgunPumpPhase = shotgunPumpPhase;
            _shotgunShellPhase = Mathf.Clamp01(shotgunShellPhase);

            if (attachedMagVisual != null)
            {
                var showAttached = !reloading || _reloadNormalizedProgress >= 0.68f;
                if (attachedMagVisual.gameObject.activeSelf != showAttached)
                {
                    attachedMagVisual.gameObject.SetActive(showAttached);
                }
            }
        }

        public void BindAttachedMagVisual(Transform attachedMag)
        {
            attachedMagVisual = attachedMag;
        }

        public void BindReloadMagVisual(Mesh magMesh, Material magMat)
        {
            reloadMagMesh = magMesh;
            reloadMagMaterial = magMat;
            if (visualRoot != null && heldMagVisual == null && reloadMagMesh != null)
            {
                var existing = visualRoot.Find("ReloadHeldMagazine");
                heldMagVisual = existing != null ? existing.gameObject : new GameObject("ReloadHeldMagazine");
                heldMagVisual.transform.SetParent(visualRoot, false);
                _heldMagMeshFilter = heldMagVisual.GetComponent<MeshFilter>();
                if (_heldMagMeshFilter == null) _heldMagMeshFilter = heldMagVisual.AddComponent<MeshFilter>();
                _heldMagMeshFilter.sharedMesh = reloadMagMesh;
                var mr = heldMagVisual.GetComponent<MeshRenderer>();
                if (mr == null) mr = heldMagVisual.AddComponent<MeshRenderer>();
                mr.sharedMaterial = reloadMagMaterial != null ? reloadMagMaterial : Core.Palette.CreateLitMaterial("Mat_HeldMag", Core.Palette.Gunmetal, 0.72f, 0.50f, false, 0f);
                heldMagVisual.transform.localScale = Vector3.one * 0.70f;
                heldMagVisual.SetActive(false);
            }
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
            if (leftLegPivot != null)
            {
                if (leftKneePivot == null) leftKneePivot = leftLegPivot.Find("LeftKneePivot");
                if (leftBootPivot == null && leftKneePivot != null) leftBootPivot = leftKneePivot.Find("LeftBootPivot");
            }
            if (rightLegPivot != null)
            {
                if (rightKneePivot == null) rightKneePivot = rightLegPivot.Find("RightKneePivot");
                if (rightBootPivot == null && rightKneePivot != null) rightBootPivot = rightKneePivot.Find("RightBootPivot");
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
        /// <summary>Computes anatomical thigh pitch (-X forward), knee flex (+X backward), and ankle angle for a leg at <paramref name="phaseRad"/>.</summary>
        private static void ComputeLegJointAngles(
            float phaseRad,
            float swingAmplitudeDeg,
            float moveBlend,
            float adsBlend,
            out float hipDeg,
            out float kneeDeg,
            out float ankleDeg)
        {
            var strideSin = Mathf.Sin(phaseRad) * moveBlend;
            var forwardLift = Mathf.Max(0f, Mathf.Cos(phaseRad)) * moveBlend;
            var backDrive = Mathf.Max(0f, -strideSin);
            var frontReach = Mathf.Max(0f, strideSin);

            var stanceBend = 4f + 18f * adsBlend;
            var strideBend = frontReach * 24f + forwardLift * 36f + backDrive * 14f;

            kneeDeg = Mathf.Clamp(stanceBend + strideBend, 0f, 88f);
            hipDeg = -strideSin * swingAmplitudeDeg - stanceBend * 0.45f;
            ankleDeg = -kneeDeg * 0.38f + strideSin * 10f;
        }

        private float ApplyLegPose(Transform hipPivot, Transform kneePivot, Transform bootPivot, float phaseRad, float moveBlend, float adsBlend)
        {
            ComputeLegJointAngles(phaseRad, limbSwingDegrees, moveBlend, adsBlend, out var hipDeg, out var kneeDeg, out var ankleDeg);

            if (hipPivot != null)
            {
                hipPivot.localRotation = Quaternion.Euler(hipDeg, 0f, 0f);
            }
            if (kneePivot != null)
            {
                kneePivot.localRotation = Quaternion.Euler(kneeDeg, 0f, 0f);
            }
            if (bootPivot != null)
            {
                bootPivot.localRotation = Quaternion.Euler(ankleDeg, 0f, 0f);
            }

            return kneeDeg;
        }

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
            _currentRecoilPitchDeg = 0f;
            _currentRecoilYawDeg = 0f;
            _currentTorsoRecoilPitch = 0f;
            _isAimingDownSights = false;
            _adsBlend = 0f;
            CurrentLeftKneeAngle = 0f;
            CurrentRightKneeAngle = 0f;
            if (leftKneePivot != null) leftKneePivot.localRotation = Quaternion.identity;
            if (rightKneePivot != null) rightKneePivot.localRotation = Quaternion.identity;
            if (leftBootPivot != null) leftBootPivot.localRotation = Quaternion.identity;
            if (rightBootPivot != null) rightBootPivot.localRotation = Quaternion.identity;
            _isReloadingPose = false;
            _reloadPoseBlend = 0f;
            _reloadNormalizedProgress = 0f;
            if (heldMagVisual != null)
            {
                heldMagVisual.SetActive(false);
            }
            _squashTimer = 0f;
            _currentSquashScale = Vector3.one;
            if (weaponAnchor != null)
            {
                weaponAnchor.localPosition = _baseWeaponAnchorLocalPos;
                weaponAnchor.localRotation = Quaternion.identity;
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

            // Blend Aim-Down-Sights posture and damp angular/torso recoil impulses
            _adsBlend = Mathf.MoveTowards(_adsBlend, (_isAimingDownSights && !_isReloadingPose) ? 1f : 0f, dt * 11f);
            var recoilDamp = 1f - Mathf.Exp(-14f * dt);
            _currentRecoilPitchDeg = Mathf.Lerp(_currentRecoilPitchDeg, 0f, recoilDamp);
            _currentRecoilYawDeg = Mathf.Lerp(_currentRecoilYawDeg, 0f, recoilDamp);
            _currentTorsoRecoilPitch = Mathf.Lerp(_currentTorsoRecoilPitch, 0f, 1f - Mathf.Exp(-12f * dt));

            // Gait bounce vs idle breathing settle (calmer vertical bob when aiming down sights)
            var activeBob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * bobAmplitude * _moveBlend * Mathf.Lerp(1f, 0.55f, _adsBlend);
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

            // Articulated 3-joint leg oscillation (opposite phase pi radians)
            var swingSin = Mathf.Sin(_gaitPhase) * _moveBlend;
            CurrentLegAngle = swingSin * limbSwingDegrees;

            CurrentLeftKneeAngle = ApplyLegPose(leftLegPivot, leftKneePivot, leftBootPivot, _gaitPhase, _moveBlend, _adsBlend);
            CurrentRightKneeAngle = ApplyLegPose(rightLegPivot, rightKneePivot, rightBootPivot, _gaitPhase + Mathf.PI, _moveBlend, _adsBlend);

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
                _reloadPoseBlend = Mathf.MoveTowards(_reloadPoseBlend, _isReloadingPose ? 1f : 0f, dt * 8.5f);
                var magSeatPulse = 0f;
                var pumpKickZ = 0f;
                var cantEuler = new Vector3(24f, -32f, 24f);

                if (_isReloadingPose)
                {
                    switch (_reloadStyle)
                    {
                        case ReloadStyle.ShellByShellShotgun:
                            cantEuler = new Vector3(-18f, 24f, 42f);
                            magSeatPulse = _shotgunPumpPhase
                                ? 0f
                                : Mathf.Exp(-Mathf.Pow((_shotgunShellPhase - 0.85f) / 0.10f, 2f)) * 0.018f;
                            pumpKickZ = _shotgunPumpPhase
                                ? -Mathf.Sin(_shotgunShellPhase * Mathf.PI) * 0.045f
                                : 0f;
                            break;
                        case ReloadStyle.FrontLoadedRocket:
                            cantEuler = new Vector3(18f, -20f, 12f);
                            magSeatPulse = Mathf.Exp(-Mathf.Pow((_reloadNormalizedProgress - 0.66f) / 0.06f, 2f)) * 0.032f;
                            break;
                        case ReloadStyle.BeltFedBoxLmg:
                            cantEuler = new Vector3(12f, -26f, 16f);
                            magSeatPulse = Mathf.Exp(-Mathf.Pow((_reloadNormalizedProgress - 0.76f) / 0.055f, 2f)) * 0.030f;
                            break;
                        case ReloadStyle.SwingOutCylinder:
                            cantEuler = new Vector3(16f, -24f, -26f);
                            magSeatPulse = Mathf.Exp(-Mathf.Pow((_reloadNormalizedProgress - 0.74f) / 0.055f, 2f)) * 0.026f;
                            break;
                        default:
                            magSeatPulse = Mathf.Exp(-Mathf.Pow((_reloadNormalizedProgress - 0.68f) / 0.055f, 2f)) * 0.028f;
                            break;
                    }
                }

                var cantCurve = _isReloadingPose
                    ? Mathf.Clamp01(Mathf.Sin(_reloadNormalizedProgress * Mathf.PI) * 1.35f)
                    : 1f;
                if (_isReloadingPose && _reloadStyle == ReloadStyle.ShellByShellShotgun && !_shotgunPumpPhase)
                {
                    cantCurve = 1f;
                }
                var effectiveCant = _reloadPoseBlend * cantCurve;
                var stanceSwayScale = Mathf.Lerp(1.0f, 0.32f, _adsBlend);
                var hipPosOffset = new Vector3(0.045f, -0.068f, -0.035f);
                var adsPosOffset = new Vector3(-0.048f, 0.055f, 0.028f);
                var posturePos = Vector3.Lerp(hipPosOffset, adsPosOffset, _adsBlend);

                var weaponBobY = (-Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.014f * _moveBlend + CurrentIdleOffset * 0.35f) * stanceSwayScale - 0.042f * effectiveCant + magSeatPulse;
                var weaponSwayX = -0.032f * effectiveCant;
                weaponAnchor.localPosition = _baseWeaponAnchorLocalPos + posturePos + _currentWeaponRecoil + new Vector3(weaponSwayX, weaponBobY, -0.025f * effectiveCant + pumpKickZ);
            }

            // Head subtle breathing counter-sway + ADS cheek-weld tilt + subtle upward recoil kick
            if (headPivot != null)
            {
                var headPitch = Mathf.Sin(_idlePhase * 0.5f) * 2.0f * (1f - _moveBlend) - CurrentLeanAngle * 0.35f;
                var headYaw = Mathf.Cos(_idlePhase * 0.5f) * 3.0f * (1f - _moveBlend);
                var adsHeadEuler = new Vector3(9f, 5f, -8f) * _adsBlend;
                headPivot.localRotation = Quaternion.Euler(headPitch - _currentTorsoRecoilPitch * 0.35f + adsHeadEuler.x, headYaw + adsHeadEuler.y, adsHeadEuler.z);
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
                visualRoot.localPosition = _baseVisualLocalPos + new Vector3(0f, CurrentBobOffset - 0.035f * _adsBlend, 0f);
                visualRoot.localRotation = Quaternion.Euler(CurrentLeanAngle - _currentTorsoRecoilPitch + 4f * _adsBlend, 0f, _smoothedBankRoll);
                visualRoot.localScale = _currentSquashScale;
            }

            if (isAimingWeapon)
            {
                AlignWeaponBarrelWithBodyYaw();
            }
            else if (useWeaponIk)
            {
                SolveWeaponArmIkImmediate();
            }
        }

        /// <summary>
        /// Locks the weapon barrel's horizontal yaw strictly 1:1 with character facing (immune to torso lean/bank roll or hipfire cant)
        /// so projectiles and laser sights stay coaxial with the physical barrel, then updates arm IK.
        /// </summary>
        public void AlignWeaponBarrelWithBodyYaw()
        {
            if (weaponAnchor == null) return;
            var cantCurve = _isReloadingPose
                ? (_reloadStyle == ReloadStyle.ShellByShellShotgun && !_shotgunPumpPhase
                    ? 1f
                    : Mathf.Clamp01(Mathf.Sin(_reloadNormalizedProgress * Mathf.PI) * 1.35f))
                : 1f;
            var effectiveCant = _reloadPoseBlend * cantCurve;
            var straightWorldRot = Quaternion.Euler(-_currentRecoilPitchDeg, transform.eulerAngles.y, 0f);
            if (effectiveCant <= 0.001f || visualRoot == null)
            {
                weaponAnchor.rotation = straightWorldRot;
            }
            else
            {
                var cantEuler = _reloadStyle switch
                {
                    ReloadStyle.ShellByShellShotgun => new Vector3(-18f, 24f, 42f),
                    ReloadStyle.FrontLoadedRocket => new Vector3(18f, -20f, 12f),
                    ReloadStyle.BeltFedBoxLmg => new Vector3(12f, -26f, 16f),
                    ReloadStyle.SwingOutCylinder => new Vector3(16f, -24f, -26f),
                    _ => new Vector3(24f, -32f, 24f)
                };
                weaponAnchor.rotation = Quaternion.Slerp(straightWorldRot, visualRoot.rotation * Quaternion.Euler(cantEuler), effectiveCant);
            }

            if (leftElbowPivot != null && rightElbowPivot != null && leftHandTarget != null && rightHandTarget != null)
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
                var leftTargetWorld = leftHandTarget.position;
                if (_reloadPoseBlend > 0.02f)
                {
                    var animTargetWorld = ComputeLeftHandReloadWorldPos(leftTargetWorld);
                    leftTargetWorld = Vector3.Lerp(leftTargetWorld, animTargetWorld, _reloadPoseBlend);
                }

                SolveTwoBoneArm(
                    leftArmPivot,
                    leftElbowPivot,
                    leftTargetWorld,
                    new Vector3(-0.36f, 0.86f, 0.20f),
                    upperArmLength,
                    forearmLength);

                UpdateHeldMagVisual(leftTargetWorld);
            }
            else if (heldMagVisual != null && heldMagVisual.activeSelf)
            {
                heldMagVisual.SetActive(false);
            }

            if (rightArmPivot != null && rightElbowPivot != null && rightHandTarget != null)
            {
                SolveTwoBoneArm(
                    rightArmPivot,
                    rightElbowPivot,
                    rightHandTarget.position,
                    new Vector3(0.45f, 0.90f, -0.05f),
                    upperArmLength,
                    forearmLength);
            }
        }

        private Vector3 ComputeLeftHandReloadWorldPos(Vector3 gripWorld)
        {
            var pouchWorld = visualRoot.TransformPoint(new Vector3(-0.22f, 0.94f, 0.16f));
            var boltFwdWorld = weaponAnchor != null
                ? weaponAnchor.TransformPoint(new Vector3(-0.075f, 0.045f, 0.05f))
                : gripWorld;
            var boltBackWorld = weaponAnchor != null
                ? weaponAnchor.TransformPoint(new Vector3(-0.075f, 0.045f, -0.045f))
                : gripWorld;

            var p = _reloadNormalizedProgress;
            switch (_reloadStyle)
            {
                case ReloadStyle.ShellByShellShotgun:
                {
                    if (_shotgunPumpPhase)
                    {
                        var pumpBackWorld = weaponAnchor != null
                            ? weaponAnchor.TransformPoint(new Vector3(0f, -0.025f, 0.11f))
                            : gripWorld - Vector3.forward * 0.07f;
                        var pt = _shotgunShellPhase;
                        return pt < 0.5f
                            ? Vector3.Lerp(gripWorld, pumpBackWorld, pt * 2f)
                            : Vector3.Lerp(pumpBackWorld, gripWorld, (pt - 0.5f) * 2f);
                    }

                    var shellPortWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0f, -0.058f, 0.075f))
                        : gripWorld + Vector3.down * 0.06f;
                    var beltCaddyWorld = visualRoot.TransformPoint(new Vector3(-0.22f, 0.91f, 0.14f));
                    var st = _shotgunShellPhase;
                    if (st < 0.28f) return Vector3.Lerp(gripWorld, beltCaddyWorld, st / 0.28f);
                    if (st < 0.72f) return Vector3.Lerp(beltCaddyWorld, shellPortWorld, (st - 0.28f) / 0.44f);
                    return Vector3.Lerp(shellPortWorld, beltCaddyWorld, (st - 0.72f) / 0.28f);
                }
                case ReloadStyle.FrontLoadedRocket:
                {
                    var quiverWorld = visualRoot.TransformPoint(new Vector3(-0.24f, 0.88f, -0.06f));
                    var muzzleEntryWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0f, 0.025f, 0.64f))
                        : gripWorld + Vector3.forward * 0.45f;
                    var boreSeatWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0f, 0.025f, 0.44f))
                        : gripWorld + Vector3.forward * 0.28f;
                    if (p < 0.18f) return Vector3.Lerp(gripWorld, quiverWorld, p / 0.18f);
                    if (p < 0.44f) return Vector3.Lerp(quiverWorld, muzzleEntryWorld, (p - 0.18f) / 0.26f);
                    if (p < 0.68f) return Vector3.Lerp(muzzleEntryWorld, boreSeatWorld, (p - 0.44f) / 0.24f);
                    return Vector3.Lerp(boreSeatWorld, gripWorld, (p - 0.68f) / 0.32f);
                }
                case ReloadStyle.BeltFedBoxLmg:
                {
                    var coverLatchWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0f, 0.085f, 0.02f))
                        : gripWorld + Vector3.up * 0.08f;
                    var boxWellWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0.02f, -0.105f, 0.11f))
                        : gripWorld + Vector3.down * 0.09f;
                    var beltTrayWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0.045f, 0.075f, 0.10f))
                        : gripWorld + Vector3.up * 0.06f;
                    if (p < 0.16f) return Vector3.Lerp(gripWorld, coverLatchWorld, p / 0.16f);
                    if (p < 0.34f) return Vector3.Lerp(coverLatchWorld, pouchWorld, (p - 0.16f) / 0.18f);
                    if (p < 0.64f) return Vector3.Lerp(pouchWorld, boxWellWorld, (p - 0.34f) / 0.30f);
                    if (p < 0.78f) return Vector3.Lerp(boxWellWorld, beltTrayWorld, (p - 0.64f) / 0.14f);
                    if (p < 0.89f) return Vector3.Lerp(beltTrayWorld, boltBackWorld, (p - 0.78f) / 0.11f);
                    return Vector3.Lerp(boltBackWorld, gripWorld, (p - 0.89f) / 0.11f);
                }
                case ReloadStyle.SwingOutCylinder:
                {
                    var cylOpenWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(-0.085f, 0.01f, 0.055f))
                        : gripWorld + Vector3.left * 0.06f;
                    var cylCloseWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(-0.02f, 0.01f, 0.055f))
                        : gripWorld;
                    if (p < 0.18f) return Vector3.Lerp(gripWorld, cylOpenWorld, p / 0.18f);
                    if (p < 0.42f) return Vector3.Lerp(cylOpenWorld, pouchWorld, (p - 0.18f) / 0.24f);
                    if (p < 0.68f) return Vector3.Lerp(pouchWorld, cylOpenWorld, (p - 0.42f) / 0.26f);
                    if (p < 0.84f) return Vector3.Lerp(cylOpenWorld, cylCloseWorld, (p - 0.68f) / 0.16f);
                    return Vector3.Lerp(cylCloseWorld, gripWorld, (p - 0.84f) / 0.16f);
                }
                default:
                {
                    var magWellWorld = weaponAnchor != null
                        ? weaponAnchor.TransformPoint(new Vector3(0f, -0.09f, 0.045f))
                        : gripWorld + Vector3.down * 0.08f;
                    if (p < 0.22f) return Vector3.Lerp(gripWorld, magWellWorld, p / 0.22f);
                    if (p < 0.46f) return Vector3.Lerp(magWellWorld, pouchWorld, (p - 0.22f) / 0.24f);
                    if (p < 0.68f) return Vector3.Lerp(pouchWorld, magWellWorld, (p - 0.46f) / 0.22f);
                    if (p < 0.80f) return Vector3.Lerp(magWellWorld, boltFwdWorld, (p - 0.68f) / 0.12f);
                    if (p < 0.90f) return Vector3.Lerp(boltFwdWorld, boltBackWorld, (p - 0.80f) / 0.10f);
                    return Vector3.Lerp(boltBackWorld, gripWorld, (p - 0.90f) / 0.10f);
                }
            }
        }

        private void UpdateHeldMagVisual(Vector3 leftHandWorldPos)
        {
            if (heldMagVisual == null)
            {
                var found = visualRoot != null ? visualRoot.Find("ReloadHeldMagazine") : null;
                if (found != null) heldMagVisual = found.gameObject;
                else return;
            }

            var p = _reloadNormalizedProgress;
            var show = _isReloadingPose && _reloadPoseBlend > 0.2f && _reloadStyle switch
            {
                ReloadStyle.ShellByShellShotgun => !_shotgunPumpPhase && _shotgunShellPhase >= 0.22f && _shotgunShellPhase <= 0.80f,
                ReloadStyle.FrontLoadedRocket => p >= 0.22f && p <= 0.66f,
                ReloadStyle.BeltFedBoxLmg => p >= 0.28f && p <= 0.66f,
                ReloadStyle.SwingOutCylinder => false,
                _ => p >= 0.36f && p <= 0.68f
            };

            if (heldMagVisual.activeSelf != show)
            {
                heldMagVisual.SetActive(show);
            }
            if (show)
            {
                _heldMagMeshFilter ??= heldMagVisual.GetComponent<MeshFilter>();
                _heldMagMeshRenderer ??= heldMagVisual.GetComponent<MeshRenderer>();
                if (_heldMagMeshFilter != null)
                {
                    var desiredMesh = _heldPropOverrideMesh != null ? _heldPropOverrideMesh : reloadMagMesh;
                    if (desiredMesh != null && _heldMagMeshFilter.sharedMesh != desiredMesh)
                    {
                        _heldMagMeshFilter.sharedMesh = desiredMesh;
                    }
                }
                if (_heldMagMeshRenderer != null)
                {
                    var desiredMat = _heldPropOverrideMat != null ? _heldPropOverrideMat : reloadMagMaterial;
                    if (desiredMat != null && _heldMagMeshRenderer.sharedMaterial != desiredMat)
                    {
                        _heldMagMeshRenderer.sharedMaterial = desiredMat;
                    }
                }
                heldMagVisual.transform.localScale = _heldPropOverrideScale;
                heldMagVisual.transform.position = leftHandWorldPos;
                heldMagVisual.transform.rotation = weaponAnchor != null ? weaponAnchor.rotation : visualRoot.rotation;
            }
        }

        private void SolveTwoBoneArm(
            Transform shoulderPivot,
            Transform elbowPivot,
            Vector3 targetWorldPos,
            Vector3 elbowHintLocal,
            float upperLen,
            float foreLen)
        {
            var s = visualRoot.InverseTransformPoint(shoulderPivot.position);
            var w = visualRoot.InverseTransformPoint(targetWorldPos);
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
