using UnityEngine;
using Hanzo.Player.Core;
using Hanzo.Player.Controllers;
using Hanzo.Player.Input;
using Photon.Pun;

namespace Hanzo.AI.Enemies
{
    /// <summary>
    /// Multiplayer-Synced AI Player Controller
    /// Master Client controls AI logic, syncs to all clients via RPCs
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerStateController))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class AIPlayerController : MonoBehaviourPun
    {
        [Header("AI Settings")]
        [SerializeField] private AIBehaviorSettings behaviorSettings;
        
        [Header("Detection")]
        [SerializeField] private LayerMask playerLayer = 1 << 6;
        [SerializeField] private float detectionRadius = 15f;
        [SerializeField] private float detectionInterval = 0.1f;
        [SerializeField] private float loseTargetDistance = 25f;
        
        [Header("Combat")]
        [SerializeField] private float attackRange = 3f;
        [SerializeField] private float meleeAttackRange = 2.5f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private float attackKnockbackForce = 10f;
        [SerializeField] private float attackStunDuration = 2f;
        
        [Header("Combat Behavior")]
        [SerializeField] private bool enableMeleeAttacks = true;
        [SerializeField] private bool enableDashAttacks = true;
        [SerializeField] private float dashAttackMultiplier = 1.5f;
        [SerializeField] private bool wanderAfterSuccessfulHit = true;
        [SerializeField] private float wanderAfterHitDuration = 3f;
        
        [Header("Movement Behavior")]
        [SerializeField] private float closeRangeStopDistance = 1.5f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private bool alwaysFaceTarget = true;
        
        [Header("Tactical Behavior")]
        [SerializeField] private bool enableTacticalRetreat = false;
        [SerializeField] private float retreatDistance = 8f;
        [SerializeField] private bool retreatWhenAbilityOnCooldown = true;
        [SerializeField] private float minRetreatDuration = 2f;
        
        [Header("Abilities")]
        [SerializeField] private bool canUseDash = true;
        [SerializeField] private bool canUseSpeedBoost = true;
        [SerializeField] private float dashUsageChance = 0.4f;
        [SerializeField] private float dashMinDistance = 3f;
        [SerializeField] private float dashMaxDistance = 10f;
        [SerializeField] private float dashCooldownTime = 3f;
        [SerializeField] private float dashDecisionInterval = 0.3f;
        [SerializeField] private float speedBoostUsageChance = 0.6f;
        [SerializeField] private float speedBoostMinDistance = 5f;
        [SerializeField] private float speedBoostMaxDistance = 12f;
        
        [Header("Wandering")]
        [SerializeField] private float wanderRadius = 10f;
        [SerializeField] private float wanderChangeInterval = 3f;
        [SerializeField] private float idleWaitTime = 1f;
        
        [Header("Collision Detection")]
        [SerializeField] private float collisionCheckRadius = 1.2f;
        [SerializeField] private LayerMask attackCollisionLayers = ~0;
        
        [Header("Network Sync")]
        [SerializeField] private float positionSyncRate = 10f; // Updates per second
        [SerializeField] private float rotationSyncRate = 10f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private bool showGizmos = true;
        
        // Core components
        private PlayerMovementController movementController;
        private PlayerStateController stateController;
        private PlayerInputHandler inputHandler;
        private Rigidbody rb;
        private Animator animator;
        
        // AI State
        private AIState currentState = AIState.Idle;
        private Transform currentTarget;
        private Vector3 wanderTarget;
        private Vector3 retreatTarget;
        private float lastDetectionCheck;
        private float lastAttackTime = -999f;
        private float lastWanderChange;
        private float lastDashDecision;
        private float lastSpeedBoostDecision;
        private float idleStartTime;
        private float retreatStartTime;
        private float postHitWanderStartTime;
        private Vector3 spawnPosition;
        private float lastDashTime = -999f;
        private float lastSpeedBoostTime = -999f;
        
        // AI Input
        private Vector2 aiMoveInput;
        
        // Attack tracking
        private bool hasDamagedThisFrame = false;
        private Transform lastDamagedTarget;
        private float lastDamageTime;
        private bool justHitPlayer = false;
        
        // Network sync
        private Vector3 networkPosition;
        private Quaternion networkRotation;
        private float lastPositionSyncTime;
        private float lastRotationSyncTime;
        
        // Animation hashes
        private static readonly int RunHash = Animator.StringToHash("RUN");
        private static readonly int DashHash = Animator.StringToHash("DASH");
        
        private enum AIState
        {
            Idle,
            Wandering,
            Chasing,
            Attacking,
            Retreating
        }
        
        private void Awake()
        {
            movementController = GetComponent<PlayerMovementController>();
            stateController = GetComponent<PlayerStateController>();
            inputHandler = GetComponent<PlayerInputHandler>();
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            
            if (behaviorSettings == null)
            {
                Debug.LogError("[AI] AIBehaviorSettings not assigned!");
            }
            
            if (inputHandler == null)
            {
                Debug.LogError("[AI] PlayerInputHandler is REQUIRED but missing!");
            }
            
            spawnPosition = transform.position;
            networkPosition = transform.position;
            networkRotation = transform.rotation;
        }
        
        private void Start()
        {
            // Only Master Client controls AI logic
            if (!PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected)
            {
                // Other clients: disable AI logic, only receive network updates
                this.enabled = false;
                return;
            }
            
            ChangeState(AIState.Idle);
            
            Debug.Log($"[AI] {gameObject.name} initialized as MASTER CLIENT - Melee: {enableMeleeAttacks}, Dash: {enableDashAttacks}");
        }
        
        private void Update()
        {
            // Master Client: Run AI logic
            if (PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)
            {
                UpdateAILogic();
                SyncTransformToNetwork();
            }
            // Other Clients: Interpolate to network position
            else
            {
                InterpolateTransform();
            }
        }
        
        #region AI Logic (Master Client Only)
        
        private void UpdateAILogic()
        {
            hasDamagedThisFrame = false;
            
            if (stateController.IsStunned)
            {
                aiMoveInput = Vector2.zero;
                SendInputToController();
                return;
            }
            
            DetectPlayers();
            
            switch (currentState)
            {
                case AIState.Idle:
                    UpdateIdle();
                    break;
                case AIState.Wandering:
                    UpdateWandering();
                    break;
                case AIState.Chasing:
                    UpdateChasing();
                    break;
                case AIState.Attacking:
                    UpdateAttacking();
                    break;
                case AIState.Retreating:
                    UpdateRetreating();
                    break;
            }
            
            if (alwaysFaceTarget && currentTarget != null)
            {
                FaceTarget(currentTarget.position);
            }
            
            if (enableDashAttacks && movementController?.DashAbility != null && movementController.DashAbility.IsActive)
            {
                CheckDashCollisions();
            }
            
            SendInputToController();
        }
        
        #endregion
        
        #region Network Sync
        
        private void SyncTransformToNetwork()
        {
            if (!PhotonNetwork.IsConnected) return;
            
            // Sync position
            if (Time.time - lastPositionSyncTime > 1f / positionSyncRate)
            {
                lastPositionSyncTime = Time.time;
                if (Vector3.Distance(transform.position, networkPosition) > 0.1f)
                {
                    networkPosition = transform.position;
                    photonView.RPC("RPC_SyncPosition", RpcTarget.Others, transform.position);
                }
            }
            
            // Sync rotation
            if (Time.time - lastRotationSyncTime > 1f / rotationSyncRate)
            {
                lastRotationSyncTime = Time.time;
                if (Quaternion.Angle(transform.rotation, networkRotation) > 5f)
                {
                    networkRotation = transform.rotation;
                    photonView.RPC("RPC_SyncRotation", RpcTarget.Others, transform.rotation);
                }
            }
        }
        
        private void InterpolateTransform()
        {
            // Smooth interpolation for other clients
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 15f);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, Time.deltaTime * 15f);
        }
        
        [PunRPC]
        private void RPC_SyncPosition(Vector3 position)
        {
            networkPosition = position;
        }
        
        [PunRPC]
        private void RPC_SyncRotation(Quaternion rotation)
        {
            networkRotation = rotation;
        }
        
        [PunRPC]
        private void RPC_SyncState(int stateIndex)
        {
            currentState = (AIState)stateIndex;
            Debug.Log($"[AI Remote] State synced: {currentState}");
        }
        
        [PunRPC]
        private void RPC_TriggerDash()
        {
            // Trigger dash visuals on all clients
            if (inputHandler != null)
            {
                inputHandler.TriggerAIDash();
            }
            Debug.Log($"[AI Remote] Dash VFX triggered");
        }
        
        [PunRPC]
        private void RPC_TriggerSpeedBoost()
        {
            // Trigger speed boost visuals on all clients
            if (inputHandler != null)
            {
                inputHandler.TriggerAISpeedBoost();
            }
            Debug.Log($"[AI Remote] Speed Boost VFX triggered");
        }
        
        [PunRPC]
        private void RPC_TriggerAttack(Vector3 targetPosition)
        {
            // Sync attack animation on all clients
            if (animator != null)
            {
                // Play attack animation if you have one
                // animator.SetTrigger("Attack");
            }
            Debug.Log($"[AI Remote] Attack animation triggered");
        }
        
        #endregion
        
        #region Player Detection
        
        private void DetectPlayers()
        {
            if (wanderAfterSuccessfulHit && justHitPlayer)
            {
                float timeSinceHit = Time.time - postHitWanderStartTime;
                if (timeSinceHit < wanderAfterHitDuration)
                {
                    return;
                }
                else
                {
                    justHitPlayer = false;
                }
            }
            
            Collider[] hits = Physics.OverlapSphere(
                transform.position, 
                detectionRadius, 
                playerLayer,
                QueryTriggerInteraction.Ignore
            );
            
            Transform closestPlayer = null;
            float closestDistance = float.MaxValue;
            
            foreach (var hit in hits)
            {
                if (hit.transform == transform) continue;
                
                var playerController = hit.GetComponent<PlayerMovementController>();
                if (playerController == null) continue;
                
                if (hit.GetComponent<AIPlayerController>() != null) continue;
                
                float distance = Vector3.Distance(transform.position, hit.transform.position);
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPlayer = hit.transform;
                }
            }
            
            if (closestPlayer != null)
            {
                bool targetChanged = currentTarget != closestPlayer;
                currentTarget = closestPlayer;
                
                if (targetChanged)
                {
                    Debug.Log($"[AI] New target acquired: {closestPlayer.name} at {closestDistance:F2}m");
                }
                
                if (currentState == AIState.Retreating)
                {
                    if (Time.time - retreatStartTime < minRetreatDuration)
                        return;
                }
                
                if (closestDistance <= meleeAttackRange)
                {
                    if (currentState != AIState.Attacking)
                    {
                        Debug.Log($"[AI] PRIORITY: ENTERING ATTACK STATE - Distance: {closestDistance:F2}m");
                        ChangeState(AIState.Attacking);
                    }
                }
                else if (closestDistance <= attackRange)
                {
                    if (currentState != AIState.Attacking && currentState != AIState.Retreating)
                    {
                        Debug.Log($"[AI] ENTERING ATTACK STATE - Distance: {closestDistance:F2}m");
                        ChangeState(AIState.Attacking);
                    }
                }
                else if (currentState != AIState.Chasing && currentState != AIState.Retreating)
                {
                    Debug.Log($"[AI] ENTERING CHASE STATE - Distance: {closestDistance:F2}m");
                    ChangeState(AIState.Chasing);
                }
            }
            else
            {
                if (currentTarget != null)
                {
                    Debug.Log("[AI] Target lost - wandering");
                    currentTarget = null;
                    ChangeState(AIState.Wandering);
                }
            }
        }
        
        #endregion
        
        #region State Updates
        
        private void UpdateIdle()
        {
            aiMoveInput = Vector2.zero;
            
            if (Time.time - idleStartTime > idleWaitTime)
            {
                ChangeState(AIState.Wandering);
            }
        }
        
        private void UpdateWandering()
        {
            if (Time.time - lastWanderChange > wanderChangeInterval)
            {
                lastWanderChange = Time.time;
                wanderTarget = GetRandomWanderPoint();
            }
            
            Vector3 direction = (wanderTarget - transform.position);
            direction.y = 0;
            
            if (direction.magnitude > 1.5f)
            {
                direction.Normalize();
                aiMoveInput = new Vector2(direction.x, direction.z);
            }
            else
            {
                aiMoveInput = Vector2.zero;
                ChangeState(AIState.Idle);
            }
        }
        
        private void UpdateChasing()
        {
            if (currentTarget == null)
            {
                aiMoveInput = Vector2.zero;
                ChangeState(AIState.Wandering);
                return;
            }
            
            float distance = Vector3.Distance(transform.position, currentTarget.position);
            
            if (distance > loseTargetDistance)
            {
                currentTarget = null;
                aiMoveInput = Vector2.zero;
                ChangeState(AIState.Wandering);
                return;
            }
            
            // Try speed boost
            if (canUseSpeedBoost && 
                Time.time - lastSpeedBoostDecision > dashDecisionInterval)
            {
                lastSpeedBoostDecision = Time.time;
                
                if (distance >= speedBoostMinDistance && 
                    distance <= speedBoostMaxDistance &&
                    CanUseSpeedBoostAbility() &&
                    Random.value < speedBoostUsageChance)
                {
                    TryUseSpeedBoost();
                }
            }
            
            // Try dash
            if (canUseDash && Time.time - lastDashDecision > dashDecisionInterval)
            {
                lastDashDecision = Time.time;
                
                if (distance >= dashMinDistance && 
                    distance <= dashMaxDistance && 
                    CanUseDashAbility() &&
                    Random.value < dashUsageChance)
                {
                    TryUseDash();
                }
            }
            
            Vector3 direction = (currentTarget.position - transform.position);
            direction.y = 0;
            direction.Normalize();
            
            aiMoveInput = new Vector2(direction.x, direction.z);
            
            if (distance <= attackRange)
            {
                ChangeState(AIState.Attacking);
            }
        }
        
        private void UpdateAttacking()
        {
            if (currentTarget == null)
            {
                aiMoveInput = Vector2.zero;
                ChangeState(AIState.Wandering);
                return;
            }
            
            float distance = Vector3.Distance(transform.position, currentTarget.position);
            
            if (distance > attackRange * 1.8f)
            {
                ChangeState(AIState.Chasing);
                return;
            }
            
            if (distance > meleeAttackRange)
            {
                Vector3 direction = (currentTarget.position - transform.position).normalized;
                direction.y = 0;
                aiMoveInput = new Vector2(direction.x, direction.z);
            }
            else
            {
                aiMoveInput = Vector2.zero;
            }
            
            if (distance <= meleeAttackRange)
            {
                float timeSinceLastAttack = Time.time - lastAttackTime;
                bool canAttack = timeSinceLastAttack >= attackCooldown;
                
                if (canAttack)
                {
                    PerformMeleeAttack();
                }
            }
        }
        
        private void UpdateRetreating()
        {
            if (currentTarget == null)
            {
                ChangeState(AIState.Wandering);
                return;
            }
            
            Vector3 directionFromTarget = (transform.position - currentTarget.position).normalized;
            retreatTarget = transform.position + directionFromTarget * retreatDistance;
            
            Vector3 direction = (retreatTarget - transform.position);
            direction.y = 0;
            
            if (direction.magnitude > 1f)
            {
                direction.Normalize();
                aiMoveInput = new Vector2(direction.x, direction.z);
            }
            else
            {
                aiMoveInput = Vector2.zero;
            }
            
            float timeSinceRetreat = Time.time - retreatStartTime;
            float distanceFromTarget = Vector3.Distance(transform.position, currentTarget.position);
            
            if (timeSinceRetreat > minRetreatDuration && 
                (distanceFromTarget >= retreatDistance * 0.8f || !ShouldRetreat()))
            {
                Debug.Log("[AI] Retreat complete - re-engaging");
                ChangeState(AIState.Chasing);
            }
        }
        
        #endregion
        
        #region Tactical Decisions
        
        private bool ShouldRetreat()
        {
            if (!enableTacticalRetreat || currentTarget == null)
                return false;
            
            float timeSinceLastAttack = Time.time - lastAttackTime;
            if (timeSinceLastAttack < attackCooldown * 0.5f)
            {
                return false;
            }
            
            if (retreatWhenAbilityOnCooldown && canUseDash)
            {
                if (!CanUseDashAbility())
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private bool CanUseDashAbility()
        {
            if (movementController?.DashAbility == null)
                return false;
            
            return Time.time - lastDashTime >= dashCooldownTime && 
                   movementController.DashAbility.CooldownRemaining <= 0f;
        }
        
        private bool CanUseSpeedBoostAbility()
        {
            return movementController != null;
        }
        
        #endregion
        
        #region Input Simulation
        
        private void SendInputToController()
        {
            if (inputHandler == null)
            {
                Debug.LogError("[AI] PlayerInputHandler is null!");
                return;
            }
            
            inputHandler.SetAIInput(aiMoveInput);
        }
        
        #endregion
        
        #region Abilities
        
        private void TryUseDash()
        {
            if (inputHandler == null || currentTarget == null) return;
            
            if (!CanUseDashAbility()) return;
            
            inputHandler.TriggerAIDash();
            lastDashTime = Time.time;
            
            // Sync dash to all clients
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_TriggerDash", RpcTarget.Others);
            }
            
            Debug.Log($"[AI] DASHING towards {currentTarget.name}!");
        }
        
        private void TryUseSpeedBoost()
        {
            if (inputHandler == null || currentTarget == null) return;
            
            if (!CanUseSpeedBoostAbility()) return;
            
            inputHandler.TriggerAISpeedBoost();
            lastSpeedBoostTime = Time.time;
            
            // Sync speed boost to all clients
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_TriggerSpeedBoost", RpcTarget.Others);
            }
            
            Debug.Log($"[AI] SPEED BOOST activated - closing gap to {currentTarget.name}!");
        }
        
        #endregion
        
        #region Combat
        
        private void PerformMeleeAttack()
        {
            if (currentTarget == null)
            {
                Debug.LogWarning("[AI] PerformMeleeAttack called but no target!");
                return;
            }
            
            PlayerStateController targetState = currentTarget.GetComponent<PlayerStateController>();
            
            if (targetState == null)
            {
                Debug.LogWarning($"[AI] Target {currentTarget.name} has no PlayerStateController!");
                return;
            }
            
            if (targetState.IsStunned)
            {
                Debug.Log($"[AI] Target already stunned, skipping attack");
                return;
            }
            
            Vector3 knockbackDir = (currentTarget.position - transform.position).normalized;
            
            targetState.ApplyKnockbackAndStun(
                knockbackDir,
                attackKnockbackForce,
                attackStunDuration
            );
            
            lastAttackTime = Time.time;
            
            // Sync attack animation to all clients
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_TriggerAttack", RpcTarget.Others, currentTarget.position);
            }
            
            if (wanderAfterSuccessfulHit)
            {
                justHitPlayer = true;
                postHitWanderStartTime = Time.time;
                currentTarget = null;
                ChangeState(AIState.Wandering);
                Debug.Log($"[AI] ✓✓✓ HIT SUCCESSFUL! Wandering for {wanderAfterHitDuration}s before re-engaging");
            }
            else
            {
                Debug.Log($"[AI] ✓✓✓ MELEE ATTACK EXECUTED on {currentTarget.name}! Next attack in {attackCooldown}s");
            }
        }
        
        private void CheckDashCollisions()
        {
            if (hasDamagedThisFrame) return;
            
            Collider[] hits = Physics.OverlapSphere(
                transform.position,
                collisionCheckRadius,
                attackCollisionLayers,
                QueryTriggerInteraction.Ignore
            );
            
            foreach (var hit in hits)
            {
                if (hit.transform == transform) continue;
                
                PlayerStateController targetState = hit.GetComponent<PlayerStateController>();
                if (targetState == null) continue;
                
                if (hit.GetComponent<AIPlayerController>() != null) continue;
                
                // Check for clash
                PlayerMovementController targetMovement = hit.GetComponent<PlayerMovementController>();
                if (targetMovement != null && targetMovement.DashAbility != null && targetMovement.DashAbility.IsActive)
                {
                    Debug.Log($"[AI] CLASH! Both dashing - Player {hit.name} wins!");
                    
                    Vector3 knockbackDir = (transform.position - hit.transform.position).normalized;
                    stateController.ApplyKnockbackAndStun(
                        knockbackDir,
                        attackKnockbackForce * dashAttackMultiplier,
                        attackStunDuration
                    );
                    
                    hasDamagedThisFrame = true;
                    return;
                }
                
                if (lastDamagedTarget == hit.transform && Time.time - lastDamageTime < 0.5f)
                    continue;
                
                Vector3 knockbackDir2 = (hit.transform.position - transform.position).normalized;
                
                targetState.ApplyKnockbackAndStun(
                    knockbackDir2,
                    attackKnockbackForce * dashAttackMultiplier,
                    attackStunDuration
                );
                
                hasDamagedThisFrame = true;
                lastDamagedTarget = hit.transform;
                lastDamageTime = Time.time;
                
                Debug.Log($"[AI] DASH HIT {hit.name}!");
                break;
            }
        }
        
        private void FaceTarget(Vector3 targetPosition)
        {
            Vector3 lookDirection = (targetPosition - transform.position);
            lookDirection.y = 0;
            
            if (lookDirection.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * rotationSpeed
                );
            }
        }
        
        #endregion
        
        #region State Management
        
        private void ChangeState(AIState newState)
        {
            if (currentState == newState) return;
            
            AIState oldState = currentState;
            
            switch (currentState)
            {
                case AIState.Chasing:
                case AIState.Wandering:
                case AIState.Attacking:
                case AIState.Retreating:
                    aiMoveInput = Vector2.zero;
                    break;
            }
            
            currentState = newState;
            
            // Sync state to all clients
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_SyncState", RpcTarget.Others, (int)newState);
            }
            
            switch (newState)
            {
                case AIState.Idle:
                    idleStartTime = Time.time;
                    aiMoveInput = Vector2.zero;
                    break;
                    
                case AIState.Wandering:
                    lastWanderChange = Time.time;
                    wanderTarget = GetRandomWanderPoint();
                    break;
                    
                case AIState.Attacking:
                    aiMoveInput = Vector2.zero;
                    Debug.Log($"[AI] Entered ATTACKING state - Cooldown ready: {(Time.time - lastAttackTime) >= attackCooldown}");
                    break;
                    
                case AIState.Retreating:
                    retreatStartTime = Time.time;
                    if (currentTarget != null)
                    {
                        Vector3 directionFromTarget = (transform.position - currentTarget.position).normalized;
                        retreatTarget = transform.position + directionFromTarget * retreatDistance;
                    }
                    Debug.Log($"[AI] RETREATING to recharge abilities");
                    break;
            }
            
            if (showDebugInfo)
            {
                Debug.Log($"[AI] {gameObject.name}: {oldState} -> {newState}");
            }
        }
        
        #endregion
        
        #region Helpers
        
        private Vector3 GetRandomWanderPoint()
        {
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomPoint = spawnPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
            randomPoint.y = transform.position.y;
            return randomPoint;
        }
        
        #endregion
        
        #region Debug
        
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            
            Gizmos.color = new Color(1, 0.5f, 0, 0.5f);
            Gizmos.DrawWireSphere(transform.position, meleeAttackRange);
            
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, closeRangeStopDistance);
            
            if (enableDashAttacks)
            {
                Gizmos.color = new Color(0, 1, 1, 0.3f);
                Gizmos.DrawWireSphere(transform.position, collisionCheckRadius);
            }
            
            if (canUseDash)
            {
                Gizmos.color = new Color(0, 1, 1, 0.2f);
                Gizmos.DrawWireSphere(transform.position, dashMinDistance);
                Gizmos.color = new Color(0, 1, 1, 0.1f);
                Gizmos.DrawWireSphere(transform.position, dashMaxDistance);
            }
            if(currentTarget != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, currentTarget.position);
            }
        }
        #endregion
    }
}