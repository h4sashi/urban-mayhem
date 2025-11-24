using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

namespace Hanzo.Core.Utilities
{
    /// <summary>
    /// Manages damage indicators with object pooling for mobile performance.
    /// Instance-based for Photon networking - each player has their own manager.
    /// Attach to IndicatorContainer under each player's HUDCanvas.
    /// </summary>
    public class DamageIndicatorManager : MonoBehaviour
    {
        // REMOVED: Static singleton - now instance-based per player
        
        [Header("Setup")]
        [SerializeField]
        private GameObject indicatorPrefab;

        [SerializeField]
        private Transform indicatorContainer;

        [SerializeField]
        private int poolSize = 8;

        [Header("Settings")]
        [SerializeField]
        public float maxTrackingDistance = 50f;

        [SerializeField]
        private int maxActiveIndicators = 5;

        // Object pool
        private List<DamageIndicator> pool;
        private Dictionary<Transform, DamageIndicator> activeIndicators;

        // Player reference - no longer cached globally
        private Transform playerTransform;
        private PhotonView playerPhotonView;
        private bool isLocalPlayer;

        private void Awake()
        {
            // NO singleton - each player has their own instance
            InitializePlayerReference();
            
            if (isLocalPlayer)
            {
                InitializePool();
            }
            else
            {
                // Disable for non-local players
                enabled = false;
            }
        }

        private void InitializePlayerReference()
        {
            // Get PhotonView from parent Player_4
            playerPhotonView = GetComponentInParent<PhotonView>();
            
            if (playerPhotonView != null)
            {
                // ONLINE: Check if this is the local player's PhotonView
                isLocalPlayer = playerPhotonView.IsMine;
                playerTransform = playerPhotonView.transform;
                
                Debug.Log($"[DIM] ONLINE Mode - Player {playerPhotonView.ViewID} - IsLocal: {isLocalPlayer}");
            }
            else
            {
                // OFFLINE: No PhotonView means single-player mode
                // Look for any player with the "Player" tag
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                    isLocalPlayer = true;
                    Debug.Log($"[DIM] OFFLINE Mode - Found player: {playerTransform.name}");
                }
                else
                {
                    Debug.LogError("[DIM] No player found! Make sure player has 'Player' tag.");
                    enabled = false;
                }
            }
        }

        private RectTransform canvasRect;

        private void InitializePool()
        {
            pool = new List<DamageIndicator>(poolSize);
            activeIndicators = new Dictionary<Transform, DamageIndicator>(poolSize);

            canvasRect = indicatorContainer
                .GetComponentInParent<Canvas>()
                .GetComponent<RectTransform>();

            for (int i = 0; i < poolSize; i++)
            {
                GameObject obj = Instantiate(indicatorPrefab, indicatorContainer);
                DamageIndicator indicator = obj.GetComponent<DamageIndicator>();
                indicator.Initialize(canvasRect);
                indicator.Deactivate();
                pool.Add(indicator);
            }

            Debug.Log($"[DIM] Initialized pool with {pool.Count} indicators for local player");
        }

        /// <summary>
        /// Shows indicator for a trap that's about to detonate.
        /// Only works if this is the local player's manager.
        /// </summary>
        public void ShowIndicator(Transform trapTransform, float duration)
        {
            if (!isLocalPlayer || playerTransform == null || trapTransform == null)
                return;

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
            if (!isLocalPlayer || trapTransform == null)
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
                    continue;

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
            if (!isLocalPlayer || playerTransform == null)
                return;

            // Clean up any null transforms and check distances
            List<Transform> toRemove = new List<Transform>();
            
            foreach (var kvp in activeIndicators)
            {
                if (kvp.Key == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                float distance = Vector3.Distance(playerTransform.position, kvp.Key.position);
                if (distance > maxTrackingDistance)
                {
                    toRemove.Add(kvp.Key);
                    Debug.Log($"[DIM] Player moved out of range for {kvp.Key.name} (distance: {distance:F1}m)");
                }
            }

            foreach (var key in toRemove)
            {
                HideIndicator(key);
            }
        }

        /// <summary>
        /// Gets the dictionary of active indicators for direct indicator updates
        /// </summary>
        public Dictionary<Transform, DamageIndicator> GetActiveIndicators()
        {
            return activeIndicators;
        }

        public bool IsLocalPlayerManager()
        {
            return isLocalPlayer;
        }
    }
}