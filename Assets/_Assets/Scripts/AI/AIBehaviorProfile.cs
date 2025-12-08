using UnityEngine;
using System;

namespace Hanzo.AI
{
    /// <summary>
    /// Defines AI personality and behavior patterns
    /// Create different profiles for varying difficulty levels and playstyles
    /// </summary>
    [CreateAssetMenu(fileName = "AIBehaviorProfile", menuName = "Hanzo/AI Behavior Profile")]
    public class AIBehaviorProfile : ScriptableObject
    {
        [Header("Personality")]
        [Tooltip("How aggressive the AI is (0-100). Higher = more likely to attack")]
        [Range(0f, 100f)]
        [SerializeField] private float aggression = 50f;
        
        [Tooltip("How cautious the AI is (0-100). Higher = more defensive")]
        [Range(0f, 100f)]
        [SerializeField] private float caution = 50f;
        
        [Tooltip("How quickly AI reacts to threats (seconds)")]
        [Range(0.1f, 2f)]
        [SerializeField] private float reactionTime = 0.3f;
        
        [Header("Combat Skills")]
        [Tooltip("Accuracy of dash attacks (0-100)")]
        [Range(0f, 100f)]
        [SerializeField] private float accuracy = 70f;
        
        [Tooltip("Minimum time between dash attempts (seconds)")]
        [Range(0.5f, 5f)]
        [SerializeField] private float dashCooldown = 2f;
        
        [Tooltip("Minimum time between speed boost uses (seconds)")]
        [Range(2f, 10f)]
        [SerializeField] private float speedBoostCooldown = 5f;
        
        [Tooltip("Preferred distance for dash attacks")]
        [Range(5f, 15f)]
        [SerializeField] private float preferredAttackDistance = 8f;
        
        [Header("Movement Behavior")]
        [Tooltip("How long to remain idle (seconds)")]
        [Range(1f, 10f)]
        [SerializeField] private float idleTime = 5f;
        
