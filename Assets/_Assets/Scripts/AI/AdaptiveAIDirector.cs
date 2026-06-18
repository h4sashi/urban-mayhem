using System.Collections.Generic;
using Hanzo.Networking;
using Hanzo.Player.Controllers;
using Hanzo.Player.Core;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.AI
{
    public class AdaptiveAIDirector : MonoBehaviour
    {
        public static AdaptiveAIDirector Instance { get; private set; }

        [Header("Adaptive Tuning")]
        [SerializeField]
        private float tuningInterval = 12f;

        [SerializeField]
        [Range(0f, 1f)]
        private float maxDifficultyPressure = 0.85f;

        [SerializeField]
        private float recentDamageMemorySeconds = 10f;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        private readonly List<AIPlayerController> aiControllers = new List<AIPlayerController>();
        private float nextTuneTime;
        private float lastMatchPressure;

        public float LastMatchPressure => lastMatchPressure;

        public static AdaptiveAIDirector EnsureExists()
        {
            if (Instance != null)
                return Instance;

            AdaptiveAIDirector existing = FindObjectOfType<AdaptiveAIDirector>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            GameObject directorObject = new GameObject("Adaptive AI Director");
            DontDestroyOnLoad(directorObject);
            return directorObject.AddComponent<AdaptiveAIDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Time.time < nextTuneTime)
                return;

            nextTuneTime = Time.time + Mathf.Max(1f, tuningInterval);
            TuneAllAI();
        }

        public void RegisterAI(AIPlayerController ai)
        {
            if (ai == null || aiControllers.Contains(ai))
                return;

            aiControllers.Add(ai);
            ai.ApplyAdaptiveTuning(BuildTuningFor(ai, CalculateMatchPressure()));
        }

        public void UnregisterAI(AIPlayerController ai)
        {
            if (ai == null)
                return;

            aiControllers.Remove(ai);
        }

        public float GetTargetPriority(AIPlayerController ai, Transform target, float distance)
        {
            bool hasLaunchableDestructible =
                ai != null && target != null && ai.HasLaunchableDestructibleToward(target);

            return GetTargetPriority(ai, target, distance, hasLaunchableDestructible);
        }

        public float GetTargetPriority(
            AIPlayerController ai,
            Transform target,
            float distance,
            bool hasLaunchableDestructible
        )
        {
            if (ai == null || target == null)
                return 0f;

            float priority = 0f;
            float healthPressure = GetTargetHealthPressure(target);
            float performance = GetTargetPerformanceScore(target);
            float weakPerformance = Mathf.Clamp01(1f - performance / 45f);
            bool recentlyUsedAbility = TargetRecentlyUsedAbility(target);

            switch (ai.Personality)
            {
                case AIPersonality.Madman:
                    priority += ai.WasRecentlyDamagedBy(target, AIPlayerController.GrudgeMemorySeconds)
                        ? 8f
                        : 0f;
                    priority += hasLaunchableDestructible ? 7f : 0f;
                    priority += Mathf.Max(0f, 10f - distance) * 0.25f;
                    break;
                case AIPersonality.Hunter:
                    priority += healthPressure * 10f;
                    priority += recentlyUsedAbility ? 2f : 0f;
                    priority += GetHumanThreatScore(target) * 0.75f;
                    break;
                case AIPersonality.Rival:
                    if (ai.WasRecentlyDamagedBy(target, AIPlayerController.GrudgeMemorySeconds))
                        priority += 14f;
                    priority += GetHumanThreatScore(target) * 1.5f;
                    break;
                case AIPersonality.Opportunist:
                    priority += healthPressure * 7f;
                    priority += recentlyUsedAbility ? 5f : 0f;
                    priority += distance > 5f ? 1.5f : -1f;
                    break;
                case AIPersonality.Brawler:
                    priority += Mathf.Max(0f, 9f - distance) * 0.6f;
                    break;
                case AIPersonality.Trapper:
                    priority += hasLaunchableDestructible ? 8f : 0f;
                    priority += distance <= 12f ? 1.5f : -1f;
                    break;
                case AIPersonality.Coward:
                    priority += healthPressure * 8f;
                    priority += weakPerformance * 4f;
                    priority -= Mathf.Clamp(performance / 8f, 0f, 7f);
                    break;
                case AIPersonality.Bully:
                    priority += weakPerformance * 9f;
                    priority += healthPressure * 3f;
                    priority -= Mathf.Clamp(performance / 10f, 0f, 5f);
                    break;
                case AIPersonality.Cleaner:
                    priority += healthPressure * 9f;
                    priority += recentlyUsedAbility ? 2f : 0f;
                    priority -= hasLaunchableDestructible ? 1f : 0f;
                    break;
                case AIPersonality.Berserker:
                    priority += ai.LowHealthPressure * 6f;
                    priority += healthPressure * Mathf.Lerp(2f, 7f, ai.LowHealthPressure);
                    priority += hasLaunchableDestructible && ai.LowHealthPressure > 0.5f ? 4f : 0f;
                    break;
                case AIPersonality.Sniper:
                    priority += distance >= 8f ? 4f : -4f;
                    priority += recentlyUsedAbility ? 3f : 0f;
                    break;
                case AIPersonality.Defender:
                    priority += distance <= 10f ? 3f : -3f;
                    priority += hasLaunchableDestructible ? 1.5f : 0f;
                    break;
                case AIPersonality.Trickster:
                    if (hasLaunchableDestructible)
                        priority += 6f;
                    priority += Mathf.PingPong(Time.time * 0.45f, 1.5f);
                    break;
                case AIPersonality.Executioner:
                    priority += recentlyUsedAbility ? 8f : 0f;
                    priority += healthPressure * 4f;
                    priority += hasLaunchableDestructible ? 3f : 0f;
                    break;
                case AIPersonality.PackRat:
                    priority += recentlyUsedAbility ? 4f : -1f;
                    priority += weakPerformance * 2f;
                    break;
            }

            if (ai.CurrentTarget != null && ai.CurrentTarget.root == target.root)
                priority += ai.TargetPersistence * 3f;

            return priority;
        }

        private void TuneAllAI()
        {
            CleanupNullAI();
            lastMatchPressure = CalculateMatchPressure();

            for (int i = 0; i < aiControllers.Count; i++)
            {
                AIPlayerController ai = aiControllers[i];
                if (ai == null)
                    continue;

                ai.ApplyAdaptiveTuning(BuildTuningFor(ai, lastMatchPressure));
            }

            if (showDebugInfo)
            {
                Debug.Log(
                    $"[AdaptiveAI] Tuned {aiControllers.Count} AIs | pressure={lastMatchPressure:F2}"
                );
            }
        }

        private AIAdaptiveTuning BuildTuningFor(AIPlayerController ai, float matchPressure)
        {
            float recentDamagePressure =
                ai != null ? ai.GetRecentDamagePressure(recentDamageMemorySeconds) : 0f;

            float individualPressure = Mathf.Clamp(
                matchPressure + recentDamagePressure * 0.25f,
                -maxDifficultyPressure,
                maxDifficultyPressure
            );

            return new AIAdaptiveTuning
            {
                matchPressure = individualPressure,
                recentDamagePressure = recentDamagePressure,
                lowHealthPressure = ai != null ? ai.LowHealthPressure : 0f,
                activeAICount = Mathf.Max(1, aiControllers.Count),
            };
        }

        private float CalculateMatchPressure()
        {
            NetworkedScoreManager scoreManager = NetworkedScoreManager.Instance;

            float humanScore = 0f;
            float humanDeaths = 0f;
            float humanHitsTaken = 0f;
            int humanCount = 0;

            if (scoreManager != null && PhotonNetwork.CurrentRoom != null)
            {
                foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
                {
                    humanCount++;
                    humanScore += scoreManager.GetPlayerScore(player.ActorNumber);
                    humanDeaths += scoreManager.GetPlayerDeaths(player.ActorNumber);
                    humanHitsTaken += scoreManager.GetPlayerHitsTaken(player.ActorNumber);
                }
            }

            float aiScore = 0f;
            float aiDeaths = 0f;
            float aiHitsTaken = 0f;
            int aiCount = 0;

            for (int i = 0; i < aiControllers.Count; i++)
            {
                AIPlayerController ai = aiControllers[i];
                if (ai == null)
                    continue;

                aiCount++;

                if (scoreManager != null)
                {
                    int aiId = scoreManager.GetAIId(ai.gameObject);
                    if (aiId != 0)
                    {
                        aiScore += scoreManager.GetAIScore(aiId);
                        aiDeaths += scoreManager.GetAIDeaths(aiId);
                        aiHitsTaken += scoreManager.GetAIHitsTaken(aiId);
                        continue;
                    }
                }

                aiDeaths += ai.DeathCount;
                aiHitsTaken += ai.DamageEventsTaken;
            }

            if (humanCount == 0)
            {
                float localAIPressure = aiCount > 0 ? aiHitsTaken / Mathf.Max(1f, aiCount * 8f) : 0f;
                return Mathf.Clamp(localAIPressure, -maxDifficultyPressure, maxDifficultyPressure);
            }

            float averageHumanScore = humanScore / Mathf.Max(1, humanCount);
            float averageAIScore = aiScore / Mathf.Max(1, aiCount);
            float averageHumanDeaths = humanDeaths / Mathf.Max(1, humanCount);
            float averageAIDeaths = aiDeaths / Mathf.Max(1, aiCount);
            float averageHumanHitsTaken = humanHitsTaken / Mathf.Max(1, humanCount);
            float averageAIHitsTaken = aiHitsTaken / Mathf.Max(1, aiCount);

            float scorePressure = (averageHumanScore - averageAIScore) / 40f;
            float deathPressure = (averageAIDeaths - averageHumanDeaths) / 4f;
            float hitPressure = (averageAIHitsTaken - averageHumanHitsTaken) / 8f;

            return Mathf.Clamp(
                scorePressure + deathPressure + hitPressure,
                -maxDifficultyPressure,
                maxDifficultyPressure
            );
        }

        private float GetHumanThreatScore(Transform target)
        {
            if (target == null || target.GetComponentInParent<AIPlayerController>() != null)
                return 0f;

            NetworkedScoreManager scoreManager = NetworkedScoreManager.Instance;
            PhotonView photonView = target.GetComponentInParent<PhotonView>();
            if (scoreManager == null || photonView == null || photonView.Owner == null)
                return 0f;

            int actorNumber = photonView.Owner.ActorNumber;
            float score = scoreManager.GetPlayerScore(actorNumber) / 20f;
            float kills = scoreManager.GetPlayerKills(actorNumber) * 0.75f;
            float deaths = scoreManager.GetPlayerDeaths(actorNumber) * 0.25f;

            return Mathf.Clamp(score + kills - deaths, 0f, 8f);
        }

        private float GetTargetHealthPressure(Transform target)
        {
            PlayerHealthComponent health =
                target != null ? target.GetComponentInParent<PlayerHealthComponent>() : null;

            if (health == null || health.MaxHealth <= 0f)
                return 0f;

            return 1f - Mathf.Clamp01(health.CurrentHealth / health.MaxHealth);
        }

        private bool TargetRecentlyUsedAbility(Transform target)
        {
            PlayerMovementController movement =
                target != null ? target.GetComponentInParent<PlayerMovementController>() : null;

            if (movement == null)
                return false;

            bool dashCommitted =
                movement.DashAbility != null
                && (movement.DashAbility.IsActive || movement.DashAbility.CooldownRemaining > 0.15f);

            bool boostCommitted =
                movement.SpeedBoostAbility != null
                && (
                    movement.SpeedBoostAbility.IsActive
                    || movement.SpeedBoostAbility.CooldownRemaining > 0.15f
                );

            return dashCommitted || boostCommitted;
        }

        private float GetTargetPerformanceScore(Transform target)
        {
            if (target == null)
                return 0f;

            NetworkedScoreManager scoreManager = NetworkedScoreManager.Instance;
            if (scoreManager == null)
                return 0f;

            AIPlayerController targetAI = target.GetComponentInParent<AIPlayerController>();
            if (targetAI != null && targetAI.AIId != 0)
            {
                int aiId = targetAI.AIId;
                return scoreManager.GetAIScore(aiId)
                    + scoreManager.GetAIKills(aiId) * 10f
                    - scoreManager.GetAIDeaths(aiId) * 4f;
            }

            PhotonView photonView = target.GetComponentInParent<PhotonView>();
            if (photonView == null || photonView.Owner == null)
                return 0f;

            int actorNumber = photonView.Owner.ActorNumber;
            return scoreManager.GetPlayerScore(actorNumber)
                + scoreManager.GetPlayerKills(actorNumber) * 10f
                - scoreManager.GetPlayerDeaths(actorNumber) * 4f;
        }

        private void CleanupNullAI()
        {
            for (int i = aiControllers.Count - 1; i >= 0; i--)
            {
                if (aiControllers[i] == null)
                    aiControllers.RemoveAt(i);
            }
        }
    }
}
