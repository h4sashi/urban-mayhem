using System.Collections.Generic;
using UnityEngine;

namespace Hanzo.Core.Utilities
{
    /// <summary>
    /// Manages damage indicators with object pooling for mobile performance.
    /// Attach to a Canvas object in your scene.
    /// </summary>
    public class DamageIndicatorManager : MonoBehaviour
    {
        public static DamageIndicatorManager Instance { get; private set; }

        [Header("Setup")]
        [SerializeField]
        private GameObject indicatorPrefab;

        [SerializeField]
        private Transform indicatorContainer;

        [SerializeField]
        private int poolSize = 8; // Max simultaneous indicators

        [Header("Settings")]
        [SerializeField]
        public float maxTrackingDistance = 50f;

        [SerializeField]
        public int maxActiveIndicators = 5; // Limit for mobile performance

        // Object pool
        private List<DamageIndicator> pool;
        private Dictionary<Transform, DamageIndicator> activeIndicators;

        // Cached player reference
        private Transform playerTransform;
        private bool playerCached;

        private List<TrapIndicatorRequest> pendingTraps = new List<TrapIndicatorRequest>();

        private class TrapIndicatorRequest
        {
            public Transform trapTransform;
            public float duration;
        }

        private void Awake()
        {
            // Singleton setup
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializePool();
        }

        private void Start()
        {
            CachePlayerReference();
        }

        private RectTransform canvasRect;

        private void InitializePool()
        {
            pool = new List<DamageIndicator>(poolSize);
            activeIndicators = new Dictionary<Transform, DamageIndicator>(poolSize);

            // Cache canvas RectTransform for screen bounds calculation
            canvasRect = indicatorContainer
                .GetComponentInParent<Canvas>()
                .GetComponent<RectTransform>();

            for (int i = 0; i < poolSize; i++)
            {
                GameObject obj = Instantiate(indicatorPrefab, indicatorContainer);
                DamageIndicator indicator = obj.GetComponent<DamageIndicator>();
                indicator.Initialize(canvasRect); // Pass canvas reference
                indicator.Deactivate();
                pool.Add(indicator);
            }

            Debug.Log($"[DIM] Initialized pool with {pool.Count} indicators");
        }

        private void CachePlayerReference()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                playerCached = true;
                Debug.Log($"[DIM] Player reference cached: {playerTransform.name}");
            }
            else
            {
                Debug.LogWarning("[DIM] Player not found! Will retry...");
                Invoke(nameof(CachePlayerReference), 0.5f);
            }
        }

        /// <summary>
        /// Shows indicator for a trap that's about to detonate.
        /// Call this when TimedDetonation starts its countdown.
        /// </summary>
        public void ShowIndicator(Transform trapTransform, float duration)
        {
            if (!playerCached || trapTransform == null){
                Debug.LogWarning("[DIM] Cannot show indicator - player not cached or trapTransform is NULL.");
                return;
            }

            // Check distance
            float dist = Vector3.Distance(playerTransform.position, trapTransform.position);
            if (dist > maxTrackingDistance)
                return;

            // Already tracking this trap?
            if (activeIndicators.ContainsKey(trapTransform))
                return;

            // Enforce max active limit
            if (activeIndicators.Count >= maxActiveIndicators)
            {
                RemoveFurthestIndicator();
            }

            // Get from pool
            DamageIndicator indicator = GetFromPool();
            if (indicator == null)
            {
                Debug.LogWarning("[DIM] Pool exhausted!");
                return;
            }

            indicator.Activate(trapTransform, playerTransform, duration);
            activeIndicators[trapTransform] = indicator;

            Debug.Log(
                $"[DIM] Activated indicator for {trapTransform.name}. Active: {activeIndicators.Count}"
            );
        }

        /// <summary>
        /// Removes indicator when trap detonates or is destroyed.
        /// </summary>
        public void HideIndicator(Transform trapTransform)
        {
            if (trapTransform == null)
                return;

            if (activeIndicators.TryGetValue(trapTransform, out DamageIndicator indicator))
            {
                indicator.Deactivate();
                activeIndicators.Remove(trapTransform);
                Debug.Log(
                    $"[DIM] Deactivated indicator for {trapTransform.name}. Active: {activeIndicators.Count}"
                );
            }
        }

        private DamageIndicator GetFromPool()
        {
            foreach (var indicator in pool)
            {
                if (!indicator.IsActive && !indicator.gameObject.activeInHierarchy)
                {
                    return indicator;
                }
            }
            return null;
        }

        private void RemoveFurthestIndicator()
        {
            float maxDist = 0f;
            Transform furthestKey = null;

            foreach (var kvp in activeIndicators)
            {
                if (kvp.Key == null)
                    continue; // Skip destroyed transforms

                float dist = kvp.Value.GetDistanceToPlayer();
                if (dist > maxDist)
                {
                    maxDist = dist;
                    furthestKey = kvp.Key;
                }
            }

            if (furthestKey != null)
            {
                HideIndicator(furthestKey);
            }
        }

        private void Update()
        {
            if (!playerCached)
                return;

            // Clean up any null transforms and check distances
            List<Transform> toRemove = new List<Transform>();
            
            foreach (var kvp in activeIndicators)
            {
                // Remove destroyed transforms
                if (kvp.Key == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Check if player has moved out of range
                float distance = Vector3.Distance(playerTransform.position, kvp.Key.position);
                if (distance > maxTrackingDistance)
                {
                    toRemove.Add(kvp.Key);
                    Debug.Log($"[DIM] Player moved out of range for {kvp.Key.name} (distance: {distance:F1}m)");
                }
            }

            // Deactivate all indicators marked for removal
            foreach (var key in toRemove)
            {
                HideIndicator(key);
            }

            if (toRemove.Count > 0)
            {
                Debug.Log($"[DIM] Cleaned up {toRemove.Count} indicators");
            }
        }

        /// <summary>
        /// Call when player takes damage from a direction (original damage indicator use)
        /// </summary>
        public void ShowDamageFromDirection(Vector3 damageSourcePosition, float duration = 2f)
        {
            if (!playerCached)
                return;

            // Create temporary tracker object (reuse pool)
            DamageIndicator indicator = GetFromPool();
            if (indicator == null)
                return;

            // For directional damage, we create a virtual target
            GameObject tempTarget = new GameObject("DamageSource_Temp");
            tempTarget.transform.position = damageSourcePosition;

            indicator.Activate(tempTarget.transform, playerTransform, duration, 0.8f);

            // Clean up temp object when indicator deactivates
            StartCoroutine(CleanupTempTarget(tempTarget, duration + 0.1f));
        }

        private System.Collections.IEnumerator CleanupTempTarget(GameObject target, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (target != null)
                Destroy(target);
        }

        /// <summary>
        /// Updates player reference if it changes (respawn, etc.)
        /// </summary>
        public void SetPlayerReference(Transform newPlayer)
        {
            playerTransform = newPlayer;
            playerCached = newPlayer != null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Debug method to show current state
        public void DebugState()
        {
            Debug.Log(
                $"[DIM] DEBUG - Pool: {pool.Count}, Active: {activeIndicators.Count}, PlayerCached: {playerCached}"
            );
            foreach (var kvp in activeIndicators)
            {
                Debug.Log(
                    $"[DIM]   Tracking: {kvp.Key?.name ?? "NULL"} -> {kvp.Value?.name ?? "NULL"}"
                );
            }
        }
    }
}