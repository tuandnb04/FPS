using System;
using System.Text;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Original 3-Act Tactical Thriller narrative coordinator for PROTOCOL ZERO // PROJECT HELIOS.
    /// Drives radio dispatches across Spectre Detachment Vanguard-09 (Sgt. Vance 'Nomad-1', Maj. Rourke,
    /// Spec. Kozlowski, Cpl. Reyes, and Dir. Chen) opposing the Ironward Syndicate across three scenes:
    /// Mission 01 (Blackwater Pier), Mission 02 (Iron Corridor), and Mission 03 (Blackcrest Summit).
    /// </summary>
    public static class StoryDirector
    {
        public const string OperationTitle = "PROTOCOL ZERO";
        public const string CampaignSubtitle = "PROJECT HELIOS // VANGUARD-09";
        public const int FinalCampaignStage = 6;
        public const int CampaignMissionCount = 3;
        public const int CampaignVictoryBonusCredits = 250;

        public static int ActiveMissionIndex { get; set; } = 1;
        public static int CurrentMissionIndex
        {
            get => ActiveMissionIndex;
            set => ActiveMissionIndex = Mathf.Clamp(value, 1, CampaignMissionCount);
        }

        private static int _lastKillMilestone;
        private static int _lastStageDispatched;
        private static bool _firstKillDispatched;

        public static string LastCallsign { get; private set; } = "DIR. CHEN";
        public static string LastChannel { get; private set; } = "AEGIS-ACTUAL";

        public static event Action<string, string, string> OnRadioTransmission;

        public static string GetMissionSceneName(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "Mission01_BorderOutpost",
                2 => "Mission02_RailDepot",
                _ => "Mission03_BlackcrestSummit"
            };
        }

        public static string GetMissionTitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "ACT I: BLACKWATER PIER",
                2 => "ACT II: IRON CORRIDOR",
                _ => "ACT III: SILENT PEAK"
            };
        }

        public static string GetMissionSubtitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "COASTAL CUSTOMS DOCKYARD // 21:40 HRS",
                2 => "AMBER GORGE RAIL DEPOT // 05:12 HRS",
                _ => "BLACKCREST ALPINE CITADEL // 23:55 HRS"
            };
        }

        public static string GetObjectiveNodeTitle(int missionIndex, int nodeKindIndex)
        {
            return (Mathf.Clamp(missionIndex, 1, CampaignMissionCount), nodeKindIndex) switch
            {
                (1, 0) => "PIER SECURITY GATE",
                (1, 1) => "HARBOR FUEL MANIFOLD",
                (1, 2) => "COASTAL RADAR SCRAMBLER",
                (1, 3) => "RAIL SIDING EXTRACTION",
                (2, 0) => "SWITCHYARD LOCK GATE",
                (2, 1) => "PUMP HOUSE VALVE",
                (2, 2) => "SIGNAL SWITCH MATRIX",
                (2, 3) => "GORGE TUNNEL EXIT",
                (3, 0) => "CITADEL PORTCULLIS",
                (3, 1) => "WEST HELIOS CAPACITOR",
                (3, 2) => "EAST HELIOS UPLINK",
                _ => "LZ VALKYRIE-6"
            };
        }

        public static void ResetCampaignRun()
        {
            _lastKillMilestone = 0;
            _lastStageDispatched = 0;
            _firstKillDispatched = false;
            LastCallsign = "MAJ. ROURKE";
            LastChannel = "IRONWOOD-1";
        }

        public static void NotifyMissionDeployed()
        {
            switch (ActiveMissionIndex)
            {
                case 1:
                    Transmit(
                        "MAJ. ROURKE",
                        "IRONWOOD-1 // PIER",
                        "Weapons free, Nomad. Clear Ironward sentries on Blackwater Pier — track where those Helios cores shipped.");
                    break;
                case 2:
                    Transmit(
                        "MAJ. ROURKE",
                        "IRONWOOD-1 // RAIL",
                        "Dawn window's closing! Push the Iron Corridor switchyard and cut Kastner's armored train off.");
                    break;
                default:
                    Transmit(
                        "MAJ. ROURKE",
                        "IRONWOOD-1 // SUMMIT",
                        "Final ascent, Vanguard-09. Breach Blackcrest Citadel, overload Helios, and take Radek down.");
                    break;
            }
        }

        public static void NotifyKillCount(int totalKills)
        {
            if (!_firstKillDispatched && totalKills >= 1)
            {
                _firstKillDispatched = true;
                Transmit(
                    "KOZLOWSKI",
                    "WRAITH-2",
                    "First Ironward hostile down. Keep muzzle lanes crisp — fuel drums by their sandbags burn hot.");
                return;
            }

            if (totalKills >= 12 && _lastKillMilestone < 12)
            {
                _lastKillMilestone = 12;
                Transmit(
                    "CPL. REYES",
                    "SPARK-3",
                    "Twelve contacts neutralized! Keep stacking corners — Ironward QRF is rattling.");
            }
            else if (totalKills >= 24 && _lastKillMilestone < 24)
            {
                _lastKillMilestone = 24;
                Transmit(
                    "DIR. CHEN",
                    "AEGIS-ACTUAL",
                    "High combat efficiency across Spectre Detachment. Keep pressure on their command net.");
            }
        }

        public static void NotifyStageStarted(int stage)
        {
            if (stage <= _lastStageDispatched) return;
            _lastStageDispatched = stage;

            switch (ActiveMissionIndex)
            {
                case 1:
                    NotifyMission1Stage(stage);
                    break;
                case 2:
                    NotifyMission2Stage(stage);
                    break;
                default:
                    NotifyMission3Stage(stage);
                    break;
            }
        }

        private static void NotifyMission1Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Eliminate outer customs sentries on Blackwater Pier to reach Security Gate 1.");
                    break;
                case 2:
                    Transmit("CPL. REYES", "SPARK-3", "Outer pier clear — armored Ironward APC 'Warden-01' rolling onto the apron!");
                    break;
                case 3:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Inside the container docks! Rig C4 on the Harbor Fuel Manifold and Coastal Radar — [E] at each site.");
                    break;
                case 4:
                    Transmit("KOZLOWSKI", "WRAITH-2", "Customs gate breached! Heavy siege squad defending Dragic's railhead.");
                    break;
                case 5:
                    Transmit("CAPT. DRAGIC", "IRONWARD NET", "You made it past the containers, Nomad — let's see your squad survive Siege-01!");
                    break;
                case 6:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Manifest decrypted! Warhead train departed north for Iron Corridor Gorge — move to Rail Extraction!");
                    break;
            }
        }

        private static void NotifyMission2Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Clear Ironward rail sentries blocking Track Alpha to reach the Switchyard Lock Gate.");
                    break;
                case 2:
                    Transmit("CPL. REYES", "SPARK-3", "Switchyard heavy armor inbound! 'Goliath-02' rail defense unit dead ahead!");
                    break;
                case 3:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "We're into the freight yard! Sabotage the Pump House Valve and Signal Matrix — [E] to plant.");
                    break;
                case 4:
                    Transmit("KOZLOWSKI", "WRAITH-2", "Rail grid offline! Heavy shock detail guarding the north gorge spur.");
                    break;
                case 5:
                    Transmit("DIR. KASTNER", "IRONWARD NET", "Too slow, Spectre! The primary Helios core is already rolling up to Blackcrest Peak!");
                    break;
                case 6:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Kastner's dead — Radek slipped north with the core! Breach the Gorge Tunnel Exit to pursue!");
                    break;
            }
        }

        private static void NotifyMission3Stage(int stage)
        {
            switch (stage)
            {
                case 1:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Break through Citadel outer guard post to unlock the Fortress Portcullis.");
                    break;
                case 2:
                    Transmit("CPL. REYES", "SPARK-3", "Heavy bastion cannon 'Vulcan-03' locking the bridge approach!");
                    break;
                case 3:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Inside Helios plaza! Overload both East and West Capacitor Pylons before orbital arming!");
                    break;
                case 4:
                    Transmit("KOZLOWSKI", "WRAITH-2", "Capacitors destabilizing! Final Praetorian guard blocking the summit ramp!");
                    break;
                case 5:
                    Transmit("COL. RADEK", "APEX-01 ENCRYPTED", "Helios rewrites the balance of power tonight, Vanguard. Step onto my pad and die!");
                    break;
                case 6:
                    Transmit("DIR. CHEN", "AEGIS-ACTUAL", "Apex-01 is down and Helios is dead! Board Blackhawk VALKYRIE-6 on the summit pad!");
                    break;
            }
        }

        public static void NotifySabotageProgress(int completed, int total, string targetName)
        {
            if (completed < total)
            {
                Transmit(
                    "KOZLOWSKI",
                    "WRAITH-2",
                    $"{targetName} sabotaged ({completed}/{total}). Push the remaining target!");
            }
            else
            {
                Transmit(
                    "MAJ. ROURKE",
                    "IRONWOOD-1",
                    "Both tactical targets destroyed! Heavy Ironward armor rolling in from north lane!");
            }
        }

        public static void NotifyGate1C4Ready()
        {
            Transmit(
                "DIR. CHEN",
                "AEGIS-ACTUAL",
                "Outer gate area clear. Move to the amber C4 breach marker and detonate the lock!");
        }

        public static void NotifyKillstreakReady(string summary)
        {
            Transmit(
                "DIR. CHEN",
                "AEGIS-ACTUAL",
                summary);
        }

        public static void NotifySquadOrder(string message)
        {
            Transmit(
                "KOZLOWSKI",
                "VANGUARD-09 NET",
                message);
        }

        public static void NotifySquadDowned(string callsign)
        {
            Transmit(
                "CPL. REYES",
                "TRAUMA NET",
                $"{callsign} is down! Hold cover — stabilizing vitals in the field!");
        }

        public static void NotifyBossSpawned(int stage)
        {
            var boss = GetBossCallsign(stage);
            if (stage <= 2)
            {
                Transmit(
                    "CPL. REYES",
                    "SPARK-3",
                    $"Heavy armor visual: {boss}! Focus fire or use explosive HE!");
            }
            else if (stage <= 4)
            {
                Transmit(
                    "KOZLOWSKI",
                    "WRAITH-2",
                    $"Sector lock guardian {boss} engaged! Cut their flanking arcs!");
            }
            else
            {
                Transmit(
                    "MAJ. ROURKE",
                    "IRONWOOD-1",
                    $"HVT confirmed: {boss}! Bring everything Vanguard-09 carries!");
            }
        }

        public static void NotifyBossDefeated(int stage)
        {
            if (stage <= 2)
            {
                Transmit(
                    "MAJ. ROURKE",
                    "IRONWOOD-1",
                    "Outer armor scrapped! Plant C4 on the security gate — [E] on the marker!");
            }
            else if (stage <= 4)
            {
                Transmit(
                    "DIR. CHEN",
                    "AEGIS-ACTUAL",
                    "Sector guardian neutralized! Final HVT sector dead ahead.");
            }
            else
            {
                Transmit(
                    "DIR. CHEN",
                    "AEGIS-ACTUAL",
                    ActiveMissionIndex < CampaignMissionCount
                        ? "Primary target eliminated! Exfil marker active — extract to advance to next sector!"
                        : "Col. Radek neutralized! Proceed to LZ VALKYRIE-6 and press [E] for Blackhawk extraction!");
            }
        }

        public static string GetBossCallsign(int stage)
        {
            return (Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount), stage <= 2 ? 2 : (stage <= 4 ? 4 : 5)) switch
            {
                (1, 2) => "IRONWARD APC 'WARDEN-01'",
                (1, 4) => "DOCK BREACHER 'HAMMER-01'",
                (1, 5) => "CAPT. DRAGIC // 'SIEGE-01'",
                (2, 2) => "RAIL TANK 'GOLIATH-02'",
                (2, 4) => "CONVOY SHIELD 'IRONWALL-02'",
                (2, 5) => "DIR. KASTNER // 'BASTION-02'",
                (3, 2) => "CITADEL CANNON 'VULCAN-03'",
                (3, 4) => "PRAETORIAN MECH 'AEGIS-03'",
                _ => "COL. RADEK // 'APEX-01'"
            };
        }

        public static string GetActLabel(int stage)
        {
            var clamped = Mathf.Clamp(stage, 1, FinalCampaignStage);
            return Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount) switch
            {
                1 => $"M1 // BLACKWATER PIER  ({clamped}/{FinalCampaignStage})",
                2 => $"M2 // IRON CORRIDOR  ({clamped}/{FinalCampaignStage})",
                _ => $"M3 // SILENT PEAK  ({clamped}/{FinalCampaignStage})"
            };
        }

        public static string GetSectorObjective(
            int stage,
            int s1Kills = 0,
            int s1Target = 8,
            int s2Done = 0,
            int s2Total = 2,
            bool gate1C4Ready = false)
        {
            var m = Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount);
            return stage switch
            {
                1 => m switch
                {
                    1 => $"Clear Pier Sentries ({s1Kills}/{Mathf.Max(1, s1Target)})",
                    2 => $"Clear Rail Pickets ({s1Kills}/{Mathf.Max(1, s1Target)})",
                    _ => $"Clear Citadel Outer Post ({s1Kills}/{Mathf.Max(1, s1Target)})"
                },
                2 => gate1C4Ready
                    ? $"Plant C4 at {GetObjectiveNodeTitle(m, 0)} [E]"
                    : $"Destroy {GetBossCallsign(2)}",
                3 => m switch
                {
                    1 => $"Sabotage Fuel & Radar ({s2Done}/{Mathf.Max(1, s2Total)})",
                    2 => $"Sabotage Pump & Rail Matrix ({s2Done}/{Mathf.Max(1, s2Total)})",
                    _ => $"Overload HELIOS Pylons ({s2Done}/{Mathf.Max(1, s2Total)})"
                },
                4 => $"Defeat {GetBossCallsign(4)} to Unlock Inner Gate",
                5 => $"Eliminate {GetBossCallsign(5)}",
                _ => $"Extract at {GetObjectiveNodeTitle(m, 3)} [E]"
            };
        }

        public static string GetEndedDebrief(bool victory, int stageReached, int kills)
        {
            var m = Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount);
            if (victory)
            {
                return m switch
                {
                    1 => $"BLACKWATER PIER SECURED ({kills} KIA) — Capt. Dragic's shipping manifest confirms Project HELIOS warheads left north by rail. Prepare for immediate canyon intercept in ACT II: IRON CORRIDOR.",
                    2 => $"IRON CORRIDOR CLEARED ({kills} KIA) — Dir. Kastner is down, but encrypted telemetry confirms Col. Radek already hauled the armed HELIOS core atop Blackcrest Citadel. Deploy immediately to ACT III: SILENT PEAK!",
                    _ => $"PROJECT HELIOS TERMINATED ({kills} KIA) — Spectre Detachment Vanguard-09 (Vance, Rourke, Kozlowski & Reyes) destroyed the summit array, eliminated Col. Radek, and extracted aboard VALKYRIE-6."
                };
            }

            return $"MIA IN MISSION 0{m} (STAGE {stageReached}/{FinalCampaignStage} | {kills} KIA) — Spectre Detachment hit heavy Ironward resistance. Requisition upgraded hardware in the Armory and redeploy.";
        }

        public static string BuildIntelDossierText(int bestWaveReached)
        {
            var save = SaveManager.EnsureInstance();
            var cleared = save.Data.highestMissionCompleted;
            var sb = new StringBuilder();
            sb.AppendLine("<b>PROTOCOL ZERO // PROJECT HELIOS — CLASSIFIED BRIEFING</b>");
            sb.AppendLine($"UNIT: Spectre Detachment (Vanguard-09)  |  CLEARANCES: {cleared}/{CampaignMissionCount} ACTS COMPLETED  |  PEAK STAGE: {bestWaveReached}/{FinalCampaignStage}");
            sb.AppendLine();

            sb.AppendLine("<b>ACT I — BLACKWATER PIER (MISSION 01)</b>");
            sb.AppendLine("Infiltrate Ironward's coastal customs dockyard at dusk, neutralize Warden-01 armor, sabotage coastal EW radar and harbor fuel lines, and interrogate Capt. Marko Dragic's shipping terminal.");
            sb.AppendLine(cleared >= 1
                ? "<color=#49C96E>[DECRYPTED LOG]: Train #774 carrying three seismic HELIOS cores departed inland through Iron Corridor Canyon.</color>"
                : "<color=#A6926F>[STATUS]: Active Priority — Clear Blackwater Pier to decrypt cargo manifest.</color>");
            sb.AppendLine();

            sb.AppendLine("<b>ACT II — IRON CORRIDOR (MISSION 02)</b>");
            sb.AppendLine("Intercept Ironward's armored rail convoy at dawn inside Iron Corridor Gorge, disable Goliath-02 rail armor, cut switchyard power, and defeat Dir. Elena Kastner.");
            sb.AppendLine(cleared >= 2
                ? "<color=#49C96E>[DECRYPTED LOG]: Kastner staged the gorge ambush as a delay — Col. Artem Radek is already arming HELIOS at Blackcrest Peak.</color>"
                : "<color=#A6926F>[STATUS]: Locked telemetry — Defeat Dir. Kastner in Mission 02.</color>");
            sb.AppendLine();

            sb.AppendLine("<b>ACT III — SILENT PEAK (MISSION 03)</b>");
            sb.AppendLine("Storm Blackcrest Alpine Citadel by moonlight, overload twin HELIOS EMP capacitor pylons, defeat rogue Col. Radek, and extract aboard Blackhawk VALKYRIE-6.");
            sb.AppendLine(cleared >= 3
                ? "<color=#49C96E>[OPERATION COMPLETE]: Project HELIOS orbital array neutralised cleanly. All Spectre operatives extracted alive.</color>"
                : "<color=#A6926F>[STATUS]: Final Strike — Eliminate Apex-01 aboard Blackcrest Summit.</color>");

            return sb.ToString();
        }

        private static void Transmit(string callsign, string channel, string body)
        {
            LastCallsign = callsign;
            LastChannel = channel;
            OnRadioTransmission?.Invoke(callsign, channel, body);
        }
    }
}
