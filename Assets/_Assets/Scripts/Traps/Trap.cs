using System.Collections;
using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Traps
{
    public enum TrapType
    {
        CollisionDetonation, // Explodes on impact
        TimedDetonation, // Explodes after a delay with shaking
    }

    public class Trap : MonoBehaviour
    {
        [Header("Trap Settings")]
        public TrapType trapType = TrapType.CollisionDetonation;

        [Header("Visual Effects")]
        public GameObject vfx;
        public bool isPlayingVFX = false;
        public GameObject detonationImpactVFX;
        public GameObject damageImpact;

        [Header("Explosion Settings")]
        public float blastRadius = 10f;
        public float explosionForce = 500f;
        public float upwardModifier = 3f;
        public float damage = 1f; // Changed to 1 hit per explosion

        [Header("Detonation")]
        public float dtSpeed = 10f;
        public LayerMask affectedLayers;
        public float detonationDelay = 2f;
        public bool detonateOnCollision = true;

        [Header("Timed Trap Settings")]
        public float shakeDuration = 1.5f;
        public float shakeIntensity = 0.3f;
        public float rotationShakeIntensity = 10f;

        private Rigidbody rb;
        private bool hasDetonated = false;
        private TrapHandler trapHandler;
        private GameObject spawnedVFX;

        private Vector3 originalPosition;
        private Quaternion originalRotation;

        void Start()
        {
            rb = GetComponent<Rigidbody>();

            // Auto-start if trap type is Timed
            if (trapType == TrapType.TimedDetonation)
            {
                StartCoroutine(TimedDetonationRoutine());
            }
        }

        void Update()
        {
            if (trapType == TrapType.CollisionDetonation)
            {
                if (isPlayingVFX)
                    PlayVFX();
                else
                    StopVFX();
            }
        }

        public void SetTrapHandler(TrapHandler handler)
        {
            trapHandler = handler;
        }

        public void ResetTrap()
        {
            hasDetonated = false;
            if (spawnedVFX != null)
            {
                Destroy(spawnedVFX);
                spawnedVFX = null;
            }
        }

        public void PlayVFX() => vfx?.SetActive(true);

        public void StopVFX() => vfx?.SetActive(false);

        void OnCollisionEnter(Collision collision)
        {
            if (trapType == TrapType.CollisionDetonation && !hasDetonated && detonateOnCollision)
            {
                Detonate();
            }
        }

        IEnumerator DetonateWithDelay()
        {
            hasDetonated = true;
            if (detonationDelay > 0)
                yield return new WaitForSeconds(detonationDelay);

            Detonate();
        }

        IEnumerator TimedDetonationRoutine()
        {
            yield return new WaitForSeconds(detonationDelay - shakeDuration);
            yield return StartCoroutine(ShakeEffect(shakeDuration));
            Detonate();
        }

        IEnumerator ShakeEffect(float duration)
        {
            float elapsed = 0f;
            originalPosition = transform.localPosition;
            originalRotation = transform.localRotation;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                // Random position/rotation shake (cartoony style)
                transform.localPosition =
                    originalPosition + Random.insideUnitSphere * shakeIntensity;
                transform.localRotation = Quaternion.Euler(
                    originalRotation.eulerAngles + Random.insideUnitSphere * rotationShakeIntensity
                );

                yield return null;
            }

            // Reset after shake
            transform.localPosition = originalPosition;
            transform.localRotation = originalRotation;
        }

        public void ActivateTrap()
        {
            if (hasDetonated)
                return;

            if (trapType == TrapType.TimedDetonation)
            {
                StartCoroutine(TimedDetonationRoutine());
            }
        }

        public void Detonate()
        {
            if (hasDetonated)
                return;
            hasDetonated = true;

            if (detonationImpactVFX != null)
                Instantiate(detonationImpactVFX, transform.position, Quaternion.identity);

            ApplyExplosionForce();

            trapHandler?.OnTrapDetonated(gameObject);
            StartCoroutine(DisableTrapAfterDelay(0.8f));
        }

        IEnumerator DisableTrapAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (trapHandler == null)
                Destroy(gameObject);
        }

        void ApplyExplosionForce()
        {
            Collider[] colliders = Physics.OverlapSphere(
                transform.position,
                blastRadius,
                affectedLayers
            );

            foreach (Collider col in colliders)
            {
                if (col.gameObject == gameObject)
                    continue;

                Rigidbody targetRb = col.GetComponent<Rigidbody>();
                if (targetRb != null)
                {
                    float distance = Vector3.Distance(transform.position, col.transform.position);
                    float forceMagnitude = explosionForce * (1 - (distance / blastRadius));
                    targetRb.AddExplosionForce(
                        forceMagnitude,
                        transform.position,
                        blastRadius,
                        upwardModifier,
                        ForceMode.Impulse
                    );

                    if (damageImpact != null)
                        Instantiate(
                            damageImpact,
                            col.ClosestPoint(transform.position),
                            Quaternion.identity
                        );

                    // ========== APPLY DAMAGE USING IDamageable ==========
                    IDamageable damageable = col.GetComponent<IDamageable>();
                    if (damageable == null)
                    {
                        // Try parent if not on this collider
                        damageable = col.GetComponentInParent<IDamageable>();
                    }
                    
                    if (damageable != null)
                    {
                        // Scale damage with distance (farther = less damage)
                        float damageAmount = damage * (1 - (distance / blastRadius));
                        
                        // Apply damage with explosion type
                        damageable.TakeDamage(damageAmount, gameObject, DamageType.Explosion);
                        
                        Debug.Log($"[Trap] 💣 Dealt {damageAmount} explosion damage to {col.name}");
                    }
                }
            }
        }

        public void TriggerExplosion()
        {
            if (!hasDetonated)
                Detonate();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, blastRadius);
        }
    }
}