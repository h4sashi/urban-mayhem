using Hanzo.Core.Interfaces;
using Hanzo.Networking;
using Hanzo.Player.Controllers;
using Photon.Pun;
using UnityEngine;
using TMPro;
using System.Collections;
using Cinemachine;

namespace Hanzo.Player.Core
{
    /// <summary>
    /// Handles player health, damage receiving, and respawning
    /// Implements IDamageable interface
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class PlayerHealthComponent : MonoBehaviourPun, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField]
        private float maxHealth = 8f; // 8 hits to die

        [SerializeField]
        private float currentHealth = 8f;

        [Header("Respawn Settings")]
        [SerializeField]
        private float respawnDelay = 3f;

        [SerializeField]
        private Vector3 respawnPosition = Vector3.zero;

        [Header("Damage Settings")]
        [SerializeField]
        private float dashDamage = 1f; // 1 hit per dash

        [SerializeField]
        private float explosionDamage = 1f; // 1 hit per explosion

        [Header("Camera Settings")]
        [SerializeField]
        private CinemachineVirtualCamera playerVirtualCamera;

        [Header("Spawn UI")]
        public GameObject respawnUI;
        public TextMeshProUGUI respawnCountdownText;
        public GameObject mobielHUDCanvas;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = true;

        // Events
        public event System.Action<float, GameObject, DamageType> OnDamageTaken;
        public event System.Action OnPlayerDied;
        public event System.Action OnPlayerRespawned;

        // Properties
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsAlive => currentHealth > 0;

        private PhotonView photonView;
        private NetworkedScoreManager scoreManager;
        private bool isDead = false;
        private Coroutine respawnCoroutine;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            currentHealth = maxHealth;

            // Hide respawn UI initially
            if (respawnUI != null)
            {
                respawnUI.SetActive(false);
            }

            // Find the virtual camera if not assigned
            if (playerVirtualCamera == null && photonView.IsMine)
            {
                playerVirtualCamera = GetComponentInChildren<CinemachineVirtualCamera>();
                
                if (playerVirtualCamera == null)
                {
                    Debug.LogWarning("[PlayerHealth] No CinemachineVirtualCamera found on player!");
                }
            }
        }

        private void Start()
        {
            scoreManager = NetworkedScoreManager.Instance;

            if (scoreManager == null)
            {
                Debug.LogError("[PlayerHealth] NetworkedScoreManager not found in scene!");
            }
        }

        /// <summary>
        /// Take damage from any source
        /// </summary>
        public void TakeDamage(
            float damageAmount,
            GameObject damageSource = null,
            DamageType damageType = DamageType.Generic
        )
        {
            if (!photonView.IsMine)
                return; // Only process damage on local client
            if (isDead)
                return; // Can't damage dead players

            // Apply damage
            currentHealth -= damageAmount;
            currentHealth = Mathf.Max(0, currentHealth);

            Debug.Log(
                $"[PlayerHealth] {photonView.Owner.NickName} took {damageAmount} damage from {damageType}. Health: {currentHealth}/{maxHealth}"
            );

            // Increment hit counter and get new count
            int hitsTaken = 0;
            if (scoreManager != null)
            {
                hitsTaken = scoreManager.IncrementPlayerHits(photonView.Owner.ActorNumber);
            }

            // Handle score changes based on damage type
            if (damageSource != null)
            {
                PhotonView sourceView = damageSource.GetComponent<PhotonView>();

                // Only give points if the damage source is another player (not self)
                if (sourceView != null && sourceView.Owner != photonView.Owner)
                {
                    if (damageType == DamageType.Dash)
                    {
                        // Attacker gets points for dash hit
                        if (scoreManager != null)
                        {
                            scoreManager.AddDashHitScore(sourceView.Owner.ActorNumber);
                            Debug.Log(
                                $"[PlayerHealth] 🎯 Score awarded to {sourceView.Owner.NickName} for dash hit"
                            );
                        }
                    }
                    else if (damageType == DamageType.Explosion)
                    {
                        // Remove points from victim for explosion hit
                        if (scoreManager != null)
                        {
                            scoreManager.RemoveExplosionScore(photonView.Owner.ActorNumber);
                            Debug.Log(
                                $"[PlayerHealth] 💣 Score deducted from {photonView.Owner.NickName} for explosion hit"
                            );
                        }
                    }
                }
            }

            // Trigger damage event
            OnDamageTaken?.Invoke(damageAmount, damageSource, damageType);

            // Sync damage to other clients
            photonView.RPC("RPC_SyncHealthUpdate", RpcTarget.OthersBuffered, currentHealth);

            // Check for death (8 hits)
            if (currentHealth <= 0 || hitsTaken >= 8)
            {
                Die();
            }
        }

        /// <summary>
        /// Handle player death
        /// </summary>
        private void Die()
        {
            if (isDead)
                return;

            isDead = true;

            Debug.Log($"[PlayerHealth] 💀 {photonView.Owner.NickName} has died!");

            // INCREMENT DEATH COUNTER
            if (scoreManager != null)
            {
                scoreManager.IncrementPlayerDeaths(photonView.Owner.ActorNumber);
            }

            OnPlayerDied?.Invoke();

            // Disable player controls/rendering
            DisablePlayer();

            // Disable Cinemachine camera for local player
            if (photonView.IsMine)
            {
                DisableCamera();
            }

            // Sync death state
            photonView.RPC("RPC_SyncDeathState", RpcTarget.AllBuffered);

            // Start respawn countdown with UI
            if (photonView.IsMine)
            {
                if (respawnCoroutine != null)
                {
                    StopCoroutine(respawnCoroutine);
                }
                respawnCoroutine = StartCoroutine(RespawnCountdown());
            }
        }

        /// <summary>
        /// Coroutine to handle respawn countdown with UI updates
        /// </summary>
        private IEnumerator RespawnCountdown()
        {
            // Show respawn UI
            if (respawnUI != null)
            {
                respawnUI.SetActive(true);
                mobielHUDCanvas.SetActive(false);
            }

            float timeRemaining = respawnDelay;

            // Update countdown every frame
            while (timeRemaining > 0)
            {
                // Update countdown text
                if (respawnCountdownText != null)
                {
                    respawnCountdownText.text = $"{Mathf.CeilToInt(timeRemaining)}";
                }

                yield return null;
                timeRemaining -= Time.deltaTime;
            }

            // Wait a brief moment before respawning
            yield return new WaitForSeconds(0.1f);

            // Hide respawn UI
            if (respawnUI != null)
            {
                respawnUI.SetActive(false);
                mobielHUDCanvas.SetActive(true);
            }

            // Respawn the player
            Respawn();
        }

        /// <summary>
        /// Respawn the player
        /// </summary>
        private void Respawn()
        {
            if (!photonView.IsMine)
                return;

            // Reset health
            currentHealth = maxHealth;
            isDead = false;

            // Reset hit counter
            if (scoreManager != null)
            {
                scoreManager.ResetPlayerHits(photonView.Owner.ActorNumber);
            }

            // Teleport to respawn point
            transform.position = respawnPosition;

            // Re-enable player
            EnablePlayer();

            // Re-enable Cinemachine camera
            EnableCamera();

            Debug.Log($"[PlayerHealth] 🔄 {photonView.Owner.NickName} respawned!");

            OnPlayerRespawned?.Invoke();

            // Sync respawn
            photonView.RPC("RPC_SyncRespawn", RpcTarget.OthersBuffered, respawnPosition);
        }

        /// <summary>
        /// Disable the Cinemachine virtual camera
        /// </summary>
        private void DisableCamera()
        {
            if (playerVirtualCamera != null)
            {
                playerVirtualCamera.enabled = false;
                Debug.Log("[PlayerHealth] 📷 Virtual camera disabled");
            }
        }

        /// <summary>
        /// Enable the Cinemachine virtual camera
        /// </summary>
        private void EnableCamera()
        {
            if (playerVirtualCamera != null)
            {
                playerVirtualCamera.enabled = true;
                Debug.Log("[PlayerHealth] 📷 Virtual camera enabled");
            }
        }

        /// <summary>
        /// Disable player during death
        /// </summary>
        private void DisablePlayer()
        {
            // Disable movement
            var movementController = GetComponent<PlayerMovementController>();
            if (movementController != null)
            {
                movementController.enabled = false;
            }

            // Disable collision
            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            // Optional: Hide player model
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = false;
            }
        }

        /// <summary>
        /// Re-enable player on respawn
        /// </summary>
        private void EnablePlayer()
        {
            // Enable movement
            var movementController = GetComponent<PlayerMovementController>();
            if (movementController != null)
            {
                movementController.enabled = true;
            }

            // Enable collision
            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = true;
            }

            // Show player model
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
            }
        }

        /// <summary>
        /// Set respawn position (can be called by spawn points)
        /// </summary>
        public void SetRespawnPosition(Vector3 position)
        {
            respawnPosition = position;
        }

        // ========== PHOTON RPCs ==========

        [PunRPC]
        private void RPC_SyncHealthUpdate(float newHealth)
        {
            currentHealth = newHealth;
            Debug.Log(
                $"[PlayerHealth] [Remote] {photonView.Owner.NickName} health synced: {currentHealth}"
            );
        }

        [PunRPC]
        private void RPC_SyncDeathState()
        {
            if (photonView.IsMine)
                return;

            isDead = true;
            DisablePlayer();
            Debug.Log($"[PlayerHealth] [Remote] {photonView.Owner.NickName} died");
        }

        [PunRPC]
        private void RPC_SyncRespawn(Vector3 position)
        {
            if (photonView.IsMine)
                return;

            isDead = false;
            currentHealth = maxHealth;
            transform.position = position;
            EnablePlayer();

            Debug.Log($"[PlayerHealth] [Remote] {photonView.Owner.NickName} respawned");
        }

        private void OnDestroy()
        {
            // Clean up coroutine if object is destroyed
            if (respawnCoroutine != null)
            {
                StopCoroutine(respawnCoroutine);
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine)
                return;

            GUILayout.BeginArea(new Rect(10, 770, 300, 120));
            GUILayout.Label("=== PLAYER HEALTH ===");
            GUILayout.Label($"Health: {currentHealth}/{maxHealth}");

            if (scoreManager != null)
            {
                int hitsTaken = scoreManager.GetPlayerHitsTaken(photonView.Owner.ActorNumber);
                int deaths = scoreManager.GetPlayerDeaths(photonView.Owner.ActorNumber);
                GUILayout.Label($"Hits Taken: {hitsTaken}/8");
                GUILayout.Label($"Deaths: {deaths}");
            }

            GUILayout.Label($"Status: {(IsAlive ? "ALIVE" : "DEAD")}");
            GUILayout.EndArea();
        }
    }
}