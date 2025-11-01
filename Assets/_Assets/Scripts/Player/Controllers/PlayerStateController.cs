
using UnityEngine;
using Photon.Pun;
using System.Collections;
using Hanzo.VFX;

namespace Hanzo.Player.Controllers
{
    /// <summary>
    /// Manages player state including stun, knockback, and recovery
    /// Works with StunVFXController for synchronized visual effects
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(StunVFXController))]
    public class PlayerStateController : MonoBehaviour
    {
        [Header("State Settings")]
        [SerializeField] private float stunDuration = 2f;
        [SerializeField] private float knockbackDrag = 8f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        // State
        private bool isStunned = false;
        private float stunTimer = 0f;
        private Coroutine stunCoroutine;
        
        // Components
        private PhotonView photonView;
        private Rigidbody rb;
        private Animator animator;
        private StunVFXController vfxController;
        
        // Animation parameter hashes
        private static readonly int StunnedHash = Animator.StringToHash("STUNNED");
        private static readonly int GetUpHash = Animator.StringToHash("GETUP");
        
        // Properties
        public bool IsStunned => isStunned;
        public float StunTimeRemaining => stunTimer;
        
        // Events
        public event System.Action OnStunStarted;
        public event System.Action OnStunEnded;
        public event System.Action<Vector3, float> OnKnockbackReceived;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            vfxController = GetComponent<StunVFXController>();
            
            if (animator == null)
            {
                Debug.LogWarning("PlayerStateController: No Animator found. Stun animations will not play.");
            }
            
            if (vfxController == null)
            {
                Debug.LogError("PlayerStateController: StunVFXController component is missing!");
            }
        }

        /// <summary>
        /// Apply knockback and stun to this player (called via RPC)
        /// </summary>
        public void ApplyKnockbackAndStun(Vector3 knockbackDirection, float knockbackForce, float duration)
        {
            if (!photonView.IsMine) return;
            
            Debug.Log($"[LOCAL] ApplyKnockbackAndStun - Direction: {knockbackDirection}, Force: {knockbackForce}");
            
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
            rb.drag = originalDrag;
            
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
            
            GUILayout.BeginArea(new Rect(10, 400, 300, 120));
            GUILayout.Label("=== PLAYER STATE ===");
            GUILayout.Label($"Stunned: {isStunned}");
            if (isStunned)
            {
                GUILayout.Label($"Recovery in: {stunTimer:F2}s");
            }
            if (vfxController != null)
            {
                GUILayout.Label($"VFX Active: {vfxController.IsStunVFXActive}");
            }
            GUILayout.EndArea();
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