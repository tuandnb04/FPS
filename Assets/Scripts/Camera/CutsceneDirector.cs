using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TopDownShooter.Audio;
using TopDownShooter.Core;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.CameraRig
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

        private Vector3 gate1LeftClosed;
        private Vector3 gate1RightClosed;
        private Vector3 gate2LeftClosed;
        private Vector3 gate2RightClosed;
        private bool gatePositionsCached;
        private bool gate1Open;
        private bool gate2Open;

        private Coroutine activeCutscene;
        private Action pendingCallback;
        private float rotorSpinSpeed;

        public bool IsPlaying => activeCutscene != null;
        public CutsceneType CurrentCutscene { get; private set; }
        public bool Gate1Open => gate1Open;
        public bool Gate2Open => gate2Open;
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
            if (gatePositionsCached) return;
            if (blastGateAct1Left != null) gate1LeftClosed = blastGateAct1Left.position;
            if (blastGateAct1Right != null) gate1RightClosed = blastGateAct1Right.position;
            if (blastGateAct2Left != null) gate2LeftClosed = blastGateAct2Left.position;
            if (blastGateAct2Right != null) gate2RightClosed = blastGateAct2Right.position;
            gatePositionsCached = blastGateAct1Left != null && blastGateAct2Left != null;
        }

        public void ResetCampaignProps()
        {
            if (activeCutscene != null)
            {
                StopCoroutine(activeCutscene);
                activeCutscene = null;
            }
            pendingCallback = null;
            rotorSpinSpeed = 0f;
            gate1Open = false;
            gate2Open = false;

            CacheInitialGatePositions();
            SetGateTransform(blastGateAct1Left, blastGateAct1Right, gate1LeftClosed, gate1RightClosed, 0f, true);
            SetGateTransform(blastGateAct2Left, blastGateAct2Right, gate2LeftClosed, gate2RightClosed, 0f, true);

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
            if (gateIndex == 1 && !gate1Open)
            {
                gate1Open = true;
                SetGateTransform(blastGateAct1Left, blastGateAct1Right, gate1LeftClosed, gate1RightClosed, 6.2f, false);
            }
            else if (gateIndex == 2 && !gate2Open)
            {
                gate2Open = true;
                SetGateTransform(blastGateAct2Left, blastGateAct2Right, gate2LeftClosed, gate2RightClosed, 6.8f, false);
            }
        }

        public void PlayCutscene(CutsceneType type, Action onComplete)
        {
            if (activeCutscene != null)
            {
                StopCoroutine(activeCutscene);
            }

            CacheInitialGatePositions();
            CurrentCutscene = type;
            pendingCallback = onComplete;
            activeCutscene = StartCoroutine(RunCutsceneRoutine(type));
        }

        public void SkipCurrentCutscene()
        {
            if (activeCutscene == null) return;
            StopCoroutine(activeCutscene);
            activeCutscene = null;
            AudioManager.Instance?.PlayUIClick();

            if (CurrentCutscene == CutsceneType.Act2GateBreach)
            {
                OpenGateImmediate(1);
            }
            else if (CurrentCutscene == CutsceneType.Act3BossVolkov)
            {
                OpenGateImmediate(1);
                OpenGateImmediate(2);
            }
            else if (CurrentCutscene == CutsceneType.VictoryEvac && evacChopperRoot != null)
            {
                evacChopperRoot.gameObject.SetActive(true);
                evacChopperRoot.SetPositionAndRotation(new Vector3(0f, 0.04f, 42f), Quaternion.Euler(0f, 155f, 0f));
                rotorSpinSpeed = 240f;
            }

            CompleteCutsceneCleanup();
        }

        private void Update()
        {
            if (evacChopperRotor != null && evacChopperRoot != null && evacChopperRoot.gameObject.activeInHierarchy && rotorSpinSpeed > 1f)
            {
                evacChopperRotor.Rotate(0f, rotorSpinSpeed * Time.unscaledDeltaTime, 0f, Space.Self);
            }

            if (activeCutscene != null && Keyboard.current != null)
            {
                if (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
                {
                    SkipCurrentCutscene();
                }
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
            }

            activeCutscene = null;
            CompleteCutsceneCleanup();
        }

        private void CompleteCutsceneCleanup()
        {
            TopDownCameraRig.Instance?.SetCutsceneOverride(false);
            UIManager.Instance?.HideCutsceneOverlayImmediate();
            Action cb = pendingCallback;
            pendingCallback = null;
            cb?.Invoke();
        }

        private IEnumerator RunIntroInfilShot()
        {
            string fullStamp = "OPERATION: IRON HARVEST\nSECTOR ECHO-7  //  06:42 HRS\nSGT. R. VANCE  -  TASK FORCE ZERO";
            string speaker = "OVERWATCH // TAC-NET CH-04";
            string line1 = "Satellite feed live across Echo-7. Kronos PMC fortified the southern road, fuel farm, and northern helipad.";
            string line2 = "Vanguard-1 inserted at South Gate. Breach the checkpoint and fight north to clear LZ EVAC-6.";

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

            // Shot 2: Dramatic crane dive toward Vanguard-1 behind concrete cover in Sector 1
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
            string fullStamp = "ACT II // FUEL & CARGO DEPOT\nBLAST GATE ALPHA: BREACHED";
            string speaker = "VANGUARD-1 // SQUAD-LOCAL";
            string line = "Detonating gate locks! Fuel silos and shipping containers ahead - pushing deep into Sector II.";

            bool triggeredBlast = false;
            float elapsed = 0f;
            const float total = 3.2f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / total);
                float smooth = Ease.InOutCubic(u);

                if (!triggeredBlast && u >= 0.24f)
                {
                    triggeredBlast = true;
                    gate1Open = true;
                    VFXManager.Instance?.Play(VfxKind.EnemyDeath, new Vector3(0f, 1.6f, -14f), Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.45f);
                    AudioManager.Instance?.PlayEnemyExplode();
                }

                float slide = u < 0.24f ? 0f : Ease.OutCubic(Mathf.Clamp01((u - 0.24f) / 0.56f)) * 6.2f;
                SetGateTransform(blastGateAct1Left, blastGateAct1Right, gate1LeftClosed, gate1RightClosed, slide, u < 0.35f);

                Vector3 camPos = Vector3.Lerp(new Vector3(7.5f, 7.2f, -29.5f), new Vector3(0.0f, 9.2f, -23.0f), smooth);
                Vector3 lookTarget = Vector3.Lerp(new Vector3(0f, 1.5f, -14.0f), new Vector3(0f, 1.8f, -2.0f), smooth);
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

            string fullStamp = "ACT III // NORTH COMMAND HELIPAD\nHVT: COL. VIKTOR VOLKOV  [IRONCLAD ALPHA]";
            string speaker = "COL. VOLKOV // OPEN FREQ 148.2";
            string line = "You tore through my outer compound, Vanguard. Finish this on the tarmac - IRONCLAD ALPHA engaging!";

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

            if (evacChopperRoot != null)
            {
                evacChopperRoot.gameObject.SetActive(true);
            }
            rotorSpinSpeed = 980f;

            string fullStamp = "MISSION COMPLETE // SECTOR SECURED\nEVAC-6 BLACKHAWK TOUCHDOWN";
            string speaker = "EVAC-6 // AIR-GROUND CH-09";
            string line = "Hot LZ clear! Evac-6 touching down on North Helipad - welcome aboard, Sergeant Vance.";

            Vector3 startFlight = new Vector3(-16f, 24f, 68f);
            Vector3 hoverPoint = new Vector3(0f, 5.2f, 44f);
            Vector3 padPoint = new Vector3(0f, 0.04f, 42f);

            bool dustPlayed = false;
            float elapsed = 0f;
            const float total = 4.3f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / total);

                if (evacChopperRoot != null)
                {
                    Vector3 chopperPos;
                    Quaternion chopperRot;
                    if (u < 0.55f)
                    {
                        float f = Ease.OutCubic(u / 0.55f);
                        chopperPos = Vector3.Lerp(startFlight, hoverPoint, f);
                        chopperRot = Quaternion.Slerp(Quaternion.Euler(14f, 145f, -12f), Quaternion.Euler(-8f, 155f, 3f), f);
                    }
                    else
                    {
                        float f = Ease.InOutCubic((u - 0.55f) / 0.45f);
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

                float camAngle = Mathf.Lerp(-35f, 28f, Ease.InOutCubic(u));
                Vector3 camOffset = Quaternion.Euler(0f, camAngle, 0f) * new Vector3(0f, Mathf.Lerp(11.5f, 6.2f, u), -15.5f);
                Vector3 lookTarget = new Vector3(0f, 1.8f, 42f);
                Vector3 camPos = lookTarget + camOffset;
                ApplyCameraPose(camPos, Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up), 46f);
                UpdateTypewriterUI(fullStamp, speaker, line, Mathf.Clamp01(u / 0.45f));
                yield return null;
            }

            rotorSpinSpeed = 260f;
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
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float s = Ease.InOutCubic(u);

                Vector3 camPos = Vector3.Lerp(startPos, endPos, s);
                Vector3 look = Vector3.Lerp(startLook, endLook, s);
                ApplyCameraPose(camPos, Quaternion.LookRotation((look - camPos).normalized, Vector3.up), Mathf.Lerp(startFov, endFov, s));
                float typeProgress = stampAlreadyTyped ? 1f : Mathf.Clamp01(u / 0.55f);
                UpdateTypewriterUI(fullStamp, speaker, caption, typeProgress);
                yield return null;
            }
        }

        private static void ApplyCameraPose(Vector3 pos, Quaternion rot, float fov)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }

        private void UpdateTypewriterUI(string fullStamp, string speaker, string caption, float stampProgress)
        {
            int chars = Mathf.Clamp(Mathf.RoundToInt(fullStamp.Length * stampProgress), 0, fullStamp.Length);
            string partial = fullStamp.Substring(0, chars);
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
                foreach (Collider col in left.GetComponentsInChildren<Collider>())
                {
                    col.enabled = collidersEnabled;
                }
            }
            if (right != null)
            {
                right.position = rightClosed + new Vector3(openDistance, 0f, 0f);
                foreach (Collider col in right.GetComponentsInChildren<Collider>())
                {
                    col.enabled = collidersEnabled;
                }
            }
        }
    }
}
