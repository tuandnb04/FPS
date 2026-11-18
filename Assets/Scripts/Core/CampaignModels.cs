using System;
using UnityEngine;

namespace Core
{
    public enum SpeakerFaction
    {
        Vanguard,
        Command,
        Hostile,
        Traitor,
        ApexBoss
    }

    public enum BossBehaviorKind
    {
        WardenBarge = 0,
        GoliathRail = 1,
        VolkovJuggernaut = 2,
        RadekApex = 3,
        ChenOverwatch = 4
    }

    public enum CampaignEndingKind
    {
        None = 0,
        BroadcastBlackout = 1,
        SilentKillSwitch = 2
    }

    public static class SquadCallsigns
    {
        public const string Lead = "NOMAD-1";
        public const string Officer = "IRONWOOD-1";
        public const string Recon = "WRAITH-2";
        public const string Breacher = "SPARK-3";
    }

    [Serializable]
    public readonly struct RadioLine
    {
        public readonly string Callsign;
        public readonly string Channel;
        public readonly string Body;
        public readonly SpeakerFaction Faction;
        public readonly float HoldSeconds;

        public RadioLine(string callsign, string channel, string body, SpeakerFaction faction, float holdSeconds = 4.4f)
        {
            Callsign = callsign;
            Channel = channel;
            Body = body;
            Faction = faction;
            HoldSeconds = Mathf.Clamp(holdSeconds, 2.2f, 8.5f);
        }
    }

    public readonly struct CutsceneBeat
    {
        public readonly float Duration;
        public readonly Vector3 StartPos;
        public readonly Vector3 EndPos;
        public readonly Vector3 StartLook;
        public readonly Vector3 EndLook;
        public readonly float StartFov;
        public readonly float EndFov;
        public readonly string Speaker;
        public readonly string Caption;
        public readonly SpeakerFaction Faction;

        public CutsceneBeat(
            float duration,
            Vector3 startPos,
            Vector3 endPos,
            Vector3 startLook,
            Vector3 endLook,
            float startFov,
            float endFov,
            string speaker,
            string caption,
            SpeakerFaction faction = SpeakerFaction.Vanguard)
        {
            Duration = duration;
            StartPos = startPos;
            EndPos = endPos;
            StartLook = startLook;
            EndLook = endLook;
            StartFov = startFov;
            EndFov = endFov;
            Speaker = speaker;
            Caption = caption;
            Faction = faction;
        }
    }

    public readonly struct IntelLogEntry
    {
        public readonly string Id;
        public readonly int MissionIndex;
        public readonly string NodeTitle;
        public readonly string LocationHint;
        public readonly string Header;
        public readonly string BodyText;
        public readonly int RewardCredits;
        public readonly RadioLine[] DiscoveryDialogue;

        public IntelLogEntry(
            string id,
            int missionIndex,
            string nodeTitle,
            string locationHint,
            string header,
            string bodyText,
            int rewardCredits,
            params RadioLine[] discoveryDialogue)
        {
            Id = id;
            MissionIndex = missionIndex;
            NodeTitle = nodeTitle;
            LocationHint = locationHint;
            Header = header;
            BodyText = bodyText;
            RewardCredits = rewardCredits;
            DiscoveryDialogue = discoveryDialogue ?? Array.Empty<RadioLine>();
        }
    }

    public readonly struct WalkableZoneRect
    {
        public readonly float MinX;
        public readonly float MaxX;
        public readonly float MinZ;
        public readonly float MaxZ;
        public readonly int MinStage;
        public readonly int MaxStage;

        public WalkableZoneRect(float minX, float maxX, float minZ, float maxZ, int minStage = 1, int maxStage = 99)
        {
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
            MinStage = minStage;
            MaxStage = maxStage;
        }

        public bool IsActiveForStage(int stage) => stage >= MinStage && stage <= MaxStage;

        public bool Contains(Vector3 p) => p.x >= MinX && p.x <= MaxX && p.z >= MinZ && p.z <= MaxZ;

        public Vector3 ClampPoint(Vector3 p) => new(Mathf.Clamp(p.x, MinX, MaxX), p.y, Mathf.Clamp(p.z, MinZ, MaxZ));
    }
}
