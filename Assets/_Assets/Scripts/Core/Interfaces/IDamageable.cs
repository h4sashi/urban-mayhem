using UnityEngine;

namespace Hanzo.Core.Interfaces
{
    /// <summary>
    /// Interface for entities that can take damage
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// Apply damage to this entity
        /// </summary>
        /// <param name="damageAmount">Amount of damage to apply</param>
        /// <param name="damageSource">The GameObject that caused the damage (optional)</param>
        /// <param name="damageType">Type of damage (Dash, Explosion, etc.)</param>
        void TakeDamage(float damageAmount, GameObject damageSource = null, DamageType damageType = DamageType.Generic);
        
        /// <summary>
        /// Current health of the entity
        /// </summary>
        float CurrentHealth { get; }
        
        /// <summary>
        /// Maximum health of the entity
        /// </summary>
        float MaxHealth { get; }
        
        /// <summary>
        /// Is this entity currently alive?
        /// </summary>
        bool IsAlive { get; }
    }
    
    /// <summary>
    /// Types of damage that can be dealt
    /// </summary>
    public enum DamageType
    {
        Generic,
        Dash,
        Explosion,
       
    }
}