using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Authoritative military-thriller narrative engine for PROTOCOL ZERO:
    /// 5 campaign operations x 8 tactical stages, 15 decryptable blackbox intel logs,
    /// deterministic FNV-1a Kokoro voice clip bindings, multi-beat cutscenes, and dual endings.
    /// </summary>
    public static class StoryDirector
    {
        public const string OperationTitle = "PROTOCOL ZERO";
        public const string CampaignSubtitle = "JSOC TASK FORCE // VANGUARD-09";
        public const int FinalCampaignStage = 8;
        public const int CampaignMissionCount = 5;
        public const int CampaignVictoryBonusCredits = 450;

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
            // ACT I: BLACKWATER PIER (3 LOGS)
            new(
                "INTEL_M1_01", 1, "PHANTOM-04 MANIFEST", "Pier Customs South Yard",
                "K4-09 NATO ORDNANCE SHIPPING MANIFEST",
                "Crate serials belong to 2nd Recon 'Phantom-04'—listed KIA off the Karst-4 border four months back. Signatures forged by AEGIS Logistics.",
                140,
                new RadioLine("KOZLOWSKI", "WRAITH-2", "Hold on—these serial plates match the missing Phantom-04 supply drop from Karst-4.", SpeakerFaction.Vanguard, 4.4f),
                new RadioLine("VANCE", "NOMAD-1", "That was my brother Daniel's unit. Those pods were stamped total loss after the airstrike.", SpeakerFaction.Vanguard, 4.5f),
                new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Stay on mission, Vanguard. Smugglers scrub hulls all the time. Torch the pods and keep moving.", SpeakerFaction.Command, 4.3f)
            ),
            new(
                "INTEL_M1_02", 1, "CUSTOMS CRYPTO SLATE", "West Fuel Berth Edge",
                "BROKEN ARROW SHELLING PRE-AUTHORIZATION",
                "AEGIS dispatch pre-loaded 155mm naval fire coordinates over Blackwater North Quay two hours before Vanguard-09 even landed.",
                160,
                new RadioLine("CPL. REYES", "SPARK-3", "Captain, SATCOM queued Broken Arrow coordinates onto our own exfil dock before we hit shore.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Copy all. Keep the radar up and hold the dock lane—nobody calls artillery on my team unopposed.", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M1_03", 1, "DRAGIC BLACK BOX", "East Radar Deck",
                "BROKER PAYROLL // TRANSFER KESTREL-774",
                "Capt. Dragic received wired AEGIS funds to route survivors' gear north through Amber Gorge aboard Armored Train #774.",
                180,
                new RadioLine("VANCE", "NOMAD-1", "Dragic wasn't hoarding rifles. He was moving black-budget ordnance straight onto an inland rail line.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("KOZLOWSKI", "WRAITH-2", "Pulling train timetable off the transceiver—Armored Train #774 departs Amber Gorge within the hour.", SpeakerFaction.Vanguard, 4.5f)
            ),

            // ACT II: IRON CORRIDOR (3 LOGS)
            new(
                "INTEL_M2_01", 2, "DERAILED ASH DRUM", "South Gorge Ravine",
                "KARST-4 BIO-ASH CONTAINMENT LEDGER",
                "Forty-two sealed bio-ash drums carrying JSOC field tags—Phantom-04 wasn't ambushed; they were scrubbed to hide nerve-agent stock.",
                200,
                new RadioLine("CPL. REYES", "SPARK-3", "Drums are stamped US-DOD... charred dogtags inside the manifest pouch. Nomad—that's Phantom-04.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("VANCE", "NOMAD-1", "Keep moving, Reyes. When we pry Kastner out of her railcar, she gives us every name above her.", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M2_02", 2, "DISPATCH RELAY NODE", "West Switch Alcove",
                "FORGED THREAT ASSESSMENT // DIR. CHEN",
                "AEGIS Director Marcus Chen fabricated high-yield missile telemetry over Karst-4 valley so JSOC would clear an emergency strike.",
                210,
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Hell... Director Chen personally certified that valley as empty of NATO boots before the bombers rolled.", SpeakerFaction.Vanguard, 4.7f),
                new RadioLine("VANCE", "NOMAD-1", "Empty? My brother called three broken-arrow checks before his radio went dead, Major.", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M2_03", 2, "FLATBED TELEMETRY", "East Rail Spur",
                "SUB-LEVEL ROUTE // KESTREL BUNKER",
                "Kastner's backup drive routes every audit trail to subterranean Kestrel Sub-Station under Warden Viktor Volkov.",
                220,
                new RadioLine("VANCE", "NOMAD-1", "Sub-station schematic locked. Volkov has the unredacted Karst-4 strike authorization keys underground.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Lock that drive in your plate carrier, Julian. Reyes, wire the breach charges—we're going deep.", SpeakerFaction.Vanguard, 4.4f)
            ),

            // ACT III: KESTREL SUB-STATION (3 LOGS)
            new(
                "INTEL_M3_01", 3, "VAULT ARCHIVE BLADE", "West Server Vault NW",
                "ROE-9 COUNTERSIGNATURE // OWEN ROURKE",
                "Strike Order K4-09 shows Dir. Chen's prime cipher paired with a reluctant emergency countersignature from Maj. Owen Rourke.",
                240,
                new RadioLine("VANCE", "NOMAD-1", "Wait—look at line nine on the Karst-4 release key. That co-signature isn't Chen's. That's Ironwood-One.", SpeakerFaction.Vanguard, 5.0f),
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "I believed Chen when he swore Phantom-04 were already dead, Julian... God help me, I signed to save the division.", SpeakerFaction.Vanguard, 5.2f)
            ),
            new(
                "INTEL_M3_02", 3, "TURBINE SCRUB VALVE", "East Turbine Hall SE",
                "PROTOCOL ZERO // HALON PURGE DIRECTIVE",
                "Dir. Chen rigged Kestrel's oxygen scrubbers to dump lethal Halon across all lift shafts whenever unauthorized decryption hits ninety percent.",
                250,
                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Chen rigged the entire facility as a mass grave! Manual lift pistons are wired to drop when we decrypt!", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("VANCE", "NOMAD-1", "We pull the ledger anyway. Nobody buries Marek and Elena under sixty meters of concrete.", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M3_03", 3, "APEX DOSSIER DRIVE", "Command Atrium North",
                "CLEANER CONTRACT // COL. RADEK (APEX-01)",
                "Volkov hired ex-spetsnaz PMC APEX-01 under Col. Artem Radek at Blackcrest Helios Citadel to execute anyone exiting Kestrel alive.",
                260,
                new RadioLine("VOLKOV", "KESTREL-01", "Already dead, Americans. Radek's orbital guns on Blackcrest Peak lock every bird lifting out of this shaft.", SpeakerFaction.Traitor, 4.6f),
                new RadioLine("VANCE", "NOMAD-1", "Then Blackcrest is next. Let Radek try locking onto an operator with nothing left to lose.", SpeakerFaction.Vanguard, 4.5f)
            ),

            // ACT IV: BLACKCREST SUMMIT (3 LOGS)
            new(
                "INTEL_M4_01", 4, "BRIDGE INTERCEPT POD", "South Alpine Causeway",
                "TERMINAL RADIO PING // DANIEL VANCE",
                "Uncensored final blackbox recording from Cpl. Daniel Vance during Karst-4 friendly fire—confirming AEGIS blocked his extraction.",
                280,
                new RadioLine("VANCE", "NOMAD-1", "Listen to that timestamp... Danny transmitted forty minutes after Rourke signed the strike window.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("DIR. CHEN", "AEGIS-SATCOM", "Keep your voice down on open band, Sergeant. Some wars only stay buried if nobody digs.", SpeakerFaction.Command, 4.6f)
            ),
            new(
                "INTEL_M4_02", 4, "PYLON OVERRIDE KEY", "Summit Bastion West",
                "AEON BLACKSITE COORDINATES // WASHINGTON",
                "Radek's backup relay exposes Dir. Chen's hardened AEON Citadel bunker beneath Sector Zero outside Washington, D.C.",
                300,
                new RadioLine("VANCE", "NOMAD-1", "Got it—AEON Citadel coordinates synced to Valkyrie navigation. Chen isn't sitting safely behind a desk anymore.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("VALKYRIE-6", "AIR SUPPORT", "Copy Nomad-1! Burning the last reserve fuel to ferry you south the second you break summit lock!", SpeakerFaction.Vanguard, 4.5f)
            ),
            new(
                "INTEL_M4_03", 4, "CITADEL UPLINK BLACKBOX", "Helipad Rim North",
                "PROTOCOL ZERO // WITNESS LIQUIDATION",
                "Chen marked Vance K.I.A. alongside Kozlowski, Reyes, and Rourke to erase every trace of JSOC Task Force Vanguard-09 before dawn.",
                320,
                new RadioLine("VANCE", "NOMAD-1", "My own service record just flipped to KIA on SATCOM. Means there's nobody left to stop what comes next.", SpeakerFaction.Vanguard, 4.7f),
                new RadioLine("AEGIS-SATCOM", "BLACKLIST-NET", "Transponder Nomad-1 flagged rogue hostile. All Blackguard garrisons engage on sight.", SpeakerFaction.Command, 4.6f)
            ),

            // ACT V: AEON BLACKSITE (3 LOGS)
            new(
                "INTEL_M5_01", 5, "AEON GATEHOUSE LOG", "Outer Courtyard West",
                "DIRECTIVE ZERO // CONGRESSIONAL COVERUP",
                "Chen diverted four hundred million in humanitarian reconstruction funds into private orbital strikes and deniable arms brokerage.",
                340,
                new RadioLine("VANCE", "NOMAD-1", "Four hundred million washed through ghost NGOs—every dollar paid in the blood of my squad.", SpeakerFaction.Vanguard, 4.6f),
                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "Men like your brother died so empires could sleep through nightfall, Vance. Drop your rifle and walk away.", SpeakerFaction.Traitor, 4.8f)
            ),
            new(
                "INTEL_M5_02", 5, "BLACKGUARD ARCHIVE", "AEON Server Wing",
                "UNREDACTED PHANTOM-04 TRANSCRIPT",
                "Audio log of Cpl. Daniel Vance holding a collapsed drainage culvert until Chen personally terminated his rescue beacon.",
                360,
                new RadioLine("VANCE", "NOMAD-1", "He pulled Danny's distress frequency offline himself. Thirty-two seconds before evac reached the ridge.", SpeakerFaction.Vanguard, 4.8f),
                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "A surgical calculation. You stood by our code until it touched your own surname, Sergeant.", SpeakerFaction.Traitor, 4.7f)
            ),
            new(
                "INTEL_M5_03", 5, "AEON CORE CIPHER", "Citadel Inner Vault",
                "FAILSAFE KEY // BROADCAST VS BURIAL",
                "Master cryptographic key governing either worldwide disclosure across every diplomatic trunk or irreversible Protocol Zero dark purge.",
                400,
                new RadioLine("VALKYRIE-6", "EVAC OVERWATCH", "Nomad-1, whole AEON basement just flared crimson! Whatever Chen wired to that vault core, it runs the globe.", SpeakerFaction.Vanguard, 4.7f),
                new RadioLine("VANCE", "NOMAD-1", "He's standing between me and the console right now. Stand by on extraction band, Valkyrie.", SpeakerFaction.Vanguard, 4.6f)
            )
        };

        public static string NormalizeVoiceCallsign(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker)) return "TAC-NET";
            var s = speaker.Trim().ToUpperInvariant();
            if (s.Contains("VANCE")) return "VANCE";
            if (s.Contains("ROURKE")) return "MAJ. ROURKE";
            if (s.Contains("KOZLOWSKI")) return "KOZLOWSKI";
            if (s.Contains("WRAITH-2")) return "WRAITH-2";
            if (s.Contains("REYES")) return "CPL. REYES";
            if (s.Contains("CHEN")) return "DIR. CHEN";
            if (s.Contains("AEGIS-SATCOM") || s.Contains("SATCOM")) return "AEGIS-SATCOM";
            if (s.Contains("DRAGIC")) return "CAPT. DRAGIC";
            if (s.Contains("KASTNER")) return "DIR. KASTNER";
            if (s.Contains("VOLKOV")) return "VOLKOV";
            if (s.Contains("RADEK")) return "COL. RADEK";
            if (s.Contains("VALKYRIE")) return "VALKYRIE-6";
            if (s.Contains("TRAUMA")) return "TRAUMA-NET";
            return "TAC-NET";
        }

        public static string GetMissionSceneName(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "Mission01_BorderOutpost",
                2 => "Mission02_RailDepot",
                3 => "Mission03_KestrelRelay",
                4 => "Mission04_BlackcrestSummit",
                _ => "Mission05_AegisCitadel"
            };
        }

        public static string GetMissionTitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "ACT I: BLACKWATER PIER",
                2 => "ACT II: IRON CORRIDOR",
                3 => "ACT III: KESTREL SUB-STATION",
                4 => "ACT IV: BLACKCREST SUMMIT",
                _ => "ACT V: AEON BLACKSITE"
            };
        }

        public static string GetMissionShortTab(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "ACT I // BLACKWATER PIER",
                2 => "ACT II // IRON CORRIDOR",
                3 => "ACT III // KESTREL RELAY",
                4 => "ACT IV // BLACKCREST SUMMIT",
                _ => "ACT V // AEON BLACKSITE"
            };
        }

        public static string GetMissionSubtitle(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "BLACKWATER DOCKYARDS // 21:40Z",
                2 => "AMBER GORGE RAIL CUT // 01:15Z",
                3 => "KESTREL SUB-LEVEL BUNKER // 04:05Z",
                4 => "HELIOS ORBITAL CITADEL // 05:50Z",
                _ => "AEON CITADEL // SECTOR ZERO // 03:45Z"
            };
        }

        public static string GetMissionGridStamp(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "GRID 38T LM-409 · FULL FIRETEAM [4/4 OPS]",
                2 => "GRID 41R KG-118 · COASTLINE EVAC [3/4 OPS]",
                3 => "GRID 44K BR-702 · BREACH TEAM [2/4 OPS]",
                4 => "GRID 52N AL-990 · SOLE SURVIVOR [1/4 OPS]",
                _ => "GRID 18S DC-001 · ROGUE ASSAULT [1/4 OPS]"
            };
        }

        public static string GetMissionTitleBrief(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => "OPORD 01: Recon Twin Harbor Piers for missing Karst-4 NATO ordnance. Breach customs, purge EW jammers, defeat Warden-01, and defend Kozlowski's extraction jammer.",
                2 => "OPORD 02: Intercept Armored Train #774 inside Amber Gorge. Lock rail switches, blow the locomotive hitch, neutralize Goliath-02, and hold the collapsing north bulkhead for Reyes.",
                3 => "OPORD 03: Infiltrate subterranean Kestrel Relay. Decrypt Karst-4 ROE-9 truth inside West Vault, cripple turbines, defeat Volkov, and survive the Halon lock alongside Rourke.",
                4 => "OPORD 04: Ascend snowbound Blackcrest Summit alone. Overload twin HELIOS shield pylons, break Col. Radek's APEX-01 exoskeleton, and extract Chen's Washington coordinates.",
                _ => "OPORD 05: Storm Dir. Marcus Chen's private AEON Citadel in Sector Zero. Shred archive uplinks, defeat Chen himself, decide Protocol Zero's fate, and break out to Valkyrie-6."
            };
        }

        public static string GetObjectiveNodeTitle(int missionIndex, int nodeKindIndex)
        {
            return (Mathf.Clamp(missionIndex, 1, CampaignMissionCount), nodeKindIndex) switch
            {
                (1, 0) => "CUSTOMS BLAST GATE C4",
                (1, 1) => "WEST FUEL MANIFOLD",
                (1, 2) => "EAST EW RADAR JAMMER",
                (1, 3) => "WRAITH-2 EW TOWER HOLD",
                (1, 4) => "NORTH QUAY EXFIL GUNBOAT",

                (2, 0) => "LOCOMOTIVE DRIVE HITCH C4",
                (2, 1) => "WEST TRACK DIVERTER",
                (2, 2) => "EAST SIGNAL RELAY",
                (2, 3) => "NORTH SHUTTER HYDRAULIC HOLD",
                (2, 4) => "NORTH GORGE TUNNEL EXIT",

                (3, 0) => "SOUTH AIRLOCK BULKHEAD C4",
                (3, 1) => "WEST VAULT ROE-9 DECRYPT",
                (3, 2) => "EAST GEOTHERMAL TURBINE",
                (3, 3) => "HALON LIFT OVERRIDE HOLD",
                (3, 4) => "NORTH FREIGHT ESCAPE LIFT",

                (4, 0) => "SOUTH BRIDGE BLAST GATE C4",
                (4, 1) => "WEST HELIOS CAPACITOR PYLON",
                (4, 2) => "EAST HELIOS CAPACITOR PYLON",
                (4, 3) => "SUMMIT HELIPAD EXTRACTION HOLD",
                (4, 4) => "VALKYRIE-6 HELIPAD LZ",

                (5, 0) => "AEON SECURITY GATE C4",
                (5, 1) => "WEST BLACKGUARD DATA ARCHIVE",
                (5, 2) => "EAST AEGIS SATCOM UPLINK",
                (5, 3) => "PROTOCOL ZERO CORE CONSOLE",
                _ => "VALKYRIE-6 COURTYARD EXFIL"
            };
        }

        public static BossBehaviorKind GetMissionBossBehavior(int missionIndex)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => BossBehaviorKind.WardenBarge,
                2 => BossBehaviorKind.GoliathRail,
                3 => BossBehaviorKind.VolkovJuggernaut,
                4 => BossBehaviorKind.RadekApex,
                _ => BossBehaviorKind.ChenOverwatch
            };
        }

        public static string GetBossCallsign(int stage)
        {
            return Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount) switch
            {
                1 => "CAPT. MARKO DRAGIC // WARDEN-01",
                2 => "DIR. ELENA KASTNER // GOLIATH-02",
                3 => "WARDEN VIKTOR VOLKOV // KESTREL-01",
                4 => "COL. ARTEM RADEK // APEX-01",
                _ => "DIR. MARCUS CHEN // AEGIS-ACTUAL"
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
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Kozlowski's beacon went dark at the harbor. Keep Reyes tight on our six.", SpeakerFaction.Vanguard, 4.8f),
                        new RadioLine("CPL. REYES", "SPARK-3", "Train #774 is idling between gorge cliffs. Flip both track diverters before they blow the bridge tunnel.", SpeakerFaction.Vanguard, 4.7f)
                    );
                    break;
                case 3:
                    TransmitConversation(
                        new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Just two rifles left, Sergeant. Stay on my six through the Airlock.", SpeakerFaction.Vanguard, 4.6f),
                        new RadioLine("VANCE", "NOMAD-1", "Reyes bought us this entry. Let's make sure we leave with Volkov's drive.", SpeakerFaction.Vanguard, 4.8f)
                    );
                    break;
                case 4:
                    TransmitConversation(
                        new RadioLine("AEGIS-SATCOM", "UNSECURED", "Vance, transponder telemetry shows Ironwood, Wraith, and Spark flatlined. You are unauthorized on Blackcrest ridge.", SpeakerFaction.Command, 5.0f),
                        new RadioLine("VANCE", "NOMAD-1", "Three names on my dogtag chain say I finish this climb anyway.", SpeakerFaction.Vanguard, 4.5f)
                    );
                    break;
                default:
                    TransmitConversation(
                        new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "You flew a stolen Blackhawk straight into protected airspace, Sergeant. There is nowhere left for a dead marine to hide.", SpeakerFaction.Traitor, 5.0f),
                        new RadioLine("VANCE", "NOMAD-1", "Not hiding, Chen. Knocking on your front gate with every round Vanguard-09 left behind.", SpeakerFaction.Vanguard, 4.8f)
                    );
                    break;
            }
        }

        public static void NotifyKillCount(int totalKills)
        {
            if (!_firstKillDispatched && totalKills >= 1)
            {
                _firstKillDispatched = true;
                if (ActiveMissionIndex >= 4 || SaveManager.EnsureInstance().KiaCount >= 3)
                {
                    Transmit("VANCE", "NOMAD-1", "Contact down. Keep armor plates intact [Q] and check flanking lanes.", SpeakerFaction.Vanguard);
                }
                else if (ActiveMissionIndex == 1)
                {
                    Transmit("KOZLOWSKI", "WRAITH-2", "First sentry down. Use [Z/X/C] ROE stances and watch bleedout beacons if anyone drops.", SpeakerFaction.Vanguard);
                }
                else
                {
                    Transmit("CPL. REYES", "SPARK-3", "Point contact eliminated. Keep barrels sweeping both canyon ridges.", SpeakerFaction.Vanguard);
                }
                return;
            }

            if (totalKills >= 18 && _lastKillMilestone < 18)
            {
                _lastKillMilestone = 18;
                if (ActiveMissionIndex <= 2)
                {
                    Transmit("CPL. REYES", "SPARK-3", "Heavy traffic on 154 MHz. QRF moving up from the north.", SpeakerFaction.Vanguard);
                }
                else
                {
                    Transmit("VANCE", "NOMAD-1", "QRF saturating the net. Stick to hard cover. No medevac coming out here.", SpeakerFaction.Vanguard);
                }
            }
            else if (totalKills >= 36 && _lastKillMilestone < 36)
            {
                _lastKillMilestone = 36;
                if (ActiveMissionIndex <= 3)
                {
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Save 155mm fire support [F] and frag charges for armored hardpoints ahead.", SpeakerFaction.Vanguard);
                }
                else
                {
                    Transmit("VANCE", "NOMAD-1", "Over thirty contacts down. Reload discipline sharp, suppress every Shielder before closing.", SpeakerFaction.Vanguard);
                }
            }
        }

        public static void NotifyStageStarted(int stage)
        {
            if (stage <= _lastStageDispatched) return;
            _lastStageDispatched = stage;

            switch (ActiveMissionIndex)
            {
                case 1:
                    switch (stage)
                    {
                        case 2:
                            TransmitConversation(
                                new RadioLine("KOZLOWSKI", "WRAITH-2", "Second wave coming off the customs sheds! Snipers on catwalks, heavies advancing center lane!", SpeakerFaction.Vanguard, 4.4f),
                                new RadioLine("VANCE", "NOMAD-1", "Drop them before they lock the harbor gate. Keep eyes up!", SpeakerFaction.Vanguard, 4.0f)
                            );
                            break;
                        case 4:
                            TransmitConversation(
                                new RadioLine("KOZLOWSKI", "WRAITH-2", "Gate's blown! West Fuel Depot is armed with NATO-spec demolitions, East Pier holds an AEGIS jamming tower.", SpeakerFaction.Vanguard, 4.8f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Hit both pier nodes simultaneously. Expect coastal QRF pouring across the footbridges!", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 5:
                            TransmitConversation(
                                new RadioLine("CPL. REYES", "SPARK-3", "North Quay bunkers just lit up! Dragic sealed Gate 2 and scrambled heavy shield teams!", SpeakerFaction.Vanguard, 4.6f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Cut through the quay guard, then crack North Quay Gate 2 wide open!", SpeakerFaction.Vanguard, 4.3f)
                            );
                            break;
                        case 6:
                            TransmitConversation(
                                new RadioLine("CAPT. DRAGIC", "WARDEN-01", "Unidentified platoon inside Pier North—Warden armor engaging lethal!", SpeakerFaction.Hostile, 4.8f),
                                new RadioLine("VANCE", "NOMAD-1", "Heavy gunship barge armor! Flank the hull cannons and watch mortar telemetry rings!", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 7:
                            TransmitConversation(
                                new RadioLine("KOZLOWSKI", "WRAITH-2", "Broken Arrow shelling inbound from AEGIS cruisers! Somebody has to hold the EW Radar Tower while the gunboat spins up!", SpeakerFaction.Vanguard, 5.0f),
                                new RadioLine("VANCE", "NOMAD-1", "Hold that jamming panel! Team, cover Marek against every cleaner squad coming down the dock!", SpeakerFaction.Vanguard, 4.8f)
                            );
                            break;
                        case 8:
                            TransmitConversation(
                                new RadioLine("KOZLOWSKI", "WRAITH-2", "Naval guidance is scrambled—go! Reach the gunboat berth now before their secondary salvo lands!", SpeakerFaction.Vanguard, 4.8f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Move, Julian! To the boat—now!", SpeakerFaction.Vanguard, 3.8f)
                            );
                            break;
                    }
                    break;

                case 2:
                    switch (stage)
                    {
                        case 2:
                            TransmitConversation(
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Gorge ambush! Cut off their heavy flankers before reaching the flatcars!", SpeakerFaction.Vanguard, 4.4f),
                                new RadioLine("CPL. REYES", "SPARK-3", "Watch high cliff lines, Nomad—gorge snipers have laser locks on the main tracks.", SpeakerFaction.Vanguard, 4.4f)
                            );
                            break;
                        case 4:
                            TransmitConversation(
                                new RadioLine("CPL. REYES", "SPARK-3", "Armored Train #774 straddles the center rails. Lock West Track Diverter and East Signal Relay!", SpeakerFaction.Vanguard, 4.8f),
                                new RadioLine("VANCE", "NOMAD-1", "Expect heavy QRF storming both side pockets while we slice the power grid!", SpeakerFaction.Vanguard, 4.4f)
                            );
                            break;
                        case 5:
                            TransmitConversation(
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Armored vanguard pouring out of the north gorge mouth! Break their cordon and clear the railhead!", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 6:
                            TransmitConversation(
                                new RadioLine("DIR. KASTNER", "GOLIATH-02", "All freight manifests flagged for incineration. Goliath-02 firing on unauthorized witnesses!", SpeakerFaction.Hostile, 5.0f),
                                new RadioLine("CPL. REYES", "SPARK-3", "She has a magnetic railgun turret! Break line of sight on amber laser lock!", SpeakerFaction.Vanguard, 4.7f)
                            );
                            break;
                        case 7:
                            TransmitConversation(
                                new RadioLine("CPL. REYES", "SPARK-3", "Thermite charges cracked the gorge ceiling! Holding the hydraulic shutter at North Exit—keep them off my back!", SpeakerFaction.Vanguard, 5.0f),
                                new RadioLine("VANCE", "NOMAD-1", "Don't talk crazy, Elena! Hold the lock while we cut this ambush in half!", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 8:
                            TransmitConversation(
                                new RadioLine("CPL. REYES", "SPARK-3", "Door piston jammed half-up! Slip through the North Tunnel right now—I can't hold the rubble!", SpeakerFaction.Vanguard, 4.8f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Move, Vance! Move!", SpeakerFaction.Vanguard, 3.6f)
                            );
                            break;
                    }
                    break;

                case 3:
                    switch (stage)
                    {
                        case 2:
                            Transmit("MAJ. ROURKE", "IRONWOOD-1", "Airlock security escalating—clean out the inner checkpoint before planting the gate charge.", SpeakerFaction.Vanguard);
                            break;
                        case 4:
                            TransmitConversation(
                                new RadioLine("VANCE", "NOMAD-1", "West Vault mainframe cracked ROE-9... wait. Owen—that countersignature on Daniel's grid is yours.", SpeakerFaction.Vanguard, 5.0f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Don't freeze on me now, Julian! Chen forged every recon scan—I thought Phantom-04 was dead already! Defend the decrypt and rip the Turbine offline!", SpeakerFaction.Vanguard, 5.5f)
                            );
                            break;
                        case 5:
                            TransmitConversation(
                                new RadioLine("VOLKOV", "KESTREL-01", "Atrium Halon alarms armed! My Blackguard will bury both of you before you touch Command Deck!", SpeakerFaction.Traitor, 4.9f),
                                new RadioLine("VANCE", "NOMAD-1", "Save your breath, Major. We clear this floor together first, then we talk.", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 6:
                            TransmitConversation(
                                new RadioLine("VOLKOV", "KESTREL-01", "Still dragging your guilt down into Chen's crypt, Major? Security Shield active—burn in Halon!", SpeakerFaction.Traitor, 5.0f),
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Flank his reactive riot barrier and hammer with frag ordnance! Drop him!", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 7:
                            TransmitConversation(
                                new RadioLine("MAJ. ROURKE", "IRONWOOD-1", "Ventilation locks dropped! I'm bypassing the lift box outside in the gas—hold this corridor until freight lift clears!", SpeakerFaction.Vanguard, 5.2f),
                                new RadioLine("VANCE", "NOMAD-1", "Owen, get back inside the shaft! That Halon density kills inside thirty seconds!", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 8:
                            Transmit("MAJ. ROURKE", "IRONWOOD-1", "Freight cage unlocked! Get aboard the lift, Julian! Make Chen answer for Daniel and for all of us!", SpeakerFaction.Vanguard);
                            break;
                    }
                    break;

                case 4:
                    switch (stage)
                    {
                        case 2:
                            Transmit("VANCE", "NOMAD-1", "Bridgehead secure, pushing up the Citadel causeway. Shielders and snipers stacked in the octagon plaza.", SpeakerFaction.Vanguard);
                            break;
                        case 4:
                            TransmitConversation(
                                new RadioLine("AEGIS-SATCOM", "CITADEL GRID", "Helios orbital sweeps locking onto Citadel Plaza. You are walking into a firing oven, Vance.", SpeakerFaction.Command, 4.6f),
                                new RadioLine("VANCE", "NOMAD-1", "Two Helios capacitor pylons power their spire dome. Dropping both—watch sweeping orbital pulses.", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 5:
                            Transmit("VANCE", "NOMAD-1", "Spire shields failing—APEX-01 vanguard deploying across North Bastion!", SpeakerFaction.Vanguard);
                            break;
                        case 6:
                            TransmitConversation(
                                new RadioLine("COL. RADEK", "APEX-01", "Only one left? Chen paid forty million to clear Karst-4 and Kestrel off the map. Time to settle his bill.", SpeakerFaction.ApexBoss, 5.2f),
                                new RadioLine("VANCE", "NOMAD-1", "Keep your forty million, Radek. You're between me and Washington.", SpeakerFaction.Vanguard, 4.4f)
                            );
                            break;
                        case 7:
                            TransmitConversation(
                                new RadioLine("VANCE", "NOMAD-1", "Pulling Chen's Washington AEON Citadel coordinates off the spire node now! Hold North Helipad until Valkyrie breaks cloud cover!", SpeakerFaction.Vanguard, 5.1f),
                                new RadioLine("VALKYRIE-6", "AIR SUPPORT", "Nomad-1, climbing through mountain flak—twenty seconds out on Summit Pad!", SpeakerFaction.Vanguard, 4.6f)
                            );
                            break;
                        case 8:
                            Transmit("VALKYRIE-6", "AIR SUPPORT", "Valkyrie-6 flaring at North Helipad! Board now—setting direct night course for AEON Citadel!", SpeakerFaction.Vanguard);
                            break;
                    }
                    break;

                default:
                    switch (stage)
                    {
                        case 2:
                            TransmitConversation(
                                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "Your dead brothers left no record, Julian. Why shred your own lungs climbing through another fortress?", SpeakerFaction.Traitor, 4.9f),
                                new RadioLine("VANCE", "NOMAD-1", "Because somebody needs to sign the bottom of your ledger in daylight.", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 4:
                            TransmitConversation(
                                new RadioLine("VANCE", "NOMAD-1", "Courtyard gate breached! Two targets inside AEON Wing: West Data Archive and East SATCOM Uplink.", SpeakerFaction.Vanguard, 4.7f),
                                new RadioLine("VALKYRIE-6", "EVAC OVERWATCH", "Thermal sweeps show heavy Blackguard squads flooding both corridors. Keep moving, Nomad!", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 5:
                            Transmit("VANCE", "NOMAD-1", "Both relay wings ruined. Cutting through Chen's personal guard to break the Inner Vault Gate.", SpeakerFaction.Vanguard);
                            break;
                        case 6:
                            TransmitConversation(
                                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "Enough ghosts! If the Senate sees Karst-4 telemetry, three defense treaties burn overnight! Blackguard Exo deploying!", SpeakerFaction.Traitor, 5.2f),
                                new RadioLine("VANCE", "NOMAD-1", "Then stand behind your own armor instead of dead marines, Marcus!", SpeakerFaction.Vanguard, 4.5f)
                            );
                            break;
                        case 7:
                            TransmitConversation(
                                new RadioLine("VANCE", "NOMAD-1", "Chen's rig is down... Core console unlocked. Now everybody hears every word—or the vault burns forever.", SpeakerFaction.Vanguard, 5.0f)
                            );
                            break;
                        case 8:
                            Transmit("VANCE", "NOMAD-1", "Console locked in! Last wave closing on the courtyard—hold until Valkyrie-6 sets down!", SpeakerFaction.Vanguard);
                            break;
                    }
                    break;
            }
        }

        public static void NotifyRadekPhase2()
        {
            TransmitConversation(
                new RadioLine("COL. RADEK", "APEX-01", "Mech hull wrecked! Switching to heavy exo-armor! Nobody walks off this summit alive!", SpeakerFaction.ApexBoss, 4.8f),
                new RadioLine("VANCE", "NOMAD-1", "Armored exo-rig loose! Watch flanking dashes and triple plasma spread!", SpeakerFaction.Vanguard, 4.5f)
            );
        }

        public static void NotifyChenPhase2()
        {
            TransmitConversation(
                new RadioLine("DIR. CHEN", "AEGIS-ACTUAL", "Command walker breached! Rerouting AEON orbital grid to close-range purge—if I fall tonight, Sector Zero turns to ash!", SpeakerFaction.Traitor, 5.1f),
                new RadioLine("VANCE", "NOMAD-1", "Orbital crossfire hot! Keep shifting cover and punch right through his kinetic barrier!", SpeakerFaction.Vanguard, 4.6f)
            );
        }

        public static void NotifyVaultHoldoutStarted()
        {
            switch (ActiveMissionIndex)
            {
                case 1:
                    Transmit("KOZLOWSKI", "WRAITH-2", "Naval guidance jammer spinning hot! Keep those pier breachers off my flank!", SpeakerFaction.Vanguard);
                    break;
                case 2:
                    Transmit("CPL. REYES", "SPARK-3", "Bypassing collapsed gorge hydraulics—cover the tracks behind me!", SpeakerFaction.Vanguard);
                    break;
                case 3:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Decryption running hot! Expect breach teams on both atrium doors!", SpeakerFaction.Vanguard);
                    break;
                case 4:
                    Transmit("VANCE", "NOMAD-1", "Pulling Washington coordinates off summit link! Dig in against orbital sweeps!", SpeakerFaction.Vanguard);
                    break;
                default:
                    Transmit("VALKYRIE-6", "EVAC OVERWATCH", "Cleaner gunships incoming over AEON Courtyard! Hold zone until ramp clears!", SpeakerFaction.Vanguard);
                    break;
            }
        }

        public static void NotifyIntelDiscovered(IntelLogEntry entry)
        {
            if (entry.DiscoveryDialogue is { Length: > 0 })
            {
                TransmitConversation(entry.DiscoveryDialogue);
            }
            else
            {
                Transmit("VANCE", "NOMAD-1", "Blackbox telemetry decrypted and stored to tactical drive.", SpeakerFaction.Vanguard);
            }
        }

        public static void NotifySabotageProgress(int completed, int total, string targetName)
        {
            if (completed < total)
            {
                switch (ActiveMissionIndex)
                {
                    case 1:
                        Transmit("VANCE", "NOMAD-1", "First pier target wrecked. Crossing center bridge to finish the second sector.", SpeakerFaction.Vanguard);
                        break;
                    case 2:
                        Transmit("CPL. REYES", "SPARK-3", "Track switch locked down. Advancing across yard to trip the second switch.", SpeakerFaction.Vanguard);
                        break;
                    case 3:
                        Transmit("MAJ. ROURKE", "IRONWOOD-1", "First bunker sector knocked cold. Crossing atrium to finish the second wing.", SpeakerFaction.Vanguard);
                        break;
                    case 4:
                        Transmit("VANCE", "NOMAD-1", "First HELIOS capacitor pylon overloaded. Pushing across plaza to shatter the second.", SpeakerFaction.Vanguard);
                        break;
                    default:
                        Transmit("VANCE", "NOMAD-1", "First AEON wing shredded. Cutting across courtyard to hit the second relay.", SpeakerFaction.Vanguard);
                        break;
                }
            }
            else
            {
                switch (ActiveMissionIndex)
                {
                    case 1:
                        Transmit("MAJ. ROURKE", "IRONWOOD-1", "Fuel and radar wrecked. North Quay perimeter open, heavy barge contact inbound!", SpeakerFaction.Vanguard);
                        break;
                    case 2:
                        Transmit("MAJ. ROURKE", "IRONWOOD-1", "Track switches locked! Train #774 stalled, set C4 on the Locomotive Coupling!", SpeakerFaction.Vanguard);
                        break;
                    case 3:
                        Transmit("MAJ. ROURKE", "IRONWOOD-1", "ROE-9 archive and East Turbine wiped! Push North Command Deck—Volkov is waiting!", SpeakerFaction.Vanguard);
                        break;
                    case 4:
                        Transmit("VANCE", "NOMAD-1", "HELIOS shield pylons down! Orbital spire exposed, APEX-01 inbound!", SpeakerFaction.Vanguard);
                        break;
                    default:
                        Transmit("VANCE", "NOMAD-1", "AEON firewall gutted! Fighting through Inner Bastion to unlock Marcus Chen's vault.", SpeakerFaction.Vanguard);
                        break;
                }
            }
        }

        public static void NotifyGate1C4Ready()
        {
            switch (ActiveMissionIndex)
            {
                case 1:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Checkpoint clear. Breach Customs Gate 1 with shaped charge [E].", SpeakerFaction.Vanguard);
                    break;
                case 2:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Tracks locked. Blow the Locomotive Drive Hitch [E] to crack North Gorge!", SpeakerFaction.Vanguard);
                    break;
                case 3:
                    Transmit("MAJ. ROURKE", "IRONWOOD-1", "Airlock outer post silent. Blow Airlock Bulkhead [E] to access Main Atrium.", SpeakerFaction.Vanguard);
                    break;
                case 4:
                    Transmit("VANCE", "NOMAD-1", "Spire perimeter clear. Plant shaped charge at Citadel Gate 2 [E] to advance to Radek.", SpeakerFaction.Vanguard);
                    break;
                default:
                    Transmit("VANCE", "NOMAD-1", "Gatehouse clear. Wire shaped charge on AEON Gate [E] and brace for counter-fire.", SpeakerFaction.Vanguard);
                    break;
            }
        }

        public static void NotifyKillstreakReady(string summary)
        {
            var callsign = ActiveMissionIndex >= 3 ? "TAC-NET" : "WRAITH-2";
            var channel = ActiveMissionIndex >= 3 ? "REAPER-04 ISR" : "LOCAL RECON";
            var faction = ActiveMissionIndex == 1 ? SpeakerFaction.Command : SpeakerFaction.Vanguard;
            var fixedLine = summary != null && summary.Contains("155mm")
                ? "155mm Battery cleared for Fire Mission [F]!"
                : "Reaper-04 ISR Drone now sweeping hostile transponders on ATAK grid.";
            Transmit(callsign, channel, fixedLine, faction);
        }

        public static void NotifySquadOrder(string message)
        {
            Transmit("VANCE", "VANGUARD-09 ROE", message, SpeakerFaction.Vanguard);
        }

        public static void NotifySquadDowned(string callsign)
        {
            if (callsign.Contains("ROURKE"))
                Transmit("TRAUMA-NET", "BIOMETRICS", "CRITICAL: MAJ. ROURKE bleeding out! Stabilize within 20s [E/CLOSE] or [Q] Trauma Kit!", SpeakerFaction.Hostile);
            else if (callsign.Contains("KOZLOWSKI"))
                Transmit("TRAUMA-NET", "BIOMETRICS", "CRITICAL: KOZLOWSKI bleeding out! Stabilize within 20s [E/CLOSE] or [Q] Trauma Kit!", SpeakerFaction.Hostile);
            else
                Transmit("TRAUMA-NET", "BIOMETRICS", "CRITICAL: CPL. REYES bleeding out! Stabilize within 20s [E/CLOSE] or [Q] Trauma Kit!", SpeakerFaction.Hostile);
        }

        public static void NotifyOperatorKia(string callsign, string roleTag)
        {
            if (callsign.Contains("ROURKE"))
                Transmit("TRAUMA-NET", "FLATLINE // K.I.A.", "IRONWOOD-1 MAJ. ROURKE, zero BPM. Operator lost permanently.", SpeakerFaction.Hostile);
            else if (callsign.Contains("KOZLOWSKI"))
                Transmit("TRAUMA-NET", "FLATLINE // K.I.A.", "WRAITH-2 KOZLOWSKI, zero BPM. Operator lost permanently.", SpeakerFaction.Hostile);
            else
                Transmit("TRAUMA-NET", "FLATLINE // K.I.A.", "SPARK-3 CPL. REYES, zero BPM. Operator lost permanently.", SpeakerFaction.Hostile);
        }

        public static void NotifyEndingChoiceMade(CampaignEndingKind choice)
        {
            if (choice == CampaignEndingKind.BroadcastBlackout)
            {
                TransmitConversation(
                    new RadioLine("VALKYRIE-6", "GLOBAL UPLINK", "Broadcast transmitted! Every NATO trunk and Senate server just mirrored Karst-4 and Phantom-04!", SpeakerFaction.Vanguard, 5.2f),
                    new RadioLine("VANCE", "NOMAD-1", "Let the world read Daniel, Marek, Elena, and Owen's names aloud. Holding courtyard against the cleanup wave!", SpeakerFaction.Vanguard, 4.9f)
                );
            }
            else
            {
                TransmitConversation(
                    new RadioLine("AEGIS-SATCOM", "BLACKBOX SEAL", "Core dark and checksum burned. No press leak detected. Survive spec-ops extraction scramble to Valkyrie-6.", SpeakerFaction.Command, 5.2f),
                    new RadioLine("VANCE", "NOMAD-1", "Four buried marines and a cold vault. Nobody leaves clean out of this rain.", SpeakerFaction.Vanguard, 4.7f)
                );
            }
        }

        public static void NotifyBossSpawned(int stage)
        {
            switch (Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount))
            {
                case 1:
                    Transmit("TAC-NET", "THREAT WARNING", "PRIORITY HVT: CAPT. MARKO DRAGIC // WARDEN-01. Lethal force cleared.", SpeakerFaction.Hostile);
                    break;
                case 2:
                    Transmit("TAC-NET", "THREAT WARNING", "PRIORITY HVT: DIR. ELENA KASTNER // GOLIATH-02. Lethal force cleared.", SpeakerFaction.Hostile);
                    break;
                case 3:
                    Transmit("TAC-NET", "THREAT WARNING", "PRIORITY HVT: WARDEN VIKTOR VOLKOV // KESTREL-01. Lethal force cleared.", SpeakerFaction.Hostile);
                    break;
                case 4:
                    Transmit("TAC-NET", "THREAT WARNING", "PRIORITY HVT: COL. ARTEM RADEK // APEX-01. Lethal force cleared.", SpeakerFaction.Hostile);
                    break;
                default:
                    Transmit("TAC-NET", "THREAT WARNING", "PRIORITY HVT: DIR. MARCUS CHEN // AEGIS-ACTUAL. Lethal force cleared.", SpeakerFaction.Hostile);
                    break;
            }
        }

        public static void NotifyBossDefeated(int stage)
        {
            UI.UIManager.Instance?.ShowMilestoneBanner("HVT NEUTRALIZED // " + GetBossCallsign(stage));
        }

        public static string GetActLabel(int stage)
        {
            var clamped = Mathf.Clamp(stage, 1, FinalCampaignStage);
            return Mathf.Clamp(ActiveMissionIndex, 1, CampaignMissionCount) switch
            {
                1 => $"ACT I // BLACKWATER PIER  ({clamped}/{FinalCampaignStage})",
                2 => $"ACT II // IRON CORRIDOR  ({clamped}/{FinalCampaignStage})",
                3 => $"ACT III // KESTREL RELAY  ({clamped}/{FinalCampaignStage})",
                4 => $"ACT IV // BLACKCREST SUMMIT  ({clamped}/{FinalCampaignStage})",
                _ => $"ACT V // AEON BLACKSITE  ({clamped}/{FinalCampaignStage})"
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
            return (m, stage) switch
            {
                (1, 1) => $"OBJ // Clear Pier Checkpoint ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (1, 2) => $"OBJ // Sweep Customs Wave ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (1, 3) => $"OBJ // Breach {GetObjectiveNodeTitle(1, 0)} [E]",
                (1, 4) => $"OBJ // Destroy Pier Manifolds ({s2Done}/{Mathf.Max(1, s2Total)})",
                (1, 5) => $"OBJ // Crush North Quay Guard ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (1, 6) => $"HVT // Neutralize {GetBossCallsign(6)}",
                (1, 7) => $"DEFEND // Cover {GetObjectiveNodeTitle(1, 3)} [E]",
                (1, _) => $"EXFIL // Board {GetObjectiveNodeTitle(1, 4)} [E]",

                (2, 1) => $"OBJ // Clear Canyon Ambush ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (2, 2) => $"OBJ // Break Gorge Snipers ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (2, 3) => $"OBJ // Blow {GetObjectiveNodeTitle(2, 0)} [E]",
                (2, 4) => $"OBJ // Lock Track Diverters ({s2Done}/{Mathf.Max(1, s2Total)})",
                (2, 5) => $"OBJ // Crush Gorge Vanguard ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (2, 6) => $"HVT // Destroy {GetBossCallsign(6)}",
                (2, 7) => $"DEFEND // Hold {GetObjectiveNodeTitle(2, 3)} [E]",
                (2, _) => $"EXFIL // Breach {GetObjectiveNodeTitle(2, 4)} [E]",

                (3, 1) => $"OBJ // Clear South Airlock ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (3, 2) => $"OBJ // Purge Inner Checkpoint ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (3, 3) => $"OBJ // Blow {GetObjectiveNodeTitle(3, 0)} [E]",
                (3, 4) => $"OBJ // Decrypt ROE-9 & Sabotage Wing ({s2Done}/{Mathf.Max(1, s2Total)})",
                (3, 5) => $"OBJ // Clear Core Atrium ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (3, 6) => $"HVT // Defeat {GetBossCallsign(6)} (Flank / HE Frag)",
                (3, 7) => $"DEFEND // Hold {GetObjectiveNodeTitle(3, 3)} [E]",
                (3, _) => $"EXFIL // Hold {GetObjectiveNodeTitle(3, 4)} [E]",

                (4, 1) => $"OBJ // Seize Alpine Bridge ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (4, 2) => $"OBJ // Push Citadel Causeway ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (4, 3) => $"OBJ // Breach {GetObjectiveNodeTitle(4, 0)} [E]",
                (4, 4) => $"OBJ // Overload Capacitor Pylons ({s2Done}/{Mathf.Max(1, s2Total)})",
                (4, 5) => $"OBJ // Break Summit Bastion ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (4, 6) => $"HVT // Eliminate {GetBossCallsign(6)}",
                (4, 7) => $"DEFEND // Hold {GetObjectiveNodeTitle(4, 3)} [E]",
                (4, _) => $"EXFIL // Board {GetObjectiveNodeTitle(4, 4)} [E]",

                (5, 1) => $"OBJ // Storm Outer Gatehouse ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (5, 2) => $"OBJ // Sweep Courtyard Patrol ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (5, 3) => $"OBJ // Breach {GetObjectiveNodeTitle(5, 0)} [E]",
                (5, 4) => $"OBJ // Shred AEON Archives & SATCOM ({s2Done}/{Mathf.Max(1, s2Total)})",
                (5, 5) => $"OBJ // Purge Blackguard Vault ({s1Kills}/{Mathf.Max(1, s1Target)})",
                (5, 6) => $"FINAL HVT // Neutralize {GetBossCallsign(6)}",
                (5, 7) => $"DECISION // Access {GetObjectiveNodeTitle(5, 3)} [E]",
                _ => $"FINAL STAND // Hold {GetObjectiveNodeTitle(5, 4)} [E]"
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
                    1 => $"BLACKWATER PIER CLEARED ({kills} HOSTILES KIA) — Command called Broken Arrow onto their own pier. Spec. Marek Kozlowski ('WRAITH-2') defended the EW jamming array through the shelling so the boat slipped out.",
                    2 => $"IRON CORRIDOR BREACHED ({kills} HOSTILES KIA) — Train #774 derailed and Kastner's railgun wrecked. Cpl. Elena Reyes ('SPARK-3') shoved the failing bulkhead piston open inside collapsing rock so Rourke and Julian made North Gorge.",
                    3 => $"KESTREL SUB-STATION ESCAPED ({kills} HOSTILES KIA) — Volkov fell and ROE-9 exposed Owen Rourke's tragic countersignature on Karst-4. In Halon gas, Major Rourke chose to hold the lift's external override so Julian lived to end Chen.",
                    4 => $"BLACKCREST SUMMIT BROKEN ({kills} HOSTILES KIA) — Col. Radek and APEX-01 shattered. Before orbital fire flattened the mountain pad, Vance ripped Marcus Chen's Washington AEON Citadel telemetry and diverted VALKYRIE-6 south.",
                    _ => save.LastEndingChoice == CampaignEndingKind.BroadcastBlackout
                        ? $"ENDING A // SCORCHED BLACKOUT BROADCAST ({kills} HOSTILES KIA) — With Dir. Chen dead inside his own Citadel, Vance broadcast the full unredacted Karst-4 & Phantom-04 ledger to every global wire. Senate indictments dropped within an hour; Daniel, Marek, Elena, and Owen were cleared."
                        : $"ENDING B // SILENT PROTOCOL SEALED ({kills} HOSTILES KIA) — With Dir. Chen eliminated, Vance pulled the AEON kill-switch silently to keep NATO coalitions intact. Only four brass dogtags riding home in Valkyrie-6 know what Blackout Ledger truly cost."
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
                    new CutsceneBeat(2.5f, new Vector3(16f, 22f, 44f), new Vector3(-6f, 16f, 6f), new Vector3(0f, 1f, 14f), new Vector3(0f, 1f, -10f), 46f, 50f,
                        "DIR. CHEN // AEGIS-SATCOM", "Grid 38T Blackwater Pier: heavy black-market containers linked to missing Karst-4 ordnance.", SpeakerFaction.Command),
                    new CutsceneBeat(2.6f, new Vector3(-12f, 14f, -24f), new Vector3(-4f, 11f, -44f), new Vector3(0f, 1f, -34f), new Vector3(0f, 1.2f, -32f), 48f, 50f,
                        "VANCE // NOMAD-1", "Karst-4 ordnance means Daniel's Phantom-04 sector. Stay sharp—this dock isn't crawling with cheap pirates.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.4f, new Vector3(-4f, 11f, -44f), new Vector3(0f, 8.5f, -51f), new Vector3(0f, 1.2f, -38f), new Vector3(0f, 1.3f, -32f), 48f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Four operators on deck. Clear customs, sweep both harbor piers, and verify every manifest tag.", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(2.6f, new Vector3(14f, 19f, 38f), new Vector3(-4f, 14f, 4f), new Vector3(0f, 1.8f, 14f), new Vector3(0f, 1.5f, -8f), 44f, 48f,
                        "CPL. REYES // SPARK-3", "Amber Gorge Cut: Train #774 blocking center rails. Both flanking trenches manned.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.5f, new Vector3(-20f, 13f, -24f), new Vector3(-16f, 9.5f, -46f), new Vector3(-14f, 1.2f, -36f), new Vector3(-14f, 1.4f, -34f), 46f, 50f,
                        "MAJ. ROURKE // IRONWOOD-1", "Dragic's manifesto pointed right at this gorge. Whoever backs AEGIS wants that evidence melted into slag.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.4f, new Vector3(-24f, 11f, -30f), new Vector3(-16f, 9.0f, -53f), new Vector3(-16f, 1.2f, -42f), new Vector3(-14f, 1.4f, -34f), 48f, 52f,
                        "SGT. VANCE // NOMAD-1", "Marek stayed back to buy us this train. Three left—move.", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(2.6f, new Vector3(0f, 20f, 34f), new Vector3(-16f, 14f, -4f), new Vector3(0f, 1.2f, 2f), new Vector3(-26f, 1.2f, -2f), 46f, 50f,
                        "MAJ. ROURKE // IRONWOOD-1", "Kestrel Sub-Level: Server Vault West, Turbine Hall East. Only two barrels firing now.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.5f, new Vector3(-16f, 14f, -4f), new Vector3(6f, 11f, -30f), new Vector3(-16f, 1.4f, -3f), new Vector3(0f, 1.2f, -34f), 48f, 50f,
                        "SGT. VANCE // NOMAD-1", "Reyes bought us this tunnel door with her life. Whatever ROE-9 hides in that vault, it comes out tonight.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.4f, new Vector3(8f, 10f, -32f), new Vector3(0f, 8.2f, -52f), new Vector3(0f, 1.1f, -40f), new Vector3(0f, 1.3f, -34f), 48f, 52f,
                        "VOLKOV // KESTREL-01", "Another pair of ghosts knocking on Chen's vault. Step inside—the Halon locks are waiting.", SpeakerFaction.Traitor)
                },
                4 => new[]
                {
                    new CutsceneBeat(2.6f, new Vector3(18f, 23f, 22f), new Vector3(-12f, 17f, -4f), new Vector3(0f, 3.5f, 0f), new Vector3(0f, 2.2f, 2f), 44f, 50f,
                        "DIR. CHEN // AEGIS-SATCOM", "Three names off your board, Sergeant. Stand down on the south bridge before APEX-01 scrubs you off.", SpeakerFaction.Command),
                    new CutsceneBeat(2.5f, new Vector3(-12f, 15f, -18f), new Vector3(-6f, 11f, -38f), new Vector3(0f, 2.0f, -6f), new Vector3(0f, 1.6f, -36f), 46f, 50f,
                        "SGT. VANCE // NOMAD-1", "You had Owen co-sign a murder order built on your forged scans, Chen. He died pulling me out of that gas.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.3f, new Vector3(-8f, 10.5f, -34f), new Vector3(0f, 8.5f, -54f), new Vector3(0f, 1.5f, -42f), new Vector3(0f, 2.0f, -26f), 48f, 52f,
                        "SGT. VANCE // NOMAD-1", "No backup. No medevac. Just what's left in the magazine.", SpeakerFaction.Vanguard)
                },
                _ => new[]
                {
                    new CutsceneBeat(2.6f, new Vector3(18f, 24f, 44f), new Vector3(-10f, 17f, 6f), new Vector3(0f, 2.5f, 12f), new Vector3(0f, 1.6f, -6f), 44f, 50f,
                        "VALKYRIE-6 // OVERWATCH", "Sector Zero Washington perimeter visual! Rain squall masks your approach, Nomad-1—every Blackguard gun faces north.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.6f, new Vector3(-10f, 14f, -20f), new Vector3(-4f, 10f, -40f), new Vector3(0f, 1.5f, -12f), new Vector3(0f, 1.4f, -34f), 46f, 50f,
                        "DIR. CHEN // AEGIS-ACTUAL", "Come up the courtyard walls if you want, Vance. This Citadel outlasted three administrations—a rogue sergeant changes nothing.", SpeakerFaction.Traitor),
                    new CutsceneBeat(2.5f, new Vector3(-4f, 10f, -40f), new Vector3(0f, 8.5f, -52f), new Vector3(0f, 1.3f, -40f), new Vector3(0f, 1.8f, -26f), 48f, 52f,
                        "SGT. VANCE // NOMAD-1", "Four fallen brothers on one drive, Chen. Time we audit AEON from the inside.", SpeakerFaction.Vanguard)
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
                        "CPL. REYES // SPARK-3", "Customs bulkhead down! Split West Fuel Pier & East EW Pier — stay within trauma range!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.4f, new Vector3(-8f, 11f, -6f), new Vector3(10f, 13f, 8f), new Vector3(-20f, 1.5f, 8f), new Vector3(20f, 1.5f, 8f), 48f, 52f,
                        "KOZLOWSKI // WRAITH-2", "I see fuel drums West and jamming arrays East. Watch crossfire across the water basin!", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-12f, 9.5f, 4f), new Vector3(4f, 11.0f, 22f), new Vector3(0f, 1.8f, 14f), new Vector3(6f, 1.6f, 34f), 46f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Drive hitch severed! Push north past the locomotive—Kastner's rail armor waiting beyond Gate 2!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.4f, new Vector3(-18f, 12f, 4f), new Vector3(18f, 12f, 4f), new Vector3(-28f, 1.5f, 2f), new Vector3(28f, 1.5f, 2f), 48f, 52f,
                        "CPL. REYES // SPARK-3", "Both gorge pockets locked onto backup generators! Kill the track diverters now!", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(9f, 8.5f, -38f), new Vector3(0f, 10.5f, -28f), new Vector3(0f, 1.5f, -26f), new Vector3(0f, 1.5f, -4f), 48f, 52f,
                        "VANCE // NOMAD-1", "Airlock blown! Vault Terminal West—get ready to pull ROE-9.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.5f, new Vector3(-6f, 11f, -14f), new Vector3(10f, 12f, -4f), new Vector3(-26f, 1.5f, -3f), new Vector3(26f, 1.5f, -3f), 48f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Decrypt that West Mainframe first, Sergeant—whatever name pops up, face it and shut East Turbine down!", SpeakerFaction.Vanguard)
                },
                4 => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-10f, 9.5f, 16f), new Vector3(0f, 11.0f, 26f), new Vector3(0f, 2.0f, 26f), new Vector3(0f, 1.6f, 42f), 46f, 52f,
                        "SGT. VANCE // NOMAD-1", "Citadel North Gate down! Popping IR flare on Pad 06—come pick up the last piece.", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.3f, new Vector3(-14f, 13f, 20f), new Vector3(12f, 13f, 28f), new Vector3(0f, 2.2f, 12f), new Vector3(0f, 1.8f, 38f), 48f, 52f,
                        "VALKYRIE-6 // AIR SUPPORT", "Radar sweep clean for thirty seconds—APEX cleaner drop teams inbound onto North Bastion!", SpeakerFaction.Vanguard)
                },
                _ => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(10f, 8.5f, -28f), new Vector3(0f, 11.5f, -16f), new Vector3(0f, 1.6f, -14f), new Vector3(0f, 1.6f, 4f), 46f, 52f,
                        "SGT. VANCE // NOMAD-1", "Security Gate wrecked! Pushing into AEON Atrium—shred both Chen's Archive Vault and Satcom Wing!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.5f, new Vector3(-14f, 13f, 0f), new Vector3(14f, 13f, 0f), new Vector3(-28f, 1.8f, 0f), new Vector3(28f, 1.8f, 0f), 46f, 50f,
                        "DIR. CHEN // AEGIS-ACTUAL", "Seal Vault Sector Bravo! Blackguard weapons free across the inner courtyard—drop him now!", SpeakerFaction.Traitor)
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
                        "CAPT. DRAGIC // WARDEN-01", "Unidentified platoon inside Pier North—Warden armor engaging lethal!", SpeakerFaction.Hostile),
                    new CutsceneBeat(2.4f, new Vector3(11f, 9.6f, 33f), new Vector3(-2f, 10f, 26f), new Vector3(0f, 1.5f, 40f), new Vector3(0f, 1.3f, 30f), 46f, 50f,
                        "MAJ. ROURKE // IRONWOOD-1", "Watch those coaxial bursts and coastal mortar flares! Tear his hull open!", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(-6f, 9.5f, 30f), new Vector3(18f, 10.2f, 35f), new Vector3(8f, 1.5f, 43f), new Vector3(8f, 1.6f, 44f), 44f, 50f,
                        "DIR. KASTNER // GOLIATH-02", "All freight manifests flagged for incineration. Goliath-02 firing on unauthorized witnesses!", SpeakerFaction.Hostile),
                    new CutsceneBeat(2.4f, new Vector3(18f, 10.2f, 35f), new Vector3(0f, 10.5f, 28f), new Vector3(8f, 1.5f, 44f), new Vector3(6f, 1.5f, 38f), 46f, 50f,
                        "SGT. VANCE // NOMAD-1", "That barrel locks magnetically—sidestep behind heavy bunkers whenever the amber beam paints us!", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(2.9f, new Vector3(-11f, 8.8f, 20f), new Vector3(11f, 9.2f, 24f), new Vector3(0f, 1.5f, 33f), new Vector3(0f, 1.6f, 33f), 44f, 50f,
                        "VOLKOV // KESTREL-01", "Look at your own Major, Sergeant—still carrying Rourke all the way down here just to watch Chen lock the blast door!", SpeakerFaction.Traitor),
                    new CutsceneBeat(2.6f, new Vector3(11f, 9.2f, 24f), new Vector3(0f, 9.6f, 18f), new Vector3(0f, 1.6f, 33f), new Vector3(0f, 1.4f, 28f), 46f, 50f,
                        "MAJ. ROURKE // IRONWOOD-1", "Shut his mouth permanently, Julian! Watch the reactive riot barrier and bounce frags behind him!", SpeakerFaction.Vanguard)
                },
                4 => new[]
                {
                    new CutsceneBeat(2.9f, new Vector3(-16f, 10.5f, 4f), new Vector3(15f, 11.2f, 8f), new Vector3(0f, 1.8f, 14f), new Vector3(0f, 1.8f, 14f), 44f, 50f,
                        "COL. RADEK // APEX-01", "Closing the file on Karst-4, Sergeant. Nothing personal—just cleanup.", SpeakerFaction.ApexBoss),
                    new CutsceneBeat(2.4f, new Vector3(15f, 11.2f, 8f), new Vector3(0f, 11.5f, -4f), new Vector3(0f, 1.8f, 14f), new Vector3(0f, 1.6f, 8f), 46f, 50f,
                        "SGT. VANCE // NOMAD-1", "Nothing personal about four dead brothers either, Colonel. Let's settle Chen's invoice right now.", SpeakerFaction.Vanguard)
                },
                _ => new[]
                {
                    new CutsceneBeat(3.0f, new Vector3(-14f, 10.5f, 20f), new Vector3(14f, 11.2f, 24f), new Vector3(0f, 1.9f, 35f), new Vector3(0f, 1.9f, 35f), 44f, 50f,
                        "DIR. CHEN // AEGIS-ACTUAL", "Four dead operators couldn't stop AEGIS policy, Vance! Step forward and join Daniel inside the blackout ledger!", SpeakerFaction.Traitor),
                    new CutsceneBeat(2.6f, new Vector3(14f, 11.2f, 24f), new Vector3(0f, 11.2f, 16f), new Vector3(0f, 1.9f, 35f), new Vector3(0f, 1.6f, 28f), 46f, 50f,
                        "SGT. VANCE // NOMAD-1", "AEGIS ends right here in Sector Zero, Marcus. Draw.", SpeakerFaction.Vanguard)
                }
            };
        }

        public static CutsceneBeat[] GetVictoryEvacBeats(int missionIndex, bool isBroadcastEnding)
        {
            return Mathf.Clamp(missionIndex, 1, CampaignMissionCount) switch
            {
                1 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(4f, 7.5f, 34f), new Vector3(16f, 9.5f, 44f), new Vector3(16f, 1.2f, 46f), new Vector3(12f, 1.4f, 40f), 48f, 52f,
                        "KOZLOWSKI // WRAITH-2", "Get on that gunboat, Sergeant! EW dish is melting down—I'll hold their radar sweep off your wake!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.7f, new Vector3(16f, 9.5f, 44f), new Vector3(26f, 11.5f, 55f), new Vector3(12f, 1.4f, 40f), new Vector3(0f, 2.0f, 34f), 50f, 54f,
                        "SGT. VANCE // NOMAD-1", "Gunboat clear! Marek's beacon just flatlined at the EW tower—Broken Arrow erased his post!", SpeakerFaction.Vanguard)
                },
                2 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(-4f, 8.0f, 34f), new Vector3(6f, 9.4f, 44f), new Vector3(8f, 1.2f, 48f), new Vector3(8f, 1.6f, 54f), 48f, 52f,
                        "CPL. REYES // SPARK-3", "Pistons dead! Wedging my rifle frame under the lockpin—run straight through North Gorge Tunnel!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.7f, new Vector3(6f, 9.4f, 44f), new Vector3(14f, 10.8f, 54f), new Vector3(8f, 1.6f, 54f), new Vector3(8f, 1.8f, 58f), 50f, 54f,
                        "SGT. VANCE // NOMAD-1", "Bulkhead dropped behind us—Reyes triggered the lock from inside the gorge fire! Move out, Rourke!", SpeakerFaction.Vanguard)
                },
                3 => new[]
                {
                    new CutsceneBeat(2.8f, new Vector3(-10f, 7.8f, 28f), new Vector3(-2f, 9.4f, 39f), new Vector3(0f, 1.2f, 40f), new Vector3(0f, 1.7f, 46f), 48f, 52f,
                        "MAJ. ROURKE // IRONWOOD-1", "Lift's away, Julian... Chest seal's leaking Halon. Carry the blackout ledger up the mountain and finish Chen's war...", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.6f, new Vector3(-2f, 9.4f, 39f), new Vector3(10f, 10.8f, 50f), new Vector3(0f, 1.7f, 46f), new Vector3(0f, 2.2f, 52f), 50f, 54f,
                        "SGT. VANCE // NOMAD-1", "Hold on, Owen! No—the cage seal just cycled. Three graves down... only Chen left.", SpeakerFaction.Vanguard)
                },
                4 => new[]
                {
                    new CutsceneBeat(2.7f, new Vector3(-10f, 9.0f, 28f), new Vector3(0f, 7.5f, 38f), new Vector3(0f, 1.6f, 42f), new Vector3(0f, 1.8f, 42f), 46f, 50f,
                        "VALKYRIE-6 // PILOT", "North Summit Pad hot! Hop aboard, Nomad-1—Washington AEON frequency just spiked live!", SpeakerFaction.Vanguard),
                    new CutsceneBeat(2.7f, new Vector3(0f, 7.5f, 38f), new Vector3(14f, 11.0f, 54f), new Vector3(0f, 1.8f, 42f), new Vector3(0f, 2.5f, 0f), 50f, 54f,
                        "SGT. VANCE // NOMAD-1", "Set fuel for Washington Sector Zero, Valkyrie. We don't stop at Blackcrest while Marcus Chen still breathes.", SpeakerFaction.Vanguard)
                },
                _ => isBroadcastEnding
                    ? new[]
                    {
                        new CutsceneBeat(2.5f, new Vector3(-14f, 10f, 26f), new Vector3(0f, 7.5f, 32f), new Vector3(0f, 1.8f, 42f), new Vector3(0f, 1.8f, 42f), 46f, 48f,
                            "VALKYRIE-6 // PILOT", "Valkyrie-6 on the deck, Sergeant! Every AEGIS black frequency is blowing up—get aboard before hunter teams close the ridge!", SpeakerFaction.Vanguard),
                        new CutsceneBeat(2.7f, new Vector3(0f, 7.5f, 32f), new Vector3(12f, 7.2f, 52f), new Vector3(0f, 1.8f, 42f), new Vector3(0f, 2.0f, 42f), 46f, 50f,
                            "SGT. VANCE // NOMAD-1", "Kozlowski, Reyes, Rourke, Daniel—your names just went out to the world. I'm ready for whatever hunts me down below.", SpeakerFaction.Vanguard)
                    }
                    : new[]
                    {
                        new CutsceneBeat(2.5f, new Vector3(-14f, 10f, 26f), new Vector3(0f, 7.5f, 32f), new Vector3(0f, 1.8f, 42f), new Vector3(0f, 1.8f, 42f), 46f, 48f,
                            "VALKYRIE-6 // PILOT", "Pad clear, Nomad-1. Satellite uplink dark, zero leaks confirmed. Boarding Valkyrie-6 alone.", SpeakerFaction.Vanguard),
                        new CutsceneBeat(2.7f, new Vector3(0f, 7.5f, 32f), new Vector3(12f, 7.2f, 52f), new Vector3(0f, 1.8f, 42f), new Vector3(0f, 2.0f, 42f), 46f, 50f,
                            "SGT. VANCE // NOMAD-1", "Three empty seats on this bird. Nobody gets what Karst-4 cost us—just three folded flags.", SpeakerFaction.Vanguard)
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
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Officer, cleared, 3, "#42A5F5", "IRONWOOD-1 // MAJ. OWEN ROURKE", "Field CO. Reluctantly countersigned Karst-4 strike key believing Phantom-04 was lost."));
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Recon, cleared, 1, "#42A5F5", "WRAITH-2 // SPEC. MAREK KOZLOWSKI", "EW/Recon Marksman. Holds Blackwater radar jammer under friendly-fire barrage."));
                    sb.AppendLine(StatusTag(save, SquadCallsigns.Breacher, cleared, 2, "#42A5F5", "SPARK-3 // CPL. ELENA REYES", "Demolitions Breacher. Overrides crumbling Amber Gorge blast shutters manually."));
                    sb.AppendLine("<color=#F2A72E><b>AEGIS-ACTUAL // DIR. MARCUS CHEN</b></color> — Final Architect. Ordered Phantom-04 erasure & Protocol Zero purge across five theaters.");
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
                    var kiaTotal = (save.IsOperatorKia(SquadCallsigns.Recon) || cleared >= 1 ? 1 : 0)
                        + (save.IsOperatorKia(SquadCallsigns.Breacher) || cleared >= 2 ? 1 : 0)
                        + (save.IsOperatorKia(SquadCallsigns.Officer) || cleared >= 3 ? 1 : 0);
                    sb.AppendLine($"<b>AFTER-ACTION LEDGER // PROTOCOL ZERO</b>  |  ACTS: {cleared}/{CampaignMissionCount}  |  KIA ROSTER: {kiaTotal}/3  |  {endingLabel}");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT I — BLACKWATER PIER (K4-09 DOCK SWEEP)</b>");
                    sb.AppendLine("Smugglers turned out to be AEGIS-contracted scrap crews; Chen dropped artillery onto Vanguard-09's own dock.");
                    sb.AppendLine(cleared >= 1
                        ? "<color=#E05A47>[CASUALTY]: WRAITH-2 (Kozlowski) KIA jamming naval strike lock so gunboat escaped.</color>"
                        : "<color=#A6926F>[ACTIVE]: Breach Customs Gate 1 and sweep pier manifests.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT II — IRON CORRIDOR (AUDIT INCINERATOR CONVOY)</b>");
                    sb.AppendLine("Ambushed Armored Train #774 carrying Karst-4 ash barrels and strike checksums inside Amber Gorge.");
                    sb.AppendLine(cleared >= 2
                        ? "<color=#E05A47>[CASUALTY]: SPARK-3 (Reyes) KIA holding collapsed tunnel bulkheads during thermite blowout.</color>"
                        : "<color=#A6926F>[LOCKED]: Halt Train #774 in Amber Gorge.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT III — KESTREL SUB-STATION (ROE-9 SIGNATURE)</b>");
                    sb.AppendLine("Decrypted Rourke's tragic Karst-4 signature and faced Chen's Halon containment purge underground.");
                    sb.AppendLine(cleared >= 3
                        ? "<color=#E05A47>[CASUALTY]: IRONWOOD-1 (Rourke) KIA holding external lift override inside choking Halon gas.</color>"
                        : "<color=#A6926F>[LOCKED]: Decrypt West Server Mainframe underground.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT IV — BLACKCREST SUMMIT (CITADEL BREACH)</b>");
                    sb.AppendLine("Solo Vance shattered Col. Radek's APEX-01 spire defense and captured Chen's Washington AEON Citadel uplink.");
                    sb.AppendLine(cleared >= 4
                        ? "<color=#49C96E>[CLEARED]: Blackcrest Spire offline — Valkyrie-6 diverted toward Sector Zero.</color>"
                        : "<color=#A6926F>[LOCKED]: Ascend Blackcrest Citadel and seize AEON relay telemetry.</color>");
                    sb.AppendLine();
                    sb.AppendLine("<b>ACT V — AEON BLACKSITE (PROTOCOL ZERO FINALE)</b>");
                    sb.AppendLine("Confront Dir. Marcus Chen in Washington, unlock the AEON Core Console, and decide whether the truth burns globally.");
                    sb.AppendLine(cleared >= 5
                        ? $"<color=#49C96E>[FINAL STATUS]: Peak Stage {bestWaveReached}/{FinalCampaignStage} — AEON Citadel Fallen.</color>"
                        : $"<color=#A6926F>[FINAL ACT]: Peak Stage {bestWaveReached}/{FinalCampaignStage} — Storm Sector Zero alone.</color>");
                    break;
            }

            return sb.ToString();
        }

        private static string StatusTag(SaveManager save, string tag, int cleared, int minClearedAct, string aliveHex, string label, string bio)
        {
            var dead = save.IsOperatorKia(tag) || cleared >= minClearedAct;
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

        /// <summary>
        /// Deterministic UTF-8 FNV-1a asset key matching Tools/generate_radio_voices.py
        /// so Unity Resources/VoiceRadio clips bind automatically without manual string tables.
        /// </summary>
        public static string GetVoiceClipKey(string callsign, string body)
        {
            var c = (callsign ?? string.Empty).Trim();
            var b = (body ?? string.Empty).Trim();
            var bytes = Encoding.UTF8.GetBytes(c + "|" + b);
            var hash = 2166136261u;
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 16777619u;
            }

            var slugSb = new StringBuilder(16);
            var prevUnderscore = true;
            foreach (var ch in c.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    slugSb.Append(ch);
                    prevUnderscore = false;
                }
                else if (!prevUnderscore)
                {
                    slugSb.Append('_');
                    prevUnderscore = true;
                }
            }
            var slug = slugSb.ToString().Trim('_');
            if (string.IsNullOrEmpty(slug)) slug = "radio";
            return $"vo_{slug}_{hash:x8}";
        }
    }
}
