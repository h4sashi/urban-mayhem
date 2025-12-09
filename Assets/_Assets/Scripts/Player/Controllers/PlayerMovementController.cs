using Cinemachine;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Abilities;
using Hanzo.Player.Core;
using Hanzo.Player.Input;
using Hanzo.Player.Movement.States;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Player.Controllers
{
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerMovementController : MonoBehaviour, IMovementController
    {
        [Header("Settings")]
        [SerializeField]
        private MovementSettings movementSettings;

        [SerializeField]
        private AbilitySettings abilitySettings;

        [Header("Camera Settings")]
        [SerializeField]
        private CinemachineVirtualCamera virtualCamera;

        [SerializeField]
        private Transform cameraFollowTarget;

        [SerializeField]
        private Transform cameraLookAtTarget;

        [SerializeField]
        private bool useCameraRelativeMovement = true;

        [Header("Ground Detection")]
        [SerializeField]
        private LayerMask groundLayer = ~0;

        [SerializeField]
        private float groundCheckDistance = 0.3f;

        [SerializeField]
        private float fallCheckInterval = 0.1f;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        // Components
        private Rigidbody rb;
        private Animator animator;
        private PhotonView photonView;
        private PlayerInputHandler inputHandler;
        private PlayerStateController stateController;
        private Camera mainCamera;

        // States
        private MovingState movingState;
        private IdleState idleState;
        private DashingState dashingState;
        private FallingState fallingState;
        private IMovementState currentState;

        // Abilities
        private DashAbility dashAbility;
        public DashAbility DashAbility => dashAbility;
        private SpeedBoostAbility speedBoostAbility;

        // Camera-relative input
        private Vector2 rawInput;
        private Vector3 cameraRelativeInput;

        // Fall detection
        private float lastFallCheck = 0f;

        // IMovementController Interface
        public Vector3 Position => transform.position;
        public Vector3 Velocity => rb.velocity;
        public Transform Transform => transform;
        public Rigidbody Rigidbody => rb;
        public Animator Animator => animator;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            photonView = GetComponent<PhotonView>();
            inputHandler = GetComponent<PlayerInputHandler>();
            stateController = GetComponent<PlayerStateController>();

            if (virtualCamera == null)
            {
                virtualCamera = GetComponentInChildren<CinemachineVirtualCamera>(true);
            }

            if (movementSettings == null)
            {
                Debug.LogError("PlayerMovementController: MovementSettings not assigned!");
            }

            if (abilitySettings == null)
            {
                Debug.LogError("PlayerMovementController: AbilitySettings not assigned!");
            }

            InitializeStates();
            InitializeAbilities();
            SubscribeToInput();
        }

        private void Start()
        {
            mainCamera = Camera.main;

            if (photonView.IsMine)
            {
                SetupVirtualCamera();
            }
            else
            {
                if (virtualCamera != null)
                {
                    virtualCamera.gameObject.SetActive(false);
                }
            }
        }

        private void Update()
        {
            if (!photonView.IsMine)
                return;

            // Don't allow movement if stunned
            if (stateController != null && stateController.IsStunned)
            {
                return;
            }

            // Convert raw input to camera-relative movement
            if (useCameraRelativeMovement)
            {
                ProcessCameraRelativeInput();
            }

            // Update abilities
            dashAbility?.Update();
            speedBoostAbility?.Update();

            // Check for falling state changes
            if (Time.time - lastFallCheck > fallCheckInterval)
            {
                lastFallCheck = Time.time;
                CheckForFalling();
            }

            // CRITICAL FIX: Always update current state
            // Remove the condition that prevented updates during falling
            currentState?.Update(this);

            // Check for state transitions
            CheckStateTransitions();
        }

        /// <summary>
        /// SIMPLIFIED: Check falling state based ONLY on PlayerStateController
        /// </summary>
        private void CheckForFalling()
        {
            // Don't check for falling while dashing
            if (currentState is DashingState)
                return;

            if (stateController == null)
                return;

            // ENTER FALLING: Controller says we're falling and we're not in FallingState
            if (stateController.IsFalling && !(currentState is FallingState))
            {
                Debug.Log("[Movement] Entering FallingState");
                ChangeState(fallingState);
            }
            // EXIT FALLING: Controller says we're grounded and we're in FallingState
            else if (stateController.IsGrounded && !stateController.IsFalling && currentState is FallingState)
            {
                Debug.Log("[Movement] Landing detected - exiting FallingState");
                
                // SIMPLE: Just check current input to determine next state
                if (rawInput.magnitude > 0.1f)
                {
                    Debug.Log("[Movement] Has input - transitioning to Moving");
                    ChangeState(movingState);
                }
                else
                {
                    Debug.Log("[Movement] No input - transitioning to Idle");
                    ChangeState(idleState);
                }
            }
        }

        private void CheckStateTransitions()
        {
            // CRITICAL FIX: Allow normal transitions even during falling
            // The state itself will handle whether to apply movement
            
            // Dash can interrupt any state
            if (dashAbility.IsActive && !(currentState is DashingState))
            {
                ChangeState(dashingState);
                return;
            }

            // Exit dash state
            if (!dashAbility.IsActive && currentState is DashingState)
            {
                if (rawInput.magnitude > 0.1f)
                {
                    ChangeState(movingState);
                }
                else
                {
                    ChangeState(idleState);
                }
                return;
            }

            // Normal idle/moving transitions (NOT blocked during falling)
            // The MovingState will check if grounded before applying forces
            if (!(currentState is DashingState) && !(currentState is FallingState))
            {
                if (rawInput.magnitude > 0.1f && currentState is IdleState)
                {
                    ChangeState(movingState);
                }
                else if (rawInput.magnitude <= 0.1f && currentState is MovingState)
                {
                    ChangeState(idleState);
                }
            }
        }

        private void SetupVirtualCamera()
        {
            if (virtualCamera == null)
            {
                Debug.LogWarning("PlayerMovementController: Virtual Camera not found!");
                return;
            }

            if (cameraFollowTarget != null)
            {
                virtualCamera.Follow = cameraFollowTarget;
            }
            else
            {
                virtualCamera.Follow = transform;
            }

            if (cameraLookAtTarget != null)
            {
                virtualCamera.LookAt = cameraLookAtTarget;
            }
            else
            {
                virtualCamera.LookAt = transform;
            }

            Debug.Log($"Virtual Camera setup complete for local player: {photonView.ViewID}");
        }

        private void InitializeStates()
        {
            movingState = new MovingState(movementSettings);
            idleState = new IdleState();
            fallingState = new FallingState(groundLayer);

            currentState = idleState;
            currentState.Enter(this);
        }

        private void InitializeAbilities()
        {
            dashAbility = new DashAbility(abilitySettings);
            dashAbility.Initialize(this);

            dashingState = new DashingState(dashAbility);

            speedBoostAbility = new SpeedBoostAbility(abilitySettings);
            speedBoostAbility.Initialize(this);

            speedBoostAbility.OnSpeedMultiplierChanged += OnSpeedBoostMultiplierChanged;
        }

        private void SubscribeToInput()
        {
            if (inputHandler == null)
                return;

            inputHandler.OnMoveInput += HandleMoveInput;
            inputHandler.OnDashInput += HandleDashInput;
            inputHandler.OnSpeedBoostInput += HandleSpeedBoostInput;
        }

        private void OnEnable()
        {
            if (inputHandler != null)
            {
                inputHandler.OnMoveInput += HandleMoveInput;
                inputHandler.OnDashInput += HandleDashInput;
                inputHandler.OnSpeedBoostInput += HandleSpeedBoostInput;
            }
        }

        private void OnDisable()
        {
            if (inputHandler != null)
            {
                inputHandler.OnMoveInput -= HandleMoveInput;
                inputHandler.OnDashInput -= HandleDashInput;
                inputHandler.OnSpeedBoostInput -= HandleSpeedBoostInput;
            }
        }

        public CinemachineVirtualCamera GetCam()
        {
            return virtualCamera;
        }

        private void ProcessCameraRelativeInput()
        {
            if (mainCamera == null || rawInput.magnitude < 0.01f)
            {
                cameraRelativeInput = Vector3.zero;
                return;
            }

            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 cameraRight = mainCamera.transform.right;

            cameraForward.y = 0f;
            cameraRight.y = 0f;
            cameraForward.Normalize();
            cameraRight.Normalize();

            cameraRelativeInput = (
                cameraForward * rawInput.y + cameraRight * rawInput.x
            ).normalized;

            Vector2 processedInput = new Vector2(cameraRelativeInput.x, cameraRelativeInput.z);
            movingState?.SetMoveInput(processedInput);
        }

        private bool IsGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            return Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer);
        }

        private void HandleMoveInput(Vector2 input)
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            rawInput = input;

            if (!useCameraRelativeMovement)
            {
                movingState?.SetMoveInput(input);
            }
        }

        private void HandleDashInput()
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            if (dashAbility != null && dashAbility.TryActivate())
            {
                Debug.Log("Dash activated!");
            }
        }

        private void HandleSpeedBoostInput()
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            if (speedBoostAbility != null && speedBoostAbility.TryActivate())
            {
                Debug.Log($"Speed Boost activated! Stack Level: {speedBoostAbility.StackLevel}");
            }
        }

        private void OnSpeedBoostMultiplierChanged(float multiplier)
        {
            if (movingState != null)
            {
                movingState.SetSpeedMultiplier(multiplier);
                Debug.Log($"Movement speed multiplier updated to: {multiplier}x");
            }
        }

        public void SetVelocity(Vector3 velocity)
        {
            if (rb != null)
            {
                rb.velocity = velocity;
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (rb != null)
            {
                rb.AddForce(force, mode);
            }
        }

        public void ChangeState(IMovementState newState)
        {
            if (newState == null)
                return;

            if (currentState != null)
            {
                if (!currentState.CanTransitionTo(newState))
                {
                    return;
                }

                currentState.Exit(this);
            }

            currentState = newState;
            currentState.Enter(this);
        }

        public void AddDashStack()
        {
            dashAbility?.AddStack();
        }

        public void AddSpeedBoostStack()
        {
            speedBoostAbility?.AddStack();
        }

        public void ResetDashStacks()
        {
            dashAbility?.ResetStacks();
        }

        public void ResetSpeedBoostStacks()
        {
            speedBoostAbility?.ResetStacks();
        }

        public void SetCameraRelativeMovement(bool enabled)
        {
            useCameraRelativeMovement = enabled;
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 350, 500));
            GUILayout.Label("=== PLAYER MOVEMENT ===");
            GUILayout.Label($"State: {currentState?.GetType().Name ?? "None"}");
            GUILayout.Label($"Velocity: {rb.velocity.magnitude:F2} m/s");
            GUILayout.Label($"Velocity Y: {rb.velocity.y:F2} m/s");
            GUILayout.Label($"Raw Input: {rawInput}");
            GUILayout.Label($"Camera-Relative: {useCameraRelativeMovement}");
            if (useCameraRelativeMovement)
            {
                GUILayout.Label($"Processed Input: ({cameraRelativeInput.x:F2}, {cameraRelativeInput.z:F2})");
            }

            GUILayout.Space(10);
            GUILayout.Label("=== DASH ABILITY ===");
            GUILayout.Label($"Active: {dashAbility?.IsActive ?? false}");
            GUILayout.Label($"Cooldown: {dashAbility?.CooldownRemaining ?? 0f:F2}s");
            GUILayout.Label($"Stack Level: {dashAbility?.StackLevel ?? 0}");

            GUILayout.Space(10);
            GUILayout.Label("=== SPEED BOOST ABILITY ===");
            GUILayout.Label($"Active: {speedBoostAbility?.IsActive ?? false}");
            GUILayout.Label($"Cooldown: {speedBoostAbility?.CooldownRemaining ?? 0f:F2}s");
            GUILayout.Label($"Stack Level: {speedBoostAbility?.StackLevel ?? 0}");
            GUILayout.Label($"Speed Multiplier: {speedBoostAbility?.CurrentSpeedMultiplier ?? 1f:F2}x");

            if (stateController != null)
            {
                GUILayout.Space(10);
                GUILayout.Label("=== STATE CONTROLLER ===");
                GUILayout.Label($"Stunned: {stateController.IsStunned}");
                GUILayout.Label($"Falling: {stateController.IsFalling}");
                GUILayout.Label($"Grounded: {stateController.IsGrounded}");
            }

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            dashAbility?.Cleanup();
            speedBoostAbility?.Cleanup();

            if (speedBoostAbility != null)
            {
                speedBoostAbility.OnSpeedMultiplierChanged -= OnSpeedBoostMultiplierChanged;
            }

            if (virtualCamera != null && virtualCamera.transform.parent == null)
            {
                Destroy(virtualCamera.gameObject);
            }
        }
    }
}