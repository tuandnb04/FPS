using System;
using System.Text;
using UnityEngine;

namespace TopDownShooter.Core
{
    /// <summary>
    /// Grounded military campaign narrative director for OPERATION IRON HARVEST.
    /// Dispatches live TAC-NET radio transmissions across Acts I-III (Waves 1-9),
    /// provides boss callsigns, debrief summaries, and persistent Intel Dossier logs.
    /// </summary>
    public static class StoryDirector
    {
        public const string OperationTitle = "OPERATION IRON HARVEST";
        public const int FinalCampaignWave = 9;
        public const int CampaignVictoryBonusCredits = 250;

        public static event Action<string, string, string> OnRadioTransmission;

        private static int lastBroadcastWave;
        private static int lastBroadcastBossWave;
        private static int lastDefeatedBossWave;
        private static bool firstKillBroadcasted;

        public static string LastCallsign { get; private set; } = "OVERWATCH";
        public static string LastChannel { get; private set; } = "TAC-NET CH-04";
        public static string LastBody { get; private set; } = string.Empty;
        public static int TransmissionCount { get; private set; }

        public static void ResetCampaignRun()
        {
            lastBroadcastWave = 0;
            lastBroadcastBossWave = 0;
            lastDefeatedBossWave = 0;
            firstKillBroadcasted = false;
            LastCallsign = "OVERWATCH";
            LastChannel = "TAC-NET CH-04";
            LastBody = string.Empty;
            TransmissionCount = 0;
        }

        public static void NotifyMissionDeployed()
        {
            ResetCampaignRun();
            lastBroadcastWave = 1;
            Emit(
                "OVERWATCH",
                "TAC-NET CH-04",
                "Vanguard-1, you're weapons-free in Sector Echo-7. Kronos AK-74 scouts are sweeping the yard - use concrete barriers for cover.");
        }

        public static void NotifyKillCount(int runKills)
        {
            if (firstKillBroadcasted || runKills < 1)
            {
                return;
            }
            firstKillBroadcasted = true;
            Emit(
                "VANGUARD-1",
                "SQUAD-LOCAL",
                "One Kronos rifleman down. Intercepted comms confirm Col. Volkov locked down the Echo-7 fuel bays.");
        }

        public static void NotifyWaveStarted(int wave)
        {
            if (wave <= lastBroadcastWave)
            {
                return;
            }
            lastBroadcastWave = wave;

            switch (wave)
            {
                case 2:
                    Emit(
                        "OVERWATCH",
                        "TAC-NET CH-04",
                        "Heavy weapon signatures on thermal! Belt-fed PKM gunners pushing the flanks - move between their 3-round bursts.");
                    break;
                case 4:
                    Emit(
                        "OVERWATCH",
                        "ACT II // CH-04",
                        "Outer cordon broken. Act II active: Kronos supply platoons are moving reinforced AK-74 and PKM fireteams.");
                    break;
                case 5:
                    Emit(
                        "COL. VOLKOV",
                        "OPEN FREQ 148.2",
                        "Trespasser, this is Colonel Volkov. Kronos PMC owns Sector Echo-7. Drop your weapon or die in the dirt.");
                    break;
                case 7:
                    Emit(
                        "EVAC-6",
                        "AIR-GROUND CH-09",
                        "Vanguard-1, Evac-6 inbound ETA 3 mikes! Final Kronos shock platoons are converging on your LZ - hold the yard!");
                    break;
                case 8:
                    Emit(
                        "OVERWATCH",
                        "ACT III // PRIORITY",
                        "All enemy reserves committed! Expect dense PKM crossfire before Volkov deploys his command IFV.");
                    break;
            }
        }

        public static void NotifyBossSpawned(int wave)
        {
            if (wave <= lastBroadcastBossWave)
            {
                return;
            }
            lastBroadcastBossWave = wave;

            if (wave <= 3)
            {
                Emit(
                    "OVERWATCH",
                    "PRIORITY ALERT",
                    "Armored IFV breaching north gate! Neutralize IRONCLAD SCOUT-01 to crack Kronos outer AA coverage.");
            }
            else if (wave <= 6)
            {
                Emit(
                    "OVERWATCH",
                    "PRIORITY ALERT",
                    "Volkov deployed his guard IFV, IRONCLAD WARDEN-02! Heavy autocannon hot - watch red artillery zones!");
            }
            else
            {
                Emit(
                    "COL. VOLKOV",
                    "IRONCLAD ALPHA",
                    "You bled my platoons dry, Vanguard! Now face IRONCLAD ALPHA yourself - no airstrike can save you here!");
            }
        }

        public static void NotifyBossDefeated(int wave)
        {
            if (wave <= lastDefeatedBossWave)
            {
                return;
            }
            lastDefeatedBossWave = wave;

            if (wave <= 3)
            {
                Emit(
                    "VANGUARD-1",
                    "TAC-NET CH-04",
                    "Ironclad Scout-01 destroyed! Outer AA battery offline - pushing into Act II Fuel Depot.");
            }
            else if (wave <= 6)
            {
                Emit(
                    "VANGUARD-1",
                    "TAC-NET CH-04",
                    "Warden-02 is burning! Volkov's inner perimeter is collapsing - Evac-6, stand by on vector North.");
            }
            else
            {
                Emit(
                    "EVAC-6",
                    "DUSTOFF CH-01",
                    "Direct hit on Ironclad Alpha! Volkov is KIA and Kronos command is broken. Touchdown at LZ - great work, Vanguard-1!");
            }
        }

