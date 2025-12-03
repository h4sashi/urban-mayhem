using System.Collections;
using System.Collections.Generic;
using Hanzo.Audio;
using Hanzo.Core.Interfaces;
using Hanzo.Core.Utilities;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Traps
{
    public enum TrapType
    {
        CollisionDetonation,
        TimedDetonation,
    }

    public class Trap : MonoBehaviourPun
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

        private Dictionary<Transform, PlayerIndicatorData> playerIndicators =
            new Dictionary<Transform, PlayerIndicatorData>();

        [Header("Audio Settings")]
        public AudioManager audioManager;
        
        [Tooltip("Doppler effect intensity (0 = disabled, 1 = realistic physics)")]
        [Range(0f, 5f)]
        public float audioDopplerLevel = 0f;

        // Track the original audio source reference (before detaching)
        private AudioSource originalAudioSource;
        private GameObject originalAudioObject;

        // MOBILE OPTIMIZATION: Cache static player references to avoid repeated FindObjectsOfType
        private static List<PlayerIndicatorData> cachedPlayers = new List<PlayerIndicatorData>();
        private static float lastPlayerCacheTime = -999f;
        private const float PLAYER_CACHE_REFRESH_INTERVAL = 2f; // Refresh every 2 seconds if needed

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
            RegisterWithCachedPlayers();
            InitSound();
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

            if (trapType == TrapType.TimedDetonation && countdownActive && showDamageIndicator)
            {
                CheckAllPlayersForIndicators();
            }
        }

        private void InitSound()
        {
            if (audioManager.audioSource == null)
            {
                foreach (var t_child in this.GetComponentsInChildren<AudioSource>())
                {
                    if (t_child.gameObject.name == "StunAudioSource")
                    {
                        audioManager.audioSource = t_child;
                        break;
                    }
                }
            }

            // Store reference to original audio components
            if (audioManager.audioSource != null)
            {
                originalAudioSource = audioManager.audioSource;
                originalAudioObject = audioManager.audioSource.gameObject;

                audioManager.audioSource.playOnAwake = false;
                audioManager.audioSource.spatialBlend = 1f;
                audioManager.audioSource.rolloffMode = audioManager.audioRolloffMode;
                audioManager.audioSource.minDistance = audioManager.audioMinDistance;
                audioManager.audioSource.maxDistance = audioManager.audioMaxDistance;
                audioManager.audioSource.dopplerLevel = audioDopplerLevel;
            }
        }

        private void PlayDetonationSound()
        {
            if (PhotonNetwork.IsConnected && photonView != null)
            {
                photonView.RPC("RPC_PlayDetonationSound", RpcTarget.All);
            }
            else
            {
                PlayDetonationSoundLocal();
            }
        }

        [PunRPC]
        private void RPC_PlayDetonationSound()
        {
            PlayDetonationSoundLocal();
        }

        private void PlayDetonationSoundLocal()
        {
            if (audioManager.audioSource != null && audioManager.audioClip != null)
            {
                // Create a temporary GameObject for the sound at the explosion position
                GameObject tempAudioObj = new GameObject("TrapExplosionSound");
                tempAudioObj.transform.position = transform.position;
                
                // Add and configure AudioSource
                AudioSource tempAudioSource = tempAudioObj.AddComponent<AudioSource>();
                tempAudioSource.clip = audioManager.audioClip;
                tempAudioSource.spatialBlend = 1f;
                tempAudioSource.rolloffMode = audioManager.audioRolloffMode;
                tempAudioSource.minDistance = audioManager.audioMinDistance;
                tempAudioSource.maxDistance = audioManager.audioMaxDistance;
                tempAudioSource.dopplerLevel = audioDopplerLevel;
                tempAudioSource.volume = audioManager.audioSource.volume;
                tempAudioSource.pitch = audioManager.audioSource.pitch;
                
                // Play the sound
                tempAudioSource.Play();
                
                // Destroy the temporary audio object after the clip finishes
                float clipLength = audioManager.audioClip.length;
                Destroy(tempAudioObj, clipLength + 0.1f);
            }
        }

        /// <summary>
        /// MOBILE OPTIMIZED: Uses static cache to avoid repeated FindObjectsOfType calls
        /// </summary>
        private static void RefreshPlayerCache()
        {
            cachedPlayers.Clear();
            
            // Try online players first
            PhotonView[] allPhotonViews = FindObjectsOfType<PhotonView>();
            bool foundOnlinePlayer = false;

            foreach (PhotonView pv in allPhotonViews)
            {
                if (pv.IsMine && pv.CompareTag("Player"))
                {
                    DamageIndicatorManager manager =
                        pv.GetComponentInChildren<DamageIndicatorManager>();

                    if (manager != null && manager.IsLocalPlayerManager())
                    {
                        PlayerIndicatorData data = new PlayerIndicatorData
                        {
                            playerTransform = pv.transform,
                            manager = manager,
                            indicator = null,
                            isShown = false,
                            maxRangeSqr = manager.maxTrackingDistance * manager.maxTrackingDistance,
                        };

                        cachedPlayers.Add(data);
                        foundOnlinePlayer = true;
                    }
                }
            }

            // Fallback to offline mode
            if (!foundOnlinePlayer)
            {
                GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("Player");

                foreach (GameObject playerObj in allPlayers)
                {
                    DamageIndicatorManager manager =
                        playerObj.GetComponentInChildren<DamageIndicatorManager>();

                    if (manager != null)
                    {
                        PlayerIndicatorData data = new PlayerIndicatorData
                        {
                            playerTransform = playerObj.transform,
                            manager = manager,
                            indicator = null,
                            isShown = false,
                            maxRangeSqr = manager.maxTrackingDistance * manager.maxTrackingDistance,
                        };

                        cachedPlayers.Add(data);
                    }
                }
            }

            lastPlayerCacheTime = Time.time;
        }

        /// <summary>
        /// MOBILE OPTIMIZED: Registers this trap with cached player list instead of searching
        /// </summary>
        private void RegisterWithCachedPlayers()
        {
            playerIndicators.Clear();

            // Refresh cache if it's empty or stale
            if (cachedPlayers.Count == 0 || Time.time - lastPlayerCacheTime > PLAYER_CACHE_REFRESH_INTERVAL)
            {
                RefreshPlayerCache();
            }

            // Copy cached player data to this trap's dictionary
            foreach (var cachedData in cachedPlayers)
            {
                // Validate references are still valid
                if (cachedData.playerTransform == null || cachedData.manager == null)
                    continue;

                // Create new instance for this trap
                PlayerIndicatorData data = new PlayerIndicatorData
                {
                    playerTransform = cachedData.playerTransform,
                    manager = cachedData.manager,
                    indicator = null,
                    isShown = false,
                    maxRangeSqr = cachedData.maxRangeSqr,
                };

                playerIndicators[cachedData.playerTransform] = data;
            }

            // If still no players found, schedule a retry
            if (playerIndicators.Count == 0)
            {
                Invoke(nameof(RegisterWithCachedPlayers), 0.5f);
            }
        }

        /// <summary>
        /// DEPRECATED: Use RegisterWithCachedPlayers instead for better performance
        /// Kept for backwards compatibility but redirects to optimized method
        /// </summary>
        private void FindAllPlayers()
        {
            RegisterWithCachedPlayers();
        }

        public void SetTrapHandler(TrapHandler handler)
        {
            trapHandler = handler;
        }

        /// <summary>
        /// MOBILE OPTIMIZED: Reuses cached player references on respawn
        /// </summary>
        public void ResetTrap()
        {
            hasDetonated = false;
            countdownActive = false;

            // Hide indicators from all players
            foreach (var data in playerIndicators.Values)
            {
                if (data.isShown && data.manager != null)
                {
                    data.manager.HideIndicator(transform);
                }
            }

            // OPTIMIZED: Reuse cached players instead of FindObjectsOfType
            RegisterWithCachedPlayers();

            if (spawnedVFX != null)
            {
                Destroy(spawnedVFX);
                spawnedVFX = null;
            }

            // Restore original audio source reference
            if (originalAudioSource != null && originalAudioObject != null)
            {
                audioManager.audioSource = originalAudioSource;
            }
            else
            {
                InitSound();
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

            if (showDamageIndicator)
            {
                CheckAllPlayersForIndicators();
            }

            float waitTime = detonationDelay - shakeDuration;
            float elapsed = 0f;

            while (elapsed < waitTime)
            {
                elapsed += Time.deltaTime;
                currentCountdown = detonationDelay - elapsed;
                yield return null;
            }

            currentCountdown = shakeDuration;
            yield return StartCoroutine(ShakeEffect(shakeDuration));

            Detonate();
        }

        void CheckAllPlayersForIndicators()
        {
            // Clean up null references
            List<Transform> toRemove = null; // Lazy allocation

            foreach (var kvp in playerIndicators)
            {
                if (kvp.Key == null || kvp.Value.playerTransform == null)
                {
                    if (toRemove == null)
                        toRemove = new List<Transform>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var key in toRemove)
                {
                    playerIndicators.Remove(key);
                }
            }

            // If no players, attempt recovery using cached data
            if (playerIndicators.Count == 0)
            {
                RegisterWithCachedPlayers();
                return;
            }

            foreach (var data in playerIndicators.Values)
            {
                if (data.playerTransform == null || data.manager == null)
                    continue;

                float distanceSqr = (
                    transform.position - data.playerTransform.position
                ).sqrMagnitude;

                if (distanceSqr <= data.maxRangeSqr && !data.isShown)
                {
                    data.manager.ShowIndicator(transform, currentCountdown);

                    var activeIndicators = data.manager.GetActiveIndicators();
                    if (activeIndicators.TryGetValue(transform, out DamageIndicator indicator))
                    {
                        data.indicator = indicator;
                    }

                    data.isShown = true;
                }
                else if (distanceSqr > data.maxRangeSqr && data.isShown)
                {
                    data.manager.HideIndicator(transform);
                    data.indicator = null;
                    data.isShown = false;
                }
                else if (data.isShown && data.indicator != null)
                {
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

            PlayDetonationSound();

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

        /// <summary>
        /// Call this when a player joins/leaves to force cache refresh
        /// </summary>
        public static void InvalidatePlayerCache()
        {
            cachedPlayers.Clear();
            lastPlayerCacheTime = -999f;
        }
    }
}