using Hanzo.Core.Interfaces;
using Hanzo.Player.Abilities;
using Hanzo.Player.Core;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Player.Controllers
{
    /// <summary>
    /// Handles collision detection during dash
    /// Applies knockback and DAMAGE to both players and destructible objects
    /// UPDATED VERSION with IDamageable integration
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class DashCollisionHandler : MonoBehaviour, IDamageDealer
    {
        [Header("Settings")]
        [SerializeField]
        private AbilitySettings abilitySettings;

        [SerializeField]
        private LayerMask playerLayer;

        [SerializeField]
        private LayerMask destructibleLayer;

        [SerializeField]
        private float stunDuration = 2f;

        [Header("Damage Settings")]
        [SerializeField]
        private float dashDamage = 1f; // 1 hit per dash

        [Header("Detection")]
        [SerializeField]
        private float detectionRadius = 1.5f;

        [SerializeField]
        private Vector3 detectionOffset = new Vector3(0, 0.5f, 0.5f);

        [Header("Destructible Settings")]
        [Tooltip("Force multiplier applied to destructible objects")]
        [SerializeField]
        private float destructibleForceMultiplier = 1.5f;

        [Tooltip("Upward force component for destructibles (0-1)")]
        [SerializeField]
        private float destructibleUpwardForce = 0.6f;

        [Header("Effects")]
        [SerializeField]
        private GameObject hitVFXPrefab;

        [SerializeField]
        private GameObject destructibleHitVFXPrefab;

        [SerializeField]
        private AudioClip hitSound;

        [SerializeField]
        private AudioClip destructibleHitSound;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugGizmos = true;

        [SerializeField]
        private bool verboseLogging = true;

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
                Debug.LogError(
                    $"[{name}] DashCollisionHandler: PlayerAbilityController NOT FOUND!"
                );
            }
            else if (playerMovementController.DashAbility == null)
            {
                Debug.LogError($"[{name}] DashCollisionHandler: DashAbility is NULL!");
            }
            else
            {
                Debug.Log($"[{name}] ✅ DashCollisionHandler initialized successfully");
            }

            DiagnoseLayerConfiguration();
        }

        private void DiagnoseLayerConfiguration()
        {
            Debug.Log($"[{name}] ========== LAYER CONFIGURATION ==========");
            Debug.Log(
                $"[{name}] This GameObject Layer: {gameObject.layer} ({LayerMask.LayerToName(gameObject.layer)})"
            );
            Debug.Log($"[{name}] Player Layer Mask Value: {playerLayer.value}");

            if (playerLayer == 0)
            {
                Debug.LogError($"[{name}] ⚠️ PlayerLayer is NOT SET! Collisions will not work!");
            }

            Debug.Log($"[{name}] ==========================================");
        }

        private void FixedUpdate()
        {
            if (!photonView.IsMine)
                return;

            bool isDashing =
                playerMovementController != null
                && playerMovementController.DashAbility != null
                && playerMovementController.DashAbility.IsActive;

            if (isDashing)
            {
                framesSinceDashActive++;

                if (verboseLogging && framesSinceDashActive % 5 == 0)
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
            if (Time.time - lastPlayerHitTime < HIT_COOLDOWN)
                return;

            Vector3 detectionPos =
                transform.position + transform.TransformDirection(detectionOffset);
            lastDetectionPos = detectionPos;

            Collider[] hitColliders = Physics.OverlapSphere(
                detectionPos,
                detectionRadius,
                playerLayer
            );

            if (verboseLogging)
            {
                if (hitColliders.Length > 0)
                {
                    Debug.Log(
                        $"[{name}] 🎯 Player collision check: Found {hitColliders.Length} colliders"
                    );
                }
                else if (framesSinceDashActive % 10 == 0)
                {
                    Debug.Log(
                        $"[{name}] 🔍 Player collision check: No colliders found at {detectionPos}"
                    );
                }
            }

            foreach (var hitCollider in hitColliders)
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[{name}] → Checking collider: {hitCollider.name} (Layer: {LayerMask.LayerToName(hitCollider.gameObject.layer)})"
                    );
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
                        Debug.LogWarning(
                            $"[{name}] ⚠️ No valid PhotonView found on {hitCollider.name}"
                        );
                    }
                    continue;
                }

                // Get hit player's state controller
                PlayerStateController targetState =
                    hitCollider.GetComponentInParent<PlayerStateController>();
                if (targetState == null)
                {
                    Debug.LogWarning(
                        $"[{name}] Player {hitCollider.name} hit but has no PlayerStateController!"
                    );
                    continue;
                }

                HandleSuccessfulPlayerHit(targetPhotonView, targetState, hitCollider);

                // Only hit one player per check
                break;
            }
        }

        private void CheckForDestructibleCollisions()
        {
            if (Time.time - lastDestructibleHitTime < HIT_COOLDOWN)
                return;

            Vector3 detectionPos =
                transform.position + transform.TransformDirection(detectionOffset);
            lastDetectionPos = detectionPos;

            Collider[] hitColliders = Physics.OverlapSphere(
                detectionPos,
                detectionRadius,
                destructibleLayer
            );

            if (verboseLogging && hitColliders.Length > 0)
            {
                // Debug.Log($"[{name}] 📦 Destructible collision check: Found {hitColliders.Length} colliders");
            }

            foreach (var hitCollider in hitColliders)
            {
                Rigidbody targetRb = hitCollider.GetComponent<Rigidbody>();
                if (targetRb == null)
                {
                    targetRb = hitCollider.GetComponentInParent<Rigidbody>();
                }

                if (targetRb == null)
                {
                    continue;
                }

                Vector3 knockbackDir = (
                    hitCollider.transform.position - transform.position
                ).normalized;

                float knockbackForce = abilitySettings.KnockbackForce;
                knockbackForce *= destructibleForceMultiplier;

                float tagMultiplier = GetForceMultiplierForTag(hitCollider.tag);
                knockbackForce *= tagMultiplier;

                if (playerMovementController.DashAbility.StackLevel >= 2)
                {
                    knockbackForce *= 1.3f;
                }
                if (playerMovementController.DashAbility.StackLevel >= 3)
                {
                    knockbackForce *= 1.5f;
                }

                Vector3 forceDirection = knockbackDir;
                forceDirection.y = destructibleUpwardForce;
                forceDirection.Normalize();

                Vector3 force = forceDirection * knockbackForce;

                targetRb.velocity = Vector3.zero;
                targetRb.AddForce(force, ForceMode.Impulse);

                Debug.Log(
                    $"[{name}] 💥 HIT DESTRUCTIBLE {hitCollider.name}! Force: {knockbackForce}"
                );

                PhotonView targetPhotonView = hitCollider.GetComponentInParent<PhotonView>();
                if (targetPhotonView != null)
                {
                    targetPhotonView.RPC(
                        "RPC_ReceiveDestructibleKnockback",
                        RpcTarget.OthersBuffered,
                        forceDirection,
                        knockbackForce
                    );
                }

                SpawnHitEffect(hitCollider.transform.position, true);

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

        // ========== IDamageDealer Implementation ==========

        public void DealDamage(IDamageable target, float damageAmount, DamageType damageType)
        {
            if (target == null)
                return;

            // Pass this GameObject as the damage source
            target.TakeDamage(damageAmount, gameObject, damageType);

            Debug.Log($"[{name}] Dealt {damageAmount} {damageType} damage to {target}");
        }

        /// <summary>
        /// Handle successful player hit with proper score tracking
        /// </summary>
        private void HandleSuccessfulPlayerHit(
            PhotonView targetPhotonView,
            PlayerStateController targetState,
            Collider hitCollider
        )
        {
            // Don't hit already stunned players
            if (targetState.IsStunned)
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[{name}] ⏭️ Target {targetPhotonView.Owner.NickName} already stunned"
                    );
                }
                return;
            }

            // ========== APPLY DAMAGE ==========
            IDamageable targetDamageable = hitCollider.GetComponentInParent<IDamageable>();
            if (targetDamageable != null)
            {
                DealDamage(targetDamageable, dashDamage, DamageType.Dash);

                // SCORE IS NOW HANDLED IN PlayerHealthComponent.TakeDamage()
                // No need to manually add score here anymore
            }
            else
            {
                Debug.LogWarning(
                    $"[{name}] Player {hitCollider.name} has no IDamageable component!"
                );
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

            Debug.Log(
                $"[{name}] 💥 HIT PLAYER {targetPhotonView.Owner.NickName}! Knockback: {knockbackForce}, Damage: {dashDamage}"
            );

            // IMPORTANT: Call the RPC on the VICTIM's PhotonView
            targetPhotonView.RPC(
                "RPC_ReceiveKnockback",
                RpcTarget.All,
                knockbackDir,
                knockbackForce,
                stunDuration,
                photonView.ViewID
            );

            // Spawn hit VFX
            SpawnHitEffect(hitCollider.transform.position, false);

            // Play hit sound
            if (hitSound != null)
            {
                audioSource.PlayOneShot(hitSound);
            }

            lastPlayerHitTime = Time.time;
        }

        public GameObject GetDamageSource()
        {
            return gameObject;
        }

        [PunRPC]
        private void RPC_ReceiveKnockback(
            Vector3 direction,
            float force,
            float stunTime,
            int attackerViewID
        )
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
                Debug.Log(
                    $"[{name}] [Remote] Destructible received knockback force: {forceMagnitude}"
                );
            }
        }

        private void SpawnHitEffect(Vector3 position, bool isDestructible)
        {
            GameObject vfxPrefab =
                isDestructible && destructibleHitVFXPrefab != null
                    ? destructibleHitVFXPrefab
                    : hitVFXPrefab;

            if (vfxPrefab != null)
            {
                GameObject vfx = Instantiate(vfxPrefab, position, Quaternion.identity);
                Destroy(vfx, 2f);
                Debug.Log($"[{vfx.name}] ✨ Spawned hit VFX at {position}");
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
                _ => 1.0f,
            };
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos)
                return;

            Vector3 detectionPos =
                transform.position + transform.TransformDirection(detectionOffset);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, detectionPos);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 detectionPos =
                transform.position + transform.TransformDirection(detectionOffset);
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, detectionPos);

            if (Application.isPlaying && framesSinceDashActive > 0)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(lastDetectionPos, 0.2f);
            }
        }

        private void OnGUI()
        {
            if (!verboseLogging || !photonView.IsMine)
                return;

            GUILayout.BeginArea(new Rect(10, 550, 350, 200));
            GUILayout.Label("=== DASH COLLISION DEBUG ===");

            bool isDashing = playerMovementController?.DashAbility?.IsActive ?? false;
            GUILayout.Label($"Dash Active: {isDashing}");
            GUILayout.Label($"Frames Active: {framesSinceDashActive}");
            GUILayout.Label($"Player Layer: {playerLayer.value}");
            GUILayout.Label($"Destructible Layer: {destructibleLayer.value}");
            GUILayout.Label($"Detection Radius: {detectionRadius}");
            GUILayout.Label($"Dash Damage: {dashDamage}");
            GUILayout.Label($"Last Player Hit: {Time.time - lastPlayerHitTime:F2}s ago");
            GUILayout.Label(
                $"Last Destructible Hit: {Time.time - lastDestructibleHitTime:F2}s ago"
            );

            GUILayout.EndArea();
        }
    }
}
