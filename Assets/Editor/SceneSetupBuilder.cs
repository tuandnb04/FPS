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
    /// compound arena geometry, player rig, Cinemachine rig, audio/VFX/feel directors, and uGUI shell.
    /// </summary>
    public static class SceneSetupBuilder
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MaterialsDir = "Assets/Materials";
        private const string TexturesDir = "Assets/Textures";
        private const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("Vanguard/Rebuild Top-Down Shooter Scene")]
        public static void BuildCompleteScene()
        {
            EnsureFolders();

            // 1. Persisted Texture Assets
            Texture2D floorTex = SaveTextureAsset(UISpriteFactory.GetArenaFloorGridTexture(), TexturesDir + "/Tex_ArenaFloorGrid.png", true);
            Sprite solidSprite = SaveSpriteAsset(UISpriteFactory.GetSolidRectSprite().texture, TexturesDir + "/Tex_SolidRect.png");
            Sprite roundedSprite = SaveSpriteAsset(UISpriteFactory.GetRoundedRectSprite().texture, TexturesDir + "/Tex_RoundedRect.png");
            Sprite shieldSprite = SaveSpriteAsset(UISpriteFactory.GetShieldIcon().texture, TexturesDir + "/Tex_ShieldIcon.png");
            Sprite crosshairSprite = SaveSpriteAsset(UISpriteFactory.GetCrosshairIcon().texture, TexturesDir + "/Tex_CrosshairIcon.png");

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

            // 6. Arena Environment
            BuildArenaEnvironment(matFloor, matWalls, matProps, matTrim, matAccentEmissive, matCyanGlow);

            // 7. Player Rig
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            PlayerController player = BuildPlayerRig(inputAsset, matPlayerPrimary, matPlayerSecondary, matAccentEmissive, matCyanGlow);

            // 8. Cinemachine Top-Down Rig
            TopDownCameraRig cameraRig = BuildCameraRig(player.transform, mainCam);

            // 9. GameSystems & UI Canvas
            GameObject systemsRoot = new GameObject("GameSystems");
            Transform projPoolRoot = new GameObject("ProjectilePoolRoot").transform;
            projPoolRoot.SetParent(systemsRoot.transform, false);
            player.Weapon.BindReferences(player.transform.Find("VisualRoot/WeaponAnchor/MuzzlePoint"), player.Motion, projPoolRoot);

            Transform vfxPoolRoot = new GameObject("VFXPoolRoot").transform;
            vfxPoolRoot.SetParent(systemsRoot.transform, false);
            Transform textRoot = new GameObject("FloatingTextRoot").transform;
            textRoot.SetParent(systemsRoot.transform, false);

            systemsRoot.AddComponent<AudioManager>();
            VFXManager vfx = systemsRoot.AddComponent<VFXManager>();
            vfx.BindPoolRoot(vfxPoolRoot);

            CinemachineImpulseSource impulse = systemsRoot.AddComponent<CinemachineImpulseSource>();
            FeelManager feel = systemsRoot.AddComponent<FeelManager>();
            feel.BindComponents(impulse, textRoot);

            systemsRoot.AddComponent<EnemySpawner>();
            UIManager ui = BuildGameCanvasAndUI(systemsRoot, solidSprite, roundedSprite, shieldSprite, crosshairSprite);
            systemsRoot.AddComponent<GameManager>();

            // Unused local guard check
            if (cameraRig == null || ui == null || matHealthGlow == null)
            {
                Debug.LogWarning("SceneSetupBuilder noticed a missing reference.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[SceneSetupBuilder] Successfully rebuilt Top-Down Shooter SampleScene.");
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

        private static VolumeProfile ConfigureVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.42f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;
            bloom.tint.overrideState = true;
            bloom.tint.value = Palette.KeyLightWarm;

            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.24f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.42f;
            vignette.color.overrideState = true;
            vignette.color.value = Palette.Shadow;

            if (!profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj = profile.Add<ColorAdjustments>(true);
            }
            colorAdj.active = true;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 10f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 8f;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.22f;

            if (profile.TryGet(out MotionBlur motionBlur))
            {
                motionBlur.active = false;
            }

            EditorUtility.SetDirty(profile);
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
            Material matCyan)
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

            // 8 Tactical Cover Blocks symmetrically placed inside arena
            BuildTacticalCover(env.transform, "Cover_NW", new Vector3(-5.2f, 0f, 5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent);
            BuildTacticalCover(env.transform, "Cover_NE", new Vector3(5.2f, 0f, 5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent);
            BuildTacticalCover(env.transform, "Cover_SW", new Vector3(-5.2f, 0f, -5.2f), -45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent);
            BuildTacticalCover(env.transform, "Cover_SE", new Vector3(5.2f, 0f, -5.2f), 45f, new Vector3(3.0f, 1.35f, 1.15f), matProps, matTrim, matAccent);

            BuildTacticalCover(env.transform, "Cover_North", new Vector3(0f, 0f, 8.2f), 0f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan);
            BuildTacticalCover(env.transform, "Cover_South", new Vector3(0f, 0f, -8.2f), 0f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan);
            BuildTacticalCover(env.transform, "Cover_East", new Vector3(8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan);
            BuildTacticalCover(env.transform, "Cover_West", new Vector3(-8.2f, 0f, 0f), 90f, new Vector3(3.6f, 1.25f, 1.1f), matProps, matTrim, matCyan);

            // 4 Corner Tactical Pylons w/ local PointLights
            BuildCornerPylon(env.transform, "Pylon_NW", new Vector3(-11.8f, 0f, 11.8f), matWalls, matTrim, matCyan, Palette.CyanGlow);
            BuildCornerPylon(env.transform, "Pylon_NE", new Vector3(11.8f, 0f, 11.8f), matWalls, matTrim, matAccent, Palette.Accent);
            BuildCornerPylon(env.transform, "Pylon_SW", new Vector3(-11.8f, 0f, -11.8f), matWalls, matTrim, matAccent, Palette.Accent);
            BuildCornerPylon(env.transform, "Pylon_SE", new Vector3(11.8f, 0f, -11.8f), matWalls, matTrim, matCyan, Palette.CyanGlow);
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

        private static void BuildTacticalCover(Transform parent, string coverName, Vector3 pos, float yawDeg, Vector3 size, Material matBody, Material matTrim, Material matGlow)
        {
            GameObject root = new GameObject(coverName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = size;
            col.center = new Vector3(0f, size.y * 0.5f, 0f);

            // Base plinth
            CreateVisualBox("BasePlinth", root.transform, new Vector3(0f, 0.14f, 0f), new Vector3(size.x + 0.18f, 0.28f, size.z + 0.18f), matTrim);
            // Main armor block
            CreateVisualBox("ArmorCore", root.transform, new Vector3(0f, size.y * 0.52f, 0f), new Vector3(size.x, size.y * 0.76f, size.z), matBody);
            // Top bevel crest
            CreateVisualBox("TopCap", root.transform, new Vector3(0f, size.y + 0.05f, 0f), new Vector3(size.x * 0.86f, 0.14f, size.z * 0.82f), matTrim);
            // Recessed neon edge trim
            CreateVisualBox("HazardBand", root.transform, new Vector3(0f, size.y * 0.78f, 0f), new Vector3(size.x + 0.05f, 0.08f, size.z + 0.05f), matGlow);
        }

        private static void BuildCornerPylon(Transform parent, string pylonName, Vector3 pos, Material matBody, Material matTrim, Material matGlow, Color lightColor)
        {
            GameObject root = new GameObject(pylonName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.1f, 3.2f, 1.1f);
            col.center = new Vector3(0f, 1.6f, 0f);

            CreateVisualBox("Base", root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 1.3f), matTrim);
            CreateVisualBox("Shaft", root.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.85f, 2.2f, 0.85f), matBody);
            CreateVisualBox("BeaconCore", root.transform, new Vector3(0f, 2.75f, 0f), new Vector3(0.95f, 0.45f, 0.95f), matGlow);
            CreateVisualBox("Cap", root.transform, new Vector3(0f, 3.1f, 0f), new Vector3(1.1f, 0.22f, 1.1f), matTrim);

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

        private static PlayerController BuildPlayerRig(
            InputActionAsset inputAsset,
            Material matPrimary,
            Material matSecondary,
            Material matAccent,
            Material matCyan)
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

            // Armored Chest & Waist
            CreateVisualPrimitive("Torso", PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.56f, 0.56f, 0.36f), matPrimary);
            CreateVisualPrimitive("ChestRig", PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.12f, 0.06f), new Vector3(0.62f, 0.34f, 0.38f), matSecondary);
            CreateVisualPrimitive("ChestCore", PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.12f, 0.26f), new Vector3(0.18f, 0.18f, 0.05f), matCyan);

            // Head & Visor
            GameObject headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.48f, 0.02f);
            CreateVisualPrimitive("Helmet", PrimitiveType.Cube, headPivot.transform, Vector3.zero, new Vector3(0.40f, 0.34f, 0.42f), matPrimary);
            CreateVisualPrimitive("TacticalVisor", PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.20f), new Vector3(0.36f, 0.12f, 0.08f), matCyan);
            CreateVisualPrimitive("Antenna", PrimitiveType.Cylinder, headPivot.transform, new Vector3(0.18f, 0.22f, -0.08f), new Vector3(0.03f, 0.16f, 0.03f), matAccent);

            // Left & Right Arms
            GameObject leftArmPivot = new GameObject("LeftArmPivot");
            leftArmPivot.transform.SetParent(visualRoot.transform, false);
            leftArmPivot.transform.localPosition = new Vector3(-0.38f, 1.24f, 0f);
            CreateVisualPrimitive("LeftShoulder", PrimitiveType.Cube, leftArmPivot.transform, new Vector3(-0.02f, -0.02f, 0f), new Vector3(0.22f, 0.20f, 0.24f), matSecondary);
            CreateVisualPrimitive("LeftForearm", PrimitiveType.Capsule, leftArmPivot.transform, new Vector3(0f, -0.24f, 0.08f), new Vector3(0.16f, 0.22f, 0.16f), matPrimary);

            GameObject rightArmPivot = new GameObject("RightArmPivot");
            rightArmPivot.transform.SetParent(visualRoot.transform, false);
            rightArmPivot.transform.localPosition = new Vector3(0.38f, 1.24f, 0f);
            CreateVisualPrimitive("RightShoulder", PrimitiveType.Cube, rightArmPivot.transform, new Vector3(0.02f, -0.02f, 0f), new Vector3(0.22f, 0.20f, 0.24f), matSecondary);
            CreateVisualPrimitive("RightForearm", PrimitiveType.Capsule, rightArmPivot.transform, new Vector3(0f, -0.24f, 0.12f), new Vector3(0.16f, 0.22f, 0.16f), matPrimary);

            // Pulse Rifle mounted to VisualRoot forward stance
            GameObject weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(visualRoot.transform, false);
            weaponAnchor.transform.localPosition = new Vector3(0.24f, 1.04f, 0.34f);
            CreateVisualPrimitive("RifleReceiver", PrimitiveType.Cube, weaponAnchor.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.14f, 0.18f, 0.62f), matSecondary);
            CreateVisualPrimitive("RifleRail", PrimitiveType.Cube, weaponAnchor.transform, new Vector3(0f, 0.06f, 0.36f), new Vector3(0.08f, 0.06f, 0.56f), matAccent);
            GameObject muzzlePoint = new GameObject("MuzzlePoint");
            muzzlePoint.transform.SetParent(weaponAnchor.transform, false);
            muzzlePoint.transform.localPosition = new Vector3(0f, 0.02f, 0.72f);

            // Legs
            GameObject leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.15f, 0.74f, 0f);
            CreateVisualPrimitive("LeftLeg", PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.20f, 0.68f, 0.22f), matSecondary);

            GameObject rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.15f, 0.74f, 0f);
            CreateVisualPrimitive("RightLeg", PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.20f, 0.68f, 0.22f), matSecondary);

            // Thruster Pack (secondary lag pivot)
            GameObject secondaryPivot = new GameObject("SecondaryPivot");
            secondaryPivot.transform.SetParent(visualRoot.transform, false);
            secondaryPivot.transform.localPosition = new Vector3(0f, 1.12f, -0.24f);
            CreateVisualPrimitive("JetpackBody", PrimitiveType.Cube, secondaryPivot.transform, Vector3.zero, new Vector3(0.36f, 0.40f, 0.18f), matSecondary);
            CreateVisualPrimitive("ThrusterGlowL", PrimitiveType.Cylinder, secondaryPivot.transform, new Vector3(-0.10f, -0.18f, -0.02f), new Vector3(0.09f, 0.08f, 0.09f), matCyan);
            CreateVisualPrimitive("ThrusterGlowR", PrimitiveType.Cylinder, secondaryPivot.transform, new Vector3(0.10f, -0.18f, -0.02f), new Vector3(0.09f, 0.08f, 0.09f), matCyan);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot.transform, rightArmPivot.transform, leftLegPivot.transform, rightLegPivot.transform, secondaryPivot.transform);
            player.BindComponents(inputAsset, cc, motion, weapon, root.GetComponentsInChildren<MeshRenderer>(true));

            return player;
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
            Sprite crosshairSprite)
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

            // Top-Center Wave & Score Pill
            RectTransform centerPill = CreatePanel("StatusPill_Center", hudGroup.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 74f), roundedSprite, Palette.PanelSurface);
            CreateImage("PillAccent", centerPill, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 3f), solidSprite, Palette.CyanGlow, false);

            TextMeshProUGUI waveText = CreateStyledText("WaveText", centerPill, "WAVE 01", 22f, FontStyles.Bold, 3.5f, Palette.CharacterPrimary, TextAlignmentOptions.TopLeft);
            SetAnchoredRect(waveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(150f, 28f));

            TextMeshProUGUI enemiesText = CreateStyledText("EnemiesText", centerPill, "8 HOSTILES", 13f, FontStyles.Bold, 2.5f, Palette.EnemySecondary, TextAlignmentOptions.BottomLeft);
            SetAnchoredRect(enemiesText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 12f), new Vector2(160f, 22f));

            TextMeshProUGUI scoreText = CreateStyledText("ScoreText", centerPill, "0 PTS", 22f, FontStyles.Bold, 2.5f, Palette.Accent, TextAlignmentOptions.MidlineRight);
            SetAnchoredRect(scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(165f, 44f));

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

            RectTransform titleCard = CreatePanel("TitleCard", titleGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 380f), roundedSprite, Palette.WithAlpha(Palette.PanelSurface, 0.92f));
            CreateImage("TitleAccentTop", titleCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.Accent, false);

            TextMeshProUGUI gameTitle = CreateStyledText("GameTitleText", titleCard, "VANGUARD", 58f, FontStyles.Bold, 8f, Palette.CharacterPrimary, TextAlignmentOptions.Center);
            SetAnchoredRect(gameTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(560f, 76f));

            TextMeshProUGUI subtitle = CreateStyledText("SubtitleText", titleCard, "NEON ARENA // TACTICAL SURVIVAL", 18f, FontStyles.Bold, 5f, Palette.CyanGlow, TextAlignmentOptions.Center);
            SetAnchoredRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(560f, 30f));

            TextMeshProUGUI titleBest = CreateStyledText("TitleHighScoreText", titleCard, "HIGH SCORE // 0", 16f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(titleBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(460f, 28f));

            TextMeshProUGUI controlsHint = CreateStyledText("ControlsHintText", titleCard, "WASD MOVE  ·  MOUSE AIM & FIRE  ·  SHIFT DASH  ·  ESC PAUSE", 13f, FontStyles.Bold, 2.2f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(controlsHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(580f, 24f));

            Button startBtn = CreateButton("StartMissionButton", titleCard, "START MISSION", new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(280f, 62f), roundedSprite, Palette.Accent, Palette.Shadow, 22f);

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

            RectTransform endedCard = CreatePanel("EndedCard", endedGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540f, 410f), roundedSprite, Palette.PanelSurface);
            CreateImage("EndedAccentTop", endedCard, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(-24f, 5f), solidSprite, Palette.EnemySecondary, false);

            TextMeshProUGUI endedTitle = CreateStyledText("EndedTitleText", endedCard, "SIGNAL LOST", 38f, FontStyles.Bold, 6f, Palette.EnemySecondary, TextAlignmentOptions.Center);
            SetAnchoredRect(endedTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(460f, 52f));

            TextMeshProUGUI endedScore = CreateStyledText("EndedScoreText", endedCard, "0 PTS", 44f, FontStyles.Bold, 3f, Palette.Accent, TextAlignmentOptions.Center);
            SetAnchoredRect(endedScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(460f, 58f));

            TextMeshProUGUI endedBest = CreateStyledText("EndedBestText", endedCard, "BEST // 0", 18f, FontStyles.Bold, 3.5f, Palette.CyanGlow, TextAlignmentOptions.Center);
            SetAnchoredRect(endedBest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(460f, 28f));

            TextMeshProUGUI endedStats = CreateStyledText("EndedStatsText", endedCard, "ELIMINATIONS: 0   |   WAVE REACHED: 1", 15f, FontStyles.Bold, 2f, Palette.MutedText, TextAlignmentOptions.Center);
            SetAnchoredRect(endedStats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(480f, 26f));

            Button eReplayBtn = CreateButton("EndedReplayButton", endedCard, "REPLAY", new Vector2(0.5f, 0.5f), new Vector2(-115f, -124f), new Vector2(205f, 56f), roundedSprite, Palette.Accent, Palette.Shadow, 20f);
            Button eTitleBtn = CreateButton("EndedTitleButton", endedCard, "MAIN MENU", new Vector2(0.5f, 0.5f), new Vector2(115f, -124f), new Vector2(205f, 56f), roundedSprite, Palette.SecondaryButtonBg, Palette.CharacterPrimary, 18f);

            UIManager ui = systemsRoot.AddComponent<UIManager>();
            ui.BindAll(
                titleGroup, hudGroup, pauseGroup, endedGroup,
                hpFill, xpFill, dashFill,
                hpValueText, levelText, waveText, enemiesText, scoreText,
                crosshairImg.rectTransform,
                bannerCg, bannerRt, milestoneTxt,
                titleBest,
                musicSlider, sfxSlider, musicPct, sfxPct,
                endedTitle, endedScore, endedBest, endedStats,
                startBtn, hudPauseBtn, resumeBtn, pRestartBtn, pTitleBtn, eReplayBtn, eTitleBtn);

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
