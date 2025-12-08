using UnityEngine;
using System.Collections.Generic;
using Hanzo.Player.Core;

namespace Hanzo.AI
{
    /// <summary>
    /// Spawns and manages AI players in the game
    /// Handles spawn points, difficulty scaling, and AI lifecycle
    /// </summary>
    public class AIPlayerSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private GameObject aiPlayerPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private int maxAIPlayers = 4;
        [SerializeField] private float spawnInterval = 2f;
        
        [Header("Behavior Profiles")]
        [SerializeField] private AIBehaviorProfile[] behaviorProfiles;
        [SerializeField] private bool useRandomProfiles = true;
        [SerializeField] private bool createVariations = true;
        [SerializeField] private float variationAmount = 0.15f;
        
        [Header("Auto Spawn")]
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private bool respawnOnDeath = true;
        [SerializeField] private float respawnDelay = 5f;
        
        [Header("Difficulty Scaling")]
        [SerializeField] private bool scaleDifficulty = false;
        [SerializeField] private float difficultyIncreaseInterval = 60f;
        [SerializeField] private float maxDifficultyMultiplier = 2f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        
        // State tracking
        private List<GameObject> spawnedAIPlayers = new List<GameObject>();
        private float lastSpawnTime;
        private float gameStartTime;
        private int totalSpawned = 0;
        
        // Difficulty
        private float currentDifficultyMultiplier = 1f;
        
        private void Start()
        {
            gameStartTime = Time.time;
            
            // Validate setup
            if (aiPlayerPrefab == null)
            {
                Debug.LogError("[AISpawner] AI Player Prefab not assigned!");
                return;
            }
            
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning("[AISpawner] No spawn points assigned. Using spawner position.");
                spawnPoints = new Transform[] { transform };
            }
            
            if (behaviorProfiles == null || behaviorProfiles.Length == 0)
            {
                Debug.LogWarning("[AISpawner] No behavior profiles assigned. AI will use defaults.");
            }
            
