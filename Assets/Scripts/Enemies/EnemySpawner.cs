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
    /// Pooled Multi-Mission Campaign Director managing mission-unique walkable zones,
    /// asymmetric garrisons, holdout waves, collectible Field Intel terminals,
    /// orbital/environmental hazards, and bespoke 5-stage campaign flows across 4 distinct maps.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        public static EnemySpawner Instance { get; private set; }

        [Header("Campaign Mission Identity")]
        [SerializeField] private int missionIndex = 1;

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
        private readonly List<CampaignObjectiveNode> _objectiveNodes = new(6);
        private readonly List<WalkableZoneRect> _walkableZones = new(8);

        private Transform _enemyPoolRoot;
        private Transform _pickupPoolRoot;
        private Transform _hazardRoot;
        private Transform _objectiveRoot;

        private Material _enemyPrimaryMat;
        private Material _enemySecondaryMat;
        private Material _enemyWeaponMat;
        private Material _bossArmorMat;
        private Material _bossTrimMat;
        private Material _bossShieldMat;
        private Material _pickupMat;
        private Material _hazardRingMat;
        private Material _objectiveTerminalMat;
        private Material _objectiveC4Mat;
        private Material _intelTerminalMat;

        private int _stage1KillTarget = 6;
        private int _stage1KillsProgress;
        private int _remainingReinforcements;
        private float _reinforcementTimer;
        private float _stageBeatTimer;
        private float _spirePulseTimer;
        private bool _transitionPending;
        private bool _stage1GatePrompted;
        private bool _vaultHoldoutTriggered;
        private bool _evacHoldoutTriggered;

        public int MissionIndex => missionIndex;

        public void SetMissionIndex(int idx)
        {
            missionIndex = Mathf.Clamp(idx, 1, StoryDirector.CampaignMissionCount);
        }

        public int CurrentStage { get; private set; } = 1;
        public int CurrentWave => CurrentStage;
        public int ActiveEnemyCount => _activeEnemies.Count;
        public IReadOnlyList<EnemyController> ActiveEnemies => _activeEnemies;
        public IReadOnlyList<CampaignObjectiveNode> ObjectiveNodes => _objectiveNodes;

        public CampaignObjectiveNode PrimaryBreachNode { get; private set; }
        public CampaignObjectiveNode WestWingNode { get; private set; }
        public CampaignObjectiveNode EastWingNode { get; private set; }
        public CampaignObjectiveNode ExfilNode { get; private set; }
        public CampaignObjectiveNode IntelNodeA { get; private set; }
        public CampaignObjectiveNode IntelNodeB { get; private set; }

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
            missionIndex = Mathf.Clamp(missionIndex, 1, StoryDirector.CampaignMissionCount);
            StoryDirector.ActiveMissionIndex = missionIndex;

            _enemyPoolRoot = new GameObject("EnemyPoolRoot").transform;
            _enemyPoolRoot.SetParent(transform, false);

            _pickupPoolRoot = new GameObject("PickupPoolRoot").transform;
            _pickupPoolRoot.SetParent(transform, false);

            _hazardRoot = new GameObject("HazardTelegraphRoot").transform;
            _hazardRoot.SetParent(transform, false);

            _objectiveRoot = new GameObject("CampaignObjectivesRoot").transform;
            _objectiveRoot.SetParent(transform, false);

            var enemyColor = missionIndex switch
            {
                2 => Palette.FromHex("#7B4028"),
                3 => Palette.FromHex("#3F4954"),
                4 => Palette.FromHex("#2E313A"),
                _ => Palette.EnemyPrimary
            };
            var bossColor = missionIndex switch
            {
                2 => Palette.FromHex("#503A2C"),
                3 => Palette.FromHex("#443528"),
                4 => Palette.FromHex("#232A38"),
                _ => Palette.GuardTowerDark
            };

            _enemyPrimaryMat = Palette.CreateLitMaterial("Mat_EnemyChassisRuntime", enemyColor, 0.20f, 0.36f);
            _enemySecondaryMat = Palette.CreateLitMaterial("Mat_EnemyVisorRuntime", Palette.Props, 0.35f, 0.40f);
            _enemyWeaponMat = Palette.CreateLitMaterial("Mat_EnemyWeaponRuntime", Palette.Gunmetal, 0.78f, 0.56f);
            _bossArmorMat = Palette.CreateLitMaterial("Mat_BossChassisRuntime", bossColor, 0.38f, 0.44f);
            _bossTrimMat = Palette.CreateLitMaterial("Mat_BossTrimRuntime", Palette.Gunmetal, 0.55f, 0.48f);
            _bossShieldMat = Palette.CreateParticleMaterial("Mat_BossShieldRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            _pickupMat = Palette.CreateLitMaterial("Mat_PickupCoreRuntime", Palette.Accent, 0.25f, 0.45f);
            _hazardRingMat = Palette.CreateParticleMaterial("Mat_HazardRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            _objectiveTerminalMat = Palette.CreateLitMaterial("Mat_ObjectiveTermRuntime", Palette.GuardTowerDark, 0.45f, 0.45f);
            _objectiveC4Mat = Palette.CreateLitMaterial("Mat_ObjectiveC4Runtime", Palette.C4Amber, 0.20f, 0.35f, true, 1.35f);
            _intelTerminalMat = Palette.CreateLitMaterial("Mat_IntelTermRuntime", Palette.M3HeliosCoreCyan, 0.30f, 0.48f, true, 1.15f);

            _enemyPool = new ObjectPool<EnemyController>(
                createFunc: CreateEnemyRig,
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 14,
                maxSize: 38
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

            ConfigureMissionWalkableZones();
            EnsureObjectiveNodesBuilt();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ConfigureMissionWalkableZones()
        {
            _walkableZones.Clear();
            switch (missionIndex)
            {
                case 1:
                    // Mission 1: South Checkpoint -> Y-Split Twin Piers over Water + Cross Bridge -> North Quay
                    _walkableZones.Add(new WalkableZoneRect(-22.8f, 22.8f, -50.8f, -14.2f, 1)); // South Apron
                    _walkableZones.Add(new WalkableZoneRect(-36.8f, 36.8f, -15.5f, -5.2f, 3));  // Pier Y-Split Apron
                    _walkableZones.Add(new WalkableZoneRect(-36.8f, -10.8f, -15.5f, 24.8f, 3)); // West Fuel Pier
                    _walkableZones.Add(new WalkableZoneRect(10.8f, 36.8f, -15.5f, 24.8f, 3));   // East Radar Pier
                    _walkableZones.Add(new WalkableZoneRect(-13.5f, 13.5f, 12.6f, 24.8f, 3));   // Cross-Pier Causeway Bridge
                    _walkableZones.Add(new WalkableZoneRect(-26.8f, 26.8f, 23.5f, 52.8f, 4));   // North Quay & Boat Exfil
                    break;

                case 2:
                    // Mission 2: SW Canyon Entry -> Dual Track Lanes flanking Train #774 + Overpass Crossings + Side Pockets -> NE Tunnel
                    _walkableZones.Add(new WalkableZoneRect(-32.8f, 4.8f, -52.8f, -19.5f, 1));   // SW Canyon Entry
                    _walkableZones.Add(new WalkableZoneRect(-21.2f, -3.6f, -21.5f, 32.8f, 2));   // West Track Lane
                    _walkableZones.Add(new WalkableZoneRect(3.6f, 21.2f, -21.5f, 32.8f, 2));     // East Track Lane
                    _walkableZones.Add(new WalkableZoneRect(-19.5f, 19.5f, -6.2f, -0.5f, 2));    // South Train Gap Crossing
                    _walkableZones.Add(new WalkableZoneRect(-19.5f, 19.5f, 9.5f, 16.5f, 2));     // North Train Coupling Crossing
                    _walkableZones.Add(new WalkableZoneRect(-39.2f, -18.5f, -8.5f, 16.5f, 2));   // West Switch Alcove
                    _walkableZones.Add(new WalkableZoneRect(18.5f, 39.2f, -12.5f, 12.5f, 2));    // East Switch Alcove
                    _walkableZones.Add(new WalkableZoneRect(-13.2f, 26.8f, 31.2f, 56.8f, 4));    // NE Gorge Boss & Tunnel
                    break;

                case 3:
                    // Mission 3: South Airlock -> Central Security Atrium + West Server Vault + East Turbine Hall -> North Command Deck
                    _walkableZones.Add(new WalkableZoneRect(-13.8f, 13.8f, -50.8f, -25.8f, 1));  // South Airlock
                    _walkableZones.Add(new WalkableZoneRect(-17.8f, 17.8f, -26.5f, 16.5f, 2));   // Central Security Atrium
                    _walkableZones.Add(new WalkableZoneRect(-41.2f, -15.5f, -16.8f, 10.8f, 2));  // West Server Vault
                    _walkableZones.Add(new WalkableZoneRect(15.5f, 41.2f, -16.8f, 10.8f, 3));    // East Turbine Hall
                    _walkableZones.Add(new WalkableZoneRect(-20.8f, 20.8f, 15.2f, 50.8f, 4));    // North Command Deck & Lift
                    break;

                default:
                    // Mission 4: South Alpine Bridge -> Wide Octagonal Citadel Courtyard -> North Summit Helipad
                    _walkableZones.Add(new WalkableZoneRect(-9.2f, 9.2f, -52.8f, -25.5f, 1));    // South Alpine Bridge
                    _walkableZones.Add(new WalkableZoneRect(-38.8f, 38.8f, -26.5f, 26.5f, 2));   // Citadel Courtyard Plateau
                    _walkableZones.Add(new WalkableZoneRect(-20.8f, 20.8f, 24.5f, 52.8f, 5));    // North Summit Helipad
                    break;
            }
        }

        private void EnsureObjectiveNodesBuilt()
        {
            if (_objectiveRoot == null) return;
            if (_objectiveNodes.Count == 6 && sabotageTerminalMesh == null) return;

            for (var i = _objectiveNodes.Count - 1; i >= 0; i--)
            {
                if (_objectiveNodes[i] != null) Destroy(_objectiveNodes[i].gameObject);
            }
            _objectiveNodes.Clear();

            var intelA = GetMissionIntelEntry(missionIndex, 0);
            var intelB = GetMissionIntelEntry(missionIndex, 1);

            switch (missionIndex)
            {
                case 1:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M1_Gate1", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(1, 0), new Vector3(0f, 0f, -16.8f), 3.2f, 1.55f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M1_WestFuel", ObjectiveNodeKind.FuelValveSabotage,
                        StoryDirector.GetObjectiveNodeTitle(1, 1), new Vector3(-24.0f, 0f, 8.0f), 3.4f, 1.65f, true);
                    EastWingNode = CreateObjectiveNode("Obj_M1_EastRadar", ObjectiveNodeKind.RadarSabotage,
                        StoryDirector.GetObjectiveNodeTitle(1, 2), new Vector3(24.0f, 0f, 8.0f), 3.4f, 1.65f, true);
                    ExfilNode = CreateObjectiveNode("Obj_M1_BoatExfil", ObjectiveNodeKind.BoatExfilDock,
                        StoryDirector.GetObjectiveNodeTitle(1, 3), new Vector3(18.0f, 0f, 47.0f), 4.0f, 1.25f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M1_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-31.5f, 0f, -4.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M1_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(31.5f, 0f, 18.0f), 2.5f, 0.75f, false, null, intelB.Id);
                    break;

                case 2:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M2_TrainCoupling", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(2, 0), new Vector3(0f, 0f, 13.5f), 3.3f, 1.65f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M2_WestSwitch", ObjectiveNodeKind.RailSwitchLever,
                        StoryDirector.GetObjectiveNodeTitle(2, 1), new Vector3(-32.0f, 0f, 4.0f), 3.3f, 1.45f, false);
                    EastWingNode = CreateObjectiveNode("Obj_M2_EastSwitch", ObjectiveNodeKind.RailSwitchLever,
                        StoryDirector.GetObjectiveNodeTitle(2, 2), new Vector3(32.0f, 0f, 0.0f), 3.3f, 1.45f, false);
                    ExfilNode = CreateObjectiveNode("Obj_M2_TunnelPortal", ObjectiveNodeKind.TunnelBreachExit,
                        StoryDirector.GetObjectiveNodeTitle(2, 3), new Vector3(8.0f, 0f, 52.0f), 4.0f, 1.15f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M2_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-28.5f, 0f, -27.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M2_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(34.5f, 0f, 9.0f), 2.5f, 0.75f, false, null, intelB.Id);
                    break;

                case 3:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M3_AirlockC4", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(3, 0), new Vector3(0f, 0f, -28.5f), 3.2f, 1.50f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M3_ServerHoldout", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(3, 1), new Vector3(-31.0f, 0f, -3.0f), 4.6f, 6.50f, false, HandleHoldoutStarted);
                    EastWingNode = CreateObjectiveNode("Obj_M3_EastTurbine", ObjectiveNodeKind.FuelValveSabotage,
                        StoryDirector.GetObjectiveNodeTitle(3, 2), new Vector3(31.0f, 0f, -3.0f), 3.4f, 1.65f, true);
                    ExfilNode = CreateObjectiveNode("Obj_M3_FreightLift", ObjectiveNodeKind.FreightLiftExit,
                        StoryDirector.GetObjectiveNodeTitle(3, 3), new Vector3(0f, 0f, 45.0f), 4.0f, 1.25f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M3_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-37.0f, 0f, 7.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M3_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(37.0f, 0f, -12.5f), 2.5f, 0.75f, false, null, intelB.Id);
                    break;

                default:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M4_CoreKillSwitch", ObjectiveNodeKind.CoreShutdownConsole,
                        StoryDirector.GetObjectiveNodeTitle(4, 0), new Vector3(0f, 0f, -8.5f), 3.4f, 1.75f, false);
                    WestWingNode = CreateObjectiveNode("Obj_M4_WestPylon", ObjectiveNodeKind.HeliosPylonOverload,
                        StoryDirector.GetObjectiveNodeTitle(4, 1), new Vector3(-31.0f, 0f, 2.0f), 3.6f, 1.70f, true);
                    EastWingNode = CreateObjectiveNode("Obj_M4_EastPylon", ObjectiveNodeKind.HeliosPylonOverload,
                        StoryDirector.GetObjectiveNodeTitle(4, 2), new Vector3(31.0f, 0f, 2.0f), 3.6f, 1.70f, true);
                    ExfilNode = CreateObjectiveNode("Obj_M4_ValkyrieLZ", ObjectiveNodeKind.HelipadEvacLZ,
                        StoryDirector.GetObjectiveNodeTitle(4, 3), new Vector3(0f, 0f, 42.0f), 4.6f, 3.60f, false, HandleHoldoutStarted);
                    IntelNodeA = CreateObjectiveNode("Obj_M4_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-31.0f, 0f, -18.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M4_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(31.0f, 0f, 18.5f), 2.5f, 0.75f, false, null, intelB.Id);
                    break;
            }
        }

        private static IntelLogEntry GetMissionIntelEntry(int mIdx, int slotIndex)
        {
            var found = 0;
            foreach (var entry in StoryDirector.IntelCatalog)
            {
                if (entry.MissionIndex != mIdx) continue;
                if (found == slotIndex) return entry;
                found++;
            }
            return StoryDirector.IntelCatalog[0];
        }

        private CampaignObjectiveNode CreateObjectiveNode(
            string goName,
            ObjectiveNodeKind kind,
            string title,
            Vector3 pos,
            float radius,
            float armSeconds,
            bool explode,
            System.Action<CampaignObjectiveNode> onStarted = null,
            string intelId = "")
        {
            var go = new GameObject(goName);
            go.transform.SetParent(_objectiveRoot, false);
            var node = go.AddComponent<CampaignObjectiveNode>();
            var mat = kind == ObjectiveNodeKind.IntelTerminal ? _intelTerminalMat : _objectiveTerminalMat;
            node.Configure(kind, title, pos, radius, armSeconds, explode, sabotageTerminalMesh, c4ChargeMesh, mat, _objectiveC4Mat, HandleObjectiveCompleted, onStarted, intelId);
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

            // Volkov Reactive Riot Shield Ring
            var shieldGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shieldGo.name = "BossShieldRing";
            shieldGo.transform.SetParent(root.transform, false);
            shieldGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            shieldGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shieldGo.transform.localScale = new Vector3(3.2f, 3.2f, 1f);
            var sc = shieldGo.GetComponent<Collider>();
            if (sc != null) Destroy(sc);
            var smr = shieldGo.GetComponent<MeshRenderer>();
            if (smr != null)
            {
                smr.sharedMaterial = _bossShieldMat;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var smpb = new MaterialPropertyBlock();
                smpb.SetColor(Color1, Palette.C4Amber);
                smr.SetPropertyBlock(smpb);
            }
            shieldGo.SetActive(false);

            // Goliath Railgun Laser Telegraph Line
            var laserGo = new GameObject("RailLaserTelegraph");
            laserGo.transform.SetParent(root.transform, false);
            var lr = laserGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.12f;
            lr.endWidth = 0.06f;
            lr.useWorldSpace = true;
            lr.material = _hazardRingMat;
            lr.enabled = false;

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot, rightArmPivot, leftLegPivot.transform, rightLegPivot.transform, reactorPivot.transform);
            motion.BindWeaponArmIk(leftElbowPivot, rightElbowPivot, weaponAnchor.transform, ak74LeftGrip, ak74RightGrip);
            ctrl.BindComponents(
                cc, motion, root.GetComponentsInChildren<MeshRenderer>(true),
                visualRoot, chaserToggle, heavyToggle, bossGroup, turretPivot.transform,
                leftArmPivot, rightArmPivot, weaponAnchor.transform,
                ak74Obj, ak74LeftGrip, ak74RightGrip, ak74Muzzle,
                pkmObj, pkmLeftGrip, pkmRightGrip, pkmMuzzle,
                bossMuzzleTip.transform, shieldGo, lr);

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
            _stage1GatePrompted = false;
            CurrentStage = Mathf.Clamp(stageNum, 1, StoryDirector.FinalCampaignStage);

            if (CurrentStage >= 2 && (missionIndex == 2 || missionIndex == 4))
            {
                CutsceneDirector.Instance?.OpenGateImmediate(1);
            }
            if (CurrentStage >= 3 && (missionIndex == 1 || missionIndex == 3))
            {
                CutsceneDirector.Instance?.OpenGateImmediate(1);
            }
            if ((CurrentStage >= 4 && missionIndex != 4) || (CurrentStage >= 5 && missionIndex == 4))
            {
                CutsceneDirector.Instance?.OpenGateImmediate(2);
            }

            switch (missionIndex)
            {
                case 1:
                    SetupMission1Stage(CurrentStage);
                    break;
                case 2:
                    SetupMission2Stage(CurrentStage);
                    break;
                case 3:
                    SetupMission3Stage(CurrentStage);
                    break;
                default:
                    SetupMission4Stage(CurrentStage);
                    break;
            }

            if (CurrentStage > 1)
            {
                StoryDirector.NotifyStageStarted(CurrentStage);
            }
            SyncMissionHud();
        }

        private void SetupMission1Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    ResetMainNodes();
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(-7.0f, 0f, -34f), new Vector3(7.0f, 0f, -34f),
                        new Vector3(-11.5f, 0f, -26f), new Vector3(11.5f, 0f, -26f),
                        new Vector3(-3.0f, 0f, -21f), new Vector3(3.0f, 0f, -21f)
                    }, heavyIndices: new[] { 4 }, alertedCount: 4);
                    break;
                case 2:
                    PrimaryBreachNode?.SetState(true, false);
                    StoryDirector.NotifyGate1C4Ready();
                    UIManager.Instance?.ShowMilestoneBanner("GATE CLEAR // PLANT C4 AT " + StoryDirector.GetObjectiveNodeTitle(1, 0));
                    break;
                case 3:
                    SabotageCompletedCount = 0;
                    PrimaryBreachNode?.SetState(false, true);
                    WestWingNode?.SetState(true, false);
                    EastWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    _remainingReinforcements = 2;
                    _reinforcementTimer = 8.0f;
                    SpawnFormation(new[]
                    {
                        new Vector3(-22f, 0f, -4f), new Vector3(-28f, 0f, 5f), new Vector3(-18f, 0f, 14f),
                        new Vector3(22f, 0f, -4f), new Vector3(28f, 0f, 5f), new Vector3(18f, 0f, 14f),
                        new Vector3(0f, 0f, 18.5f)
                    }, heavyIndices: new[] { 1, 4 }, alertedCount: 0);
                    break;
                case 4:
                    CutsceneDirector.Instance?.OpenGateImmediate(2);
                    SpawnFormation(new[]
                    {
                        new Vector3(-11f, 0f, 34f), new Vector3(11f, 0f, 34f),
                        new Vector3(-15f, 0f, 44f), new Vector3(15f, 0f, 44f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 4);
                    SpawnBossAt(new Vector3(0f, 0f, 40f));
                    ShowBossMilestone();
                    break;
                case 5:
                    ExfilNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("PURGE INBOUND // SPRINT TO GUNBOAT DOCK!");
                    break;
            }
        }

        private void SetupMission2Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    ResetMainNodes();
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(-22f, 0f, -36f), new Vector3(-8f, 0f, -35f),
                        new Vector3(-25f, 0f, -28f), new Vector3(-14f, 0f, -26f),
                        new Vector3(-4f, 0f, -24f), new Vector3(-10f, 0f, -22f),
                        new Vector3(-20f, 0f, -23f)
                    }, heavyIndices: new[] { 3, 5 }, alertedCount: 4);
                    break;
                case 2:
                    SabotageCompletedCount = 0;
                    WestWingNode?.SetState(true, false);
                    EastWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    CutsceneDirector.Instance?.OpenGateImmediate(1);
                    SpawnFormation(new[]
                    {
                        new Vector3(-12f, 0f, -10f), new Vector3(-29f, 0f, 0f), new Vector3(-12f, 0f, 18f),
                        new Vector3(12f, 0f, -10f), new Vector3(29f, 0f, -4f), new Vector3(12f, 0f, 18f),
                        new Vector3(-11f, 0f, 4f), new Vector3(11f, 0f, 8f)
                    }, heavyIndices: new[] { 1, 4, 7 }, alertedCount: 2);
                    break;
                case 3:
                    PrimaryBreachNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("SWITCHES LOCKED // PLANT C4 ON TRAIN #774!");
                    SpawnFormation(new[]
                    {
                        new Vector3(-9f, 0f, 21f), new Vector3(9f, 0f, 21f)
                    }, heavyIndices: new[] { 1 }, alertedCount: 2);
                    break;
                case 4:
                    CutsceneDirector.Instance?.OpenGateImmediate(2);
                    SpawnFormation(new[]
                    {
                        new Vector3(-6f, 0f, 38f), new Vector3(20f, 0f, 38f),
                        new Vector3(18f, 0f, 48f)
                    }, heavyIndices: new[] { 1 }, alertedCount: 3);
                    SpawnBossAt(new Vector3(8f, 0f, 43f));
                    ShowBossMilestone();
                    break;
                case 5:
                    ExfilNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("VOLKOV EXPOSED // BREACH NORTH GORGE TUNNEL!");
                    break;
            }
        }

        private void SetupMission3Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    ResetMainNodes();
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(-8f, 0f, -38f), new Vector3(8f, 0f, -38f),
                        new Vector3(-6f, 0f, -31f), new Vector3(6f, 0f, -31f),
                        new Vector3(0f, 0f, -33f), new Vector3(0f, 0f, -29f)
                    }, heavyIndices: new[] { 4 }, alertedCount: 4);
                    break;
                case 2:
                    _vaultHoldoutTriggered = false;
                    PrimaryBreachNode?.SetState(false, true);
                    WestWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(-9f, 0f, -15f), new Vector3(9f, 0f, -15f),
                        new Vector3(-11f, 0f, 4f), new Vector3(-24f, 0f, -6f),
                        new Vector3(-29f, 0f, 4f), new Vector3(-35f, 0f, -8f)
                    }, heavyIndices: new[] { 2, 4 }, alertedCount: 2);
                    break;
                case 3:
                    EastWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(9f, 0f, -6f), new Vector3(24f, 0f, -2f),
                        new Vector3(31f, 0f, 5f), new Vector3(34f, 0f, -9f),
                        new Vector3(11f, 0f, 8f)
                    }, heavyIndices: new[] { 1, 3 }, alertedCount: 2);
                    break;
                case 4:
                    CutsceneDirector.Instance?.OpenGateImmediate(2);
                    SpawnFormation(new[]
                    {
                        new Vector3(-11f, 0f, 25f), new Vector3(11f, 0f, 25f),
                        new Vector3(-14f, 0f, 36f), new Vector3(14f, 0f, 36f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 4);
                    SpawnBossAt(new Vector3(0f, 0f, 32f));
                    ShowBossMilestone();
                    break;
                case 5:
                    ExfilNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("AEGIS NET RESTORED // BOARD FREIGHT LIFT!");
                    break;
            }
        }

        private void SetupMission4Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    ResetMainNodes();
                    UnlockUncollectedIntel();
                    SpawnFormation(new[]
                    {
                        new Vector3(-5.5f, 0f, -40f), new Vector3(5.5f, 0f, -40f),
                        new Vector3(-6.0f, 0f, -33f), new Vector3(6.0f, 0f, -33f),
                        new Vector3(-3.5f, 0f, -28f), new Vector3(3.5f, 0f, -28f)
                    }, heavyIndices: new[] { 4, 5 }, alertedCount: 6);
                    break;
                case 2:
                    SabotageCompletedCount = 0;
                    _spirePulseTimer = 4.5f;
                    WestWingNode?.SetState(true, false);
                    EastWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    CutsceneDirector.Instance?.OpenGateImmediate(1);
                    SpawnFormation(new[]
                    {
                        new Vector3(-16f, 0f, -12f), new Vector3(-29f, 0f, -4f), new Vector3(-26f, 0f, 10f),
                        new Vector3(16f, 0f, -12f), new Vector3(29f, 0f, -4f), new Vector3(26f, 0f, 10f),
                        new Vector3(0f, 0f, 15f)
                    }, heavyIndices: new[] { 1, 4, 6 }, alertedCount: 2);
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-13f, 0f, 8f), new Vector3(13f, 0f, 8f),
                        new Vector3(-16f, 0f, 20f), new Vector3(16f, 0f, 20f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 4);
                    SpawnBossAt(new Vector3(0f, 0f, 14f));
                    ShowBossMilestone();
                    break;
                case 4:
                    PrimaryBreachNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("RADEK DOWN // HIT HELIOS KILL-SWITCH AT SPIRE!");
                    break;
                case 5:
                    _evacHoldoutTriggered = false;
                    CutsceneDirector.Instance?.OpenGateImmediate(2);
                    ExfilNode?.SetState(true, false);
                    UIManager.Instance?.ShowMilestoneBanner("VALKYRIE-6 INBOUND // POP FLARE & HOLD LZ!");
                    break;
            }
        }

        private void ResetMainNodes()
        {
            PrimaryBreachNode?.SetState(false, false);
            WestWingNode?.SetState(false, false);
            EastWingNode?.SetState(false, false);
            ExfilNode?.SetState(false, false);
        }

        private void UnlockUncollectedIntel()
        {
            if (IntelNodeA != null && !IntelNodeA.IsCompleted) IntelNodeA.SetState(true, false);
            if (IntelNodeB != null && !IntelNodeB.IsCompleted) IntelNodeB.SetState(true, false);
        }

        private void SpawnFormation(Vector3[] positions, int[] heavyIndices, int alertedCount)
        {
            _stage1KillsProgress = 0;
            _stage1KillTarget = positions.Length;
            var heavies = new HashSet<int>(heavyIndices);
            for (var i = 0; i < positions.Length; i++)
            {
                var arch = heavies.Contains(i) ? EnemyArchetype.Heavy : EnemyArchetype.Chaser;
                var e = SpawnEnemyAt(arch, positions[i]);
                e.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                e.SetGarrisonAlert(i < alertedCount, 28f);
            }
        }

        private void ShowBossMilestone()
        {
            StoryDirector.NotifyBossSpawned(CurrentStage);
            UIManager.Instance?.ShowMilestoneBanner("HVT // " + StoryDirector.GetBossCallsign(CurrentStage));
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
            {
                GameManager.Instance.TriggerCutscene(CutsceneType.FinalBossIntro);
            }
        }

        private void HandleHoldoutStarted(CampaignObjectiveNode node)
        {
            if (missionIndex == 3 && node == WestWingNode && !_vaultHoldoutTriggered)
            {
                _vaultHoldoutTriggered = true;
                _remainingReinforcements = 4;
                _reinforcementTimer = 1.6f;
                StoryDirector.NotifyVaultHoldoutStarted();
                UIManager.Instance?.ShowMilestoneBanner("DECRYPTING // HOLD WEST SERVER VAULT!");
            }
            else if (missionIndex == 4 && node == ExfilNode && !_evacHoldoutTriggered)
            {
                _evacHoldoutTriggered = true;
                var isBroadcast = GameManager.Instance != null && GameManager.Instance.ActiveEndingChoice == CampaignEndingKind.BroadcastBlackout;
                if (isBroadcast)
                {
                    UIManager.Instance?.TriggerThreatWarning("!! BLACKOUT UPLINK LIVE — AEGIS CLEANER TEAMS CONVERGING !!", 3.2f);
                    SpawnFormation(new[]
                    {
                        new Vector3(-13f, 0f, 34f), new Vector3(13f, 0f, 34f),
                        new Vector3(-16f, 0f, 41f), new Vector3(16f, 0f, 41f),
                        new Vector3(-14f, 0f, 48f), new Vector3(14f, 0f, 48f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 6);
                }
                else
                {
                    SpawnFormation(new[]
                    {
                        new Vector3(-11f, 0f, 34f), new Vector3(11f, 0f, 34f),
                        new Vector3(-13f, 0f, 46f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 3);
                }
            }
        }

        private void Update()
        {
            if (_transitionPending || GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Reinforcements during M1 Pier Sabotage & M3 Server Vault Holdout
            if (_remainingReinforcements > 0)
            {
                _reinforcementTimer -= dt;
                if (_reinforcementTimer <= 0f && _activeEnemies.Count < 10)
                {
                    _remainingReinforcements--;
                    _reinforcementTimer = missionIndex == 3 ? 2.4f : 7.5f;
                    var spawnPos = missionIndex == 3
                        ? new Vector3(-14f, 0f, UnityEngine.Random.Range(-7f, 4f))
                        : new Vector3(UnityEngine.Random.Range(-10f, 10f), 0f, 18f);
                    var esc = SpawnEnemyAt(_remainingReinforcements == 0 ? EnemyArchetype.Heavy : EnemyArchetype.Chaser, spawnPos);
                    esc.SetGarrisonAlert(true, 60f);
                }
            }

            // Mission 4 Central HELIOS Spire environmental pulse hazard during Stage 2
            if (missionIndex == 4 && CurrentStage == 2 && PlayerController.Instance != null && !PlayerController.Instance.IsDead)
            {
                _spirePulseTimer -= dt;
                if (_spirePulseTimer <= 0f)
                {
                    _spirePulseTimer = 5.4f;
                    SpawnHazardTelegraph(PlayerController.Instance.transform.position, 2.4f, 18f);
                }
            }

            // Stage completion checks per mission
            switch (missionIndex)
            {
                case 1:
                    UpdateMission1Progression(dt);
                    break;
                case 2:
                    UpdateMission2Progression(dt);
                    break;
                case 3:
                    UpdateMission3Progression(dt);
                    break;
                default:
                    UpdateMission4Progression(dt);
                    break;
            }
        }

        private void UpdateMission1Progression(float dt)
        {
            if (CurrentStage == 1 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.75f)
                {
                    _stageBeatTimer = 0f;
                    BeginCampaignStage(2);
                }
            }
            else if (CurrentStage == 4 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.85f)
                {
                    _stageBeatTimer = 0f;
                    BeginCampaignStage(5);
                }
            }
        }

        private void UpdateMission2Progression(float dt)
        {
            if (CurrentStage == 1 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.75f)
                {
                    _stageBeatTimer = 0f;
                    _transitionPending = true;
                    CutsceneDirector.Instance?.OpenGateImmediate(1);
                    VFXManager.Instance?.Play(VfxKind.EnemyDeath, new Vector3(-10f, 1.5f, -20f), Palette.RocketOrange);
                    FeelManager.Instance?.TriggerCameraShake(0.32f);
                    AudioManager.Instance?.PlayEnemyExplode();
                    BeginCampaignStage(2);
                }
            }
            else if (CurrentStage == 4 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.85f)
                {
                    _stageBeatTimer = 0f;
                    BeginCampaignStage(5);
                }
            }
        }

        private void UpdateMission3Progression(float dt)
        {
            if (CurrentStage == 1 && !_stage1GatePrompted && _activeEnemies.Count == 0)
            {
                _stage1GatePrompted = true;
                PrimaryBreachNode?.SetState(true, false);
                StoryDirector.NotifyGate1C4Ready();
                UIManager.Instance?.ShowMilestoneBanner("AIRLOCK CLEAR // BREACH BULKHEAD [E]");
                SyncMissionHud();
            }
            else if (CurrentStage == 4 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.85f)
                {
                    _stageBeatTimer = 0f;
                    BeginCampaignStage(5);
                }
            }
        }

        private void UpdateMission4Progression(float dt)
        {
            if (CurrentStage == 1 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.75f)
                {
                    _stageBeatTimer = 0f;
                    _transitionPending = true;
                    CutsceneDirector.Instance?.OpenGateImmediate(1);
                    VFXManager.Instance?.Play(VfxKind.EnemyDeath, new Vector3(0f, 1.6f, -26f), Palette.M3HeliosCoreCyan);
                    FeelManager.Instance?.TriggerCameraShake(0.36f);
                    AudioManager.Instance?.PlayEnemyExplode();
                    BeginCampaignStage(2);
                }
            }
            else if (CurrentStage == 3 && _activeEnemies.Count == 0)
            {
                _stageBeatTimer += dt;
                if (_stageBeatTimer >= 0.85f)
                {
                    _stageBeatTimer = 0f;
                    BeginCampaignStage(4);
                }
            }
        }

        private void HandleObjectiveCompleted(CampaignObjectiveNode node)
        {
            // Optional Collectible Field Intel Terminal
            if (node.Kind == ObjectiveNodeKind.IntelTerminal)
            {
                foreach (var entry in StoryDirector.IntelCatalog)
                {
                    if (entry.Id != node.IntelId) continue;
                    var newlyFound = SaveManager.EnsureInstance().UnlockIntelLog(entry.Id);
                    GameManager.Instance?.AddRunCredits(newlyFound ? entry.RewardCredits : 30);
                    StoryDirector.NotifyIntelDiscovered(entry);
                    UIManager.Instance?.ShowMilestoneBanner($"INTEL // {entry.Header}");
                    break;
                }
                return;
            }

            if (node == ExfilNode && CurrentStage == 5)
            {
                _transitionPending = true;
                // Clear remaining final LZ rushers cleanly on Mission 4 extraction flare
                for (var i = _activeEnemies.Count - 1; i >= 0; i--)
                {
                    if (_activeEnemies[i] != null) _activeEnemies[i].ForceReturnToPool();
                }
                _activeEnemies.Clear();
                GameManager.Instance?.CompleteCampaignVictory();
                return;
            }

            switch (missionIndex)
            {
                case 1:
                    if (node == PrimaryBreachNode && CurrentStage == 2)
                    {
                        TriggerGateBreachTransition(3, "TWIN HARBOR PIERS");
                    }
                    else if ((node == WestWingNode || node == EastWingNode) && CurrentStage == 3)
                    {
                        SabotageCompletedCount++;
                        StoryDirector.NotifySabotageProgress(SabotageCompletedCount, 2, node.DisplayTitle);
                        SyncMissionHud();
                        if (SabotageCompletedCount >= 2)
                        {
                            PlayerController.Instance?.AddFragGrenades(1);
                            BeginCampaignStage(4);
                        }
                    }
                    break;

                case 2:
                    if ((node == WestWingNode || node == EastWingNode) && CurrentStage == 2)
                    {
                        SabotageCompletedCount++;
                        StoryDirector.NotifySabotageProgress(SabotageCompletedCount, 2, node.DisplayTitle);
                        SyncMissionHud();
                        if (SabotageCompletedCount >= 2)
                        {
                            BeginCampaignStage(3);
                        }
                    }
                    else if (node == PrimaryBreachNode && CurrentStage == 3)
                    {
                        PlayerController.Instance?.AddFragGrenades(1);
                        TriggerGateBreachTransition(4, "NORTH GORGE // GOLIATH-02");
                    }
                    break;

                case 3:
                    if (node == PrimaryBreachNode && CurrentStage == 1)
                    {
                        TriggerGateBreachTransition(2, "CENTRAL SECURITY ATRIUM");
                    }
                    else if (node == WestWingNode && CurrentStage == 2)
                    {
                        _remainingReinforcements = 0;
                        BeginCampaignStage(3);
                    }
                    else if (node == EastWingNode && CurrentStage == 3)
                    {
                        PlayerController.Instance?.AddFragGrenades(1);
                        BeginCampaignStage(4);
                    }
                    break;

                default:
                    if ((node == WestWingNode || node == EastWingNode) && CurrentStage == 2)
                    {
                        SabotageCompletedCount++;
                        StoryDirector.NotifySabotageProgress(SabotageCompletedCount, 2, node.DisplayTitle);
                        SyncMissionHud();
                        if (SabotageCompletedCount >= 2)
                        {
                            PlayerController.Instance?.AddFragGrenades(1);
                            BeginCampaignStage(3);
                        }
                    }
                    else if (node == PrimaryBreachNode && CurrentStage == 4)
                    {
                        if (GameManager.Instance != null)
                        {
                            GameManager.Instance.PresentAct4CoreChoice();
                        }
                        else
                        {
                            CompleteAct4CoreDecision(CampaignEndingKind.SilentKillSwitch);
                        }
                    }
                    break;
            }
        }

        public void CompleteAct4CoreDecision(CampaignEndingKind choice)
        {
            var banner = choice == CampaignEndingKind.BroadcastBlackout
                ? "BLACKOUT LEDGER BROADCAST // REACH LZ VALKYRIE-6!"
                : "SPIRE DARK // PROCEED TO LZ VALKYRIE-6";
            TriggerGateBreachTransition(5, banner);
        }

        private void TriggerGateBreachTransition(int nextStage, string bannerText)
        {
            _transitionPending = true;
            PlayerController.Instance?.AddFragGrenades(1);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerCutscene(CutsceneType.Act2GateBreach, () =>
                {
                    BeginCampaignStage(nextStage);
                    UIManager.Instance?.ShowMilestoneBanner(bannerText);
                });
            }
            else
            {
                BeginCampaignStage(nextStage);
            }
        }

        public Vector3 GetActiveObjectiveWorldPoint()
        {
            if (PrimaryBreachNode != null && PrimaryBreachNode.IsUnlocked) return PrimaryBreachNode.transform.position;
            var westAvail = WestWingNode != null && WestWingNode.IsUnlocked;
            var eastAvail = EastWingNode != null && EastWingNode.IsUnlocked;
            if (westAvail && eastAvail && PlayerController.Instance != null)
            {
                var dW = (WestWingNode.transform.position - PlayerController.Instance.transform.position).sqrMagnitude;
                var dE = (EastWingNode.transform.position - PlayerController.Instance.transform.position).sqrMagnitude;
                return dW <= dE ? WestWingNode.transform.position : EastWingNode.transform.position;
            }
            if (westAvail) return WestWingNode.transform.position;
            if (eastAvail) return EastWingNode.transform.position;
            if (ExfilNode != null && ExfilNode.IsUnlocked) return ExfilNode.transform.position;
            if (_activeEnemies.Count > 0 && _activeEnemies[0] != null) return _activeEnemies[0].transform.position;
            return Vector3.zero;
        }

        public Vector3 ClampPositionToUnlockedSectors(Vector3 pos)
        {
            if (_walkableZones.Count == 0) ConfigureMissionWalkableZones();

            var bestDistSq = float.MaxValue;
            var bestPoint = pos;

            foreach (var z in _walkableZones)
            {
                if (!z.IsActiveForStage(CurrentStage)) continue;
                if (z.Contains(pos)) return pos;

                var clamped = z.ClampPoint(pos);
                var dx = clamped.x - pos.x;
                var dz = clamped.z - pos.z;
                var dSq = dx * dx + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestPoint = clamped;
                }
            }
            return bestPoint;
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
            if (PlayerController.Instance != null && !PlayerController.Instance.IsDead)
            {
                var d = worldPos - PlayerController.Instance.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= 110f)
                {
                    UIManager.Instance?.TriggerThreatWarning("!! RWR ALERT: INCOMING ARTILLERY / ORBITAL IMPACT !!", 1.85f);
                }
            }
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
                    player.TakeDamage(dmg, center);
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
                        SpawnPickupAt(PickupKind.CreditCoin, center + offset, 30);
                    }
                }
                else
                {
                    var roll = UnityEngine.Random.value;
                    switch (roll)
                    {
                        case < 0.24f:
                            SpawnPickupAt(PickupKind.Medkit, enemy.transform.position);
                            break;
                        case < 0.58f:
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
                PrimaryBreachNode != null && PrimaryBreachNode.IsUnlocked);

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
            _spirePulseTimer = 4.5f;
            SabotageCompletedCount = 0;
            _stage1GatePrompted = false;
            _vaultHoldoutTriggered = false;
            _evacHoldoutTriggered = false;

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
