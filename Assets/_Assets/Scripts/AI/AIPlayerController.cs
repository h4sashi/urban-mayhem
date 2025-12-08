using System.Collections.Generic;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Controllers;
using Hanzo.Player.Core;
using Hanzo.Player.Input;
using UnityEngine;

namespace Hanzo.AI
{
    /// <summary>
    /// AI Controller that simulates player behavior
    /// ENHANCED: Better debug logging to track movement issues
    /// </summary>
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerHealthComponent))]
    [RequireComponent(typeof(PlayerStateController))]
    public class AIPlayerController : MonoBehaviour
    {
        [Header("AI Settings")]
        [SerializeField]
        private AIBehaviorProfile behaviorProfile;

        [SerializeField]
        private float updateInterval = 0.1f;

        [SerializeField]
        private float decisionDelay = 0.2f;

        [Header("Target Selection")]
        [SerializeField]
        private float targetScanRadius = 30f;

        [SerializeField]
        private float targetSwitchCooldown = 3f;

        [SerializeField]
        private LayerMask playerLayer;

        [Header("Combat")]
        [SerializeField]
        private float optimalDashRange = 8f;

        [SerializeField]
        private float minDashRange = 3f;

        [SerializeField]
        private float maxDashRange = 15f;

        [SerializeField]
        private float retreatHealthThreshold = 0.3f;

        [Header("Movement")]
        [SerializeField]
        private float wanderRadius = 20f;

        [SerializeField]
        private float arrivalDistance = 2f;

        [SerializeField]
        private float obstacleAvoidanceDistance = 5f;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugGizmos = true;

        [SerializeField]
        private bool verboseLogging = true; // ENABLED BY DEFAULT for debugging

        // Components
        private PlayerMovementController movementController;
        private PlayerHealthComponent healthComponent;
        private PlayerStateController stateController;
        private PlayerInputHandler inputHandler;

        // AI State
        private AIState currentState = AIState.Idle;
        private GameObject currentTarget;
        private Vector3 currentDestination;
        private float lastTargetSwitchTime;
        private float lastUpdateTime;
        private float lastDecisionTime;
        private float stateTimer;

        // Decision making
        private Queue<AIAction> actionQueue = new Queue<AIAction>();
        private AIAction currentAction;

        // Movement input (we'll send this directly)
        private Vector2 currentMoveInput;

        // Abilities
        private bool wantsDash;
        private bool wantsSpeedBoost;
        private float lastDashTime;
        private float lastSpeedBoostTime;

        // Disable PhotonView checks for AI
        private bool isAIControlled = true;
        
        // Debug tracking
        private int framesSinceInput = 0;

        private void Awake()
        {
            movementController = GetComponent<PlayerMovementController>();
            healthComponent = GetComponent<PlayerHealthComponent>();
            stateController = GetComponent<PlayerStateController>();
            inputHandler = GetComponent<PlayerInputHandler>();

            Debug.Log($"[AI] Awake - Found components: Movement={movementController != null}, Health={healthComponent != null}, State={stateController != null}, Input={inputHandler != null}");

            // CRITICAL: Mark input handler as AI-controlled
            if (inputHandler != null)
            {
                inputHandler.SetAIControlled(true);
                Debug.Log("[AI] ✓ Input handler marked as AI-controlled");
            }
            else
            {
                Debug.LogError("[AI] PlayerInputHandler not found!");
            }

            if (behaviorProfile == null)
            {
                Debug.LogWarning("[AI] No behavior profile assigned! Using default settings.");
            }
        }

        private void Start()
        {
            // Subscribe to events
            if (healthComponent != null)
            {
                healthComponent.OnDamageTaken += OnDamageTaken;
                healthComponent.OnPlayerDied += OnDeath;
                healthComponent.OnPlayerRespawned += OnRespawn;
            }

            if (stateController != null)
            {
                stateController.OnStunStarted += OnStunned;
                stateController.OnStunEnded += OnStunRecovered;
            }

            // Disable mobile UI for AI
            if (inputHandler != null)
            {
                inputHandler.SwitchToMobileControls(false);
            }

            // Start in patrol state so AI immediately starts moving
            Debug.Log("[AI] Starting in PATROL state");
            ChangeState(AIState.Patrol);
        }

        private void Update()
        {
            // Don't update if dead or stunned
            if (!healthComponent.IsAlive || stateController.IsStunned)
            {
                ClearMovementInput();
                SendMovementInput();
                return;
            }

            // Update state machine at intervals
            if (Time.time - lastUpdateTime >= updateInterval)
            {
                lastUpdateTime = Time.time;
                UpdateAI();
            }

            // Execute current action
            ExecuteCurrentAction();

            // CRITICAL: Send input every frame for smooth movement
            SendMovementInput();
            
            // Debug tracking
            if (currentMoveInput.magnitude > 0.1f)
            {
                framesSinceInput = 0;
            }
            else
            {
                framesSinceInput++;
                if (framesSinceInput == 100) // Log every 100 frames of no input
                {
                    Debug.LogWarning($"[AI] No movement input for 100 frames! State: {currentState}");
                }
            }
        }

        private void SendMovementInput()
        {
            if (inputHandler == null)
            {
                Debug.LogError("[AI] InputHandler is null!");
                return;
            }

            // Log for debugging - show when we're sending movement
            if (verboseLogging && currentMoveInput.magnitude > 0.1f)
            {
                Debug.Log($"[AI] 🎮 Sending move input: {currentMoveInput} (State: {currentState})");
            }

            // Send move input using the public method
            inputHandler.SendMoveInput(currentMoveInput);

            // Handle ability inputs
            if (wantsDash && CanUseDash())
            {
                Debug.Log("[AI] 💨 Triggering Dash!");
                inputHandler.TriggerDash();
                wantsDash = false;
                lastDashTime = Time.time;
            }

            if (wantsSpeedBoost && CanUseSpeedBoost())
            {
                Debug.Log("[AI] ⚡ Triggering Speed Boost!");
                inputHandler.TriggerSpeedBoost();
                wantsSpeedBoost = false;
                lastSpeedBoostTime = Time.time;
            }
        }

        private void UpdateAI()
        {
            stateTimer += updateInterval;

            // Make decisions at intervals
            if (Time.time - lastDecisionTime >= decisionDelay)
            {
                lastDecisionTime = Time.time;
                MakeDecision();
            }

            // Update state-specific behavior
            switch (currentState)
            {
                case AIState.Idle:
                    UpdateIdle();
                    break;
                case AIState.Patrol:
                    UpdatePatrol();
                    break;
                case AIState.Chase:
                    UpdateChase();
                    break;
                case AIState.Attack:
                    UpdateAttack();
                    break;
                case AIState.Retreat:
                    UpdateRetreat();
                    break;
                case AIState.Evade:
                    UpdateEvade();
                    break;
            }
        }

        private void MakeDecision()
        {
            // Low health? Retreat
            if (healthComponent.CurrentHealth / healthComponent.MaxHealth < retreatHealthThreshold)
            {
                if (currentState != AIState.Retreat)
                {
                    ChangeState(AIState.Retreat);
                    return;
                }
            }

            // Find or update target
            if (currentTarget == null || Time.time - lastTargetSwitchTime > targetSwitchCooldown)
            {
                FindTarget();
            }

            // No target? Patrol or idle
            if (currentTarget == null)
            {
                if (currentState == AIState.Chase || currentState == AIState.Attack)
                {
                    ChangeState(Random.value > 0.5f ? AIState.Patrol : AIState.Idle);
                }
                return;
            }

            // Calculate distance to target
            float distanceToTarget = Vector3.Distance(
                transform.position,
                currentTarget.transform.position
            );

            // Decision tree based on distance and state
            if (distanceToTarget <= maxDashRange && distanceToTarget >= minDashRange)
            {
                if (currentState != AIState.Attack)
                {
                    ChangeState(AIState.Attack);
                }
            }
            else if (distanceToTarget > maxDashRange)
            {
                if (currentState != AIState.Chase)
                {
                    ChangeState(AIState.Chase);
                }
            }
            else if (distanceToTarget < minDashRange)
            {
                if (currentState != AIState.Evade)
                {
                    ChangeState(AIState.Evade);
                }
            }
        }

        #region State Updates

        private void UpdateIdle()
        {
            // Stay idle or occasionally patrol
            if (stateTimer > GetBehaviorValue(BehaviorStat.IdleTime))
            {
                if (verboseLogging)
                    Debug.Log("[AI] Idle timeout - switching to Patrol");
                ChangeState(AIState.Patrol);
            }

            ClearMovementInput();
        }

        private void UpdatePatrol()
        {
            // Pick new destination if we don't have one or reached it
            if (
                currentDestination == Vector3.zero
                || Vector3.Distance(transform.position, currentDestination) < arrivalDistance
            )
            {
                currentDestination = GetRandomPatrolPoint();
                if (verboseLogging)
                    Debug.Log($"[AI] 🎯 New patrol destination: {currentDestination}");
            }

            MoveTowards(currentDestination);

            // Occasionally stop to idle
            if (stateTimer > 15f && Random.value < 0.3f)
            {
                ChangeState(AIState.Idle);
            }
        }

        private void UpdateChase()
        {
            if (currentTarget == null)
            {
                ChangeState(AIState.Idle);
                return;
            }

            // Move towards target
            MoveTowards(currentTarget.transform.position);

            // Consider using speed boost to close distance
            float distanceToTarget = Vector3.Distance(
                transform.position,
                currentTarget.transform.position
            );
            if (distanceToTarget > optimalDashRange * 1.5f && CanUseSpeedBoost())
            {
                TryUseSpeedBoost();
            }
        }

        private void UpdateAttack()
        {
            if (currentTarget == null)
            {
                ChangeState(AIState.Idle);
                return;
            }

            float distanceToTarget = Vector3.Distance(
                transform.position,
                currentTarget.transform.position
            );

            // Position for optimal dash
            if (distanceToTarget > optimalDashRange)
            {
                MoveTowards(currentTarget.transform.position);
            }
            else if (distanceToTarget < minDashRange)
            {
                // Back up a bit
                Vector3 awayDirection = (
                    transform.position - currentTarget.transform.position
                ).normalized;
                MoveTowards(transform.position + awayDirection * 5f);
            }
            else
            {
                // In optimal range, strafe and dash
                CircleStrafe(currentTarget.transform.position, distanceToTarget);

                // Try to dash at target
                if (CanUseDash() && ShouldDashNow())
                {
                    TryUseDash();
                }
            }
        }

        private void UpdateRetreat()
        {
            // Move away from nearest threat
            GameObject nearestThreat = FindNearestPlayer();

            if (nearestThreat != null)
            {
                Vector3 retreatDirection = (
                    transform.position - nearestThreat.transform.position
                ).normalized;
                currentDestination = transform.position + retreatDirection * wanderRadius;
                MoveTowards(currentDestination);

                // Use speed boost to escape
                if (CanUseSpeedBoost())
                {
                    TryUseSpeedBoost();
                }
            }
            else
            {
                ChangeState(AIState.Idle);
            }

            // Return to normal behavior when health recovers
            if (
                healthComponent.CurrentHealth / healthComponent.MaxHealth
                > retreatHealthThreshold + 0.2f
            )
            {
                ChangeState(AIState.Idle);
            }
        }

        private void UpdateEvade()
        {
            if (currentTarget == null)
            {
                ChangeState(AIState.Idle);
                return;
            }

            // Create distance from target
            Vector3 evadeDirection = (
                transform.position - currentTarget.transform.position
            ).normalized;
            currentDestination = transform.position + evadeDirection * 10f;
            MoveTowards(currentDestination);

            float distanceToTarget = Vector3.Distance(
                transform.position,
                currentTarget.transform.position
            );

            // Once at safe distance, switch back to attack
            if (distanceToTarget > optimalDashRange)
            {
                ChangeState(AIState.Attack);
            }
        }

        #endregion

        #region Movement Helpers

        private void MoveTowards(Vector3 destination)
        {
            Vector3 direction = (destination - transform.position);
            direction.y = 0; // Flatten to horizontal plane

            if (direction.magnitude < 0.1f)
            {
                ClearMovementInput();
                return;
            }

            direction.Normalize();

            // Avoid obstacles
            if (DetectObstacle(direction, out Vector3 avoidanceDirection))
            {
                direction = avoidanceDirection;
            }

            currentMoveInput = new Vector2(direction.x, direction.z);
            
            if (verboseLogging && Time.frameCount % 60 == 0) // Log every 60 frames
            {
                Debug.Log($"[AI] 🚶 Moving towards {destination}, input: {currentMoveInput}");
            }
        }

        private void CircleStrafe(Vector3 center, float radius)
        {
            // Strafe around target in a circle
            Vector3 toCenter = center - transform.position;
            toCenter.y = 0;

            // Get perpendicular direction (for strafing)
            Vector3 strafeDirection = Vector3.Cross(toCenter, Vector3.up).normalized;

            // Randomly change strafe direction
            if (Random.value < 0.02f)
            {
                strafeDirection = -strafeDirection;
            }

            // Mix forward and strafe movement
            Vector3 moveDirection = (
                toCenter.normalized * 0.3f + strafeDirection * 0.7f
            ).normalized;

            currentMoveInput = new Vector2(moveDirection.x, moveDirection.z);
        }

        private bool DetectObstacle(Vector3 direction, out Vector3 avoidanceDirection)
        {
            avoidanceDirection = direction;

            // Raycast to detect obstacles
            if (
                Physics.Raycast(
                    transform.position + Vector3.up,
                    direction,
                    out RaycastHit hit,
                    obstacleAvoidanceDistance
                )
            )
            {
                // Turn perpendicular to obstacle
                avoidanceDirection = Vector3.Cross(hit.normal, Vector3.up).normalized;
                return true;
            }

            return false;
        }

        private Vector3 GetRandomPatrolPoint()
        {
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomPoint =
                transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

            // Make sure point is on navmesh/ground
            if (
                Physics.Raycast(
                    randomPoint + Vector3.up * 10f,
                    Vector3.down,
                    out RaycastHit hit,
                    20f
                )
            )
            {
                return hit.point;
            }

            return randomPoint;
        }

        private void ClearMovementInput()
        {
            currentMoveInput = Vector2.zero;
        }

        #endregion

        #region Target Selection

        private void FindTarget()
        {
            GameObject nearestPlayer = FindNearestPlayer();

            if (nearestPlayer != null)
            {
                currentTarget = nearestPlayer;
                lastTargetSwitchTime = Time.time;

                if (verboseLogging)
                {
                    Debug.Log($"[AI] 🎯 Found target: {currentTarget.name}");
                }
            }
        }

        private GameObject FindNearestPlayer()
        {
            Collider[] players = Physics.OverlapSphere(
                transform.position,
                targetScanRadius,
                playerLayer
            );

            GameObject nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var col in players)
            {
                // Skip self
                if (col.transform.root == transform.root)
                    continue;

                // Check if player is alive
                PlayerHealthComponent targetHealth =
                    col.GetComponentInParent<PlayerHealthComponent>();
                if (targetHealth == null || !targetHealth.IsAlive)
                    continue;

                float distance = Vector3.Distance(transform.position, col.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = col.gameObject;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        #endregion

        #region Ability Usage

        private bool CanUseDash()
        {
            if (movementController.DashAbility == null)
                return false;

            float dashCooldownBuffer = GetBehaviorValue(BehaviorStat.DashCooldown);
            return movementController.DashAbility.CanActivate
                && Time.time - lastDashTime >= dashCooldownBuffer;
        }

        private bool ShouldDashNow()
        {
            if (currentTarget == null)
                return false;

            float distanceToTarget = Vector3.Distance(
                transform.position,
                currentTarget.transform.position
            );

            if (distanceToTarget < minDashRange || distanceToTarget > maxDashRange)
                return false;

            Vector3 toTarget = (currentTarget.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, toTarget);

            if (dot < 0.7f)
                return false;

            float dashChance = GetBehaviorValue(BehaviorStat.Aggression) * 0.01f;
            return Random.value < dashChance;
        }

        private void TryUseDash()
        {
            wantsDash = true;
            lastDashTime = Time.time;

            if (verboseLogging)
            {
                Debug.Log("[AI] 💨 Using Dash!");
            }
        }

        private bool CanUseSpeedBoost()
        {
            float boostCooldownBuffer = GetBehaviorValue(BehaviorStat.SpeedBoostCooldown);
            return Time.time - lastSpeedBoostTime >= boostCooldownBuffer;
        }

        private void TryUseSpeedBoost()
        {
            wantsSpeedBoost = true;
            lastSpeedBoostTime = Time.time;

            if (verboseLogging)
            {
                Debug.Log("[AI] ⚡ Using Speed Boost!");
            }
        }

        #endregion

        #region Input Simulation

        private void ExecuteCurrentAction()
        {
            if (currentAction == null && actionQueue.Count > 0)
            {
                currentAction = actionQueue.Dequeue();
            }

            if (currentAction != null)
            {
                currentAction.Execute(this);

                if (currentAction.IsComplete)
                {
                    currentAction = null;
                }
            }
        }

        #endregion

        #region State Management

        private void ChangeState(AIState newState)
        {
            if (verboseLogging)
            {
                Debug.Log($"[AI] 🔄 State change: {currentState} -> {newState}");
            }

            currentState = newState;
            stateTimer = 0f;
            
            // Clear destination when changing states
            if (newState == AIState.Idle)
            {
                currentDestination = Vector3.zero;
            }
        }

        #endregion

        #region Event Handlers

        private void OnDamageTaken(float damage, GameObject source, DamageType type)
        {
            if (verboseLogging)
            {
                Debug.Log($"[AI] 💥 Took {damage} damage from {source?.name ?? "unknown"}");
            }

            // React to damage
            if (currentState == AIState.Idle || currentState == AIState.Patrol)
            {
                // Switch to combat mode
                if (source != null)
                {
                    currentTarget = source;
                    ChangeState(AIState.Attack);
                }
            }
        }

        private void OnDeath()
        {
            if (verboseLogging)
            {
                Debug.Log("[AI] ☠️ Died");
            }

            ClearMovementInput();
            currentTarget = null;
        }

        private void OnRespawn()
        {
            if (verboseLogging)
            {
                Debug.Log("[AI] 🔄 Respawned");
            }

            ChangeState(AIState.Patrol); // Start patrolling again
        }

        private void OnStunned()
        {
            if (verboseLogging)
            {
                Debug.Log("[AI] 😵 Stunned");
            }

            ClearMovementInput();
        }

        private void OnStunRecovered()
        {
            if (verboseLogging)
            {
                Debug.Log("[AI] ✅ Recovered from stun");
            }
        }

        #endregion

        #region Behavior Profile Helpers

        private float GetBehaviorValue(BehaviorStat stat)
        {
            if (behaviorProfile == null)
            {
                return GetDefaultBehaviorValue(stat);
            }

            return behaviorProfile.GetValue(stat);
        }

        private float GetDefaultBehaviorValue(BehaviorStat stat)
        {
            return stat switch
            {
                BehaviorStat.Aggression => 50f,
                BehaviorStat.Caution => 50f,
                BehaviorStat.ReactionTime => 0.3f,
                BehaviorStat.Accuracy => 70f,
                BehaviorStat.DashCooldown => 2f,
                BehaviorStat.SpeedBoostCooldown => 5f,
                BehaviorStat.IdleTime => 5f,
                _ => 50f,
            };
        }

        #endregion

        public Vector2 GetAIMoveInput()
        {
            return currentMoveInput;
        }

        #region Debug

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos)
                return;

            // Draw target scan radius
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, targetScanRadius);

            // Draw current target
            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, currentTarget.transform.position);
                Gizmos.DrawWireSphere(currentTarget.transform.position, 1f);
            }

            // Draw destination
            if (currentDestination != Vector3.zero)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, currentDestination);
                Gizmos.DrawWireSphere(currentDestination, arrivalDistance);
                
                // Draw movement direction
                Vector3 dir = (currentDestination - transform.position).normalized;
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(transform.position, dir * 3f);
            }

            // Draw attack ranges
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, optimalDashRange);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, minDashRange);
            Gizmos.DrawWireSphere(transform.position, maxDashRange);
        }

        private void OnGUI()
        {
            if (!verboseLogging)
                return;

            GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 310, 350));
            GUILayout.Label("=== AI DEBUG ===");
            GUILayout.Label($"State: {currentState}");
            GUILayout.Label($"Target: {currentTarget?.name ?? "None"}");
            GUILayout.Label(
                $"Health: {healthComponent.CurrentHealth:F1}/{healthComponent.MaxHealth:F1}"
            );
            GUILayout.Label($"Move Input: {currentMoveInput}");
            GUILayout.Label($"Input Magnitude: {currentMoveInput.magnitude:F3}");
            GUILayout.Label($"Destination: {currentDestination}");
            if (currentDestination != Vector3.zero)
            {
                float dist = Vector3.Distance(transform.position, currentDestination);
                GUILayout.Label($"Distance to Dest: {dist:F1}m");
            }
            GUILayout.Label($"Can Dash: {CanUseDash()}");
            GUILayout.Label($"Can Speed Boost: {CanUseSpeedBoost()}");
            GUILayout.Label($"Stunned: {stateController.IsStunned}");
            GUILayout.Label($"Falling: {stateController.IsFalling}");
            GUILayout.Label($"Grounded: {stateController.IsGrounded}");

            if (currentTarget != null)
            {
                float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
                GUILayout.Label($"Distance to Target: {dist:F1}m");
            }

            GUILayout.EndArea();
        }

        #endregion

        private void OnDestroy()
        {
            if (healthComponent != null)
            {
                healthComponent.OnDamageTaken -= OnDamageTaken;
                healthComponent.OnPlayerDied -= OnDeath;
                healthComponent.OnPlayerRespawned -= OnRespawn;
            }

            if (stateController != null)
            {
                stateController.OnStunStarted -= OnStunned;
                stateController.OnStunEnded -= OnStunRecovered;
            }
        }
    }

    #region Supporting Classes

    public enum AIState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Retreat,
        Evade,
    }

    public enum BehaviorStat
    {
        Aggression,
        Caution,
        ReactionTime,
        Accuracy,
        DashCooldown,
        SpeedBoostCooldown,
        IdleTime,
    }

    public abstract class AIAction
    {
        public bool IsComplete { get; protected set; }
        public abstract void Execute(AIPlayerController controller);
    }

    #endregion
}