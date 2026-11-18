using System.Collections.Generic;
using Core;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.Tests.EditMode
{
    public sealed class NavMeshAndRadioVoiceEditModeTests
    {
        private static readonly string[] CampaignScenePaths =
        {
            "Assets/Scenes/Mission01_BorderOutpost.unity",
            "Assets/Scenes/Mission02_RailDepot.unity",
            "Assets/Scenes/Mission03_KestrelRelay.unity",
            "Assets/Scenes/Mission04_BlackcrestSummit.unity",
            "Assets/Scenes/Mission05_AegisCitadel.unity"
        };

        private string _initialScenePath;

        [OneTimeSetUp]
        public void CaptureInitialScene()
        {
            _initialScenePath = EditorSceneManager.GetActiveScene().path;
        }

        [OneTimeTearDown]
        public void RestoreInitialScene()
        {
            var targetPath = string.IsNullOrEmpty(_initialScenePath)
                ? CampaignScenePaths[0]
                : _initialScenePath;
            EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);
        }

        [Test]
        public void AllExpandedKokoroVoiceResources_ExistAndMatchStoryDirectorFnvKeys()
        {
            var allClips = Resources.LoadAll<AudioClip>("VoiceRadio");
            Assert.IsNotNull(allClips);
            Assert.GreaterOrEqual(allClips.Length, 140, "Expected synthesized Kokoro voice WAVs covering all 5 acts, cutscenes & 15 intel logs.");

            var seenKeys = new HashSet<string>();
            void AssertClipExists(string rawSpeakerOrCallsign, string body, bool normalizeSpeaker)
            {
                var callsign = normalizeSpeaker ? StoryDirector.NormalizeVoiceCallsign(rawSpeakerOrCallsign) : rawSpeakerOrCallsign;
                var key = StoryDirector.GetVoiceClipKey(callsign, body);
                Assert.IsTrue(key.StartsWith("vo_"), $"Unexpected clip key prefix: {key}");
                var clip = Resources.Load<AudioClip>("VoiceRadio/" + key);
                Assert.IsNotNull(clip, $"Missing Kokoro WAV asset Resources/VoiceRadio/{key}.wav for '{callsign}': {body}");
                Assert.Greater(clip.length, 0.35f, $"Empty clip duration for {key}");
                Assert.AreEqual(24000, clip.frequency, $"Unexpected sample rate for {key}");
                seenKeys.Add(key);
            }

            foreach (var entry in StoryDirector.IntelCatalog)
            {
                foreach (var line in entry.DiscoveryDialogue)
                {
                    AssertClipExists(line.Callsign, line.Body, normalizeSpeaker: false);
                }
            }

            for (var m = 1; m <= StoryDirector.CampaignMissionCount; m++)
            {
                foreach (var b in StoryDirector.GetIntroCutsceneBeats(m)) AssertClipExists(b.Speaker, b.Caption, normalizeSpeaker: true);
                foreach (var b in StoryDirector.GetMidBreachCutsceneBeats(m)) AssertClipExists(b.Speaker, b.Caption, normalizeSpeaker: true);
                foreach (var b in StoryDirector.GetBossCutsceneBeats(m)) AssertClipExists(b.Speaker, b.Caption, normalizeSpeaker: true);
                foreach (var b in StoryDirector.GetVictoryEvacBeats(m, false)) AssertClipExists(b.Speaker, b.Caption, normalizeSpeaker: true);
                foreach (var b in StoryDirector.GetVictoryEvacBeats(m, true)) AssertClipExists(b.Speaker, b.Caption, normalizeSpeaker: true);
            }

            // Spot-check critical story & boss lines verbatim from StoryDirector.cs
            var spotLines = new (string callsign, string body)[]
            {
                ("DIR. CHEN", "Keep your voice down on open band, Sergeant. Some wars only stay buried if nobody digs."),
                ("COL. RADEK", "Only one left? Chen paid forty million to clear Karst-4 and Kestrel off the map. Time to settle his bill."),
                ("DIR. CHEN", "Enough ghosts! If the Senate sees Karst-4 telemetry, three defense treaties burn overnight! Blackguard Exo deploying!"),
                ("VALKYRIE-6", "Valkyrie-6 on the deck, Sergeant! Every AEGIS black frequency is blowing up—get aboard before hunter teams close the ridge!"),
                ("COL. RADEK", "Mech hull wrecked! Switching to heavy exo-armor! Nobody walks off this summit alive!")
            };

            foreach (var (callsign, body) in spotLines)
            {
                AssertClipExists(callsign, body, normalizeSpeaker: false);
            }

            Assert.GreaterOrEqual(seenKeys.Count, 75);
        }

        [Test]
        public void AllFiveCampaignScenes_HaveBakedNavMeshSurfaceAndDynamicObstacles()
        {
            foreach (var scenePath in CampaignScenePaths)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var env = GameObject.Find("ArenaEnvironment");
                Assert.IsNotNull(env, $"ArenaEnvironment missing in {scenePath}");

                var surface = env.GetComponent<NavMeshSurface>();
                Assert.IsNotNull(surface, $"NavMeshSurface missing on ArenaEnvironment in {scenePath}");
                Assert.IsNotNull(surface.navMeshData, $"NavMeshData not baked in {scenePath}");

                var playerGo = GameObject.Find("Player");
                Assert.IsNotNull(playerGo, $"Player missing in {scenePath}");
                var sampledStart = NavMesh.SamplePosition(playerGo.transform.position, out var startHit, 3.0f, NavMesh.AllAreas);
                Assert.IsTrue(sampledStart, $"Player spawn {playerGo.transform.position} is not near walkable NavMesh in {scenePath}");

                var northPad = GameObject.Find("Helipad_Main") ?? GameObject.Find("NorthHelipad_PB");
                if (northPad != null)
                {
                    var sampledNorth = NavMesh.SamplePosition(northPad.transform.position, out _, 4.0f, NavMesh.AllAreas);
                    Assert.IsTrue(sampledNorth, $"North area not on walkable NavMesh in {scenePath}");
                }

                var obstacles = Object.FindObjectsByType<NavMeshObstacle>(FindObjectsInactive.Include);
                Assert.Greater(obstacles.Length, 0, $"Expected dynamic NavMeshObstacle carving components in {scenePath}");
                foreach (var obs in obstacles)
                {
                    Assert.IsTrue(obs.carving, $"NavMeshObstacle {obs.name} in {scenePath} should enable carving.");
                }
            }
        }
    }
}
