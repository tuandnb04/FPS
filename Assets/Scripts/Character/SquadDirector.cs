using System.Collections.Generic;
using System.Linq;
using Audio;
using Combat;
using Core;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace Character
{
    /// <summary>
    /// Coordinates Spectre Detachment Vanguard-09 (MAJ. ROURKE, SPEC. KOZLOWSKI, CPL. REYES),
    /// tactical squad orders ([Z] Follow, [X] Hold, [C] Breach), pooled friendly ballistic tracers,
    /// and cutscene squad staging across all three campaign operations.
    /// </summary>
    public sealed class SquadDirector : MonoBehaviour
    {
        public static SquadDirector Instance { get; private set; }

        [Header("Blender Squad Meshes (Bound by SceneSetupBuilder)")]
        [SerializeField] private Mesh torsoMesh;
        [SerializeField] private Mesh chestRigMesh;
        [SerializeField] private Mesh helmetMesh;
        [SerializeField] private Mesh skullMaskMesh;
        [SerializeField] private Mesh boonieHatMesh;
        [SerializeField] private Mesh visorMesh;
        [SerializeField] private Mesh upperArmMesh;
        [SerializeField] private Mesh forearmMesh;
        [SerializeField] private Mesh limbMesh;
        [SerializeField] private Mesh backpackMesh;
        [SerializeField] private Mesh rifleM4Mesh;
        [FormerlySerializedAs("rifleMK14Mesh")] [SerializeField] private Mesh rifleMk14Mesh;
        [FormerlySerializedAs("rifleAN94Mesh")] [SerializeField] private Mesh rifleAn94Mesh;

        private readonly List<SquadMemberController> _members = new(3);
        private ObjectPool<Projectile> _squadBulletPool;
        private Transform _squadRoot;
        private Transform _bulletPoolRoot;
        private Material _squadBulletMat;
        private Material _weaponSteelMat;
        private Material _nvgLensMat;

        public SquadOrder CurrentOrder { get; private set; } = SquadOrder.Follow;

        public IReadOnlyList<SquadMemberController> Members => _members;
        public int MemberCount => _members.Count;

        public int StandingCount
        {
            get
            {
                return _members.Count(t => t != null && !t.IsDowned && !t.IsKia);
            }
        }

        public int AliveRosterCount
        {
            get
            {
                return _members.Count(t => t != null && !t.IsKia);
            }
        }

        public int DownedCount
        {
            get
            {
                return _members.Count(t => t != null && t.IsDowned && !t.IsKia);
            }
        }

        public void BindMeshes(
            Mesh torso, Mesh chestRig, Mesh helmet, Mesh boonieHat, Mesh visor,
            Mesh upperArm, Mesh forearm, Mesh limb, Mesh backpack,
            Mesh m4, Mesh mk14, Mesh an94,
            Mesh skullMask = null)
        {
            torsoMesh = torso;
            chestRigMesh = chestRig;
            helmetMesh = helmet;
            skullMaskMesh = skullMask;
            boonieHatMesh = boonieHat;
            visorMesh = visor;
            upperArmMesh = upperArm;
            forearmMesh = forearm;
            limbMesh = limb;
            backpackMesh = backpack;
            rifleM4Mesh = m4;
            rifleMk14Mesh = mk14;
            rifleAn94Mesh = an94;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _squadRoot = new GameObject("Bravo6_SquadRoot").transform;
            _squadRoot.SetParent(transform, false);

            _bulletPoolRoot = new GameObject("SquadBulletPoolRoot").transform;
            _bulletPoolRoot.SetParent(transform, false);

            _squadBulletMat = Palette.CreateLitMaterial("Mat_SquadTracerRuntime", Palette.RailgunCyan, 0.1f, 0.25f, true, 1.4f);
            _weaponSteelMat = Palette.CreateLitMaterial("Mat_SquadWeaponSteelRuntime", Palette.Gunmetal, 0.78f, 0.56f, false, 0f);
            _nvgLensMat = Palette.CreateLitMaterial("Mat_SquadNvgRuntime", Palette.NvgGreen, 0.25f, 0.68f, false, 0f);

            _squadBulletPool = new ObjectPool<Projectile>(
                createFunc: CreateSquadBulletInstance,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false,
                defaultCapacity: 24,
                maxSize: 64
            );
        }

        private void Start()
        {
            EnsureSquadSpawned();
            ResetSquadForMission();
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
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            if (Keyboard.current == null) return;
            if (Keyboard.current.zKey.wasPressedThisFrame) SetOrder(SquadOrder.Follow);
            else if (Keyboard.current.xKey.wasPressedThisFrame) SetOrder(SquadOrder.Hold);
            else if (Keyboard.current.cKey.wasPressedThisFrame) SetOrder(SquadOrder.Breach);
        }

        public void SetOrder(SquadOrder order, bool quiet = false)
        {
            CurrentOrder = order;
            foreach (var t in _members.Where(t => t != null && !t.IsKia))
            {
                if (order == SquadOrder.Hold) t.CaptureHoldAnchor();
                else t.ClearHoldAnchor();
            }

            UIManager.Instance?.UpdateSquadOrderUI(order);

            if (quiet || AliveRosterCount <= 0) return;
            AudioManager.Instance?.PlayUIClick();
            var msg = order switch
            {
                SquadOrder.Follow => "Closing diamond formation on Lead.",
                SquadOrder.Hold => "Locking perimeter firing sectors.",
                _ => "Stacking up — weapons free ahead."
            };
            StoryDirector.NotifySquadOrder(msg);
        }

        public bool TryPatchNearestDownedMember(Vector3 origin, float radius)
        {
            var rSq = radius * radius;
            SquadMemberController best = null;
            var bestDist = float.MaxValue;
            foreach (var m in _members)
            {
                if (m == null || m.IsKia || !m.IsDowned) continue;
                var d = m.transform.position - origin;
                d.y = 0f;
                if (d.sqrMagnitude <= rSq && d.sqrMagnitude < bestDist)
                {
                    bestDist = d.sqrMagnitude;
                    best = m;
                }
            }
            return best != null && best.ApplyEmergencyTraumaPatch();
        }

        public void MarkMemberKiaByTag(string roleTag, bool broadcastRadio = true)
        {
            foreach (var m in _members)
            {
                if (m != null && m.RoleTag == roleTag)
                {
                    m.MarkKilledInAction(broadcastRadio);
                }
            }
        }

        public void EnsureSquadSpawned()
        {
            if (_members.Count == 3) return;

            for (var i = _members.Count - 1; i >= 0; i--)
            {
                if (_members[i] != null) Destroy(_members[i].gameObject);
            }
            _members.Clear();

            var wraithHead = skullMaskMesh != null ? skullMaskMesh : helmetMesh;
            _members.Add(BuildSquadMemberRig(SquadRole.SquadLeadHayes, Palette.CharacterPrimary, Palette.CoyoteTan, boonieHatMesh != null ? boonieHatMesh : helmetMesh, useVisor: false, rifleM4Mesh, 0.65f, new Vector3(0f, -0.028f, 0.165f), new Vector3(0f, -0.060f, -0.033f), new Vector3(0f, 0.020f, 0.555f), Palette.CharacterPrimary));
            _members.Add(BuildSquadMemberRig(SquadRole.MarksmanBishop, Palette.SkinCrimsonPrimary, Palette.GuardTowerDark, wraithHead, useVisor: skullMaskMesh == null, rifleMk14Mesh, 0.66f, new Vector3(0f, -0.010f, 0.195f), new Vector3(0f, -0.058f, -0.052f), new Vector3(0f, 0.020f, 0.690f), Palette.SkullMaskBone));
            _members.Add(BuildSquadMemberRig(SquadRole.BreacherTorres, Palette.SkinGoldPrimary, Palette.CoyoteTan, helmetMesh, useVisor: true, rifleAn94Mesh, 0.66f, new Vector3(0f, -0.024f, 0.180f), new Vector3(0f, -0.060f, -0.038f), new Vector3(0f, 0.017f, 0.598f), Palette.SkinGoldPrimary));
        }

        public void ResetSquadForMission()
        {
            EnsureSquadSpawned();
            CurrentOrder = SquadOrder.Follow;
            UIManager.Instance?.UpdateSquadOrderUI(CurrentOrder);
            var leadPos = PlayerController.Instance != null ? PlayerController.Instance.transform.position : new Vector3(0f, 0.02f, -43f);
            PlaceSquadFormation(leadPos, Vector3.forward, resetHp: true);
        }

        public void PlaceSquadFormation(Vector3 leadPos, Vector3 forward, bool resetHp = false)
        {
            EnsureSquadSpawned();
            var save = SaveManager.EnsureInstance();
            var fwd = forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);

            Vector3[] offsets =
            {
                right * -2.35f - fwd * 0.85f,
                right * 2.65f - fwd * 1.65f,
                right * 2.05f + fwd * 0.95f
            };

            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i] == null) continue;
                var targetPos = leadPos + offsets[i];
                targetPos.y = leadPos.y;
                if (save.IsOperatorKia(_members[i].RoleTag))
                {
                    _members[i].ApplyPreexistingKiaState();
                    continue;
                }
                if (resetHp)
                {
                    _members[i].ResetForMission(targetPos);
                }

                _members[i].Teleport(targetPos, rot);
            }
        }

        public bool HasStandingMemberNear(Vector3 point, float radius)
        {
            var rSq = radius * radius;
            foreach (var m in _members)
            {
                if (m == null || m.IsKia || m.IsDowned) continue;
                var d = m.transform.position - point;
                d.y = 0f;
                if (d.sqrMagnitude <= rSq) return true;
            }
            return false;
        }

        public void SpawnSquadBullet(Vector3 origin, Quaternion rot, float speed, float damage, int pierce, Vector3 visualScale)
        {
            var p = _squadBulletPool.Get();
            p.Initialize(
                _squadBulletPool,
                origin,
                rot,
                speed,
                damage,
                lifetime: 1.5f,
                pierceCount: pierce,
                aoeRadius: 0f,
                enemyProjectile: false,
                tintColor: Palette.RailgunCyan,
                visualScale: visualScale,
                criticalHit: false);
        }

        private Projectile CreateSquadBulletInstance()
        {
            var go = new GameObject("Squad_TracerBolt");
            go.transform.SetParent(_bulletPoolRoot, false);

            var meshGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshGo.name = "BoltVisual";
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localScale = new Vector3(0.065f, 0.25f, 0.065f);
            var c = meshGo.GetComponent<Collider>();
            if (c != null) Destroy(c);
            var mr = meshGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _squadBulletMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var proj = go.AddComponent<Projectile>();
            go.SetActive(false);
            return proj;
        }

        private SquadMemberController BuildSquadMemberRig(
            SquadRole role,
            Color primaryColor,
            Color secondaryColor,
            Mesh headMesh,
            bool useVisor,
            Mesh weaponMesh,
            float weaponScale,
            Vector3 leftGripLocal,
            Vector3 rightGripLocal,
            Vector3 muzzleLocal,
            Color? headGearColor = null)
        {
            var matPrimary = Palette.CreateLitMaterial($"Mat_Squad_{role}_Primary", primaryColor, 0.12f, 0.34f, false, 0f);
            var matSecondary = Palette.CreateLitMaterial($"Mat_Squad_{role}_Secondary", secondaryColor, 0.10f, 0.30f, false, 0f);
            var matHeadGear = headGearColor.HasValue
                ? Palette.CreateLitMaterial($"Mat_Squad_{role}_Head", headGearColor.Value, 0.10f, 0.32f, false, 0f)
                : matPrimary;

            var root = new GameObject($"Squadmate_{role}");
            root.transform.SetParent(_squadRoot, false);
            var allyLayer = LayerMask.NameToLayer("SquadAlly");
            if (allyLayer >= 0) root.layer = allyLayer;

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.42f;
            cc.height = 1.74f;
            cc.center = new Vector3(0f, 0.87f, 0f);
            cc.stepOffset = 0.25f;

            var motion = root.AddComponent<ProceduralCharacterMotion>();
            var ctrl = root.AddComponent<SquadMemberController>();

            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            CreateFit("Torso", torsoMesh, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.56f, 0.56f, 0.36f), matPrimary);
            CreateFit("ChestRig", chestRigMesh, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.11f, 0.04f), new Vector3(0.60f, 0.35f, 0.33f), matSecondary);

            var headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.48f, 0.02f);
            var headTargetSize = headMesh == boonieHatMesh && boonieHatMesh != null
                ? new Vector3(0.56f, 0.37f, 0.56f)
                : new Vector3(0.42f, 0.38f, 0.44f);
            CreateFit("HeadGear", headMesh, PrimitiveType.Cube, headPivot.transform, Vector3.zero, headTargetSize, matHeadGear);
            if (useVisor)
            {
                CreateFit("TacticalVisor", visorMesh, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.36f, 0.12f, 0.14f), _nvgLensMat);
            }

            var leftArm = BuildArm("Left", visualRoot.transform, new Vector3(-0.28f, 1.24f, 0.07f), matPrimary, out var leftElbow);
            var rightArm = BuildArm("Right", visualRoot.transform, new Vector3(0.30f, 1.24f, -0.02f), matPrimary, out var rightElbow);

            var weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.11f, 1.18f, 0.42f);

            var weaponBody = new GameObject("WeaponBody");
            weaponBody.transform.SetParent(weaponAnchor.transform, false);
            if (weaponMesh != null)
            {
                var mf = weaponBody.AddComponent<MeshFilter>();
                mf.sharedMesh = weaponMesh;
                var mr = weaponBody.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _weaponSteelMat;
                weaponBody.transform.localScale = Vector3.one * weaponScale;
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(weaponBody.transform, false);
                cube.transform.localScale = new Vector3(0.08f, 0.13f, 0.55f);
                var c = cube.GetComponent<Collider>();
                if (c != null) Destroy(c);
                cube.GetComponent<MeshRenderer>().sharedMaterial = _weaponSteelMat;
            }

            var leftGrip = new GameObject("LeftHandGrip");
            leftGrip.transform.SetParent(weaponAnchor.transform, false);
            leftGrip.transform.localPosition = leftGripLocal;

            var rightGrip = new GameObject("RightHandGrip");
            rightGrip.transform.SetParent(weaponAnchor.transform, false);
            rightGrip.transform.localPosition = rightGripLocal;

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(weaponAnchor.transform, false);
            muzzle.transform.localPosition = muzzleLocal;

            var leftLeg = new GameObject("LeftLegPivot");
            leftLeg.transform.SetParent(visualRoot.transform, false);
            leftLeg.transform.localPosition = new Vector3(-0.15f, 0.74f, 0f);
            CreateFit("LeftLeg", limbMesh, PrimitiveType.Cube, leftLeg.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            var rightLeg = new GameObject("RightLegPivot");
            rightLeg.transform.SetParent(visualRoot.transform, false);
            rightLeg.transform.localPosition = new Vector3(0.15f, 0.74f, 0f);
            CreateFit("RightLeg", limbMesh, PrimitiveType.Cube, rightLeg.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            var pack = new GameObject("BackpackPivot");
            pack.transform.SetParent(visualRoot.transform, false);
            pack.transform.localPosition = new Vector3(0f, 1.09f, -0.26f);
            CreateFit("FieldPack", backpackMesh, PrimitiveType.Cube, pack.transform, Vector3.zero, new Vector3(0.32f, 0.42f, 0.20f), matSecondary);

            // Friendly subtle blue floor chevron/beacon disc so teammates pop clearly in chaotic gunfights
            var ringMat = Palette.CreateParticleMaterial($"Mat_SquadRing_{role}", UISpriteFactory.GetTelegraphRingTexture());
            ringMat.color = Palette.SquadBlue;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "SquadMarkerRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
            var rc = ring.GetComponent<Collider>();
            if (rc != null) Destroy(rc);
            var rmr = ring.GetComponent<MeshRenderer>();
            if (rmr != null)
            {
                rmr.sharedMaterial = ringMat;
                rmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArm, rightArm, leftLeg.transform, rightLeg.transform, pack.transform);
            motion.BindWeaponArmIk(leftElbow, rightElbow, weaponAnchor.transform, leftGrip.transform, rightGrip.transform, 0.34f, 0.35f);
            ctrl.InitializeRig(role, cc, motion, root.GetComponentsInChildren<MeshRenderer>(true), visualRoot.transform, muzzle.transform);

            return ctrl;
        }

        private Transform BuildArm(string side, Transform parent, Vector3 shoulderPos, Material sleeveMat, out Transform elbowOut)
        {
            var armPivot = new GameObject(side + "ArmPivot");
            armPivot.transform.SetParent(parent, false);
            armPivot.transform.localPosition = shoulderPos;
            CreateFit(side + "UpperArm", upperArmMesh, PrimitiveType.Capsule, armPivot.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.34f, 0.14f), sleeveMat);

            var elbowPivot = new GameObject(side + "ElbowPivot");
            elbowPivot.transform.SetParent(armPivot.transform, false);
            elbowPivot.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            elbowOut = elbowPivot.transform;
            CreateFit(side + "Forearm", forearmMesh, PrimitiveType.Capsule, elbowPivot.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.12f, 0.30f, 0.12f), sleeveMat);

            var glove = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glove.name = side + "Glove";
            glove.transform.SetParent(elbowPivot.transform, false);
            glove.transform.localPosition = new Vector3(0f, -0.335f, 0f);
            glove.transform.localScale = new Vector3(0.085f, 0.09f, 0.095f);
            var gc = glove.GetComponent<Collider>();
            if (gc != null) Destroy(gc);
            glove.GetComponent<MeshRenderer>().sharedMaterial = _weaponSteelMat;

            return armPivot.transform;
        }

        private static void CreateFit(string partName, Mesh customMesh, PrimitiveType fallbackType, Transform parent,
            Vector3 localPos, Vector3 targetSize, Material mat)
        {
            GameObject go;
            if (customMesh != null)
            {
                go = new GameObject(partName);
                go.transform.SetParent(parent, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = customMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                var b = customMesh.bounds.size;
                var fit = new Vector3(
                    targetSize.x / Mathf.Max(0.01f, b.x),
                    targetSize.y / Mathf.Max(0.01f, b.y),
                    targetSize.z / Mathf.Max(0.01f, b.z));
                go.transform.localScale = fit;
                go.transform.localPosition = localPos - Vector3.Scale(customMesh.bounds.center, fit);
            }
            else
            {
                go = GameObject.CreatePrimitive(fallbackType);
                go.name = partName;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.transform.localScale = targetSize;
                var c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }
        }
    }
}
