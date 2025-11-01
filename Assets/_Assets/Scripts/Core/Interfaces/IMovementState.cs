using UnityEngine;

namespace Hanzo.Core.Interfaces
{
  public interface IMovementState
    {
        void Enter(IMovementController controller);
        void Update(IMovementController controller);
        void Exit(IMovementController controller);
        bool CanTransitionTo(IMovementState newState);
    }
}