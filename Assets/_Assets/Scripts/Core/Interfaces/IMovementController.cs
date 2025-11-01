using UnityEngine;

namespace Hanzo.Core.Interfaces
{
  public interface IMovementController
    {
        Vector3 Position { get; }
        Vector3 Velocity { get; }
        Transform Transform { get; }
        Rigidbody Rigidbody { get; }
        Animator Animator { get; }
        
        void SetVelocity(Vector3 velocity);
        void AddForce(Vector3 force, ForceMode mode = ForceMode.Force);
        void ChangeState(IMovementState newState);
    }
}