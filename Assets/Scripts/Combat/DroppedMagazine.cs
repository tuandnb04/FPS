using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.Pool;

namespace Combat
{
    /// <summary>
    /// Pooled physical empty magazine ejected onto the arena floor whenever the operator
    /// or a Vanguard-09 squadmate executes a tactical reload.
    /// </summary>
    public sealed class DroppedMagazine : MonoBehaviour
    {
        private static readonly List<DroppedMagazine> ActiveInstances = new(64);
        private static ObjectPool<DroppedMagazine> _pool;
        private static Transform _poolParent;
        private static Material _defaultMat;
        private static Material _brassCasingMat;
        private static Material _shotgunHullMat;
        private static Mesh _casingCylinderMesh;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Vector3 _velocity;
        private Vector3 _angularVel;
        private float _lifeTimer;
        private float _totalLifetime;
        private int _bouncesRemaining;
        private bool _settled;
        private Vector3 _baseScale = Vector3.one;

        public static int ActiveCount => ActiveInstances.Count;
        public static int TotalShellCasingsSpawned { get; private set; }

        public static DroppedMagazine Spawn(
            Mesh magMesh,
            Material magMaterial,
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 ejectionVel,
            Vector3? customScale = null)
        {
            EnsurePool();

            DroppedMagazine instance = null;
            for (var guard = 0; guard < 12; guard++)
            {
                instance = _pool.Get();
                if (instance != null) break;
            }
            if (instance == null) return null;

            instance.Activate(magMesh, magMaterial, worldPos, worldRot, ejectionVel, customScale);
            if (!ActiveInstances.Contains(instance))
            {
                ActiveInstances.Add(instance);
            }
            return instance;
        }

        public static DroppedMagazine SpawnFromVisual(
            Transform magVisual,
            Mesh fallbackMesh,
            Material fallbackMat,
            Vector3 fallbackPos,
            Quaternion fallbackRot,
            Vector3 ejectionVel)
        {
            var mesh = fallbackMesh;
            var mat = fallbackMat;
            var spawnPos = fallbackPos;
            var spawnRot = fallbackRot;
            Vector3? spawnScale = null;

            if (magVisual != null)
            {
                var mf = magVisual.GetComponentInChildren<MeshFilter>(true);
                var mr = magVisual.GetComponentInChildren<MeshRenderer>(true);
                if (mf != null && mf.sharedMesh != null) mesh = mf.sharedMesh;
                if (mr != null && mr.sharedMaterial != null) mat = mr.sharedMaterial;
                spawnPos = magVisual.position;
                spawnRot = magVisual.rotation;
                spawnScale = magVisual.lossyScale;
            }

            return Spawn(mesh, mat, spawnPos, spawnRot, ejectionVel, spawnScale);
        }

        public static void SpawnShellCasing(
            Vector3 worldPos,
            Vector3 ejectRightDir,
            bool isShotgunShell = false,
            Mesh customMesh = null,
            Material customMat = null)
        {
            if (!Application.isPlaying) return;
            EnsurePool();

            if (ActiveInstances.Count >= 64 && ActiveInstances[0] != null)
            {
                ActiveInstances[0].ReturnToPool();
            }

            DroppedMagazine instance = null;
            for (var guard = 0; guard < 8; guard++)
            {
                instance = _pool.Get();
                if (instance != null) break;
            }
            if (instance == null) return;

            TotalShellCasingsSpawned++;
            instance.ActivateShellCasing(worldPos, ejectRightDir, isShotgunShell, customMesh, customMat);
            if (!ActiveInstances.Contains(instance))
            {
                ActiveInstances.Add(instance);
            }
        }

        public static void ReturnAllActive()
        {
            for (var i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                var inst = ActiveInstances[i];
                if (inst == null)
                {
                    ActiveInstances.RemoveAt(i);
                    continue;
                }
                inst.ReturnToPool();
            }
            ActiveInstances.Clear();
        }

