using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Controllers;
using Hanzo.Player.Core;
using Hanzo.Player.Input;
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
        private float lastDecisionTime;
        private float lastRepositionTime;
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
        // personalAttackDelay is this AI's running extra pause that is re-drawn
        // after every attack and periodically nudged by the resync guard.
        private float personalAttackDelay = 0f;
        private float attackCooldownTimer = 0f; // counts down; dash blocked while > 0
        private float resyncTimer = 0f; // counts down to next timing nudge

        // ── Coroutines ────────────────────────────────────────────────────────
        private Coroutine aiLoopCoroutine;

        // ── Properties ───────────────────────────────────────────────────────
        public AIState CurrentState => currentState;
        public Transform CurrentTarget => currentTarget;

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

            if (inputHandler == null || !inputHandler.IsAIControlled())
                Debug.LogError("[AI] PlayerInputHandler did not detect AIPlayerController!");

            if (behaviorProfile == null)
            {
                behaviorProfile = ScriptableObject.CreateInstance<AIBehaviorProfile>();
                behaviorProfile.Initialize();
                Debug.LogWarning("[AI] No behavior profile assigned, using defaults");
            }

            patrolCenter = transform.position;

            if (healthComponent != null)
                healthComponent.SetRespawnPosition(transform.position);

            // Seed this AI's first personal attack delay with a unique random value
            // so no two AIs on the same frame begin with the same rhythm.
            RollNewAttackCooldown();
            resyncTimer = behaviorProfile.resyncCheckInterval;
        }

        private void OnEnable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnDamageTaken += OnDamageTaken;
                healthComponent.OnPlayerDied += OnDeath;
                healthComponent.OnPlayerRespawned += OnRespawn;
                healthComponent.OnPlayerDied += () =>
                {
                    isDyingStun = true;
                    StopMovement();
                };
            }

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
            }
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
                    float nudge = Random.Range(0f, behaviorProfile.resyncJitterMax);
                    attackCooldownTimer += nudge;
                    resyncTimer = behaviorProfile.resyncCheckInterval;

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
            float startupJitter = Random.Range(0f, behaviorProfile.startupJitterMax);
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
                float thisReaction = Random.Range(reactionTimeMin, reactionTimeMax);
                float thisDecision = Random.Range(decisionIntervalMin, decisionIntervalMax);

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

                if (dist <= dashRange && dist >= minDashDistance && CanUseDash())
                {
                    currentState = AIState.Dashing;
                }
                // NEW — check for a nearby destructible worth launching at the target
                else if (CanUseDash() && HasLaunchableDestructibleToward(currentTarget))
                {
                    currentState = AIState.DashingDestructible;
                }
                else if (dist <= safeDistance)
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
                    idleTimer = idleTimeAtWaypoint;
                    StopMovement();
                }
                else
                {
                    idleTimer -= decisionInterval;
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
        private bool HasLaunchableDestructibleToward(Transform target)
        {
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
                    _pendingDestructibleTarget = col.transform;
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
                if (Random.value < behaviorProfile.strafeProbability)
                {
                    Vector3 strafeDir = Vector3.Cross(direction, Vector3.up).normalized;
                    strafeDir *= Random.value > 0.5f ? 1f : -1f;
                    direction = (direction + strafeDir * 0.5f).normalized;
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
            evasionTimer -= decisionInterval;
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
                behaviorProfile.attackCooldownMin,
                behaviorProfile.attackCooldownMax
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

            float closestDistance = Mathf.Infinity;
            Transform closestTarget = null;

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
                if (distance < closestDistance)
                {
                    closestDistance = distance;
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
                return target.position + targetRb.velocity * predictionTime;

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
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
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
                && Random.value < behaviorProfile.speedBoostUseProbability;
        }

        // ─────────────────────────────────────────────────────────────────────
        // EVENT HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private void OnDamageTaken(float damage, GameObject source, DamageType damageType)
        {
            if (!healthComponent.IsAlive || isDyingStun)
                return;

            if (source != null && Random.value < behaviorProfile.evasionOnDamageChance)
            {
                evasionDirection = (transform.position - source.transform.position).normalized;
                evasionTimer = EVASION_DURATION;
                currentState = AIState.Evading;
            }
        }

        private void OnDeath()
        {
            StopMovement();
            currentState = AIState.Idle;
            currentTarget = null;
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
            resyncTimer = behaviorProfile.resyncCheckInterval;

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
