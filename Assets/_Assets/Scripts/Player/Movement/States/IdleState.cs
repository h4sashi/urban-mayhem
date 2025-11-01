using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Player.Movement.States
{
    public class IdleState : IMovementState
    {
        public void Enter(IMovementController controller)
        {
            // Apply ground drag for natural deceleration
            controller.Rigidbody.drag = 6f;
        }
        
        public void Update(IMovementController controller)
        {
            // Idle state - minimal processing
            // Natural deceleration handled by drag
        }
        
        public void Exit(IMovementController controller)
        {
            // Clean up if needed
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            // Can transition to moving from idle
            return newState is MovingState;
        }
    }
}