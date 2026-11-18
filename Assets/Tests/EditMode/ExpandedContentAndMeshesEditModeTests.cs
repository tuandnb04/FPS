using Combat;
using Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPS.Tests.EditMode
{
    public sealed class ExpandedContentAndMeshesEditModeTests
    {
        [Test]
        public void Arsenal_IntelCatalog_AndProBuilderHardSurfaceAssets_AreComplete()
        {
            Assert.AreEqual(10, SaveManager.WeaponCount, "Expected 10 tactical weapons including M16A4 Burst and XM25 Airburst.");
            Assert.AreEqual(5, StoryDirector.CampaignMissionCount, "Expected 5-Act campaign progression.");
            Assert.AreEqual(8, StoryDirector.FinalCampaignStage, "Expected 8 tactical stages per campaign act.");
            Assert.AreEqual(15, StoryDirector.IntelCatalog.Length, "Expected 15 campaign blackbox logs (3 per act across 5 acts).");
            Assert.AreEqual(4, (int)BossBehaviorKind.ChenOverwatch);
            Assert.AreEqual(6, (int)WeaponType.M249Saw);
            Assert.AreEqual(7, (int)WeaponType.M32Launcher);
            Assert.AreEqual(8, (int)WeaponType.M16A4Burst);
            Assert.AreEqual(9, (int)WeaponType.Xm25Airburst);

            string[] requiredMeshes =
            {
                "Assets/Models/ProBuilder/PB_Weapon_M249.asset",
                "Assets/Models/ProBuilder/PB_Weapon_M32.asset",
                "Assets/Models/ProBuilder/PB_DroppedMagazine.asset",
                "Assets/Models/ProBuilder/PB_RiotShield.asset",
                "Assets/Models/ProBuilder/PB_SniperOpticRifle.asset",
                "Assets/Models/ProBuilder/PB_FieldPack.asset",
                "Assets/Models/ProBuilder/PB_ArmorPauldron.asset",
                "Assets/Models/ProBuilder/PB_MunitionCrate.asset",
                "Assets/Models/ProBuilder/PB_SabotageTerminal.asset",
                "Assets/Models/ProBuilder/PB_C4Charge.asset"
            };

            foreach (var assetPath in requiredMeshes)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                Assert.IsNotNull(mesh, $"Missing ProBuilder hard-surface asset: {assetPath}");
                Assert.Greater(mesh.vertexCount, 24, $"Expected beveled multi-part mesh geometry in {assetPath}");
            }

            string[] forwardFacingWeapons =
            {
                "Assets/Models/ProBuilder/PB_Weapon_M249.asset",
                "Assets/Models/ProBuilder/PB_Weapon_M32.asset",
                "Assets/Models/ProBuilder/PB_SniperOpticRifle.asset"
            };
            foreach (var weaponPath in forwardFacingWeapons)
            {
                var wMesh = AssetDatabase.LoadAssetAtPath<Mesh>(weaponPath);
                var sz = wMesh.bounds.size;
                Assert.Greater(sz.z, sz.y, $"{weaponPath} should align barrel forward along +Z, not upright +Y (size={sz}).");
                Assert.Greater(sz.z, sz.x, $"{weaponPath} should have length along Z greater than width X (size={sz}).");
            }

            // Verify Blender hero character/vehicle/pickup meshes exist in SM_Vanguard_Assets.fbx (all 13 firearms use unified ProBuilder PB_Weapon_* below)
            var fbxAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/SM_Vanguard_Assets.fbx");
            Mesh FindFbxMesh(string meshName)
            {
                foreach (var obj in fbxAssets)
                {
                    if (obj is Mesh m && m.name == meshName) return m;
                }
                return null;
            }

            string[] requiredFbxHeroMeshes =
            {
                "Mesh_PlayerTorso",
                "Mesh_PlayerHelmet",
                "Mesh_Head_BoonieHat",
                "Mesh_Head_SkullMask",
                "Mesh_ChaserTorso",
                "Mesh_HeavyTorso",
                "Mesh_BossTorso",
                "Mesh_BossTurret",
                "Mesh_Vehicle_Blackhawk",
                "Mesh_Env_HMMWV",
                "Mesh_Pickup_Medkit",
                "Mesh_Pickup_CreditCoin"
            };
            foreach (var fbxName in requiredFbxHeroMeshes)
            {
                var m = FindFbxMesh(fbxName);
                Assert.IsNotNull(m, $"Missing Blender hero/vehicle FBX mesh {fbxName}");
                Assert.Greater(m.vertexCount, 24, $"Expected sculpted geometry in {fbxName}");
            }

            var m249Mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_M249.asset");
            var m32Mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_M32.asset");
            var sniperMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_SniperOpticRifle.asset");
            Assert.LessOrEqual(m249Mesh.bounds.max.y, 0.140f, "M249 SAW should use iron sights / carry handle without scope.");
            Assert.GreaterOrEqual(m249Mesh.bounds.min.y, -0.185f, "M249 SAW bipod legs should fold tucked under handguard rather than sticking down awkwardly.");
            Assert.LessOrEqual(m32Mesh.bounds.max.y, 0.156f, "M32 MGL should not carry the tall OpticReflexPod box.");
            Assert.GreaterOrEqual(sniperMesh.bounds.max.y, 0.145f, "Enemy Sniper rifle should keep its telescopic optic.");

            string[] unifiedProBuilderWeapons =
            {
                "Assets/Models/ProBuilder/PB_Weapon_PulseRifle.asset",
                "Assets/Models/ProBuilder/PB_Weapon_Shotgun.asset",
                "Assets/Models/ProBuilder/PB_Weapon_Railgun.asset",
                "Assets/Models/ProBuilder/PB_Weapon_Rocket.asset",
                "Assets/Models/ProBuilder/PB_Weapon_AN94.asset",
                "Assets/Models/ProBuilder/PB_Weapon_VectorSMG.asset",
                "Assets/Models/ProBuilder/PB_Weapon_M249.asset",
                "Assets/Models/ProBuilder/PB_Weapon_M32.asset",
                "Assets/Models/ProBuilder/PB_Weapon_M16A4.asset",
                "Assets/Models/ProBuilder/PB_Weapon_XM25.asset",
                "Assets/Models/ProBuilder/PB_Weapon_AK74.asset",
                "Assets/Models/ProBuilder/PB_Weapon_PKM.asset",
                "Assets/Models/ProBuilder/PB_SniperOpticRifle.asset"
            };
            foreach (var pbPath in unifiedProBuilderWeapons)
            {
                var pm = AssetDatabase.LoadAssetAtPath<Mesh>(pbPath);
                Assert.IsNotNull(pm, $"Missing unified ProBuilder weapon mesh {pbPath}");
                Assert.AreEqual(pm.vertexCount, pm.colors.Length, $"Unified weapon {pbPath} must include vertex color tones.");
                Assert.Greater(pm.bounds.size.z, pm.bounds.size.y, $"Unified weapon {pbPath} must orient barrel forward along +Z.");
            }

            var pbRailgun = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_Railgun.asset");
            var pbPulse = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_PulseRifle.asset");
            var pbShotgun = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_Shotgun.asset");
            var pbAn94 = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_AN94.asset");
            var pbVector = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_VectorSMG.asset");
            var pbRpg = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_Rocket.asset");
            var pbM16A4 = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_M16A4.asset");
            var pbXm25 = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/ProBuilder/PB_Weapon_XM25.asset");
            Assert.GreaterOrEqual(pbRailgun.bounds.max.y, 0.145f, "Unified PB_Weapon_Railgun MK14 EBR must have telescopic optic.");
            Assert.LessOrEqual(pbPulse.bounds.max.y, 0.120f, "Unified PB_Weapon_PulseRifle must use iron sights only.");
            Assert.LessOrEqual(pbShotgun.bounds.max.y, 0.105f, "Unified PB_Weapon_Shotgun must use iron sights only.");
            Assert.LessOrEqual(pbAn94.bounds.max.y, 0.095f, "Unified PB_Weapon_AN94 must use iron sights only.");
            Assert.LessOrEqual(pbVector.bounds.max.y, 0.115f, "Unified PB_Weapon_VectorSMG must use iron sights only.");
            Assert.LessOrEqual(pbRpg.bounds.max.y, 0.125f, "Unified PB_Weapon_Rocket must use iron sights only.");
            Assert.LessOrEqual(pbM16A4.bounds.max.y, 0.125f, "Unified PB_Weapon_M16A4 must use carry-handle iron sights only.");
            Assert.LessOrEqual(pbXm25.bounds.max.y, 0.125f, "Unified PB_Weapon_XM25 must use low-profile rangefinder bridge.");

            string[] detachableMags =
            {
                "Assets/Models/ProBuilder/PB_Mag_Stanag.asset",
                "Assets/Models/ProBuilder/PB_Mag_DmrBox.asset",
                "Assets/Models/ProBuilder/PB_Mag_AkCurved.asset",
                "Assets/Models/ProBuilder/PB_Mag_VectorStick.asset",
                "Assets/Models/ProBuilder/PB_Mag_M249Box.asset",
                "Assets/Models/ProBuilder/PB_Mag_RpgWarhead.asset",
                "Assets/Models/ProBuilder/PB_Mag_M32Cylinder.asset",
                "Assets/Models/ProBuilder/PB_Mag_ShotgunShell.asset"
            };
            foreach (var magPath in detachableMags)
            {
                var mm = AssetDatabase.LoadAssetAtPath<Mesh>(magPath);
                Assert.IsNotNull(mm, $"Missing detachable ordnance/magazine mesh {magPath}");
                Assert.Greater(mm.vertexCount, 24, $"Expected beveled multi-part geometry in {magPath}");
                Assert.AreEqual(mm.vertexCount, mm.colors.Length, $"Detachable magazine {magPath} must include vertex color tones.");
            }

            string[] requiredMaterials =
            {
                "Assets/Materials/Mat_WeaponSteel.mat",
                "Assets/Materials/Mat_WeaponPolymerDark.mat",
                "Assets/Materials/Mat_WeaponCoyoteTan.mat",
                "Assets/Materials/Mat_WeaponOliveDrab.mat",
                "Assets/Materials/Mat_WeaponWood.mat",
                "Assets/Materials/Mat_ShotgunShellRed.mat"
            };
            foreach (var matPath in requiredMaterials)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                Assert.IsNotNull(mat, $"Missing editable weapon material at {matPath}");
            }

            string[] editableWeaponPrefabs =
            {
                "Assets/Prefabs/Weapons/Prefab_Weapon_PulseRifle.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_Shotgun.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_Railgun.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_Rocket.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_AN94.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_VectorSMG.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_M249.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_M32.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_M16A4.prefab",
                "Assets/Prefabs/Weapons/Prefab_Weapon_XM25.prefab"
            };
            Assert.AreEqual(10, LayerMask.NameToLayer("Pickup"), "Expected Pickup layer in slot 10.");
            Assert.AreEqual(12, LayerMask.NameToLayer("Projectile"), "Expected Projectile layer in slot 12.");
            Assert.AreEqual(13, LayerMask.NameToLayer("WeaponProp"), "Expected WeaponProp layer in slot 13.");

            foreach (var prefabPath in editableWeaponPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefab, $"Missing editable weapon prefab at {prefabPath}");
                Assert.IsTrue(prefab.CompareTag("Weapon"), $"Expected Weapon tag on {prefabPath}");
                Assert.AreEqual(LayerMask.NameToLayer("WeaponProp"), prefab.layer, $"Expected WeaponProp layer on {prefabPath}");
                var modularRoot = prefab.transform.Find("ModularParts");
                Assert.IsNotNull(modularRoot, $"Expected ModularParts child hierarchy inside {prefabPath}");
                Assert.GreaterOrEqual(modularRoot.childCount, 4, $"Expected individual editable sub-parts inside {prefabPath}");
            }

            string[] editableMagPrefabs =
            {
                "Assets/Prefabs/Magazines/Prefab_Mag_Stanag.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_ShotgunShell.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_DmrBox.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_RpgWarhead.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_AkCurved.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_VectorStick.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_M249Box.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_M32Cylinder.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_M16Stanag.prefab",
                "Assets/Prefabs/Magazines/Prefab_Mag_Xm25Box.prefab"
            };
            foreach (var prefabPath in editableMagPrefabs)
            {
                var magPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(magPrefab, $"Missing editable magazine prefab at {prefabPath}");
                Assert.IsTrue(magPrefab.CompareTag("Magazine"), $"Expected Magazine tag on {prefabPath}");
                Assert.AreEqual(LayerMask.NameToLayer("WeaponProp"), magPrefab.layer, $"Expected WeaponProp layer on {prefabPath}");
                var mf = magPrefab.GetComponent<MeshFilter>();
                var mr = magPrefab.GetComponent<MeshRenderer>();
                Assert.IsNotNull(mf?.sharedMesh, $"Expected MeshFilter mesh on {prefabPath}");
                Assert.IsNotNull(mr?.sharedMaterial, $"Expected MeshRenderer material on {prefabPath}");
            }

            var prevMission = StoryDirector.ActiveMissionIndex;
            try
            {
                StoryDirector.ActiveMissionIndex = 4;
                Assert.AreEqual("SOUTH BRIDGE BLAST GATE C4", StoryDirector.GetObjectiveNodeTitle(4, 0));
                Assert.IsTrue(StoryDirector.GetSectorObjective(3).Contains("Breach SOUTH BRIDGE BLAST GATE C4"), "Act 4 Stage 3 must instruct breaching Gate 1.");
                Assert.IsTrue(StoryDirector.GetSectorObjective(4).Contains("Capacitor Pylons"), "Act 4 Stage 4 must instruct overloading Capacitor Pylons.");
            }
            finally
            {
                StoryDirector.ActiveMissionIndex = prevMission;
            }
        }

        [Test]
        public void Tactical_And_Weapon_Icon_Sprites_Exist_And_Are_Valid()
        {
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var wpnPath = $"Assets/Textures/Tex_Icon_Weapon_{i}.png";
                var spr = AssetDatabase.LoadAssetAtPath<Sprite>(wpnPath);
                Assert.IsNotNull(spr, $"Expected generated weapon silhouette icon sprite at {wpnPath}");
            }

            string[] requiredIcons =
            {
                "Assets/Textures/Tex_Icon_Speaker.png",
                "Assets/Textures/Tex_Icon_Frag.png",
                "Assets/Textures/Tex_Icon_Artillery.png",
                "Assets/Textures/Tex_Icon_Medal.png",
                "Assets/Textures/Tex_Icon_Intel.png",
                "Assets/Textures/Tex_Icon_Broadcast.png",
                "Assets/Textures/Tex_Icon_Lock.png",
                "Assets/Textures/Tex_Icon_Credits.png",
                "Assets/Textures/Tex_Icon_Play.png",
                "Assets/Textures/Tex_Icon_Restart.png",
                "Assets/Textures/Tex_Icon_Home.png"
            };
            foreach (var path in requiredIcons)
            {
                var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsNotNull(spr, $"Expected generated tactical UI icon sprite at {path}");
            }

            var medal = UISpriteFactory.GetMedalIcon();
            Assert.IsNotNull(medal);
            var tex = medal.texture;
            var centerAlpha = tex.GetPixel(tex.width / 2, tex.height / 2 + 2).a;
            var cornerAlpha = tex.GetPixel(0, 0).a;
            Assert.Greater(centerAlpha, 0.85f, "Medal star center must be opaque white, not inverted.");
            Assert.Less(cornerAlpha, 0.05f, "Medal outer corners must be transparent.");
        }

        [Test]
        public void FloorMaterials_HaveTacticalGridTexture_And_SceneLightsExist()
        {
            var floorTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Tex_TacticalFloorGrid.png");
            Assert.IsNotNull(floorTex, "Expected procedural floor grid texture at Assets/Textures/Tex_TacticalFloorGrid.png.");

            for (var m = 1; m <= StoryDirector.CampaignMissionCount; m++)
            {
                var floorMat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/Mat_Floor_M{m}.mat");
                var roadMat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/Mat_RoadAsphalt_M{m}.mat");
                Assert.IsNotNull(floorMat, $"Missing Mat_Floor_M{m}.mat");
                Assert.IsNotNull(roadMat, $"Missing Mat_RoadAsphalt_M{m}.mat");
                Assert.IsNotNull(floorMat.mainTexture, $"Mat_Floor_M{m} must have Tex_TacticalFloorGrid assigned.");
                Assert.IsNotNull(roadMat.mainTexture, $"Mat_RoadAsphalt_M{m} must have Tex_TacticalFloorGrid assigned.");
            }

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Mission04_BlackcrestSummit.unity", OpenSceneMode.Single);
            var lights = Object.FindObjectsByType<Light>();
            var pointCount = 0;
            foreach (var l in lights)
            {
                if (l.type == LightType.Point) pointCount++;
            }
            Assert.GreaterOrEqual(pointCount, 4, "Mission 4 should contain local URP Point lights on towers/pylons/floodlights.");
            EditorSceneManager.OpenScene("Assets/Scenes/Mission01_BorderOutpost.unity", OpenSceneMode.Single);
        }
    }
}
