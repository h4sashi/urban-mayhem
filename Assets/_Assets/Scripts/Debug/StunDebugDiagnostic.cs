using UnityEngine;
using Photon.Pun;
using Hanzo.Player.Controllers;

namespace Hanzo.DebugTools
{
    /// <summary>
    /// Temporary diagnostic script to find why stun animation replays
    /// Attach to player prefab alongside PlayerStateController
    /// REMOVE after fixing the issue
    /// </summary>
    public class StunDebugDiagnostic : MonoBehaviour
    {
        private Animator animator;
        private PhotonView photonView;
        private PlayerStateController stateController;
        
        private int stunnedHash;
        private int getUpHash;
        
        private bool wasStunned = false;
        private int stunEntryCount = 0;
        private int animatorStunTriggerCount = 0;
        
        private AnimatorStateInfo lastStateInfo;
        
        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            photonView = GetComponent<PhotonView>();
            stateController = GetComponent<PlayerStateController>();
            
            stunnedHash = Animator.StringToHash("STUNNED");
            getUpHash = Animator.StringToHash("GETUP");
        }
        
        private void Update()
        {
            if (!photonView.IsMine || animator == null || stateController == null) return;
            
            // Track when IsStunned changes
            bool currentlyStunned = stateController.IsStunned;
            if (currentlyStunned != wasStunned)
            {
                if (currentlyStunned)
                {
                    stunEntryCount++;
                    Debug.LogError($"[DIAGNOSTIC] IsStunned changed to TRUE (Entry #{stunEntryCount})");
                    Debug.LogError($"[DIAGNOSTIC] Stack trace: {System.Environment.StackTrace}");
                }
                else
                {
                    Debug.Log($"[DIAGNOSTIC] IsStunned changed to FALSE");
                }
                wasStunned = currentlyStunned;
            }
            
            // Track animator parameter changes
            bool stunnedParam = animator.GetBool(stunnedHash);
            if (stunnedParam && currentlyStunned)
            {
                // Check if we entered Stunned state in animator
                AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
                
                if (currentState.IsName("Stunned"))
                {
                    // Check if this is a new entry (normalizedTime reset to near 0)
                    if (currentState.normalizedTime < 0.1f && 
                        (lastStateInfo.shortNameHash != currentState.shortNameHash || 
                         lastStateInfo.normalizedTime > 0.5f))
                    {
                        animatorStunTriggerCount++;
                        Debug.LogError($"[DIAGNOSTIC] Animator ENTERED Stunned state (Entry #{animatorStunTriggerCount})");
                        Debug.LogError($"[DIAGNOSTIC] NormalizedTime: {currentState.normalizedTime}");
                        Debug.LogError($"[DIAGNOSTIC] STUNNED param: {stunnedParam}, GETUP param: {animator.GetBool(getUpHash)}");
                    }
                }
                
                lastStateInfo = currentState;
            }
        }
        
        private void OnGUI()
        {
            if (!photonView.IsMine) return;
            
            GUILayout.BeginArea(new Rect(Screen.width - 350, 250, 340, 200));
            GUI.backgroundColor = Color.red;
            GUILayout.Box("=== STUN DIAGNOSTIC ===");
            GUI.backgroundColor = Color.white;
            
            GUILayout.Label($"IsStunned Trigger Count: {stunEntryCount}");
            GUILayout.Label($"Animator Entry Count: {animatorStunTriggerCount}");
            
            if (animator != null)
            {
                var currentState = animator.GetCurrentAnimatorStateInfo(0);
                GUILayout.Label($"Current State: {(currentState.IsName("Stunned") ? "Stunned" : "Other")}");
                GUILayout.Label($"Normalized Time: {currentState.normalizedTime:F2}");
                GUILayout.Label($"STUNNED param: {animator.GetBool(stunnedHash)}");
                GUILayout.Label($"GETUP param: {animator.GetBool(getUpHash)}");
            }
            
            if (stateController != null)
            {
                GUILayout.Label($"IsStunned: {stateController.IsStunned}");
            }
            
            GUILayout.EndArea();
        }
    }
}