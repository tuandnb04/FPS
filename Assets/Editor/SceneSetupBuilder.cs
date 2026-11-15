using System.Collections.Generic;
using System.IO;
using TMPro;
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
using TopDownShooter.Audio;
using TopDownShooter.CameraRig;
using TopDownShooter.Character;
using TopDownShooter.Combat;
using TopDownShooter.Core;
using TopDownShooter.Enemies;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.Editor
{
    /// <summary>
    /// Deterministic idempotent builder creating materials, textures, URP lighting,
    /// Blender hard-surface geometry, player rig, Cinemachine rig, audio/VFX/feel/save directors, and 6-state uGUI shell.
    /// </summary>
    public static class SceneSetupBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MaterialsDir = "Assets/Materials";
        private const string TexturesDir = "Assets/Textures";
        private const string FbxAssetPath = "Assets/Models/SM_Vanguard_Assets.fbx";
        private const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
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

            public Mesh TacticalCrate;
            public Mesh EnergyPylon;
            public Mesh EnvShippingContainer;
            public Mesh EnvFuelTank;
            public Mesh EnvBunker;
            public Mesh EnvRadarDish;
            public Mesh VehicleBlackhawk;
            public Mesh VehicleBlackhawkRotor;
            public Mesh PickupXpCrystal;
            public Mesh PickupMedkit;
            public Mesh PickupCreditCoin;
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
            EnsureFolders();

            // 1. Persisted Texture Assets & Custom Blender FBX Meshes
            Texture2D floorTex = SaveTextureAsset(UISpriteFactory.GetArenaFloorGridTexture(), TexturesDir + "/Tex_ArenaFloorGrid.png", true);
            Sprite solidSprite = SaveSpriteAsset(UISpriteFactory.GetSolidRectSprite().texture, TexturesDir + "/Tex_SolidRect.png");
            Sprite roundedSprite = SaveSpriteAsset(UISpriteFactory.GetRoundedRectSprite().texture, TexturesDir + "/Tex_RoundedRect.png");
            Sprite shieldSprite = SaveSpriteAsset(UISpriteFactory.GetShieldIcon().texture, TexturesDir + "/Tex_ShieldIcon.png");
            Sprite crosshairSprite = SaveSpriteAsset(UISpriteFactory.GetCrosshairIcon().texture, TexturesDir + "/Tex_CrosshairIcon.png");
            Sprite circleSprite = SaveSpriteAsset(UISpriteFactory.GetCircleSprite().texture, TexturesDir + "/Tex_Circle.png");

            VanguardMeshPack meshes = LoadVanguardMeshes();

            // 2. Persisted Grounded Military Palette Materials & Skybox
            Material matFloor = SaveOrUpdateLitMaterial("Mat_Floor", Palette.Floor, 0.06f, 0.25f, false, 0f, floorTex, new Vector2(38f, 54f));
            Material matWalls = SaveOrUpdateLitMaterial("Mat_Walls", Palette.Walls, 0.08f, 0.24f, false, 0f);
            Material matProps = SaveOrUpdateLitMaterial("Mat_Props", Palette.Props, 0.08f, 0.26f, false, 0f);
            Material matTrim = SaveOrUpdateLitMaterial("Mat_TrimDark", Palette.Gunmetal, 0.62f, 0.46f, false, 0f);
            Material matPlayerPrimary = SaveOrUpdateLitMaterial("Mat_PlayerPrimary", Palette.CharacterPrimary, 0.12f, 0.34f, false, 0f);
            Material matPlayerSecondary = SaveOrUpdateLitMaterial("Mat_PlayerSecondary", Palette.CoyoteTan, 0.10f, 0.30f, false, 0f);
            Material matAccentEmissive = SaveOrUpdateLitMaterial("Mat_AccentEmissive", Palette.HazardYellow, 0.08f, 0.30f, false, 0f);
            Material matCyanGlow = SaveOrUpdateLitMaterial("Mat_CyanGlow", Palette.NvgGreen, 0.25f, 0.68f, false, 0f);
            Material matHealthGlow = SaveOrUpdateLitMaterial("Mat_HealthEmissive", Palette.KeyLightWarm, 0.15f, 0.45f, true, 1.55f);
            Material matContainerRust = SaveOrUpdateLitMaterial("Mat_ContainerRust", Palette.EnemyPrimary, 0.16f, 0.32f, false, 0f);
            Material matFuelSteel = SaveOrUpdateLitMaterial("Mat_FuelSteel", Palette.GuardTowerDark, 0.42f, 0.48f, false, 0f);
            Material matSkybox = SaveOrUpdateSkyboxMaterial("Mat_ArenaSkybox");

            // 3. Configure URP Global Volume Profile
            VolumeProfile profile = ConfigureVolumeProfile();

            // 4. Open/Reset Scene Hierarchy Cleanly
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ClearOldGeneratedRoots();

            // 5. Scene Lighting, Fog, Skybox, Camera, Volume
            ConfigureEnvironmentLighting(matSkybox, profile);
            UnityEngine.Camera mainCam = ConfigureMainCamera();

            // 6. Echo-7 3-Sector Military Base Environment + Extraction Blackhawk
            CampaignCutsceneProps campaignProps = BuildArenaEnvironment(
                matFloor, matWalls, matProps, matTrim, matAccentEmissive, matHealthGlow,
                matPlayerPrimary, matContainerRust, matFuelSteel, meshes);

            // 7. Player Rig (Modern Tactical Operator + 4 Real-World Weapons)
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            PlayerController player = BuildPlayerRig(inputAsset, matPlayerPrimary, matPlayerSecondary, matAccentEmissive, matCyanGlow, matHealthGlow, meshes);

            // 8. Cinemachine Top-Down Rig
            TopDownCameraRig cameraRig = BuildCameraRig(player.transform, mainCam);

            // 9. GameSystems & UI Canvas (SaveManager first before GameManager)
            GameObject systemsRoot = new GameObject("GameSystems");
            Transform projPoolRoot = new GameObject("ProjectilePoolRoot").transform;
            projPoolRoot.SetParent(systemsRoot.transform, false);
            player.Weapon.BindReferences(player.transform.Find("VisualRoot/WeaponAnchor/MuzzlePoint"), player.Motion, projPoolRoot);

            Transform vfxPoolRoot = new GameObject("VFXPoolRoot").transform;
            vfxPoolRoot.SetParent(systemsRoot.transform, false);
            Transform textRoot = new GameObject("FloatingTextRoot").transform;
            textRoot.SetParent(systemsRoot.transform, false);

            systemsRoot.AddComponent<SaveManager>();
            systemsRoot.AddComponent<AudioManager>();
            VFXManager vfx = systemsRoot.AddComponent<VFXManager>();
            vfx.BindPoolRoot(vfxPoolRoot);

            CinemachineImpulseSource impulse = systemsRoot.AddComponent<CinemachineImpulseSource>();
            FeelManager feel = systemsRoot.AddComponent<FeelManager>();
            feel.BindComponents(impulse, textRoot);

            EnemySpawner spawner = systemsRoot.AddComponent<EnemySpawner>();
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
                meshes.PickupCreditCoin,
                meshes.EnemyArm,
                meshes.PlayerUpperArm,
                meshes.PlayerForearm,
                meshes.WeaponAK74,
                meshes.WeaponPKM);

            UIManager ui = BuildGameCanvasAndUI(systemsRoot, solidSprite, roundedSprite, shieldSprite, crosshairSprite, circleSprite);
            CutsceneDirector cutscene = systemsRoot.AddComponent<CutsceneDirector>();
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
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[SceneSetupBuilder] Successfully rebuilt Store Edition 2.0 SampleScene.");
        }

        private static VanguardMeshPack LoadVanguardMeshes()
        {
            VanguardMeshPack pack = new VanguardMeshPack();
            if (!File.Exists(FbxAssetPath))
            {
                Debug.LogWarning("[SceneSetupBuilder] Custom FBX not found at " + FbxAssetPath + ". Falling back to primitives.");
                return pack;
            }

            AssetDatabase.ImportAsset(FbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(FbxAssetPath) as ModelImporter;
            if (importer != null && (importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable || Mathf.Abs(importer.globalScale - 1f) > 0.001f))
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.globalScale = 1f;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FbxAssetPath);
            Dictionary<string, Mesh> byName = new Dictionary<string, Mesh>();
            foreach (Object obj in assets)
            {
                if (obj is Mesh m)
                {
                    byName[m.name] = m;
                }
            }

            Mesh Get(string key) => byName.TryGetValue(key, out Mesh m) ? m : null;

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

            pack.TacticalCrate = Get("Mesh_TacticalCrate");
            pack.EnergyPylon = Get("Mesh_EnergyPylon");
            pack.EnvShippingContainer = Get("Mesh_Env_ShippingContainer");
            pack.EnvFuelTank = Get("Mesh_Env_FuelTank");
            pack.EnvBunker = Get("Mesh_Env_Bunker");
            pack.EnvRadarDish = Get("Mesh_Env_RadarDish");
            pack.VehicleBlackhawk = Get("Mesh_Vehicle_Blackhawk");
            pack.VehicleBlackhawkRotor = Get("Mesh_Vehicle_BlackhawkRotor");
            pack.PickupXpCrystal = Get("Mesh_Pickup_XpCrystal");
            pack.PickupMedkit = Get("Mesh_Pickup_Medkit");
            pack.PickupCreditCoin = Get("Mesh_Pickup_CreditCoin");

            Debug.Log("[SceneSetupBuilder] Loaded " + byName.Count + " Blender FBX meshes from " + FbxAssetPath);
            return pack;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(TexturesDir)) AssetDatabase.CreateFolder("Assets", "Textures");
        }

        private static Texture2D SaveTextureAsset(Texture2D source, string assetPath, bool repeat)
        {
            byte[] png = source.EncodeToPNG();
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 4;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Sprite SaveSpriteAsset(Texture2D source, string assetPath)
        {
            byte[] png = source.EncodeToPNG();
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
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
            float emissionIntensity,
            Texture2D mainTex = null,
            Vector2? tiling = null)
        {
            string path = MaterialsDir + "/" + matName + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material generated = Palette.CreateLitMaterial(matName, baseColor, metallic, smoothness, emissive, emissionIntensity, mainTex);
            if (tiling.HasValue)
            {
                generated.mainTextureScale = tiling.Value;
                if (generated.HasProperty("_BaseMap"))
                {
                    generated.SetTextureScale("_BaseMap", tiling.Value);
                }
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

        private static Material SaveOrUpdateSkyboxMaterial(string matName)
        {
            string path = MaterialsDir + "/" + matName + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material generated = Palette.CreateSkyboxMaterial();
            generated.name = matName;

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

        private static VolumeProfile ConfigureVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            profile.components.RemoveAll(c => c == null);

            Tonemapping tonemapping = EnsureVolumeComponent<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            Bloom bloom = EnsureVolumeComponent<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.25f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.22f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.55f;
            bloom.tint.overrideState = true;
            bloom.tint.value = Palette.KeyLightWarm;

            Vignette vignette = EnsureVolumeComponent<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.20f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.46f;
            vignette.color.overrideState = true;
            vignette.color.value = Palette.Shadow;

            ColorAdjustments colorAdj = EnsureVolumeComponent<ColorAdjustments>(profile);
            colorAdj.active = true;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 4f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 12f;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.28f;

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

            for (int i = 0; i < generatedNames.Length; i++)
            {
                GameObject existing = GameObject.Find(generatedNames[i]);
                while (existing != null)
                {
                    Object.DestroyImmediate(existing);
                    existing = GameObject.Find(generatedNames[i]);
                }
            }
        }

        private static void ConfigureEnvironmentLighting(Material skybox, VolumeProfile profile)
        {
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.AmbientSky;
            RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
            RenderSettings.ambientGroundColor = Palette.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Palette.SkyboxHorizon;
            RenderSettings.fogStartDistance = 52f;
            RenderSettings.fogEndDistance = 158f;

            GameObject keyLightGo = GameObject.Find("Directional Light");
            if (keyLightGo == null)
            {
                keyLightGo = new GameObject("Directional Light");
                keyLightGo.AddComponent<Light>();
            }
            keyLightGo.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            Light keyLight = keyLightGo.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Palette.KeyLightWarm;
            keyLight.intensity = 1.68f;
            keyLight.shadows = LightShadows.Soft;

            GameObject rimLightGo = new GameObject("DirectionalFillLight");
            rimLightGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
            Light rimLight = rimLightGo.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = Palette.AmbientSky;
            rimLight.intensity = 0.72f;
            rimLight.shadows = LightShadows.None;

            GameObject volGo = GameObject.Find("Global Volume");
            if (volGo == null)
            {
                volGo = new GameObject("Global Volume");
            }
            Volume volume = volGo.GetComponent<Volume>();
            if (volume == null) volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
        }

        private static UnityEngine.Camera ConfigureMainCamera()
        {
            UnityEngine.Camera mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
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

            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain == null) brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
            brain.IgnoreTimeScale = false;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.4f);

            return mainCam;
        }

        private static CampaignCutsceneProps BuildArenaEnvironment(
            Material matFloor,
            Material matWalls,
            Material matProps,
            Material matTrim,
            Material matAccent,
            Material matCyan,
            Material matOlive,
            Material matRust,
            Material matFuelSteel,
            VanguardMeshPack meshes)
        {
            CampaignCutsceneProps props = new CampaignCutsceneProps();
            GameObject env = new GameObject("ArenaEnvironment");

            // Huge 3-Sector Echo-7 Tarmac Compound Floor (76m x 108m: x[-38..38], z[-52..56])
            GameObject floorRoot = new GameObject("ArenaFloor");
            floorRoot.transform.SetParent(env.transform, false);
            BoxCollider floorCol = floorRoot.AddComponent<BoxCollider>();
            floorCol.size = new Vector3(76f, 0.4f, 108f);
            floorCol.center = new Vector3(0f, -0.2f, 2f);

            GameObject floorVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorVisual.name = "FloorDeck";
            floorVisual.transform.SetParent(floorRoot.transform, false);
            floorVisual.transform.localPosition = new Vector3(0f, -0.2f, 2f);
            floorVisual.transform.localScale = new Vector3(76f, 0.4f, 108f);
            Object.DestroyImmediate(floorVisual.GetComponent<Collider>());
            floorVisual.GetComponent<MeshRenderer>().sharedMaterial = matFloor;

            GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Cube);
            apron.name = "OuterFoundation";
            apron.transform.SetParent(env.transform, false);
            apron.transform.localPosition = new Vector3(0f, -0.55f, 2f);
            apron.transform.localScale = new Vector3(116f, 0.4f, 148f);
            Object.DestroyImmediate(apron.GetComponent<Collider>());
            apron.GetComponent<MeshRenderer>().sharedMaterial = matTrim;

            // Central Convoy Asphalt Road Strip & Hazard Road Markings (South Gate -> Helipad)
            BuildRoadDeck(env.transform, matTrim, matAccent);

            // Outer Perimeter Blast Walls framing 3 stepped Sectors
            BuildPerimeterWall(env.transform, "Wall_South", new Vector3(0f, 0f, -52f), new Vector3(57.4f, 2.4f, 1.4f), matWalls, matTrim, matAccent);
            BuildPerimeterWall(env.transform, "Wall_North", new Vector3(0f, 0f, 56f), new Vector3(61.4f, 2.6f, 1.4f), matWalls, matTrim, matAccent);
            BuildPerimeterWall(env.transform, "Wall_S1_West", new Vector3(-28f, 0f, -33f), new Vector3(1.4f, 2.4f, 38.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S1_East", new Vector3(28f, 0f, -33f), new Vector3(1.4f, 2.4f, 38.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S1_WingWest", new Vector3(-33f, 0f, -14f), new Vector3(11.4f, 2.4f, 1.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S1_WingEast", new Vector3(33f, 0f, -14f), new Vector3(11.4f, 2.4f, 1.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S2_West", new Vector3(-38f, 0f, 4f), new Vector3(1.4f, 2.4f, 36.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S2_East", new Vector3(38f, 0f, 4f), new Vector3(1.4f, 2.4f, 36.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S2_WingWest", new Vector3(-34f, 0f, 22f), new Vector3(9.4f, 2.4f, 1.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S2_WingEast", new Vector3(34f, 0f, 22f), new Vector3(9.4f, 2.4f, 1.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S3_West", new Vector3(-30f, 0f, 39f), new Vector3(1.4f, 2.6f, 34.4f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(env.transform, "Wall_S3_East", new Vector3(30f, 0f, 39f), new Vector3(1.4f, 2.6f, 34.4f), matWalls, matTrim, matTrim);

            // Sector Divider Checkpoints + Sliding Blast Gates
            BuildBlastGateDivider(
                env.transform, "GateDivider_Act1", -14f, 56f, 12f,
                matWalls, matTrim, matAccent,
                out props.Gate1Left, out props.Gate1Right);
            BuildBlastGateDivider(
                env.transform, "GateDivider_Act2", 22f, 60f, 14f,
                matWalls, matTrim, matAccent,
                out props.Gate2Left, out props.Gate2Right);

            // SECTOR 1 (z: -52..-14): South Insertion Checkpoint & Convoy Yard
            BuildEnvPropMesh(env.transform, "S1_Bunker_West", meshes.EnvBunker, new Vector3(-18.5f, 0f, -24.5f), 18f, new Vector3(5.8f, 3.3f, 4.6f), matWalls);
            BuildEnvPropMesh(env.transform, "S1_Container_SW", meshes.EnvShippingContainer, new Vector3(-14.5f, 0f, -40f), 14f, new Vector3(2.55f, 2.6f, 6.0f), matOlive);
            BuildEnvPropMesh(env.transform, "S1_Container_SE", meshes.EnvShippingContainer, new Vector3(15.5f, 0f, -38f), -12f, new Vector3(2.55f, 2.6f, 6.0f), matRust);
            BuildEnvPropMesh(env.transform, "S1_Container_NE", meshes.EnvShippingContainer, new Vector3(17.5f, 0f, -22.5f), 86f, new Vector3(2.55f, 2.6f, 6.0f), matOlive);

            BuildTacticalCover(env.transform, "S1_Cover_SW", new Vector3(-5.8f, 0f, -38.5f), 12f, new Vector3(3.2f, 1.25f, 1.1f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "S1_Cover_SE", new Vector3(5.8f, 0f, -38.5f), -12f, new Vector3(3.2f, 1.25f, 1.1f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "S1_Cover_MidW", new Vector3(-7.8f, 0f, -28.5f), -18f, new Vector3(3.5f, 1.3f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "S1_Cover_MidE", new Vector3(7.8f, 0f, -28.5f), 18f, new Vector3(3.5f, 1.3f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildCornerPylon(env.transform, "Pylon_S1_SW", new Vector3(-24.5f, 0f, -48.5f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_S1_SE", new Vector3(24.5f, 0f, -48.5f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);

            // SECTOR 2 (z: -14..+22): Central Fuel Tank Farm & Cargo Container Depot
            // Note: Keeps (0, 0, 0), (0, 0, 6), and (10, 0, 10) completely unobstructed!
            BuildTacticalCover(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildCornerPylon(env.transform, "Pylon_NW", new Vector3(-11.8f, 0f, 11.8f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_NE", new Vector3(11.8f, 0f, 11.8f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);

            // West Fuel Farm Silos & Radar Array
            BuildEnvPropMesh(env.transform, "S2_FuelTank_1", meshes.EnvFuelTank, new Vector3(-25.5f, 0f, -6.0f), 0f, new Vector3(3.4f, 4.6f, 3.4f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S2_FuelTank_2", meshes.EnvFuelTank, new Vector3(-25.5f, 0f, 3.5f), 90f, new Vector3(3.4f, 4.6f, 3.4f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S2_FuelTank_3", meshes.EnvFuelTank, new Vector3(-19.5f, 0f, -1.2f), 180f, new Vector3(3.4f, 4.6f, 3.4f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S2_FuelTank_4", meshes.EnvFuelTank, new Vector3(-25.5f, 0f, 13.0f), 45f, new Vector3(3.4f, 4.6f, 3.4f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S2_Radar_West", meshes.EnvRadarDish, new Vector3(-32.5f, 0f, 14.5f), -35f, new Vector3(2.7f, 4.6f, 2.7f), matFuelSteel);

            // East ISO Cargo Container Yard (with iconic 2-story stacked container)
            BuildEnvPropMesh(env.transform, "S2_Container_E1", meshes.EnvShippingContainer, new Vector3(18.5f, 0f, -5.5f), 0f, new Vector3(2.55f, 2.6f, 6.0f), matOlive);
            BuildEnvPropMesh(env.transform, "S2_Container_E2", meshes.EnvShippingContainer, new Vector3(23.0f, 0f, -5.5f), 0f, new Vector3(2.55f, 2.6f, 6.0f), matRust);
            BuildEnvPropMesh(env.transform, "S2_Container_E2_Top", meshes.EnvShippingContainer, new Vector3(23.0f, 2.6f, -5.5f), 4f, new Vector3(2.55f, 2.6f, 6.0f), matProps);
            BuildEnvPropMesh(env.transform, "S2_Container_E3", meshes.EnvShippingContainer, new Vector3(21.5f, 0f, 6.0f), 90f, new Vector3(2.55f, 2.6f, 6.0f), matOlive);
            BuildEnvPropMesh(env.transform, "S2_Container_E4", meshes.EnvShippingContainer, new Vector3(30.5f, 0f, 12.0f), -16f, new Vector3(2.55f, 2.6f, 6.0f), matRust);

            // SECTOR 3 (z: +22..+56): North Command HQ, Bunkers & EVAC-6 Helipad
            BuildHelipadMarkings(env.transform, new Vector3(0f, 0.015f, 42f), matTrim, matAccent);
            BuildEnvPropMesh(env.transform, "S3_Bunker_West", meshes.EnvBunker, new Vector3(-19.5f, 0f, 40.5f), -22f, new Vector3(5.8f, 3.3f, 4.6f), matWalls);
            BuildEnvPropMesh(env.transform, "S3_Bunker_East", meshes.EnvBunker, new Vector3(19.5f, 0f, 40.5f), 22f, new Vector3(5.8f, 3.3f, 4.6f), matWalls);
            BuildEnvPropMesh(env.transform, "S3_Radar_NE", meshes.EnvRadarDish, new Vector3(24.0f, 0f, 50.5f), -140f, new Vector3(2.7f, 4.6f, 2.7f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S3_FuelTank_NW", meshes.EnvFuelTank, new Vector3(-23.5f, 0f, 50.0f), 120f, new Vector3(3.4f, 4.6f, 3.4f), matFuelSteel);
            BuildEnvPropMesh(env.transform, "S3_Container_SW", meshes.EnvShippingContainer, new Vector3(-17.5f, 0f, 28.5f), 88f, new Vector3(2.55f, 2.6f, 6.0f), matOlive);
            BuildEnvPropMesh(env.transform, "S3_Container_SE", meshes.EnvShippingContainer, new Vector3(17.5f, 0f, 28.5f), -88f, new Vector3(2.55f, 2.6f, 6.0f), matRust);
            BuildCornerPylon(env.transform, "Pylon_S3_NW", new Vector3(-26.5f, 0f, 52.5f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_S3_NE", new Vector3(26.5f, 0f, 52.5f), matTrim, matWalls, matCyan, Palette.KeyLightWarm, meshes.EnergyPylon);

            // EVAC-6 Blackhawk Helicopter (parked sky-high inactive until Victory Evac Cutscene)
            BuildBlackhawkProp(env.transform, meshes.VehicleBlackhawk, meshes.VehicleBlackhawkRotor, matOlive, matTrim, out props.EvacChopperRoot, out props.EvacChopperRotor);

            return props;
        }

        private static void BuildRoadDeck(Transform parent, Material matAsphaltDark, Material matStripeYellow)
        {
            GameObject road = CreateVisualBox("ConvoyMainRoad", parent, new Vector3(0f, 0.005f, -5f), new Vector3(11.5f, 0.01f, 88f), matAsphaltDark);
            road.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (float z = -47f; z <= 31f; z += 5.5f)
            {
                GameObject dash = CreateVisualBox("RoadCenterDash_" + Mathf.RoundToInt(z), parent, new Vector3(0f, 0.012f, z), new Vector3(0.32f, 0.01f, 2.6f), matStripeYellow);
                dash.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void BuildBlastGateDivider(
            Transform parent,
            string gateName,
            float zPos,
            float totalWidth,
            float openingWidth,
            Material matWalls,
            Material matTrim,
            Material matAccent,
            out Transform leftLeafOut,
            out Transform rightLeafOut)
        {
            GameObject root = new GameObject(gateName);
            root.transform.SetParent(parent, false);

            float wingW = Mathf.Max(2f, (totalWidth - openingWidth) * 0.5f);
            float wingOffsetX = openingWidth * 0.5f + wingW * 0.5f;
            BuildPerimeterWall(root.transform, gateName + "_WingWest", new Vector3(-wingOffsetX, 0f, zPos), new Vector3(wingW, 2.6f, 1.5f), matWalls, matTrim, matTrim);
            BuildPerimeterWall(root.transform, gateName + "_WingEast", new Vector3(wingOffsetX, 0f, zPos), new Vector3(wingW, 2.6f, 1.5f), matWalls, matTrim, matTrim);

            // Overhead Gantry Bridge spanning the blast gate opening (visual only, non-blocking!)
            CreateVisualBox(gateName + "_GantryBeam", root.transform, new Vector3(0f, 3.25f, zPos), new Vector3(openingWidth + 2.4f, 0.65f, 1.6f), matTrim);
            CreateVisualBox(gateName + "_GantryHazard", root.transform, new Vector3(0f, 3.00f, zPos - 0.82f), new Vector3(openingWidth, 0.16f, 0.08f), matAccent);

            float leafW = openingWidth * 0.5f;
            leftLeafOut = CreateSlidingBlastLeaf(root.transform, gateName + "_LeafLeft", new Vector3(-leafW * 0.5f, 0f, zPos), new Vector3(leafW, 2.45f, 0.72f), matTrim, matAccent);
            rightLeafOut = CreateSlidingBlastLeaf(root.transform, gateName + "_LeafRight", new Vector3(leafW * 0.5f, 0f, zPos), new Vector3(leafW, 2.45f, 0.72f), matTrim, matAccent);
        }

        private static Transform CreateSlidingBlastLeaf(Transform parent, string leafName, Vector3 pos, Vector3 size, Material matDoor, Material matStripe)
        {
            GameObject leaf = new GameObject(leafName);
            leaf.transform.SetParent(parent, false);
            leaf.transform.position = pos;

            BoxCollider col = leaf.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            CreateVisualBox("SteelDoorPanel", leaf.transform, new Vector3(0f, size.y * 0.5f, 0f), size, matDoor);
            CreateVisualBox("DoorHazardBand", leaf.transform, new Vector3(0f, size.y * 0.52f, 0f), new Vector3(size.x * 0.96f, 0.22f, size.z + 0.06f), matStripe);
            return leaf.transform;
        }

        private static void BuildHelipadMarkings(Transform parent, Vector3 centerPos, Material matPadDark, Material matYellow)
        {
            GameObject pad = new GameObject("NorthHelipad_LZ");
            pad.transform.SetParent(parent, false);
            pad.transform.position = centerPos;

            GameObject deck = CreateVisualBox("HelipadConcretePad", pad.transform, Vector3.zero, new Vector3(15f, 0.02f, 15f), matPadDark);
            deck.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Bold yellow "H" marking + outer apron border strips (visual decals cleanly on y=0.02)
            CreateVisualBox("H_LeftLeg", pad.transform, new Vector3(-2.1f, 0.02f, 0f), new Vector3(0.9f, 0.015f, 6.2f), matYellow);
            CreateVisualBox("H_RightLeg", pad.transform, new Vector3(2.1f, 0.02f, 0f), new Vector3(0.9f, 0.015f, 6.2f), matYellow);
            CreateVisualBox("H_CrossBar", pad.transform, new Vector3(0f, 0.02f, 0f), new Vector3(4.2f, 0.015f, 1.0f), matYellow);

            CreateVisualBox("Border_N", pad.transform, new Vector3(0f, 0.02f, 6.8f), new Vector3(13.6f, 0.015f, 0.45f), matYellow);
            CreateVisualBox("Border_S", pad.transform, new Vector3(0f, 0.02f, -6.8f), new Vector3(13.6f, 0.015f, 0.45f), matYellow);
            CreateVisualBox("Border_W", pad.transform, new Vector3(-6.8f, 0.02f, 0f), new Vector3(0.45f, 0.015f, 13.6f), matYellow);
            CreateVisualBox("Border_E", pad.transform, new Vector3(6.8f, 0.02f, 0f), new Vector3(0.45f, 0.015f, 13.6f), matYellow);
        }

        private static void BuildEnvPropMesh(
            Transform parent,
            string propName,
            Mesh mesh,
            Vector3 worldPos,
            float yawDeg,
            Vector3 colliderSize,
            Material mat)
        {
            GameObject root = new GameObject(propName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yawDeg, 0f));

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = colliderSize;
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);

            if (mesh != null)
            {
                GameObject vis = new GameObject("VisualMesh");
                vis.transform.SetParent(root.transform, false);
                MeshFilter mf = vis.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                MeshRenderer mr = vis.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
            }
            else
            {
                CreateVisualBox("FallbackBlock", root.transform, new Vector3(0f, colliderSize.y * 0.5f, 0f), colliderSize, mat);
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
            GameObject chopper = new GameObject("EVAC6_Blackhawk");
            chopper.transform.SetParent(parent, false);
            chopper.transform.SetPositionAndRotation(new Vector3(0f, 28f, 66f), Quaternion.Euler(0f, 180f, 0f));

            if (hullMesh != null)
            {
                GameObject hull = new GameObject("BlackhawkHull");
                hull.transform.SetParent(chopper.transform, false);
                hull.AddComponent<MeshFilter>().sharedMesh = hullMesh;
                hull.AddComponent<MeshRenderer>().sharedMaterial = matHull;
            }
            else
            {
                CreateVisualBox("BlackhawkHullFallback", chopper.transform, new Vector3(0f, 1.4f, 0f), new Vector3(2.8f, 2.2f, 9.5f), matHull);
            }

            GameObject rotor = new GameObject("MainRotor");
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

        private static void BuildPerimeterWall(Transform parent, string wallName, Vector3 pos, Vector3 size, Material matWall, Material matTrim, Material matGlow)
        {
            GameObject root = new GameObject(wallName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            CreateVisualBox("MainBlock", root.transform, new Vector3(0f, size.y * 0.5f, 0f), size, matWall);
            CreateVisualBox("TopCap", root.transform, new Vector3(0f, size.y + 0.08f, 0f), new Vector3(size.x + 0.14f, 0.16f, size.z + 0.14f), matTrim);
            CreateVisualBox("WarningTrim", root.transform, new Vector3(0f, size.y * 0.78f, 0f), new Vector3(size.x + 0.04f, 0.07f, size.z + 0.04f), matGlow);
        }

        private static void BuildTacticalCover(Transform parent, string coverName, Vector3 pos, float yawDeg, Vector3 size, Material matBody, Material matTrim, Material matGlow, Mesh crateMesh)
        {
            GameObject root = new GameObject(coverName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            if (crateMesh != null)
            {
                CreateVisualMeshFit("TacticalCrateMesh", crateMesh, PrimitiveType.Cube, root.transform, new Vector3(0f, size.y * 0.5f, 0f), size, matBody);
                CreateVisualBox("BasePlinth", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(size.x + 0.12f, 0.14f, size.z + 0.10f), matTrim);
                CreateVisualBox("HazardStencil", root.transform, new Vector3(0f, size.y * 0.86f, 0f), new Vector3(size.x * 0.88f, 0.06f, size.z * 0.48f), matGlow);
            }
            else
            {
                CreateVisualBox("BasePlinth", root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(size.x + 0.18f, 0.28f, size.z + 0.18f), matTrim);
                CreateVisualBox("ArmorCore", root.transform, new Vector3(0f, size.y * 0.52f, 0f), new Vector3(size.x, size.y * 0.76f, size.z), matBody);
                CreateVisualBox("TopCap", root.transform, new Vector3(0f, size.y + 0.05f, 0f), new Vector3(size.x * 0.86f, 0.14f, size.z * 0.82f), matTrim);
                CreateVisualBox("HazardStencil", root.transform, new Vector3(0f, size.y * 0.78f, 0f), new Vector3(size.x + 0.04f, 0.07f, size.z + 0.04f), matGlow);
            }
        }

        private static void BuildCornerPylon(Transform parent, string pylonName, Vector3 pos, Material matBody, Material matTrim, Material matGlow, Color lightColor, Mesh pylonMesh)
        {
            GameObject root = new GameObject(pylonName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.1f, 3.2f, 1.1f);
            col.center = new Vector3(0f, 1.6f, 0f);

            if (pylonMesh != null)
            {
                CreateVisualMeshFit("EnergyPylonMesh", pylonMesh, PrimitiveType.Cube, root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(1.25f, 3.2f, 1.25f), matBody);
                CreateVisualBox("FloodlightBank", root.transform, new Vector3(0f, 3.06f, 0f), new Vector3(0.74f, 0.18f, 0.54f), matGlow);
            }
            else
            {
                CreateVisualBox("Base", root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 1.3f), matTrim);
                CreateVisualBox("Shaft", root.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.85f, 2.2f, 0.85f), matBody);
                CreateVisualBox("FloodlightBank", root.transform, new Vector3(0f, 2.85f, 0f), new Vector3(0.85f, 0.30f, 0.85f), matGlow);
                CreateVisualBox("Cap", root.transform, new Vector3(0f, 3.1f, 0f), new Vector3(1.1f, 0.22f, 1.1f), matTrim);
            }

            GameObject lightGo = new GameObject("PylonLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 3.05f, 0f);
            Light pt = lightGo.AddComponent<Light>();
            pt.type = LightType.Point;
            pt.color = lightColor;
            pt.intensity = 3.2f;
            pt.range = 12f;
            pt.shadows = LightShadows.None;
        }

        private static GameObject CreateVisualBox(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
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
            GameObject go = GameObject.CreatePrimitive(type);
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

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            Vector3 bSize = mesh.bounds.size;
            Vector3 fitScale;
            if (uniformAspect)
            {
                float maxTarget = Mathf.Max(targetSize.x, Mathf.Max(targetSize.y, targetSize.z));
                float maxMesh = Mathf.Max(0.01f, Mathf.Max(bSize.x, Mathf.Max(bSize.y, bSize.z)));
                float s = maxTarget / maxMesh;
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
            Material matAccent,
            Material matCyan,
            Material matHealth,
            VanguardMeshPack meshes)
        {
            GameObject root = new GameObject("Player");
            root.tag = "Player";
            root.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, -43f), Quaternion.identity);

            CharacterController cc = root.AddComponent<CharacterController>();
            cc.radius = 0.45f;
            cc.height = 1.76f;
            cc.center = new Vector3(0f, 0.88f, 0f);
            cc.stepOffset = 0.25f;

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.88f;
            trigger.center = new Vector3(0f, 0.88f, 0f);

            ProceduralCharacterMotion motion = root.AddComponent<ProceduralCharacterMotion>();
            WeaponController weapon = root.AddComponent<WeaponController>();
            PlayerController player = root.AddComponent<PlayerController>();

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            // Operator Plate Carrier & Combat Torso (Blender custom meshes)
            Material matWeaponSteel = SaveOrUpdateLitMaterial("Mat_WeaponSteel", Palette.Gunmetal, 0.78f, 0.56f, false, 0f);
            GameObject torsoGo = CreateVisualMeshFit("Torso", meshes.PlayerTorso, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.58f, 0.58f, 0.38f), matPrimary);
            CreateVisualMeshFit("ChestRig", meshes.PlayerChestRig, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.11f, 0.04f), new Vector3(0.62f, 0.36f, 0.34f), matSecondary);

            // FAST Tactical Helmet & NVG Goggles
            GameObject headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.48f, 0.02f);
            GameObject helmetGo = CreateVisualMeshFit("Helmet", meshes.PlayerHelmet, PrimitiveType.Cube, headPivot.transform, Vector3.zero, new Vector3(0.42f, 0.36f, 0.44f), matPrimary);
            CreateVisualMeshFit("TacticalVisor", meshes.PlayerVisor, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.36f, 0.12f, 0.14f), matCyan);
            CreateVisualPrimitive("Antenna", PrimitiveType.Cylinder, headPivot.transform, new Vector3(0.18f, 0.14f, -0.10f), new Vector3(0.018f, 0.10f, 0.018f), matWeaponSteel);

            // Articulated Two-Bone Tactical Arms + Gloved Hands (UpperArm 0.34m + Forearm/Glove 0.35m)
            Transform leftArmPivot = BuildOperatorArm(
                "Left", visualRoot.transform, new Vector3(-0.28f, 1.24f, 0.07f),
                meshes.PlayerUpperArm, meshes.PlayerForearm, matPrimary, matWeaponSteel,
                out Transform leftElbowPivot, out MeshRenderer leftUpperRenderer, out MeshRenderer leftForearmRenderer);

            Transform rightArmPivot = BuildOperatorArm(
                "Right", visualRoot.transform, new Vector3(0.30f, 1.24f, -0.02f),
                meshes.PlayerUpperArm, meshes.PlayerForearm, matPrimary, matWeaponSteel,
                out Transform rightElbowPivot, out MeshRenderer rightUpperRenderer, out MeshRenderer rightForearmRenderer);

            // 6 Switchable Modern Military Firearms mounted dead-straight under WeaponAnchor with exact grip & muzzle nodes
            GameObject weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.11f, 1.18f, 0.43f);

            GameObject[] weaponVisuals = new GameObject[SaveManager.WeaponCount];
            Transform[] weaponLeftGrips = new Transform[SaveManager.WeaponCount];
            Transform[] weaponRightGrips = new Transform[SaveManager.WeaponCount];
            Transform[] weaponMuzzleTips = new Transform[SaveManager.WeaponCount];

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

            GameObject muzzlePoint = new GameObject("MuzzlePoint");
            muzzlePoint.transform.SetParent(weaponAnchor.transform, false);
            muzzlePoint.transform.SetPositionAndRotation(weaponMuzzleTips[0].position, weaponMuzzleTips[0].rotation);

            // Legs
            GameObject leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.15f, 0.74f, 0f);
            CreateVisualMeshFit("LeftLeg", meshes.PlayerLimb, PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            GameObject rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.15f, 0.74f, 0f);
            CreateVisualMeshFit("RightLeg", meshes.PlayerLimb, PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            // Tactical Field Rucksack (secondary lag pivot - zero thrusters)
            GameObject secondaryPivot = new GameObject("SecondaryPivot");
            secondaryPivot.transform.SetParent(visualRoot.transform, false);
            secondaryPivot.transform.localPosition = new Vector3(0f, 1.10f, -0.24f);
            CreateVisualMeshFit("JetpackBody", meshes.PlayerJetpack, PrimitiveType.Cube, secondaryPivot.transform, Vector3.zero, new Vector3(0.38f, 0.44f, 0.22f), matSecondary);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot, rightArmPivot, leftLegPivot.transform, rightLegPivot.transform, secondaryPivot.transform);
            motion.BindWeaponArmIk(leftElbowPivot, rightElbowPivot, weaponAnchor.transform, weaponLeftGrips[0], weaponRightGrips[0], 0.34f, 0.35f);
            weapon.BindReferences(muzzlePoint.transform, motion, null);
            weapon.BindWeaponVisuals(weaponVisuals);
            weapon.BindWeaponGrips(weaponLeftGrips, weaponRightGrips, weaponMuzzleTips);

            MeshRenderer[] primaryArmorParts = new[]
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
            GameObject armPivot = new GameObject(side + "ArmPivot");
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

            GameObject elbowPivot = new GameObject(side + "ElbowPivot");
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
            GameObject container = new GameObject(name);
            container.transform.SetParent(anchor, false);
            container.transform.localPosition = localOffset;
            container.transform.localRotation = Quaternion.Euler(localEuler);

            if (weaponMesh != null)
            {
                GameObject body = new GameObject("MeshBody");
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

            GameObject leftGrip = new GameObject("LeftHandGrip");
            leftGrip.transform.SetParent(container.transform, false);
            leftGrip.transform.localPosition = leftGripLocal;
            leftGripOut = leftGrip.transform;

            GameObject rightGrip = new GameObject("RightHandGrip");
            rightGrip.transform.SetParent(container.transform, false);
            rightGrip.transform.localPosition = rightGripLocal;
            rightGripOut = rightGrip.transform;

            GameObject muzzleTip = new GameObject("MuzzleTip");
            muzzleTip.transform.SetParent(container.transform, false);
            muzzleTip.transform.localPosition = muzzleLocal;
            muzzleOut = muzzleTip.transform;

            return container;
        }

        private static TopDownCameraRig BuildCameraRig(Transform playerTransform, UnityEngine.Camera mainCam)
        {
            GameObject proxyGo = new GameObject("CameraFollowProxy");
            proxyGo.transform.position = playerTransform.position + Vector3.up * 0.9f;

            GameObject vcamGo = new GameObject("TopDownVirtualCamera");
            vcamGo.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);

            CinemachineCamera vcam = vcamGo.AddComponent<CinemachineCamera>();
            CinemachineFollow follow = vcamGo.AddComponent<CinemachineFollow>();
            CinemachineImpulseListener listener = vcamGo.AddComponent<CinemachineImpulseListener>();
            TopDownCameraRig rig = vcamGo.AddComponent<TopDownCameraRig>();
            rig.BindComponents(vcam, follow, listener, proxyGo.transform, playerTransform);

            return rig;
        }

        private static UIManager BuildGameCanvasAndUI(
            GameObject systemsRoot,
            Sprite solidSprite,
            Sprite roundedSprite,
            Sprite shieldSprite,
            Sprite crosshairSprite,
            Sprite circleSprite)
        {
            // EventSystem using New Input System UI Module
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // Root Canvas
            GameObject canvasGo = new GameObject("GameCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 1. HUD PANEL
            CanvasGroup hudGroup = CreateFullScreenGroup("HUDPanel", canvasGo.transform);

            // Top-Left Status Card (HP + Dash)
            RectTransform hpCard = CreatePanel("StatusCard_Left", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(360f, 104f), roundedSprite, Palette.PanelSurface);
            CreateImage("CardAccentTop", hpCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);
            CreateIcon("ShieldIcon", hpCard, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(44f, 44f), shieldSprite, Palette.HealthGreen);

            TextMeshProUGUI hpLabel = CreateStyledText("HPLabel", hpCard, "OPERATOR HP", 13f, FontStyles.Bold, 3.5f, Palette.MutedText, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(hpLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(74f, -14f), new Vector2(140f, 22f));

            TextMeshProUGUI hpValueText = CreateStyledText("HPValueText", hpCard, "100 / 100", 16f, FontStyles.Bold, 2f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(hpValueText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -13f), new Vector2(130f, 24f));

            Image hpBg = CreateImage("HPBarBg", hpCard, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 18f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(hpBg.rectTransform, 74f, 16f, 4f, 18f);

            Image hpFill = CreateFilledBar("HPBarFill", hpBg.transform, roundedSprite, Palette.HealthGreen);

            TextMeshProUGUI dashLabel = CreateStyledText("DashLabel", hpCard, "SPRINT [SHIFT]", 11f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(dashLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(74f, 14f), new Vector2(150f, 18f));

            Image dashBg = CreateImage("DashBarBg", hpCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 7f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(dashBg.rectTransform, 225f, 16f, -31f, 7f);
            Image dashFill = CreateFilledBar("DashBarFill", dashBg.transform, roundedSprite, Palette.Accent);

            // Top-Left Credits Pill (below HP card)
            RectTransform crPill = CreatePanel("CreditsPill_Left", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -136f), new Vector2(200f, 34f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            CreateImage("CreditsAccentLeft", crPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 20f), solidSprite, Palette.CreditGold, false);
            TextMeshProUGUI hudCreditsText = CreateStyledText("HUD_CreditsText", crPill, "200 CR", 14f, FontStyles.Bold, 2.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(hudCreditsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(10f, 0f), new Vector2(-24f, 28f));

            // Top-Left Non-Blocking TAC-NET Radio Comms Box (below Credits pill)
            RectTransform radioRt = CreatePanel("RadioCommsBox", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -182f), new Vector2(410f, 98f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            Image radioBgImg = radioRt.GetComponent<Image>();
            if (radioBgImg != null) radioBgImg.raycastTarget = false;
            CanvasGroup radioCg = radioRt.gameObject.AddComponent<CanvasGroup>();
            radioCg.alpha = 0f;
            radioCg.interactable = false;
            radioCg.blocksRaycasts = false;
            Image radioStripe = CreateImage("RadioAccentStripe", radioRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, 70f), solidSprite, Palette.Accent, false);
            TextMeshProUGUI radioCallsignTxt = CreateStyledText("RadioCallsignText", radioRt, "OVERWATCH // TAC-NET CH-04", 12.5f, FontStyles.Bold, 2.2f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(radioCallsignTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(376f, 22f));
            TextMeshProUGUI radioBodyTxt = CreateStyledText("RadioBodyText", radioRt, "", 13f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            radioBodyTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(radioBodyTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -34f), new Vector2(376f, 56f));

            // Top-Center Wave & Score Pill
            RectTransform centerPill = CreatePanel("StatusPill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 74f), roundedSprite, Palette.PanelSurface);
            CreateImage("PillAccent", centerPill, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI waveText = CreateStyledText("WaveText", centerPill, "WAVE 01", 22f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(waveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(150f, 28f));

            TextMeshProUGUI enemiesText = CreateStyledText("EnemiesText", centerPill, "8 HOSTILES", 13f, FontStyles.Bold, 2.5f, Palette.EnemySecondary, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(enemiesText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 12f), new Vector2(160f, 22f));

            TextMeshProUGUI scoreText = CreateStyledText("ScoreText", centerPill, "0 PTS", 22f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(165f, 44f));

            // Top-Center Objective Banner + Boss Bar below StatusPill_Center
            RectTransform objPill = CreatePanel("ObjectivePill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(460f, 30f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.97f));
            CreateImage("ObjAccentLeft", objPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, 18f), solidSprite, Palette.Accent, false);
            TextMeshProUGUI objectiveText = CreateStyledText("ObjectiveBannerText", objPill, "OBJ I // BREACH SOUTH CHECKPOINT", 11.5f, FontStyles.Bold, 2.4f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(objectiveText.rectTransform);

            // Boss Encounter Health Bar (anchored cleanly beneath Objective Pill)
            RectTransform bossRoot = CreatePanel("BossBarRoot", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(540f, 56f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f));
            CreateImage("BossBarAccent", bossRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.EnemySecondary, false);

            TextMeshProUGUI bossNameText = CreateStyledText("BossNameText", bossRoot, "ARMORED IFV // IRONCLAD", 13f, FontStyles.Bold, 3.5f, Palette.EnemySecondary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(bossNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(300f, 20f));

            TextMeshProUGUI bossHpText = CreateStyledText("BossHpText", bossRoot, "1200 / 1200", 13f, FontStyles.Bold, 2f, Palette.CharacterSecondary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(bossHpText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(160f, 20f));

            Image bossHpBg = CreateImage("BossHpBg", bossRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(bossHpBg.rectTransform, 18f, 18f, -14f, 12f);
            Image bossHpFill = CreateFilledBar("BossHpFill", bossHpBg.transform, roundedSprite, Palette.EnemySecondary);
            bossRoot.gameObject.SetActive(false);

            // Top-Right Rank/XP & Pause Card
            RectTransform rightCard = CreatePanel("StatusCard_Right", hudGroup.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(340f, 88f), roundedSprite, Palette.PanelSurface);
            CreateImage("RightAccentTop", rightCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI levelText = CreateStyledText("LevelText", rightCard, "RANK 01", 20f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(140f, 28f));

            Image xpBg = CreateImage("XPBarBg", rightCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 14f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(xpBg.rectTransform, 18f, 84f, -22f, 14f);
            Image xpFill = CreateFilledBar("XPBarFill", xpBg.transform, roundedSprite, Palette.Accent);
            xpFill.fillAmount = 0f;

            Button hudPauseBtn = CreateButton("HUD_PauseButton", rightCard, "| |", new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(56f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // Bottom-Center 6-Weapon Loadout Bar
            RectTransform weaponBar = CreatePanel("WeaponLoadoutBar", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(976f, 64f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.90f));
            CreateImage("WeaponBarAccent", weaponBar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-28f, 2f), solidSprite, Palette.Accent, false);

            Button[] wpnBtns = new Button[SaveManager.WeaponCount];
            Image[] wpnBgs = new Image[SaveManager.WeaponCount];
            TextMeshProUGUI[] wpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            string[] wpnSlotDefault = { "[1] M4A1 CARBINE", "[2] M870 SHOTGUN", "[3] MK14 SNIPER", "[4] RPG-7", "[5] AN-94 ABAKAN", "[6] VECTOR .45 SMG" };
            for (int i = 0; i < SaveManager.WeaponCount; i++)
            {
                float slotX = -395f + i * 158f;
                Button b = CreateButton("WeaponSlot_" + i, weaponBar, wpnSlotDefault[i], new Vector2(0.5f, 0.5f), new Vector2(slotX, 0f), new Vector2(150f, 44f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 11.2f);
                wpnBtns[i] = b;
                wpnBgs[i] = b.GetComponent<Image>();
                wpnLbls[i] = b.GetComponentInChildren<TextMeshProUGUI>();
                if (wpnLbls[i] != null) wpnLbls[i].characterSpacing = 1.6f;
            }

            // Mobile / Touch Controls (Shown strictly on mobile/handheld devices, hidden on PC)
            GameObject touchRoot = new GameObject("MobileTouchControls");
            touchRoot.transform.SetParent(hudGroup.transform, false);
            RectTransform touchRt = touchRoot.AddComponent<RectTransform>();
            StretchRect(touchRt);

            RectTransform moveBgRt = CreatePanel("MoveStickBg", touchRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            moveBgRt.pivot = new Vector2(0.5f, 0.5f);
            moveBgRt.anchoredPosition = new Vector2(138f, 138f);
            Image moveHandleImg = CreateImage("MoveStickHandle", moveBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.CharacterSecondary, 0.65f), false);
            VirtualJoystick leftStick = moveBgRt.gameObject.AddComponent<VirtualJoystick>();
            leftStick.BindRects(moveBgRt, moveHandleImg.rectTransform, 52f);

            RectTransform aimBgRt = CreatePanel("AimStickBg", touchRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            aimBgRt.pivot = new Vector2(0.5f, 0.5f);
            aimBgRt.anchoredPosition = new Vector2(-138f, 138f);
            Image aimHandleImg = CreateImage("AimStickHandle", aimBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.78f), false);
            VirtualJoystick rightStick = aimBgRt.gameObject.AddComponent<VirtualJoystick>();
            rightStick.BindRects(aimBgRt, aimHandleImg.rectTransform, 52f);

            Button touchDashBtn = CreateButton("TouchDashButton", touchRt, "SPRINT", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.30f), Palette.CharacterSecondary, 15f);
            RectTransform dashBtnRt = touchDashBtn.GetComponent<RectTransform>();
            dashBtnRt.anchorMin = dashBtnRt.anchorMax = new Vector2(1f, 0f);
            dashBtnRt.pivot = new Vector2(0.5f, 0.5f);
            dashBtnRt.anchoredPosition = new Vector2(-288f, 114f);

            // Transient Milestone Banner
            GameObject bannerGo = new GameObject("MilestoneBanner");
            bannerGo.transform.SetParent(hudGroup.transform, false);
            RectTransform bannerRt = bannerGo.AddComponent<RectTransform>();
            bannerRt.anchorMin = bannerRt.anchorMax = new Vector2(0.5f, 0.80f);
            bannerRt.sizeDelta = new Vector2(460f, 54f);
            CanvasGroup bannerCg = bannerGo.AddComponent<CanvasGroup>();
            bannerCg.alpha = 0f;
            bannerCg.blocksRaycasts = false;
            Image bannerBg = bannerGo.AddComponent<Image>();
            bannerBg.sprite = roundedSprite;
            bannerBg.color = Palette.WithAlpha(Palette.Shadow, 0.88f);
            bannerBg.raycastTarget = false;
            CreateImage("BannerStripe", bannerRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);
            TextMeshProUGUI milestoneTxt = CreateStyledText("MilestoneText", bannerRt, "RANK UP // SUPPLY DROP", 22f, FontStyles.Bold, 4f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(milestoneTxt.rectTransform);

            // Reticle Crosshair
            Image crosshairImg = CreateImage("Crosshair", hudGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f), crosshairSprite, Palette.WithAlpha(Palette.Accent, 0.9f), false);

            // 2. TITLE PANEL
            CanvasGroup titleGroup = CreateFullScreenGroup("TitlePanel", canvasGo.transform);
            CreateImage("TitleScrim", titleGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.Shadow, 0.36f), true);

            RectTransform titleCard = CreatePanel("TitleCard", titleGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 470f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.93f));
            CreateImage("TitleAccentTop", titleCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI gameTitle = CreateStyledText("GameTitleText", titleCard, "TASK FORCE // ZERO", 46f, FontStyles.Bold, 6f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(gameTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(680f, 70f));

            TextMeshProUGUI subtitle = CreateStyledText("SubtitleText", titleCard, "OPERATION IRON HARVEST // SECTOR ECHO-7", 15.5f, FontStyles.Bold, 4f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -114f), new Vector2(660f, 28f));

            TextMeshProUGUI titleBrief = CreateStyledText("TitleBriefText", titleCard, "Break Kronos PMC's blockade, flank PKM gunners, and destroy Col. Volkov's Wave 9 Ironclad IFV to clear Evac-6.", 13.5f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            titleBrief.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(titleBrief.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -152f), new Vector2(700f, 46f));

            TextMeshProUGUI titleBest = CreateStyledText("TitleHighScoreText", titleCard, "HIGH SCORE // 0", 16f, FontStyles.Bold, 3f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(titleBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(460f, 28f));

            TextMeshProUGUI titleCredits = CreateStyledText("TitleCreditsText", titleCard, "FUNDS // 200 CR", 16f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(titleCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(460f, 26f));

            TextMeshProUGUI controlsHint = CreateStyledText("ControlsHintText", titleCard, "WASD MOVE  ·  MOUSE AIM / FIRE  ·  1-6 WEAPONS  ·  SHIFT SPRINT  ·  ESC PAUSE", 11.5f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(controlsHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(700f, 24f));

            Button startBtn = CreateButton("StartMissionButton", titleCard, "DEPLOY", new Vector2(0.5f, 0.5f), new Vector2(-216f, -82f), new Vector2(204f, 58f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button armoryBtn = CreateButton("TitleArmoryButton", titleCard, "ARMORY", new Vector2(0.5f, 0.5f), new Vector2(0f, -82f), new Vector2(204f, 58f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);
            Button intelBtn = CreateButton("TitleIntelButton", titleCard, "INTEL DOSSIER", new Vector2(0.5f, 0.5f), new Vector2(216f, -82f), new Vector2(204f, 58f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);

            // 3. PAUSE PANEL
            CanvasGroup pauseGroup = CreateFullScreenGroup("PausePanel", canvasGo.transform);
            CreateImage("PauseScrim", pauseGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform pauseCard = CreatePanel("PauseCard", pauseGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 440f), roundedSprite, Palette.PanelSurface);
            CreateImage("PauseAccentTop", pauseCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 4f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI pauseHeader = CreateStyledText("PauseHeaderText", pauseCard, "PAUSED", 34f, FontStyles.Bold, 6f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(pauseHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(400f, 48f));

            Slider musicSlider = CreateLabeledSlider(pauseCard, "Music", "MUSIC", new Vector2(0f, 70f), roundedSprite, solidSprite, Palette.HealthGreen, out TextMeshProUGUI musicPct);
            Slider sfxSlider = CreateLabeledSlider(pauseCard, "SFX", "SFX", new Vector2(0f, 14f), roundedSprite, solidSprite, Palette.Accent, out TextMeshProUGUI sfxPct);

            Button resumeBtn = CreateButton("ResumeButton", pauseCard, "RESUME", new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(260f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button pRestartBtn = CreateButton("PauseRestartButton", pauseCard, "REDEPLOY", new Vector2(0.5f, 0.5f), new Vector2(0f, -118f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);
            Button pTitleBtn = CreateButton("PauseTitleButton", pauseCard, "ABORT TO HQ", new Vector2(0.5f, 0.5f), new Vector2(0f, -174f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);

            // 4. ENDED PANEL
            CanvasGroup endedGroup = CreateFullScreenGroup("EndedPanel", canvasGo.transform);
            CreateImage("EndedScrim", endedGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform endedCard = CreatePanel("EndedCard", endedGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 476f), roundedSprite, Palette.PanelSurface);
            Image endedAccent = CreateImage("EndedAccentTop", endedCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.EnemySecondary, false);

            TextMeshProUGUI endedTitle = CreateStyledText("EndedTitleText", endedCard, "K.I.A. // MISSION FAILED", 30f, FontStyles.Bold, 4.5f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(540f, 50f));

            TextMeshProUGUI endedScore = CreateStyledText("EndedScoreText", endedCard, "0 PTS", 44f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(endedScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 84f), new Vector2(460f, 56f));

            TextMeshProUGUI endedBest = CreateStyledText("EndedBestText", endedCard, "BEST // 0", 18f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(460f, 28f));

            TextMeshProUGUI endedStats = CreateStyledText("EndedStatsText", endedCard, "ELIMINATIONS: 0   |   WAVE REACHED: 1", 15f, FontStyles.Bold, 2f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(endedStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(500f, 24f));

            TextMeshProUGUI endedCredits = CreateStyledText("EndedCreditsText", endedCard, "EARNED +0 CR   |   FUNDS: 200 CR", 15f, FontStyles.Bold, 2f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(endedCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(520f, 24f));

            TextMeshProUGUI endedNarrative = CreateStyledText("EndedNarrativeText", endedCard, "", 13f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
            endedNarrative.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(endedNarrative.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(520f, 58f));

            Button eReplayBtn = CreateButton("EndedReplayButton", endedCard, "REDEPLOY", new Vector2(0.5f, 0.5f), new Vector2(-115f, -168f), new Vector2(205f, 54f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button eTitleBtn = CreateButton("EndedTitleButton", endedCard, "HQ MENU", new Vector2(0.5f, 0.5f), new Vector2(115f, -168f), new Vector2(205f, 54f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // 5. ARMORY & LOADOUT MODAL PANEL
            CanvasGroup armoryGroup = CreateFullScreenGroup("ArmoryPanel", canvasGo.transform);
            CreateImage("ArmoryScrim", armoryGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform armoryCard = CreatePanel("ArmoryCard", armoryGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 680f), roundedSprite, Palette.PanelSurface);
            CreateImage("ArmoryAccentTop", armoryCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 4f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI armoryHeader = CreateStyledText("ArmoryHeaderText", armoryCard, "TACTICAL ARMORY // FIELD LOADOUT", 23f, FontStyles.Bold, 3.5f, Palette.CharacterSecondary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(armoryHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -44f), new Vector2(620f, 44f));

            TextMeshProUGUI armoryCreditsText = CreateStyledText("ArmoryCreditsText", armoryCard, "200 CR", 22f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(armoryCreditsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -44f), new Vector2(280f, 44f));

            // Left Column: 4 Permanent Field Training Upgrades
            TextMeshProUGUI leftColHdr = CreateStyledText("UpgradesSectionTitle", armoryCard, "FIELD TRAINING & PERKS", 14f, FontStyles.Bold, 3.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(leftColHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, 212f), new Vector2(440f, 26f));

            string[] upgDescs = { "+20 MAX HP / RANK", "+10% BALLISTIC DMG / RANK", "+8% SPRINT SPD / RANK", "+15% FUNDS / RANK" };
            Button[] upgBtns = new Button[4];
            TextMeshProUGUI[] upgRankLbls = new TextMeshProUGUI[4];
            TextMeshProUGUI[] upgCostLbls = new TextMeshProUGUI[4];

            for (int i = 0; i < 4; i++)
            {
                float rowY = 152f - i * 94f;
                RectTransform row = CreatePanel("UpgradeRow_" + i, armoryCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, rowY), new Vector2(440f, 80f), roundedSprite, TrimBarBg());
                CreateImage("UpgRowStripe", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 50f), solidSprite, Palette.Accent, false);

                TextMeshProUGUI uRank = CreateStyledText("UpgRank_" + i, row, "UPGRADE   [LV 0/5]", 13f, FontStyles.Bold, 1f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
                SetAnchoredRect(uRank.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(280f, 22f));
                upgRankLbls[i] = uRank;

                TextMeshProUGUI uDesc = CreateStyledText("UpgSub_" + i, row, upgDescs[i], 12f, FontStyles.Bold, 1.5f, Palette.MutedText, TextAlignmentOptions.BottomLeft);
                SetAnchoredRect(uDesc.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 14f), new Vector2(275f, 20f));

                Button buyBtn = CreateButton("UpgradeBtn_" + i, row, "100 CR", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(128f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 15f);
                upgBtns[i] = buyBtn;
                upgCostLbls[i] = buyBtn.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Right Column Top: 6 Unlockable Firearms (2x3 Tactical Grid)
            TextMeshProUGUI rightWpnHdr = CreateStyledText("WeaponsSectionTitle", armoryCard, "WEAPONS ARSENAL", 14f, FontStyles.Bold, 3.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightWpnHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, 212f), new Vector2(440f, 26f));

            Button[] shopWpnBtns = new Button[SaveManager.WeaponCount];
            TextMeshProUGUI[] shopWpnLbls = new TextMeshProUGUI[SaveManager.WeaponCount];
            for (int i = 0; i < SaveManager.WeaponCount; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float wx = col == 0 ? 122f : 348f;
                float wy = 162f - row * 64f;
                Button wb = CreateButton("ShopWeapon_" + i, armoryCard, "WEAPON " + i, new Vector2(0.5f, 0.5f), new Vector2(wx, wy), new Vector2(214f, 54f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 11.5f);
                shopWpnBtns[i] = wb;
                shopWpnLbls[i] = wb.GetComponentInChildren<TextMeshProUGUI>();
                if (shopWpnLbls[i] != null) shopWpnLbls[i].characterSpacing = 1.6f;
            }

            // Right Column Bottom: 3 Tactical Uniform Camo Patterns
            TextMeshProUGUI rightSkinHdr = CreateStyledText("SkinsSectionTitle", armoryCard, "CAMO UNIFORMS", 14f, FontStyles.Bold, 3.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightSkinHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, -48f), new Vector2(440f, 26f));

            Button[] shopSkinBtns = new Button[3];
            TextMeshProUGUI[] shopSkinLbls = new TextMeshProUGUI[3];
            Color[] skinSwatches = { Palette.CharacterPrimary, Palette.SkinGoldPrimary, Palette.SkinCrimsonPrimary };
            for (int i = 0; i < 3; i++)
            {
                float sx = 85f + i * 150f;
                Button sb = CreateButton("ShopSkin_" + i, armoryCard, "CAMO " + i, new Vector2(0.5f, 0.5f), new Vector2(sx, -114f), new Vector2(138f, 70f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 12f);
                CreateImage("Swatch", sb.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(96f, 5f), solidSprite, skinSwatches[i], false);
                shopSkinBtns[i] = sb;
                shopSkinLbls[i] = sb.GetComponentInChildren<TextMeshProUGUI>();
            }

            Button armoryCloseBtn = CreateButton("ArmoryCloseButton", armoryCard, "BACK TO HQ", new Vector2(0.5f, 0.5f), new Vector2(0f, -292f), new Vector2(240f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 18f);

            // 6. FIELD PERK SUPPLY DROP MODAL PANEL
            CanvasGroup draftGroup = CreateFullScreenGroup("LevelUpDraftPanel", canvasGo.transform);
            CreateImage("DraftScrim", draftGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform draftRoot = CreatePanel("DraftContainer", draftGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 470f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f));
            CreateImage("DraftAccentTop", draftRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 5f), solidSprite, Palette.CardRareBorder, false);

            TextMeshProUGUI draftTitle = CreateStyledText("DraftHeaderTitle", draftRoot, "FIELD PERK SUPPLY DROP", 30f, FontStyles.Bold, 5.5f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(draftTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 42f));

            TextMeshProUGUI draftSub = CreateStyledText("DraftHeaderSub", draftRoot, "SELECT ONE TACTICAL PERK TO EQUIP", 13.5f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(draftSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(800f, 24f));

            Button[] drftBtns = new Button[3];
            TextMeshProUGUI[] drftTitles = new TextMeshProUGUI[3];
            TextMeshProUGUI[] drftRanks = new TextMeshProUGUI[3];
            TextMeshProUGUI[] drftDescs = new TextMeshProUGUI[3];

            for (int i = 0; i < 3; i++)
            {
                float cx = -325f + i * 325f;
                Button cardBtn = CreateButton("DraftCard_" + i, draftRoot, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(cx, -32f), new Vector2(296f, 290f), roundedSprite, TrimBarBg(), Palette.CharacterSecondary, 16f);
                CreateImage("CardTopStripe", cardBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-24f, 4f), solidSprite, Palette.CardRareBorder, false);

                TextMeshProUGUI dTitle = CreateStyledText("DraftTitle_" + i, cardBtn.transform, "STOPPING POWER", 19f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.Center);
                SetAnchoredRect(dTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(260f, 36f));

                TextMeshProUGUI dRank = CreateStyledText("DraftRank_" + i, cardBtn.transform, "PERK TIER 1", 14f, FontStyles.Bold, 2.5f, Palette.HealthGreen, TextAlignmentOptions.Center);
                SetAnchoredRect(dRank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(240f, 26f));

                TextMeshProUGUI dDesc = CreateStyledText("DraftDesc_" + i, cardBtn.transform, "+18% Ballistic Damage across all firearms", 15f, FontStyles.Normal, 1.2f, Palette.CharacterSecondary, TextAlignmentOptions.Center);
                dDesc.textWrappingMode = TextWrappingModes.Normal;
                SetAnchoredRect(dDesc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(248f, 96f));

                TextMeshProUGUI dInstall = CreateStyledText("DraftAction_" + i, cardBtn.transform, "[ EQUIP PERK ]", 14f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
                SetAnchoredRect(dInstall.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(200f, 28f));

                drftBtns[i] = cardBtn;
                drftTitles[i] = dTitle;
                drftRanks[i] = dRank;
                drftDescs[i] = dDesc;
            }

            // 7. INTEL DOSSIER MODAL OVERLAY
            CanvasGroup intelGroup = CreateFullScreenGroup("IntelPanel", canvasGo.transform);
            intelGroup.alpha = 0f;
            intelGroup.interactable = false;
            intelGroup.blocksRaycasts = false;
            CreateImage("IntelScrim", intelGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform intelCard = CreatePanel("IntelCard", intelGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 590f), roundedSprite, Palette.PanelSurface);
            CreateImage("IntelAccentTop", intelCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 4f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI intelHeader = CreateStyledText("IntelHeaderText", intelCard, "TAC-NET INTEL DOSSIER // OPERATION IRON HARVEST", 21f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(intelHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -42f), new Vector2(810f, 40f));

            TextMeshProUGUI intelBodyText = CreateStyledText("IntelBodyText", intelCard, StoryDirector.BuildIntelDossierText(0), 13.5f, FontStyles.Normal, 0.6f, Palette.CharacterSecondary, TextAlignmentOptions.TopLeft);
            intelBodyText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(intelBodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(810f, 420f));

            Button intelCloseBtn = CreateButton("IntelCloseButton", intelCard, "CLOSE DOSSIER", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(240f, 48f), roundedSprite, Palette.Accent, Palette.Shadow, 17f);

            // 8. CALL-OF-DUTY WIDESCREEN CUTSCENE LETTERBOX OVERLAY
            CanvasGroup cinemaGroup = CreateFullScreenGroup("CinemaOverlay", canvasGo.transform);
            cinemaGroup.alpha = 0f;
            cinemaGroup.interactable = false;
            cinemaGroup.blocksRaycasts = false;

            CreateImage("TopCinemaBar", cinemaGroup.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 100f), solidSprite, Palette.Shadow, true);
            RectTransform bottomCinemaBar = CreatePanel("BottomCinemaBar", cinemaGroup.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 136f), solidSprite, Palette.Shadow);

            TextMeshProUGUI cinemaStampTxt = CreateStyledText("CinemaMissionStampText", bottomCinemaBar, "OPERATION: IRON HARVEST\nSECTOR ECHO-7", 17f, FontStyles.Bold, 2.2f, Palette.HealthGreen, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(cinemaStampTxt.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(42f, 20f), new Vector2(540f, 96f));

            TextMeshProUGUI cinemaSpeakerTxt = CreateStyledText("CinemaSpeakerText", bottomCinemaBar, "OVERWATCH // TAC-NET CH-04", 13f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(cinemaSpeakerTxt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(30f, -14f), new Vector2(680f, 24f));

            TextMeshProUGUI cinemaDialogueTxt = CreateStyledText("CinemaDialogueText", bottomCinemaBar, "", 15.5f, FontStyles.Normal, 0.8f, Palette.CharacterSecondary, TextAlignmentOptions.Top);
            cinemaDialogueTxt.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredRect(cinemaDialogueTxt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30f, -10f), new Vector2(720f, 68f));

            Button skipCutsceneBtn = CreateButton("SkipCutsceneButton", bottomCinemaBar, "SKIP [SPACE] >>", new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(204f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 14f);

            UIManager ui = systemsRoot.AddComponent<UIManager>();
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

            return ui;
        }

        private static CanvasGroup CreateFullScreenGroup(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            StretchRect(rt);
            return go.AddComponent<CanvasGroup>();
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Sprite sprite, Color bgColor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = bgColor;
            img.raycastTarget = true;
            return rt;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Sprite sprite, Color color, bool raycastTarget)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        private static Image CreateIcon(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            return CreateImage(name, parent, anchor, anchor, pos, size, sprite, color, false);
        }

        private static Image CreateFilledBar(string name, Transform bgParent, Sprite fillSprite, Color fillColor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(bgParent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            StretchRect(rt);

            Image img = go.AddComponent<Image>();
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
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
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
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = bgColor;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Palette.ButtonTintIdle;
            cb.highlightedColor = Palette.ButtonTintHover;
            cb.pressedColor = Palette.CharacterSecondary;
            cb.colorMultiplier = 1f;
            btn.colors = cb;

            TextMeshProUGUI txt = CreateStyledText("Label", rt, label, fontSize, FontStyles.Bold, 3.5f, textColor, TextAlignmentOptions.Center);
            StretchRect(txt.rectTransform);
            return btn;
        }

        private static Slider CreateLabeledSlider(Transform parent, string key, string title, Vector2 pos, Sprite roundedSprite, Sprite solidSprite, Color fillColor, out TextMeshProUGUI pctOut)
        {
            RectTransform row = CreatePanel("Row_" + key, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(380f, 40f), roundedSprite, Palette.Transparent);
            row.GetComponent<Image>().raycastTarget = false;

            TextMeshProUGUI lbl = CreateStyledText("Label_" + key, row, title, 15f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(90f, 30f));

            pctOut = CreateStyledText("Pct_" + key, row, "80%", 15f, FontStyles.Bold, 2f, Palette.CharacterPrimary, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(pctOut.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(60f, 30f));

            GameObject sliderGo = new GameObject("Slider_" + key);
            sliderGo.transform.SetParent(row, false);
            RectTransform sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRt.anchoredPosition = new Vector2(10f, 0f);
            sliderRt.sizeDelta = new Vector2(210f, 24f);

            Image bg = CreateImage("Background", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 10f), roundedSprite, TrimBarBg(), true);
            bg.rectTransform.sizeDelta = new Vector2(0f, 10f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderRt, false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.offsetMin = new Vector2(0f, -5f);
            fillAreaRt.offsetMax = new Vector2(0f, 5f);

            Image fillImg = CreateImage("Fill", fillAreaRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, roundedSprite, fillColor, false);

            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderRt, false);
            RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
            StretchRect(handleAreaRt);

            Image handleImg = CreateImage("Handle", handleAreaRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 22f), solidSprite, Palette.CharacterPrimary, true);

            Slider slider = sliderGo.AddComponent<Slider>();
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
