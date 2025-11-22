using System.Collections;
using Hanzo.Core.Interfaces;
using Hanzo.Core.Utilities;
using UnityEngine;

namespace Hanzo.Traps
{
    public enum TrapType
    {
        CollisionDetonation,
        TimedDetonation,
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
        private DamageIndicator activeIndicator;

        // OPTIMIZATION: Cache player reference to avoid FindGameObjectWithTag every frame
        private Transform cachedPlayer;
        private float maxRangeSqr; // Use squared distance to avoid sqrt calculation

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            CachePlayerReference();
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
                
                // Update the indicator's remaining time directly if it's shown
                if (indicatorShown && activeIndicator != null)
                {
                    activeIndicator.UpdateRemainingTime(currentCountdown);
                }
            }
        }

        // OPTIMIZATION: Cache player reference once instead of finding every frame
        private void CachePlayerReference()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                cachedPlayer = playerObj.transform;
                
                // Pre-calculate squared distance threshold
                if (DamageIndicatorManager.Instance != null)
                {
                    float maxRange = DamageIndicatorManager.Instance.maxTrackingDistance;
                    maxRangeSqr = maxRange * maxRange;
                }
            }
            else
            {
                // Retry if player not found yet
                Invoke(nameof(CachePlayerReference), 0.5f);
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
            activeIndicator = null;
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
            currentCountdown = shakeDuration;
            yield return StartCoroutine(ShakeEffect(shakeDuration));
            
            Detonate();
        }

        // OPTIMIZATION: Zero-allocation distance check using sqrMagnitude
        void CheckPlayerRangeForIndicator()
        {
            if (DamageIndicatorManager.Instance == null || cachedPlayer == null)
            {
                // Try to cache player if not found
                if (cachedPlayer == null)
                    CachePlayerReference();
                return;
            }

            // Use sqrMagnitude to avoid expensive Sqrt calculation
            float distanceSqr = (transform.position - cachedPlayer.position).sqrMagnitude;

            if (distanceSqr <= maxRangeSqr && !indicatorShown)
            {
                // Player entered range - show indicator ONCE
                float actualDistance = Mathf.Sqrt(distanceSqr); // Only calc when needed for logging
                Debug.Log($"[Trap] Showing indicator for {gameObject.name} (distance: {actualDistance:F1}m, countdown: {currentCountdown:F1}s)");
                DamageIndicatorManager.Instance.ShowIndicator(transform, currentCountdown);
                
                // Cache the indicator reference for direct updates
                var activeIndicators = DamageIndicatorManager.Instance.GetActiveIndicators();
                if (activeIndicators.TryGetValue(transform, out DamageIndicator indicator))
                {
                    activeIndicator = indicator;
                }
                
                indicatorShown = true;
            }
            else if (distanceSqr > maxRangeSqr && indicatorShown)
            {
                // Player left range - hide indicator
                float actualDistance = Mathf.Sqrt(distanceSqr);
                Debug.Log($"[Trap] Hiding indicator for {gameObject.name} (distance: {actualDistance:F1}m)");
                DamageIndicatorManager.Instance.HideIndicator(transform);
                activeIndicator = null;
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
                currentCountdown = duration - elapsed;

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
                activeIndicator = null;
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