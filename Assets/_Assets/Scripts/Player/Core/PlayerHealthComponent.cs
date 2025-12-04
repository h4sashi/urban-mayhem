using System.Collections;
using Cinemachine;
using Hanzo.Audio;
using Hanzo.Core.Interfaces;
using Hanzo.Networking;
using Hanzo.Player.Controllers;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hanzo.Player.Core
{
    [RequireComponent(typeof(PhotonView))]
    public class PlayerHealthComponent : MonoBehaviourPun, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField]
        private float maxHealth = 8f;

        [SerializeField]
        private float currentHealth = 8f;

        public Image damageOverlay;
        public float fadeOverlaySpeed = 2f;
        public Color damageColor = Color.red;
        
        [Header("Health UI Settings")]
        public Image healthUIFill;
        public float healthBarSmoothSpeed = 5f; // Speed for smooth health bar transitions
        public Color healthBarHighColor = Color.green; // Color when health is high (75-100%)
        public Color healthBarMidColor = Color.yellow; // Color when health is medium (25-75%)
        public Color healthBarLowColor = Color.red; // Color when health is low (0-25%)
        public bool animateHealthBar = true; // Toggle smooth animation
        public bool colorHealthBar = true; // Toggle color transitions based on health

        [Header("Respawn Settings")]
        [SerializeField]
        private float respawnDelay = 3f;

        [SerializeField]
        private Vector3 respawnPosition = Vector3.zero;


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
        private Coroutine damageOverlayCoroutine;
        private Coroutine healthBarCoroutine; // Track health bar animation
        private float targetHealthFill; // Target fill amount for smooth transitions

        // Offline compatibility
        private bool isOfflineMode = false;
        private int offlineHitsTaken = 0;
        private int offlineDeaths = 0;
        private string playerName = "Player";

        [Header("Audio Settings")]
        public AudioManager audioManager;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            currentHealth = maxHealth;
            targetHealthFill = 1f;

            if (respawnUI != null)
                respawnUI.SetActive(false);

            // Initialize damage overlay to transparent
            if (damageOverlay != null)
            {
                Color transparent = damageColor;
                transparent.a = 0f;
                damageOverlay.color = transparent;
                damageOverlay.enabled = true;
                Debug.Log("[PlayerHealth] Damage overlay initialized (transparent)");
            }
            else
            {
                Debug.LogWarning("[PlayerHealth] ⚠️ Damage overlay Image not assigned!");
            }

            // Initialize health UI
            InitializeHealthUI();

            // Check offline mode
            CheckOfflineMode();

            if (playerVirtualCamera == null && IsLocalPlayer())
            {
                playerVirtualCamera = GetComponentInChildren<CinemachineVirtualCamera>();
                if (playerVirtualCamera == null)
                    Debug.LogWarning("[PlayerHealth] No CinemachineVirtualCamera found!");
            }
        }

        /// <summary>
        /// Initialize the health UI fill bar
        /// </summary>
        private void InitializeHealthUI()
        {
            if (healthUIFill != null)
            {
                healthUIFill.fillAmount = 1f;
                healthUIFill.type = Image.Type.Filled;
                healthUIFill.fillMethod = Image.FillMethod.Horizontal;
                
                if (colorHealthBar)
                {
                    healthUIFill.color = healthBarHighColor;
                }
                
                Debug.Log("[PlayerHealth] Health UI initialized");
            }
            else
            {
                Debug.LogWarning("[PlayerHealth] ⚠️ Health UI Fill Image not assigned!");
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
                    Debug.LogWarning(
                        "[PlayerHealth] NetworkedScoreManager not found (OK if offline)"
                    );
            }

            InitSound();
        }

        private void InitSound()
        {
            if (audioManager.audioSource == null)
            {
                foreach (var t_child in this.GetComponentsInChildren<AudioSource>())
                {
                    if (t_child.gameObject.name == "StunAudioSource")
                    {
                        audioManager.audioSource = t_child;
                        break;
                    }
                }
            }

            // Configure 3D spatial audio with distance-based falloff
            if (audioManager.audioSource != null)
            {
                audioManager.audioSource.playOnAwake = false;
                audioManager.audioSource.spatialBlend = 1f;
                audioManager.audioSource.rolloffMode = audioManager.audioRolloffMode;
                audioManager.audioSource.minDistance = audioManager.audioMinDistance;
                audioManager.audioSource.maxDistance = audioManager.audioMaxDistance;
                audioManager.audioSource.dopplerLevel = 0f;
                Debug.Log(
                    $"[PlayerHealth] AudioSource configured: Min={audioManager.audioMinDistance}m, Max={audioManager.audioMaxDistance}m, Rolloff={audioManager.audioRolloffMode}"
                );
            }
            else
            {
                Debug.LogWarning("[PlayerHealth] AudioSource not found!");
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
            if (isOfflineMode)
                return true;

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
            if (isOfflineMode)
                return -1;

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
            if (isOfflineMode)
                return playerName;

            try
            {
                return photonView?.Owner?.NickName ?? playerName;
            }
            catch
            {
                return playerName;
            }
        }

        /// <summary>
        /// Shows damage overlay only for the local player
        /// </summary>
        private void ShowDamageOverlay()
        {
            if (!IsLocalPlayer())
                return;

            if (damageOverlay == null)
            {
                Debug.LogWarning("[PlayerHealth] Cannot show damage overlay - Image is null!");
                return;
            }

            // Stop existing overlay animation if running
            if (damageOverlayCoroutine != null)
            {
                StopCoroutine(damageOverlayCoroutine);
            }
            
            // Start new overlay animation
            damageOverlayCoroutine = StartCoroutine(FadeDamageOverlay());
        }

        /// <summary>
        /// Shows damage overlay at full intensity, then fades out
        /// </summary>
        IEnumerator FadeDamageOverlay()
        {
            // FLASH: Set overlay to full visibility first
            float maxAlpha = damageColor.a;
            damageOverlay.color = new Color(damageColor.r, damageColor.g, damageColor.b, maxAlpha);
            
            Debug.Log($"[PlayerHealth] 🩸 Damage overlay flashed (alpha: {maxAlpha}) for local player");

            // Small delay at full intensity for impact
            yield return new WaitForSeconds(0.1f);

            // FADE: Gradually fade out
            float alpha = maxAlpha;
            while (alpha > 0f)
            {
                alpha -= Time.deltaTime * fadeOverlaySpeed;
                alpha = Mathf.Max(0f, alpha);
                damageOverlay.color = new Color(damageColor.r, damageColor.g, damageColor.b, alpha);
                yield return null;
            }

            // Ensure fully transparent at end
            damageOverlay.color = new Color(damageColor.r, damageColor.g, damageColor.b, 0f);
            Debug.Log("[PlayerHealth] Damage overlay faded out");
        }

        /// <summary>
        /// Update health bar with smooth animation and color transitions
        /// </summary>
        private void UpdateHealthUI()
        {
            if (healthUIFill == null || !IsLocalPlayer())
                return;

            // Calculate target fill amount
            targetHealthFill = currentHealth / maxHealth;

            // Stop existing animation if running
            if (healthBarCoroutine != null)
            {
                StopCoroutine(healthBarCoroutine);
            }

            if (animateHealthBar)
            {
                // Start smooth animation
                healthBarCoroutine = StartCoroutine(AnimateHealthBar());
            }
            else
            {
                // Instant update
                healthUIFill.fillAmount = targetHealthFill;
                UpdateHealthBarColor();
            }
        }

        /// <summary>
        /// Smoothly animate the health bar fill amount
        /// </summary>
        IEnumerator AnimateHealthBar()
        {
            float currentFill = healthUIFill.fillAmount;

            while (Mathf.Abs(currentFill - targetHealthFill) > 0.001f)
            {
                currentFill = Mathf.Lerp(currentFill, targetHealthFill, Time.deltaTime * healthBarSmoothSpeed);
                healthUIFill.fillAmount = currentFill;
                
                // Update color during animation
                if (colorHealthBar)
                {
                    UpdateHealthBarColor();
                }

                yield return null;
            }

            // Ensure exact target is reached
            healthUIFill.fillAmount = targetHealthFill;
            UpdateHealthBarColor();
        }

        /// <summary>
        /// Update health bar color based on current health percentage
        /// </summary>
        private void UpdateHealthBarColor()
        {
            if (!colorHealthBar || healthUIFill == null)
                return;

            float healthPercent = currentHealth / maxHealth;

            if (healthPercent > 0.75f)
            {
                // High health (75-100%) - Green
                healthUIFill.color = healthBarHighColor;
            }
            else if (healthPercent > 0.25f)
            {
                // Medium health (25-75%) - Gradient from Green to Yellow to Red
                float t = (healthPercent - 0.25f) / 0.5f; // Normalize to 0-1 range
                healthUIFill.color = Color.Lerp(healthBarMidColor, healthBarHighColor, t);
            }
            else
            {
                // Low health (0-25%) - Gradient from Red to Yellow
                float t = healthPercent / 0.25f; // Normalize to 0-1 range
                healthUIFill.color = Color.Lerp(healthBarLowColor, healthBarMidColor, t);
            }
        }

        /// <summary>
        /// Plays hurt sound effect locally and syncs to network
        /// </summary>
        private void PlayHurtSound()
        {
            if (audioManager.audioClip != null && audioManager.audioSource != null)
            {
                audioManager.audioSource.PlayOneShot(audioManager.audioClip, 0.8f);
                Debug.Log($"[PlayerHealth] 🔊 Playing hurt SFX (Local)");
            }

            // Sync sound to other players
            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_PlayHurtSound", RpcTarget.Others);
                }
                catch { }
            }
        }

        private IEnumerator DelayedPlayHurtSound(float delay)
        {
            yield return new WaitForSeconds(delay);
            PlayHurtSound();
        }

        [PunRPC]
        private void RPC_PlayHurtSound()
        {
            // Remote players hear the sound with distance-based attenuation
            if (audioManager.audioClip != null && audioManager.audioSource != null)
            {
                audioManager.audioSource.PlayOneShot(audioManager.audioClip, 0.8f);
                Debug.Log($"[PlayerHealth] 🔊 Playing hurt SFX (Remote)");
            }
        }

        public void TakeDamage(
            float damageAmount,
            GameObject damageSource = null,
            DamageType damageType = DamageType.Generic
        )
        {
            if (!IsLocalPlayer())
                return;
            if (isDead)
                return;

            // Apply damage FIRST
            currentHealth -= damageAmount;
            currentHealth = Mathf.Max(0, currentHealth);

            Debug.Log(
                $"[PlayerHealth] {GetPlayerName()} took {damageAmount} damage from {damageType}. Health: {currentHealth}/{maxHealth}"
            );

            // Update health UI with smooth animation and color change
            UpdateHealthUI();

            // Show damage overlay and play sound when damage is taken
            if (damageAmount > 0)
            {
                ShowDamageOverlay();
                StartCoroutine(DelayedPlayHurtSound(0.95f));
            }

            // Only increment hit counter for Dash damage
            int hitsTaken = 0;
            if (damageType == DamageType.Dash)
            {
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
            }
            else
            {
                hitsTaken = isOfflineMode
                    ? offlineHitsTaken
                    : (
                        scoreManager != null ? scoreManager.GetPlayerHitsTaken(GetActorNumber()) : 0
                    );
            }

            // Handle score changes based on damage type (online only)
            if (!isOfflineMode && damageSource != null && scoreManager != null)
            {
                PhotonView sourceView = damageSource.GetComponent<PhotonView>();

                if (
                    sourceView != null
                    && sourceView.Owner != null
                    && photonView.Owner != null
                    && sourceView.Owner != photonView.Owner
                )
                {
                    if (damageType == DamageType.Dash)
                    {
                        scoreManager.AddDashHitScore(sourceView.Owner.ActorNumber);
                        Debug.Log(
                            $"[PlayerHealth] 🎯 Score awarded to {sourceView.Owner.NickName}"
                        );
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
                try
                {
                    photonView.RPC("RPC_SyncHealthUpdate", RpcTarget.OthersBuffered, currentHealth);
                }
                catch { }
            }

            // Check for death based on health first, then hit count as backup
            if (currentHealth <= 0)
            {
                Debug.Log($"[PlayerHealth] ☠️ {GetPlayerName()} died from health depletion!");
                Die();
            }
            else if (damageType == DamageType.Dash && hitsTaken >= 8)
            {
                Debug.Log($"[PlayerHealth] ☠️ {GetPlayerName()} died from 8 dash hits!");
                Die();
            }
        }

        private void Die()
        {
            if (isDead)
                return;
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
                try
                {
                    photonView.RPC("RPC_SyncDeathState", RpcTarget.AllBuffered);
                }
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
            if (!IsLocalPlayer())
                return;

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

            // Reset health UI to full with animation
            targetHealthFill = 1f;
            if (healthUIFill != null)
            {
                if (animateHealthBar)
                {
                    if (healthBarCoroutine != null)
                        StopCoroutine(healthBarCoroutine);
                    healthBarCoroutine = StartCoroutine(AnimateHealthBar());
                }
                else
                {
                    healthUIFill.fillAmount = 1f;
                    UpdateHealthBarColor();
                }
            }

            // Clear damage overlay
            if (damageOverlay != null)
            {
                Color transparent = damageColor;
                transparent.a = 0f;
                damageOverlay.color = transparent;
            }

            Debug.Log($"[PlayerHealth] 🔄 {GetPlayerName()} respawned!");

            OnPlayerRespawned?.Invoke();

            // Sync respawn (online only)
            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_SyncRespawn", RpcTarget.OthersBuffered, respawnPosition);
                }
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
            if (IsLocalPlayer())
                return;

            isDead = true;
            DisablePlayer();
            Debug.Log($"[PlayerHealth] [Remote] {GetPlayerName()} died");
        }

        [PunRPC]
        private void RPC_SyncRespawn(Vector3 position)
        {
            if (IsLocalPlayer())
                return;

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
            
            if (damageOverlayCoroutine != null)
                StopCoroutine(damageOverlayCoroutine);

            if (healthBarCoroutine != null)
                StopCoroutine(healthBarCoroutine);
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !IsLocalPlayer())
                return;

            GUILayout.BeginArea(new Rect(10, 770, 300, 140));
            GUILayout.Label("=== PLAYER HEALTH ===");
            GUILayout.Label($"Mode: {(isOfflineMode ? "OFFLINE" : "ONLINE")}");
            GUILayout.Label($"Health: {currentHealth}/{maxHealth}");

            int hitsTaken = isOfflineMode
                ? offlineHitsTaken
                : (scoreManager != null ? scoreManager.GetPlayerHitsTaken(GetActorNumber()) : 0);
            int deaths = isOfflineMode
                ? offlineDeaths
                : (scoreManager != null ? scoreManager.GetPlayerDeaths(GetActorNumber()) : 0);

            GUILayout.Label($"Hits Taken: {hitsTaken}/8");
            GUILayout.Label($"Deaths: {deaths}");
            GUILayout.Label($"Status: {(IsAlive ? "ALIVE" : "DEAD")}");
            GUILayout.EndArea();
        }
    }
}