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
            public Mesh PlayerJetpack;

            public Mesh WeaponPulseRifle;
            public Mesh WeaponShotgun;
            public Mesh WeaponRailgun;
            public Mesh WeaponRocket;

            public Mesh ChaserTorso;
            public Mesh ChaserHead;
            public Mesh HeavyTorso;
            public Mesh HeavyCrown;
            public Mesh BossTorso;
            public Mesh BossTurret;

            public Mesh TacticalCrate;
            public Mesh EnergyPylon;
            public Mesh PickupXpCrystal;
            public Mesh PickupMedkit;
            public Mesh PickupCreditCoin;
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

            // 2. Persisted Palette Materials & Skybox
            Material matFloor = SaveOrUpdateLitMaterial("Mat_Floor", Palette.Floor, 0.14f, 0.42f, false, 0f, floorTex, new Vector2(14f, 14f));
            Material matWalls = SaveOrUpdateLitMaterial("Mat_Walls", Palette.Walls, 0.35f, 0.44f, false, 0f);
            Material matProps = SaveOrUpdateLitMaterial("Mat_Props", Palette.Props, 0.55f, 0.52f, false, 0f);
            Material matTrim = SaveOrUpdateLitMaterial("Mat_TrimDark", Palette.Shadow, 0.40f, 0.35f, false, 0f);
            Material matPlayerPrimary = SaveOrUpdateLitMaterial("Mat_PlayerPrimary", Palette.CharacterPrimary, 0.25f, 0.58f, false, 0f);
            Material matPlayerSecondary = SaveOrUpdateLitMaterial("Mat_PlayerSecondary", Palette.CharacterSecondary, 0.65f, 0.62f, false, 0f);
            Material matAccentEmissive = SaveOrUpdateLitMaterial("Mat_AccentEmissive", Palette.Accent, 0.15f, 0.2f, true, 2.5f);
            Material matCyanGlow = SaveOrUpdateLitMaterial("Mat_CyanGlow", Palette.CyanGlow, 0.15f, 0.2f, true, 2.2f);
            Material matHealthGlow = SaveOrUpdateLitMaterial("Mat_HealthEmissive", Palette.HealthGreen, 0.15f, 0.2f, true, 2.0f);
            Material matSkybox = SaveOrUpdateSkyboxMaterial("Mat_ArenaSkybox");

            // 3. Configure URP Global Volume Profile
            VolumeProfile profile = ConfigureVolumeProfile();

            // 4. Open/Reset Scene Hierarchy Cleanly
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ClearOldGeneratedRoots();

            // 5. Scene Lighting, Fog, Skybox, Camera, Volume
            ConfigureEnvironmentLighting(matSkybox, profile);
            UnityEngine.Camera mainCam = ConfigureMainCamera();

            // 6. Arena Environment (Blender crates & pylons + perimeter walls)
            BuildArenaEnvironment(matFloor, matWalls, matProps, matTrim, matAccentEmissive, matCyanGlow, meshes);

            // 7. Player Rig (Blender armor parts + 4 switchable weapons)
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
                meshes.PickupCreditCoin);

            UIManager ui = BuildGameCanvasAndUI(systemsRoot, solidSprite, roundedSprite, shieldSprite, crosshairSprite, circleSprite);
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
            pack.PlayerJetpack = Get("Mesh_PlayerJetpack");

            pack.WeaponPulseRifle = Get("Mesh_Weapon_PulseRifle");
            pack.WeaponShotgun = Get("Mesh_Weapon_Shotgun");
            pack.WeaponRailgun = Get("Mesh_Weapon_Railgun");
            pack.WeaponRocket = Get("Mesh_Weapon_Rocket");

            pack.ChaserTorso = Get("Mesh_ChaserTorso");
            pack.ChaserHead = Get("Mesh_ChaserHead");
            pack.HeavyTorso = Get("Mesh_HeavyTorso");
            pack.HeavyCrown = Get("Mesh_HeavyCrown");
            pack.BossTorso = Get("Mesh_BossTorso");
            pack.BossTurret = Get("Mesh_BossTurret");

            pack.TacticalCrate = Get("Mesh_TacticalCrate");
            pack.EnergyPylon = Get("Mesh_EnergyPylon");
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
            bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.42f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;
            bloom.tint.overrideState = true;
            bloom.tint.value = Palette.KeyLightWarm;

            Vignette vignette = EnsureVolumeComponent<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.24f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.42f;
            vignette.color.overrideState = true;
            vignette.color.value = Palette.Shadow;

            ColorAdjustments colorAdj = EnsureVolumeComponent<ColorAdjustments>(profile);
            colorAdj.active = true;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 10f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 8f;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.22f;

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
            RenderSettings.fogColor = Palette.Shadow;
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 72f;

            GameObject keyLightGo = GameObject.Find("Directional Light");
            if (keyLightGo == null)
            {
                keyLightGo = new GameObject("Directional Light");
                keyLightGo.AddComponent<Light>();
            }
            keyLightGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Light keyLight = keyLightGo.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Palette.KeyLightWarm;
            keyLight.intensity = 1.35f;
            keyLight.shadows = LightShadows.Soft;

            GameObject rimLightGo = new GameObject("DirectionalFillLight");
            rimLightGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
            Light rimLight = rimLightGo.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = Palette.AmbientSky;
            rimLight.intensity = 0.62f;
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
            mainCam.transform.SetPositionAndRotation(new Vector3(0f, 13.4f, -8.2f), Quaternion.Euler(56.7f, 0f, 0f));

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

        private static void BuildArenaEnvironment(
            Material matFloor,
            Material matWalls,
            Material matProps,
            Material matTrim,
            Material matAccent,
            Material matCyan,
            VanguardMeshPack meshes)
        {
            GameObject env = new GameObject("ArenaEnvironment");

            // Ground plane (visual) + unit-scale floor BoxCollider
            GameObject floorRoot = new GameObject("ArenaFloor");
            floorRoot.transform.SetParent(env.transform, false);
            BoxCollider floorCol = floorRoot.AddComponent<BoxCollider>();
            floorCol.size = new Vector3(28f, 0.4f, 28f);
            floorCol.center = new Vector3(0f, -0.2f, 0f);

            GameObject floorVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorVisual.name = "FloorDeck";
            floorVisual.transform.SetParent(floorRoot.transform, false);
            floorVisual.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            floorVisual.transform.localScale = new Vector3(28f, 0.4f, 28f);
            Object.DestroyImmediate(floorVisual.GetComponent<Collider>());
            floorVisual.GetComponent<MeshRenderer>().sharedMaterial = matFloor;

            // Outer Perimeter Apron (extends subtly into fog so edge looks intentional)
            GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Cube);
            apron.name = "OuterFoundation";
            apron.transform.SetParent(env.transform, false);
            apron.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            apron.transform.localScale = new Vector3(44f, 0.4f, 44f);
            Object.DestroyImmediate(apron.GetComponent<Collider>());
            apron.GetComponent<MeshRenderer>().sharedMaterial = matTrim;

            // 4 Compound Perimeter Walls (unit-scale collider holder)
            BuildPerimeterWall(env.transform, "Wall_North", new Vector3(0f, 0f, 13.6f), new Vector3(28.4f, 1.8f, 1.0f), matWalls, matTrim, matCyan);
            BuildPerimeterWall(env.transform, "Wall_South", new Vector3(0f, 0f, -13.6f), new Vector3(28.4f, 1.8f, 1.0f), matWalls, matTrim, matCyan);
            BuildPerimeterWall(env.transform, "Wall_East", new Vector3(13.6f, 0f, 0f), new Vector3(1.0f, 1.8f, 26.2f), matWalls, matTrim, matCyan);
            BuildPerimeterWall(env.transform, "Wall_West", new Vector3(-13.6f, 0f, 0f), new Vector3(1.0f, 1.8f, 26.2f), matWalls, matTrim, matCyan);

            // 8 Tactical Cover Blocks (Blender SM_TacticalCrate + emissive trim)
            BuildTacticalCover(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent, meshes.TacticalCrate);

            BuildTacticalCover(env.transform, "Cover_North", new Vector3(0f, 0f, 8.2f), 0f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_South", new Vector3(0f, 0f, -8.2f), 0f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_East", new Vector3(8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan, meshes.TacticalCrate);
            BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan, meshes.TacticalCrate);

            // 4 Corner Tactical Pylons (Blender SM_EnergyPylon + PointLights)
            BuildCornerPylon(env.transform, "Pylon_NW", new Vector3(-11.8f, 0f, 11.8f), matWalls, matTrim, matCyan, Palette.CyanGlow, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_NE", new Vector3(11.8f, 0f, 11.8f), matWalls, matTrim, matAccent, Palette.Accent, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_SW", new Vector3(-11.8f, 0f, -11.8f), matWalls, matTrim, matAccent, Palette.Accent, meshes.EnergyPylon);
            BuildCornerPylon(env.transform, "Pylon_SE", new Vector3(11.8f, 0f, -11.8f), matWalls, matTrim, matCyan, Palette.CyanGlow, meshes.EnergyPylon);
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
            CreateVisualBox("NeonStrip", root.transform, new Vector3(0f, size.y * 0.72f, 0f), new Vector3(size.x + 0.06f, 0.10f, size.z + 0.06f), matGlow);
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
                CreateVisualBox("BasePlinth", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(size.x + 0.16f, 0.16f, size.z + 0.16f), matTrim);
                CreateVisualBox("HazardBand", root.transform, new Vector3(0f, size.y * 0.80f, 0f), new Vector3(size.x * 0.94f, 0.07f, size.z * 0.94f), matGlow);
            }
            else
            {
                CreateVisualBox("BasePlinth", root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(size.x + 0.18f, 0.28f, size.z + 0.18f), matTrim);
                CreateVisualBox("ArmorCore", root.transform, new Vector3(0f, size.y * 0.52f, 0f), new Vector3(size.x, size.y * 0.76f, size.z), matBody);
                CreateVisualBox("TopCap", root.transform, new Vector3(0f, size.y + 0.05f, 0f), new Vector3(size.x * 0.86f, 0.14f, size.z * 0.82f), matTrim);
                CreateVisualBox("HazardBand", root.transform, new Vector3(0f, size.y * 0.78f, 0f), new Vector3(size.x + 0.05f, 0.08f, size.z + 0.05f), matGlow);
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
                CreateVisualBox("BeaconCore", root.transform, new Vector3(0f, 2.72f, 0f), new Vector3(0.92f, 0.42f, 0.92f), matGlow);
            }
            else
            {
                CreateVisualBox("Base", root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 1.3f), matTrim);
                CreateVisualBox("Shaft", root.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.85f, 2.2f, 0.85f), matBody);
                CreateVisualBox("BeaconCore", root.transform, new Vector3(0f, 2.75f, 0f), new Vector3(0.95f, 0.45f, 0.95f), matGlow);
                CreateVisualBox("Cap", root.transform, new Vector3(0f, 3.1f, 0f), new Vector3(1.1f, 0.22f, 1.1f), matTrim);
            }

            GameObject lightGo = new GameObject("PylonLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.85f, 0f);
            Light pt = lightGo.AddComponent<Light>();
            pt.type = LightType.Point;
            pt.color = lightColor;
            pt.intensity = 4.5f;
            pt.range = 10f;
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
            root.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, 0f), Quaternion.identity);

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

            // Armored Chest & Waist (Blender custom meshes)
            GameObject torsoGo = CreateVisualMeshFit("Torso", meshes.PlayerTorso, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.58f, 0.58f, 0.38f), matPrimary);
            CreateVisualMeshFit("ChestRig", meshes.PlayerChestRig, PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.12f, 0.06f), new Vector3(0.64f, 0.36f, 0.40f), matSecondary);
            CreateVisualPrimitive("ChestCore", PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.12f, 0.27f), new Vector3(0.18f, 0.18f, 0.05f), matCyan);

            // Head & Visor
            GameObject headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.48f, 0.02f);
            GameObject helmetGo = CreateVisualMeshFit("Helmet", meshes.PlayerHelmet, PrimitiveType.Cube, headPivot.transform, Vector3.zero, new Vector3(0.42f, 0.36f, 0.44f), matPrimary);
            CreateVisualMeshFit("TacticalVisor", meshes.PlayerVisor, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.20f), new Vector3(0.36f, 0.12f, 0.10f), matCyan);
            CreateVisualPrimitive("Antenna", PrimitiveType.Cylinder, headPivot.transform, new Vector3(0.18f, 0.18f, -0.08f), new Vector3(0.03f, 0.07f, 0.03f), matAccent);

            // Left & Right Arms
            GameObject leftArmPivot = new GameObject("LeftArmPivot");
            leftArmPivot.transform.SetParent(visualRoot.transform, false);
            leftArmPivot.transform.localPosition = new Vector3(-0.38f, 1.24f, 0f);
            CreateVisualPrimitive("LeftShoulder", PrimitiveType.Cube, leftArmPivot.transform, new Vector3(-0.02f, -0.02f, 0f), new Vector3(0.22f, 0.20f, 0.24f), matSecondary);
            GameObject leftForearmGo = CreateVisualMeshFit("LeftForearm", meshes.PlayerLimb, PrimitiveType.Capsule, leftArmPivot.transform, new Vector3(0f, -0.24f, 0.08f), new Vector3(0.17f, 0.44f, 0.17f), matPrimary);

            GameObject rightArmPivot = new GameObject("RightArmPivot");
            rightArmPivot.transform.SetParent(visualRoot.transform, false);
            rightArmPivot.transform.localPosition = new Vector3(0.38f, 1.24f, 0f);
            CreateVisualPrimitive("RightShoulder", PrimitiveType.Cube, rightArmPivot.transform, new Vector3(0.02f, -0.02f, 0f), new Vector3(0.22f, 0.20f, 0.24f), matSecondary);
            GameObject rightForearmGo = CreateVisualMeshFit("RightForearm", meshes.PlayerLimb, PrimitiveType.Capsule, rightArmPivot.transform, new Vector3(0f, -0.24f, 0.12f), new Vector3(0.17f, 0.44f, 0.17f), matPrimary);

            // 4 Switchable Weapons mounted under WeaponAnchor
            GameObject weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.24f, 1.04f, 0.34f);

            GameObject[] weaponVisuals = new GameObject[4];
            weaponVisuals[0] = BuildWeaponModel("Weapon_PulseRifle", weaponAnchor.transform, meshes.WeaponPulseRifle, new Vector3(0.16f, 0.20f, 0.72f), matSecondary, matCyan);
            weaponVisuals[1] = BuildWeaponModel("Weapon_Shotgun", weaponAnchor.transform, meshes.WeaponShotgun, new Vector3(0.20f, 0.22f, 0.64f), matSecondary, matAccent);
            weaponVisuals[2] = BuildWeaponModel("Weapon_Railgun", weaponAnchor.transform, meshes.WeaponRailgun, new Vector3(0.16f, 0.22f, 0.94f), matSecondary, matCyan);
            weaponVisuals[3] = BuildWeaponModel("Weapon_Rocket", weaponAnchor.transform, meshes.WeaponRocket, new Vector3(0.26f, 0.28f, 0.84f), matSecondary, matHealth);
            weapon.BindWeaponVisuals(weaponVisuals);

            GameObject muzzlePoint = new GameObject("MuzzlePoint");
            muzzlePoint.transform.SetParent(weaponAnchor.transform, false);
            muzzlePoint.transform.localPosition = new Vector3(0f, 0.02f, 0.74f);

            // Legs
            GameObject leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.15f, 0.74f, 0f);
            CreateVisualMeshFit("LeftLeg", meshes.PlayerLimb, PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            GameObject rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.15f, 0.74f, 0f);
            CreateVisualMeshFit("RightLeg", meshes.PlayerLimb, PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.68f, 0.22f), matSecondary);

            // Thruster Pack (secondary lag pivot)
            GameObject secondaryPivot = new GameObject("SecondaryPivot");
            secondaryPivot.transform.SetParent(visualRoot.transform, false);
            secondaryPivot.transform.localPosition = new Vector3(0f, 1.12f, -0.24f);
            CreateVisualMeshFit("JetpackBody", meshes.PlayerJetpack, PrimitiveType.Cube, secondaryPivot.transform, Vector3.zero, new Vector3(0.40f, 0.44f, 0.22f), matSecondary);
            CreateVisualPrimitive("ThrusterGlowL", PrimitiveType.Cylinder, secondaryPivot.transform, new Vector3(-0.10f, -0.18f, -0.02f), new Vector3(0.09f, 0.08f, 0.09f), matCyan);
            CreateVisualPrimitive("ThrusterGlowR", PrimitiveType.Cylinder, secondaryPivot.transform, new Vector3(0.10f, -0.18f, -0.02f), new Vector3(0.09f, 0.08f, 0.09f), matCyan);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot.transform, rightArmPivot.transform, leftLegPivot.transform, rightLegPivot.transform, secondaryPivot.transform);
            MeshRenderer[] primaryArmorParts = new[]
            {
                torsoGo.GetComponent<MeshRenderer>(),
                helmetGo.GetComponent<MeshRenderer>(),
                leftForearmGo.GetComponent<MeshRenderer>(),
                rightForearmGo.GetComponent<MeshRenderer>()
            };
            player.BindComponents(inputAsset, cc, motion, weapon, root.GetComponentsInChildren<MeshRenderer>(true), primaryArmorParts);

            return player;
        }

        private static GameObject BuildWeaponModel(
            string name,
            Transform anchor,
            Mesh weaponMesh,
            Vector3 targetSize,
            Material matBody,
            Material matGlow)
        {
            GameObject container = new GameObject(name);
            container.transform.SetParent(anchor, false);
            container.transform.localPosition = Vector3.zero;

            if (weaponMesh != null)
            {
                CreateVisualMeshFit("MeshBody", weaponMesh, PrimitiveType.Cube, container.transform, new Vector3(0f, 0f, 0.22f), targetSize, matBody, true);
                CreateVisualPrimitive("MuzzleCore", PrimitiveType.Cube, container.transform, new Vector3(0f, 0.03f, targetSize.z * 0.45f), new Vector3(0.06f, 0.05f, targetSize.z * 0.28f), matGlow);
            }
            else
            {
                CreateVisualPrimitive("Receiver", PrimitiveType.Cube, container.transform, new Vector3(0f, 0f, 0.16f), new Vector3(targetSize.x, targetSize.y, targetSize.z * 0.85f), matBody);
                CreateVisualPrimitive("RailGlow", PrimitiveType.Cube, container.transform, new Vector3(0f, 0.06f, 0.36f), new Vector3(targetSize.x * 0.55f, 0.06f, targetSize.z * 0.65f), matGlow);
            }

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

            TextMeshProUGUI hpLabel = CreateStyledText("HPLabel", hpCard, "INTEGRITY", 13f, FontStyles.Bold, 3.5f, Palette.MutedText, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(hpLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(74f, -14f), new Vector2(130f, 22f));

            TextMeshProUGUI hpValueText = CreateStyledText("HPValueText", hpCard, "100 / 100", 16f, FontStyles.Bold, 2f, Palette.CharacterPrimary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(hpValueText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -13f), new Vector2(130f, 24f));

            Image hpBg = CreateImage("HPBarBg", hpCard, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 18f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(hpBg.rectTransform, 74f, 16f, 4f, 18f);

            Image hpFill = CreateFilledBar("HPBarFill", hpBg.transform, roundedSprite, Palette.HealthGreen);

            TextMeshProUGUI dashLabel = CreateStyledText("DashLabel", hpCard, "THRUSTER [SHIFT]", 11f, FontStyles.Bold, 2.5f, Palette.CyanGlow, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(dashLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(74f, 14f), new Vector2(150f, 18f));

            Image dashBg = CreateImage("DashBarBg", hpCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 7f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(dashBg.rectTransform, 225f, 16f, -31f, 7f);
            Image dashFill = CreateFilledBar("DashBarFill", dashBg.transform, roundedSprite, Palette.CyanGlow);

            // Top-Left Credits Pill (below HP card)
            RectTransform crPill = CreatePanel("CreditsPill_Left", hudGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -136f), new Vector2(200f, 34f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            CreateImage("CreditsAccentLeft", crPill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 20f), solidSprite, Palette.CreditGold, false);
            TextMeshProUGUI hudCreditsText = CreateStyledText("HUD_CreditsText", crPill, "BANK // 200 CR", 14f, FontStyles.Bold, 2.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(hudCreditsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(10f, 0f), new Vector2(-24f, 28f));

            // Top-Center Wave & Score Pill
            RectTransform centerPill = CreatePanel("StatusPill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 74f), roundedSprite, Palette.PanelSurface);
            CreateImage("PillAccent", centerPill, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.CyanGlow, false);

            TextMeshProUGUI waveText = CreateStyledText("WaveText", centerPill, "WAVE 01", 22f, FontStyles.Bold, 3.5f, Palette.CharacterPrimary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(waveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(150f, 28f));

            TextMeshProUGUI enemiesText = CreateStyledText("EnemiesText", centerPill, "8 HOSTILES", 13f, FontStyles.Bold, 2.5f, Palette.EnemySecondary, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(enemiesText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 12f), new Vector2(160f, 22f));

            TextMeshProUGUI scoreText = CreateStyledText("ScoreText", centerPill, "0 PTS", 22f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(165f, 44f));

            // Boss Encounter Health Bar (anchored cleanly beneath Center Pill)
            RectTransform bossRoot = CreatePanel("BossBarRoot", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(540f, 56f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.94f));
            CreateImage("BossBarAccent", bossRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.BossPurple, false);

            TextMeshProUGUI bossNameText = CreateStyledText("BossNameText", bossRoot, "DREADNOUGHT MK-IV", 13f, FontStyles.Bold, 3.5f, Palette.BossPurple, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(bossNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(300f, 20f));

            TextMeshProUGUI bossHpText = CreateStyledText("BossHpText", bossRoot, "1200 / 1200", 13f, FontStyles.Bold, 2f, Palette.CharacterPrimary, TextAlignmentOptions.TopRight);
            SetAnchoredRect(bossHpText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(160f, 20f));

            Image bossHpBg = CreateImage("BossHpBg", bossRoot, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(bossHpBg.rectTransform, 18f, 18f, -14f, 12f);
            Image bossHpFill = CreateFilledBar("BossHpFill", bossHpBg.transform, roundedSprite, Palette.BossPurple);
            bossRoot.gameObject.SetActive(false);

            // Top-Right Level/XP & Pause Card
            RectTransform rightCard = CreatePanel("StatusCard_Right", hudGroup.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(340f, 88f), roundedSprite, Palette.PanelSurface);
            CreateImage("RightAccentTop", rightCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI levelText = CreateStyledText("LevelText", rightCard, "LVL 01", 20f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(140f, 28f));

            Image xpBg = CreateImage("XPBarBg", rightCard, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 14f), roundedSprite, TrimBarBg(), false);
            SetHorizontalBarRect(xpBg.rectTransform, 18f, 84f, -22f, 14f);
            Image xpFill = CreateFilledBar("XPBarFill", xpBg.transform, roundedSprite, Palette.Accent);
            xpFill.fillAmount = 0f;

            Button hudPauseBtn = CreateButton("HUD_PauseButton", rightCard, "| |", new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(56f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 18f);

            // Bottom-Center 4-Weapon Loadout Bar
            RectTransform weaponBar = CreatePanel("WeaponLoadoutBar", hudGroup.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(660f, 62f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.88f));
            CreateImage("WeaponBarAccent", weaponBar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-28f, 2f), solidSprite, Palette.CyanGlow, false);

            Button[] wpnBtns = new Button[4];
            Image[] wpnBgs = new Image[4];
            TextMeshProUGUI[] wpnLbls = new TextMeshProUGUI[4];
            string[] wpnSlotDefault = { "[1] PULSE", "[2] SHOTGUN", "[3] RAILGUN", "[4] ROCKET" };
            for (int i = 0; i < 4; i++)
            {
                float slotX = -237f + i * 158f;
                Button b = CreateButton("WeaponSlot_" + i, weaponBar, wpnSlotDefault[i], new Vector2(0.5f, 0.5f), new Vector2(slotX, 0f), new Vector2(148f, 44f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 13f);
                wpnBtns[i] = b;
                wpnBgs[i] = b.GetComponent<Image>();
                wpnLbls[i] = b.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Mobile / Touch Controls (Simultaneous Twin Virtual Joysticks + Touch Dash)
            GameObject touchRoot = new GameObject("MobileTouchControls");
            touchRoot.transform.SetParent(hudGroup.transform, false);
            RectTransform touchRt = touchRoot.AddComponent<RectTransform>();
            StretchRect(touchRt);

            RectTransform moveBgRt = CreatePanel("MoveStickBg", touchRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            moveBgRt.pivot = new Vector2(0.5f, 0.5f);
            moveBgRt.anchoredPosition = new Vector2(138f, 138f);
            Image moveHandleImg = CreateImage("MoveStickHandle", moveBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.CyanGlow, 0.72f), false);
            VirtualJoystick leftStick = moveBgRt.gameObject.AddComponent<VirtualJoystick>();
            leftStick.BindRects(moveBgRt, moveHandleImg.rectTransform, 52f);

            RectTransform aimBgRt = CreatePanel("AimStickBg", touchRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-138f, 138f), new Vector2(168f, 168f), circleSprite, Palette.WithAlpha(Palette.PanelSurface, 0.44f));
            aimBgRt.pivot = new Vector2(0.5f, 0.5f);
            aimBgRt.anchoredPosition = new Vector2(-138f, 138f);
            Image aimHandleImg = CreateImage("AimStickHandle", aimBgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f), circleSprite, Palette.WithAlpha(Palette.Accent, 0.78f), false);
            VirtualJoystick rightStick = aimBgRt.gameObject.AddComponent<VirtualJoystick>();
            rightStick.BindRects(aimBgRt, aimHandleImg.rectTransform, 52f);

            Button touchDashBtn = CreateButton("TouchDashButton", touchRt, "DASH", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f), circleSprite, Palette.WithAlpha(Palette.CyanGlow, 0.28f), Palette.CharacterPrimary, 16f);
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
            TextMeshProUGUI milestoneTxt = CreateStyledText("MilestoneText", bannerRt, "LEVEL UP // OVERDRIVE", 22f, FontStyles.Bold, 4f, Palette.Accent, TextAlignmentOptions.Center);
            StretchRect(milestoneTxt.rectTransform);

            // Reticle Crosshair
            Image crosshairImg = CreateImage("Crosshair", hudGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f), crosshairSprite, Palette.WithAlpha(Palette.Accent, 0.9f), false);

            // 2. TITLE PANEL
            CanvasGroup titleGroup = CreateFullScreenGroup("TitlePanel", canvasGo.transform);
            CreateImage("TitleScrim", titleGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.WithAlpha(Palette.Shadow, 0.42f), true);

            RectTransform titleCard = CreatePanel("TitleCard", titleGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 430f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            CreateImage("TitleAccentTop", titleCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI gameTitle = CreateStyledText("GameTitleText", titleCard, "VANGUARD", 58f, FontStyles.Bold, 8f, Palette.CharacterPrimary, TextAlignmentOptions.Center);
            SetAnchoredRect(gameTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(560f, 76f));

            TextMeshProUGUI subtitle = CreateStyledText("SubtitleText", titleCard, "NEON ARENA // STORE EDITION 2.0", 17f, FontStyles.Bold, 5f, Palette.CyanGlow, TextAlignmentOptions.Center);
            SetAnchoredRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(560f, 30f));

            TextMeshProUGUI titleBest = CreateStyledText("TitleHighScoreText", titleCard, "HIGH SCORE // 0", 16f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(titleBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(460f, 28f));

            TextMeshProUGUI titleCredits = CreateStyledText("TitleCreditsText", titleCard, "CYBER-CREDITS // 200 CR", 16f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(titleCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(460f, 26f));

            TextMeshProUGUI controlsHint = CreateStyledText("ControlsHintText", titleCard, "WASD / STICK MOVE  ·  MOUSE / AIM FIRE  ·  1-4 WEAPONS  ·  SHIFT DASH  ·  ESC PAUSE", 11.5f, FontStyles.Bold, 1.8f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(controlsHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(630f, 24f));

            Button startBtn = CreateButton("StartMissionButton", titleCard, "START MISSION", new Vector2(0.5f, 0.5f), new Vector2(-132f, -78f), new Vector2(244f, 60f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button armoryBtn = CreateButton("TitleArmoryButton", titleCard, "ARMORY & SHOP", new Vector2(0.5f, 0.5f), new Vector2(132f, -78f), new Vector2(244f, 60f), roundedSprite, Palette.SecondaryButtonBg, Palette.CyanGlow, 19f);

            // 3. PAUSE PANEL
            CanvasGroup pauseGroup = CreateFullScreenGroup("PausePanel", canvasGo.transform);
            CreateImage("PauseScrim", pauseGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform pauseCard = CreatePanel("PauseCard", pauseGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 440f), roundedSprite, Palette.PanelSurface);
            CreateImage("PauseAccentTop", pauseCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 4f), solidSprite, Palette.CyanGlow, false);

            TextMeshProUGUI pauseHeader = CreateStyledText("PauseHeaderText", pauseCard, "PAUSED", 34f, FontStyles.Bold, 6f, Palette.CharacterPrimary, TextAlignmentOptions.Center);
            SetAnchoredRect(pauseHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(400f, 48f));

            Slider musicSlider = CreateLabeledSlider(pauseCard, "Music", "MUSIC", new Vector2(0f, 70f), roundedSprite, solidSprite, Palette.CyanGlow, out TextMeshProUGUI musicPct);
            Slider sfxSlider = CreateLabeledSlider(pauseCard, "SFX", "SFX", new Vector2(0f, 14f), roundedSprite, solidSprite, Palette.Accent, out TextMeshProUGUI sfxPct);

            Button resumeBtn = CreateButton("ResumeButton", pauseCard, "RESUME", new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(260f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button pRestartBtn = CreateButton("PauseRestartButton", pauseCard, "RESTART", new Vector2(0.5f, 0.5f), new Vector2(0f, -118f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 17f);
            Button pTitleBtn = CreateButton("PauseTitleButton", pauseCard, "MAIN MENU", new Vector2(0.5f, 0.5f), new Vector2(0f, -174f), new Vector2(260f, 46f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterSecondary, 17f);

            // 4. ENDED PANEL
            CanvasGroup endedGroup = CreateFullScreenGroup("EndedPanel", canvasGo.transform);
            CreateImage("EndedScrim", endedGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform endedCard = CreatePanel("EndedCard", endedGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 440f), roundedSprite, Palette.PanelSurface);
            CreateImage("EndedAccentTop", endedCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.EnemySecondary, false);

            TextMeshProUGUI endedTitle = CreateStyledText("EndedTitleText", endedCard, "SIGNAL LOST", 38f, FontStyles.Bold, 6f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(460f, 52f));

            TextMeshProUGUI endedScore = CreateStyledText("EndedScoreText", endedCard, "0 PTS", 44f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(endedScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 52f), new Vector2(460f, 58f));

            TextMeshProUGUI endedBest = CreateStyledText("EndedBestText", endedCard, "BEST // 0", 18f, FontStyles.Bold, 3.5f, Palette.CyanGlow, TextAlignmentOptions.Center);
            SetAnchoredRect(endedBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(460f, 28f));

            TextMeshProUGUI endedStats = CreateStyledText("EndedStatsText", endedCard, "ELIMINATIONS: 0   |   WAVE REACHED: 1", 15f, FontStyles.Bold, 2f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(endedStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(480f, 26f));

            TextMeshProUGUI endedCredits = CreateStyledText("EndedCreditsText", endedCard, "CREDITS EARNED: +0 CR   |   BANK: 200 CR", 15f, FontStyles.Bold, 2f, Palette.CreditGold, TextAlignmentOptions.Center);
            SetAnchoredRect(endedCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -72f), new Vector2(500f, 26f));

            Button eReplayBtn = CreateButton("EndedReplayButton", endedCard, "REPLAY", new Vector2(0.5f, 0.5f), new Vector2(-115f, -142f), new Vector2(205f, 56f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button eTitleBtn = CreateButton("EndedTitleButton", endedCard, "MAIN MENU", new Vector2(0.5f, 0.5f), new Vector2(115f, -142f), new Vector2(205f, 56f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 18f);

            // 5. ARMORY & UPGRADES MODAL PANEL
            CanvasGroup armoryGroup = CreateFullScreenGroup("ArmoryPanel", canvasGo.transform);
            CreateImage("ArmoryScrim", armoryGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform armoryCard = CreatePanel("ArmoryCard", armoryGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 680f), roundedSprite, Palette.PanelSurface);
            CreateImage("ArmoryAccentTop", armoryCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 4f), solidSprite, Palette.CyanGlow, false);

            TextMeshProUGUI armoryHeader = CreateStyledText("ArmoryHeaderText", armoryCard, "VANGUARD ARMORY // META-UPGRADES", 23f, FontStyles.Bold, 3.5f, Palette.CharacterPrimary, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(armoryHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -44f), new Vector2(620f, 44f));

            TextMeshProUGUI armoryCreditsText = CreateStyledText("ArmoryCreditsText", armoryCard, "BANK: 200 CR", 22f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(armoryCreditsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -44f), new Vector2(280f, 44f));

            // Left Column: 4 Permanent Upgrades
            TextMeshProUGUI leftColHdr = CreateStyledText("UpgradesSectionTitle", armoryCard, "PERMANENT CYBERNETICS", 14f, FontStyles.Bold, 3.5f, Palette.CyanGlow, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(leftColHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, 212f), new Vector2(440f, 26f));

            string[] upgDescs = { "+20 MAX HP / RANK", "+10% ALL DMG / RANK", "+8% MOVE SPD / RANK", "+15% CREDITS / RANK" };
            Button[] upgBtns = new Button[4];
            TextMeshProUGUI[] upgRankLbls = new TextMeshProUGUI[4];
            TextMeshProUGUI[] upgCostLbls = new TextMeshProUGUI[4];

            for (int i = 0; i < 4; i++)
            {
                float rowY = 152f - i * 94f;
                RectTransform row = CreatePanel("UpgradeRow_" + i, armoryCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-235f, rowY), new Vector2(440f, 80f), roundedSprite, TrimBarBg());
                CreateImage("UpgRowStripe", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(4f, 50f), solidSprite, Palette.Accent, false);

                TextMeshProUGUI uRank = CreateStyledText("UpgRank_" + i, row, "UPGRADE   [LV 0/5]", 13f, FontStyles.Bold, 1f, Palette.CharacterPrimary, TextAlignmentOptions.TopLeft);
                SetAnchoredRect(uRank.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(280f, 22f));
                upgRankLbls[i] = uRank;

                TextMeshProUGUI uDesc = CreateStyledText("UpgSub_" + i, row, upgDescs[i], 12f, FontStyles.Bold, 1.5f, Palette.CyanGlow, TextAlignmentOptions.BottomLeft);
                SetAnchoredRect(uDesc.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 14f), new Vector2(275f, 20f));

                Button buyBtn = CreateButton("UpgradeBtn_" + i, row, "100 CR", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(128f, 52f), roundedSprite, Palette.Accent, Palette.Shadow, 15f);
                upgBtns[i] = buyBtn;
                upgCostLbls[i] = buyBtn.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Right Column Top: 4 Unlockable Weapons
            TextMeshProUGUI rightWpnHdr = CreateStyledText("WeaponsSectionTitle", armoryCard, "ARSENAL LOADOUT", 14f, FontStyles.Bold, 3.5f, Palette.Accent, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightWpnHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, 212f), new Vector2(440f, 26f));

            Button[] shopWpnBtns = new Button[4];
            TextMeshProUGUI[] shopWpnLbls = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                float wy = 166f - i * 62f;
                Button wb = CreateButton("ShopWeapon_" + i, armoryCard, "WEAPON " + i, new Vector2(0.5f, 0.5f), new Vector2(235f, wy), new Vector2(440f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 14f);
                shopWpnBtns[i] = wb;
                shopWpnLbls[i] = wb.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Right Column Bottom: 3 Armor Chassis Skins
            TextMeshProUGUI rightSkinHdr = CreateStyledText("SkinsSectionTitle", armoryCard, "CHASSIS COATING", 14f, FontStyles.Bold, 3.5f, Palette.CreditGold, TextAlignmentOptions.MidlineLeft);
            SetAnchoredRect(rightSkinHdr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(235f, -74f), new Vector2(440f, 26f));

            Button[] shopSkinBtns = new Button[3];
            TextMeshProUGUI[] shopSkinLbls = new TextMeshProUGUI[3];
            Color[] skinSwatches = { Palette.CharacterPrimary, Palette.SkinGoldPrimary, Palette.SkinCrimsonPrimary };
            for (int i = 0; i < 3; i++)
            {
                float sx = 85f + i * 150f;
                Button sb = CreateButton("ShopSkin_" + i, armoryCard, "SKIN " + i, new Vector2(0.5f, 0.5f), new Vector2(sx, -132f), new Vector2(138f, 70f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 12f);
                CreateImage("Swatch", sb.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(96f, 5f), solidSprite, skinSwatches[i], false);
                shopSkinBtns[i] = sb;
                shopSkinLbls[i] = sb.GetComponentInChildren<TextMeshProUGUI>();
            }

            Button armoryCloseBtn = CreateButton("ArmoryCloseButton", armoryCard, "BACK TO MENU", new Vector2(0.5f, 0.5f), new Vector2(0f, -292f), new Vector2(240f, 52f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 18f);

            // 6. LEVEL-UP ROGUELITE SKILL DRAFT MODAL PANEL
            CanvasGroup draftGroup = CreateFullScreenGroup("LevelUpDraftPanel", canvasGo.transform);
            CreateImage("DraftScrim", draftGroup.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, solidSprite, Palette.ScrimDark, true);

            RectTransform draftRoot = CreatePanel("DraftContainer", draftGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 470f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.96f));
            CreateImage("DraftAccentTop", draftRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-36f, 5f), solidSprite, Palette.CardRareBorder, false);

            TextMeshProUGUI draftTitle = CreateStyledText("DraftHeaderTitle", draftRoot, "TACTICAL UPGRADE DRAFT", 30f, FontStyles.Bold, 5.5f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(draftTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 42f));

            TextMeshProUGUI draftSub = CreateStyledText("DraftHeaderSub", draftRoot, "CHOOSE ONE COMBAT PROTOCOL TO INSTALL", 13.5f, FontStyles.Bold, 3f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(draftSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(800f, 24f));

            Button[] drftBtns = new Button[3];
            TextMeshProUGUI[] drftTitles = new TextMeshProUGUI[3];
            TextMeshProUGUI[] drftRanks = new TextMeshProUGUI[3];
            TextMeshProUGUI[] drftDescs = new TextMeshProUGUI[3];

            for (int i = 0; i < 3; i++)
            {
                float cx = -325f + i * 325f;
                Button cardBtn = CreateButton("DraftCard_" + i, draftRoot, string.Empty, new Vector2(0.5f, 0.5f), new Vector2(cx, -32f), new Vector2(296f, 290f), roundedSprite, TrimBarBg(), Palette.CharacterPrimary, 16f);
                CreateImage("CardTopStripe", cardBtn.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(-24f, 4f), solidSprite, Palette.CardRareBorder, false);

                TextMeshProUGUI dTitle = CreateStyledText("DraftTitle_" + i, cardBtn.transform, "OVERCLOCK", 20f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
                SetAnchoredRect(dTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(260f, 36f));

                TextMeshProUGUI dRank = CreateStyledText("DraftRank_" + i, cardBtn.transform, "RANK 1 -> 2", 14f, FontStyles.Bold, 2.5f, Palette.CyanGlow, TextAlignmentOptions.Center);
                SetAnchoredRect(dRank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(240f, 26f));

                TextMeshProUGUI dDesc = CreateStyledText("DraftDesc_" + i, cardBtn.transform, "+18% Fire Rate across all weapons", 15f, FontStyles.Normal, 1.2f, Palette.CharacterPrimary, TextAlignmentOptions.Center);
                dDesc.textWrappingMode = TextWrappingModes.Normal;
                SetAnchoredRect(dDesc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(248f, 96f));

                TextMeshProUGUI dInstall = CreateStyledText("DraftAction_" + i, cardBtn.transform, "[ INSTALL ]", 14f, FontStyles.Bold, 3f, Palette.CreditGold, TextAlignmentOptions.Center);
                SetAnchoredRect(dInstall.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(200f, 28f));

                drftBtns[i] = cardBtn;
                drftTitles[i] = dTitle;
                drftRanks[i] = dRank;
                drftDescs[i] = dDesc;
            }

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
                drftBtns, drftTitles, drftRanks, drftDescs);

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

        private static Color TrimBarBg() => Palette.FromHex("#0F1522");

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
