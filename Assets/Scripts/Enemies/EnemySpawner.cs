using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;
using TopDownShooter.Pickups;
using TopDownShooter.UI;

namespace TopDownShooter.Enemies
{
    /// <summary>
    /// Pooled wave director spawning 3D compound enemies & Dreadnought Bosses,
    /// managing telegraphed hazard rings, and dropping XP/Medkit/Cyber-Credit collectibles.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public static EnemySpawner Instance { get; private set; }

        [Header("Arena Bounds")]
        [SerializeField] private float arenaHalfExtent = 11.6f;

        [Header("Blender Custom Meshes (Assigned by SceneSetupBuilder)")]
        [SerializeField] private Mesh chaserTorsoMesh;
        [SerializeField] private Mesh chaserHeadMesh;
        [SerializeField] private Mesh heavyTorsoMesh;
        [SerializeField] private Mesh heavyCrownMesh;
        [SerializeField] private Mesh bossTorsoMesh;
        [SerializeField] private Mesh bossTurretMesh;
        [SerializeField] private Mesh limbMesh;
        [SerializeField] private Mesh xpCrystalMesh;
        [SerializeField] private Mesh medkitMesh;
        [SerializeField] private Mesh creditCoinMesh;

        private ObjectPool<EnemyController> enemyPool;
        private ObjectPool<PickupItem> pickupPool;

        private readonly List<EnemyController> activeEnemies = new List<EnemyController>();
        private readonly List<PickupItem> activePickups = new List<PickupItem>();
        private readonly List<GameObject> activeHazards = new List<GameObject>();

        private Transform enemyPoolRoot;
        private Transform pickupPoolRoot;
        private Transform hazardRoot;

        private Material enemyPrimaryMat;
        private Material enemySecondaryMat;
        private Material bossArmorMat;
        private Material bossTrimMat;
        private Material pickupMat;
        private Material hazardRingMat;

        private int currentWave = 1;
        private int remainingToSpawnInWave;
        private float spawnTimer;
        private float waveIntermissionTimer;

        public int CurrentWave => currentWave;
        public int ActiveEnemyCount => activeEnemies.Count;
        public int ActivePickupCount => activePickups.Count;

        public void BindMeshes(
            Mesh chaserTorso, Mesh chaserHead,
            Mesh heavyTorso, Mesh heavyCrown,
            Mesh bossTorso, Mesh bossTurret,
            Mesh limb,
            Mesh xpShard, Mesh medkit, Mesh creditCoin)
        {
            chaserTorsoMesh = chaserTorso;
            chaserHeadMesh = chaserHead;
            heavyTorsoMesh = heavyTorso;
            heavyCrownMesh = heavyCrown;
            bossTorsoMesh = bossTorso;
            bossTurretMesh = bossTurret;
            limbMesh = limb;
            xpCrystalMesh = xpShard;
            medkitMesh = medkit;
            creditCoinMesh = creditCoin;
        }

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

            hazardRoot = new GameObject("HazardTelegraphRoot").transform;
            hazardRoot.SetParent(transform, false);

            enemyPrimaryMat = Palette.CreateLitMaterial("Mat_EnemyChassisRuntime", Palette.EnemyPrimary, 0.45f, 0.42f);
            enemySecondaryMat = Palette.CreateLitMaterial("Mat_EnemyVisorRuntime", Palette.EnemySecondary, 0.25f, 0.25f, true, 2.2f);
            bossArmorMat = Palette.CreateLitMaterial("Mat_BossChassisRuntime", Palette.Walls, 0.65f, 0.55f);
            bossTrimMat = Palette.CreateLitMaterial("Mat_BossTrimRuntime", Palette.BossPurple, 0.2f, 0.2f, true, 2.8f);
            pickupMat = Palette.CreateLitMaterial("Mat_PickupCoreRuntime", Palette.Accent, 0.2f, 0.2f, true, 2.5f);
            hazardRingMat = Palette.CreateParticleMaterial("Mat_HazardRingRuntime", UISpriteFactory.GetTelegraphRingTexture());

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
                defaultCapacity: 24,
                maxSize: 64
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
            cc.radius = 0.48f;
            cc.height = 1.75f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.25f;

