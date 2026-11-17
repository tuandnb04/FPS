using System.Collections.Generic;
using System.IO;
using Combat;
using Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.Rendering;

namespace TopDownShooter.Editor
{
    /// <summary>
    /// Procedural ProBuilder 6 architectural and prop kit generator providing unified beveled
    /// geometry, 35-degree smoothing breaks, compound buildings, multi-surface terrain decks,
    /// and saved single-submesh runtime objective/drop meshes across all 3 campaign scenes.
    /// </summary>
    internal static class ProBuilderKitBuilder
    {
        private const string SavedMeshesDir = "Assets/Models/ProBuilder";
        private const float DefaultSmoothAngle = 35f;

        /// <summary>
        /// Ensures the 3 runtime-spawned ProBuilder prop meshes (Sabotage Terminal, C4 Satchel,
        /// Munition Crate) exist as single-submesh mesh assets on disk and loads them.
        /// </summary>
        public static void BuildOrLoadRuntimePropMeshes(
            Material referenceSingleMat,
            out Mesh sabotageTerminalMesh,
            out Mesh c4ChargeMesh,
            out Mesh munitionCrateMesh)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Models"))
            {
                AssetDatabase.CreateFolder("Assets", "Models");
            }
            if (!AssetDatabase.IsValidFolder(SavedMeshesDir))
            {
                AssetDatabase.CreateFolder("Assets/Models", "ProBuilder");
            }

            sabotageTerminalMesh = BuildAndSaveSingleSubmeshAsset(
                SavedMeshesDir + "/PB_SabotageTerminal.asset",
                "PB_SabotageTerminal",
                referenceSingleMat,
                BuildSabotageTerminalParts);

            c4ChargeMesh = BuildAndSaveSingleSubmeshAsset(
                SavedMeshesDir + "/PB_C4Charge.asset",
                "PB_C4Charge",
                referenceSingleMat,
                BuildC4ChargeParts);

            munitionCrateMesh = BuildAndSaveSingleSubmeshAsset(
                SavedMeshesDir + "/PB_MunitionCrate.asset",
                "PB_MunitionCrate",
                referenceSingleMat,
                BuildMunitionCrateParts);

            AssetDatabase.SaveAssets();
        }

        private delegate void SingleSubmeshPartBuilder(List<ProBuilderMesh> parts, Material mat);

