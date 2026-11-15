using System;
using System.Collections;
using Audio;
using Character;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Camera
{
    public enum CutsceneType
    {
        IntroInfil,
        Act2GateBreach,
        Act3BossVolkov,
        VictoryEvac
    }

    /// <summary>
    /// Call-of-Duty-inspired in-engine cutscene director handling widescreen cinema letterbox,
    /// bottom-left green typewriter mission stamps, multi-sector flyover/orbit camera shots,
    /// sliding perimeter blast gates, and EVAC-6 Blackhawk extraction sequences.
    /// </summary>
    public sealed class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }

        [Header("Campaign Sector Props")]
        [SerializeField] private Transform blastGateAct1Left;
        [SerializeField] private Transform blastGateAct1Right;
        [SerializeField] private Transform blastGateAct2Left;
        [SerializeField] private Transform blastGateAct2Right;
        [SerializeField] private Transform evacChopperRoot;
        [SerializeField] private Transform evacChopperRotor;

        private Vector3 _gate1LeftClosed;
        private Vector3 _gate1RightClosed;
        private Vector3 _gate2LeftClosed;
        private Vector3 _gate2RightClosed;
        private bool _gatePositionsCached;

        private Coroutine _activeCutscene;
        private Action _pendingCallback;
        private float _rotorSpinSpeed;

        public bool IsPlaying => _activeCutscene != null;
        public CutsceneType CurrentCutscene { get; private set; }
        public bool Gate1Open { get; private set; }

        public bool Gate2Open { get; private set; }

        public bool IsEvacChopperVisible => evacChopperRoot != null && evacChopperRoot.gameObject.activeInHierarchy;
        public string CurrentStampText { get; private set; } = string.Empty;

        public void BindSceneProps(
            Transform gate1Left,
            Transform gate1Right,
            Transform gate2Left,
            Transform gate2Right,
            Transform evacChopper,
            Transform evacRotor)
        {
            blastGateAct1Left = gate1Left;
            blastGateAct1Right = gate1Right;
            blastGateAct2Left = gate2Left;
            blastGateAct2Right = gate2Right;
            evacChopperRoot = evacChopper;
            evacChopperRotor = evacRotor;
            CacheInitialGatePositions();
            ResetCampaignProps();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CacheInitialGatePositions();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void CacheInitialGatePositions()
        {
            if (_gatePositionsCached) return;
            if (blastGateAct1Left != null) _gate1LeftClosed = blastGateAct1Left.position;
            if (blastGateAct1Right != null) _gate1RightClosed = blastGateAct1Right.position;
            if (blastGateAct2Left != null) _gate2LeftClosed = blastGateAct2Left.position;
            if (blastGateAct2Right != null) _gate2RightClosed = blastGateAct2Right.position;
            _gatePositionsCached = blastGateAct1Left != null && blastGateAct2Left != null;
        }

        public void ResetCampaignProps()
        {
            if (_activeCutscene != null)
            {
                StopCoroutine(_activeCutscene);
                _activeCutscene = null;
            }
            _pendingCallback = null;
            _rotorSpinSpeed = 0f;
            Gate1Open = false;
            Gate2Open = false;

            CacheInitialGatePositions();
            SetGateTransform(blastGateAct1Left, blastGateAct1Right, _gate1LeftClosed, _gate1RightClosed, 0f, true);
            SetGateTransform(blastGateAct2Left, blastGateAct2Right, _gate2LeftClosed, _gate2RightClosed, 0f, true);

            if (evacChopperRoot != null)
            {
                evacChopperRoot.SetPositionAndRotation(new Vector3(0f, 28f, 66f), Quaternion.Euler(0f, 180f, 0f));
                evacChopperRoot.gameObject.SetActive(false);
            }

            TopDownCameraRig.Instance?.SetCutsceneOverride(false);
            UIManager.Instance?.HideCutsceneOverlayImmediate();
            CurrentStampText = string.Empty;
        }

        public void OpenGateImmediate(int gateIndex)
        {
            CacheInitialGatePositions();
            switch (gateIndex)
            {
                case 1 when !Gate1Open:
                    Gate1Open = true;
                    SetGateTransform(blastGateAct1Left, blastGateAct1Right, _gate1LeftClosed, _gate1RightClosed, 6.2f, false);
                    break;
                case 2 when !Gate2Open:
                    Gate2Open = true;
                    SetGateTransform(blastGateAct2Left, blastGateAct2Right, _gate2LeftClosed, _gate2RightClosed, 6.8f, false);
                    break;
            }
        }

        public void PlayCutscene(CutsceneType type, Action onComplete)
        {
            if (_activeCutscene != null)
            {
                StopCoroutine(_activeCutscene);
            }

            CacheInitialGatePositions();
            CurrentCutscene = type;
            _pendingCallback = onComplete;
            _activeCutscene = StartCoroutine(RunCutsceneRoutine(type));
        }

        public void SkipCurrentCutscene()
        {
            if (_activeCutscene == null) return;
            StopCoroutine(_activeCutscene);
            _activeCutscene = null;
            AudioManager.Instance?.PlayUIClick();

            switch (CurrentCutscene)
            {
                case CutsceneType.Act2GateBreach:
                    OpenGateImmediate(1);
                    break;
                case CutsceneType.Act3BossVolkov:
                    OpenGateImmediate(1);
                    OpenGateImmediate(2);
                    break;
                case CutsceneType.VictoryEvac when evacChopperRoot != null:
                    evacChopperRoot.gameObject.SetActive(true);
                    evacChopperRoot.SetPositionAndRotation(new Vector3(0f, 0.04f, 42f), Quaternion.Euler(0f, 155f, 0f));
                    _rotorSpinSpeed = 240f;
                    break;
                case CutsceneType.IntroInfil:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            CompleteCutsceneCleanup();
        }

        private void Update()
        {
            if (evacChopperRotor != null && evacChopperRoot != null && evacChopperRoot.gameObject.activeInHierarchy && _rotorSpinSpeed > 1f)
            {
                evacChopperRotor.Rotate(0f, _rotorSpinSpeed * Time.unscaledDeltaTime, 0f, Space.Self);
            }

            if (_activeCutscene == null || Keyboard.current == null) return;
            if (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
            {
                SkipCurrentCutscene();
            }
        }

        private IEnumerator RunCutsceneRoutine(CutsceneType type)
        {
            TopDownCameraRig.Instance?.SetCutsceneOverride(true);
            UIManager.Instance?.ShowCutsceneOverlay();

            switch (type)
            {
                case CutsceneType.IntroInfil:
                    yield return RunIntroInfilShot();
                    break;
                case CutsceneType.Act2GateBreach:
                    yield return RunAct2BreachShot();
                    break;
                case CutsceneType.Act3BossVolkov:
                    yield return RunAct3BossVolkovShot();
                    break;
                case CutsceneType.VictoryEvac:
                    yield return RunVictoryEvacShot();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            _activeCutscene = null;
            CompleteCutsceneCleanup();
        }

        private void CompleteCutsceneCleanup()
        {
            TopDownCameraRig.Instance?.SetCutsceneOverride(false);
            UIManager.Instance?.HideCutsceneOverlayImmediate();
            var cb = _pendingCallback;
            _pendingCallback = null;
            cb?.Invoke();
        }

        private IEnumerator RunIntroInfilShot()
        {
            SquadDirector.Instance?.PlaceSquadFormation(new Vector3(0f, 0.02f, -43f), Vector3.forward, resetHp: false);

            const string fullStamp = "OPERATION: NIGHTFALL\nHAJIN DMZ // KOREAN PENINSULA // 03:18 HRS\nTASK FORCE 141 - CPT. PRICE, GHOST & GAZ";
            const string speaker = "CPT. PRICE // TF-141 ACTUAL";
            const string line1 = "Eyes up, Bravo-6. Kronos mercenaries hold the checkpoint road, central fuel depot, and northern helipad.";
            const string line2 = "Stack up at South Gate! Clear the garrison and let Hammer blow those blast locks wide open.";

            // Shot 1: Sweeping high establishing drone flyover North -> South across the whole 3-sector compound
            yield return AnimateCameraShot(
                duration: 2.35f,
                startPos: new Vector3(14f, 24f, 52f),
                endPos: new Vector3(-8f, 18f, 4f),
                startLook: new Vector3(0f, 1.5f, 18f),
                endLook: new Vector3(0f, 1.2f, -18f),
                startFov: 44f,
                endFov: 48f,
                fullStamp: fullStamp,
                speaker: speaker,
                caption: line1);

            // Shot 2: Dramatic crane dive toward Fireteam Bravo-6 behind South Checkpoint cover
            yield return AnimateCameraShot(
                duration: 2.15f,
                startPos: new Vector3(-8f, 14f, -26f),
                endPos: new Vector3(0f, 8.2f, -51.5f),
                startLook: new Vector3(0f, 1.0f, -38f),
                endLook: new Vector3(0f, 1.2f, -34f),
                startFov: 48f,
                endFov: 52f,
                fullStamp: fullStamp,
                speaker: speaker,
                caption: line2,
                stampAlreadyTyped: true);
        }

        private IEnumerator RunAct2BreachShot()
        {
            SquadDirector.Instance?.PlaceSquadFormation(new Vector3(0f, 0.02f, -19.5f), Vector3.forward, resetHp: false);

            const string fullStamp = "ACT II // HAJIN DEPOT\nBLAST GATE ALPHA: BREACHED";
            const string speaker = "SGT. GAZ // GARRICK-3";
            const string line = "Fire in the hole! Gate Alpha is down — Bravo-6 pushing into the fuel depot and radar yard!";

            var triggeredBlast = false;
            var elapsed = 0f;
            const float total = 3.2f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(elapsed / total);
                var smooth = Ease.InOutCubic(u);

                if (!triggeredBlast && u >= 0.24f)
                {
                    triggeredBlast = true;
                    Gate1Open = true;
                    VFXManager.Instance?.Play(VfxKind.EnemyDeath, new Vector3(0f, 1.6f, -14f), Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.45f);
                    AudioManager.Instance?.PlayEnemyExplode();
                }

                var slide = u < 0.24f ? 0f : Ease.OutCubic(Mathf.Clamp01((u - 0.24f) / 0.56f)) * 6.2f;
                SetGateTransform(blastGateAct1Left, blastGateAct1Right, _gate1LeftClosed, _gate1RightClosed, slide, u < 0.35f);

                var camPos = Vector3.Lerp(new Vector3(7.5f, 7.2f, -29.5f), new Vector3(0.0f, 9.2f, -23.0f), smooth);
                var lookTarget = Vector3.Lerp(new Vector3(0f, 1.5f, -14.0f), new Vector3(0f, 1.8f, -2.0f), smooth);
                ApplyCameraPose(camPos, Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up), Mathf.Lerp(48f, 52f, smooth));
                UpdateTypewriterUI(fullStamp, speaker, line, Mathf.Clamp01(u / 0.5f));
                yield return null;
            }

            OpenGateImmediate(1);
        }

        private IEnumerator RunAct3BossVolkovShot()
        {
            OpenGateImmediate(1);
            OpenGateImmediate(2);

            const string fullStamp = "ACT III // HAJIN COMMAND HELIPAD\nHVT: KOMANDER VOLKOV  [KONNI BTR-82A]";
            const string speaker = "VOLKOV // KONNI ENCRYPTED";
            const string line = "You tore through my outer compound, Vanguard. Finish this on the tarmac - IRONCLAD ALPHA engaging!";

            AudioManager.Instance?.PlayMilestoneSting();
            yield return AnimateCameraShot(
                duration: 3.1f,
                startPos: new Vector3(-13f, 8.8f, 29f),
                endPos: new Vector3(11f, 9.6f, 34f),
                startLook: new Vector3(0f, 1.4f, 42f),
                endLook: new Vector3(0f, 1.6f, 43f),
                startFov: 44f,
                endFov: 50f,
                fullStamp: fullStamp,
                speaker: speaker,
                caption: line);
        }

        private IEnumerator RunVictoryEvacShot()
        {
            OpenGateImmediate(1);
            OpenGateImmediate(2);
            PlayerController.Instance?.TeleportTo(new Vector3(-1.4f, 0.02f, 35.5f));
            SquadDirector.Instance?.PlaceSquadFormation(new Vector3(-1.0f, 0.02f, 34.8f), Vector3.forward, resetHp: false);

            if (evacChopperRoot != null)
            {
                evacChopperRoot.gameObject.SetActive(true);
            }
            _rotorSpinSpeed = 980f;

            const string fullStamp = "MISSION COMPLETE // HAJIN DMZ SECURED\nEVAC-6 BLACKHAWK TOUCHDOWN";
            const string speaker = "CPT. PRICE // TF-141 ACTUAL";
            const string line = "Hot LZ secured! On the bird, Bravo-6 — good fighting today, Vanguard!";

            var startFlight = new Vector3(-16f, 24f, 68f);
            var hoverPoint = new Vector3(0f, 5.2f, 44f);
            var padPoint = new Vector3(0f, 0.04f, 42f);

            var dustPlayed = false;
            var elapsed = 0f;
            const float total = 4.3f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(elapsed / total);

                if (evacChopperRoot != null)
                {
                    Vector3 chopperPos;
                    Quaternion chopperRot;
                    if (u < 0.55f)
                    {
                        var f = Ease.OutCubic(u / 0.55f);
                        chopperPos = Vector3.Lerp(startFlight, hoverPoint, f);
                        chopperRot = Quaternion.Slerp(Quaternion.Euler(14f, 145f, -12f), Quaternion.Euler(-8f, 155f, 3f), f);
                    }
                    else
                    {
                        var f = Ease.InOutCubic((u - 0.55f) / 0.45f);
                        chopperPos = Vector3.Lerp(hoverPoint, padPoint, f);
                        chopperRot = Quaternion.Slerp(Quaternion.Euler(-8f, 155f, 3f), Quaternion.Euler(0f, 155f, 0f), f);
                        if (!dustPlayed)
                        {
                            dustPlayed = true;
                            VFXManager.Instance?.Play(VfxKind.HitSpark, padPoint + Vector3.up * 0.25f, Palette.CoyoteTan);
                            FeelManager.Instance?.TriggerCameraShake(0.25f);
                        }
                    }
                    evacChopperRoot.SetPositionAndRotation(chopperPos, chopperRot);
                }

                var camAngle = Mathf.Lerp(-35f, 28f, Ease.InOutCubic(u));
                var camOffset = Quaternion.Euler(0f, camAngle, 0f) * new Vector3(0f, Mathf.Lerp(11.5f, 6.2f, u), -15.5f);
                var lookTarget = new Vector3(0f, 1.8f, 42f);
                var camPos = lookTarget + camOffset;
                ApplyCameraPose(camPos, Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up), 46f);
                UpdateTypewriterUI(fullStamp, speaker, line, Mathf.Clamp01(u / 0.45f));
                yield return null;
            }

            _rotorSpinSpeed = 260f;
        }

        private IEnumerator AnimateCameraShot(
            float duration,
            Vector3 startPos,
            Vector3 endPos,
            Vector3 startLook,
            Vector3 endLook,
            float startFov,
            float endFov,
            string fullStamp,
            string speaker,
            string caption,
            bool stampAlreadyTyped = false)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                var s = Ease.InOutCubic(u);

                var camPos = Vector3.Lerp(startPos, endPos, s);
                var look = Vector3.Lerp(startLook, endLook, s);
                ApplyCameraPose(camPos, Quaternion.LookRotation((look - camPos).normalized, Vector3.up), Mathf.Lerp(startFov, endFov, s));
                var typeProgress = stampAlreadyTyped ? 1f : Mathf.Clamp01(u / 0.55f);
                UpdateTypewriterUI(fullStamp, speaker, caption, typeProgress);
                yield return null;
            }
        }

        private static UnityEngine.Camera _cachedCutsceneCam;

        private static void ApplyCameraPose(Vector3 pos, Quaternion rot, float fov)
        {
            if (_cachedCutsceneCam == null) _cachedCutsceneCam = UnityEngine.Camera.main;
            if (_cachedCutsceneCam == null) return;
            _cachedCutsceneCam.transform.SetPositionAndRotation(pos, rot);
            _cachedCutsceneCam.fieldOfView = fov;
        }

        private void UpdateTypewriterUI(string fullStamp, string speaker, string caption, float stampProgress)
        {
            var chars = Mathf.Clamp(Mathf.RoundToInt(fullStamp.Length * stampProgress), 0, fullStamp.Length);
            var partial = fullStamp.Substring(0, chars);
            CurrentStampText = partial;
            UIManager.Instance?.UpdateCutsceneText(partial, speaker, caption);
        }

        private static void SetGateTransform(
            Transform left,
            Transform right,
            Vector3 leftClosed,
            Vector3 rightClosed,
            float openDistance,
            bool collidersEnabled)
        {
            if (left != null)
            {
                left.position = leftClosed + new Vector3(-openDistance, 0f, 0f);
                foreach (var col in left.GetComponentsInChildren<Collider>())
                {
                    col.enabled = collidersEnabled;
                }
            }

            if (right == null) return;
            {
                right.position = rightClosed + new Vector3(openDistance, 0f, 0f);
                foreach (var col in right.GetComponentsInChildren<Collider>())
                {
                    col.enabled = collidersEnabled;
                }
            }
        }
    }
}
