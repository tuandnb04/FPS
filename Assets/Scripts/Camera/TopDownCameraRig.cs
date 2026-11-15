using UnityEngine;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;

namespace TopDownShooter.CameraRig
{
    /// <summary>
    /// Cinemachine 6.6.0 top-down camera rig. Uses a smoothed FollowProxy for look-ahead framing,
    /// avoids CinemachineDeoccluder indoors, and executes a gentle 5 deg/sec orbit in Title state.
    /// </summary>
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        public static TopDownCameraRig Instance { get; private set; }

        [Header("Framing")]
        [SerializeField] private Vector3 baseFollowOffset = new Vector3(0f, 12.5f, -8.2f);
        [SerializeField] private float fieldOfView = 52f;
        [SerializeField] private float maxLookAheadDistance = 1.35f;
        [SerializeField] private float titleOrbitDegPerSec = 5f;

        [SerializeField, HideInInspector] private CinemachineCamera vcam;
        [SerializeField, HideInInspector] private CinemachineFollow follow;
        [SerializeField, HideInInspector] private CinemachineImpulseListener impulseListener;
        [SerializeField, HideInInspector] private Transform followProxy;
        [SerializeField, HideInInspector] private Transform playerTarget;

        private float titleOrbitAngle;
        private Vector3 currentLookAhead;
        private bool isTitleMode = true;
        private bool cutsceneOverride;
        private CinemachineBrain cachedBrain;

        public float ConfiguredDistance => baseFollowOffset.magnitude;
        public CinemachineCamera VirtualCamera => vcam;
        public CinemachineFollow FollowComponent => follow;
        public bool IsCutsceneOverride => cutsceneOverride;

        public void SetCutsceneOverride(bool active)
        {
            cutsceneOverride = active;
            if (cachedBrain == null && Camera.main != null)
            {
                cachedBrain = Camera.main.GetComponent<CinemachineBrain>();
            }
            if (cachedBrain != null)
            {
                cachedBrain.enabled = !active;
            }

            if (!active)
            {
                if (Camera.main != null)
                {
                    Camera.main.fieldOfView = fieldOfView;
                }
                if (followProxy != null && playerTarget != null)
                {
                    followProxy.position = playerTarget.position + Vector3.up * 0.9f + currentLookAhead;
                }
                if (follow != null)
                {
                    follow.FollowOffset = baseFollowOffset;
                }
                if (vcam != null && followProxy != null)
                {
                    vcam.transform.position = followProxy.position + baseFollowOffset;
                    SnapOrientationToOffset(baseFollowOffset);
                }
            }
        }

        public void BindComponents(
            CinemachineCamera virtualCamera,
            CinemachineFollow followComponent,
            CinemachineImpulseListener listener,
            Transform proxy,
            Transform player)
        {
            vcam = virtualCamera;
            follow = followComponent;
            impulseListener = listener;
            followProxy = proxy;
            playerTarget = player;
            ConfigureSettings();
        }

        public void SetPlayerTarget(Transform player)
        {
            playerTarget = player;
        }

        public void SetTitleMode(bool titleActive)
        {
            isTitleMode = titleActive;
            if (!titleActive)
            {
                titleOrbitAngle = 0f;
                if (follow != null)
                {
                    follow.FollowOffset = baseFollowOffset;
                }
            }
        }

        public void UpdateAimLookAhead(Vector3 aimDirectionWorld)
        {
            Vector3 flatAim = new Vector3(aimDirectionWorld.x, 0f, aimDirectionWorld.z);
            if (flatAim.sqrMagnitude > 0.001f)
            {
                Vector3 desired = flatAim.normalized * maxLookAheadDistance;
                currentLookAhead = Vector3.Lerp(currentLookAhead, desired, Time.deltaTime * 8f);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (vcam == null) vcam = GetComponent<CinemachineCamera>();
            if (follow == null) follow = GetComponent<CinemachineFollow>();
            if (impulseListener == null) impulseListener = GetComponent<CinemachineImpulseListener>();
            if (followProxy == null && vcam != null) followProxy = vcam.Target.TrackingTarget;
            ConfigureSettings();
        }

        public void SnapToPlayerImmediate()
        {
            if (followProxy == null && vcam != null) followProxy = vcam.Target.TrackingTarget;
            if (followProxy == null || playerTarget == null) return;
            followProxy.position = playerTarget.position + Vector3.up * 0.9f + currentLookAhead;
            if (follow != null) follow.FollowOffset = baseFollowOffset;
            if (vcam != null)
            {
                vcam.transform.position = followProxy.position + baseFollowOffset;
                SnapOrientationToOffset(baseFollowOffset);
            }
        }

        private void ConfigureSettings()
        {
            if (vcam == null)
            {
                return;
            }

            vcam.Lens.FieldOfView = fieldOfView;
            vcam.Lens.NearClipPlane = 0.3f;
            vcam.Lens.FarClipPlane = 260f;

            if (followProxy != null)
            {
                vcam.Target.TrackingTarget = followProxy;
                vcam.Target.CustomLookAtTarget = false;
            }

            if (follow != null)
            {
                follow.FollowOffset = baseFollowOffset;
                follow.TrackerSettings = new TrackerSettings
                {
                    BindingMode = BindingMode.WorldSpace,
                    PositionDamping = new Vector3(0.16f, 0.16f, 0.16f),
                    AngularDampingMode = AngularDampingMode.Euler,
                    RotationDamping = Vector3.zero,
                    QuaternionDamping = 0f
                };
            }

            if (impulseListener != null)
            {
                impulseListener.ChannelMask = 1;
                impulseListener.Gain = 1f;
                impulseListener.UseCameraSpace = true;
            }

            SnapOrientationToOffset(baseFollowOffset);
        }

        private void LateUpdate()
        {
            if (cutsceneOverride || followProxy == null || follow == null || vcam == null)
            {
                return;
            }

            if (isTitleMode)
            {
                titleOrbitAngle += titleOrbitDegPerSec * Time.unscaledDeltaTime;
                Vector3 anchor = playerTarget != null ? playerTarget.position + Vector3.up * 0.9f : new Vector3(0f, 0.9f, 0f);
                followProxy.position = Vector3.Lerp(followProxy.position, anchor, Time.unscaledDeltaTime * 6f);

                Vector3 rotatedOffset = Quaternion.Euler(0f, titleOrbitAngle, 0f) * baseFollowOffset;
                follow.FollowOffset = rotatedOffset;
                SnapOrientationToOffset(rotatedOffset);
            }
            else if (playerTarget != null)
            {
                Vector3 desiredProxy = playerTarget.position + Vector3.up * 0.9f + currentLookAhead;
                followProxy.position = Vector3.Lerp(followProxy.position, desiredProxy, 1f - Mathf.Exp(-14f * Time.deltaTime));
                follow.FollowOffset = Vector3.Lerp(follow.FollowOffset, baseFollowOffset, 1f - Mathf.Exp(-10f * Time.deltaTime));
                SnapOrientationToOffset(follow.FollowOffset);
            }
        }

        private void SnapOrientationToOffset(Vector3 offset)
        {
            if (offset.sqrMagnitude > 0.01f && vcam != null)
            {
                vcam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
            }
        }
    }
}