            ProceduralCharacterMotion motion = root.AddComponent<ProceduralCharacterMotion>();
            EnemyController ctrl = root.AddComponent<EnemyController>();

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            // Standard bipedal Chaser & Heavy pivots
            GameObject torsoGroup = new GameObject("TorsoRoot");
            torsoGroup.transform.SetParent(visualRoot.transform, false);

            GameObject chaserBody = CreatePartWithMesh("ChaserBody", chaserTorsoMesh, PrimitiveType.Cube, torsoGroup.transform, new Vector3(0f, 1.0f, 0f), chaserTorsoMesh != null ? Vector3.one : new Vector3(0.68f, 0.62f, 0.46f), enemyPrimaryMat);
            GameObject heavyBody = CreatePartWithMesh("HeavyBody", heavyTorsoMesh, PrimitiveType.Cube, torsoGroup.transform, new Vector3(0f, 1.05f, 0f), heavyTorsoMesh != null ? Vector3.one : new Vector3(0.86f, 0.72f, 0.62f), enemyPrimaryMat);
            heavyBody.SetActive(false);

            GameObject headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(visualRoot.transform, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.46f, 0.04f);

            GameObject chaserHeadObj = CreatePartWithMesh("ChaserHead", chaserHeadMesh, PrimitiveType.Cube, headPivot.transform, Vector3.zero, chaserHeadMesh != null ? Vector3.one : new Vector3(0.46f, 0.34f, 0.44f), enemyPrimaryMat);
            CreatePartWithMesh("ChaserVisor", null, PrimitiveType.Cube, chaserHeadObj.transform, new Vector3(0f, 0.02f, 0.21f), new Vector3(0.36f, 0.10f, 0.08f), enemySecondaryMat);

            GameObject heavyHeadObj = CreatePartWithMesh("HeavyCrown", heavyCrownMesh, PrimitiveType.Cube, headPivot.transform, new Vector3(0f, 0.06f, 0f), heavyCrownMesh != null ? Vector3.one : new Vector3(0.56f, 0.24f, 0.50f), enemySecondaryMat);
            heavyHeadObj.SetActive(false);

            GameObject leftArmPivot = new GameObject("LeftArmPivot");
            leftArmPivot.transform.SetParent(visualRoot.transform, false);
            leftArmPivot.transform.localPosition = new Vector3(-0.46f, 1.15f, 0f);
            CreatePartWithMesh("LeftArm", limbMesh, PrimitiveType.Capsule, leftArmPivot.transform, new Vector3(0f, -0.22f, 0.08f), limbMesh != null ? new Vector3(0.85f, 0.72f, 0.85f) : new Vector3(0.20f, 0.26f, 0.20f), enemyPrimaryMat);

            GameObject rightArmPivot = new GameObject("RightArmPivot");
            rightArmPivot.transform.SetParent(visualRoot.transform, false);
            rightArmPivot.transform.localPosition = new Vector3(0.46f, 1.15f, 0f);
            CreatePartWithMesh("RightArm", limbMesh, PrimitiveType.Capsule, rightArmPivot.transform, new Vector3(0f, -0.22f, 0.08f), limbMesh != null ? new Vector3(0.85f, 0.72f, 0.85f) : new Vector3(0.20f, 0.26f, 0.20f), enemyPrimaryMat);

            GameObject leftLegPivot = new GameObject("LeftLegPivot");
            leftLegPivot.transform.SetParent(visualRoot.transform, false);
            leftLegPivot.transform.localPosition = new Vector3(-0.18f, 0.68f, 0f);
            CreatePartWithMesh("LeftLeg", limbMesh, PrimitiveType.Cube, leftLegPivot.transform, new Vector3(0f, -0.34f, 0f), limbMesh != null ? Vector3.one : new Vector3(0.22f, 0.62f, 0.24f), enemyPrimaryMat);

