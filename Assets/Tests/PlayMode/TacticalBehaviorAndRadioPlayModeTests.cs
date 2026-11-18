using System.Collections;
using Audio;
using Camera;
using Character;
using Core;
using Enemies;
using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class TacticalBehaviorAndRadioPlayModeTests
    {
        [TearDown]
        public void CleanUpCampaignState()
        {
            AudioManager.Instance?.StopRadioVoice();
            SaveManager.EnsureInstance().ResetCampaignRoster();
        }

        [UnityTest]
        public IEnumerator Mission01_DynamicGateCarving_SquadBehaviorTree_EnemyBehaviorTree_AndRadioDspWork()
        {
            SaveManager.EnsureInstance().ResetCampaignRoster();
            yield return SceneManager.LoadSceneAsync("Mission01_BorderOutpost", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.IsNotNull(GameManager.Instance);
            Assert.IsNotNull(AudioManager.Instance);
            Assert.IsNotNull(SquadDirector.Instance);
            Assert.IsNotNull(EnemySpawner.Instance);
            Assert.IsNotNull(CutsceneDirector.Instance);

            // 1. Verify Radio DSP chain & real Kokoro clip loading
            var highPass = AudioManager.Instance.GetComponentInChildren<AudioHighPassFilter>(true);
            var lowPass = AudioManager.Instance.GetComponentInChildren<AudioLowPassFilter>(true);
            var distortion = AudioManager.Instance.GetComponentInChildren<AudioDistortionFilter>(true);
            Assert.IsNotNull(highPass, "Expected AudioHighPassFilter on radio channel");
            Assert.IsNotNull(lowPass, "Expected AudioLowPassFilter on radio channel");
            Assert.IsNotNull(distortion, "Expected AudioDistortionFilter on radio channel");
            Assert.GreaterOrEqual(highPass.cutoffFrequency, 450f);
            Assert.LessOrEqual(lowPass.cutoffFrequency, 3000f);

            var intelLine = StoryDirector.IntelCatalog[0].DiscoveryDialogue[0];
            var clipLength = AudioManager.Instance.PlayRadioLineAudio(intelLine.Callsign, intelLine.Body);
            Assert.IsTrue(AudioManager.Instance.HasRadioVoiceLoaded, $"Failed to play Kokoro clip for {intelLine.Callsign}: {intelLine.Body}");
            Assert.Greater(clipLength, 0.8f);
            Assert.IsTrue(AudioManager.Instance.IsRadioVoicePlaying, "Radio audio source should remain actively playing after trigger.");

            // Verify minor tactical ROE chatter never interrupts active story radio speech
            StoryDirector.TransmitConversation(intelLine);
            yield return null;
            StoryDirector.NotifySquadOrder("Closing diamond formation on Lead.");
            yield return null;
            Assert.IsNotNull(UIManager.Instance);
            Assert.AreEqual(intelLine.Callsign, UIManager.Instance.LastDisplayedRadioCallsign,
                "Low-priority squad order should not truncate an active story conversation line mid-speech.");

            AudioManager.Instance.StopRadioVoice();

            // 2. Enter Combat PlayMode
            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.35f);

            // 3. Verify Dynamic Gate Carving (closed blocks south->north center gate; OpenGateImmediate opens NavMesh path)
            var southSampleOk = NavMesh.SamplePosition(new Vector3(0f, 0f, -20f), out var southHit, 3.0f, NavMesh.AllAreas);
            var northSampleOk = NavMesh.SamplePosition(new Vector3(0f, 0f, 40f), out var northHit, 4.0f, NavMesh.AllAreas);
            Assert.IsTrue(southSampleOk && northSampleOk, "Expected valid NavMesh points south and north of gates.");

            var blockedPath = new NavMeshPath();
            NavMesh.CalculatePath(southHit.position, northHit.position, NavMesh.AllAreas, blockedPath);
            Assert.AreNotEqual(NavMeshPathStatus.PathComplete, blockedPath.status, "Closed blast gates should prevent path completion to North Pier.");

            CutsceneDirector.Instance.OpenGateImmediate(1);
            CutsceneDirector.Instance.OpenGateImmediate(2);
            yield return new WaitForSeconds(0.25f);

            var openPath = new NavMeshPath();
            NavMesh.CalculatePath(southHit.position, northHit.position, NavMesh.AllAreas, openPath);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, openPath.status, "Opening Blast Gates 1 & 2 should carve open a complete NavMesh path to North Pier.");

            // 4. Verify Squad NavMeshAgent & Fluid Behavior Tree states
            var squadMembers = SquadDirector.Instance.Members;
            Assert.GreaterOrEqual(squadMembers.Count, 3);
            foreach (var member in squadMembers)
            {
                Assert.IsNotNull(member.NavAgent);
                Assert.IsTrue(member.NavAgent.enabled);
                Assert.IsTrue(member.NavAgent.isOnNavMesh, $"{member.Callsign} failed to bind onto NavMesh.");
                Assert.IsFalse(string.IsNullOrEmpty(member.CurrentBehaviorState));
            }

            // Trigger critical bleedout on SquadMember 0 -> verify BT transitions to BLEEDOUT & halts agent
            var rourke = squadMembers[0];
            rourke.TakeDamage(999f);
            yield return null;
            Assert.IsTrue(rourke.IsDowned);
            Assert.AreEqual("BLEEDOUT", rourke.CurrentBehaviorState);
            Assert.IsTrue(rourke.NavAgent.isStopped);

            Assert.IsTrue(rourke.ApplyEmergencyTraumaPatch());
            yield return null;
            Assert.IsFalse(rourke.IsDowned);
            Assert.AreNotEqual("BLEEDOUT", rourke.CurrentBehaviorState);

            // 5. Verify Enemy NavMeshAgent & Fluid Behavior Tree active evaluation
            Assert.Greater(EnemySpawner.Instance.ActiveEnemies.Count, 0, "Expected initial Mission 1 enemies active.");
            EnemyController enemy = null;
            for (var i = EnemySpawner.Instance.ActiveEnemies.Count - 1; i >= 0; i--)
            {
                var candidate = EnemySpawner.Instance.ActiveEnemies[i];
                if (candidate != null && !candidate.IsDead)
                {
                    enemy = candidate;
                    break;
                }
            }
            Assert.IsNotNull(enemy, "Expected a living Mission 1 enemy.");
            Assert.IsNotNull(enemy.NavAgent);
            Assert.IsTrue(enemy.NavAgent.enabled);
            Assert.IsTrue(enemy.NavAgent.isOnNavMesh);

            enemy.SetGarrisonAlert(true, 45f);
            yield return new WaitForSeconds(0.25f);
            Assert.IsTrue(
                enemy.CurrentBehaviorState == "ADVANCE_CORRIDOR"
                || enemy.CurrentBehaviorState == "FLANK_LOS"
                || enemy.CurrentBehaviorState == "SUPPRESS_FIRE"
                || enemy.CurrentBehaviorState == "TAKE_COVER",
                $"Unexpected enemy BT state after alert: {enemy.CurrentBehaviorState}");
        }

        [UnityTest]
        public IEnumerator Mission05_AegisCitadel_ChenBossOverdrive_And8StageProgressionWork()
        {
            SaveManager.EnsureInstance().ResetCampaignRoster();
            yield return SceneManager.LoadSceneAsync("Mission05_AegisCitadel", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.IsNotNull(GameManager.Instance);
            Assert.IsNotNull(EnemySpawner.Instance);
            Assert.AreEqual(5, StoryDirector.ActiveMissionIndex);
            Assert.IsNotNull(EnemySpawner.Instance.DefenseHoldNode);
            Assert.IsNotNull(EnemySpawner.Instance.ExfilNode);

            GameManager.Instance.StartMission();
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(0, SquadDirector.Instance.AliveRosterCount, "Act V should deploy Vance as sole survivor.");

            // Jump to Stage 6 Final Boss encounter (Dir. Marcus Chen // AEGIS-ACTUAL)
            EnemySpawner.Instance.BeginWave(6);
            yield return new WaitForSeconds(0.15f);
            CutsceneDirector.Instance?.SkipCurrentCutscene();
            yield return new WaitForSeconds(0.15f);

            EnemyController chenBoss = null;
            for (var i = 0; i < EnemySpawner.Instance.ActiveEnemies.Count; i++)
            {
                var candidate = EnemySpawner.Instance.ActiveEnemies[i];
                if (candidate.Archetype == EnemyArchetype.Boss)
                {
                    chenBoss = candidate;
                    break;
                }
            }
            Assert.IsNotNull(chenBoss, "Expected Dir. Chen Boss to spawn on Mission 5 Stage 6.");
            Assert.IsTrue(chenBoss.DisplayCallsign.Contains("CHEN"), $"Unexpected Act V boss callsign: {chenBoss.DisplayCallsign}");

            // Drop Chen below 52% HP to verify Phase 2 AEON Overdrive transition
            chenBoss.ApplyDamage(chenBoss.MaxHp * 0.55f, chenBoss.transform.position, Vector3.forward, isCrit: false, isExplosive: true);
            yield return null;
            Assert.Greater(chenBoss.CurrentHp, 0f);
            Assert.Less(chenBoss.CurrentHp, chenBoss.MaxHp * 0.50f);

            // Verify Stage 7 Core Choice -> Stage 8 Exfil Final Stand progression
            EnemySpawner.Instance.BeginWave(7);
            yield return null;
            Assert.IsTrue(EnemySpawner.Instance.DefenseHoldNode.IsUnlocked);

            GameManager.Instance.PresentAct4CoreChoice();
            Assert.AreEqual(GameState.Decision, GameManager.Instance.CurrentState);
            GameManager.Instance.ResolveAct4CoreChoice(CampaignEndingKind.BroadcastBlackout);
            yield return null;

            Assert.AreEqual(8, EnemySpawner.Instance.CurrentStage);
            Assert.IsTrue(EnemySpawner.Instance.ExfilNode.IsUnlocked);
            Assert.AreEqual(GameState.Playing, GameManager.Instance.CurrentState);
        }
    }
}
