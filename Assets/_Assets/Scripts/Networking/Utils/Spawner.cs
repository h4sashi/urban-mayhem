using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

namespace Hanzo.Networking.Utils
{
    public class Spawner : MonoBehaviourPunCallbacks
    {
        [Header("Prefab (must be in Resources/)")]
        public GameObject[] playerPrefab; // put prefab in Assets/Resources/

        [Header("Spawn area")]
        public float spawnRadius = 20f;           // radius around this GameObject to search for spawn positions
        public LayerMask obstacleMask;            // layers considered as obstacles (players, props) when checking overlap
        public float clearRadius = 1.0f;          // how much empty space is required around spawn point
        public int maxAttempts = 30;              // how many random tries before fallback

        [Header("NavMesh")]
        public float navSampleDistance = 2f;      // how far to search to snap to navmesh

        [Header("Options")]
        public bool spawnOnJoinedRoom = true;     // automatically spawn when joining room
        public bool spawnAtTransformIfFail = true; // use this transform position if no valid spot found

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

            if (playerPrefab == null)
            {
                Debug.LogError("[Spawner] playerPrefab is not assigned!");
                return;
            }

            Vector3 spawnPos;
            bool found = FindValidSpawn(out spawnPos);

            if (!found)
            {
                if (spawnAtTransformIfFail)
                {
                    spawnPos = transform.position;
                    Debug.LogWarning("[Spawner] No valid random spawn found. Falling back to spawner transform position.");
                }
                else
                {
                    Debug.LogError("[Spawner] No valid spawn found and fallback disabled. Aborting spawn.");
                    return;
                }
            }

            // Instantiate via Photon (prefab path relative to Resources folder)
            int prefabIndex = Random.Range(0, playerPrefab.Length);
            string resourceName = playerPrefab[prefabIndex].gameObject.name;
            GameObject player = PhotonNetwork.Instantiate(resourceName, spawnPos, Quaternion.identity, 0);
            Debug.Log("[Spawner] Spawned player at " + spawnPos);
        }

        /// <summary>
        /// Tries multiple times to find a NavMesh-snapable, unobstructed point within spawnRadius.
        /// Uses Physics.OverlapSphere to ensure no nearby colliders in obstacleMask.
        /// </summary>
        private bool FindValidSpawn(out Vector3 result)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                // random point in circle on XZ plane
                Vector2 circle = Random.insideUnitCircle * spawnRadius;
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

                // sample navmesh to snap candidate to nav surface (if close enough)
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(candidate, out navHit, navSampleDistance, NavMesh.AllAreas))
                {
                    Vector3 navPoint = navHit.position;

                    // check overlap - ensure no obstacles within clearRadius
                    Collider[] hits = Physics.OverlapSphere(navPoint, clearRadius, obstacleMask);
                    if (hits.Length == 0)
                    {
                        result = navPoint;
                        return true;
                    }
                    // else there's something too close; try again
                }
                // else candidate wasn't on navmesh; try again
            }

            // nothing found
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
    }
}