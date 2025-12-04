using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Traps
{
    public class TrapHandler : MonoBehaviour
    {
        [Header("Trap References")]
        public GameObject trapPrefab;
        public Transform spawnTransform;

        [Header("Spawn Settings")]
        public Vector3 spawnOffset = Vector3.zero;
        public float respawnDelay = 15f;
        public bool autoRespawn = true;

        [Header("Object Pool Settings")]
        public int poolSize = 3;

        [Header("Trap Configuration")]
        public float trapMass = 5f;
        public bool useConvexCollider = true;

        private Queue<GameObject> trapPool = new Queue<GameObject>();
        private GameObject currentTrap;
        private bool isWaitingToRespawn = false;

        void Start()
        {
            InitializePool();
            SpawnTrap();
        }

        void InitializePool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject trap = Instantiate(trapPrefab);
                trap.SetActive(false);

                // Disable Trap component initially
                Trap trapScript = trap.GetComponent<Trap>();
                if (trapScript != null)
                {
                    trapScript.enabled = false;
                    // Register callback for when trap is destroyed
                    trapScript.SetTrapHandler(this);
                }

                trapPool.Enqueue(trap);
            }
        }

        GameObject GetTrapFromPool()
{
    // Use PhotonNetwork.Instantiate instead of regular Instantiate
    GameObject trap = PhotonNetwork.Instantiate(
        trapPrefab.name, // Must match prefab name in Resources folder
        Vector3.zero,
        Quaternion.identity
    );
    
    Trap trapScript = trap.GetComponent<Trap>();
    if (trapScript != null)
    {
        trapScript.SetTrapHandler(this);
    }
    
    return trap;
}
        void ReturnTrapToPool(GameObject trap)
        {
            // Reset trap state
            ResetTrap(trap);
            trap.SetActive(false);
            trapPool.Enqueue(trap);
        }

        void ResetTrap(GameObject trap)
        {
            // Remove dynamically added components
            Rigidbody rb = trap.GetComponent<Rigidbody>();
            if (rb != null)
                Destroy(rb);

            MeshCollider meshCol = trap.GetComponent<MeshCollider>();
            if (meshCol != null)
                Destroy(meshCol);

            // Reset Trap script (this will disable emission)
            Trap trapScript = trap.GetComponent<Trap>();
            if (trapScript != null)
            {
                trapScript.enabled = false;
                trapScript.ResetTrap();
            }

            // Reset transform
            trap.transform.parent = spawnTransform;
            trap.transform.localPosition = Vector3.zero;
            trap.transform.localRotation = Quaternion.identity;
            trap.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        }

        void SpawnTrap()
        {
            if (currentTrap != null)
                return;

            currentTrap = GetTrapFromPool();
            currentTrap.SetActive(true);

            // Position trap at spawn point with offset
            Vector3 spawnPos = spawnTransform.position + spawnOffset;
            currentTrap.transform.position = spawnPos;
            currentTrap.transform.rotation = spawnTransform.rotation;
            currentTrap.transform.parent = spawnTransform;
            currentTrap.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);

            isWaitingToRespawn = false;

            // Auto-activate timed traps immediately
            Trap trapScript = currentTrap.GetComponent<Trap>();
            if (trapScript != null && trapScript.trapType == TrapType.TimedDetonation)
            {
                ActivateTrap(currentTrap);
            }
        }

        public void OnTrapDetonated(GameObject trap)
        {
            if (trap == currentTrap)
            {
                currentTrap = null;

                // Return to pool after destruction animation completes
                StartCoroutine(ReturnTrapAfterDelay(trap, 1f));

                // Start respawn timer
                if (autoRespawn && !isWaitingToRespawn)
                {
                    StartCoroutine(RespawnTrapAfterDelay());
                }
            }
        }

        IEnumerator ReturnTrapAfterDelay(GameObject trap, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnTrapToPool(trap);
        }

        IEnumerator RespawnTrapAfterDelay()
        {
            isWaitingToRespawn = true;
            yield return new WaitForSeconds(respawnDelay);
            SpawnTrap();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && currentTrap != null)
            {
                Trap trapScript = currentTrap.GetComponent<Trap>();

                // Only trigger collision-based traps
                if (trapScript != null && trapScript.trapType == TrapType.CollisionDetonation)
                {
                    ActivateTrap(currentTrap);
                }
            }
        }

        void ActivateTrap(GameObject trap)
        {
            trap.transform.parent = null;

            MeshCollider meshCol = trap.AddComponent<MeshCollider>();
            meshCol.convex = useConvexCollider;

            Rigidbody rb = trap.AddComponent<Rigidbody>();
            rb.mass = trapMass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            Trap trapScript = trap.GetComponent<Trap>();
            if (trapScript != null)
            {
                trapScript.enabled = true;

                // Make kinematic for timed traps during countdown
                if (trapScript.trapType == TrapType.TimedDetonation)
                {
                    rb.isKinematic = true;
                }

                trapScript.isPlayingVFX = true;
                trapScript.ActivateTrap();
            }
        }

        // Manual spawn for testing
        public void ManualSpawn()
        {
            if (currentTrap == null && !isWaitingToRespawn)
            {
                SpawnTrap();
            }
        }

        // Visualize spawn point in editor
        void OnDrawGizmosSelected()
        {
            if (spawnTransform != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(spawnTransform.position + spawnOffset, 0.5f);
                Gizmos.DrawLine(spawnTransform.position, spawnTransform.position + spawnOffset);
            }
        }
    }
}
