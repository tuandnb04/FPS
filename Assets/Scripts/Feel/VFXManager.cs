using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace Feel
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
    /// Zero GC allocations after pre-warm: ParticleSystem StopAction.Callback returns instances directly to pool.
    /// </summary>
    public sealed class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        private sealed class PooledParticleReturner : MonoBehaviour
        {
            private ObjectPool<ParticleSystem> _pool;
            private ParticleSystem _ps;
            [FormerlySerializedAs("IsLeased")] public bool isLeased;

            public void Bind(ObjectPool<ParticleSystem> pool, ParticleSystem ps)
            {
                _pool = pool;
                _ps = ps;
            }

            private void OnParticleSystemStopped()
            {
                if (!isLeased || _pool == null || _ps == null) return;
                isLeased = false;
                _pool.Release(_ps);
            }
        }

        [SerializeField] private Transform poolRoot;
        private Material _particleMaterial;
        private readonly Dictionary<VfxKind, ObjectPool<ParticleSystem>> _pools = new();
        private readonly Dictionary<ParticleSystem, PooledParticleReturner> _returners = new();
        private ParticleSystem _ambientMotes;

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
                var rootGo = new GameObject("VFXPoolRoot");
                rootGo.transform.SetParent(transform, false);
                poolRoot = rootGo.transform;
            }

            _particleMaterial = Palette.CreateParticleMaterial(UISpriteFactory.GetSoftParticleTexture());
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
            var pool = new ObjectPool<ParticleSystem>(
                createFunc: () =>
                {
                    var ps = BuildParticleSystem(kind, lifetime, baseColor, startSize, burstCount, speed);
                    var ret = ps.gameObject.AddComponent<PooledParticleReturner>();
                    ret.Bind(null, ps);
                    _returners[ps] = ret;
                    return ps;
                },
                actionOnGet: ps => ps.gameObject.SetActive(true),
                actionOnRelease: ps =>
                {
                    if (_returners.TryGetValue(ps, out var ret)) ret.isLeased = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.gameObject.SetActive(false);
                },
                actionOnDestroy: ps =>
                {
                    if (ps == null) return;
                    _returners.Remove(ps);
                    Destroy(ps.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize
            );
            _pools[kind] = pool;

            // Pre-warm initial instances so first burst doesn't allocate mid-frame
            var prewarm = new List<ParticleSystem>(defaultCapacity);
            for (var i = 0; i < Mathf.Min(defaultCapacity, 6); i++)
            {
                prewarm.Add(pool.Get());
            }
            foreach (var t in prewarm)
            {
                pool.Release(t);
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
            var go = new GameObject("VFX_" + kind);
            go.transform.SetParent(poolRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Callback;
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
            var shrink = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.55f),
                new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, shrink);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            go.SetActive(false);
            return ps;
        }

        private void EnsureAmbientMotes()
        {
            if (_ambientMotes != null) return;

            var go = new GameObject("VFX_AmbientMotes");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 2.2f, 0f);
            _ambientMotes = go.AddComponent<ParticleSystem>();

            var main = _ambientMotes.main;
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

            var emission = _ambientMotes.emission;
            emission.rateOverTime = 8f;

            var shape = _ambientMotes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 3.5f, 24f);

            var sizeOverLifetime = _ambientMotes.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var pulse = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.25f, 1f),
                new Keyframe(0.8f, 0.9f),
                new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, pulse);

            var renderer = _ambientMotes.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            _ambientMotes.Play();
        }

        public void Play(VfxKind kind, Vector3 worldPos, Color? overrideColor = null)
        {
            if (!_pools.TryGetValue(kind, out var pool)) return;

            var ps = pool.Get();
            ps.transform.position = worldPos;
            if (overrideColor.HasValue)
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(overrideColor.Value, Palette.Lerp(overrideColor.Value, Palette.CharacterSecondary, 0.3f));
            }
            if (_returners.TryGetValue(ps, out var ret))
            {
                ret.isLeased = true;
            }
            ps.Clear(true);
            ps.Play(true);
        }
    }
}
