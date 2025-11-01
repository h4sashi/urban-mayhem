using UnityEngine;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Input;
using Hanzo.Player.Abilities;
using Hanzo.Player.Movement.States;
using Hanzo.Player.Core;
using Photon.Pun;
using Cinemachine;

namespace Hanzo.Player.Controllers
{
    /// <summary>
    /// Main player movement controller with camera-relative input
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerMovementController : MonoBehaviour, IMovementController
    {
        [Header("Settings")]
        [SerializeField] private MovementSettings movementSettings;
        [SerializeField] private AbilitySettings abilitySettings;

        [Header("Camera Settings")]
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private Transform cameraFollowTarget;
        [SerializeField] private Transform cameraLookAtTarget;
        [SerializeField] private bool useCameraRelativeMovement = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

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
        private IMovementState currentState;

        // Abilities
        private DashAbility dashAbility;
        public DashAbility DashAbility => dashAbility;
        private SpeedBoostAbility speedBoostAbility;

        // Camera-relative input
        private Vector2 rawInput;
        private Vector3 cameraRelativeInput;

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

            // Auto-find virtual camera if not assigned
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

            // Setup camera only for local player
            if (photonView.IsMine)
            {
                SetupVirtualCamera();
            }
            else
            {
                // Disable camera for non-local players
                if (virtualCamera != null)
                {
                    virtualCamera.gameObject.SetActive(false);
                }
            }
        }

        private void SetupVirtualCamera()
        {
            if (virtualCamera == null)
            {
                Debug.LogWarning("PlayerMovementController: Virtual Camera not found as child! Camera setup skipped.");
                return;
            }

            // Set Follow target
            if (cameraFollowTarget != null)
            {
                virtualCamera.Follow = cameraFollowTarget;
            }
            else
            {
                virtualCamera.Follow = transform;
                Debug.LogWarning("PlayerMovementController: Camera Follow Target not assigned, using player transform.");
            }

            // Set LookAt target
            if (cameraLookAtTarget != null)
            {
                virtualCamera.LookAt = cameraLookAtTarget;
            }
            else
            {
                virtualCamera.LookAt = transform;
                Debug.LogWarning("PlayerMovementController: Camera LookAt Target not assigned, using player transform.");
            }

            Debug.Log($"Virtual Camera setup complete for local player: {photonView.ViewID}");
        }

        private void InitializeStates()
        {
            movingState = new MovingState(movementSettings);
            idleState = new IdleState();

            currentState = idleState;
            currentState.Enter(this);
        }

        private void InitializeAbilities()
        {
            // Initialize Dash Ability
            dashAbility = new DashAbility(abilitySettings);
            dashAbility.Initialize(this);

            // Create dashing state with dash ability reference
            dashingState = new DashingState(dashAbility);

            // Initialize Speed Boost Ability
            speedBoostAbility = new SpeedBoostAbility(abilitySettings);
            speedBoostAbility.Initialize(this);

            // CRITICAL: Subscribe to speed multiplier changes
            speedBoostAbility.OnSpeedMultiplierChanged += OnSpeedBoostMultiplierChanged;
        }

