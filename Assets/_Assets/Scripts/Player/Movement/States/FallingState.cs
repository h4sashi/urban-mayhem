using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Player.Movement.States
{
    public class FallingState : IMovementState
    {
        private float fallStartHeight;
        private bool hasLanded;
        
        // Ground detection settings
        private readonly float groundCheckDistance = 0.3f;
        private readonly LayerMask groundLayer;
        
        // Animation hashes
        private static readonly int IsFallingHash = Animator.StringToHash("FALLING");
        private static readonly int IsGroundedHash = Animator.StringToHash("GROUNDED");
        
        public FallingState(LayerMask groundLayerMask)
        {
            groundLayer = groundLayerMask;
        }
        
        public void Enter(IMovementController controller)
        {
            // Record starting height for fall damage calculations if needed
            fallStartHeight = controller.Position.y;
            hasLanded = false;
            
            // Reduce drag for realistic falling
            controller.Rigidbody.drag = 0.5f;
            
            // Set falling animation
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsFallingHash, true);
                controller.Animator.SetBool(IsGroundedHash, false);
            }
            
            Debug.Log($"Entered Falling State at height: {fallStartHeight}");
        }
        
        public void Update(IMovementController controller)
        {
            // Check if we've landed
            if (IsGrounded(controller))
            {
                if (!hasLanded)
                {
                    hasLanded = true;
                    float fallDistance = fallStartHeight - controller.Position.y;
                    Debug.Log($"Landed! Fall distance: {fallDistance:F2}m");
                    
                    // Optionally apply fall damage here based on fallDistance
                    // if (fallDistance > damageThreshold) { ApplyFallDamage(); }
                }
            }
            
            // Optional: Allow slight air control while falling
            // This keeps the player from feeling helpless
            ApplyAirControl(controller);
        }
        
        public void Exit(IMovementController controller)
        {
            // Reset animation
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsFallingHash, false);
                controller.Animator.SetBool(IsGroundedHash, true);
            }
            
            Debug.Log("Exited Falling State");
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            // Can only transition to Idle or Moving when grounded
            if (hasLanded)
            {
                return newState is IdleState || newState is MovingState;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if the player is on the ground using raycasts
        /// </summary>
        private bool IsGrounded(IMovementController controller)
        {
            Vector3 origin = controller.Position + Vector3.up * 0.1f;
            
            // Cast a ray downward to check for ground
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundLayer))
            {
                // Additional check: make sure vertical velocity is near zero or downward
                return controller.Velocity.y <= 0.1f;
            }
            
            return false;
        }
        
        /// <summary>
        /// Allows limited air control while falling
        /// </summary>
        private void ApplyAirControl(IMovementController controller)
        {
            // Get horizontal velocity only
            Vector3 horizontalVelocity = new Vector3(controller.Velocity.x, 0, controller.Velocity.z);
            
            // Apply light drag to horizontal movement
            if (horizontalVelocity.magnitude > 0.1f)
            {
                Vector3 dragForce = -horizontalVelocity.normalized * 2f;
                controller.AddForce(dragForce, ForceMode.Acceleration);
            }
        }
    }
}