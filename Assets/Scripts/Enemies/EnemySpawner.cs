using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Pickups;
using TopDownShooter.UI;

namespace TopDownShooter.Enemies
{
    /// <summary>
    /// Pooled wave director spawning compound enemies at safe perimeter coordinates and dropping
    /// pooled XP Shards / Medkits upon elimination.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public static EnemySpawner Instance { get; private set; }

        [Header("Arena Bounds")]
        [SerializeField] private float arenaHalfExtent = 11.6f;

        private ObjectPool<EnemyController> enemyPool;
        private ObjectPool<PickupItem> pickupPool;

        private readonly List<EnemyController> activeEnemies = new List<EnemyController>();
        private readonly List<PickupItem> activePickups = new List<PickupItem>();

        private Transform enemyPoolRoot;
        private Transform pickupPoolRoot;

        private Material enemyPrimaryMat;
        private Material enemySecondaryMat;
        private Material pickupMat;

        private int currentWave = 1;
        private int remainingToSpawnInWave;
        private float spawnTimer;
        private float waveIntermissionTimer;

        public int CurrentWave => currentWave;
        public int ActiveEnemyCount => activeEnemies.Count;
        public int ActivePickupCount => activePickups.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            enemyPoolRoot = new GameObject("EnemyPoolRoot").transform;
            enemyPoolRoot.SetParent(transform, false);

            pickupPoolRoot = new GameObject("PickupPoolRoot").transform;
            pickupPoolRoot.SetParent(transform, false);

            enemyPrimaryMat = Palette.CreateLitMaterial("Mat_EnemyChassisRuntime", Palette.EnemyPrimary, 0.45f, 0.4f);
            enemySecondaryMat = Palette.CreateLitMaterial("Mat_EnemyVisorRuntime", Palette.EnemySecondary, 0.25f, 0.25f, true, 2.2f);
            pickupMat = Palette.CreateLitMaterial("Mat_PickupCoreRuntime", Palette.Accent, 0.2f, 0.2f, true, 2.5f);

            enemyPool = new ObjectPool<EnemyController>(
                createFunc: CreateEnemyRig,
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => { if (e != null) Destroy(e.gameObject); },
                collectionCheck: false,
                defaultCapacity: 12,
                maxSize: 36
            );

            pickupPool = new ObjectPool<PickupItem>(
                createFunc: CreatePickupRig,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 48
            );
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private EnemyController CreateEnemyRig()
        {
            GameObject root = new GameObject("Enemy_Sentinel");
            root.transform.SetParent(enemyPoolRoot, false);

            CharacterController cc = root.AddComponent<CharacterController>();
            cc.radius = 0.46f;
            cc.height = 1.70f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.25f;

            ProceduralCharacterMotion motion = root.AddComponent<ProceduralCharacterMotion>();
            EnemyController ctrl = root.AddComponent<EnemyController>();

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            // Tapered armored torso
            GameObject torso = CreatePart("Torso", PrimitiveType.Cube, visualRoot.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.68f, 0.62f, 0.46f), enemyPrimaryMat);
            // Sloped armored chest plate
            CreatePart("ChestPlate", PrimitiveType.Cube, torso.transform, new Vector3(0f, 0.12f, 0.40f), new Vector3(0.84f, 0.54f, 0.36f), enemyPrimaryMat);

