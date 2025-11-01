using UnityEngine;

namespace Hanzo.Core.Interfaces
{
public interface IAbility
{
string AbilityName { get; }
bool CanActivate { get; }
bool IsActive { get; }
float CooldownRemaining { get; }

    void Initialize(IMovementController controller);
    bool TryActivate();
    void Update();
    void Cleanup();
}

}