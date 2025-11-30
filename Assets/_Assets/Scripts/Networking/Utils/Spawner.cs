using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

namespace Hanzo.Networking.Utils
{
    public class Spawner : MonoBehaviourPunCallbacks
    {
        [Header("Prefab (must be in Resources/)")]
        public GameObject[] playerPrefab; // put prefab in Assets/Resources/

        [Header("Spawn area")]
        public float spawnRadius = 20f; // radius around this GameObject to search for spawn positions
        public LayerMask obstacleMask; // layers considered as obstacles (players, props) when checking overlap
        public float clearRadius = 2.0f; // how much empty space is required around spawn point
        public int maxAttempts = 30; // how many random tries before fallback

        [Header("NavMesh")]
        public float navSampleDistance = 2f; // how far to search to snap to navmesh

        [Header("Options")]
        public bool spawnOnJoinedRoom = true; // automatically spawn when joining room
        public bool spawnAtTransformIfFail = true; // use this transform position if no valid spot found

        // Character selection key (matches ShopManager and CharacterSelector)
        private const string SELECTED_CHARACTER_PREF_KEY = "SelectedCharacterIndex";

        // Start is called before the first frame update
        void Start()
        {
            // Optionally spawn if already in room and spawnOnJoinedRoom is true
            if (spawnOnJoinedRoom && PhotonNetwork.InRoom)
            {
                TrySpawnPlayer();
            }
        }

        // Called when the local client successfully joins a room
        public override void OnJoinedRoom()
        {
            if (spawnOnJoinedRoom)
            {
                TrySpawnPlayer();
            }
        }

        /// <summary>
        /// Public method you can call from UI or manager to spawn a player
        /// </summary>
        public void TrySpawnPlayer()
        {
            if (!PhotonNetwork.IsConnected)
            {
                Debug.LogWarning("[Spawner] Cannot spawn - Photon not connected.");
                return;
            }

            if (playerPrefab == null || playerPrefab.Length == 0)
            {
                Debug.LogError("[Spawner] playerPrefab array is not assigned or empty!");
                return;
            }

            Vector3 spawnPos;
            bool found = FindValidSpawn(out spawnPos);

            if (!found)
            {
                if (spawnAtTransformIfFail)
                {
                    spawnPos = transform.position;
                    spawnPos.y += 3.0f; // small offset
                    Debug.LogWarning(
                        "[Spawner] No valid random spawn found. Falling back to spawner transform position."
                    );
                }
                else
                {
                    Debug.LogError(
                        "[Spawner] No valid spawn found and fallback disabled. Aborting spawn."
                    );
                    return;
                }
            }

            // Get selected character index from PlayerPrefs
            int selectedIndex = GetSelectedCharacterIndex();
            
            // Validate index
            if (selectedIndex < 0 || selectedIndex >= playerPrefab.Length)
            {
                Debug.LogWarning(
                    $"[Spawner] Selected character index {selectedIndex} is out of range. " +
                    $"Available prefabs: {playerPrefab.Length}. Defaulting to 0."
                );
                selectedIndex = 0;
            }

            // Ensure the prefab at the selected index is not null
            if (playerPrefab[selectedIndex] == null)
            {
                Debug.LogError($"[Spawner] Player prefab at index {selectedIndex} is null! Using index 0.");
                selectedIndex = 0;
                
                if (playerPrefab[0] == null)
                {
                    Debug.LogError("[Spawner] Default prefab (index 0) is also null! Cannot spawn.");
                    return;
                }
            }

            string resourceName = playerPrefab[selectedIndex].name;
            Debug.Log($"[Spawner] Spawning selected character: {resourceName} (index: {selectedIndex})");

            GameObject player = PhotonNetwork.Instantiate(
                resourceName,
                spawnPos,
                Quaternion.identity,
                0
            );

            // Fix position immediately after spawn
            StartCoroutine(StabilizePlayerPosition(player, spawnPos));

            Debug.Log($"[Spawner] Spawned player '{resourceName}' at {spawnPos}");
        }

        /// <summary>
        /// Get the selected character index from PlayerPrefs
        /// </summary>
        private int GetSelectedCharacterIndex()
        {
            int index = PlayerPrefs.GetInt(SELECTED_CHARACTER_PREF_KEY, 0);
            Debug.Log($"[Spawner] Retrieved selected character index: {index}");
            return index;
        }

        /// <summary>
        /// Optional: Manually set which character to spawn (useful for testing)
        /// </summary>
        public void SetSelectedCharacterIndex(int index)
        {
            if (index >= 0 && index < playerPrefab.Length)
            {
                PlayerPrefs.SetInt(SELECTED_CHARACTER_PREF_KEY, index);
                PlayerPrefs.Save();
                Debug.Log($"[Spawner] Set selected character index to: {index}");
            }
            else
            {
                Debug.LogError($"[Spawner] Invalid character index: {index}. Available: 0-{playerPrefab.Length - 1}");
            }
        }

        private System.Collections.IEnumerator StabilizePlayerPosition(
            GameObject player,
            Vector3 targetPos
        )
        {
            // Wait for one frame to let Photon initialize
            yield return null;

            // Disable physics temporarily
            Rigidbody rb = player.GetComponent<Rigidbody>();
            CharacterController cc = player.GetComponent<CharacterController>();

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (cc != null)
            {
                cc.enabled = false;
            }

            // Force position
            player.transform.position = targetPos;

            // Wait another frame
            yield return null;

            // Re-enable physics
            if (rb != null)
            {
                rb.isKinematic = false;
            }

            if (cc != null)
            {
                cc.enabled = true;
            }
        }

        /// <summary>
        /// Tries multiple times to find a NavMesh-snapable, unobstructed point within spawnRadius.
        /// Uses Physics.OverlapSphere to ensure no nearby colliders in obstacleMask.
        /// </summary>
        private bool FindValidSpawn(out Vector3 result)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector2 circle = Random.insideUnitCircle * spawnRadius;
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

                NavMeshHit navHit;
                if (
                    NavMesh.SamplePosition(
                        candidate,
                        out navHit,
                        navSampleDistance,
                        NavMesh.AllAreas
                    )
                )
                {
                    Vector3 navPoint = navHit.position + Vector3.up * 0.5f; // Add small offset

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

        // Optional: draw gizmos for debugging spawn area and last sampled points
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, clearRadius);
        }

#if UNITY_EDITOR
        [ContextMenu("Show Current Selected Character")]
        private void ShowSelectedCharacter()
        {
            int index = GetSelectedCharacterIndex();
            if (index >= 0 && index < playerPrefab.Length && playerPrefab[index] != null)
            {
                Debug.Log($"[Spawner] Currently selected character: {playerPrefab[index].name} (index: {index})");
            }
            else
            {
                Debug.Log($"[Spawner] Selected index: {index} (Invalid or out of range)");
            }
        }

        [ContextMenu("Reset to Default Character (0)")]
        private void ResetToDefaultCharacter()
        {
            SetSelectedCharacterIndex(0);
            Debug.Log("[Spawner] Reset to default character (index 0)");
        }
#endif
    }
}