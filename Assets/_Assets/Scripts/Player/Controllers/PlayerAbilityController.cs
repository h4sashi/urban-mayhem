using UnityEngine;
using System.Collections.Generic;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Abilities;
using Hanzo.VFX;
using Photon.Pun;

namespace Hanzo.Player.Controllers
{
    public class PlayerAbilityController : MonoBehaviourPun
    {
        [Header("Settings")]
        [SerializeField] private AbilitySettings abilitySettings;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private IMovementController movementController;
        private List<IAbility> abilities = new List<IAbility>();
        
        // Quick access to specific abilities
        private DashAbility dashAbility;
        public DashAbility DashAbility => dashAbility;
        
        // Visual components
        private TrailRenderer dashTrail;
        private DashVFXController dashVFX;
        private Animator animator;

        private void Awake()
        {
            movementController = GetComponent<IMovementController>();
            
            InitializeAbilities();
            CacheVisualComponents();
        }

        private void InitializeAbilities()
        {
            // Create dash ability
            dashAbility = new DashAbility(abilitySettings);
            
            // Wire up VFX controller
            dashVFX = GetComponentInChildren<DashVFXController>(true);
            if (dashVFX != null)
            {
                dashAbility.SetVFXController(dashVFX);
                Debug.Log("PlayerAbilityController: DashVFXController injected successfully.");
            }
            else
            {
                Debug.LogWarning("PlayerAbilityController: No DashVFXController found. VFX won't play.");
            }
            
            dashAbility.Initialize(movementController);
            abilities.Add(dashAbility);
        }

        private void CacheVisualComponents()
        {
            // Cache components for network sync
            animator = GetComponentInChildren<Animator>(true);
            
            // Find the dash trail that was created by DashAbility
            Transform trailTransform = transform.Find("DashTrail");
            if (trailTransform != null)
            {
                dashTrail = trailTransform.GetComponent<TrailRenderer>();
            }
        }

        private void Update()
        {
            // Only update abilities for local player
            if (!photonView.IsMine) return;

            // Update all abilities
            foreach (var ability in abilities)
            {
                ability.Update();
            }
        }

        public bool TryActivateDash()
        {
            if (!photonView.IsMine) return false;
            
            bool activated = dashAbility.TryActivate();
            
            return activated;
        }
        
        /// <summary>
        /// RPC called by local player to show dash visuals on remote clients
        /// </summary>
        [PunRPC]
        private void RPC_PlayDashVisuals()
        {
            Debug.Log($"[REMOTE] RPC_PlayDashVisuals called on {gameObject.name}");
            
            // Play trail
            if (dashTrail != null)
            {
                dashTrail.emitting = true;
                dashTrail.Clear();
                Debug.Log("[REMOTE] Trail started");
            }
            else
            {
                Debug.LogWarning("[REMOTE] DashTrail not found!");
            }
            
            // Play VFX
            if (dashVFX != null)
            {
                dashVFX.Play();
                Debug.Log("[REMOTE] VFX played");
            }
            else
            {
                Debug.LogWarning("[REMOTE] DashVFX not found!");
            }
            
            // Set animation
            if (animator != null)
            {
                animator.SetBool("DASH", true);
                Debug.Log("[REMOTE] Animation set to DASH");
            }
            else
            {
                Debug.LogWarning("[REMOTE] Animator not found!");
            }
        }
        
        /// <summary>
        /// RPC called by local player to stop dash visuals on remote clients
        /// </summary>
        [PunRPC]
        private void RPC_StopDashVisuals()
        {
            Debug.Log($"[REMOTE] RPC_StopDashVisuals called on {gameObject.name}");
            
            // Stop trail
            if (dashTrail != null)
            {
                dashTrail.emitting = false;
            }
            
            // Stop animation
            if (animator != null)
            {
                animator.SetBool("DASH", false);
            }
        }
        
        /// <summary>
        /// Called when player picks up a dash power-up (GDD stacking system)
        /// </summary>
        public void AddDashStack()
        {
            if (dashAbility != null)
            {
                dashAbility.AddStack();
            }
        }
        
        /// <summary>
        /// Reset dash to base level (e.g., on respawn or round start)
        /// </summary>
        public void ResetDashStacks()
        {
            if (dashAbility != null)
            {
                dashAbility.ResetStacks();
            }
        }

        private void OnDestroy()
        {
            // Cleanup all abilities
            foreach (var ability in abilities)
            {
                ability.Cleanup();
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine) return;

            GUILayout.BeginArea(new Rect(10, 230, 300, 180));
            GUILayout.Label("=== ABILITIES ===");
            GUILayout.Label($"Dash Ready: {dashAbility.CanActivate}");
            GUILayout.Label($"Dash Active: {dashAbility.IsActive}");
            GUILayout.Label($"Dash Stack: {dashAbility.StackLevel}/3");
            GUILayout.Label($"Cooldown: {dashAbility.CooldownRemaining:F2}s");
            
            // Show stack effects
            string stackEffect = dashAbility.StackLevel switch
            {
                1 => "Base Dash",
                2 => "Enhanced (1.5x distance)",
                3 => "Chain Dash Ready",
                _ => "Unknown"
            };
            GUILayout.Label($"Effect: {stackEffect}");
            
            GUILayout.EndArea();
        }
    }
}