using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Core
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int credits = 200;
        public int highScore;
        public int highestWave = 1;
        public int highestMissionCompleted;
        public int totalKills;

        public int armorRank;
        public int firepowerRank;
        public int thrusterRank;
        public int greedRank;

        public int selectedWeapon;
        public int selectedSkin;

        public List<int> unlockedWeapons = new() { 0 };
        public List<int> unlockedSkins = new() { 0 };
        public List<string> discoveredIntelIds = new();
        public List<string> kiaOperators = new();
        public int lastEndingChoice;

        public string checksum = string.Empty;
    }

    /// <summary>
    /// Store-ready encrypted JSON persistence with checksum integrity verification
    /// and atomic temp-file swap under Application.persistentDataPath.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        private static SaveManager Instance { get; set; }

        public const int MaxUpgradeRank = 5;
        public const int WeaponCount = 6;
        private const string SaveFileName = "vanguard_save.dat";
        private const string EncryptionKey = "VANGUARD_NEON_STORE_2026_KEY!";

        private GameSaveData _data = new();
        private bool _isLoaded;

        public GameSaveData Data
        {
            get
            {
                if (!_isLoaded) Load();
                return _data;
            }
        }

        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static SaveManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = GameObject.Find("GameSystems");
            if (go == null) go = new GameObject("GameSystems");
            var mgr = go.GetComponent<SaveManager>();
            if (mgr == null) mgr = go.AddComponent<SaveManager>();
            return mgr;
        }

        public static bool PendingAutoDeploy { get; set; }
        public static int SelectedMissionIndex { get; set; } = 1;
        public static int PendingCarryScore { get; set; }
        public static int PendingCarryKills { get; set; }
        public static int PendingCarryCredits { get; set; }

        public void RecordMissionCompleted(int missionIndex)
        {
            var clamped = Mathf.Clamp(missionIndex, 1, StoryDirector.CampaignMissionCount);
            if (clamped > Data.highestMissionCompleted)
            {
                Data.highestMissionCompleted = clamped;
                Save();
            }
        }

        public bool IsIntelDiscovered(string intelId)
        {
            return !string.IsNullOrEmpty(intelId) && Data.discoveredIntelIds != null && Data.discoveredIntelIds.Contains(intelId);
        }

        public bool UnlockIntelLog(string intelId)
        {
            if (string.IsNullOrEmpty(intelId)) return false;
            Data.discoveredIntelIds ??= new List<string>();
            if (Data.discoveredIntelIds.Contains(intelId)) return false;
            Data.discoveredIntelIds.Add(intelId);
            Save();
            return true;
        }

        public int DiscoveredIntelCount => Data.discoveredIntelIds != null ? Data.discoveredIntelIds.Count : 0;

        public bool IsOperatorKia(string roleTag)
        {
            return !string.IsNullOrEmpty(roleTag) && Data.kiaOperators != null && Data.kiaOperators.Contains(roleTag);
        }

        public bool MarkOperatorKia(string roleTag)
        {
            if (string.IsNullOrEmpty(roleTag)) return false;
            Data.kiaOperators ??= new List<string>();
            if (Data.kiaOperators.Contains(roleTag)) return false;
            Data.kiaOperators.Add(roleTag);
            Save();
            return true;
        }

        public int KiaCount => Data.kiaOperators != null ? Data.kiaOperators.Count : 0;

        public CampaignEndingKind LastEndingChoice => (CampaignEndingKind)Mathf.Clamp(Data.lastEndingChoice, 0, 2);

        public void RecordEndingChoice(CampaignEndingKind choice)
        {
            Data.lastEndingChoice = (int)choice;
            Save();
        }

        public void SyncRosterForActEntry(int missionIndex, bool preserveExistingLosses)
        {
            Data.kiaOperators ??= new List<string>();
            if (!preserveExistingLosses)
            {
                Data.kiaOperators.Clear();
                if (missionIndex >= 2 && !Data.kiaOperators.Contains(SquadCallsigns.Recon))
                    Data.kiaOperators.Add(SquadCallsigns.Recon);
                if (missionIndex >= 3 && !Data.kiaOperators.Contains(SquadCallsigns.Breacher))
                    Data.kiaOperators.Add(SquadCallsigns.Breacher);
                if (missionIndex >= 4 && !Data.kiaOperators.Contains(SquadCallsigns.Officer))
                    Data.kiaOperators.Add(SquadCallsigns.Officer);
            }
            else
            {
                if (missionIndex >= 2 && !Data.kiaOperators.Contains(SquadCallsigns.Recon))
                    Data.kiaOperators.Add(SquadCallsigns.Recon);
                if (missionIndex >= 3 && !Data.kiaOperators.Contains(SquadCallsigns.Breacher))
                    Data.kiaOperators.Add(SquadCallsigns.Breacher);
                if (missionIndex >= 4 && !Data.kiaOperators.Contains(SquadCallsigns.Officer))
                    Data.kiaOperators.Add(SquadCallsigns.Officer);
            }
            Save();
        }

        public void ResetCampaignRoster()
        {
            Data.kiaOperators ??= new List<string>();
            Data.kiaOperators.Clear();
            Data.lastEndingChoice = 0;
            Save();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Load();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Load()
        {
            var mainPath = SaveFilePath;
            var bakPath = mainPath + ".bak";

            if (TryReadValidatedFile(mainPath, out var loaded))
            {
                _data = loaded;
            }
            else if (TryReadValidatedFile(bakPath, out var backupLoaded))
            {
                _data = backupLoaded;
            }
            else
            {
                _data = CreateFreshData();
            }

            NormalizeLists(_data);
            _isLoaded = true;
        }

        public void Save()
        {
            if (!_isLoaded && _data == null) _data = CreateFreshData();
            NormalizeLists(_data);
            _data.checksum = ComputeChecksum(_data);

            var json = JsonUtility.ToJson(_data, false);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var cipherBytes = XorBytes(plainBytes, EncryptionKey);
            var encodedPayload = Convert.ToBase64String(cipherBytes);

            var mainPath = SaveFilePath;
            var tmpPath = mainPath + ".tmp";
            var bakPath = mainPath + ".bak";

            var dir = Path.GetDirectoryName(mainPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(tmpPath, encodedPayload, Encoding.UTF8);

            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }
            if (File.Exists(mainPath))
            {
                File.Move(mainPath, bakPath);
            }
            File.Move(tmpPath, mainPath);
            PlayerPrefs.SetInt("Vanguard_HighScore", _data.highScore);
            PlayerPrefs.Save();
        }

        public void AddCredits(int amount)
        {
            if (amount <= 0) return;
            Data.credits += amount;
            Save();
        }

        public static int GetUpgradeCost(int currentRank)
        {
            if (currentRank >= MaxUpgradeRank) return 0;
            return 100 + currentRank * 75;
        }

        public int GetUpgradeRank(string statKey)
        {
            var d = Data;
            return statKey switch
            {
                "armor" => d.armorRank,
                "firepower" => d.firepowerRank,
                "thruster" => d.thrusterRank,
                "greed" => d.greedRank,
                _ => 0
            };
        }

        public bool TryUpgradeStat(string statKey)
        {
            var rank = GetUpgradeRank(statKey);
            if (rank >= MaxUpgradeRank) return false;
            var cost = GetUpgradeCost(rank);
            if (Data.credits < cost) return false;

            _data.credits -= cost;
            switch (statKey)
            {
                case "armor": _data.armorRank++; break;
                case "firepower": _data.firepowerRank++; break;
                case "thruster": _data.thrusterRank++; break;
                case "greed": _data.greedRank++; break;
                default:
                    _data.credits += cost;
                    return false;
            }
            Save();
            return true;
        }

        public bool IsWeaponUnlocked(int id)
        {
            return Data.unlockedWeapons.Contains(id);
        }

        public bool TryUnlockOrEquipWeapon(int id, int cost)
        {
            if (id is < 0 or >= WeaponCount) return false;
            if (IsWeaponUnlocked(id))
            {
                Data.selectedWeapon = id;
                Save();
                return true;
            }
            if (Data.credits < cost) return false;
            Data.credits -= cost;
            Data.unlockedWeapons.Add(id);
            Data.selectedWeapon = id;
            Save();
            return true;
        }

        public bool IsSkinUnlocked(int id)
        {
            return Data.unlockedSkins.Contains(id);
        }

        public bool TryUnlockOrEquipSkin(int id, int cost)
        {
            if (id is < 0 or > 2) return false;
            if (IsSkinUnlocked(id))
            {
                Data.selectedSkin = id;
                Save();
                return true;
            }
            if (Data.credits < cost) return false;
            Data.credits -= cost;
            Data.unlockedSkins.Add(id);
            Data.selectedSkin = id;
            Save();
            return true;
        }

        public void RecordRunResult(int runScore, int runKills, int waveReached)
        {
            var d = Data;
            if (runScore > d.highScore) d.highScore = runScore;
            if (waveReached > d.highestWave) d.highestWave = waveReached;
            d.totalKills += Mathf.Max(0, runKills);
            Save();
        }

        private static GameSaveData CreateFreshData()
        {
            return new GameSaveData
            {
                credits = 200,
                highScore = 0,
                highestWave = 1,
                totalKills = 0,
                armorRank = 0,
                firepowerRank = 0,
                thrusterRank = 0,
                greedRank = 0,
                selectedWeapon = 0,
                selectedSkin = 0,
                unlockedWeapons = new List<int> { 0 },
                unlockedSkins = new List<int> { 0 },
                discoveredIntelIds = new List<string>(),
                kiaOperators = new List<string>()
            };
        }

        private static void NormalizeLists(GameSaveData d)
        {
            d.highestMissionCompleted = Mathf.Clamp(d.highestMissionCompleted, 0, StoryDirector.CampaignMissionCount);
            d.unlockedWeapons ??= new List<int>();
            if (!d.unlockedWeapons.Contains(0)) d.unlockedWeapons.Add(0);
            d.unlockedSkins ??= new List<int>();
            if (!d.unlockedSkins.Contains(0)) d.unlockedSkins.Add(0);
            d.discoveredIntelIds ??= new List<string>();
            d.kiaOperators ??= new List<string>();
            if (!d.unlockedWeapons.Contains(d.selectedWeapon)) d.selectedWeapon = 0;
            if (!d.unlockedSkins.Contains(d.selectedSkin)) d.selectedSkin = 0;
        }

        private static bool TryReadValidatedFile(string path, out GameSaveData loaded)
        {
            loaded = null;
            if (!File.Exists(path)) return false;

            try
            {
                var encoded = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(encoded)) return false;
                var cipherBytes = Convert.FromBase64String(encoded);
                var plainBytes = XorBytes(cipherBytes, EncryptionKey);
                var json = Encoding.UTF8.GetString(plainBytes);
                var parsed = JsonUtility.FromJson<GameSaveData>(json);
                if (parsed == null) return false;

                var expected = ComputeChecksum(parsed);
                if (!string.Equals(parsed.checksum, expected, StringComparison.Ordinal))
                {
                    return false;
                }

                loaded = parsed;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ComputeChecksum(GameSaveData d)
        {
            var sb = new StringBuilder();
            sb.Append(d.credits).Append('|')
              .Append(d.highScore).Append('|')
              .Append(d.highestWave).Append('|')
              .Append(d.highestMissionCompleted).Append('|')
              .Append(d.totalKills).Append('|')
              .Append(d.armorRank).Append('|')
              .Append(d.firepowerRank).Append('|')
              .Append(d.thrusterRank).Append('|')
              .Append(d.greedRank).Append('|')
              .Append(d.selectedWeapon).Append('|')
              .Append(d.selectedSkin).Append('|');

            if (d.unlockedWeapons != null)
            {
                foreach (var t in d.unlockedWeapons)
                    sb.Append(t).Append(',');
            }
            sb.Append('|');
            if (d.unlockedSkins != null)
            {
                foreach (var t in d.unlockedSkins)
                    sb.Append(t).Append(',');
            }
            if (d.discoveredIntelIds is { Count: > 0 })
            {
                sb.Append('|');
                foreach (var id in d.discoveredIntelIds)
                    sb.Append(id).Append(',');
            }
            if (d.kiaOperators is { Count: > 0 } || d.lastEndingChoice != 0)
            {
                sb.Append('|').Append(d.lastEndingChoice).Append(':');
                if (d.kiaOperators != null)
                {
                    foreach (var op in d.kiaOperators)
                        sb.Append(op).Append(',');
                }
            }

            var hash = 2166136261u;
            var raw = sb.ToString();
            foreach (var t in raw)
            {
                hash ^= t;
                hash *= 16777619u;
            }
            return hash.ToString("X8");
        }

        private static byte[] XorBytes(byte[] input, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var result = new byte[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                result[i] = (byte)(input[i] ^ keyBytes[i % keyBytes.Length]);
            }
            return result;
        }
    }
}
