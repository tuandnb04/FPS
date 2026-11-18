using System;
using System.Collections.Generic;
using Character;
using Combat;
using Core;
using Enemies;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.Rendering;

namespace Camera
{
    /// <summary>
    /// Cinemachine 6.6.0 top-down camera rig. Uses a smoothed FollowProxy for look-ahead framing,
    /// avoids CinemachineDeoccluder indoors, and executes a gentle 5 deg/sec orbit in Title state.
    /// </summary>
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        public static TopDownCameraRig Instance { get; private set; }

        [Header("Framing")]
        [SerializeField] private Vector3 baseFollowOffset = new(0f, 12.5f, -8.2f);
        [SerializeField] private float fieldOfView = 52f;
        [SerializeField] private float maxLookAheadDistance = 1.35f;
        [SerializeField] private float titleOrbitDegPerSec = 5f;

        [SerializeField, HideInInspector] private CinemachineCamera vcam;
        [SerializeField, HideInInspector] private CinemachineFollow follow;
        [SerializeField, HideInInspector] private CinemachineImpulseListener impulseListener;
        [SerializeField, HideInInspector] private Transform followProxy;
        [SerializeField, HideInInspector] private Transform playerTarget;

        private float _titleOrbitAngle;
        private Vector3 _currentLookAhead;
        private bool _isTitleMode = true;
        private CinemachineBrain _cachedBrain;
        private UnityEngine.Camera _cachedMainCamera;
        private readonly RaycastHit[] _occlusionHits = new RaycastHit[24];
        private readonly Dictionary<MeshRenderer, ShadowCastingMode> _originalShadowModes = new();
        private readonly Dictionary<Collider, MeshRenderer[]> _colliderRendererCache = new();
        private readonly HashSet<MeshRenderer> _currentlyOccluded = new();
        private readonly List<MeshRenderer> _restoreBuffer = new();

        public float ConfiguredDistance => baseFollowOffset.magnitude;
        public CinemachineCamera VirtualCamera => vcam;
        public CinemachineFollow FollowComponent => follow;
        public bool IsCutsceneOverride { get; private set; }

        public void SetCutsceneOverride(bool active)
        {
            IsCutsceneOverride = active;
            if (_cachedMainCamera == null) _cachedMainCamera = UnityEngine.Camera.main;
            if (_cachedBrain == null && _cachedMainCamera != null)
            {
                _cachedBrain = _cachedMainCamera.GetComponent<CinemachineBrain>();
            }
            if (_cachedBrain != null)
            {
                _cachedBrain.enabled = !active;
            }

            if (active) return;
            if (_cachedMainCamera != null)
            {
                _cachedMainCamera.fieldOfView = fieldOfView;
            }
            if (followProxy != null && playerTarget != null)
            {
                followProxy.position = playerTarget.position + Vector3.up * 0.9f + _currentLookAhead;
            }
            if (follow != null)
            {
                follow.FollowOffset = baseFollowOffset;
            }

            if (vcam == null || followProxy == null) return;
            vcam.transform.position = followProxy.position + baseFollowOffset;
            SnapOrientationToOffset(baseFollowOffset);
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
            _isTitleMode = titleActive;
            if (titleActive) return;
            _titleOrbitAngle = 0f;
            if (follow != null)
            {
                follow.FollowOffset = baseFollowOffset;
            }
        }

        public void UpdateAimLookAhead(Vector3 aimDirectionWorld)
        {
            var flatAim = new Vector3(aimDirectionWorld.x, 0f, aimDirectionWorld.z);
            if (!(flatAim.sqrMagnitude > 0.001f)) return;
            var lookDist = maxLookAheadDistance;
            var player = PlayerController.Instance;
            if (player != null && player.IsAimingDownSights)
            {
                lookDist = (player.Weapon != null && player.Weapon.CurrentWeapon == WeaponType.PlasmaRailgun)
                    ? 3.60f
                    : 2.85f;
            }
            var desired = flatAim.normalized * lookDist;
            _currentLookAhead = Vector3.Lerp(_currentLookAhead, desired, Time.deltaTime * 8f);
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
            followProxy.position = playerTarget.position + Vector3.up * 0.9f + _currentLookAhead;
            if (follow != null) follow.FollowOffset = baseFollowOffset;
            if (vcam == null) return;
            vcam.transform.position = followProxy.position + baseFollowOffset;
            SnapOrientationToOffset(baseFollowOffset);
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
            if (IsCutsceneOverride || followProxy == null || follow == null || vcam == null)
            {
                return;
            }

            if (_isTitleMode)
            {
                _titleOrbitAngle += titleOrbitDegPerSec * Time.unscaledDeltaTime;
                var anchor = playerTarget != null ? playerTarget.position + Vector3.up * 0.9f : new Vector3(0f, 0.9f, 0f);
                followProxy.position = Vector3.Lerp(followProxy.position, anchor, Time.unscaledDeltaTime * 6f);

                var rotatedOffset = Quaternion.Euler(0f, _titleOrbitAngle, 0f) * baseFollowOffset;
                follow.FollowOffset = rotatedOffset;
                SnapOrientationToOffset(rotatedOffset);
            }
            else if (playerTarget != null)
            {
                var desiredProxy = playerTarget.position + Vector3.up * 0.9f + _currentLookAhead;
                followProxy.position = Vector3.Lerp(followProxy.position, desiredProxy, 1f - Mathf.Exp(-14f * Time.deltaTime));
                follow.FollowOffset = Vector3.Lerp(follow.FollowOffset, baseFollowOffset, 1f - Mathf.Exp(-10f * Time.deltaTime));
                SnapOrientationToOffset(follow.FollowOffset);
                UpdateWallOcclusion();
                return;
            }
            RestoreAllOccludedRenderers();
        }