            GameObject rightLegPivot = new GameObject("RightLegPivot");
            rightLegPivot.transform.SetParent(visualRoot.transform, false);
            rightLegPivot.transform.localPosition = new Vector3(0.18f, 0.68f, 0f);
            CreatePartWithMesh("RightLeg", limbMesh, PrimitiveType.Cube, rightLegPivot.transform, new Vector3(0f, -0.34f, 0f), limbMesh != null ? Vector3.one : new Vector3(0.22f, 0.62f, 0.24f), enemyPrimaryMat);

            GameObject reactorPivot = new GameObject("SecondaryPivot");
            reactorPivot.transform.SetParent(visualRoot.transform, false);
            reactorPivot.transform.localPosition = new Vector3(0f, 1.05f, -0.32f);
            CreatePartWithMesh("ReactorCore", null, PrimitiveType.Sphere, reactorPivot.transform, Vector3.zero, new Vector3(0.26f, 0.30f, 0.22f), enemySecondaryMat);

            // Chaser/Heavy group holders (only toggle non-shared heavy crown/boss hull)
            GameObject chaserToggle = new GameObject("ChaserMarker");
            chaserToggle.transform.SetParent(visualRoot.transform, false);

            GameObject heavyToggle = new GameObject("HeavyMarker");
            heavyToggle.transform.SetParent(visualRoot.transform, false);
            heavyBody.transform.SetParent(heavyToggle.transform, true);
            heavyHeadObj.transform.SetParent(heavyToggle.transform, true);
            heavyBody.SetActive(true);
            heavyHeadObj.SetActive(true);

            // Notice: Chaser uses chaserBody & chaserHeadObj directly when active
            chaserBody.transform.SetParent(chaserToggle.transform, true);
            chaserHeadObj.transform.SetParent(chaserToggle.transform, true);

            // Dedicated Dreadnought Boss rig
            GameObject bossGroup = new GameObject("BossRigRoot");
            bossGroup.transform.SetParent(root.transform, false);
            CreatePartWithMesh("BossHull", bossTorsoMesh, PrimitiveType.Cube, bossGroup.transform, new Vector3(0f, 0.72f, 0f), bossTorsoMesh != null ? Vector3.one : new Vector3(1.5f, 0.75f, 1.4f), bossArmorMat);
            CreatePartWithMesh("BossCoreGlow", null, PrimitiveType.Sphere, bossGroup.transform, new Vector3(0f, 0.85f, 0.38f), new Vector3(0.45f, 0.30f, 0.35f), bossTrimMat);
            GameObject turretPivot = new GameObject("BossTurretPivot");
            turretPivot.transform.SetParent(bossGroup.transform, false);
            turretPivot.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            CreatePartWithMesh("BossTurretMesh", bossTurretMesh, PrimitiveType.Cube, turretPivot.transform, Vector3.zero, bossTurretMesh != null ? Vector3.one : new Vector3(0.85f, 0.38f, 0.9f), bossTrimMat);
            bossGroup.SetActive(false);

            // Note: ProceduralCharacterMotion moves bipedal pivots when visible; for Boss, visualRoot hides cleanly
            motion.BindPivots(visualRoot.transform, headPivot.transform, leftArmPivot.transform, rightArmPivot.transform, leftLegPivot.transform, rightLegPivot.transform, reactorPivot.transform);
            ctrl.BindComponents(cc, motion, root.GetComponentsInChildren<MeshRenderer>(true), visualRoot, chaserToggle, heavyToggle, bossGroup, turretPivot.transform);

