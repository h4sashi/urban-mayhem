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
    [RequireComponent(typeof(PhotonView))]
    public class PlayerHealthComponent : MonoBehaviourPun, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 8f;
        [SerializeField] private float currentHealth = 8f;

        [Header("Respawn Settings")]
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Vector3 respawnPosition = Vector3.zero;

        [Header("Damage Settings")]
        [SerializeField] private float dashDamage = 1f;
        [SerializeField] private float explosionDamage = 1f;

        [Header("Camera Settings")]
        [SerializeField] private CinemachineVirtualCamera playerVirtualCamera;

        [Header("Spawn UI")]
        public GameObject respawnUI;
        public TextMeshProUGUI respawnCountdownText;
        public GameObject mobielHUDCanvas;

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
        private Coroutine respawnCoroutine;
        
        // Offline compatibility
        private bool isOfflineMode = false;
        private int offlineHitsTaken = 0;
        private int offlineDeaths = 0;
        private string playerName = "Player";

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            currentHealth = maxHealth;

            if (respawnUI != null)
                respawnUI.SetActive(false);

            // Check offline mode
            CheckOfflineMode();

            if (playerVirtualCamera == null && IsLocalPlayer())
            {
                playerVirtualCamera = GetComponentInChildren<CinemachineVirtualCamera>();
                if (playerVirtualCamera == null)
                    Debug.LogWarning("[PlayerHealth] No CinemachineVirtualCamera found!");
            }
        }

        private void Start()
        {
            CheckOfflineMode();
            
            // Only get score manager if online
            if (!isOfflineMode)
            {
                scoreManager = NetworkedScoreManager.Instance;
                if (scoreManager == null)
                    Debug.LogWarning("[PlayerHealth] NetworkedScoreManager not found (OK if offline)");
            }
        }

        /// <summary>
        /// Check if running in offline mode
        /// </summary>
        private void CheckOfflineMode()
        {
            try
            {
                isOfflineMode = !PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode;
                
                if (!isOfflineMode && photonView != null && photonView.Owner != null)
                    playerName = photonView.Owner.NickName;
                else
                    playerName = "Player (Offline)";
            }
            catch
            {
                isOfflineMode = true;
                playerName = "Player (Offline)";
            }
            
            if (isOfflineMode)
                Debug.Log("[PlayerHealth] Running in OFFLINE mode");
        }

        /// <summary>
        /// Safe check for local player (works offline)
        /// </summary>
        private bool IsLocalPlayer()
        {
            if (isOfflineMode) return true;
            
            try
            {
                return photonView != null && photonView.IsMine;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Get actor number safely (returns -1 for offline)
        /// </summary>
        private int GetActorNumber()
        {
            if (isOfflineMode) return -1;
            
            try
            {
                return photonView?.Owner?.ActorNumber ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Get player name safely
        /// </summary>
        private string GetPlayerName()
        {
            if (isOfflineMode) return playerName;
            
            try
            {
                return photonView?.Owner?.NickName ?? playerName;
            }
            catch
            {
                return playerName;
            }
        }

        public void TakeDamage(float damageAmount, GameObject damageSource = null, 
            DamageType damageType = DamageType.Generic)
        {
            if (!IsLocalPlayer()) return;
            if (isDead) return;

            // Apply damage
            currentHealth -= damageAmount;
            currentHealth = Mathf.Max(0, currentHealth);

            Debug.Log($"[PlayerHealth] {GetPlayerName()} took {damageAmount} damage from {damageType}. Health: {currentHealth}/{maxHealth}");

            // Handle hit counter
            int hitsTaken = 0;
            if (isOfflineMode)
            {
                offlineHitsTaken++;
                hitsTaken = offlineHitsTaken;
            }
            else if (scoreManager != null)
            {
                int actorNum = GetActorNumber();
                if (actorNum >= 0)
                    hitsTaken = scoreManager.IncrementPlayerHits(actorNum);
            }

            // Handle score changes based on damage type (online only)
            if (!isOfflineMode && damageSource != null && scoreManager != null)
            {
                PhotonView sourceView = damageSource.GetComponent<PhotonView>();

                if (sourceView != null && sourceView.Owner != null && 
                    photonView.Owner != null && sourceView.Owner != photonView.Owner)
                {
                    if (damageType == DamageType.Dash)
                    {
                        scoreManager.AddDashHitScore(sourceView.Owner.ActorNumber);
                        Debug.Log($"[PlayerHealth] 🎯 Score awarded to {sourceView.Owner.NickName}");
                    }
                    else if (damageType == DamageType.Explosion)
                    {
                        int actorNum = GetActorNumber();
                        if (actorNum >= 0)
                        {
                            scoreManager.RemoveExplosionScore(actorNum);
                            Debug.Log($"[PlayerHealth] 💣 Score deducted from {GetPlayerName()}");
                        }
                    }
                }
            }

            // Trigger damage event
            OnDamageTaken?.Invoke(damageAmount, damageSource, damageType);

            // Sync damage to other clients (online only)
            if (!isOfflineMode && photonView != null)
            {
                try { photonView.RPC("RPC_SyncHealthUpdate", RpcTarget.OthersBuffered, currentHealth); }
                catch { }
            }

            // Check for death
            if (currentHealth <= 0 || hitsTaken >= 8)
            {
                Die();
            }
        }

        private void Die()
        {
            if (isDead) return;
            isDead = true;

            Debug.Log($"[PlayerHealth] 💀 {GetPlayerName()} has died!");

            // Increment death counter
            if (isOfflineMode)
            {
                offlineDeaths++;
            }
            else if (scoreManager != null)
            {
                int actorNum = GetActorNumber();
                if (actorNum >= 0)
                    scoreManager.IncrementPlayerDeaths(actorNum);
            }

            OnPlayerDied?.Invoke();
            DisablePlayer();

            if (IsLocalPlayer())
                DisableCamera();

            // Sync death state (online only)
            if (!isOfflineMode && photonView != null)
            {
                try { photonView.RPC("RPC_SyncDeathState", RpcTarget.AllBuffered); }
                catch { }
            }

            // Start respawn countdown
            if (IsLocalPlayer())
            {
                if (respawnCoroutine != null)
                    StopCoroutine(respawnCoroutine);
                respawnCoroutine = StartCoroutine(RespawnCountdown());
            }
        }

        private IEnumerator RespawnCountdown()
        {
            if (respawnUI != null)
            {
                respawnUI.SetActive(true);
                if (mobielHUDCanvas != null)
                    mobielHUDCanvas.SetActive(false);
            }

            float timeRemaining = respawnDelay;

            while (timeRemaining > 0)
            {
                if (respawnCountdownText != null)
                    respawnCountdownText.text = $"{Mathf.CeilToInt(timeRemaining)}";

                yield return null;
                timeRemaining -= Time.deltaTime;
            }

            yield return new WaitForSeconds(0.1f);

            if (respawnUI != null)
            {
                respawnUI.SetActive(false);
                if (mobielHUDCanvas != null)
                    mobielHUDCanvas.SetActive(true);
            }

            Respawn();
        }

        private void Respawn()
        {
            if (!IsLocalPlayer()) return;

            currentHealth = maxHealth;
            isDead = false;

            // Reset hit counter
            if (isOfflineMode)
            {
                offlineHitsTaken = 0;
            }
            else if (scoreManager != null)
            {
                int actorNum = GetActorNumber();
                if (actorNum >= 0)
                    scoreManager.ResetPlayerHits(actorNum);
            }

            transform.position = respawnPosition;
            EnablePlayer();
            EnableCamera();

            Debug.Log($"[PlayerHealth] 🔄 {GetPlayerName()} respawned!");

            OnPlayerRespawned?.Invoke();

            // Sync respawn (online only)
            if (!isOfflineMode && photonView != null)
            {
                try { photonView.RPC("RPC_SyncRespawn", RpcTarget.OthersBuffered, respawnPosition); }
                catch { }
            }
        }

        private void DisableCamera()
        {
            if (playerVirtualCamera != null)
            {
                playerVirtualCamera.enabled = false;
                Debug.Log("[PlayerHealth] 📷 Camera disabled");
            }
        }

        private void EnableCamera()
        {
            if (playerVirtualCamera != null)
            {
                playerVirtualCamera.enabled = true;
                Debug.Log("[PlayerHealth] 📷 Camera enabled");
            }
        }

        private void DisablePlayer()
        {
            var movementController = GetComponent<PlayerMovementController>();
            if (movementController != null)
                movementController.enabled = false;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            foreach (var rend in GetComponentsInChildren<Renderer>())
                rend.enabled = false;
        }

        private void EnablePlayer()
        {
            var movementController = GetComponent<PlayerMovementController>();
            if (movementController != null)
                movementController.enabled = true;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = true;

            foreach (var rend in GetComponentsInChildren<Renderer>())
                rend.enabled = true;
        }

        public void SetRespawnPosition(Vector3 position)
        {
            respawnPosition = position;
        }

        // ========== PHOTON RPCs ==========

        [PunRPC]
        private void RPC_SyncHealthUpdate(float newHealth)
        {
            currentHealth = newHealth;
            Debug.Log($"[PlayerHealth] [Remote] {GetPlayerName()} health synced: {currentHealth}");
        }

        [PunRPC]
        private void RPC_SyncDeathState()
        {
            if (IsLocalPlayer()) return;

            isDead = true;
            DisablePlayer();
            Debug.Log($"[PlayerHealth] [Remote] {GetPlayerName()} died");
        }

        [PunRPC]
        private void RPC_SyncRespawn(Vector3 position)
        {
            if (IsLocalPlayer()) return;

            isDead = false;
            currentHealth = maxHealth;
            transform.position = position;
            EnablePlayer();

            Debug.Log($"[PlayerHealth] [Remote] {GetPlayerName()} respawned");
        }

        private void OnDestroy()
        {
            if (respawnCoroutine != null)
                StopCoroutine(respawnCoroutine);
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !IsLocalPlayer()) return;

            GUILayout.BeginArea(new Rect(10, 770, 300, 140));
            GUILayout.Label("=== PLAYER HEALTH ===");
            GUILayout.Label($"Mode: {(isOfflineMode ? "OFFLINE" : "ONLINE")}");
            GUILayout.Label($"Health: {currentHealth}/{maxHealth}");

            int hitsTaken = isOfflineMode ? offlineHitsTaken : 
                (scoreManager != null ? scoreManager.GetPlayerHitsTaken(GetActorNumber()) : 0);
            int deaths = isOfflineMode ? offlineDeaths : 
                (scoreManager != null ? scoreManager.GetPlayerDeaths(GetActorNumber()) : 0);

            GUILayout.Label($"Hits Taken: {hitsTaken}/8");
            GUILayout.Label($"Deaths: {deaths}");
            GUILayout.Label($"Status: {(IsAlive ? "ALIVE" : "DEAD")}");
            GUILayout.EndArea();
        }
    }
}