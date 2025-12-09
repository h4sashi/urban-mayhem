using UnityEngine;

namespace Hanzo.AI
{
    /// <summary>
    /// ScriptableObject for AI behavior configuration with difficulty presets
    /// </summary>
    [CreateAssetMenu(fileName = "AIBehaviorSettings", menuName = "Hanzo/AI/Behavior Settings")]
    public class AIBehaviorSettings : ScriptableObject
    {
        [Header("Difficulty Preset")]
        [Tooltip("Select a difficulty preset - overrides all settings below")]
        public AIDifficulty difficulty = AIDifficulty.Medium;
        
        [Tooltip("Apply difficulty preset on awake")]
        public bool usePreset = true;
        
        [Header("Movement")]
        [Tooltip("Base movement speed when chasing or wandering")]
        public float moveSpeed = 5f;
        
        [Tooltip("How quickly the AI accelerates")]
        public float acceleration = 15f;
        
        [Tooltip("Rotation speed in degrees per second")]
        public float rotationSpeed = 540f;
        
        [Header("Detection")]
        [Tooltip("How far the AI can see players")]
        [SerializeField] private float detectionRadius = 15f;
        
        [Tooltip("How often to check for players (seconds)")]
        [SerializeField] private float detectionInterval = 0.2f;
        
        [Tooltip("Distance at which AI loses interest in target")]
        [SerializeField] private float loseTargetDistance = 25f;
        
        [Header("Combat")]
        [Tooltip("How close AI needs to be to attack")]
        [SerializeField] private float attackRange = 2f;
        
        [Tooltip("Time between attacks (seconds)")]
        [SerializeField] private float attackCooldown = 1.5f;
        
        [Tooltip("Force applied when hitting player")]
        [SerializeField] private float attackKnockbackForce = 10f;
        
        [Tooltip("How long player is stunned when hit")]
        [SerializeField] private float attackStunDuration = 2f;
        
        [Header("Wandering")]
        [Tooltip("How far from spawn point AI will wander")]
        [SerializeField] private float wanderRadius = 10f;
        
        [Tooltip("How often to pick new wander destination (seconds)")]
        [SerializeField] private float wanderChangeInterval = 3f;
        
        [Tooltip("How long AI stays idle before wandering again")]
        [SerializeField] private float idleWaitTime = 2f;
        
        [Header("Aggression")]
        [Tooltip("AI will chase players within this distance even if not directly seen")]
        [SerializeField] private bool persistentChase = true;
        
        [Tooltip("AI will actively hunt players vs. waiting for them to approach")]
        [SerializeField] private bool aggressiveMode = false;
        
        [Tooltip("Chance to dash toward player (0-1) - requires dash ability")]
        [SerializeField] [Range(0f, 1f)] private float dashChance = 0.3f;
        
        [Tooltip("Chance to use speed boost (0-1)")]
        [SerializeField] [Range(0f, 1f)] private float speedBoostChance = 0.5f;
        
        [Header("Advanced Behavior")]
        [Tooltip("AI will retreat when abilities are on cooldown")]
        [SerializeField] private bool tacticalRetreat = false;
        
        [Tooltip("AI wanders after successful hit")]
        [SerializeField] private bool wanderAfterHit = true;
        
        [Tooltip("How long to wander after hit (seconds)")]
        [SerializeField] private float wanderAfterHitDuration = 3f;
        
        // Properties
        public float MoveSpeed => moveSpeed;
        public float Acceleration => acceleration;
        public float RotationSpeed => rotationSpeed;
        public float DetectionRadius => detectionRadius;
        public float DetectionInterval => detectionInterval;
        public float LoseTargetDistance => loseTargetDistance;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public float AttackKnockbackForce => attackKnockbackForce;
        public float AttackStunDuration => attackStunDuration;
        public float WanderRadius => wanderRadius;
        public float WanderChangeInterval => wanderChangeInterval;
        public float IdleWaitTime => idleWaitTime;
        public bool PersistentChase => persistentChase;
        public bool AggressiveMode => aggressiveMode;
        public float DashChance => dashChance;
        public float SpeedBoostChance => speedBoostChance;
        public bool TacticalRetreat => tacticalRetreat;
        public bool WanderAfterHit => wanderAfterHit;
        public float WanderAfterHitDuration => wanderAfterHitDuration;
        
        // Difficulty Enum
        public enum AIDifficulty
        {
            Easy,
            Medium,
            Hard,
            Expert,
            Custom
        }
        
