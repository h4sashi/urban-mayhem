using System.Collections;
using Hanzo.Player.Core;
using Hanzo.VFX;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Player.Controllers
{
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(StunVFXController))]
    public class PlayerStateController : MonoBehaviour
    {
        [Header("State Settings")]
        [SerializeField]
        private float stunDuration = 2f;

        [SerializeField]
        private float knockbackDrag = 8f;

        [Header("Falling Settings")]
        [SerializeField]
        private MovementSettings movementSettings;

        [SerializeField]
        private float fallThreshold = 0.5f;

        [SerializeField]
        private float fallCheckInterval = 0.1f;

        [SerializeField]
        private float groundCheckDistance = 0.5f;

        [SerializeField]
        private float groundCheckRadius = 0.3f;

        [SerializeField]
        private LayerMask groundLayer = ~0;

        [Header("Fall Detection")]
        [SerializeField]
        private float minFallVelocity = -2f;

        [SerializeField]
        private float groundedGracePeriod = 0.15f;

        [Header("Fall Damage (Optional)")]
        [SerializeField]
        private bool enableFallDamage = false;

        [SerializeField]
        private float fallDamageThreshold = 5f;

        [SerializeField]
        private float fallDamageMultiplier = 10f;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        // State
        private bool isStunned = false;
        private bool isFalling = false;
        private bool isGrounded = true;
        private float stunTimer = 0f;
        private float lastFallCheck = 0f;
        private float fallStartHeight = 0f;
        private float lastGroundedTime = 0f;
        private Coroutine stunCoroutine;

        // Components
        private PhotonView photonView;
        private Rigidbody rb;
        private Animator animator;
        private StunVFXController vfxController;

        // Offline compatibility
        private bool isOfflineMode = false;
        private bool isLocalPlayer = true;

        // Animation hashes
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
        public event System.Action<float> OnLanded;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            vfxController = GetComponent<StunVFXController>();

            // Check if we're in offline mode
            CheckOfflineMode();

            if (animator == null)
                Debug.LogWarning("[PlayerState] No Animator found.");

            if (vfxController == null)
                Debug.LogError("[PlayerState] StunVFXController missing!");
        }

        private void Start()
        {
            // Re-check offline mode in Start (PhotonNetwork may initialize later)
            CheckOfflineMode();

            // Initialize grounded state with a proper check
            ForceGroundCheck();
            lastGroundedTime = Time.time;
        }

        /// <summary>
        /// Determines if running offline or if this is the local player
        /// </summary>
        private void CheckOfflineMode()
        {
            try
            {
                // Check if PhotonNetwork is available and connected
                isOfflineMode = !PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode;

                if (isOfflineMode)
                {
                    isLocalPlayer = true;
                    Debug.Log("[PlayerState] Running in OFFLINE mode");
                }
                else
                {
                    isLocalPlayer = photonView != null && photonView.IsMine;
                }
            }
            catch (System.Exception)
            {
                // If any Photon error occurs, assume offline mode
                isOfflineMode = true;
                isLocalPlayer = true;
                Debug.Log("[PlayerState] Photon unavailable, running OFFLINE");
            }
        }

        /// <summary>
        /// Returns true if this is the local player (works offline and online)
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
                return true; // Fallback to local if Photon fails
            }
        }

        private void Update()
        {
            if (!IsLocalPlayer())
                return;

            // Periodically check for falling (only if not stunned)
            if (!isStunned && Time.time - lastFallCheck > fallCheckInterval)
            {
                lastFallCheck = Time.time;
                CheckForFalling();
            }
        }

        /// <summary>
        /// Improved ground detection using SphereCast
        /// </summary>
        private void UpdateGroundedState()
        {
            Vector3 origin = transform.position + Vector3.up * (groundCheckRadius + 0.05f);

            // Use SphereCast for more reliable ground detection
            if (
                Physics.SphereCast(
                    origin,
                    groundCheckRadius,
                    Vector3.down,
                    out RaycastHit hit,
                    groundCheckDistance,
                    groundLayer,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                // Check if the surface is walkable (not too steep)
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                bool isWalkable = slopeAngle < 45f;

                // Grounded if on walkable surface and not moving up fast
                isGrounded = isWalkable && rb.velocity.y < 2f;
            }
            else
            {
                isGrounded = false;
            }
        }

        /// <summary>
        /// Force an immediate ground check (useful for initialization)
        /// </summary>
        public void ForceGroundCheck()
        {
            UpdateGroundedState();

            // If grounded, make sure we're not in falling state
            if (isGrounded && isFalling)
            {
                isFalling = false;
                if (animator != null)
                {
                    animator.SetBool(FallingHash, false);
                    animator.SetBool(GroundedHash, true);
                }
            }

            Debug.Log($"[PlayerState] Force ground check: Grounded={isGrounded}");
        }

        private void CheckForFalling()
        {
            bool wasGrounded = isGrounded;
            UpdateGroundedState();

            // Track when we were last grounded
            if (isGrounded)
            {
                lastGroundedTime = Time.time;

                // CRITICAL: If we become grounded and were falling, exit falling state immediately
                if (isFalling)
                {
                    ExitFallingState();
                }
            }

            // Only enter falling if:
            // 1. Not grounded
            // 2. Not already falling
            // 3. Moving downward (negative Y velocity)
            // 4. Grace period has passed
            bool shouldFall =
                !isGrounded
                && !isFalling
                && rb.velocity.y < -0.5f
                && (Time.time - lastGroundedTime) > groundedGracePeriod;

            if (shouldFall)
            {
                EnterFallingState();
            }
        }

        // CRITICAL FIXES for PlayerStateController.cs
        // Replace the ExitFallingState() and EnterFallingState() methods with these versions:

        private void EnterFallingState()
        {
            isFalling = true;
            fallStartHeight = transform.position.y;

            // Set air drag for falling
            rb.drag = movementSettings != null ? movementSettings.AirDrag : 0.5f;

            if (animator != null)
            {
                animator.SetBool(FallingHash, true);
                animator.SetBool(GroundedHash, false);
            }

            OnFallStarted?.Invoke();
            Debug.Log($"[Falling] Started at height: {fallStartHeight:F2}m, Drag: {rb.drag}");

            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_SyncFallingState", RpcTarget.OthersBuffered, true);
                }
                catch { }
            }
        }

        private void ExitFallingState()
        {
            float fallDistance = fallStartHeight - transform.position.y;
            isFalling = false;

            // CRITICAL FIX: Clear falling animation IMMEDIATELY
            if (animator != null)
            {
                animator.SetBool(FallingHash, false);
                animator.SetBool(GroundedHash, true);
            }

            // Restore ground drag
            if (!isStunned)
            {
                rb.drag = movementSettings != null ? movementSettings.GroundDrag : 6f;
            }

            Debug.Log($"[Falling] Landed! Distance: {fallDistance:F2}m, Drag: {rb.drag}");

            if (enableFallDamage && fallDistance > fallDamageThreshold)
            {
                float damage = (fallDistance - fallDamageThreshold) * fallDamageMultiplier;
                Debug.Log($"[Fall Damage] {damage:F1} from {fallDistance:F2}m");
            }

            OnLanded?.Invoke(fallDistance);

            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_SyncFallingState", RpcTarget.OthersBuffered, false);
                }
                catch { }
            }
        }

        // REMOVE the SetGroundedAfterFrame() coroutine - it's no longer needed
        // DELETE this entire method from your code:
        // private IEnumerator SetGroundedAfterFrame() { ... }

        private IEnumerator SetGroundedAfterFrame()
        {
            // Wait one frame to ensure animator processes FALLING = false
            yield return null;

            if (animator != null)
            {
                animator.SetBool(GroundedHash, true);
            }
        }

        public void ApplyKnockbackAndStun(
            Vector3 knockbackDirection,
            float knockbackForce,
            float duration
        )
        {
            if (!IsLocalPlayer())
                return;

            Debug.Log(
                $"[LOCAL] ApplyKnockbackAndStun - Dir: {knockbackDirection}, Force: {knockbackForce}"
            );

            if (isFalling)
            {
                isFalling = false;
                if (animator != null)
                    animator.SetBool(FallingHash, false);
            }

            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                Vector3 knockback = knockbackDirection.normalized * knockbackForce;
                knockback.y = knockbackForce * 0.4f;
                rb.velocity = knockback;
            }

            StartStun(duration);
            OnKnockbackReceived?.Invoke(knockbackDirection, knockbackForce);

            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_SyncStunState", RpcTarget.OthersBuffered, true, duration);
                }
                catch { }
            }
        }

        private void StartStun(float duration)
        {
            if (isStunned)
                return;

            if (stunCoroutine != null)
                StopCoroutine(stunCoroutine);

            stunCoroutine = StartCoroutine(StunCoroutine(duration));
        }

        private IEnumerator StunCoroutine(float duration)
        {
            isStunned = true;
            stunTimer = duration;

            if (animator != null)
                animator.SetBool(StunnedHash, true);

            if (vfxController != null)
            {
                vfxController.ApplyStunTint();
                vfxController.ShowStunVFX();
            }

            float originalDrag = rb.drag;
            rb.drag = knockbackDrag;

            OnStunStarted?.Invoke();

            while (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                yield return null;
            }

            if (animator != null)
            {
                animator.SetBool(StunnedHash, false);
                animator.SetBool(GetUpHash, true);
            }

            if (vfxController != null)
            {
                vfxController.HideStunVFX();
                vfxController.ShowRecoveryVFX();
            }

            float getUpDuration = GetAnimationLength("Get Up");
            if (getUpDuration <= 0)
                getUpDuration = 0.8f;

            yield return new WaitForSeconds(getUpDuration);

            isStunned = false;
            stunTimer = 0f;

            // Force ground check after stun
            ForceGroundCheck();

            if (movementSettings != null)
                rb.drag = isGrounded ? movementSettings.GroundDrag : movementSettings.AirDrag;
            else
                rb.drag = originalDrag;

            if (animator != null)
                animator.SetBool(GetUpHash, false);

            if (vfxController != null)
                vfxController.RemoveStunTint();

            OnStunEnded?.Invoke();

            if (!isOfflineMode && photonView != null)
            {
                try
                {
                    photonView.RPC("RPC_SyncStunState", RpcTarget.OthersBuffered, false, 0f);
                }
                catch { }
            }
        }

        private float GetAnimationLength(string stateName)
        {
            if (animator == null)
                return 0f;
            var controller = animator.runtimeAnimatorController;
            if (controller == null)
                return 0f;

            foreach (var clip in controller.animationClips)
            {
                if (clip.name.Contains(stateName) || stateName.Contains(clip.name))
                    return clip.length;
            }
            return 0f;
        }

        [PunRPC]
        private void RPC_SyncStunState(bool stunned, float duration)
        {
            if (stunned)
            {
                isStunned = true;
                stunTimer = duration;
                if (animator != null)
                    animator.SetBool(StunnedHash, true);
                if (vfxController != null)
                {
                    vfxController.ApplyStunTint();
                    vfxController.ShowStunVFX();
                }
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
            isFalling = falling;
            if (animator != null)
            {
                animator.SetBool(FallingHash, falling);
                animator.SetBool(GroundedHash, !falling);
            }
        }

        private IEnumerator RemoteStunVisualCoroutine(float stunDur)
        {
            yield return new WaitForSeconds(stunDur);

            if (animator != null)
            {
                animator.SetBool(StunnedHash, false);
                animator.SetBool(GetUpHash, true);
            }
            if (vfxController != null)
            {
                vfxController.HideStunVFX();
                vfxController.ShowRecoveryVFX();
            }

            float getUpDur = GetAnimationLength("Get Up");
            if (getUpDur <= 0)
                getUpDur = 0.8f;
            yield return new WaitForSeconds(getUpDur);

            if (animator != null)
                animator.SetBool(GetUpHash, false);
            if (vfxController != null)
                vfxController.RemoveStunTint();
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !IsLocalPlayer())
                return;

            GUILayout.BeginArea(new Rect(10, 400, 300, 180));
            GUILayout.Label("=== PLAYER STATE ===");
            GUILayout.Label($"Mode: {(isOfflineMode ? "OFFLINE" : "ONLINE")}");
            GUILayout.Label($"Grounded: {isGrounded}");
            GUILayout.Label($"Falling: {isFalling}");
            GUILayout.Label($"Stunned: {isStunned}");
            GUILayout.Label($"Velocity Y: {rb.velocity.y:F2}");
            if (isStunned)
                GUILayout.Label($"Recovery in: {stunTimer:F2}s");
            if (isFalling)
            {
                float dist = fallStartHeight - transform.position.y;
                GUILayout.Label($"Fall Distance: {dist:F2}m");
            }
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !showDebugInfo)
                return;

            Vector3 origin = transform.position + Vector3.up * (groundCheckRadius + 0.05f);
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin, groundCheckRadius);
            Gizmos.DrawLine(origin, origin + Vector3.down * groundCheckDistance);
            Gizmos.DrawWireSphere(origin + Vector3.down * groundCheckDistance, groundCheckRadius);
        }

        private void OnDestroy()
        {
            if (stunCoroutine != null)
                StopCoroutine(stunCoroutine);
        }
    }
}
