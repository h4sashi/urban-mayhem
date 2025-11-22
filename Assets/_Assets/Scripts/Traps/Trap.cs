using System.Collections;
using Hanzo.Core.Interfaces;
using Hanzo.Core.Utilities;
using UnityEngine;

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
        public float damage = 1f;

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
        public bool hasDetonated = false;
        private TrapHandler trapHandler;
        private GameObject spawnedVFX;

        private Vector3 originalPosition;
        private Quaternion originalRotation;

        [Header("Indicator Settings")]
        public bool showDamageIndicator = true;

        [Header("Countdown Tracking")]
        public float currentCountdown = 0f;
        private bool countdownActive = false;
        private bool indicatorShown = false;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
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

            // Continuously check if player is in range for timed traps
            if (trapType == TrapType.TimedDetonation && countdownActive && showDamageIndicator)
            {
                CheckPlayerRangeForIndicator();
            }

            if(indicatorShown == true && countdownActive)
            {
              DamageIndicatorManager.Instance.ShowIndicator(transform, currentCountdown);
            }
            else
            {
                DamageIndicatorManager.Instance.HideIndicator(transform);
            }
        }

        public void SetTrapHandler(TrapHandler handler)
        {
            trapHandler = handler;
        }

        public void ResetTrap()
        {
            hasDetonated = false;
            countdownActive = false;
            indicatorShown = false;
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
            if (showDamageIndicator && detonationDelay > 0)
            {
                DamageIndicatorManager.Instance?.ShowIndicator(transform, detonationDelay);
            }

            if (detonationDelay > 0)
                yield return new WaitForSeconds(detonationDelay);

            Detonate();
        }

        IEnumerator TimedDetonationRoutine()
        {
            countdownActive = true;
            currentCountdown = detonationDelay;

            // Check immediately if player is in range
            if (showDamageIndicator)
            {
                CheckPlayerRangeForIndicator();
            }

            // Wait before shake
            float waitTime = detonationDelay - shakeDuration;
            float elapsed = 0f;

            while (elapsed < waitTime)
            {
                elapsed += Time.deltaTime;
                currentCountdown = detonationDelay - elapsed;
                yield return null;
            }

            // Shake phase
            yield return StartCoroutine(ShakeEffect(shakeDuration));
            
            Detonate();
        }

        void CheckPlayerRangeForIndicator()
        {
            if (DamageIndicatorManager.Instance == null)
            {
                Debug.LogWarning("[Trap] DamageIndicatorManager.Instance is NULL!");
                return;
            }

            Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (player == null)
                return;

            float maxRange = DamageIndicatorManager.Instance.maxTrackingDistance; // Match DamageIndicatorManager's maxTrackingDistance
            float distance = Vector3.Distance(transform.position, player.position);

            if (distance <= maxRange && !indicatorShown)
            {
                // Player entered range - show indicator
                Debug.Log($"[Trap] Showing indicator for {gameObject.name} (distance: {distance:F1}m, countdown: {currentCountdown:F1}s)"); 
                indicatorShown = true;
            }
            else if (distance > maxRange && indicatorShown)
            {
                // Player left range - hide indicator
                Debug.Log($"[Trap] Hiding indicator for {gameObject.name} (distance: {distance:F1}m)");
                DamageIndicatorManager.Instance.HideIndicator(transform);
                indicatorShown = false;
            }
        }

        public float GetRemainingTime()
        {
            if (!countdownActive)
                return 0f;
            return currentCountdown;
        }

        IEnumerator ShakeEffect(float duration)
        {
            float elapsed = 0f;
            originalPosition = transform.localPosition;
            originalRotation = transform.localRotation;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                transform.localPosition =
                    originalPosition + Random.insideUnitSphere * shakeIntensity;
                transform.localRotation = Quaternion.Euler(
                    originalRotation.eulerAngles + Random.insideUnitSphere * rotationShakeIntensity
                );

                yield return null;
            }

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
            countdownActive = false;

            // Always try to hide indicator
            if (showDamageIndicator)
            {
                DamageIndicatorManager.Instance?.HideIndicator(transform);
                indicatorShown = false;
            }

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
                float distance = Vector3.Distance(transform.position, col.transform.position);
                float forceMagnitude = explosionForce * (1 - (distance / blastRadius));

                if (targetRb != null)
                {
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
                }

                IDamageable damageable = col.GetComponent<IDamageable>();
                if (damageable == null)
                {
                    damageable = col.GetComponentInParent<IDamageable>();
                }

                if (damageable != null)
                {
                    float damageAmount = damage * (1 - (distance / blastRadius));
                    damageAmount = Mathf.Max(0.5f, damageAmount);
                    damageable.TakeDamage(damageAmount, gameObject, DamageType.Explosion);

                    Debug.Log($"[Trap] 💣 Dealt {damageAmount} explosion damage to {col.name}");
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