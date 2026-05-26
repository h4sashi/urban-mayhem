using System.Collections;
using Hanzo.AI;
using Photon.Pun;
using UnityEngine;

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

        [Header("Spawn Positions")]
        [SerializeField]
        private GameObject[] spawnPoints;

        [Header("Spawn Area")]
        [SerializeField]
        private float spawnRadius = 20f;

        [SerializeField]
        private LayerMask obstacleMask;

        [SerializeField]
        private float clearRadius = 2.0f;

        [SerializeField]
        private int maxAttempts = 30;

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
            // These lines MUST NOT exist — delete them if present
            // PlayerPrefs.SetInt("SpawnAIPlayers", 0);  ← DELETE IF PRESENT
            // PlayerPrefs.Save();                        ← DELETE IF PRESENT

            if (!PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected)
            {
                Debug.Log("[AI Spawner] Not master client, skipping.");
                return;
            }

            Debug.Log("[AI Spawner] Ready. Waiting for AIFillTimer to trigger spawn.");
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

                Debug.Log(
                    $"[AI Spawner] Starting AI spawn: {numberOfAIs} AIs using prefab '{aiPrefabName}'"
                );

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

        public void ResetSpawnFlag()
        {
            hasSpawnedAIs = false;
        }

        private IEnumerator SpawnAIPlayers(int count, string prefabName)
        {
            // Get available AI prefab names
            string[] availablePrefabs = new string[]
            {
                "AIPlayer_1",
                "AIPlayer_2",
                "AIPlayer_3",
                "AIPlayer_4",
                "AIPlayer_5",
                "AIPlayer_6",
            };

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos;
                bool found = FindValidSpawnPosition(out spawnPos);

                if (!found)
                {
                    Debug.LogWarning(
                        $"[AI Spawner] Could not find valid spawn for AI {i + 1}. Using fallback position."
                    );
                    spawnPos = transform.position + Random.insideUnitSphere * spawnRadius;
                    spawnPos.y = transform.position.y + 1f;
                }

                // Select a random AI prefab from available ones
                string selectedPrefab = availablePrefabs[Random.Range(0, availablePrefabs.Length)];
                int aiId = -(i + 1);
                string aiDisplayName = AINameCatalog.GetNameForId(aiId);
                object[] instantiationData = { aiId };

                // Spawn AI using Photon
                GameObject aiPlayer = PhotonNetwork.Instantiate(
                    selectedPrefab, // Use the correct prefab name
                    spawnPos,
                    Quaternion.identity,
                    0,
                    instantiationData
                );

                if (aiPlayer != null)
                {
                    aiPlayer.name = aiDisplayName;

                    Hanzo.AI.AIPlayerController aiController =
                        aiPlayer.GetComponent<Hanzo.AI.AIPlayerController>();
                    if (aiController != null)
                        aiController.ConfigureIdentity(aiId, aiDisplayName);

                    if (Hanzo.Networking.NetworkedScoreManager.Instance != null)
                        Hanzo.Networking.NetworkedScoreManager.Instance.RegisterAIPlayer(
                            aiId,
                            aiDisplayName
                        );

                    PhotonView pv = aiPlayer.GetComponent<PhotonView>();
                    if (pv != null)
                    {
                        Debug.Log(
                            $"[AI Spawner] ✓ Spawned {selectedPrefab} as {aiDisplayName} at {spawnPos} (slot {i + 1}/{count})"
                        );
                    }

                    yield return new WaitForSeconds(delayBetweenSpawns);
                }
                else
                {
                    Debug.LogError(
                        $"[AI Spawner] Failed to spawn {selectedPrefab}! Check Resources folder."
                    );
                }
            }

            Debug.Log($"[AI Spawner] ✓✓✓ All {count} AIs spawned successfully!");
        }

        /// <summary>
        /// Find a valid spawn position using explicit spawn point GameObjects.
        /// Falls back to random positions around this manager if no points are assigned.
        /// </summary>
        private bool FindValidSpawnPosition(out Vector3 result)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                for (int i = 0; i < maxAttempts; i++)
                {
                    GameObject spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
                    if (spawnPoint == null || !spawnPoint.activeInHierarchy)
                    {
                        continue;
                    }

                    Vector3 candidate = spawnPoint.transform.position;
                    Collider[] hits = Physics.OverlapSphere(candidate, clearRadius, obstacleMask);
                    if (hits.Length == 0)
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
            else
            {
                for (int i = 0; i < maxAttempts; i++)
                {
                    Vector2 circle = Random.insideUnitCircle * spawnRadius;
                    Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);
                    Collider[] hits = Physics.OverlapSphere(candidate, clearRadius, obstacleMask);
                    if (hits.Length == 0)
                    {
                        result = candidate;
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
                Debug.LogWarning("[AI Spawner] Already spawned — call ResetSpawnFlag() first.");
                return;
            }

            int count = PlayerPrefs.GetInt("NumberOfAIs", 0);
            string prefabName = PlayerPrefs.GetString("AIPrefabName", "AIPlayer");

            Debug.Log($"[AI Spawner] ManualSpawnAIs: count={count}, prefab={prefabName}");

            if (count <= 0)
            {
                Debug.LogWarning("[AI Spawner] NumberOfAIs=0, nothing to spawn.");
                return;
            }

            hasSpawnedAIs = true;
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
