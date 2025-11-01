using UnityEngine;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Controllers;

namespace Hanzo.Player.Movement.States
{
    /// <summary>
    /// Dedicated state for when player is stunned
    /// Place in Scripts/Player/Movement/States/
    /// </summary>
    public class StunnedState : IMovementState
    {
        private PlayerStateController stateController;
        
        public StunnedState(PlayerStateController controller)
        {
            stateController = controller;
        }
        
        public void Enter(IMovementController controller)
        {
            Debug.Log("StunnedState ENTERED");
            
            // Allow physics to continue (for knockback) but zero out horizontal velocity after a moment
            // The knockback itself is applied by PlayerStateController
            
            // Turn off all movement animations - stunned animation is handled by animator directly
            if (controller.Animator != null)
            {
                controller.Animator.SetBool("RUN", false);
                controller.Animator.SetBool("DASH", false);
                // STUNNED parameter is set by PlayerStateController, not here
            }
            
            // Higher drag to stop knockback momentum gradually
            controller.Rigidbody.drag = 8f;
        }
        
        public void Update(IMovementController controller)
        {
            // While stunned, do nothing - player is locked
            // Physics (knockback) still applies via Rigidbody
            // Animation is controlled by PlayerStateController
        }
        
        public void Exit(IMovementController controller)
        {
            Debug.Log("StunnedState EXITED");
            
            // Restore normal drag
            controller.Rigidbody.drag = 6f;
            
            // Cleanup handled by PlayerStateController for animations
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            // Can only exit stunned state once recovery (including get-up) is complete
            if (stateController == null) return false;
            
            bool canExit = !stateController.IsStunned;
            
            if (canExit)
            {
                Debug.Log($"StunnedState: Can transition to {newState?.GetType().Name}");
            }
            
            return canExit && (newState is IdleState || newState is MovingState);
        }
    }
}