        private static void EnsurePool()
        {
            if (_poolParent != null && _pool != null) return;

            ActiveInstances.Clear();
            var root = new GameObject("__DroppedMagazinePool");
            _poolParent = root.transform;
            if (_defaultMat == null)
            {
                _defaultMat = Palette.CreateLitMaterial("Mat_DroppedMagRuntime", Palette.Gunmetal, 0.72f, 0.50f, false, 0f);
            }
            if (_brassCasingMat == null)
            {
                _brassCasingMat = Palette.CreateLitMaterial("Mat_BrassCasingRuntime", Palette.CreditGold, 0.85f, 0.72f, false, 0f);
            }
            if (_shotgunHullMat == null)
            {
                _shotgunHullMat = Palette.CreateLitMaterial("Mat_ShotgunHullRuntime", new Color(0.86f, 0.20f, 0.16f, 1f), 0.18f, 0.48f, false, 0f);
            }

            _pool = new ObjectPool<DroppedMagazine>(
                createFunc: CreateInstance,
                actionOnGet: m =>
                {
                    if (m != null) m.gameObject.SetActive(true);
                },
                actionOnRelease: m =>
                {
                    if (m != null) m.gameObject.SetActive(false);
                },
                actionOnDestroy: m =>
                {
                    if (m != null) Destroy(m.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 96);
        }

        private static DroppedMagazine CreateInstance()
        {
            var go = new GameObject("DroppedMagazine_Prop");
            go.transform.SetParent(_poolParent, false);
            try { go.tag = "Magazine"; } catch (UnityException) { }
            var propLayer = LayerMask.NameToLayer("WeaponProp");
            if (propLayer >= 0) go.layer = propLayer;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _defaultMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            var mag = go.AddComponent<DroppedMagazine>();
            mag._meshFilter = mf;
            mag._meshRenderer = mr;
            go.SetActive(false);
            return mag;
        }

        private static Mesh _fallbackBoxMesh;

        private static Mesh GetFallbackMagMesh()
        {
            if (_fallbackBoxMesh != null) return _fallbackBoxMesh;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _fallbackBoxMesh = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            _fallbackBoxMesh.name = "FallbackMagMesh";
            Destroy(cube);
            return _fallbackBoxMesh;
        }

        private static Mesh GetCasingMesh()
        {
            if (_casingCylinderMesh != null) return _casingCylinderMesh;
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _casingCylinderMesh = Instantiate(cyl.GetComponent<MeshFilter>().sharedMesh);
            _casingCylinderMesh.name = "ShellCasingMesh";
            Destroy(cyl);
            return _casingCylinderMesh;
        }

        private void ActivateShellCasing(
            Vector3 worldPos,
            Vector3 ejectRightDir,
            bool isShotgunShell = false,
            Mesh customMesh = null,
            Material customMat = null)
        {
            var right = new Vector3(ejectRightDir.x, 0f, ejectRightDir.z);
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            var back = Vector3.Cross(Vector3.up, right);

            transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(right, Vector3.up));
            if (_meshFilter != null) _meshFilter.sharedMesh = (isShotgunShell && customMesh != null) ? customMesh : GetCasingMesh();
            if (_meshRenderer != null)
            {
                _meshRenderer.sharedMaterial = isShotgunShell
                    ? (customMat != null ? customMat : (_shotgunHullMat != null ? _shotgunHullMat : _defaultMat))
                    : (_brassCasingMat != null ? _brassCasingMat : _defaultMat);
            }

            _baseScale = isShotgunShell
                ? (customMesh != null ? Vector3.one * 0.62f : new Vector3(0.032f, 0.048f, 0.032f))
                : new Vector3(0.022f, 0.036f, 0.022f);
            transform.localScale = _baseScale;
            _velocity = right * UnityEngine.Random.Range(isShotgunShell ? 1.6f : 1.85f, isShotgunShell ? 2.4f : 2.75f)
                        + Vector3.up * UnityEngine.Random.Range(1.45f, 2.25f)
                        + back * UnityEngine.Random.Range(-0.45f, 0.35f);
            _angularVel = new Vector3(
                UnityEngine.Random.Range(620f, 980f),
                UnityEngine.Random.Range(-480f, 480f),
                UnityEngine.Random.Range(-640f, 640f));
            _bouncesRemaining = isShotgunShell ? 2 : 1;
            _settled = false;
            _totalLifetime = isShotgunShell ? 2.2f : 0.95f;
            _lifeTimer = _totalLifetime;
        }

        private void Activate(
            Mesh magMesh,
            Material magMaterial,
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 ejectionVel,
            Vector3? customScale = null)
        {
            transform.SetPositionAndRotation(worldPos, worldRot);
            if (_meshFilter != null)
            {
                if (magMesh != null)
                {
                    _meshFilter.sharedMesh = magMesh;
                    _baseScale = customScale ?? (Vector3.one * 0.68f);
                }
                else
                {
                    _meshFilter.sharedMesh = GetFallbackMagMesh();
                    _baseScale = new Vector3(0.06f, 0.16f, 0.10f);
                }
            }
            if (_meshRenderer != null)
            {
                _meshRenderer.sharedMaterial = magMaterial != null ? magMaterial : _defaultMat;
            }

            transform.localScale = _baseScale;
            _velocity = ejectionVel;
            _angularVel = new Vector3(
                UnityEngine.Random.Range(320f, 540f),
                UnityEngine.Random.Range(-240f, 240f),
                UnityEngine.Random.Range(-360f, 360f));
            _bouncesRemaining = 2;
            _settled = false;
            _totalLifetime = 3.6f;
            _lifeTimer = _totalLifetime;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            _lifeTimer -= dt;
            if (_lifeTimer <= 0f)
            {
                ReturnToPool();
                return;
            }

            if (!_settled)
            {
                _velocity.y -= 17.5f * dt;
                var nextPos = transform.position + _velocity * dt;
                const float floorY = 0.036f;

                if (nextPos.y <= floorY)
                {
                    nextPos.y = floorY;
                    if (_bouncesRemaining > 0 && Mathf.Abs(_velocity.y) > 0.65f)
                    {
                        _velocity.y = Mathf.Abs(_velocity.y) * 0.34f;
                        _velocity.x *= 0.52f;
                        _velocity.z *= 0.52f;
                        _angularVel *= 0.45f;
                        _bouncesRemaining--;
                    }
                    else
                    {
                        _velocity = Vector3.zero;
                        _angularVel = Vector3.zero;
                        _settled = true;
                        var euler = transform.eulerAngles;
                        transform.rotation = Quaternion.Euler(90f, euler.y, 0f);
                    }
                }
                transform.position = nextPos;
                if (!_settled)
                {
                    transform.Rotate(_angularVel * dt, Space.Self);
                }
            }

            if (_lifeTimer < 0.40f)
            {
                var shrink = Mathf.Clamp01(_lifeTimer / 0.40f);
                transform.localScale = _baseScale * shrink;
            }
        }

        private void ReturnToPool()
        {
            ActiveInstances.Remove(this);
            if (_pool != null && gameObject != null)
            {
                _pool.Release(this);
            }
            else if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }
    }
}
