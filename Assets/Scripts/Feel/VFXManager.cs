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
        LevelUpNova,
        RocketSmoke
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

            private void Update()
            {
                if (!isLeased || _pool == null || _ps == null) return;
                if (!_ps.IsAlive(true))
                {
                    isLeased = false;
                    _pool.Release(_ps);
                }
            }

            private void OnParticleSystemStopped()
            {
                ForceReturnToPool();
            }

            public void ForceReturnToPool()
            {
                if (!isLeased || _pool == null || _ps == null) return;
                isLeased = false;
                _pool.Release(_ps);
            }
        }

        private struct ScorchEntry
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public float Elapsed;
            public float Duration;
            public float BaseScale;
        }

        private struct ShockwaveEntry
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public float Elapsed;
            public float Duration;
            public float MaxScale;
            public Color BaseColor;
        }

        [SerializeField] private Transform poolRoot;
        private Material _particleMaterial;
        private Material _scorchMaterial;
        private Material _shockwaveMaterial;
        private readonly Dictionary<VfxKind, ObjectPool<ParticleSystem>> _pools = new();
        private readonly Dictionary<ParticleSystem, PooledParticleReturner> _returners = new();
        private readonly List<ParticleSystem> _activeLeased = new(64);
        private readonly List<ScorchEntry> _scorchDecals = new(24);
        private readonly List<ShockwaveEntry> _shockwaveRings = new(12);
        private readonly Stack<ScorchEntry> _scorchPool = new(24);
        private readonly Stack<ShockwaveEntry> _shockwavePool = new(12);
        private MaterialPropertyBlock _decalMpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
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
            _scorchMaterial = Palette.CreateParticleMaterial("Mat_ScorchDecalRuntime", UISpriteFactory.GetSoftParticleTexture());
            _shockwaveMaterial = Palette.CreateParticleMaterial("Mat_ShockwaveRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            InitializePools();
            EnsureAmbientMotes();
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            _decalMpb ??= new MaterialPropertyBlock();

            for (var i = _scorchDecals.Count - 1; i >= 0; i--)
            {
                var entry = _scorchDecals[i];
                if (entry.Go == null)
                {
                    _scorchDecals.RemoveAt(i);
                    continue;
                }
                entry.Elapsed += dt;
                if (entry.Elapsed >= entry.Duration)
                {
                    RecycleScorchAt(i);
                    continue;
                }
                var t = entry.Elapsed / entry.Duration;
                var alpha = t < 0.72f ? 0.68f : Mathf.Lerp(0.68f, 0f, (t - 0.72f) / 0.28f);
                if (entry.Renderer != null)
                {
                    entry.Renderer.GetPropertyBlock(_decalMpb);
                    _decalMpb.SetColor(ColorId, new Color(0.05f, 0.05f, 0.06f, alpha));
                    entry.Renderer.SetPropertyBlock(_decalMpb);
                }
                _scorchDecals[i] = entry;
            }

            for (var i = _shockwaveRings.Count - 1; i >= 0; i--)
            {
                var ring = _shockwaveRings[i];
                if (ring.Go == null)
                {
                    _shockwaveRings.RemoveAt(i);
                    continue;
                }
                ring.Elapsed += dt;
                if (ring.Elapsed >= ring.Duration)
                {
                    RecycleShockwaveAt(i);
                    continue;
                }
                var t = Mathf.Clamp01(ring.Elapsed / ring.Duration);
                var easeOut = 1f - (1f - t) * (1f - t);
                var s = Mathf.Lerp(0.45f, ring.MaxScale, easeOut);
                ring.Go.transform.localScale = new Vector3(s, s, 1f);
                if (ring.Renderer != null)
                {
                    var col = ring.BaseColor;
                    col.a = Mathf.Lerp(0.92f, 0f, t);
                    ring.Renderer.GetPropertyBlock(_decalMpb);
                    _decalMpb.SetColor(ColorId, col);
                    ring.Renderer.SetPropertyBlock(_decalMpb);
                }
                _shockwaveRings[i] = ring;
            }
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
            CreatePool(VfxKind.MuzzleFlash, 8, 24, 0.16f, Palette.Accent, 0.22f, 8, 3.6f);
            CreatePool(VfxKind.HitSpark, 12, 32, 0.35f, Palette.CyanGlow, 0.28f, 12, 6.0f);
            CreatePool(VfxKind.EnemyDeath, 10, 28, 0.55f, Palette.EnemySecondary, 0.45f, 22, 8.0f);
            CreatePool(VfxKind.PickupBurst, 10, 28, 0.42f, Palette.HealthGreen, 0.30f, 14, 5.0f);
            CreatePool(VfxKind.LevelUpNova, 4, 8, 0.72f, Palette.Accent, 0.55f, 36, 9.5f);
            CreatePool(VfxKind.RocketSmoke, 14, 48, 0.38f, new Color(0.76f, 0.78f, 0.82f, 0.82f), 0.34f, 4, 1.35f);
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
            ObjectPool<ParticleSystem> pool = null;
            pool = new ObjectPool<ParticleSystem>(
                createFunc: () =>
                {
                    var ps = BuildParticleSystem(kind, lifetime, baseColor, startSize, burstCount, speed);
                    var ret = ps.gameObject.AddComponent<PooledParticleReturner>();
                    ret.Bind(pool, ps);
                    _returners[ps] = ret;
                    return ps;
                },
                actionOnGet: ps => ps.gameObject.SetActive(true),
                actionOnRelease: ps =>
                {
                    if (_returners.TryGetValue(ps, out var ret)) ret.isLeased = false;
                    _activeLeased.Remove(ps);
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
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.duration = lifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.6f, startSize);
            main.startColor = new ParticleSystem.MinMaxGradient(baseColor, Palette.Lerp(baseColor, Palette.CharacterPrimary, 0.35f));
            main.gravityModifier = kind == VfxKind.RocketSmoke ? -0.16f : 0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = burstCount + 8;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = kind == VfxKind.LevelUpNova ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = kind == VfxKind.LevelUpNova ? 0.55f : (kind == VfxKind.MuzzleFlash ? 0.035f : (kind == VfxKind.RocketSmoke ? 0.08f : 0.16f));
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
            if (!_activeLeased.Contains(ps))
            {
                _activeLeased.Add(ps);
            }
            ps.Clear(true);
            ps.Play(true);
        }

        private (GameObject go, MeshRenderer renderer) CreateQuadRenderer(string quadName, Material sharedMat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = quadName;
            quad.transform.SetParent(poolRoot, false);
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = sharedMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return (quad, mr);
        }

        private void RecycleScorchAt(int idx)
        {
            var entry = _scorchDecals[idx];
            _scorchDecals.RemoveAt(idx);
            if (entry.Go == null) return;
            entry.Go.SetActive(false);
            _scorchPool.Push(entry);
        }

        private void RecycleShockwaveAt(int idx)
        {
            var entry = _shockwaveRings[idx];
            _shockwaveRings.RemoveAt(idx);
            if (entry.Go == null) return;
            entry.Go.SetActive(false);
            _shockwavePool.Push(entry);
        }

        public void SpawnScorchMark(Vector3 worldPos, float radius = 2.2f)
        {
            if (poolRoot == null) return;
            var r = Mathf.Clamp(radius, 0.9f, 5.2f);
            if (_scorchDecals.Count >= 22)
            {
                RecycleScorchAt(0);
            }

            ScorchEntry entry;
            if (_scorchPool.Count > 0)
            {
                entry = _scorchPool.Pop();
                if (entry.Go == null)
                {
                    var created = CreateQuadRenderer("FX_ScorchDecal", _scorchMaterial);
                    entry.Go = created.go;
                    entry.Renderer = created.renderer;
                }
                entry.Go.SetActive(true);
            }
            else
            {
                var created = CreateQuadRenderer("FX_ScorchDecal", _scorchMaterial);
                entry = new ScorchEntry { Go = created.go, Renderer = created.renderer };
            }

            entry.Go.transform.position = new Vector3(worldPos.x, 0.022f, worldPos.z);
            entry.Go.transform.rotation = Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f);
            entry.Go.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
            entry.Elapsed = 0f;
            entry.Duration = 11.5f;
            entry.BaseScale = r * 2f;

            if (entry.Renderer != null)
            {
                _decalMpb ??= new MaterialPropertyBlock();
                _decalMpb.SetColor(ColorId, new Color(0.05f, 0.05f, 0.06f, 0.68f));
                entry.Renderer.SetPropertyBlock(_decalMpb);
            }
            _scorchDecals.Add(entry);

            SpawnShockwaveRing(worldPos, r * 2.5f, Palette.C4Amber);
        }

        public void SpawnShockwaveRing(Vector3 worldPos, float maxDiameter, Color ringColor)
        {
            if (poolRoot == null) return;
            if (_shockwaveRings.Count >= 10)
            {
                RecycleShockwaveAt(0);
            }

            ShockwaveEntry entry;
            if (_shockwavePool.Count > 0)
            {
                entry = _shockwavePool.Pop();
                if (entry.Go == null)
                {
                    var created = CreateQuadRenderer("FX_ShockwaveRing", _shockwaveMaterial);
                    entry.Go = created.go;
                    entry.Renderer = created.renderer;
                }
                entry.Go.SetActive(true);
            }
            else
            {
                var created = CreateQuadRenderer("FX_ShockwaveRing", _shockwaveMaterial);
                entry = new ShockwaveEntry { Go = created.go, Renderer = created.renderer };
            }

            entry.Go.transform.position = new Vector3(worldPos.x, 0.055f, worldPos.z);
            entry.Go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            entry.Go.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
            entry.Elapsed = 0f;
            entry.Duration = 0.24f;
            entry.MaxScale = Mathf.Max(1.8f, maxDiameter);
            entry.BaseColor = ringColor;

            if (entry.Renderer != null)
            {
                _decalMpb ??= new MaterialPropertyBlock();
                _decalMpb.SetColor(ColorId, ringColor);
                entry.Renderer.SetPropertyBlock(_decalMpb);
            }
            _shockwaveRings.Add(entry);
        }

        public void ClearAllActiveParticles()
        {
            for (var i = _activeLeased.Count - 1; i >= 0; i--)
            {
                var ps = _activeLeased[i];
                if (ps != null && _returners.TryGetValue(ps, out var ret) && ret != null)
                {
                    ret.ForceReturnToPool();
                }
            }
            _activeLeased.Clear();

            for (var i = _scorchDecals.Count - 1; i >= 0; i--)
            {
                RecycleScorchAt(i);
            }

            for (var i = _shockwaveRings.Count - 1; i >= 0; i--)
            {
                RecycleShockwaveAt(i);
            }
        }
    }
}