        private static Mesh BuildAndSaveSingleSubmeshAsset(
            string assetPath,
            string meshName,
            Material mat,
            SingleSubmeshPartBuilder builder)
        {
            var tempRoot = new GameObject("__PB_AssetGen_" + meshName);
            var parts = new List<ProBuilderMesh>();
            builder(parts, mat);
            var merged = MergeParts(parts, meshName, tempRoot.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
            var generatedMesh = merged != null && merged.GetComponent<MeshFilter>() != null
                ? merged.GetComponent<MeshFilter>().sharedMesh
                : null;

            Mesh savedCopy = null;
            if (generatedMesh != null)
            {
                savedCopy = Object.Instantiate(generatedMesh);
                savedCopy.name = meshName;
                if (File.Exists(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                AssetDatabase.CreateAsset(savedCopy, assetPath);
            }

            Object.DestroyImmediate(tempRoot);
            return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) ?? savedCopy;
        }

        private static void BuildSabotageTerminalParts(List<ProBuilderMesh> parts, Material mat)
        {
            var steel = new Color(0.26f, 0.29f, 0.33f, 1f);
            var chassis = new Color(0.68f, 0.72f, 0.76f, 1f);
            var screen = new Color(0.32f, 0.88f, 0.96f, 1f);
            var keypad = new Color(0.95f, 0.70f, 0.22f, 1f);

            parts.Add(MakeBeveledCube("Base", new Vector3(0.95f, 0.16f, 0.76f), new Vector3(0f, 0.08f, 0f), mat, 0.025f, color: steel));
            parts.Add(MakeBeveledCube("Pedestal", new Vector3(0.76f, 0.88f, 0.56f), new Vector3(0f, 0.60f, -0.02f), mat, 0.04f, color: chassis));
            parts.Add(MakeBeveledCube("ConsoleDeck", new Vector3(0.84f, 0.14f, 0.68f), new Vector3(0f, 1.06f, 0.04f), mat, 0.02f, color: steel));
            parts.Add(MakeBeveledCube("Keypad", new Vector3(0.54f, 0.05f, 0.24f), new Vector3(0f, 1.14f, 0.18f), mat, 0.008f, color: keypad));
            parts.Add(MakeBeveledCube("ScreenHood", new Vector3(0.80f, 0.50f, 0.12f), new Vector3(0f, 1.36f, -0.18f), mat, 0.02f, rot: Quaternion.Euler(-16f, 0f, 0f), color: steel));
            parts.Add(MakeBeveledCube("MonitorFace", new Vector3(0.68f, 0.36f, 0.04f), new Vector3(0f, 1.36f, -0.12f), mat, 0.006f, rot: Quaternion.Euler(-16f, 0f, 0f), color: screen));
            parts.Add(MakeCyl("Antenna", 8, 0.028f, 0.48f, new Vector3(0.32f, 1.56f, -0.22f), mat, color: steel));
        }

        private static void BuildC4ChargeParts(List<ProBuilderMesh> parts, Material mat)
        {
            var pack = new Color(0.55f, 0.58f, 0.44f, 1f);
            var strap = new Color(0.22f, 0.24f, 0.26f, 1f);
            var led = new Color(1.00f, 0.55f, 0.12f, 1f);

            parts.Add(MakeBeveledCube("SatchelBody", new Vector3(0.48f, 0.18f, 0.34f), new Vector3(0f, 0.09f, 0f), mat, 0.028f, color: pack));
            parts.Add(MakeBeveledCube("StrapL", new Vector3(0.07f, 0.20f, 0.36f), new Vector3(-0.14f, 0.09f, 0f), mat, 0.01f, color: strap));
            parts.Add(MakeBeveledCube("StrapR", new Vector3(0.07f, 0.20f, 0.36f), new Vector3(0.14f, 0.09f, 0f), mat, 0.01f, color: strap));
            parts.Add(MakeBeveledCube("DetonatorTimer", new Vector3(0.18f, 0.07f, 0.14f), new Vector3(0.04f, 0.20f, 0f), mat, 0.012f, color: strap));
            parts.Add(MakeBeveledCube("StatusBeacon", new Vector3(0.10f, 0.04f, 0.08f), new Vector3(-0.08f, 0.20f, 0f), mat, 0.006f, color: led));
            parts.Add(MakeCyl("WhipeAntenna", 6, 0.012f, 0.22f, new Vector3(0.16f, 0.28f, -0.09f), mat, color: strap));
        }

        private static void BuildMunitionCrateParts(List<ProBuilderMesh> parts, Material mat)
        {
            var olive = new Color(0.52f, 0.58f, 0.42f, 1f);
            var gold = new Color(0.95f, 0.78f, 0.24f, 1f);
            var latch = new Color(0.24f, 0.26f, 0.29f, 1f);

            parts.Add(MakeBeveledCube("CrateLower", new Vector3(0.68f, 0.34f, 0.44f), new Vector3(0f, 0.17f, 0f), mat, 0.025f, color: olive));
            parts.Add(MakeBeveledCube("CrateLid", new Vector3(0.72f, 0.11f, 0.48f), new Vector3(0f, 0.37f, 0f), mat, 0.02f, color: olive));
            parts.Add(MakeBeveledCube("AmmoBand", new Vector3(0.70f, 0.08f, 0.46f), new Vector3(0f, 0.24f, 0f), mat, 0.008f, color: gold));
            parts.Add(MakeBeveledCube("LatchFrontL", new Vector3(0.07f, 0.14f, 0.05f), new Vector3(-0.18f, 0.30f, 0.23f), mat, 0.008f, color: latch));
            parts.Add(MakeBeveledCube("LatchFrontR", new Vector3(0.07f, 0.14f, 0.05f), new Vector3(0.18f, 0.30f, 0.23f), mat, 0.008f, color: latch));
        }

        /// <summary>
        /// Builds bespoke ProBuilder ground decks, water basins, canyon shelves, underground wing floors,
        /// alpine suspension bridge, and transit spines tailored uniquely per mission.
        /// </summary>
        public static void BuildCompoundDeckAndRamps(Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats, int missionIndex)
        {
            var floorRoot = new GameObject("ArenaFloor");
            floorRoot.transform.SetParent(envRoot, false);

            var apronParts = new List<ProBuilderMesh>
            {
                MakeBeveledCube(
                    missionIndex == 1 ? "HarborWaterSurface" : "OuterFoundation",
                    new Vector3(190f, 0.4f, 214f),
                    new Vector3(0f, missionIndex == 4 ? -2.2f : -0.62f, 2f),
                    mats.DmzEarth,
                    0f)
            };
            MergeParts(apronParts, missionIndex == 1 ? "HarborWaterSurface" : "OuterFoundation", envRoot, castShadows: false, smoothingAngle: 0f);

            switch (missionIndex)
            {
                case 1:
                    BuildMission1TwinPierDeck(floorRoot.transform, envRoot, mats);
                    break;
                case 2:
                    BuildMission2CanyonRailDeck(floorRoot.transform, envRoot, mats);
                    break;
                case 3:
                    BuildMission3SubstationDeck(floorRoot.transform, envRoot, mats);
                    break;
                default:
                    BuildMission4SummitDeck(floorRoot.transform, envRoot, mats);
                    break;
            }
        }

        private static void AddDeckSlab(Transform floorRoot, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(floorRoot, false);
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube(name + "_Mesh", size, Vector3.zero, mat, 0.06f)
            };
            MergeParts(parts, name + "_PB", go.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildMission1TwinPierDeck(Transform floorRoot, Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            // H-Shaped Twin Harbor Piers leaving an open water channel in the middle (-10..+10, z -5..+13)
            AddDeckSlab(floorRoot, "SouthApronDeck", new Vector3(0f, -0.2f, -33f), new Vector3(48f, 0.4f, 38f), mats.Floor);
            AddDeckSlab(floorRoot, "PierYSplitDeck", new Vector3(0f, -0.2f, -10f), new Vector3(76f, 0.4f, 10f), mats.Floor);
            AddDeckSlab(floorRoot, "WestFuelPierDeck", new Vector3(-24f, -0.2f, 10f), new Vector3(28f, 0.4f, 32f), mats.Floor);
            AddDeckSlab(floorRoot, "EastRadarPierDeck", new Vector3(24f, -0.2f, 10f), new Vector3(28f, 0.4f, 32f), mats.Floor);
            AddDeckSlab(floorRoot, "CrossPierBridgeDeck", new Vector3(0f, -0.2f, 19f), new Vector3(24f, 0.4f, 12f), mats.Road);
            AddDeckSlab(floorRoot, "NorthQuayDeck", new Vector3(0f, -0.2f, 39f), new Vector3(56f, 0.4f, 30f), mats.Floor);

            // Roadway stripe down South Apron + Bridge curb + Offshore Barges
            var roadParts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("SouthCheckpointLane", new Vector3(9.6f, 0.02f, 36f), new Vector3(0f, 0.01f, -33f), mats.Road, 0.005f),
                MakeBeveledCube("BridgeHazardS", new Vector3(20f, 0.03f, 0.35f), new Vector3(0f, 0.015f, 13.3f), mats.Accent, 0f),
                MakeBeveledCube("BridgeHazardN", new Vector3(20f, 0.03f, 0.35f), new Vector3(0f, 0.015f, 24.4f), mats.Accent, 0f),
            };
            MergeParts(roadParts, "PierMarkings_PB", envRoot, castShadows: false, smoothingAngle: DefaultSmoothAngle);

            var seawallParts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Seawall_WestOuter", new Vector3(1.6f, 0.72f, 44f), new Vector3(-38.6f, -0.02f, 6f), mats.Walls, 0.06f),
                MakeBeveledCube("Seawall_EastOuter", new Vector3(1.6f, 0.72f, 44f), new Vector3(38.6f, -0.02f, 6f), mats.Walls, 0.06f),
            };
            for (var bz = -14f; bz <= 22f; bz += 12f)
            {
                seawallParts.Add(MakeCyl("BollardW_" + Mathf.RoundToInt(bz), 8, 0.24f, 0.52f, new Vector3(-37.4f, 0.26f, bz), mats.Trim));
                seawallParts.Add(MakeCyl("BollardE_" + Mathf.RoundToInt(bz), 8, 0.24f, 0.52f, new Vector3(37.4f, 0.26f, bz), mats.Trim));
            }
            MergeParts(seawallParts, "HarborSeawalls_PB", envRoot, castShadows: true, smoothingAngle: DefaultSmoothAngle);

            BuildCargoBarge(envRoot, "HarborBarge_West", new Vector3(-48f, -0.35f, 4f), 4f, mats);
            BuildCargoBarge(envRoot, "HarborBarge_East", new Vector3(48f, -0.35f, 8f), -176f, mats);
            BuildExfilGunboat(envRoot, "NE_ExfilGunboat", new Vector3(29.8f, -0.30f, 47f), mats);
        }

        private static void BuildMission2CanyonRailDeck(Transform floorRoot, Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            // S-Curve Canyon Floor + Twin Flanking Pockets
            AddDeckSlab(floorRoot, "SWCanyonEntryDeck", new Vector3(-14f, -0.2f, -37f), new Vector3(38f, 0.4f, 34f), mats.Floor);
            AddDeckSlab(floorRoot, "CentralRailYardDeck", new Vector3(0f, -0.2f, 6f), new Vector3(44f, 0.4f, 56f), mats.Floor);
            AddDeckSlab(floorRoot, "WestSwitchPocketDeck", new Vector3(-30f, -0.2f, 4f), new Vector3(22f, 0.4f, 28f), mats.Floor);
            AddDeckSlab(floorRoot, "EastSwitchPocketDeck", new Vector3(30f, -0.2f, 0f), new Vector3(22f, 0.4f, 28f), mats.Floor);
            AddDeckSlab(floorRoot, "NEGorgeTunnelDeck", new Vector3(7f, -0.2f, 45f), new Vector3(42f, 0.4f, 28f), mats.Floor);

            // Central Twin-Track Ballast & Steel Rails along x=0 (splitting the yard into West/East tactical trenches)
            var railParts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("MainRailBallast", new Vector3(4.6f, 0.04f, 80f), new Vector3(0f, 0.015f, 16f), mats.Road, 0.01f),
                MakeBeveledCube("MainRailL", new Vector3(0.18f, 0.12f, 80f), new Vector3(-0.95f, 0.06f, 16f), mats.RailSteel, 0.02f),
                MakeBeveledCube("MainRailR", new Vector3(0.18f, 0.12f, 80f), new Vector3(0.95f, 0.06f, 16f), mats.RailSteel, 0.02f),
            };
            for (var z = -22f; z <= 54f; z += 2.6f)
            {
                railParts.Add(MakeBeveledCube("RailTie_" + Mathf.RoundToInt(z), new Vector3(3.1f, 0.06f, 0.32f), new Vector3(0f, 0.03f, z), mats.Trim, 0.01f));
            }
            MergeParts(railParts, "CentralRailSpine_PB", envRoot, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildMission3SubstationDeck(Transform floorRoot, Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            // Multi-Wing Underground Complex: South Airlock -> Central Atrium + West Server Vault + East Turbine Hall -> North Command Deck
            AddDeckSlab(floorRoot, "SouthAirlockDeck", new Vector3(0f, -0.2f, -39f), new Vector3(30f, 0.4f, 28f), mats.Floor);
            AddDeckSlab(floorRoot, "CentralAtriumDeck", new Vector3(0f, -0.2f, -5f), new Vector3(38f, 0.4f, 44f), mats.Floor);
            AddDeckSlab(floorRoot, "WestServerVaultDeck", new Vector3(-30f, -0.2f, -3f), new Vector3(26f, 0.4f, 30f), mats.Road);
            AddDeckSlab(floorRoot, "EastTurbineHallDeck", new Vector3(30f, -0.2f, -3f), new Vector3(26f, 0.4f, 30f), mats.Road);
            AddDeckSlab(floorRoot, "NorthCommandDeck", new Vector3(0f, -0.2f, 34f), new Vector3(44f, 0.4f, 38f), mats.Floor);

            var floorTrim = new List<ProBuilderMesh>
            {
                MakeBeveledCube("AtriumCrossN", new Vector3(34f, 0.02f, 3.2f), new Vector3(0f, 0.01f, -3f), mats.Trim, 0.005f),
                MakeBeveledCube("AtriumSpine", new Vector3(4.6f, 0.02f, 86f), new Vector3(0f, 0.01f, -3f), mats.Trim, 0.005f),
                MakeBeveledCube("VaultWarningStrip", new Vector3(20f, 0.02f, 0.35f), new Vector3(-30f, 0.015f, -3f), mats.HeliosGlow, 0f),
                MakeBeveledCube("TurbineWarningStrip", new Vector3(20f, 0.02f, 0.35f), new Vector3(30f, 0.015f, -3f), mats.Accent, 0f),
            };
            MergeParts(floorTrim, "SubstationMarkings_PB", envRoot, castShadows: false, smoothingAngle: DefaultSmoothAngle);
            BuildFreightLiftPad(envRoot, new Vector3(0f, 0f, 45f), mats);
        }

        private static void BuildMission4SummitDeck(Transform floorRoot, Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            // Alpine Bridge + Wide Octagonal Citadel Plateau + North Helipad
            AddDeckSlab(floorRoot, "SouthAlpineBridgeDeck", new Vector3(0f, -0.2f, -40f), new Vector3(20f, 0.4f, 28f), mats.Road);
            AddDeckSlab(floorRoot, "CitadelOctagonPlaza", new Vector3(0f, -0.2f, 0f), new Vector3(80f, 0.4f, 54f), mats.Floor);
            AddDeckSlab(floorRoot, "NorthSummitHelipadDeck", new Vector3(0f, -0.2f, 40f), new Vector3(44f, 0.4f, 28f), mats.Floor);

            var plazaRing = new List<ProBuilderMesh>
            {
                MakeBeveledCube("PlazaRingN", new Vector3(24f, 0.025f, 0.5f), new Vector3(0f, 0.015f, 12f), mats.HeliosGlow, 0f),
                MakeBeveledCube("PlazaRingS", new Vector3(24f, 0.025f, 0.5f), new Vector3(0f, 0.015f, -12f), mats.HeliosGlow, 0f),
                MakeBeveledCube("PlazaRingW", new Vector3(0.5f, 0.025f, 24f), new Vector3(-12f, 0.015f, 0f), mats.HeliosGlow, 0f),
                MakeBeveledCube("PlazaRingE", new Vector3(0.5f, 0.025f, 24f), new Vector3(12f, 0.015f, 0f), mats.HeliosGlow, 0f),
                MakeBeveledCube("ConduitLineW", new Vector3(24f, 0.03f, 0.7f), new Vector3(-19f, 0.018f, 1.5f), mats.HeliosGlow, 0f),
                MakeBeveledCube("ConduitLineE", new Vector3(24f, 0.03f, 0.7f), new Vector3(19f, 0.018f, 1.5f), mats.HeliosGlow, 0f),
            };
            MergeParts(plazaRing, "SummitConduitLines_PB", envRoot, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildCargoBarge(Transform parent, string bargeName, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = new GameObject(bargeName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Hull", new Vector3(7.5f, 2.2f, 24f), new Vector3(0f, 0.6f, 0f), mats.Trim, 0.22f),
                MakePrism("Bow", new Vector3(7.4f, 2.2f, 5.2f), new Vector3(0f, 0.6f, 14.5f), mats.Rust, rot: Quaternion.Euler(0f, 0f, 0f)),
                MakeBeveledCube("DeckHouse", new Vector3(5.6f, 2.8f, 4.4f), new Vector3(0f, 3.0f, -8.5f), mats.Props, 0.10f),
                MakeBeveledCube("CargoISO_A", new Vector3(2.6f, 2.6f, 6.2f), new Vector3(-1.5f, 2.9f, 2.5f), mats.Olive, 0.08f),
                MakeBeveledCube("CargoISO_B", new Vector3(2.6f, 2.6f, 6.2f), new Vector3(1.5f, 2.9f, 2.5f), mats.Rust, 0.08f),
                MakeBeveledCube("CargoISO_Top", new Vector3(2.6f, 2.6f, 6.2f), new Vector3(1.5f, 5.5f, 2.5f), mats.Accent, 0.08f),
            };
            MergeParts(parts, bargeName + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildExfilGunboat(Transform parent, string name, Vector3 pos, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 12f, 0f));

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("GunboatHull", new Vector3(4.2f, 1.8f, 12.5f), new Vector3(0f, 0.55f, 0f), mats.Trim, 0.20f),
                MakePrism("GunboatBow", new Vector3(4.0f, 1.7f, 3.4f), new Vector3(0f, 0.55f, 7.8f), mats.FuelSteel, rot: Quaternion.Euler(0f, 0f, 0f)),
                MakeBeveledCube("Wheelhouse", new Vector3(2.8f, 2.0f, 3.2f), new Vector3(0f, 2.25f, -1.2f), mats.Walls, 0.08f),
                MakeBeveledCube("RadarMast", new Vector3(0.35f, 1.8f, 0.35f), new Vector3(0f, 4.1f, -1.2f), mats.Trim, 0.02f),
                MakeBeveledCube("SignalBeacon", new Vector3(0.65f, 0.35f, 0.65f), new Vector3(0f, 5.1f, -1.2f), mats.SodiumGlow, 0.02f),
                MakeBeveledCube("Gangplank", new Vector3(2.2f, 0.16f, 4.4f), new Vector3(-2.6f, 0.35f, 0.5f), mats.Accent, 0.02f, rot: Quaternion.Euler(0f, 90f, 0f)),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildArmoredLocomotive(Transform parent, string name, Vector3 pos, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = CreatePropRoot(parent, name, pos, 0f, new Vector3(3.2f, 4.2f, 10.6f));
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Chassis", new Vector3(3.15f, 1.1f, 10.4f), new Vector3(0f, 0.72f, 0f), mats.Trim, 0.06f),
                MakeBeveledCube("EngineHood", new Vector3(2.95f, 2.15f, 6.8f), new Vector3(0f, 2.25f, -1.5f), mats.FuelSteel, 0.12f),
                MakeBeveledCube("ArmoredCab", new Vector3(3.10f, 2.75f, 3.1f), new Vector3(0f, 2.55f, 3.2f), mats.Rust, 0.10f),
                MakeBeveledCube("CabSlit", new Vector3(3.16f, 0.35f, 1.6f), new Vector3(0f, 3.25f, 3.8f), mats.SodiumGlow, 0.02f),
                MakePrism("Cowcatcher", new Vector3(3.1f, 1.2f, 1.8f), new Vector3(0f, 0.62f, 5.8f), mats.Accent, rot: Quaternion.Euler(0f, 0f, 0f)),
                MakeCyl("ExhaustStack", 8, 0.42f, 0.9f, new Vector3(0f, 3.65f, -2.5f), mats.Trim),
                MakeBeveledCube("SideHazard", new Vector3(3.04f, 0.28f, 6.4f), new Vector3(0f, 2.2f, -1.5f), mats.Accent, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildTunnelPortal(Transform parent, string name, Vector3 pos, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("PortalWallW", new Vector3(4.5f, 6.2f, 4.2f), new Vector3(-6.2f, 3.1f, 0f), mats.Walls, 0.10f),
                MakeBeveledCube("PortalWallE", new Vector3(4.5f, 6.2f, 4.2f), new Vector3(6.2f, 3.1f, 0f), mats.Walls, 0.10f),
                MakeBeveledCube("PortalLintel", new Vector3(17.2f, 2.6f, 4.4f), new Vector3(0f, 5.5f, 0f), mats.Trim, 0.10f),
                MakeBeveledCube("TunnelVoid", new Vector3(8.2f, 4.2f, 2.5f), new Vector3(0f, 2.1f, 1.4f), mats.RailSteel, 0.02f),
                MakeBeveledCube("WarningStrobe", new Vector3(8.2f, 0.25f, 0.3f), new Vector3(0f, 4.35f, -2.2f), mats.SodiumGlow, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildServerRack(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(1.5f, 2.7f, 3.4f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("RackFrame", new Vector3(1.42f, 2.65f, 3.32f), new Vector3(0f, 1.32f, 0f), mats.Trim, 0.04f),
                MakeBeveledCube("BladePanelL", new Vector3(1.48f, 1.85f, 2.8f), new Vector3(0f, 1.35f, 0f), mats.FuelSteel, 0.02f),
                MakeBeveledCube("LedStripTop", new Vector3(1.52f, 0.14f, 2.9f), new Vector3(0f, 2.25f, 0f), mats.HeliosGlow, 0.01f),
                MakeBeveledCube("LedStripMid", new Vector3(1.52f, 0.12f, 2.9f), new Vector3(0f, 1.45f, 0f), mats.SodiumGlow, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildGeothermalTurbine(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(3.4f, 3.2f, 6.2f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("SkidBase", new Vector3(3.3f, 0.45f, 6.0f), new Vector3(0f, 0.22f, 0f), mats.Trim, 0.05f),
                MakeCyl("RotorDrum", 12, 1.35f, 4.8f, new Vector3(0f, 1.85f, 0f), mats.FuelSteel, rot: Quaternion.Euler(90f, 0f, 0f)),
                MakeCyl("CollarF", 12, 1.46f, 0.35f, new Vector3(0f, 1.85f, 1.7f), mats.Accent, rot: Quaternion.Euler(90f, 0f, 0f)),
                MakeCyl("CollarR", 12, 1.46f, 0.35f, new Vector3(0f, 1.85f, -1.7f), mats.Accent, rot: Quaternion.Euler(90f, 0f, 0f)),
                MakeCyl("TopExhaust", 8, 0.48f, 1.25f, new Vector3(0f, 3.25f, 0f), mats.Trim),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildFreightLiftPad(Transform parent, Vector3 centerPos, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var pad = new GameObject("NorthFreightLift");
            pad.transform.SetParent(parent, false);
            pad.transform.position = centerPos;

            var col = pad.AddComponent<BoxCollider>();
            col.size = new Vector3(11f, 0.12f, 11f);
            col.center = new Vector3(0f, 0.06f, 0f);

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("LiftPlate", new Vector3(11f, 0.12f, 11f), new Vector3(0f, 0.06f, 0f), mats.Trim, 0.03f),
                MakeBeveledCube("LiftStripeN", new Vector3(9.8f, 0.03f, 0.4f), new Vector3(0f, 0.12f, 4.8f), mats.Accent, 0f),
                MakeBeveledCube("LiftStripeS", new Vector3(9.8f, 0.03f, 0.4f), new Vector3(0f, 0.12f, -4.8f), mats.Accent, 0f),
                MakeBeveledCube("LiftStripeW", new Vector3(0.4f, 0.03f, 9.8f), new Vector3(-4.8f, 0.12f, 0f), mats.Accent, 0f),
                MakeBeveledCube("LiftStripeE", new Vector3(0.4f, 0.03f, 9.8f), new Vector3(4.8f, 0.12f, 0f), mats.Accent, 0f),
                MakeCyl("LiftPostNW", 8, 0.25f, 2.8f, new Vector3(-5.2f, 1.4f, 5.2f), mats.FuelSteel),
                MakeCyl("LiftPostNE", 8, 0.25f, 2.8f, new Vector3(5.2f, 1.4f, 5.2f), mats.FuelSteel),
            };
            MergeParts(parts, "NorthFreightLift_PB", pad.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildHeliosOrbitalSpire(Transform parent, Vector3 pos, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(5.2f, 12.0f, 5.2f);
            var root = CreatePropRoot(parent, "HeliosOrbitalSpire", pos, 0f, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("SpireBase", new Vector3(5.4f, 1.2f, 5.4f), new Vector3(0f, 0.6f, 0f), mats.Trim, 0.12f),
                MakeCyl("ReactorCore", 12, 1.75f, 7.6f, new Vector3(0f, 4.8f, 0f), mats.FuelSteel),
                MakeCyl("EnergyRingLow", 12, 2.15f, 0.55f, new Vector3(0f, 3.2f, 0f), mats.HeliosGlow),
                MakeCyl("EnergyRingMid", 12, 2.15f, 0.55f, new Vector3(0f, 5.8f, 0f), mats.HeliosGlow),
                MakeCyl("EnergyRingHigh", 12, 1.95f, 0.55f, new Vector3(0f, 8.2f, 0f), mats.HeliosGlow),
                MakeCone("EmitterNeedle", 1.15f, 3.4f, 10, new Vector3(0f, 10.4f, 0f), mats.HeliosGlow),
            };
            MergeParts(parts, "HeliosOrbitalSpire_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        /// <summary>
        /// Builds distinct ProBuilder perimeter walls, interior wing partitions, and flanking landmarks per mission.
        /// </summary>
        public static void BuildPerimeterWallsAndBuildings(Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats, int missionIndex)
        {
            var glow = missionIndex == 4 || missionIndex == 3 ? mats.HeliosGlow : mats.SodiumGlow;

            if (missionIndex == 1)
            {
                // Mission 1: Twin Harbor Piers Perimeter + Basin Guardrails + Cranes & Warehouses
                BuildPerimeterWall(envRoot, "Wall_South", new Vector3(0f, 0f, -52f), new Vector3(49.4f, 2.4f, 1.4f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Wall_North", new Vector3(0f, 0f, 54f), new Vector3(57.4f, 2.4f, 1.4f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Wall_SA_West", new Vector3(-24f, 0f, -33f), new Vector3(1.4f, 2.4f, 38.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Wall_SA_East", new Vector3(24f, 0f, -33f), new Vector3(1.4f, 2.4f, 38.4f), mats.Walls, mats.Trim, mats.Trim);

                // Inner water basin guardrails framing the central open harbor channel
                BuildPerimeterWall(envRoot, "BasinRail_S", new Vector3(0f, 0f, -5f), new Vector3(20.4f, 1.15f, 0.85f), mats.Trim, mats.Accent, mats.Accent);
                BuildPerimeterWall(envRoot, "BasinRail_W", new Vector3(-10f, 0f, 4f), new Vector3(0.85f, 1.15f, 18.4f), mats.Trim, mats.Accent, mats.Accent);
                BuildPerimeterWall(envRoot, "BasinRail_E", new Vector3(10f, 0f, 4f), new Vector3(0.85f, 1.15f, 18.4f), mats.Trim, mats.Accent, mats.Accent);
                BuildPerimeterWall(envRoot, "BridgeRail_S", new Vector3(0f, 0f, 13f), new Vector3(20.4f, 0.95f, 0.75f), mats.Trim, mats.Accent, mats.Trim);

                BuildPerimeterWall(envRoot, "PierWingWall_SW", new Vector3(-31f, 0f, -15f), new Vector3(15.4f, 1.6f, 1.2f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PierWingWall_SE", new Vector3(31f, 0f, -15f), new Vector3(15.4f, 1.6f, 1.2f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PierWall_West", new Vector3(-38f, 0f, 4.5f), new Vector3(1.4f, 1.8f, 40f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PierWall_East", new Vector3(38f, 0f, 4.5f), new Vector3(1.4f, 1.8f, 40f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PierWingWall_NW", new Vector3(-33f, 0f, 24f), new Vector3(11.4f, 2.0f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PierWingWall_NE", new Vector3(33f, 0f, 24f), new Vector3(11.4f, 2.0f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "NorthQuay_West", new Vector3(-28f, 0f, 39f), new Vector3(1.4f, 2.2f, 30.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "NorthQuay_East", new Vector3(28f, 0f, 39f), new Vector3(1.4f, 2.2f, 30.4f), mats.Walls, mats.Trim, mats.Trim);

                BuildCompoundWarehouse(envRoot, "CustomsHangar_SW", new Vector3(-34f, 0f, -34f), 90f, new Vector3(18f, 6.2f, 10f), mats, glow);
                BuildCompoundWarehouse(envRoot, "CustomsHangar_SE", new Vector3(34f, 0f, -32f), -90f, new Vector3(18f, 6.2f, 10f), mats, glow);
                BuildHarborGantryCrane(envRoot, "PierCrane_W", new Vector3(-42f, 0f, 8f), 90f, mats);
                BuildHarborGantryCrane(envRoot, "PierCrane_E", new Vector3(42f, 0f, 8f), -90f, mats);
            }
            else if (missionIndex == 2)
            {
                // Mission 2: S-Curve Canyon Gorge Walls & Overhead Pipe Bridges
                BuildPerimeterWall(envRoot, "Canyon_South", new Vector3(-14f, 0f, -54f), new Vector3(39.4f, 3.2f, 1.6f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Canyon_SW_West", new Vector3(-33f, 0f, -37f), new Vector3(1.6f, 3.2f, 35.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Canyon_SW_East", new Vector3(5f, 0f, -37f), new Vector3(1.6f, 3.2f, 35.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Canyon_WingSW", new Vector3(-27.5f, 0f, -20f), new Vector3(12f, 3.0f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Canyon_WingSE", new Vector3(13.5f, 0f, -20f), new Vector3(18f, 3.0f, 1.4f), mats.Walls, mats.Trim, mats.Trim);

                // West Switch Alcove boundaries
                BuildPerimeterWall(envRoot, "PocketW_South", new Vector3(-31.5f, 0f, -10f), new Vector3(20f, 2.8f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PocketW_North", new Vector3(-31.5f, 0f, 18f), new Vector3(20f, 2.8f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PocketW_West", new Vector3(-41f, 0f, 4f), new Vector3(1.6f, 3.2f, 29.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "YardW_Lower", new Vector3(-22f, 0f, -15f), new Vector3(1.6f, 3.0f, 11f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "YardW_Upper", new Vector3(-22f, 0f, 25.5f), new Vector3(1.6f, 3.0f, 15f), mats.Walls, mats.Trim, mats.Trim);

                // East Switch Alcove boundaries
                BuildPerimeterWall(envRoot, "PocketE_South", new Vector3(31.5f, 0f, -14f), new Vector3(20f, 2.8f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PocketE_North", new Vector3(31.5f, 0f, 14f), new Vector3(20f, 2.8f, 1.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "PocketE_East", new Vector3(41f, 0f, 0f), new Vector3(1.6f, 3.2f, 29.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "YardE_Lower", new Vector3(22f, 0f, -17f), new Vector3(1.6f, 3.0f, 7f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "YardE_Upper", new Vector3(22f, 0f, 23.5f), new Vector3(1.6f, 3.0f, 19f), mats.Walls, mats.Trim, mats.Trim);

                // NE Gorge & Tunnel
                BuildPerimeterWall(envRoot, "GorgeN_West", new Vector3(-14f, 0f, 45f), new Vector3(1.6f, 3.4f, 28.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "GorgeN_East", new Vector3(28f, 0f, 45f), new Vector3(1.6f, 3.4f, 28.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "GorgeN_NorthW", new Vector3(-6.5f, 0f, 58f), new Vector3(16f, 3.4f, 1.6f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "GorgeN_NorthE", new Vector3(21.5f, 0f, 58f), new Vector3(14f, 3.4f, 1.6f), mats.Walls, mats.Trim, mats.Trim);
                BuildTunnelPortal(envRoot, "NorthGorgeTunnelPortal", new Vector3(8f, 0f, 57.5f), mats);

                BuildCompoundWarehouse(envRoot, "RailControlHouse_W", new Vector3(-34f, 0f, 26f), 45f, new Vector3(14f, 7.0f, 9f), mats, glow);
                BuildRailOverheadPipeBridge(envRoot, "SwitchyardPipeBridge_S", new Vector3(0f, 0f, -5f), 44f, mats);
                BuildRailOverheadPipeBridge(envRoot, "SwitchyardPipeBridge_N", new Vector3(0f, 0f, 13.5f), 44f, mats);
            }
            else if (missionIndex == 3)
            {
                // Mission 3: Subterranean Multi-Wing Bunker Complex with Real Interior Partition Walls
                BuildPerimeterWall(envRoot, "Wall_AirlockS", new Vector3(0f, 0f, -53f), new Vector3(31.4f, 3.4f, 1.6f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Wall_AirlockW", new Vector3(-15f, 0f, -39f), new Vector3(1.6f, 3.4f, 29.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Wall_AirlockE", new Vector3(15f, 0f, -39f), new Vector3(1.6f, 3.4f, 29.4f), mats.Walls, mats.Trim, mats.Trim);

                // Interior Partition Walls between Atrium and West/East Wings (leaving 11m doorways at z in -8.5..+2.5)
                BuildPerimeterWall(envRoot, "Part_AtriumW_S", new Vector3(-18f, 0f, -17.5f), new Vector3(1.5f, 3.2f, 17.5f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Part_AtriumW_N", new Vector3(-18f, 0f, 9.5f), new Vector3(1.5f, 3.2f, 14.0f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Part_AtriumE_S", new Vector3(18f, 0f, -17.5f), new Vector3(1.5f, 3.2f, 17.5f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Part_AtriumE_N", new Vector3(18f, 0f, 9.5f), new Vector3(1.5f, 3.2f, 14.0f), mats.Walls, mats.Trim, mats.Trim);

                // West Server Vault outer walls
                BuildPerimeterWall(envRoot, "Vault_West", new Vector3(-43f, 0f, -3f), new Vector3(1.6f, 3.4f, 31.4f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Vault_South", new Vector3(-30.5f, 0f, -18f), new Vector3(26.4f, 3.4f, 1.5f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Vault_North", new Vector3(-30.5f, 0f, 12f), new Vector3(26.4f, 3.4f, 1.5f), mats.Walls, mats.Trim, mats.Trim);

                // East Turbine Hall outer walls
                BuildPerimeterWall(envRoot, "Turbine_East", new Vector3(43f, 0f, -3f), new Vector3(1.6f, 3.4f, 31.4f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Turbine_South", new Vector3(30.5f, 0f, -18f), new Vector3(26.4f, 3.4f, 1.5f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Turbine_North", new Vector3(30.5f, 0f, 12f), new Vector3(26.4f, 3.4f, 1.5f), mats.Walls, mats.Trim, mats.Trim);

                // North Command Deck walls
                BuildPerimeterWall(envRoot, "Cmd_West", new Vector3(-22f, 0f, 34f), new Vector3(1.6f, 3.6f, 38.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Cmd_East", new Vector3(22f, 0f, 34f), new Vector3(1.6f, 3.6f, 38.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Cmd_North", new Vector3(0f, 0f, 53f), new Vector3(45.4f, 3.6f, 1.6f), mats.Walls, mats.Trim, mats.Accent);

                BuildCompoundWarehouse(envRoot, "SubstationGenerator_NW", new Vector3(-34f, 0f, 32f), 90f, new Vector3(16f, 6.2f, 10f), mats, glow);
                BuildCompoundWarehouse(envRoot, "SubstationGenerator_NE", new Vector3(34f, 0f, 32f), -90f, new Vector3(16f, 6.2f, 10f), mats, glow);
            }
            else
            {
                // Mission 4: South Bridge + Chamfered Octagonal Citadel Fortress + North Summit Pad
                BuildPerimeterWall(envRoot, "Bridge_South", new Vector3(0f, 0f, -54f), new Vector3(21.4f, 1.6f, 1.2f), mats.Walls, mats.Trim, mats.Accent);
                BuildPerimeterWall(envRoot, "Bridge_ParapetW", new Vector3(-10f, 0f, -40f), new Vector3(1.2f, 1.5f, 28.4f), mats.Trim, mats.HeliosGlow, mats.Trim);
                BuildPerimeterWall(envRoot, "Bridge_ParapetE", new Vector3(10f, 0f, -40f), new Vector3(1.2f, 1.5f, 28.4f), mats.Trim, mats.HeliosGlow, mats.Trim);

                // Wide Citadel Courtyard Perimeter
                BuildPerimeterWall(envRoot, "Citadel_SW_Wing", new Vector3(-25f, 0f, -26f), new Vector3(31f, 2.8f, 1.6f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Citadel_SE_Wing", new Vector3(25f, 0f, -26f), new Vector3(31f, 2.8f, 1.6f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Citadel_West", new Vector3(-40f, 0f, 0f), new Vector3(1.6f, 3.0f, 53.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Citadel_East", new Vector3(40f, 0f, 0f), new Vector3(1.6f, 3.0f, 53.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Citadel_NW_Wing", new Vector3(-31f, 0f, 26f), new Vector3(19f, 2.8f, 1.6f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "Citadel_NE_Wing", new Vector3(31f, 0f, 26f), new Vector3(19f, 2.8f, 1.6f), mats.Walls, mats.Trim, mats.Trim);

                // North Summit Helipad Perimeter
                BuildPerimeterWall(envRoot, "SummitPad_West", new Vector3(-22f, 0f, 40f), new Vector3(1.4f, 2.4f, 28.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "SummitPad_East", new Vector3(22f, 0f, 40f), new Vector3(1.4f, 2.4f, 28.4f), mats.Walls, mats.Trim, mats.Trim);
                BuildPerimeterWall(envRoot, "SummitPad_North", new Vector3(0f, 0f, 54f), new Vector3(45.4f, 2.4f, 1.4f), mats.Walls, mats.Trim, mats.Accent);

                BuildWatchtower(envRoot, "CitadelTower_SW", new Vector3(-41.5f, 0f, -27.5f), mats, glow);
                BuildWatchtower(envRoot, "CitadelTower_SE", new Vector3(41.5f, 0f, -27.5f), mats, glow);
                BuildWatchtower(envRoot, "CitadelTower_NW", new Vector3(-41.5f, 0f, 27.5f), mats, glow);
                BuildWatchtower(envRoot, "CitadelTower_NE", new Vector3(41.5f, 0f, 27.5f), mats, glow);
                BuildObservatoryBastion(envRoot, "CitadelTelemetry_W", new Vector3(-49f, 0f, 2f), 90f, mats, glow);
                BuildObservatoryBastion(envRoot, "CitadelTelemetry_E", new Vector3(49f, 0f, 2f), -90f, mats, glow);
            }
        }

        public static void BuildPerimeterWall(Transform parent, string wallName, Vector3 pos, Vector3 size, Material matWall, Material matTrim, Material matGlow)
        {
            var root = new GameObject(wallName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            var col = root.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("MainBlock", size, new Vector3(0f, size.y * 0.5f, 0f), matWall, 0.06f),
                MakeBeveledCube("BaseFooting", new Vector3(size.x + 0.24f, 0.28f, size.z + 0.24f), new Vector3(0f, 0.14f, 0f), matTrim, 0.04f),
                MakeBeveledCube("TopCap", new Vector3(size.x + 0.20f, 0.18f, size.z + 0.20f), new Vector3(0f, size.y + 0.08f, 0f), matTrim, 0.04f),
                MakeBeveledCube("WarningBand", new Vector3(size.x + 0.06f, 0.09f, size.z + 0.06f), new Vector3(0f, size.y * 0.78f, 0f), matGlow, 0.01f),
            };

            // Add spaced structural concrete pilasters along long axes
            var longAlongX = size.x >= size.z;
            var span = longAlongX ? size.x : size.z;
            var thick = longAlongX ? size.z : size.x;
            if (span >= 8f)
            {
                var steps = Mathf.Max(2, Mathf.FloorToInt(span / 10f));
                for (var i = 0; i <= steps; i++)
                {
                    var t = -span * 0.45f + (span * 0.90f) * (i / (float)steps);
                    var pPos = longAlongX ? new Vector3(t, size.y * 0.52f, 0f) : new Vector3(0f, size.y * 0.52f, t);
                    var pSize = longAlongX ? new Vector3(0.68f, size.y * 1.04f, thick + 0.36f) : new Vector3(thick + 0.36f, size.y * 1.04f, 0.68f);
                    parts.Add(MakeBeveledCube("Pilaster_" + i, pSize, pPos, matTrim, 0.04f));
                }
            }

            MergeParts(parts, wallName + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildWatchtower(Transform parent, string name, Vector3 pos, SceneSetupBuilder.ArenaMaterialPack mats, Material matWindowGlow)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("BasePad", new Vector3(4.2f, 0.45f, 4.2f), new Vector3(0f, 0.22f, 0f), mats.Trim, 0.05f),
                MakeBeveledCube("LegNW", new Vector3(0.45f, 3.6f, 0.45f), new Vector3(-1.35f, 2.1f, 1.35f), mats.Trim, 0.04f),
                MakeBeveledCube("LegNE", new Vector3(0.45f, 3.6f, 0.45f), new Vector3(1.35f, 2.1f, 1.35f), mats.Trim, 0.04f),
                MakeBeveledCube("LegSW", new Vector3(0.45f, 3.6f, 0.45f), new Vector3(-1.35f, 2.1f, -1.35f), mats.Trim, 0.04f),
                MakeBeveledCube("LegSE", new Vector3(0.45f, 3.6f, 0.45f), new Vector3(1.35f, 2.1f, -1.35f), mats.Trim, 0.04f),
                MakeBeveledCube("CabFloor", new Vector3(3.9f, 0.32f, 3.9f), new Vector3(0f, 3.95f, 0f), mats.Walls, 0.05f),
                MakeBeveledCube("CabParapet", new Vector3(3.7f, 1.15f, 3.7f), new Vector3(0f, 4.65f, 0f), mats.Walls, 0.05f),
                MakeBeveledCube("WindowBand", new Vector3(3.78f, 0.42f, 3.78f), new Vector3(0f, 5.45f, 0f), matWindowGlow, 0.02f),
                MakeBeveledCube("Roof", new Vector3(4.4f, 0.35f, 4.4f), new Vector3(0f, 5.95f, 0f), mats.Trim, 0.08f),
                MakeCyl("RoofRadar", 8, 0.35f, 0.65f, new Vector3(0.8f, 6.42f, 0.6f), mats.FuelSteel),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildCompoundWarehouse(Transform parent, string name, Vector3 pos, float yaw, Vector3 size, SceneSetupBuilder.ArenaMaterialPack mats, Material matGlow)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Plinth", new Vector3(size.x + 0.6f, 0.45f, size.z + 0.6f), new Vector3(0f, 0.22f, 0f), mats.Trim, 0.06f),
                MakeBeveledCube("Hull", size, new Vector3(0f, size.y * 0.5f + 0.35f, 0f), mats.Walls, 0.10f),
                MakeBeveledCube("ShutterBay", new Vector3(size.x * 0.42f, size.y * 0.62f, 0.3f), new Vector3(0f, size.y * 0.35f + 0.2f, size.z * 0.5f + 0.05f), mats.Trim, 0.04f),
                MakeBeveledCube("ClerestoryGlow", new Vector3(size.x * 0.86f, 0.36f, size.z + 0.08f), new Vector3(0f, size.y * 0.85f + 0.35f, 0f), matGlow, 0.01f),
                MakePrism("GableRoof", new Vector3(size.x + 0.5f, 1.8f, size.z + 0.5f), new Vector3(0f, size.y + 1.25f, 0f), mats.Props),
                MakeCyl("RoofVentL", 8, 0.65f, 0.85f, new Vector3(-size.x * 0.26f, size.y + 0.8f, 0f), mats.FuelSteel),
                MakeCyl("RoofVentR", 8, 0.65f, 0.85f, new Vector3(size.x * 0.26f, size.y + 0.8f, 0f), mats.FuelSteel),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildHarborGantryCrane(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("LegS", new Vector3(1.1f, 9.5f, 1.1f), new Vector3(0f, 4.75f, -4.5f), mats.Rust, 0.06f),
                MakeBeveledCube("LegN", new Vector3(1.1f, 9.5f, 1.1f), new Vector3(0f, 4.75f, 4.5f), mats.Rust, 0.06f),
                MakeBeveledCube("TopGirder", new Vector3(2.2f, 1.2f, 15.5f), new Vector3(0f, 9.8f, 0f), mats.Accent, 0.08f),
                MakeBeveledCube("MachineryCab", new Vector3(2.8f, 2.2f, 3.6f), new Vector3(0f, 10.9f, -1.5f), mats.Trim, 0.08f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildRailOverheadPipeBridge(Transform parent, string name, Vector3 pos, float width, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("PipeSpanMain", new Vector3(width, 0.85f, 0.95f), new Vector3(0f, 5.4f, 0f), mats.FuelSteel, 0.06f),
                MakeBeveledCube("PipeSpanAux", new Vector3(width, 0.45f, 0.55f), new Vector3(0f, 6.05f, 0f), mats.Trim, 0.04f),
                MakeBeveledCube("TowerW", new Vector3(1.3f, 6.2f, 1.3f), new Vector3(-width * 0.49f, 3.1f, 0f), mats.Trim, 0.06f),
                MakeBeveledCube("TowerE", new Vector3(1.3f, 6.2f, 1.3f), new Vector3(width * 0.49f, 3.1f, 0f), mats.Trim, 0.06f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        private static void BuildObservatoryBastion(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats, Material matGlow)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("BastionBase", new Vector3(14f, 4.4f, 10f), new Vector3(0f, 2.2f, 0f), mats.Walls, 0.12f),
                MakeCyl("TelemetryDrum", 10, 4.2f, 3.2f, new Vector3(0f, 5.8f, 0f), mats.Trim),
                MakeIco("RadarDome", 3.6f, 1, new Vector3(0f, 7.6f, 0f), mats.Props),
                MakeBeveledCube("BeaconBand", new Vector3(14.15f, 0.28f, 10.15f), new Vector3(0f, 4.1f, 0f), matGlow, 0.02f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        /// <summary>
        /// Builds ProBuilder outer skyline ridges, coastal/canyon rock mesas, boulders,
        /// and 8-sided multi-tiered conifer pine trees replacing heavy Blender terrain assets.
        /// </summary>
        public static void BuildProBuilderHorizon(Transform envRoot, SceneSetupBuilder.ArenaMaterialPack mats, int missionIndex)
        {
            var hills = new GameObject("PerimeterBackdrop");
            hills.transform.SetParent(envRoot, false);

            var heightScale = missionIndex == 2 ? 1.38f : (missionIndex == 1 ? 0.82f : 1.25f);
            var peakSpecs = new (Vector3 pos, float yaw, Vector3 scale)[]
            {
                (new Vector3(0f, 0f, 79f), 12f, new Vector3(3.4f, 3.0f, 2.5f)),
                (new Vector3(-26f, 0f, 75f), -18f, new Vector3(2.8f, 2.4f, 2.3f)),
                (new Vector3(26f, 0f, 75f), 22f, new Vector3(2.8f, 2.5f, 2.3f)),
                (new Vector3(-52f, 0f, 70f), 38f, new Vector3(2.6f, 2.7f, 2.4f)),
                (new Vector3(52f, 0f, 70f), -42f, new Vector3(2.6f, 2.6f, 2.4f)),
                (new Vector3(-47f, 0f, 44f), 84f, new Vector3(1.8f, 1.9f, 1.9f)),
                (new Vector3(-56f, 0f, 26f), 102f, new Vector3(2.2f, 2.3f, 2.0f)),
                (new Vector3(-56f, 0f, 4f), 78f, new Vector3(2.1f, 2.1f, 2.1f)),
                (new Vector3(-54f, 0f, -18f), 96f, new Vector3(2.0f, 1.8f, 2.0f)),
                (new Vector3(-47f, 0f, -38f), 112f, new Vector3(1.9f, 1.7f, 1.8f)),
                (new Vector3(-44f, 0f, -56f), 45f, new Vector3(2.1f, 1.6f, 1.8f)),
                (new Vector3(47f, 0f, 44f), -84f, new Vector3(1.8f, 1.9f, 1.9f)),
                (new Vector3(56f, 0f, 26f), -102f, new Vector3(2.2f, 2.3f, 2.0f)),
                (new Vector3(56f, 0f, 4f), -78f, new Vector3(2.1f, 2.1f, 2.1f)),
                (new Vector3(54f, 0f, -18f), -96f, new Vector3(2.0f, 1.8f, 2.0f)),
                (new Vector3(47f, 0f, -38f), -112f, new Vector3(1.9f, 1.7f, 1.8f)),
                (new Vector3(44f, 0f, -56f), -45f, new Vector3(2.1f, 1.6f, 1.8f)),
                (new Vector3(-18f, 0f, -68f), 172f, new Vector3(2.6f, 1.6f, 2.0f)),
                (new Vector3(18f, 0f, -68f), -170f, new Vector3(2.6f, 1.6f, 2.0f)),
                (new Vector3(0f, 0f, -74f), 184f, new Vector3(3.1f, 2.0f, 2.2f)),
            };

            for (var i = 0; i < peakSpecs.Length; i++)
            {
                var p = peakSpecs[i];
                var peakPos = p.pos;
                if (missionIndex == 1 && Mathf.Abs(peakPos.x) > 35f && peakPos.z > -52f && peakPos.z < 64f)
                {
                    peakPos.x += Mathf.Sign(peakPos.x) * 24f;
                }

                var h = 5.4f * p.scale.y * heightScale;
                var w = 11.5f * p.scale.x;
                var d = 8.6f * p.scale.z;
                var peakParts = new List<ProBuilderMesh>
                {
                    MakeFacetedMesaPeak(w, h, d, mats.DmzHill, mats.Rock, missionIndex == 2)
                };
                var go = MergeParts(peakParts, "HorizonPeak_" + i, hills.transform, castShadows: false, smoothingAngle: 0f);
                if (go != null)
                {
                    go.transform.SetPositionAndRotation(new Vector3(peakPos.x, -0.35f, peakPos.z), Quaternion.Euler(0f, p.yaw, 0f));
                }
            }

            if (missionIndex != 2 && missionIndex != 3)
            {
                var treeCoords = new (float x, float z, float s, float yaw)[]
                {
                    (-33.5f, -49f, 1.25f, 18f), (-35.8f, -41f, 1.45f, 84f), (-32.5f, -33f, 1.15f, 142f), (-36.5f, -24f, 1.38f, 210f),
                    (-43.0f, -10f, 1.32f, 45f), (-42.5f, 0f, 1.48f, 118f), (-44.0f, 12f, 1.28f, 260f), (-35.5f, 29f, 1.42f, 33f),
                    (-34.2f, 38f, 1.18f, 195f), (-36.8f, 49f, 1.50f, 72f), (-26.5f, 61.5f, 1.35f, 128f), (-14.5f, 63.0f, 1.22f, 285f),
                    (33.5f, -49f, 1.28f, 52f), (35.8f, -40f, 1.42f, 165f), (32.8f, -31f, 1.16f, 240f), (36.2f, -22f, 1.44f, 310f),
                    (43.2f, -9f, 1.36f, 22f), (42.8f, 2f, 1.52f, 150f), (44.2f, 14f, 1.30f, 275f), (35.8f, 30f, 1.40f, 64f),
                    (34.5f, 40f, 1.20f, 218f), (37.0f, 50f, 1.48f, 95f), (26.5f, 61.5f, 1.36f, 180f), (14.5f, 63.0f, 1.26f, 42f),
                    (-21.5f, -58.5f, 1.30f, 88f), (-9.0f, -59.5f, 1.12f, 174f), (9.5f, -59.5f, 1.18f, 295f), (22.0f, -58.5f, 1.34f, 12f),
                };

                for (var i = 0; i < treeCoords.Length; i++)
                {
                    var t = treeCoords[i];
                    if (missionIndex == 1 && t.z > -54f && t.z < 60f) continue;
                    if (missionIndex == 4 && Mathf.Abs(t.x) < 42f && t.z > -30f && t.z < 56f) continue;
                    BuildConiferPine(hills.transform, "PerimeterPine_" + i, new Vector3(t.x, -0.35f, t.z), t.s, t.yaw, mats);
                }

                if (missionIndex == 4)
                {
                    var outerAlpine = new (float x, float z, float s, float yaw)[]
                    {
                        (-46f, -34f, 1.55f, 40f), (46f, -34f, 1.55f, 140f), (-50f, -4f, 1.62f, 92f), (50f, -4f, 1.62f, 240f),
                        (-46f, 36f, 1.58f, 15f), (46f, 36f, 1.58f, 195f), (-31f, 66f, 1.65f, 70f), (31f, 66f, 1.65f, 285f),
                        (-6f, 66f, 1.52f, 115f), (6f, 66f, 1.52f, 330f), (-28f, -63f, 1.48f, 55f), (28f, -63f, 1.48f, 210f)
                    };
                    for (var i = 0; i < outerAlpine.Length; i++)
                    {
                        var t = outerAlpine[i];
                        BuildConiferPine(hills.transform, "AlpinePineOuter_" + i, new Vector3(t.x, -2.0f, t.z), t.s, t.yaw, mats);
                    }
                }
            }

            var rockCoords = new (float x, float z, float sx, float sy, float sz, float yaw)[]
            {
                (-31.8f, -45f, 1.15f, 1.20f, 1.10f, 25f), (-45.5f, -20f, 1.30f, 1.45f, 1.25f, 110f),
                (-46.5f, -4f, 1.40f, 1.35f, 1.20f, 200f), (-36.0f, 44f, 1.25f, 1.40f, 1.15f, 70f),
                (-19.0f, 61.0f, 1.45f, 1.50f, 1.30f, 145f), (19.0f, 61.0f, 1.45f, 1.50f, 1.30f, 35f),
                (31.8f, -45f, 1.15f, 1.20f, 1.10f, 285f), (45.5f, -20f, 1.30f, 1.45f, 1.25f, 60f),
                (46.5f, -4f, 1.40f, 1.35f, 1.20f, 170f), (36.0f, 44f, 1.25f, 1.40f, 1.15f, 315f),
                (-14.5f, -57.0f, 1.25f, 1.20f, 1.15f, 92f), (14.5f, -57.0f, 1.25f, 1.20f, 1.15f, 212f),
            };
            var rockBoost = missionIndex == 2 ? 1.55f : 1.0f;
            for (var i = 0; i < rockCoords.Length; i++)
            {
                var r = rockCoords[i];
                if (missionIndex == 1 && r.z > -54f && r.z < 58f) continue;
                if (missionIndex == 3) continue; // Underground facility has zero outdoor rocks
                BuildRockOutcrop(hills.transform, "PerimeterRock_" + i, new Vector3(r.x, missionIndex == 4 ? -2.0f : -0.35f, r.z), new Vector3(r.sx, r.sy, r.sz) * rockBoost, r.yaw, mats.Rock);
            }

            if (missionIndex == 2)
            {
                var canyonBoulders = new (float x, float z, float s, float yaw)[]
                {
                    (-42f, -32f, 2.2f, 14f), (42f, -32f, 2.2f, 194f), (-44f, 16f, 2.4f, 88f), (44f, 16f, 2.4f, 268f),
                    (-36f, 54f, 2.1f, 42f), (36f, 54f, 2.1f, 152f), (0f, 62f, 2.3f, 75f), (0f, -60f, 2.0f, 255f)
                };
                for (var i = 0; i < canyonBoulders.Length; i++)
                {
                    var b = canyonBoulders[i];
                    BuildRockOutcrop(hills.transform, "CanyonBoulder_" + i, new Vector3(b.x, -0.35f, b.z), new Vector3(b.s, b.s * 1.3f, b.s), b.yaw, mats.Rock);
                }
            }
        }

        private static ProBuilderMesh MakeFacetedMesaPeak(float width, float height, float depth, Material matSlope, Material matRock, bool flatMesa)
        {
            var pb = ShapeGenerator.GenerateCone(PivotLocation.Center, 0.5f, 1.0f, 8);
            var verts = pb.GetVertices();
            for (var i = 0; i < verts.Length; i++)
            {
                var p = verts[i].position;
                var ny = Mathf.Clamp01(p.y + 0.5f);
                var n = Mathf.Sin(i * 1.93f + p.x * 3.1f) * 0.14f + Mathf.Cos(i * 2.71f + p.z * 2.8f) * 0.10f;
                if (flatMesa && ny > 0.72f)
                {
                    ny = 0.72f + (ny - 0.72f) * 0.2f;
                }
                var taper = flatMesa ? Mathf.Lerp(1.0f, 0.42f, ny) : Mathf.Lerp(1.0f, 0.16f, ny);
                var radial = 1f + n * (1f - ny * 0.4f);
                verts[i].position = new Vector3(
                    p.x * width * taper * radial,
                    ny * height,
                    p.z * depth * taper * radial);
            }
            pb.SetVertices(verts);
            pb.SetMaterial(pb.faces, matSlope);
            foreach (var f in pb.faces)
            {
                var cy = 0f;
                foreach (var idx in f.distinctIndexes) cy += verts[idx].position.y;
                cy /= Mathf.Max(1, f.distinctIndexes.Count);
                if (cy < height * 0.35f)
                {
                    pb.SetMaterial(new[] { f }, matRock);
                }
            }
            pb.ToMesh();
            pb.Refresh();
            return pb;
        }

        private static void BuildConiferPine(Transform parent, string name, Vector3 pos, float scale, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var parts = new List<ProBuilderMesh>
            {
                MakeCyl("Trunk", 6, 0.22f * scale, 1.6f * scale, new Vector3(0f, 0.8f * scale, 0f), mats.Trim),
                MakeCone("Tier1", 1.35f * scale, 1.8f * scale, 7, new Vector3(0f, 2.0f * scale, 0f), mats.Foliage),
                MakeCone("Tier2", 1.05f * scale, 1.7f * scale, 7, new Vector3(0f, 3.0f * scale, 0f), mats.Foliage),
                MakeCone("Tier3", 0.72f * scale, 1.4f * scale, 7, new Vector3(0f, 4.0f * scale, 0f), mats.Foliage),
            };
            var go = MergeParts(parts, name, parent, castShadows: false, smoothingAngle: 0f);
            if (go != null)
            {
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            }
        }

        private static void BuildRockOutcrop(Transform parent, string name, Vector3 pos, Vector3 scale, float yaw, Material matRock)
        {
            var parts = new List<ProBuilderMesh>
            {
                MakeIco("RockMain", 1.15f, 1, new Vector3(0f, 0.55f * scale.y, 0f), matRock, scale: new Vector3(1.25f * scale.x, 0.72f * scale.y, 0.98f * scale.z)),
                MakeIco("RockSide", 0.78f, 1, new Vector3(0.85f * scale.x, 0.35f * scale.y, 0.3f * scale.z), matRock, scale: new Vector3(scale.x, 0.65f * scale.y, scale.z)),
            };
            var go = MergeParts(parts, name, parent, castShadows: false, smoothingAngle: 0f);
            if (go != null)
            {
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            }
        }

        /// <summary>
        /// Builds ProBuilder Sector Blast Gate Divider with beveled gantry bridge & moving blast doors.
        /// </summary>
        public static void BuildGateDivider(
            Transform parent,
            string gateName,
            float zPos,
            float totalWidth,
            float openingWidth,
            Material matWalls,
            Material matTrim,
            Material matAccent,
            out Transform leftLeafOut,
            out Transform rightLeafOut,
            float centerX = 0f)
        {
            var root = new GameObject(gateName);
            root.transform.SetParent(parent, false);

            var wingW = Mathf.Max(2f, (totalWidth - openingWidth) * 0.5f);
            var wingOffsetX = openingWidth * 0.5f + wingW * 0.5f;
            BuildPerimeterWall(root.transform, gateName + "_WingWest", new Vector3(centerX - wingOffsetX, 0f, zPos), new Vector3(wingW, 2.6f, 1.5f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(root.transform, gateName + "_WingEast", new Vector3(centerX + wingOffsetX, 0f, zPos), new Vector3(wingW, 2.6f, 1.5f), matWalls, matTrim, matTrim);

            var gantryParts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("PylonW", new Vector3(1.1f, 3.6f, 1.7f), new Vector3(centerX - openingWidth * 0.5f - 0.55f, 1.8f, zPos), matTrim, 0.05f),
                MakeBeveledCube("PylonE", new Vector3(1.1f, 3.6f, 1.7f), new Vector3(centerX + openingWidth * 0.5f + 0.55f, 1.8f, zPos), matTrim, 0.05f),
                MakeBeveledCube("GantrySpan", new Vector3(openingWidth + 2.4f, 0.68f, 1.65f), new Vector3(centerX, 3.30f, zPos), matTrim, 0.05f),
                MakeBeveledCube("GantryRoof", new Vector3(openingWidth + 1.8f, 0.24f, 1.90f), new Vector3(centerX, 3.74f, zPos), matWalls, 0.04f),
                MakeBeveledCube("HazardS", new Vector3(openingWidth, 0.16f, 0.10f), new Vector3(centerX, 3.04f, zPos - 0.84f), matAccent, 0.01f),
                MakeBeveledCube("HazardN", new Vector3(openingWidth, 0.16f, 0.10f), new Vector3(centerX, 3.04f, zPos + 0.84f), matAccent, 0.01f),
            };
            MergeParts(gantryParts, gateName + "_Gantry_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);

            var leafW = openingWidth * 0.5f;
            leftLeafOut = CreateSlidingBlastLeaf(root.transform, gateName + "_LeafLeft", new Vector3(centerX - leafW * 0.5f, 0f, zPos), new Vector3(leafW, 2.45f, 0.72f), matTrim, matAccent);
            rightLeafOut = CreateSlidingBlastLeaf(root.transform, gateName + "_LeafRight", new Vector3(centerX + leafW * 0.5f, 0f, zPos), new Vector3(leafW, 2.45f, 0.72f), matTrim, matAccent);
        }

        private static Transform CreateSlidingBlastLeaf(Transform parent, string leafName, Vector3 pos, Vector3 size, Material matDoor, Material matStripe)
        {
            var leaf = new GameObject(leafName);
            leaf.transform.SetParent(parent, false);
            leaf.transform.position = pos;

            var col = leaf.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("ArmorLeaf", size, new Vector3(0f, size.y * 0.5f, 0f), matDoor, 0.045f),
                MakeBeveledCube("RibL", new Vector3(0.28f, size.y * 0.90f, size.z + 0.08f), new Vector3(-size.x * 0.28f, size.y * 0.48f, 0f), matDoor, 0.02f),
                MakeBeveledCube("RibR", new Vector3(0.28f, size.y * 0.90f, size.z + 0.08f), new Vector3(size.x * 0.28f, size.y * 0.48f, 0f), matDoor, 0.02f),
                MakeBeveledCube("DoorHazardBand", new Vector3(size.x * 0.94f, 0.22f, size.z + 0.08f), new Vector3(0f, size.y * 0.52f, 0f), matStripe, 0.01f),
            };
            MergeParts(parts, leafName + "_PB", leaf.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
            return leaf.transform;
        }

        /// <summary>
        /// Builds the stepped octagon North Helipad LZ with 4 smooth Prism access ramps,
        /// inlaid yellow [H] markings, and corner perimeter bollards.
        /// </summary>
        public static void BuildHelipad(Transform parent, Vector3 centerPos, Material matPadDark, Material matYellow, Material matWalls)
        {
            var pad = new GameObject("NorthHelipad_LZ");
            pad.transform.SetParent(parent, false);
            pad.transform.position = centerPos;

            // Low beveled landing pad deck (0.10m step <= CharacterController 0.25m stepOffset, walkable smoothly)
            var col = pad.AddComponent<BoxCollider>();
            col.size = new Vector3(15f, 0.10f, 15f);
            col.center = new Vector3(0f, 0.05f, 0f);

            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("PadDeck", new Vector3(15f, 0.10f, 15f), new Vector3(0f, 0.05f, 0f), matPadDark, 0.03f),
                MakePrism("RampS", new Vector3(7.5f, 0.10f, 1.4f), new Vector3(0f, 0.05f, -8.15f), matWalls, rot: Quaternion.Euler(0f, 180f, 0f)),
                MakePrism("RampN", new Vector3(7.5f, 0.10f, 1.4f), new Vector3(0f, 0.05f, 8.15f), matWalls, rot: Quaternion.Euler(0f, 0f, 0f)),
                MakePrism("RampW", new Vector3(7.5f, 0.10f, 1.4f), new Vector3(-8.15f, 0.05f, 0f), matWalls, rot: Quaternion.Euler(0f, 90f, 0f)),
                MakePrism("RampE", new Vector3(7.5f, 0.10f, 1.4f), new Vector3(8.15f, 0.05f, 0f), matWalls, rot: Quaternion.Euler(0f, -90f, 0f)),
                MakeBeveledCube("H_LeftLeg", new Vector3(0.95f, 0.02f, 6.2f), new Vector3(-2.1f, 0.105f, 0f), matYellow, 0f),
                MakeBeveledCube("H_RightLeg", new Vector3(0.95f, 0.02f, 6.2f), new Vector3(2.1f, 0.105f, 0f), matYellow, 0f),
                MakeBeveledCube("H_CrossBar", new Vector3(4.2f, 0.02f, 1.0f), new Vector3(0f, 0.105f, 0f), matYellow, 0f),
                MakeBeveledCube("Border_N", new Vector3(13.6f, 0.02f, 0.45f), new Vector3(0f, 0.105f, 6.8f), matYellow, 0f),
                MakeBeveledCube("Border_S", new Vector3(13.6f, 0.02f, 0.45f), new Vector3(0f, 0.105f, -6.8f), matYellow, 0f),
                MakeBeveledCube("Border_W", new Vector3(0.45f, 0.02f, 13.6f), new Vector3(-6.8f, 0.105f, 0f), matYellow, 0f),
                MakeBeveledCube("Border_E", new Vector3(0.45f, 0.02f, 13.6f), new Vector3(6.8f, 0.105f, 0f), matYellow, 0f),
            };

            foreach (var sx in new[] { -7.0f, 7.0f })
            {
                foreach (var sz in new[] { -7.0f, 7.0f })
                {
                    parts.Add(MakeCyl("LzBeacon_" + sx + "_" + sz, 6, 0.24f, 0.36f, new Vector3(sx, 0.24f, sz), matYellow));
                }
            }

            MergeParts(parts, "NorthHelipad_PB", pad.transform, castShadows: false, smoothingAngle: DefaultSmoothAngle);
        }

        // -------------------------------------------------------------------------
        // MODULAR TACTICAL COVER & INDUSTRIAL PROP BUILDERS (UNIFIED PROBUILDER KIT)
        // -------------------------------------------------------------------------

        public static void BuildBunker(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(5.8f, 3.3f, 4.6f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Plinth", new Vector3(6.1f, 0.42f, 4.9f), new Vector3(0f, 0.21f, 0f), mats.Trim, 0.06f),
                MakeBeveledCube("LowerWalls", new Vector3(5.7f, 1.55f, 4.5f), new Vector3(0f, 1.15f, 0f), mats.Walls, 0.08f),
                MakeBeveledCube("SlitDarkRecess", new Vector3(4.3f, 0.46f, 4.35f), new Vector3(0f, 2.05f, 0f), mats.Trim, 0.03f),
                MakeBeveledCube("PillarFL", new Vector3(0.65f, 0.65f, 0.65f), new Vector3(-2.3f, 2.08f, 1.9f), mats.Walls, 0.04f),
                MakeBeveledCube("PillarFR", new Vector3(0.65f, 0.65f, 0.65f), new Vector3(2.3f, 2.08f, 1.9f), mats.Walls, 0.04f),
                MakeBeveledCube("BrowArmor", new Vector3(6.05f, 0.68f, 4.85f), new Vector3(0f, 2.66f, 0f), mats.Walls, 0.12f),
                MakeBeveledCube("RoofCap", new Vector3(4.8f, 0.34f, 3.6f), new Vector3(0f, 3.16f, 0f), mats.Trim, 0.06f),
                MakeCyl("RoofComms", 6, 0.04f, 0.75f, new Vector3(-1.8f, 3.55f, -1.2f), mats.Trim),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildContainer(Transform parent, string name, Vector3 pos, float yaw, Material matBody, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(2.55f, 2.6f, 6.0f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("MainBox", new Vector3(2.44f, 2.48f, 5.92f), new Vector3(0f, 1.30f, 0f), matBody, 0.05f),
                MakeBeveledCube("BottomRail", new Vector3(2.56f, 0.16f, 6.02f), new Vector3(0f, 0.08f, 0f), mats.Trim, 0.02f),
                MakeBeveledCube("TopRail", new Vector3(2.56f, 0.16f, 6.02f), new Vector3(0f, 2.52f, 0f), mats.Trim, 0.02f),
            };
            for (var rz = -2.2f; rz <= 2.25f; rz += 1.1f)
            {
                parts.Add(MakeBeveledCube("Rib_" + rz, new Vector3(2.54f, 2.18f, 0.24f), new Vector3(0f, 1.30f, rz), matBody, 0.025f));
            }
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildRailcar(Transform parent, string name, Vector3 pos, float yaw, Material matBody, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(3.0f, 3.8f, 10.2f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("FlatbedDeck", new Vector3(2.96f, 0.42f, 10.2f), new Vector3(0f, 0.96f, 0f), mats.Trim, 0.05f),
                MakeBeveledCube("BogieF", new Vector3(2.45f, 0.55f, 2.1f), new Vector3(0f, 0.42f, 3.4f), mats.RailSteel, 0.04f),
                MakeBeveledCube("BogieR", new Vector3(2.45f, 0.55f, 2.1f), new Vector3(0f, 0.42f, -3.4f), mats.RailSteel, 0.04f),
                MakeCyl("TankHull", 12, 1.24f, 8.8f, new Vector3(0f, 2.38f, 0f), matBody, rot: Quaternion.Euler(90f, 0f, 0f)),
                MakeCyl("TankDomedEnds", 12, 1.16f, 9.35f, new Vector3(0f, 2.38f, 0f), mats.Trim, rot: Quaternion.Euler(90f, 0f, 0f)),
                MakeCyl("TopHatch", 8, 0.48f, 0.42f, new Vector3(0f, 3.62f, 0f), mats.Accent),
                MakeBeveledCube("HazardBand", new Vector3(2.60f, 0.28f, 1.2f), new Vector3(0f, 2.38f, 0f), mats.Accent, 0.02f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildJerseyBarrier(Transform parent, string name, Vector3 pos, float yaw, Vector3 size, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("FootSkirt", new Vector3(size.x, size.y * 0.34f, size.z), new Vector3(0f, size.y * 0.17f, 0f), mats.JerseyBarrier, 0.04f),
                MakeBeveledCube("UpperStem", new Vector3(size.x * 0.98f, size.y * 0.72f, size.z * 0.52f), new Vector3(0f, size.y * 0.64f, 0f), mats.JerseyBarrier, 0.05f),
                MakeBeveledCube("ReflectorStrip", new Vector3(size.x * 0.72f, 0.11f, size.z * 0.56f), new Vector3(0f, size.y * 0.80f, 0f), mats.Accent, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildHescoBarrier(Transform parent, string name, Vector3 pos, float yaw, Vector3 size, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("EarthFill", new Vector3(size.x * 0.95f, size.y * 0.92f, size.z * 0.90f), new Vector3(0f, size.y * 0.46f, 0f), mats.Sandbag, 0.06f),
                MakeBeveledCube("FrameTop", new Vector3(size.x, 0.08f, size.z), new Vector3(0f, size.y * 0.94f, 0f), mats.Trim, 0.01f),
                MakeBeveledCube("FrameBottom", new Vector3(size.x, 0.10f, size.z), new Vector3(0f, 0.05f, 0f), mats.Trim, 0.01f),
                MakeBeveledCube("CornerPostW", new Vector3(0.08f, size.y, size.z + 0.02f), new Vector3(-size.x * 0.48f, size.y * 0.5f, 0f), mats.Trim, 0.01f),
                MakeBeveledCube("CornerPostM", new Vector3(0.08f, size.y, size.z + 0.02f), new Vector3(0f, size.y * 0.5f, 0f), mats.Trim, 0.01f),
                MakeBeveledCube("CornerPostE", new Vector3(0.08f, size.y, size.z + 0.02f), new Vector3(size.x * 0.48f, size.y * 0.5f, 0f), mats.Trim, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildSandbagWall(Transform parent, string name, Vector3 pos, float yaw, Vector3 size, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("SkidBase", new Vector3(size.x * 0.96f, 0.12f, size.z * 0.96f), new Vector3(0f, 0.06f, 0f), mats.Trim, 0.02f)
            };

            const int rows = 3;
            const int cols = 4;
            var bagW = (size.x * 0.92f) / cols;
            var bagH = (size.y - 0.10f) / rows;
            for (var r = 0; r < rows; r++)
            {
                var stagger = (r % 2 == 1) ? bagW * 0.25f : -bagW * 0.12f;
                var rowCols = cols - (r == rows - 1 ? 1 : 0);
                for (var c = 0; c < rowCols; c++)
                {
                    var bx = -size.x * 0.34f + c * (bagW * 0.95f) + stagger * 0.5f;
                    var by = 0.14f + bagH * (r + 0.45f);
                    parts.Add(MakeBeveledCube($"Bag_{r}_{c}", new Vector3(bagW * 0.94f, bagH * 0.90f, size.z * 0.88f), new Vector3(bx, by, 0f), mats.Sandbag, 0.055f));
                }
            }
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildTacticalCover(Transform parent, string name, Vector3 pos, float yaw, Vector3 size, Material matBody, Material matTrim, Material matAccent)
        {
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("BasePlinth", new Vector3(size.x + 0.16f, 0.22f, size.z + 0.14f), new Vector3(0f, 0.11f, 0f), matTrim, 0.04f),
                MakeBeveledCube("ArmorCore", new Vector3(size.x, size.y * 0.78f, size.z), new Vector3(0f, size.y * 0.50f, 0f), matBody, 0.06f),
                MakeBeveledCube("TopCap", new Vector3(size.x * 0.90f, 0.16f, size.z * 0.88f), new Vector3(0f, size.y - 0.05f, 0f), matTrim, 0.03f),
                MakeBeveledCube("HazardStencil", new Vector3(size.x * 0.88f, 0.09f, size.z + 0.05f), new Vector3(0f, size.y * 0.78f, 0f), matAccent, 0.01f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildFuelTank(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(3.4f, 4.6f, 3.4f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("ConcretePad", new Vector3(3.6f, 0.36f, 3.6f), new Vector3(0f, 0.18f, 0f), mats.Walls, 0.05f),
                MakeCyl("SiloBody", 12, 1.48f, 3.85f, new Vector3(0f, 2.25f, 0f), mats.FuelSteel),
                MakeCyl("BandLow", 12, 1.56f, 0.18f, new Vector3(0f, 1.25f, 0f), mats.Trim),
                MakeCyl("BandMid", 12, 1.56f, 0.18f, new Vector3(0f, 2.65f, 0f), mats.Accent),
                MakeCyl("RoofHatch", 8, 0.58f, 0.36f, new Vector3(0.45f, 4.30f, 0.25f), mats.Trim),
                MakeBeveledCube("LadderRail", new Vector3(0.46f, 3.8f, 0.18f), new Vector3(0f, 2.25f, 1.54f), mats.Trim, 0.02f),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildHeliosPylon(Transform parent, string name, Vector3 pos, Vector3 scaleSize, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var root = CreatePropRoot(parent, name, pos, 0f, scaleSize);
            var w = scaleSize.x;
            var h = scaleSize.y;
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Plinth", new Vector3(w * 1.05f, h * 0.14f, w * 1.05f), new Vector3(0f, h * 0.07f, 0f), mats.Trim, 0.06f),
                MakeCyl("CoreColumn", 8, w * 0.34f, h * 0.78f, new Vector3(0f, h * 0.48f, 0f), mats.FuelSteel),
                MakeBeveledCube("CyanCapacitorRing", new Vector3(w * 0.78f, h * 0.09f, w * 0.78f), new Vector3(0f, h * 0.67f, 0f), mats.HeliosGlow, 0.03f),
                MakeBeveledCube("UpperCollar", new Vector3(w * 0.88f, h * 0.14f, w * 0.88f), new Vector3(0f, h * 0.88f, 0f), mats.Trim, 0.05f),
                MakeCone("EmitterSpire", w * 0.22f, h * 0.22f, 8, new Vector3(0f, h * 1.04f, 0f), mats.FuelSteel),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildRadarDish(Transform parent, string name, Vector3 pos, float yaw, SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var size = new Vector3(2.7f, 4.6f, 2.7f);
            var root = CreatePropRoot(parent, name, pos, yaw, size);
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("Pedestal", new Vector3(2.2f, 1.35f, 2.2f), new Vector3(0f, 0.68f, 0f), mats.Walls, 0.08f),
                MakeCyl("TurretNeck", 8, 0.55f, 1.15f, new Vector3(0f, 1.85f, 0f), mats.Trim),
                MakeBeveledCube("GimbalYoke", new Vector3(1.1f, 0.85f, 0.68f), new Vector3(0f, 2.65f, 0f), mats.Trim, 0.04f),
                MakeCone("DishShell", 1.35f, 0.72f, 10, new Vector3(0f, 3.25f, 0.15f), mats.FuelSteel, rot: Quaternion.Euler(122f, 0f, 0f)),
                MakeCyl("FeedHorn", 6, 0.08f, 1.05f, new Vector3(0f, 3.55f, 0.52f), mats.Accent, rot: Quaternion.Euler(32f, 0f, 0f)),
            };
            MergeParts(parts, name + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
        }

        public static void BuildFloodlightTower(
            Transform parent,
            string towerName,
            Vector3 pos,
            Material matBody,
            Material matTrim,
            Material matGlow,
            Color lightColor)
        {
            var root = CreatePropRoot(parent, towerName, pos, 0f, new Vector3(1.1f, 3.6f, 1.1f));
            var parts = new List<ProBuilderMesh>
            {
                MakeBeveledCube("BaseFooting", new Vector3(1.28f, 0.42f, 1.28f), new Vector3(0f, 0.21f, 0f), matTrim, 0.04f),
                MakeCyl("MastShaft", 8, 0.22f, 3.15f, new Vector3(0f, 1.92f, 0f), matBody),
                MakeBeveledCube("CrossArm", new Vector3(1.18f, 0.22f, 0.58f), new Vector3(0f, 3.58f, 0f), matTrim, 0.03f),
                MakeBeveledCube("LampLens", new Vector3(0.92f, 0.18f, 0.42f), new Vector3(0f, 3.44f, 0f), matGlow, 0.01f),
            };
            MergeParts(parts, towerName + "_PB", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);

            var lightGo = new GameObject("TowerLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 3.40f, 0f);
            var pt = lightGo.AddComponent<Light>();
            pt.type = LightType.Point;
            pt.color = lightColor;
            pt.intensity = 3.4f;
            pt.range = 14f;
            pt.shadows = LightShadows.None;
        }

        public static void BuildExplosiveBarrel(
            Transform parent,
            string barrelName,
            Vector3 worldPos,
            SceneSetupBuilder.ArenaMaterialPack mats)
        {
            var destructibleLayer = LayerMask.NameToLayer("Destructible");
            var root = new GameObject(barrelName);
            if (destructibleLayer >= 0) root.layer = destructibleLayer;
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(worldPos, Quaternion.identity);

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.44f;
            col.height = 1.15f;
            col.center = new Vector3(0f, 0.575f, 0f);

            root.AddComponent<ExplosiveBarrel>();

            var parts = new List<ProBuilderMesh>
            {
                MakeCyl("DrumBody", 12, 0.37f, 1.09f, new Vector3(0f, 0.575f, 0f), mats.BarrelHazard),
                MakeCyl("ChimeBot", 12, 0.395f, 0.07f, new Vector3(0f, 0.035f, 0f), mats.Trim),
                MakeCyl("ChimeTop", 12, 0.395f, 0.07f, new Vector3(0f, 1.055f, 0f), mats.Trim),
                MakeCyl("HazardBand", 12, 0.382f, 0.22f, new Vector3(0f, 0.575f, 0f), mats.Accent),
                MakeCyl("BungCap", 6, 0.065f, 0.05f, new Vector3(0.14f, 1.14f, 0.10f), mats.Trim),
            };
            var vis = MergeParts(parts, "BarrelVisual", root.transform, castShadows: true, smoothingAngle: DefaultSmoothAngle);
            if (vis != null && destructibleLayer >= 0)
            {
                vis.layer = destructibleLayer;
            }
        }

        // -------------------------------------------------------------------------
        // LOW-LEVEL PROBUILDER GEOMETRY FACTORIES
        // -------------------------------------------------------------------------

        private static GameObject CreatePropRoot(Transform parent, string propName, Vector3 pos, float yawDeg, Vector3 colliderSize)
        {
            var root = new GameObject(propName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));

            var col = root.AddComponent<BoxCollider>();
            col.size = colliderSize;
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            return root;
        }

        private static ProBuilderMesh MakeBeveledCube(
            string name,
            Vector3 size,
            Vector3 localPos,
            Material mat,
            float bevelAmount,
            Quaternion? rot = null,
            Color? color = null)
        {
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            pb.gameObject.name = name;
            pb.transform.localPosition = localPos;
            if (rot.HasValue) pb.transform.localRotation = rot.Value;

            var maxSafeBevel = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.25f;
            var safeBevel = Mathf.Min(bevelAmount, maxSafeBevel);
            if (safeBevel > 0.003f)
            {
                var edges = new List<Edge>();
                foreach (var f in pb.faces)
                {
                    edges.AddRange(f.edges);
                }
                Bevel.BevelEdges(pb, edges, safeBevel);
            }

            ApplyMaterialAndColor(pb, mat, color);
            return pb;
        }

        private static ProBuilderMesh MakeCyl(
            string name,
            int axisDivs,
            float radius,
            float height,
            Vector3 localPos,
            Material mat,
            Quaternion? rot = null,
            Color? color = null)
        {
            var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, axisDivs, radius, height, 0, -1);
            pb.gameObject.name = name;
            pb.transform.localPosition = localPos;
            if (rot.HasValue) pb.transform.localRotation = rot.Value;
            ApplyMaterialAndColor(pb, mat, color);
            return pb;
        }

        private static ProBuilderMesh MakePrism(
            string name,
            Vector3 size,
            Vector3 localPos,
            Material mat,
            Quaternion? rot = null,
            Color? color = null)
        {
            var pb = ShapeGenerator.GeneratePrism(PivotLocation.Center, size);
            pb.gameObject.name = name;
            pb.transform.localPosition = localPos;
            if (rot.HasValue) pb.transform.localRotation = rot.Value;
            ApplyMaterialAndColor(pb, mat, color);
            return pb;
        }

        private static ProBuilderMesh MakeCone(
            string name,
            float radius,
            float height,
            int axisDivs,
            Vector3 localPos,
            Material mat,
            Quaternion? rot = null,
            Color? color = null)
        {
            var pb = ShapeGenerator.GenerateCone(PivotLocation.Center, radius, height, axisDivs);
            pb.gameObject.name = name;
            pb.transform.localPosition = localPos;
            if (rot.HasValue) pb.transform.localRotation = rot.Value;
            ApplyMaterialAndColor(pb, mat, color);
            return pb;
        }

        private static ProBuilderMesh MakeIco(
            string name,
            float radius,
            int subdivisions,
            Vector3 localPos,
            Material mat,
            Vector3? scale = null,
            Color? color = null)
        {
            var pb = ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, radius, subdivisions, weldVertices: true, manualUvs: false);
            pb.gameObject.name = name;
            pb.transform.localPosition = localPos;
            if (scale.HasValue) pb.transform.localScale = scale.Value;
            ApplyMaterialAndColor(pb, mat, color);
            return pb;
        }

        private static void ApplyMaterialAndColor(ProBuilderMesh pb, Material mat, Color? color)
        {
            if (mat != null)
            {
                pb.SetMaterial(pb.faces, mat);
            }
            if (color.HasValue)
            {
                foreach (var f in pb.faces)
                {
                    pb.SetFaceColor(f, color.Value);
                }
            }
        }

        private static GameObject MergeParts(
            List<ProBuilderMesh> parts,
            string finalName,
            Transform parent,
            bool castShadows,
            float smoothingAngle)
        {
            if (parts == null || parts.Count == 0) return null;

            foreach (var p in parts)
            {
                p.ToMesh();
                p.Refresh();
            }

            var primary = parts[0];
            if (parts.Count > 1)
            {
                CombineMeshes.Combine(parts, primary);
                for (var i = 1; i < parts.Count; i++)
                {
                    if (parts[i] != null)
                    {
                        Object.DestroyImmediate(parts[i].gameObject);
                    }
                }
            }

            primary.gameObject.name = finalName;
            primary.transform.SetParent(parent, false);
            if (smoothingAngle > 1f)
            {
                Smoothing.ApplySmoothingGroups(primary, primary.faces, smoothingAngle);
            }
            primary.ToMesh();
            primary.Refresh();

            var mr = primary.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            return primary.gameObject;
        }
    }
}
