using UnityEngine;
using Photon.Pun;
using Hanzo.Player.Abilities;
using Hanzo.Player.Core;

namespace Hanzo.Player.Controllers
{
    /// <summary>
    /// Handles collision detection during dash
    /// Applies knockback to both players and destructible objects
    /// IMPROVED VERSION with better debugging and reliability
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class DashCollisionHandler : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private AbilitySettings abilitySettings;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private LayerMask destructibleLayer;
        [SerializeField] private float stunDuration = 2f;
        
        [Header("Detection")]
        [SerializeField] private float detectionRadius = 1.5f;
        [SerializeField] private Vector3 detectionOffset = new Vector3(0, 0.5f, 0.5f);
        
        [Header("Destructible Settings")]
        [Tooltip("Force multiplier applied to destructible objects")]
        [SerializeField] private float destructibleForceMultiplier = 1.5f;
        [Tooltip("Upward force component for destructibles (0-1)")]
        [SerializeField] private float destructibleUpwardForce = 0.6f;
        
        [Header("Effects")]
        [SerializeField] private GameObject hitVFXPrefab;
        [SerializeField] private GameObject destructibleHitVFXPrefab;
        [SerializeField] private AudioClip hitSound;
        [SerializeField] private AudioClip destructibleHitSound;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private bool verboseLogging = true;
        
        private PhotonView photonView;
        private PlayerAbilityController abilityController;

        public PlayerMovementController playerMovementController;
        private Rigidbody rb;
        private AudioSource audioSource;
        
        // Cooldown to prevent multiple hits in one dash
        private float lastPlayerHitTime = 0f;
        private float lastDestructibleHitTime = 0f;
        private const float HIT_COOLDOWN = 0.2f;
        
        // Debug info
        private int framesSinceDashActive = 0;
        private Vector3 lastDetectionPos;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            abilityController = GetComponent<PlayerAbilityController>();
            rb = GetComponent<Rigidbody>();
            
            // Setup audio source
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // 3D sound
            
            // CRITICAL: Verify components
            if (abilityController == null)
            {
                Debug.LogError($"[{name}] DashCollisionHandler: PlayerAbilityController NOT FOUND!");
            }
            else if (playerMovementController.DashAbility == null)
            {
                Debug.LogError($"[{name}] DashCollisionHandler: DashAbility is NULL!");
            }
            else
            {
                Debug.Log($"[{name}] ✅ DashCollisionHandler initialized successfully");
            }
            
