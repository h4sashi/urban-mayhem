using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace Hanzo.Networking.Utils
{
    /// <summary>
    /// Spawns AI players in the game scene if the player started matchmaking alone
    /// and no other players joined within the timeout period.
    /// </summary>
    public class AISpawnerManager : MonoBehaviourPunCallbacks
    {
        [Header("AI Spawn Settings")]
        [SerializeField]
        private GameObject[] aiPrefabs; // AI prefabs (should be in Resources folder)
        
        [Header("Spawn Area")]
        [SerializeField]
        private float spawnRadius = 20f;
        [SerializeField]
        private LayerMask obstacleMask;
        [SerializeField]
        private float clearRadius = 2.0f;
        [SerializeField]
        private int maxAttempts = 30;
        
        [Header("NavMesh")]
        [SerializeField]
        private float navSampleDistance = 2f;
        
        [Header("Timing")]
        [SerializeField]
        private float delayBeforeSpawn = 1f; // Wait a bit for scene to fully load
        [SerializeField]
        private float delayBetweenSpawns = 0.3f; // Stagger AI spawns
        
        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = true;
        
        private bool hasSpawnedAIs = false;
        
        void Start()
        {
            // Only Master Client spawns AIs
            if (!PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected)
            {
                Debug.Log("[AI Spawner] Not master client, skipping AI spawn check");
                return;
            }
            
            // Check if we should spawn AIs
            StartCoroutine(CheckAndSpawnAIs());
        }
        
        private IEnumerator CheckAndSpawnAIs()
        {
            // Wait for scene to fully load
            yield return new WaitForSeconds(delayBeforeSpawn);
            
            // Check PlayerPrefs flag set by MatchmakingManager
            int shouldSpawnAIs = PlayerPrefs.GetInt("SpawnAIPlayers", 0);
            
            if (shouldSpawnAIs == 1 && !hasSpawnedAIs)
            {
                int numberOfAIs = PlayerPrefs.GetInt("NumberOfAIs", 3);
                string aiPrefabName = PlayerPrefs.GetString("AIPrefabName", "AIPlayer");
                
                Debug.Log($"[AI Spawner] Starting AI spawn: {numberOfAIs} AIs using prefab '{aiPrefabName}'");
                
                yield return StartCoroutine(SpawnAIPlayers(numberOfAIs, aiPrefabName));
                
                // Clear the flag so AIs don't spawn again if scene reloads
                PlayerPrefs.SetInt("SpawnAIPlayers", 0);
                PlayerPrefs.Save();
                
                hasSpawnedAIs = true;
            }
            else
            {
                Debug.Log($"[AI Spawner] No AI spawn needed (Flag: {shouldSpawnAIs})");
            }
        }
        
        
        private IEnumerator SpawnAIPlayers(int count, string prefabName)
{
    // Get available AI prefab names
    string[] availablePrefabs = new string[] { "AIPlayer_1", "AIPlayer_2", "AIPlayer_4" };
    
    for (int i = 0; i < count; i++)
    {
        Vector3 spawnPos;
        bool found = FindValidSpawnPosition(out spawnPos);
        
        if (!found)
        {
            Debug.LogWarning($"[AI Spawner] Could not find valid spawn for AI {i + 1}. Using fallback position.");
            spawnPos = transform.position + Random.insideUnitSphere * spawnRadius;
            spawnPos.y = transform.position.y + 1f;
        }
        
        // Select a random AI prefab from available ones
        string selectedPrefab = availablePrefabs[Random.Range(0, availablePrefabs.Length)];
        
        // Spawn AI using Photon
        GameObject aiPlayer = PhotonNetwork.Instantiate(
            selectedPrefab,  // Use the correct prefab name
            spawnPos,
            Quaternion.identity,
            0
        );
        
        if (aiPlayer != null)
        {
            aiPlayer.name = $"AI_Player_{i + 1}";
            PhotonView pv = aiPlayer.GetComponent<PhotonView>();
            if (pv != null)
            {
                Debug.Log($"[AI Spawner] ✓ Spawned {selectedPrefab} as AI {i + 1}/{count} at {spawnPos}");
            }
            
            yield return new WaitForSeconds(delayBetweenSpawns);
        }
        else
        {
            Debug.LogError($"[AI Spawner] Failed to spawn {selectedPrefab}! Check Resources folder.");
        }
    }
    
    Debug.Log($"[AI Spawner] ✓✓✓ All {count} AIs spawned successfully!");
}


        /// <summary>
        /// Find a valid spawn position using NavMesh and obstacle checking
        /// </summary>
        private bool FindValidSpawnPosition(out Vector3 result)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector2 circle = Random.insideUnitCircle * spawnRadius;
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);
                
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(candidate, out navHit, navSampleDistance, NavMesh.AllAreas))
                {
                    Vector3 navPoint = navHit.position + Vector3.up * 0.5f;
                    
                    // Check if position is clear
                    Collider[] hits = Physics.OverlapSphere(navPoint, clearRadius, obstacleMask);
                    if (hits.Length == 0)
                    {
                        result = navPoint;
                        return true;
                    }
                }
            }
            
            result = Vector3.zero;
            return false;
        }
        
        /// <summary>
        /// Public method to manually trigger AI spawn (useful for testing)
        /// </summary>
        [ContextMenu("Spawn AIs Now")]
        public void ManualSpawnAIs()
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                Debug.LogWarning("[AI Spawner] Only Master Client can spawn AIs!");
                return;
            }
            
            if (hasSpawnedAIs)
            {
                Debug.LogWarning("[AI Spawner] AIs already spawned!");
                return;
            }
            
            int count = PlayerPrefs.GetInt("NumberOfAIs", 3);
            string prefabName = PlayerPrefs.GetString("AIPrefabName", "AIPlayer");
            StartCoroutine(SpawnAIPlayers(count, prefabName));
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
            
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, clearRadius);
        }
    }
}