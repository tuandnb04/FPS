using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Grounded JSOC military-thriller narrative engine for PROTOCOL ZERO // BLACKOUT LEDGER.
    /// Drives multi-turn comms that adapt dynamically to fallen (K.I.A.) operators,
    /// scripted sacrifice arcs across all 4 acts, moral ending choices, and 8 recoverable blackbox logs.
    /// </summary>
    public static class StoryDirector
    {
        public const string OperationTitle = "PROTOCOL ZERO";
        public const string CampaignSubtitle = "JSOC TASK FORCE // VANGUARD-09";
        public const int FinalCampaignStage = 5;
        public const int CampaignMissionCount = 4;
        public const int CampaignVictoryBonusCredits = 350;

        public static int ActiveMissionIndex { get; set; } = 1;
        public static int CurrentMissionIndex
        {
            get => ActiveMissionIndex;
            set => ActiveMissionIndex = Mathf.Clamp(value, 1, CampaignMissionCount);
        }

        private static int _lastKillMilestone;
        private static int _lastStageDispatched;
        private static bool _firstKillDispatched;

        public static string LastCallsign { get; private set; } = "MAJ. ROURKE";
        public static string LastChannel { get; private set; } = "IRONWOOD-1";
        public static SpeakerFaction LastFaction { get; private set; } = SpeakerFaction.Vanguard;

        public static event Action<string, string, string> OnRadioTransmission;
        public static event Action<RadioLine[]> OnRadioConversation;

        public static readonly IntelLogEntry[] IntelCatalog =
        {
            new(
                "INTEL_M1_01", 1, "PHANTOM-04 MANIFEST",
                "West Fuel Pier — Behind Oil Silos",
                "BILL OF LADING #K4-09",
                "Customs bill signed under AEGIS shell procurement. Recovered canisters carry Phantom-04 unit paint burned off with thermite — someone cleared Daniel Vance's unit off NATO books before burial.",
                75,
                new RadioLine("KOZLOWSKI", "WRAITH-2", "Vance, lot numbers on these drums match K4-09. Phantom-04 wasn't overrun—somebody scrubbed their manifests and sold the salvage.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("VANCE", "NOMAD-1", "Hold the rail. Copy the manifest checksums and keep pushing north—somebody on our side signed that dock clearance.", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M1_02", 1, "SCRUBBED IFF BLACKBOX",
                "East Container Pier — North Stack",
                "IFF BLACKBOX // CPL. D. VANCE",
                "Charred blue-force transponder recovered from harbor radar lockers. Command net disabled Phantom-04's friendly squawk 90 seconds prior to an unrecorded artillery grid sweep.",
                75,
                new RadioLine("CPL. REYES", "SPARK-3", "Blackbox buffer off the radar locker—Command turned Phantom-04's IFF squawk dark ninety seconds before artillery hit their valley.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Keep that drive encrypted. Off open net until we secure Dragic's shipping ledger.", SpeakerFaction.Vanguard, 4.4f)
            ),
            new(
                "INTEL_M2_01", 2, "AUDIT PURGE ORDER #774",
                "SW Canyon — Wrecked Convoy Truck",
                "INCINERATOR MANIFEST // KASTNER",
                "Dispatch from Dir. Elena Kastner: 'Transport Karst-4 ash barrels and AEGIS signatory drives via Armored Train #774. Once clear of Amber Gorge, wipe transit logs.'",
                90,
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "They aren't hauling warheads. It's an audit purge convoy burning hard drives and body tags before dawn.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("CPL. REYES", "SPARK-3", "If that train hits the northern furnace tunnel, everything AEGIS did to Phantom-04 turns to slag.", SpeakerFaction.Vanguard, 4.6f)
            ),
            new(
                "INTEL_M2_02", 2, "ROE-9 STRIKE ROUTING",
                "East Switchyard — Signal Shack",
                "ROUTING RELAY // KESTREL BUNKER",
                "Satellite hop ledger tracing the Karst-4 friendly-fire confirmation directly through AEGIS's subterranean relay beneath Kestrel Ridge.",
                90,
                new RadioLine("CPL. REYES", "SPARK-3", "Pulled the relay table. The spoofed Broken Arrow strike on Daniel's platoon routed through Kestrel Sub-Station—authorized internally.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("VANCE", "NOMAD-1", "Half my squad's already bleeding in this dirt. We follow that cable straight underground.", SpeakerFaction.Vanguard, 4.3f)
            ),
            new(
                "INTEL_M3_01", 3, "KARST-4 SIGNATURE BLOCK",
                "West Server Vault — North Rack",
                "DIRECTIVE ROE-9 // SIGNED AUTH",
                "Uncensored fire-order block. Dir. Marcus Chen flagged Karst-4 as 'Compromised Asset Liquidation', countersigned by Maj. Owen Rourke under falsified casualty reports.",
                110,
                new RadioLine("VANCE", "NOMAD-1", "Rourke... this authorization block carries your crypto key. You countersigned the fire mission over Daniel's valley.", SpeakerFaction.Vanguard, 5.0f),
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Chen told me Phantom-04 was already dead to nerve agents, Julian. By the time I learned it was a lie, I was neck-deep in blood.", SpeakerFaction.Vanguard, 5.2f)
            ),
            new(
                "INTEL_M3_02", 3, "PROTOCOL ZERO ARCHIVE",
                "East Turbine Hall — South Alcove",
                "BLACKOUT LEDGER // CONTAINMENT",
                "Continuity file ordering permanent containment of Vanguard-09 once Kestrel's mainframe unlocks: Halon flood below, hired apex cleaners at Blackcrest above.",
                110,
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Protocol Zero wasn't Radek's operation, Vance. It's Chen's mop-up order to bury every surviving witness from Karst-4.", SpeakerFaction.Vanguard, 5.0f),
                new RadioLine("VANCE", "NOMAD-1", "Then we blow the turbines, drag that ledger up the lift, and stop letting suits write us off.", SpeakerFaction.Vanguard, 4.6f)
            ),
            new(
                "INTEL_M4_01", 4, "CLEANER CONTRACT // APEX-01",
                "SW Citadel Bastion — Bunkers",
                "AEGIS LIQUIDATION CONTRACT",
                "Wire transfer from AEGIS Reserve Account #009 to Col. Artem Radek's Apex unit: 'Scrub Kestrel server mirrors, scrub Blackcrest Spire, eliminate surviving Vanguard element.'",
                130,
                new RadioLine("VANCE", "NOMAD-1", "No rogue general. Just another clean-up contract stamped and wired straight from Washington.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "Watch your language on open band, Sergeant. Some wars only stay won if nobody digs up the floorboards.", SpeakerFaction.Command, 4.8f)
            ),
            new(
                "INTEL_M4_02", 4, "DANIEL VANCE DOGTAGS",
                "NE Citadel Bastion — Relic Crate",
                "PHANTOM-04 // CPL. DANIEL VANCE",
                "Salt-stained steel tags bagged with personal effects waiting for Blackcrest's incinerator burn barrel. No ceremony. Just an inventory barcode.",
                150,
                new RadioLine("VANCE", "NOMAD-1", "Found Daniel's tags in a burn bag... Kozlowski, Reyes, Rourke—I'm the only name left standing on this roster.", SpeakerFaction.Vanguard, 5.0f)
            )
        };

        public static string GetMissionSceneName(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "Mission01_BorderOutpost",
                2 => "Mission02_RailDepot",
                3 => "Mission03_KestrelRelay",
                _ => "Mission04_BlackcrestSummit"
            };
        }

        public static string GetMissionTitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "ACT I: BLACKWATER PIER",
                2 => "ACT II: IRON CORRIDOR",
                3 => "ACT III: KESTREL SUB-STATION",
                _ => "ACT IV: BLACKCREST SUMMIT"
            };
        }

        public static string GetMissionShortTab(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "ACT I: PIER",
                2 => "ACT II: CORRIDOR",
                3 => "ACT III: KESTREL",
                _ => "ACT IV: SUMMIT"
            };
        }

        public static string GetMissionSubtitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "BLACKWATER DOCKYARDS // 21:40Z",
                2 => "AMBER GORGE RAIL CUT // 04:55Z",
                3 => "KESTREL SUB-LEVEL // 18:20Z",
                _ => "BLACKCREST ALPINE RIDGE // 23:45Z"
            };
        }

        public static string GetMissionGridStamp(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "DTG 162140Z // GRID 38T LM-409",
                2 => "DTG 170455Z // GRID 38T LM-518",
                3 => "DTG 171820Z // GRID 38T LM-604",
                _ => "DTG 172345Z // GRID 38T LM-792"
            };
        }

        public static string GetMissionTitleBrief(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "OPORD 01: Recon Twin Harbor Piers for missing Karst-4 NATO ordnance. Expect heavy arms broker presence. Maintain IFF discipline and stabilize downed operators before bleedout.",
                2 => "OPORD 02: Cut off Ironward's mobile audit-purge train inside Amber Gorge. Throw dual rail switches to stall the convoy before its furnace tunnel destroys Karst-4 evidence.",
                3 => "OPORD 03: Infiltrate the sealed geothermal relay beneath Kestrel Ridge. Decrypt server ledger ROE-9 while holding off counter-assaults—surviving fireteam drops to skeleton strength.",
                _ => "OPORD 04: Sole remaining Vanguard operator ascends Blackcrest Alpine Citadel. Break cleaner unit APEX-01, seize the Helios Core uplink, and decide the fate of the Blackout Ledger."
            };
        }

        public static string GetObjectiveNodeTitle(int missionIndex, int nodeKindIndex)
        {
            return (Mathf.Clamp(missionIndex, 1, CampaignMissionCount), nodeKindIndex) switch
            {
                (1, 0) => "CUSTOMS PIER GATE",
                (1, 1) => "WEST FUEL MANIFOLD",
                (1, 2) => "EAST EW RADAR JAMMER",
                (1, 3) => "NE EXFIL GUNBOAT",

                (2, 0) => "LOCOMOTIVE COUPLING",
                (2, 1) => "WEST TRACK DIVERTER",
                (2, 2) => "EAST SIGNAL MATRIX",
                (2, 3) => "NORTH TUNNEL BULKHEAD",

                (3, 0) => "AIRLOCK BULKHEAD",
                (3, 1) => "WEST SERVER MAINFRAME",
                (3, 2) => "EAST TURBINE BUS",
                (3, 3) => "FREIGHT LIFT PIT",

                (4, 0) => "HELIOS CORE TERMINAL",
                (4, 1) => "WEST CAPACITOR PYLON",
                (4, 2) => "EAST CAPACITOR PYLON",
                _ => "LZ VALKYRIE-6 PAD"
            };
        }

        public static BossBehaviorKind GetMissionBossBehavior(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => BossBehaviorKind.WardenBarge,
                2 => BossBehaviorKind.GoliathRail,
                3 => BossBehaviorKind.VolkovJuggernaut,
                _ => BossBehaviorKind.RadekApex
            };
        }

        public static string GetBossCallsign(int stage)
        {
            return Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount) switch
            {
                1 => "CAPT. MARKO DRAGIC // WARDEN-01",
                2 => "DIR. ELENA KASTNER // GOLIATH-02",
                3 => "WARDEN VIKTOR VOLKOV // KESTREL-01",
                _ => "COL. ARTEM RADEK // APEX-01"
            };
        }

        public static void ResetCampaignRun()
        {
            _lastKillMilestone = 0;
            _lastStageDispatched = 0;
            _firstKillDispatched = false;
            LastCallsign = "MAJ. ROURKE";
            LastChannel = "IRONWOOD-1";
            LastFaction = SpeakerFaction.Vanguard;
        }

        public static void NotifyMissionDeployed()
        {
            switch (ActiveMissionIndex)
            {
                case 1:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Vanguard-09, ROE weapons-free on Customs Pier. Recover salvaged NATO pods and purge dockyard resistance.", SpeakerFaction.Command, 4.5f),
                        new RadioLine("VANCE", "NOMAD-1", "Dockyard looks less like smugglers and more like regular army defense. Keep headcounts tight.", SpeakerFaction.Vanguard, 4.4f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Standard sweep, Nomad. Breach the gate and pull whatever serial plates we find.", SpeakerFaction.Vanguard, 4.2f)
                    );
                    break;
                case 2:
                    TransmitConversation(
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Kozlowski's beacon went flat back in the harbor. One coffin already on our books—keep Reyes close.", SpeakerFaction.Vanguard, 4.8f),
                        new RadioLine("CPL. REYES", "SPARK-3", "Train #774 is idling between gorge cliffs. Flip both track diverters before they blow the bridge tunnel.", SpeakerFaction.Vanguard, 4.7f)
                    );
                    break;
                case 3:
                    TransmitConversation(
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Two guns left, Sergeant. Just you and me below ground—watch my six through the Airlock.", SpeakerFaction.Vanguard, 4.6f),
                        new RadioLine("VANCE", "NOMAD-1", "Reyes stayed in that tunnel so we'd reach Volkov alive. Let's make the cost worth paying.", SpeakerFaction.Vanguard, 4.8f)
                    );
                    break;
                default:
                    TransmitConversation(
                        new RadioLine("AEGIS-SATCOM", "UNSECURED", "Vance, transponder telemetry shows Ironwood, Wraith, and Spark flatlined. You are unauthorized on Blackcrest ridge.", SpeakerFaction.Command, 5.0f),
                        new RadioLine("VANCE", "NOMAD-1", "Three names on my dogtag chain say I finish this climb anyway.", SpeakerFaction.Vanguard, 4.5f)
                    );
                    break;
            }
        }

        public static void NotifyKillCount(int totalKills)
        {
            if (!_firstKillDispatched && totalKills >= 1)
            {
                _firstKillDispatched = true;
                if (ActiveMissionIndex == 4 || SaveManager.EnsureInstance().KiaCount >= 3)
                {
                    Transmit("VANCE", "NOMAD-1", "Contact down. Keep armor plates intact [Q] and check flanking lanes.", SpeakerFaction.Vanguard);
                }
                else
                {
                    Transmit("KOZLOWSKI", "WRAITH-2", "First sentry down. Use [Z/X/C] ROE stances and watch bleedout beacons if anyone drops.", SpeakerFaction.Vanguard);
                }
                return;
            }

            if (totalKills >= 14 && _lastKillMilestone < 14)
            {
                _lastKillMilestone = 14;
                if (ActiveMissionIndex <= 2)
                {
                    Transmit("CPL. REYES", "SPARK-3", "Heavy enemy radio traffic on 154 MHz—QRF elements mobilizing ahead.", SpeakerFaction.Vanguard);
                }
                else
                {
                    Transmit("VANCE", "NOMAD-1", "QRF net saturating. Stay inside cover—no spare medevac out here.", SpeakerFaction.Vanguard);
                }
            }
            else if (totalKills >= 28 && _lastKillMilestone < 28)
            {
                _lastKillMilestone = 28;
                Transmit("MAJ. ROURKE", "IRONWOOD-1", "Save 155mm fire support [F] and frag charges for armored hardpoints ahead.", SpeakerFaction.Vanguard);
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
                case 3:
                    NotifyMission3Stage(stage);
                    break;
                default:
                    NotifyMission4Stage(stage);
                    break;
            }
        }

        private static void NotifyMission1Stage(int stage)
        {
            switch (stage)
            {
                case 2:
                    Transmit("CPL. REYES", "SPARK-3", "Checkpoint clear. Set shaped charge [E] on Customs Gate 1.", SpeakerFaction.Vanguard);
                    break;
                case 3:
                    TransmitConversation(
                        new RadioLine("KOZLOWSKI", "WRAITH-2", "Harbor basin splits West Fuel and East EW piers. Bridge gangway is under crossfire.", SpeakerFaction.Vanguard, 4.5f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Destroy both manifolds and grab any shipping manifests inside pier lockers.", SpeakerFaction.Vanguard, 4.6f)
                    );
                    break;
                case 4:
                    TransmitConversation(
                        new RadioLine("CAPT. DRAGIC", "MARITIME NET", "Unknown callsigns breaching Quay North—Warden-01 engaging unauthorized boarders!", SpeakerFaction.Hostile, 4.6f),
                        new RadioLine("VANCE", "NOMAD-1", "Target armored barge on the quay! Spread out—incoming mortar splash!", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 5:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Vanguard-09, declare Broken Arrow. Sanitization barrage authorized on Blackwater docks in ten seconds—drop weapons.", SpeakerFaction.Command, 5.2f),
                        new RadioLine("KOZLOWSKI", "WRAITH-2", "Chen locked artillery onto our own quay! Get on that gunboat—I'll hold the optical jammer tower from the crane!", SpeakerFaction.Vanguard, 5.2f),
                        new RadioLine("VANCE", "NOMAD-1", "Marek, negative! Leave the jammer and sprint for the dock—!", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
            }
        }

        private static void NotifyMission2Stage(int stage)
        {
            switch (stage)
            {
                case 2:
                    TransmitConversation(
                        new RadioLine("CPL. REYES", "SPARK-3", "Armored Train #774 is choking the canyon. Cross through open flatcar gaps to reach East Switch.", SpeakerFaction.Vanguard, 4.7f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Lock both rail diverters before Kastner vents the cargo vaults.", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 3:
                    Transmit("CPL. REYES", "SPARK-3", "Switches jammed! Push up to the Locomotive Coupling and blow the drive hitch [E].", SpeakerFaction.Vanguard);
                    break;
                case 4:
                    TransmitConversation(
                        new RadioLine("DIR. KASTNER", "IRONWARD COMMS", "Check the cryptographic hash on Car 3, Sergeant. Your own Major authorized the Karst-4 grid wipe.", SpeakerFaction.Hostile, 5.0f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Eyes front, Vance—shut her up! Dodge Goliath-02's targeting laser!", SpeakerFaction.Vanguard, 4.5f)
                    );
                    break;
                case 5:
                    TransmitConversation(
                        new RadioLine("CPL. REYES", "SPARK-3", "Deadman charges armed across the north tunnel ceiling! Blast door hydraulics are jamming!", SpeakerFaction.Vanguard, 4.8f),
                        new RadioLine("CPL. REYES", "SPARK-3", "I'm wedging the manual release from outside! Run, Nomad—don't look back through the smoke!", SpeakerFaction.Vanguard, 5.0f)
                    );
                    break;
            }
        }

        private static void NotifyMission3Stage(int stage)
        {
            switch (stage)
            {
                case 2:
                    TransmitConversation(
                        new RadioLine("VANCE", "NOMAD-1", "Reyes bought us entry with her life. Mainframe vault lies West—expect heavy QRF while decrypting.", SpeakerFaction.Vanguard, 4.8f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Hold inside the terminal radius until ROE-9 unlocks. Stay alive, Vance.", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 3:
                    TransmitConversation(
                        new RadioLine("VOLKOV", "KESTREL-01", "You still think you're the rescue party, Major? Chen only sent you down here to trip the Halon lockdown for him.", SpeakerFaction.Traitor, 5.2f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Cut the turbine bus in the East Hall! We drag that ledger out no matter who gets buried.", SpeakerFaction.Vanguard, 4.6f)
                    );
                    break;
                case 4:
                    TransmitConversation(
                        new RadioLine("VOLKOV", "KESTREL-01", "Come claim your brother's file on Command Deck, Nomad! Watch how long ballistic shields last under real fire!", SpeakerFaction.Traitor, 4.8f),
                        new RadioLine("VANCE", "NOMAD-1", "Frontal ballistic barrier deflecting rifle fire! Use flanking arcs or M67 Frags [G]!", SpeakerFaction.Vanguard, 4.7f)
                    );
                    break;
                case 5:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Decryption signature detected. Activating Kestrel subterranean Halon purge and lift seal. Stand down, Ironwood.", SpeakerFaction.Command, 5.2f),
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Get in the lift cage, Julian! My chest plate's cracked—I'm overriding the external lift breaker from down here!", SpeakerFaction.Vanguard, 5.2f)
                    );
                    break;
            }
        }

        private static void NotifyMission4Stage(int stage)
        {
            switch (stage)
            {
                case 2:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Stop at the bridgehead, Sergeant. Rourke, Reyes, and Kozlowski understood containment necessity—don't throw the mission away.", SpeakerFaction.Command, 5.2f),
                        new RadioLine("VANCE", "NOMAD-1", "Save the speech, Chen. Dropping both Helios pylons now.", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 3:
                    TransmitConversation(
                        new RadioLine("COL. RADEK", "APEX-01 ENCRYPTED", "Just one Phantom left? Chen paid forty million to clear Karst-4 and Kestrel off the map. Time to balance his ledger.", SpeakerFaction.ApexBoss, 5.0f),
                        new RadioLine("VANCE", "NOMAD-1", "For Phantom-04. For Vanguard-09. Weapons free.", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 4:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Radek is dead! Access the Helios Spire Terminal [E] — seal Protocol Zero quietly and board Valkyrie-6 as a hero.", SpeakerFaction.Command, 5.2f),
                        new RadioLine("VANCE", "NOMAD-1", "Three empty caskets say otherwise. Approaching Spire Core.", SpeakerFaction.Vanguard, 4.4f)
                    );
                    break;
                case 5:
                    TransmitConversation(
                        new RadioLine("VALKYRIE-6", "AIR-EVAC", "Nomad-1, this is Valkyrie-6 inbound North Summit Pad! Hold perimeter flare—burners spinning for dusty extraction!", SpeakerFaction.Vanguard, 5.0f)
                    );
                    break;
            }
        }

        public static void NotifyRadekPhase2()
        {
            TransmitConversation(
                new RadioLine("COL. RADEK", "APEX-01 OVERCHARGE", "Mech hull compromised—ejecting into heavy exo-armor! Nobody walks off this summit alive!", SpeakerFaction.ApexBoss, 4.8f),
                new RadioLine("VANCE", "NOMAD-1", "Armored exo-rig loose! Watch flanking dashes and triple plasma spread!", SpeakerFaction.Vanguard, 4.5f)
            );
        }

        public static void NotifyVaultHoldoutStarted()
        {
            Transmit("MAJ. ROURKE", "IRONWOOD-1", "Server decryption spinning up—expect breach squads on both atrium doors!", SpeakerFaction.Vanguard);
        }

        public static void NotifyIntelDiscovered(IntelLogEntry entry)
        {
            if (entry.DiscoveryDialogue is { Length: > 0 })
            {
                TransmitConversation(entry.DiscoveryDialogue);
            }
            else
            {
                Transmit("VANCE", "NOMAD-1", $"Blackbox recovered: {entry.Header} (+{entry.RewardCredits} CR).", SpeakerFaction.Vanguard);
            }
        }

        public static void NotifySabotageProgress(int completed, int total, string targetName)
        {
            if (completed < total)
            {
                Transmit(
                    "VANCE",
                    "NOMAD-1",
                    $"{targetName} neutralized ({completed}/{total}). Pushing across to secondary target.",
                    SpeakerFaction.Vanguard);
            }
            else
            {
                var summary = ActiveMissionIndex switch
                {
                    1 => "Fuel and radar crippled. North Quay perimeter breached — heavy barge contact incoming!",
                    2 => "Track switches locked! Train #774 stalled — set C4 on Locomotive Coupling!",
                    _ => "HELIOS shield pylons down! Orbital spire exposed — APEX-01 inbound!"
                };
                Transmit("MAJ. ROURKE", "IRONWOOD-1", summary, SpeakerFaction.Vanguard);
            }
        }

        public static void NotifyGate1C4Ready()
        {
            var msg = ActiveMissionIndex switch
            {
                1 => "Checkpoint clear. Breach Customs Gate 1 with shaped charge [E].",
                3 => "Airlock outer post silent. Blow Airlock Bulkhead [E] to access Main Atrium.",
                _ => "Perimeter clear. Plant charge [E] at primary breach point."
            };
            Transmit("MAJ. ROURKE", "IRONWOOD-1", msg, SpeakerFaction.Vanguard);
        }

        public static void NotifyKillstreakReady(string summary)
        {
            var callsign = ActiveMissionIndex >= 3 ? "TAC-NET" : "WRAITH-2";
            var channel = ActiveMissionIndex >= 3 ? "REAPER-04 ISR" : "LOCAL RECON";
            var faction = ActiveMissionIndex == 1 ? SpeakerFaction.Command : SpeakerFaction.Vanguard;
            Transmit(callsign, channel, summary, faction);
        }

        public static void NotifySquadOrder(string message)
        {
            Transmit("VANCE", "VANGUARD-09 ROE", message, SpeakerFaction.Vanguard);
        }

        public static void NotifySquadDowned(string callsign)
        {
            Transmit("TRAUMA-NET", "BIOMETRICS", $"CRITICAL: {callsign} bleeding out! Stabilize within 20s [E/CLOSE] or [Q] Trauma Kit!", SpeakerFaction.Hostile);
        }

        public static void NotifyOperatorKia(string callsign, string roleTag)
        {
            Transmit("TRAUMA-NET", "FLATLINE // K.I.A.", $"{roleTag} // {callsign} heartbeat 0 BPM — operator lost permanently.", SpeakerFaction.Hostile);
        }

        public static void NotifyEndingChoiceMade(CampaignEndingKind choice)
        {
            if (choice == CampaignEndingKind.BroadcastBlackout)
            {
                TransmitConversation(
                    new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "What did you just broadcast?! Cut that uplink—every black station in Europe just pinged K4-09!", SpeakerFaction.Hostile, 5.2f),
                    new RadioLine("VANCE", "NOMAD-1", "Let them read every dead marine's name, Director. Valkyrie-6, sprint flare lit at North Pad!", SpeakerFaction.Vanguard, 4.8f)
                );
            }
            else
            {
                TransmitConversation(
                    new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Zero checksum confirmed. Satellite dark. Clear the spire and board Valkyrie-6—none of this occurred.", SpeakerFaction.Command, 5.2f),
                    new RadioLine("VANCE", "NOMAD-1", "Three buried brothers and an empty ledger. Lifting to extraction pad...", SpeakerFaction.Vanguard, 4.6f)
                );
            }
        }

        public static void NotifyBossSpawned(int stage)
        {
            var boss = GetBossCallsign(stage);
            Transmit("TAC-NET", "THREAT WARNING", $"PRIORITY HVT: {boss}. Lethal force cleared.", SpeakerFaction.Hostile);
        }

        public static void NotifyBossDefeated(int stage)
        {
        }

        public static string GetActLabel(int stage)
        {
            var clamped = Mathf.Clamp(stage, 1, FinalCampaignStage);
            return Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount) switch
            {
                1 => $"ACT I // BLACKWATER PIER  ({clamped}/{FinalCampaignStage})",
                2 => $"ACT II // IRON CORRIDOR  ({clamped}/{FinalCampaignStage})",
                3 => $"ACT III // KESTREL RELAY  ({clamped}/{FinalCampaignStage})",
                _ => $"ACT IV // BLACKCREST SUMMIT  ({clamped}/{FinalCampaignStage})"
            };
        }

        public static string GetSectorObjective(
            int stage,
            int s1Kills = 0,
            int s1Target = 6,
            int s2Done = 0,
            int s2Total = 2,
            bool gate1C4Ready = false)
        {
            var m = Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount);
            return (m, stage) switch
            {
                (1, 1) => $"OBJ // Clear Dock Checkpoint ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (1, 2) => $"OBJ // Breach {GetObjectiveNodeTitle(1, 0)} [E]",
                (1, 3) => $"OBJ // Destroy Pier Manifolds ({s2Done}/{Mathf.Max(1, s2Total)})",
                (1, 4) => $"HVT // Neutralize {GetBossCallsign(4)}",
                (1, _) => $"EXFIL // Board {GetObjectiveNodeTitle(1, 3)} [E]",

                (2, 1) => $"OBJ // Clear Gorge Pickets ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (2, 2) => $"OBJ // Lock Track Diverters ({s2Done}/{Mathf.Max(1, s2Total)})",
                (2, 3) => $"OBJ // Blow {GetObjectiveNodeTitle(2, 0)} [E]",
                (2, 4) => $"HVT // Destroy {GetBossCallsign(4)}",
                (2, _) => $"EXFIL // Breach {GetObjectiveNodeTitle(2, 3)} [E]",

                (3, 1) => gate1C4Ready ? $"OBJ // Blow {GetObjectiveNodeTitle(3, 0)} [E]" : $"OBJ // Clear South Airlock ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (3, 2) => $"OBJ // Hold & Decrypt {GetObjectiveNodeTitle(3, 1)} [E]",
                (3, 3) => $"OBJ // Sabotage {GetObjectiveNodeTitle(3, 2)} [E]",
                (3, 4) => $"HVT // Defeat {GetBossCallsign(4)} (Flank / HE Frag)",
                (3, _) => $"EXFIL // Hold {GetObjectiveNodeTitle(3, 3)} [E]",

                (4, 1) => $"OBJ // Take Alpine Bridgehead ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (4, 2) => $"OBJ // Overload Capacitor Pylons ({s2Done}/{Mathf.Max(1, s2Total)})",
                (4, 3) => $"HVT // Eliminate {GetBossCallsign(3)}",
                (4, 4) => $"DECISION // Access {GetObjectiveNodeTitle(4, 0)} [E]",
                _ => $"EXFIL // Hold {GetObjectiveNodeTitle(4, 3)} [E]"
            };
        }

        public static string BuildMemorialRollText(bool victory)
        {
            var save = SaveManager.EnsureInstance();
            var sb = new StringBuilder();
            sb.Append("<b>CASUALTY LEDGER:</b> PHANTOM-04 [K.I.A. IN KARST-4]");
            if (save.IsOperatorKia(SquadCallsigns.Recon)) sb.Append(" · WRAITH-2 KOZLOWSKI [K.I.A.]");
            if (save.IsOperatorKia(SquadCallsigns.Breacher)) sb.Append(" · SPARK-3 REYES [K.I.A.]");
            if (save.IsOperatorKia(SquadCallsigns.Officer)) sb.Append(" · IRONWOOD-1 ROURKE [K.I.A.]");
            if (!victory)
            {
                sb.Append(" · NOMAD-1 VANCE [M.I.A./K.I.A.]");
            }
            else if (save.KiaCount == 0)
            {
                sb.Append(" · VANGUARD-09 [0 KIA THIS SECTOR]");
            }
            return sb.ToString();
        }

        public static string GetEndedDebrief(bool victory, int stageReached, int kills)
        {
            var m = Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount);
            var save = SaveManager.EnsureInstance();
            if (victory)
            {
                return m switch
                {
                    1 => $"BLACKWATER PIER CLEARED ({kills} HOSTILES KIA) — Command declared Broken Arrow over our own berth. Spec. Marek Kozlowski ('WRAITH-2') remained at the EW crane jamming orbital shells so the gunboat escaped. Rest easy, Marek.",
                    2 => $"IRON CORRIDOR BREACHED ({kills} HOSTILES KIA) — Train #774 scrapped and Kastner's railgun down. Cpl. Elena Reyes ('SPARK-3') crushed beneath the North Gorge Tunnel bulkhead holding the blast door for Rourke and Vance.",
                    3 => $"KESTREL SUB-STATION ESCAPED ({kills} HOSTILES KIA) — Volkov neutralized, ROE-9 signature unmasked. Maj. Owen Rourke ('IRONWOOD-1') took lethal chest wounds overriding Dir. Chen's Halon vault seal from outside the lift.",
                    _ => save.LastEndingChoice == CampaignEndingKind.BroadcastBlackout
                        ? $"ENDING A // SCORCHED BLACKOUT LEDGER ({kills} HOSTILES KIA) — Before detonating Helios, Vance transmitted full Karst-4 & Kestrel war-crime telemetry across global bands. AEGIS's black council collapsed; Phantom-04 & Vanguard-09 names cleared—at the cost of leaving Vance stateless and hunted."
                        : $"ENDING B // SILENT BURIAL ({kills} HOSTILES KIA) — Vance shut down Helios silently and wiped Protocol Zero dark to prevent NATO destabilization. Dir. Chen walks free in shadow while Vance returns aboard VALKYRIE-6 alone with three folded flags and Daniel's tags."
                };
            }

            return $"UNIT WIPED IN ACT 0{m} (STAGE {stageReached}/{FinalCampaignStage} | {kills} HOSTILES KIA) — Fireteam signal flatlined. Requisition armor plating, weapons, or trauma gear before redeploy.";
        }

        public static CutsceneBeat[] GetIntroCutsceneBeats(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => new[]
                {
                    new CutsceneBeat(2.3f, new Vector3(16f, 22f, 44f), new Vector3(-6f, 16f, 6f), new Vector3(0f, 1f, 14f), new Vector3(0f, 1f, -10f), 46f, 50f,
                        "DIR. CHEN // AEGIS-SATCOM", "Grid 38T Blackwater Pier: heavy black-market containers linked to missing Karst-4 ordnance.", SpeakerFaction.Command),
                    new CutsceneBeat(2.2f, new Vector3(-10f, 13f, -26f), new Vector3(0f, 8.5f, -51f), new Vector3(0f, 1f, -38f), new Vector3(0f, 1.2f, -34f), 48f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Four operators on deck. Clear customs, sweep both harbor piers, and verify every manifest tag.", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(2.4f, new Vector3(14f, 19f, 38f), new Vector3(-4f, 14f, 4f), new Vector3(0f, 1.8f, 14f), new Vector3(0f, 1.5f, -8f), 44f, 48f,
                        "CPL. REYES // SPARK-3", "Amber Gorge Cut: Train #774 blocking center rails. Both flanking trenches manned.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.2f, new Vector3(-24f, 11f, -30f), new Vector3(-16f, 9.0f, -53f), new Vector3(-16f, 1.2f, -42f), new Vector3(-14f, 1.4f, -34f), 48f, 52f,
                        "SGT. VANCE // NOMAD-1", "Marek stayed back to buy us this train. Three left—move.", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(2.4f, new Vector3(0f, 20f, 34f), new Vector3(-16f, 14f, -4f), new Vector3(0f, 1.2f, 2f), new Vector3(-26f, 1.2f, -2f), 46f, 50f,
                        "MAJ. ROURKE // IRONWOOD-1", "Kestrel Sub-Level: Server Vault West, Turbine Hall East. Only two barrels firing now.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.2f, new Vector3(8f, 10f, -32f), new Vector3(0f, 8.2f, -52f), new Vector3(0f, 1.1f, -40f), new Vector3(0f, 1.3f, -34f), 48f, 52f,
                        "VOLKOV // KESTREL-01", "Another pair of ghosts knocking on Chen's vault. Step inside—the Halon locks are waiting.", SpeakerFaction.Traitor)
                },
                _ => new[]
                {
                    new CutsceneBeat(2.5f, new Vector3(18f, 23f, 22f), new Vector3(-12f, 17f, -4f), new Vector3(0f, 3.5f, 0f), new Vector3(0f, 2.2f, 2f), 44f, 50f,
                        "DIR. CHEN // AEGIS-SATCOM", "Three names off your board, Sergeant. Stand down on the south bridge before APEX-01 scrubs you off.", SpeakerFaction.Command),
                    new CutsceneBeat(2.2f, new Vector3(-8f, 10.5f, -34f), new Vector3(0f, 8.5f, -54f), new Vector3(0f, 1.5f, -42f), new Vector3(0f, 2.0f, -26f), 48f, 52f,
                        "SGT. VANCE // NOMAD-1", "No backup. No medevac. Just what's left in the magazine.", SpeakerFaction.Vanguard)
                }
            };
        }

        public static CutsceneBeat[] GetMidBreachCutsceneBeats(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(8.5f, 7.5f, -29f), new Vector3(0f, 11.5f, -18f), new Vector3(0f, 1.5f, -14f), new Vector3(0f, 1.2f, 6f), 48f, 54f,
                        "CPL. REYES // SPARK-3", "Customs bulkhead down! Split West Fuel Pier & East EW Pier — stay within trauma range!", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-12f, 9.5f, 4f), new Vector3(4f, 11.0f, 22f), new Vector3(0f, 1.8f, 14f), new Vector3(6f, 1.6f, 34f), 46f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Drive hitch severed! Push north past the locomotive—Kastner's rail armor waiting beyond Gate 2!", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(9f, 8.5f, -38f), new Vector3(0f, 10.5f, -28f), new Vector3(0f, 1.5f, -26f), new Vector3(0f, 1.5f, -4f), 48f, 52f,
                        "VANCE // NOMAD-1", "Airlock blown! Vault Terminal West—get ready to pull ROE-9.", SpeakerFaction.Vanguard)
                },
                _ => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-10f, 9.5f, 16f), new Vector3(0f, 11.0f, 26f), new Vector3(0f, 2.0f, 26f), new Vector3(0f, 1.6f, 42f), 46f, 52f,
                        "SGT. VANCE // NOMAD-1", "Citadel North Gate down! Popping IR flare on Pad 06—come pick up the last piece.", SpeakerFaction.Vanguard)
                }
            };
        }

        public static CutsceneBeat[] GetBossCutsceneBeats(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(-13f, 9f, 28f), new Vector3(11f, 9.6f, 33f), new Vector3(0f, 1.4f, 40f), new Vector3(0f, 1.6f, 40f), 44f, 50f,
                        "CAPT. DRAGIC // WARDEN-01", "Unidentified platoon inside Pier North—Warden armor engaging lethal!", SpeakerFaction.Hostile)
                },
                2 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(-6f, 9.5f, 30f), new Vector3(18f, 10.2f, 35f), new Vector3(8f, 1.5f, 43f), new Vector3(8f, 1.6f, 44f), 44f, 50f,
                        "DIR. KASTNER // GOLIATH-02", "All freight manifests flagged for incineration. Goliath-02 firing on unauthorized witnesses!", SpeakerFaction.Hostile)
                },
                3 => new[]
                {
                    new CutsceneBeat(2.9f, new Vector3(-11f, 8.8f, 20f), new Vector3(11f, 9.2f, 24f), new Vector3(0f, 1.5f, 33f), new Vector3(0f, 1.6f, 33f), 44f, 50f,
                        "VOLKOV // KESTREL-01", "Look at your own Major, Sergeant—still carrying Rourke all the way down here just to watch Chen lock the blast door!", SpeakerFaction.Traitor)
                },
                _ => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-16f, 10.5f, 4f), new Vector3(15f, 11.2f, 8f), new Vector3(0f, 1.8f, 14f), new Vector3(0f, 1.8f, 14f), 44f, 50f,
                        "COL. RADEK // APEX-01", "Closing the file on Karst-4, Sergeant. Nothing personal—just cleanup.", SpeakerFaction.ApexBoss)
                }
            };
        }

        public static string BuildIntelDossierText(int bestWaveReached)
        {
            return BuildIntelDossierTabText(0, bestWaveReached);
        }

        public static string BuildIntelDossierTabText(int tabIndex, int bestWaveReached)
        {
            var save = SaveManager.EnsureInstance();
            var cleared = save.Data.highestMissionCompleted;
            var intelFound = save.DiscoveredIntelCount;
            var sb = new StringBuilder();

            switch (tabIndex)
            {
                case 1:
                    sb.AppendLine("<b>JSOC ROSTER // VANGUARD-09 & COMMAND NET</b>");
                    sb.AppendLine();
                    sb.AppendLine("<color=#42A5F5><b>NOMAD-1 // SGT. JULIAN VANCE</b></color> — Fireteam Lead. Brother Cpl. Daniel Vance (Phantom-04) killed under forged strike ROE-9.");
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Officer, "#42A5F5", "IRONWOOD-1 // MAJ. OWEN ROURKE", "Field CO. Reluctantly countersigned Karst-4 strike key believing Phantom-04 was lost."));
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Recon, "#42A5F5", "WRAITH-2 // SPEC. MAREK KOZLOWSKI", "EW/Recon Marksman. Holds Blackwater radar jammer under friendly-fire barrage."));
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Breacher, "#42A5F5", "SPARK-3 // CPL. ELENA REYES", "Demolitions Breacher. Overrides crumbling Amber Gorge blast shutters manually."));
                    sb.AppendLine("<color=#F2A72E><b>AEGIS-ACTUAL // DIR. MARCUS CHEN</b></color> — Authorized Phantom-04 erasure & Protocol Zero Halon purge to bury illegal weapons sales.");
                    sb.AppendLine();
                    sb.AppendLine("<color=#E05A47><b>WARDEN-01 // CAPT. MARKO DRAGIC</b></color> — Harbor broker scrubbing salvaged Phantom-04 ordnance.");
                    sb.AppendLine("<color=#E05A47><b>GOLIATH-02 // DIR. ELENA KASTNER</b></color> — Incinerator train chief wiping AEGIS audit ledgers in Amber Gorge.");
                    sb.AppendLine("<color=#F2A72E><b>KESTREL-01 // WARDEN VIKTOR VOLKOV</b></color> — Trapped auditor hiding inside Kestrel bunker.");
                    sb.AppendLine("<color=#D6453A><b>APEX-01 // COL. ARTEM RADEK</b></color> — AEGIS contracted liquidation unit securing Blackcrest Spire.");
                    break;

                case 2:
                    sb.AppendLine($"<b>DECRYPTED BLACKBOX LOGS  [{intelFound}/{IntelCatalog.Length} RECOVERED]</b>");
                    sb.AppendLine();
                    foreach (var item in IntelCatalog)
                    {
                        var found = save.IsIntelDiscovered(item.Id);
                        if (found)
                        {
                            sb.AppendLine($"<color=#49C96E><b>[ACT 0{item.MissionIndex}] {item.Header}</b></color> — {item.BodyText}");
                        }
                        else
                        {
                            sb.AppendLine($"<color=#A6926F><b>[ACT 0{item.MissionIndex} // ENCRYPTED]</b></color> Grid Cue: {item.LocationHint}");
                        }
                    }
                    break;

                default:
                    var endingLabel = save.LastEndingChoice switch
                    {
                        CampaignEndingKind.BroadcastBlackout => "<color=#49C96E>ENDING A: SCORCHED BLACKOUT BROADCAST</color>",
                        CampaignEndingKind.SilentKillSwitch => "<color=#F2A72E>ENDING B: SILENT BURIAL / LEDGER SEALED</color>",
                        _ => "<color=#A6926F>OPERATION PENDING RESOLUTION</color>"
                    };
                    sb.AppendLine($"<b>AFTER-ACTION LEDGER // PROTOCOL ZERO</b>  |  ACTS: {cleared}/{CampaignMissionCount}  |  KIA ROSTER: {save.KiaCount}/3  |  {endingLabel}");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT I — BLACKWATER PIER (K4-09 DOCK SWEEP)</b>");
                    sb.AppendLine("Smugglers turned out to be AEGIS-contracted scrap crews; Dir. Chen dropped artillery onto Vanguard-09's own dock.");
                    sb.AppendLine(cleared >= 1
                        ? "<color=#E05A47>[CASUALTY]: WRAITH-2 (Kozlowski) KIA jamming naval strike lock so gunboat escaped.</color>"
                        : "<color=#A6926F>[ACTIVE]: Breach Customs Gate 1 and sweep pier manifests.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT II — IRON CORRIDOR (AUDIT INCINERATOR CONVOY)</b>");
                    sb.AppendLine("Ambushed Armored Train #774 carrying Karst-4 ash barrels and strike checksums inside Amber Gorge.");
                    sb.AppendLine(cleared >= 2
                        ? "<color=#E05A47>[CASUALTY]: SPARK-3 (Reyes) KIA wedging collapsed tunnel bulkheads during thermite blowout.</color>"
                        : "<color=#A6926F>[LOCKED]: Halt Train #774 in Amber Gorge.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT III — KESTREL SUB-STATION (ROE-9 SIGNATURE)</b>");
                    sb.AppendLine("Uncovered Rourke's unwilling signature on Karst-4 and Chen's Protocol Zero Halon lockdown.");
                    sb.AppendLine(cleared >= 3
                        ? "<color=#E05A47>[CASUALTY]: IRONWOOD-1 (Rourke) KIA holding external lift override inside choking Halon gas.</color>"
                        : "<color=#A6926F>[LOCKED]: Decrypt West Server Mainframe underground.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT IV — BLACKCREST SUMMIT (SOLE SURVIVOR)</b>");
                    sb.AppendLine("Sgt. Vance climbs alone against cleaner unit APEX-01 to either broadcast the truth worldwide or bury the ledger.");
                    sb.AppendLine(cleared >= 4
                        ? $"<color=#49C96E>[FINAL STATUS]: Peak Stage {bestWaveReached}/{FinalCampaignStage} — Spire Shutdown Complete.</color>"
                        : $"<color=#A6926F>[FINAL ACT]: Peak Stage {bestWaveReached}/{FinalCampaignStage} — Ascend Citadel alone.</color>");
                    break;
            }

            return sb.ToString();
        }

        private static string StatusTag(SaveManager save, string tag, string aliveHex, string label, string bio)
        {
            var dead = save.IsOperatorKia(tag);
            return dead
                ? $"<color=#D6453A><b>{label} [K.I.A.]</b></color> — {bio}"
                : $"<color={aliveHex}><b>{label} [ACTIVE]</b></color> — {bio}";
        }

        public static void Transmit(string callsign, string channel, string body, SpeakerFaction faction = SpeakerFaction.Vanguard)
        {
            TransmitConversation(new RadioLine(callsign, channel, body, faction, 4.5f));
        }

        public static void TransmitConversation(params RadioLine[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            var save = SaveManager.EnsureInstance();
            var filtered = new List<RadioLine>(lines.Length);

            foreach (var line in lines)
            {
                var speakerTag = ResolveTagFromLine(line);
                if (!string.IsNullOrEmpty(speakerTag) && save.IsOperatorKia(speakerTag) && !line.Channel.Contains("FLATLINE"))
                {
                    // Fallen operators never speak from beyond the grave; drop purely conversational lines
                    // or reroute vital tactical alerts to surviving lead NOMAD-1 if needed.
                    if (lines.Length == 1 && (line.Body.Contains("[E]") || line.Body.Contains("[G]") || line.Body.Contains("Gate") || line.Body.Contains("Vault")))
                    {
                        filtered.Add(new RadioLine("VANCE", "NOMAD-1", line.Body, SpeakerFaction.Vanguard, line.HoldSeconds));
                    }
                    continue;
                }
                filtered.Add(line);
            }

            if (filtered.Count == 0) return;
            var result = filtered.ToArray();
            var last = result[^1];
            LastCallsign = last.Callsign;
            LastChannel = last.Channel;
            LastFaction = last.Faction;

            if (OnRadioConversation != null)
            {
                OnRadioConversation.Invoke(result);
            }
            else
            {
                OnRadioTransmission?.Invoke(result[0].Callsign, result[0].Channel, result[0].Body);
            }
        }

        private static string ResolveTagFromLine(RadioLine line)
        {
            if (line.Channel.Contains(SquadCallsigns.Officer) || line.Callsign.Contains(SquadCallsigns.Officer) || line.Callsign.Contains("ROURKE")) return SquadCallsigns.Officer;
            if (line.Channel.Contains(SquadCallsigns.Recon) || line.Callsign.Contains(SquadCallsigns.Recon) || line.Callsign.Contains("KOZLOWSKI")) return SquadCallsigns.Recon;
            if (line.Channel.Contains(SquadCallsigns.Breacher) || line.Callsign.Contains(SquadCallsigns.Breacher) || line.Callsign.Contains("REYES")) return SquadCallsigns.Breacher;
            return null;
        }
    }
}
