using UnityEngine;

namespace Hanzo.AI
{
    public enum AIUtilityAction
    {
        None,
        Patrol,
        Chase,
        Dash,
        DestructibleDash,
        SpeedBoost,
        Evade,
    }

    public struct AIUtilityContext
    {
        public Transform target;
        public Transform utilityTarget;
        public bool hasTarget;
        public bool canDash;
        public bool inDashRange;
        public bool canSpeedBoost;
        public bool hasLaunchableDestructible;
        public bool targetRecentlyDamagedUs;
        public bool isCurrentTarget;
        public float distanceToTarget;
        public float normalizedDistance;
        public float targetPriority;
        public float adaptiveSafeDistance;
        public float closeDanger;
        public float dashRangeFit;
        public float speedBoostRangeFit;
        public float recentDamagePressure;
        public float lowHealthPressure;
    }

    public struct AIUtilityDecision
    {
        public AIUtilityAction action;
        public Transform target;
        public Transform utilityTarget;
        public float score;

        public AIUtilityDecision(
            AIUtilityAction action,
            Transform target,
            Transform utilityTarget,
            float score
        )
        {
            this.action = action;
            this.target = target;
            this.utilityTarget = utilityTarget;
            this.score = score;
        }
    }
}