        [Tooltip("Movement speed multiplier (0.5-1.5)")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float movementSpeedMultiplier = 1f;
        
        [Tooltip("How often to change patrol destination (seconds)")]
        [Range(5f, 30f)]
        [SerializeField] private float patrolChangeInterval = 15f;
        
        [Header("Decision Making")]
        [Tooltip("Health % below which AI will retreat")]
        [Range(0.1f, 0.8f)]
        [SerializeField] private float retreatThreshold = 0.3f;
        
        [Tooltip("Health % above which AI will stop retreating")]
        [Range(0.3f, 1f)]
        [SerializeField] private float returnToFightThreshold = 0.5f;
        
        [Tooltip("How far to retreat when low health")]
        [Range(10f, 50f)]
        [SerializeField] private float retreatDistance = 20f;
        
        [Header("Target Selection")]
        [Tooltip("Prefer targeting low health enemies")]
        [SerializeField] private bool preferWeakTargets = true;
        
        [Tooltip("Prefer targeting closest enemies")]
        [SerializeField] private bool preferCloseTargets = true;
        
        [Tooltip("How often to switch targets (seconds)")]
        [Range(2f, 10f)]
        [SerializeField] private float targetSwitchInterval = 5f;
        
        [Header("Difficulty Presets")]
        [SerializeField] private DifficultyLevel difficultyLevel = DifficultyLevel.Medium;
        
        // Public properties
        public float Aggression => aggression;
        public float Caution => caution;
        public float ReactionTime => reactionTime;
        public float Accuracy => accuracy;
        public float DashCooldown => dashCooldown;
        public float SpeedBoostCooldown => speedBoostCooldown;
        public float PreferredAttackDistance => preferredAttackDistance;
        public float IdleTime => idleTime;
        public float MovementSpeedMultiplier => movementSpeedMultiplier;
        public float PatrolChangeInterval => patrolChangeInterval;
        public float RetreatThreshold => retreatThreshold;
        public float ReturnToFightThreshold => returnToFightThreshold;
        public float RetreatDistance => retreatDistance;
        public bool PreferWeakTargets => preferWeakTargets;
        public bool PreferCloseTargets => preferCloseTargets;
        public float TargetSwitchInterval => targetSwitchInterval;
        
        /// <summary>
        /// Get behavior value by stat type
        /// </summary>
        public float GetValue(BehaviorStat stat)
        {
            return stat switch
            {
                BehaviorStat.Aggression => aggression,
                BehaviorStat.Caution => caution,
                BehaviorStat.ReactionTime => reactionTime,
                BehaviorStat.Accuracy => accuracy,
                BehaviorStat.DashCooldown => dashCooldown,
                BehaviorStat.SpeedBoostCooldown => speedBoostCooldown,
                BehaviorStat.IdleTime => idleTime,
                _ => 50f
            };
        }
        
        /// <summary>
        /// Apply a difficulty preset to this profile
        /// </summary>
        [ContextMenu("Apply Difficulty Preset")]
        public void ApplyDifficultyPreset()
        {
            switch (difficultyLevel)
            {
                case DifficultyLevel.Easy:
                    ApplyEasyPreset();
                    break;
                case DifficultyLevel.Medium:
                    ApplyMediumPreset();
                    break;
                case DifficultyLevel.Hard:
                    ApplyHardPreset();
                    break;
                case DifficultyLevel.Expert:
                    ApplyExpertPreset();
                    break;
            }
        }
        
        private void ApplyEasyPreset()
        {
            aggression = 30f;
            caution = 70f;
            reactionTime = 0.8f;
            accuracy = 50f;
            dashCooldown = 4f;
            speedBoostCooldown = 8f;
            preferredAttackDistance = 10f;
            idleTime = 8f;
            movementSpeedMultiplier = 0.8f;
            retreatThreshold = 0.5f;
            Debug.Log("Applied Easy difficulty preset");
        }
        
        private void ApplyMediumPreset()
        {
            aggression = 50f;
            caution = 50f;
            reactionTime = 0.4f;
            accuracy = 70f;
            dashCooldown = 2.5f;
            speedBoostCooldown = 5f;
            preferredAttackDistance = 8f;
            idleTime = 5f;
            movementSpeedMultiplier = 1f;
            retreatThreshold = 0.3f;
            Debug.Log("Applied Medium difficulty preset");
        }
        
        private void ApplyHardPreset()
        {
            aggression = 70f;
            caution = 30f;
            reactionTime = 0.25f;
            accuracy = 85f;
            dashCooldown = 1.8f;
            speedBoostCooldown = 4f;
            preferredAttackDistance = 8f;
            idleTime = 3f;
            movementSpeedMultiplier = 1.2f;
            retreatThreshold = 0.2f;
            Debug.Log("Applied Hard difficulty preset");
        }
        
        private void ApplyExpertPreset()
        {
            aggression = 90f;
            caution = 20f;
            reactionTime = 0.15f;
            accuracy = 95f;
            dashCooldown = 1.2f;
            speedBoostCooldown = 3f;
            preferredAttackDistance = 8f;
            idleTime = 2f;
            movementSpeedMultiplier = 1.4f;
            retreatThreshold = 0.15f;
            preferWeakTargets = true;
            preferCloseTargets = false;
            targetSwitchInterval = 3f;
            Debug.Log("Applied Expert difficulty preset");
        }
        
        /// <summary>
        /// Create a randomized variation of this profile
        /// </summary>
        public AIBehaviorProfile CreateVariation(float variationAmount = 0.15f)
        {
            AIBehaviorProfile variation = Instantiate(this);
            
            variation.aggression = Mathf.Clamp(aggression + UnityEngine.Random.Range(-variationAmount * 100f, variationAmount * 100f), 0f, 100f);
            variation.caution = Mathf.Clamp(caution + UnityEngine.Random.Range(-variationAmount * 100f, variationAmount * 100f), 0f, 100f);
            variation.reactionTime = Mathf.Clamp(reactionTime + UnityEngine.Random.Range(-variationAmount, variationAmount), 0.1f, 2f);
            variation.accuracy = Mathf.Clamp(accuracy + UnityEngine.Random.Range(-variationAmount * 100f, variationAmount * 100f), 0f, 100f);
            
            return variation;
        }
        
        private void OnValidate()
        {
            // Ensure values stay in valid ranges
            aggression = Mathf.Clamp(aggression, 0f, 100f);
            caution = Mathf.Clamp(caution, 0f, 100f);
            accuracy = Mathf.Clamp(accuracy, 0f, 100f);
            reactionTime = Mathf.Max(0.1f, reactionTime);
            dashCooldown = Mathf.Max(0.5f, dashCooldown);
            speedBoostCooldown = Mathf.Max(2f, speedBoostCooldown);
            movementSpeedMultiplier = Mathf.Clamp(movementSpeedMultiplier, 0.5f, 1.5f);
            retreatThreshold = Mathf.Clamp01(retreatThreshold);
            returnToFightThreshold = Mathf.Clamp(returnToFightThreshold, retreatThreshold, 1f);
        }
    }
    
    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard,
        Expert
    }
}