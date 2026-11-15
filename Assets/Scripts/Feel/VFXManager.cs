using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Core;

namespace TopDownShooter.Feel
{
    public enum VfxKind
    {
        MuzzleFlash,
        HitSpark,
        EnemyDeath,
        PickupBurst,
        LevelUpNova
    }

    /// <summary>
    /// Manages pooled ParticleSystems across all combat and pickup feedback events.
    /// Zero allocation spikes: once pools warm up, Transform child counts under PoolRoot stay constant.
    /// </summary>
    public sealed class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        [SerializeField] private Transform poolRoot;
        private Material particleMaterial;
        private readonly Dictionary<VfxKind, ObjectPool<ParticleSystem>> pools = new Dictionary<VfxKind, ObjectPool<ParticleSystem>>();
        private readonly Dictionary<VfxKind, float> burstDurations = new Dictionary<VfxKind, float>();
        private ParticleSystem ambientMotes;

        public Transform PoolRoot => poolRoot;

        public void BindPoolRoot(Transform root)
        {
            poolRoot = root;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (poolRoot == null)
            {
                GameObject rootGo = new GameObject("VFXPoolRoot");
                rootGo.transform.SetParent(transform, false);
                poolRoot = rootGo.transform;
            }

            particleMaterial = Palette.CreateParticleMaterial(UISpriteFactory.GetSoftParticleTexture());
            InitializePools();
            EnsureAmbientMotes();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitializePools()
        {
            CreatePool(VfxKind.MuzzleFlash, 8, 24, 0.28f, Palette.Accent, 0.35f, 10, 4.5f);
            CreatePool(VfxKind.HitSpark, 12, 32, 0.35f, Palette.CyanGlow, 0.28f, 12, 6.0f);
            CreatePool(VfxKind.EnemyDeath, 10, 28, 0.55f, Palette.EnemySecondary, 0.45f, 22, 8.0f);
            CreatePool(VfxKind.PickupBurst, 10, 28, 0.42f, Palette.HealthGreen, 0.30f, 14, 5.0f);
            CreatePool(VfxKind.LevelUpNova, 4, 8, 0.72f, Palette.Accent, 0.55f, 36, 9.5f);
        }

        private void CreatePool(
            VfxKind kind,
            int defaultCapacity,
            int maxSize,
            float lifetime,
            Color baseColor,
            float startSize,
            int burstCount,
            float speed)
        {
            burstDurations[kind] = lifetime + 0.1f;

            ObjectPool<ParticleSystem> pool = null;
            pool = new ObjectPool<ParticleSystem>(
                createFunc: () => BuildParticleSystem(kind, lifetime, baseColor, startSize, burstCount, speed),
                actionOnGet: ps => ps.gameObject.SetActive(true),
                actionOnRelease: ps =>
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.gameObject.SetActive(false);
                },
                actionOnDestroy: ps =>
                {
                    if (ps != null) Destroy(ps.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize
            );
            pools[kind] = pool;

            // Pre-warm initial instances so first burst doesn't allocate mid-frame
            List<ParticleSystem> prewarm = new List<ParticleSystem>(defaultCapacity);
            for (int i = 0; i < Mathf.Min(defaultCapacity, 6); i++)
            {
                prewarm.Add(pool.Get());
            }
            for (int i = 0; i < prewarm.Count; i++)
            {
                pool.Release(prewarm[i]);
            }
        }

        private ParticleSystem BuildParticleSystem(
            VfxKind kind,
            float lifetime,
            Color baseColor,
            float startSize,
            int burstCount,
            float speed)
        {
            GameObject go = new GameObject("VFX_" + kind);
            go.transform.SetParent(poolRoot, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = lifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.6f, startSize);
            main.startColor = new ParticleSystem.MinMaxGradient(baseColor, Palette.Lerp(baseColor, Palette.CharacterPrimary, 0.35f));
            main.gravityModifier = 0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = burstCount + 8;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = kind == VfxKind.LevelUpNova ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = kind == VfxKind.LevelUpNova ? 0.55f : 0.16f;
            if (kind == VfxKind.LevelUpNova)
            {
                shape.rotation = new Vector3(90f, 0f, 0f);
            }

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve shrink = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.55f),
                new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, shrink);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            go.SetActive(false);
            return ps;
        }

        private void EnsureAmbientMotes()
        {
            if (ambientMotes != null) return;

            GameObject go = new GameObject("VFX_AmbientMotes");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 2.2f, 0f);
            ambientMotes = go.AddComponent<ParticleSystem>();

            var main = ambientMotes.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Palette.WithAlpha(Palette.CyanGlow, 0.35f),
                Palette.WithAlpha(Palette.Accent, 0.28f));
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ambientMotes.emission;
            emission.rateOverTime = 8f;

            var shape = ambientMotes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 3.5f, 24f);

            var sizeOverLifetime = ambientMotes.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve pulse = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.25f, 1f),
                new Keyframe(0.8f, 0.9f),
                new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, pulse);

            var renderer = ambientMotes.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            ambientMotes.Play();
        }

        public void Play(VfxKind kind, Vector3 worldPos, Color? overrideColor = null)
        {
            if (!pools.TryGetValue(kind, out var pool)) return;

            ParticleSystem ps = pool.Get();
            ps.transform.position = worldPos;
            if (overrideColor.HasValue)
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(overrideColor.Value, Palette.Lerp(overrideColor.Value, Palette.CharacterSecondary, 0.3f));
            }
            ps.Clear(true);
            ps.Play(true);

            StartCoroutine(ReleaseAfterRoutine(pool, ps, burstDurations[kind]));
        }

        private static IEnumerator ReleaseAfterRoutine(ObjectPool<ParticleSystem> pool, ParticleSystem ps, float waitSeconds)
        {
            yield return new WaitForSecondsRealtime(waitSeconds);
            if (ps != null && ps.gameObject.activeSelf)
            {
                pool.Release(ps);
            }
        }
    }
}
