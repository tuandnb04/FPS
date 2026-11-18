using System.Collections;
using System.Collections.Generic;
using Audio;
using Camera;
using Character;
using Combat;
using Core;
using Enemies;
using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FPS.Tests.PlayMode
{
    public sealed class ReloadWeaponsAndEnemyTypesPlayModeTests
    {
        private int _savedCredits;
        private int _savedWeapon;
        private int _savedFirepowerRank;
        private int _savedArmorRank;
        private List<int> _savedUnlockedWeapons;

        [TearDown]
        public void RestoreSaveAndCleanup()
        {
            AudioManager.Instance?.StopRadioVoice();
            DroppedMagazine.ReturnAllActive();
            var save = SaveManager.EnsureInstance();
            save.ResetCampaignRoster();
            if (_savedUnlockedWeapons != null)
            {
                save.Data.credits = _savedCredits;
                save.Data.selectedWeapon = _savedWeapon;
                save.Data.firepowerRank = _savedFirepowerRank;
                save.Data.armorRank = _savedArmorRank;
                save.Data.unlockedWeapons = new List<int>(_savedUnlockedWeapons);
                save.Save();
            }
        }

        [UnityTest]
        public IEnumerator ExpandedArsenal_MechanicalReload_ShielderCounterplay_SniperAndIntelWork()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.firepowerRank = 0;
            save.Data.armorRank = 0;

            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.25f);

            var player = PlayerController.Instance;
            Assert.IsNotNull(player);
            Assert.IsNotNull(player.Weapon);
            Assert.IsNotNull(EnemySpawner.Instance);
            Assert.IsNotNull(EnemySpawner.Instance.IntelNodeC, "Expected 3rd blackbox intel terminal in Mission 1.");

            // Unlock & test [7] M249 SAW & [8] M32 MGL + mechanical reload action + physical DroppedMagazine
            save.Data.credits += 1500;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M249Saw, 380));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M32Launcher, 520));

            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.M249Saw));
            Assert.AreEqual(WeaponType.M249Saw, player.Weapon.CurrentWeapon);
            Assert.AreEqual(100, player.Weapon.CurrentMagCapacity);

            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            Assert.AreEqual(99, player.Weapon.CurrentMagAmmo);

            DroppedMagazine.ReturnAllActive();
            Assert.IsTrue(player.Weapon.StartReload(false));
            Assert.IsTrue(player.Weapon.IsReloading);
            Assert.GreaterOrEqual(DroppedMagazine.ActiveCount, 1, "Reloading should eject a physical DroppedMagazine prop.");

            yield return new WaitForSeconds(0.25f);
            Assert.IsTrue(player.Motion.IsInReloadPose, "ProceduralCharacterMotion should enter multi-phase IK reload pose.");
            Assert.Greater(player.Motion.ReloadNormalizedProgress, 0.04f);

            // Switch to M32 Rotary Grenade Launcher and test Armory firepower + armor progression scaling
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.M32Launcher));
            Assert.AreEqual(6, player.Weapon.CurrentMagCapacity);
            var armorBefore = player.MaxArmor;
            save.Data.firepowerRank = 4;
            save.Data.armorRank = 1;
            player.ApplySaveLoadout();
            Assert.AreEqual(8, player.Weapon.CurrentMagCapacity, "Armory Ordnance upgrade should boost M32 drum from 6 to 8 rounds.");
            Assert.Greater(player.MaxArmor, armorBefore);

            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            Assert.AreEqual(7, player.Weapon.CurrentMagAmmo);
            player.Weapon.InstantRefillCurrentMagazine();
            Assert.AreEqual(8, player.Weapon.CurrentMagAmmo);

            // Test Shielder directional ballistic block + explosive shield shatter, and Sniper presence in Act I
            EnemyController shielder = null;
            EnemyController sniper = null;
            for (var i = 0; i < EnemySpawner.Instance.ActiveEnemies.Count; i++)
            {
                var e = EnemySpawner.Instance.ActiveEnemies[i];
                if (e.Archetype == EnemyArchetype.Shielder) shielder = e;
                else if (e.Archetype == EnemyArchetype.Sniper) sniper = e;
            }
            Assert.IsNotNull(shielder, "Expected Mission 1 Stage 1 formation to contain a Riot Shielder.");
            Assert.IsNotNull(sniper, "Expected Mission 1 Stage 1 formation to contain a Sniper.");
            Assert.IsTrue(shielder.HasIntactRiotShield);

            var hpBefore = shielder.CurrentHp;
            shielder.ApplyDamage(50f, shielder.transform.position + shielder.transform.forward, -shielder.transform.forward, false, false);
            var frontalDealt = hpBefore - shielder.CurrentHp;
            Assert.Less(frontalDealt, 15f, "Frontal ballistic rounds should be deflected by Riot Shield.");
            Assert.IsTrue(shielder.HasIntactRiotShield);

            shielder.ApplyDamage(40f, shielder.transform.position, -shielder.transform.forward, false, true);
            Assert.IsFalse(shielder.HasIntactRiotShield, "Explosive blast should shatter Riot Shield.");

            // Test Sniper laser telegraph activation in clear line of sight before Squad clears the courtyard
            sniper.SetGarrisonAlert(true, 45f);
            yield return new WaitForSeconds(0.15f);
            Assert.IsTrue(sniper.NavAgent.isOnNavMesh);
            var sniperLaser = sniper.GetComponentInChildren<LineRenderer>(true);
            Assert.IsNotNull(sniperLaser, "Sniper must have a laser sight LineRenderer.");
            Assert.IsTrue(sniperLaser.sharedMaterial == null || sniperLaser.sharedMaterial.mainTexture == null, "Sniper laser material must use clean untextured Sprites/Default, not radial ring texture.");
            Assert.LessOrEqual(sniperLaser.startWidth, 0.03f, "Sniper laser width should match crisp player laser sight gauge.");

            // Test VFX particle pool release (never leak hundreds of active ParticleSystems in Scene View)
            Assert.IsNotNull(Feel.VFXManager.Instance);
            Feel.VFXManager.Instance.ClearAllActiveParticles();
            Feel.VFXManager.Instance.Play(Feel.VfxKind.PickupBurst, player.transform.position);
            yield return new WaitForSeconds(0.65f);
            var activePickupBursts = 0;
            var root = Feel.VFXManager.Instance.PoolRoot;
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i).gameObject;
                if (child.name == "VFX_PickupBurst" && child.activeSelf)
                {
                    activePickupBursts++;
                }
            }
            Assert.AreEqual(0, activePickupBursts, "Finished PickupBurst ParticleSystem must return cleanly to VFXManager ObjectPool.");
            Assert.LessOrEqual(root.childCount, 96, "VFX pool should stay bounded instead of leaking hundreds of ParticleSystems.");

            DroppedMagazine.ReturnAllActive();
        }

        [UnityTest]
        public IEnumerator DetachableMagazines_ShellByShellShotgun_AndRpgWarheadReload_WorkDistinctly()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.firepowerRank = 0;
            save.Data.armorRank = 0;
            save.Data.credits += 3000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.ScatterShotgun, 120));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.HellfireLauncher, 280));

            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.20f);

            var player = PlayerController.Instance;
            Assert.IsNotNull(player);
            Assert.AreEqual(SaveManager.WeaponCount, player.Weapon.WeaponConfigs.Count, "WeaponController must expose 8 Inspector-editable WeaponConfigs.");

            // 1. Box-mag Rifle (M4A1): detaches WeaponMagVisual at start, drops STANAG mag with IDENTICAL mesh/material/scale, re-attaches when seated
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.PulseRifle));
            var m4MagVisual = player.transform.Find("VisualRoot/WeaponAnchor/Weapon_PulseRifle/WeaponMagVisual")?.gameObject;
            Assert.IsNotNull(m4MagVisual, "M4A1 should have a detachable WeaponMagVisual child.");
            Assert.IsTrue(m4MagVisual.activeSelf);
            var m4MagMesh = m4MagVisual.GetComponent<MeshFilter>()?.sharedMesh;
            var m4MagMat = m4MagVisual.GetComponent<MeshRenderer>()?.sharedMaterial;
            var m4MagScale = m4MagVisual.transform.lossyScale;

            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            DroppedMagazine.ReturnAllActive();
            Assert.IsTrue(player.Weapon.StartReload(true));
            Assert.IsFalse(m4MagVisual.activeSelf, "Box-mag reload should detach/hide gun's attached magazine while spent mag drops.");
            Assert.GreaterOrEqual(DroppedMagazine.ActiveCount, 1);

            var droppedMag = Object.FindAnyObjectByType<DroppedMagazine>();
            Assert.IsNotNull(droppedMag, "Expected spawned DroppedMagazine on reload.");
            var droppedMesh = droppedMag.GetComponent<MeshFilter>()?.sharedMesh;
            var droppedMat = droppedMag.GetComponent<MeshRenderer>()?.sharedMaterial;
            Assert.AreSame(m4MagMesh, droppedMesh, "Dropped magazine mesh MUST match WeaponMagVisual mesh 1:1.");
            Assert.AreSame(m4MagMat, droppedMat, "Dropped magazine material MUST match WeaponMagVisual material 1:1.");
            Assert.LessOrEqual(Vector3.Distance(m4MagScale, droppedMag.transform.localScale), 0.02f, "Dropped magazine initial scale must match WeaponMagVisual scale.");

            yield return new WaitForSeconds(1.25f);
            Assert.IsFalse(player.Weapon.IsReloading);
            Assert.IsTrue(m4MagVisual.activeSelf, "Completed box-mag reload must re-attach WeaponMagVisual.");

            // 2. Shotgun (M870): shell-by-shell reload (+1 per step, live counter on HUD, NO box magazine dropped, fire-interruptible)
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.ScatterShotgun));
            Assert.AreEqual(ReloadStyle.ShellByShellShotgun, player.Weapon.ActiveReloadStyle);
            player.Weapon.InstantRefillCurrentMagazine();
            yield return new WaitForSeconds(0.55f);

            for (var shot = 0; shot < 3; shot++)
            {
                Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
                yield return new WaitForSeconds(0.50f);
            }
            Assert.AreEqual(5, player.Weapon.CurrentMagAmmo);

            DroppedMagazine.ReturnAllActive();
            Assert.IsTrue(player.Weapon.StartReload(true));
            Assert.AreEqual(0, DroppedMagazine.ActiveCount, "Shotgun shell-by-shell reload must NOT eject a box magazine.");
            yield return new WaitForSeconds(0.35f);
            Assert.IsTrue(player.Weapon.IsReloading, "3-shell shotgun reload should still be active after 1 shell.");
            Assert.AreEqual(6, player.Weapon.CurrentMagAmmo, "Shotgun should increment ammo by +1 shell rather than refilling all at once.");
            Assert.IsTrue(UIManager.Instance.DisplayedAmmoLabel.Contains("06 / 08"), $"Expected HUD to display live shell count during shotgun reload, got: {UIManager.Instance.DisplayedAmmoLabel}");

            // Fire interrupt mid-reload
            var preInterruptAmmo = player.Weapon.CurrentMagAmmo;
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward), "Pressing fire while shotgun has >=1 shell should interrupt reload and fire immediately.");
            Assert.IsFalse(player.Weapon.IsReloading, "Firing during shotgun shell reload must cancel the reload.");
            Assert.AreEqual(preInterruptAmmo - 1, player.Weapon.CurrentMagAmmo);

            // 3. RPG-7 (HellfireLauncher): front-loaded warhead disappears while reloading without dropping a box mag
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.HellfireLauncher));
            var rpgWarheadVisual = player.transform.Find("VisualRoot/WeaponAnchor/Weapon_Rocket/WeaponMagVisual")?.gameObject;
            Assert.IsNotNull(rpgWarheadVisual, "RPG-7 should have a separable front warhead WeaponMagVisual child.");
            Assert.IsTrue(rpgWarheadVisual.activeSelf);
            player.Weapon.InstantRefillCurrentMagazine();
            yield return new WaitForSeconds(0.72f);

            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            DroppedMagazine.ReturnAllActive();
            Assert.IsTrue(player.Weapon.StartReload(true));
            Assert.AreEqual(0, DroppedMagazine.ActiveCount, "RPG-7 front warhead load must NOT drop a box magazine.");
            Assert.IsFalse(rpgWarheadVisual.activeSelf, "RPG-7 warhead visual should hide while tube is empty/reloading.");

            yield return new WaitForSeconds(1.65f);
            Assert.IsFalse(player.Weapon.IsReloading);
            Assert.IsTrue(rpgWarheadVisual.activeSelf, "RPG-7 warhead should reappear on muzzle once loaded.");

            DroppedMagazine.ReturnAllActive();
        }

        [UnityTest]
        public IEnumerator Projectiles_EmergeAheadOfMuzzleTip_NeverBackwardIntoHandguard()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.credits += 5000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.PlasmaRailgun, 220));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.An94Abakan, 260));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.VectorSmg, 300));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M32Launcher, 520));

            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.15f);

            var player = PlayerController.Instance;
            Assert.IsNotNull(player);
            Assert.IsNotNull(player.Weapon);
            player.transform.rotation = Quaternion.identity;

            var testedWeapons = new[]
            {
                WeaponType.PulseRifle,
                WeaponType.PlasmaRailgun,
                WeaponType.An94Abakan,
                WeaponType.VectorSmg,
                WeaponType.M32Launcher
            };

            foreach (var weaponType in testedWeapons)
            {
                Assert.IsTrue(player.TrySwitchWeapon((int)weaponType));
                player.Weapon.InstantRefillCurrentMagazine();
                yield return new WaitForSeconds(0.58f);

                var shotDir = new Vector3(player.transform.forward.x, 0f, player.transform.forward.z);
                shotDir = shotDir.sqrMagnitude > 0.001f ? shotDir.normalized : Vector3.forward;

                var existing = new HashSet<Projectile>(Object.FindObjectsByType<Projectile>());
                Assert.IsTrue(player.Weapon.TryFire(true, shotDir), $"Expected {weaponType} TryFire to succeed after cooldown.");

                Projectile spawned = null;
                foreach (var p in Object.FindObjectsByType<Projectile>())
                {
                    if (!existing.Contains(p))
                    {
                        spawned = p;
                        break;
                    }
                }
                Assert.IsNotNull(spawned, $"Expected firing {weaponType} to spawn a Projectile.");

                var goName = weaponType switch
                {
                    WeaponType.PulseRifle => "Weapon_PulseRifle",
                    WeaponType.PlasmaRailgun => "Weapon_Railgun",
                    WeaponType.An94Abakan => "Weapon_AN94",
                    WeaponType.VectorSmg => "Weapon_VectorSMG",
                    _ => "Weapon_M32"
                };
                var activeTip = player.transform.Find($"VisualRoot/WeaponAnchor/{goName}/MuzzleTip");
                var leftGrip = activeTip.parent.Find("LeftHandGrip");
                Assert.IsNotNull(activeTip);
                Assert.IsNotNull(leftGrip);

                var tipProj = Vector3.Dot(activeTip.position, shotDir);
                var gripProj = Vector3.Dot(leftGrip.position, shotDir);
                Assert.Greater(tipProj, gripProj + 0.12f, $"{weaponType} MuzzleTip must stay well ahead of LeftHandGrip.");
                Assert.LessOrEqual(Vector3.Distance(spawned.transform.position, activeTip.position), 0.05f,
                    $"{weaponType} Projectile spawn origin must align with MuzzleTip, not handguard.");

                var flatBarrelFwd = new Vector3(activeTip.forward.x, 0f, activeTip.forward.z).normalized;
                var flatProjFwd = new Vector3(spawned.transform.forward.x, 0f, spawned.transform.forward.z).normalized;
                Assert.Less(Vector3.Angle(flatBarrelFwd, flatProjFwd), 0.1f,
                    $"{weaponType} Projectile trajectory must fly 100% straight along weapon barrel forward axis.");

                var renderer = spawned.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(renderer);
                var mf = renderer.GetComponent<MeshFilter>();
                var halfY = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.extents.y : 1f;
                var visualTf = renderer.transform;
                var bulletTailWorld = visualTf.position - visualTf.up * (halfY * visualTf.localScale.y);
                var tailAheadOfMuzzle = Vector3.Dot(bulletTailWorld - activeTip.position, shotDir);
                Assert.GreaterOrEqual(tailAheadOfMuzzle, 0f,
                    $"{weaponType} tracer tail offset ({tailAheadOfMuzzle:F3}m) must start at/ahead of MuzzleTip instead of poking backward into the handguard.");
            }

            // Also verify enemy tracer emerges ahead of spawn origin rather than poking backward into enemy rifle handguard
            var enemyExisting = new HashSet<Projectile>(Object.FindObjectsByType<Projectile>());
            var enemySpawn = player.transform.position + Vector3.right * 4f + Vector3.up * 1.2f;
            player.Weapon.SpawnEnemyProjectile(enemySpawn, Vector3.forward, 15f, 10f, Palette.EnemySecondary, new Vector3(0.06f, 0.34f, 0.06f), 1.5f);
            Projectile enemyProj = null;
            foreach (var p in Object.FindObjectsByType<Projectile>())
            {
                if (!enemyExisting.Contains(p))
                {
                    enemyProj = p;
                    break;
                }
            }
            Assert.IsNotNull(enemyProj);
            var enemyRenderer = enemyProj.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(enemyRenderer);
            Assert.GreaterOrEqual(enemyRenderer.bounds.min.z, enemySpawn.z,
                "Enemy tracer tail must start at/ahead of muzzle spawn origin.");
        }

        [UnityTest]
        public IEnumerator HudCrosshair_VisibleOnlyForDmrOrSniper_HiddenForAllOtherWeapons()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.credits += 6000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.ScatterShotgun, 100));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.PlasmaRailgun, 220));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.HellfireLauncher, 220));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.An94Abakan, 260));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.VectorSmg, 300));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M249Saw, 380));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M32Launcher, 520));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M16A4Burst, 180));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.Xm25Airburst, 480));

            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.15f);

            var player = PlayerController.Instance;
            var ui = UI.UIManager.Instance;
            Assert.IsNotNull(player);
            Assert.IsNotNull(ui);

            WeaponType[] noCrosshairWeapons =
            {
                WeaponType.PulseRifle,
                WeaponType.ScatterShotgun,
                WeaponType.HellfireLauncher,
                WeaponType.An94Abakan,
                WeaponType.VectorSmg,
                WeaponType.M249Saw,
                WeaponType.M32Launcher,
                WeaponType.M16A4Burst,
                WeaponType.Xm25Airburst
            };

            foreach (var wt in noCrosshairWeapons)
            {
                Assert.IsTrue(player.TrySwitchWeapon((int)wt));
                yield return null;
                Assert.IsFalse(ui.IsCrosshairVisible, $"HUD crosshair must remain hidden while wielding {wt}.");
            }

            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.PlasmaRailgun));
            yield return null;
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Assert.IsTrue(ui.IsCrosshairVisible, "HUD crosshair should appear when wielding MK14 EBR DMR.");
            }
        }

        [UnityTest]
        public IEnumerator Ui_Icons_Contrast_Intel_Modal_And_Objective_Prompt_Cleanup_Work()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ui = UIManager.Instance;
            Assert.IsNotNull(ui, "Expected UIManager instance in Mission01_BorderOutpost.");

            // 1. Check all HUD weapon slot icons & Armory weapon icons are assigned
            for (var i = 0; i < SaveManager.WeaponCount; i++)
            {
                var slotIconGo = GameObject.Find("WpnSlotIcon_" + i);
                Assert.IsNotNull(slotIconGo, $"Missing WpnSlotIcon_{i} on HUD.");
                var slotImg = slotIconGo.GetComponent<Image>();
                Assert.IsNotNull(slotImg.sprite, $"WpnSlotIcon_{i} must have a sprite assigned.");

                var shopIconGo = GameObject.Find("ShopWpnIcon_" + i);
                Assert.IsNotNull(shopIconGo, $"Missing ShopWpnIcon_{i} in Armory.");
                var shopImg = shopIconGo.GetComponent<Image>();
                Assert.IsNotNull(shopImg.sprite, $"ShopWpnIcon_{i} must have a sprite assigned.");
            }

            // 2. Check equipped weapon card contrast (dark surface + crisp bright text/accent, no white-on-gold wash)
            ui.RefreshArmoryUI();
            var eqWpnIndex = SaveManager.EnsureInstance().Data.selectedWeapon;
            var eqCardGo = GameObject.Find("ShopWeapon_" + eqWpnIndex);
            Assert.IsNotNull(eqCardGo);
            var eqBg = eqCardGo.GetComponent<Image>();
            var eqLblTransform = eqCardGo.transform.Find("Label");
            var eqLbl = eqLblTransform != null ? eqLblTransform.GetComponent<Graphic>() : null;
            Assert.IsNotNull(eqLbl);
            Assert.Less(eqBg.color.r, 0.40f, "Equipped Armory card must keep dark bronze surface for crisp contrast.");
            Assert.Greater(eqLbl.color.r, 0.70f, "Equipped Armory card text must stay bright and high-contrast.");

            // 3. Check Intel modal open & CloseIntel() public toggle
            ui.OpenIntel();
            Assert.IsTrue(ui.IsIntelOpen, "OpenIntel should set IsIntelOpen true.");
            ui.CloseIntel();
            Assert.IsFalse(ui.IsIntelOpen, "CloseIntel should close Intel dossier.");

            // 4. Verify objective approach prompt clears instead of sticking when moving back out
            GameManager.Instance.StartMission();
            yield return null;
            CutsceneDirector.Instance?.SkipCurrentCutscene();
            yield return null;
            var node = EnemySpawner.Instance.PrimaryBreachNode;
            var player = PlayerController.Instance;
            Assert.IsNotNull(node);
            Assert.IsNotNull(player);
            node.SetState(true, false);

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = node.transform.position + new Vector3(0f, 0f, -4.5f);
            if (cc != null) cc.enabled = true;
            yield return null;
            yield return null;
            Assert.IsNotEmpty(ui.CurrentContextPrompt, "Approaching active objective ring should show context prompt.");

            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0f, player.transform.position.y, -40f);
            if (cc != null) cc.enabled = true;
            yield return null;
            yield return null;
            Assert.AreEqual(string.Empty, ui.CurrentContextPrompt, "Moving out of objective proximity must clear context prompt instead of leaving it stuck.");
        }

        [UnityTest]
        public IEnumerator Combat_Ads_Bloom_Casings_WeaponAudio_And_ActHazards_Work()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.credits += 4000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.ScatterShotgun, 100));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.PlasmaRailgun, 220));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.VectorSmg, 300));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M32Launcher, 520));

            GameManager.Instance.StartMission();
            yield return null;
            CutsceneDirector.Instance?.SkipCurrentCutscene();
            yield return new WaitForSeconds(0.15f);

            var player = PlayerController.Instance;
            var audio = AudioManager.Instance;
            var spawner = EnemySpawner.Instance;
            Assert.IsNotNull(player);
            Assert.IsNotNull(audio);
            Assert.IsNotNull(spawner);

            // 1. ADS tightens weapon spread and enables tactical sight line
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.PulseRifle));
            var hipSpread = player.Weapon.GetEffectiveSpreadDeg(WeaponType.PulseRifle);
            player.SetSimulatedAimDownSights(true);
            yield return null;
            var adsSpread = player.Weapon.GetEffectiveSpreadDeg(WeaponType.PulseRifle);
            Assert.Less(adsSpread, hipSpread * 0.5f, "ADS must significantly tighten effective weapon spread.");
            Assert.IsTrue(player.Weapon.IsSightLineVisible, "ADS should enable tactical laser sight line.");

            // 2. Firing ballistic rifle while ADS aligns projectile directly with tactical laser sight line & ejects brass shell casings
            var sightLine = player.Weapon.transform.Find("TacticalLaserSight")?.GetComponent<LineRenderer>();
            Assert.IsNotNull(sightLine, "Expected TacticalLaserSight LineRenderer under WeaponController.");
            var aimTargetPt = player.transform.position + Vector3.forward * 14f;
            player.Weapon.SetAimInput(Vector3.forward, aimTargetPt);
            player.Weapon.SyncActiveMuzzle();
            var rifleSightStart = sightLine.GetPosition(0);
            var rifleSightEnd = sightLine.GetPosition(1);
            var rifleSightDir = new Vector3(rifleSightEnd.x - rifleSightStart.x, 0f, rifleSightEnd.z - rifleSightStart.z).normalized;

            var existingBeforeRifle = new HashSet<Projectile>(Object.FindObjectsByType<Projectile>());
            var casingsBefore = DroppedMagazine.TotalShellCasingsSpawned;
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward, aimTargetPt));
            Assert.Greater(DroppedMagazine.TotalShellCasingsSpawned, casingsBefore, "Ballistic weapons must eject brass shell casings.");
            Assert.Greater(player.Weapon.CurrentSpreadBloom, 0.1f, "Continuous firing must accumulate dynamic spread bloom.");

            Projectile rifleProj = null;
            foreach (var p in Object.FindObjectsByType<Projectile>())
            {
                if (!existingBeforeRifle.Contains(p)) { rifleProj = p; break; }
            }
            Assert.IsNotNull(rifleProj);
            var rifleProjDir = new Vector3(rifleProj.transform.forward.x, 0f, rifleProj.transform.forward.z).normalized;
            Assert.Less(Vector3.Angle(rifleSightDir, rifleProjDir), 0.25f, "ADS PulseRifle projectile must match tactical laser sight line.");

            // Also verify MK14 EBR DMR projectile alignment with ADS laser sight line
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.PlasmaRailgun));
            yield return new WaitForSeconds(0.08f);
            player.Weapon.SetAimInput(Vector3.forward, aimTargetPt);
            player.Weapon.SyncActiveMuzzle();
            var dmrSightStart = sightLine.GetPosition(0);
            var dmrSightEnd = sightLine.GetPosition(1);
            var dmrSightDir = new Vector3(dmrSightEnd.x - dmrSightStart.x, 0f, dmrSightEnd.z - dmrSightStart.z).normalized;

            var existingBeforeDmr = new HashSet<Projectile>(Object.FindObjectsByType<Projectile>());
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward, aimTargetPt));
            Projectile dmrProj = null;
            foreach (var p in Object.FindObjectsByType<Projectile>())
            {
                if (!existingBeforeDmr.Contains(p)) { dmrProj = p; break; }
            }
            Assert.IsNotNull(dmrProj);
            var dmrProjDir = new Vector3(dmrProj.transform.forward.x, 0f, dmrProj.transform.forward.z).normalized;
            Assert.Less(Vector3.Angle(dmrSightDir, dmrProjDir), 0.25f, "ADS PlasmaRailgun projectile must match tactical laser sight line.");

            player.SetSimulatedAimDownSights(false);
            yield return null;
            Assert.IsFalse(player.Weapon.IsSightLineVisible, "Releasing ADS must hide tactical laser sight line.");

            // 3. Weapon-distinct procedural audio clips
            var rifleClip = audio.GetWeaponClip(WeaponType.PulseRifle);
            var shotgunClip = audio.GetWeaponClip(WeaponType.ScatterShotgun);
            var dmrClip = audio.GetWeaponClip(WeaponType.PlasmaRailgun);
            var smgClip = audio.GetWeaponClip(WeaponType.VectorSmg);
            var launcherClip = audio.GetWeaponClip(WeaponType.M32Launcher);
            Assert.IsNotNull(rifleClip);
            Assert.AreNotSame(rifleClip, shotgunClip, "Shotgun must use its own distinct procedural clip.");
            Assert.AreNotSame(rifleClip, dmrClip, "MK14 EBR DMR must use its own distinct procedural clip.");
            Assert.AreNotSame(rifleClip, smgClip, "SMG must use its own distinct procedural clip.");
            Assert.AreNotSame(rifleClip, launcherClip, "Launcher must use its own distinct procedural clip.");

            // 4. Per-act environmental hazard system & ground scorch decals
            spawner.TriggerEnvironmentalHazardBeat();
            Assert.AreEqual("OFFSHORE NAVAL BARRAGE", spawner.ActiveEnvironmentalHazardName);

            Feel.VFXManager.Instance?.SpawnScorchMark(player.transform.position + Vector3.forward * 3f, 2.2f);
            yield return new WaitForSeconds(0.15f);
            Assert.IsNotNull(GameObject.Find("FX_ScorchDecal"), "Explosions/impacts should spawn ground scorch mark decals.");

            // 5. Squad [C] Breach designates priority target when enemies are alive
            if (spawner.ActiveEnemyCount > 0 && SquadDirector.Instance != null)
            {
                SquadDirector.Instance.SetOrder(SquadOrder.Breach);
                Assert.IsNotNull(SquadDirector.Instance.PriorityTarget, "[C] Assault order should designate a priority target.");
            }
        }

        [UnityTest]
        public IEnumerator SquadParity_BackwardRecoil_AdsPosture_EjectionPorts_ShotgunShells_And_ArticulatedLegs()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.credits += 4000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.ScatterShotgun, 100));

            GameManager.Instance.StartMission();
            yield return null;
            CutsceneDirector.Instance?.SkipCurrentCutscene();
            yield return new WaitForSeconds(0.2f);

            var player = PlayerController.Instance;
            Assert.IsNotNull(player);
            var motion = player.GetComponent<ProceduralCharacterMotion>();
            Assert.IsNotNull(motion);

            // 1. Editable EjectionPort transforms exist on weapons & M870 Shotgun carries side-saddle shells + ejects red hulls
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.ScatterShotgun));
            yield return null;
            var shotgunConfig = player.Weapon.WeaponConfigs[(int)WeaponType.ScatterShotgun];
            Assert.IsNotNull(shotgunConfig.ejectionPort, "Shotgun config must expose an editable EjectionPort transform.");
            var shotgunGo = player.transform.Find("VisualRoot/WeaponAnchor/Weapon_Shotgun");
            Assert.IsNotNull(shotgunGo);
            Assert.IsNotNull(shotgunGo.Find("ModularParts/SideShellCarrier"), "Shotgun must have SideShellCarrier.");
            Assert.IsNotNull(shotgunGo.Find("ModularParts/SideShell_0"), "Shotgun must mount visible 12ga side-saddle shells.");
            Assert.IsNotNull(shotgunGo.Find("ModularParts/SideShell_4"), "Shotgun must mount 5 visible 12ga side-saddle shells.");

            DroppedMagazine.ReturnAllActive();
            var casingsBeforeShotgun = DroppedMagazine.TotalShellCasingsSpawned;
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            Assert.Greater(DroppedMagazine.TotalShellCasingsSpawned, casingsBeforeShotgun, "Shotgun must eject spent shell hull.");

            var activeCasings = Object.FindObjectsByType<DroppedMagazine>();
            var foundRedHull = false;
            for (var i = 0; i < activeCasings.Length; i++)
            {
                var mr = activeCasings[i].GetComponentInChildren<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.color.r > 0.55f && mr.sharedMaterial.color.g < 0.25f)
                {
                    foundRedHull = true;
                    break;
                }
            }
            Assert.IsTrue(foundRedHull, "Shotgun ejected hull must use distinct red 12-gauge shell material.");

            // 2. True backward recoil (-Z kick + muzzle climb) immediately on fire frame
            Assert.Less(motion.CurrentWeaponRecoilOffset.z, -0.02f, "Weapon recoil must kick straight backward along -Z.");
            Assert.Greater(motion.CurrentRecoilPitchDeg, 1.0f, "Weapon recoil must elevate muzzle pitch upward.");

            // 3. Hipfire vs ADS weapon posture differs clearly
            var weaponAnchor = player.transform.Find("VisualRoot/WeaponAnchor");
            Assert.IsNotNull(weaponAnchor);
            player.SetSimulatedAimDownSights(false);
            yield return new WaitForSeconds(0.25f);
            var hipPos = weaponAnchor.localPosition;
            Assert.Less(motion.AdsBlend, 0.1f);

            player.SetSimulatedAimDownSights(true);
            yield return new WaitForSeconds(0.25f);
            var adsPos = weaponAnchor.localPosition;
            Assert.Greater(motion.AdsBlend, 0.9f);
            Assert.Greater(Vector3.Distance(hipPos, adsPos), 0.06f, "ADS posture must shift WeaponAnchor markedly from hipfire.");
            player.SetSimulatedAimDownSights(false);

            // 4. Articulated 3-joint legs on Player, Squadmates, and Enemies + human backward knee flex (+X) during movement
            var playerLeftKnee = player.transform.Find("VisualRoot/LeftLegPivot/LeftKneePivot");
            var playerRightKnee = player.transform.Find("VisualRoot/RightLegPivot/RightKneePivot");
            Assert.IsNotNull(playerLeftKnee, "Player must have LeftKneePivot.");
            Assert.IsNotNull(playerRightKnee, "Player must have RightKneePivot.");
            Assert.IsNotNull(playerLeftKnee.Find("LeftBootPivot"), "Player must have LeftBootPivot.");

            var prevEnabled = player.enabled;
            player.enabled = false;
            motion.ResetMotionState();
            for (var f = 0; f < 22; f++)
            {
                motion.StepMotion(Vector3.forward * 6.0f, 0f, isAimingWeapon: true);
                yield return null;
            }
            Assert.Greater(motion.CurrentLeftKneeAngle, 5f, "Running gait must flex articulated knees.");
            Assert.Greater(playerLeftKnee.localEulerAngles.x, 5f, "Knee flex must use positive X rotation (backward bend).");
            Assert.Less(playerLeftKnee.localEulerAngles.x, 120f, "Knee flex must remain anatomically valid.");
            player.enabled = prevEnabled;

            var enemies = Object.FindObjectsByType<EnemyController>();
            Assert.Greater(enemies.Length, 0);
            Assert.IsNotNull(enemies[0].transform.Find("VisualRoot/LeftLegPivot/LeftKneePivot"), "Enemy rigs must use articulated LeftKneePivot.");
            Assert.IsNotNull(enemies[0].transform.Find("VisualRoot/RightLegPivot/RightKneePivot"), "Enemy rigs must use articulated RightKneePivot.");

            // 5. Squadmates have articulated legs, editable EjectionPort, and eject shell casings when firing
            var squad = Object.FindObjectsByType<SquadMemberController>();
            Assert.Greater(squad.Length, 0);
            var shooter = squad[0];
            Assert.IsNotNull(shooter.transform.Find("VisualRoot/LeftLegPivot/LeftKneePivot"), "Squadmates must use articulated LeftKneePivot.");
            Assert.IsNotNull(shooter.transform.Find("VisualRoot/WeaponAnchor/EjectionPort"), "Squadmates must have editable EjectionPort.");

            DroppedMagazine.ReturnAllActive();
            var sqCasingsBefore = DroppedMagazine.TotalShellCasingsSpawned;
            var sqShotsBefore = shooter.ShotsFiredCount;
            if (shooter.NavAgent != null)
            {
                shooter.NavAgent.Warp(enemies[0].transform.position - enemies[0].transform.forward * 8.5f);
            }
            SquadDirector.Instance?.SetOrder(SquadOrder.Breach);

            var waitElapsed = 0f;
            while (shooter.ShotsFiredCount == sqShotsBefore && waitElapsed < 2.8f)
            {
                waitElapsed += Time.deltaTime;
                yield return null;
            }
            Assert.Greater(shooter.ShotsFiredCount, sqShotsBefore, "Squadmate should fire at nearby enemy in breach order.");
            Assert.Greater(DroppedMagazine.TotalShellCasingsSpawned, sqCasingsBefore, "Squadmate firing must eject shell casings.");
        }

        [UnityTest]
        public IEnumerator RealWorldWeapons_An94SamePoint_M16BurstOnly_And_Xm25Airburst_Work()
        {
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var save = SaveManager.EnsureInstance();
            _savedCredits = save.Data.credits;
            _savedWeapon = save.Data.selectedWeapon;
            _savedFirepowerRank = save.Data.firepowerRank;
            _savedArmorRank = save.Data.armorRank;
            _savedUnlockedWeapons = new List<int>(save.Data.unlockedWeapons);
            save.ResetCampaignRoster();
            save.Data.credits += 6000;
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.An94Abakan, 140));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.M16A4Burst, 180));
            Assert.IsTrue(save.TryUnlockOrEquipWeapon((int)WeaponType.Xm25Airburst, 480));

            GameManager.Instance.StartMission();
            yield return null;
            CutsceneDirector.Instance?.SkipCurrentCutscene();
            yield return new WaitForSeconds(0.2f);

            var player = PlayerController.Instance;
            Assert.IsNotNull(player);
            player.transform.rotation = Quaternion.identity;

            // 1. AN-94 2-round 1800 RPM hyperburst places both rounds along the exact same trajectory (< 0.05 deg)
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.An94Abakan));
            yield return new WaitForSeconds(0.08f);
            var beforeAn94 = new HashSet<Projectile>(Object.FindObjectsByType<Projectile>());
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            yield return new WaitForSeconds(0.055f);

            var an94Projs = new List<Projectile>(2);
            foreach (var p in Object.FindObjectsByType<Projectile>())
            {
                if (!beforeAn94.Contains(p)) an94Projs.Add(p);
            }
            Assert.AreEqual(2, an94Projs.Count, "AN-94 initial trigger pull must fire a 2-round hyperburst.");
            var d0 = new Vector3(an94Projs[0].transform.forward.x, 0f, an94Projs[0].transform.forward.z).normalized;
            var d1 = new Vector3(an94Projs[1].transform.forward.x, 0f, an94Projs[1].transform.forward.z).normalized;
            Assert.Less(Vector3.Angle(d0, d1), 0.05f, "AN-94 2-round hyperburst must fire both projectiles onto the exact same point.");

            // 2. M16A4 BURST fires strictly 3 rounds per trigger pull and refuses full-auto until trigger release
            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.M16A4Burst));
            yield return new WaitForSeconds(0.08f);
            var prevPlayerEnabled = player.enabled;
            player.enabled = false;
            var m16ShotsBefore = player.Weapon.TotalShotsFired;
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));
            Assert.IsTrue(player.Weapon.IsBurstTriggerLatched, "M16A4 must latch trigger sear on burst start.");
            yield return new WaitForSeconds(0.28f);
            Assert.AreEqual(m16ShotsBefore + 3, player.Weapon.TotalShotsFired, "M16A4 must complete all 3 rounds of the burst.");
            Assert.IsFalse(player.Weapon.TryFire(true, Vector3.forward), "M16A4 burst-only rifle must NOT continue firing while trigger stays held.");
            Assert.IsFalse(player.Weapon.TryFire(false, Vector3.forward));
            Assert.IsFalse(player.Weapon.IsBurstTriggerLatched, "Releasing trigger must reset the M16A4 burst cam.");
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward), "Re-pulling trigger after release must start the next 3-round burst.");
            yield return new WaitForSeconds(0.15f);
            player.enabled = prevPlayerEnabled;

            // 3. XM25 PUNISHER 25mm smart grenade airbursts near enemies without direct contact
            var enemies = Object.FindObjectsByType<EnemyController>();
            Assert.Greater(enemies.Length, 0);
            EnemyController airburstTarget = null;
            for (var i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && !enemies[i].IsDead)
                {
                    airburstTarget = enemies[i];
                    break;
                }
            }
            Assert.IsNotNull(airburstTarget);
            if (airburstTarget.NavAgent != null)
            {
                airburstTarget.NavAgent.Warp(player.transform.position + Vector3.forward * 6.5f + Vector3.right * 1.4f);
            }
            else
            {
                airburstTarget.transform.position = player.transform.position + Vector3.forward * 6.5f + Vector3.right * 1.4f;
            }
            yield return null;

            Assert.IsTrue(player.TrySwitchWeapon((int)WeaponType.Xm25Airburst));
            yield return new WaitForSeconds(0.10f);
            var hpBefore = airburstTarget.CurrentHp;
            Assert.IsTrue(player.Weapon.TryFire(true, Vector3.forward));

            var waited = 0f;
            while (airburstTarget != null && airburstTarget.CurrentHp >= hpBefore && waited < 0.65f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Assert.Less(airburstTarget.CurrentHp, hpBefore, "XM25 25mm smart round must proximity-airburst beside off-axis enemy.");
        }
    }
}
