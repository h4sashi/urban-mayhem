using System;
using UnityEngine;

namespace Hanzo.AI
{
    [CreateAssetMenu(fileName = "AI Utility Profile", menuName = "Hanzo/AI/Utility Profile")]
    public class AIUtilityProfile : ScriptableObject
    {
        [Serializable]
        public class ActionWeights
        {
            public float baseScore;
            public float aggressionWeight;
            public float dashPreferenceWeight;
            public float speedBoostPreferenceWeight;
            public float evasionPreferenceWeight;
            public float recklessnessWeight;
            public float cautionWeight;
            public float targetPersistenceWeight;
            public float distanceWeight;
            public float closeDangerWeight;
            public float dashRangeFitWeight;
            public float speedBoostRangeFitWeight;
            public float lowHealthWeight;
            public float recentDamageWeight;
            public float targetPriorityWeight;
            public float currentTargetWeight;
        }

        [Serializable]
        public class PersonalityWeights
        {
            public float chaseBias;
            public float dashBias;
            public float farDashBias;
            public float closeDashBias;
            public float destructibleDashBias;
            public float speedBoostBias;
            public float evadeBias;
            public float lowHealthEvadeBias;
            public float grudgeChaseBias;
            public float grudgeDashBias;
            public float grudgeDestructibleBias;
            public float grudgeSpeedBoostBias;
        }

        [Header("General")]
        [Range(0f, 1f)]
        public float stateInertia = 0.05f;

        [Range(0f, 1f)]
        public float patrolWithoutTarget = 0.68f;

        [Range(0f, 1f)]
        public float patrolWithTarget = 0.02f;

        [Range(0f, 1f)]
        public float evasionLowHealthThreshold = 0.45f;

        [Range(0f, 1f)]
        public float evasionRecentDamageThreshold = 0.15f;

        [Header("Actions")]
        public ActionWeights chase = new ActionWeights
        {
            baseScore = 0.28f,
            aggressionWeight = 0.24f,
            targetPersistenceWeight = 0.12f,
            distanceWeight = 0.22f,
            closeDangerWeight = -0.12f,
            targetPriorityWeight = 0.18f,
            currentTargetWeight = 0.08f,
        };

        public ActionWeights dash = new ActionWeights
        {
            baseScore = 0.12f,
            aggressionWeight = 0.16f,
            dashPreferenceWeight = 0.36f,
            recklessnessWeight = 0.14f,
            dashRangeFitWeight = 0.26f,
            targetPriorityWeight = 0.1f,
        };

        public ActionWeights destructibleDash = new ActionWeights
        {
            baseScore = 0.1f,
            dashPreferenceWeight = 0.24f,
            recklessnessWeight = 0.18f,
            distanceWeight = 0.16f,
            targetPriorityWeight = 0.12f,
        };

        public ActionWeights speedBoost = new ActionWeights
        {
            baseScore = 0.08f,
            speedBoostPreferenceWeight = 0.38f,
            speedBoostRangeFitWeight = 0.28f,
            lowHealthWeight = 0.08f,
            targetPriorityWeight = 0.06f,
        };

        public ActionWeights evade = new ActionWeights
        {
            evasionPreferenceWeight = 0.28f,
            closeDangerWeight = 0.44f,
            lowHealthWeight = 0.28f,
            recentDamageWeight = 0.18f,
            cautionWeight = 0.12f,
            recklessnessWeight = -0.1f,
        };

        [Header("Personalities")]
        public PersonalityWeights hunter = new PersonalityWeights
        {
            chaseBias = 0.16f,
            dashBias = 0.06f,
            farDashBias = 0.09f,
            destructibleDashBias = -0.12f,
            speedBoostBias = 0.14f,
            evadeBias = -0.28f,
            lowHealthEvadeBias = 0.26f,
        };

        public PersonalityWeights brawler = new PersonalityWeights
        {
            chaseBias = 0.1f,
            dashBias = 0.28f,
            closeDashBias = 0.08f,
            destructibleDashBias = -0.1f,
            speedBoostBias = -0.12f,
            evadeBias = -0.34f,
        };

        public PersonalityWeights trickster = new PersonalityWeights
        {
            chaseBias = -0.04f,
            dashBias = -0.08f,
            destructibleDashBias = 0.42f,
            speedBoostBias = 0.1f,
            evadeBias = 0.1f,
        };

        public PersonalityWeights survivor = new PersonalityWeights
        {
            chaseBias = -0.12f,
            dashBias = -0.36f,
            destructibleDashBias = -0.28f,
            speedBoostBias = 0.18f,
            evadeBias = 0.32f,
            lowHealthEvadeBias = 0.2f,
        };

        public PersonalityWeights rival = new PersonalityWeights
        {
            chaseBias = 0.1f,
            evadeBias = -0.1f,
            lowHealthEvadeBias = 0.14f,
            grudgeChaseBias = 0.22f,
            grudgeDashBias = 0.22f,
            grudgeDestructibleBias = 0.11f,
            grudgeSpeedBoostBias = 0.08f,
        };