            // Auto spawn if enabled
            if (spawnOnStart)
            {
                StartCoroutine(SpawnAllAI());
            }
        }
        
        private void Update()
        {
            // Update difficulty over time
            if (scaleDifficulty)
            {
                UpdateDifficulty();
            }
            
            // Clean up null references
            spawnedAIPlayers.RemoveAll(ai => ai == null);
        }
        
        private void UpdateDifficulty()
        {
            float gameTime = Time.time - gameStartTime;
            float difficultyProgress = gameTime / difficultyIncreaseInterval;
            currentDifficultyMultiplier = Mathf.Lerp(1f, maxDifficultyMultiplier, Mathf.Clamp01(difficultyProgress));
        }
        
        /// <summary>
        /// Spawn all AI players up to max count
        /// </summary>
        private System.Collections.IEnumerator SpawnAllAI()
        {
            for (int i = 0; i < maxAIPlayers; i++)
            {
                SpawnAIPlayer();
                yield return new WaitForSeconds(spawnInterval);
            }
        }
        
        /// <summary>
        /// Spawn a single AI player
        /// </summary>
        public GameObject SpawnAIPlayer()
        {
            if (spawnedAIPlayers.Count >= maxAIPlayers)
            {
                Debug.LogWarning("[AISpawner] Max AI players reached!");
                return null;
            }
            
            // Get spawn point
            Transform spawnPoint = GetNextSpawnPoint();
            
            // Instantiate AI player
            GameObject aiPlayer = Instantiate(aiPlayerPrefab, spawnPoint.position, spawnPoint.rotation);
            aiPlayer.name = $"AI_Player_{totalSpawned + 1}";
            
            // Setup AI controller
            AIPlayerController aiController = aiPlayer.GetComponent<AIPlayerController>();
            if (aiController != null)
            {
                // Assign behavior profile
                AIBehaviorProfile profile = GetBehaviorProfile();
                if (profile != null)
                {
                    // Use reflection to set the profile since it's a private field
                    var field = typeof(AIPlayerController).GetField("behaviorProfile", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(aiController, profile);
                }
            }
            else
            {
                Debug.LogWarning($"[AISpawner] {aiPlayer.name} missing AIPlayerController component!");
            }
            
            // Setup health component respawn
            if (respawnOnDeath)
            {
                PlayerHealthComponent health = aiPlayer.GetComponent<PlayerHealthComponent>();
                if (health != null)
                {
                    health.OnPlayerDied += () => OnAIPlayerDied(aiPlayer);
                }
            }
            
            // Track spawned AI
            spawnedAIPlayers.Add(aiPlayer);
            totalSpawned++;
            lastSpawnTime = Time.time;
            
            Debug.Log($"[AISpawner] Spawned {aiPlayer.name} at {spawnPoint.position}");
            
            return aiPlayer;
        }
        
        /// <summary>
        /// Get spawn point using round-robin
        /// </summary>
        private Transform GetNextSpawnPoint()
        {
            if (spawnPoints.Length == 1)
                return spawnPoints[0];
            
            int index = totalSpawned % spawnPoints.Length;
            return spawnPoints[index];
        }
        
        /// <summary>
        /// Get random spawn point
        /// </summary>
        private Transform GetRandomSpawnPoint()
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }
        
        /// <summary>
        /// Select behavior profile based on settings
        /// </summary>
        private AIBehaviorProfile GetBehaviorProfile()
        {
            if (behaviorProfiles == null || behaviorProfiles.Length == 0)
                return null;
            
            AIBehaviorProfile profile;
            
            if (useRandomProfiles)
            {
                profile = behaviorProfiles[Random.Range(0, behaviorProfiles.Length)];
            }
            else
            {
                int index = totalSpawned % behaviorProfiles.Length;
                profile = behaviorProfiles[index];
            }
            
            // Create variation if enabled
            if (createVariations && profile != null)
            {
                profile = profile.CreateVariation(variationAmount);
            }
            
            // Apply difficulty scaling
            if (scaleDifficulty && profile != null)
            {
                profile = ApplyDifficultyScaling(profile);
            }
            
            return profile;
        }
        
        /// <summary>
        /// Scale profile stats based on current difficulty
        /// </summary>
        private AIBehaviorProfile ApplyDifficultyScaling(AIBehaviorProfile profile)
        {
            // Create a copy to avoid modifying the original
            AIBehaviorProfile scaled = ScriptableObject.Instantiate(profile);
            
            // Scale combat effectiveness
            float aggressionBonus = (currentDifficultyMultiplier - 1f) * 20f;
            float reactionBonus = (1f - currentDifficultyMultiplier) * 0.2f; // Lower = faster
            float accuracyBonus = (currentDifficultyMultiplier - 1f) * 15f;
            
            // Apply using reflection (since fields are private)
            var type = typeof(AIBehaviorProfile);
            
            var aggressionField = type.GetField("aggression", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            aggressionField?.SetValue(scaled, Mathf.Clamp((float)aggressionField.GetValue(scaled) + aggressionBonus, 0f, 100f));
            
            var reactionField = type.GetField("reactionTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            reactionField?.SetValue(scaled, Mathf.Max((float)reactionField.GetValue(scaled) + reactionBonus, 0.1f));
            
            var accuracyField = type.GetField("accuracy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            accuracyField?.SetValue(scaled, Mathf.Clamp((float)accuracyField.GetValue(scaled) + accuracyBonus, 0f, 100f));
            
            return scaled;
        }
        
        /// <summary>
        /// Handle AI player death
        /// </summary>
        private void OnAIPlayerDied(GameObject aiPlayer)
        {
            if (!respawnOnDeath)
                return;
            
            Debug.Log($"[AISpawner] {aiPlayer.name} died. Respawning in {respawnDelay}s...");
            
            // Respawn after delay
            StartCoroutine(RespawnAIPlayer(aiPlayer));
        }
        
        private System.Collections.IEnumerator RespawnAIPlayer(GameObject aiPlayer)
        {
            yield return new WaitForSeconds(respawnDelay);
            
            // Check if still needed
            if (spawnedAIPlayers.Contains(aiPlayer) && aiPlayer != null)
            {
                // Respawn at new location
                Transform spawnPoint = GetRandomSpawnPoint();
                
                PlayerHealthComponent health = aiPlayer.GetComponent<PlayerHealthComponent>();
                if (health != null)
                {
                    health.SetRespawnPosition(spawnPoint.position);
                }
                
                Debug.Log($"[AISpawner] Respawned {aiPlayer.name} at {spawnPoint.position}");
            }
        }
        
        /// <summary>
        /// Despawn all AI players
        /// </summary>
        public void DespawnAllAI()
        {
            foreach (var ai in spawnedAIPlayers)
            {
                if (ai != null)
                {
                    Destroy(ai);
                }
            }
            
            spawnedAIPlayers.Clear();
            Debug.Log("[AISpawner] Despawned all AI players");
        }
        
        /// <summary>
        /// Get count of active AI players
        /// </summary>
        public int GetActiveAICount()
        {
            spawnedAIPlayers.RemoveAll(ai => ai == null);
            return spawnedAIPlayers.Count;
        }
        
        /// <summary>
        /// Spawn additional AI player
        /// </summary>
        [ContextMenu("Spawn AI Player")]
        public void SpawnAdditionalAI()
        {
            SpawnAIPlayer();
        }
        
        /// <summary>
        /// Despawn random AI player
        /// </summary>
        [ContextMenu("Despawn Random AI")]
        public void DespawnRandomAI()
        {
            if (spawnedAIPlayers.Count == 0)
                return;
            
            GameObject aiToRemove = spawnedAIPlayers[Random.Range(0, spawnedAIPlayers.Count)];
            spawnedAIPlayers.Remove(aiToRemove);
            Destroy(aiToRemove);
            
            Debug.Log($"[AISpawner] Despawned {aiToRemove.name}");
        }
        
        private void OnDrawGizmos()
        {
            if (spawnPoints == null)
                return;
            
            // Draw spawn points
            Gizmos.color = Color.cyan;
            foreach (var point in spawnPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 1f);
                    Gizmos.DrawLine(point.position, point.position + point.forward * 2f);
                }
            }
        }
        
        private void OnGUI()
        {
            if (!showDebugInfo)
                return;
            
            GUILayout.BeginArea(new Rect(10, Screen.height - 150, 300, 140));
            GUILayout.Label("=== AI SPAWNER ===");
            GUILayout.Label($"Active AI: {GetActiveAICount()}/{maxAIPlayers}");
            GUILayout.Label($"Total Spawned: {totalSpawned}");
            GUILayout.Label($"Difficulty: {currentDifficultyMultiplier:F2}x");
            
            if (GUILayout.Button("Spawn AI"))
            {
                SpawnAdditionalAI();
            }
            
            if (GUILayout.Button("Despawn Random AI"))
            {
                DespawnRandomAI();
            }
            
            GUILayout.EndArea();
        }
    }
}