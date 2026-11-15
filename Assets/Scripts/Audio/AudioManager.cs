using System;
using UnityEngine;

namespace TopDownShooter.Audio
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
        private const int SampleRate = 44100;

        [Header("Headless Gain Layer")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.70f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.85f;

        private AudioSource musicSource;
        private AudioSource sfxSource;
        private AudioSource gunSfxSource;

        private AudioClip shootClip;
        private AudioClip hitClip;
        private AudioClip explodeClip;
        private AudioClip pickupBlipClip;
        private AudioClip milestoneStingClip;
        private AudioClip uiClickClip;
        private AudioClip dashClip;
        private AudioClip ambientBedClip;

        private bool isDucked;
        private float currentDuckGain = 1f;

        /// <summary>Normalized Music volume [0..1] for UI sliders and verification checks.</summary>
        public float MusicVol => musicVolume;

        /// <summary>Normalized SFX volume [0..1] for UI sliders and verification checks.</summary>
        public float SFXVol => sfxVolume;

        /// <summary>True when pause -6 dB ducking is currently active.</summary>
        public bool IsDucked => isDucked;

        /// <summary>Effective linear gain currently applied to the ambient music AudioSource.</summary>
        public float CurrentMusicOutputGain => musicSource != null ? musicSource.volume : 0f;

        public AudioClip ShootClip => shootClip;
        public AudioClip HitClip => hitClip;
        public AudioClip ExplodeClip => explodeClip;
        public AudioClip PickupBlipClip => pickupBlipClip;
        public AudioClip MilestoneStingClip => milestoneStingClip;
        public AudioClip UIClickClip => uiClickClip;
        public AudioClip AmbientBedClip => ambientBedClip;

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
            float targetDuck = isDucked ? 0.5f : 1f; // -6.02 dB pause attenuation
            currentDuckGain = Mathf.MoveTowards(currentDuckGain, targetDuck, Time.unscaledDeltaTime * 4f);
            ApplyVolumes();
        }

        private void EnsureAudioSources()
        {
            var sources = GetComponents<AudioSource>();
            musicSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            sfxSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
            gunSfxSource = sources.Length > 2 ? sources[2] : gameObject.AddComponent<AudioSource>();

            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;

            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            gunSfxSource.loop = false;
            gunSfxSource.playOnAwake = false;
            gunSfxSource.spatialBlend = 0f;
            ApplyVolumes();
        }

        private static void EnsureAudioListener()
        {
            if (FindAnyObjectByType<AudioListener>() != null)
            {
                return;
            }
            UnityEngine.Camera mainCam = UnityEngine.Camera.main;
            if (mainCam != null)
            {
                mainCam.gameObject.AddComponent<AudioListener>();
            }
        }

        private void GenerateClips()
        {
            if (shootClip == null) shootClip = BuildGunshotClip("SFX_Shoot", 0.11f, 0.36f);
            if (hitClip == null) hitClip = BuildSweepClip("SFX_Hit", 0.085f, 195f, 52f, 0.38f, false);
            if (explodeClip == null) explodeClip = BuildExplodeClip("SFX_Explode", 0.28f, 0.48f);
            if (pickupBlipClip == null) pickupBlipClip = BuildSweepClip("SFX_PickupBlip", 0.075f, 340f, 640f, 0.26f, true);
            if (milestoneStingClip == null) milestoneStingClip = BuildTriadClip("SFX_Milestone", 220.00f, 277.18f, 329.63f, 0.42f, 0.34f);
            if (uiClickClip == null) uiClickClip = BuildSweepClip("SFX_UIClick", 0.024f, 920f, 440f, 0.22f, false);
            if (dashClip == null) dashClip = BuildFoleyWhooshClip("SFX_Dash", 0.16f, 0.28f);
            if (ambientBedClip == null) ambientBedClip = BuildAmbientBed("BGM_AmbientBed", 6.0f, 0.12f);
        }

        private void StartAmbientBed()
        {
            if (musicSource != null && ambientBedClip != null && !musicSource.isPlaying)
            {
                musicSource.clip = ambientBedClip;
                musicSource.Play();
            }
            ApplyVolumes();
        }

        public void SetMusicVolume(float normalized)
        {
            musicVolume = Mathf.Clamp01(normalized);
            PlayerPrefs.SetFloat(PrefMusicVol, musicVolume);
            ApplyVolumes();
        }

        public void SetSFXVolume(float normalized)
        {
            sfxVolume = Mathf.Clamp01(normalized);
            PlayerPrefs.SetFloat(PrefSfxVol, sfxVolume);
            ApplyVolumes();
        }

        public void SetPausedDucking(bool paused)
        {
            isDucked = paused;
            currentDuckGain = isDucked ? 0.5f : 1f;
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null)
            {
                musicSource.volume = musicVolume * currentDuckGain;
            }
            if (sfxSource != null)
            {
                sfxSource.volume = sfxVolume;
            }
            if (gunSfxSource != null)
            {
                gunSfxSource.volume = sfxVolume;
            }
        }

        public void PlayShoot(float pitchMultiplier = 1f, float volumeScale = 0.55f)
        {
            AudioSource src = gunSfxSource != null ? gunSfxSource : sfxSource;
            if (src == null || shootClip == null || sfxVolume <= 0.001f)
            {
                return;
            }
            src.pitch = Mathf.Clamp(pitchMultiplier * UnityEngine.Random.Range(0.97f, 1.03f), 0.45f, 1.85f);
            src.PlayOneShot(shootClip, volumeScale);
        }

        public void PlayHit()
        {
            PlayOneShotWithPitch(hitClip, UnityEngine.Random.Range(0.92f, 1.08f), 0.70f);
        }

        public void PlayEnemyExplode()
        {
            PlayOneShotWithPitch(explodeClip, UnityEngine.Random.Range(0.90f, 1.10f), 0.85f);
        }

        public void PlayPickupBlip()
        {
            PlayOneShotWithPitch(pickupBlipClip, UnityEngine.Random.Range(0.92f, 1.08f), 0.75f);
        }

        public void PlayMilestoneSting()
        {
            PlayOneShotWithPitch(milestoneStingClip, 1f, 0.90f);
        }

        public void PlayUIClick()
        {
            PlayOneShotWithPitch(uiClickClip, 1f, 0.65f);
        }

        public void PlayDash()
        {
            PlayOneShotWithPitch(dashClip, UnityEngine.Random.Range(0.96f, 1.04f), 0.65f);
        }

        private void PlayOneShotWithPitch(AudioClip clip, float pitch, float scale)
        {
            if (sfxSource == null || clip == null || sfxVolume <= 0.001f)
            {
                return;
            }
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, scale);
        }

        private static AudioClip BuildGunshotClip(string clipName, float duration, float amplitude)
        {
            int sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            double phase = 0.0;
            int fadeSamples = Mathf.Min(sampleCount / 6, Mathf.RoundToInt(0.002f * SampleRate));
            System.Random rng = new System.Random(7091);
            float lpNoise = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                float thumpFreq = Mathf.Lerp(320f, 68f, Mathf.Pow(t, 0.35f));
                phase += (2.0 * Math.PI * thumpFreq) / SampleRate;

                float rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, Mathf.Lerp(0.65f, 0.12f, t));
                float transientCrack = rawNoise * Mathf.Exp(-t * 36f);
                float bodyThump = (float)Math.Sin(phase) * Mathf.Exp(-t * 14f);

                float mixed = 0.52f * lpNoise * Mathf.Exp(-t * 11f) + 0.34f * transientCrack + 0.38f * bodyThump;
                float env = ComputeFadeEnvelope(i, sampleCount, fadeSamples);
                data[i] = Mathf.Clamp(mixed * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildFoleyWhooshClip(string clipName, float duration, float amplitude)
        {
            int sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            int fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.008f * SampleRate));
            System.Random rng = new System.Random(2048);
            float lp = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = Mathf.Lerp(lp, white, Mathf.Lerp(0.08f, 0.24f, t));
                float arch = Mathf.Sin(t * Mathf.PI);
                float env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * arch;
                data[i] = Mathf.Clamp(lp * amplitude * env * 1.4f, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildSweepClip(string clipName, float duration, float startHz, float endHz, float amplitude, bool addHarmonic)
        {
            int sampleCount = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            double phase = 0.0;
            int fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.005f * SampleRate));

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                float freq = Mathf.Lerp(startHz, endHz, t);
                phase += (2.0 * Math.PI * freq) / SampleRate;

                float wave = (float)Math.Sin(phase);
                if (addHarmonic)
                {
                    wave = 0.75f * wave + 0.25f * (float)Math.Sin(phase * 2.0);
                }

                float decay = 1f - t * 0.75f;
                float env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * decay;
                data[i] = Mathf.Clamp(wave * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildExplodeClip(string clipName, float duration, float amplitude)
        {
            int sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            double phase = 0.0;
            int fadeSamples = Mathf.Min(sampleCount / 4, Mathf.RoundToInt(0.005f * SampleRate));
            System.Random rng = new System.Random(1337);
            float lpNoise = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                float freq = Mathf.Lerp(260f, 45f, t);
                phase += (2.0 * Math.PI * freq) / SampleRate;

                float rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, 0.14f);

                float sub = (float)Math.Sin(phase);
                float mixed = 0.55f * sub + 0.45f * lpNoise;
                float env = ComputeFadeEnvelope(i, sampleCount, fadeSamples) * Mathf.Pow(1f - t, 1.3f);
                data[i] = Mathf.Clamp(mixed * amplitude * env, -1f, 1f);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildTriadClip(string clipName, float f0, float f1, float f2, float duration, float amplitude)
        {
            int sampleCount = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            float[] notes = { f0, f1, f2 };
            int noteLen = sampleCount / 3;
            int fadeSamples = Mathf.Min(noteLen / 4, Mathf.RoundToInt(0.005f * SampleRate));

            for (int n = 0; n < 3; n++)
            {
                int start = n * noteLen;
                int end = (n == 2) ? sampleCount : (n + 1) * noteLen;
                double phase = 0.0;
                float freq = notes[n];

                for (int i = start; i < end; i++)
                {
                    int localIdx = i - start;
                    int localLen = end - start;
                    float localT = (float)localIdx / Mathf.Max(1, localLen - 1);
                    phase += (2.0 * Math.PI * freq) / SampleRate;
                    float env = ComputeFadeEnvelope(localIdx, localLen, fadeSamples) * (1f - localT * 0.35f);
                    float wave = 0.8f * (float)Math.Sin(phase) + 0.2f * (float)Math.Sin(phase * 2.0);
                    data[i] = Mathf.Clamp(wave * amplitude * env, -1f, 1f);
                }
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static AudioClip BuildAmbientBed(string clipName, float duration, float amplitude)
        {
            int sampleCount = Mathf.Max(SampleRate, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];
            System.Random rng = new System.Random(4242);
            float brown = 0f;
            double subPhase = 0.0;
            double fifthPhase = 0.0;

            // Integer cycles so loop wraps cleanly at zero crossing with zero click
            float baseFreq = Mathf.Round(55f * duration) / duration;
            float fifthFreq = Mathf.Round(82.5f * duration) / duration;

            for (int i = 0; i < sampleCount; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                brown = (brown + 0.02f * white) / 1.02f;

                subPhase += (2.0 * Math.PI * baseFreq) / SampleRate;
                fifthPhase += (2.0 * Math.PI * fifthFreq) / SampleRate;

                float pad = 0.45f * (float)Math.Sin(subPhase) + 0.25f * (float)Math.Sin(fifthPhase);
                float sample = (brown * 2.6f + pad) * amplitude;
                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            // Crossfade seam so looping never clicks
            int seamSamples = Mathf.RoundToInt(0.05f * SampleRate);
            for (int i = 0; i < seamSamples; i++)
            {
                float blend = (float)i / seamSamples;
                float edgeEnv = ComputeFadeEnvelope(i, sampleCount, seamSamples);
                data[i] *= blend * edgeEnv + (1f - edgeEnv);
                data[sampleCount - 1 - i] *= blend * edgeEnv + (1f - edgeEnv);
            }

            return CreateClipFromSamples(clipName, data);
        }

        private static float ComputeFadeEnvelope(int index, int totalSamples, int fadeSamples)
        {
            if (fadeSamples <= 0) return 1f;
            float env = 1f;
            if (index < fadeSamples)
            {
                env = Mathf.Min(env, (float)index / fadeSamples);
            }
            int remaining = (totalSamples - 1) - index;
            if (remaining < fadeSamples)
            {
                env = Mathf.Min(env, (float)remaining / fadeSamples);
            }
            return Mathf.Clamp01(env);
        }

        private static AudioClip CreateClipFromSamples(string clipName, float[] samples)
        {
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