        /// <summary>
        /// Apply difficulty preset to all settings
        /// </summary>
        public void ApplyDifficultyPreset()
        {
            if (!usePreset) return;
            
            switch (difficulty)
            {
                case AIDifficulty.Easy:
                    ApplyEasyPreset();
                    break;
                case AIDifficulty.Medium:
                    ApplyMediumPreset();
                    break;
                case AIDifficulty.Hard:
                    ApplyHardPreset();
                    break;
                case AIDifficulty.Expert:
                    ApplyExpertPreset();
                    break;
                case AIDifficulty.Custom:
                    // Don't override - use manual settings
                    break;
            }
        }
        
        private void ApplyEasyPreset()
        {
            // EASY: Slow, inaccurate, forgiving
            moveSpeed = 4f;
            acceleration = 10f;
            rotationSpeed = 360f;
            
            detectionRadius = 12f;
            detectionInterval = 0.3f;
            loseTargetDistance = 20f;
            
            attackRange = 2.5f;
            attackCooldown = 2.5f; // Slow attacks
            attackKnockbackForce = 8f;
            attackStunDuration = 1.5f;
            
            wanderRadius = 15f;
            wanderChangeInterval = 4f;
            idleWaitTime = 3f;
            
            persistentChase = false;
            aggressiveMode = false;
            dashChance = 0.15f; // Rarely dashes
            speedBoostChance = 0.3f;
            
            tacticalRetreat = false;
            wanderAfterHit = true;
            wanderAfterHitDuration = 5f; // Long break after hit
        }
        
        private void ApplyMediumPreset()
        {
            // MEDIUM: Balanced, standard gameplay
            moveSpeed = 5f;
            acceleration = 15f;
            rotationSpeed = 540f;
            
            detectionRadius = 15f;
            detectionInterval = 0.2f;
            loseTargetDistance = 25f;
            
            attackRange = 2.5f;
            attackCooldown = 1.5f;
            attackKnockbackForce = 10f;
            attackStunDuration = 2f;
            
            wanderRadius = 10f;
            wanderChangeInterval = 3f;
            idleWaitTime = 2f;
            
            persistentChase = true;
            aggressiveMode = false;
            dashChance = 0.4f;
            speedBoostChance = 0.6f;
            
            tacticalRetreat = false;
            wanderAfterHit = true;
            wanderAfterHitDuration = 3f;
        }
        
        private void ApplyHardPreset()
        {
            // HARD: Fast, aggressive, challenging
            moveSpeed = 6f;
            acceleration = 20f;
            rotationSpeed = 720f;
            
            detectionRadius = 18f;
            detectionInterval = 0.15f;
            loseTargetDistance = 30f;
            
            attackRange = 3f;
            attackCooldown = 1.2f; // Faster attacks
            attackKnockbackForce = 12f;
            attackStunDuration = 2.5f;
            
            wanderRadius = 8f;
            wanderChangeInterval = 2f;
            idleWaitTime = 1f;
            
            persistentChase = true;
            aggressiveMode = true;
            dashChance = 0.6f;
            speedBoostChance = 0.75f;
            
            tacticalRetreat = true;
            wanderAfterHit = true;
            wanderAfterHitDuration = 2f; // Short break
        }
        
        private void ApplyExpertPreset()
        {
            // EXPERT: Lightning fast, relentless, brutal
            moveSpeed = 7f;
            acceleration = 25f;
            rotationSpeed = 900f;
            
            detectionRadius = 20f;
            detectionInterval = 0.1f; // Very frequent checks
            loseTargetDistance = 35f;
            
            attackRange = 3f;
            attackCooldown = 1f; // Very fast attacks
            attackKnockbackForce = 15f;
            attackStunDuration = 3f;
            
            wanderRadius = 5f;
            wanderChangeInterval = 1.5f;
            idleWaitTime = 0.5f;
            
            persistentChase = true;
            aggressiveMode = true;
            dashChance = 0.8f; // Almost always dashes
            speedBoostChance = 0.9f; // Almost always speed boosts
            
            tacticalRetreat = true;
            wanderAfterHit = false; // NO BREAKS - constant pressure
            wanderAfterHitDuration = 0f;
        }
        
        /// <summary>
        /// Call this in Inspector or at runtime to apply preset
        /// </summary>
        [ContextMenu("Apply Difficulty Preset")]
        public void ApplyPreset()
        {
            ApplyDifficultyPreset();
            Debug.Log($"[AI Behavior] Applied {difficulty} difficulty preset");
        }
        
        private void OnValidate()
        {
            // Auto-apply preset when difficulty changes in Inspector
            if (usePreset && difficulty != AIDifficulty.Custom)
            {
                ApplyDifficultyPreset();
            }
        }
    }
}