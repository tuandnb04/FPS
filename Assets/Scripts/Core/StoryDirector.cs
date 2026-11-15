using System;
using System.Text;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Narrative campaign director grounded in Call of Duty: Modern Warfare 4 (2026) — Korean Peninsula operations.
    /// Drives radio dispatches between Capt. Price, Ghost, Gaz, and Pvt. Park Hyun-Jun against Konni Group forces in Hajin DMZ.
    /// </summary>
    public static class StoryDirector
    {
        public const string OperationTitle = "OPERATION NIGHTFALL";
        public const int FinalCampaignStage = 6;
        public const int FinalCampaignWave = FinalCampaignStage; // Legacy alias
        public const int CampaignVictoryBonusCredits = 250;

        public static event Action<string, string, string> OnRadioTransmission;

        private static int _lastBroadcastStage;
        private static int _lastBroadcastBossStage;
        private static int _lastDefeatedBossStage;
        private static bool _firstKillBroadcasted;
        private static int _lastSabotageBroadcasted;

        public static string LastCallsign { get; private set; } = "CPT. PRICE";
        public static string LastChannel { get; private set; } = "TF-141 ACTUAL";
        public static string LastBody { get; private set; } = string.Empty;
        public static int TransmissionCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomainStaticState()
        {
            OnRadioTransmission = null;
            ResetCampaignRun();
        }

        public static void ResetCampaignRun()
        {
            _lastBroadcastStage = 0;
            _lastBroadcastBossStage = 0;
            _lastDefeatedBossStage = 0;
            _firstKillBroadcasted = false;
            _lastSabotageBroadcasted = 0;
            LastCallsign = "CPT. PRICE";
            LastChannel = "TF-141 ACTUAL";
            LastBody = string.Empty;
            TransmissionCount = 0;
        }

        public static void NotifyMissionDeployed()
        {
            Emit(
                "CPT. PRICE",
                "TF-141 NET // DMZ",
                "Off-the-grid, lights out. Ghost, Gaz — clear the Hajin checkpoint and lock down Konni's forward lane.");
        }

        public static void NotifyKillCount(int runKills)
        {
            if (!_firstKillBroadcasted && runKills >= 1)
            {
                _firstKillBroadcasted = true;
                Emit(
                    "GHOST",
                    "OVERWATCH-1",
                    "First Konni contact dropped. Keep suppressors hot and watch fuel drums near their cover.");
            }
        }

        public static void NotifyWaveStarted(int stage)
        {
            NotifyStageStarted(stage);
        }

        public static void NotifyStageStarted(int stage)
        {
            if (stage <= _lastBroadcastStage) return;
            _lastBroadcastStage = stage;

            switch (stage)
            {
                case 1:
                    Emit(
                        "PVT. PARK // ROK 707",
                        "LOCAL TAC-LINK",
                        "Captain Price, Konni mercenaries seized the Hajin border compound. Neutralize 8 sentries so we can blow Gate Alpha.");
                    break;
                case 2:
                    Emit(
                        "SGT. GAZ",
                        "GARRICK-3",
                        "Blast Gate Alpha cleared for breach! Watch out — Konni IFV armor is rolling up to plug the choke.");
                    break;
                case 3:
                    Emit(
                        "CPT. PRICE",
                        "TF-141 ACTUAL",
                        "We're inside the DMZ courtyard. Plant charges on Konni's Fuel Manifold and the EW Jammer Uplink — press [E] at each site.");
                    break;
                case 4:
                    Emit(
                        "GHOST",
                        "OVERWATCH-1",
                        "Sabotage complete. Konni BTR-82A hunter-killer is pushing from the north depot — stay sharp, hit her flanks.");
                    break;
                case 5:
                    Emit(
                        "PVT. PARK // ROK 707",
                        "LOCAL TAC-LINK",
                        "Road to Hajin Helipad is open! Break through the last Juggernaut screen before Makarov's gunship lifts off.");
                    break;
                case 6:
                    Emit(
                        "KOMANDER VOLKOV",
                        "KONNI ENCRYPTED",
                        "Price... Task Force 141 never learns. Your signal dies right here on the Korean Peninsula!");
                    break;
            }
        }

        public static void NotifyGate1C4Ready()
        {
            Emit(
                "SGT. GAZ",
                "GARRICK-3",
                "Sentry picket's thin! Move to Blast Gate Alpha and press [E] to plant breaching C4.");
        }

        public static void NotifySabotageProgress(int completedCount, int totalCount, string targetLabel)
        {
            if (completedCount <= _lastSabotageBroadcasted) return;
            _lastSabotageBroadcasted = completedCount;

            if (completedCount < totalCount)
            {
                Emit(
                    "GHOST",
                    "OVERWATCH-1",
                    $"{targetLabel} destroyed ({completedCount}/{totalCount}). Push across the yard and take out the second node.");
            }
            else
            {
                Emit(
                    "CPT. PRICE",
                    "TF-141 ACTUAL",
                    "Fuel and EW jammers are scrap! Hold the compound — Konni heavy armor incoming.");
            }
        }

        public static void NotifyKillstreakReady(string streakLabel)
        {
            Emit(
                "LASWELL",
                "CI-SATCOM",
                $"{streakLabel} Confirmed on thermal over Hajin DMZ.");
        }

        public static void NotifySquadOrder(string orderLabel)
        {
            Emit(
                "GHOST",
                "TF-141 NET",
                orderLabel);
        }

        public static void NotifySquadDowned(string callsign)
        {
            Emit(
                callsign,
                "EMERGENCY",
                "Taking heavy fire — bleeding out! Need a field revive or cover!");
        }

        public static void NotifyBossSpawned(int stage)
        {
            if (stage <= _lastBroadcastBossStage) return;
            _lastBroadcastBossStage = stage;

            switch (stage)
            {
                case 2:
                    Emit(
                        "SGT. GAZ",
                        "GARRICK-3",
                        "Visual on Konni IFV 'Viper-01' blocking Gate Alpha! Switch to HE or focus fire!");
                    break;
                case 4:
                    Emit(
                        "GHOST",
                        "OVERWATCH-1",
                        "BTR-82A 'Krasny-04' breaching Sector 2! Watch the mortar telegraph rings and keep moving!");
                    break;
                default:
                    Emit(
                        "CPT. PRICE",
                        "TF-141 ACTUAL",
                        "It's Volkov's Command BTR — bring everything we have! Tear her armor apart!");
                    break;
            }
        }

        public static void NotifyBossDefeated(int stage)
        {
            if (stage <= _lastDefeatedBossStage) return;
            _lastDefeatedBossStage = stage;

            switch (stage)
            {
                case 2:
                    Emit(
                        "CPT. PRICE",
                        "TF-141 ACTUAL",
                        "Gate IFV neutralized. Pushing deeper into Hajin DMZ.");
                    break;
                case 4:
                    Emit(
                        "PVT. PARK // ROK 707",
                        "LOCAL TAC-LINK",
                        "Northern depot clear! Helipad sector straight ahead, Price.");
                    break;
                default:
                    Emit(
                        "LASWELL",
                        "CI-SATCOM",
                        "Konni Command BTR destroyed! Move to Extraction LZ Evac-6 and press [E] to board Blackhawk!");
                    break;
            }
        }

        public static string GetBossCallsign(int stage) => stage switch
        {
            2 => "KONNI IFV 'VIPER-01'",
            4 => "BTR-82A 'KRASNY-04'",
            _ => "KOMANDER VOLKOV // KONNI BTR"
        };

        public static string GetActLabel(int stage) => stage switch
        {
            <= 2 => $"ACT I // DMZ GATE {stage}/{FinalCampaignStage}",
            <= 4 => $"ACT II // HAJIN DEPOT {stage}/{FinalCampaignStage}",
            _ => $"ACT III // HELIPAD EXFIL {stage}/{FinalCampaignStage}"
        };

        public static string GetSectorObjective(
            int stage,
            int killsDone = 0,
            int killsGoal = 0,
            int sabDone = 0,
            int sabTotal = 2,
            bool gateC4Unlocked = false) => stage switch
        {
            1 when gateC4Unlocked => "Move to Gate Alpha -> Hold [E] to Detonate C4",
            1 => $"Eliminate Hajin Sentries ({killsDone}/{Mathf.Max(1, killsGoal)})",
            2 => "Destroy Konni IFV 'Viper-01' & Breach Gate Alpha",
            3 => $"Plant C4 Charges: Fuel & EW Uplink ({sabDone}/{sabTotal})",
            4 => "Destroy BTR-82A 'Krasny-04' at North Depot",
            5 => "Eliminate Volkov's Helipad Screen -> Enter LZ",
            _ => sabDone > 0
                ? "EXFIL AVAILABLE -> Reach Helipad LZ & Hold [E]"
                : "Destroy Volkov's Konni BTR to Secure Extraction"
        };

        public static string GetEndedDebrief(bool victory, int stageReached, int kills)
        {
            if (victory)
            {
                return $"HAJIN DMZ NIGHTFALL COMPLETE — Capt. Price, Ghost, Gaz & Pvt. Park neutralized Volkov's Konni cell ({kills} KIA) and secured critical peninsula intel before dawn.";
            }

            return stageReached switch
            {
                <= 2 => $"MIA AT HAJIN GATE (Act I) — {kills} hostiles eliminated. Regroup on Nightfall optics and use fuel drums for gate defense.",
                <= 4 => $"OVERRUN IN CENTRAL DEPOT (Act II) — Reached Stage {stageReached}/{FinalCampaignStage} ({kills} KIA). Use [N] NVG and [G] frags to clear sabotage lanes.",
                _ => $"LOST AT EXTRACTION LZ (Act III) — Reached Stage {stageReached}/{FinalCampaignStage} ({kills} KIA). Volkov's BTR held the pad — hit the Armory and deploy again."
            };
        }

        public static string BuildIntelDossierText(int highestStage)
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("<b>OPERATION NIGHTFALL // HAJIN DMZ (KOREAN PENINSULA)</b>");
            sb.AppendLine("Classification: <color=#E5A024>TIER-1 OFF-GRID BLACK OPS</color>");
            sb.AppendLine();
            sb.AppendLine("<b>ACT I — CHECKPOINT NIGHT RAID (STAGE 1-2)</b>");
            sb.AppendLine("Capt. Price, Ghost, and Gaz infiltrate off-the-grid with ROK 707 liaison Pvt. Park Hyun-Jun to smash Konni's forward picket and breach Blast Gate Alpha.");
            sb.AppendLine();

            if (highestStage >= 3)
            {
                sb.AppendLine("<b>ACT II — BLACKOUT & SABOTAGE (STAGE 3-4)</b>");
                sb.AppendLine("Task Force 141 rigs the Hajin Fuel Manifold and Electronic Warfare uplink with C4 while dismantling BTR-82A hunter-killer armor.");
            }
            else
            {
                sb.AppendLine("<b>ACT II — [CLASSIFIED: REACH ACT II IN CAMPAIGN]</b>");
                sb.AppendLine("Clear Gate Alpha to decrypt Konni EW jamming logs inside Hajin City.");
            }
            sb.AppendLine();

            if (highestStage >= 5)
            {
                sb.AppendLine("<b>ACT III — VOLKOV'S ESCAPE & EXFIL (STAGE 5-6)</b>");
                sb.AppendLine("Komander Volkov locks down the northern helipad with reactive-armored Command BTR support. Eliminate him to capture peninsula intel and extract cleanly.");
            }
            else
            {
                sb.AppendLine("<b>ACT III — [CLASSIFIED: REACH ACT III IN CAMPAIGN]</b>");
                sb.AppendLine("Eliminate the Konni Depot BTR to unlock Extraction LZ Evac-6 dossier logs.");
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