        public static string GetBossCallsign(int wave)
        {
            if (wave <= 3) return "IRONCLAD SCOUT-01";
            if (wave <= 6) return "IRONCLAD WARDEN-02";
            return "COL. VOLKOV // IRONCLAD ALPHA";
        }

        public static string GetActLabel(int wave)
        {
            if (wave <= 3) return "ACT I: PERIMETER BREACH";
            if (wave <= 6) return "ACT II: FUEL DEPOT";
            return "ACT III: IRON HARVEST";
        }

        public static string GetEndedDebrief(bool victory, int waveReached, int kills)
        {
            if (victory)
            {
                return $"OP IRON HARVEST COMPLETE - Col. Volkov's Ironclad Alpha destroyed on Wave {waveReached}. Evac-6 extracted Vanguard-1 (+{CampaignVictoryBonusCredits} CR bonus). Hostiles neutralized: {kills}.";
            }

            if (waveReached >= 7)
            {
                return $"EXTRACTION ABORTED IN ACT III (Wave {waveReached}) - Vanguard-1 fell minutes from Evac-6 touchdown after eliminating {kills} Kronos hostiles.";
            }
            if (waveReached >= 4)
            {
                return $"CONTACT LOST IN ACT II (Wave {waveReached}) - Kronos supply garrison pinned Vanguard-1 in the fuel depot ({kills} hostiles KIA).";
            }
            return $"INSERTION COMPROMISED IN ACT I (Wave {waveReached}) - Kronos perimeter scouts overwhelmed forward recon ({kills} hostiles KIA).";
        }

        public static string BuildIntelDossierText(int highestWave)
        {
            int clampedHighest = Mathf.Max(0, highestWave);
            StringBuilder sb = new StringBuilder(1024);

            sb.AppendLine("TASK FORCE BRIEFING // OPERATION IRON HARVEST");
            sb.AppendLine("Location: Echo-7 Logistics Yard | Mission: Eliminate Kronos PMC garrison & Col. Volkov's Ironclad IFVs (Wave 9 Clear = Full Victory).");
            sb.AppendLine();

            sb.AppendLine("[COMMAND & CALLSIGNS]");
            sb.AppendLine("- SGT. K. VANCE (VANGUARD-1): Forward Task Force operator dropped behind enemy AA lines to break Kronos fuel & armor staging.");
            sb.AppendLine("- LT. S. MERCER (OVERWATCH): High-altitude TAC-NET controller coordinating target telemetry and evac windows.");
            sb.AppendLine("- COL. VIKTOR VOLKOV (KRONOS-ACTUAL): Rogue mechanized commander inside IFV Ironclad Alpha, fortifying Echo-7.");
            sb.AppendLine("- CPT. H. TAKAHASHI (EVAC-6): UH-60 Dustoff pilot holding outside SAM range until Wave 9 IFV threat is neutralized.");
            sb.AppendLine();

            sb.AppendLine("[KRONOS PMC THREAT INTEL]");
            sb.AppendLine("- MILITIA RIFLEMAN (AK-74): Fast breach troopers engaging at mid-range with single-shot tracer bursts. Concrete barriers block incoming rounds.");
            sb.AppendLine("- HEAVY GUNNER (PKM): Reinforced shock infantry firing 3-round 7.62mm suppressive bursts; flank between reload cycles.");
            sb.AppendLine("- IRONCLAD IFV (AUTOCANNON + MORTAR): Deployed Waves 3, 6, and 9 with coaxial turret fire, radial shrapnel volleys, and red artillery danger zones.");
            sb.AppendLine();

            sb.AppendLine("[CAMPAIGN FIELD LOGS]");
            sb.AppendLine("- CHAPTER I [UNLOCKED]: Satellite sweeps detected armored fuel convoys staging under radar cover inside Echo-7.");
            if (clampedHighest >= 4)
            {
                sb.AppendLine("- CHAPTER II [UNLOCKED]: Decrypted AK-74 squad comms reveal Volkov personally commands the inner yard from Ironclad Alpha.");
            }
            else
            {
                sb.AppendLine("- CHAPTER II [LOCKED - Reach Wave 4]: Clear Wave 3 IFV Scout-01 to decrypt Kronos inner depot traffic.");
            }

            if (clampedHighest >= 7)
            {
                sb.AppendLine("- CHAPTER III [UNLOCKED]: AA grid severed. Survive Act III shock platoons and destroy Volkov at Wave 9 for Evac-6 extraction.");
            }
            else
            {
                sb.AppendLine("- CHAPTER III [LOCKED - Reach Wave 7]: Advance past Act II Fuel Depot to unlock extraction orders.");
            }

            return sb.ToString();
        }

        private static void Emit(string callsign, string channel, string body)
        {
            LastCallsign = callsign;
            LastChannel = channel;
            LastBody = body;
            TransmissionCount++;
            OnRadioTransmission?.Invoke(callsign, channel, body);
        }
    }
}
