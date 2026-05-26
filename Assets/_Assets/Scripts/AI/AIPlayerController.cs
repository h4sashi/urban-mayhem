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

        [Header("Utility AI")]
        [SerializeField]
        private AIUtilityProfile utilityProfile;

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
        private bool showDebugInfo = false;

        [SerializeField]
        private bool showGizmos = false;

        private Transform _pendingDestructibleTarget;
        private const int MaxTargetQueryResults = 64;
        private const int MaxDestructibleQueryResults = 48;
        private readonly Collider[] targetQueryBuffer = new Collider[MaxTargetQueryResults];
        private readonly Collider[] destructibleQueryBuffer =
            new Collider[MaxDestructibleQueryResults];
        private readonly HashSet<Transform> visitedTargetRoots = new HashSet<Transform>();
        private AIBehaviorProfile baseBehaviorProfile;
        private AIBehaviorProfile runtimeBehaviorProfile;
        private AIUtilityProfile runtimeUtilityProfile;
        private AIPersonality resolvedPersonality = AIPersonality.Auto;
        private AdaptiveAIDirector adaptiveDirector;
        private AIAdaptiveTuning lastAdaptiveTuning = AIAdaptiveTuning.Neutral;
        private int aiId;
        private string aiDisplayName;
        private bool tacticalDefaultsCaptured;
        private float baseDetectionRadius;
        private float baseVisionAngle;
        private float baseDashRange;
        private float baseDestructibleDashRange;
        private float baseMinDashDistance;
        private float baseSpeedBoostRange;
        private float baseSafeDistance;
        private float baseStrafeSpeed;
        private float baseRepositionInterval;
        private float basePredictionTime;

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
        private AIUtilityAction lastUtilityAction = AIUtilityAction.None;
        private float lastUtilityScore;

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

        private AIUtilityProfile UtilityProfile => runtimeUtilityProfile != null
            ? runtimeUtilityProfile
            : utilityProfile;

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
            CaptureTacticalDefaults();
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
            EnsureUtilityProfile();
            ApplyAdaptiveTuning(AIAdaptiveTuning.Neutral);
            ApplyPersonalityTacticalBias();

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
                lastUtilityAction = AIUtilityAction.Evade;
                lastUtilityScore = 1f;
                currentState = AIState.Evading;
                return;
            }

            AIUtilityDecision decision = ChooseBestUtilityDecision();
            ApplyUtilityDecision(decision);
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

            int nearbyCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                destructibleDashRange,
                destructibleQueryBuffer,
                dashCollisionHandler != null ? dashCollisionHandler.DestructibleLayer : ~0,
                QueryTriggerInteraction.UseGlobal
            );

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= Mathf.Epsilon)
                return false;

            toTarget.Normalize();
            float bestAngle = 45f;

            for (int i = 0; i < nearbyCount; i++)
            {
                Collider col = destructibleQueryBuffer[i];
                if (col == null)
                    continue;

                // Must be a recognised destructible tag
                if (!IsDestructibleTag(col.tag))
                    continue;

                Vector3 toObj = col.transform.position - transform.position;
                toObj.y = 0f;
                if (toObj.sqrMagnitude <= Mathf.Epsilon)
                    continue;

                toObj.Normalize();
                float angle = Vector3.Angle(toTarget, toObj);

                // Only pursue objects that are roughly between the AI and the target
                if (angle < bestAngle)
                {
                    destructible = col.transform;
                    bestAngle = angle;
                }
            }

            return destructible != null;
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

        private IEnumerator ExecuteSpeedBoostSequence(bool resumeChase = true)
        {
            isExecutingSpeedBoost = true;

            if (inputHandler != null)
                inputHandler.TriggerAISpeedBoost();

            if (resumeChase)
                currentState = AIState.Chasing;

            yield return new WaitForSeconds(2f);

            isExecutingSpeedBoost = false;
        }

        private void ExecuteEvasion()
        {
            TryUseSpeedBoostDuringEvasion();
            MoveInDirection(evasionDirection);
            evasionTimer -= currentDecisionDelay;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private AIUtilityDecision ChooseBestUtilityDecision()
        {
            AIUtilityContext patrolContext = BuildBaseUtilityContext();
            AIUtilityDecision best = CreateUtilityDecision(AIUtilityAction.Patrol, patrolContext);

            int targetCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                detectionRadius,
                targetQueryBuffer,
                targetLayer,
                QueryTriggerInteraction.UseGlobal
            );
            visitedTargetRoots.Clear();

            for (int i = 0; i < targetCount; i++)
            {
                Collider col = targetQueryBuffer[i];
                if (!TryBuildTargetUtilityContext(col, visitedTargetRoots, out AIUtilityContext context))
                    continue;

                ConsiderUtilityDecision(ref best, AIUtilityAction.Chase, context);
                ConsiderUtilityDecision(ref best, AIUtilityAction.Dash, context);
                ConsiderUtilityDecision(ref best, AIUtilityAction.DestructibleDash, context);
                ConsiderUtilityDecision(ref best, AIUtilityAction.SpeedBoost, context);
                ConsiderUtilityDecision(ref best, AIUtilityAction.Evade, context);
            }

            return best;
        }

        private AIUtilityContext BuildBaseUtilityContext()
        {
            return new AIUtilityContext
            {
                hasTarget = false,
                adaptiveSafeDistance = GetAdaptiveSafeDistance(),
                lowHealthPressure = LowHealthPressure,
                recentDamagePressure = GetRecentDamagePressure(10f),
            };
        }

        private bool TryBuildTargetUtilityContext(
            Collider targetCollider,
            HashSet<Transform> visitedRoots,
            out AIUtilityContext context
        )
        {
            context = BuildBaseUtilityContext();

            if (targetCollider == null)
                return false;

            Transform targetRoot = targetCollider.transform.root;
            if (targetRoot == transform.root || visitedRoots.Contains(targetRoot))
                return false;

            visitedRoots.Add(targetRoot);

            PlayerHealthComponent targetHealth =
                targetCollider.GetComponentInParent<PlayerHealthComponent>();
            if (targetHealth != null && !targetHealth.IsAlive)
                return false;

            Transform target = targetHealth != null ? targetHealth.transform : targetRoot;
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= Mathf.Epsilon)
                return false;

            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            if (angle > visionAngle / 2f)
                return false;

            float distance = toTarget.magnitude;
            bool canDash = CanUseDash();
            bool canSpeedBoost = CanUseSpeedBoostAbility() && distance <= speedBoostRange;

            Transform destructibleTarget = null;
            bool hasLaunchableDestructible =
                canDash && TryFindLaunchableDestructibleToward(target, out destructibleTarget);

            float adaptiveSafeDistance = GetAdaptiveSafeDistance();
            float safeDistanceForMath = Mathf.Max(0.1f, adaptiveSafeDistance);
            float preferredDashDistance = Mathf.Lerp(minDashDistance, dashRange, 0.58f);
            float boostSpan = Mathf.Max(0.1f, speedBoostRange - dashRange);
            float rawTargetPriority =
                adaptiveDirector != null
                    ? adaptiveDirector.GetTargetPriority(
                        this,
                        target,
                        distance,
                        hasLaunchableDestructible
                    )
                    : 0f;

            context = new AIUtilityContext
            {
                target = target,
                utilityTarget = destructibleTarget,
                hasTarget = true,
                canDash = canDash,
                inDashRange = canDash && distance <= dashRange && distance >= minDashDistance,
                canSpeedBoost = canSpeedBoost,
                hasLaunchableDestructible = hasLaunchableDestructible,
                targetRecentlyDamagedUs = WasRecentlyDamagedBy(target, 30f),
                isCurrentTarget = currentTarget != null && target.root == currentTarget.root,
                distanceToTarget = distance,
                normalizedDistance = Mathf.Clamp01(distance / Mathf.Max(1f, detectionRadius)),
                targetPriority = Mathf.Clamp(rawTargetPriority / 14f, -1f, 1f),
                adaptiveSafeDistance = adaptiveSafeDistance,
                closeDanger = Mathf.Clamp01(1f - distance / safeDistanceForMath),
                dashRangeFit = Mathf.Clamp01(
                    1f - Mathf.Abs(distance - preferredDashDistance) / Mathf.Max(0.1f, dashRange)
                ),
                speedBoostRangeFit = Mathf.Clamp01((distance - dashRange * 0.65f) / boostSpan),
                recentDamagePressure = GetRecentDamagePressure(10f),
                lowHealthPressure = LowHealthPressure,
            };

            return true;
        }

        private AIUtilityDecision CreateUtilityDecision(
            AIUtilityAction action,
            AIUtilityContext context
        )
        {
            float score =
                UtilityProfile != null
                    ? UtilityProfile.Evaluate(
                        action,
                        context,
                        resolvedPersonality,
                        Profile,
                        GetActiveUtilityAction()
                    )
                    : 0f;

            return new AIUtilityDecision(action, context.target, context.utilityTarget, score);
        }

        private void ConsiderUtilityDecision(
            ref AIUtilityDecision best,
            AIUtilityAction action,
            AIUtilityContext context
        )
        {
            AIUtilityDecision candidate = CreateUtilityDecision(action, context);
            if (candidate.score > best.score)
                best = candidate;
        }

        private void ApplyUtilityDecision(AIUtilityDecision decision)
        {
            lastUtilityAction = decision.action;
            lastUtilityScore = decision.score;
            currentTarget = decision.target;

            switch (decision.action)
            {
                case AIUtilityAction.Patrol:
                    _pendingDestructibleTarget = null;
                    currentTarget = null;
                    if (currentState != AIState.Patrolling)
                        GenerateNewWaypoint();
                    currentState = AIState.Patrolling;
                    break;
                case AIUtilityAction.Chase:
                    _pendingDestructibleTarget = null;
                    currentState = currentTarget != null ? AIState.Chasing : AIState.Patrolling;
                    if (currentTarget == null)
                        GenerateNewWaypoint();
                    break;
                case AIUtilityAction.Dash:
                    _pendingDestructibleTarget = null;
                    currentState = currentTarget != null ? AIState.Dashing : AIState.Patrolling;
                    break;
                case AIUtilityAction.DestructibleDash:
                    _pendingDestructibleTarget = decision.utilityTarget;
                    currentState =
                        currentTarget != null && _pendingDestructibleTarget != null
                            ? AIState.DashingDestructible
                            : AIState.Chasing;
                    break;
                case AIUtilityAction.SpeedBoost:
                    _pendingDestructibleTarget = null;
                    currentState = currentTarget != null ? AIState.SpeedBoosting : AIState.Patrolling;
                    break;
                case AIUtilityAction.Evade:
                    _pendingDestructibleTarget = null;
                    if (currentTarget != null)
                    {
                        currentState = AIState.Evading;
                        InitiateEvasion();
                    }
                    else
                    {
                        currentState = AIState.Patrolling;
                        GenerateNewWaypoint();
                    }
                    break;
                default:
                    _pendingDestructibleTarget = null;
                    currentState = currentTarget != null ? AIState.Chasing : AIState.Patrolling;
                    break;
            }
        }

        private AIUtilityAction GetActiveUtilityAction()
        {
            switch (currentState)
            {
                case AIState.Patrolling:
                    return AIUtilityAction.Patrol;
                case AIState.Chasing:
                    return AIUtilityAction.Chase;
                case AIState.Dashing:
                    return AIUtilityAction.Dash;
                case AIState.DashingDestructible:
                    return AIUtilityAction.DestructibleDash;
                case AIState.SpeedBoosting:
                    return AIUtilityAction.SpeedBoost;
                case AIState.Evading:
                    return AIUtilityAction.Evade;
                default:
                    return AIUtilityAction.None;
            }
        }

        private void CaptureTacticalDefaults()
        {
            if (tacticalDefaultsCaptured)
                return;

            baseDetectionRadius = detectionRadius;
            baseVisionAngle = visionAngle;
            baseDashRange = dashRange;
            baseDestructibleDashRange = destructibleDashRange;
            baseMinDashDistance = minDashDistance;
            baseSpeedBoostRange = speedBoostRange;
            baseSafeDistance = safeDistance;
            baseStrafeSpeed = strafeSpeed;
            baseRepositionInterval = repositionInterval;
            basePredictionTime = predictionTime;
            tacticalDefaultsCaptured = true;
        }

        private void ApplyPersonalityTacticalBias()
        {
            if (!tacticalDefaultsCaptured)
                CaptureTacticalDefaults();

            float detectionMultiplier = 1f;
            float visionMultiplier = 1f;
            float dashMultiplier = 1f;
            float destructibleDashMultiplier = 1f;
            float minDashMultiplier = 1f;
            float speedBoostMultiplier = 1f;
            float safeDistanceMultiplier = 1f;
            float strafeMultiplier = 1f;
            float repositionMultiplier = 1f;
            float predictionMultiplier = 1f;

            switch (resolvedPersonality)
            {
                case AIPersonality.Hunter:
                    detectionMultiplier = 1.2f;
                    visionMultiplier = 1.15f;
                    dashMultiplier = 1.05f;
                    destructibleDashMultiplier = 0.85f;
                    minDashMultiplier = 0.85f;
                    speedBoostMultiplier = 1.25f;
                    safeDistanceMultiplier = 0.75f;
                    strafeMultiplier = 0.75f;
                    repositionMultiplier = 1.2f;
                    predictionMultiplier = 1.35f;
                    break;
                case AIPersonality.Brawler:
                    detectionMultiplier = 1.05f;
                    dashMultiplier = 1.3f;
                    destructibleDashMultiplier = 0.9f;
                    minDashMultiplier = 0.6f;
                    speedBoostMultiplier = 0.75f;
                    safeDistanceMultiplier = 0.55f;
                    strafeMultiplier = 0.45f;
                    repositionMultiplier = 1.35f;
                    predictionMultiplier = 0.85f;
                    break;
                case AIPersonality.Trickster:
                    detectionMultiplier = 1.1f;
                    visionMultiplier = 1.1f;
                    dashMultiplier = 1.05f;
                    destructibleDashMultiplier = 1.55f;
                    minDashMultiplier = 0.75f;
                    speedBoostMultiplier = 1.15f;
                    safeDistanceMultiplier = 1.05f;
                    strafeMultiplier = 1.65f;
                    repositionMultiplier = 0.5f;
                    predictionMultiplier = 1.15f;
                    break;
                case AIPersonality.Survivor:
                    detectionMultiplier = 0.95f;
                    visionMultiplier = 1.25f;
                    dashMultiplier = 0.75f;
                    destructibleDashMultiplier = 0.7f;
                    minDashMultiplier = 1.2f;
                    speedBoostMultiplier = 1.3f;
                    safeDistanceMultiplier = 1.7f;
                    strafeMultiplier = 1.25f;
                    repositionMultiplier = 0.65f;
                    predictionMultiplier = 0.9f;
                    break;
                case AIPersonality.Rival:
                    detectionMultiplier = 1.15f;
                    visionMultiplier = 1.15f;
                    dashMultiplier = 1.15f;
                    destructibleDashMultiplier = 1.05f;
                    minDashMultiplier = 0.8f;
                    speedBoostMultiplier = 1.05f;
                    safeDistanceMultiplier = 0.85f;
                    strafeMultiplier = 0.9f;
                    repositionMultiplier = 0.75f;
                    predictionMultiplier = 1.2f;
                    break;
            }

            detectionRadius = Mathf.Max(4f, baseDetectionRadius * detectionMultiplier);
            visionAngle = Mathf.Clamp(baseVisionAngle * visionMultiplier, 30f, 180f);

            float tunedDashRange = Mathf.Max(1f, baseDashRange * dashMultiplier);
            minDashDistance = Mathf.Clamp(
                baseMinDashDistance * minDashMultiplier,
                0.5f,
                Mathf.Max(0.5f, tunedDashRange - 0.25f)
            );
            dashRange = Mathf.Max(tunedDashRange, minDashDistance + 0.25f);

            destructibleDashRange = Mathf.Max(
                dashRange,
                baseDestructibleDashRange * destructibleDashMultiplier
            );
            speedBoostRange = Mathf.Max(2f, baseSpeedBoostRange * speedBoostMultiplier);
            safeDistance = Mathf.Max(0.5f, baseSafeDistance * safeDistanceMultiplier);
            strafeSpeed = Mathf.Max(0f, baseStrafeSpeed * strafeMultiplier);
            repositionInterval = Mathf.Max(0.35f, baseRepositionInterval * repositionMultiplier);
            predictionTime = Mathf.Clamp(basePredictionTime * predictionMultiplier, 0.1f, 1.5f);
        }

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
            InitiateEvasionFrom(currentTarget);
        }

        private void InitiateEvasionFrom(Transform threat)
        {
            if (threat == null)
                return;

            Vector3 awayFromTarget = (transform.position - threat.position).normalized;
            Vector3 randomOffset = new Vector3(
                Random.Range(-1f, 1f),
                0,
                Random.Range(-1f, 1f)
            ).normalized;

            evasionDirection = (
                awayFromTarget + randomOffset * GetEvasionRandomOffsetWeight()
            ).normalized;
            evasionTimer = EVASION_DURATION * GetEvasionDurationMultiplier();
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

        private bool CanUseSpeedBoostAbility()
        {
            return !isExecutingSpeedBoost
                && movementController != null
                && movementController.SpeedBoostAbility != null
                && movementController.SpeedBoostAbility.CanActivate;
        }

        private void TryUseSpeedBoostDuringEvasion()
        {
            float chance = 0f;

            switch (resolvedPersonality)
            {
                case AIPersonality.Survivor:
                    chance = 0.45f + LowHealthPressure * 0.25f;
                    break;
                case AIPersonality.Trickster:
                    chance = 0.2f;
                    break;
                case AIPersonality.Rival:
                    chance = lastAdaptiveTuning.recentDamagePressure * 0.2f;
                    break;
            }

            if (chance <= 0f || !CanUseSpeedBoostAbility())
                return;

            if (Random.value < Mathf.Clamp01(chance))
                StartCoroutine(ExecuteSpeedBoostSequence(false));
        }

        private float GetEvasionDurationMultiplier()
        {
            switch (resolvedPersonality)
            {
                case AIPersonality.Hunter:
                    return 0.7f;
                case AIPersonality.Brawler:
                    return 0.55f;
                case AIPersonality.Trickster:
                    return 1.1f;
                case AIPersonality.Survivor:
                    return 1.45f;
                case AIPersonality.Rival:
                    return 0.85f;
                default:
                    return 1f;
            }
        }

        private float GetEvasionRandomOffsetWeight()
        {
            switch (resolvedPersonality)
            {
                case AIPersonality.Hunter:
                    return 0.15f;
                case AIPersonality.Brawler:
                    return 0.1f;
                case AIPersonality.Trickster:
                    return 0.75f;
                case AIPersonality.Survivor:
                    return 0.35f;
                case AIPersonality.Rival:
                    return 0.25f;
                default:
                    return 0.3f;
            }
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

        private float GetAdaptiveSafeDistance()
        {
            float caution = 1f - Profile.recklessness;
            float multiplier = Mathf.Lerp(0.75f, 1.35f, caution);
            multiplier += LowHealthPressure * 0.35f;
            return safeDistance * multiplier;
        }

        private bool ShouldRetaliateOnDamage()
        {
            switch (resolvedPersonality)
            {
                case AIPersonality.Brawler:
                case AIPersonality.Rival:
                    return true;
                case AIPersonality.Hunter:
                    return Random.value < 0.65f;
                case AIPersonality.Trickster:
                    return Random.value < 0.35f;
                default:
                    return false;
            }
        }

        private Transform ResolveCombatantTransform(GameObject source)
        {
            if (source == null)
                return null;

            PlayerHealthComponent sourceHealth = source.GetComponentInParent<PlayerHealthComponent>();
            if (sourceHealth != null && sourceHealth != healthComponent)
                return sourceHealth.transform;

            PhotonView sourceView = source.GetComponentInParent<PhotonView>();
            if (sourceView != null && sourceView.transform.root != transform.root)
                return sourceView.transform;

            return null;
        }

        private float GetDamageReactionEvasionChance()
        {
            float chance = Profile.evasionOnDamageChance;
            chance += LowHealthPressure * 0.25f;
            chance -= Profile.recklessness * 0.1f;

            switch (resolvedPersonality)
            {
                case AIPersonality.Hunter:
                    chance -= 0.25f;
                    break;
                case AIPersonality.Brawler:
                    chance -= 0.45f;
                    break;
                case AIPersonality.Trickster:
                    chance += 0.12f;
                    break;
                case AIPersonality.Survivor:
                    chance += 0.35f;
                    break;
                case AIPersonality.Rival:
                    chance -= 0.12f;
                    chance += lastAdaptiveTuning.recentDamagePressure * 0.2f;
                    break;
            }

            return Mathf.Clamp01(chance);
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

        private void EnsureUtilityProfile()
        {
            if (utilityProfile != null || runtimeUtilityProfile != null)
                return;

            runtimeUtilityProfile = ScriptableObject.CreateInstance<AIUtilityProfile>();
            runtimeUtilityProfile.name = $"{name}_RuntimeAIUtilityProfile";
            runtimeUtilityProfile.hideFlags = HideFlags.DontSave;
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
                    profile.aggressiveness += 0.25f;
                    profile.predictionAccuracy += 0.25f;
                    profile.speedBoostUseProbability += 0.25f;
                    profile.targetPersistence += 0.3f;
                    profile.evasionOnDamageChance -= 0.18f;
                    profile.recklessness += 0.05f;
                    profile.reactionTime -= 0.06f;
                    profile.decisionInterval -= 0.05f;
                    profile.attackCooldownMin -= 0.02f;
                    profile.attackCooldownMax -= 0.08f;
                    break;
                case AIPersonality.Brawler:
                    profile.aggressiveness += 0.35f;
                    profile.dashUseProbability += 0.25f;
                    profile.recklessness += 0.35f;
                    profile.evasionOnDamageChance -= 0.35f;
                    profile.strafeProbability -= 0.25f;
                    profile.predictionAccuracy -= 0.05f;
                    profile.targetPersistence -= 0.15f;
                    profile.attackCooldownMin -= 0.04f;
                    profile.attackCooldownMax -= 0.12f;
                    break;
                case AIPersonality.Trickster:
                    profile.dashUseProbability -= 0.12f;
                    profile.strafeProbability += 0.35f;
                    profile.predictionAccuracy += 0.12f;
                    profile.speedBoostUseProbability += 0.18f;
                    profile.evasionOnDamageChance += 0.05f;
                    profile.targetPersistence -= 0.22f;
                    profile.recklessness += 0.12f;
                    profile.decisionInterval -= 0.04f;
                    break;
                case AIPersonality.Survivor:
                    profile.evasionOnDamageChance += 0.35f;
                    profile.speedBoostUseProbability += 0.25f;
                    profile.strafeProbability += 0.25f;
                    profile.recklessness -= 0.45f;
                    profile.dashUseProbability -= 0.45f;
                    profile.targetPersistence -= 0.25f;
                    profile.attackCooldownMin += 0.14f;
                    profile.attackCooldownMax += 0.22f;
                    break;
                case AIPersonality.Rival:
                    profile.targetPersistence += 0.35f;
                    profile.aggressiveness += 0.18f + tuning.recentDamagePressure * 0.2f;
                    profile.predictionAccuracy += 0.12f;
                    profile.dashUseProbability += tuning.recentDamagePressure * 0.25f;
                    profile.recklessness += tuning.recentDamagePressure * 0.2f;
                    profile.reactionTime -= tuning.recentDamagePressure * 0.04f;
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

            ApplyPersonalityTacticalBias();

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

            if (source == null)
                return;

            Transform attacker = ResolveCombatantTransform(source);

            if (attacker != null && ShouldRetaliateOnDamage())
            {
                currentTarget = attacker;
                lastKnownTargetPosition = currentTarget.position;
            }

            if (Random.value < GetDamageReactionEvasionChance())
            {
                InitiateEvasionFrom(attacker != null ? attacker : source.transform);
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

            if (showDebugInfo)
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!showDebugInfo)
                return;

            GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 310, 280));
            GUILayout.Label($"=== AI: {gameObject.name} ===");
            GUILayout.Label($"Personality: {resolvedPersonality}");
            GUILayout.Label($"Adaptive Pressure: {adaptivePressure:F2}");
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Utility: {lastUtilityAction} ({lastUtilityScore:F2})");
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
#endif
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
