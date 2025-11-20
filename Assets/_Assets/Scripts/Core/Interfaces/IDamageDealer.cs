using UnityEngine;

namespace Hanzo.Core.Interfaces
{
    /// <summary>
    /// Interface for entities that can deal damage
    /// </summary>
    public interface IDamageDealer
    {
        /// <summary>
        /// Deal damage to a target
        /// </summary>

        void DealDamage(IDamageable target, float damageAmount, DamageType damageType);
        
        GameObject GetDamageSource();
    }
}