using System;
using System.Collections;
using System.Collections.Generic;
using Audio;
using Camera;
using Character;
using Core;
using Feel;
using Pickups;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace Enemies
{
    /// <summary>
    /// Pooled multi-stage campaign mission coordinator (5 Acts x 8 Tactical Stages):
    /// manages walkable sector clamps, 3 blackbox intel terminals per act, multi-wave QRF
    /// reinforcements, interactive last-stand holdouts, boss showdowns, and hazard telegraphs.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public static EnemySpawner Instance { get; private set; }

        private static readonly int Color1 = Shader.PropertyToID("_BaseColor");

        private readonly struct WalkableZoneRect
        {
            public readonly float MinX, MaxX, MinZ, MaxZ;
            public readonly int UnlockStage;

            public WalkableZoneRect(float minX, float maxX, float minZ, float maxZ, int unlockStage)
            {
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                UnlockStage = unlockStage;
            }

            public bool IsActiveForStage(int stage) => stage >= UnlockStage;
            public bool Contains(Vector3 p) => p.x >= MinX && p.x <= MaxX && p.z >= MinZ && p.z <= MaxZ;

            public Vector3 ClampPoint(Vector3 p) => new(
                Mathf.Clamp(p.x, MinX, MaxX),
                p.y,
                Mathf.Clamp(p.z, MinZ, MaxZ));
        }

        [Header("Mission Identity")]
        [SerializeField] private int missionIndex = 1;

        [Header("Enemy & Pickup Geometry")]
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
        [SerializeField] private Mesh medkitMesh;
        [SerializeField] private Mesh creditCoinMesh;
        [SerializeField] private Mesh sabotageTerminalMesh;
        [SerializeField] private Mesh c4ChargeMesh;
        [SerializeField] private Mesh sniperRifleMesh;
        [SerializeField] private Mesh riotShieldMesh;
        [SerializeField] private Mesh fieldPackMesh;
        [SerializeField] private Mesh armorPauldronMesh;
        [SerializeField] private Mesh munitionCrateMesh;
        [SerializeField] private Mesh enemyVisorMesh;
        [SerializeField] private Mesh enemyMagAkCurvedMesh;
        [SerializeField] private Mesh enemyMagBoxDrumMesh;
        [SerializeField] private Material enemyMagAkMat;
        [SerializeField] private Material enemyMagDrumMat;

        private ObjectPool<EnemyController> _enemyPool;
        private ObjectPool<PickupItem> _pickupPool;

        private readonly List<EnemyController> _activeEnemies = new();
        private readonly List<PickupItem> _activePickups = new();
        private readonly List<GameObject> _activeHazards = new();
        private readonly List<CampaignObjectiveNode> _objectiveNodes = new(8);
        private readonly List<WalkableZoneRect> _walkableZones = new(10);

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
        private Material _laserSightMat;

        private int _stageKillsProgress;
        private int _stageKillTarget = 6;
        private int _remainingReinforcements;
        private float _reinforcementTimer;
        private float _reinforcementInterval = 5.5f;
        private int _reinforcementBatchSize = 2;
        private Vector3[] _reinforcementPoints = Array.Empty<Vector3>();
        private float _stageBeatTimer;
        private float _hazardPulseTimer;
        private float _hazardPulsePeriod;
        private bool _transitionPending;
        private bool _holdoutWavesStarted;

        public int MissionIndex => missionIndex;

        public void SetMissionIndex(int idx)
        {
            missionIndex = Mathf.Clamp(idx, 1, StoryDirector.CampaignMissionCount);
        }

        public int CurrentStage { get; private set; } = 1;
        public int CurrentWave => CurrentStage;
        public string ActiveEnvironmentalHazardName { get; private set; } = string.Empty;
        public int ActiveEnemyCount => _activeEnemies.Count;
        public IReadOnlyList<EnemyController> ActiveEnemies => _activeEnemies;
        public IReadOnlyList<CampaignObjectiveNode> ObjectiveNodes => _objectiveNodes;

        public CampaignObjectiveNode PrimaryBreachNode { get; private set; }
        public CampaignObjectiveNode WestWingNode { get; private set; }
        public CampaignObjectiveNode EastWingNode { get; private set; }
        public CampaignObjectiveNode DefenseHoldNode { get; private set; }
        public CampaignObjectiveNode ExfilNode { get; private set; }
        public CampaignObjectiveNode IntelNodeA { get; private set; }
        public CampaignObjectiveNode IntelNodeB { get; private set; }
        public CampaignObjectiveNode IntelNodeC { get; private set; }

        public int SabotageCompletedCount { get; private set; }

        public void BindMeshes(
            Mesh chaserTorso, Mesh chaserHead,
            Mesh heavyTorso, Mesh heavyCrown,
            Mesh bossTorso, Mesh bossTurret,
            Mesh limb,
            Mesh medkit, Mesh creditCoin,
            Mesh enemyArm = null,
            Mesh enemyUpperArm = null,
            Mesh enemyForearm = null,
            Mesh enemyAk74 = null,
            Mesh enemyPkm = null,
            Mesh sabotageTerminal = null,
            Mesh c4Charge = null,
            Mesh sniperRifle = null,
            Mesh riotShield = null,
            Mesh fieldPack = null,
            Mesh armorPauldron = null,
            Mesh munitionCrate = null,
            Mesh enemyVisor = null,
            Mesh enemyMagAk = null,
            Mesh enemyMagDrum = null,
            Material magAkMat = null,
            Material magDrumMat = null)
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
            medkitMesh = medkit;
            creditCoinMesh = creditCoin;
            sabotageTerminalMesh = sabotageTerminal;
            c4ChargeMesh = c4Charge;
            if (sniperRifle != null) sniperRifleMesh = sniperRifle;
            if (riotShield != null) riotShieldMesh = riotShield;
            if (fieldPack != null) fieldPackMesh = fieldPack;
            if (armorPauldron != null) armorPauldronMesh = armorPauldron;
            if (munitionCrate != null) munitionCrateMesh = munitionCrate;
            if (enemyVisor != null) enemyVisorMesh = enemyVisor;
            if (enemyMagAk != null) enemyMagAkCurvedMesh = enemyMagAk;
            if (enemyMagDrum != null) enemyMagBoxDrumMesh = enemyMagDrum;
            if (magAkMat != null) enemyMagAkMat = magAkMat;
            if (magDrumMat != null) enemyMagDrumMat = magDrumMat;
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
                5 => Palette.FromHex("#1D222B"),
                _ => Palette.EnemyPrimary
            };
            var bossColor = missionIndex switch
            {
                2 => Palette.FromHex("#503A2C"),
                3 => Palette.FromHex("#443528"),
                4 => Palette.FromHex("#232A38"),
                5 => Palette.FromHex("#34181E"),
                _ => Palette.GuardTowerDark
            };

            _enemyPrimaryMat = Palette.CreateLitMaterial("Mat_EnemyChassisRuntime", enemyColor, 0.20f, 0.36f);
            _enemySecondaryMat = Palette.CreateLitMaterial("Mat_EnemyVisorRuntime", Palette.Props, 0.35f, 0.40f);
            _enemyWeaponMat = Palette.CreateLitMaterial("Mat_EnemyWeaponRuntime", new Color(0.48f, 0.52f, 0.56f, 1f), 0.35f, 0.50f);
            _bossArmorMat = Palette.CreateLitMaterial("Mat_BossChassisRuntime", bossColor, 0.38f, 0.44f);
            _bossTrimMat = Palette.CreateLitMaterial("Mat_BossTrimRuntime", Palette.Gunmetal, 0.55f, 0.48f);
            _bossShieldMat = Palette.CreateParticleMaterial("Mat_BossShieldRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            _pickupMat = Palette.CreateLitMaterial("Mat_PickupCoreRuntime", Palette.Accent, 0.25f, 0.45f);
            _hazardRingMat = Palette.CreateParticleMaterial("Mat_HazardRingRuntime", UISpriteFactory.GetTelegraphRingTexture());
            _laserSightMat = Palette.CreateParticleMaterial("Mat_EnemyLaserSightRuntime");
            _objectiveTerminalMat = Palette.CreateLitMaterial("Mat_ObjectiveTermRuntime", Palette.GuardTowerDark, 0.45f, 0.45f);
            _objectiveC4Mat = Palette.CreateLitMaterial("Mat_ObjectiveC4Runtime", Palette.C4Amber, 0.20f, 0.35f, true, 1.35f);
            _intelTerminalMat = Palette.CreateLitMaterial("Mat_IntelTermRuntime", Palette.M3HeliosCoreCyan, 0.30f, 0.48f, true, 1.15f);

            _enemyPool = new ObjectPool<EnemyController>(
                createFunc: CreateEnemyRig,
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 18,
                maxSize: 48
            );

            _pickupPool = new ObjectPool<PickupItem>(
                createFunc: CreatePickupRig,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 20,
                maxSize: 54
            );

            ConfigureMissionWalkableZones();
            EnsureObjectiveNodesBuilt();
        }

        private void Start()
        {
            PrewarmPools();
        }

        private void PrewarmPools()
        {
            if (_enemyPool != null)
            {
                var preEnemies = new EnemyController[10];
                for (var i = 0; i < preEnemies.Length; i++) preEnemies[i] = _enemyPool.Get();
                for (var i = 0; i < preEnemies.Length; i++) _enemyPool.Release(preEnemies[i]);
            }

            if (_pickupPool != null)
            {
                var prePickups = new PickupItem[10];
                for (var i = 0; i < prePickups.Length; i++) prePickups[i] = _pickupPool.Get();
                for (var i = 0; i < prePickups.Length; i++) _pickupPool.Release(prePickups[i]);
            }
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
                    _walkableZones.Add(new WalkableZoneRect(-22.8f, 22.8f, -50.8f, -14.2f, 1));
                    _walkableZones.Add(new WalkableZoneRect(-36.8f, 36.8f, -15.5f, -5.2f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-36.8f, -10.8f, -15.5f, 24.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(10.8f, 36.8f, -15.5f, 24.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-13.5f, 13.5f, 12.6f, 24.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-26.8f, 26.8f, 23.5f, 52.8f, 6));
                    break;

                case 2:
                    _walkableZones.Add(new WalkableZoneRect(-32.8f, 4.8f, -52.8f, -19.5f, 1));
                    _walkableZones.Add(new WalkableZoneRect(-21.2f, -3.6f, -21.5f, 32.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(3.6f, 21.2f, -21.5f, 32.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-19.5f, 19.5f, -6.2f, -0.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-19.5f, 19.5f, 9.5f, 16.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-39.2f, -18.5f, -8.5f, 16.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(18.5f, 39.2f, -12.5f, 12.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-13.2f, 26.8f, 31.2f, 56.8f, 6));
                    break;

                case 3:
                    _walkableZones.Add(new WalkableZoneRect(-13.8f, 13.8f, -50.8f, -25.8f, 1));
                    _walkableZones.Add(new WalkableZoneRect(-17.8f, 17.8f, -26.5f, 16.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-41.2f, -15.5f, -16.8f, 10.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(15.5f, 41.2f, -16.8f, 10.8f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-20.8f, 20.8f, 15.2f, 50.8f, 6));
                    break;

                case 4:
                    _walkableZones.Add(new WalkableZoneRect(-9.2f, 9.2f, -52.8f, -25.5f, 1));
                    _walkableZones.Add(new WalkableZoneRect(-38.8f, 38.8f, -26.5f, 26.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-20.8f, 20.8f, 24.5f, 52.8f, 7));
                    break;

                default:
                    _walkableZones.Add(new WalkableZoneRect(-16.0f, 16.0f, -51.0f, -19.5f, 1));
                    _walkableZones.Add(new WalkableZoneRect(-40.2f, 40.2f, -20.5f, 24.5f, 4));
                    _walkableZones.Add(new WalkableZoneRect(-22.2f, 22.2f, 23.0f, 54.0f, 6));
                    break;
            }
        }

        private void EnsureObjectiveNodesBuilt()
        {
            if (_objectiveRoot == null) return;
            if (_objectiveNodes.Count == 8 && sabotageTerminalMesh == null) return;

            for (var i = _objectiveNodes.Count - 1; i >= 0; i--)
            {
                if (_objectiveNodes[i] != null) Destroy(_objectiveNodes[i].gameObject);
            }
            _objectiveNodes.Clear();

            var intelA = GetMissionIntelEntry(missionIndex, 0);
            var intelB = GetMissionIntelEntry(missionIndex, 1);
            var intelC = GetMissionIntelEntry(missionIndex, 2);

            switch (missionIndex)
            {
                case 1:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M1_Gate1", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(1, 0), new Vector3(0f, 0f, -16.8f), 3.2f, 1.85f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M1_WestFuel", ObjectiveNodeKind.FuelValveSabotage,
                        StoryDirector.GetObjectiveNodeTitle(1, 1), new Vector3(-24.0f, 0f, 8.0f), 3.5f, 2.20f, true);
                    EastWingNode = CreateObjectiveNode("Obj_M1_EastRadar", ObjectiveNodeKind.RadarSabotage,
                        StoryDirector.GetObjectiveNodeTitle(1, 2), new Vector3(24.0f, 0f, 8.0f), 3.5f, 2.20f, true);
                    DefenseHoldNode = CreateObjectiveNode("Obj_M1_JammerHold", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(1, 3), new Vector3(-11.0f, 0f, 38.0f), 4.6f, 14.0f, false, HandleHoldoutStarted);
                    ExfilNode = CreateObjectiveNode("Obj_M1_BoatExfil", ObjectiveNodeKind.BoatExfilDock,
                        StoryDirector.GetObjectiveNodeTitle(1, 4), new Vector3(18.0f, 0f, 47.0f), 4.0f, 1.80f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M1_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-31.5f, 0f, -4.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M1_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(31.5f, 0f, 18.0f), 2.5f, 0.75f, false, null, intelB.Id);
                    IntelNodeC = CreateObjectiveNode("Obj_M1_IntelC", ObjectiveNodeKind.IntelTerminal,
                        intelC.NodeTitle, new Vector3(-18.5f, 0f, 38.0f), 2.5f, 0.75f, false, null, intelC.Id);
                    break;

                case 2:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M2_Gate1C4", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(2, 0), new Vector3(-14.0f, 0f, -22.2f), 3.3f, 1.90f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M2_WestSwitch", ObjectiveNodeKind.RailSwitchLever,
                        StoryDirector.GetObjectiveNodeTitle(2, 1), new Vector3(-32.0f, 0f, 4.0f), 3.4f, 1.85f, false);
                    EastWingNode = CreateObjectiveNode("Obj_M2_EastSwitch", ObjectiveNodeKind.RailSwitchLever,
                        StoryDirector.GetObjectiveNodeTitle(2, 2), new Vector3(32.0f, 0f, 0.0f), 3.4f, 1.85f, false);
                    DefenseHoldNode = CreateObjectiveNode("Obj_M2_ShutterHold", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(2, 3), new Vector3(8.0f, 0f, 45.0f), 4.6f, 15.0f, false, HandleHoldoutStarted);
                    ExfilNode = CreateObjectiveNode("Obj_M2_TunnelPortal", ObjectiveNodeKind.TunnelBreachExit,
                        StoryDirector.GetObjectiveNodeTitle(2, 4), new Vector3(8.0f, 0f, 52.0f), 4.0f, 1.75f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M2_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-28.5f, 0f, -27.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M2_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(34.5f, 0f, 9.0f), 2.5f, 0.75f, false, null, intelB.Id);
                    IntelNodeC = CreateObjectiveNode("Obj_M2_IntelC", ObjectiveNodeKind.IntelTerminal,
                        intelC.NodeTitle, new Vector3(-16.0f, 0f, 24.5f), 2.5f, 0.75f, false, null, intelC.Id);
                    break;

                case 3:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M3_AirlockC4", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(3, 0), new Vector3(0f, 0f, -28.5f), 3.2f, 1.80f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M3_ServerHoldout", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(3, 1), new Vector3(-31.0f, 0f, -3.0f), 4.6f, 13.5f, false, HandleHoldoutStarted);
                    EastWingNode = CreateObjectiveNode("Obj_M3_EastTurbine", ObjectiveNodeKind.FuelValveSabotage,
                        StoryDirector.GetObjectiveNodeTitle(3, 2), new Vector3(31.0f, 0f, -3.0f), 3.5f, 2.20f, true);
                    DefenseHoldNode = CreateObjectiveNode("Obj_M3_HalonHold", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(3, 3), new Vector3(0f, 0f, 34.0f), 4.8f, 16.0f, false, HandleHoldoutStarted);
                    ExfilNode = CreateObjectiveNode("Obj_M3_FreightLift", ObjectiveNodeKind.FreightLiftExit,
                        StoryDirector.GetObjectiveNodeTitle(3, 4), new Vector3(0f, 0f, 45.0f), 4.0f, 1.80f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M3_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-37.0f, 0f, 7.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M3_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(37.0f, 0f, -12.5f), 2.5f, 0.75f, false, null, intelB.Id);
                    IntelNodeC = CreateObjectiveNode("Obj_M3_IntelC", ObjectiveNodeKind.IntelTerminal,
                        intelC.NodeTitle, new Vector3(13.5f, 0f, 11.5f), 2.5f, 0.75f, false, null, intelC.Id);
                    break;

                case 4:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M4_BridgeGateC4", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(4, 0), new Vector3(0f, 0f, -28.5f), 3.3f, 1.90f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M4_WestPylon", ObjectiveNodeKind.HeliosPylonOverload,
                        StoryDirector.GetObjectiveNodeTitle(4, 1), new Vector3(-31.0f, 0f, 2.0f), 3.6f, 2.15f, true);
                    EastWingNode = CreateObjectiveNode("Obj_M4_EastPylon", ObjectiveNodeKind.HeliosPylonOverload,
                        StoryDirector.GetObjectiveNodeTitle(4, 2), new Vector3(31.0f, 0f, 2.0f), 3.6f, 2.15f, true);
                    DefenseHoldNode = CreateObjectiveNode("Obj_M4_SummitHold", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(4, 3), new Vector3(0f, 0f, 34.0f), 4.6f, 16.5f, false, HandleHoldoutStarted);
                    ExfilNode = CreateObjectiveNode("Obj_M4_ValkyrieLZ", ObjectiveNodeKind.HelipadEvacLZ,
                        StoryDirector.GetObjectiveNodeTitle(4, 4), new Vector3(0f, 0f, 43.0f), 4.4f, 2.0f, false);
                    IntelNodeA = CreateObjectiveNode("Obj_M4_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-31.0f, 0f, -18.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M4_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(31.0f, 0f, 18.5f), 2.5f, 0.75f, false, null, intelB.Id);
                    IntelNodeC = CreateObjectiveNode("Obj_M4_IntelC", ObjectiveNodeKind.IntelTerminal,
                        intelC.NodeTitle, new Vector3(-15.5f, 0f, 34.0f), 2.5f, 0.75f, false, null, intelC.Id);
                    break;

                default:
                    PrimaryBreachNode = CreateObjectiveNode("Obj_M5_AeonGateC4", ObjectiveNodeKind.GateC4Breach,
                        StoryDirector.GetObjectiveNodeTitle(5, 0), new Vector3(0f, 0f, -22.2f), 3.4f, 1.95f, true);
                    WestWingNode = CreateObjectiveNode("Obj_M5_WestArchive", ObjectiveNodeKind.ServerHackHoldout,
                        StoryDirector.GetObjectiveNodeTitle(5, 1), new Vector3(-31.0f, 0f, 2.0f), 4.0f, 5.5f, false, HandleHoldoutStarted);
                    EastWingNode = CreateObjectiveNode("Obj_M5_EastSatcom", ObjectiveNodeKind.RadarSabotage,
                        StoryDirector.GetObjectiveNodeTitle(5, 2), new Vector3(31.0f, 0f, 2.0f), 3.6f, 2.25f, true);
                    DefenseHoldNode = CreateObjectiveNode("Obj_M5_CoreConsole", ObjectiveNodeKind.CoreShutdownConsole,
                        StoryDirector.GetObjectiveNodeTitle(5, 3), new Vector3(0f, 0f, 29.0f), 3.6f, 1.85f, false);
                    ExfilNode = CreateObjectiveNode("Obj_M5_ValkyrieLZ", ObjectiveNodeKind.HelipadEvacLZ,
                        StoryDirector.GetObjectiveNodeTitle(5, 4), new Vector3(0f, 0f, 43.0f), 4.6f, 14.0f, false, HandleHoldoutStarted);
                    IntelNodeA = CreateObjectiveNode("Obj_M5_IntelA", ObjectiveNodeKind.IntelTerminal,
                        intelA.NodeTitle, new Vector3(-12.5f, 0f, -31.5f), 2.5f, 0.75f, false, null, intelA.Id);
                    IntelNodeB = CreateObjectiveNode("Obj_M5_IntelB", ObjectiveNodeKind.IntelTerminal,
                        intelB.NodeTitle, new Vector3(-35.5f, 0f, 14.0f), 2.5f, 0.75f, false, null, intelB.Id);
                    IntelNodeC = CreateObjectiveNode("Obj_M5_IntelC", ObjectiveNodeKind.IntelTerminal,
                        intelC.NodeTitle, new Vector3(17.5f, 0f, 34.0f), 2.5f, 0.75f, false, null, intelC.Id);
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
            Action<CampaignObjectiveNode> onStarted = null,
            string intelId = "")
        {
            var go = new GameObject(goName);
            go.transform.SetParent(_objectiveRoot, false);
            TrySetTag(go, "Objective");
            var propLayer = LayerMask.NameToLayer("WeaponProp");
            if (propLayer >= 0) go.layer = propLayer;
            var node = go.AddComponent<CampaignObjectiveNode>();
            var mat = kind == ObjectiveNodeKind.IntelTerminal ? _intelTerminalMat : _objectiveTerminalMat;
            node.Configure(kind, title, pos, radius, armSeconds, explode, sabotageTerminalMesh, c4ChargeMesh, mat, _objectiveC4Mat, HandleObjectiveCompleted, onStarted, intelId);
            _objectiveNodes.Add(node);
            return node;
        }

        private static void TrySetTag(GameObject go, string tagName)
        {
            if (go == null || string.IsNullOrEmpty(tagName)) return;
            try { go.tag = tagName; } catch (UnityException) { }
        }

        private EnemyController CreateEnemyRig()
        {
            var root = new GameObject("Enemy_Sentinel");
            root.transform.SetParent(_enemyPoolRoot, false);
            TrySetTag(root, "Enemy");
            var enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) root.layer = enemyLayer;

            var hitCol = root.AddComponent<CapsuleCollider>();
            hitCol.radius = 0.48f;
            hitCol.height = 1.75f;
            hitCol.center = new Vector3(0f, 0.875f, 0f);

            var navAgent = root.AddComponent<NavMeshAgent>();
            navAgent.agentTypeID = 0;
            navAgent.speed = 4.2f;
            navAgent.angularSpeed = 0f;
            navAgent.acceleration = 22f;
            navAgent.stoppingDistance = 0.45f;
            navAgent.radius = 0.46f;
            navAgent.height = 1.75f;
            navAgent.autoBraking = true;
            navAgent.autoRepath = true;
            navAgent.updateRotation = false;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            navAgent.avoidancePriority = 55;

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
            CreatePartWithMesh("ChaserVisor", enemyVisorMesh, PrimitiveType.Cube, chaserHeadObj.transform, new Vector3(0f, 0.02f, 0.19f), enemyVisorMesh != null ? new Vector3(1.12f, 1.15f, 0.90f) : new Vector3(0.36f, 0.10f, 0.08f), _enemySecondaryMat);

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
                out var ak74LeftGrip, out var ak74RightGrip, out var ak74Muzzle,
                enemyMagAkCurvedMesh, new Vector3(0f, -0.105f, 0.080f), new Vector3(0f, 0f, 3f), enemyMagAkMat);

            var pkmObj = BuildEnemyWeapon(
                "EnemyWeapon_PKM", weaponAnchor.transform, enemyPkmMesh, 0.68f,
                new Vector3(0f, -0.026f, 0.210f), new Vector3(0f, -0.064f, -0.044f), new Vector3(0f, 0.019f, 0.656f),
                out var pkmLeftGrip, out var pkmRightGrip, out var pkmMuzzle,
                enemyMagBoxDrumMesh, new Vector3(0.025f, -0.100f, 0.110f), Vector3.zero, enemyMagDrumMat);
            pkmObj.SetActive(false);

            var sniperObj = BuildEnemyWeapon(
                "EnemyWeapon_Sniper", weaponAnchor.transform, sniperRifleMesh != null ? sniperRifleMesh : enemyAk74Mesh, 0.70f,
                new Vector3(0f, -0.022f, 0.210f), new Vector3(0f, -0.070f, -0.070f), new Vector3(0f, 0.022f, 0.790f),
                out var sniperLeftGrip, out var sniperRightGrip, out var sniperMuzzle);
            sniperObj.SetActive(false);

            var riotShieldObj = CreatePartWithMesh(
                "EnemyRiotShield",
                riotShieldMesh,
                PrimitiveType.Cube,
                visualRoot.transform,
                new Vector3(-0.08f, 0.92f, 0.56f),
                riotShieldMesh != null ? Vector3.one : new Vector3(0.84f, 1.38f, 0.08f),
                _enemyWeaponMat != null ? _enemyWeaponMat : _enemySecondaryMat);
            riotShieldObj.SetActive(false);

            var bootAccentMat = _enemyWeaponMat != null ? _enemyWeaponMat : _enemySecondaryMat;
            var leftLegPivot = BuildEnemyArticulatedLeg("Left", visualRoot.transform, new Vector3(-0.18f, 0.74f, 0f), limbMesh, _enemyPrimaryMat, bootAccentMat);
            var rightLegPivot = BuildEnemyArticulatedLeg("Right", visualRoot.transform, new Vector3(0.18f, 0.74f, 0f), limbMesh, _enemyPrimaryMat, bootAccentMat);

            var reactorPivot = new GameObject("SecondaryPivot");
            reactorPivot.transform.SetParent(visualRoot.transform, false);
            reactorPivot.transform.localPosition = new Vector3(0f, 1.05f, -0.30f);
            CreatePartWithMesh("FieldPack", fieldPackMesh, PrimitiveType.Cube, reactorPivot.transform, Vector3.zero, fieldPackMesh != null ? new Vector3(0.70f, 0.55f, 0.82f) : new Vector3(0.32f, 0.36f, 0.18f), _enemySecondaryMat);

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

            var laserGo = new GameObject("RailLaserTelegraph");
            laserGo.transform.SetParent(root.transform, false);
            var lr = laserGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.018f;
            lr.endWidth = 0.008f;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = _laserSightMat != null ? _laserSightMat : _hazardRingMat;
            lr.enabled = false;

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot, rightArmPivot, leftLegPivot.transform, rightLegPivot.transform, reactorPivot.transform);
            motion.BindWeaponArmIk(leftElbowPivot, rightElbowPivot, weaponAnchor.transform, ak74LeftGrip, ak74RightGrip);
            ctrl.BindComponents(
                navAgent, motion, root.GetComponentsInChildren<MeshRenderer>(true),
                visualRoot, chaserToggle, heavyToggle, bossGroup, turretPivot.transform,
                leftArmPivot, rightArmPivot, weaponAnchor.transform,
                ak74Obj, ak74LeftGrip, ak74RightGrip, ak74Muzzle,
                pkmObj, pkmLeftGrip, pkmRightGrip, pkmMuzzle,
                bossMuzzleTip.transform, shieldGo, lr);
            ctrl.BindExpandedEquipment(sniperObj, sniperLeftGrip, sniperRightGrip, sniperMuzzle, riotShieldObj);

            root.SetActive(false);
            return ctrl;
        }

        private Transform BuildEnemyOperatorArm(string side, Transform visualRoot, Vector3 shoulderPos, out Transform elbowPivotOut)
        {
            var armPivot = new GameObject(side + "ArmPivot");
            armPivot.transform.SetParent(visualRoot, false);
            armPivot.transform.localPosition = shoulderPos;

            if (armorPauldronMesh != null)
            {
                var sideSign = side == "Left" ? -0.02f : 0.02f;
                CreatePartWithMesh(side + "Pauldron", armorPauldronMesh, PrimitiveType.Cube, armPivot.transform, new Vector3(sideSign, -0.02f, 0f), new Vector3(0.92f, 0.75f, 0.92f), _enemySecondaryMat);
            }

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
            out Transform muzzleOut,
            Mesh magMesh = null,
            Vector3 magOffsetUnscaled = default,
            Vector3 magEuler = default,
            Material magMaterial = null)
        {
            var container = new GameObject(name);
            container.transform.SetParent(anchor, false);

            CreatePartWithMesh("MeshBody", weaponMesh, PrimitiveType.Cube, container.transform,
                weaponMesh != null ? Vector3.zero : new Vector3(0f, 0f, 0.16f),
                weaponMesh != null ? Vector3.one * uniformScale : new Vector3(0.09f, 0.14f, 0.56f),
                _enemyWeaponMat);

            if (magMesh != null && weaponMesh != null)
            {
                var magGo = CreatePartWithMesh("WeaponMagVisual", magMesh, PrimitiveType.Cube, container.transform,
                    magOffsetUnscaled * uniformScale, Vector3.one * uniformScale, magMaterial != null ? magMaterial : _enemyWeaponMat);
                magGo.transform.localRotation = Quaternion.Euler(magEuler);
            }

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
            TrySetTag(root, "Pickup");
            var pickupLayer = LayerMask.NameToLayer("Pickup");
            if (pickupLayer >= 0) root.layer = pickupLayer;

            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.85f;

            var visualHolder = new GameObject("VisualSpinRoot");
            visualHolder.transform.SetParent(root.transform, false);
            TrySetTag(visualHolder, "Pickup");
            if (pickupLayer >= 0) visualHolder.layer = pickupLayer;

            var medObj = CreatePartWithMesh("MedkitVisual", medkitMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, medkitMesh != null ? Vector3.one : new Vector3(0.44f, 0.24f, 0.36f), _pickupMat);
            var credObj = CreatePartWithMesh("CreditCoinVisual", creditCoinMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, creditCoinMesh != null ? Vector3.one : new Vector3(0.55f, 0.35f, 0.42f), _pickupMat);
            var munMesh = munitionCrateMesh != null ? munitionCrateMesh : creditCoinMesh;
            var munObj = CreatePartWithMesh("MunitionCacheVisual", munMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, munMesh != null ? Vector3.one * 0.92f : new Vector3(0.56f, 0.38f, 0.44f), _pickupMat);
            foreach (var v in new[] { medObj, credObj, munObj })
            {
                if (v == null) continue;
                TrySetTag(v, "Pickup");
                if (pickupLayer >= 0) v.layer = pickupLayer;
            }

            medObj.SetActive(false);
            credObj.SetActive(false);
            munObj.SetActive(false);

            var item = root.AddComponent<PickupItem>();
            item.ConfigureMultiVisuals(
                visualHolder.transform,
                medObj, medObj.GetComponent<MeshRenderer>(),
                credObj, credObj.GetComponent<MeshRenderer>(),
                munObj, munObj.GetComponent<MeshRenderer>());

            root.SetActive(false);
            return item;
        }

        private static Transform BuildEnemyArticulatedLeg(
            string side,
            Transform parent,
            Vector3 hipLocalPos,
            Mesh limbMesh,
            Material primaryMat,
            Material accentMat)
        {
            var hipPivot = new GameObject(side + "LegPivot");
            hipPivot.transform.SetParent(parent, false);
            hipPivot.transform.localPosition = hipLocalPos;
            var thighScale = limbMesh != null ? new Vector3(1.0f, 0.34f / 0.55f, 0.95f) : new Vector3(0.22f, 0.34f, 0.23f);
            CreatePartWithMesh(side + "Thigh", limbMesh, PrimitiveType.Cube, hipPivot.transform, new Vector3(0f, -0.17f, 0f), thighScale, primaryMat);

            var kneePivot = new GameObject(side + "KneePivot");
            kneePivot.transform.SetParent(hipPivot.transform, false);
            kneePivot.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            CreatePartWithMesh(side + "KneePad", null, PrimitiveType.Cube, kneePivot.transform, new Vector3(0f, 0.01f, 0.035f), new Vector3(0.19f, 0.11f, 0.15f), accentMat);
            var shinScale = limbMesh != null ? new Vector3(0.84f, 0.32f / 0.55f, 0.80f) : new Vector3(0.18f, 0.32f, 0.19f);
            CreatePartWithMesh(side + "Shin", limbMesh, PrimitiveType.Cube, kneePivot.transform, new Vector3(0f, -0.16f, 0f), shinScale, primaryMat);

            var bootPivot = new GameObject(side + "BootPivot");
            bootPivot.transform.SetParent(kneePivot.transform, false);
            bootPivot.transform.localPosition = new Vector3(0f, -0.32f, 0f);
            CreatePartWithMesh(side + "Boot", null, PrimitiveType.Cube, bootPivot.transform, new Vector3(0f, -0.04f, 0.045f), new Vector3(0.19f, 0.09f, 0.28f), accentMat);

            return hipPivot.transform;
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
            _holdoutWavesStarted = false;
            _remainingReinforcements = 0;
            _hazardPulsePeriod = 0f;
            CurrentStage = Mathf.Clamp(stageNum, 1, StoryDirector.FinalCampaignStage);

            if (CurrentStage >= 4) CutsceneDirector.Instance?.OpenGateImmediate(1);
            if ((CurrentStage >= 6 && missionIndex != 4) || (CurrentStage >= 7 && missionIndex == 4))
            {
                CutsceneDirector.Instance?.OpenGateImmediate(2);
            }

            ConfigureStageState(missionIndex, CurrentStage);

            if (CurrentStage > 1)
            {
                StoryDirector.NotifyStageStarted(CurrentStage);
                if (PlayerController.Instance != null)
                {
                    var dropPos = ClampPositionToUnlockedSectors(PlayerController.Instance.transform.position + Vector3.forward * 2.2f);
                    SpawnPickupAt(PickupKind.MunitionCache, dropPos);
                }
            }
            SyncMissionHud();
        }

        private void ConfigureStageState(int m, int stage)
        {
            switch (stage)
            {
                case 1:
                    ResetMainNodes();
                    UnlockUncollectedIntel();
                    SpawnStage1Perimeter(m);
                    break;

                case 2:
                    if (m <= 3)
                    {
                        _hazardPulsePeriod = m == 2 ? 6.8f : 7.2f;
                        _hazardPulseTimer = 3.8f;
                    }
                    SpawnStage2CounterWave(m);
                    break;

                case 3:
                    PrimaryBreachNode?.SetState(true, false);
                    StoryDirector.NotifyGate1C4Ready();
                    UIManager.Instance?.ShowMilestoneBanner("GATE CLEAR // PLANT C4 AT " + StoryDirector.GetObjectiveNodeTitle(m, 0));
                    SpawnStage3GateDefenders(m);
                    break;

                case 4:
                    SabotageCompletedCount = 0;
                    PrimaryBreachNode?.SetState(false, true);
                    WestWingNode?.SetState(true, false);
                    EastWingNode?.SetState(true, false);
                    UnlockUncollectedIntel();
                    _hazardPulsePeriod = m switch
                    {
                        5 => 5.4f,
                        4 => 5.6f,
                        2 => 6.6f,
                        _ => 7.0f
                    };
                    _hazardPulseTimer = 4.0f;
                    SpawnStage4DualWing(m);
                    break;

                case 5:
                    if (m >= 4)
                    {
                        _hazardPulsePeriod = m == 5 ? 5.2f : 5.8f;
                        _hazardPulseTimer = 3.6f;
                    }
                    SpawnStage5BastionAssualt(m);
                    break;

                case 6:
                    SpawnStage6BossEncounter(m);
                    ShowBossMilestone();
                    break;

                case 7:
                    DefenseHoldNode?.SetState(true, false);
                    if (m == 5)
                    {
                        UIManager.Instance?.ShowMilestoneBanner("CHEN NEUTRALIZED // ACCESS AEON CORE CONSOLE [E]");
                    }
                    else
                    {
                        _hazardPulsePeriod = m switch
                        {
                            4 => 4.8f,
                            2 => 6.0f,
                            _ => 6.4f
                        };
                        _hazardPulseTimer = 3.4f;
                        UIManager.Instance?.ShowMilestoneBanner("LAST STAND // HOLD " + StoryDirector.GetObjectiveNodeTitle(m, 3) + "!");
                    }
                    break;

                case 8:
                    ExfilNode?.SetState(true, false);
                    var exfilTitle = StoryDirector.GetObjectiveNodeTitle(m, 4);
                    if (m == 5)
                    {
                        _hazardPulsePeriod = 4.2f;
                        _hazardPulseTimer = 2.8f;
                        UIManager.Instance?.ShowMilestoneBanner("FINAL STAND // HOLD " + exfilTitle + " FOR VALKYRIE-6!");
                    }
                    else
                    {
                        UIManager.Instance?.ShowMilestoneBanner("EXFIL OPEN // REACH " + exfilTitle + "!");
                    }
                    break;
            }
        }

        private void SpawnStage1Perimeter(int m)
        {
            switch (m)
            {
                case 1:
                    // Must retain both a Shielder and Sniper on Mission 1 Stage 1 for combat coverage
                    SpawnFormation(new[]
                    {
                        new Vector3(-7.0f, 0f, -34f), new Vector3(7.0f, 0f, -34f),
                        new Vector3(-11.5f, 0f, -26f), new Vector3(11.5f, 0f, -26f),
                        new Vector3(-3.0f, 0f, -21f), new Vector3(3.0f, 0f, -21f),
                        new Vector3(0f, 0f, -18.5f)
                    }, heavyIndices: new[] { 4 }, alertedCount: 5, sniperIndices: new[] { 3 }, shielderIndices: new[] { 5 });
                    QueueReinforcements(1, 5.5f, 5.0f, 3, new[]
                    {
                        new Vector3(-14f, 0f, -20f), new Vector3(14f, 0f, -20f), new Vector3(0f, 0f, -18f)
                    });
                    break;
                case 2:
                    SpawnFormation(new[]
                    {
                        new Vector3(-22f, 0f, -36f), new Vector3(-8f, 0f, -35f),
                        new Vector3(-25f, 0f, -28f), new Vector3(-14f, 0f, -26f),
                        new Vector3(-4f, 0f, -24f), new Vector3(-20f, 0f, -23f)
                    }, heavyIndices: new[] { 3 }, alertedCount: 4, sniperIndices: new[] { 4 }, shielderIndices: new[] { 2 });
                    QueueReinforcements(1, 5.0f, 5.0f, 3, new[]
                    {
                        new Vector3(-24f, 0f, -24f), new Vector3(-10f, 0f, -24f), new Vector3(-18f, 0f, -22f)
                    });
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-8f, 0f, -37f), new Vector3(8f, 0f, -37f),
                        new Vector3(-6f, 0f, -31f), new Vector3(6f, 0f, -31f),
                        new Vector3(0f, 0f, -33f), new Vector3(0f, 0f, -29f)
                    }, heavyIndices: new[] { 4 }, alertedCount: 4, sniperIndices: new[] { 3 }, shielderIndices: new[] { 5 });
                    break;
                case 4:
                    SpawnFormation(new[]
                    {
                        new Vector3(-5.5f, 0f, -40f), new Vector3(5.5f, 0f, -40f),
                        new Vector3(-6.0f, 0f, -33f), new Vector3(6.0f, 0f, -33f),
                        new Vector3(-3.5f, 0f, -28f), new Vector3(3.5f, 0f, -28f)
                    }, heavyIndices: new[] { 4, 5 }, alertedCount: 6, sniperIndices: new[] { 2 }, shielderIndices: new[] { 3 });
                    break;
                default:
                    SpawnFormation(new[]
                    {
                        new Vector3(-9.5f, 0f, -38f), new Vector3(9.5f, 0f, -38f),
                        new Vector3(-11.0f, 0f, -31f), new Vector3(11.0f, 0f, -31f),
                        new Vector3(-4.5f, 0f, -25f), new Vector3(4.5f, 0f, -25f),
                        new Vector3(0f, 0f, -28f)
                    }, heavyIndices: new[] { 4, 6 }, alertedCount: 5, sniperIndices: new[] { 2 }, shielderIndices: new[] { 5 });
                    break;
            }
        }

        private void SpawnStage2CounterWave(int m)
        {
            switch (m)
            {
                case 1:
                    SpawnFormation(new[]
                    {
                        new Vector3(-15f, 0f, -22f), new Vector3(15f, 0f, -22f),
                        new Vector3(-9f, 0f, -18f), new Vector3(9f, 0f, -18f),
                        new Vector3(-4f, 0f, -25f), new Vector3(4f, 0f, -25f),
                        new Vector3(0f, 0f, -19f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 7, sniperIndices: new[] { 0, 1 }, shielderIndices: new[] { 6 });
                    QueueReinforcements(1, 5.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-12f, 0f, -21f), new Vector3(12f, 0f, -21f), new Vector3(0f, 0f, -19f)
                    });
                    break;
                case 2:
                    SpawnFormation(new[]
                    {
                        new Vector3(-25f, 0f, -25f), new Vector3(-12f, 0f, -25f),
                        new Vector3(-2f, 0f, -25f), new Vector3(-19f, 0f, -32f),
                        new Vector3(-8f, 0f, -32f), new Vector3(-15f, 0f, -22f)
                    }, heavyIndices: new[] { 1, 3 }, alertedCount: 6, sniperIndices: new[] { 2 }, shielderIndices: new[] { 5 });
                    QueueReinforcements(1, 5.0f, 4.5f, 4, new[]
                    {
                        new Vector3(-24f, 0f, -23f), new Vector3(-4f, 0f, -23f), new Vector3(-14f, 0f, -23f)
                    });
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-9f, 0f, -30f), new Vector3(9f, 0f, -30f),
                        new Vector3(-5f, 0f, -35f), new Vector3(5f, 0f, -35f),
                        new Vector3(0f, 0f, -29f), new Vector3(0f, 0f, -38f)
                    }, heavyIndices: new[] { 0, 1 }, alertedCount: 6, sniperIndices: new[] { 5 }, shielderIndices: new[] { 4 });
                    QueueReinforcements(1, 4.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-8f, 0f, -30f), new Vector3(8f, 0f, -30f)
                    });
                    break;
                case 4:
                    SpawnFormation(new[]
                    {
                        new Vector3(-6.5f, 0f, -31f), new Vector3(6.5f, 0f, -31f),
                        new Vector3(-4.0f, 0f, -36f), new Vector3(4.0f, 0f, -36f),
                        new Vector3(0f, 0f, -29f), new Vector3(0f, 0f, -40f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 6, sniperIndices: new[] { 5 }, shielderIndices: new[] { 4 });
                    QueueReinforcements(1, 4.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-6f, 0f, -29f), new Vector3(6f, 0f, -29f)
                    });
                    break;
                default:
                    SpawnFormation(new[]
                    {
                        new Vector3(-12f, 0f, -25f), new Vector3(12f, 0f, -25f),
                        new Vector3(-7f, 0f, -30f), new Vector3(7f, 0f, -30f),
                        new Vector3(0f, 0f, -23f), new Vector3(-10f, 0f, -36f),
                        new Vector3(10f, 0f, -36f)
                    }, heavyIndices: new[] { 0, 1 }, alertedCount: 7, sniperIndices: new[] { 5, 6 }, shielderIndices: new[] { 4 });
                    QueueReinforcements(1, 5.0f, 4.5f, 4, new[]
                    {
                        new Vector3(-9f, 0f, -24f), new Vector3(9f, 0f, -24f), new Vector3(0f, 0f, -22f)
                    });
                    break;
            }
        }

        private void SpawnStage3GateDefenders(int m)
        {
            var pts = m switch
            {
                2 => new[] { new Vector3(-22f, 0f, -25f), new Vector3(-6f, 0f, -25f), new Vector3(-14f, 0f, -22f) },
                3 => new[] { new Vector3(-7f, 0f, -30f), new Vector3(7f, 0f, -30f), new Vector3(0f, 0f, -29f) },
                4 => new[] { new Vector3(-5f, 0f, -30f), new Vector3(5f, 0f, -30f), new Vector3(0f, 0f, -29f) },
                _ => new[] { new Vector3(-9f, 0f, -22f), new Vector3(9f, 0f, -22f), new Vector3(0f, 0f, -20f) }
            };
            SpawnFormation(pts, heavyIndices: new[] { 0 }, alertedCount: pts.Length, shielderIndices: new[] { 2 });
        }

        private void SpawnStage4DualWing(int m)
        {
            switch (m)
            {
                case 1:
                    SpawnFormation(new[]
                    {
                        new Vector3(-22f, 0f, -4f), new Vector3(-28f, 0f, 5f), new Vector3(-18f, 0f, 14f),
                        new Vector3(22f, 0f, -4f), new Vector3(28f, 0f, 5f), new Vector3(18f, 0f, 14f),
                        new Vector3(0f, 0f, 18.5f), new Vector3(-24f, 0f, 12f), new Vector3(24f, 0f, 12f)
                    }, heavyIndices: new[] { 1, 4 }, alertedCount: 3, sniperIndices: new[] { 2, 5 }, shielderIndices: new[] { 6, 7 });
                    QueueReinforcements(3, 6.0f, 6.5f, 3, new[]
                    {
                        new Vector3(-10f, 0f, 18f), new Vector3(10f, 0f, 18f), new Vector3(0f, 0f, -8f)
                    });
                    break;
                case 2:
                    SpawnFormation(new[]
                    {
                        new Vector3(-12f, 0f, -10f), new Vector3(-29f, 0f, 0f), new Vector3(-12f, 0f, 18f),
                        new Vector3(12f, 0f, -10f), new Vector3(29f, 0f, -4f), new Vector3(12f, 0f, 18f),
                        new Vector3(-11f, 0f, 4f), new Vector3(11f, 0f, 8f), new Vector3(11f, 0f, 22f)
                    }, heavyIndices: new[] { 1, 4 }, alertedCount: 3, sniperIndices: new[] { 2, 5 }, shielderIndices: new[] { 6, 7 });
                    QueueReinforcements(3, 6.0f, 6.5f, 3, new[]
                    {
                        new Vector3(-10f, 0f, 24f), new Vector3(10f, 0f, 24f), new Vector3(-24f, 0f, 6f)
                    });
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-9f, 0f, -15f), new Vector3(9f, 0f, -15f),
                        new Vector3(-24f, 0f, -6f), new Vector3(-29f, 0f, 4f), new Vector3(-35f, 0f, -8f),
                        new Vector3(24f, 0f, -2f), new Vector3(31f, 0f, 5f), new Vector3(34f, 0f, -9f)
                    }, heavyIndices: new[] { 3, 5 }, alertedCount: 2, sniperIndices: new[] { 4, 7 }, shielderIndices: new[] { 1, 6 });
                    break;
                case 4:
                    SpawnFormation(new[]
                    {
                        new Vector3(-16f, 0f, -12f), new Vector3(-29f, 0f, -4f), new Vector3(-26f, 0f, 10f),
                        new Vector3(16f, 0f, -12f), new Vector3(29f, 0f, -4f), new Vector3(26f, 0f, 10f),
                        new Vector3(0f, 0f, 15f), new Vector3(0f, 0f, -12f)
                    }, heavyIndices: new[] { 1, 4 }, alertedCount: 3, sniperIndices: new[] { 2, 5 }, shielderIndices: new[] { 0, 6 });
                    QueueReinforcements(3, 6.5f, 6.5f, 3, new[]
                    {
                        new Vector3(-20f, 0f, 14f), new Vector3(20f, 0f, 14f), new Vector3(0f, 0f, 18f)
                    });
                    break;
                default:
                    SpawnFormation(new[]
                    {
                        new Vector3(-16f, 0f, -10f), new Vector3(-29f, 0f, -2f), new Vector3(-26f, 0f, 12f),
                        new Vector3(16f, 0f, -10f), new Vector3(29f, 0f, -2f), new Vector3(26f, 0f, 12f),
                        new Vector3(-8f, 0f, 14f), new Vector3(8f, 0f, 14f), new Vector3(0f, 0f, 4f)
                    }, heavyIndices: new[] { 1, 4, 8 }, alertedCount: 4, sniperIndices: new[] { 2, 5 }, shielderIndices: new[] { 6, 7 });
                    QueueReinforcements(3, 5.8f, 6.2f, 3, new[]
                    {
                        new Vector3(-18f, 0f, 16f), new Vector3(18f, 0f, 16f), new Vector3(0f, 0f, 18f)
                    });
                    break;
            }
        }

        private void SpawnStage5BastionAssualt(int m)
        {
            switch (m)
            {
                case 1:
                    SpawnFormation(new[]
                    {
                        new Vector3(-8f, 0f, 14f), new Vector3(8f, 0f, 14f),
                        new Vector3(-4f, 0f, 20f), new Vector3(4f, 0f, 20f),
                        new Vector3(-22f, 0f, 14f), new Vector3(22f, 0f, 14f),
                        new Vector3(0f, 0f, 17f)
                    }, heavyIndices: new[] { 0, 1 }, alertedCount: 7, sniperIndices: new[] { 4, 5 }, shielderIndices: new[] { 2, 6 });
                    QueueReinforcements(1, 4.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-10f, 0f, 19f), new Vector3(10f, 0f, 19f)
                    });
                    break;
                case 2:
                    SpawnFormation(new[]
                    {
                        new Vector3(-10f, 0f, 18f), new Vector3(10f, 0f, 18f),
                        new Vector3(-11f, 0f, 26f), new Vector3(11f, 0f, 26f),
                        new Vector3(-15f, 0f, 10f), new Vector3(15f, 0f, 10f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 6, sniperIndices: new[] { 4, 5 }, shielderIndices: new[] { 0, 1 });
                    QueueReinforcements(1, 4.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-10f, 0f, 25f), new Vector3(10f, 0f, 25f)
                    });
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-10f, 0f, -2f), new Vector3(10f, 0f, -2f),
                        new Vector3(-12f, 0f, 9f), new Vector3(12f, 0f, 9f),
                        new Vector3(0f, 0f, 11f), new Vector3(0f, 0f, -12f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 6, sniperIndices: new[] { 0, 1 }, shielderIndices: new[] { 4, 5 });
                    QueueReinforcements(1, 4.5f, 4.5f, 4, new[]
                    {
                        new Vector3(-10f, 0f, 10f), new Vector3(10f, 0f, 10f)
                    });
                    break;
                case 4:
                    SpawnFormation(new[]
                    {
                        new Vector3(-15f, 0f, 8f), new Vector3(15f, 0f, 8f),
                        new Vector3(-18f, 0f, 19f), new Vector3(18f, 0f, 19f),
                        new Vector3(0f, 0f, 20f), new Vector3(-8f, 0f, 14f), new Vector3(8f, 0f, 14f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 7, sniperIndices: new[] { 0, 1 }, shielderIndices: new[] { 5, 6 });
                    QueueReinforcements(1, 5.0f, 4.5f, 4, new[]
                    {
                        new Vector3(-14f, 0f, 20f), new Vector3(14f, 0f, 20f)
                    });
                    break;
                default:
                    SpawnFormation(new[]
                    {
                        new Vector3(-14f, 0f, 8f), new Vector3(14f, 0f, 8f),
                        new Vector3(-18f, 0f, 18f), new Vector3(18f, 0f, 18f),
                        new Vector3(-6f, 0f, 19f), new Vector3(6f, 0f, 19f),
                        new Vector3(0f, 0f, 14f)
                    }, heavyIndices: new[] { 2, 3, 6 }, alertedCount: 7, sniperIndices: new[] { 0, 1 }, shielderIndices: new[] { 4, 5 });
                    QueueReinforcements(1, 5.0f, 4.5f, 5, new[]
                    {
                        new Vector3(-14f, 0f, 20f), new Vector3(14f, 0f, 20f), new Vector3(0f, 0f, 21f)
                    });
                    break;
            }
        }

        private void SpawnStage6BossEncounter(int m)
        {
            switch (m)
            {
                case 1:
                    SpawnFormation(new[]
                    {
                        new Vector3(-11f, 0f, 34f), new Vector3(11f, 0f, 34f),
                        new Vector3(-15f, 0f, 44f), new Vector3(15f, 0f, 44f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 4, sniperIndices: new[] { 3 }, shielderIndices: new[] { 1 });
                    SpawnBossAt(new Vector3(0f, 0f, 40f));
                    break;
                case 2:
                    SpawnFormation(new[]
                    {
                        new Vector3(-6f, 0f, 38f), new Vector3(20f, 0f, 38f),
                        new Vector3(18f, 0f, 48f), new Vector3(-2f, 0f, 47f)
                    }, heavyIndices: new[] { 1 }, alertedCount: 4, sniperIndices: new[] { 2 }, shielderIndices: new[] { 0, 3 });
                    SpawnBossAt(new Vector3(8f, 0f, 43f));
                    break;
                case 3:
                    SpawnFormation(new[]
                    {
                        new Vector3(-11f, 0f, 25f), new Vector3(11f, 0f, 25f),
                        new Vector3(-14f, 0f, 36f), new Vector3(14f, 0f, 36f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 4, sniperIndices: new[] { 3 }, shielderIndices: new[] { 0, 1 });
                    SpawnBossAt(new Vector3(0f, 0f, 32f));
                    break;
                case 4:
                    SpawnFormation(new[]
                    {
                        new Vector3(-13f, 0f, 8f), new Vector3(13f, 0f, 8f),
                        new Vector3(-16f, 0f, 20f), new Vector3(16f, 0f, 20f)
                    }, heavyIndices: new[] { 2 }, alertedCount: 4, sniperIndices: new[] { 3 }, shielderIndices: new[] { 0, 1 });
                    SpawnBossAt(new Vector3(0f, 0f, 14f));
                    break;
                default:
                    SpawnFormation(new[]
                    {
                        new Vector3(-12f, 0f, 31f), new Vector3(12f, 0f, 31f),
                        new Vector3(-14f, 0f, 42f), new Vector3(14f, 0f, 42f),
                        new Vector3(-6f, 0f, 39f), new Vector3(6f, 0f, 39f)
                    }, heavyIndices: new[] { 2, 3 }, alertedCount: 6, sniperIndices: new[] { 0, 1 }, shielderIndices: new[] { 4, 5 });
                    SpawnBossAt(new Vector3(0f, 0f, 36f));
                    break;
            }
        }

        private void ResetMainNodes()
        {
            PrimaryBreachNode?.SetState(false, false);
            WestWingNode?.SetState(false, false);
            EastWingNode?.SetState(false, false);
            DefenseHoldNode?.SetState(false, false);
            ExfilNode?.SetState(false, false);
        }

        private void UnlockUncollectedIntel()
        {
            if (IntelNodeA != null && !IntelNodeA.IsCompleted) IntelNodeA.SetState(true, false);
            if (IntelNodeB != null && !IntelNodeB.IsCompleted) IntelNodeB.SetState(true, false);
            if (IntelNodeC != null && !IntelNodeC.IsCompleted) IntelNodeC.SetState(true, false);
        }

        private void QueueReinforcements(int batchCount, float firstDelay, float interval, int perBatchCount, Vector3[] points)
        {
            _remainingReinforcements = Mathf.Max(0, batchCount);
            _reinforcementTimer = firstDelay;
            _reinforcementInterval = Mathf.Max(1.8f, interval);
            _reinforcementBatchSize = Mathf.Max(1, perBatchCount);
            _reinforcementPoints = points ?? Array.Empty<Vector3>();
        }

        private void SpawnFormation(
            Vector3[] positions,
            int[] heavyIndices,
            int alertedCount,
            int[] sniperIndices = null,
            int[] shielderIndices = null)
        {
            _stageKillsProgress = 0;
            _stageKillTarget = positions.Length;
            var heavies = new HashSet<int>(heavyIndices);
            var snipers = sniperIndices != null ? new HashSet<int>(sniperIndices) : null;
            var shielders = shielderIndices != null ? new HashSet<int>(shielderIndices) : null;
            for (var i = 0; i < positions.Length; i++)
            {
                EnemyArchetype arch;
                if (heavies.Contains(i)) arch = EnemyArchetype.Heavy;
                else if (shielders != null && shielders.Contains(i)) arch = EnemyArchetype.Shielder;
                else if (snipers != null && snipers.Contains(i)) arch = EnemyArchetype.Sniper;
                else arch = EnemyArchetype.Chaser;

                var e = SpawnEnemyAt(arch, positions[i]);
                e.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                e.SetGarrisonAlert(i < alertedCount, arch == EnemyArchetype.Sniper ? 34f : 28f);
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
            if (_holdoutWavesStarted) return;
            _holdoutWavesStarted = true;
            StoryDirector.NotifyVaultHoldoutStarted();

            if (missionIndex == 3 && node == WestWingNode && CurrentStage == 4)
            {
                QueueReinforcements(4, 1.4f, 2.8f, 2, new[]
                {
                    new Vector3(-14f, 0f, -7f), new Vector3(-14f, 0f, 2f)
                });
                UIManager.Instance?.ShowMilestoneBanner("DECRYPTING ROE-9 // DEFEND WEST VAULT!");
                return;
            }

            if (CurrentStage == 7)
            {
                var spawns = missionIndex switch
                {
                    1 => new[] { new Vector3(-18f, 0f, 30f), new Vector3(18f, 0f, 30f), new Vector3(0f, 0f, 26f) },
                    2 => new[] { new Vector3(-9f, 0f, 35f), new Vector3(20f, 0f, 35f), new Vector3(6f, 0f, 32f) },
                    3 => new[] { new Vector3(-15f, 0f, 22f), new Vector3(15f, 0f, 22f), new Vector3(0f, 0f, 20f) },
                    _ => new[] { new Vector3(-14f, 0f, 30f), new Vector3(14f, 0f, 30f), new Vector3(-16f, 0f, 44f), new Vector3(16f, 0f, 44f) }
                };
                QueueReinforcements(4, 1.0f, 3.2f, 3, spawns);
                UIManager.Instance?.TriggerThreatWarning("!! DEFEND OBJECTIVE UNDER CONTINUOUS QRF ASSAULT !!", 2.8f);
                return;
            }

            if (missionIndex == 5 && node == ExfilNode && CurrentStage == 8)
            {
                var isBroadcast = GameManager.Instance != null && GameManager.Instance.ActiveEndingChoice == CampaignEndingKind.BroadcastBlackout;
                var spawns = new[]
                {
                    new Vector3(-16f, 0f, 30f), new Vector3(16f, 0f, 30f),
                    new Vector3(-18f, 0f, 44f), new Vector3(18f, 0f, 44f)
                };
                QueueReinforcements(isBroadcast ? 5 : 4, 0.8f, isBroadcast ? 2.6f : 3.0f, isBroadcast ? 4 : 3, spawns);
                UIManager.Instance?.TriggerThreatWarning(isBroadcast
                    ? "!! BLACKOUT UPLINK LIVE — GLOBAL CLEANERS STORMED COURTYARD !!"
                    : "!! PROTOCOL SEALED — BLACKGUARD HUNTERS LOCKING LZ !!", 3.2f);
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

            StepReinforcements(dt);
            StepEnvironmentalHazards(dt);
            StepCombatStageAdvance(dt);
        }

        private void StepReinforcements(float dt)
        {
            if (_remainingReinforcements <= 0 || _reinforcementPoints == null || _reinforcementPoints.Length == 0) return;

            // Never leave player standing in an empty arena waiting for the next QRF timer
            if (_activeEnemies.Count == 0 && _reinforcementTimer > 0.35f)
            {
                _reinforcementTimer = 0.35f;
            }

            _reinforcementTimer -= dt;
            if (_reinforcementTimer > 0f || _activeEnemies.Count >= 14) return;

            _remainingReinforcements--;
            _reinforcementTimer = _reinforcementInterval;

            for (var i = 0; i < _reinforcementBatchSize; i++)
            {
                if (_activeEnemies.Count >= 16) break;
                var pt = _reinforcementPoints[UnityEngine.Random.Range(0, _reinforcementPoints.Length)];
                var jitter = new Vector3(UnityEngine.Random.Range(-1.8f, 1.8f), 0f, UnityEngine.Random.Range(-1.8f, 1.8f));
                var arch = (i == 0 && _remainingReinforcements == 0)
                    ? EnemyArchetype.Heavy
                    : (i % 3 == 2 ? EnemyArchetype.Shielder : (i % 3 == 1 ? EnemyArchetype.Sniper : EnemyArchetype.Chaser));
                var esc = SpawnEnemyAt(arch, ClampPositionToUnlockedSectors(pt + jitter));
                esc.SetGarrisonAlert(true, 60f);
            }

            _stageKillTarget += _reinforcementBatchSize;
            SyncMissionHud();
        }

        private void StepEnvironmentalHazards(float dt)
        {
            if (_hazardPulsePeriod <= 0.1f || PlayerController.Instance == null || PlayerController.Instance.IsDead) return;
            _hazardPulseTimer -= dt;
            if (_hazardPulseTimer <= 0f)
            {
                _hazardPulseTimer = _hazardPulsePeriod;
                TriggerEnvironmentalHazardBeat();
            }
        }

        public void TriggerEnvironmentalHazardBeat()
        {
            var player = PlayerController.Instance;
            var anchor = player != null ? player.transform.position : Vector3.zero;
            switch (missionIndex)
            {
                case 1:
                    ActiveEnvironmentalHazardName = "OFFSHORE NAVAL BARRAGE";
                    UIManager.Instance?.TriggerThreatWarning("!! COASTAL ALERT: OFFSHORE NAVAL SALVO INBOUND !!", 1.85f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor + new Vector3(UnityEngine.Random.Range(-1.8f, 1.8f), 0f, UnityEngine.Random.Range(-1.8f, 1.8f))), 2.35f, 16f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor + new Vector3(UnityEngine.Random.Range(-4.8f, 4.8f), 0f, UnityEngine.Random.Range(-3.5f, 3.5f))), 2.15f, 14f);
                    break;
                case 2:
                    ActiveEnvironmentalHazardName = "TRAIN #774 AUTO-TURRET SWEEP";
                    UIManager.Instance?.TriggerThreatWarning("!! RAIL CORRIDOR ALERT: ARMORED TRAIN TURRET SWEEP !!", 1.85f);
                    var sweepPt = new Vector3(Mathf.Clamp(anchor.x, -8f, 8f), 0f, anchor.z);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(sweepPt), 2.45f, 17f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(sweepPt + new Vector3(0f, 0f, 4.2f)), 2.10f, 15f);
                    break;
                case 3:
                    ActiveEnvironmentalHazardName = "SUB-STATION COOLANT VENT SURGE";
                    UIManager.Instance?.TriggerThreatWarning("!! FACILITY HAZARD: HIGH-PRESSURE COOLANT VENT SURGE !!", 1.85f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor + new Vector3(UnityEngine.Random.Range(-2.0f, 2.0f), 0f, UnityEngine.Random.Range(-2.0f, 2.0f))), 2.55f, 16f);
                    break;
                case 4:
                    ActiveEnvironmentalHazardName = "HELIOS PYLON EMP DISCHARGE";
                    UIManager.Instance?.TriggerThreatWarning("!! HELIOS PYLON SURGE: EMP SHOCKWAVE RING !!", 1.95f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor), 2.45f, 19f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor + new Vector3(UnityEngine.Random.Range(-4.5f, 4.5f), 0f, UnityEngine.Random.Range(-4.5f, 4.5f))), 2.10f, 16f);
                    break;
                default:
                    ActiveEnvironmentalHazardName = "AEON ORBITAL LANCE STRIKE";
                    UIManager.Instance?.TriggerThreatWarning("!! AEON OVERWATCH: ORBITAL KINETIC LANCE LOCKED !!", 1.95f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor), 2.50f, 20f);
                    SpawnHazardTelegraph(ClampPositionToUnlockedSectors(anchor + new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 0f, UnityEngine.Random.Range(-4.5f, 4.5f))), 2.25f, 18f);
                    break;
            }
        }

        private void StepCombatStageAdvance(float dt)
        {
            if (CurrentStage is not (1 or 2 or 5 or 6)) return;
            if (_activeEnemies.Count > 0 || _remainingReinforcements > 0) return;

            _stageBeatTimer += dt;
            if (_stageBeatTimer < 0.75f) return;

            _stageBeatTimer = 0f;
            BeginCampaignStage(CurrentStage + 1);
        }

        private void HandleObjectiveCompleted(CampaignObjectiveNode node)
        {
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

            if (node == PrimaryBreachNode && CurrentStage == 3)
            {
                _remainingReinforcements = 0;
                ClearRemainingEnemiesBeforeStageTransition();
                TriggerGateBreachTransition(4, StoryDirector.GetMissionShortTab(missionIndex));
                return;
            }

            if ((node == WestWingNode || node == EastWingNode) && CurrentStage == 4)
            {
                SabotageCompletedCount++;
                StoryDirector.NotifySabotageProgress(SabotageCompletedCount, 2, node.DisplayTitle);
                SyncMissionHud();
                if (SabotageCompletedCount >= 2)
                {
                    _remainingReinforcements = 0;
                    PlayerController.Instance?.AddFragGrenades(1);
                    BeginCampaignStage(5);
                }
                return;
            }

            if (node == DefenseHoldNode && CurrentStage == 7)
            {
                _remainingReinforcements = 0;
                ClearRemainingEnemiesBeforeStageTransition();
                if (missionIndex == 5)
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
                else
                {
                    BeginCampaignStage(8);
                }
                return;
            }

            if (node == ExfilNode && CurrentStage == 8)
            {
                _transitionPending = true;
                _remainingReinforcements = 0;
                ClearRemainingEnemiesBeforeStageTransition();
                GameManager.Instance?.CompleteCampaignVictory();
            }
        }

        private void ClearRemainingEnemiesBeforeStageTransition()
        {
            for (var i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                if (_activeEnemies[i] != null) _activeEnemies[i].ForceReturnToPool();
            }
            _activeEnemies.Clear();
        }

        public void CompleteAct4CoreDecision(CampaignEndingKind choice)
        {
            var banner = choice == CampaignEndingKind.BroadcastBlackout
                ? "BLACKOUT LEDGER BROADCAST // HOLD COURTYARD FOR VALKYRIE-6!"
                : "SPIRE DARK // SURVIVE EXTRACTION TO VALKYRIE-6!";
            UIManager.Instance?.ShowMilestoneBanner(banner);
            BeginCampaignStage(8);
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
            if (DefenseHoldNode != null && DefenseHoldNode.IsUnlocked) return DefenseHoldNode.transform.position;
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
            item.Initialize(_pickupPool, worldPos, kind, kind == PickupKind.Medkit ? 30f : 0f, HandlePickupReleased, creditAmount);
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
                if (CurrentStage is 1 or 2 or 5) _stageKillsProgress++;

                if (enemy.Archetype == EnemyArchetype.Boss)
                {
                    StoryDirector.NotifyBossDefeated(CurrentStage);
                    var center = enemy.transform.position;
                    SpawnPickupAt(PickupKind.Medkit, center);
                    SpawnPickupAt(PickupKind.MunitionCache, center + Vector3.right * 1.1f);
                    for (var i = 0; i < 4; i++)
                    {
                        var offset = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 1.4f;
                        SpawnPickupAt(PickupKind.CreditCoin, center + offset, 35);
                    }
                }
                else
                {
                    var roll = UnityEngine.Random.value;
                    switch (roll)
                    {
                        case < 0.16f:
                            SpawnPickupAt(PickupKind.MunitionCache, enemy.transform.position);
                            break;
                        case < 0.34f:
                            SpawnPickupAt(PickupKind.Medkit, enemy.transform.position);
                            break;
                        case < 0.64f:
                            SpawnPickupAt(PickupKind.CreditCoin, enemy.transform.position, (enemy.Archetype == EnemyArchetype.Heavy || enemy.Archetype == EnemyArchetype.Shielder) ? 25 : 15);
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
                _stageKillsProgress,
                _stageKillTarget,
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
            _stageKillsProgress = 0;
            _stageKillTarget = 6;
            _remainingReinforcements = 0;
            _reinforcementTimer = 0f;
            _reinforcementPoints = Array.Empty<Vector3>();
            _stageBeatTimer = 0f;
            _hazardPulseTimer = 0f;
            _hazardPulsePeriod = 0f;
            ActiveEnvironmentalHazardName = string.Empty;
            SabotageCompletedCount = 0;
            _holdoutWavesStarted = false;

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