        private void OnDisable()
        {
            RestoreAllOccludedRenderers(clearRendererCache: true);
        }

        private MeshRenderer[] GetFilteredWallRenderers(Collider col)
        {
            if (_colliderRendererCache.TryGetValue(col, out var cached))
            {
                return cached;
            }

            if (col.GetComponentInParent<PlayerController>() != null
                || col.GetComponentInParent<EnemyController>() != null
                || col.GetComponentInParent<SquadMemberController>() != null
                || col.GetComponentInParent<ExplosiveBarrel>() != null
                || col.GetComponentInParent<CampaignObjectiveNode>() != null)
            {
                cached = Array.Empty<MeshRenderer>();
                _colliderRendererCache[col] = cached;
                return cached;
            }

            var raw = col.GetComponentsInChildren<MeshRenderer>(true);
            var kept = new List<MeshRenderer>(raw.Length);
            foreach (var mr in raw)
            {
                if (mr == null || mr.bounds.max.y < 2.05f) continue;
                var goName = mr.gameObject.name;
                if (goName.Contains("Floor") || goName.Contains("Deck") || goName.Contains("Road") || goName.Contains("Helipad")) continue;
                kept.Add(mr);
            }

            cached = kept.Count > 0 ? kept.ToArray() : Array.Empty<MeshRenderer>();
            _colliderRendererCache[col] = cached;
            return cached;
        }

        private void UpdateWallOcclusion()
        {
            if (_cachedMainCamera == null) _cachedMainCamera = UnityEngine.Camera.main;
            var camPos = _cachedMainCamera != null ? _cachedMainCamera.transform.position : vcam.transform.position;
            var targetPos = playerTarget.position + Vector3.up * 1.0f;
            var toTarget = targetPos - camPos;
            var dist = toTarget.magnitude;
            if (dist <= 2.0f)
            {
                RestoreAllOccludedRenderers();
                return;
            }

            _currentlyOccluded.Clear();
            var hitCount = Physics.SphereCastNonAlloc(camPos, 0.52f, toTarget / dist, _occlusionHits, dist - 1.1f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hitCount; i++)
            {
                var col = _occlusionHits[i].collider;
                if (col == null) continue;

                var renderers = GetFilteredWallRenderers(col);
                foreach (var mr in renderers)
                {
                    if (mr == null) continue;

                    _currentlyOccluded.Add(mr);
                    if (!_originalShadowModes.ContainsKey(mr))
                    {
                        _originalShadowModes[mr] = mr.shadowCastingMode;
                    }
                    mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }

            _restoreBuffer.Clear();
            foreach (var kv in _originalShadowModes)
            {
                if (kv.Key == null || !_currentlyOccluded.Contains(kv.Key))
                {
                    _restoreBuffer.Add(kv.Key);
                }
            }
            foreach (var mr in _restoreBuffer)
            {
                if (mr != null && _originalShadowModes.TryGetValue(mr, out var mode))
                {
                    mr.shadowCastingMode = mode;
                }
                _originalShadowModes.Remove(mr);
            }
        }

        private void RestoreAllOccludedRenderers(bool clearRendererCache = false)
        {
            if (clearRendererCache)
            {
                _colliderRendererCache.Clear();
            }
            if (_originalShadowModes.Count == 0) return;
            foreach (var kv in _originalShadowModes)
            {
                if (kv.Key != null)
                {
                    kv.Key.shadowCastingMode = kv.Value;
                }
            }
            _originalShadowModes.Clear();
            _currentlyOccluded.Clear();
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
