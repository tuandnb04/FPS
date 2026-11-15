using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;

namespace TopDownShooter.Combat
{
    /// <summary>
    /// Handles weapon cooldowns, projectile pooling, muzzle VFX, recoil squash, and upgrade scaling.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private Transform projectilePoolRoot;

        [Header("Ballistics")]
        [SerializeField] private float baseFireInterval = 0.145f;
        [SerializeField] private float projectileSpeed = 30f;
        [SerializeField] private float baseDamage = 26f;

        private ObjectPool<Projectile> projectilePool;
        private readonly List<Projectile> allCreatedProjectiles = new List<Projectile>();
        private Material boltMaterial;
        private float fireCooldown;
        private int weaponTier = 1;

        public void BindReferences(Transform muzzle, ProceduralCharacterMotion motion, Transform poolRoot)
        {
            muzzlePoint = muzzle;
            characterMotion = motion;
            projectilePoolRoot = poolRoot;
        }

        private void Awake()
        {
            if (projectilePoolRoot == null)
            {
                GameObject rootGo = new GameObject("ProjectilePoolRoot");
                projectilePoolRoot = rootGo.transform;
            }

            boltMaterial = Palette.CreateLitMaterial("Mat_ProjectileBoltRuntime", Palette.Accent, 0.1f, 0.1f, true, 3.2f);
            projectilePool = new ObjectPool<Projectile>(
                createFunc: CreateProjectileInstance,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p =>
                {
                    if (p != null) Destroy(p.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: 24,
                maxSize: 64
            );
        }

        private Projectile CreateProjectileInstance()
        {
            GameObject go = new GameObject("Projectile_Bolt");
            go.transform.SetParent(projectilePoolRoot, false);

            GameObject meshGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshGo.name = "BoltVisual";
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localScale = new Vector3(0.13f, 0.28f, 0.13f);

            Collider c = meshGo.GetComponent<Collider>();
            if (c != null) Destroy(c);

            MeshRenderer mr = meshGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = boltMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Projectile proj = go.AddComponent<Projectile>();
            go.SetActive(false);
            allCreatedProjectiles.Add(proj);
            return proj;
        }

        private void Update()
        {
            if (fireCooldown > 0f)
            {
                fireCooldown -= Time.deltaTime;
            }
        }

        public void SetWeaponTier(int level)
        {
            weaponTier = Mathf.Max(1, level);
        }

        public bool TryFire(bool triggerHeld, Vector3 flatAimDirection)
        {
            if (!triggerHeld || fireCooldown > 0f)
            {
                return false;
            }

            float tierInterval = Mathf.Max(0.085f, baseFireInterval - (weaponTier - 1) * 0.012f);
            float tierDamage = baseDamage + (weaponTier - 1) * 6f;
            fireCooldown = tierInterval;

            Vector3 dir = flatAimDirection.sqrMagnitude > 0.001f
                ? new Vector3(flatAimDirection.x, 0f, flatAimDirection.z).normalized
                : transform.forward;
            Vector3 spawnPos = muzzlePoint != null
                ? muzzlePoint.position
                : transform.position + Vector3.up * 0.95f + dir * 0.75f;

            SpawnBolt(spawnPos, Quaternion.LookRotation(dir, Vector3.up), tierDamage);

            // Multi-shot spread on Level 3+
            if (weaponTier >= 3)
            {
                SpawnBolt(spawnPos, Quaternion.LookRotation(Quaternion.Euler(0f, -6f, 0f) * dir, Vector3.up), tierDamage * 0.7f);
                SpawnBolt(spawnPos, Quaternion.LookRotation(Quaternion.Euler(0f, 6f, 0f) * dir, Vector3.up), tierDamage * 0.7f);
            }

            VFXManager.Instance?.Play(VfxKind.MuzzleFlash, spawnPos, Palette.Accent);
            AudioManager.Instance?.PlayShoot();
            characterMotion?.TriggerSquash(new Vector3(1.10f, 0.92f, 0.90f), 0.10f);
            FeelManager.Instance?.TriggerCameraShake(0.07f);
            return true;
        }

        private void SpawnBolt(Vector3 pos, Quaternion rot, float dmg)
        {
            Projectile p = projectilePool.Get();
            p.Initialize(projectilePool, pos, rot, projectileSpeed, dmg, 1.5f);
        }

        public void ReturnAllActive()
        {
            fireCooldown = 0f;
            weaponTier = 1;
            for (int i = 0; i < allCreatedProjectiles.Count; i++)
            {
                if (allCreatedProjectiles[i] != null && allCreatedProjectiles[i].gameObject.activeSelf)
                {
                    allCreatedProjectiles[i].ForceReturnToPool();
                }
            }
        }
    }
}
