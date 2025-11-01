
using UnityEngine;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Core;

namespace Hanzo.Player.Movement.States
{
    public class MovingState : IMovementState
    {
        private MovementSettings settings;
        private Vector2 moveInput;
        private Vector2 smoothedInput;
        private Vector2 inputVelocity;
        
        // Speed boost runtime multiplier
        private float currentSpeedMultiplier = 1f;
        
        private static readonly int IsRunningHash = Animator.StringToHash("RUN");
        
        public MovingState(MovementSettings movementSettings)
        {
            settings = movementSettings;
        }
        
        public void SetMoveInput(Vector2 input)
        {
            moveInput = input;
        }
        
        /// <summary>
        /// Called by SpeedBoostAbility to modify movement speed in real-time
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            currentSpeedMultiplier = Mathf.Max(1f, multiplier);
        }
        
        public void Enter(IMovementController controller)
        {
            controller.Rigidbody.drag = settings.GroundDrag;
            
            // Set RUN animation to true when entering moving state
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsRunningHash, true);
            }
        }
        
        public void Update(IMovementController controller)
        {
            // Smooth input for better feel
            smoothedInput = Vector2.SmoothDamp(smoothedInput, moveInput, ref inputVelocity, settings.InputSmoothing);
            
            if (smoothedInput.magnitude < 0.1f) return;
            
            // Calculate movement direction in world space
            Vector3 moveDirection = new Vector3(smoothedInput.x, 0, smoothedInput.y).normalized;
            
            // Apply movement force WITH speed boost multiplier
            float effectiveMoveSpeed = settings.MoveSpeed * currentSpeedMultiplier;
            Vector3 targetVelocity = moveDirection * effectiveMoveSpeed;
            Vector3 currentVelocity = new Vector3(controller.Velocity.x, 0, controller.Velocity.z);
            Vector3 velocityDiff = targetVelocity - currentVelocity;
            
            // Apply acceleration force
            Vector3 force = velocityDiff * settings.Acceleration;
            controller.AddForce(force, ForceMode.Acceleration);
            
            // Rotate towards movement direction
            if (moveDirection.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                controller.Transform.rotation = Quaternion.RotateTowards(
                    controller.Transform.rotation,
                    targetRotation,
                    settings.RotationSpeed * Time.deltaTime
                );
            }
        }
        
        public void Exit(IMovementController controller)
        {
            // Reset input smoothing
            smoothedInput = Vector2.zero;
            inputVelocity = Vector2.zero;
            
            // Reset speed multiplier on exit
            currentSpeedMultiplier = 1f;
            
            // Set RUN animation to false when exiting moving state
            if (controller.Animator != null)
            {
                controller.Animator.SetBool(IsRunningHash, false);
            }
        }
        
        public bool CanTransitionTo(IMovementState newState)
        {
            return newState is IdleState;
        }
    }
}