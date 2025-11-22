using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Player.Movement.States
{
    public class FallingState : IMovementState
    {
        private float fallStartHeight;
        
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
            fallStartHeight = controller.Position.y;
            
            // Set air drag for realistic falling physics
            controller.Rigidbody.drag = 0.5f;
            
            // Set falling animation
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsFallingHash, true);
                controller.Animator.SetBool(IsGroundedHash, false);
            }
            
            Debug.Log($"[FallingState] Entered - Height: {fallStartHeight:F2}m, Drag: {controller.Rigidbody.drag}");
        }
        
        public void Update(IMovementController controller)
        {
            // CRITICAL: Do nothing during update
            // This state is purely passive - it just maintains falling animations
            // PlayerMovementController will detect landing and transition states
            
            // Maintain air drag
            if (controller.Rigidbody.drag != 0.5f)
            {
                controller.Rigidbody.drag = 0.5f;
            }
        }
        
        public void Exit(IMovementController controller)
        {
            float fallDistance = fallStartHeight - controller.Position.y;
            
            // Clear falling animation IMMEDIATELY
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsFallingHash, false);
                controller.Animator.SetBool(IsGroundedHash, true);
            }
            
            // Restore ground drag
            controller.Rigidbody.drag = 6f;
            
            Debug.Log($"[FallingState] Exited - Distance: {fallDistance:F2}m, Drag: {controller.Rigidbody.drag}");
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            // Allow transitions to any state (controlled by PlayerMovementController)
            return true;
        }
    }
}