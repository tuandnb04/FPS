using System;
using Audio;
using Character;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    public enum ObjectiveNodeKind
    {
        GateC4Breach,
        FuelValveSabotage,
        RadarSabotage,
        HelipadEvacLZ
    }

    /// <summary>
    /// Interactive 3D campaign objective node (C4 gate breach, Fuel Valve / EW Radar sabotage,
    /// or Extraction LZ flare zone). Supports stand-in-zone arming, [E] boost, and Vanguard-09 buddy assist.
    /// </summary>
    public sealed class CampaignObjectiveNode : MonoBehaviour
    {
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private float _interactRadius = 3.2f;
        private float _requiredDuration = 1.6f;
        private bool _detonateOnComplete = true;

        private GameObject _terminalVisual;
        private GameObject _c4ChargeVisual;
        private MeshRenderer _zoneRingRenderer;
        private Transform _zoneRingTransform;
        private MaterialPropertyBlock _mpb;

        private float _currentProgress;
        private Action<CampaignObjectiveNode> _onCompletedCallback;

        public ObjectiveNodeKind Kind { get; private set; }

        public string DisplayTitle { get; private set; } = "PLANT C4";

        public bool IsUnlocked { get; private set; }

        public bool IsCompleted { get; private set; }

        private float ProgressNormalized => Mathf.Clamp01(_currentProgress / Mathf.Max(0.1f, _requiredDuration));

        public void Configure(
            ObjectiveNodeKind nodeKind,
            string title,
            Vector3 worldPos,
            float radius,
            float armSeconds,
            bool explodeOnFinish,
            Mesh terminalMesh,
            Mesh c4Mesh,
            Material hardwareMat,
            Material accentMat,
            Action<CampaignObjectiveNode> onComplete)
        {
            Kind = nodeKind;
            DisplayTitle = title;
            _interactRadius = radius;
            _requiredDuration = armSeconds;
            _detonateOnComplete = explodeOnFinish;
            _onCompletedCallback = onComplete;
            transform.position = new Vector3(worldPos.x, 0f, worldPos.z);

            BuildVisuals(terminalMesh, c4Mesh, hardwareMat, accentMat);
            SetState(unlocked: false, completed: false);
        }

        private void BuildVisuals(Mesh terminalMesh, Mesh c4Mesh, Material hardwareMat, Material accentMat)
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            if (Kind != ObjectiveNodeKind.HelipadEvacLZ)
            {
                if (terminalMesh != null)
                {
                    _terminalVisual = new GameObject("SabotageTerminal");
                    _terminalVisual.transform.SetParent(transform, false);
                    var mf = _terminalVisual.AddComponent<MeshFilter>();
                    mf.sharedMesh = terminalMesh;
                    var mr = _terminalVisual.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = hardwareMat;
                }
                else
                {
                    _terminalVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _terminalVisual.name = "SabotageTerminal";
                    _terminalVisual.transform.SetParent(transform, false);
                    _terminalVisual.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                    _terminalVisual.transform.localScale = new Vector3(1.1f, 1.3f, 0.85f);
                    var c = _terminalVisual.GetComponent<Collider>();
                    if (c != null) Destroy(c);
                    _terminalVisual.GetComponent<MeshRenderer>().sharedMaterial = hardwareMat;
                }

                if (c4Mesh != null)
                {
                    _c4ChargeVisual = new GameObject("PlantedC4");
                    _c4ChargeVisual.transform.SetParent(transform, false);
                    _c4ChargeVisual.transform.localPosition = new Vector3(0f, 0.96f, 0.36f);
                    _c4ChargeVisual.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
                    var cmf = _c4ChargeVisual.AddComponent<MeshFilter>();
                    cmf.sharedMesh = c4Mesh;
                    var cmr = _c4ChargeVisual.AddComponent<MeshRenderer>();
                    cmr.sharedMaterial = accentMat;
                    _c4ChargeVisual.SetActive(false);
                }
            }

            var ringMat = Palette.CreateParticleMaterial("Mat_ObjectiveZone_" + Kind, UISpriteFactory.GetTelegraphRingTexture());
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "ObjectiveZoneRing";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(_interactRadius * 2f, _interactRadius * 2f, 1f);
            var rc = ring.GetComponent<Collider>();
            if (rc != null) Destroy(rc);
            _zoneRingRenderer = ring.GetComponent<MeshRenderer>();
            if (_zoneRingRenderer != null)
            {
                _zoneRingRenderer.sharedMaterial = ringMat;
                _zoneRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _zoneRingTransform = ring.transform;
        }

        public void SetState(bool unlocked, bool completed)
        {
            IsUnlocked = unlocked;
            IsCompleted = completed;
            _currentProgress = completed ? _requiredDuration : 0f;

            if (_zoneRingTransform != null)
            {
                _zoneRingTransform.gameObject.SetActive(unlocked && !completed);
            }
            if (_c4ChargeVisual != null)
            {
                _c4ChargeVisual.SetActive(completed);
            }
            ApplyZoneColor(0f);
        }

        public void ForceCompleteImmediate()
        {
            if (IsCompleted) return;
            CompleteNode();
        }

        private void Update()
        {
            if (!IsUnlocked || IsCompleted) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

            var player = PlayerController.Instance;
            if (player == null || player.IsDead) return;

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var diff = player.transform.position - transform.position;
            diff.y = 0f;
            var playerInside = diff.magnitude <= _interactRadius;

            var squadAssist = SquadDirector.Instance != null && SquadDirector.Instance.HasStandingMemberNear(transform.position, _interactRadius + 1.2f);
            var interactBoost = Keyboard.current != null && Keyboard.current.eKey.isPressed;

            if (playerInside)
            {
                var rate = 1f + (squadAssist ? 0.55f : 0f) + (interactBoost ? 1.1f : 0f);
                _currentProgress = Mathf.Min(_requiredDuration, _currentProgress + dt * rate);

                var pct = Mathf.RoundToInt(ProgressNormalized * 100f);
                var verb = Kind == ObjectiveNodeKind.HelipadEvacLZ ? "SIGNALING EVAC-6" : "ARMING C4";
                UIManager.Instance?.UpdateContextPrompt($"{verb} [{pct}%]  ·  HOLD [E] BOOST");

                if (_currentProgress >= _requiredDuration)
                {
                    CompleteNode();
                    return;
                }
            }
            else if (diff.magnitude <= _interactRadius + 3.0f)
            {
                UIManager.Instance?.UpdateContextPrompt($"MOVE TO {DisplayTitle}");
                _currentProgress = Mathf.Max(0f, _currentProgress - dt * 0.4f);
            }
            else
            {
                _currentProgress = Mathf.Max(0f, _currentProgress - dt * 0.6f);
            }

            var pulse = 0.94f + 0.06f * Mathf.Sin(Time.time * 5.2f);
            if (_zoneRingTransform != null)
            {
                _zoneRingTransform.localScale = new Vector3(_interactRadius * 2f * pulse, _interactRadius * 2f * pulse, 1f);
            }
            ApplyZoneColor(ProgressNormalized);
        }

        private void CompleteNode()
        {
            IsCompleted = true;
            IsUnlocked = false;
            _currentProgress = _requiredDuration;
            if (_zoneRingTransform != null) _zoneRingTransform.gameObject.SetActive(false);
            if (_c4ChargeVisual != null) _c4ChargeVisual.SetActive(true);
            UIManager.Instance?.UpdateContextPrompt(string.Empty);

            if (_detonateOnComplete)
            {
                var blastCenter = transform.position + Vector3.up * 0.85f;
                VFXManager.Instance?.Play(VfxKind.EnemyDeath, blastCenter, Palette.RocketOrange);
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, blastCenter, Palette.C4Amber);
                FeelManager.Instance?.TriggerCameraShake(0.45f);
                AudioManager.Instance?.PlayEnemyExplode();
                FeelManager.Instance?.SpawnFloatingText(blastCenter + Vector3.up * 0.5f, "C4 DETONATED!", Palette.C4Amber);
            }
            else
            {
                var lzCenter = transform.position + Vector3.up * 0.5f;
                VFXManager.Instance?.Play(VfxKind.LevelUpNova, lzCenter, Palette.RadarGreen);
                AudioManager.Instance?.PlayMilestoneSting();
                FeelManager.Instance?.SpawnFloatingText(lzCenter + Vector3.up * 0.6f, "EVAC FLARE POPPED!", Palette.RadarGreen);
            }

            _onCompletedCallback?.Invoke(this);
        }

        private void ApplyZoneColor(float armProgress)
        {
            if (_zoneRingRenderer == null) return;
            _mpb ??= new MaterialPropertyBlock();

            var baseCol = Kind == ObjectiveNodeKind.HelipadEvacLZ ? Palette.RadarGreen : Palette.C4Amber;
            var activeCol = Palette.Lerp(baseCol, Palette.HealthGreen, armProgress * 0.6f);
            activeCol.a = 0.72f + armProgress * 0.25f;
            _zoneRingRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(Color1, activeCol);
            _zoneRingRenderer.SetPropertyBlock(_mpb);
        }
    }
}
