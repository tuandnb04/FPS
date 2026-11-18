using System;
using System.Collections.Generic;
using Combat;
using Core;
using UnityEngine;

namespace Audio
{
    /// <summary>
    /// Procedural PCM audio synthesizer, military VHF radio voice DSP channel
    /// (520-2550Hz bandpass + push-to-talk squelch), and headless gain-layer mixer controller.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private const string PrefMusicVol = "Vanguard_MusicVol";
        private const string PrefSfxVol = "Vanguard_SFXVol";
        private const int SampleRate = 22050;

        [Header("Headless Gain Layer")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.70f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.85f;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private AudioSource _gunSfxSource;
        private AudioSource _radioVoiceSource;
        private readonly Dictionary<string, AudioClip> _voiceClipCache = new();
        private readonly Queue<string> _voiceClipLru = new(16);
        private readonly float[] _radioOutputBuffer = new float[64];
        private float _pendingSquelchCloseAt = -1f;

        private AudioClip _dashClip;
        private AudioClip _hitmarkerClip;
        private AudioClip _hitmarkerKillClip;
        private AudioClip _reloadClip;
        private AudioClip _shotgunShellInsertClip;
        private AudioClip _nvgOnClip;
        private AudioClip _nvgOffClip;
        private AudioClip _radioSquelchClip;
        private AudioClip _rifleClip;
        private AudioClip _burstCarbineClip;
        private AudioClip _smgClip;
        private AudioClip _shotgunClip;
        private AudioClip _dmrCrackClip;
        private AudioClip _lmgClip;
        private AudioClip _launcherThumpClip;
        private AudioClip _footstepClip;

        private float _currentDuckGain = 1f;
        private float _lastEnemyShootTime = -10f;
        private float _lastHitmarkerTime = -10f;
        private float _lastFootstepTime = -10f;

        /// <summary>Normalized Music volume [0..1] for UI sliders and verification checks.</summary>
        public float MusicVol => musicVolume;

        /// <summary>Normalized SFX volume [0..1] for UI sliders and verification checks.</summary>
        public float SfxVol => sfxVolume;

        /// <summary>True when pause -6 dB ducking is currently active.</summary>
        public bool IsDucked { get; private set; }

        /// <summary>Effective linear gain currently applied to the ambient music AudioSource.</summary>
        public float CurrentMusicOutputGain => _musicSource != null ? _musicSource.volume : 0f;

        public AudioClip ShootClip { get; private set; }

        public AudioClip HitClip { get; private set; }

        public AudioClip ExplodeClip { get; private set; }

        public AudioClip PickupBlipClip { get; private set; }

        public AudioClip MilestoneStingClip { get; private set; }

        public AudioClip UIClickClip { get; private set; }

        public AudioClip RadioChirpClip { get; private set; }

        public AudioClip AmbientBedClip { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            musicVolume = PlayerPrefs.GetFloat(PrefMusicVol, 0.70f);
            sfxVolume = PlayerPrefs.GetFloat(PrefSfxVol, 0.85f);

            EnsureAudioSources();
            EnsureAudioListener();
            GenerateClips();
            StartAmbientBed();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            var targetDuck = IsDucked ? 0.5f : 1f; // -6.02 dB pause attenuation
            if (Mathf.Abs(_currentDuckGain - targetDuck) > 0.0001f)
            {
                _currentDuckGain = Mathf.MoveTowards(_currentDuckGain, targetDuck, Time.unscaledDeltaTime * 4f);
                ApplyVolumes();
            }

            if (_pendingSquelchCloseAt > 0f && Time.unscaledTime >= _pendingSquelchCloseAt)
            {
                _pendingSquelchCloseAt = -1f;
                PlayOneShotWithPitch(_radioSquelchClip, 1f, 0.42f);
            }
        }

        private void EnsureAudioSources()
        {
            var sources = GetComponents<AudioSource>();
            _musicSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            _sfxSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
            _gunSfxSource = sources.Length > 2 ? sources[2] : gameObject.AddComponent<AudioSource>();

            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;

            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f;

            _gunSfxSource.loop = false;
            _gunSfxSource.playOnAwake = false;
            _gunSfxSource.spatialBlend = 0f;

            if (_radioVoiceSource == null)
            {
                var radioChild = transform.Find("RadioVoiceChannel");
                GameObject radioGo;
                if (radioChild != null)
                {
                    radioGo = radioChild.gameObject;
                }
                else
                {
                    radioGo = new GameObject("RadioVoiceChannel");
                    radioGo.transform.SetParent(transform, false);
                }

                _radioVoiceSource = GetOrAdd<AudioSource>(radioGo);
                _radioVoiceSource.loop = false;
                _radioVoiceSource.playOnAwake = false;
                _radioVoiceSource.spatialBlend = 0f;

                var hp = GetOrAdd<AudioHighPassFilter>(radioGo);
                hp.cutoffFrequency = 520f;
                hp.highpassResonanceQ = 1.35f;

                var lp = GetOrAdd<AudioLowPassFilter>(radioGo);
                lp.cutoffFrequency = 2550f;
                lp.lowpassResonanceQ = 1.55f;

                var dist = GetOrAdd<AudioDistortionFilter>(radioGo);
                dist.distortionLevel = 0.22f;
            }
            ApplyVolumes();
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static void EnsureAudioListener()
        {
            if (FindAnyObjectByType<AudioListener>() != null)
            {
                return;
            }
            var mainCam = UnityEngine.Camera.main;
            if (mainCam != null)
            {
                mainCam.gameObject.AddComponent<AudioListener>();
            }
        }

        private void GenerateClips()
        {
            if (ShootClip == null) ShootClip = BuildGunshotClip("SFX_Shoot", 0.11f, 0.36f);
            if (_rifleClip == null) _rifleClip = BuildStyledGunshotClip("SFX_Rifle_M4", 0.12f, 0.38f, 340f, 75f, 11f, 34f, 0.56f, 7101);
            if (_burstCarbineClip == null) _burstCarbineClip = BuildStyledGunshotClip("SFX_Carbine_AN94", 0.095f, 0.37f, 440f, 110f, 16f, 42f, 0.70f, 7202);
            if (_smgClip == null) _smgClip = BuildStyledGunshotClip("SFX_SMG_Vector", 0.068f, 0.32f, 520f, 145f, 22f, 48f, 0.78f, 7303);
            if (_shotgunClip == null) _shotgunClip = BuildStyledGunshotClip("SFX_Shotgun_M870", 0.24f, 0.48f, 190f, 36f, 6.2f, 20f, 0.44f, 7404);
            if (_dmrCrackClip == null) _dmrCrackClip = BuildStyledGunshotClip("SFX_DMR_MK14", 0.19f, 0.45f, 460f, 82f, 9.5f, 46f, 0.85f, 7505);
            if (_lmgClip == null) _lmgClip = BuildStyledGunshotClip("SFX_LMG_M249", 0.135f, 0.40f, 275f, 62f, 9.2f, 28f, 0.50f, 7606);
            if (_launcherThumpClip == null) _launcherThumpClip = BuildLauncherThumpClip("SFX_Launcher_M32", 0.28f, 0.46f);
            if (_footstepClip == null) _footstepClip = BuildFootstepClip("SFX_Footstep", 0.085f, 0.24f);
            if (HitClip == null) HitClip = BuildSweepClip("SFX_Hit", 0.085f, 195f, 52f, 0.38f, false);
            if (ExplodeClip == null) ExplodeClip = BuildExplodeClip("SFX_Explode", 0.28f, 0.48f);
            if (PickupBlipClip == null) PickupBlipClip = BuildSweepClip("SFX_PickupBlip", 0.075f, 340f, 640f, 0.26f, true);
            if (MilestoneStingClip == null) MilestoneStingClip = BuildTriadClip("SFX_Milestone", 220.00f, 277.18f, 329.63f, 0.42f, 0.34f);
            if (UIClickClip == null) UIClickClip = BuildSweepClip("SFX_UIClick", 0.024f, 920f, 440f, 0.22f, false);
            if (_dashClip == null) _dashClip = BuildFoleyWhooshClip("SFX_Dash", 0.16f, 0.28f);
            if (_hitmarkerClip == null) _hitmarkerClip = BuildHitmarkerTickClip("SFX_Hitmarker", 0.028f, 0.28f, doubleTick: false);
            if (_hitmarkerKillClip == null) _hitmarkerKillClip = BuildHitmarkerTickClip("SFX_HitmarkerKill", 0.068f, 0.34f, doubleTick: true);
            if (_reloadClip == null) _reloadClip = BuildMechanicalReloadClip("SFX_Reload", 0.28f, 0.30f);
            if (_shotgunShellInsertClip == null) _shotgunShellInsertClip = BuildShotgunShellInsertClip("SFX_ShotgunShellInsert", 0.095f, 0.34f);
            if (_nvgOnClip == null) _nvgOnClip = BuildSweepClip("SFX_NvgOn", 0.22f, 1550f, 4150f, 0.24f, true);
            if (_nvgOffClip == null) _nvgOffClip = BuildSweepClip("SFX_NvgOff", 0.16f, 2800f, 650f, 0.20f, false);
            if (RadioChirpClip == null) RadioChirpClip = BuildRadioChirpClip("SFX_RadioChirp", 0.11f, 0.24f);
            if (_radioSquelchClip == null) _radioSquelchClip = BuildRadioSquelchClip("SFX_RadioSquelch", 0.085f, 0.22f);
            if (AmbientBedClip == null) AmbientBedClip = BuildAmbientBed("BGM_AmbientBed", 2.5f, 0.12f);
        }

        private void StartAmbientBed()
        {
            if (_musicSource != null && AmbientBedClip != null && !_musicSource.isPlaying)
            {
                _musicSource.clip = AmbientBedClip;
                _musicSource.Play();
            }
            ApplyVolumes();
        }

        public void SetMusicVolume(float normalized)
        {
            musicVolume = Mathf.Clamp01(normalized);
            PlayerPrefs.SetFloat(PrefMusicVol, musicVolume);
            ApplyVolumes();
        }

        public void SetSfxVolume(float normalized)
        {
            sfxVolume = Mathf.Clamp01(normalized);
            PlayerPrefs.SetFloat(PrefSfxVol, sfxVolume);
            ApplyVolumes();
        }

        public void SetPausedDucking(bool paused)
        {
            IsDucked = paused;
            _currentDuckGain = IsDucked ? 0.5f : 1f;
            if (_radioVoiceSource != null)
            {
                if (paused && _radioVoiceSource.isPlaying) _radioVoiceSource.Pause();
                else if (!paused) _radioVoiceSource.UnPause();
            }
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (_musicSource != null)
            {
                _musicSource.volume = musicVolume * _currentDuckGain;
            }
            if (_sfxSource != null)
            {
                _sfxSource.volume = sfxVolume;
            }
            if (_gunSfxSource != null)
            {
                _gunSfxSource.volume = sfxVolume;
            }
            if (_radioVoiceSource != null)
            {
                _radioVoiceSource.volume = Mathf.Clamp01(sfxVolume * 1.15f);
            }
        }

        public void PlayShoot(float pitchMultiplier = 1f, float volumeScale = 0.55f)
        {
            var src = _gunSfxSource != null ? _gunSfxSource : _sfxSource;
            if (src == null || ShootClip == null || sfxVolume <= 0.001f)
            {
                return;
            }
            src.pitch = Mathf.Clamp(pitchMultiplier * UnityEngine.Random.Range(0.97f, 1.03f), 0.45f, 1.85f);
            src.PlayOneShot(ShootClip, volumeScale);
        }

        public AudioClip GetWeaponClip(WeaponType weapon)
        {
            return weapon switch
            {
                WeaponType.ScatterShotgun => _shotgunClip ?? ShootClip,
                WeaponType.PlasmaRailgun => _dmrCrackClip ?? ShootClip,
                WeaponType.HellfireLauncher => _launcherThumpClip ?? ShootClip,
                WeaponType.An94Abakan => _burstCarbineClip ?? ShootClip,
                WeaponType.VectorSmg => _smgClip ?? ShootClip,
                WeaponType.M249Saw => _lmgClip ?? ShootClip,
                WeaponType.M32Launcher => _launcherThumpClip ?? ShootClip,
                WeaponType.M16A4Burst => _burstCarbineClip ?? _rifleClip ?? ShootClip,
                WeaponType.Xm25Airburst => _launcherThumpClip ?? ShootClip,
                _ => _rifleClip ?? ShootClip
            };
        }

        public void PlayWeaponFire(WeaponType weapon, float pitchMultiplier = 1f, float volumeScale = 0.58f)
        {
            var src = _gunSfxSource != null ? _gunSfxSource : _sfxSource;
            var clip = GetWeaponClip(weapon);
            if (src == null || clip == null || sfxVolume <= 0.001f)
            {
                return;
            }
            src.pitch = Mathf.Clamp(pitchMultiplier * UnityEngine.Random.Range(0.97f, 1.03f), 0.45f, 1.85f);
            src.PlayOneShot(clip, volumeScale);
        }

        public void PlayFootstep()
        {
            if (Time.unscaledTime - _lastFootstepTime < 0.18f) return;
            _lastFootstepTime = Time.unscaledTime;
            PlayOneShotWithPitch(_footstepClip, UnityEngine.Random.Range(0.90f, 1.10f), 0.24f);
        }

        public void PlayHit()
        {
            PlayOneShotWithPitch(HitClip, UnityEngine.Random.Range(0.92f, 1.08f), 0.70f);
        }

        public void PlayEnemyExplode()
        {
            PlayOneShotWithPitch(ExplodeClip, UnityEngine.Random.Range(0.90f, 1.10f), 0.85f);
        }

        public void PlayPickupBlip()
        {
            PlayOneShotWithPitch(PickupBlipClip, UnityEngine.Random.Range(0.92f, 1.08f), 0.75f);
        }

        public void PlayMilestoneSting()
        {
            PlayOneShotWithPitch(MilestoneStingClip, 1f, 0.90f);
        }

        public void PlayUIClick()
        {
            PlayOneShotWithPitch(UIClickClip, 1f, 0.65f);
        }

        public void PlayDash()
        {
            PlayOneShotWithPitch(_dashClip, UnityEngine.Random.Range(0.96f, 1.04f), 0.65f);
        }

        public void PlayRadioChirp()
        {
            PlayOneShotWithPitch(RadioChirpClip, 1f, 0.72f);
        }

        public bool HasRadioVoiceLoaded { get; private set; }

        public bool IsRadioVoicePlaying => _radioVoiceSource != null && _radioVoiceSource.isPlaying;

        public float PeekRadioClipDuration(string callsign, string body)
        {
            var key = StoryDirector.GetVoiceClipKey(callsign, body);
            var clip = LoadRadioVoiceClip(key);
            return clip != null ? clip.length + 0.45f : 0f;
        }

        public float PlayRadioLineAudio(string callsign, string body)
        {
            PlayRadioChirp();
            _pendingSquelchCloseAt = -1f;

            var key = StoryDirector.GetVoiceClipKey(callsign, body);
            var clip = LoadRadioVoiceClip(key);
            if (clip == null)
            {
                HasRadioVoiceLoaded = false;
                return 0f;
            }

            HasRadioVoiceLoaded = true;
            if (_radioVoiceSource != null && sfxVolume > 0.001f)
            {
                _radioVoiceSource.Stop();
                _radioVoiceSource.clip = clip;
                _radioVoiceSource.volume = Mathf.Clamp01(sfxVolume * 1.15f);
                _radioVoiceSource.PlayDelayed(0.08f);
                _pendingSquelchCloseAt = Time.unscaledTime + 0.08f + clip.length;
            }
            return clip.length + 0.45f;
        }

        public void StopRadioVoice()
        {
            _pendingSquelchCloseAt = -1f;
            if (_radioVoiceSource != null && _radioVoiceSource.isPlaying)
            {
                _radioVoiceSource.Stop();
            }
        }

        public bool SampleRadioLiveSpectrum(float[] barHeights, float dt)
        {
            if (_radioVoiceSource == null || !_radioVoiceSource.isPlaying || barHeights == null || barHeights.Length == 0)
            {
                return false;
            }

            _radioVoiceSource.GetOutputData(_radioOutputBuffer, 0);
            var bars = barHeights.Length;
            var chunk = Mathf.Max(1, _radioOutputBuffer.Length / bars);
            var smooth = 1f - Mathf.Exp(-22f * dt);

            for (var b = 0; b < bars; b++)
            {
                var peak = 0f;
                var start = b * chunk;
                var end = Mathf.Min(_radioOutputBuffer.Length, start + chunk);
                for (var i = start; i < end; i++)
                {
                    var abs = Mathf.Abs(_radioOutputBuffer[i]);
                    if (abs > peak) peak = abs;
                }
                var target = Mathf.Clamp(0.18f + peak * 3.8f, 0.16f, 1.0f);
                barHeights[b] = Mathf.Lerp(barHeights[b], target, smooth);
            }
            return true;
        }

        private AudioClip LoadRadioVoiceClip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_voiceClipCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var loaded = Resources.Load<AudioClip>("VoiceRadio/" + key);
            _voiceClipCache[key] = loaded;
            _voiceClipLru.Enqueue(key);

            while (_voiceClipLru.Count > 12)
            {
                var oldestKey = _voiceClipLru.Dequeue();
                if (!_voiceClipCache.Remove(oldestKey, out var oldClip)) continue;
                if (oldClip != null && (_radioVoiceSource == null || _radioVoiceSource.clip != oldClip))
                {
                    Resources.UnloadAsset(oldClip);
                }
            }

            return loaded;
        }

        public void PlayHitmarker(bool isKill)
        {
            if (Time.unscaledTime - _lastHitmarkerTime < 0.032f) return;
            _lastHitmarkerTime = Time.unscaledTime;
            if (isKill && _hitmarkerKillClip != null)
            {
                PlayOneShotWithPitch(_hitmarkerKillClip, UnityEngine.Random.Range(0.97f, 1.03f), 0.75f);
            }
            else if (_hitmarkerClip != null)
            {
                PlayOneShotWithPitch(_hitmarkerClip, UnityEngine.Random.Range(0.95f, 1.05f), 0.58f);
            }
        }

        public void PlayReload()
        {
            PlayOneShotWithPitch(_reloadClip, UnityEngine.Random.Range(0.96f, 1.04f), 0.72f);
        }

        public void PlayShotgunShellInsert()
        {
            PlayOneShotWithPitch(_shotgunShellInsertClip, UnityEngine.Random.Range(0.94f, 1.08f), 0.74f);
        }

        public void PlayNvgToggle(bool active)
        {
            PlayOneShotWithPitch(active ? _nvgOnClip : _nvgOffClip, 1f, 0.70f);
        }

        public void PlayEnemyShoot(float pitchMultiplier = 0.88f, float volumeScale = 0.22f)
        {
            if (Time.unscaledTime - _lastEnemyShootTime < 0.045f)
            {
                return;
            }
            _lastEnemyShootTime = Time.unscaledTime;
            PlayShoot(pitchMultiplier, volumeScale);
        }

        private void PlayOneShotWithPitch(AudioClip clip, float pitch, float scale)
        {
            if (_sfxSource == null || clip == null || sfxVolume <= 0.001f)
            {
                return;
            }
            _sfxSource.pitch = pitch;
            _sfxSource.PlayOneShot(clip, scale);
        }

        private static AudioClip BuildGunshotClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.002f * SampleRate));
            var rng = new System.Random(7091);
            var lpNoise = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var thumpFreq = Mathf.Lerp(320f, 68f, Mathf.Pow(t, 0.35f));
                phase += (2.0 * Math.PI * thumpFreq) / SampleRate;

                var rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, Mathf.Lerp(0.65f, 0.12f, t));
                var transientCrack = rawNoise * Mathf.Exp(-t * 36f);
                var bodyThump = (float)Math.Sin(phase) * Mathf.Exp(-t * 14f);

                var mixed = 0.52f * lpNoise * Mathf.Exp(-t * 11f) + 0.34f * transientCrack + 0.38f * bodyThump;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp(mixed * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildFoleyWhooshClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.008f * SampleRate));
            var rng = new System.Random(2048);
            var lp = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, white, Mathf.Lerp(0.08f, 0.24f, t));
                var arch = Mathf.Sin(t * Mathf.PI);
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * arch;
                data[i] = Mathf.Clamp(lp * amplitude * env * 1.4f, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildSweepClip(string clipName, float duration, float startHz, float endHz, float amplitude, bool addHarmonic)
        {
            var sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.005f * SampleRate));

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var freq = Mathf.Lerp(startHz, endHz, t);
                phase += (2.0 * Math.PI * freq) / SampleRate;

                var wave = (float)Math.Sin(phase);
                if (addHarmonic)
                {
                    wave = 0.75f * wave + 0.25f * (float)Math.Sin(phase * 2.0);
                }

                var decay = 1f - t * 0.75f;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * decay;
                data[i] = Mathf.Clamp(wave * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildExplodeClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.004f * SampleRate));
            var rng = new System.Random(1337);
            var lpNoise = 0f;
            var prevNoise = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var freq = Mathf.Lerp(125f, 24f, Mathf.Pow(t, 0.4f));
                phase += (2.0 * Math.PI * freq) / SampleRate;

                var rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, Mathf.Lerp(0.45f, 0.06f, t));
                var crackle = (rawNoise - prevNoise) * Mathf.Exp(-t * 9f);
                prevNoise = rawNoise;

                var subPunch = (float)Math.Sin(phase) * Mathf.Exp(-t * 6.5f);
                var blast = 0.50f * subPunch + 0.42f * lpNoise * Mathf.Exp(-t * 3.2f) + 0.24f * crackle;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp(blast * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildMechanicalReloadClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var rng = new System.Random(4093);
            var prev = 0f;
            var phase = 0.0;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                var metallic = raw - prev * 0.86f;
                prev = raw;

                phase += (2.0 * Math.PI * (t < 0.45f ? 980f : 1680f)) / SampleRate;
                var latchBurst = Mathf.Exp(-Mathf.Abs(t - 0.14f) * 48f) * 0.65f;
                var boltSlap = Mathf.Exp(-Mathf.Abs(t - 0.68f) * 42f) * 1.0f;
                var ping = (float)Math.Sin(phase) * 0.22f;
                var sample = (metallic * 0.85f + ping) * (latchBurst + boltSlap);
                data[i] = Mathf.Clamp(sample * amplitude, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildShotgunShellInsertClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var rng = new System.Random(5179);
            var prev = 0f;
            var phase = 0.0;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                var scrape = raw - prev * 0.80f;
                prev = raw;

                phase += (2.0 * Math.PI * Mathf.Lerp(920f, 1480f, t)) / SampleRate;
                var slideEnv = Mathf.Exp(-t * 14f) * 0.35f;
                var clickEnv = Mathf.Exp(-Mathf.Abs(t - 0.56f) * 58f) * 1.15f;
                var ping = (float)Math.Sin(phase) * 0.34f;
                data[i] = Mathf.Clamp((scrape * 0.72f + ping) * (slideEnv + clickEnv) * amplitude, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildHitmarkerTickClip(string clipName, float duration, float amplitude, bool doubleTick)
        {
            var sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var rng = new System.Random(doubleTick ? 9123 : 6211);
            var prev = 0f;
            var phase = 0.0;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                var crisp = (raw - prev * 0.72f);
                prev = raw;
                phase += (2.0 * Math.PI * (doubleTick ? 1950f : 1520f)) / SampleRate;

                var env1 = Mathf.Exp(-t * (doubleTick ? 36f : 44f));
                var env2 = doubleTick && t > 0.42f ? Mathf.Exp(-(t - 0.42f) * 42f) * 0.9f : 0f;
                var mix = (crisp * 0.65f + (float)Math.Sin(phase) * 0.45f) * Mathf.Max(env1, env2);
                data[i] = Mathf.Clamp(mix * amplitude, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildTriadClip(string clipName, float f0, float f1, float f2, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            float[] notes = { f0, f1, f2 };
            var noteLen = sampleCount / 3;
            var fadeSamples = Mathf.Min(noteLen / 4, Mathf.RoundToInt(0.005f * SampleRate));

            for (var n = 0; n < 3; n++)
            {
                var start = n * noteLen;
                var end = (n == 2) ? sampleCount : (n + 1) * noteLen;
                var phase = 0.0;
                var freq = notes[n];

                for (var i = start; i < end; i++)
                {
                    var localIdx = i - start;
                    var localLen = end - start;
                    var localT = (float)localIdx / Mathf.Max(1, localLen - 1);
                    phase += (2.0 * Math.PI * freq) / SampleRate;
                    var env = ComputeFadeEnvelope(localIdx, localLen, fadeSamples) * (1f - localT * 0.35f);
                    var wave = 0.8f * (float)Math.Sin(phase) + 0.2f * (float)Math.Sin(phase * 2.0);
                    data[i] = Mathf.Clamp(wave * amplitude * env, -1f, 1f);
                }
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildAmbientBed(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(SampleRate, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var rng = new System.Random(4242);
            var brown = 0f;
            var gustLp = 0f;
            var turbinePhase = 0.0;
            var modPhase = 0.0;

            var turbineHz = Mathf.Round(48f * duration) / duration;
            var modHz = Mathf.Round(0.5f * duration) / duration;

            for (var i = 0; i < sampleCount; i++)
            {
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                brown = (brown + 0.014f * white) / 1.014f;
                gustLp = Mathf.Lerp(gustLp, white, 0.03f);

                turbinePhase += (2.0 * Math.PI * turbineHz) / SampleRate;
                modPhase += (2.0 * Math.PI * modHz) / SampleRate;

                var windSwell = 0.55f + 0.45f * (float)Math.Sin(modPhase);
                var distantHum = 0.18f * (float)Math.Sin(turbinePhase);
                var sample = (brown * 2.8f + gustLp * 0.45f * windSwell + distantHum) * amplitude;
                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            // Crossfade seam so looping never clicks
            var seamSamples = Mathf.RoundToInt(0.08f * SampleRate);
            for (var i = 0; i < seamSamples; i++)
            {
                var blend = (float)i / seamSamples;
                data[i] = Mathf.Lerp(data[sampleCount - 1 - i], data[i], blend);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static float ComputeFadeEnvelope(int index, int totalSamples, int fadeSamples)
        {
            if (fadeSamples <= 0) return 1f;
            var env = 1f;
            if (index < fadeSamples)
            {
                env = Mathf.Min(env, (float)index / fadeSamples);
            }
            var remaining = (totalSamples - 1) - index;
            if (remaining < fadeSamples)
            {
                env = Mathf.Min(env, (float)remaining / fadeSamples);
            }
            return Mathf.Clamp01(env);
        }

        private static AudioClip BuildRadioChirpClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var rng = new System.Random(8841);
            var fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.003f * SampleRate));

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var toneFreq = t < 0.45f ? 1760f : 2120f;
                phase += (2.0 * Math.PI * toneFreq) / SampleRate;
                var squelchGate = (t < 0.34f || t > 0.56f) ? 1f : 0.12f;
                var staticNoise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.22f;
                var beep = (float)Math.Sin(phase) * 0.78f;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * squelchGate;
                data[i] = Mathf.Clamp((beep + staticNoise) * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildRadioSquelchClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var rng = new System.Random(5150);
            var fadeSamples = Mathf.Min(sampleCount / 5, Mathf.RoundToInt(0.004f * SampleRate));
            var prev = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                var band = (white - prev) * 0.7f;
                prev = white;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * (1f - t * 0.55f);
                data[i] = Mathf.Clamp(band * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip CreateClipFromSamples(string clipName, float[] samples)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip BuildStyledGunshotClip(
            string clipName,
            float duration,
            float amplitude,
            float thumpStartHz,
            float thumpEndHz,
            float tailDecay,
            float crackDecay,
            float snapBrightness,
            int seed)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.002f * SampleRate));
            var rng = new System.Random(seed);
            var lpNoise = 0f;
            var prevNoise = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var thumpFreq = Mathf.Lerp(thumpStartHz, thumpEndHz, Mathf.Pow(t, 0.35f));
                phase += (2.0 * Math.PI * thumpFreq) / SampleRate;

                var rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, Mathf.Lerp(0.72f * snapBrightness, 0.10f, t));
                var hpSnap = (rawNoise - prevNoise * 0.65f) * snapBrightness;
                prevNoise = rawNoise;

                var transientCrack = hpSnap * Mathf.Exp(-t * crackDecay);
                var bodyThump = (float)Math.Sin(phase) * Mathf.Exp(-t * (tailDecay * 1.2f));
                var mixed = 0.48f * lpNoise * Mathf.Exp(-t * tailDecay) + 0.36f * transientCrack + 0.40f * bodyThump;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp(mixed * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildLauncherThumpClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.004f * SampleRate));
            var rng = new System.Random(7901);
            var lp = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var tubeFreq = Mathf.Lerp(175f, 54f, Mathf.Pow(t, 0.42f));
                phase += (2.0 * Math.PI * tubeFreq) / SampleRate;
                var raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, raw, Mathf.Lerp(0.18f, 0.34f, t));
                var hollowThump = (float)Math.Sin(phase) * Mathf.Exp(-t * 14f);
                var propellantWhoosh = lp * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * Mathf.Exp(-t * 2.4f);
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp((hollowThump * 0.72f + propellantWhoosh * 0.55f) * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildFootstepClip(string clipName, float duration, float amplitude)
        {
            var sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];
            var phase = 0.0;
            var fadeSamples = Mathf.Min(sampleCount / 5, Mathf.RoundToInt(0.004f * SampleRate));
            var rng = new System.Random(6611);
            var lp = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var bootFreq = Mathf.Lerp(140f, 44f, t);
                phase += (2.0 * Math.PI * bootFreq) / SampleRate;
                var raw = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, raw, 0.16f);
                var heelThud = (float)Math.Sin(phase) * Mathf.Exp(-t * 24f);
                var grit = lp * Mathf.Exp(-t * 18f) * 0.45f;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp((heelThud * 0.75f + grit) * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }
    }
}
