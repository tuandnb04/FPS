using System;
using UnityEngine;

namespace Audio
{
    /// <summary>
    /// Procedural PCM audio synthesizer and headless gain-layer mixer controller.
    /// Avoids silent gameplay by generating click-free waveforms with 5ms attack/release envelopes.
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

        private AudioClip _dashClip;
        private AudioClip _hitmarkerClip;
        private AudioClip _hitmarkerKillClip;
        private AudioClip _reloadClip;
        private AudioClip _nvgOnClip;
        private AudioClip _nvgOffClip;

        private float _currentDuckGain = 1f;
        private float _lastEnemyShootTime = -10f;
        private float _lastHitmarkerTime = -10f;

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
            _currentDuckGain = Mathf.MoveTowards(_currentDuckGain, targetDuck, Time.unscaledDeltaTime * 4f);
            ApplyVolumes();
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
            ApplyVolumes();
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
            if (HitClip == null) HitClip = BuildSweepClip("SFX_Hit", 0.085f, 195f, 52f, 0.38f, false);
            if (ExplodeClip == null) ExplodeClip = BuildExplodeClip("SFX_Explode", 0.28f, 0.48f);
            if (PickupBlipClip == null) PickupBlipClip = BuildSweepClip("SFX_PickupBlip", 0.075f, 340f, 640f, 0.26f, true);
            if (MilestoneStingClip == null) MilestoneStingClip = BuildTriadClip("SFX_Milestone", 220.00f, 277.18f, 329.63f, 0.42f, 0.34f);
            if (UIClickClip == null) UIClickClip = BuildSweepClip("SFX_UIClick", 0.024f, 920f, 440f, 0.22f, false);
            if (_dashClip == null) _dashClip = BuildFoleyWhooshClip("SFX_Dash", 0.16f, 0.28f);
            if (_hitmarkerClip == null) _hitmarkerClip = BuildSweepClip("SFX_Hitmarker", 0.028f, 2900f, 1150f, 0.26f, false);
            if (_hitmarkerKillClip == null) _hitmarkerKillClip = BuildTriadClip("SFX_HitmarkerKill", 2200f, 1450f, 3100f, 0.055f, 0.32f);
            if (_reloadClip == null) _reloadClip = BuildTriadClip("SFX_Reload", 420f, 260f, 540f, 0.18f, 0.24f);
            if (_nvgOnClip == null) _nvgOnClip = BuildSweepClip("SFX_NvgOn", 0.22f, 1550f, 4150f, 0.24f, true);
            if (_nvgOffClip == null) _nvgOffClip = BuildSweepClip("SFX_NvgOff", 0.16f, 2800f, 650f, 0.20f, false);
            if (RadioChirpClip == null) RadioChirpClip = BuildRadioChirpClip("SFX_RadioChirp", 0.11f, 0.24f);
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
            var fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.005f * SampleRate));
            var rng = new System.Random(1337);
            var lpNoise = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1);
                var freq = Mathf.Lerp(260f, 45f, t);
                phase += (2.0 * Math.PI * freq) / SampleRate;

                var rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, 0.14f);

                var sub = (float)Math.Sin(phase);
                var mixed = 0.55f * sub + 0.45f * lpNoise;
                var env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * Mathf.Pow(1f - t, 1.3f);
                data[i] = Mathf.Clamp(mixed * amplitude * env, -1f, 1f);
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
            var subPhase = 0.0;
            var fifthPhase = 0.0;

            // Integer cycles so loop wraps cleanly at zero crossing with zero click
            var baseFreq = Mathf.Round(55f * duration) / duration;
            var fifthFreq = Mathf.Round(82.5f * duration) / duration;

            for (var i = 0; i < sampleCount; i++)
            {
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                brown = (brown + 0.02f * white) / 1.02f;

                subPhase += (2.0 * Math.PI * baseFreq) / SampleRate;
                fifthPhase += (2.0 * Math.PI * fifthFreq) / SampleRate;

                var pad = 0.45f * (float)Math.Sin(subPhase) + 0.25f * (float)Math.Sin(fifthPhase);
                var sample = (brown * 2.6f + pad) * amplitude;
                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            // Crossfade seam so looping never clicks
            var seamSamples = Mathf.RoundToInt(0.05f * SampleRate);
            for (var i = 0; i < seamSamples; i++)
            {
                var blend = (float)i / seamSamples;
                var edgeEnv = ComputeFadeEnvelope(i, sampleCount, seamSamples);
                data[i] *= blend * edgeEnv + (1f - edgeEnv);
                data[sampleCount - 1 - i] *= blend * edgeEnv + (1f - edgeEnv);
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

        private static AudioClip CreateClipFromSamples(string clipName, float[] samples)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
