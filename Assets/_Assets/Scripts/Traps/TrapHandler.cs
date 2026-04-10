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
                GameObject trap;

                // Use PhotonNetwork.Instantiate so the trap exists on all clients
                if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
                {
                    trap = PhotonNetwork.Instantiate(
                        trapPrefab.name,
                        Vector3.zero,
                        Quaternion.identity
                    );
                }
                else
                {
                    trap = Instantiate(trapPrefab);
                }

                trap.SetActive(false);

                Trap trapScript = trap.GetComponent<Trap>();
                if (trapScript != null)
                {
                    trapScript.enabled = false;
                    trapScript.SetTrapHandler(this);
                }

                trapPool.Enqueue(trap);
            }
        }

        GameObject GetTrapFromPool()
        {
            // Use pooled object if available
            if (trapPool.Count > 0)
            {
                GameObject pooled = trapPool.Dequeue();
                if (pooled != null)
                {
                    Trap trapScript = pooled.GetComponent<Trap>();
                    if (trapScript != null)
                        trapScript.SetTrapHandler(this);
                    return pooled;
                }
            }

            // Pool exhausted — instantiate a new one
            Debug.LogWarning("[TrapHandler] Pool exhausted, instantiating new trap.");
            GameObject trap;
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                trap = PhotonNetwork.Instantiate(
                    trapPrefab.name,
                    Vector3.zero,
                    Quaternion.identity
                );
            }
            else
            {
                trap = Instantiate(trapPrefab);
            }

            Trap ts = trap.GetComponent<Trap>();
            if (ts != null)
                ts.SetTrapHandler(this);

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
            // Disable rather than destroy so components remain for next activation
            Rigidbody rb = trap.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            MeshCollider meshCol = trap.GetComponent<MeshCollider>();
            if (meshCol != null)
                meshCol.enabled = false;

            Trap trapScript = trap.GetComponent<Trap>();
            if (trapScript != null)
            {
                trapScript.enabled = false;
                trapScript.ResetTrap();
            }

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

            // Enable pre-existing collider and rigidbody from prefab
            // instead of adding them dynamically (dynamic adds don't sync over network)
            MeshCollider meshCol = trap.GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                meshCol.convex = useConvexCollider;
                meshCol.enabled = true;
            }

            Rigidbody rb = trap.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = trapMass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.isKinematic = false;
            }

            Trap trapScript = trap.GetComponent<Trap>();
            if (trapScript != null)
            {
                trapScript.enabled = true;

                if (trapScript.trapType == TrapType.TimedDetonation && rb != null)
                    rb.isKinematic = true;

                trapScript.isPlayingVFX = true;
                trapScript.ActivateTrap(); // Now fires RPC to all clients
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
