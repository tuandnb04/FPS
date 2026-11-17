using System.Collections.Generic;
using System.IO;
using Audio;
using Camera;
using Character;
using Combat;
using Core;
using Enemies;
using Feel;
using TMPro;
using UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TopDownShooter.Editor
{
    /// <summary>
    /// Deterministic idempotent builder creating materials, textures, URP lighting,
    /// Blender hard-surface geometry, player rig, Cinemachine rig, audio/VFX/feel/save directors, and 6-state uGUI shell.
    /// </summary>
    public static class SceneSetupBuilder
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int SkyTint = Shader.PropertyToID("_SkyTint");
        private static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int Exposure = Shader.PropertyToID("_Exposure");
        private const string MaterialsDir = "Assets/Materials";
        private const string TexturesDir = "Assets/Textures";
        private const string FbxAssetPath = "Assets/Models/SM_Vanguard_Assets.fbx";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private struct VanguardMeshPack
        {
            public Mesh PlayerTorso;
            public Mesh PlayerChestRig;
            public Mesh PlayerHelmet;
            public Mesh PlayerVisor;
            public Mesh PlayerLimb;
            public Mesh PlayerUpperArm;
            public Mesh PlayerForearm;
            public Mesh PlayerJetpack;

            public Mesh WeaponPulseRifle;
            public Mesh WeaponShotgun;
            public Mesh WeaponRailgun;
            public Mesh WeaponRocket;
            public Mesh WeaponAN94;
            public Mesh WeaponVectorSMG;
            public Mesh WeaponAK74;
            public Mesh WeaponPKM;

            public Mesh ChaserTorso;
            public Mesh ChaserHead;
            public Mesh HeavyTorso;
            public Mesh HeavyCrown;
            public Mesh BossTorso;
            public Mesh BossTurret;
            public Mesh EnemyArm;

            public Mesh VehicleBlackhawk;
            public Mesh VehicleBlackhawkRotor;
            public Mesh EnvHMMWV;
            public Mesh PickupXpCrystal;
            public Mesh PickupMedkit;
            public Mesh PickupCreditCoin;

            public Mesh HeadBoonieHat;
            public Mesh HeadSkullMask;
            public Mesh PropSabotageTerminal;
            public Mesh PropC4Charge;
            public Mesh PropMunitionCrate;
        }

        internal struct ArenaMaterialPack
        {
            public Material Floor;
            public Material Road;
            public Material Walls;
            public Material Props;
            public Material Trim;
            public Material Accent;
            public Material SodiumGlow;
            public Material Olive;
            public Material Rust;
            public Material FuelSteel;
            public Material DmzEarth;
            public Material DmzHill;
            public Material Foliage;
            public Material Rock;
            public Material JerseyBarrier;
            public Material Sandbag;
            public Material BarrelHazard;
            public Material HeliosGlow;
            public Material RailSteel;
        }

        private struct CampaignCutsceneProps
        {
            public Transform Gate1Left;
            public Transform Gate1Right;
            public Transform Gate2Left;
            public Transform Gate2Right;
            public Transform EvacChopperRoot;
            public Transform EvacChopperRotor;
        }

        private struct UiSpritePack
        {
            public Sprite Solid;
            public Sprite Rounded;
            public Sprite Chamfer;
            public Sprite TacticalBorder;
            public Sprite Shield;
            public Sprite Crosshair;
            public Sprite Circle;
            public Sprite Chevron;
            public Sprite DamageVignette;
            public Sprite RadarRings;
            public Sprite RadarCone;
            public Sprite HitArc;
            public Sprite SkullKia;
        }

        [MenuItem("Vanguard/Rebuild Top-Down Shooter Scene")]
        public static void BuildCompleteScene()
        {
            BuildAllCampaignScenes();
        }

        [MenuItem("Vanguard/Rebuild All 4 Campaign Scenes")]
        public static void BuildAllCampaignScenes()
        {
            EnsureFolders();
            EnsurePhysicsLayersAndMatrix();
            ConfigureUrpAssetOptimization();

            var uiSprites = new UiSpritePack
            {
                Solid = SaveSpriteAsset(UISpriteFactory.GetSolidRectSprite(), TexturesDir + "/Tex_SolidRect.png", FilterMode.Point),
                Rounded = SaveSpriteAsset(UISpriteFactory.GetRoundedRectSprite(), TexturesDir + "/Tex_RoundedRect.png", FilterMode.Bilinear, new Vector4(10f, 10f, 10f, 10f)),
                Chamfer = SaveSpriteAsset(UISpriteFactory.GetChamferPanelSprite(), TexturesDir + "/Tex_ChamferPanel.png", FilterMode.Point),
                TacticalBorder = SaveSpriteAsset(UISpriteFactory.GetTacticalBorderSprite(), TexturesDir + "/Tex_TacticalBorder.png", FilterMode.Point),
                Shield = SaveSpriteAsset(UISpriteFactory.GetShieldIcon(), TexturesDir + "/Tex_ShieldIcon.png", FilterMode.Bilinear),
                Crosshair = SaveSpriteAsset(UISpriteFactory.GetCrosshairIcon(), TexturesDir + "/Tex_CrosshairIcon.png", FilterMode.Point),
                Circle = SaveSpriteAsset(UISpriteFactory.GetCircleSprite(), TexturesDir + "/Tex_Circle.png", FilterMode.Bilinear),
                Chevron = SaveSpriteAsset(UISpriteFactory.GetChevronIcon(), TexturesDir + "/Tex_ChevronIcon.png", FilterMode.Bilinear),
                DamageVignette = SaveSpriteAsset(UISpriteFactory.GetDamageVignetteSprite(), TexturesDir + "/Tex_DamageVignette.png", FilterMode.Bilinear),
                RadarRings = SaveSpriteAsset(UISpriteFactory.GetRadarGridRingSprite(), TexturesDir + "/Tex_RadarRings.png", FilterMode.Bilinear),
                RadarCone = SaveSpriteAsset(UISpriteFactory.GetRadarConeSprite(), TexturesDir + "/Tex_RadarCone.png", FilterMode.Bilinear),
                HitArc = SaveSpriteAsset(UISpriteFactory.GetHitArcSprite(), TexturesDir + "/Tex_HitArc.png", FilterMode.Bilinear),
                SkullKia = SaveSpriteAsset(UISpriteFactory.GetSkullKiaIcon(), TexturesDir + "/Tex_SkullKia.png", FilterMode.Point)
            };

            var meshes = LoadVanguardMeshes();

            var scenePaths = new string[StoryDirector.CampaignMissionCount];
            for (var m = 1; m <= StoryDirector.CampaignMissionCount; m++)
            {
                scenePaths[m - 1] = "Assets/Scenes/" + StoryDirector.GetMissionSceneName(m) + ".unity";
                BuildMissionScene(m, scenePaths[m - 1], meshes, uiSprites);
            }

            var buildScenes = new EditorBuildSettingsScene[scenePaths.Length];
            for (var i = 0; i < scenePaths.Length; i++)
            {
                buildScenes[i] = new EditorBuildSettingsScene(scenePaths[i], true);
            }
            EditorBuildSettings.scenes = buildScenes;

            string[] legacyPaths =
            {
                "Assets/Scenes/SampleScene.unity",
                "Assets/Settings/SampleSceneProfile.asset",
                "Assets/Scenes/Mission03_BlackcrestSummit.unity"
            };
            foreach (var lp in legacyPaths)
            {
                if (File.Exists(lp))
                {
                    AssetDatabase.DeleteAsset(lp);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(scenePaths[0], OpenSceneMode.Single);
            Debug.Log("[SceneSetupBuilder] Rebuilt all 4 Campaign Scenes and registered them cleanly in EditorBuildSettings.");
        }

        private static Vector3 GetMissionSpawnPosition(int missionIndex)
        {
            return missionIndex switch
            {
                2 => new Vector3(-16f, 0.02f, -45f),
                3 => new Vector3(0f, 0.02f, -44f),
                4 => new Vector3(0f, 0.02f, -46f),
                _ => new Vector3(0f, 0.02f, -43f)
            };
        }

        private static void BuildMissionScene(
            int missionIndex,
            string scenePath,
            VanguardMeshPack meshes,
            UiSpritePack uiSprites)
        {
            var mTag = "_M" + missionIndex;
            var floorColor = missionIndex switch
            {
                1 => Palette.M1ConcreteApron,
                2 => Palette.RoadAsphalt,
                3 => Palette.FromHex("#2E343B"),
                _ => Palette.M3CitadelStone
            };
            var earthColor = missionIndex switch
            {
                1 => Palette.M1PierWater,
                2 => Palette.M2CanyonEarth,
                3 => Palette.FromHex("#1A1F24"),
                _ => Palette.DmzRidgeEarth
            };
            var wallColor = missionIndex switch
            {
                1 => Palette.Walls,
                2 => Palette.M2CanyonEarth,
                3 => Palette.FromHex("#3A4048"),
                _ => Palette.M3CitadelStone
            };
            var hillTint = missionIndex switch
            {
                1 => Palette.AmbientSky,
                2 => Palette.M2CanyonEarth,
                3 => Palette.FromHex("#252B33"),
                _ => Palette.ButtonTintIdle
            };

            // Shared character / weapon / prop materials + per-mission terrain/skybox materials
            var matFloor = SaveOrUpdateLitMaterial("Mat_Floor" + mTag, floorColor, 0.03f, missionIndex == 1 ? 0.24f : 0.16f, false, 0f);
            var matRoad = SaveOrUpdateLitMaterial("Mat_RoadAsphalt" + mTag, Palette.RoadAsphalt, 0.02f, 0.15f, false, 0f);
            var matWalls = SaveOrUpdateLitMaterial("Mat_Walls" + mTag, wallColor, 0.03f, 0.18f, false, 0f);
            var matProps = SaveOrUpdateLitMaterial("Mat_Props", Palette.Props, 0.03f, 0.20f, false, 0f);
            var matTrim = SaveOrUpdateLitMaterial("Mat_TrimDark", Palette.Gunmetal, 0.45f, 0.36f, false, 0f);
            var matPlayerPrimary = SaveOrUpdateLitMaterial("Mat_PlayerPrimary", Palette.CharacterPrimary, 0.06f, 0.24f, false, 0f);
            var matPlayerSecondary = SaveOrUpdateLitMaterial("Mat_PlayerSecondary", Palette.CoyoteTan, 0.05f, 0.22f, false, 0f);
            var matAccentEmissive = SaveOrUpdateLitMaterial("Mat_AccentEmissive", Palette.HazardYellow, 0.05f, 0.22f, false, 0f);
            var matCyanGlow = SaveOrUpdateLitMaterial("Mat_CyanGlow", Palette.NvgGreen, 0.18f, 0.45f, false, 0f);
            var matHealthGlow = SaveOrUpdateLitMaterial("Mat_HealthEmissive", Palette.SodiumAmber, 0.10f, 0.32f, true, 1.85f);
            var matHeliosGlow = SaveOrUpdateLitMaterial("Mat_HeliosCyanGlow", Palette.M3HeliosCoreCyan, 0.12f, 0.38f, true, 2.25f);
            var matContainerRust = SaveOrUpdateLitMaterial("Mat_ContainerRust", Palette.EnemyPrimary, 0.10f, 0.24f, false, 0f);
            var matContainerOlive = SaveOrUpdateLitMaterial("Mat_ContainerOlive", Palette.CharacterPrimary, 0.10f, 0.24f, false, 0f);
            var matFuelSteel = SaveOrUpdateLitMaterial("Mat_FuelSteel", Palette.GuardTowerDark, 0.32f, 0.36f, false, 0f);
            var matRailSteel = SaveOrUpdateLitMaterial("Mat_RailSteel", Palette.M2RailSteel, 0.42f, 0.38f, false, 0f);
            var matDmzEarth = SaveOrUpdateLitMaterial("Mat_DmzEarth" + mTag, earthColor, missionIndex == 1 ? 0.18f : 0.0f, missionIndex == 1 ? 0.55f : 0.12f, false, 0f);
            var matDmzHill = SaveOrUpdateLitMaterial("Mat_DmzHill" + mTag, hillTint, 0.0f, 0.14f, false, 0f);
            var matFoliage = SaveOrUpdateLitMaterial("Mat_DmzFoliage", Palette.FromHex("#3A5B3E"), 0.0f, 0.14f, false, 0f);
            var matRock = SaveOrUpdateLitMaterial("Mat_DmzRock" + mTag, missionIndex == 2 ? Palette.M2CanyonEarth : Palette.FromHex("#67717B"), 0.02f, 0.16f, false, 0f);
            var matJerseyBarrier = SaveOrUpdateLitMaterial("Mat_JerseyBarrier", Palette.FromHex("#8A8E88"), 0.02f, 0.22f, false, 0f);
            var matSandbag = SaveOrUpdateLitMaterial("Mat_Sandbag", Palette.CoyoteTan, 0.02f, 0.16f, false, 0f);
            var matBarrelHazard = SaveOrUpdateLitMaterial("Mat_BarrelHazard", Palette.BarrelHazardRed, 0.18f, 0.34f, false, 0f);
            var matSkybox = SaveOrUpdateSkyboxMaterial("Mat_ArenaSkybox" + mTag, missionIndex);

            EnsureAllMaterialsInstanced();

            var profile = ConfigureVolumeProfile(missionIndex);

            var scene = File.Exists(scenePath)
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ClearOldGeneratedRoots();

            ConfigureEnvironmentLighting(matSkybox, profile, missionIndex);
            var mainCam = ConfigureMainCamera();

            var envMats = new ArenaMaterialPack
            {
                Floor = matFloor,
                Road = matRoad,
                Walls = matWalls,
                Props = matProps,
                Trim = matTrim,
                Accent = matAccentEmissive,
                SodiumGlow = matHealthGlow,
                Olive = matContainerOlive,
                Rust = matContainerRust,
                FuelSteel = matFuelSteel,
                DmzEarth = matDmzEarth,
                DmzHill = matDmzHill,
                Foliage = matFoliage,
                Rock = matRock,
                JerseyBarrier = matJerseyBarrier,
                Sandbag = matSandbag,
                BarrelHazard = matBarrelHazard,
                HeliosGlow = matHeliosGlow,
                RailSteel = matRailSteel
            };
            var campaignProps = BuildArenaEnvironment(envMats, meshes, missionIndex);

            var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            var spawnPos = GetMissionSpawnPosition(missionIndex);
            var player = BuildPlayerRig(inputAsset, matPlayerPrimary, matPlayerSecondary, matCyanGlow, meshes, spawnPos);
            player.SetSpawnPosition(spawnPos);
            var cameraRig = BuildCameraRig(player.transform, mainCam);

            var systemsRoot = new GameObject("GameSystems");
            var projPoolRoot = new GameObject("ProjectilePoolRoot").transform;
            projPoolRoot.SetParent(systemsRoot.transform, false);
            player.Weapon.BindReferences(player.transform.Find("VisualRoot/WeaponAnchor/MuzzlePoint"), player.Motion, projPoolRoot);

            var vfxPoolRoot = new GameObject("VFXPoolRoot").transform;
            vfxPoolRoot.SetParent(systemsRoot.transform, false);
            var textRoot = new GameObject("FloatingTextRoot").transform;
            textRoot.SetParent(systemsRoot.transform, false);

            systemsRoot.AddComponent<SaveManager>();
            systemsRoot.AddComponent<AudioManager>();
            var vfx = systemsRoot.AddComponent<VFXManager>();
            vfx.BindPoolRoot(vfxPoolRoot);

            var impulse = systemsRoot.AddComponent<CinemachineImpulseSource>();
            var feel = systemsRoot.AddComponent<FeelManager>();
            feel.BindComponents(impulse, textRoot);

            var squadDirector = systemsRoot.AddComponent<SquadDirector>();
            squadDirector.BindMeshes(
                meshes.PlayerTorso,
                meshes.PlayerChestRig,
                meshes.PlayerHelmet,
                meshes.HeadBoonieHat,
                meshes.PlayerVisor,
                meshes.PlayerUpperArm,
                meshes.PlayerForearm,
                meshes.PlayerLimb,
                meshes.PlayerJetpack,
                meshes.WeaponPulseRifle,
                meshes.WeaponRailgun,
                meshes.WeaponAN94,
                meshes.HeadSkullMask);

            var spawner = systemsRoot.AddComponent<EnemySpawner>();
            spawner.SetMissionIndex(missionIndex);
            spawner.BindMeshes(
                meshes.ChaserTorso,
                meshes.ChaserHead,
                meshes.HeavyTorso,
                meshes.HeavyCrown,
                meshes.BossTorso,
                meshes.BossTurret,
                meshes.PlayerLimb,
                meshes.PickupXpCrystal,
                meshes.PickupMedkit,
                meshes.PropMunitionCrate != null ? meshes.PropMunitionCrate : meshes.PickupCreditCoin,
                meshes.EnemyArm,
                meshes.PlayerUpperArm,
                meshes.PlayerForearm,
                meshes.WeaponAK74,
                meshes.WeaponPKM,
                meshes.PropSabotageTerminal,
                meshes.PropC4Charge);

            var ui = BuildGameCanvasAndUI(missionIndex, systemsRoot, uiSprites);
            var cutscene = systemsRoot.AddComponent<CutsceneDirector>();
            cutscene.BindSceneProps(
                campaignProps.Gate1Left,
                campaignProps.Gate1Right,
                campaignProps.Gate2Left,
                campaignProps.Gate2Right,
                campaignProps.EvacChopperRoot,
                campaignProps.EvacChopperRotor);
            systemsRoot.AddComponent<GameManager>();

            if (cameraRig == null || ui == null || matHealthGlow == null)
            {
                Debug.LogWarning("SceneSetupBuilder noticed a missing reference.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[SceneSetupBuilder] Rebuilt mission scene: " + scenePath);
        }

        private static VanguardMeshPack LoadVanguardMeshes()
        {
            var pack = new VanguardMeshPack();
            if (!File.Exists(FbxAssetPath))
            {
                Debug.LogWarning("[SceneSetupBuilder] Custom FBX not found at " + FbxAssetPath + ". Falling back to primitives.");
                return pack;
            }

            AssetDatabase.ImportAsset(FbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(FbxAssetPath) as ModelImporter;
            if (importer != null && (importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable || Mathf.Abs(importer.globalScale - 1f) > 0.001f))
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.globalScale = 1f;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(FbxAssetPath);
            var byName = new Dictionary<string, Mesh>();
            foreach (var obj in assets)
            {
                if (obj is Mesh m)
                {
                    byName[m.name] = m;
                }
            }

            pack.PlayerTorso = Get("Mesh_PlayerTorso");
            pack.PlayerChestRig = Get("Mesh_PlayerChestRig");
            pack.PlayerHelmet = Get("Mesh_PlayerHelmet");
            pack.PlayerVisor = Get("Mesh_PlayerVisor");
            pack.PlayerLimb = Get("Mesh_PlayerLimb");
            pack.PlayerUpperArm = Get("Mesh_PlayerUpperArm");
            pack.PlayerForearm = Get("Mesh_PlayerForearm");
            pack.PlayerJetpack = Get("Mesh_PlayerJetpack");

            pack.WeaponPulseRifle = Get("Mesh_Weapon_PulseRifle");
            pack.WeaponShotgun = Get("Mesh_Weapon_Shotgun");
            pack.WeaponRailgun = Get("Mesh_Weapon_Railgun");
            pack.WeaponRocket = Get("Mesh_Weapon_Rocket");
            pack.WeaponAN94 = Get("Mesh_Weapon_AN94");
            pack.WeaponVectorSMG = Get("Mesh_Weapon_VectorSMG");
            pack.WeaponAK74 = Get("Mesh_Weapon_AK74");
            pack.WeaponPKM = Get("Mesh_Weapon_PKM");

            pack.ChaserTorso = Get("Mesh_ChaserTorso");
            pack.ChaserHead = Get("Mesh_ChaserHead");
            pack.HeavyTorso = Get("Mesh_HeavyTorso");
            pack.HeavyCrown = Get("Mesh_HeavyCrown");
            pack.BossTorso = Get("Mesh_BossTorso");
            pack.BossTurret = Get("Mesh_BossTurret");
            pack.EnemyArm = Get("Mesh_EnemyArm");

            pack.VehicleBlackhawk = Get("Mesh_Vehicle_Blackhawk");
            pack.VehicleBlackhawkRotor = Get("Mesh_Vehicle_BlackhawkRotor");
            pack.EnvHMMWV = Get("Mesh_Env_HMMWV");
            pack.PickupXpCrystal = Get("Mesh_Pickup_XpCrystal");
            pack.PickupMedkit = Get("Mesh_Pickup_Medkit");
            pack.PickupCreditCoin = Get("Mesh_Pickup_CreditCoin");
            pack.HeadBoonieHat = Get("Mesh_Head_BoonieHat");
            pack.HeadSkullMask = Get("Mesh_Head_SkullMask");

            var matPropFallback = SaveOrUpdateLitMaterial("Mat_Props", Palette.Props, 0.03f, 0.20f, false, 0f);
            ProBuilderKitBuilder.BuildOrLoadRuntimePropMeshes(
                matPropFallback,
                out pack.PropSabotageTerminal,
                out pack.PropC4Charge,
                out pack.PropMunitionCrate);

            Debug.Log("[SceneSetupBuilder] Loaded " + byName.Count + " Blender hero FBX meshes + 3 ProBuilder runtime prop assets.");
            return pack;

            Mesh Get(string key) => byName.GetValueOrDefault(key);
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(TexturesDir)) AssetDatabase.CreateFolder("Assets", "Textures");
        }

        private static void EnsurePhysicsLayersAndMatrix()
        {
            var tagAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagAssets is { Length: > 0 })
            {
                var tagMgr = new SerializedObject(tagAssets[0]);
                var layersProp = tagMgr.FindProperty("layers");
                if (layersProp is { isArray: true })
                {
                    string[] customNames = { "Player", "SquadAlly", "Enemy", "Environment", "Pickup", "Destructible" };
                    for (var i = 0; i < customNames.Length; i++)
                    {
                        var slot = 6 + i;
                        if (slot < layersProp.arraySize)
                        {
                            layersProp.GetArrayElementAtIndex(slot).stringValue = customNames[i];
                        }
                    }
                    tagMgr.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            Physics.IgnoreLayerCollision(6, 7, true);
            Physics.IgnoreLayerCollision(7, 7, true);
            Physics.IgnoreLayerCollision(10, 7, true);
            Physics.IgnoreLayerCollision(10, 8, true);
            Physics.IgnoreLayerCollision(10, 9, true);
            Physics.IgnoreLayerCollision(10, 10, true);
            Physics.IgnoreLayerCollision(10, 11, true);
        }

        private static void ConfigureUrpAssetOptimization()
        {
            string[] urpPaths =
            {
                "Assets/Settings/PC_RPAsset.asset",
                "Assets/Settings/Mobile_RPAsset.asset"
            };
            foreach (var t in urpPaths)
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(t);
                if (rp == null) continue;
                rp.msaaSampleCount = 2;
                rp.shadowDistance = 40f;
                rp.shadowCascadeCount = 2;
                EditorUtility.SetDirty(rp);
            }
            AssetDatabase.SaveAssets();
        }

        private static void EnsureAllMaterialsInstanced()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) return;
            var guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsDir });
            foreach (var t in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(t);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.enableInstancing) continue;
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }
        }

        private static Sprite SaveSpriteAsset(Sprite sourceSprite, string assetPath, FilterMode filter = FilterMode.Bilinear, Vector4? overrideBorder = null)
        {
            var source = sourceSprite.texture;
            var png = source.EncodeToPNG();
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = sourceSprite.pixelsPerUnit > 0f ? sourceSprite.pixelsPerUnit : 100f;
                importer.spriteBorder = overrideBorder ?? sourceSprite.border;
                importer.filterMode = filter;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Material SaveOrUpdateLitMaterial(
            string matName,
            Color baseColor,
            float metallic,
            float smoothness,
            bool emissive,
            float emissionIntensity)
        {
            var path = MaterialsDir + "/" + matName + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var generated = Palette.CreateLitMaterial(matName, baseColor, metallic, smoothness, emissive, emissionIntensity);

            if (existing != null)
            {
                if (existing.HasProperty(BaseMap))
                {
                    existing.SetTexture(BaseMap, null);
                    existing.SetTextureScale(BaseMap, Vector2.one);
                }
                existing.shader = generated.shader;
                existing.CopyPropertiesFromMaterial(generated);
                existing.enableInstancing = true;
                Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            generated.enableInstancing = true;
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        private static Material SaveOrUpdateSkyboxMaterial(string matName, int missionIndex)
        {
            var path = MaterialsDir + "/" + matName + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var generated = Palette.CreateSkyboxMaterial();
            generated.name = matName;

            if (missionIndex == 2)
            {
                if (generated.HasProperty("_SkyTint")) generated.SetColor("_SkyTint", Palette.M2DepotSkyTop);
                if (generated.HasProperty("_GroundColor")) generated.SetColor("_GroundColor", Palette.M2CanyonEarth);
                if (generated.HasProperty("_AtmosphereThickness")) generated.SetFloat("_AtmosphereThickness", 0.72f);
                if (generated.HasProperty("_Exposure")) generated.SetFloat("_Exposure", 0.76f);
            }
            else if (missionIndex == 3)
            {
                if (generated.HasProperty("_SkyTint")) generated.SetColor("_SkyTint", Palette.FromHex("#1A1F28"));
                if (generated.HasProperty("_GroundColor")) generated.SetColor("_GroundColor", Palette.FromHex("#14181E"));
                if (generated.HasProperty("_AtmosphereThickness")) generated.SetFloat("_AtmosphereThickness", 0.35f);
                if (generated.HasProperty("_Exposure")) generated.SetFloat("_Exposure", 0.45f);
            }
            else if (missionIndex == 4)
            {
                if (generated.HasProperty("_SkyTint")) generated.SetColor("_SkyTint", Palette.FromHex("#0D1724"));
                if (generated.HasProperty("_GroundColor")) generated.SetColor("_GroundColor", Palette.FromHex("#18212C"));
                if (generated.HasProperty("_AtmosphereThickness")) generated.SetFloat("_AtmosphereThickness", 0.42f);
                if (generated.HasProperty("_Exposure")) generated.SetFloat("_Exposure", 0.62f);
            }
            else
            {
                if (generated.HasProperty("_SkyTint")) generated.SetColor("_SkyTint", Palette.SkyboxTop);
                if (generated.HasProperty("_GroundColor")) generated.SetColor("_GroundColor", Palette.AmbientGround);
                if (generated.HasProperty("_Exposure")) generated.SetFloat("_Exposure", 0.58f);
            }

            if (existing != null)
            {
                existing.shader = generated.shader;
                existing.CopyPropertiesFromMaterial(generated);
                Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        private static T EnsureVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T comp) || comp == null)
            {
                comp = profile.Add<T>(true);
            }
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(comp)))
            {
                comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            EditorUtility.SetDirty(comp);
            return comp;
        }

        private static VolumeProfile ConfigureVolumeProfile(int missionIndex)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }
            var profilePath = "Assets/Settings/Mission0" + missionIndex + "Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            profile.components.RemoveAll(c => c == null);

            var tonemapping = EnsureVolumeComponent<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            var bloom = EnsureVolumeComponent<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.25f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = missionIndex >= 3 ? 0.28f : 0.22f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.55f;
            bloom.tint.overrideState = true;
            bloom.tint.value = missionIndex == 2
                ? Palette.M2KeyLightAmber
                : (missionIndex >= 3 ? Palette.M3HeliosCoreCyan : Palette.KeyLightWarm);

            var vignette = EnsureVolumeComponent<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.20f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.46f;
            vignette.color.overrideState = true;
            vignette.color.value = Palette.Shadow;

            var colorAdj = EnsureVolumeComponent<ColorAdjustments>(profile);
            colorAdj.active = true;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = missionIndex == 2 ? 8f : 4f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 12f;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = missionIndex == 2 ? 0.34f : (missionIndex >= 3 ? 0.26f : 0.28f);

            if (profile.TryGet(out MotionBlur motionBlur) && motionBlur != null)
            {
                motionBlur.active = false;
                EditorUtility.SetDirty(motionBlur);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void ClearOldGeneratedRoots()
        {
            string[] generatedNames =
            {
                "ArenaEnvironment",
                "Player",
                "CameraFollowProxy",
                "TopDownVirtualCamera",
                "GameSystems",
                "GameCanvas",
                "EventSystem",
                "DirectionalFillLight"
            };

            foreach (var t in generatedNames)
            {
                var existing = GameObject.Find(t);
                while (existing != null)
                {
                    Object.DestroyImmediate(existing);
                    existing = GameObject.Find(t);
                }
            }
        }

        private static void ConfigureEnvironmentLighting(Material skybox, VolumeProfile profile, int missionIndex)
        {
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            if (missionIndex == 2)
            {
                RenderSettings.ambientSkyColor = Palette.M2DepotSkyHorizon;
                RenderSettings.ambientEquatorColor = Palette.M2CanyonEarth;
                RenderSettings.ambientGroundColor = Palette.AmbientGround;
                RenderSettings.fogColor = Palette.M2DepotSkyHorizon;
                RenderSettings.fogStartDistance = 62f;
                RenderSettings.fogEndDistance = 180f;
            }
            else if (missionIndex == 3)
            {
                RenderSettings.ambientSkyColor = Palette.FromHex("#3F5062");
                RenderSettings.ambientEquatorColor = Palette.FromHex("#2E353E");
                RenderSettings.ambientGroundColor = Palette.AmbientGround;
                RenderSettings.fogColor = Palette.FromHex("#1A212B");
                RenderSettings.fogStartDistance = 48f;
                RenderSettings.fogEndDistance = 145f;
            }
            else if (missionIndex == 4)
            {
                RenderSettings.ambientSkyColor = Palette.M3PeakSnow * 0.48f;
                RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
                RenderSettings.ambientGroundColor = Palette.AmbientGround;
                RenderSettings.fogColor = Palette.SkyboxHorizon;
                RenderSettings.fogStartDistance = 55f;
                RenderSettings.fogEndDistance = 170f;
            }
            else
            {
                RenderSettings.ambientSkyColor = Palette.AmbientSky;
                RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
                RenderSettings.ambientGroundColor = Palette.AmbientGround;
                RenderSettings.fogColor = Palette.SkyboxHorizon;
                RenderSettings.fogStartDistance = 58f;
                RenderSettings.fogEndDistance = 175f;
            }

            var keyLightGo = GameObject.Find("Directional Light");
            if (keyLightGo == null)
            {
                keyLightGo = new GameObject("Directional Light");
                keyLightGo.AddComponent<Light>();
            }
            keyLightGo.transform.rotation = missionIndex == 2
                ? Quaternion.Euler(26f, -58f, 0f)
                : (missionIndex >= 3 ? Quaternion.Euler(48f, -32f, 0f) : Quaternion.Euler(44f, -40f, 0f));
            var keyLight = keyLightGo.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = missionIndex == 2
                ? Palette.M2KeyLightAmber
                : (missionIndex == 3 ? Palette.FromHex("#C6DCF2") : (missionIndex == 4 ? Palette.FromHex("#DDEEFF") : Palette.KeyLightWarm));
            keyLight.intensity = missionIndex == 2 ? 2.15f : (missionIndex == 3 ? 1.65f : (missionIndex == 4 ? 2.05f : 1.85f));
            keyLight.shadows = LightShadows.Soft;

            var rimLightGo = new GameObject("DirectionalFillLight")
            {
                transform =
                {
                    rotation = Quaternion.Euler(35f, 145f, 0f)
                }
            };
            var rimLight = rimLightGo.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = missionIndex == 2
                ? Palette.M2DepotSkyTop
                : (missionIndex >= 3 ? Palette.M3HeliosCoreCyan * 0.75f : Palette.AmbientSky);
            rimLight.intensity = missionIndex == 2 ? 0.85f : (missionIndex >= 3 ? 0.88f : 0.78f);
            rimLight.shadows = LightShadows.None;

            var volGo = GameObject.Find("Global Volume");
            if (volGo == null)
            {
                volGo = new GameObject("Global Volume");
            }
            var volume = volGo.GetComponent<Volume>();
            if (volume == null) volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
        }

        private static UnityEngine.Camera ConfigureMainCamera()
        {
            var mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                var camGo = new GameObject("Main Camera")
                {
                    tag = "MainCamera"
                };
                mainCam = camGo.AddComponent<UnityEngine.Camera>();
            }

            mainCam.clearFlags = CameraClearFlags.Skybox;
            mainCam.allowHDR = true;
            mainCam.fieldOfView = 52f;
            mainCam.farClipPlane = 260f;
            mainCam.transform.SetPositionAndRotation(new Vector3(0f, 13.4f, -51.2f), Quaternion.Euler(56.7f, 0f, 0f));

            var urpCam = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCam == null) urpCam = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = true;

            if (mainCam.GetComponent<AudioListener>() == null)
            {
                mainCam.gameObject.AddComponent<AudioListener>();
            }

            var brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain == null) brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
            brain.IgnoreTimeScale = false;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.4f);

            return mainCam;
        }

        private static CampaignCutsceneProps BuildArenaEnvironment(
            ArenaMaterialPack mats,
            VanguardMeshPack meshes,
            int missionIndex)
        {
            var matWalls = mats.Walls;
            var matProps = mats.Props;
            var matTrim = mats.Trim;
            var matAccent = mats.Accent;
            var matSodiumGlow = mats.SodiumGlow;
            var matOlive = mats.Olive;
            var matRust = mats.Rust;
            var matHeliosGlow = mats.HeliosGlow;
            var matSandbag = mats.Sandbag;

            var props = new CampaignCutsceneProps();
            var env = new GameObject("ArenaEnvironment");

            ProBuilderKitBuilder.BuildCompoundDeckAndRamps(env.transform, mats, missionIndex);
            ProBuilderKitBuilder.BuildProBuilderHorizon(env.transform, mats, missionIndex);
            ProBuilderKitBuilder.BuildPerimeterWallsAndBuildings(env.transform, mats, missionIndex);

            switch (missionIndex)
            {
                case 1:
                    // ACT I: BLACKWATER PIER — Twin Harbor Piers over Water + Cross-Pier Bridge + Gunboat Dock
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act1", -14f, 48f, 12f, matWalls, matTrim, matAccent, out props.Gate1Left, out props.Gate1Right, 0f);
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act2", 24f, 56f, 14f, matWalls, matTrim, matAccent, out props.Gate2Left, out props.Gate2Right, 0f);

                    // South Customs Apron
                    BuildFittedCoverProp(env.transform, "M1_HMMWV_W", meshes.EnvHMMWV, new Vector3(-9.5f, 0f, -35f), 22f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                    BuildFittedCoverProp(env.transform, "M1_HMMWV_E", meshes.EnvHMMWV, new Vector3(10.0f, 0f, -41f), -155f, new Vector3(2.4f, 1.95f, 4.9f), matSandbag);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M1_Cover_SW", new Vector3(-6.2f, 0f, -37.5f), 12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M1_Cover_SE", new Vector3(6.2f, 0f, -37.5f), -12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M1_Sandbag_W", new Vector3(-8.0f, 0f, -26.5f), -18f, new Vector3(3.4f, 1.15f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M1_Sandbag_E", new Vector3(8.0f, 0f, -26.5f), 18f, new Vector3(3.4f, 1.15f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M1_GateBar_W", new Vector3(-6.8f, 0f, -17.5f), 26f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M1_GateBar_E", new Vector3(6.8f, 0f, -17.5f), -26f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_S1", new Vector3(-4.2f, 0f, -30.5f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_S2", new Vector3(5.6f, 0f, -23.0f), mats);

                    // West Fuel Pier (-38..-10, z -6..25)
                    ProBuilderKitBuilder.BuildFuelTank(env.transform, "M1_WestTank_1", new Vector3(-33.5f, 0f, 4.5f), 0f, mats);
                    ProBuilderKitBuilder.BuildFuelTank(env.transform, "M1_WestTank_2", new Vector3(-33.5f, 0f, 14.5f), 90f, mats);
                    ProBuilderKitBuilder.BuildFuelTank(env.transform, "M1_WestTank_3", new Vector3(-16.5f, 0f, -1.0f), 180f, mats);
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M1_WestCover_1", new Vector3(-20.0f, 0f, 6.5f), 90f, new Vector3(3.2f, 1.25f, 1.05f), matProps, matTrim, matAccent);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M1_WestSandbag", new Vector3(-25.5f, 0f, 15.5f), 12f, new Vector3(3.2f, 1.15f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_WestPier", new Vector3(-26.5f, 0f, 2.5f), mats);

                    // East Radar Pier (10..38, z -6..25)
                    ProBuilderKitBuilder.BuildContainer(env.transform, "M1_EastCont_1", new Vector3(16.5f, 0f, -1.0f), 0f, matOlive, mats);
                    ProBuilderKitBuilder.BuildContainer(env.transform, "M1_EastCont_2", new Vector3(32.0f, 0f, -1.5f), 12f, matRust, mats);
                    ProBuilderKitBuilder.BuildContainer(env.transform, "M1_EastCont_3", new Vector3(17.0f, 0f, 16.5f), 90f, matOlive, mats);
                    ProBuilderKitBuilder.BuildRadarDish(env.transform, "M1_EastRadarDish", new Vector3(32.5f, 0f, 8.0f), -45f, mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M1_EastBarrier", new Vector3(23.5f, 0f, 1.5f), -18f, new Vector3(2.8f, 1.0f, 0.78f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_EastPier", new Vector3(27.5f, 0f, 13.5f), mats);

                    // Cross-Pier Bridge & North Quay
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M1_BridgeCoverW", new Vector3(-4.5f, 0f, 18.5f), 15f, new Vector3(2.6f, 1.05f, 0.9f), mats);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M1_BridgeCoverE", new Vector3(4.5f, 0f, 18.5f), -15f, new Vector3(2.6f, 1.05f, 0.9f), mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M1_QuayBunkerW", new Vector3(-19.5f, 0f, 43.0f), -22f, mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M1_QuayBunkerE", new Vector3(19.5f, 0f, 34.0f), 22f, mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M1_QuayHescoW", new Vector3(-9.0f, 0f, 33.5f), -25f, new Vector3(3.2f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M1_QuayHescoE", new Vector3(9.0f, 0f, 33.5f), 25f, new Vector3(3.2f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_QuayW", new Vector3(-7.5f, 0f, 40.5f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M1_Barrel_QuayE", new Vector3(8.5f, 0f, 41.0f), mats);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M1_Light_SW", new Vector3(-21.5f, 0f, -49f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M1_Light_SE", new Vector3(21.5f, 0f, -49f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M1_Light_NW", new Vector3(-25f, 0f, 51f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M1_Light_NE", new Vector3(25f, 0f, 51f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                    break;

                case 2:
                    // ACT II: IRON CORRIDOR — S-Curve Canyon Gorge & Armored Train #774 Interception
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act1", -20f, 38f, 12f, matWalls, matTrim, matAccent, out props.Gate1Left, out props.Gate1Right, -14f);
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act2", 32f, 42f, 14f, matWalls, matTrim, matAccent, out props.Gate2Left, out props.Gate2Right, 7f);

                    // SW Canyon Entry
                    BuildFittedCoverProp(env.transform, "M2_WreckHMMWV_W", meshes.EnvHMMWV, new Vector3(-23.5f, 0f, -34f), 38f, new Vector3(2.4f, 1.95f, 4.9f), matRust);
                    BuildFittedCoverProp(env.transform, "M2_HMMWV_E", meshes.EnvHMMWV, new Vector3(-3.5f, 0f, -39f), -145f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M2_EntrySandW", new Vector3(-19.5f, 0f, -27.5f), -16f, new Vector3(3.4f, 1.15f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M2_EntryBarE", new Vector3(-7.0f, 0f, -27.5f), 22f, new Vector3(2.6f, 0.95f, 0.72f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M2_Barrel_Entry", new Vector3(-14.0f, 0f, -31.0f), mats);

                    // Central Armored Train #774 along x=0 (with crossing gaps at z=-3.5 & z=13.5)
                    ProBuilderKitBuilder.BuildRailcar(env.transform, "Train774_TankerSouth", new Vector3(0f, 0f, -13.5f), 0f, matRust, mats);
                    ProBuilderKitBuilder.BuildRailcar(env.transform, "Train774_FlatbedMid", new Vector3(0f, 0f, 4.5f), 180f, mats.FuelSteel, mats);
                    ProBuilderKitBuilder.BuildArmoredLocomotive(env.transform, "Train774_Locomotive", new Vector3(0f, 0f, 24.5f), mats);

                    // West & East Canyon Track Cover + Switch Pockets
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M2_TrackCoverW1", new Vector3(-11.5f, 0f, -9.5f), 90f, new Vector3(3.4f, 1.2f, 1.05f), matProps, matTrim, matAccent);
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M2_TrackCoverE1", new Vector3(11.5f, 0f, -9.5f), 90f, new Vector3(3.4f, 1.2f, 1.05f), matProps, matTrim, matAccent);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M2_TrackHescoW2", new Vector3(-12.5f, 0f, 16.5f), 25f, new Vector3(3.0f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M2_TrackHescoE2", new Vector3(12.5f, 0f, 16.5f), -25f, new Vector3(3.0f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildFuelTank(env.transform, "M2_WestSwitchTank", new Vector3(-37.0f, 0f, 12.0f), 0f, mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M2_WestSwitchBunker", new Vector3(-25.5f, 0f, -4.5f), 45f, mats);
                    ProBuilderKitBuilder.BuildContainer(env.transform, "M2_EastSwitchCont", new Vector3(35.5f, 0f, -8.5f), 15f, matOlive, mats);
                    ProBuilderKitBuilder.BuildRadarDish(env.transform, "M2_EastSignalDish", new Vector3(26.0f, 0f, 7.5f), -135f, mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M2_Barrel_TrackW", new Vector3(-8.5f, 0f, 1.5f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M2_Barrel_TrackE", new Vector3(8.5f, 0f, 6.5f), mats);

                    // NE Gorge Boss Sector
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M2_GorgeBunkerW", new Vector3(-7.5f, 0f, 44.0f), -25f, mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M2_GorgeBunkerE", new Vector3(21.5f, 0f, 44.0f), 25f, mats);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M2_GorgeSandW", new Vector3(0.5f, 0f, 38.0f), 20f, new Vector3(3.2f, 1.1f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M2_GorgeSandE", new Vector3(15.5f, 0f, 38.0f), -20f, new Vector3(3.2f, 1.1f, 1.0f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M2_Barrel_Gorge", new Vector3(13.5f, 0f, 42.5f), mats);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M2_Light_SW", new Vector3(-30f, 0f, -50f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M2_Light_NE", new Vector3(24f, 0f, 54f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
                    break;

                case 3:
                    // ACT III: KESTREL SUB-STATION — Underground Multi-Wing Server Vault & Geothermal Turbine Hall
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act1", -26f, 30f, 10f, matWalls, matTrim, matAccent, out props.Gate1Left, out props.Gate1Right, 0f);
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act2", 16f, 44f, 12f, matWalls, matTrim, matAccent, out props.Gate2Left, out props.Gate2Right, 0f);

                    // South Airlock
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M3_AirlockBarW", new Vector3(-6.2f, 0f, -38.0f), 14f, new Vector3(2.8f, 1.0f, 0.78f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M3_AirlockBarE", new Vector3(6.2f, 0f, -38.0f), -14f, new Vector3(2.8f, 1.0f, 0.78f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_Barrel_Airlock", new Vector3(4.5f, 0f, -31.5f), mats);

                    // Central Atrium Pillars & Security Desk
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M3_AtriumPillar_SW", new Vector3(-8.5f, 0f, -13.0f), 0f, new Vector3(2.4f, 2.8f, 2.4f), matWalls, matTrim, matHeliosGlow);
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M3_AtriumPillar_SE", new Vector3(8.5f, 0f, -13.0f), 0f, new Vector3(2.4f, 2.8f, 2.4f), matWalls, matTrim, matHeliosGlow);
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M3_AtriumPillar_NW", new Vector3(-8.5f, 0f, 6.5f), 0f, new Vector3(2.4f, 2.8f, 2.4f), matWalls, matTrim, matHeliosGlow);
                    ProBuilderKitBuilder.BuildTacticalCover(env.transform, "M3_AtriumPillar_NE", new Vector3(8.5f, 0f, 6.5f), 0f, new Vector3(2.4f, 2.8f, 2.4f), matWalls, matTrim, matHeliosGlow);
                    ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M3_AtriumDesk", new Vector3(0f, 0f, -5.0f), 0f, new Vector3(4.2f, 1.15f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_Barrel_Atrium", new Vector3(-6.5f, 0f, -3.5f), mats);

                    // West Server Vault (racks flanking -31, -3 clear of NW intel -37, 7.5)
                    ProBuilderKitBuilder.BuildServerRack(env.transform, "M3_Rack_N1", new Vector3(-24.5f, 0f, 7.0f), 0f, mats);
                    ProBuilderKitBuilder.BuildServerRack(env.transform, "M3_Rack_N2", new Vector3(-30.5f, 0f, 7.0f), 0f, mats);
                    ProBuilderKitBuilder.BuildServerRack(env.transform, "M3_Rack_S1", new Vector3(-24.5f, 0f, -12.5f), 0f, mats);
                    ProBuilderKitBuilder.BuildServerRack(env.transform, "M3_Rack_S2", new Vector3(-34.0f, 0f, -12.5f), 0f, mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M3_VaultBar", new Vector3(-23.0f, 0f, -3.0f), 90f, new Vector3(3.0f, 1.0f, 0.78f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_Barrel_Vault", new Vector3(-37.5f, 0f, -3.0f), mats);

                    // East Geothermal Turbine Hall (turbines clear of 31,-3 and SE intel 37,-12.5)
                    ProBuilderKitBuilder.BuildGeothermalTurbine(env.transform, "M3_Turbine_NW", new Vector3(25.0f, 0f, 6.0f), 90f, mats);
                    ProBuilderKitBuilder.BuildGeothermalTurbine(env.transform, "M3_Turbine_NE", new Vector3(36.0f, 0f, 6.0f), 90f, mats);
                    ProBuilderKitBuilder.BuildGeothermalTurbine(env.transform, "M3_Turbine_SW", new Vector3(25.0f, 0f, -11.5f), 90f, mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_CoolantDrum_1", new Vector3(24.0f, 0f, -2.5f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_CoolantDrum_2", new Vector3(35.0f, 0f, -3.5f), mats);

                    // North Command Deck (Volkov Arena)
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M3_CmdCoverW", new Vector3(-9.5f, 0f, 25.0f), 32f, new Vector3(3.2f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M3_CmdCoverE", new Vector3(9.5f, 0f, 25.0f), -32f, new Vector3(3.2f, 1.3f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_Barrel_CmdW", new Vector3(-11.0f, 0f, 34.0f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M3_Barrel_CmdE", new Vector3(11.0f, 0f, 34.0f), mats);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M3_Light_AtriumW", new Vector3(-15f, 0f, 13f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M3_Light_AtriumE", new Vector3(15f, 0f, 13f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M3_Light_CmdN", new Vector3(-18f, 0f, 49f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                    break;

                default:
                    // ACT IV: BLACKCREST SUMMIT — Alpine Bridge, Octagonal Citadel, Central HELIOS Spire & Helipad
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act1", -26f, 20f, 12f, matWalls, matTrim, matHeliosGlow, out props.Gate1Left, out props.Gate1Right, 0f);
                    ProBuilderKitBuilder.BuildGateDivider(env.transform, "GateDivider_Act2", 26f, 44f, 14f, matWalls, matTrim, matHeliosGlow, out props.Gate2Left, out props.Gate2Right, 0f);

                    // South Bridge Cover
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M4_BridgeBar_1", new Vector3(-4.2f, 0f, -38.5f), 14f, new Vector3(2.8f, 1.0f, 0.8f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M4_BridgeBar_2", new Vector3(4.2f, 0f, -33.5f), -14f, new Vector3(2.8f, 1.0f, 0.8f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M4_Barrel_Bridge", new Vector3(0f, 0f, -35.5f), mats);

                    // Central HELIOS Orbital Spire & Octagonal Shield Bastions
                    ProBuilderKitBuilder.BuildHeliosOrbitalSpire(env.transform, new Vector3(0f, 0f, 0f), mats);
                    ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M4_HeliosPylon_W", new Vector3(-35.5f, 0f, 2.0f), new Vector3(2.6f, 5.2f, 2.6f), mats);
                    ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M4_HeliosPylon_E", new Vector3(35.5f, 0f, 2.0f), new Vector3(2.6f, 5.2f, 2.6f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M4_RingHesco_NW", new Vector3(-9.5f, 0f, 8.5f), 45f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "M4_RingHesco_NE", new Vector3(9.5f, 0f, 8.5f), -45f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M4_RingBar_SW", new Vector3(-9.5f, 0f, -8.5f), -45f, new Vector3(3.0f, 1.05f, 0.85f), mats);
                    ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M4_RingBar_SE", new Vector3(9.5f, 0f, -8.5f), 45f, new Vector3(3.0f, 1.05f, 0.85f), mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M4_Bastion_SW", new Vector3(-26.5f, 0f, -15.5f), 32f, mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M4_Bastion_NE", new Vector3(26.5f, 0f, 15.5f), -148f, mats);
                    ProBuilderKitBuilder.BuildRadarDish(env.transform, "M4_Radar_NW", new Vector3(-32.0f, 0f, 18.0f), -45f, mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M4_Barrel_W", new Vector3(-16.0f, 0f, 2.0f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M4_Barrel_E", new Vector3(16.0f, 0f, 2.0f), mats);
                    ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "M4_Barrel_N", new Vector3(0f, 0f, 18.5f), mats);

                    // North Summit VALKYRIE-6 Helipad
                    ProBuilderKitBuilder.BuildHelipad(env.transform, new Vector3(0f, 0f, 42f), matTrim, matAccent, matWalls);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M4_PadBunkerW", new Vector3(-17.5f, 0f, 41.0f), -22f, mats);
                    ProBuilderKitBuilder.BuildBunker(env.transform, "M4_PadBunkerE", new Vector3(17.5f, 0f, 41.0f), 22f, mats);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M4_Light_SW", new Vector3(-36f, 0f, -22f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M4_Light_SE", new Vector3(36f, 0f, -22f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M4_Light_PadNW", new Vector3(-19f, 0f, 51f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "M4_Light_PadNE", new Vector3(19f, 0f, 51f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                    break;
            }

            // VALKYRIE-6 Blackhawk Helicopter (parked sky-high inactive until Act IV Victory Evac Cutscene)
            BuildBlackhawkProp(env.transform, meshes.VehicleBlackhawk, meshes.VehicleBlackhawkRotor, matOlive, matTrim, out props.EvacChopperRoot, out props.EvacChopperRotor);
            MarkStaticArenaObjects(env, props);
            return props;
        }

        private static void BuildFittedCoverProp(
            Transform parent,
            string propName,
            Mesh mesh,
            Vector3 worldPos,
            float yawDeg,
            Vector3 colliderSize,
            Material mat)
        {
            var root = new GameObject(propName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yawDeg, 0f));

            var col = root.AddComponent<BoxCollider>();
            col.size = colliderSize;
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);

            if (mesh != null)
            {
                CreateVisualMeshFit(propName + "_Mesh", mesh, PrimitiveType.Cube, root.transform, new Vector3(0f, colliderSize.y * 0.5f, 0f), colliderSize, mat);
            }
            else
            {
                CreateVisualBox(propName + "_Fallback", root.transform, new Vector3(0f, colliderSize.y * 0.5f, 0f), colliderSize, mat);
            }
        }

        private static void MarkStaticArenaObjects(GameObject envRoot, CampaignCutsceneProps props)
        {
            var envLayer = LayerMask.NameToLayer("Environment");
            var dynamicRoots = new HashSet<Transform>();
            if (props.Gate1Left != null) dynamicRoots.Add(props.Gate1Left);
            if (props.Gate1Right != null) dynamicRoots.Add(props.Gate1Right);
            if (props.Gate2Left != null) dynamicRoots.Add(props.Gate2Left);
            if (props.Gate2Right != null) dynamicRoots.Add(props.Gate2Right);
            if (props.EvacChopperRoot != null) dynamicRoots.Add(props.EvacChopperRoot);

            var allTransforms = envRoot.GetComponentsInChildren<Transform>(true);
            foreach (var tr in allTransforms)
            {
                if (tr == null) continue;
                var go = tr.gameObject;

                var isExplosiveBarrel = tr.GetComponentInParent<ExplosiveBarrel>() != null;
                if (isExplosiveBarrel) continue;

                var underDynamicRoot = false;
                var cursor = tr;
                while (cursor != null)
                {
                    if (dynamicRoots.Contains(cursor))
                    {
                        underDynamicRoot = true;
                        break;
                    }
                    cursor = cursor.parent;
                }

                if (envLayer >= 0) go.layer = envLayer;
                if (underDynamicRoot) continue;
                if (go.GetComponent<Light>() != null) continue;

                GameObjectUtility.SetStaticEditorFlags(
                    go,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic);
            }
        }

        private static void BuildBlackhawkProp(
            Transform parent,
            Mesh hullMesh,
            Mesh rotorMesh,
            Material matHull,
            Material matRotor,
            out Transform rootOut,
            out Transform rotorOut)
        {
            var chopper = new GameObject("EVAC6_Blackhawk");
            chopper.transform.SetParent(parent, false);
            chopper.transform.SetPositionAndRotation(new Vector3(0f, 28f, 66f), Quaternion.Euler(0f, 180f, 0f));

            if (hullMesh != null)
            {
                var hull = new GameObject("BlackhawkHull");
                hull.transform.SetParent(chopper.transform, false);
                hull.AddComponent<MeshFilter>().sharedMesh = hullMesh;
                hull.AddComponent<MeshRenderer>().sharedMaterial = matHull;
            }
            else
            {
                CreateVisualBox("BlackhawkHullFallback", chopper.transform, new Vector3(0f, 1.4f, 0f), new Vector3(2.8f, 2.2f, 9.5f), matHull);
            }

            var rotor = new GameObject("MainRotor");
            rotor.transform.SetParent(chopper.transform, false);
            rotor.transform.localPosition = new Vector3(0f, 2.66f, 0.25f);
            if (rotorMesh != null)
            {
                rotor.AddComponent<MeshFilter>().sharedMesh = rotorMesh;
                rotor.AddComponent<MeshRenderer>().sharedMaterial = matRotor;
            }
            else
            {
                CreateVisualBox("RotorBladeA", rotor.transform, Vector3.zero, new Vector3(9.2f, 0.08f, 0.3f), matRotor);
                CreateVisualBox("RotorBladeB", rotor.transform, Vector3.zero, new Vector3(0.3f, 0.08f, 9.2f), matRotor);
            }

            chopper.SetActive(false);
            rootOut = chopper.transform;
            rotorOut = rotor.transform;
        }

        private static GameObject CreateVisualBox(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject CreateVisualPrimitive(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Vector3? localEuler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (localEuler.HasValue) go.transform.localRotation = Quaternion.Euler(localEuler.Value);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject CreateVisualMeshFit(
            string name,
            Mesh mesh,
            PrimitiveType fallbackType,
            Transform parent,
            Vector3 localPos,
            Vector3 targetSize,
            Material mat,
            bool uniformAspect = false)
        {
            if (mesh == null)
            {
                return CreateVisualPrimitive(name, fallbackType, parent, localPos, targetSize, mat);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            var bSize = mesh.bounds.size;
            Vector3 fitScale;
            if (uniformAspect)
            {
                var maxTarget = Mathf.Max(targetSize.x, Mathf.Max(targetSize.y, targetSize.z));
                var maxMesh = Mathf.Max(0.01f, Mathf.Max(bSize.x, Mathf.Max(bSize.y, bSize.z)));
                var s = maxTarget / maxMesh;
                fitScale = new Vector3(s, s, s);
            }
            else
            {
                fitScale = new Vector3(
                    targetSize.x / Mathf.Max(0.01f, bSize.x),
                    targetSize.y / Mathf.Max(0.01f, bSize.y),
                    targetSize.z / Mathf.Max(0.01f, bSize.z));
            }

            go.transform.localScale = fitScale;
            go.transform.localPosition = localPos - Vector3.Scale(mesh.bounds.center, fitScale);
            return go;
        }

        private static PlayerController BuildPlayerRig(
            InputActionAsset inputAsset,
            Material matPrimary,
            Material matSecondary,
            Material matCyan,
            VanguardMeshPack meshes,
            Vector3 spawnPos)
        {
            var playerLayer = LayerMask.NameToLayer("Player");
            var root = new GameObject("Player")
            {
                tag = "Player",
                layer = playerLayer >= 0 ? playerLayer : 0
            };
            root.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.45f;
            cc.height = 1.76f;
            cc.center = new Vector3(0f, 0.88f, 0f);
            cc.stepOffset = 0.25f;

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.88f;
            trigger.center = new Vector3(0f, 0.88f, 0f);

            var motion = root.AddComponent<ProceduralCharacterMotion>();
            var weapon = root.AddComponent<WeaponController>();
            var player = root.AddComponent<PlayerController>();

            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            // Operator Plate Carrier & Combat Torso (Blender custom meshes)
            var matWeaponSteel = SaveOrUpdateLitMaterial("Mat_WeaponSteel", Palette.Gunmetal, 0.78f, 0.56f, false, 0f);
            var torsoGo = CreateVisualMeshFit("Torso", meshes.PlayerTorso, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.58f, 0.58f, 0.38f), matPrimary);
            CreateVisualMeshFit("ChestRig", meshes.PlayerChestRig, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.11f, 0.04f), new Vector3(0.62f, 0.36f, 0.34f), matSecondary);

            // FAST Tactical Helmet & NVG Goggles
            var headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.48f, 0.02f);
            var helmetGo = CreateVisualMeshFit("Helmet", meshes.PlayerHelmet, PrimitiveType.Cube, headPivot.transform, Vector3.zero, new Vector3(0.42f, 0.36f, 0.44f), matPrimary);
            CreateVisualMeshFit("TacticalVisor", meshes.PlayerVisor, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.36f, 0.12f, 0.14f), matCyan);
            CreateVisualPrimitive("Antenna", PrimitiveType.Cylinder, headPivot.transform, new Vector3(0.18f, 0.14f, -0.10f), new Vector3(0.018f, 0.10f, 0.018f), matWeaponSteel);

            // Articulated Two-Bone Tactical Arms + Gloved Hands (UpperArm 0.34m + Forearm/Glove 0.35m)
            var leftArmPivot = BuildOperatorArm(
                "Left", visualRoot.transform, new Vector3(-0.28f, 1.24f, 0.07f),
                meshes.PlayerUpperArm, meshes.PlayerForearm, matPrimary, matWeaponSteel,
                out var leftElbowPivot, out var leftUpperRenderer, out var leftForearmRenderer);

            var rightArmPivot = BuildOperatorArm(
                "Right", visualRoot.transform, new Vector3(0.30f, 1.24f, -0.02f),
                meshes.PlayerUpperArm, meshes.PlayerForearm, matPrimary, matWeaponSteel,
                out var rightElbowPivot, out var rightUpperRenderer, out var rightForearmRenderer);

            // 6 Switchable Modern Military Firearms mounted dead-straight under WeaponAnchor with exact grip & muzzle nodes
            var weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.11f, 1.18f, 0.43f);

            var weaponVisuals = new GameObject[SaveManager.WeaponCount];
            var weaponLeftGrips = new Transform[SaveManager.WeaponCount];
            var weaponRightGrips = new Transform[SaveManager.WeaponCount];
            var weaponMuzzleTips = new Transform[SaveManager.WeaponCount];

            weaponVisuals[0] = BuildWeaponModel(
                "Weapon_PulseRifle", weaponAnchor.transform, meshes.WeaponPulseRifle, 0.65f,
                Vector3.zero, Vector3.zero, matWeaponSteel,
                new Vector3(0f, -0.028f, 0.165f), new Vector3(0f, -0.060f, -0.033f), new Vector3(0f, 0.020f, 0.555f),
                out weaponLeftGrips[0], out weaponRightGrips[0], out weaponMuzzleTips[0]);

            weaponVisuals[1] = BuildWeaponModel(
                "Weapon_Shotgun", weaponAnchor.transform, meshes.WeaponShotgun, 0.68f,
                Vector3.zero, Vector3.zero, matWeaponSteel,
                new Vector3(0f, -0.012f, 0.180f), new Vector3(0f, -0.060f, -0.055f), new Vector3(0f, 0.030f, 0.515f),
                out weaponLeftGrips[1], out weaponRightGrips[1], out weaponMuzzleTips[1]);

            weaponVisuals[2] = BuildWeaponModel(
                "Weapon_Railgun", weaponAnchor.transform, meshes.WeaponRailgun, 0.66f,
                new Vector3(0f, 0f, -0.02f), Vector3.zero, matWeaponSteel,
                new Vector3(0f, -0.010f, 0.195f), new Vector3(0f, -0.058f, -0.052f), new Vector3(0f, 0.020f, 0.690f),
                out weaponLeftGrips[2], out weaponRightGrips[2], out weaponMuzzleTips[2]);

            weaponVisuals[3] = BuildWeaponModel(
                "Weapon_Rocket", weaponAnchor.transform, meshes.WeaponRocket, 0.85f,
                new Vector3(0.14f, 0.21f, -0.14f), Vector3.zero, matPrimary,
                new Vector3(0f, -0.068f, 0.136f), new Vector3(0f, -0.068f, -0.034f), new Vector3(0f, 0.025f, 0.595f),
                out weaponLeftGrips[3], out weaponRightGrips[3], out weaponMuzzleTips[3]);

            weaponVisuals[4] = BuildWeaponModel(
                "Weapon_AN94", weaponAnchor.transform, meshes.WeaponAN94, 0.66f,
                Vector3.zero, Vector3.zero, matWeaponSteel,
                new Vector3(0f, -0.024f, 0.180f), new Vector3(0f, -0.060f, -0.038f), new Vector3(0f, 0.017f, 0.598f),
                out weaponLeftGrips[4], out weaponRightGrips[4], out weaponMuzzleTips[4]);

            weaponVisuals[5] = BuildWeaponModel(
                "Weapon_VectorSMG", weaponAnchor.transform, meshes.WeaponVectorSMG, 0.65f,
                new Vector3(0f, 0f, 0.02f), Vector3.zero, matWeaponSteel,
                new Vector3(0f, -0.042f, 0.165f), new Vector3(0f, -0.060f, -0.032f), new Vector3(0f, 0.014f, 0.362f),
                out weaponLeftGrips[5], out weaponRightGrips[5], out weaponMuzzleTips[5]);

            var muzzlePoint = new GameObject("MuzzlePoint");
            muzzlePoint.transform.SetParent(weaponAnchor.transform, false);
            muzzlePoint.transform.SetPositionAndRotation(weaponMuzzleTips[0].position, weaponMuzzleTips[0].rotation);

            // Legs
            var leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.15f, 0.74f, 0f);
            CreateVisualMeshFit("LeftLeg", meshes.PlayerLimb, PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            var rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.15f, 0.74f, 0f);
            CreateVisualMeshFit("RightLeg", meshes.PlayerLimb, PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            // Tactical Field Rucksack (secondary lag pivot - zero thrusters)
            var secondaryPivot = new GameObject("SecondaryPivot");
            secondaryPivot.transform.SetParent(visualRoot.transform, false);
            secondaryPivot.transform.localPosition = new Vector3(0f, 1.10f, -0.24f);
            CreateVisualMeshFit("JetpackBody", meshes.PlayerJetpack, PrimitiveType.Cube, secondaryPivot.transform, Vector3.zero, new Vector3(0.38f, 0.44f, 0.22f), matSecondary);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot, rightArmPivot, leftLegPivot.transform, rightLegPivot.transform, secondaryPivot.transform);
            motion.BindWeaponArmIk(leftElbowPivot, rightElbowPivot, weaponAnchor.transform, weaponLeftGrips[0], weaponRightGrips[0]);
            weapon.BindReferences(muzzlePoint.transform, motion, null);
            weapon.BindWeaponVisuals(weaponVisuals);
            weapon.BindWeaponGrips(weaponLeftGrips, weaponRightGrips, weaponMuzzleTips);

            var primaryArmorParts = new[]
            {
                torsoGo.GetComponent<MeshRenderer>(),
                helmetGo.GetComponent<MeshRenderer>(),
                leftUpperRenderer,
                leftForearmRenderer,
                rightUpperRenderer,
                rightForearmRenderer
            };
            player.BindComponents(inputAsset, cc, motion, weapon, root.GetComponentsInChildren<MeshRenderer>(true), primaryArmorParts);

            return player;
        }

        private static Transform BuildOperatorArm(
            string side,
            Transform visualRoot,
            Vector3 shoulderLocalPos,
            Mesh upperArmMesh,
            Mesh forearmMesh,
            Material matSleeve,
            Material matGlove,
            out Transform elbowPivotOut,
            out MeshRenderer upperRendererOut,
            out MeshRenderer forearmRendererOut)
        {
            var armPivot = new GameObject(side + "ArmPivot");
            armPivot.transform.SetParent(visualRoot, false);
            armPivot.transform.localPosition = shoulderLocalPos;

            GameObject upperGo;
            if (upperArmMesh != null)
            {
                upperGo = new GameObject(side + "UpperArm");
                upperGo.transform.SetParent(armPivot.transform, false);
                upperGo.transform.localPosition = Vector3.zero;
                upperGo.transform.localScale = new Vector3(1.06f, 0.34f / 0.295f, 1.06f);
                upperGo.AddComponent<MeshFilter>().sharedMesh = upperArmMesh;
                upperRendererOut = upperGo.AddComponent<MeshRenderer>();
                upperRendererOut.sharedMaterial = matSleeve;
            }
            else
            {
                upperGo = CreateVisualPrimitive(side + "UpperArm", PrimitiveType.Capsule, armPivot.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.18f, 0.14f), matSleeve);
                upperRendererOut = upperGo.GetComponent<MeshRenderer>();
            }

            var elbowPivot = new GameObject(side + "ElbowPivot");
            elbowPivot.transform.SetParent(armPivot.transform, false);
            elbowPivot.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            elbowPivotOut = elbowPivot.transform;

            GameObject forearmGo;
            if (forearmMesh != null)
            {
                forearmGo = new GameObject(side + "Forearm");
                forearmGo.transform.SetParent(elbowPivot.transform, false);
                forearmGo.transform.localPosition = Vector3.zero;
                forearmGo.transform.localScale = new Vector3(1.04f, 0.30f / 0.25f, 1.04f);
                forearmGo.AddComponent<MeshFilter>().sharedMesh = forearmMesh;
                forearmRendererOut = forearmGo.AddComponent<MeshRenderer>();
                forearmRendererOut.sharedMaterial = matSleeve;
            }
            else
            {
                forearmGo = CreateVisualPrimitive(side + "Forearm", PrimitiveType.Capsule, elbowPivot.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.12f, 0.16f, 0.12f), matSleeve);
                forearmRendererOut = forearmGo.GetComponent<MeshRenderer>();
            }

            CreateVisualBox(side + "Glove", elbowPivot.transform, new Vector3(0f, -0.335f, 0f), new Vector3(0.085f, 0.09f, 0.095f), matGlove);
            return armPivot.transform;
        }

        private static GameObject BuildWeaponModel(
            string name,
            Transform anchor,
            Mesh weaponMesh,
            float uniformScale,
            Vector3 localOffset,
            Vector3 localEuler,
            Material matBody,
            Vector3 leftGripLocal,
            Vector3 rightGripLocal,
            Vector3 muzzleLocal,
            out Transform leftGripOut,
            out Transform rightGripOut,
            out Transform muzzleOut)
        {
            var container = new GameObject(name);
            container.transform.SetParent(anchor, false);
            container.transform.localPosition = localOffset;
            container.transform.localRotation = Quaternion.Euler(localEuler);

            if (weaponMesh != null)
            {
                var body = new GameObject("MeshBody");
                body.transform.SetParent(container.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localScale = Vector3.one * uniformScale;
                body.AddComponent<MeshFilter>().sharedMesh = weaponMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = matBody;
            }
            else
            {
                CreateVisualPrimitive("Receiver", PrimitiveType.Cube, container.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.10f, 0.16f, 0.60f), matBody);
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

        private static TopDownCameraRig BuildCameraRig(Transform playerTransform, UnityEngine.Camera mainCam)
        {
            var proxyGo = new GameObject("CameraFollowProxy")
            {
                transform =
                {
                    position = playerTransform.position + Vector3.up * 0.9f
                }
            };

            var vcamGo = new GameObject("TopDownVirtualCamera");
            vcamGo.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            var listener = vcamGo.AddComponent<CinemachineImpulseListener>();
            var rig = vcamGo.AddComponent<TopDownCameraRig>();
            rig.BindComponents(vcam, follow, listener, proxyGo.transform, playerTransform);

            return rig;
        }

        private static UIManager BuildGameCanvasAndUI(int missionIndex, GameObject systemsRoot, UiSpritePack s)
        {
            StoryDirector.ActiveMissionIndex = missionIndex;

            var solidSprite = s.Solid;
            var roundedSprite = s.Rounded;
            var chamferSprite = s.Chamfer;
            var borderSprite = s.TacticalBorder;
            var shieldSprite = s.Shield;
            var crosshairSprite = s.Crosshair;
            var circleSprite = s.Circle;
            var chevronSprite = s.Chevron;
            var damageVignetteSprite = s.DamageVignette;

            // EventSystem using New Input System UI Module
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // Root Canvas (Native 1920x1080)
            var canvasGo = new GameObject("GameCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 1. HUD PANEL
            var hudGroup = CreateFullScreenGroup("HUDPanel", canvasGo.transform);

            // AN/PVS-31 Tactical Night-Vision Goggles Overlay (toggled by [N], non-blocking)
            var nvgOverlayGroup = CreateFullScreenGroup("NvgOverlayGroup", hudGroup.transform);
            nvgOverlayGroup.alpha = 0f;
            nvgOverlayGroup.blocksRaycasts = false;
            nvgOverlayGroup.interactable = false;
            CreateImage("NvgPhosphorTint", nvgOverlayGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.11f), false);
            CreateImage("NvgVignetteMask", nvgOverlayGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, damageVignetteSprite, Palette.WithAlpha(Palette.Shadow, 0.45f), false);
            // Optical NVG viewfinder corner brackets cleanly framing center viewport
            CreateImage("NvgBracketTL_H", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460f, 255f), new Vector2(60f, 3f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketTL_V", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-489f, 226f), new Vector2(3f, 60f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketTR_H", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(460f, 255f), new Vector2(60f, 3f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketTR_V", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(489f, 226f), new Vector2(3f, 60f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketBL_H", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460f, -255f), new Vector2(60f, 3f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketBL_V", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-489f, -226f), new Vector2(3f, 60f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketBR_H", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(460f, -255f), new Vector2(60f, 3f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            CreateImage("NvgBracketBR_V", nvgOverlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(489f, -226f), new Vector2(3f, 60f), solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.72f), false);
            var nvgStamp = CreateStyledText("NvgHudStamp", nvgOverlayGroup.transform, "AN/PVS-31 BINOCULAR FLIR // IR GAIN AUTO // REC", 14.5f, FontStyles.Bold, 2.2f, Palette.WithAlpha(Palette.NvgPhosphor, 0.94f), TextAlignmentOptions.Center);
            SetAnchoredRect(nvgStamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 286f), new Vector2(620f, 24f));

            // Fullscreen Crimson Damage Vignette (Non-blocking background overlay)
            var damageVignetteImg = CreateImage("DamageVignette", hudGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, damageVignetteSprite, Palette.WithAlpha(Palette.DamageBloodRed, 0f), false);

            // Top-Left ATAK Tactical Radar Minimap (x: 24..274)
            var radarBox = CreateTacticalPanel("TacticalRadarBox", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(250f, 274f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.SquadBlue, 0.65f), out _, false);
            CreateImage("RadarAccentTop", radarBox, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-18f, 3f), solidSprite, Palette.SquadBlue, false);

            var radarUavTxt = CreateStyledText("RadarUavStatusText", radarBox, "ATAK // GRID 38T", 13.5f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(radarUavTxt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(228f, 22f));

            var radarFieldRt = CreateTacticalPanel("RadarField", radarBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(220f, 220f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.Shadow, 0.88f), Palette.WithAlpha(Palette.CharacterSecondary, 0.28f), out _, false);
            CreateImage("RadarRangeRings", radarFieldRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, s.RadarRings, Palette.WithAlpha(Palette.SquadBlue, 0.72f), false);
            var radarConeImg = CreateImage("RadarFovCone", radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210f, 210f), s.RadarCone, Palette.WithAlpha(Palette.Accent, 0.55f), false);

            var rNorth = CreateStyledText("RadarNorth", radarFieldRt, "N", 11.5f, FontStyles.Bold, 1f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(rNorth.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(20f, 16f));
            var rSouth = CreateStyledText("RadarSouth", radarFieldRt, "S", 11f, FontStyles.Bold, 1f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(rSouth.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(20f, 16f));
            var rEast = CreateStyledText("RadarEast", radarFieldRt, "E", 11f, FontStyles.Bold, 1f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(rEast.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(20f, 16f));
            var rWest = CreateStyledText("RadarWest", radarFieldRt, "W", 11f, FontStyles.Bold, 1f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(rWest.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(20f, 16f));

            var radarObjectiveDots = new Image[6];
            for (var i = 0; i < radarObjectiveDots.Length; i++)
            {
                radarObjectiveDots[i] = CreateImage("RadarObjDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 15f), circleSprite, Palette.C4Amber, false);
                radarObjectiveDots[i].gameObject.SetActive(false);
            }
            var radarEnemyDots = new Image[16];
            for (var i = 0; i < radarEnemyDots.Length; i++)
            {
                radarEnemyDots[i] = CreateImage("RadarEnemyDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f), circleSprite, Palette.EnemySecondary, false);
                radarEnemyDots[i].gameObject.SetActive(false);
            }
            var radarSquadDots = new Image[3];
            for (var i = 0; i < radarSquadDots.Length; i++)
            {
                radarSquadDots[i] = CreateImage("RadarSquadDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f), circleSprite, Palette.SquadBlue, false);
                radarSquadDots[i].gameObject.SetActive(false);
            }
            var playerChevronImg = CreateImage("RadarPlayerChevron", radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f), chevronSprite, Palette.CharacterSecondary, false);

            // Top-Left Requisition, Mission Clock & Blackbox Counter Pill (x: 286..522)
            var crPill = CreateTacticalPanel("CreditsPill_Left", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(286f, -22f), new Vector2(236f, 102f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.CreditGold, 0.55f), out _, false);
            CreateImage("CreditsAccentLeft", crPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(4f, 74f), solidSprite, Palette.CreditGold, false);
            var missionClockTxt = CreateStyledText("MissionClockText", crPill, "T+00:00", 16f, FontStyles.Bold, 1.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(missionClockTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(206f, 24f));
            var hudCreditsText = CreateStyledText("HUD_CreditsText", crPill, "REQ // 200 CR", 16.5f, FontStyles.Bold, 1.6f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(hudCreditsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, -2f), new Vector2(206f, 26f));
            var intelHudCountTxt = CreateStyledText("IntelHudCountText", crPill, "BLACKBOX // 0/8", 14.5f, FontStyles.Bold, 1.4f, Palette.M3HeliosCoreCyan, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(intelHudCountTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 12f), new Vector2(206f, 22f));

            // Top-Left Non-Blocking TAC-NET Radio Comms Box with Signal Waveform Bars (x: 24..522, y: -308)
            var radioRt = CreateTacticalPanel("RadioCommsBox", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -308f), new Vector2(498f, 132f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.95f), Palette.WithAlpha(Palette.Accent, 0.65f), out _, false);
            var radioCg = radioRt.gameObject.AddComponent<CanvasGroup>();
            radioCg.alpha = 0f;
            radioCg.interactable = false;
            radioCg.blocksRaycasts = false;
            var radioStripe = CreateImage("RadioAccentStripe", radioRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(5f, 102f), solidSprite, Palette.Accent, false);
            var radioCallsignTxt = CreateStyledText("RadioCallsignText", radioRt, "AEGIS-SATCOM // CH-09", 16f, FontStyles.Bold, 1.8f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(radioCallsignTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(396f, 26f));

            var waveBars = new Image[5];
            for (var i = 0; i < waveBars.Length; i++)
            {
                waveBars[i] = CreateImage("RadioWave_" + i, radioRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f + i * 10f, -22f), new Vector2(6f, 22f), solidSprite, Palette.HealthGreen, false);
            }

            var radioBodyTxt = CreateStyledText("RadioBodyText", radioRt, "", 16f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            radioBodyTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(radioBodyTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -42f), new Vector2(460f, 80f));

            // Bottom-Left Fireteam Casualty Roster, Orders, Ballistic Plates & Biometrics Card (x: 24..534)
            var sqCard = CreateTacticalPanel("StatusCard_Left", hudGroup.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 22f), new Vector2(510f, 332f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.SquadBlue, 0.65f), out _);
            CreateImage("CardAccentTop", sqCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.SquadBlue, false);

            string[] orderLabels = { "[Z] DIAMOND", "[X] OVERWATCH", "[C] ASSAULT" };
            var orderBtns = new Button[3];
            var orderBgs = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                var ob = CreateTacticalButton("SquadOrderBtn_" + i, sqCard, orderLabels[i], new Vector2(0f, 1f), new Vector2(16f + i * 162f, -32f), new Vector2(154f, 34f), chamferSprite, borderSprite, i == 0 ? Palette.SquadBlue : Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.35f), Palette.CharacterSecondary, 13.5f, out _);
                var ort = ob.GetComponent<RectTransform>();
                ort.pivot = new Vector2(0f, 0.5f);
                ort.anchoredPosition = new Vector2(16f + i * 162f, -32f);
                orderBtns[i] = ob;
                orderBgs[i] = ob.GetComponent<Image>();
                var olbl = ob.GetComponentInChildren<TextMeshProUGUI>();
                if (olbl != null) olbl.characterSpacing = 1.0f;
            }

            string[] defaultSqNames = { "MAJ. ROURKE", "KOZLOWSKI", "CPL. REYES" };
            var sqNames = new TextMeshProUGUI[3];
            var sqBars = new Image[3];
            var sqStatuses = new TextMeshProUGUI[3];
            var sqKiaIcons = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                var rowY = -72f - i * 34f;
                sqKiaIcons[i] = CreateImage("SquadKiaIcon_" + i, sqCard, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, rowY - 1f), new Vector2(20f, 20f), s.SkullKia, Palette.EnemySecondary, false);
                sqKiaIcons[i].enabled = false;

                sqNames[i] = CreateStyledText("SquadName_" + i, sqCard, defaultSqNames[i], 15f, FontStyles.Bold, 1.3f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
                SetAnchoredRect(sqNames[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, rowY), new Vector2(148f, 24f));

                var sBarBg = CreateImage("SquadHpBg_" + i, sqCard, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, rowY - 4f), new Vector2(190f, 12f), chamferSprite, TrimBarBg(), false);
                sqBars[i] = CreateFilledBar("SquadHpFill_" + i, sBarBg.transform, chamferSprite, Palette.SquadBlue);

                sqStatuses[i] = CreateStyledText("SquadStatus_" + i, sqCard, "100%", 14.5f, FontStyles.Bold, 1.1f, Palette.SquadBlue, TextAlignmentOptions.MidlineRight);
                SetAnchoredRect(sqStatuses[i].rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, rowY), new Vector2(110f, 24f));
            }

            CreateImage("SquadVitalsDivider", sqCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -170f), new Vector2(-32f, 1f), solidSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.22f), false);
            CreateIcon("ShieldIcon", sqCard, new Vector2(0f, 0f), new Vector2(18f, 52f), new Vector2(46f, 46f), shieldSprite, Palette.M3HeliosCoreCyan);

            var hpLabel = CreateStyledText("HPLabel", sqCard, "NOMAD-1 // VANCE", 14.5f, FontStyles.Bold, 1.6f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(hpLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(78f, 122f), new Vector2(210f, 22f));

            var heartRateTxt = CreateStyledText("HeartRateText", sqCard, "74 BPM // NOMINAL", 14.5f, FontStyles.Bold, 1.3f, Palette.HealthGreen, TextAlignmentOptions.BottomRight);
            SetAnchoredRect(heartRateTxt.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 122f), new Vector2(204f, 22f));

            var armorValueTxt = CreateStyledText("ArmorValueText", sqCard, "PLATES 50/50", 14f, FontStyles.Bold, 1.4f, Palette.M3HeliosCoreCyan, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(armorValueTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(78f, 94f), new Vector2(210f, 22f));

            var hpValueText = CreateStyledText("HPValueText", sqCard, "100 / 100", 19f, FontStyles.Bold, 1.5f, Palette.CharacterSecondary, TextAlignmentOptions.BottomRight);
            SetAnchoredRect(hpValueText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 92f), new Vector2(160f, 26f));

            var armorBg = CreateImage("ArmorBarBg", sqCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f), chamferSprite, TrimBarBg(), false);
            armorBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            armorBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            armorBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            armorBg.rectTransform.offsetMin = new Vector2(78f, 76f);
            armorBg.rectTransform.offsetMax = new Vector2(-16f, 88f);
            var armorFill = CreateFilledBar("ArmorBarFill", armorBg.transform, chamferSprite, Palette.M3HeliosCoreCyan);

            var hpBg = CreateImage("HPBarBg", sqCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 18f), chamferSprite, TrimBarBg(), false);
            hpBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            hpBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            hpBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hpBg.rectTransform.offsetMin = new Vector2(78f, 48f);
            hpBg.rectTransform.offsetMax = new Vector2(-16f, 66f);
            var hpFill = CreateFilledBar("HPBarFill", hpBg.transform, chamferSprite, Palette.HealthGreen);

            var dashLabel = CreateStyledText("DashLabel", sqCard, "STAMINA // SPRINT [SHIFT]", 13.5f, FontStyles.Bold, 1.3f, Palette.Accent, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(dashLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(78f, 14f), new Vector2(226f, 22f));

            var dashBg = CreateImage("DashBarBg", sqCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 10f), chamferSprite, TrimBarBg(), false);
            dashBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            dashBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            dashBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dashBg.rectTransform.offsetMin = new Vector2(314f, 19f);
            dashBg.rectTransform.offsetMax = new Vector2(-16f, 29f);
            var dashFill = CreateFilledBar("DashBarFill", dashBg.transform, chamferSprite, Palette.Accent);

            // Top-Center Tactical Compass Bearing Strip (width 610 -> x: 655..1265 on 1080p)
            var compassPill = CreateTacticalPanel("CompassBearingStrip", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(610f, 34f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.Shadow, 0.88f), Palette.WithAlpha(Palette.CharacterSecondary, 0.30f), out _, false);
            var compassTxt = CreateStyledText("CompassBearingText", compassPill, "345°  ·  [ 000° N ]  ·  015°   |   WP 000° (30M)", 15f, FontStyles.Bold, 1.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            StretchRect(compassTxt.rectTransform);

            // Top-Center SATCOM Status Strip (Act, DTG Grid, Hostiles & Confirmed KIA)
            var centerPill = CreateTacticalPanel("StatusPill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(610f, 70f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f), Palette.WithAlpha(Palette.Accent, 0.65f), out _, false);
            CreateImage("PillAccent", centerPill, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var waveText = CreateStyledText("WaveText", centerPill, StoryDirector.GetActLabel(1), 16.5f, FontStyles.Bold, 1.1f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(waveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(304f, 24f));

            var gridStampTxt = CreateStyledText("GridStampText", centerPill, StoryDirector.GetMissionGridStamp(missionIndex), 13f, FontStyles.Bold, 1.0f, Palette.MutedText, TextAlignmentOptions.TopRight);
            SetAnchoredRect(gridStampTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -11f), new Vector2(276f, 22f));

            var enemiesText = CreateStyledText("EnemiesText", centerPill, "8 HOSTILES IN SECTOR", 15.5f, FontStyles.Bold, 1.5f, Palette.EnemySecondary, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(enemiesText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 10f), new Vector2(320f, 24f));

            var scoreText = CreateStyledText("ScoreText", centerPill, "OP-SCORE // 0", 16.5f, FontStyles.Bold, 1.7f, Palette.Accent, TextAlignmentOptions.BottomRight);
            SetAnchoredRect(scoreText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 10f), new Vector2(250f, 24f));

            // Top-Center Objective Banner
            var objPill = CreateTacticalPanel("ObjectivePill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -134f), new Vector2(610f, 40f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f), Palette.WithAlpha(Palette.C4Amber, 0.62f), out _, false);
            CreateImage("ObjAccentLeft", objPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(4f, 24f), solidSprite, Palette.C4Amber, false);
            var objectiveText = CreateStyledText("ObjectiveBannerText", objPill, StoryDirector.GetSectorObjective(1), 15.5f, FontStyles.Bold, 1.6f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(objectiveText.rectTransform);

            // Boss / HVT Lock Health Bar
            var bossRoot = CreateTacticalPanel("BossBarRoot", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -182f), new Vector2(640f, 66f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f), Palette.EnemySecondary, out _, false);
            CreateImage("BossBarAccent", bossRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 4f), solidSprite, Palette.EnemySecondary, false);

            var bossNameText = CreateStyledText("BossNameText", bossRoot, "HVT LOCK // " + StoryDirector.GetBossCallsign(2), 16.5f, FontStyles.Bold, 2.2f, Palette.EnemySecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(bossNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(460f, 24f));

            var bossHpText = CreateStyledText("BossHpText", bossRoot, "380 / 380", 16.5f, FontStyles.Bold, 1.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(bossHpText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(140f, 24f));

            var bossHpBg = CreateImage("BossHpBg", bossRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 14f), chamferSprite, TrimBarBg(), false);
            SetHorizontalBarRect(bossHpBg.rectTransform, 18f, 18f, -14f, 14f);
            var bossHpFill = CreateFilledBar("BossHpFill", bossHpBg.transform, chamferSprite, Palette.EnemySecondary);
            bossRoot.gameObject.SetActive(false);

            // RWR Threat Warning Receiver Strip
            var rwrRt = CreateTacticalPanel("RwrThreatWarning", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -256f), new Vector2(610f, 38f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.DamageBloodRed, 0.92f), Palette.HitmarkerWhite, out _, false);
            var rwrGroup = rwrRt.gameObject.AddComponent<CanvasGroup>();
            rwrGroup.alpha = 0f;
            rwrGroup.blocksRaycasts = false;
            rwrGroup.interactable = false;
            var rwrTxt = CreateStyledText("RwrWarningText", rwrRt, "!! RWR ALERT !!", 16f, FontStyles.Bold, 2.0f, Palette.HitmarkerWhite, TextAlignmentOptions.Center);
            StretchRect(rwrTxt.rectTransform);

            // Top-Right ISR Overwatch / 155mm CAS Support & Pause Card
            var rightCard = CreateTacticalPanel("StatusCard_Right", hudGroup.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -22f), new Vector2(422f, 102f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.Accent, 0.65f), out _);
            CreateImage("RightAccentTop", rightCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var levelText = CreateStyledText("LevelText", rightCard, "TAC-RECON // 0 KIA", 17.5f, FontStyles.Bold, 1.6f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(314f, 26f));

            var streakTxt = CreateStyledText("StreakText", rightCard, "ISR TASKING // 0/3 KIA", 14f, FontStyles.Bold, 1.4f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(streakTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 32f), new Vector2(314f, 22f));

            var xpBg = CreateImage("XPBarBg", rightCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f), chamferSprite, TrimBarBg(), false);
            xpBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            xpBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            xpBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            xpBg.rectTransform.offsetMin = new Vector2(18f, 14f);
            xpBg.rectTransform.offsetMax = new Vector2(-94f, 26f);
            var xpFill = CreateFilledBar("XPBarFill", xpBg.transform, chamferSprite, Palette.Accent);
            xpFill.fillAmount = 0f;

            var hudPauseBtn = CreateTacticalButton("HUD_PauseButton", rightCard, "| |", new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(66f, 66f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.45f), Palette.CharacterSecondary, 21f, out _);

            // Top-Right Rolling Combat Telemetry Feed
            var killFeedTxt = CreateStyledText("KillFeedText", hudGroup.transform, string.Empty, 15.5f, FontStyles.Bold, 1.1f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(killFeedTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -138f), new Vector2(460f, 108f));

            // Bottom-Right Ordnance, Fire Support, Trauma Kit & FLIR Card
            var equipCard = CreateTacticalPanel("TacticalEquipCard", hudGroup.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 22f), new Vector2(496f, 222f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.Accent, 0.65f), out _);
            CreateImage("EquipAccentTop", equipCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);
            var wpnCaliberTxt = CreateStyledText("WeaponCaliberText", equipCard, "5.56x45 NATO · AUTO", 14f, FontStyles.Bold, 1.5f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(wpnCaliberTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(300f, 24f));

            var nvgToggleBtn = CreateTacticalButton("NvgToggleButton", equipCard, "[N] FLIR: OFF", new Vector2(1f, 1f), new Vector2(-16f, -10f), new Vector2(148f, 32f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.NvgPhosphor, 0.45f), Palette.MutedText, 13.5f, out _);
            var nvgStatusTxt = nvgToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (nvgStatusTxt != null) nvgStatusTxt.characterSpacing = 1.0f;

            var ammoWpnNameTxt = CreateStyledText("AmmoWeaponNameText", equipCard, "M4A1 CARBINE", 17f, FontStyles.Bold, 1.6f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(ammoWpnNameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -64f), new Vector2(210f, 28f));

            var ammoHudTxt = CreateStyledText("AmmoHudText", equipCard, "30 / 30  [R]", 27f, FontStyles.Bold, 1.5f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(ammoHudTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -64f), new Vector2(260f, 34f));

            var reloadBg = CreateImage("ReloadBarBg", equipCard, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 10f), chamferSprite, TrimBarBg(), false);
            reloadBg.rectTransform.offsetMin = new Vector2(16f, -108f);
            reloadBg.rectTransform.offsetMax = new Vector2(-16f, -98f);
            var reloadProgressFill = CreateFilledBar("ReloadProgressFill", reloadBg.transform, chamferSprite, Palette.Accent);

            var fragBtn = CreateTacticalButton("FragOrdnanceBtn", equipCard, "[G] FRAG x3", new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(146f, 66f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.Accent, 0.55f), Palette.Accent, 14.5f, out _);
            fragBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
            var fragCountTxt = fragBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (fragCountTxt != null) fragCountTxt.characterSpacing = 0.9f;

            var strikeBtn = CreateTacticalButton("MortarStrikeBtn", equipCard, "[F] 155MM (0/5)", new Vector2(0f, 0f), new Vector2(175f, 16f), new Vector2(146f, 66f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.35f), Palette.MutedText, 13.5f, out _);
            strikeBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
            var strikeStatusTxt = strikeBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (strikeStatusTxt != null) strikeStatusTxt.characterSpacing = 0.8f;

            var traumaBtn = CreateTacticalButton("TraumaPlateBtn", equipCard, "[Q] PLATE x2", new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(146f, 66f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.M3HeliosCoreCyan, 0.55f), Palette.M3HeliosCoreCyan, 14.5f, out _);
            traumaBtn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
            var traumaCountTxt = traumaBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (traumaCountTxt != null) traumaCountTxt.characterSpacing = 0.9f;

            // Bottom-Center Context Action Prompt Pill ([E] C4 Breach / Sabotage / Stabilize)
            var ctxPill = CreateTacticalPanel("ContextActionPill", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 108f), new Vector2(610f, 44f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f), Palette.C4Amber, out _, false);
            var ctxGroup = ctxPill.gameObject.AddComponent<CanvasGroup>();
            ctxGroup.alpha = 0f;
            ctxGroup.blocksRaycasts = false;
            ctxGroup.interactable = false;
            CreateImage("CtxAccentLeft", ctxPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(5f, 24f), solidSprite, Palette.C4Amber, false);
            var ctxText = CreateStyledText("ContextPromptText", ctxPill, "[E] PLANT C4 AT " + StoryDirector.GetObjectiveNodeTitle(missionIndex, 0), 16f, FontStyles.Bold, 1.6f, Palette.C4Amber, TextAlignmentOptions.Center);
            StretchRect(ctxText.rectTransform);

            // Bottom-Center 6-Weapon Loadout Bar (width 640 fits cleanly between Left 534 and Right 1400!)
            var weaponBar = CreateTacticalPanel("WeaponLoadoutBar", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(640f, 74f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.Accent, 0.55f), out _);
            CreateImage("WeaponBarAccent", weaponBar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var wpnBtns = new Button[SaveManager.WeaponCount];
            var wpnBgs = new Image[SaveManager.WeaponCount];
            var wpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            string[] wpnSlotDefault = { "[1]\nM4A1", "[2]\nM870", "[3]\nMK14", "[4]\nRPG7", "[5]\nAN94", "[6]\nVEC" };
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var slotX = -255f + i * 102f;
                var b = CreateTacticalButton("WeaponSlot_" + i, weaponBar, wpnSlotDefault[i], new Vector2(0.5f, 0.5f), new Vector2(slotX, 0f), new Vector2(94f, 52f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.30f), Palette.CharacterSecondary, 13f, out _);
                wpnBtns[i] = b;
                wpnBgs[i] = b.GetComponent<Image>();
                wpnLbls[i] = b.GetComponentInChildren<TextMeshProUGUI>();
                if (wpnLbls[i] != null) wpnLbls[i].characterSpacing = 0.6f;
            }

            // Mobile / Touch Controls (Shown strictly on mobile/handheld devices, hidden on PC)
            var touchRoot = new GameObject("MobileTouchControls");
            touchRoot.transform.SetParent(hudGroup.transform, false);
            var touchRt = touchRoot.AddComponent<RectTransform>();
            StretchRect(touchRt);

            var moveBgRt = CreatePanel("MoveStickBg", touchRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(180f, 180f), new Vector2(210f, 210f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            moveBgRt.pivot = new Vector2(0.5f, 0.5f);
            moveBgRt.anchoredPosition = new Vector2(180f, 180f);
            var moveHandleImg = CreateImage("MoveStickHandle", moveBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f), circleSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.65f), false);
            var leftStick = moveBgRt.gameObject.AddComponent<VirtualJoystick>();
            leftStick.BindRects(moveBgRt, moveHandleImg.rectTransform);

            var aimBgRt = CreatePanel("AimStickBg", touchRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 180f), new Vector2(210f, 210f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            aimBgRt.pivot = new Vector2(0.5f, 0.5f);
            aimBgRt.anchoredPosition = new Vector2(-180f, 180f);
            var aimHandleImg = CreateImage("AimStickHandle", aimBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.78f), false);
            var rightStick = aimBgRt.gameObject.AddComponent<VirtualJoystick>();
            rightStick.BindRects(aimBgRt, aimHandleImg.rectTransform);

            var touchDashBtn = CreateButton("TouchDashButton", touchRt, "SPRINT", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.30f), Palette.CharacterSecondary, 18f);
            var dashBtnRt = touchDashBtn.GetComponent<RectTransform>();
            dashBtnRt.anchorMin = dashBtnRt.anchorMax = new Vector2(1f, 0f);
            dashBtnRt.pivot = new Vector2(0.5f, 0.5f);
            dashBtnRt.anchoredPosition = new Vector2(-360f, 148f);

            // Transient Milestone Banner
            var bannerGo = new GameObject("MilestoneBanner");
            bannerGo.transform.SetParent(hudGroup.transform, false);
            var bannerRt = bannerGo.AddComponent<RectTransform>();
            bannerRt.anchorMin = bannerRt.anchorMax = new Vector2(0.5f, 1f);
            bannerRt.pivot = new Vector2(0.5f, 1f);
            bannerRt.anchoredPosition = new Vector2(0f, -304f);
            bannerRt.sizeDelta = new Vector2(640f, 52f);
            var bannerCg = bannerGo.AddComponent<CanvasGroup>();
            bannerCg.alpha = 0f;
            bannerCg.blocksRaycasts = false;
            var bannerBg = bannerGo.AddComponent<Image>();
            bannerBg.sprite = chamferSprite;
            if (chamferSprite != null && chamferSprite.border.sqrMagnitude > 0f) bannerBg.type = Image.Type.Sliced;
            bannerBg.color = Palette.WithAlpha(Palette.Shadow, 0.92f);
            bannerBg.raycastTarget = false;
            var bannerBorder = CreateImage("BannerBorder", bannerRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, borderSprite, Palette.Accent, false);
            if (borderSprite != null && borderSprite.border.sqrMagnitude > 0f) bannerBorder.type = Image.Type.Sliced;
            CreateImage("BannerStripe", bannerRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f), new Vector2(-24f, 4f), solidSprite, Palette.Accent, false);
            var milestoneTxt = CreateStyledText("MilestoneText", bannerRt, "ISR LINK // OVERWATCH", 21f, FontStyles.Bold, 2.8f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(milestoneTxt.rectTransform);

            // Directional Hit Arc Indicator (Centered on viewport)
            var hitArcGo = new GameObject("HitDirectionArc");
            hitArcGo.transform.SetParent(hudGroup.transform, false);
            var hitArcRt = hitArcGo.AddComponent<RectTransform>();
            hitArcRt.anchorMin = hitArcRt.anchorMax = new Vector2(0.5f, 0.5f);
            hitArcRt.anchoredPosition = Vector2.zero;
            hitArcRt.sizeDelta = new Vector2(220f, 220f);
            var hitArcCg = hitArcGo.AddComponent<CanvasGroup>();
            hitArcCg.alpha = 0f;
            hitArcCg.blocksRaycasts = false;
            hitArcCg.interactable = false;
            var hitArcImg = CreateImage("HitArcSprite", hitArcRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, s.HitArc, Palette.EnemySecondary, false);
            hitArcImg.preserveAspect = true;

            // Reticle Crosshair, Low-Ammo Hint & Hitmarker
            var crosshairImg = CreateImage("Crosshair", hudGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42f, 42f), crosshairSprite, Palette.WithAlpha(Palette.Accent, 0.95f), false);

            var crosshairReloadHintTxt = CreateStyledText("CrosshairReloadHint", hudGroup.transform, "[R] RELOAD", 14f, FontStyles.Bold, 1.5f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(crosshairReloadHintTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(190f, 24f));
            crosshairReloadHintTxt.gameObject.SetActive(false);

            var hitmarkerGo = new GameObject("HitmarkerGroup");
            hitmarkerGo.transform.SetParent(crosshairImg.transform, false);
            var hmRt = hitmarkerGo.AddComponent<RectTransform>();
            hmRt.anchorMin = hmRt.anchorMax = new Vector2(0.5f, 0.5f);
            hmRt.anchoredPosition = Vector2.zero;
            hmRt.sizeDelta = new Vector2(42f, 42f);
            var hitmarkerGroup = hitmarkerGo.AddComponent<CanvasGroup>();
            hitmarkerGroup.alpha = 0f;
            hitmarkerGroup.blocksRaycasts = false;
            hitmarkerGroup.interactable = false;

            var hmTicks = new Image[4];
            Vector2[] tickPos = { new Vector2(-10f, 10f), new Vector2(10f, 10f), new Vector2(-10f, -10f), new Vector2(10f, -10f) };
            float[] tickAngles = { -45f, 45f, 45f, -45f };
            for (var i = 0; i < 4; i++)
            {
                hmTicks[i] = CreateImage("HmTick_" + i, hmRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), tickPos[i], new Vector2(14f, 3f), solidSprite, Palette.HitmarkerWhite, false);
                hmTicks[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, tickAngles[i]);
            }

            // 2. TITLE PANEL (OPEN-VIEWPORT JSOC TAC-NET COMMAND DASHBOARD - 1920x1080)
            var titleGroup = CreateFullScreenGroup("TitlePanel", canvasGo.transform);
            // Very subtle full-screen raycast scrim + left-edge contrast gradient keeps 3D squad & map open
            CreateImage("TitleScrim", titleGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.Shadow, 0.14f), true);
            CreateImage("TitleLeftBackdrop", titleGroup.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(688f, 0f), solidSprite, Palette.WithAlpha(Palette.Shadow, 0.62f), false);

            // Top Command Header Strip
            var titleTopBar = CreateTacticalPanel("TitleTopBar", titleGroup.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 72f), solidSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f), Palette.WithAlpha(Palette.Accent, 0.40f), out _);
            CreateImage("TitleTopGoldRule", titleTopBar, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f), solidSprite, Palette.WithAlpha(Palette.Accent, 0.85f), false);
            CreateImage("TitleBrandStripe", titleTopBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(5f, 42f), solidSprite, Palette.Accent, false);

            var gameTitle = CreateStyledText("GameTitleText", titleTopBar, StoryDirector.OperationTitle, 30f, FontStyles.Bold, 4.2f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(gameTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(340f, 44f));

            var classTag = CreateStyledText("TitleClassHeader", titleTopBar, "JSOC TAC-NET  //  TS-SCI  //  OPORD BLACKOUT LEDGER", 13.5f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(classTag.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(396f, 0f), new Vector2(460f, 26f));

            var titleBest = CreateStyledText("TitleHighScoreText", titleTopBar, "PEAK RATING // 0", 15.5f, FontStyles.Bold, 1.8f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(titleBest.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-528f, 0f), new Vector2(276f, 28f));

            var titleCredits = CreateStyledText("TitleCreditsText", titleTopBar, "REQUISITION // 200 CR", 16.5f, FontStyles.Bold, 1.8f, Palette.CreditGold, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(titleCredits.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-232f, 0f), new Vector2(276f, 28f));

            var resetCampaignBtn = CreateTacticalButton("TitleResetRosterBtn", titleTopBar, "RESET ROSTER", new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(184f, 42f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.EnemySecondary, 0.72f), Palette.EnemySecondary, 14f, out _);

            // Left Column: Operation Sector List
            var sectorHdr = CreateStyledText("TitleSectorHeader", titleGroup.transform, "SELECT DEPLOYMENT SECTOR", 14.5f, FontStyles.Bold, 2.4f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(sectorHdr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -92f), new Vector2(320f, 24f));

            var titleHeaderMetaTxt = CreateStyledText("TitleHeaderMetaText", titleGroup.transform, "BLACKBOX 0/8  ·  KIA 0/3", 13.5f, FontStyles.Bold, 1.5f, Palette.M3HeliosCoreCyan, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(titleHeaderMetaTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(360f, -92f), new Vector2(292f, 24f));

            var mBtns = new Button[StoryDirector.CampaignMissionCount];
            var mBgs = new Image[StoryDirector.CampaignMissionCount];
            var mLbls = new TextMeshProUGUI[StoryDirector.CampaignMissionCount];
            var actBorders = new Image[StoryDirector.CampaignMissionCount];
            var actSubTxts = new TextMeshProUGUI[StoryDirector.CampaignMissionCount];
            for (var i = 0; i < StoryDirector.CampaignMissionCount; i++)
            {
                var selected = (i + 1) == missionIndex;
                var yPos = -124f - i * 80f;
                var mb = CreateTacticalButton(
                    "MissionSelectBtn_" + i,
                    titleGroup.transform,
                    StoryDirector.GetMissionShortTab(i + 1),
                    new Vector2(0f, 1f),
                    new Vector2(36f, yPos),
                    new Vector2(616f, 70f),
                    chamferSprite,
                    borderSprite,
                    selected ? Palette.WithAlpha(Palette.Accent, 0.24f) : Palette.WithAlpha(Palette.SecondaryButtonBg, 0.94f),
                    selected ? Palette.Accent : Palette.WithAlpha(Palette.CharacterSecondary, 0.28f),
                    selected ? Palette.Accent : Palette.CharacterSecondary,
                    17.5f,
                    out var bdr);

                mBtns[i] = mb;
                mBgs[i] = mb.GetComponent<Image>();
                actBorders[i] = bdr;

                var ml = mb.GetComponentInChildren<TextMeshProUGUI>();
                mLbls[i] = ml;
                if (ml != null)
                {
                    ml.alignment = TextAlignmentOptions.MidlineLeft;
                    ml.characterSpacing = 1.6f;
                    SetAnchoredRect(ml.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(574f, 26f));
                }

                CreateImage("ActStripe_" + i, mb.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(4f, 46f), solidSprite, selected ? Palette.Accent : Palette.SquadBlue, false);
                var subLine = CreateStyledText("TitleActSub_" + i, mb.transform, StoryDirector.GetMissionGridStamp(i + 1), 13f, FontStyles.Normal, 1.2f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
                SetAnchoredRect(subLine.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 11f), new Vector2(574f, 22f));
                actSubTxts[i] = subLine;
            }

            // Left Column: Active OPORD Briefing Card
            var opordPanel = CreateTacticalPanel("TitleOpordPanel", titleGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -448f), new Vector2(616f, 174f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f), Palette.WithAlpha(Palette.Accent, 0.58f), out _, false);
            CreateImage("OpordTopStripe", opordPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var subtitle = CreateStyledText("SubtitleText", opordPanel, StoryDirector.GetMissionTitle(missionIndex) + "  //  " + StoryDirector.GetMissionSubtitle(missionIndex), 14.5f, FontStyles.Bold, 1.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(576f, 38f));

            var missionBriefCopy = StoryDirector.GetMissionTitleBrief(missionIndex);
            var titleBrief = CreateStyledText("TitleBriefText", opordPanel, missionBriefCopy, 14.5f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            titleBrief.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(titleBrief.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -56f), new Vector2(576f, 106f));

            // Left Column: Fireteam Vanguard-09 Live Roster (2x2 Cards)
            var rosterHdr = CreateStyledText("TitleRosterHeader", titleGroup.transform, "TASK FORCE VANGUARD-09  //  CASUALTY & READINESS", 13.5f, FontStyles.Bold, 2.0f, Palette.SquadBlue, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rosterHdr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -636f), new Vector2(616f, 22f));

            var titleRosterTxts = new TextMeshProUGUI[4];
            var rosterBorders = new Image[4];
            var rosterSkulls = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var rx = 36f + col * 316f;
                var ry = -664f - row * 94f;
                var rCard = CreateTacticalPanel("TitleRosterCard_" + i, titleGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(300f, 84f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f), Palette.WithAlpha(Palette.SquadBlue, 0.65f), out var rbdr, false);
                rosterBorders[i] = rbdr;

                var skull = CreateImage("TitleRosterSkull_" + i, rCard, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(26f, 26f), s.SkullKia, Palette.EnemySecondary, false);
                skull.enabled = false;
                rosterSkulls[i] = skull;

                titleRosterTxts[i] = CreateStyledText("TitleRosterSlot_" + i, rCard, "[NOMAD-1] VANCE\nFIRETEAM LEAD · ACTIVE", 14f, FontStyles.Bold, 1.0f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
                titleRosterTxts[i].textWrappingMode = TextWrappingModes.Normal;
                SetAnchoredRect(titleRosterTxts[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(246f, 64f));
            }

            // Bottom-Left Keybind Reference Pill (single clean line)
            var controlsPill = CreateTacticalPanel("ControlsHintPanel", titleGroup.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 30f), new Vector2(768f, 40f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.90f), Palette.WithAlpha(Palette.CharacterSecondary, 0.28f), out _, false);
            var controlsHint = CreateStyledText("ControlsHintText", controlsPill, "WASD MOVE · LMB FIRE · [R] RELOAD · [Q] PLATE · [N] FLIR · [G] FRAG · [F] 155MM · [E] ACT · [Z/X/C] ROE", 11.8f, FontStyles.Bold, 0.6f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(controlsHint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(736f, 28f));

            // Bottom-Right Active Loadout Strip + 3 Spaced Launch Dock Buttons
            var loadoutStrip = CreateTacticalPanel("TitleLoadoutStrip", titleGroup.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 116f), new Vector2(804f, 48f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f), Palette.WithAlpha(Palette.CreditGold, 0.55f), out _, false);
            var titleLoadoutPreviewTxt = CreateStyledText("TitleLoadoutPreviewText", loadoutStrip, "LOADOUT: M4A1 CARBINE (5.56x45 NATO · AUTO)   |   RIG: RANGER OLIVE", 14.5f, FontStyles.Bold, 1.4f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            StretchRect(titleLoadoutPreviewTxt.rectTransform);

            var intelBtn = CreateTacticalButton("TitleIntelButton", titleGroup.transform, "BLACKBOX ARCHIVE", new Vector2(1f, 0f), new Vector2(-36f, 30f), new Vector2(244f, 72f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.M3HeliosCoreCyan, 0.65f), Palette.CharacterSecondary, 16f, out _);
            var armoryBtn = CreateTacticalButton("TitleArmoryButton", titleGroup.transform, "ARMORY / LOADOUT", new Vector2(1f, 0f), new Vector2(-304f, 30f), new Vector2(244f, 72f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CreditGold, 0.65f), Palette.CharacterSecondary, 16f, out _);
            var startBtn = CreateTacticalButton("StartMissionButton", titleGroup.transform, "DEPLOY OPERATION", new Vector2(1f, 0f), new Vector2(-572f, 30f), new Vector2(268f, 72f), chamferSprite, borderSprite, Palette.Accent, Palette.CreditGold, Palette.Shadow, 18.5f, out _);

            // 3. PAUSE PANEL (1920x1080)
            var pauseGroup = CreateFullScreenGroup("PausePanel", canvasGo.transform);
            CreateImage("PauseScrim", pauseGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var pauseCard = CreateTacticalPanel("PauseCard", pauseGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 520f), chamferSprite, borderSprite, Palette.PanelSurface, Palette.Accent, out _);
            CreateImage("PauseAccentTop", pauseCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-28f, 4f), solidSprite, Palette.Accent, false);

            var pauseHeader = CreateStyledText("PauseHeaderText", pauseCard, "TACTICAL PAUSE", 38f, FontStyles.Bold, 5.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(pauseHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(520f, 52f));

            var musicSlider = CreateLabeledSlider(pauseCard, "Music", "MUSIC", new Vector2(0f, 88f), chamferSprite, solidSprite, Palette.HealthGreen, out var musicPct);
            var sfxSlider = CreateLabeledSlider(pauseCard, "SFX", "SFX", new Vector2(0f, 24f), chamferSprite, solidSprite, Palette.Accent, out var sfxPct);

            var resumeBtn = CreateTacticalButton("ResumeButton", pauseCard, "RESUME OP", new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(340f, 60f), chamferSprite, borderSprite, Palette.Accent, Palette.CreditGold, Palette.Shadow, 21f, out _);
            var pRestartBtn = CreateTacticalButton("PauseRestartButton", pauseCard, "REDEPLOY ACT", new Vector2(0.5f, 0.5f), new Vector2(0f, -138f), new Vector2(340f, 54f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.45f), Palette.CharacterSecondary, 18.5f, out _);
            var pTitleBtn = CreateTacticalButton("PauseTitleButton", pauseCard, "ABORT TO TAC-NET", new Vector2(0.5f, 0.5f), new Vector2(0f, -206f), new Vector2(340f, 54f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.EnemySecondary, 0.55f), Palette.CharacterSecondary, 18f, out _);

            // 4. ENDED PANEL (AFTER-ACTION CASUALTY & MEMORIAL REPORT - 1920x1080)
            var endedGroup = CreateFullScreenGroup("EndedPanel", canvasGo.transform);
            CreateImage("EndedScrim", endedGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var endedCard = CreateTacticalPanel("EndedCard", endedGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060f, 720f), chamferSprite, borderSprite, Palette.PanelSurface, Palette.EnemySecondary, out _);
            var endedAccent = CreateImage("EndedAccentTop", endedCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 5f), solidSprite, Palette.EnemySecondary, false);

            var endedTitle = CreateStyledText("EndedTitleText", endedCard, "UNIT WIPED // MISSION M.I.A.", 33f, FontStyles.Bold, 3.6f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(960f, 48f));

            var endedScore = CreateStyledText("EndedScoreText", endedCard, "0 PTS", 50f, FontStyles.Bold, 3.2f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(endedScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 186f), new Vector2(640f, 60f));

            var endedBest = CreateStyledText("EndedBestText", endedCard, "PEAK RECORD // 0", 19f, FontStyles.Bold, 2.8f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(640f, 28f));

            var endedStats = CreateStyledText("EndedStatsText", endedCard, "ACT 01/4   |   HOSTILES KIA: 0   |   VANGUARD KIA: 0/3", 17f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(endedStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 88f), new Vector2(920f, 28f));

            var endedCredits = CreateStyledText("EndedCreditsText", endedCard, "REQUISITION +0 CR   |   FUNDS: 200 CR", 17.5f, FontStyles.Bold, 1.8f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(endedCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 48f), new Vector2(920f, 28f));

            var endedNarrative = CreateStyledText("EndedNarrativeText", endedCard, "", 16.5f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            endedNarrative.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(endedNarrative.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(940f, 96f));

            var memorialBox = CreateTacticalPanel("MemorialLedgerBox", endedCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -138f), new Vector2(940f, 86f), chamferSprite, borderSprite, TrimBarBg(), Palette.WithAlpha(Palette.EnemySecondary, 0.72f), out _, false);
            CreateImage("MemorialStripeLeft", memorialBox, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(5f, 58f), solidSprite, Palette.EnemySecondary, false);
            var endedMemorialTxt = CreateStyledText("EndedMemorialText", memorialBox, "CASUALTY LEDGER: PHANTOM-04 [K.I.A.]", 15.5f, FontStyles.Bold, 1.1f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            endedMemorialTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(endedMemorialTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(8f, 0f), new Vector2(888f, 70f));

            var nextMissionDefault = missionIndex < StoryDirector.CampaignMissionCount
                ? (missionIndex == 1 ? "DEPLOY: ACT II ->" : (missionIndex == 2 ? "DEPLOY: ACT III ->" : "DEPLOY: ACT IV ->"))
                : "NEXT MISSION";
            var eReplayBtn = CreateTacticalButton("EndedReplayButton", endedCard, "REDEPLOY", new Vector2(0.5f, 0.5f), new Vector2(-304f, -260f), new Vector2(276f, 64f), chamferSprite, borderSprite, Palette.Accent, Palette.CreditGold, Palette.Shadow, 20f, out _);
            var eNextMissionBtn = CreateTacticalButton("EndedNextMissionButton", endedCard, nextMissionDefault, new Vector2(0.5f, 0.5f), new Vector2(0f, -260f), new Vector2(284f, 64f), chamferSprite, borderSprite, Palette.HealthGreen, Palette.CharacterSecondary, Palette.Shadow, 18.5f, out _);
            eNextMissionBtn.gameObject.SetActive(missionIndex < StoryDirector.CampaignMissionCount);
            var eTitleBtn = CreateTacticalButton("EndedTitleButton", endedCard, "TAC-NET HQ", new Vector2(0.5f, 0.5f), new Vector2(304f, -260f), new Vector2(276f, 64f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.45f), Palette.CharacterSecondary, 19.5f, out _);

            // 5. ARMORY & LOADOUT MODAL PANEL (1920x1080)
            var armoryGroup = CreateFullScreenGroup("ArmoryPanel", canvasGo.transform);
            CreateImage("ArmoryScrim", armoryGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var armoryCard = CreateTacticalPanel("ArmoryCard", armoryGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 860f), chamferSprite, borderSprite, Palette.PanelSurface, Palette.CreditGold, out _);
            CreateImage("ArmoryAccentTop", armoryCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-40f, 5f), solidSprite, Palette.Accent, false);

            var armoryHeader = CreateStyledText("ArmoryHeaderText", armoryCard, "TACTICAL ARMORY // FIELD LOADOUT", 30f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(armoryHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -50f), new Vector2(780f, 48f));

            var armoryCreditsText = CreateStyledText("ArmoryCreditsText", armoryCard, "200 CR", 28f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(armoryCreditsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -50f), new Vector2(340f, 48f));

            // Left Column: 4 Permanent Field Training Upgrades
            var leftColHdr = CreateStyledText("UpgradesSectionTitle", armoryCard, "FIELD TRAINING & PERKS", 17.5f, FontStyles.Bold, 3.2f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(leftColHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-315f, 286f), new Vector2(580f, 30f));

            string[] upgDescs = { "+20 MAX HP / RANK", "+10% BALLISTIC DMG / RANK", "+8% SPRINT SPD / RANK", "+15% FUNDS / RANK" };
            var upgBtns = new Button[4];
            var upgRankLbls = new TextMeshProUGUI[4];
            var upgCostLbls = new TextMeshProUGUI[4];

            for (var i = 0; i < 4; i++)
            {
                var rowY = 204f - i * 120f;
                var row = CreateTacticalPanel("UpgradeRow_" + i, armoryCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-315f, rowY), new Vector2(580f, 102f), chamferSprite, borderSprite, TrimBarBg(), Palette.WithAlpha(Palette.Accent, 0.40f), out _);
                CreateImage("UpgRowStripe", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(5f, 68f), solidSprite, Palette.Accent, false);

                var uRank = CreateStyledText("UpgRank_" + i, row, "UPGRADE   [LV 0/5]", 15.5f, FontStyles.Bold, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
                SetAnchoredRect(uRank.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -18f), new Vector2(382f, 28f));
                upgRankLbls[i] = uRank;

                var uDesc = CreateStyledText("UpgSub_" + i, row, upgDescs[i], 15f, FontStyles.Bold, 1.4f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
                SetAnchoredRect(uDesc.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 18f), new Vector2(360f, 26f));

                var buyBtn = CreateTacticalButton("UpgradeBtn_" + i, row, "100 CR", new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(158f, 64f), chamferSprite, borderSprite, Palette.Accent, Palette.CreditGold, Palette.Shadow, 18f, out _);
                upgBtns[i] = buyBtn;
                upgCostLbls[i] = buyBtn.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Right Column Top: 6 Unlockable Firearms (2x3 Tactical Grid)
            var rightWpnHdr = CreateStyledText("WeaponsSectionTitle", armoryCard, "WEAPONS ARSENAL", 17.5f, FontStyles.Bold, 3.2f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightWpnHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(315f, 286f), new Vector2(580f, 30f));

            var shopWpnBtns = new Button[SaveManager.WeaponCount];
            var shopWpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var wx = col == 0 ? 165f : 465f;
                var wy = 216f - row * 86f;
                var wb = CreateTacticalButton("ShopWeapon_" + i, armoryCard, "WEAPON " + i, new Vector2(0.5f, 0.5f), new Vector2(wx, wy), new Vector2(280f, 72f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.35f), Palette.CharacterSecondary, 15f, out _);
                shopWpnBtns[i] = wb;
                shopWpnLbls[i] = wb.GetComponentInChildren<TextMeshProUGUI>();
                if (shopWpnLbls[i] != null) shopWpnLbls[i].characterSpacing = 1.4f;
            }

            // Right Column Bottom: 3 Tactical Uniform Camo Patterns
            var rightSkinHdr = CreateStyledText("SkinsSectionTitle", armoryCard, "CAMO UNIFORMS", 17.5f, FontStyles.Bold, 3.2f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightSkinHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(315f, -54f), new Vector2(580f, 30f));

            var shopSkinBtns = new Button[3];
            var shopSkinLbls = new TextMeshProUGUI[3];
            Color[] skinSwatches = { Palette.CharacterPrimary, Palette.SkinGoldPrimary, Palette.SkinCrimsonPrimary };
            for (var i = 0; i < 3; i++)
            {
                var sx = 115f + i * 200f;
                var sb = CreateTacticalButton("ShopSkin_" + i, armoryCard, "CAMO " + i, new Vector2(0.5f, 0.5f), new Vector2(sx, -138f), new Vector2(180f, 92f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CreditGold, 0.45f), Palette.CharacterSecondary, 15.5f, out _);
                CreateImage("Swatch", sb.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(124f, 6f), solidSprite, skinSwatches[i], false);
                shopSkinBtns[i] = sb;
                shopSkinLbls[i] = sb.GetComponentInChildren<TextMeshProUGUI>();
            }

            var armoryCloseBtn = CreateTacticalButton("ArmoryCloseButton", armoryCard, "BACK TO HQ", new Vector2(0.5f, 0.5f), new Vector2(0f, -356f), new Vector2(300f, 64f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.45f), Palette.CharacterSecondary, 21f, out _);

            // 6. FIELD PERK SUPPLY DROP MODAL PANEL (1920x1080)
            var draftGroup = CreateFullScreenGroup("LevelUpDraftPanel", canvasGo.transform);
            CreateImage("DraftScrim", draftGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var draftRoot = CreateTacticalPanel("DraftContainer", draftGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1340f, 610f), chamferSprite, borderSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f), Palette.CardRareBorder, out _);
            CreateImage("DraftAccentTop", draftRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-40f, 5f), solidSprite, Palette.CardRareBorder, false);

            var draftTitle = CreateStyledText("DraftHeaderTitle", draftRoot, "FIELD PERK SUPPLY DROP", 38f, FontStyles.Bold, 5.5f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(draftTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1020f, 50f));

            var draftSub = CreateStyledText("DraftHeaderSub", draftRoot, "SELECT ONE TACTICAL PERK TO EQUIP", 18f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(draftSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(1020f, 30f));

            var drftBtns = new Button[3];
            var drftTitles = new TextMeshProUGUI[3];
            var drftRanks = new TextMeshProUGUI[3];
            var drftDescs = new TextMeshProUGUI[3];

            for (var i = 0; i < 3; i++)
            {
                var cx = -420f + i * 420f;
                var cardBtn = CreateTacticalButton("DraftCard_" + i, draftRoot, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(cx, -42f), new Vector2(388f, 384f), chamferSprite, borderSprite, TrimBarBg(), Palette.CardRareBorder, Palette.CharacterSecondary, 18f, out _);
                CreateImage("CardTopStripe", cardBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-28f, 5f), solidSprite, Palette.CardRareBorder, false);

                var dTitle = CreateStyledText("DraftTitle_" + i, cardBtn.transform, "STOPPING POWER", 24f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.Center);
                SetAnchoredRect(dTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(344f, 44f));

                var dRank = CreateStyledText("DraftRank_" + i, cardBtn.transform, "PERK TIER 1", 18f, FontStyles.Bold, 2.4f, Palette.HealthGreen, TextAlignmentOptions.Center);
                SetAnchoredRect(dRank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(320f, 32f));

                var dDesc = CreateStyledText("DraftDesc_" + i, cardBtn.transform, "+18% Ballistic Damage across all firearms", 19f, FontStyles.Normal, 1.0f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
                dDesc.textWrappingMode = TextWrappingModes.Normal;
                SetAnchoredRect(dDesc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(332f, 128f));

                var dInstall = CreateStyledText("DraftAction_" + i, cardBtn.transform, "[ EQUIP PERK ]", 18f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
                SetAnchoredRect(dInstall.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(260f, 34f));

                drftBtns[i] = cardBtn;
                drftTitles[i] = dTitle;
                drftRanks[i] = dRank;
                drftDescs[i] = dDesc;
            }

            // 7. INTEL DOSSIER MODAL OVERLAY (3-Tab Codex - 1920x1080)
            var intelGroup = CreateFullScreenGroup("IntelPanel", canvasGo.transform);
            intelGroup.alpha = 0f;
            intelGroup.interactable = false;
            intelGroup.blocksRaycasts = false;
            CreateImage("IntelScrim", intelGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var intelCard = CreateTacticalPanel("IntelCard", intelGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1260f, 800f), chamferSprite, borderSprite, Palette.PanelSurface, Palette.M3HeliosCoreCyan, out _);
            CreateImage("IntelAccentTop", intelCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-40f, 5f), solidSprite, Palette.Accent, false);

            var intelHeader = CreateStyledText("IntelHeaderText", intelCard, "TAC-NET INTEL DOSSIER // " + StoryDirector.OperationTitle, 28f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(intelHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -46f), new Vector2(1140f, 44f));

            string[] intelTabNames = { "CAMPAIGN LOG", "PERSONNEL", "FIELD INTEL (8)" };
            var intelTabBtns = new Button[3];
            var intelTabBgs = new Image[3];
            var intelTabLbls = new TextMeshProUGUI[3];
            for (var i = 0; i < 3; i++)
            {
                var tx = -380f + i * 380f;
                var tb = CreateTacticalButton("IntelTabBtn_" + i, intelCard, intelTabNames[i], new Vector2(0.5f, 1f), new Vector2(tx, -104f), new Vector2(354f, 48f), chamferSprite, borderSprite, i == 0 ? Palette.Accent : Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.CharacterSecondary, 0.45f), i == 0 ? Palette.Shadow : Palette.CharacterSecondary, 16.5f, out _);
                intelTabBtns[i] = tb;
                intelTabBgs[i] = tb.GetComponent<Image>();
                intelTabLbls[i] = tb.GetComponentInChildren<TextMeshProUGUI>();
            }

            var intelBodyText = CreateStyledText("IntelBodyText", intelCard, StoryDirector.BuildIntelDossierText(0), 17f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            intelBodyText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(intelBodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(1168f, 496f));

            var intelCloseBtn = CreateTacticalButton("IntelCloseButton", intelCard, "CLOSE DOSSIER", new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(300f, 60f), chamferSprite, borderSprite, Palette.Accent, Palette.CreditGold, Palette.Shadow, 21f, out _);

            // 8. WIDESCREEN CUTSCENE LETTERBOX OVERLAY (1920x1080)
            var cinemaGroup = CreateFullScreenGroup("CinemaOverlay", canvasGo.transform);
            cinemaGroup.alpha = 0f;
            cinemaGroup.interactable = false;
            cinemaGroup.blocksRaycasts = false;

            CreateImage("TopCinemaBar", cinemaGroup.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 120f), solidSprite, Palette.Shadow, true);
            var bottomCinemaBar = CreatePanel("BottomCinemaBar", cinemaGroup.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 172f), solidSprite, Palette.Shadow);

            var cinemaStampTxt = CreateStyledText("CinemaMissionStampText", bottomCinemaBar, StoryDirector.GetMissionTitle(missionIndex) + "\n" + StoryDirector.GetMissionSubtitle(missionIndex), 22f, FontStyles.Bold, 2.2f, Palette.HealthGreen, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(cinemaStampTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 26f), new Vector2(620f, 118f));

            var cinemaSpeakerTxt = CreateStyledText("CinemaSpeakerText", bottomCinemaBar, "MAJ. ROURKE // IRONWOOD-1", 18f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(cinemaSpeakerTxt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, -20f), new Vector2(820f, 32f));

            var cinemaDialogueTxt = CreateStyledText("CinemaDialogueText", bottomCinemaBar, "", 20f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Top);
            cinemaDialogueTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(cinemaDialogueTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40f, -14f), new Vector2(860f, 92f));

            var skipCutsceneBtn = CreateTacticalButton("SkipCutsceneButton", bottomCinemaBar, "SKIP [SPACE] >>", new Vector2(1f, 0.5f), new Vector2(-42f, 0f), new Vector2(256f, 58f), chamferSprite, borderSprite, Palette.SecondaryButtonBg, Palette.WithAlpha(Palette.Accent, 0.55f), Palette.CharacterSecondary, 18f, out _);

            // 9. ACT IV MORAL DECISION MODAL (HELIOS CORE TERMINAL - 1920x1080)
            var choiceGroup = CreateFullScreenGroup("CoreChoicePanel", canvasGo.transform);
            choiceGroup.alpha = 0f;
            choiceGroup.interactable = false;
            choiceGroup.blocksRaycasts = false;
            CreateImage("ChoiceScrim", choiceGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var choiceCard = CreateTacticalPanel("ChoiceContainer", choiceGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1260f, 640f), chamferSprite, borderSprite, Palette.PanelSurface, Palette.C4Amber, out _);
            CreateImage("ChoiceAccentTop", choiceCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-40f, 5f), solidSprite, Palette.C4Amber, false);

            var choiceTitle = CreateStyledText("ChoiceHeaderText", choiceCard, "PROTOCOL ZERO // HELIOS CORE OVERRIDE", 32f, FontStyles.Bold, 3.6f, Palette.C4Amber, TextAlignmentOptions.Center);
            SetAnchoredRect(choiceTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1140f, 48f));

            var choiceDesc = CreateStyledText("ChoiceContextText", choiceCard, "Kozlowski, Reyes, and Rourke paid with their lives to open this console. AEGIS SATCOM is ordering silent scrub before exfil. Decide the fate of Karst-4's Blackout Ledger.", 17.5f, FontStyles.Normal, 0.7f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            choiceDesc.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(choiceDesc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(1120f, 66f));

            var choiceBroadcastBtn = CreateTacticalButton("ChoiceBroadcastBtn", choiceCard, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(-294f, -80f), new Vector2(544f, 344f), chamferSprite, borderSprite, TrimBarBg(), Palette.HealthGreen, Palette.CharacterSecondary, 18f, out _);
            CreateImage("ChoiceLeftStripe", choiceBroadcastBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-28f, 5f), solidSprite, Palette.HealthGreen, false);
            var cbTitle = CreateStyledText("ChoiceBroadcastTitle", choiceBroadcastBtn.transform, "OPTION A // BROADCAST LEDGER\n[SCORCHED TRUTH]", 21f, FontStyles.Bold, 2.2f, Palette.HealthGreen, TextAlignmentOptions.Center);
            SetAnchoredRect(cbTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(490f, 66f));
            var cbBody = CreateStyledText("ChoiceBroadcastBody", choiceBroadcastBtn.transform, "Transmit uncensored Karst-4 & Kestrel war-crime telemetry globally before core destruction.\n\nExposes Dir. Chen & clears Phantom-04 / Vanguard-09 names, but triggers full cleaner purge at LZ VALKYRIE-6.", 16.5f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            cbBody.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(cbBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(486f, 186f));

            var choiceSilentBtn = CreateTacticalButton("ChoiceSilentBtn", choiceCard, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(294f, -80f), new Vector2(544f, 344f), chamferSprite, borderSprite, TrimBarBg(), Palette.C4Amber, Palette.CharacterSecondary, 18f, out _);
            CreateImage("ChoiceRightStripe", choiceSilentBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-28f, 5f), solidSprite, Palette.C4Amber, false);
            var csTitle = CreateStyledText("ChoiceSilentTitle", choiceSilentBtn.transform, "OPTION B // SILENT KILL-SWITCH\n[SEAL PROTOCOL ZERO]", 21f, FontStyles.Bold, 2.2f, Palette.C4Amber, TextAlignmentOptions.Center);
            SetAnchoredRect(csTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(490f, 66f));
            var csBody = CreateStyledText("ChoiceSilentBody", choiceSilentBtn.transform, "Wipe Helios relay & Blackout Ledger dark silently to preserve NATO front cohesion.\n\nDir. Chen walks free under cover while Vance boards VALKYRIE-6 alone with sealed casualty records.", 16.5f, FontStyles.Normal, 0.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            csBody.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(csBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(486f, 186f));

            var ui = systemsRoot.AddComponent<UIManager>();
            ui.BindCore(
                titleGroup, hudGroup, pauseGroup, endedGroup, armoryGroup, draftGroup,
                hpFill, xpFill, dashFill,
                hpValueText, levelText, waveText, enemiesText, scoreText, hudCreditsText,
                crosshairImg.rectTransform,
                bossRoot, bossHpFill, bossNameText, bossHpText,
                bannerCg, bannerRt, milestoneTxt,
                titleBest, titleCredits,
                musicSlider, sfxSlider, musicPct, sfxPct,
                endedTitle, endedScore, endedBest, endedStats, endedCredits,
                startBtn, armoryBtn, hudPauseBtn, resumeBtn, pRestartBtn, pTitleBtn, eReplayBtn, eTitleBtn);

            ui.BindStoreAndMobileWidgets(
                wpnBtns, wpnBgs, wpnLbls,
                leftStick, rightStick, touchDashBtn,
                armoryCreditsText, armoryCloseBtn,
                upgBtns, upgRankLbls, upgCostLbls,
                shopWpnBtns, shopWpnLbls,
                shopSkinBtns, shopSkinLbls,
                drftBtns, drftTitles, drftRanks, drftDescs,
                touchRoot);

            ui.BindStoryWidgets(
                radioCg, radioStripe, radioCallsignTxt, radioBodyTxt,
                intelGroup, intelBodyText, intelBtn, intelCloseBtn, endedNarrative, endedAccent);
            ui.BindIntelTabWidgets(intelTabBtns, intelTabBgs, intelTabLbls);

            ui.BindCutsceneWidgets(
                cinemaGroup, cinemaStampTxt, cinemaSpeakerTxt, cinemaDialogueTxt,
                skipCutsceneBtn, objectiveText);

            ui.BindTacticalSquadWidgets(
                damageVignetteImg,
                ctxGroup, ctxText,
                radarFieldRt, playerChevronImg.rectTransform,
                radarSquadDots, radarEnemyDots, radarObjectiveDots, radarUavTxt,
                sqNames, sqBars, sqStatuses,
                orderBtns, orderBgs,
                fragCountTxt, strikeStatusTxt, streakTxt,
                fragBtn, strikeBtn);

            ui.BindMw4HudWidgets(
                hitmarkerGroup, hmTicks, killFeedTxt,
                ammoHudTxt, reloadProgressFill,
                nvgOverlayGroup, nvgStatusTxt, nvgToggleBtn);

            ui.BindCampaignUi(
                mBtns, mBgs, mLbls,
                eNextMissionBtn, eNextMissionBtn.GetComponentInChildren<TextMeshProUGUI>(),
                subtitle, titleBrief);

            ui.BindModernWarfareWidgets(
                compassTxt, gridStampTxt,
                rwrGroup, rwrTxt,
                armorFill, armorValueTxt, heartRateTxt,
                traumaBtn, traumaCountTxt,
                wpnCaliberTxt, missionClockTxt, intelHudCountTxt,
                waveBars, endedMemorialTxt,
                titleRosterTxts, resetCampaignBtn,
                choiceGroup, choiceBroadcastBtn, choiceSilentBtn);

            ui.BindTacticalV2Widgets(
                radarConeImg.rectTransform,
                hitArcCg,
                hitArcRt,
                crosshairReloadHintTxt,
                sqKiaIcons,
                actBorders,
                actSubTxts,
                rosterBorders,
                rosterSkulls,
                titleHeaderMetaTxt,
                titleLoadoutPreviewTxt,
                ammoWpnNameTxt);

            OptimizeCanvasRaycastTargets(canvasGo);

            return ui;
        }

        private static void OptimizeCanvasRaycastTargets(GameObject canvasRoot)
        {
            var graphics = canvasRoot.GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
            {
                if (g == null) continue;
                var keepInteractive =
                    g.GetComponent<Selectable>() != null ||
                    g.GetComponent<VirtualJoystick>() != null ||
                    g.name.EndsWith("Scrim") ||
                    (g.GetComponentInParent<Slider>() != null && g.name is "Handle" or "Background");
                g.raycastTarget = keepInteractive;
            }
        }

        private static CanvasGroup CreateFullScreenGroup(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            StretchRect(rt);
            return go.AddComponent<CanvasGroup>();
        }

        private static RectTransform CreateTacticalPanel(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pos,
            Vector2 size,
            Sprite fillSprite,
            Sprite borderSprite,
            Color bgColor,
            Color borderColor,
            out Image borderOut,
            bool raycast = true)
        {
            var rt = CreatePanel(name, parent, anchorMin, anchorMax, pos, size, fillSprite, bgColor);
            var bgImg = rt.GetComponent<Image>();
            if (bgImg != null) bgImg.raycastTarget = raycast;

            borderOut = CreateImage("FrameBorder", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, borderSprite, borderColor, false);
            StretchRect(borderOut.rectTransform);
            return rt;
        }

        private static Button CreateTacticalButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchor,
            Vector2 pos,
            Vector2 size,
            Sprite fillSprite,
            Sprite borderSprite,
            Color bgColor,
            Color borderColor,
            Color textColor,
            float fontSize,
            out Image borderOut)
        {
            var btn = CreateButton(name, parent, label, anchor, pos, size, fillSprite, bgColor, textColor, fontSize);
            borderOut = CreateImage("FrameBorder", btn.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, borderSprite, borderColor, false);
            StretchRect(borderOut.rectTransform);
            return btn;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Sprite sprite, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite != null && sprite.border.sqrMagnitude > 0f)
            {
                img.type = Image.Type.Sliced;
            }
            img.color = bgColor;
            img.raycastTarget = true;
            return rt;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Sprite sprite, Color color, bool raycastTarget)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite != null && sprite.border.sqrMagnitude > 0f)
            {
                img.type = Image.Type.Sliced;
            }
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        private static void CreateIcon(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
            Sprite sprite, Color color)
        {
            CreateImage(name, parent, anchor, anchor, pos, size, sprite, color, false);
        }

        private static Image CreateFilledBar(string name, Transform bgParent, Sprite fillSprite, Color fillColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(bgParent, false);
            var rt = go.AddComponent<RectTransform>();
            StretchRect(rt);

            var img = go.AddComponent<Image>();
            img.sprite = fillSprite;
            img.color = fillColor;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI CreateStyledText(string name, Transform parent, string text, float size, FontStyles style, float spacing, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.characterSpacing = spacing;
            tmp.color = color;
            tmp.alignment = align;
            tmp.outlineWidth = 0.18f;
            tmp.outlineColor = Palette.Shadow;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite, Color bgColor, Color textColor, float fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite != null && sprite.border.sqrMagnitude > 0f)
            {
                img.type = Image.Type.Sliced;
            }
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = Palette.ButtonTintIdle;
            cb.highlightedColor = Palette.ButtonTintHover;
            cb.pressedColor = Palette.CharacterSecondary;
            cb.colorMultiplier = 1f;
            btn.colors = cb;

            var txt = CreateStyledText("Label", rt, label, fontSize, FontStyles.Bold, 3.5f, textColor, TextAlignmentOptions.Center);
            StretchRect(txt.rectTransform);
            return btn;
        }

        private static Slider CreateLabeledSlider(Transform parent, string key, string title, Vector2 pos, Sprite roundedSprite, Sprite solidSprite, Color fillColor, out TextMeshProUGUI pctOut)
        {
            var row = CreatePanel("Row_" + key, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(490f, 48f), roundedSprite, Palette.Transparent);
            row.GetComponent<Image>().raycastTarget = false;

            var lbl = CreateStyledText("Label_" + key, row, title, 18f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(110f, 34f));

            pctOut = CreateStyledText("Pct_" + key, row, "80%", 18f, FontStyles.Bold, 2f, Palette.CharacterPrimary, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(pctOut.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(76f, 34f));

            var sliderGo = new GameObject("Slider_" + key);
            sliderGo.transform.SetParent(row, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRt.anchoredPosition = new Vector2(14f, 0f);
            sliderRt.sizeDelta = new Vector2(276f, 28f);

            var bg = CreateImage("Background", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 12f), roundedSprite, TrimBarBg(), true);
            bg.rectTransform.sizeDelta = new Vector2(0f, 12f);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderRt, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.offsetMin = new Vector2(0f, -6f);
            fillAreaRt.offsetMax = new Vector2(0f, 6f);

            var fillImg = CreateImage("Fill", fillAreaRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, roundedSprite, fillColor, false);

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderRt, false);
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            StretchRect(handleAreaRt);

            var handleImg = CreateImage("Handle", handleAreaRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 26f), solidSprite, Palette.CharacterPrimary, true);

            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillImg.rectTransform;
            slider.handleRect = handleImg.rectTransform;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = key == "Music" ? 0.70f : 0.85f;
            return slider;
        }

        private static Color TrimBarBg() => Palette.FromHex("#1D2225");

        private static void SetHorizontalBarRect(RectTransform rt, float leftPadding, float rightPadding, float centerY, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(leftPadding, centerY - height * 0.5f);
            rt.offsetMax = new Vector2(-rightPadding, centerY + height * 0.5f);
        }

        private static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetAnchoredRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
