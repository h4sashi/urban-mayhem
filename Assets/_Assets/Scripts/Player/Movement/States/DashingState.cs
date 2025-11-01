using UnityEngine;
using Hanzo.Core.Interfaces;

namespace Hanzo.Player.Movement.States
{
public class DashingState : IMovementState
{
private IAbility dashAbility;

    public DashingState(IAbility ability)
    {
        dashAbility = ability;
    }
    
    public void Enter(IMovementController controller)
    {
        Debug.Log("DashingState ENTERED");
        
        // Reduce drag during dash for smoother movement
        controller.Rigidbody.drag = 0f;
        
        // Set animation immediately on enter
        if (controller.Animator != null)
        {
            Debug.Log("Setting IsDashing animation on Enter");
            controller.Animator.SetBool("DASH", true);
        }
        else
        {
            Debug.LogWarning("Animator is NULL on Enter");
        }
        
        // Optional: Disable gravity during dash
        // controller.Rigidbody.useGravity = false;
    }
    
    public void Update(IMovementController controller)
    {
        Debug.Log("DashingState Update called");
        
        // Dash ability handles the actual dash logic
        // State just needs to monitor when dash is complete
        
        // Optional: Add dash animation trigger
        if (controller.Animator != null)
        {
            Debug.Log("Setting IsDashing animation to true");
            controller.Animator.SetBool("DASH", true);
        }
        else
        {
            Debug.LogWarning("Animator is NULL in DashingState");
        }
    }
    
    public void Exit(IMovementController controller)
    {
        Debug.Log("DashingState EXITED");
        
        // Restore normal drag
        controller.Rigidbody.drag = 6f;
        
        // Optional: Re-enable gravity
        // controller.Rigidbody.useGravity = true;
        
        // Reset animation
        if (controller.Animator != null)
        {
            Debug.Log("Setting IsDashing animation to false on Exit");
            controller.Animator.SetBool("DASH", false);
        }
    }
    
    public bool CanTransitionTo(IMovementState newState)
    {
        // Can transition out of dash when ability is no longer active
        return !dashAbility.IsActive && (newState is IdleState || newState is MovingState);
    }
}

}