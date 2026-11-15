using System;
using UnityEngine;
using UnityEngine.Pool;
using TopDownShooter.Audio;
using TopDownShooter.Character;
using TopDownShooter.Core;
using TopDownShooter.Feel;
using TopDownShooter.UI;

namespace TopDownShooter.Enemies
{
    public enum EnemyArchetype
    {
        Chaser,
        Heavy
    }

    /// <summary>
    /// Pooled compound cyber-sentinel enemy with procedural locomotion, archetype scaling,
    /// hit flash, floating damage numbers, and burst VFX/audio on elimination.
    /// </summary>
    public sealed class EnemyController : MonoBehaviour
    {
        [SerializeField] private CharacterController controller;
        [SerializeField] private ProceduralCharacterMotion characterMotion;
        [SerializeField] private MeshRenderer[] bodyRenderers;
        [SerializeField] private Transform heavyArmorCrown;

        private ObjectPool<EnemyController> ownerPool;
        private EnemyArchetype archetype;
        private float maxHp;
        private float currentHp;
        private float moveSpeed;
        private float contactDamage;
        private int scoreValue;
        private float hitFlashTimer;
        private float contactCooldown;
        private float prevYaw;
        private bool isDead;

        private MaterialPropertyBlock mpb;
        private Action<EnemyController, bool> onFinishedCallback;

        public bool IsDead => isDead;
        public EnemyArchetype Archetype => archetype;

        public void BindComponents(
            CharacterController cc,
            ProceduralCharacterMotion motion,
            MeshRenderer[] renderers,
            Transform crown)
        {
            controller = cc;
            characterMotion = motion;
            bodyRenderers = renderers;
            heavyArmorCrown = crown;
        }

        public void Initialize(
            ObjectPool<EnemyController> pool,
            EnemyArchetype kind,
            Vector3 spawnPos,
            int waveNumber,
            Action<EnemyController, bool> onFinished)
        {
            ownerPool = pool;
            archetype = kind;
            onFinishedCallback = onFinished;
            isDead = false;
            hitFlashTimer = 0f;
            contactCooldown = 0.25f;

            float waveScale = 1f + (waveNumber - 1) * 0.16f;
            if (kind == EnemyArchetype.Heavy)
            {
                maxHp = 115f * waveScale;
                moveSpeed = 3.15f + Mathf.Min(1.2f, waveNumber * 0.12f);
                contactDamage = 20f;
                scoreValue = 250;
                transform.localScale = Vector3.one * 1.32f;
                if (heavyArmorCrown != null) heavyArmorCrown.gameObject.SetActive(true);
            }
            else
            {
                maxHp = 48f * waveScale;
                moveSpeed = 4.35f + Mathf.Min(1.6f, waveNumber * 0.18f);
                contactDamage = 12f;
                scoreValue = 100;
                transform.localScale = Vector3.one;
                if (heavyArmorCrown != null) heavyArmorCrown.gameObject.SetActive(false);
            }

            currentHp = maxHp;

            if (controller != null)
            {
                controller.enabled = false;
                transform.position = new Vector3(spawnPos.x, 0.02f, spawnPos.z);
                controller.enabled = true;
            }
            else
            {
                transform.position = new Vector3(spawnPos.x, 0.02f, spawnPos.z);
            }

            prevYaw = transform.eulerAngles.y;
            ApplyHitFlash(0f);
        }

        private void Update()
        {
            if (isDead) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (hitFlashTimer > 0f)
            {
                hitFlashTimer -= dt;
                ApplyHitFlash(Mathf.Clamp01(hitFlashTimer / 0.10f));
            }

            if (contactCooldown > 0f)
            {
                contactCooldown -= dt;
            }

            PlayerController player = PlayerController.Instance;
            Vector3 velocity = Vector3.zero;

            if (player != null && !player.IsDead)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;

                if (dist > 0.05f)
                {
                    Vector3 dir = toPlayer / dist;
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-10f * dt));

                    if (dist > 0.85f)
                    {
                        velocity = dir * moveSpeed;
                        if (controller != null && controller.enabled)
                        {
                            controller.Move((velocity + Vector3.down * 4f) * dt);
                        }
                        else
                        {
                            transform.position += velocity * dt;
                        }
                    }

                    if (dist <= 1.25f && contactCooldown <= 0f)
                    {
                        contactCooldown = 0.85f;
                        player.TakeDamage(contactDamage);
                    }
                }
            }

            float currentYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(prevYaw, currentYaw) / Mathf.Max(0.001f, dt);
            prevYaw = currentYaw;

            characterMotion?.StepMotion(velocity, yawRate, isAimingWeapon: false);
        }

        public void ApplyDamage(float dmg, Vector3 hitPoint, Vector3 shotDir)
        {
            if (isDead) return;

            currentHp -= dmg;
            hitFlashTimer = 0.10f;
            ApplyHitFlash(1f);

            characterMotion?.TriggerSquash(new Vector3(1.22f, 0.80f, 1.22f), 0.12f);
            FeelManager.Instance?.SpawnFloatingText(hitPoint, "-" + Mathf.RoundToInt(dmg), Palette.EnemySecondary);

            if (controller != null && controller.enabled)
            {
                Vector3 knock = new Vector3(shotDir.x, 0f, shotDir.z).normalized * (archetype == EnemyArchetype.Heavy ? 0.12f : 0.24f);
                controller.Move(knock);
            }

            if (currentHp <= 0f)
            {
                Die();
            }
        }

        private void Die()
        {
            if (isDead) return;
            isDead = true;

            Vector3 deathPos = transform.position + Vector3.up * 0.9f;
            VFXManager.Instance?.Play(VfxKind.EnemyDeath, deathPos, Palette.EnemySecondary);
            AudioManager.Instance?.PlayEnemyExplode();
            FeelManager.Instance?.TriggerCameraShake(archetype == EnemyArchetype.Heavy ? 0.36f : 0.20f);
            if (archetype == EnemyArchetype.Heavy)
            {
                FeelManager.Instance?.TriggerHitStop(0.055f, 0.05f);
            }

            GameManager.Instance?.AddScoreAndKill(scoreValue);
            onFinishedCallback?.Invoke(this, true);
            ownerPool?.Release(this);
        }

        public void ForceReturnToPool()
        {
            if (!gameObject.activeSelf && isDead) return;
            isDead = true;
            onFinishedCallback?.Invoke(this, false);
            if (ownerPool != null)
            {
                ownerPool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void ApplyHitFlash(float intensity)
        {
            if (bodyRenderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            Color flashEmission = Palette.Lerp(Palette.Shadow, Palette.CharacterPrimary, intensity) * (intensity * 1.8f);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                bodyRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", flashEmission);
                bodyRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