            // Head & glowing crimson visor
            GameObject headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.44f, 0.04f);
            CreatePart("HeadHelmet", PrimitiveType.Cube, headPivot.transform, Vector3.zero, new Vector3(0.46f, 0.34f, 0.44f), enemyPrimaryMat);
            CreatePart("VisorSlit", PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.40f, 0.11f, 0.08f), enemySecondaryMat);

            // Heavy variant crown (toggled on Heavy)
            GameObject crown = CreatePart("HeavyCrown", PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.25f, -0.02f), new Vector3(0.56f, 0.16f, 0.48f), enemySecondaryMat);
            crown.SetActive(false);

            // Arms / Twin cannons
            GameObject leftArmPivot = new GameObject("LeftArmPivot");
            leftArmPivot.transform.SetParent(visualRoot.transform, false);
            leftArmPivot.transform.localPosition = new Vector3(-0.46f, 1.15f, 0f);
            CreatePart("LeftArm", PrimitiveType.Capsule, leftArmPivot.transform, new Vector3(0f, -0.22f, 0.12f), new Vector3(0.20f, 0.26f, 0.20f), enemyPrimaryMat);

            GameObject rightArmPivot = new GameObject("RightArmPivot");
            rightArmPivot.transform.SetParent(visualRoot.transform, false);
            rightArmPivot.transform.localPosition = new Vector3(0.46f, 1.15f, 0f);
            CreatePart("RightArm", PrimitiveType.Capsule, rightArmPivot.transform, new Vector3(0f, -0.22f, 0.12f), new Vector3(0.20f, 0.26f, 0.20f), enemyPrimaryMat);

            // Legs
            GameObject leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.18f, 0.68f, 0f);
            CreatePart("LeftLeg", PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.34f, 0f), new Vector3(0.22f, 0.62f, 0.24f), enemyPrimaryMat);

            GameObject rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.18f, 0.68f, 0f);
            CreatePart("RightLeg", PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.34f, 0f), new Vector3(0.22f, 0.62f, 0.24f), enemyPrimaryMat);

            // Rear reactor pack (secondary lag)
            GameObject reactorPivot = new GameObject("SecondaryPivot");
            reactorPivot.transform.SetParent(visualRoot.transform, false);
            reactorPivot.transform.localPosition = new Vector3(0f, 1.05f, -0.32f);
            CreatePart("ReactorCore", PrimitiveType.Sphere, reactorPivot.transform, Vector3.zero, new Vector3(0.26f, 0.30f, 0.22f), enemySecondaryMat);

            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot.transform, rightArmPivot.transform, leftLegPivot.transform, rightLegPivot.transform, reactorPivot.transform);
            ctrl.BindComponents(cc, motion, root.GetComponentsInChildren<MeshRenderer>(true), crown.transform);

            root.SetActive(false);
            return ctrl;
        }

        private PickupItem CreatePickupRig()
        {
            GameObject root = new GameObject("Pickup_Collectible");
            root.transform.SetParent(pickupPoolRoot, false);

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.78f;

            GameObject visual = CreatePart("CrystalCore", PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.36f, 0.36f, 0.36f), pickupMat);
            MeshRenderer mr = visual.GetComponent<MeshRenderer>();

            PickupItem item = root.AddComponent<PickupItem>();
            item.ConfigureVisual(visual.transform, mr);

            root.SetActive(false);
            return item;
        }

        private static GameObject CreatePart(string partName, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            return go;
        }

        public void BeginWave(int waveNum)
        {
            currentWave = Mathf.Max(1, waveNum);
            remainingToSpawnInWave = 5 + currentWave * 3;
            spawnTimer = 0.2f;
            UIManager.Instance?.UpdateWave(currentWave, remainingToSpawnInWave + activeEnemies.Count);
        }

        private void Update()
        {
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (remainingToSpawnInWave > 0)
            {
                spawnTimer -= dt;
                int maxConcurrent = Mathf.Min(16, 6 + currentWave * 2);
                if (spawnTimer <= 0f && activeEnemies.Count < maxConcurrent)
                {
                    SpawnOneWaveEnemy();
                    remainingToSpawnInWave--;
                    spawnTimer = Mathf.Max(0.32f, 1.05f - currentWave * 0.08f);
                    UIManager.Instance?.UpdateWave(currentWave, remainingToSpawnInWave + activeEnemies.Count);
                }
            }
            else if (activeEnemies.Count == 0)
            {
                waveIntermissionTimer += dt;
                if (waveIntermissionTimer >= 1.4f)
                {
                    waveIntermissionTimer = 0f;
                    BeginWave(currentWave + 1);
                    UIManager.Instance?.ShowMilestoneBanner("WAVE " + currentWave + " INCOMING");
                }
            }
        }

        private void SpawnOneWaveEnemy()
        {
            EnemyArchetype kind = (currentWave >= 2 && UnityEngine.Random.value < Mathf.Min(0.35f, 0.12f + currentWave * 0.04f))
                ? EnemyArchetype.Heavy
                : EnemyArchetype.Chaser;
            Vector3 spawnPos = PickSafeSpawnPosition();
            SpawnEnemyAt(kind, spawnPos);
        }

        public EnemyController SpawnEnemyAt(EnemyArchetype kind, Vector3 worldPos)
        {
            EnemyController enemy = enemyPool.Get();
            activeEnemies.Add(enemy);
            enemy.Initialize(enemyPool, kind, worldPos, currentWave, HandleEnemyFinished);
            return enemy;
        }

        public PickupItem SpawnPickupAt(PickupKind kind, Vector3 worldPos)
        {
            PickupItem item = pickupPool.Get();
            activePickups.Add(item);
            item.Initialize(pickupPool, worldPos, kind, kind == PickupKind.XpShard ? 25 : 0, kind == PickupKind.Medkit ? 25f : 0f, HandlePickupReleased);
            return item;
        }

        private void HandleEnemyFinished(EnemyController enemy, bool killedByPlayer)
        {
            activeEnemies.Remove(enemy);
            if (killedByPlayer)
            {
                bool dropMedkit = UnityEngine.Random.value < 0.18f;
                SpawnPickupAt(dropMedkit ? PickupKind.Medkit : PickupKind.XpShard, enemy.transform.position);
            }
            UIManager.Instance?.UpdateWave(currentWave, remainingToSpawnInWave + activeEnemies.Count);
        }

        private void HandlePickupReleased(PickupItem item)
        {
            activePickups.Remove(item);
        }

        private Vector3 PickSafeSpawnPosition()
        {
            Vector3 playerPos = PlayerController.Instance != null ? PlayerController.Instance.transform.position : Vector3.zero;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int side = UnityEngine.Random.Range(0, 4);
                float edge = arenaHalfExtent;
                float along = UnityEngine.Random.Range(-edge, edge);
                Vector3 candidate = side switch
                {
                    0 => new Vector3(along, 0f, edge),
                    1 => new Vector3(along, 0f, -edge),
                    2 => new Vector3(edge, 0f, along),
                    _ => new Vector3(-edge, 0f, along)
                };

                if (Vector3.Distance(candidate, playerPos) < 8.0f) continue;
                if (Physics.CheckSphere(candidate + Vector3.up * 0.9f, 0.75f, ~0, QueryTriggerInteraction.Ignore)) continue;
                return candidate;
            }
            return new Vector3(0f, 0f, arenaHalfExtent);
        }

        public void ResetAndClearAll()
        {
            currentWave = 1;
            remainingToSpawnInWave = 0;
            spawnTimer = 0f;
            waveIntermissionTimer = 0f;

            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                if (activeEnemies[i] != null)
                {
                    activeEnemies[i].ForceReturnToPool();
                }
            }
            activeEnemies.Clear();

            for (int i = activePickups.Count - 1; i >= 0; i--)
            {
                if (activePickups[i] != null)
                {
                    activePickups[i].ForceReturnToPool();
                }
            }
            activePickups.Clear();
        }
    }
}
