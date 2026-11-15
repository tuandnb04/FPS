using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Audio;
using Camera;
using Character;
using Core;
using Feel;
using Pickups;
using UI;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace Enemies
{
    /// <summary>
    /// Pooled Echo-7 3-Sector Campaign Director managing sector garrisons, armored IFV encounters,
    /// interactive C4 breach & sabotage objectives, Extraction LZ, and tactical supply drops.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        public static EnemySpawner Instance { get; private set; }

        [Header("Campaign Sector Bounds (Echo-7)")]
        [SerializeField] private float sector1HalfWidth = 25.0f;
        [SerializeField] private float sector2HalfWidth = 34.5f;
        [SerializeField] private float sector3HalfWidth = 26.5f;
        [SerializeField] private float compoundMinZ = -49.5f;
        [SerializeField] private float sector1MaxZ = -17.0f;
        [SerializeField] private float sector2MaxZ = 19.0f;
        [SerializeField] private float sector3MaxZ = 52.0f;

        [Header("Blender Custom Meshes (Assigned by SceneSetupBuilder)")]
        [SerializeField] private Mesh chaserTorsoMesh;
        [SerializeField] private Mesh chaserHeadMesh;
        [SerializeField] private Mesh heavyTorsoMesh;
        [SerializeField] private Mesh heavyCrownMesh;
        [SerializeField] private Mesh bossTorsoMesh;
        [SerializeField] private Mesh bossTurretMesh;
        [SerializeField] private Mesh limbMesh;
        [SerializeField] private Mesh enemyArmMesh;
        [SerializeField] private Mesh enemyUpperArmMesh;
        [SerializeField] private Mesh enemyForearmMesh;
        [FormerlySerializedAs("enemyAK74Mesh")] [SerializeField] private Mesh enemyAk74Mesh;
        [FormerlySerializedAs("enemyPKMMesh")] [SerializeField] private Mesh enemyPkmMesh;
        [SerializeField] private Mesh xpCrystalMesh;
        [SerializeField] private Mesh medkitMesh;
        [SerializeField] private Mesh creditCoinMesh;
        [SerializeField] private Mesh sabotageTerminalMesh;
        [SerializeField] private Mesh c4ChargeMesh;

        private ObjectPool<EnemyController> _enemyPool;
        private ObjectPool<PickupItem> _pickupPool;

        private readonly List<EnemyController> _activeEnemies = new();
        private readonly List<PickupItem> _activePickups = new();
        private readonly List<GameObject> _activeHazards = new();
        private readonly List<CampaignObjectiveNode> _objectiveNodes = new(4);

        private Transform _enemyPoolRoot;
        private Transform _pickupPoolRoot;
        private Transform _hazardRoot;
        private Transform _objectiveRoot;

        private Material _enemyPrimaryMat;
        private Material _enemySecondaryMat;
        private Material _enemyWeaponMat;
        private Material _bossArmorMat;
        private Material _bossTrimMat;
        private Material _pickupMat;
        private Material _hazardRingMat;
        private Material _objectiveTerminalMat;
        private Material _objectiveC4Mat;

        private int _stage1KillTarget = 8;
        private int _stage1KillsProgress;
        private int _remainingReinforcements;
        private float _reinforcementTimer;
        private float _stageBeatTimer;
        private bool _transitionPending;
        private bool _stage2BossCleared;

        public int CurrentStage { get; private set; } = 1;

        public int CurrentWave => CurrentStage; // Compatibility alias for SaveManager / legacy hooks
        public int ActiveEnemyCount => _activeEnemies.Count;
        public IReadOnlyList<EnemyController> ActiveEnemies => _activeEnemies;
        public IReadOnlyList<CampaignObjectiveNode> ObjectiveNodes => _objectiveNodes;
        public CampaignObjectiveNode Gate1BreachNode { get; private set; }

        public CampaignObjectiveNode FuelValveNode { get; private set; }

        public CampaignObjectiveNode RadarTerminalNode { get; private set; }

        public CampaignObjectiveNode HelipadLzNode { get; private set; }

        public int SabotageCompletedCount { get; private set; }

        public void BindMeshes(
            Mesh chaserTorso, Mesh chaserHead,
            Mesh heavyTorso, Mesh heavyCrown,
            Mesh bossTorso, Mesh bossTurret,
            Mesh limb,
            Mesh xpShard, Mesh medkit, Mesh creditCoin,
            Mesh enemyArm = null,
            Mesh enemyUpperArm = null,
            Mesh enemyForearm = null,
            Mesh enemyAk74 = null,
            Mesh enemyPkm = null,
            Mesh sabotageTerminal = null,
            Mesh c4Charge = null)
        {
            chaserTorsoMesh = chaserTorso;
            chaserHeadMesh = chaserHead;
            heavyTorsoMesh = heavyTorso;
            heavyCrownMesh = heavyCrown;
            bossTorsoMesh = bossTorso;
            bossTurretMesh = bossTurret;
            limbMesh = limb;
            enemyArmMesh = enemyArm;
            enemyUpperArmMesh = enemyUpperArm;
            enemyForearmMesh = enemyForearm;
            enemyAk74Mesh = enemyAk74;
            enemyPkmMesh = enemyPkm;
            xpCrystalMesh = xpShard;
            medkitMesh = medkit;
            creditCoinMesh = creditCoin;
            sabotageTerminalMesh = sabotageTerminal;
            c4ChargeMesh = c4Charge;
            EnsureObjectiveNodesBuilt();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _enemyPoolRoot = new GameObject("EnemyPoolRoot").transform;
            _enemyPoolRoot.SetParent(transform, false);

            _pickupPoolRoot = new GameObject("PickupPoolRoot").transform;
            _pickupPoolRoot.SetParent(transform, false);

            _hazardRoot = new GameObject("HazardTelegraphRoot").transform;
            _hazardRoot.SetParent(transform, false);

            _objectiveRoot = new GameObject("CampaignObjectivesRoot").transform;
            _objectiveRoot.SetParent(transform, false);

            _enemyPrimaryMat = Palette.CreateLitMaterial("Mat_EnemyChassisRuntime", Palette.EnemyPrimary, 0.20f, 0.36f);
            _enemySecondaryMat = Palette.CreateLitMaterial("Mat_EnemyVisorRuntime", Palette.Props, 0.35f, 0.40f);
            _enemyWeaponMat = Palette.CreateLitMaterial("Mat_EnemyWeaponRuntime", Palette.Gunmetal, 0.78f, 0.56f);
            _bossArmorMat = Palette.CreateLitMaterial("Mat_BossChassisRuntime", Palette.GuardTowerDark, 0.36f, 0.42f);
            _bossTrimMat = Palette.CreateLitMaterial("Mat_BossTrimRuntime", Palette.Gunmetal, 0.55f, 0.48f);
            _pickupMat = Palette.CreateLitMaterial("Mat_PickupCoreRuntime", Palette.Accent, 0.25f, 0.45f);
            _hazardRingMat = Palette.CreateParticleMaterial("Mat_HazardRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            _objectiveTerminalMat = Palette.CreateLitMaterial("Mat_ObjectiveTermRuntime", Palette.GuardTowerDark, 0.45f, 0.45f);
            _objectiveC4Mat = Palette.CreateLitMaterial("Mat_ObjectiveC4Runtime", Palette.C4Amber, 0.20f, 0.35f, true, 1.35f);

            _enemyPool = new ObjectPool<EnemyController>(
                createFunc: CreateEnemyRig,
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 14,
                maxSize: 36
            );

            _pickupPool = new ObjectPool<PickupItem>(
                createFunc: CreatePickupRig,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 18,
                maxSize: 48
            );

            EnsureObjectiveNodesBuilt();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void EnsureObjectiveNodesBuilt()
        {
            if (_objectiveRoot == null) return;
            if (_objectiveNodes.Count == 4 && sabotageTerminalMesh == null) return;

            for (var i = _objectiveNodes.Count - 1; i >= 0; i--)
            {
                if (_objectiveNodes[i] != null) Destroy(_objectiveNodes[i].gameObject);
            }
            _objectiveNodes.Clear();

            Gate1BreachNode = CreateObjectiveNode(
                "Obj_Gate1_Breach", ObjectiveNodeKind.GateC4Breach, "BLAST GATE ALPHA",
                new Vector3(0f, 0f, -17.5f), 3.3f, 1.55f, true);

            FuelValveNode = CreateObjectiveNode(
                "Obj_S2_FuelValve", ObjectiveNodeKind.FuelValveSabotage, "FUEL MANIFOLD",
                new Vector3(-16.5f, 0f, -1.2f), 3.4f, 1.65f, true);

            RadarTerminalNode = CreateObjectiveNode(
                "Obj_S2_RadarTerminal", ObjectiveNodeKind.RadarSabotage, "EW RADAR UPLINK",
                new Vector3(21.5f, 0f, 1.5f), 3.4f, 1.65f, true);

            HelipadLzNode = CreateObjectiveNode(
                "Obj_S3_HelipadLZ", ObjectiveNodeKind.HelipadEvacLZ, "EXTRACTION LZ EVAC-6",
                new Vector3(0f, 0f, 42.0f), 4.2f, 1.10f, false);
        }

        private CampaignObjectiveNode CreateObjectiveNode(
            string goName,
            ObjectiveNodeKind kind,
            string title,
            Vector3 pos,
            float radius,
            float armSeconds,
            bool explode)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(_objectiveRoot, false);
            var node = go.AddComponent<CampaignObjectiveNode>();
            node.Configure(kind, title, pos, radius, armSeconds, explode, sabotageTerminalMesh, c4ChargeMesh, _objectiveTerminalMat, _objectiveC4Mat, HandleObjectiveCompleted);
            _objectiveNodes.Add(node);
            return node;
        }

        private EnemyController CreateEnemyRig()
        {
            var root = new GameObject("Enemy_Sentinel");
            root.transform.SetParent(_enemyPoolRoot, false);
            var enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) root.layer = enemyLayer;

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.48f;
            cc.height = 1.75f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.25f;

            var motion = root.AddComponent<ProceduralCharacterMotion>();
            var ctrl = root.AddComponent<EnemyController>();

            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            var torsoGroup = new GameObject("TorsoRoot");
            torsoGroup.transform.SetParent(visualRoot.transform, false);

            var chaserBody = CreatePartWithMesh("ChaserBody", chaserTorsoMesh, PrimitiveType.Cube, torsoGroup.transform, new Vector3(0f, 1.0f, 0f), chaserTorsoMesh != null ? Vector3.one : new Vector3(0.68f, 0.62f, 0.46f), _enemyPrimaryMat);
            var heavyBody = CreatePartWithMesh("HeavyBody", heavyTorsoMesh, PrimitiveType.Cube, torsoGroup.transform, new Vector3(0f, 1.05f, 0f), heavyTorsoMesh != null ? Vector3.one : new Vector3(0.86f, 0.72f, 0.62f), _enemyPrimaryMat);
            heavyBody.SetActive(false);

            var headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.46f, 0.04f);

            var chaserHeadObj = CreatePartWithMesh("ChaserHead", chaserHeadMesh, PrimitiveType.Cube, headPivot.transform, Vector3.zero, chaserHeadMesh != null ? Vector3.one : new Vector3(0.46f, 0.34f, 0.44f), _enemyPrimaryMat);
            CreatePartWithMesh("ChaserVisor", null, PrimitiveType.Cube, chaserHeadObj.transform, new Vector3(0f, 0.02f, 0.21f), new Vector3(0.36f, 0.10f, 0.08f), _enemySecondaryMat);

            var heavyHeadObj = CreatePartWithMesh("HeavyCrown", heavyCrownMesh, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.06f, 0f), heavyCrownMesh != null ? Vector3.one : new Vector3(0.56f, 0.24f, 0.50f), _enemySecondaryMat);
            heavyHeadObj.SetActive(false);

            var leftArmPivot = BuildEnemyOperatorArm("Left", visualRoot.transform, new Vector3(-0.30f, 1.20f, 0.07f), out var leftElbowPivot);
            var rightArmPivot = BuildEnemyOperatorArm("Right", visualRoot.transform, new Vector3(0.32f, 1.20f, -0.02f), out var rightElbowPivot);

            var weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.12f, 1.12f, 0.35f);

            var ak74Obj = BuildEnemyWeapon(
                "EnemyWeapon_AK74", weaponAnchor.transform, enemyAk74Mesh, 0.65f,
                new Vector3(0f, -0.024f, 0.175f), new Vector3(0f, -0.060f, -0.036f), new Vector3(0f, 0.015f, 0.569f),
                out var ak74LeftGrip, out var ak74RightGrip, out var ak74Muzzle);

            var pkmObj = BuildEnemyWeapon(
                "EnemyWeapon_PKM", weaponAnchor.transform, enemyPkmMesh, 0.68f,
                new Vector3(0f, -0.026f, 0.210f), new Vector3(0f, -0.064f, -0.044f), new Vector3(0f, 0.019f, 0.656f),
                out var pkmLeftGrip, out var pkmRightGrip, out var pkmMuzzle);
            pkmObj.SetActive(false);

            var leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.18f, 0.74f, 0f);
            CreatePartWithMesh("LeftLeg", limbMesh, PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.35f, 0f), limbMesh != null ? Vector3.one : new Vector3(0.22f, 0.62f, 0.24f), _enemyPrimaryMat);

            var rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.18f, 0.74f, 0f);
            CreatePartWithMesh("RightLeg", limbMesh, PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.35f, 0f), limbMesh != null ? Vector3.one : new Vector3(0.22f, 0.62f, 0.24f), _enemyPrimaryMat);

            var reactorPivot = new GameObject("SecondaryPivot");
            reactorPivot.transform.SetParent(visualRoot.transform, false);
            reactorPivot.transform.localPosition = new Vector3(0f, 1.05f, -0.30f);
            CreatePartWithMesh("FieldPack", null, PrimitiveType.Cube, reactorPivot.transform, Vector3.zero, new Vector3(0.32f, 0.36f, 0.18f), _enemySecondaryMat);

            var chaserToggle = new GameObject("ChaserMarker");
            chaserToggle.transform.SetParent(visualRoot.transform, false);

            var heavyToggle = new GameObject("HeavyMarker");
            heavyToggle.transform.SetParent(visualRoot.transform, false);
            heavyBody.transform.SetParent(heavyToggle.transform, true);
            heavyHeadObj.transform.SetParent(heavyToggle.transform, true);
            heavyBody.SetActive(true);
            heavyHeadObj.SetActive(true);

            chaserBody.transform.SetParent(chaserToggle.transform, true);
            chaserHeadObj.transform.SetParent(chaserToggle.transform, true);

            var bossGroup = new GameObject("BossRigRoot");
            bossGroup.transform.SetParent(root.transform, false);
            CreatePartWithMesh("BossHull", bossTorsoMesh, PrimitiveType.Cube, bossGroup.transform, new Vector3(0f, 0.72f, 0f), bossTorsoMesh != null ? Vector3.one : new Vector3(1.5f, 0.75f, 1.4f), _bossArmorMat);
            CreatePartWithMesh("DriverPeriscope", null, PrimitiveType.Cube, bossGroup.transform, new Vector3(0.26f, 1.05f, 0.64f), new Vector3(0.24f, 0.14f, 0.22f), _bossTrimMat);
            var turretPivot = new GameObject("BossTurretPivot");
            turretPivot.transform.SetParent(bossGroup.transform, false);
            turretPivot.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            CreatePartWithMesh("BossTurretMesh", bossTurretMesh, PrimitiveType.Cube, turretPivot.transform, Vector3.zero, bossTurretMesh != null ? Vector3.one : new Vector3(0.85f, 0.38f, 0.9f), _bossTrimMat);
            var bossMuzzleTip = new GameObject("BossMuzzleTip");
            bossMuzzleTip.transform.SetParent(turretPivot.transform, false);
            bossMuzzleTip.transform.localPosition = new Vector3(0f, 0.02f, 1.52f);
            bossGroup.SetActive(false);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot, rightArmPivot, leftLegPivot.transform, rightLegPivot.transform, reactorPivot.transform);
            motion.BindWeaponArmIk(leftElbowPivot, rightElbowPivot, weaponAnchor.transform, ak74LeftGrip, ak74RightGrip);
            ctrl.BindComponents(
                cc, motion, root.GetComponentsInChildren<MeshRenderer>(true),
                visualRoot, chaserToggle, heavyToggle, bossGroup, turretPivot.transform,
                leftArmPivot, rightArmPivot, weaponAnchor.transform,
                ak74Obj, ak74LeftGrip, ak74RightGrip, ak74Muzzle,
                pkmObj, pkmLeftGrip, pkmRightGrip, pkmMuzzle,
                bossMuzzleTip.transform);

            root.SetActive(false);
            return ctrl;
        }

        private Transform BuildEnemyOperatorArm(string side, Transform visualRoot, Vector3 shoulderPos, out Transform elbowPivotOut)
        {
            var armPivot = new GameObject(side + "ArmPivot");
            armPivot.transform.SetParent(visualRoot, false);
            armPivot.transform.localPosition = shoulderPos;

            if (enemyUpperArmMesh != null)
            {
                CreatePartWithMesh(side + "UpperArm", enemyUpperArmMesh, PrimitiveType.Capsule, armPivot.transform, Vector3.zero, new Vector3(1.06f, 0.34f / 0.295f, 1.06f), _enemyPrimaryMat);
            }
            else if (enemyArmMesh != null)
            {
                CreatePartWithMesh(side + "UpperArm", enemyArmMesh, PrimitiveType.Capsule, armPivot.transform, Vector3.zero, new Vector3(0.95f, 0.60f, 0.95f), _enemyPrimaryMat);
            }
            else
            {
                CreatePartWithMesh(side + "UpperArm", null, PrimitiveType.Capsule, armPivot.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.18f, 0.14f), _enemyPrimaryMat);
            }

            var elbowPivot = new GameObject(side + "ElbowPivot");
            elbowPivot.transform.SetParent(armPivot.transform, false);
            elbowPivot.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            elbowPivotOut = elbowPivot.transform;

            if (enemyForearmMesh != null)
            {
                CreatePartWithMesh(side + "Forearm", enemyForearmMesh, PrimitiveType.Capsule, elbowPivot.transform, Vector3.zero, new Vector3(1.04f, 0.30f / 0.25f, 1.04f), _enemyPrimaryMat);
            }
            else
            {
                CreatePartWithMesh(side + "Forearm", null, PrimitiveType.Capsule, elbowPivot.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.12f, 0.16f, 0.12f), _enemyPrimaryMat);
            }

            CreatePartWithMesh(side + "Glove", null, PrimitiveType.Cube, elbowPivot.transform, new Vector3(0f, -0.335f, 0f), new Vector3(0.085f, 0.09f, 0.095f), _enemyWeaponMat);
            return armPivot.transform;
        }

        private GameObject BuildEnemyWeapon(
            string name,
            Transform anchor,
            Mesh weaponMesh,
            float uniformScale,
            Vector3 leftGripLocal,
            Vector3 rightGripLocal,
            Vector3 muzzleLocal,
            out Transform leftGripOut,
            out Transform rightGripOut,
            out Transform muzzleOut)
        {
            var container = new GameObject(name);
            container.transform.SetParent(anchor, false);

            CreatePartWithMesh("MeshBody", weaponMesh, PrimitiveType.Cube, container.transform,
                weaponMesh != null ? Vector3.zero : new Vector3(0f, 0f, 0.16f),
                weaponMesh != null ? Vector3.one * uniformScale : new Vector3(0.09f, 0.14f, 0.56f),
                _enemyWeaponMat);

            var leftGrip = new GameObject("LeftHandGrip");
            leftGrip.transform.SetParent(container.transform, false);
            leftGrip.transform.localPosition = leftGripLocal;
            leftGripOut = leftGrip.transform;

            var rightGrip = new GameObject("RightHandGrip");
            rightGrip.transform.SetParent(container.transform, false);
            rightGrip.transform.localPosition = rightGripLocal;
            rightGripOut = rightGrip.transform;

            var muzzleTip = new GameObject("MuzzleTip");
            muzzleTip.transform.SetParent(container.transform, false);
            muzzleTip.transform.localPosition = muzzleLocal;
            muzzleOut = muzzleTip.transform;

            return container;
        }

        private PickupItem CreatePickupRig()
        {
            var root = new GameObject("Pickup_Collectible");
            root.transform.SetParent(_pickupPoolRoot, false);
            var pickupLayer = LayerMask.NameToLayer("Pickup");
            if (pickupLayer >= 0) root.layer = pickupLayer;

            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.85f;

            var visualHolder = new GameObject("VisualSpinRoot");
            visualHolder.transform.SetParent(root.transform, false);

            var xpObj = CreatePartWithMesh("XpCrystalVisual", xpCrystalMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, xpCrystalMesh != null ? Vector3.one : new Vector3(0.36f, 0.36f, 0.36f), _pickupMat);
            var medObj = CreatePartWithMesh("MedkitVisual", medkitMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, medkitMesh != null ? Vector3.one : new Vector3(0.44f, 0.24f, 0.36f), _pickupMat);
            var credObj = CreatePartWithMesh("CreditCoinVisual", creditCoinMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, creditCoinMesh != null ? Vector3.one : new Vector3(0.55f, 0.35f, 0.42f), _pickupMat);

            medObj.SetActive(false);
            credObj.SetActive(false);

            var item = root.AddComponent<PickupItem>();
            item.ConfigureMultiVisuals(
                visualHolder.transform,
                xpObj, xpObj.GetComponent<MeshRenderer>(),
                medObj, medObj.GetComponent<MeshRenderer>(),
                credObj, credObj.GetComponent<MeshRenderer>());

            root.SetActive(false);
            return item;
        }

        private static GameObject CreatePartWithMesh(string partName, Mesh customMesh, PrimitiveType fallbackType, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
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
            }
            else
            {
                go = GameObject.CreatePrimitive(fallbackType);
                go.name = partName;
                go.transform.SetParent(parent, false);
                var c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go;
        }

        public void BeginWave(int stageNum)
        {
            BeginCampaignStage(stageNum);
        }

        private void BeginCampaignStage(int stageNum)
        {
            EnsureObjectiveNodesBuilt();
            _transitionPending = false;
            _stageBeatTimer = 0f;
            CurrentStage = Mathf.Clamp(stageNum, 1, StoryDirector.FinalCampaignStage);

            if (CurrentStage >= 3)
            {
                CutsceneDirector.Instance?.OpenGateImmediate(1);
            }
            if (CurrentStage >= 5)
            {
                CutsceneDirector.Instance?.OpenGateImmediate(2);
            }

            switch (CurrentStage)
            {
                case 1:
                    SetupStage1CheckpointGarrison();
                    break;
                case 2:
                    SetupStage2WatchdogAndGateC4();
                    break;
                case 3:
                    SetupStage3FuelAndRadarSabotage();
                    break;
                case 4:
                    SetupStage4WardenAndGate2();
                    break;
                case 5:
                    SetupStage5VolkovNorthHq();
                    break;
                case 6:
                    SetupStage6ExtractionLz();
                    break;
            }

            if (CurrentStage > 1)
            {
                StoryDirector.NotifyStageStarted(CurrentStage);
            }
            SyncMissionHud();
        }

        private void SetupStage1CheckpointGarrison()
        {
            Gate1BreachNode?.SetState(false, false);
            FuelValveNode?.SetState(false, false);
            RadarTerminalNode?.SetState(false, false);
            HelipadLzNode?.SetState(false, false);

            Vector3[] sentries =
            {
                new(-5.8f, 0f, -34.0f),
                new(5.8f, 0f, -34.0f),
                new(-8.0f, 0f, -26.5f),
                new(8.0f, 0f, -26.5f),
                new(-13.5f, 0f, -29.0f),
                new(13.0f, 0f, -27.5f),
                new(-2.4f, 0f, -20.5f),
                new(2.4f, 0f, -20.5f)
            };

            _stage1KillsProgress = 0;
            _stage1KillTarget = sentries.Length;
            _remainingReinforcements = 0;

            for (var i = 0; i < sentries.Length; i++)
            {
                var e = SpawnEnemyAt(i == 5 ? EnemyArchetype.Heavy : EnemyArchetype.Chaser, sentries[i]);
                e.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                e.SetGarrisonAlert(i < 4);
            }
        }

        private void SetupStage2WatchdogAndGateC4()
        {
            _stage2BossCleared = false;
            Gate1BreachNode?.SetState(false, false);
            _remainingReinforcements = 0;

            SpawnEnemyAt(EnemyArchetype.Chaser, new Vector3(-6.5f, 0f, -22.5f)).SetGarrisonAlert(true, 45f);
            SpawnEnemyAt(EnemyArchetype.Chaser, new Vector3(6.5f, 0f, -22.5f)).SetGarrisonAlert(true, 45f);
            SpawnEnemyAt(EnemyArchetype.Heavy, new Vector3(0f, 0f, -25.0f)).SetGarrisonAlert(true, 45f);

            SpawnBossAt(new Vector3(0f, 0f, -20.5f));
            StoryDirector.NotifyBossSpawned(CurrentStage);
            UIManager.Instance?.ShowMilestoneBanner("THREAT // " + StoryDirector.GetBossCallsign(CurrentStage));
        }

        private void SetupStage3FuelAndRadarSabotage()
        {
            SabotageCompletedCount = 0;
            Gate1BreachNode?.SetState(false, true);
            FuelValveNode?.SetState(true, false);
            RadarTerminalNode?.SetState(true, false);
            HelipadLzNode?.SetState(false, false);
            _remainingReinforcements = 3;
            _reinforcementTimer = 6.5f;

            // Garrison defending West Fuel Depot, Container Maze, and East Radar Array
            Vector3[] rifles =
            {
                new(-16.0f, 0f, -7.5f),
                new(-18.5f, 0f, 3.5f),
                new(-6.0f, 0f, 2.5f),
                new(7.0f, 0f, -3.5f),
                new(17.5f, 0f, -4.5f),
                new(23.5f, 0f, 6.0f)
            };
            Vector3[] heavies =
            {
                new(-21.0f, 0f, 0.5f),
                new(18.5f, 0f, 4.5f),
                new(0.0f, 0f, 11.0f)
            };

            foreach (var t in rifles)
            {
                var e = SpawnEnemyAt(EnemyArchetype.Chaser, t);
                e.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                e.SetGarrisonAlert(false, 22f);
            }
            foreach (var t in heavies)
            {
                var h = SpawnEnemyAt(EnemyArchetype.Heavy, t);
                h.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                h.SetGarrisonAlert(false, 23f);
            }
        }

        private void SetupStage4WardenAndGate2()
        {
            _remainingReinforcements = 0;
            foreach (var t in _activeEnemies.Where(t => t != null))
            {
                t.SetGarrisonAlert(true, 64f);
            }

            SpawnEnemyAt(EnemyArchetype.Heavy, new Vector3(-7.5f, 0f, 14.5f)).SetGarrisonAlert(true, 55f);
            SpawnEnemyAt(EnemyArchetype.Chaser, new Vector3(7.5f, 0f, 14.5f)).SetGarrisonAlert(true, 55f);
            SpawnBossAt(new Vector3(0f, 0f, 15.5f));
            StoryDirector.NotifyBossSpawned(CurrentStage);
            UIManager.Instance?.ShowMilestoneBanner("THREAT // " + StoryDirector.GetBossCallsign(CurrentStage));
        }

        private void SetupStage5VolkovNorthHq()
        {
            _remainingReinforcements = 0;
            CutsceneDirector.Instance?.OpenGateImmediate(2);

            SpawnEnemyAt(EnemyArchetype.Chaser, new Vector3(-9.5f, 0f, 29.0f)).SetGarrisonAlert(true, 55f);
            SpawnEnemyAt(EnemyArchetype.Chaser, new Vector3(9.5f, 0f, 29.0f)).SetGarrisonAlert(true, 55f);
            SpawnEnemyAt(EnemyArchetype.Heavy, new Vector3(-11.5f, 0f, 36.5f)).SetGarrisonAlert(true, 55f);
            SpawnEnemyAt(EnemyArchetype.Heavy, new Vector3(11.5f, 0f, 36.5f)).SetGarrisonAlert(true, 55f);

            SpawnBossAt(new Vector3(0f, 0f, 42.0f));
            StoryDirector.NotifyBossSpawned(CurrentStage);
            UIManager.Instance?.ShowMilestoneBanner("FINAL TARGET // COL. VOLKOV");

            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
            {
                GameManager.Instance.TriggerCutscene(CutsceneType.Act3BossVolkov);
            }
        }

        private void SetupStage6ExtractionLz()
        {
            _remainingReinforcements = 0;
            HelipadLzNode?.SetState(true, false);
            UIManager.Instance?.ShowMilestoneBanner("REACH EXTRACTION LZ EVAC-6!");
        }

        private void Update()
        {
            if (_transitionPending || GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (CurrentStage == 3 && _remainingReinforcements > 0)
            {
                _reinforcementTimer -= dt;
                if (_reinforcementTimer <= 0f && _activeEnemies.Count < 10)
                {
                    _remainingReinforcements--;
                    _reinforcementTimer = 6.0f;
                    var esc = SpawnEnemyAt(_remainingReinforcements == 0 ? EnemyArchetype.Heavy : EnemyArchetype.Chaser, new Vector3(Random.Range(-6f, 6f), 0f, 16.5f));
                    esc.SetGarrisonAlert(true, 55f);
                }
            }

            switch (CurrentStage)
            {
                // Stage advancement checks based on Campaign Objectives rather than arcade wave counters
                case 1 when _activeEnemies.Count == 0:
                {
                    _stageBeatTimer += dt;
                    if (_stageBeatTimer >= 0.85f)
                    {
                        _stageBeatTimer = 0f;
                        BeginCampaignStage(2);
                    }

                    break;
                }
                case 2 when !_stage2BossCleared && _activeEnemies.Count == 0:
                    _stage2BossCleared = true;
                    Gate1BreachNode?.SetState(true, false);
                    StoryDirector.NotifyGate1C4Ready();
                    UIManager.Instance?.ShowMilestoneBanner("GATE CLEAR // PLANT C4 AT GATE ALPHA");
                    SyncMissionHud();
                    break;
                case 4 when _activeEnemies.Count == 0:
                {
                    _stageBeatTimer += dt;
                    if (_stageBeatTimer >= 0.9f)
                    {
                        _stageBeatTimer = 0f;
                        _transitionPending = true;
                        CutsceneDirector.Instance?.OpenGateImmediate(2);
                        var g2Center = new Vector3(0f, 1.6f, 22f);
                        VFXManager.Instance?.Play(VfxKind.EnemyDeath, g2Center, Palette.RocketOrange);
                        FeelManager.Instance?.TriggerCameraShake(0.38f);
                        AudioManager.Instance?.PlayEnemyExplode();
                        PlayerController.Instance?.AddFragGrenades(1);
                        BeginCampaignStage(5);
                    }

                    break;
                }
                case 5 when _activeEnemies.Count == 0:
                {
                    _stageBeatTimer += dt;
                    if (_stageBeatTimer >= 0.8f)
                    {
                        _stageBeatTimer = 0f;
                        BeginCampaignStage(6);
                    }

                    break;
                }
            }
        }

        private void HandleObjectiveCompleted(CampaignObjectiveNode node)
        {
            if (node == Gate1BreachNode && CurrentStage == 2)
            {
                _transitionPending = true;
                PlayerController.Instance?.AddFragGrenades(1);
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerCutscene(CutsceneType.Act2GateBreach, () =>
                    {
                        BeginCampaignStage(3);
                        UIManager.Instance?.ShowMilestoneBanner(StoryDirector.GetActLabel(3));
                    });
                }
                else
                {
                    BeginCampaignStage(3);
                }
                return;
            }

            if ((node == FuelValveNode || node == RadarTerminalNode) && CurrentStage == 3)
            {
                SabotageCompletedCount++;
                StoryDirector.NotifySabotageProgress(SabotageCompletedCount, 2, node.DisplayTitle);
                SyncMissionHud();

                if (SabotageCompletedCount < 2) return;
                UIManager.Instance?.ShowMilestoneBanner("EW JAMMING SEVERED // DESTROY WARDEN-02");
                BeginCampaignStage(4);
                return;
            }

            if (node != HelipadLzNode || CurrentStage != 6) return;
            _transitionPending = true;
            GameManager.Instance?.CompleteCampaignVictory();
        }

        public Vector3 GetActiveObjectiveWorldPoint()
        {
            switch (CurrentStage)
            {
                case 2 when Gate1BreachNode != null && Gate1BreachNode.IsUnlocked:
                    return Gate1BreachNode.transform.position;
                case 3:
                {
                    var fuelAvail = FuelValveNode != null && FuelValveNode.IsUnlocked;
                    var radarAvail = RadarTerminalNode != null && RadarTerminalNode.IsUnlocked;
                    switch (fuelAvail)
                    {
                        case true when radarAvail && PlayerController.Instance != null:
                        {
                            var dFuel = (FuelValveNode.transform.position - PlayerController.Instance.transform.position).sqrMagnitude;
                            var dRad = (RadarTerminalNode.transform.position - PlayerController.Instance.transform.position).sqrMagnitude;
                            return dFuel <= dRad ? FuelValveNode.transform.position : RadarTerminalNode.transform.position;
                        }
                        case true:
                            return FuelValveNode.transform.position;
                    }

                    if (radarAvail) return RadarTerminalNode.transform.position;
                    break;
                }
                case 6 when HelipadLzNode != null:
                    return HelipadLzNode.transform.position;
            }

            if (_activeEnemies.Count > 0 && _activeEnemies[0] != null)
            {
                return _activeEnemies[0].transform.position;
            }
            return new Vector3(0f, 0f, CurrentStage <= 2 ? -17.5f : (CurrentStage <= 4 ? 16f : 42f));
        }

        public Vector3 ClampPositionToUnlockedSectors(Vector3 pos)
        {
            var maxZ = CurrentStage <= 2 ? sector1MaxZ : (CurrentStage <= 4 ? sector2MaxZ : sector3MaxZ);
            var z = Mathf.Clamp(pos.z, compoundMinZ, maxZ);
            var halfW = z < -14f ? sector1HalfWidth : (z <= 22f ? sector2HalfWidth : sector3HalfWidth);
            var x = Mathf.Clamp(pos.x, -halfW, halfW);
            return new Vector3(x, pos.y, z);
        }

        private void SpawnBossAt(Vector3 worldPos)
        {
            SpawnEnemyAt(EnemyArchetype.Boss, worldPos);
        }

        private EnemyController SpawnEnemyAt(EnemyArchetype kind, Vector3 worldPos)
        {
            var enemy = _enemyPool.Get();
            _activeEnemies.Add(enemy);
            enemy.Initialize(_enemyPool, kind, worldPos, CurrentStage, HandleEnemyFinished);
            SyncMissionHud();
            return enemy;
        }

        private void SpawnPickupAt(PickupKind kind, Vector3 worldPos, int creditAmount = 20)
        {
            var item = _pickupPool.Get();
            _activePickups.Add(item);
            item.Initialize(_pickupPool, worldPos, kind, 0, kind == PickupKind.Medkit ? 30f : 0f, HandlePickupReleased, creditAmount);
        }

        public void SpawnHazardTelegraph(Vector3 worldPos, float radius, float dmg)
        {
            StartCoroutine(HazardTelegraphRoutine(new Vector3(worldPos.x, 0.04f, worldPos.z), radius, dmg));
        }

        private IEnumerator HazardTelegraphRoutine(Vector3 center, float radius, float dmg)
        {
            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "BossHazardTelegraph";
            ringGo.transform.SetParent(_hazardRoot, false);
            ringGo.transform.position = new Vector3(center.x, 0.03f, center.z);
            ringGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            var c = ringGo.GetComponent<Collider>();
            if (c != null) Destroy(c);

            var mr = ringGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _hazardRingMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _activeHazards.Add(ringGo);

            var block = new MaterialPropertyBlock();
            var elapsed = 0f;
            const float windup = 1.15f;
            while (elapsed < windup)
            {
                if (ringGo == null) yield break;
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / windup);
                    var pulse = 0.92f + 0.08f * Mathf.Sin(t * 18f);
                    ringGo.transform.localScale = new Vector3(radius * 2f * pulse, radius * 2f * pulse, 1f);
                    if (mr != null)
                    {
                        var col = Palette.Lerp(Palette.BossPurple, Palette.EnemySecondary, t);
                        col.a = 0.78f + t * 0.22f;
                        block.SetColor(Color1, col);
                        mr.SetPropertyBlock(block);
                    }
                }
                yield return null;
            }

            if (ringGo == null) yield break;

            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center + Vector3.up * 0.4f, Palette.BossPurple);
            FeelManager.Instance?.TriggerCameraShake(0.26f);
            AudioManager.Instance?.PlayEnemyExplode();

            var player = PlayerController.Instance;
            if (player != null && !player.IsDead)
            {
                var diff = player.transform.position - center;
                diff.y = 0f;
                if (diff.magnitude <= radius)
                {
                    player.TakeDamage(dmg);
                }
            }

            if (SquadDirector.Instance != null)
            {
                var squad = SquadDirector.Instance.Members;
                foreach (var t in squad)
                {
                    if (t == null || t.IsDowned) continue;
                    var d = t.transform.position - center;
                    d.y = 0f;
                    if (d.magnitude <= radius)
                    {
                        t.TakeDamage(dmg * 0.75f);
                    }
                }
            }

            _activeHazards.Remove(ringGo);
            Destroy(ringGo);
        }

        private void HandleEnemyFinished(EnemyController enemy, bool killedByFireteam)
        {
            _activeEnemies.Remove(enemy);
            if (killedByFireteam)
            {
                if (CurrentStage == 1) _stage1KillsProgress++;

                if (enemy.Archetype == EnemyArchetype.Boss)
                {
                    StoryDirector.NotifyBossDefeated(CurrentStage);
                    var center = enemy.transform.position;
                    SpawnPickupAt(PickupKind.Medkit, center);
                    for (var i = 0; i < 4; i++)
                    {
                        var offset = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 1.4f;
                        SpawnPickupAt(PickupKind.CreditCoin, center + offset, 25);
                    }
                }
                else
                {
                    // Realistic occasional Trauma IFAK & Munition Crate drops (NO glowing arcade XP gems)
                    var roll = UnityEngine.Random.value;
                    switch (roll)
                    {
                        case < 0.22f:
                            SpawnPickupAt(PickupKind.Medkit, enemy.transform.position);
                            break;
                        case < 0.56f:
                            SpawnPickupAt(PickupKind.CreditCoin, enemy.transform.position, enemy.Archetype == EnemyArchetype.Heavy ? 25 : 15);
                            break;
                    }
                }
            }
            SyncMissionHud();
        }

        private void SyncMissionHud()
        {
            var actTitle = StoryDirector.GetActLabel(CurrentStage);
            var objText = StoryDirector.GetSectorObjective(
                CurrentStage,
                _stage1KillsProgress,
                _stage1KillTarget,
                SabotageCompletedCount,
                2,
                Gate1BreachNode != null && Gate1BreachNode.IsUnlocked);

            UIManager.Instance?.UpdateMissionStatus(actTitle, _activeEnemies.Count, objText);
        }

        private void HandlePickupReleased(PickupItem item)
        {
            _activePickups.Remove(item);
        }

        public void ResetAndClearAll()
        {
            StopAllCoroutines();
            _transitionPending = false;
            CurrentStage = 1;
            _stage1KillsProgress = 0;
            _remainingReinforcements = 0;
            _reinforcementTimer = 0f;
            _stageBeatTimer = 0f;
            SabotageCompletedCount = 0;
            _stage2BossCleared = false;

            for (var i = _objectiveNodes.Count - 1; i >= 0; i--)
            {
                if (_objectiveNodes[i] != null) _objectiveNodes[i].SetState(false, false);
            }

            for (var i = _activeHazards.Count - 1; i >= 0; i--)
            {
                if (_activeHazards[i] != null) Destroy(_activeHazards[i]);
            }
            _activeHazards.Clear();

            for (var i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                if (_activeEnemies[i] != null) _activeEnemies[i].ForceReturnToPool();
            }
            _activeEnemies.Clear();

            for (var i = _activePickups.Count - 1; i >= 0; i--)
            {
                if (_activePickups[i] != null) _activePickups[i].ForceReturnToPool();
            }
            _activePickups.Clear();

            UIManager.Instance?.HideBossHp();
            UIManager.Instance?.UpdateContextPrompt(string.Empty);
        }
    }
}