        private void SubscribeToInput()
        {
            if (inputHandler == null) return;

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

        private void Update()
        {
            if (!photonView.IsMine) return;

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

            // Update current state
            currentState?.Update(this);

            // Check for state transitions
            CheckStateTransitions();
        }

        /// <summary>
        /// Converts joystick input to camera-relative direction
        /// </summary>
        private void ProcessCameraRelativeInput()
        {
            if (mainCamera == null || rawInput.magnitude < 0.01f)
            {
                cameraRelativeInput = Vector3.zero;
                return;
            }

            // Get camera forward and right vectors
            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 cameraRight = mainCamera.transform.right;

            // Flatten to horizontal plane (ignore Y)
            cameraForward.y = 0f;
            cameraRight.y = 0f;
            cameraForward.Normalize();
            cameraRight.Normalize();

            // Calculate camera-relative movement direction
            cameraRelativeInput = (cameraForward * rawInput.y + cameraRight * rawInput.x).normalized;

            // Pass to moving state as Vector2 (x, z)
            Vector2 processedInput = new Vector2(cameraRelativeInput.x, cameraRelativeInput.z);
            movingState?.SetMoveInput(processedInput);
        }

        private void CheckStateTransitions()
        {
            // Priority: Dashing > Moving > Idle

            // Check if we should enter dashing state
            if (dashAbility.IsActive && !(currentState is DashingState))
            {
                ChangeState(dashingState);
            }
            // Check if we should exit dashing state
            else if (!dashAbility.IsActive && currentState is DashingState)
            {
                // Return to moving if there's input, otherwise idle
                if (rawInput.magnitude > 0.1f)
                {
                    ChangeState(movingState);
                }
                else
                {
                    ChangeState(idleState);
                }
            }
            // Normal state transitions (not dashing)
            else if (!(currentState is DashingState))
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

        private void HandleMoveInput(Vector2 input)
        {
            if (!photonView.IsMine) return;
            if (stateController != null && stateController.IsStunned) return;

            // Store raw input
            rawInput = input;

            // If not using camera-relative, pass directly to moving state
            if (!useCameraRelativeMovement)
            {
                movingState?.SetMoveInput(input);
            }
        }

        private void HandleDashInput()
        {
            if (!photonView.IsMine) return;
            if (stateController != null && stateController.IsStunned) return;

            if (dashAbility != null && dashAbility.TryActivate())
            {
                Debug.Log("Dash activated!");
            }
        }

        private void HandleSpeedBoostInput()
        {
            if (!photonView.IsMine) return;
            if (stateController != null && stateController.IsStunned) return;

            if (speedBoostAbility != null && speedBoostAbility.TryActivate())
            {
                Debug.Log($"Speed Boost activated! Stack Level: {speedBoostAbility.StackLevel}");
            }
        }

        /// <summary>
        /// Called by SpeedBoostAbility when speed multiplier changes
        /// </summary>
        private void OnSpeedBoostMultiplierChanged(float multiplier)
        {
            // Apply speed multiplier to MovingState
            if (movingState != null)
            {
                movingState.SetSpeedMultiplier(multiplier);
                Debug.Log($"Movement speed multiplier updated to: {multiplier}x");
            }
        }

        // IMovementController Interface Methods
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
            if (newState == null) return;

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

        // Public API for external systems (e.g., pickup system)
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

        // Public methods to toggle camera-relative movement
        public void SetCameraRelativeMovement(bool enabled)
        {
            useCameraRelativeMovement = enabled;
        }

        // Photon RPC Methods for network synchronization
        [PunRPC]
        private void RPC_PlayDashVisuals()
        {
            if (photonView.IsMine) return;

            if (animator != null)
            {
                animator.SetBool("DASH", true);
            }

            Debug.Log("Remote player started dashing");
        }

        [PunRPC]
        private void RPC_StopDashVisuals()
        {
            if (photonView.IsMine) return;

            if (animator != null)
            {
                animator.SetBool("DASH", false);
            }

            Debug.Log("Remote player stopped dashing");
        }

        [PunRPC]
        private void RPC_PlaySpeedBoostVisuals(int stackLevel)
        {
            if (photonView.IsMine) return;

            if (animator != null)
            {
                animator.SetBool("SPEEDBOOST", true);
            }

            Debug.Log($"Remote player activated Speed Boost (Stack {stackLevel})");
        }

        [PunRPC]
        private void RPC_StopSpeedBoostVisuals()
        {
            if (photonView.IsMine) return;

            if (animator != null)
            {
                animator.SetBool("SPEEDBOOST", false);
            }

            Debug.Log("Remote player deactivated Speed Boost");
        }

         private void OnTriggerEnter(Collider other) {
            if(other.CompareTag("Item")){
                other.gameObject.SetActive(false);
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || !photonView.IsMine) return;

            GUILayout.BeginArea(new Rect(10, 10, 350, 450));
            GUILayout.Label("=== PLAYER MOVEMENT ===");
            GUILayout.Label($"State: {currentState?.GetType().Name ?? "None"}");
            GUILayout.Label($"Velocity: {rb.velocity.magnitude:F2} m/s");
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

            GUILayout.Space(10);
            GUILayout.Label("=== CAMERA ===");
            GUILayout.Label($"Virtual Camera: {(virtualCamera != null ? "Found" : "Not Found")}");
            GUILayout.Label($"Main Camera: {(mainCamera != null ? "Found" : "Not Found")}");

            if (stateController != null)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Stunned: {stateController.IsStunned}");
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

            // Clean up virtual camera when player is destroyed (only if it was unparented)
            if (virtualCamera != null && virtualCamera.transform.parent == null)
            {
                Destroy(virtualCamera.gameObject);
            }
        }
    }
}