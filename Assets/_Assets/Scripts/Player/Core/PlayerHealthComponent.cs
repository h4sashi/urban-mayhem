using UnityEngine;
using Photon.Pun;
using Hanzo.Core.Interfaces;
using Hanzo.Networking;
using Hanzo.Player.Controllers;

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
        [SerializeField] private float maxHealth = 8f; // 8 hits to die
        [SerializeField] private float currentHealth = 8f;
        
        [Header("Respawn Settings")]
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Vector3 respawnPosition = Vector3.zero;
        
        [Header("Damage Settings")]
        [SerializeField] private float dashDamage = 1f; // 1 hit per dash
        [SerializeField] private float explosionDamage = 1f; // 1 hit per explosion
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        
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
        
        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            currentHealth = maxHealth;
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
        public void TakeDamage(float damageAmount, GameObject damageSource = null, DamageType damageType = DamageType.Generic)
        {
            if (!photonView.IsMine) return; // Only process damage on local client
            if (isDead) return; // Can't damage dead players
            
            // Apply damage
            currentHealth -= damageAmount;
            currentHealth = Mathf.Max(0, currentHealth);
            
            Debug.Log($"[PlayerHealth] {photonView.Owner.NickName} took {damageAmount} damage from {damageType}. Health: {currentHealth}/{maxHealth}");
            
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
                
                if (damageType == DamageType.Dash && sourceView != null)
                {
                    // Attacker gets points for dash hit
                    if (scoreManager != null)
                    {
                        scoreManager.AddDashHitScore(sourceView.Owner.ActorNumber);
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
            if (isDead) return;
            
            isDead = true;
            
            Debug.Log($"[PlayerHealth] 💀 {photonView.Owner.NickName} has died!");
            
            OnPlayerDied?.Invoke();
            
            // Disable player controls/rendering
            DisablePlayer();
            
            // Sync death state
            photonView.RPC("RPC_SyncDeathState", RpcTarget.AllBuffered);
            
            // Start respawn countdown
            Invoke(nameof(Respawn), respawnDelay);
        }
        
        /// <summary>
        /// Respawn the player
        /// </summary>
        private void Respawn()
        {
            if (!photonView.IsMine) return;
            
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
            
            Debug.Log($"[PlayerHealth] 🔄 {photonView.Owner.NickName} respawned!");
            
            OnPlayerRespawned?.Invoke();
            
            // Sync respawn
            photonView.RPC("RPC_SyncRespawn", RpcTarget.OthersBuffered, respawnPosition);
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
            Debug.Log($"[PlayerHealth] [Remote] {photonView.Owner.NickName} health synced: {currentHealth}");
        }
        
        [PunRPC]
        private void RPC_SyncDeathState()
        {
            if (photonView.IsMine) return;
            
            isDead = true;
            DisablePlayer();
            Debug.Log($"[PlayerHealth] [Remote] {photonView.Owner.NickName} died");
        }
        
        [PunRPC]
        private void RPC_SyncRespawn(Vector3 position)
        {
            if (photonView.IsMine) return;
            
            isDead = false;
            currentHealth = maxHealth;
            transform.position = position;
            EnablePlayer();
            
            Debug.Log($"[PlayerHealth] [Remote] {photonView.Owner.NickName} respawned");
        }
        
        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine) return;
            
            GUILayout.BeginArea(new Rect(10, 770, 300, 100));
            GUILayout.Label("=== PLAYER HEALTH ===");
            GUILayout.Label($"Health: {currentHealth}/{maxHealth}");
            
            if (scoreManager != null)
            {
                int hitsTaken = scoreManager.GetPlayerHitsTaken(photonView.Owner.ActorNumber);
                GUILayout.Label($"Hits Taken: {hitsTaken}/8");
            }
            
            GUILayout.Label($"Status: {(IsAlive ? "ALIVE" : "DEAD")}");
            GUILayout.EndArea();
        }
    }
}