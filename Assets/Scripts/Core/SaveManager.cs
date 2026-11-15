using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TopDownShooter.Core
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int credits = 200;
        public int highScore = 0;
        public int highestWave = 1;
        public int totalKills = 0;

        public int armorRank = 0;
        public int firepowerRank = 0;
        public int thrusterRank = 0;
        public int greedRank = 0;

        public int selectedWeapon = 0;
        public int selectedSkin = 0;

        public List<int> unlockedWeapons = new List<int> { 0 };
        public List<int> unlockedSkins = new List<int> { 0 };

        public string checksum = string.Empty;
    }

    /// <summary>
    /// Store-ready encrypted JSON persistence with checksum integrity verification
    /// and atomic temp-file swap under Application.persistentDataPath.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        public const int MaxUpgradeRank = 5;
        public const int WeaponCount = 6;
        private const string SaveFileName = "vanguard_save.dat";
        private const string EncryptionKey = "VANGUARD_NEON_STORE_2026_KEY!";

        private GameSaveData data = new GameSaveData();
        private bool isLoaded;

        public GameSaveData Data
        {
            get
            {
                if (!isLoaded) Load();
                return data;
            }
        }

        public static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static SaveManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            GameObject go = GameObject.Find("GameSystems");
            if (go == null) go = new GameObject("GameSystems");
            SaveManager mgr = go.GetComponent<SaveManager>();
            if (mgr == null) mgr = go.AddComponent<SaveManager>();
            return mgr;
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

        public void Load()
        {
            string mainPath = SaveFilePath;
            string bakPath = mainPath + ".bak";

            if (TryReadValidatedFile(mainPath, out GameSaveData loaded))
            {
                data = loaded;
            }
            else if (TryReadValidatedFile(bakPath, out GameSaveData backupLoaded))
            {
                data = backupLoaded;
            }
            else
            {
                data = CreateFreshData();
            }

            NormalizeLists(data);
            isLoaded = true;
        }

        public void Save()
        {
            if (!isLoaded && data == null) data = CreateFreshData();
            NormalizeLists(data);
            data.checksum = ComputeChecksum(data);

            string json = JsonUtility.ToJson(data, false);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] cipherBytes = XorBytes(plainBytes, EncryptionKey);
            string encodedPayload = Convert.ToBase64String(cipherBytes);

            string mainPath = SaveFilePath;
            string tmpPath = mainPath + ".tmp";
            string bakPath = mainPath + ".bak";

            string dir = Path.GetDirectoryName(mainPath);
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
            PlayerPrefs.SetInt("Vanguard_HighScore", data.highScore);
            PlayerPrefs.Save();
        }

        public void AddCredits(int amount)
        {
            if (amount <= 0) return;
            Data.credits += amount;
            Save();
        }

        public bool TrySpendCredits(int cost)
        {
            if (cost < 0 || Data.credits < cost) return false;
            Data.credits -= cost;
            Save();
            return true;
        }

        public int GetUpgradeCost(int currentRank)
        {
            if (currentRank >= MaxUpgradeRank) return 0;
            return 100 + currentRank * 75;
        }

        public int GetUpgradeRank(string statKey)
        {
            GameSaveData d = Data;
            switch (statKey)
            {
                case "armor": return d.armorRank;
                case "firepower": return d.firepowerRank;
                case "thruster": return d.thrusterRank;
                case "greed": return d.greedRank;
                default: return 0;
            }
        }

        public bool TryUpgradeStat(string statKey)
        {
            int rank = GetUpgradeRank(statKey);
            if (rank >= MaxUpgradeRank) return false;
            int cost = GetUpgradeCost(rank);
            if (Data.credits < cost) return false;

            data.credits -= cost;
            switch (statKey)
            {
                case "armor": data.armorRank++; break;
                case "firepower": data.firepowerRank++; break;
                case "thruster": data.thrusterRank++; break;
                case "greed": data.greedRank++; break;
                default:
                    data.credits += cost;
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
            if (id < 0 || id >= WeaponCount) return false;
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
            if (id < 0 || id > 2) return false;
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
            GameSaveData d = Data;
            if (runScore > d.highScore) d.highScore = runScore;
            if (waveReached > d.highestWave) d.highestWave = waveReached;
            d.totalKills += Mathf.Max(0, runKills);
            Save();
        }

        public void ResetForTesting()
        {
            data = CreateFreshData();
            isLoaded = true;
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
                unlockedSkins = new List<int> { 0 }
            };
        }

        private static void NormalizeLists(GameSaveData d)
        {
            if (d.unlockedWeapons == null) d.unlockedWeapons = new List<int>();
            if (!d.unlockedWeapons.Contains(0)) d.unlockedWeapons.Add(0);
            if (d.unlockedSkins == null) d.unlockedSkins = new List<int>();
            if (!d.unlockedSkins.Contains(0)) d.unlockedSkins.Add(0);
            if (!d.unlockedWeapons.Contains(d.selectedWeapon)) d.selectedWeapon = 0;
            if (!d.unlockedSkins.Contains(d.selectedSkin)) d.selectedSkin = 0;
        }

        private static bool TryReadValidatedFile(string path, out GameSaveData loaded)
        {
            loaded = null;
            if (!File.Exists(path)) return false;

            try
            {
                string encoded = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(encoded)) return false;
                byte[] cipherBytes = Convert.FromBase64String(encoded);
                byte[] plainBytes = XorBytes(cipherBytes, EncryptionKey);
                string json = Encoding.UTF8.GetString(plainBytes);
                GameSaveData parsed = JsonUtility.FromJson<GameSaveData>(json);
                if (parsed == null) return false;

                string expected = ComputeChecksum(parsed);
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
            StringBuilder sb = new StringBuilder();
            sb.Append(d.credits).Append('|')
              .Append(d.highScore).Append('|')
              .Append(d.highestWave).Append('|')
              .Append(d.totalKills).Append('|')
              .Append(d.armorRank).Append('|')
              .Append(d.firepowerRank).Append('|')
              .Append(d.thrusterRank).Append('|')
              .Append(d.greedRank).Append('|')
              .Append(d.selectedWeapon).Append('|')
              .Append(d.selectedSkin).Append('|');

            if (d.unlockedWeapons != null)
            {
                for (int i = 0; i < d.unlockedWeapons.Count; i++) sb.Append(d.unlockedWeapons[i]).Append(',');
            }
            sb.Append('|');
            if (d.unlockedSkins != null)
            {
                for (int i = 0; i < d.unlockedSkins.Count; i++) sb.Append(d.unlockedSkins[i]).Append(',');
            }

            uint hash = 2166136261u;
            string raw = sb.ToString();
            for (int i = 0; i < raw.Length; i++)
            {
                hash ^= raw[i];
                hash *= 16777619u;
            }
            return hash.ToString("X8");
        }

        private static byte[] XorBytes(byte[] input, string key)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] result = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = (byte)(input[i] ^ keyBytes[i % keyBytes.Length]);
            }
            return result;
        }
    }
}
