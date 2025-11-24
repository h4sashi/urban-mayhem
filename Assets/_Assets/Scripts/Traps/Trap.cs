using System.Collections;
using System.Collections.Generic;
using Hanzo.Core.Interfaces;
using Hanzo.Core.Utilities;
using UnityEngine;
using Photon.Pun;

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

        // PHOTON MULTIPLAYER: Track indicators per player
        private Dictionary<Transform, PlayerIndicatorData> playerIndicators = 
            new Dictionary<Transform, PlayerIndicatorData>();

        private class PlayerIndicatorData
        {
            public Transform playerTransform;
            public DamageIndicatorManager manager;
            public DamageIndicator indicator;
            public bool isShown;
            public float maxRangeSqr;
        }

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            FindAllPlayers();
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

            // Check all players for proximity
            if (trapType == TrapType.TimedDetonation && countdownActive && showDamageIndicator)
            {
                CheckAllPlayersForIndicators();
            }
        }

        /// <summary>
        /// ONLINE & OFFLINE: Find all players and their indicator managers
        /// </summary>
        private void FindAllPlayers()
        {
            playerIndicators.Clear();

            // Try ONLINE mode first (Photon Network)
            PhotonView[] allPhotonViews = FindObjectsOfType<PhotonView>();
            bool foundOnlinePlayer = false;
            
            foreach (PhotonView pv in allPhotonViews)
            {
                // Look for local player only (each client handles their own indicators)
                if (pv.IsMine && pv.CompareTag("Player"))
                {
                    // Find the DamageIndicatorManager under this player's hierarchy
                    DamageIndicatorManager manager = pv.GetComponentInChildren<DamageIndicatorManager>();
                    
                    if (manager != null && manager.IsLocalPlayerManager())
                    {
                        PlayerIndicatorData data = new PlayerIndicatorData
                        {
                            playerTransform = pv.transform,
                            manager = manager,
                            indicator = null,
                            isShown = false,
                            maxRangeSqr = manager.maxTrackingDistance * manager.maxTrackingDistance
                        };
                        
                        playerIndicators[pv.transform] = data;
                        foundOnlinePlayer = true;
                        Debug.Log($"[Trap] ONLINE - Registered player {pv.ViewID} for damage indicators");
                    }
                }
            }

            // If no online players found, try OFFLINE mode
            if (!foundOnlinePlayer)
            {
                GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("Player");
                
                foreach (GameObject playerObj in allPlayers)
                {
                    DamageIndicatorManager manager = playerObj.GetComponentInChildren<DamageIndicatorManager>();
                    
                    if (manager != null)
                    {
                        PlayerIndicatorData data = new PlayerIndicatorData
                        {
                            playerTransform = playerObj.transform,
                            manager = manager,
                            indicator = null,
                            isShown = false,
                            maxRangeSqr = manager.maxTrackingDistance * manager.maxTrackingDistance
                        };
                        
                        playerIndicators[playerObj.transform] = data;
                        Debug.Log($"[Trap] OFFLINE - Registered player {playerObj.name} for damage indicators");
                    }
                }
            }

            if (playerIndicators.Count == 0)
            {
                // Retry if no players found yet
                Invoke(nameof(FindAllPlayers), 0.5f);
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
            
            // Clear all player indicators
            foreach (var data in playerIndicators.Values)
            {
                if (data.isShown && data.manager != null)
                {
                    data.manager.HideIndicator(transform);
                }
            }
            playerIndicators.Clear();
            
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

        IEnumerator TimedDetonationRoutine()
        {
            countdownActive = true;
            currentCountdown = detonationDelay;

            // Check immediately if any players are in range
            if (showDamageIndicator)
            {
                CheckAllPlayersForIndicators();
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

        /// <summary>
        /// PHOTON: Check all registered players for proximity
        /// </summary>
        void CheckAllPlayersForIndicators()
        {
            // Clean up any destroyed players first
            List<Transform> toRemove = new List<Transform>();
            foreach (var kvp in playerIndicators)
            {
                if (kvp.Key == null || kvp.Value.playerTransform == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var key in toRemove)
            {
                playerIndicators.Remove(key);
            }

            // Check each player's distance
            foreach (var data in playerIndicators.Values)
            {
                if (data.playerTransform == null || data.manager == null)
                    continue;

                float distanceSqr = (transform.position - data.playerTransform.position).sqrMagnitude;

                if (distanceSqr <= data.maxRangeSqr && !data.isShown)
                {
                    // Player entered range - show indicator
                    data.manager.ShowIndicator(transform, currentCountdown);
                    
                    var activeIndicators = data.manager.GetActiveIndicators();
                    if (activeIndicators.TryGetValue(transform, out DamageIndicator indicator))
                    {
                        data.indicator = indicator;
                    }
                    
                    data.isShown = true;
                    Debug.Log($"[Trap] Showing indicator for player at distance: {Mathf.Sqrt(distanceSqr):F1}m");
                }
                else if (distanceSqr > data.maxRangeSqr && data.isShown)
                {
                    // Player left range - hide indicator
                    data.manager.HideIndicator(transform);
                    data.indicator = null;
                    data.isShown = false;
                    Debug.Log($"[Trap] Hiding indicator - player out of range: {Mathf.Sqrt(distanceSqr):F1}m");
                }
                else if (data.isShown && data.indicator != null)
                {
                    // Update existing indicator
                    data.indicator.UpdateRemainingTime(currentCountdown);
                }
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

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = true;

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

            // Hide indicators for all players
            if (showDamageIndicator)
            {
                foreach (var data in playerIndicators.Values)
                {
                    if (data.isShown && data.manager != null)
                    {
                        data.manager.HideIndicator(transform);
                    }
                }
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