        public float Evaluate(
            AIUtilityAction action,
            AIUtilityContext context,
            AIPersonality personality,
            AIBehaviorProfile behavior,
            AIUtilityAction activeAction
        )
        {
            if (behavior == null)
                return 0f;

            float score;
            switch (action)
            {
                case AIUtilityAction.Patrol:
                    score = context.hasTarget ? patrolWithTarget : patrolWithoutTarget;
                    break;
                case AIUtilityAction.Chase:
                    if (!context.hasTarget)
                        return 0f;
                    score = ScoreAction(chase, context, behavior);
                    break;
                case AIUtilityAction.Dash:
                    if (!context.hasTarget || !context.inDashRange)
                        return 0f;
                    score = ScoreAction(dash, context, behavior);
                    break;
                case AIUtilityAction.DestructibleDash:
                    if (!context.hasTarget || !context.hasLaunchableDestructible)
                        return 0f;
                    score = ScoreAction(destructibleDash, context, behavior);
                    break;
                case AIUtilityAction.SpeedBoost:
                    if (!context.hasTarget || !context.canSpeedBoost)
                        return 0f;
                    score = ScoreAction(speedBoost, context, behavior);
                    break;
                case AIUtilityAction.Evade:
                    if (!context.hasTarget || !IsThreatened(context))
                        return 0f;
                    score = ScoreAction(evade, context, behavior);
                    break;
                default:
                    return 0f;
            }

            score += GetPersonalityBias(action, context, personality);

            if (action == activeAction)
                score += stateInertia;

            return Mathf.Clamp01(score);
        }

        private float ScoreAction(
            ActionWeights weights,
            AIUtilityContext context,
            AIBehaviorProfile behavior
        )
        {
            if (weights == null)
                return 0f;

            float caution = 1f - behavior.recklessness;
            return weights.baseScore
                + behavior.aggressiveness * weights.aggressionWeight
                + behavior.dashUseProbability * weights.dashPreferenceWeight
                + behavior.speedBoostUseProbability * weights.speedBoostPreferenceWeight
                + behavior.evasionOnDamageChance * weights.evasionPreferenceWeight
                + behavior.recklessness * weights.recklessnessWeight
                + caution * weights.cautionWeight
                + behavior.targetPersistence * weights.targetPersistenceWeight
                + context.normalizedDistance * weights.distanceWeight
                + context.closeDanger * weights.closeDangerWeight
                + context.dashRangeFit * weights.dashRangeFitWeight
                + context.speedBoostRangeFit * weights.speedBoostRangeFitWeight
                + context.lowHealthPressure * weights.lowHealthWeight
                + context.recentDamagePressure * weights.recentDamageWeight
                + context.targetPriority * weights.targetPriorityWeight
                + (context.isCurrentTarget ? weights.currentTargetWeight : 0f);
        }

        private bool IsThreatened(AIUtilityContext context)
        {
            return context.closeDanger > 0f
                || context.lowHealthPressure > evasionLowHealthThreshold
                || context.recentDamagePressure > evasionRecentDamageThreshold;
        }

        private float GetPersonalityBias(
            AIUtilityAction action,
            AIUtilityContext context,
            AIPersonality personality
        )
        {
            PersonalityWeights weights = GetPersonalityWeights(personality);
            if (weights == null)
                return 0f;

            float bias = 0f;
            switch (action)
            {
                case AIUtilityAction.Chase:
                    bias += weights.chaseBias;
                    break;
                case AIUtilityAction.Dash:
                    bias += weights.dashBias;
                    if (context.distanceToTarget > 0f && context.normalizedDistance > 0.7f)
                        bias += weights.farDashBias;
                    bias += context.closeDanger * weights.closeDashBias;
                    break;
                case AIUtilityAction.DestructibleDash:
                    bias += weights.destructibleDashBias;
                    break;
                case AIUtilityAction.SpeedBoost:
                    bias += weights.speedBoostBias;
                    break;
                case AIUtilityAction.Evade:
                    bias += weights.evadeBias;
                    bias += context.lowHealthPressure * weights.lowHealthEvadeBias;
                    break;
            }

            if (context.targetRecentlyDamagedUs)
            {
                switch (action)
                {
                    case AIUtilityAction.Chase:
                        bias += weights.grudgeChaseBias;
                        break;
                    case AIUtilityAction.Dash:
                        bias += weights.grudgeDashBias;
                        break;
                    case AIUtilityAction.DestructibleDash:
                        bias += weights.grudgeDestructibleBias;
                        break;
                    case AIUtilityAction.SpeedBoost:
                        bias += weights.grudgeSpeedBoostBias;
                        break;
                }
            }

            return bias;
        }

        private PersonalityWeights GetPersonalityWeights(AIPersonality personality)
        {
            switch (personality)
            {
                case AIPersonality.Hunter:
                    return hunter;
                case AIPersonality.Brawler:
                    return brawler;
                case AIPersonality.Trickster:
                    return trickster;
                case AIPersonality.Survivor:
                    return survivor;
                case AIPersonality.Rival:
                    return rival;
                default:
                    return null;
            }
        }
    }
}
