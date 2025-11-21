using UnityEngine;
using Photon.Pun;
using System.Collections;
using Hanzo.VFX;
using Hanzo.Player.Core;

namespace Hanzo.Player.Controllers
{
    /// <summary>
    /// Manages player state including stun, knockback, falling, and recovery
    /// Works with StunVFXController for synchronized visual effects
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(StunVFXController))]
    public class PlayerStateController : MonoBehaviour
    {
        [Header("State Settings")]
        [SerializeField] private float stunDuration = 2f;
        [SerializeField] private float knockbackDrag = 8f;
        
        [Header("Falling Settings")]
        [SerializeField] private MovementSettings movementSettings;
        [SerializeField] private float fallThreshold = 0.5f;
        [SerializeField] private float fallCheckInterval = 0.1f;
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private LayerMask groundLayer = ~0;
        
        [Header("Fall Damage (Optional)")]
        [SerializeField] private bool enableFallDamage = false;
        [SerializeField] private float fallDamageThreshold = 5f;
        [SerializeField] private float fallDamageMultiplier = 10f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        // State
        private bool isStunned = false;
        private bool isFalling = false;
        private bool isGrounded = true;
        private float stunTimer = 0f;
        private float lastFallCheck = 0f;
        private float fallStartHeight = 0f;
        private Coroutine stunCoroutine;
        
        // Components
        private PhotonView photonView;
        private Rigidbody rb;
        private Animator animator;
        private StunVFXController vfxController;
        
        // Animation parameter hashes
        private static readonly int StunnedHash = Animator.StringToHash("STUNNED");
        private static readonly int GetUpHash = Animator.StringToHash("GETUP");
        private static readonly int FallingHash = Animator.StringToHash("FALLING");
        private static readonly int GroundedHash = Animator.StringToHash("GROUNDED");
        
        // Properties
        public bool IsStunned => isStunned;
        public bool IsFalling => isFalling;
        public bool IsGrounded => isGrounded;
        public float StunTimeRemaining => stunTimer;
        
        // Events
        public event System.Action OnStunStarted;
        public event System.Action OnStunEnded;
        public event System.Action<Vector3, float> OnKnockbackReceived;
        public event System.Action OnFallStarted;
        public event System.Action<float> OnLanded; // Passes fall distance

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            vfxController = GetComponent<StunVFXController>();
            
            if (animator == null)
            {
                Debug.LogWarning("PlayerStateController: No Animator found. Animations will not play.");
            }
            
            if (vfxController == null)
            {
                Debug.LogError("PlayerStateController: StunVFXController component is missing!");
            }
            
            // Initialize grounded state
            UpdateGroundedState();
        }

        private void Update()
        {
            if (!photonView.IsMine) return;
            
            // Periodically check for falling (only if not stunned)
            if (!isStunned && Time.time - lastFallCheck > fallCheckInterval)
            {
                lastFallCheck = Time.time;
                CheckForFalling();
            }
        }

        /// <summary>
        /// Checks if player should enter falling state
        /// </summary>
        private void CheckForFalling()
        {
            bool wasGrounded = isGrounded;
            UpdateGroundedState();
            
            // Enter falling state if not grounded and moving downward
            if (!isGrounded && !isFalling && rb.velocity.y < -0.5f)
            {
                EnterFallingState();
            }
            // Exit falling state if grounded
            else if (isGrounded && isFalling)
            {
                ExitFallingState();
            }
            
            // Update animator grounded state
            if (animator != null && wasGrounded != isGrounded)
            {
                animator.SetBool(GroundedHash, isGrounded);
            }
        }

        /// <summary>
        /// Updates grounded state using raycast
        /// </summary>
        private void UpdateGroundedState()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundLayer))
            {
                // Additional check: make sure vertical velocity is near zero or downward
                isGrounded = rb.velocity.y <= 0.1f;
            }
            else
            {
                isGrounded = false;
            }
        }

        /// <summary>
        /// Enter falling state
        /// </summary>
        private void EnterFallingState()
        {
            isFalling = true;
            fallStartHeight = transform.position.y;
            
            // Reduce drag for realistic falling
            rb.drag = 0.5f;
            
            // Set falling animation
            if (animator != null)
            {
                animator.SetBool(FallingHash, true);
                animator.SetBool(GroundedHash, false);
            }
            
            OnFallStarted?.Invoke();
            
            Debug.Log($"[Falling] Started at height: {fallStartHeight:F2}m");
            
            // Sync to other clients
            photonView.RPC("RPC_SyncFallingState", RpcTarget.OthersBuffered, true);
        }

        /// <summary>
        /// Exit falling state and handle landing
        /// </summary>
        private void ExitFallingState()
        {
            float fallDistance = fallStartHeight - transform.position.y;
            isFalling = false;
            
            // Reset falling animation
            if (animator != null)
            {
                animator.SetBool(FallingHash, false);
                animator.SetBool(GroundedHash, true);
            }
            
            // Restore normal drag (unless stunned)
            if (!isStunned && movementSettings != null)
            {
                rb.drag = movementSettings.GroundDrag;
            }
            else if (!isStunned)
            {
                rb.drag = 6f; // Fallback value
            }
            
            Debug.Log($"[Falling] Landed! Fall distance: {fallDistance:F2}m");
            
            // Apply fall damage if enabled
            if (enableFallDamage && fallDistance > fallDamageThreshold)
            {
                float damage = (fallDistance - fallDamageThreshold) * fallDamageMultiplier;
                Debug.Log($"[Fall Damage] {damage:F1} damage from {fallDistance:F2}m fall");
                // TODO: Apply damage to player health system
            }
            
            OnLanded?.Invoke(fallDistance);
            
            // Sync to other clients
            photonView.RPC("RPC_SyncFallingState", RpcTarget.OthersBuffered, false);
        }

        /// <summary>
        /// Apply knockback and stun to this player (called via RPC)
        /// </summary>
        public void ApplyKnockbackAndStun(Vector3 knockbackDirection, float knockbackForce, float duration)
        {
            if (!photonView.IsMine) return;
            
            Debug.Log($"[LOCAL] ApplyKnockbackAndStun - Direction: {knockbackDirection}, Force: {knockbackForce}");
            
            // Exit falling state if currently falling
            if (isFalling)
            {
                isFalling = false;
                if (animator != null)
                {
                    animator.SetBool(FallingHash, false);
                }
            }
            
            // Apply knockback force
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                
                Vector3 knockbackVelocity = knockbackDirection.normalized * knockbackForce;
                knockbackVelocity.y = knockbackForce * 0.4f;
                
                rb.velocity = knockbackVelocity;
                
                Debug.Log($"[PHYSICS] Knockback velocity: {rb.velocity}");
            }
            
            // Apply stun
            StartStun(duration);
            
            // Notify listeners
            OnKnockbackReceived?.Invoke(knockbackDirection, knockbackForce);
            
            // Sync to other clients
            photonView.RPC("RPC_SyncStunState", RpcTarget.OthersBuffered, true, duration);
        }

        private void StartStun(float duration)
        {
            if (isStunned)
            {
                Debug.LogWarning("StartStun called but player is already stunned. Ignoring.");
                return;
            }
            
            if (stunCoroutine != null)
            {
                StopCoroutine(stunCoroutine);
            }
            
            stunCoroutine = StartCoroutine(StunCoroutine(duration));
        }

        private IEnumerator StunCoroutine(float duration)
        {
            isStunned = true;
            stunTimer = duration;
            
            Debug.Log($"[PlayerState] Stunned for {duration}s");
            
            // SET STUNNED ANIMATION
            if (animator != null)
            {
                animator.SetBool(StunnedHash, true);
                Debug.Log("✅ STUNNED animation parameter set to TRUE");
            }
            
            // APPLY VISUAL EFFECTS via VFXController
            if (vfxController != null)
            {
                vfxController.ApplyStunTint();
                vfxController.ShowStunVFX();
            }
            
            // Temporarily increase drag for knockback slowdown
            float originalDrag = rb.drag;
            rb.drag = knockbackDrag;
            
            OnStunStarted?.Invoke();
            
            // Wait for stun duration
            while (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                yield return null;
            }
            
            // START RECOVERY - Trigger Get Up animation
            Debug.Log("[PlayerState] Stun ended, starting Get Up animation");
            
            if (animator != null)
            {
                animator.SetBool(StunnedHash, false);
                animator.SetBool(GetUpHash, true);
                Debug.Log("✅ GETUP animation parameter set to TRUE");
            }
            
            // STOP STUN VFX and SPAWN RECOVERY VFX
            if (vfxController != null)
            {
                vfxController.HideStunVFX();
                vfxController.ShowRecoveryVFX();
            }
            
            // Wait for Get Up animation
            float getUpDuration = GetAnimationLength("Get Up");
            
            if (getUpDuration <= 0)
            {
                getUpDuration = 0.8f;
                Debug.LogWarning("Could not determine Get Up animation length, using fallback: 0.8s");
            }
            
            yield return new WaitForSeconds(getUpDuration);
            
            // RECOVERY COMPLETE
            isStunned = false;
            stunTimer = 0f;
            
            // Restore appropriate drag based on current state
            if (movementSettings != null)
            {
                rb.drag = isGrounded ? movementSettings.GroundDrag : movementSettings.AirDrag;
            }
            else
            {
                rb.drag = originalDrag;
            }
            
            // Turn off Get Up animation
            if (animator != null)
            {
                animator.SetBool(GetUpHash, false);
                Debug.Log("✅ GETUP animation parameter set to FALSE");
            }
            
            // Remove visual tint
            if (vfxController != null)
            {
                vfxController.RemoveStunTint();
            }
            
            OnStunEnded?.Invoke();
            
            Debug.Log("[PlayerState] Fully recovered from stun");
            
            // Sync recovery to other clients
            if (photonView.IsMine)
            {
                photonView.RPC("RPC_SyncStunState", RpcTarget.OthersBuffered, false, 0f);
            }
        }
        
        private float GetAnimationLength(string stateName)
        {
            if (animator == null) return 0f;
            
            var controller = animator.runtimeAnimatorController;
            if (controller == null) return 0f;
            
            foreach (var clip in controller.animationClips)
            {
                if (clip.name.Contains(stateName) || stateName.Contains(clip.name))
                {
                    Debug.Log($"Found animation clip '{clip.name}' with length: {clip.length}s");
                    return clip.length;
                }
            }
            
            return 0f;
        }

        [PunRPC]
        private void RPC_SyncStunState(bool stunned, float duration)
        {
            // Sync stun state for remote players (visual only)
            if (stunned)
            {
                isStunned = true;
                stunTimer = duration;
                
                if (animator != null)
                {
                    animator.SetBool(StunnedHash, true);
                }
                
                // Apply VFX via controller
                if (vfxController != null)
                {
                    vfxController.ApplyStunTint();
                    vfxController.ShowStunVFX();
                }
                
                // Start coroutine to handle get-up for remote players
                StartCoroutine(RemoteStunVisualCoroutine(duration));
            }
            else
            {
                isStunned = false;
                stunTimer = 0f;
                
                if (animator != null)
                {
                    animator.SetBool(StunnedHash, false);
                    animator.SetBool(GetUpHash, false);
                }
                
                // Remove VFX via controller
                if (vfxController != null)
                {
                    vfxController.HideStunVFX();
                    vfxController.RemoveStunTint();
                }
            }
        }
        
        [PunRPC]
        private void RPC_SyncFallingState(bool falling)
        {
            // Sync falling animation for remote players
            isFalling = falling;
            
            if (animator != null)
            {
                animator.SetBool(FallingHash, falling);
                animator.SetBool(GroundedHash, !falling);
            }
        }
        
        /// <summary>
        /// Handles animation states for remote players viewing the stunned player
        /// </summary>
        private IEnumerator RemoteStunVisualCoroutine(float stunDuration)
        {
            // Wait for stun duration
            yield return new WaitForSeconds(stunDuration);
            
            // Trigger get-up animation
            if (animator != null)
            {
                animator.SetBool(StunnedHash, false);
                animator.SetBool(GetUpHash, true);
            }
            
            // Stop stun VFX and show recovery VFX
            if (vfxController != null)
            {
                vfxController.HideStunVFX();
                vfxController.ShowRecoveryVFX();
            }
            
            // Wait for get-up animation
            float getUpDuration = GetAnimationLength("Get Up");
            if (getUpDuration <= 0) getUpDuration = 0.8f;
            
            yield return new WaitForSeconds(getUpDuration);
            
            // Complete recovery
            if (animator != null)
            {
                animator.SetBool(GetUpHash, false);
            }
            
            // Remove visual tint
            if (vfxController != null)
            {
                vfxController.RemoveStunTint();
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine) return;
            
            GUILayout.BeginArea(new Rect(10, 400, 300, 160));
            GUILayout.Label("=== PLAYER STATE ===");
            GUILayout.Label($"Grounded: {isGrounded}");
            GUILayout.Label($"Falling: {isFalling}");
            GUILayout.Label($"Stunned: {isStunned}");
            if (isStunned)
            {
                GUILayout.Label($"Recovery in: {stunTimer:F2}s");
            }
            if (isFalling)
            {
                float currentFallDistance = fallStartHeight - transform.position.y;
                GUILayout.Label($"Fall Distance: {currentFallDistance:F2}m");
            }
            if (vfxController != null)
            {
                GUILayout.Label($"VFX Active: {vfxController.IsStunVFXActive}");
            }
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !showDebugInfo) return;
            
            // Visualize ground check
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(origin, origin + Vector3.down * groundCheckDistance);
            
            // Draw sphere at ground check point
            Gizmos.DrawWireSphere(origin + Vector3.down * groundCheckDistance, 0.1f);
            
            // Show fall threshold check
            if (!isGrounded)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(origin, origin + Vector3.down * fallThreshold);
            }
        }

        private void OnDestroy()
        {
            if (stunCoroutine != null)
            {
                StopCoroutine(stunCoroutine);
            }
        }
    }
}