using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Player.Movement.States
{
    /// <summary>
    /// Optional state for Speed Boost - not strictly necessary since boost modifies MovingState
    /// Include only if you want explicit state tracking separate from ability logic
    /// </summary>
    public class SpeedBoostingState : IMovementState
    {
        private IAbility speedBoostAbility;
        
        private static readonly int IsSpeedBoostHash = Animator.StringToHash("SPEEDBOOST");
        private static readonly int IsRunningHash = Animator.StringToHash("RUN");
        
        public SpeedBoostingState(IAbility ability)
        {
            speedBoostAbility = ability;
        }
        
        public void Enter(IMovementController controller)
        {
            Debug.Log("SpeedBoostingState ENTERED");
            
            // Keep RUN animation active during speed boost
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsRunningHash, true);
                controller.Animator.SetBool(IsSpeedBoostHash, true);
            }
        }
        
        public void Update(IMovementController controller)
        {
            // Speed boost logic is handled by the ability itself
            // This state just monitors the ability status
        }
        
        public void Exit(IMovementController controller)
        {
            Debug.Log("SpeedBoostingState EXITED");
            
            // Reset animation
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsSpeedBoostHash, false);
                // Don't turn off RUN here - let MovingState/IdleState handle it
            }
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            // Can transition out when boost is no longer active
            return !speedBoostAbility.IsActive;
        }
    }
}