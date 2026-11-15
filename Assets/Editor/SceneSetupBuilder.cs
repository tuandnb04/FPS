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

        [MenuItem("Vanguard/Rebuild Top-Down Shooter Scene")]
        public static void BuildCompleteScene()
        {
            BuildAllCampaignScenes();
        }

        [MenuItem("Vanguard/Rebuild All 3 Campaign Scenes")]
        public static void BuildAllCampaignScenes()
        {
            EnsureFolders();
            EnsurePhysicsLayersAndMatrix();
            ConfigureUrpAssetOptimization();

            var solidSprite = SaveSpriteAsset(UISpriteFactory.GetSolidRectSprite().texture, TexturesDir + "/Tex_SolidRect.png");
            var roundedSprite = SaveSpriteAsset(UISpriteFactory.GetRoundedRectSprite().texture, TexturesDir + "/Tex_RoundedRect.png");
            var shieldSprite = SaveSpriteAsset(UISpriteFactory.GetShieldIcon().texture, TexturesDir + "/Tex_ShieldIcon.png");
            var crosshairSprite = SaveSpriteAsset(UISpriteFactory.GetCrosshairIcon().texture, TexturesDir + "/Tex_CrosshairIcon.png");
            var circleSprite = SaveSpriteAsset(UISpriteFactory.GetCircleSprite().texture, TexturesDir + "/Tex_Circle.png");
            var chevronSprite = SaveSpriteAsset(UISpriteFactory.GetChevronIcon().texture, TexturesDir + "/Tex_ChevronIcon.png");
            var damageVignetteSprite = SaveSpriteAsset(UISpriteFactory.GetDamageVignetteSprite().texture, TexturesDir + "/Tex_DamageVignette.png");

            var meshes = LoadVanguardMeshes();

            var scenePaths = new string[StoryDirector.CampaignMissionCount];
            for (var m = 1; m <= StoryDirector.CampaignMissionCount; m++)
            {
                scenePaths[m - 1] = "Assets/Scenes/" + StoryDirector.GetMissionSceneName(m) + ".unity";
                BuildMissionScene(
                    m,
                    scenePaths[m - 1],
                    meshes,
                    solidSprite,
                    roundedSprite,
                    shieldSprite,
                    crosshairSprite,
                    circleSprite,
                    chevronSprite,
                    damageVignetteSprite);
            }

            var buildScenes = new EditorBuildSettingsScene[scenePaths.Length];
            for (var i = 0; i < scenePaths.Length; i++)
            {
                buildScenes[i] = new EditorBuildSettingsScene(scenePaths[i], true);
            }
            EditorBuildSettings.scenes = buildScenes;

            if (File.Exists("Assets/Scenes/SampleScene.unity"))
            {
                AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            }
            if (File.Exists("Assets/Settings/SampleSceneProfile.asset"))
            {
                AssetDatabase.DeleteAsset("Assets/Settings/SampleSceneProfile.asset");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(scenePaths[0], OpenSceneMode.Single);
            Debug.Log("[SceneSetupBuilder] Rebuilt all 3 Campaign Scenes and registered them cleanly in EditorBuildSettings.");
        }

        private static void BuildMissionScene(
            int missionIndex,
            string scenePath,
            VanguardMeshPack meshes,
            Sprite solidSprite,
            Sprite roundedSprite,
            Sprite shieldSprite,
            Sprite crosshairSprite,
            Sprite circleSprite,
            Sprite chevronSprite,
            Sprite damageVignetteSprite)
        {
            var mTag = "_M" + missionIndex;
            var floorColor = missionIndex switch
            {
                1 => Palette.M1ConcreteApron,
                2 => Palette.RoadAsphalt,
                _ => Palette.M3CitadelStone
            };
            var earthColor = missionIndex switch
            {
                1 => Palette.M1PierWater,
                2 => Palette.M2CanyonEarth,
                _ => Palette.DmzRidgeEarth
            };
            var wallColor = missionIndex switch
            {
                1 => Palette.Walls,
                2 => Palette.M2CanyonEarth,
                _ => Palette.M3CitadelStone
            };
            var hillTint = missionIndex switch
            {
                1 => Palette.AmbientSky,
                2 => Palette.M2CanyonEarth,
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
            var player = BuildPlayerRig(inputAsset, matPlayerPrimary, matPlayerSecondary, matCyanGlow, meshes);
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

            var ui = BuildGameCanvasAndUI(missionIndex, systemsRoot, solidSprite, roundedSprite, shieldSprite, crosshairSprite, circleSprite, chevronSprite, damageVignetteSprite);
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

        private static Sprite SaveSpriteAsset(Texture2D source, string assetPath)
        {
            var png = source.EncodeToPNG();
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
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
            bloom.intensity.value = missionIndex == 3 ? 0.28f : 0.22f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.55f;
            bloom.tint.overrideState = true;
            bloom.tint.value = missionIndex == 2
                ? Palette.M2KeyLightAmber
                : (missionIndex == 3 ? Palette.M3HeliosCoreCyan : Palette.KeyLightWarm);

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
            colorAdj.postExposure.value = missionIndex == 2 ? 0.34f : (missionIndex == 3 ? 0.24f : 0.28f);

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
                : (missionIndex == 3 ? Quaternion.Euler(48f, -32f, 0f) : Quaternion.Euler(44f, -40f, 0f));
            var keyLight = keyLightGo.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = missionIndex == 2
                ? Palette.M2KeyLightAmber
                : (missionIndex == 3 ? Palette.FromHex("#DDEEFF") : Palette.KeyLightWarm);
            keyLight.intensity = missionIndex == 2 ? 2.15f : (missionIndex == 3 ? 2.05f : 1.85f);
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
                : (missionIndex == 3 ? Palette.M3HeliosCoreCyan * 0.75f : Palette.AmbientSky);
            rimLight.intensity = missionIndex == 2 ? 0.85f : (missionIndex == 3 ? 0.88f : 0.78f);
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
            var matBarrelHazard = mats.BarrelHazard;
            var matHeliosGlow = mats.HeliosGlow;
            var matSandbag = mats.Sandbag;

            var props = new CampaignCutsceneProps();
            var env = new GameObject("ArenaEnvironment");

            // Unified ProBuilder 6 Ground Deck, Curbs, Loading Platforms, Ramps & Skyline Horizon
            ProBuilderKitBuilder.BuildCompoundDeckAndRamps(env.transform, mats, missionIndex);
            ProBuilderKitBuilder.BuildProBuilderHorizon(env.transform, mats, missionIndex);
            ProBuilderKitBuilder.BuildPerimeterWallsAndBuildings(env.transform, mats, missionIndex);

            // ProBuilder Blast Gate Dividers + Sliding Blast Doors
            ProBuilderKitBuilder.BuildGateDivider(
                env.transform, "GateDivider_Act1", -14f, 56f, 12f,
                matWalls, matTrim, matAccent,
                out props.Gate1Left, out props.Gate1Right);
            ProBuilderKitBuilder.BuildGateDivider(
                env.transform, "GateDivider_Act2", 22f, 60f, 14f,
                matWalls, matTrim, matAccent,
                out props.Gate2Left, out props.Gate2Right);

            if (missionIndex == 2)
            {
                // ACT II: IRON CORRIDOR RAIL DEPOT — ProBuilder Tanker Train Consists & Fuel Switchyard
                // SECTOR 1 (z: -52..-14): Railcar Yard Approach
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S1_Railcar_W", new Vector3(-14.5f, 0f, -38.5f), 0f, mats.FuelSteel, mats);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S1_Railcar_E", new Vector3(14.5f, 0f, -39.5f), 180f, matRust, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "M2_S1_Bunker_West", new Vector3(-22.5f, 0f, -24.0f), 18f, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "M2_S1_Container_E", new Vector3(22.5f, 0f, -23.0f), 90f, matRust, mats);

                BuildFittedCoverProp(env.transform, "M2_S1_HMMWV_W", meshes.EnvHMMWV, new Vector3(-7.8f, 0f, -33.0f), 20f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                BuildFittedCoverProp(env.transform, "M2_S1_HMMWV_E", meshes.EnvHMMWV, new Vector3(7.8f, 0f, -43.0f), -160f, new Vector3(2.4f, 1.95f, 4.9f), matSandbag);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M2_S1_Cover_MidW", new Vector3(-6.8f, 0f, -24.5f), -16f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "M2_S1_Cover_MidE", new Vector3(6.8f, 0f, -24.5f), 16f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M2_S1_Barrier_GateW", new Vector3(-7.2f, 0f, -16.8f), 28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "M2_S1_Barrier_GateE", new Vector3(7.2f, 0f, -16.8f), -28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_W", new Vector3(-5.0f, 0f, -30.5f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_E", new Vector3(5.6f, 0f, -20.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SW", new Vector3(-24.5f, 0f, -48.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SE", new Vector3(24.5f, 0f, -48.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);

                // SECTOR 2 (z: -14..+22): Canyon Rail Switchyard
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S2_Railcar_SW", new Vector3(-14.5f, 0f, -8.5f), 0f, matRust, mats);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S2_Railcar_NW", new Vector3(-14.5f, 0f, 8.8f), 180f, mats.FuelSteel, mats);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S2_Railcar_SE", new Vector3(14.5f, 0f, -8.5f), 180f, matOlive, mats);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S2_Railcar_NE", new Vector3(14.5f, 0f, 10.2f), 0f, matProps, mats);

                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(3.0f, 1.15f, 1.0f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(3.0f, 1.15f, 1.0f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateW", new Vector3(-7.6f, 0f, 19.6f), 25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateE", new Vector3(7.6f, 0f, 19.6f), -25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matAccent);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Fuel", new Vector3(-10.5f, 0f, 2.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Cargo", new Vector3(10.5f, 0f, 0.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NW", new Vector3(-9.5f, 0f, 14.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NE", new Vector3(9.5f, 0f, 14.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);

                ProBuilderKitBuilder.BuildFuelTank(env.transform, "M2_S2_FuelTank_W1", new Vector3(-27.5f, 0f, -6.5f), 0f, mats);
                ProBuilderKitBuilder.BuildFuelTank(env.transform, "M2_S2_FuelTank_W2", new Vector3(-27.5f, 0f, 8.5f), 90f, mats);
                ProBuilderKitBuilder.BuildRadarDish(env.transform, "M2_S2_Radar_W", new Vector3(-33.0f, 0f, 15.0f), -35f, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "M2_S2_Container_E1", new Vector3(29.0f, 0f, -6.0f), 0f, matRust, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "M2_S2_Container_E2", new Vector3(29.0f, 0f, 10.5f), 12f, matOlive, mats);

                // SECTOR 3 (z: +22..+56): Rail Terminal Loading Platform & LZ
                ProBuilderKitBuilder.BuildHelipad(env.transform, new Vector3(0f, 0f, 42f), matTrim, matAccent, matWalls);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S3_Railcar_W", new Vector3(-14.5f, 0f, 34.5f), 0f, matProps, mats);
                ProBuilderKitBuilder.BuildRailcar(env.transform, "M2_S3_Railcar_E", new Vector3(14.5f, 0f, 34.5f), 180f, mats.FuelSteel, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_West", new Vector3(-22.5f, 0f, 44.5f), -22f, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_East", new Vector3(22.5f, 0f, 44.5f), 22f, mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S3_Sandbag_NW", new Vector3(-8.8f, 0f, 48.2f), 25f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S3_Sandbag_NE", new Vector3(8.8f, 0f, 48.2f), -25f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_W", new Vector3(-8.2f, 0f, 38.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_E", new Vector3(8.5f, 0f, 44.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NW", new Vector3(-26.5f, 0f, 52.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NE", new Vector3(26.5f, 0f, 52.5f), matTrim, matWalls, matSodiumGlow, Palette.M2KeyLightAmber);
            }
            else if (missionIndex == 3)
            {
                // ACT III: SILENT PEAK ALPINE CITADEL — ProBuilder Stone Bastions & Project HELIOS Capacitor Pylons
                // SECTOR 1 (z: -52..-14): Outer Alpine Ridge Checkpoints
                ProBuilderKitBuilder.BuildBunker(env.transform, "M3_S1_Bunker_W", new Vector3(-18.5f, 0f, -24.5f), 18f, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "M3_S1_Bunker_E", new Vector3(18.5f, 0f, -24.5f), -18f, mats);
                BuildFittedCoverProp(env.transform, "S1_HMMWV_West", meshes.EnvHMMWV, new Vector3(-10.5f, 0f, -36.0f), 22f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                BuildFittedCoverProp(env.transform, "S1_HMMWV_East", meshes.EnvHMMWV, new Vector3(10.8f, 0f, -43.5f), -155f, new Vector3(2.4f, 1.95f, 4.9f), matSandbag);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S1_Cover_SW", new Vector3(-6.2f, 0f, -38.5f), 12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S1_Cover_SE", new Vector3(6.2f, 0f, -38.5f), -12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_MidW", new Vector3(-8.2f, 0f, -28.5f), -18f, new Vector3(2.6f, 1.05f, 0.78f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_MidE", new Vector3(8.2f, 0f, -28.5f), 18f, new Vector3(2.6f, 1.05f, 0.78f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_GateW", new Vector3(-7.2f, 0f, -16.8f), 28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_GateE", new Vector3(7.2f, 0f, -16.8f), -28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_W", new Vector3(-4.2f, 0f, -32.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_E", new Vector3(7.2f, 0f, -23.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SW", new Vector3(-24.5f, 0f, -48.5f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SE", new Vector3(24.5f, 0f, -48.5f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);

                // SECTOR 2 (z: -14..+22): Project HELIOS Reactor Courtyard & Twin Capacitor Pylons
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(2.8f, 1.05f, 0.85f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(2.8f, 1.05f, 0.85f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateW", new Vector3(-7.6f, 0f, 19.6f), 25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateE", new Vector3(7.6f, 0f, 19.6f), -25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matHeliosGlow);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Fuel", new Vector3(-13.2f, 0f, 2.8f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Cargo", new Vector3(15.2f, 0f, -1.2f), mats);

                ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M3_HeliosPylon_W", new Vector3(-25.5f, 0f, -1.2f), new Vector3(2.4f, 4.8f, 2.4f), mats);
                ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M3_HeliosPylon_E", new Vector3(27.5f, 0f, 1.5f), new Vector3(2.4f, 4.8f, 2.4f), mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "M3_S2_Bunker_NW", new Vector3(-28.5f, 0f, 14.5f), -35f, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "M3_S2_Bunker_NE", new Vector3(28.5f, 0f, 14.5f), 35f, mats);
                ProBuilderKitBuilder.BuildRadarDish(env.transform, "M3_S2_Radar_SE", new Vector3(31.5f, 0f, -9.5f), 145f, mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NW", new Vector3(-11.8f, 0f, 11.8f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NE", new Vector3(11.8f, 0f, 11.8f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);

                // SECTOR 3 (z: +22..+56): Summit Bastion, HELIOS Array & VALKYRIE-6 Extraction LZ
                ProBuilderKitBuilder.BuildHelipad(env.transform, new Vector3(0f, 0f, 42f), matTrim, matAccent, matWalls);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_West", new Vector3(-19.5f, 0f, 40.5f), -22f, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_East", new Vector3(19.5f, 0f, 40.5f), 22f, mats);
                ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M3_HeliosPylon_NW", new Vector3(-16.0f, 0f, 51.5f), new Vector3(2.2f, 4.4f, 2.2f), mats);
                ProBuilderKitBuilder.BuildHeliosPylon(env.transform, "M3_HeliosPylon_NE", new Vector3(16.0f, 0f, 51.5f), new Vector3(2.2f, 4.4f, 2.2f), mats);
                ProBuilderKitBuilder.BuildRadarDish(env.transform, "S3_Radar_NE", new Vector3(24.0f, 0f, 50.5f), -140f, mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S3_Hesco_W", new Vector3(-10.8f, 0f, 34.0f), -28f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S3_Hesco_E", new Vector3(10.8f, 0f, 34.0f), 28f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S3_Barrier_NW", new Vector3(-9.5f, 0f, 48.2f), 25f, new Vector3(2.8f, 1.05f, 0.85f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S3_Barrier_NE", new Vector3(9.5f, 0f, 48.2f), -25f, new Vector3(2.8f, 1.05f, 0.85f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_W", new Vector3(-8.2f, 0f, 38.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_E", new Vector3(8.5f, 0f, 44.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NW", new Vector3(-26.5f, 0f, 52.5f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NE", new Vector3(26.5f, 0f, 52.5f), matTrim, matWalls, matHeliosGlow, Palette.M3HeliosCoreCyan);
            }
            else
            {
                // ACT I: BLACKWATER PIER DOCKYARD — ProBuilder Containers, Fuel Bunkers & Dockyard Defenses
                // SECTOR 1 (z: -52..-14): Harbor Dockyard Gate & Staging Apron
                ProBuilderKitBuilder.BuildBunker(env.transform, "S1_Bunker_West", new Vector3(-18.5f, 0f, -24.5f), 18f, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S1_Container_SW", new Vector3(-14.5f, 0f, -40f), 14f, matOlive, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S1_Container_SE", new Vector3(15.5f, 0f, -38f), -12f, matRust, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S1_Container_NE", new Vector3(17.5f, 0f, -22.5f), 86f, matOlive, mats);

                BuildFittedCoverProp(env.transform, "S1_HMMWV_West", meshes.EnvHMMWV, new Vector3(-10.5f, 0f, -34.0f), 22f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                BuildFittedCoverProp(env.transform, "S1_HMMWV_East", meshes.EnvHMMWV, new Vector3(10.8f, 0f, -43.5f), -155f, new Vector3(2.4f, 1.95f, 4.9f), matSandbag);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S1_Cover_SW", new Vector3(-5.8f, 0f, -38.5f), 12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S1_Cover_SE", new Vector3(5.8f, 0f, -38.5f), -12f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S1_Cover_MidW", new Vector3(-7.8f, 0f, -28.5f), -18f, new Vector3(3.5f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S1_Cover_MidE", new Vector3(7.8f, 0f, -28.5f), 18f, new Vector3(3.5f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_SouthW", new Vector3(-6.8f, 0f, -47.0f), 14f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_SouthE", new Vector3(6.8f, 0f, -47.0f), -14f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_GateW", new Vector3(-7.2f, 0f, -16.8f), 28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S1_Barrier_GateE", new Vector3(7.2f, 0f, -16.8f), -28f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_W", new Vector3(-4.2f, 0f, -32.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S1_E", new Vector3(7.2f, 0f, -23.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SW", new Vector3(-24.5f, 0f, -48.5f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S1_SE", new Vector3(24.5f, 0f, -48.5f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);

                // SECTOR 2 (z: -14..+22): Central Coastal Fuel Tank Farm & Cargo Container Pier
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(3.0f, 1.15f, 1.0f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(3.0f, 1.15f, 1.0f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateW", new Vector3(-7.6f, 0f, 19.6f), 25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildJerseyBarrier(env.transform, "S2_Barrier_GateE", new Vector3(7.6f, 0f, 19.6f), -25f, new Vector3(2.4f, 0.95f, 0.72f), mats);
                ProBuilderKitBuilder.BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matAccent);
                BuildFittedCoverProp(env.transform, "S2_HMMWV_East", meshes.EnvHMMWV, new Vector3(14.5f, 0f, 15.5f), -118f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Fuel", new Vector3(-13.2f, 0f, 2.8f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S2_Cargo", new Vector3(15.2f, 0f, -1.2f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NW", new Vector3(-11.8f, 0f, 11.8f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_NE", new Vector3(11.8f, 0f, 11.8f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);

                ProBuilderKitBuilder.BuildFuelTank(env.transform, "S2_FuelTank_1", new Vector3(-25.5f, 0f, -6.0f), 0f, mats);
                ProBuilderKitBuilder.BuildFuelTank(env.transform, "S2_FuelTank_2", new Vector3(-25.5f, 0f, 3.5f), 90f, mats);
                ProBuilderKitBuilder.BuildFuelTank(env.transform, "S2_FuelTank_3", new Vector3(-19.5f, 0f, -1.2f), 180f, mats);
                ProBuilderKitBuilder.BuildFuelTank(env.transform, "S2_FuelTank_4", new Vector3(-25.5f, 0f, 13.0f), 45f, mats);
                ProBuilderKitBuilder.BuildRadarDish(env.transform, "S2_Radar_West", new Vector3(-32.5f, 0f, 14.5f), -35f, mats);

                ProBuilderKitBuilder.BuildContainer(env.transform, "S2_Container_E1", new Vector3(18.5f, 0f, -5.5f), 0f, matOlive, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S2_Container_E2", new Vector3(23.0f, 0f, -5.5f), 0f, matRust, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S2_Container_E2_Top", new Vector3(23.0f, 2.6f, -5.5f), 4f, matProps, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S2_Container_E3", new Vector3(21.5f, 0f, 6.0f), 90f, matOlive, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S2_Container_E4", new Vector3(30.5f, 0f, 12.0f), -16f, matRust, mats);

                // SECTOR 3 (z: +22..+56): North Pier Command HQ & VALKYRIE-6 Extraction Helipad
                ProBuilderKitBuilder.BuildHelipad(env.transform, new Vector3(0f, 0f, 42f), matTrim, matAccent, matWalls);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_West", new Vector3(-19.5f, 0f, 40.5f), -22f, mats);
                ProBuilderKitBuilder.BuildBunker(env.transform, "S3_Bunker_East", new Vector3(19.5f, 0f, 40.5f), 22f, mats);
                ProBuilderKitBuilder.BuildRadarDish(env.transform, "S3_Radar_NE", new Vector3(24.0f, 0f, 50.5f), -140f, mats);
                ProBuilderKitBuilder.BuildFuelTank(env.transform, "S3_FuelTank_NW", new Vector3(-23.5f, 0f, 50.0f), 120f, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S3_Container_SW", new Vector3(-17.5f, 0f, 28.5f), 88f, matOlive, mats);
                ProBuilderKitBuilder.BuildContainer(env.transform, "S3_Container_SE", new Vector3(17.5f, 0f, 28.5f), -88f, matRust, mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S3_Hesco_W", new Vector3(-10.8f, 0f, 34.0f), -28f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildHescoBarrier(env.transform, "S3_Hesco_E", new Vector3(10.8f, 0f, 34.0f), 28f, new Vector3(3.2f, 1.35f, 1.1f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S3_Sandbag_NW", new Vector3(-9.5f, 0f, 48.2f), 25f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                ProBuilderKitBuilder.BuildSandbagWall(env.transform, "S3_Sandbag_NE", new Vector3(9.5f, 0f, 48.2f), -25f, new Vector3(3.4f, 1.15f, 1.05f), mats);
                BuildFittedCoverProp(env.transform, "S3_HMMWV_NW", meshes.EnvHMMWV, new Vector3(-13.8f, 0f, 47.0f), 145f, new Vector3(2.4f, 1.95f, 4.9f), matOlive);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_W", new Vector3(-8.2f, 0f, 38.2f), mats);
                ProBuilderKitBuilder.BuildExplosiveBarrel(env.transform, "Barrel_S3_E", new Vector3(8.5f, 0f, 44.5f), mats);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NW", new Vector3(-26.5f, 0f, 52.5f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
                ProBuilderKitBuilder.BuildFloodlightTower(env.transform, "Pylon_S3_NE", new Vector3(26.5f, 0f, 52.5f), matTrim, matWalls, matSodiumGlow, Palette.SodiumAmber);
            }

            // VALKYRIE-6 Blackhawk Helicopter (parked sky-high inactive until Victory Evac Cutscene)
            BuildBlackhawkProp(env.transform, meshes.VehicleBlackhawk, meshes.VehicleBlackhawkRotor, matOlive, matTrim, out props.EvacChopperRoot, out props.EvacChopperRotor);

            // Static batching, occluder flags, and clean layer separation
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
            VanguardMeshPack meshes)
        {
            var playerLayer = LayerMask.NameToLayer("Player");
            var root = new GameObject("Player")
            {
                tag = "Player",
                layer = playerLayer >= 0 ? playerLayer : 0
            };
            root.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, -43f), Quaternion.identity);

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

        private static UIManager BuildGameCanvasAndUI(
            int missionIndex,
            GameObject systemsRoot,
            Sprite solidSprite,
            Sprite roundedSprite,
            Sprite shieldSprite,
            Sprite crosshairSprite,
            Sprite circleSprite,
            Sprite chevronSprite,
            Sprite damageVignetteSprite)
        {
            StoryDirector.ActiveMissionIndex = missionIndex;

            // EventSystem using New Input System UI Module
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // Root Canvas
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
            CreateImage("NvgPhosphorTint", nvgOverlayGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.NvgPhosphor, 0.12f), false);
            CreateImage("NvgVignetteMask", nvgOverlayGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, damageVignetteSprite, Palette.WithAlpha(Palette.Shadow, 0.42f), false);
            var nvgStamp = CreateStyledText("NvgHudStamp", nvgOverlayGroup.transform, "AN/PVS-31 NVG // IR GAIN HIGH", 11f, FontStyles.Bold, 2.2f, Palette.WithAlpha(Palette.NvgPhosphor, 0.90f), TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(nvgStamp.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(218f, -64f), new Vector2(280f, 20f));

            // Fullscreen Crimson Damage Vignette (Non-blocking background overlay)
            var damageVignetteImg = CreateImage("DamageVignette", hudGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, damageVignetteSprite, Palette.WithAlpha(Palette.DamageBloodRed, 0f), false);

            // Top-Left Tactical Radar Minimap
            var radarBox = CreatePanel("TacticalRadarBox", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(182f, 182f), roundedSprite, Palette.HudDarkGlass);
            radarBox.GetComponent<Image>().raycastTarget = false;
            CreateImage("RadarAccentTop", radarBox, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-18f, 3f), solidSprite, Palette.SquadBlue, false);

            var radarUavTxt = CreateStyledText("RadarUavStatusText", radarBox, StoryDirector.GetMissionTitle(missionIndex), 10f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(radarUavTxt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(168f, 18f));

            var radarFieldRt = CreatePanel("RadarField", radarBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(142f, 142f), roundedSprite, Palette.WithAlpha(Palette.Shadow, 0.72f));
            radarFieldRt.GetComponent<Image>().raycastTarget = false;
            CreateImage("RadarGridH", radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(138f, 1f), solidSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.14f), false);
            CreateImage("RadarGridV", radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1f, 138f), solidSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.14f), false);

            var radarObjectiveDots = new Image[4];
            for (var i = 0; i < radarObjectiveDots.Length; i++)
            {
                radarObjectiveDots[i] = CreateImage("RadarObjDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f), circleSprite, Palette.C4Amber, false);
                radarObjectiveDots[i].gameObject.SetActive(false);
            }
            var radarEnemyDots = new Image[16];
            for (var i = 0; i < radarEnemyDots.Length; i++)
            {
                radarEnemyDots[i] = CreateImage("RadarEnemyDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f), circleSprite, Palette.EnemySecondary, false);
                radarEnemyDots[i].gameObject.SetActive(false);
            }
            var radarSquadDots = new Image[3];
            for (var i = 0; i < radarSquadDots.Length; i++)
            {
                radarSquadDots[i] = CreateImage("RadarSquadDot_" + i, radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f), circleSprite, Palette.SquadBlue, false);
                radarSquadDots[i].gameObject.SetActive(false);
            }
            var playerChevronImg = CreateImage("RadarPlayerChevron", radarFieldRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f), chevronSprite, Palette.CharacterSecondary, false);

            // Top-Left Credits Pill (placed cleanly beside Radar)
            var crPill = CreatePanel("CreditsPill_Left", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(218f, -20f), new Vector2(184f, 34f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            CreateImage("CreditsAccentLeft", crPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 20f), solidSprite, Palette.CreditGold, false);
            var hudCreditsText = CreateStyledText("HUD_CreditsText", crPill, "200 CR", 14f, FontStyles.Bold, 2.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(hudCreditsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(10f, 0f), new Vector2(-24f, 28f));

            // Top-Left Non-Blocking TAC-NET Radio Comms Box (cleanly below Radar)
            var radioRt = CreatePanel("RadioCommsBox", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -214f), new Vector2(400f, 92f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            var radioBgImg = radioRt.GetComponent<Image>();
            if (radioBgImg != null) radioBgImg.raycastTarget = false;
            var radioCg = radioRt.gameObject.AddComponent<CanvasGroup>();
            radioCg.alpha = 0f;
            radioCg.interactable = false;
            radioCg.blocksRaycasts = false;
            var radioStripe = CreateImage("RadioAccentStripe", radioRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, 68f), solidSprite, Palette.Accent, false);
            var radioCallsignTxt = CreateStyledText("RadioCallsignText", radioRt, "AEGIS-ACTUAL // TAC-NET CH-04", 12f, FontStyles.Bold, 2.0f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(radioCallsignTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(368f, 20f));
            var radioBodyTxt = CreateStyledText("RadioBodyText", radioRt, "", 12.5f, FontStyles.Normal, 0.7f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            radioBodyTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(radioBodyTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -32f), new Vector2(368f, 54f));

            // Bottom-Left Combined Spectre Detachment Squad Roster, Orders & Operator Vitals Card
            var sqCard = CreatePanel("StatusCard_Left", hudGroup.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 20f), new Vector2(420f, 232f), roundedSprite, Palette.HudDarkGlass);
            CreateImage("CardAccentTop", sqCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.SquadBlue, false);

            string[] orderLabels = { "[Z] FOLLOW", "[X] HOLD", "[C] BREACH" };
            var orderBtns = new Button[3];
            var orderBgs = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                var ob = CreateButton("SquadOrderBtn_" + i, sqCard, orderLabels[i], new Vector2(0f, 1f), new Vector2(16f + i * 132f, -26f), new Vector2(124f, 26f), roundedSprite, i == 0 ? Palette.SquadBlue : Palette.SecondaryButtonBg, Palette.CharacterSecondary, 10.5f);
                var ort = ob.GetComponent<RectTransform>();
                ort.pivot = new Vector2(0f, 0.5f);
                ort.anchoredPosition = new Vector2(16f + i * 132f, -26f);
                orderBtns[i] = ob;
                orderBgs[i] = ob.GetComponent<Image>();
                var olbl = ob.GetComponentInChildren<TextMeshProUGUI>();
                if (olbl != null) olbl.characterSpacing = 1.3f;
            }

            string[] defaultSqNames = { "MAJ. ROURKE", "KOZLOWSKI", "CPL. REYES" };
            var sqNames = new TextMeshProUGUI[3];
            var sqBars = new Image[3];
            var sqStatuses = new TextMeshProUGUI[3];
            for (var i = 0; i < 3; i++)
            {
                var rowY = -58f - i * 28f;
                sqNames[i] = CreateStyledText("SquadName_" + i, sqCard, defaultSqNames[i], 11.5f, FontStyles.Bold, 1.8f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
                SetAnchoredRect(sqNames[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, rowY), new Vector2(124f, 20f));

                var sBarBg = CreateImage("SquadHpBg_" + i, sqCard, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(146f, rowY - 5f), new Vector2(178f, 10f), roundedSprite, TrimBarBg(), false);
                sqBars[i] = CreateFilledBar("SquadHpFill_" + i, sBarBg.transform, roundedSprite, Palette.SquadBlue);

                sqStatuses[i] = CreateStyledText("SquadStatus_" + i, sqCard, "100%", 11f, FontStyles.Bold, 1.4f, Palette.SquadBlue, TextAlignmentOptions.MidlineRight);
                SetAnchoredRect(sqStatuses[i].rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, rowY), new Vector2(72f, 20f));
            }

            CreateImage("SquadVitalsDivider", sqCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -138f), new Vector2(-32f, 1f), solidSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.16f), false);
            CreateIcon("ShieldIcon", sqCard, new Vector2(0f, 0f), new Vector2(36f, 48f), new Vector2(38f, 38f), shieldSprite, Palette.HealthGreen);

            var hpLabel = CreateStyledText("HPLabel", sqCard, "YOU // NOMAD-1", 11.5f, FontStyles.Bold, 2.5f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(hpLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(72f, 64f), new Vector2(190f, 20f));

            var hpValueText = CreateStyledText("HPValueText", sqCard, "100 / 100", 15f, FontStyles.Bold, 2f, Palette.CharacterSecondary, TextAlignmentOptions.BottomRight);
            SetAnchoredRect(hpValueText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 63f), new Vector2(130f, 22f));

            var hpBg = CreateImage("HPBarBg", sqCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 15f), roundedSprite, TrimBarBg(), false);
            hpBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            hpBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            hpBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hpBg.rectTransform.offsetMin = new Vector2(72f, 41f);
            hpBg.rectTransform.offsetMax = new Vector2(-16f, 56f);
            var hpFill = CreateFilledBar("HPBarFill", hpBg.transform, roundedSprite, Palette.HealthGreen);

            var dashLabel = CreateStyledText("DashLabel", sqCard, "TACTICAL SPRINT [SHIFT]", 10.5f, FontStyles.Bold, 2.0f, Palette.Accent, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(dashLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(72f, 14f), new Vector2(180f, 16f));

            var dashBg = CreateImage("DashBarBg", sqCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 7f), roundedSprite, TrimBarBg(), false);
            dashBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            dashBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            dashBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dashBg.rectTransform.offsetMin = new Vector2(262f, 18f);
            dashBg.rectTransform.offsetMax = new Vector2(-16f, 25f);
            var dashFill = CreateFilledBar("DashBarFill", dashBg.transform, roundedSprite, Palette.Accent);

            // Top-Center Mission Act, Hostile Status & Score Pill
            var centerPill = CreatePanel("StatusPill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(520f, 68f), roundedSprite, Palette.PanelSurface);
            CreateImage("PillAccent", centerPill, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var waveText = CreateStyledText("WaveText", centerPill, StoryDirector.GetActLabel(1), 15f, FontStyles.Bold, 2.4f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(waveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -11f), new Vector2(310f, 24f));

            var enemiesText = CreateStyledText("EnemiesText", centerPill, "8 HOSTILES IN SECTOR", 12f, FontStyles.Bold, 2.2f, Palette.EnemySecondary, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(enemiesText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 11f), new Vector2(310f, 20f));

            var scoreText = CreateStyledText("ScoreText", centerPill, "0 PTS", 22f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(165f, 44f));

            // Top-Center Objective Banner + Boss Bar below StatusPill_Center
            var objPill = CreatePanel("ObjectivePill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(520f, 32f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.97f));
            CreateImage("ObjAccentLeft", objPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, 18f), solidSprite, Palette.C4Amber, false);
            var objectiveText = CreateStyledText("ObjectiveBannerText", objPill, StoryDirector.GetSectorObjective(1), 12f, FontStyles.Bold, 2.2f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(objectiveText.rectTransform);

            // Boss Encounter Health Bar (anchored cleanly beneath Objective Pill)
            var bossRoot = CreatePanel("BossBarRoot", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(540f, 56f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f));
            CreateImage("BossBarAccent", bossRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.EnemySecondary, false);

            var bossNameText = CreateStyledText("BossNameText", bossRoot, StoryDirector.GetBossCallsign(2), 13f, FontStyles.Bold, 3.5f, Palette.EnemySecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(bossNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(300f, 20f));

            var bossHpText = CreateStyledText("BossHpText", bossRoot, "380 / 380", 13f, FontStyles.Bold, 2f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(bossHpText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(160f, 20f));

            var bossHpBg = CreateImage("BossHpBg", bossRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(bossHpBg.rectTransform, 18f, 18f, -14f, 12f);
            var bossHpFill = CreateFilledBar("BossHpFill", bossHpBg.transform, roundedSprite, Palette.EnemySecondary);
            bossRoot.gameObject.SetActive(false);

            // Top-Right Killstreak UAV / Mortar Charge & Pause Card
            var rightCard = CreatePanel("StatusCard_Right", hudGroup.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(340f, 82f), roundedSprite, Palette.PanelSurface);
            CreateImage("RightAccentTop", rightCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            var levelText = CreateStyledText("LevelText", rightCard, "KILLSTREAK // 0 KILLS", 14.5f, FontStyles.Bold, 2.2f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(246f, 24f));

            var streakTxt = CreateStyledText("StreakText", rightCard, "STREAK 0 // UAV 3K", 11f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(streakTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 27f), new Vector2(246f, 18f));

            var xpBg = CreateImage("XPBarBg", rightCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 10f), roundedSprite, TrimBarBg(), false);
            xpBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            xpBg.rectTransform.anchorMax = new Vector2(1f, 0f);
            xpBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            xpBg.rectTransform.offsetMin = new Vector2(18f, 11f);
            xpBg.rectTransform.offsetMax = new Vector2(-84f, 21f);
            var xpFill = CreateFilledBar("XPBarFill", xpBg.transform, roundedSprite, Palette.Accent);
            xpFill.fillAmount = 0f;

            var hudPauseBtn = CreateButton("HUD_PauseButton", rightCard, "| |", new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(56f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // Top-Right Rolling Tactical Killfeed (non-blocking text stack below StatusCard_Right)
            var killFeedTxt = CreateStyledText("KillFeedText", hudGroup.transform, string.Empty, 12f, FontStyles.Bold, 1.3f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(killFeedTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -110f), new Vector2(360f, 70f));

            // Bottom-Right Tactical Equipment, Ammo/Reload & NVG Card ([G] Frag, [F] Mortar, [R] Reload, [N] NVG)
            var equipCard = CreatePanel("TacticalEquipCard", hudGroup.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(360f, 148f), roundedSprite, Palette.HudDarkGlass);
            CreateImage("EquipAccentTop", equipCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);
            var equipHdr = CreateStyledText("EquipHeaderText", equipCard, "ORDNANCE // MAG & NVG", 10.5f, FontStyles.Bold, 2.0f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(equipHdr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -11f), new Vector2(196f, 20f));

            var nvgToggleBtn = CreateButton("NvgToggleButton", equipCard, "[N] NVG: OFF", new Vector2(1f, 1f), new Vector2(-16f, -9f), new Vector2(126f, 24f), roundedSprite, Palette.SecondaryButtonBg, Palette.MutedText, 10.5f);
            var nvgStatusTxt = nvgToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (nvgStatusTxt != null) nvgStatusTxt.characterSpacing = 1.4f;

            var ammoHudTxt = CreateStyledText("AmmoHudText", equipCard, "30 / 30  [R]", 16.5f, FontStyles.Bold, 1.8f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(ammoHudTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -44f), new Vector2(220f, 22f));

            var reloadBg = CreateImage("ReloadBarBg", equipCard, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 7f), roundedSprite, TrimBarBg(), false);
            reloadBg.rectTransform.offsetMin = new Vector2(16f, -72f);
            reloadBg.rectTransform.offsetMax = new Vector2(-16f, -65f);
            var reloadProgressFill = CreateFilledBar("ReloadProgressFill", reloadBg.transform, roundedSprite, Palette.Accent);

            var fragBtn = CreateButton("FragOrdnanceBtn", equipCard, "[G] FRAG x3", new Vector2(0f, 0f), new Vector2(16f, 14f), new Vector2(158f, 50f), roundedSprite, Palette.SecondaryButtonBg, Palette.Accent, 12.5f);
            fragBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
            var fragCountTxt = fragBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (fragCountTxt != null) fragCountTxt.characterSpacing = 1.6f;

            var strikeBtn = CreateButton("MortarStrikeBtn", equipCard, "[F] MORTAR (0/5)", new Vector2(1f, 0f), new Vector2(-16f, 14f), new Vector2(162f, 50f), roundedSprite, Palette.SecondaryButtonBg, Palette.MutedText, 12f);
            strikeBtn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
            var strikeStatusTxt = strikeBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (strikeStatusTxt != null) strikeStatusTxt.characterSpacing = 1.5f;

            // Bottom-Center Context Action Prompt Pill ([E] C4 Breach / Sabotage / Buddy Revive)
            var ctxPill = CreatePanel("ContextActionPill", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(460f, 36f), roundedSprite, Palette.HudDarkGlass);
            ctxPill.GetComponent<Image>().raycastTarget = false;
            var ctxGroup = ctxPill.gameObject.AddComponent<CanvasGroup>();
            ctxGroup.alpha = 0f;
            ctxGroup.blocksRaycasts = false;
            ctxGroup.interactable = false;
            CreateImage("CtxAccentLeft", ctxPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, 20f), solidSprite, Palette.C4Amber, false);
            var ctxText = CreateStyledText("ContextPromptText", ctxPill, "[E] PLANT C4 AT " + StoryDirector.GetObjectiveNodeTitle(missionIndex, 0), 12.5f, FontStyles.Bold, 2.0f, Palette.C4Amber, TextAlignmentOptions.Center);
            StretchRect(ctxText.rectTransform);

            // Bottom-Center 6-Weapon Loadout Bar
            var weaponBar = CreatePanel("WeaponLoadoutBar", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(920f, 60f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.90f));
            CreateImage("WeaponBarAccent", weaponBar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-28f, 2f), solidSprite, Palette.Accent, false);

            var wpnBtns = new Button[SaveManager.WeaponCount];
            var wpnBgs = new Image[SaveManager.WeaponCount];
            var wpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            string[] wpnSlotDefault = { "[1] M4A1 CARBINE", "[2] M870 SHOTGUN", "[3] MK14 SNIPER", "[4] RPG-7", "[5] AN-94 ABAKAN", "[6] VECTOR .45 SMG" };
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var slotX = -375f + i * 150f;
                var b = CreateButton("WeaponSlot_" + i, weaponBar, wpnSlotDefault[i], new Vector2(0.5f, 0.5f), new Vector2(slotX, 0f), new Vector2(142f, 42f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 10.6f);
                wpnBtns[i] = b;
                wpnBgs[i] = b.GetComponent<Image>();
                wpnLbls[i] = b.GetComponentInChildren<TextMeshProUGUI>();
                if (wpnLbls[i] != null) wpnLbls[i].characterSpacing = 1.3f;
            }

            // Mobile / Touch Controls (Shown strictly on mobile/handheld devices, hidden on PC)
            var touchRoot = new GameObject("MobileTouchControls");
            touchRoot.transform.SetParent(hudGroup.transform, false);
            var touchRt = touchRoot.AddComponent<RectTransform>();
            StretchRect(touchRt);

            var moveBgRt = CreatePanel("MoveStickBg", touchRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            moveBgRt.pivot = new Vector2(0.5f, 0.5f);
            moveBgRt.anchoredPosition = new Vector2(138f, 138f);
            var moveHandleImg = CreateImage("MoveStickHandle", moveBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.65f), false);
            var leftStick = moveBgRt.gameObject.AddComponent<VirtualJoystick>();
            leftStick.BindRects(moveBgRt, moveHandleImg.rectTransform);

            var aimBgRt = CreatePanel("AimStickBg", touchRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            aimBgRt.pivot = new Vector2(0.5f, 0.5f);
            aimBgRt.anchoredPosition = new Vector2(-138f, 138f);
            var aimHandleImg = CreateImage("AimStickHandle", aimBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.78f), false);
            var rightStick = aimBgRt.gameObject.AddComponent<VirtualJoystick>();
            rightStick.BindRects(aimBgRt, aimHandleImg.rectTransform);

            var touchDashBtn = CreateButton("TouchDashButton", touchRt, "SPRINT", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.30f), Palette.CharacterSecondary, 15f);
            var dashBtnRt = touchDashBtn.GetComponent<RectTransform>();
            dashBtnRt.anchorMin = dashBtnRt.anchorMax = new Vector2(1f, 0f);
            dashBtnRt.pivot = new Vector2(0.5f, 0.5f);
            dashBtnRt.anchoredPosition = new Vector2(-288f, 114f);

            // Transient Milestone Banner (docked cleanly beneath BossBarRoot so they never overlap)
            var bannerGo = new GameObject("MilestoneBanner");
            bannerGo.transform.SetParent(hudGroup.transform, false);
            var bannerRt = bannerGo.AddComponent<RectTransform>();
            bannerRt.anchorMin = bannerRt.anchorMax = new Vector2(0.5f, 1f);
            bannerRt.pivot = new Vector2(0.5f, 1f);
            bannerRt.anchoredPosition = new Vector2(0f, -206f);
            bannerRt.sizeDelta = new Vector2(560f, 48f);
            var bannerCg = bannerGo.AddComponent<CanvasGroup>();
            bannerCg.alpha = 0f;
            bannerCg.blocksRaycasts = false;
            var bannerBg = bannerGo.AddComponent<Image>();
            bannerBg.sprite = roundedSprite;
            bannerBg.color = Palette.WithAlpha(Palette.Shadow, 0.88f);
            bannerBg.raycastTarget = false;
            CreateImage("BannerStripe", bannerRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);
            var milestoneTxt = CreateStyledText("MilestoneText", bannerRt, "KILLSTREAK // ONLINE", 18.5f, FontStyles.Bold, 3.2f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(milestoneTxt.rectTransform);

            // Reticle Crosshair & Center Hitmarker (4 diagonal ticks)
            var crosshairImg = CreateImage("Crosshair", hudGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f), crosshairSprite, Palette.WithAlpha(Palette.Accent, 0.9f), false);

            var hitmarkerGo = new GameObject("HitmarkerGroup");
            hitmarkerGo.transform.SetParent(crosshairImg.transform, false);
            var hmRt = hitmarkerGo.AddComponent<RectTransform>();
            hmRt.anchorMin = hmRt.anchorMax = new Vector2(0.5f, 0.5f);
            hmRt.anchoredPosition = Vector2.zero;
            hmRt.sizeDelta = new Vector2(34f, 34f);
            var hitmarkerGroup = hitmarkerGo.AddComponent<CanvasGroup>();
            hitmarkerGroup.alpha = 0f;
            hitmarkerGroup.blocksRaycasts = false;
            hitmarkerGroup.interactable = false;

            var hmTicks = new Image[4];
            Vector2[] tickPos = { new Vector2(-8f, 8f), new Vector2(8f, 8f), new Vector2(-8f, -8f), new Vector2(8f, -8f) };
            float[] tickAngles = { -45f, 45f, 45f, -45f };
            for (var i = 0; i < 4; i++)
            {
                hmTicks[i] = CreateImage("HmTick_" + i, hmRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), tickPos[i], new Vector2(11f, 2.6f), solidSprite, Palette.HitmarkerWhite, false);
                hmTicks[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, tickAngles[i]);
            }

            // 2. TITLE PANEL
            var titleGroup = CreateFullScreenGroup("TitlePanel", canvasGo.transform);
            CreateImage("TitleScrim", titleGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.Shadow, 0.36f), true);

            var titleCard = CreatePanel("TitleCard", titleGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 568f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.93f));
            CreateImage("TitleAccentTop", titleCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.Accent, false);

            var gameTitle = CreateStyledText("GameTitleText", titleCard, StoryDirector.OperationTitle, 44f, FontStyles.Bold, 6f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(gameTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(740f, 64f));

            var subtitle = CreateStyledText("SubtitleText", titleCard, StoryDirector.GetMissionTitle(missionIndex) + "  //  " + StoryDirector.GetMissionSubtitle(missionIndex), 14.5f, FontStyles.Bold, 3.2f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(760f, 26f));

            var missionBriefCopy = missionIndex switch
            {
                1 => "Lead Spectre Detachment (Maj. Rourke, Spec. Kozlowski & Cpl. Reyes) into Blackwater Pier: breach customs gate, sabotage coastal radar & fuel lines, and eliminate Capt. Dragic.",
                2 => "Ambush Ironward's armored rail convoy inside Iron Corridor Gorge: smash Goliath-02 armor, cut switchyard power, and eliminate Dir. Elena Kastner.",
                _ => "Assault Blackcrest Alpine Citadel by moonlight: overload twin Project HELIOS capacitor pylons, defeat Col. Artem Radek, and extract aboard Blackhawk VALKYRIE-6."
            };
            var titleBrief = CreateStyledText("TitleBriefText", titleCard, missionBriefCopy, 13.5f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            titleBrief.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(titleBrief.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(740f, 56f));

            var titleBest = CreateStyledText("TitleHighScoreText", titleCard, "HIGH SCORE // 0", 15.5f, FontStyles.Bold, 3f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(titleBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 52f), new Vector2(460f, 26f));

            var titleCredits = CreateStyledText("TitleCreditsText", titleCard, "FUNDS // 200 CR", 15.5f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(titleCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(460f, 24f));

            // 3-Act Mission Selector Strip
            var mBtns = new Button[StoryDirector.CampaignMissionCount];
            var mBgs = new Image[StoryDirector.CampaignMissionCount];
            var mLbls = new TextMeshProUGUI[StoryDirector.CampaignMissionCount];
            string[] missionShortLabels = { "ACT I: PIER", "ACT II: DEPOT", "ACT III: PEAK" };
            for (var i = 0; i < StoryDirector.CampaignMissionCount; i++)
            {
                var mx = -224f + i * 224f;
                var mb = CreateButton("MissionSelectBtn_" + i, titleCard, missionShortLabels[i], new Vector2(0.5f, 0.5f), new Vector2(mx, -30f), new Vector2(210f, 40f), roundedSprite, (i + 1 == missionIndex) ? Palette.SquadBlue : Palette.SecondaryButtonBg, Palette.CharacterSecondary, 12.5f);
                mBtns[i] = mb;
                mBgs[i] = mb.GetComponent<Image>();
                mLbls[i] = mb.GetComponentInChildren<TextMeshProUGUI>();
                if (mLbls[i] != null) mLbls[i].characterSpacing = 1.4f;
            }

            var controlsHint = CreateStyledText("ControlsHintText", titleCard, "WASD MOVE · MOUSE FIRE · [R] RELOAD · [N] NVG · [G] FRAG · [F] MORTAR · [E] C4/REVIVE · [Z/X/C] ORDERS", 10.5f, FontStyles.Bold, 1.3f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(controlsHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(760f, 24f));

            var startBtn = CreateButton("StartMissionButton", titleCard, "DEPLOY", new Vector2(0.5f, 0.5f), new Vector2(-216f, -112f), new Vector2(204f, 56f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            var armoryBtn = CreateButton("TitleArmoryButton", titleCard, "ARMORY", new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(204f, 56f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);
            var intelBtn = CreateButton("TitleIntelButton", titleCard, "INTEL DOSSIER", new Vector2(0.5f, 0.5f), new Vector2(216f, -112f), new Vector2(204f, 56f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);

            // 3. PAUSE PANEL
            var pauseGroup = CreateFullScreenGroup("PausePanel", canvasGo.transform);
            CreateImage("PauseScrim", pauseGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var pauseCard = CreatePanel("PauseCard", pauseGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 440f), roundedSprite, Palette.PanelSurface);
            CreateImage("PauseAccentTop", pauseCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 4f), solidSprite, Palette.Accent, false);

            var pauseHeader = CreateStyledText("PauseHeaderText", pauseCard, "PAUSED", 34f, FontStyles.Bold, 6f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(pauseHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(400f, 48f));

            var musicSlider = CreateLabeledSlider(pauseCard, "Music", "MUSIC", new Vector2(0f, 70f), roundedSprite, solidSprite, Palette.HealthGreen, out var musicPct);
            var sfxSlider = CreateLabeledSlider(pauseCard, "SFX", "SFX", new Vector2(0f, 14f), roundedSprite, solidSprite, Palette.Accent, out var sfxPct);

            var resumeBtn = CreateButton("ResumeButton", pauseCard, "RESUME", new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(260f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            var pRestartBtn = CreateButton("PauseRestartButton", pauseCard, "REDEPLOY", new Vector2(0.5f, 0.5f), new Vector2(0f, -118f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);
            var pTitleBtn = CreateButton("PauseTitleButton", pauseCard, "ABORT TO HQ", new Vector2(0.5f, 0.5f), new Vector2(0f, -174f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);

            // 4. ENDED PANEL
            var endedGroup = CreateFullScreenGroup("EndedPanel", canvasGo.transform);
            CreateImage("EndedScrim", endedGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var endedCard = CreatePanel("EndedCard", endedGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 490f), roundedSprite, Palette.PanelSurface);
            var endedAccent = CreateImage("EndedAccentTop", endedCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.EnemySecondary, false);

            var endedTitle = CreateStyledText("EndedTitleText", endedCard, "K.I.A. // MISSION FAILED", 30f, FontStyles.Bold, 4.5f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(600f, 50f));

            var endedScore = CreateStyledText("EndedScoreText", endedCard, "0 PTS", 44f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(endedScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 84f), new Vector2(460f, 56f));

            var endedBest = CreateStyledText("EndedBestText", endedCard, "BEST // 0", 18f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(460f, 28f));

            var endedStats = CreateStyledText("EndedStatsText", endedCard, "ELIMINATIONS: 0   |   STAGE REACHED: 1", 15f, FontStyles.Bold, 2f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(endedStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(540f, 24f));

            var endedCredits = CreateStyledText("EndedCreditsText", endedCard, "EARNED +0 CR   |   FUNDS: 200 CR", 15f, FontStyles.Bold, 2f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(endedCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(560f, 24f));

            var endedNarrative = CreateStyledText("EndedNarrativeText", endedCard, "", 13f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            endedNarrative.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(endedNarrative.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -92f), new Vector2(600f, 68f));

            var nextMissionDefault = missionIndex < StoryDirector.CampaignMissionCount
                ? (missionIndex == 1 ? "NEXT: ACT II ->" : "NEXT: ACT III ->")
                : "NEXT MISSION";
            var eReplayBtn = CreateButton("EndedReplayButton", endedCard, "REDEPLOY", new Vector2(0.5f, 0.5f), new Vector2(-220f, -168f), new Vector2(200f, 54f), roundedSprite, Palette.Accent, Palette.Shadow, 19f);
            var eNextMissionBtn = CreateButton("EndedNextMissionButton", endedCard, nextMissionDefault, new Vector2(0.5f, 0.5f), new Vector2(0f, -168f), new Vector2(204f, 54f), roundedSprite, Palette.HealthGreen, Palette.Shadow, 16.5f);
            eNextMissionBtn.gameObject.SetActive(missionIndex < StoryDirector.CampaignMissionCount);
            var eTitleBtn = CreateButton("EndedTitleButton", endedCard, "HQ MENU", new Vector2(0.5f, 0.5f), new Vector2(220f, -168f), new Vector2(200f, 54f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // 5. ARMORY & LOADOUT MODAL PANEL
            var armoryGroup = CreateFullScreenGroup("ArmoryPanel", canvasGo.transform);
            CreateImage("ArmoryScrim", armoryGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var armoryCard = CreatePanel("ArmoryCard", armoryGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 680f), roundedSprite, Palette.PanelSurface);
            CreateImage("ArmoryAccentTop", armoryCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 4f), solidSprite, Palette.Accent, false);

            var armoryHeader = CreateStyledText("ArmoryHeaderText", armoryCard, "TACTICAL ARMORY // FIELD LOADOUT", 23f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(armoryHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -44f), new Vector2(620f, 44f));

            var armoryCreditsText = CreateStyledText("ArmoryCreditsText", armoryCard, "200 CR", 22f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(armoryCreditsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -44f), new Vector2(280f, 44f));

            // Left Column: 4 Permanent Field Training Upgrades
            var leftColHdr = CreateStyledText("UpgradesSectionTitle", armoryCard, "FIELD TRAINING & PERKS", 14f, FontStyles.Bold, 3.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(leftColHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, 212f), new Vector2(440f, 26f));

            string[] upgDescs = { "+20 MAX HP / RANK", "+10% BALLISTIC DMG / RANK", "+8% SPRINT SPD / RANK", "+15% FUNDS / RANK" };
            var upgBtns = new Button[4];
            var upgRankLbls = new TextMeshProUGUI[4];
            var upgCostLbls = new TextMeshProUGUI[4];

            for (var i = 0; i < 4; i++)
            {
                var rowY = 152f - i * 94f;
                var row = CreatePanel("UpgradeRow_" + i, armoryCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, rowY), new Vector2(440f, 80f), roundedSprite, TrimBarBg());
                CreateImage("UpgRowStripe", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 50f), solidSprite, Palette.Accent, false);

                var uRank = CreateStyledText("UpgRank_" + i, row, "UPGRADE   [LV 0/5]", 13f, FontStyles.Bold, 1f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
                SetAnchoredRect(uRank.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(280f, 22f));
                upgRankLbls[i] = uRank;

                var uDesc = CreateStyledText("UpgSub_" + i, row, upgDescs[i], 12f, FontStyles.Bold, 1.5f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
                SetAnchoredRect(uDesc.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 14f), new Vector2(275f, 20f));

                var buyBtn = CreateButton("UpgradeBtn_" + i, row, "100 CR", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(128f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 15f);
                upgBtns[i] = buyBtn;
                upgCostLbls[i] = buyBtn.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Right Column Top: 6 Unlockable Firearms (2x3 Tactical Grid)
            var rightWpnHdr = CreateStyledText("WeaponsSectionTitle", armoryCard, "WEAPONS ARSENAL", 14f, FontStyles.Bold, 3.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightWpnHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, 212f), new Vector2(440f, 26f));

            var shopWpnBtns = new Button[SaveManager.WeaponCount];
            var shopWpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var wx = col == 0 ? 122f : 348f;
                var wy = 162f - row * 64f;
                var wb = CreateButton("ShopWeapon_" + i, armoryCard, "WEAPON " + i, new Vector2(0.5f, 0.5f), new Vector2(wx, wy), new Vector2(214f, 54f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 11.5f);
                shopWpnBtns[i] = wb;
                shopWpnLbls[i] = wb.GetComponentInChildren<TextMeshProUGUI>();
                if (shopWpnLbls[i] != null) shopWpnLbls[i].characterSpacing = 1.6f;
            }

            // Right Column Bottom: 3 Tactical Uniform Camo Patterns
            var rightSkinHdr = CreateStyledText("SkinsSectionTitle", armoryCard, "CAMO UNIFORMS", 14f, FontStyles.Bold, 3.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightSkinHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, -48f), new Vector2(440f, 26f));

            var shopSkinBtns = new Button[3];
            var shopSkinLbls = new TextMeshProUGUI[3];
            Color[] skinSwatches = { Palette.CharacterPrimary, Palette.SkinGoldPrimary, Palette.SkinCrimsonPrimary };
            for (var i = 0; i < 3; i++)
            {
                var sx = 85f + i * 150f;
                var sb = CreateButton("ShopSkin_" + i, armoryCard, "CAMO " + i, new Vector2(0.5f, 0.5f), new Vector2(sx, -114f), new Vector2(138f, 70f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 12f);
                CreateImage("Swatch", sb.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(96f, 5f), solidSprite, skinSwatches[i], false);
                shopSkinBtns[i] = sb;
                shopSkinLbls[i] = sb.GetComponentInChildren<TextMeshProUGUI>();
            }

            var armoryCloseBtn = CreateButton("ArmoryCloseButton", armoryCard, "BACK TO HQ", new Vector2(0.5f, 0.5f), new Vector2(0f, -292f), new Vector2(240f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // 6. FIELD PERK SUPPLY DROP MODAL PANEL
            var draftGroup = CreateFullScreenGroup("LevelUpDraftPanel", canvasGo.transform);
            CreateImage("DraftScrim", draftGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var draftRoot = CreatePanel("DraftContainer", draftGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 470f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f));
            CreateImage("DraftAccentTop", draftRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 5f), solidSprite, Palette.CardRareBorder, false);

            var draftTitle = CreateStyledText("DraftHeaderTitle", draftRoot, "FIELD PERK SUPPLY DROP", 30f, FontStyles.Bold, 5.5f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(draftTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 42f));

            var draftSub = CreateStyledText("DraftHeaderSub", draftRoot, "SELECT ONE TACTICAL PERK TO EQUIP", 13.5f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(draftSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(800f, 24f));

            var drftBtns = new Button[3];
            var drftTitles = new TextMeshProUGUI[3];
            var drftRanks = new TextMeshProUGUI[3];
            var drftDescs = new TextMeshProUGUI[3];

            for (var i = 0; i < 3; i++)
            {
                var cx = -325f + i * 325f;
                var cardBtn = CreateButton("DraftCard_" + i, draftRoot, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(cx, -32f), new Vector2(296f, 290f), roundedSprite, TrimBarBg(), Palette.CharacterSecondary, 16f);
                CreateImage("CardTopStripe", cardBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-24f, 4f), solidSprite, Palette.CardRareBorder, false);

                var dTitle = CreateStyledText("DraftTitle_" + i, cardBtn.transform, "STOPPING POWER", 19f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.Center);
                SetAnchoredRect(dTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(260f, 36f));

                var dRank = CreateStyledText("DraftRank_" + i, cardBtn.transform, "PERK TIER 1", 14f, FontStyles.Bold, 2.5f, Palette.HealthGreen, TextAlignmentOptions.Center);
                SetAnchoredRect(dRank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(240f, 26f));

                var dDesc = CreateStyledText("DraftDesc_" + i, cardBtn.transform, "+18% Ballistic Damage across all firearms", 15f, FontStyles.Normal, 1.2f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
                dDesc.textWrappingMode = TextWrappingModes.Normal;
                SetAnchoredRect(dDesc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(248f, 96f));

                var dInstall = CreateStyledText("DraftAction_" + i, cardBtn.transform, "[ EQUIP PERK ]", 14f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
                SetAnchoredRect(dInstall.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(200f, 28f));

                drftBtns[i] = cardBtn;
                drftTitles[i] = dTitle;
                drftRanks[i] = dRank;
                drftDescs[i] = dDesc;
            }

            // 7. INTEL DOSSIER MODAL OVERLAY
            var intelGroup = CreateFullScreenGroup("IntelPanel", canvasGo.transform);
            intelGroup.alpha = 0f;
            intelGroup.interactable = false;
            intelGroup.blocksRaycasts = false;
            CreateImage("IntelScrim", intelGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            var intelCard = CreatePanel("IntelCard", intelGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 590f), roundedSprite, Palette.PanelSurface);
            CreateImage("IntelAccentTop", intelCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 4f), solidSprite, Palette.Accent, false);

            var intelHeader = CreateStyledText("IntelHeaderText", intelCard, "TAC-NET INTEL DOSSIER // " + StoryDirector.OperationTitle, 21f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(intelHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -42f), new Vector2(810f, 40f));

            var intelBodyText = CreateStyledText("IntelBodyText", intelCard, StoryDirector.BuildIntelDossierText(0), 13.5f, FontStyles.Normal, 0.6f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            intelBodyText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(intelBodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(810f, 420f));

            var intelCloseBtn = CreateButton("IntelCloseButton", intelCard, "CLOSE DOSSIER", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(240f, 48f), roundedSprite, Palette.Accent, Palette.Shadow, 17f);

            // 8. WIDESCREEN CUTSCENE LETTERBOX OVERLAY
            var cinemaGroup = CreateFullScreenGroup("CinemaOverlay", canvasGo.transform);
            cinemaGroup.alpha = 0f;
            cinemaGroup.interactable = false;
            cinemaGroup.blocksRaycasts = false;

            CreateImage("TopCinemaBar", cinemaGroup.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 100f), solidSprite, Palette.Shadow, true);
            var bottomCinemaBar = CreatePanel("BottomCinemaBar", cinemaGroup.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 136f), solidSprite, Palette.Shadow);

            var cinemaStampTxt = CreateStyledText("CinemaMissionStampText", bottomCinemaBar, StoryDirector.GetMissionTitle(missionIndex) + "\n" + StoryDirector.GetMissionSubtitle(missionIndex), 17f, FontStyles.Bold, 2.2f, Palette.HealthGreen, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(cinemaStampTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(42f, 20f), new Vector2(540f, 96f));

            var cinemaSpeakerTxt = CreateStyledText("CinemaSpeakerText", bottomCinemaBar, "MAJ. ROURKE // IRONWOOD-1", 13f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(cinemaSpeakerTxt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(30f, -14f), new Vector2(680f, 24f));

            var cinemaDialogueTxt = CreateStyledText("CinemaDialogueText", bottomCinemaBar, "", 15.5f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Top);
            cinemaDialogueTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(cinemaDialogueTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30f, -10f), new Vector2(720f, 68f));

            var skipCutsceneBtn = CreateButton("SkipCutsceneButton", bottomCinemaBar, "SKIP [SPACE] >>", new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(204f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 14f);

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
                eNextMissionBtn, eNextMissionBtn.GetComponentInChildren<TextMeshProUGUI>());

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
            var row = CreatePanel("Row_" + key, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(380f, 40f), roundedSprite, Palette.Transparent);
            row.GetComponent<Image>().raycastTarget = false;

            var lbl = CreateStyledText("Label_" + key, row, title, 15f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(90f, 30f));

            pctOut = CreateStyledText("Pct_" + key, row, "80%", 15f, FontStyles.Bold, 2f, Palette.CharacterPrimary, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(pctOut.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(60f, 30f));

            var sliderGo = new GameObject("Slider_" + key);
            sliderGo.transform.SetParent(row, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRt.anchoredPosition = new Vector2(10f, 0f);
            sliderRt.sizeDelta = new Vector2(210f, 24f);

            var bg = CreateImage("Background", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 10f), roundedSprite, TrimBarBg(), true);
            bg.rectTransform.sizeDelta = new Vector2(0f, 10f);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderRt, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.offsetMin = new Vector2(0f, -5f);
            fillAreaRt.offsetMax = new Vector2(0f, 5f);

            var fillImg = CreateImage("Fill", fillAreaRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, roundedSprite, fillColor, false);

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderRt, false);
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            StretchRect(handleAreaRt);

            var handleImg = CreateImage("Handle", handleAreaRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 22f), solidSprite, Palette.CharacterPrimary, true);

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