            root.SetActive(false);
            return ctrl;
        }

        private PickupItem CreatePickupRig()
        {
            GameObject root = new GameObject("Pickup_Collectible");
            root.transform.SetParent(pickupPoolRoot, false);

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.82f;

            GameObject visualHolder = new GameObject("VisualSpinRoot");
            visualHolder.transform.SetParent(root.transform, false);

            GameObject xpObj = CreatePartWithMesh("XpCrystalVisual", xpCrystalMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, xpCrystalMesh != null ? Vector3.one : new Vector3(0.36f, 0.36f, 0.36f), pickupMat);
            GameObject medObj = CreatePartWithMesh("MedkitVisual", medkitMesh, PrimitiveType.Cube, visualHolder.transform, Vector3.zero, medkitMesh != null ? Vector3.one : new Vector3(0.44f, 0.24f, 0.36f), pickupMat);
            GameObject credObj = CreatePartWithMesh("CreditCoinVisual", creditCoinMesh, PrimitiveType.Cylinder, visualHolder.transform, Vector3.zero, creditCoinMesh != null ? Vector3.one : new Vector3(0.42f, 0.08f, 0.42f), pickupMat);

            medObj.SetActive(false);
            credObj.SetActive(false);

            PickupItem item = root.AddComponent<PickupItem>();
            item.ConfigureMultiVisuals(
                visualHolder.transform,
                xpObj, xpObj.GetComponent<MeshRenderer>(),
                medObj, medObj.GetComponent<MeshRenderer>(),
                credObj, credObj.GetComponent<MeshRenderer>());

            root.SetActive(false);
            return item;
        }

        private static GameObject CreatePartWithMesh(string partName, Mesh customMesh, PrimitiveType fallbackType, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject go;
            if (customMesh != null)
            {
                go = new GameObject(partName);
                go.transform.SetParent(parent, false);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = customMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
            }
            else
            {
                go = GameObject.CreatePrimitive(fallbackType);
                go.name = partName;
                go.transform.SetParent(parent, false);
                Collider c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go;
        }

        public void BeginWave(int waveNum)
        {
            currentWave = Mathf.Max(1, waveNum);
            remainingToSpawnInWave = 5 + currentWave * 3;
            spawnTimer = 0.2f;

            if (currentWave % 3 == 0)
            {
                SpawnBossAt(PickSafeSpawnPosition());
                UIManager.Instance?.ShowMilestoneBanner("WARNING // DREADNOUGHT MK-IV");
            }

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
                    if (currentWave % 3 != 0)
                    {
                        UIManager.Instance?.ShowMilestoneBanner("WAVE " + currentWave + " INCOMING");
                    }
                }
            }
        }

        private void SpawnOneWaveEnemy()
        {
            EnemyArchetype kind = (currentWave >= 2 && UnityEngine.Random.value < Mathf.Min(0.35f, 0.12f + currentWave * 0.04f))
                ? EnemyArchetype.Heavy
                : EnemyArchetype.Chaser;
            SpawnEnemyAt(kind, PickSafeSpawnPosition());
        }

        public EnemyController SpawnBossAt(Vector3 worldPos)
        {
            return SpawnEnemyAt(EnemyArchetype.Boss, worldPos);
        }

        public EnemyController SpawnEnemyAt(EnemyArchetype kind, Vector3 worldPos)
        {
            EnemyController enemy = enemyPool.Get();
            activeEnemies.Add(enemy);
            enemy.Initialize(enemyPool, kind, worldPos, currentWave, HandleEnemyFinished);
            return enemy;
        }

        public PickupItem SpawnPickupAt(PickupKind kind, Vector3 worldPos, int creditAmount = 15)
        {
            PickupItem item = pickupPool.Get();
            activePickups.Add(item);
            item.Initialize(pickupPool, worldPos, kind, kind == PickupKind.XpShard ? 25 : 0, kind == PickupKind.Medkit ? 25f : 0f, HandlePickupReleased, creditAmount);
            return item;
        }

        public void SpawnHazardTelegraph(Vector3 worldPos, float radius, float dmg)
        {
            StartCoroutine(HazardTelegraphRoutine(new Vector3(worldPos.x, 0.04f, worldPos.z), radius, dmg));
        }

        private IEnumerator HazardTelegraphRoutine(Vector3 center, float radius, float dmg)
        {
            GameObject ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "BossHazardTelegraph";
            ringGo.transform.SetParent(hazardRoot, false);
            ringGo.transform.position = new Vector3(center.x, 0.03f, center.z);
            ringGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            Collider c = ringGo.GetComponent<Collider>();
            if (c != null) Destroy(c);

            MeshRenderer mr = ringGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = hazardRingMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            activeHazards.Add(ringGo);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            float elapsed = 0f;
            const float windup = 1.15f;
            while (elapsed < windup)
            {
                if (ringGo == null) yield break;
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / windup);
                    float pulse = 0.92f + 0.08f * Mathf.Sin(t * 18f);
                    ringGo.transform.localScale = new Vector3(radius * 2f * pulse, radius * 2f * pulse, 1f);
                    if (mr != null)
                    {
                        Color col = Palette.Lerp(Palette.BossPurple, Palette.EnemySecondary, t);
                        col.a = 0.78f + t * 0.22f;
                        block.SetColor("_Color", col);
                        mr.SetPropertyBlock(block);
                    }
                }
                yield return null;
            }

            if (ringGo == null) yield break;

            VFXManager.Instance?.Play(VfxKind.EnemyDeath, center + Vector3.up * 0.4f, Palette.BossPurple);
            FeelManager.Instance?.TriggerCameraShake(0.26f);
            AudioManager.Instance?.PlayEnemyExplode();

            PlayerController player = PlayerController.Instance;
            if (player != null && !player.IsDead)
            {
                Vector3 diff = player.transform.position - center;
                diff.y = 0f;
                if (diff.magnitude <= radius)
                {
                    player.TakeDamage(dmg);
                }
            }

            activeHazards.Remove(ringGo);
            Destroy(ringGo);
        }

        private void HandleEnemyFinished(EnemyController enemy, bool killedByPlayer)
        {
            activeEnemies.Remove(enemy);
            if (killedByPlayer)
            {
                if (enemy.Archetype == EnemyArchetype.Boss)
                {
                    Vector3 center = enemy.transform.position;
                    SpawnPickupAt(PickupKind.Medkit, center);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 offset = Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 1.1f;
                        SpawnPickupAt(PickupKind.XpShard, center + offset);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 offset = Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 1.6f;
                        SpawnPickupAt(PickupKind.CreditCoin, center + offset, 25);
                    }
                }
                else
                {
                    bool dropMedkit = UnityEngine.Random.value < 0.18f;
                    SpawnPickupAt(dropMedkit ? PickupKind.Medkit : PickupKind.XpShard, enemy.transform.position);
                    if (UnityEngine.Random.value < 0.65f)
                    {
                        SpawnPickupAt(PickupKind.CreditCoin, enemy.transform.position + new Vector3(0.45f, 0f, -0.35f), enemy.Archetype == EnemyArchetype.Heavy ? 25 : 15);
                    }
                }
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
            StopAllCoroutines();
            currentWave = 1;
            remainingToSpawnInWave = 0;
            spawnTimer = 0f;
            waveIntermissionTimer = 0f;

            for (int i = activeHazards.Count - 1; i >= 0; i--)
            {
                if (activeHazards[i] != null) Destroy(activeHazards[i]);
            }
            activeHazards.Clear();

            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                if (activeEnemies[i] != null) activeEnemies[i].ForceReturnToPool();
            }
            activeEnemies.Clear();

            for (int i = activePickups.Count - 1; i >= 0; i--)
            {
                if (activePickups[i] != null) activePickups[i].ForceReturnToPool();
            }
            activePickups.Clear();

            UIManager.Instance?.HideBossHp();
        }
    }
}