            // DETAILED LAYER DIAGNOSTICS
            DiagnoseLayerConfiguration();
        }
        
        /// <summary>
        /// Comprehensive layer configuration diagnostics
        /// </summary>
        private void DiagnoseLayerConfiguration()
        {
            Debug.Log($"[{name}] ========== LAYER CONFIGURATION ==========");
            
            // Check this GameObject's layer
            Debug.Log($"[{name}] This GameObject Layer: {gameObject.layer} ({LayerMask.LayerToName(gameObject.layer)})");
            
            // Check player layer mask
            Debug.Log($"[{name}] Player Layer Mask Value: {playerLayer.value}");
            if (playerLayer == 0)
            {
                Debug.LogError($"[{name}] ⚠️ PlayerLayer is NOT SET! Collisions will not work!");
            }
            else
            {
                // Show which layers are included in the mask
                for (int i = 0; i < 32; i++)
                {
                    if ((playerLayer.value & (1 << i)) != 0)
                    {
                        string layerName = LayerMask.LayerToName(i);
                        Debug.Log($"[{name}]   → Player mask includes Layer {i}: '{layerName}' (Mask value: {1 << i})");
                    }
                }
            }
            
            // Check destructible layer mask
            Debug.Log($"[{name}] Destructible Layer Mask Value: {destructibleLayer.value}");
            if (destructibleLayer == 0)
            {
                Debug.LogWarning($"[{name}] ⚠️ DestructibleLayer is NOT SET!");
            }
            else
            {
                for (int i = 0; i < 32; i++)
                {
                    if ((destructibleLayer.value & (1 << i)) != 0)
                    {
                        string layerName = LayerMask.LayerToName(i);
                        Debug.Log($"[{name}]   → Destructible mask includes Layer {i}: '{layerName}' (Mask value: {1 << i})");
                    }
                }
            }
            
            // Test detection in a 10 unit radius
            Debug.Log($"[{name}] Scanning for nearby objects...");
            Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, 10f);
            Debug.Log($"[{name}] Found {nearbyColliders.Length} total colliders nearby");
            
            int playerCount = 0;
            int destructibleCount = 0;
            
            foreach (var col in nearbyColliders)
            {
                int objLayer = col.gameObject.layer;
                string objLayerName = LayerMask.LayerToName(objLayer);
                
                // Check if this object would be detected by player mask
                if ((playerLayer.value & (1 << objLayer)) != 0)
                {
                    playerCount++;
                    Debug.Log($"[{name}]   ✅ WOULD DETECT (Player): {col.name} | Layer {objLayer} ({objLayerName})");
                }
                
                // Check if this object would be detected by destructible mask
                if ((destructibleLayer.value & (1 << objLayer)) != 0)
                {
                    destructibleCount++;
                    Debug.Log($"[{name}]   ✅ WOULD DETECT (Destructible): {col.name} | Layer {objLayer} ({objLayerName})");
                }
            }
            
            Debug.Log($"[{name}] Summary: {playerCount} detectable players, {destructibleCount} detectable destructibles");
            
            if (playerCount == 0 && nearbyColliders.Length > 0)
            {
                Debug.LogError($"[{name}] ❌ NO PLAYERS DETECTABLE! Check that player objects are on the correct layer!");
            }
            
            Debug.Log($"[{name}] ==========================================");
        }

        private void FixedUpdate()
        {
            if (!photonView.IsMine) return;
            
            // Check if dash is active
            bool isDashing = playerMovementController != null && 
                           playerMovementController.DashAbility != null && 
                           playerMovementController.DashAbility.IsActive;
            
            if (isDashing)
            {
                framesSinceDashActive++;
                
                if (verboseLogging && framesSinceDashActive % 5 == 0) // Log every 5th frame to reduce spam
                {
                    Debug.Log($"[{name}] 🏃 Dash Active (Frame {framesSinceDashActive})");
                }
                
                CheckForPlayerCollisions();
                CheckForDestructibleCollisions();
            }
            else
            {
                if (framesSinceDashActive > 0)
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"[{name}] ⏹️ Dash ended after {framesSinceDashActive} frames");
                    }
                    framesSinceDashActive = 0;
                }
            }
        }

        private void CheckForPlayerCollisions()
        {
            // Cooldown check
            if (Time.time - lastPlayerHitTime < HIT_COOLDOWN)
                return;
            
            // Calculate detection position
            Vector3 detectionPos = transform.position + transform.TransformDirection(detectionOffset);
            lastDetectionPos = detectionPos;
            
            // Perform overlap sphere
            Collider[] hitColliders = Physics.OverlapSphere(detectionPos, detectionRadius, playerLayer);
            
            // ALWAYS log when checking, even if nothing found (for debugging)
            if (verboseLogging)
            {
                if (hitColliders.Length > 0)
                {
                    Debug.Log($"[{name}] 🎯 Player collision check: Found {hitColliders.Length} colliders");
                }
                else
                {
                    // Log every 10 frames to avoid spam
                    if (framesSinceDashActive % 10 == 0)
                    {
                        Debug.Log($"[{name}] 🔍 Player collision check: No colliders found at {detectionPos}");
                    }
                }
            }
            
            foreach (var hitCollider in hitColliders)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[{name}] → Checking collider: {hitCollider.name} (Layer: {LayerMask.LayerToName(hitCollider.gameObject.layer)})");
                }
                
                // Skip self
                if (hitCollider.transform.root == transform.root)
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"[{name}] ⏭️ Skipping self");
                    }
                    continue;
                }
                
                // Get hit player's PhotonView
                PhotonView targetPhotonView = hitCollider.GetComponentInParent<PhotonView>();
                if (targetPhotonView == null || targetPhotonView == photonView)
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning($"[{name}] ⚠️ No valid PhotonView found on {hitCollider.name}");
                    }
                    continue;
                }
                
                // Get hit player's state controller
                PlayerStateController targetState = hitCollider.GetComponentInParent<PlayerStateController>();
                if (targetState == null)
                {
                    Debug.LogWarning($"[{name}] Player {hitCollider.name} hit but has no PlayerStateController!");
                    continue;
                }
                
                // Don't hit already stunned players
                if (targetState.IsStunned)
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"[{name}] ⏭️ Target {targetPhotonView.Owner.NickName} already stunned");
                    }
                    continue;
                }
                
                // Calculate knockback direction
                Vector3 knockbackDir = (hitCollider.transform.position - transform.position).normalized;
                
                // Apply knockback via RPC
                float knockbackForce = abilitySettings.KnockbackForce;
                
                // Stack bonus: higher stacks = more knockback
                if (playerMovementController.DashAbility.StackLevel >= 2)
                {
                    knockbackForce *= 1.3f;
                }
                if (playerMovementController.DashAbility.StackLevel >= 3)
                {
                    knockbackForce *= 1.5f;
                }
                
                Debug.Log($"[{name}] 💥 HIT PLAYER {targetPhotonView.Owner.NickName}! Knockback: {knockbackForce}");
                
                // IMPORTANT: Call the RPC on the VICTIM's PhotonView
                targetPhotonView.RPC("RPC_ReceiveKnockback", RpcTarget.All, 
                    knockbackDir, knockbackForce, stunDuration, photonView.ViewID);
                
                // Spawn hit VFX
                SpawnHitEffect(hitCollider.transform.position, false);
                
                // Play hit sound
                if (hitSound != null)
                {
                    audioSource.PlayOneShot(hitSound);
                }
                
                lastPlayerHitTime = Time.time;
                
                // Only hit one player per check
                break;
            }
        }

        private void CheckForDestructibleCollisions()
        {
            // Cooldown check
            if (Time.time - lastDestructibleHitTime < HIT_COOLDOWN)
                return;
            
            // Calculate detection position
            Vector3 detectionPos = transform.position + transform.TransformDirection(detectionOffset);
            lastDetectionPos = detectionPos;
            
            // Perform overlap sphere
            Collider[] hitColliders = Physics.OverlapSphere(detectionPos, detectionRadius, destructibleLayer);
            
            if (verboseLogging && hitColliders.Length > 0)
            {
                Debug.Log($"[{name}] 📦 Destructible collision check: Found {hitColliders.Length} colliders");
            }
            
            foreach (var hitCollider in hitColliders)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[{name}] → Checking destructible: {hitCollider.name} (Tag: {hitCollider.tag})");
                }
                
                // Check if object has rigidbody
                Rigidbody targetRb = hitCollider.GetComponent<Rigidbody>();
                if (targetRb == null)
                {
                    targetRb = hitCollider.GetComponentInParent<Rigidbody>();
                }
                
                if (targetRb == null)
                {
                    Debug.LogWarning($"[{name}] Destructible object {hitCollider.name} has no Rigidbody!");
                    continue;
                }
                
                // Calculate knockback direction
                Vector3 knockbackDir = (hitCollider.transform.position - transform.position).normalized;
                
                // Calculate force
                float knockbackForce = abilitySettings.KnockbackForce;
                knockbackForce *= destructibleForceMultiplier;
                
                // Apply tag-based multiplier
                float tagMultiplier = GetForceMultiplierForTag(hitCollider.tag);
                knockbackForce *= tagMultiplier;
                
                // Stack bonus
                if (playerMovementController.DashAbility.StackLevel >= 2)
                {
                    knockbackForce *= 1.3f;
                }
                if (playerMovementController.DashAbility.StackLevel >= 3)
                {
                    knockbackForce *= 1.5f;
                }
                
                // Apply force with upward component
                Vector3 forceDirection = knockbackDir;
                forceDirection.y = destructibleUpwardForce;
                forceDirection.Normalize();
                
                Vector3 force = forceDirection * knockbackForce;
                
                // Apply force locally
                targetRb.velocity = Vector3.zero;
                targetRb.AddForce(force, ForceMode.Impulse);
                
                Debug.Log($"[{name}] 💥 HIT DESTRUCTIBLE {hitCollider.name}! Force: {knockbackForce}");
                
                // If object has PhotonView, sync across network
                PhotonView targetPhotonView = hitCollider.GetComponentInParent<PhotonView>();
                if (targetPhotonView != null)
                {
                    targetPhotonView.RPC("RPC_ReceiveDestructibleKnockback", RpcTarget.OthersBuffered, 
                        forceDirection, knockbackForce);
                }
                
                // Spawn destructible hit VFX
                SpawnHitEffect(hitCollider.transform.position, true);
                
                // Play destructible hit sound
                if (destructibleHitSound != null)
                {
                    audioSource.PlayOneShot(destructibleHitSound);
                }
                else if (hitSound != null)
                {
                    audioSource.PlayOneShot(hitSound);
                }
                
                lastDestructibleHitTime = Time.time;
            }
        }

        [PunRPC]
        private void RPC_ReceiveKnockback(Vector3 direction, float force, float stunTime, int attackerViewID)
        {
            PlayerStateController stateController = GetComponent<PlayerStateController>();
            if (stateController != null)
            {
                stateController.ApplyKnockbackAndStun(direction, force, stunTime);
                Debug.Log($"[{name}] [Victim] Received knockback from ViewID {attackerViewID}");
            }
        }

        [PunRPC]
        private void RPC_ReceiveDestructibleKnockback(Vector3 forceDirection, float forceMagnitude)
        {
            Rigidbody targetRb = GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                targetRb.velocity = Vector3.zero;
                targetRb.AddForce(forceDirection * forceMagnitude, ForceMode.Impulse);
                Debug.Log($"[{name}] [Remote] Destructible received knockback force: {forceMagnitude}");
            }
        }

        private void SpawnHitEffect(Vector3 position, bool isDestructible)
        {
            GameObject vfxPrefab = isDestructible && destructibleHitVFXPrefab != null 
                ? destructibleHitVFXPrefab 
                : hitVFXPrefab;
            
            if (vfxPrefab != null)
            {
                GameObject vfx = Instantiate(vfxPrefab, position, Quaternion.identity);
                Destroy(vfx, 2f);
                Debug.Log($"[{vfx.name}] ✨ Spawned hit VFX at {position}");
            }
            else
            {
                Debug.LogWarning($"[{name}] ⚠️ Hit VFX prefab is null!");
            }
        }

        private float GetForceMultiplierForTag(string tag)
        {
            return tag switch
            {
                "LightObject" => 2.65f,
                "HeavyObject" => 0.7f,
                "Crate" => 1.5f,
                "Barrel" => 1.2f,
                "Explosive" => 1.85f,
                "Fragile" => 2.1f,
                _ => 1.0f
            };
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;
            
            Vector3 detectionPos = transform.position + transform.TransformDirection(detectionOffset);
            
            // Draw player detection sphere (red)
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);
            
            // Draw destructible detection sphere (yellow/orange)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);
            
            // Draw forward direction
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, detectionPos);
            
            // Draw label
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(detectionPos, $"Detection\nRadius: {detectionRadius}");
            #endif
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 detectionPos = transform.position + transform.TransformDirection(detectionOffset);
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);
            
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, detectionPos);
            
            // Draw actual collision hits if dashing
            if (Application.isPlaying && framesSinceDashActive > 0)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(lastDetectionPos, 0.2f);
            }
        }

        private void OnGUI()
        {
            if (!verboseLogging || !photonView.IsMine) return;
            
            GUILayout.BeginArea(new Rect(10, 550, 350, 200));
            GUILayout.Label("=== DASH COLLISION DEBUG ===");
            
            bool isDashing = playerMovementController?.DashAbility?.IsActive ?? false;
            GUILayout.Label($"Dash Active: {isDashing}");
            GUILayout.Label($"Frames Active: {framesSinceDashActive}");
            GUILayout.Label($"Player Layer: {playerLayer.value}");
            GUILayout.Label($"Destructible Layer: {destructibleLayer.value}");
            GUILayout.Label($"Detection Radius: {detectionRadius}");
            GUILayout.Label($"Last Player Hit: {Time.time - lastPlayerHitTime:F2}s ago");
            GUILayout.Label($"Last Destructible Hit: {Time.time - lastDestructibleHitTime:F2}s ago");
            
            GUILayout.EndArea();
        }
    }
}