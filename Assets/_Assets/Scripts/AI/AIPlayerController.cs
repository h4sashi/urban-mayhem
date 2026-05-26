using System.Collections;
using System.Collections.Generic;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Controllers;
using Hanzo.Player.Core;
using Hanzo.Player.Input;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.AI
{
    /// <summary>
    /// AI Controller that drives a player prefab with all player components.
    /// Works seamlessly with PlayerInputHandler, PlayerMovementController, and PlayerHealthComponent.
    ///
    /// DESYNC SYSTEM — how simultaneous attacks are prevented:
    ///
    ///   1. STARTUP JITTER   — each AI waits a unique random delay before its loop begins,
    ///                         so loop ticks are never aligned from the first frame.
    ///   2. ATTACK COOLDOWN  — after every dash attempt the AI draws a fresh random extra
    ///                         pause before it is allowed to dash again.  Because each AI
    ///                         draws independently, their attack windows drift apart.
    ///   3. RESYNC GUARD     — every few seconds each AI re-rolls a small timing nudge,
    ///                         ensuring that even if two AIs happen to drift back into phase
    ///                         they will diverge again within one check interval.
    /// </summary>
    [RequireComponent(typeof(PlayerInputHandler))]
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerHealthComponent))]
    public class AIPlayerController : MonoBehaviour
    {
        [Header("AI Behavior Settings")]
        [SerializeField]
        private AIBehaviorProfile behaviorProfile;

        [Header("Adaptive AI")]
        [SerializeField]
        private bool enableAdaptiveTuning = true;

        [SerializeField]
        private AIPersonality personality = AIPersonality.Auto;

        [Header("Target Detection")]
        [SerializeField]
        private float detectionRadius = 20f;

        [SerializeField]
        private LayerMask targetLayer;

        [SerializeField]
        private float visionAngle = 120f;

        [Header("Combat Settings")]
        [SerializeField]
        private float dashRange = 8f;

        [SerializeField]
        private float destructibleDashRange = 12f;

        [SerializeField]
        private float minDashDistance = 3f;

        [SerializeField]
        private float speedBoostRange = 15f;

        [SerializeField]
        private float safeDistance = 5f;

        [Header("Movement Settings")]
        [SerializeField]
        private float strafeSpeed = 3f;

        [SerializeField]
        private float repositionInterval = 2f;

        [SerializeField]
        private float predictionTime = 0.5f;

        [Header("Decision Making")]
        [SerializeField]
        private float decisionInterval = 0.2f;

        [SerializeField]
        private float reactionTime = 0.1f;

        // NEW — random ranges for loop timing variation
        [SerializeField]
        private float reactionTimeMin = 0.08f;

        [SerializeField]
        private float reactionTimeMax = 0.25f;

        [SerializeField]
        private float decisionIntervalMin = 0.15f;

        [SerializeField]
        private float decisionIntervalMax = 0.4f;

        [Header("Patrol Settings")]
        [SerializeField]
        private float patrolRadius = 15f;

        [SerializeField]
        private float waypointReachedDistance = 2f;

        [SerializeField]
        private float idleTimeAtWaypoint = 1f;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = true;

        [SerializeField]
        private bool showGizmos = true;

        private Transform _pendingDestructibleTarget;
        private AIBehaviorProfile baseBehaviorProfile;
        private AIBehaviorProfile runtimeBehaviorProfile;
        private AIPersonality resolvedPersonality = AIPersonality.Auto;
        private AdaptiveAIDirector adaptiveDirector;
        private AIAdaptiveTuning lastAdaptiveTuning = AIAdaptiveTuning.Neutral;
        private int aiId;
        private string aiDisplayName;

        // ── Component references ──────────────────────────────────────────────
        private PlayerInputHandler inputHandler;
        private PlayerMovementController movementController;
        private PlayerHealthComponent healthComponent;
        private PlayerStateController stateController;
        private DashCollisionHandler dashCollisionHandler;

        // ── AI state ──────────────────────────────────────────────────────────
        private AIState currentState = AIState.Idle;
        private Transform currentTarget;
        private Vector3 moveDirection;
        private Vector3 lastKnownTargetPosition;
        private float lastRepositionTime;
        private float currentDecisionDelay = 0.2f;
        private bool isExecutingDash;
        private bool isExecutingSpeedBoost;
        private bool isDyingStun;

        // ── Patrol state ──────────────────────────────────────────────────────
        private Vector3 patrolCenter;
        private Vector3 currentWaypoint;
        private float idleTimer;
        private bool isWaitingAtWaypoint;

        // ── Evasion ───────────────────────────────────────────────────────────
        private Vector3 evasionDirection;
        private float evasionTimer;
        private const float EVASION_DURATION = 1f;

        // ── Desync timing ─────────────────────────────────────────────────────
        private float attackCooldownTimer = 0f; // counts down; dash blocked while > 0
        private float resyncTimer = 0f; // counts down to next timing nudge

        // ── Adaptive memory ───────────────────────────────────────────────────
        private Transform lastDamageSourceRoot;
        private Transform lastKillerRoot;
        private float lastDamageTime = -999f;
        private float lastDeathTime = -999f;
        private int damageEventsTaken;
        private int deathCount;
        private int dashAttempts;
        private float adaptivePressure;

        // ── Coroutines ────────────────────────────────────────────────────────
        private Coroutine aiLoopCoroutine;

        // ── Properties ───────────────────────────────────────────────────────
        public AIState CurrentState => currentState;
        public Transform CurrentTarget => currentTarget;
        public AIPersonality Personality => resolvedPersonality;
        public int DamageEventsTaken => damageEventsTaken;
        public int DeathCount => deathCount;
        public float AdaptivePressure => adaptivePressure;
        public float TargetPersistence => Profile != null ? Profile.targetPersistence : 0f;
        public float LowHealthPressure => 1f - HealthPercent;
        public int AIId => aiId;
        public string AIDisplayName => AINameCatalog.GetNameForId(aiId);

        private AIBehaviorProfile Profile => runtimeBehaviorProfile != null
            ? runtimeBehaviorProfile
            : behaviorProfile;

        private float HealthPercent
        {
            get
            {
                if (healthComponent == null || healthComponent.MaxHealth <= 0f)
                    return 1f;

                return Mathf.Clamp01(healthComponent.CurrentHealth / healthComponent.MaxHealth);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            inputHandler = GetComponent<PlayerInputHandler>();
            movementController = GetComponent<PlayerMovementController>();
            healthComponent = GetComponent<PlayerHealthComponent>();
            stateController = GetComponent<PlayerStateController>();
            dashCollisionHandler = GetComponent<DashCollisionHandler>();
            ReadIdentityFromPhotonData();

            if (inputHandler == null || !inputHandler.IsAIControlled())
                Debug.LogError("[AI] PlayerInputHandler did not detect AIPlayerController!");

            if (behaviorProfile == null)
            {
                behaviorProfile = ScriptableObject.CreateInstance<AIBehaviorProfile>();
                behaviorProfile.Initialize();
                Debug.LogWarning("[AI] No behavior profile assigned, using defaults");
            }

            baseBehaviorProfile = behaviorProfile;
            resolvedPersonality = ResolvePersonality(personality);
            CreateRuntimeProfile();
            ApplyAdaptiveTuning(AIAdaptiveTuning.Neutral);

            patrolCenter = transform.position;

            if (healthComponent != null)
                healthComponent.SetRespawnPosition(transform.position);

            // Seed this AI's first personal attack delay with a unique random value
            // so no two AIs on the same frame begin with the same rhythm.
            RollNewAttackCooldown();
            resyncTimer = Profile.resyncCheckInterval;
        }

        private void OnEnable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnDamageTaken += OnDamageTaken;
                healthComponent.OnPlayerDied += OnDeath;
                healthComponent.OnPlayerRespawned += OnRespawn;
                healthComponent.OnPlayerDied += OnDeathStunLock;
            }

            adaptiveDirector = AdaptiveAIDirector.EnsureExists();
            adaptiveDirector.RegisterAI(this);

            if (aiLoopCoroutine != null)
                StopCoroutine(aiLoopCoroutine);
            aiLoopCoroutine = StartCoroutine(AILoop());
        }

        private void OnDisable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnDamageTaken -= OnDamageTaken;
                healthComponent.OnPlayerDied -= OnDeath;
                healthComponent.OnPlayerRespawned -= OnRespawn;
                healthComponent.OnPlayerDied -= OnDeathStunLock;
            }

            if (adaptiveDirector != null)
                adaptiveDirector.UnregisterAI(this);

            if (aiLoopCoroutine != null)
                StopCoroutine(aiLoopCoroutine);
        }

        private void Update()
        {
            // Tick the attack cooldown timer every frame for accuracy
            if (attackCooldownTimer > 0f)
                attackCooldownTimer -= Time.deltaTime;

            // Periodic resync guard — nudge this AI's timing so it can't
            // accidentally lock back in phase with another AI over a long fight.
            if (resyncTimer > 0f)
            {
                resyncTimer -= Time.deltaTime;
                if (resyncTimer <= 0f)
                {
                    float nudge = Random.Range(0f, Profile.resyncJitterMax);
                    attackCooldownTimer += nudge;
                    resyncTimer = Profile.resyncCheckInterval;

                    if (showDebugInfo)
                        Debug.Log($"[AI:{name}] Resync nudge +{nudge:F3}s applied.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // MAIN AI LOOP
        // ─────────────────────────────────────────────────────────────────────

        private IEnumerator AILoop()
        {
            float startupJitter = Random.Range(0f, Profile.startupJitterMax);
            yield return new WaitForSeconds(startupJitter);

            if (showDebugInfo)
                Debug.Log($"[AI:{name}] Loop started after {startupJitter:F3}s startup jitter.");

            while (true)
            {
                bool paused =
                    !healthComponent.IsAlive
                    || (stateController != null && stateController.IsStunned);

                if (paused)
                {
                    StopMovement();
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // Each tick draws a fresh random reaction time and decision interval
                // so no two AIs ever share the same loop cadence for long
                float thisReaction = GetRandomReactionDelay();
                float thisDecision = GetRandomDecisionDelay();
                currentDecisionDelay = thisDecision;

                yield return new WaitForSeconds(thisReaction);

                MakeDecisions();
                ExecuteCurrentState();

                yield return new WaitForSeconds(thisDecision);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // DECISION MAKING
        // ─────────────────────────────────────────────────────────────────────

        private void MakeDecisions()
        {
            if (!healthComponent.IsAlive)
                return;

            if (evasionTimer > 0)
            {
                currentState = AIState.Evading;
                return;
            }

            FindTarget();

            if (currentTarget != null)
            {
                float dist = Vector3.Distance(transform.position, currentTarget.position);
                float adaptiveSafeDistance = GetAdaptiveSafeDistance();
                bool canDash = CanUseDash();

                if (
                    canDash
                    && resolvedPersonality == AIPersonality.Trickster
                    && ShouldUseDestructibleDash()
                    && TryReserveLaunchableDestructibleToward(currentTarget)
                )
                {
                    currentState = AIState.DashingDestructible;
                }
                else if (dist <= dashRange && dist >= minDashDistance && canDash && ShouldUseDash(dist))
                {
                    currentState = AIState.Dashing;
                }
                // NEW — check for a nearby destructible worth launching at the target
                else if (
                    canDash
                    && ShouldUseDestructibleDash()
                    && TryReserveLaunchableDestructibleToward(currentTarget)
                )
                {
                    currentState = AIState.DashingDestructible;
                }
                else if (dist <= adaptiveSafeDistance && ShouldEvadeAtDistance())
                {
                    currentState = AIState.Evading;
                    InitiateEvasion();
                }
                else if (dist <= speedBoostRange && CanUseSpeedBoost())
                {
                    currentState = AIState.SpeedBoosting;
                }
                else
                {
                    currentState = AIState.Chasing;
                }
            }
            else if (currentState != AIState.Patrolling)
            {
                GenerateNewWaypoint();
                currentState = AIState.Patrolling;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // STATE EXECUTION
        // ─────────────────────────────────────────────────────────────────────

        private void ExecuteCurrentState()
        {
            switch (currentState)
            {
                case AIState.Idle:
                    ExecuteIdle();
                    break;
                case AIState.Patrolling:
                    ExecutePatrol();
                    break;
                case AIState.Chasing:
                    ExecuteChase();
                    break;
                case AIState.Dashing:
                    ExecuteDash();
                    break;
                case AIState.SpeedBoosting:
                    ExecuteSpeedBoost();
                    break;
                case AIState.Evading:
                    ExecuteEvasion();
                    break;
                case AIState.DashingDestructible:
                    ExecuteDestructibleDash();
                    break;
            }
        }

        private void ExecuteIdle() => StopMovement();

        private void ExecutePatrol()
        {
            if (Vector3.Distance(transform.position, currentWaypoint) < waypointReachedDistance)
            {
                if (!isWaitingAtWaypoint)
                {
                    isWaitingAtWaypoint = true;
                    idleTimer = Profile != null ? Profile.waypointIdleTime : idleTimeAtWaypoint;
                    StopMovement();
                }
                else
                {
                    idleTimer -= currentDecisionDelay;
                    if (idleTimer <= 0)
                    {
                        // Drift the center toward where we just arrived
                        // so the next waypoint fans out from here, not spawn
                        patrolCenter = Vector3.Lerp(patrolCenter, currentWaypoint, 0.4f);
                        GenerateNewWaypoint();
                        isWaitingAtWaypoint = false;
                    }
                }
            }
            else
            {
                MoveInDirection((currentWaypoint - transform.position).normalized);
            }
        }

        /// <summary>
        /// Returns true if there's a destructible object between the AI and its
        /// target that's worth dashing into — i.e. it's closer than the target
        /// and roughly on the path toward them.
        /// </summary>
        public bool HasLaunchableDestructibleToward(Transform target)
        {
            return TryFindLaunchableDestructibleToward(target, out _);
        }

        private bool TryReserveLaunchableDestructibleToward(Transform target)
        {
            if (!TryFindLaunchableDestructibleToward(target, out Transform destructible))
                return false;

            _pendingDestructibleTarget = destructible;
            return true;
        }

        private bool TryFindLaunchableDestructibleToward(
            Transform target,
            out Transform destructible
        )
        {
            destructible = null;

            if (target == null)
                return false;

            Collider[] nearby = Physics.OverlapSphere(
                transform.position,
                destructibleDashRange,
                dashCollisionHandler != null ? dashCollisionHandler.DestructibleLayer : ~0
            );

            Vector3 toTarget = (target.position - transform.position).normalized;

            foreach (var col in nearby)
            {
                // Must be a recognised destructible tag
                if (!IsDestructibleTag(col.tag))
                    continue;

                Vector3 toObj = (col.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(toTarget, toObj);

                // Only pursue objects that are roughly between the AI and the target
                if (angle < 45f)
                {
                    destructible = col.transform;
                    return true;
                }
            }
            return false;
        }

        private bool IsDestructibleTag(string tag)
        {
            return tag == "LightObject"
                || tag == "HeavyObject"
                || tag == "Crate"
                || tag == "Barrel"
                || tag == "Explosive"
                || tag == "Fragile";
        }

        private void ExecuteDestructibleDash()
        {
            if (_pendingDestructibleTarget == null || isExecutingDash)
                return;
            StartCoroutine(ExecuteDestructibleDashSequence(_pendingDestructibleTarget));
            _pendingDestructibleTarget = null;
        }

        private IEnumerator ExecuteDestructibleDashSequence(Transform destructible)
        {
            isExecutingDash = true;

            // Dash straight at the object
            Vector3 dirToObj = (destructible.position - transform.position).normalized;
            MoveInDirection(dirToObj);
            yield return new WaitForSeconds(0.1f);

            if (inputHandler != null)
                inputHandler.TriggerAIDash();

            RollNewAttackCooldown();
            yield return new WaitForSeconds(0.5f);

            isExecutingDash = false;
        }

        private void ExecuteChase()
        {
            if (currentTarget == null)
                return;

            Vector3 predictedPosition = PredictTargetPosition(currentTarget);
            lastKnownTargetPosition = predictedPosition;
            Vector3 direction = (predictedPosition - transform.position).normalized;

            if (Time.time - lastRepositionTime > repositionInterval)
            {
                lastRepositionTime = Time.time;
                if (Random.value < Profile.strafeProbability)
                {
                    Vector3 strafeDir = Vector3.Cross(direction, Vector3.up).normalized;
                    strafeDir *= Random.value > 0.5f ? 1f : -1f;
                    float strafeWeight = Mathf.Clamp(strafeSpeed / 6f, 0.25f, 1f);
                    direction = (direction + strafeDir * strafeWeight).normalized;
                }
            }

            MoveInDirection(direction);
        }

        private void ExecuteDash()
        {
            if (currentTarget == null || isExecutingDash)
                return;

            Vector3 predictedPosition = PredictTargetPosition(currentTarget);
            Vector3 dashDirection = (predictedPosition - transform.position).normalized;
            StartCoroutine(ExecuteDashSequence(dashDirection));
        }

        private IEnumerator ExecuteDashSequence(Vector3 direction)
        {
            isExecutingDash = true;
            dashAttempts++;

            MoveInDirection(direction);
            yield return new WaitForSeconds(0.1f);

            if (inputHandler != null)
                inputHandler.TriggerAIDash();

            // After the dash fires, draw a fresh random cooldown.
            // This is the core of the desync — each AI independently
            // rolls its own next-attack window, so they can never align.
            RollNewAttackCooldown();

            yield return new WaitForSeconds(0.5f);

            isExecutingDash = false;
        }

        private void ExecuteSpeedBoost()
        {
            if (isExecutingSpeedBoost)
                return;
            StartCoroutine(ExecuteSpeedBoostSequence());
        }

        private IEnumerator ExecuteSpeedBoostSequence()
        {
            isExecutingSpeedBoost = true;

            if (inputHandler != null)
                inputHandler.TriggerAISpeedBoost();

            currentState = AIState.Chasing;
            yield return new WaitForSeconds(2f);

            isExecutingSpeedBoost = false;
        }

        private void ExecuteEvasion()
        {
            MoveInDirection(evasionDirection);
            evasionTimer -= currentDecisionDelay;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rolls a new random attack cooldown for this AI instance.
        /// Called on Awake (to seed the first window) and after every dash
        /// (to keep the AI's rhythm unique going forward).
        /// </summary>
        private void RollNewAttackCooldown()
        {
            float newCooldown = Random.Range(
                Profile.attackCooldownMin,
                Profile.attackCooldownMax
            );
            // Add on top of any remaining cooldown so we never undercut ourselves.
            attackCooldownTimer = Mathf.Max(attackCooldownTimer, 0f) + newCooldown;

            if (showDebugInfo)
                Debug.Log(
                    $"[AI:{name}] New attack cooldown: {newCooldown:F3}s "
                        + $"(total pending: {attackCooldownTimer:F3}s)"
                );
        }

        private void FindTarget()
        {
            Collider[] potentialTargets = Physics.OverlapSphere(
                transform.position,
                detectionRadius,
                targetLayer
            );

            Transform closestTarget = null;
            float bestTargetScore = Mathf.Infinity;

            foreach (var col in potentialTargets)
            {
                if (col.transform.root == transform.root)
                    continue;

                Vector3 dir = (col.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, dir);
                if (angle > visionAngle / 2f)
                    continue;

                var targetHealth = col.GetComponentInParent<PlayerHealthComponent>();
                if (targetHealth != null && !targetHealth.IsAlive)
                    continue;

                float distance = Vector3.Distance(transform.position, col.transform.position);
                float targetScore = distance - Profile.aggressiveness * 2f;

                if (currentTarget != null && col.transform.root == currentTarget.root)
                    targetScore -= Profile.targetPersistence * 4f;

                if (adaptiveDirector != null)
                    targetScore -= adaptiveDirector.GetTargetPriority(this, col.transform, distance);

                if (targetScore < bestTargetScore)
                {
                    bestTargetScore = targetScore;
                    closestTarget = col.transform;
                }
            }

            currentTarget = closestTarget;
        }

        private Vector3 PredictTargetPosition(Transform target)
        {
            if (target == null)
                return lastKnownTargetPosition;

            Rigidbody targetRb = target.GetComponentInParent<Rigidbody>();
            if (targetRb != null)
                return target.position + targetRb.velocity * predictionTime * Profile.predictionAccuracy;

            return target.position;
        }

        private void MoveInDirection(Vector3 direction)
        {
            direction.y = 0;
            direction.Normalize();

            if (inputHandler != null)
                inputHandler.SetAIInput(new Vector2(direction.x, direction.z));

            moveDirection = direction;
        }

        private void StopMovement()
        {
            if (inputHandler != null)
                inputHandler.SetAIInput(Vector2.zero);
            moveDirection = Vector3.zero;
        }

        private void GenerateNewWaypoint()
        {
            float activePatrolRadius = Profile != null ? Profile.patrolRadius : patrolRadius;
            Vector2 randomCircle = Random.insideUnitCircle * activePatrolRadius;
            currentWaypoint = patrolCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
        }

        private void InitiateEvasion()
        {
            if (currentTarget == null)
                return;

            Vector3 awayFromTarget = (transform.position - currentTarget.position).normalized;
            Vector3 randomOffset = new Vector3(
                Random.Range(-1f, 1f),
                0,
                Random.Range(-1f, 1f)
            ).normalized;

            evasionDirection = (awayFromTarget + randomOffset * 0.3f).normalized;
            evasionTimer = EVASION_DURATION;
        }

        /// <summary>
        /// Dash is available only when:
        ///   • the ability itself is off cooldown (engine side), AND
        ///   • this AI's personal attack cooldown has expired (desync side).
        /// </summary>
        private bool CanUseDash()
        {
            if (movementController == null || movementController.DashAbility == null)
                return false;

            // Personal attack cooldown must have drained to zero.
            if (attackCooldownTimer > 0f)
                return false;

            return movementController.DashAbility.CanActivate && !isExecutingDash;
        }

        private bool CanUseSpeedBoost()
        {
            return !isExecutingSpeedBoost
                && Random.value < Profile.speedBoostUseProbability;
        }

        private float GetRandomReactionDelay()
        {
            float baseReaction = Profile != null ? Profile.reactionTime : reactionTime;
            float spread = Mathf.Max(0.03f, (reactionTimeMax - reactionTimeMin) * 0.5f);
            float min = Mathf.Max(0.03f, baseReaction - spread);
            float max = Mathf.Max(min + 0.02f, baseReaction + spread);
            return Random.Range(min, max);
        }

        private float GetRandomDecisionDelay()
        {
            float baseDecision = Profile != null ? Profile.decisionInterval : decisionInterval;
            float spread = Mathf.Max(0.03f, (decisionIntervalMax - decisionIntervalMin) * 0.5f);
            float min = Mathf.Max(0.05f, baseDecision - spread);
            float max = Mathf.Max(min + 0.03f, baseDecision + spread);
            return Random.Range(min, max);
        }

        private bool ShouldUseDash(float distanceToTarget)
        {
            float chance = Profile.dashUseProbability;
            chance += Profile.aggressiveness * 0.12f;

            if (distanceToTarget < dashRange * 0.6f)
                chance += Profile.recklessness * 0.08f;

            return Random.value < Mathf.Clamp01(chance);
        }

        private bool ShouldUseDestructibleDash()
        {
            float chance = Profile.dashUseProbability * Mathf.Lerp(0.25f, 0.75f, Profile.recklessness);

            if (resolvedPersonality == AIPersonality.Trickster)
                chance += 0.25f;

            if (lastAdaptiveTuning.matchPressure > 0f)
                chance += lastAdaptiveTuning.matchPressure * 0.1f;

            return Random.value < Mathf.Clamp01(chance);
        }

        private bool ShouldEvadeAtDistance()
        {
            float chance = Profile.evasionOnDamageChance;
            chance += LowHealthPressure * 0.35f;
            chance -= Profile.recklessness * 0.15f;

            if (resolvedPersonality == AIPersonality.Survivor)
                chance += 0.2f;

            if (resolvedPersonality == AIPersonality.Brawler)
                chance -= 0.15f;

            return Random.value < Mathf.Clamp01(chance);
        }

        private float GetAdaptiveSafeDistance()
        {
            float caution = 1f - Profile.recklessness;
            float multiplier = Mathf.Lerp(0.75f, 1.35f, caution);
            multiplier += LowHealthPressure * 0.35f;
            return safeDistance * multiplier;
        }

        public float GetRecentDamagePressure(float memorySeconds)
        {
            if (lastDamageTime < 0f || memorySeconds <= 0f)
                return 0f;

            float elapsed = Time.time - lastDamageTime;
            if (elapsed >= memorySeconds)
                return 0f;

            return Mathf.Clamp01(1f - elapsed / memorySeconds);
        }

        public bool WasRecentlyDamagedBy(Transform target, float memorySeconds)
        {
            if (target == null || lastDamageSourceRoot == null)
                return false;

            bool recentDamageSource =
                Time.time - lastDamageTime <= memorySeconds
                && target.root == lastDamageSourceRoot;

            bool recentKiller =
                lastKillerRoot != null
                && Time.time - lastDeathTime <= memorySeconds
                && target.root == lastKillerRoot;

            return recentDamageSource || recentKiller;
        }

        public void ApplyAdaptiveTuning(AIAdaptiveTuning tuning)
        {
            lastAdaptiveTuning = tuning;

            if (runtimeBehaviorProfile == null)
                CreateRuntimeProfile();

            if (runtimeBehaviorProfile == null)
                return;

            ResetRuntimeProfileFromBase();
            ApplyPersonalityBias(runtimeBehaviorProfile, resolvedPersonality, tuning);

            if (enableAdaptiveTuning)
                ApplyPressureTuning(runtimeBehaviorProfile, tuning);

            ClampRuntimeProfile(runtimeBehaviorProfile);
            adaptivePressure = enableAdaptiveTuning ? tuning.matchPressure : 0f;
        }

        private void CreateRuntimeProfile()
        {
            if (runtimeBehaviorProfile != null || behaviorProfile == null)
                return;

            runtimeBehaviorProfile = Instantiate(behaviorProfile);
            runtimeBehaviorProfile.name = $"{name}_RuntimeAIProfile";
            runtimeBehaviorProfile.hideFlags = HideFlags.DontSave;
        }

        private void ResetRuntimeProfileFromBase()
        {
            if (runtimeBehaviorProfile == null)
                return;

            AIBehaviorProfile source = baseBehaviorProfile != null
                ? baseBehaviorProfile
                : behaviorProfile;

            if (source == null)
                return;

            runtimeBehaviorProfile.aggressiveness = source.aggressiveness;
            runtimeBehaviorProfile.dashUseProbability = source.dashUseProbability;
            runtimeBehaviorProfile.speedBoostUseProbability = source.speedBoostUseProbability;
            runtimeBehaviorProfile.evasionOnDamageChance = source.evasionOnDamageChance;
            runtimeBehaviorProfile.strafeProbability = source.strafeProbability;
            runtimeBehaviorProfile.reactionTime = source.reactionTime;
            runtimeBehaviorProfile.predictionAccuracy = source.predictionAccuracy;
            runtimeBehaviorProfile.decisionInterval = source.decisionInterval;
            runtimeBehaviorProfile.startupJitterMax = source.startupJitterMax;
            runtimeBehaviorProfile.attackCooldownMin = source.attackCooldownMin;
            runtimeBehaviorProfile.attackCooldownMax = source.attackCooldownMax;
            runtimeBehaviorProfile.resyncCheckInterval = source.resyncCheckInterval;
            runtimeBehaviorProfile.resyncJitterMax = source.resyncJitterMax;
            runtimeBehaviorProfile.recklessness = source.recklessness;
            runtimeBehaviorProfile.targetPersistence = source.targetPersistence;
            runtimeBehaviorProfile.waypointIdleTime = source.waypointIdleTime;
            runtimeBehaviorProfile.patrolRadius = source.patrolRadius;
        }

        private void ApplyPressureTuning(AIBehaviorProfile profile, AIAdaptiveTuning tuning)
        {
            float hardPressure = Mathf.Max(0f, tuning.matchPressure);
            float easyPressure = Mathf.Max(0f, -tuning.matchPressure);

            profile.aggressiveness += hardPressure * 0.16f - easyPressure * 0.22f;
            profile.dashUseProbability += hardPressure * 0.14f - easyPressure * 0.2f;
            profile.speedBoostUseProbability += hardPressure * 0.12f - easyPressure * 0.16f;
            profile.evasionOnDamageChance += hardPressure * 0.06f - easyPressure * 0.08f;
            profile.strafeProbability += hardPressure * 0.14f - easyPressure * 0.1f;
            profile.reactionTime += easyPressure * 0.18f - hardPressure * 0.08f;
            profile.predictionAccuracy += hardPressure * 0.18f - easyPressure * 0.2f;
            profile.decisionInterval += easyPressure * 0.12f - hardPressure * 0.05f;
            profile.attackCooldownMin += easyPressure * 0.08f - hardPressure * 0.03f;
            profile.attackCooldownMax += easyPressure * 0.12f - hardPressure * 0.05f;
            profile.targetPersistence += hardPressure * 0.08f - easyPressure * 0.1f;
        }

        private void ApplyPersonalityBias(
            AIBehaviorProfile profile,
            AIPersonality activePersonality,
            AIAdaptiveTuning tuning
        )
        {
            switch (activePersonality)
            {
                case AIPersonality.Hunter:
                    profile.aggressiveness += 0.12f;
                    profile.predictionAccuracy += 0.12f;
                    profile.speedBoostUseProbability += 0.1f;
                    profile.targetPersistence += 0.18f;
                    profile.reactionTime -= 0.03f;
                    profile.decisionInterval -= 0.03f;
                    break;
                case AIPersonality.Brawler:
                    profile.aggressiveness += 0.18f;
                    profile.dashUseProbability += 0.18f;
                    profile.recklessness += 0.25f;
                    profile.evasionOnDamageChance -= 0.15f;
                    profile.targetPersistence -= 0.08f;
                    profile.attackCooldownMax -= 0.06f;
                    break;
                case AIPersonality.Trickster:
                    profile.strafeProbability += 0.25f;
                    profile.predictionAccuracy += 0.08f;
                    profile.speedBoostUseProbability += 0.15f;
                    profile.targetPersistence -= 0.1f;
                    profile.recklessness += 0.08f;
                    break;
                case AIPersonality.Survivor:
                    profile.evasionOnDamageChance += 0.2f;
                    profile.strafeProbability += 0.12f;
                    profile.recklessness -= 0.2f;
                    profile.dashUseProbability -= 0.08f;
                    profile.attackCooldownMin += 0.04f;
                    profile.attackCooldownMax += 0.08f;
                    break;
                case AIPersonality.Rival:
                    profile.targetPersistence += 0.25f;
                    profile.aggressiveness += 0.08f + tuning.recentDamagePressure * 0.15f;
                    profile.dashUseProbability += tuning.recentDamagePressure * 0.2f;
                    profile.recklessness += tuning.recentDamagePressure * 0.15f;
                    break;
            }
        }

        private void ClampRuntimeProfile(AIBehaviorProfile profile)
        {
            profile.aggressiveness = Mathf.Clamp01(profile.aggressiveness);
            profile.dashUseProbability = Mathf.Clamp01(profile.dashUseProbability);
            profile.speedBoostUseProbability = Mathf.Clamp01(profile.speedBoostUseProbability);
            profile.evasionOnDamageChance = Mathf.Clamp01(profile.evasionOnDamageChance);
            profile.strafeProbability = Mathf.Clamp01(profile.strafeProbability);
            profile.reactionTime = Mathf.Clamp(profile.reactionTime, 0.05f, 1f);
            profile.predictionAccuracy = Mathf.Clamp01(profile.predictionAccuracy);
            profile.decisionInterval = Mathf.Clamp(profile.decisionInterval, 0.1f, 1f);
            profile.startupJitterMax = Mathf.Clamp(profile.startupJitterMax, 0f, 1f);
            profile.attackCooldownMin = Mathf.Clamp(profile.attackCooldownMin, 0f, 1f);
            profile.attackCooldownMax = Mathf.Clamp(
                Mathf.Max(profile.attackCooldownMin, profile.attackCooldownMax),
                0f,
                1f
            );
            profile.resyncCheckInterval = Mathf.Clamp(profile.resyncCheckInterval, 2f, 10f);
            profile.resyncJitterMax = Mathf.Clamp(profile.resyncJitterMax, 0f, 0.4f);
            profile.recklessness = Mathf.Clamp01(profile.recklessness);
            profile.targetPersistence = Mathf.Clamp01(profile.targetPersistence);
            profile.waypointIdleTime = Mathf.Clamp(profile.waypointIdleTime, 0f, 5f);
            profile.patrolRadius = Mathf.Clamp(profile.patrolRadius, 5f, 30f);
        }

        private AIPersonality ResolvePersonality(AIPersonality configuredPersonality)
        {
            if (configuredPersonality != AIPersonality.Auto)
                return configuredPersonality;

            AIPersonality[] personalities =
            {
                AIPersonality.Hunter,
                AIPersonality.Brawler,
                AIPersonality.Trickster,
                AIPersonality.Survivor,
                AIPersonality.Rival,
            };

            int index = GetStablePersonalityIndex();
            return personalities[index];
        }

        private int GetStablePersonalityIndex()
        {
            if (aiId != 0)
                return (Mathf.Abs(aiId) - 1) % 5;

            for (int i = name.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(name[i]))
                    continue;

                int end = i;
                int start = i;
                while (start > 0 && char.IsDigit(name[start - 1]))
                    start--;

                string numberText = name.Substring(start, end - start + 1);
                if (int.TryParse(numberText, out int parsedNumber))
                    return Mathf.Abs(parsedNumber - 1) % 5;

                break;
            }

            return Mathf.Abs(GetInstanceID()) % 5;
        }

        public void ConfigureIdentity(int id, string displayName)
        {
            _ = displayName;
            aiId = id;
            // Display names always come from AINameCatalog, never prefab/Photon payload data.
            aiDisplayName = AINameCatalog.GetNameForId(id);

            gameObject.name = aiDisplayName;

            if (personality == AIPersonality.Auto)
                resolvedPersonality = ResolvePersonality(personality);

            if (runtimeBehaviorProfile != null)
                ApplyAdaptiveTuning(lastAdaptiveTuning);
        }

        private void ReadIdentityFromPhotonData()
        {
            PhotonView photonView = GetComponent<PhotonView>();
            object[] instantiationData = photonView != null ? photonView.InstantiationData : null;

            if (instantiationData != null && instantiationData.Length >= 1)
            {
                int id = instantiationData[0] is int dataId ? dataId : 0;

                if (id != 0)
                {
                    ConfigureIdentity(id, null);
                    return;
                }
            }

            if (AINameCatalog.TryGetIdFromPrefabName(name, out int parsedId))
            {
                ConfigureIdentity(parsedId, null);
            }
        }

        private void RecordDamageSource(GameObject source)
        {
            damageEventsTaken++;
            lastDamageTime = Time.time;

            if (source != null)
                lastDamageSourceRoot = source.transform.root;
        }

        // ─────────────────────────────────────────────────────────────────────
        // EVENT HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private void OnDamageTaken(float damage, GameObject source, DamageType damageType)
        {
            if (!healthComponent.IsAlive || isDyingStun)
                return;

            RecordDamageSource(source);

            if (source != null && Random.value < Profile.evasionOnDamageChance)
            {
                evasionDirection = (transform.position - source.transform.position).normalized;
                evasionTimer = EVASION_DURATION;
                currentState = AIState.Evading;
            }
        }

        private void OnDeath()
        {
            deathCount++;
            lastKillerRoot = lastDamageSourceRoot;
            lastDeathTime = Time.time;

            StopMovement();
            currentState = AIState.Idle;
            currentTarget = null;
        }

        private void OnDeathStunLock()
        {
            isDyingStun = true;
            StopMovement();
        }

        private void OnRespawn()
        {
            patrolCenter =
                healthComponent != null ? healthComponent.RespawnPosition : transform.position;

            isExecutingDash = false;
            isExecutingSpeedBoost = false;
            isDyingStun = false;
            evasionTimer = 0f;
            currentTarget = null;

            // Re-seed attack timing on respawn so a just-respawned AI
            // doesn't immediately sync up with a still-fighting opponent.
            RollNewAttackCooldown();
            resyncTimer = Profile.resyncCheckInterval;

            GenerateNewWaypoint();
            currentState = AIState.Patrolling;

            Debug.Log($"[AI:{name}] Respawned. Patrolling from {patrolCenter}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // DEBUG
        // ─────────────────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                transform.position,
                transform.position
                    + Quaternion.Euler(0, -visionAngle / 2f, 0)
                        * transform.forward
                        * detectionRadius
            );
            Gizmos.DrawLine(
                transform.position,
                transform.position
                    + Quaternion.Euler(0, visionAngle / 2f, 0) * transform.forward * detectionRadius
            );

            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, currentTarget.position);
                Gizmos.DrawWireSphere(currentTarget.position, 0.5f);
            }

            if (currentState == AIState.Patrolling)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(currentWaypoint, 0.5f);
                Gizmos.DrawLine(transform.position, currentWaypoint);
            }

            if (currentState == AIState.Evading)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, evasionDirection * 3f);
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo)
                return;

            GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 310, 280));
            GUILayout.Label($"=== AI: {gameObject.name} ===");
            GUILayout.Label($"Personality: {resolvedPersonality}");
            GUILayout.Label($"Adaptive Pressure: {adaptivePressure:F2}");
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Target: {(currentTarget != null ? currentTarget.name : "None")}");
            GUILayout.Label(
                $"Health: {healthComponent?.CurrentHealth:F1}/{healthComponent?.MaxHealth:F1}"
            );

            if (currentTarget != null)
                GUILayout.Label(
                    $"Distance: {Vector3.Distance(transform.position, currentTarget.position):F1}m"
                );

            GUILayout.Label($"Can Dash: {CanUseDash()}");
            GUILayout.Label($"Dash Attempts: {dashAttempts}");
            GUILayout.Label($"Attack Cooldown: {Mathf.Max(0f, attackCooldownTimer):F2}s");
            GUILayout.Label($"Resync in: {Mathf.Max(0f, resyncTimer):F1}s");
            GUILayout.Label($"Stunned: {stateController?.IsStunned ?? false}");
            GUILayout.Label($"Falling: {stateController?.IsFalling ?? false}");

            if (evasionTimer > 0)
                GUILayout.Label($"Evading: {evasionTimer:F1}s");

            GUILayout.EndArea();
        }
    }

    public enum AIState
    {
        Idle,
        Patrolling,
        Chasing,
        Dashing,
        DashingDestructible,
        SpeedBoosting,
        Evading,
    }
}
