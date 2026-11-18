using System;
using System.Collections;
using Audio;
using Character;
using Core;
using Feel;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Camera
{
    public enum CutsceneType
    {
        IntroInfil,
        Act2GateBreach,
        FinalBossIntro,
        VictoryEvac
    }

    /// <summary>
    /// Tactically framed in-engine cutscene director handling widescreen cinema letterbox,
    /// bottom-left green typewriter mission stamps, multi-beat story camera choreography,
    /// sliding perimeter blast gates, and mission-unique extraction/epilogue sequences.
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
            AudioManager.Instance?.StopRadioVoice();
            AudioManager.Instance?.PlayUIClick();

            var m = StoryDirector.ActiveMissionIndex;
            switch (CurrentCutscene)
            {
                case CutsceneType.Act2GateBreach:
                    OpenGateImmediate(1);
                    break;
                case CutsceneType.FinalBossIntro:
                    OpenGateImmediate(1);
                    if (m != 4) OpenGateImmediate(2);
                    break;
                case CutsceneType.VictoryEvac when evacChopperRoot != null && m >= StoryDirector.CampaignMissionCount:
                    OpenGateImmediate(1);
                    OpenGateImmediate(2);
                    evacChopperRoot.gameObject.SetActive(true);
                    evacChopperRoot.SetPositionAndRotation(new Vector3(0f, 0.04f, 42f), Quaternion.Euler(0f, 155f, 0f));
                    _rotorSpinSpeed = 240f;
                    break;
                case CutsceneType.VictoryEvac:
                    OpenGateImmediate(1);
                    OpenGateImmediate(2);
                    break;
                case CutsceneType.IntroInfil:
                    break;
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
                    yield return RunMidBreachShot();
                    break;
                case CutsceneType.FinalBossIntro:
                    yield return RunFinalBossIntroShot();
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
            var m = StoryDirector.ActiveMissionIndex;
            var spawnPos = PlayerController.Instance != null ? PlayerController.Instance.transform.position : new Vector3(0f, 0.02f, -43f);
            SquadDirector.Instance?.PlaceSquadFormation(spawnPos, Vector3.forward, resetHp: false);

            var fullStamp = StoryDirector.GetMissionTitle(m) + "\n" + StoryDirector.GetMissionSubtitle(m) + "\nVANGUARD-09 // PROTOCOL ZERO";
            var beats = StoryDirector.GetIntroCutsceneBeats(m);
            for (var i = 0; i < beats.Length; i++)
            {
                yield return PlayBeat(beats[i], fullStamp, stampAlreadyTyped: i > 0);
            }
        }

        private IEnumerator RunMidBreachShot()
        {
            var m = StoryDirector.ActiveMissionIndex;
            const int targetGate = 1;
            var gateLeft = blastGateAct1Left;
            var gateRight = blastGateAct1Right;
            var leftClosed = _gate1LeftClosed;
            var rightClosed = _gate1RightClosed;
            var blastPos = m switch
            {
                1 => new Vector3(0f, 1.5f, -14f),
                2 => new Vector3(-14f, 1.6f, -20f),
                3 => new Vector3(0f, 1.5f, -26f),
                4 => new Vector3(0f, 1.6f, -26f),
                _ => new Vector3(0f, 1.6f, -20f)
            };

            var fullStamp = StoryDirector.GetMissionTitle(m) + "\nBREACH CONFIRMED // SECTOR UNLOCKED";
            var beats = StoryDirector.GetMidBreachCutsceneBeats(m);
            var beat = beats[0];
            var total = PlayBeatVoiceAndGetDuration(beat);

            var triggeredBlast = false;
            var elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, total));
                var smooth = Ease.InOutCubic(u);

                if (!triggeredBlast && u >= 0.24f)
                {
                    triggeredBlast = true;
                    Gate1Open = true;
                    VFXManager.Instance?.Play(VfxKind.EnemyDeath, blastPos, Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.45f);
                    AudioManager.Instance?.PlayEnemyExplode();
                }

                var slide = u < 0.24f ? 0f : Ease.OutCubic(Mathf.Clamp01((u - 0.24f) / 0.56f)) * 6.5f;
                SetGateTransform(gateLeft, gateRight, leftClosed, rightClosed, slide, u < 0.35f);

                var camPos = Vector3.Lerp(beat.StartPos, beat.EndPos, smooth);
                var lookTarget = Vector3.Lerp(beat.StartLook, beat.EndLook, smooth);
                ApplyCameraPose(camPos, Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up), Mathf.Lerp(beat.StartFov, beat.EndFov, smooth));
                UpdateTypewriterUI(fullStamp, beat.Speaker, beat.Caption, Mathf.Clamp01(u / 0.5f));
                yield return null;
            }
            yield return WaitForRadioVoiceFinish();

            OpenGateImmediate(targetGate);
            for (var i = 1; i < beats.Length; i++)
            {
                yield return PlayBeat(beats[i], fullStamp, stampAlreadyTyped: true);
            }
        }

        private IEnumerator RunFinalBossIntroShot()
        {
            var m = StoryDirector.ActiveMissionIndex;
            OpenGateImmediate(1);
            if (m != 4) OpenGateImmediate(2);

            var fullStamp = StoryDirector.GetMissionTitle(m) + "\nHVT // " + StoryDirector.GetBossCallsign(4);
            AudioManager.Instance?.PlayMilestoneSting();

            var beats = StoryDirector.GetBossCutsceneBeats(m);
            for (var i = 0; i < beats.Length; i++)
            {
                yield return PlayBeat(beats[i], fullStamp, stampAlreadyTyped: i > 0);
            }
        }

        private IEnumerator RunVictoryEvacShot()
        {
            var m = StoryDirector.ActiveMissionIndex;
            OpenGateImmediate(1);
            OpenGateImmediate(2);

            var isBroadcast = SaveManager.EnsureInstance().LastEndingChoice == CampaignEndingKind.BroadcastBlackout;
            var beats = StoryDirector.GetVictoryEvacBeats(m, isBroadcast);

            if (m < StoryDirector.CampaignMissionCount)
            {
                var exfilStamp = m switch
                {
                    1 => "ACT I EXFIL // WRAITH-2 (KOZLOWSKI) K.I.A.\nBROKEN ARROW SHELLING ON PIER NORTH",
                    2 => "ACT II EXFIL // SPARK-3 (REYES) K.I.A.\nNORTH TUNNEL BULKHEAD COLLAPSE",
                    3 => "ACT III EXFIL // IRONWOOD-1 (ROURKE) K.I.A.\nSUB-LEVEL HALON CONTAINMENT SEAL",
                    _ => "ACT IV EXFIL // APEX-01 SHATTERED\nCOURSE SET FOR WASHINGTON SECTOR ZERO"
                };

                VFXManager.Instance?.Play(VfxKind.EnemyDeath, beats[0].StartLook + Vector3.up * 0.6f, Palette.RocketOrange);
                FeelManager.Instance?.TriggerCameraShake(0.35f);
                for (var i = 0; i < beats.Length; i++)
                {
                    yield return PlayBeat(beats[i], exfilStamp, stampAlreadyTyped: i > 0);
                }
                yield break;
            }

            // ACT V CLIMAX: Blackhawk VALKYRIE-6 Touchdown & True Campaign Finale
            PlayerController.Instance?.TeleportTo(new Vector3(-1.4f, 0.02f, 35.5f));
            SquadDirector.Instance?.PlaceSquadFormation(new Vector3(-1.0f, 0.02f, 34.8f), Vector3.forward, resetHp: false);

            if (evacChopperRoot != null)
            {
                evacChopperRoot.gameObject.SetActive(true);
            }
            _rotorSpinSpeed = 980f;

            var fullStamp = isBroadcast
                ? "ENDING A // BLACKOUT LEDGER BROADCAST\nVALKYRIE-6 // SOLE OPERATOR EVAC"
                : "ENDING B // PROTOCOL ZERO SEALED\nVALKYRIE-6 // CLASSIFIED DEBRIEF";

            var beat0 = beats[0];
            var beat1 = beats.Length > 1 ? beats[1] : beat0;
            var d0 = PlayBeatVoiceAndGetDuration(beat0);
            var d1 = beats.Length > 1 ? Mathf.Max(beat1.Duration, AudioManager.Instance != null ? AudioManager.Instance.PeekRadioClipDuration(StoryDirector.NormalizeVoiceCallsign(beat1.Speaker), beat1.Caption) : 0f) : 0f;
            var switchedSpeaker = false;

            var startFlight = new Vector3(-16f, 24f, 68f);
            var hoverPoint = new Vector3(0f, 5.2f, 44f);
            var padPoint = new Vector3(0f, 0.04f, 42f);

            var dustPlayed = false;
            var elapsed = 0f;
            var total = d0 + d1;
            while (elapsed < total || (!switchedSpeaker && AudioManager.Instance != null && AudioManager.Instance.IsRadioVoicePlaying))
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, total));

                if (evacChopperRoot != null)
                {
                    Vector3 chopperPos;
                    Quaternion chopperRot;
                    if (u < 0.52f)
                    {
                        var f = Ease.OutCubic(u / 0.52f);
                        chopperPos = Vector3.Lerp(startFlight, hoverPoint, f);
                        chopperRot = Quaternion.Slerp(Quaternion.Euler(14f, 145f, -12f), Quaternion.Euler(-8f, 155f, 3f), f);
                    }
                    else
                    {
                        var f = Ease.InOutCubic((u - 0.52f) / 0.48f);
                        chopperPos = Vector3.Lerp(hoverPoint, padPoint, f);
                        chopperRot = Quaternion.Slerp(Quaternion.Euler(-8f, 155f, 3f), Quaternion.Euler(0f, 155f, 0f), f);
                        if (!dustPlayed)
                        {
                            dustPlayed = true;
                            VFXManager.Instance?.Play(VfxKind.HitSpark, padPoint + Vector3.up * 0.25f, Palette.CoyoteTan);
                            FeelManager.Instance?.TriggerCameraShake();
                        }
                    }
                    evacChopperRoot.SetPositionAndRotation(chopperPos, chopperRot);
                }

                if (!switchedSpeaker && beats.Length > 1 && elapsed >= d0 && (AudioManager.Instance == null || !AudioManager.Instance.IsRadioVoicePlaying))
                {
                    switchedSpeaker = true;
                    PlayBeatVoiceAndGetDuration(beat1);
                }

                var camAngle = Mathf.Lerp(-35f, 28f, Ease.InOutCubic(u));
                var camOffset = Quaternion.Euler(0f, camAngle, 0f) * new Vector3(0f, Mathf.Lerp(11.5f, 6.2f, u), -15.5f);
                var lookTarget = new Vector3(0f, 1.8f, 42f);
                var camPos = lookTarget + camOffset;
                ApplyCameraPose(camPos, Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up), 46f);

                var activeBeat = !switchedSpeaker ? beat0 : beat1;
                UpdateTypewriterUI(fullStamp, activeBeat.Speaker, activeBeat.Caption, Mathf.Clamp01(u / 0.35f));
                yield return null;
            }
            yield return WaitForRadioVoiceFinish();

            _rotorSpinSpeed = 260f;
        }

        private static float PlayBeatVoiceAndGetDuration(CutsceneBeat beat)
        {
            var spoken = AudioManager.Instance != null
                ? AudioManager.Instance.PlayRadioLineAudio(StoryDirector.NormalizeVoiceCallsign(beat.Speaker), beat.Caption)
                : 0f;
            return Mathf.Max(beat.Duration, spoken);
        }

        private static IEnumerator WaitForRadioVoiceFinish()
        {
            while (AudioManager.Instance != null && AudioManager.Instance.IsRadioVoicePlaying)
            {
                yield return null;
            }
        }

        private IEnumerator PlayBeat(CutsceneBeat beat, string fullStamp, bool stampAlreadyTyped)
        {
            var totalDuration = PlayBeatVoiceAndGetDuration(beat);
            yield return AnimateCameraShot(
                totalDuration,
                beat.StartPos,
                beat.EndPos,
                beat.StartLook,
                beat.EndLook,
                beat.StartFov,
                beat.EndFov,
                fullStamp,
                beat.Speaker,
                beat.Caption,
                stampAlreadyTyped);
            yield return WaitForRadioVoiceFinish();
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
            ApplyGateLeafState(left, leftClosed, -openDistance, collidersEnabled);
            ApplyGateLeafState(right, rightClosed, openDistance, collidersEnabled);
        }

        private static void ApplyGateLeafState(Transform leaf, Vector3 closedPos, float xOffset, bool blocking)
        {
            if (leaf == null) return;
            leaf.position = closedPos + new Vector3(xOffset, 0f, 0f);
            foreach (var col in leaf.GetComponentsInChildren<Collider>())
            {
                col.enabled = blocking;
            }
            foreach (var obstacle in leaf.GetComponentsInChildren<NavMeshObstacle>())
            {
                obstacle.enabled = blocking;
            }
        }
    }
}
