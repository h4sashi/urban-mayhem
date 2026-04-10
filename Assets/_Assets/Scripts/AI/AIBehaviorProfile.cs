using UnityEngine;

namespace Hanzo.AI
{
    /// <summary>
    /// Scriptable Object for configuring AI behavior characteristics.
    /// Create different profiles for varying AI difficulty levels.
    /// </summary>
    [CreateAssetMenu(fileName = "AI Behavior Profile", menuName = "Hanzo/AI/Behavior Profile")]
    public class AIBehaviorProfile : ScriptableObject
    {
        [Header("Aggression")]
        [Tooltip("How likely AI is to engage in combat (0-1)")]
        [Range(0f, 1f)]
        public float aggressiveness = 0.7f;

        [Tooltip("How likely AI is to use dash ability (0-1)")]
        [Range(0f, 1f)]
        public float dashUseProbability = 0.8f;

        [Tooltip("How likely AI is to use speed boost (0-1)")]
        [Range(0f, 1f)]
        public float speedBoostUseProbability = 0.6f;

        [Header("Defensive Behavior")]
        [Tooltip("How likely AI is to evade when taking damage (0-1)")]
        [Range(0f, 1f)]
        public float evasionOnDamageChance = 0.7f;

        [Tooltip("How likely AI is to strafe during chase (0-1)")]
        [Range(0f, 1f)]
        public float strafeProbability = 0.5f;

        [Header("Accuracy & Timing")]
        [Tooltip("Base reaction time in seconds (lower = faster reactions)")]
        [Range(0.05f, 1f)]
        public float reactionTime = 0.2f;

        [Tooltip("How well AI predicts target movement (0-1)")]
        [Range(0f, 1f)]
        public float predictionAccuracy = 0.7f;

        [Tooltip("Base delay between decisions (seconds)")]
        [Range(0.1f, 1f)]
        public float decisionInterval = 0.2f;

        // ── Attack Desync Settings ────────────────────────────────────────────
        [Header("Attack Timing Desync")]
        [Tooltip("Extra one-time delay added at AI startup to stagger loop start times. " +
                 "Each AI instance randomises within this range so they never fire together.")]
        [Range(0f, 1f)]
        public float startupJitterMax = 0.6f;

        [Tooltip("Minimum random extra cooldown added after each attack attempt (seconds). " +
                 "Prevents both AIs landing hits in the same frame during a long fight.")]
        [Range(0f, 0.5f)]
        public float attackCooldownMin = 0.05f;

        [Tooltip("Maximum random extra cooldown added after each attack attempt (seconds).")]
        [Range(0f, 1f)]
        public float attackCooldownMax = 0.35f;

        [Tooltip("Every this many seconds the AI re-rolls its personal timing offset, " +
                 "so two AIs that drifted back into sync will diverge again.")]
        [Range(2f, 10f)]
        public float resyncCheckInterval = 4f;

        [Tooltip("When re-syncing, how large a timing nudge can be applied (seconds).")]
        [Range(0f, 0.4f)]
        public float resyncJitterMax = 0.2f;

        // ── Personality ───────────────────────────────────────────────────────
        [Header("Personality")]
        [Tooltip("How cautious vs reckless the AI is (0 = cautious, 1 = reckless)")]
        [Range(0f, 1f)]
        public float recklessness = 0.5f;

        [Tooltip("How focused AI stays on single target (0-1)")]
        [Range(0f, 1f)]
        public float targetPersistence = 0.8f;

        [Header("Patrol Behavior")]
        [Tooltip("Time AI waits at patrol waypoints")]
        [Range(0f, 5f)]
        public float waypointIdleTime = 1f;

        [Tooltip("Distance AI patrols from spawn point")]
        [Range(5f, 30f)]
        public float patrolRadius = 15f;

        public void Initialize()
        {
            aggressiveness          = 0.7f;
            dashUseProbability      = 0.8f;
            speedBoostUseProbability = 0.6f;
            evasionOnDamageChance   = 0.7f;
            strafeProbability       = 0.5f;
            reactionTime            = 0.2f;
            predictionAccuracy      = 0.7f;
            decisionInterval        = 0.2f;
            startupJitterMax        = 0.6f;
            attackCooldownMin       = 0.05f;
            attackCooldownMax       = 0.35f;
            resyncCheckInterval     = 4f;
            resyncJitterMax         = 0.2f;
            recklessness            = 0.5f;
            targetPersistence       = 0.8f;
            waypointIdleTime        = 1f;
            patrolRadius            = 15f;
        }
    }
}