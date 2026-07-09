using System;
using System.Collections;
using Cinemachine;
using Hanzo.Core.Interfaces;
using Hanzo.Player.Abilities;
using Hanzo.Player.Core;
using Hanzo.Player.Input;
using Hanzo.Player.Movement.States;
using Hanzo.Player.Weapons;
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

        [Header("Bazooka Camera Settings")]
        [Tooltip("Optional bazooka POV virtual camera. If empty, a child camera named fpsPOV is used.")]
        [SerializeField]
        private CinemachineVirtualCamera bazookaPovCamera;

        [SerializeField]
        private string bazookaPovCameraName = "fpsPOV";

        [Tooltip("Optional Follow target for the bazooka POV camera. Leave empty to keep the camera's current transform-driven setup.")]
        [SerializeField]
        private Transform bazookaCameraFollowTarget;

        [Tooltip("Optional LookAt target for the bazooka POV camera. Leave empty to keep the camera's current transform-driven setup.")]
        [SerializeField]
        private Transform bazookaCameraLookAtTarget;

        [SerializeField]
        private int normalCameraPriority = 10;

        [SerializeField]
        private int bazookaPovCameraPriority = 30;

        [Tooltip("Disables CinemachineCollider while bazooka POV is active to stop obstacle-avoidance snapping near the player/weapon.")]
        [SerializeField]
        private bool disableBazookaCameraColliderWhileActive = true;

        [Tooltip("Mutes Cinemachine noise while bazooka POV is active. The current fpsPOV scene camera has noise enabled, which reads as fidgeting.")]
        [SerializeField]
        private bool muteBazookaCameraNoiseWhileActive = true;

        [Tooltip("Disables the fpsPOV Aim Composer while bazooka aiming so drag input can directly pitch/yaw the virtual camera.")]
        [SerializeField]
        private bool disableBazookaCameraComposerWhileActive = true;

        [Tooltip("Disables the fpsPOV Body Transposer while bazooka aiming so the virtual camera follows the same direct aim rotation as the hip rig.")]
        [SerializeField]
        private bool disableBazookaCameraTransposerWhileActive = true;

        [Tooltip("Orbits fpsPOV around the player aim center while dragging so the player stays framed in the middle of the view.")]
        [SerializeField]
        private bool keepPlayerCenteredDuringBazookaAim = true;

        [Tooltip("Optional center point for bazooka camera orbit/framing. If empty, the player camera LookAt target is used.")]
        [SerializeField]
        private Transform bazookaCameraAimCenter;

        [Tooltip("Local offset from the bazooka camera aim center that should stay centered on screen.")]
        [SerializeField]
        private Vector3 bazookaCameraAimCenterOffset = Vector3.zero;

        [Header("Bazooka Aim Settings")]
        [Tooltip("Uses the dedicated mobile drag area for bazooka camera aiming.")]
        [SerializeField]
        private bool useBazookaAimDragArea = true;

        [SerializeField]
        private float bazookaAimYawDragSensitivity = 18f;

        [SerializeField]
        private float bazookaAimPitchDragSensitivity = 14f;

        [Tooltip("Optional fallback for keyboard/gamepad tests. Leave off on mobile so the movement joystick never controls bazooka aim.")]
        [SerializeField]
        private bool useMovementInputToAimBazooka = false;

        [SerializeField]
        private float bazookaAimYawSpeed = 75f;

        [SerializeField]
        private float bazookaAimPitchSpeed = 55f;

        [SerializeField]
        private Vector2 bazookaAimYawLimits = new Vector2(-45f, 45f);

        [SerializeField]
        private Vector2 bazookaAimPitchLimits = new Vector2(-22f, 26f);

        [Tooltip("Gradually rotates the player body when bazooka yaw reaches its rig limit, similar to lock-on turning.")]
        [SerializeField]
        private bool rotateBodyWhenBazookaYawAtLimit = true;

        [Tooltip("How close to the yaw limit body turning starts. 0.8 means turning starts at 80% of the yaw range.")]
        [SerializeField, Range(0f, 1f)]
        private float bazookaBodyTurnStartNormalized = 0.8f;

        [Tooltip("Maximum body turn speed in degrees per second while the player keeps aiming past the rig yaw range.")]
        [SerializeField]
        private float bazookaBodyTurnSpeed = 145f;

        [Tooltip("How quickly local rig yaw is recentered while body turning is active.")]
        [SerializeField]
        private float bazookaYawRecenteringSpeed = 120f;

        [SerializeField]
        private bool snapCameraBlendOnBazookaSwitch = true;

        [Header("Ground Detection")]
        [SerializeField]
        private LayerMask groundLayer = ~0;

        [SerializeField]
        private float groundCheckDistance = 0.3f;

        [SerializeField]
        private float fallCheckInterval = 0.1f;

        [Header("Bazooka Hold")]
        [Tooltip("Projectile weapon object shown while the bazooka hold is active.")]
        [SerializeField]
        private GameObject projectileWeapon;

        [Tooltip("Animator state played while the bazooka is held.")]
        [SerializeField]
        private string bazookaHoldStateName = "Bazooka_Hold";

        [SerializeField]
        private float bazookaHoldFadeDuration = 0.08f;

        [Tooltip("Enable in Play Mode to keep the bazooka hold pose and weapon active while positioning the projectile weapon.")]
        [SerializeField]
        private bool forceBazookaHoldForWeaponPositioning = false;

        [Tooltip("Prevents the local player from moving while the bazooka is active.")]
        [SerializeField]
        private bool lockMovementWhileBazookaActive = true;

        [Header("Bazooka Fire")]
        [SerializeField]
        private BazookaMissileLauncher bazookaMissileLauncher;

        [Tooltip("Optional firing direction override. Leave empty to fire along the fpsPOV camera forward.")]
        [SerializeField]
        private Transform bazookaFireDirection;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        // Components
        private Rigidbody rb;
        private Animator animator;
        private PhotonView photonView;
        private PhotonTransformViewClassic transformSync;
        private PlayerInputHandler inputHandler;
        private PlayerStateController stateController;
        private Camera mainCamera;
        private Quaternion lastNetworkRotation;
        private bool isBazookaHolding;
        private bool isBazookaHoldInputRequested;
        private bool lastForceBazookaHoldForWeaponPositioning;
        private bool hasSubscribedToInput;
        private bool hasSubscribedToStateController;
        private bool hasWarnedMissingProjectileWeapon;
        private bool hasWarnedMissingBazookaHoldState;
        private bool hasBazookaLockPosition;
        private Vector3 bazookaLockPosition;
        private CinemachineCollider bazookaPovCameraCollider;
        private CinemachineBasicMultiChannelPerlin bazookaPovCameraNoise;
        private CinemachineComposer bazookaPovCameraComposer;
        private CinemachineTransposer bazookaPovCameraTransposer;
        private bool hasCachedBazookaCameraOverrides;
        private bool cachedBazookaCameraColliderEnabled;
        private bool cachedBazookaCameraComposerEnabled;
        private bool cachedBazookaCameraTransposerEnabled;
        private float cachedBazookaCameraNoiseAmplitude;
        private float cachedBazookaCameraNoiseFrequency;
        private bool hasCachedBazookaCameraRestPose;
        private Vector3 bazookaCameraRestLocalPosition;
        private Quaternion bazookaCameraRestLocalRotation;
        private Vector3 bazookaCameraRestOffsetFromAimCenter;
        private Vector3 bazookaCameraRestRightAxis;
        private Vector3 bazookaCameraRestAimForward;
        private Vector3 bazookaCurrentAimForward;
        private Vector2 bazookaAimInput;
        private Vector2 bazookaAimAngles;
        private CinemachineBrain mainCameraBrain;
        private CinemachineBlendDefinition cachedDefaultCameraBlend;
        private Coroutine restoreCameraBlendCoroutine;
        private bool hasCachedDefaultCameraBlend;

        private const string BaseLayerName = "Base Layer";
        private static readonly int RunHash = Animator.StringToHash("RUN");

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
        public SpeedBoostAbility SpeedBoostAbility => speedBoostAbility;

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
        public bool IsBazookaHolding => isBazookaHolding;
        public Vector2 BazookaAimInput => bazookaAimInput;
        public Vector2 BazookaAimAngles => bazookaAimAngles;
        public Vector2 NormalizedBazookaAim => new Vector2(
            NormalizeSignedAxis(bazookaAimAngles.x, bazookaAimYawLimits),
            NormalizeSignedAxis(bazookaAimAngles.y, bazookaAimPitchLimits)
        );
        public event System.Action<bool> OnBazookaHoldChanged;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>(true);
            photonView = GetComponent<PhotonView>();
            transformSync = GetComponent<PhotonTransformViewClassic>();
            inputHandler = GetComponent<PlayerInputHandler>();
            stateController = GetComponent<PlayerStateController>();
            bazookaMissileLauncher = bazookaMissileLauncher != null
                ? bazookaMissileLauncher
                : GetComponentInChildren<BazookaMissileLauncher>(true);
            lastNetworkRotation = transform.rotation;
            ResetBazookaHoldStartupState();

            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            CacheVirtualCameras();

            if (movementSettings == null)
            {
                Debug.LogError("PlayerMovementController: MovementSettings not assigned!");
            }

            InitializeStates();
            InitializeAbilities();
        }

        private void Start()
        {
            mainCamera = Camera.main;
            mainCameraBrain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;

            if (photonView.IsMine)
            {
                SubscribeToInput();
                SubscribeToStateController();
                SetupVirtualCamera();
                SetBazookaCameraActive(false);
            }
            else
            {
                DisableCamerasForRemotePlayer();
            }
        }

        private void Update()
        {
            if (!photonView.IsMine)
                return;

            // Don't allow movement if stunned
            if (stateController != null && stateController.IsStunned)
            {
                if (isBazookaHolding)
                {
                    SetBazookaHold(false, true);
                }

                return;
            }

            UpdateForcedBazookaHold();

            if (isBazookaHolding)
            {
                UpdateBazookaAim(Time.deltaTime);
                ClearBazookaMovementInput();
                EnsureBazookaHoldAnimation();
                return;
            }

            // Convert raw input to camera-relative movement
            if (useCameraRelativeMovement)
            {
                ProcessCameraRelativeInput();
            }

            // Check for falling state changes
            if (Time.time - lastFallCheck > fallCheckInterval)
            {
                lastFallCheck = Time.time;
                CheckForFalling();
            }

            // Check for state transitions
            CheckStateTransitions();
        }

        private void FixedUpdate()
        {
            if (!photonView.IsMine)
                return;

            if (stateController != null && stateController.IsStunned)
            {
                UpdateNetworkMovementSync();
                return;
            }

            if (ShouldLockBazookaMovement())
            {
                EnforceBazookaMovementLock();
                UpdateNetworkMovementSync();
                return;
            }

            dashAbility?.Update();
            speedBoostAbility?.Update();
            currentState?.Update(this);
            UpdateNetworkMovementSync();
        }

        private void UpdateNetworkMovementSync()
        {
            if (transformSync == null)
                return;

            float turnSpeed = Quaternion.Angle(lastNetworkRotation, transform.rotation)
                / Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);

            transformSync.SetSynchronizedValues(rb.velocity, turnSpeed);
            lastNetworkRotation = transform.rotation;
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
                // Debug.Log("[Movement] Entering FallingState");
                ChangeState(fallingState);
            }
            // EXIT FALLING: Controller says we're grounded and we're in FallingState
            else if (stateController.IsGrounded && !stateController.IsFalling && currentState is FallingState)
            {
                // Debug.Log("[Movement] Landing detected - exiting FallingState");

                // SIMPLE: Just check current input to determine next state
                if (rawInput.magnitude > 0.1f)
                {
                    // Debug.Log("[Movement] Has input - transitioning to Moving");
                    ChangeState(movingState);
                }
                else
                {
                    // Debug.Log("[Movement] No input - transitioning to Idle");
                    ChangeState(idleState);
                }
            }
        }

        private void CheckStateTransitions()
        {
            // CRITICAL FIX: Allow normal transitions even during falling
            // The state itself will handle whether to apply movement

            // Dash can interrupt any state
            bool isDashActive = dashAbility != null && dashAbility.IsActive;

            if (isDashActive && dashingState != null && !(currentState is DashingState))
            {
                ChangeState(dashingState);
                return;
            }

            // Exit dash state
            if (!isDashActive && currentState is DashingState)
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
            CacheVirtualCameras();

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

            virtualCamera.Priority = normalCameraPriority;

            // Debug.Log($"Virtual Camera setup complete for local player: {photonView.ViewID}");
        }

        private void CacheVirtualCameras()
        {
            CinemachineVirtualCamera[] cameras = GetComponentsInChildren<CinemachineVirtualCamera>(true);

            if (bazookaPovCamera == null)
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    CinemachineVirtualCamera candidate = cameras[i];
                    if (candidate != null && string.Equals(candidate.name, bazookaPovCameraName, StringComparison.OrdinalIgnoreCase))
                    {
                        bazookaPovCamera = candidate;
                        break;
                    }
                }
            }

            if (virtualCamera == null)
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    CinemachineVirtualCamera candidate = cameras[i];
                    if (candidate != null && candidate != bazookaPovCamera)
                    {
                        virtualCamera = candidate;
                        break;
                    }
                }

                if (virtualCamera == null && cameras.Length > 0)
                {
                    virtualCamera = cameras[0];
                }
            }

            CacheBazookaCameraPipelineComponents();
        }

        private void CacheBazookaCameraPipelineComponents()
        {
            if (bazookaPovCamera == null)
                return;

            if (bazookaPovCameraCollider == null)
            {
                bazookaPovCameraCollider = bazookaPovCamera.GetComponent<CinemachineCollider>();
            }

            if (bazookaPovCameraNoise == null)
            {
                bazookaPovCameraNoise = bazookaPovCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
            }

            if (bazookaPovCameraComposer == null)
            {
                bazookaPovCameraComposer = bazookaPovCamera.GetCinemachineComponent<CinemachineComposer>();
            }

            if (bazookaPovCameraTransposer == null)
            {
                bazookaPovCameraTransposer = bazookaPovCamera.GetCinemachineComponent<CinemachineTransposer>();
            }
        }

        private void DisableCamerasForRemotePlayer()
        {
            CacheVirtualCameras();

            if (virtualCamera != null)
            {
                virtualCamera.gameObject.SetActive(false);
            }

            if (bazookaPovCamera != null && bazookaPovCamera != virtualCamera)
            {
                bazookaPovCamera.gameObject.SetActive(false);
            }
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
            if (abilitySettings == null)
            {
                Debug.LogError(
                    "PlayerMovementController: AbilitySettings not assigned. Dash and speed boost are disabled.",
                    this
                );
                return;
            }

            dashAbility = new DashAbility(abilitySettings);
            dashAbility.Initialize(this);

            dashingState = new DashingState(dashAbility);

            speedBoostAbility = new SpeedBoostAbility(abilitySettings);
            speedBoostAbility.Initialize(this);

            speedBoostAbility.OnSpeedMultiplierChanged += OnSpeedBoostMultiplierChanged;
        }

        private void SubscribeToInput()
        {
            if (hasSubscribedToInput)
                return;

            if (inputHandler == null)
            {
                inputHandler = GetComponent<PlayerInputHandler>();
            }

            if (inputHandler == null)
                return;

            inputHandler.OnMoveInput += HandleMoveInput;
            inputHandler.OnDashInput += HandleDashInput;
            inputHandler.OnSpeedBoostInput += HandleSpeedBoostInput;
            inputHandler.OnBazookaHoldStarted += HandleBazookaHoldStarted;
            inputHandler.OnBazookaHoldEnded += HandleBazookaHoldEnded;
            hasSubscribedToInput = true;
        }

        private void UnsubscribeFromInput()
        {
            if (!hasSubscribedToInput || inputHandler == null)
                return;

            inputHandler.OnMoveInput -= HandleMoveInput;
            inputHandler.OnDashInput -= HandleDashInput;
            inputHandler.OnSpeedBoostInput -= HandleSpeedBoostInput;
            inputHandler.OnBazookaHoldStarted -= HandleBazookaHoldStarted;
            inputHandler.OnBazookaHoldEnded -= HandleBazookaHoldEnded;
            hasSubscribedToInput = false;
        }

        private void SubscribeToStateController()
        {
            if (hasSubscribedToStateController)
                return;

            if (stateController == null)
            {
                stateController = GetComponent<PlayerStateController>();
            }

            if (stateController == null)
                return;

            stateController.OnStunStarted += HandleStunStarted;
            stateController.OnStunEnded += HandleStunEnded;
            hasSubscribedToStateController = true;
        }

        private void UnsubscribeFromStateController()
        {
            if (!hasSubscribedToStateController || stateController == null)
                return;

            stateController.OnStunStarted -= HandleStunStarted;
            stateController.OnStunEnded -= HandleStunEnded;
            hasSubscribedToStateController = false;
        }

        private void OnEnable()
        {
            lastForceBazookaHoldForWeaponPositioning = forceBazookaHoldForWeaponPositioning;
            SubscribeToInput();
            SubscribeToStateController();
        }

        private void OnDisable()
        {
            isBazookaHoldInputRequested = false;
            SetBazookaHold(false, false);
            SetBazookaCameraActive(false);
            RestoreCameraBlendNow();

            UnsubscribeFromInput();
            UnsubscribeFromStateController();
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
            {
                ClearMovementInput();
                return;
            }

            if (ShouldLockBazookaMovement())
            {
                SetBazookaMoveAimFallbackInput(input);
                ClearBazookaMovementInput();
                return;
            }

            rawInput = input;

            if (!useCameraRelativeMovement)
            {
                movingState?.SetMoveInput(input);
            }
        }

        private void HandleStunStarted()
        {
            isBazookaHoldInputRequested = false;
            SetBazookaHold(false, true);
            dashAbility?.Cancel();
            speedBoostAbility?.Cancel();
            ClearMovementInput();

            if (animator != null)
                animator.SetBool(RunHash, false);
        }

        private void HandleStunEnded()
        {
            StopHorizontalMotion();
            ClearMovementInput();

            if (!(currentState is IdleState))
                ChangeState(idleState);

            RefreshBazookaHoldState(true);
        }

        private void ClearMovementInput()
        {
            inputHandler?.ResetMovementInput();
            rawInput = Vector2.zero;
            cameraRelativeInput = Vector3.zero;
            movingState?.ClearMoveInput();
        }

        private void StopHorizontalMotion()
        {
            if (rb == null)
                return;

            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            rb.angularVelocity = Vector3.zero;
        }

        private void HandleDashInput()
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            if (isBazookaHolding)
                return;

            if (dashAbility != null && dashAbility.TryActivate())
            {
                // Debug.Log("Dash activated!");
            }
        }

        private void HandleSpeedBoostInput()
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            if (isBazookaHolding)
                return;

            if (speedBoostAbility != null && speedBoostAbility.TryActivate())
            {
                // Debug.Log($"Speed Boost activated! Stack Level: {speedBoostAbility.StackLevel}");
            }
        }

        private void HandleBazookaHoldStarted()
        {
            if (!photonView.IsMine)
                return;
            if (stateController != null && stateController.IsStunned)
                return;

            isBazookaHoldInputRequested = true;
            RefreshBazookaHoldState(true);
        }

        private void ResetBazookaHoldStartupState()
        {
            forceBazookaHoldForWeaponPositioning = false;
            isBazookaHoldInputRequested = false;
            isBazookaHolding = false;
            SetProjectileWeaponActive(false);
        }

        private void HandleBazookaHoldEnded()
        {
            if (!photonView.IsMine)
                return;

            bool shouldFire = isBazookaHolding && !forceBazookaHoldForWeaponPositioning;
            if (shouldFire)
            {
                FireBazookaMissile();
            }

            isBazookaHoldInputRequested = false;
            RefreshBazookaHoldState(true);
        }

        private void UpdateForcedBazookaHold()
        {
            if (lastForceBazookaHoldForWeaponPositioning == forceBazookaHoldForWeaponPositioning)
                return;

            lastForceBazookaHoldForWeaponPositioning = forceBazookaHoldForWeaponPositioning;
            RefreshBazookaHoldState(true);
        }

        private void RefreshBazookaHoldState(bool syncNetwork)
        {
            bool shouldHold = isBazookaHoldInputRequested || forceBazookaHoldForWeaponPositioning;
            SetBazookaHold(shouldHold, syncNetwork);
        }

        private void SetBazookaHold(bool holding, bool syncNetwork)
        {
            if (holding)
            {
                if (!isBazookaHolding)
                {
                    BeginBazookaHold();
                }

                PlayBazookaHoldAnimation();
            }
            else
            {
                if (!isBazookaHolding)
                    return;

                isBazookaHolding = false;
                OnBazookaHoldChanged?.Invoke(false);
                hasBazookaLockPosition = false;
                SetBazookaCameraActive(false);
                RestoreAnimationAfterBazookaHold();
            }

            if (syncNetwork && photonView != null && photonView.IsMine && PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RPC_SetBazookaHold), RpcTarget.OthersBuffered, holding);
            }
        }

        private void BeginBazookaHold()
        {
            isBazookaHolding = true;
            OnBazookaHoldChanged?.Invoke(true);
            hasBazookaLockPosition = false;

            if (photonView == null || photonView.IsMine)
            {
                dashAbility?.Cancel();
                speedBoostAbility?.Cancel();
                ClearBazookaMovementInput();
                CaptureBazookaLockPosition();
                StopHorizontalMotion();
                SetBazookaCameraActive(true);

                if (currentState is MovingState || currentState is DashingState)
                {
                    ChangeState(idleState);
                }
            }
        }

        private void SetBazookaCameraActive(bool active)
        {
            if (photonView != null && !photonView.IsMine)
                return;

            CacheVirtualCameras();

            if (bazookaPovCamera == null)
                return;

            CutNextCameraBlend();
            ConfigureBazookaCameraTargets();

            if (virtualCamera != null && virtualCamera != bazookaPovCamera)
            {
                virtualCamera.Priority = active
                    ? Mathf.Min(normalCameraPriority, bazookaPovCameraPriority - 1)
                    : normalCameraPriority;
            }

            bazookaPovCamera.Priority = active ? bazookaPovCameraPriority : GetInactiveBazookaCameraPriority();
            bazookaPovCamera.PreviousStateIsValid = false;

            if (active)
            {
                CaptureBazookaCameraRestPose();
                ApplyBazookaCameraOverrides();
                ApplyBazookaCameraAim();
            }
            else
            {
                ResetBazookaAim(true);
                RestoreBazookaCameraOverrides();
            }
        }

        private int GetInactiveBazookaCameraPriority()
        {
            return Mathf.Min(normalCameraPriority - 1, 0);
        }

        private void ConfigureBazookaCameraTargets()
        {
            if (bazookaPovCamera == null)
                return;

            if (bazookaCameraFollowTarget != null)
            {
                bazookaPovCamera.Follow = bazookaCameraFollowTarget;
            }

            if (bazookaCameraLookAtTarget != null)
            {
                bazookaPovCamera.LookAt = bazookaCameraLookAtTarget;
            }
        }

        private void ApplyBazookaCameraOverrides()
        {
            CacheBazookaCameraPipelineComponents();

            if (!hasCachedBazookaCameraOverrides)
            {
                cachedBazookaCameraColliderEnabled = bazookaPovCameraCollider != null && bazookaPovCameraCollider.enabled;
                cachedBazookaCameraComposerEnabled = bazookaPovCameraComposer != null && bazookaPovCameraComposer.enabled;
                cachedBazookaCameraTransposerEnabled = bazookaPovCameraTransposer != null && bazookaPovCameraTransposer.enabled;

                if (bazookaPovCameraNoise != null)
                {
                    cachedBazookaCameraNoiseAmplitude = bazookaPovCameraNoise.m_AmplitudeGain;
                    cachedBazookaCameraNoiseFrequency = bazookaPovCameraNoise.m_FrequencyGain;
                }

                hasCachedBazookaCameraOverrides = true;
            }

            if (disableBazookaCameraColliderWhileActive && bazookaPovCameraCollider != null)
            {
                bazookaPovCameraCollider.enabled = false;
            }

            if (muteBazookaCameraNoiseWhileActive && bazookaPovCameraNoise != null)
            {
                bazookaPovCameraNoise.m_AmplitudeGain = 0f;
                bazookaPovCameraNoise.m_FrequencyGain = 0f;
            }

            if (disableBazookaCameraComposerWhileActive && bazookaPovCameraComposer != null)
            {
                bazookaPovCameraComposer.enabled = false;
            }

            if (disableBazookaCameraTransposerWhileActive && bazookaPovCameraTransposer != null)
            {
                bazookaPovCameraTransposer.enabled = false;
            }

            if (bazookaPovCamera != null)
            {
                bazookaPovCamera.PreviousStateIsValid = false;
            }
        }

        private void RestoreBazookaCameraOverrides()
        {
            if (!hasCachedBazookaCameraOverrides)
                return;

            if (bazookaPovCameraCollider != null)
            {
                bazookaPovCameraCollider.enabled = cachedBazookaCameraColliderEnabled;
            }

            if (bazookaPovCameraNoise != null)
            {
                bazookaPovCameraNoise.m_AmplitudeGain = cachedBazookaCameraNoiseAmplitude;
                bazookaPovCameraNoise.m_FrequencyGain = cachedBazookaCameraNoiseFrequency;
            }

            if (bazookaPovCameraComposer != null)
            {
                bazookaPovCameraComposer.enabled = cachedBazookaCameraComposerEnabled;
            }

            if (bazookaPovCameraTransposer != null)
            {
                bazookaPovCameraTransposer.enabled = cachedBazookaCameraTransposerEnabled;
            }

            if (bazookaPovCamera != null)
            {
                bazookaPovCamera.PreviousStateIsValid = false;
            }

            hasCachedBazookaCameraOverrides = false;
        }

        private void SetBazookaMoveAimFallbackInput(Vector2 input)
        {
            bazookaAimInput = useMovementInputToAimBazooka
                ? Vector2.ClampMagnitude(input, 1f)
                : Vector2.zero;
        }

        private void UpdateBazookaAim(float deltaTime)
        {
            Vector2 dragAimInput = useBazookaAimDragArea && inputHandler != null
                ? inputHandler.BazookaAimInput
                : Vector2.zero;

            if (dragAimInput.sqrMagnitude > Mathf.Epsilon)
            {
                bazookaAimInput = dragAimInput;
                bazookaAimAngles.x += dragAimInput.x * bazookaAimYawDragSensitivity;
                bazookaAimAngles.y += dragAimInput.y * bazookaAimPitchDragSensitivity;
            }
            else if (useMovementInputToAimBazooka && inputHandler != null)
            {
                SetBazookaMoveAimFallbackInput(inputHandler.MoveInput);
                bazookaAimAngles.x += bazookaAimInput.x * bazookaAimYawSpeed * deltaTime;
                bazookaAimAngles.y += bazookaAimInput.y * bazookaAimPitchSpeed * deltaTime;
            }
            else
            {
                bazookaAimInput = Vector2.zero;
            }

            ApplyBazookaBodyTurnAssist(deltaTime);

            bazookaAimAngles.x = Mathf.Clamp(bazookaAimAngles.x, bazookaAimYawLimits.x, bazookaAimYawLimits.y);
            bazookaAimAngles.y = Mathf.Clamp(bazookaAimAngles.y, bazookaAimPitchLimits.x, bazookaAimPitchLimits.y);

            ApplyBazookaCameraAim();
        }

        private void ApplyBazookaBodyTurnAssist(float deltaTime)
        {
            if (!rotateBodyWhenBazookaYawAtLimit || deltaTime <= 0f)
                return;

            float yaw = bazookaAimAngles.x;
            if (Mathf.Abs(yaw) <= Mathf.Epsilon)
                return;

            float direction = Mathf.Sign(yaw);
            float limit = GetYawLimitForDirection(direction);
            if (limit <= Mathf.Epsilon)
                return;

            float startNormalized = Mathf.Clamp01(bazookaBodyTurnStartNormalized);
            float start = limit * startNormalized;
            float absYaw = Mathf.Abs(yaw);

            if (absYaw <= start)
                return;

            float turnIntentX = bazookaAimInput.x;
            bool hasTurnIntent = Mathf.Abs(turnIntentX) > 0.01f && Mathf.Sign(turnIntentX) == direction;
            if (!hasTurnIntent)
                return;

            float overLimitFactor = Mathf.InverseLerp(start, limit, absYaw);
            float turnSpeed = Mathf.Max(0f, bazookaBodyTurnSpeed) * overLimitFactor;
            if (turnSpeed <= Mathf.Epsilon)
                return;

            float turnAmount = turnSpeed * deltaTime;
            RotateBazookaBodyYaw(direction * turnAmount);

            float targetYaw = direction * start;
            float recenterStep = Mathf.Max(0f, bazookaYawRecenteringSpeed) * deltaTime;
            bazookaAimAngles.x = Mathf.MoveTowards(bazookaAimAngles.x, targetYaw, recenterStep);
        }

        private float GetYawLimitForDirection(float direction)
        {
            if (direction >= 0f)
            {
                return Mathf.Max(0f, bazookaAimYawLimits.y);
            }

            return Mathf.Max(0f, Mathf.Abs(bazookaAimYawLimits.x));
        }

        private void RotateBazookaBodyYaw(float deltaYawDegrees)
        {
            if (Mathf.Abs(deltaYawDegrees) <= Mathf.Epsilon)
                return;

            Quaternion currentRotation = transform.rotation;
            Quaternion nextRotation = Quaternion.Euler(
                0f,
                currentRotation.eulerAngles.y + deltaYawDegrees,
                0f
            );

            if (rb != null && !rb.isKinematic)
            {
                rb.MoveRotation(nextRotation);
            }
            else
            {
                transform.rotation = nextRotation;
            }
        }

        private void CaptureBazookaCameraRestPose()
        {
            if (bazookaPovCamera == null || hasCachedBazookaCameraRestPose)
                return;

            Transform aimCenter = ResolveBazookaCameraAimCenter();
            Vector3 aimCenterPosition = GetBazookaCameraAimCenterPosition(aimCenter);

            bazookaCameraRestLocalPosition = bazookaPovCamera.transform.localPosition;
            bazookaCameraRestLocalRotation = bazookaPovCamera.transform.localRotation;
            bazookaCameraRestOffsetFromAimCenter = bazookaPovCamera.transform.position - aimCenterPosition;
            bazookaCameraRestRightAxis = bazookaPovCamera.transform.right;
            bazookaCameraRestAimForward = bazookaPovCamera.transform.forward;
            bazookaCurrentAimForward = bazookaCameraRestAimForward;

            if (bazookaCameraRestOffsetFromAimCenter.sqrMagnitude <= Mathf.Epsilon)
            {
                bazookaCameraRestOffsetFromAimCenter = -bazookaCameraRestAimForward;
            }

            hasCachedBazookaCameraRestPose = true;
        }

        private void ApplyBazookaCameraAim()
        {
            if (bazookaPovCamera == null)
                return;

            CaptureBazookaCameraRestPose();

            if (keepPlayerCenteredDuringBazookaAim)
            {
                ApplyCenteredBazookaCameraAim();
                bazookaPovCamera.PreviousStateIsValid = false;
                return;
            }

            Quaternion yawRotation = Quaternion.AngleAxis(bazookaAimAngles.x, Vector3.up);
            Quaternion pitchRotation = Quaternion.AngleAxis(-bazookaAimAngles.y, Vector3.right);
            bazookaPovCamera.transform.localRotation = bazookaCameraRestLocalRotation * yawRotation * pitchRotation;
            bazookaCurrentAimForward = bazookaPovCamera.transform.forward;
            bazookaPovCamera.PreviousStateIsValid = false;
        }

        private void ApplyCenteredBazookaCameraAim()
        {
            Transform aimCenter = ResolveBazookaCameraAimCenter();
            Vector3 aimCenterPosition = GetBazookaCameraAimCenterPosition(aimCenter);

            Vector3 restOffset = bazookaCameraRestOffsetFromAimCenter;
            if (restOffset.sqrMagnitude <= Mathf.Epsilon)
            {
                restOffset = -GetBazookaRestAimForward();
            }

            Quaternion yawRotation = Quaternion.AngleAxis(bazookaAimAngles.x, Vector3.up);
            Vector3 pitchAxis = yawRotation * GetBazookaRestRightAxis();
            if (pitchAxis.sqrMagnitude <= Mathf.Epsilon)
            {
                pitchAxis = Vector3.right;
            }

            pitchAxis.Normalize();

            Quaternion pitchRotation = Quaternion.AngleAxis(-bazookaAimAngles.y, pitchAxis);
            Vector3 aimedOffset = pitchRotation * (yawRotation * restOffset);
            Vector3 cameraPosition = aimCenterPosition + aimedOffset;
            Vector3 lookDirection = aimCenterPosition - cameraPosition;

            if (lookDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                lookDirection = pitchRotation * (yawRotation * GetBazookaRestAimForward());
            }

            bazookaPovCamera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            );

            bazookaCurrentAimForward = (pitchRotation * (yawRotation * GetBazookaRestAimForward())).normalized;
        }

        private Transform ResolveBazookaCameraAimCenter()
        {
            if (bazookaCameraAimCenter != null)
                return bazookaCameraAimCenter;

            if (bazookaCameraLookAtTarget != null)
                return bazookaCameraLookAtTarget;

            if (cameraLookAtTarget != null)
                return cameraLookAtTarget;

            if (bazookaCameraFollowTarget != null)
                return bazookaCameraFollowTarget;

            if (cameraFollowTarget != null)
                return cameraFollowTarget;

            return transform;
        }

        private Vector3 GetBazookaCameraAimCenterPosition(Transform aimCenter)
        {
            if (aimCenter == null)
                return transform.position + Vector3.up * 1.4f;

            Vector3 centerOffset = bazookaCameraAimCenterOffset;
            if (aimCenter == transform && centerOffset.sqrMagnitude <= Mathf.Epsilon)
            {
                centerOffset = Vector3.up * 1.4f;
            }

            return aimCenter.TransformPoint(centerOffset);
        }

        private Vector3 GetBazookaRestRightAxis()
        {
            if (bazookaCameraRestRightAxis.sqrMagnitude > Mathf.Epsilon)
                return bazookaCameraRestRightAxis.normalized;

            return transform.right;
        }

        private Vector3 GetBazookaRestAimForward()
        {
            if (bazookaCameraRestAimForward.sqrMagnitude > Mathf.Epsilon)
                return bazookaCameraRestAimForward.normalized;

            return transform.forward;
        }

        private void ResetBazookaAim(bool resetCameraRotation)
        {
            bazookaAimInput = Vector2.zero;
            bazookaAimAngles = Vector2.zero;
            bazookaCurrentAimForward = bazookaCameraRestAimForward;

            if (resetCameraRotation && bazookaPovCamera != null && hasCachedBazookaCameraRestPose)
            {
                bazookaPovCamera.transform.localPosition = bazookaCameraRestLocalPosition;
                bazookaPovCamera.transform.localRotation = bazookaCameraRestLocalRotation;
                bazookaPovCamera.PreviousStateIsValid = false;
            }
        }

        private void FireBazookaMissile()
        {
            if (bazookaMissileLauncher == null)
            {
                bazookaMissileLauncher = GetComponentInChildren<BazookaMissileLauncher>(true);
            }

            if (bazookaMissileLauncher == null)
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning("PlayerMovementController: No BazookaMissileLauncher assigned for bazooka fire.", this);
                }

                return;
            }

            bazookaMissileLauncher.Fire(GetBazookaFireDirection());
        }

        private Vector3 GetBazookaFireDirection()
        {
            if (bazookaFireDirection != null)
                return bazookaFireDirection.forward;

            if (bazookaCurrentAimForward.sqrMagnitude > Mathf.Epsilon)
                return bazookaCurrentAimForward.normalized;

            if (bazookaPovCamera != null)
                return bazookaPovCamera.transform.forward;

            if (mainCamera != null)
                return mainCamera.transform.forward;

            return transform.forward;
        }

        private void CutNextCameraBlend()
        {
            if (!snapCameraBlendOnBazookaSwitch || !isActiveAndEnabled)
                return;

            if (mainCameraBrain == null && mainCamera != null)
            {
                mainCameraBrain = mainCamera.GetComponent<CinemachineBrain>();
            }

            if (mainCameraBrain == null)
                return;

            if (!hasCachedDefaultCameraBlend)
            {
                cachedDefaultCameraBlend = mainCameraBrain.m_DefaultBlend;
                hasCachedDefaultCameraBlend = true;
            }

            mainCameraBrain.m_DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Style.Cut,
                0f
            );

            if (restoreCameraBlendCoroutine != null)
            {
                StopCoroutine(restoreCameraBlendCoroutine);
            }

            restoreCameraBlendCoroutine = StartCoroutine(RestoreCameraBlendAfterFrame());
        }

        private IEnumerator RestoreCameraBlendAfterFrame()
        {
            yield return null;
            RestoreCameraBlendNow();
        }

        private void RestoreCameraBlendNow()
        {
            if (mainCameraBrain != null && hasCachedDefaultCameraBlend)
            {
                mainCameraBrain.m_DefaultBlend = cachedDefaultCameraBlend;
            }

            hasCachedDefaultCameraBlend = false;
            restoreCameraBlendCoroutine = null;
        }

        private bool ShouldLockBazookaMovement()
        {
            return lockMovementWhileBazookaActive && isBazookaHolding;
        }

        private void CaptureBazookaLockPosition()
        {
            if (!lockMovementWhileBazookaActive)
                return;

            bazookaLockPosition = rb != null ? rb.position : transform.position;
            hasBazookaLockPosition = true;
        }

        private void EnforceBazookaMovementLock()
        {
            if (!hasBazookaLockPosition)
            {
                CaptureBazookaLockPosition();
            }

            ClearBazookaMovementInput();

            if (rb == null)
            {
                transform.position = bazookaLockPosition;
                return;
            }

            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(bazookaLockPosition);
        }

        private void ClearBazookaMovementInput()
        {
            rawInput = Vector2.zero;
            cameraRelativeInput = Vector3.zero;
            movingState?.ClearMoveInput();

            if (animator != null)
            {
                animator.SetBool(RunHash, false);
            }
        }

        private void EnsureBazookaHoldAnimation()
        {
            if (animator == null || animator.IsInTransition(0) || IsInBazookaHoldState())
                return;

            PlayBazookaHoldAnimation();
        }

        private void PlayBazookaHoldAnimation()
        {
            if (animator == null)
                return;

            SetProjectileWeaponActive(true);

            string statePath = GetPlayableStatePath(bazookaHoldStateName);
            if (string.IsNullOrEmpty(statePath))
            {
                WarnMissingBazookaHoldState();
                return;
            }

            animator.SetBool(RunHash, false);
            animator.CrossFadeInFixedTime(statePath, Mathf.Max(0f, bazookaHoldFadeDuration), 0, 0f);
        }

        private void RestoreAnimationAfterBazookaHold()
        {
            SetProjectileWeaponActive(false);

            if (animator == null)
                return;

            if (stateController != null && stateController.IsStunned)
                return;

            if (dashAbility != null && dashAbility.IsActive)
            {
                CrossFadeToState("Dash");
                return;
            }

            if (speedBoostAbility != null && speedBoostAbility.IsActive)
            {
                CrossFadeToState("SpeedBoots");
                return;
            }

            if (stateController != null && stateController.IsFalling)
            {
                CrossFadeToState("Fall");
                return;
            }

            bool shouldRun = rawInput.magnitude > 0.1f;
            animator.SetBool(RunHash, shouldRun);
            CrossFadeToState(shouldRun ? "Run" : "Idle");
        }

        private void SetProjectileWeaponActive(bool active)
        {
            if (projectileWeapon != null)
            {
                projectileWeapon.SetActive(active);
                return;
            }

            if (active && !hasWarnedMissingProjectileWeapon)
            {
                Debug.LogWarning(
                    "PlayerMovementController: projectileWeapon is not assigned. Bazooka weapon visual will be skipped.",
                    this
                );
                hasWarnedMissingProjectileWeapon = true;
            }
        }

        private void CrossFadeToState(string stateName)
        {
            string statePath = GetPlayableStatePath(stateName);
            if (!string.IsNullOrEmpty(statePath))
            {
                animator.CrossFadeInFixedTime(statePath, Mathf.Max(0f, bazookaHoldFadeDuration), 0, 0f);
            }
        }

        private bool IsInBazookaHoldState()
        {
            if (animator == null || string.IsNullOrEmpty(bazookaHoldStateName))
                return false;

            int shortNameHash = Animator.StringToHash(bazookaHoldStateName);
            int fullPathHash = Animator.StringToHash($"{BaseLayerName}.{bazookaHoldStateName}");
            AnimatorStateInfo currentStateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (currentStateInfo.shortNameHash == shortNameHash || currentStateInfo.fullPathHash == fullPathHash)
                return true;

            if (!animator.IsInTransition(0))
                return false;

            AnimatorStateInfo nextStateInfo = animator.GetNextAnimatorStateInfo(0);
            return nextStateInfo.shortNameHash == shortNameHash || nextStateInfo.fullPathHash == fullPathHash;
        }

        private string GetPlayableStatePath(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
                return null;

            int shortNameHash = Animator.StringToHash(stateName);
            if (animator.HasState(0, shortNameHash))
                return stateName;

            string fullPath = $"{BaseLayerName}.{stateName}";
            int fullPathHash = Animator.StringToHash(fullPath);
            return animator.HasState(0, fullPathHash) ? fullPath : null;
        }

        private void WarnMissingBazookaHoldState()
        {
            if (hasWarnedMissingBazookaHoldState)
                return;

            hasWarnedMissingBazookaHoldState = true;
            string controllerName = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "the assigned Animator Controller";

            Debug.LogWarning($"PlayerMovementController: State '{bazookaHoldStateName}' was not found in {controllerName}.");
        }

        [PunRPC]
        private void RPC_SetBazookaHold(bool holding)
        {
            SetBazookaHold(holding, false);
        }

        private static float NormalizeSignedAxis(float value, Vector2 limits)
        {
            if (value > 0f)
            {
                return Mathf.Approximately(limits.y, 0f) ? 0f : Mathf.Clamp01(value / limits.y);
            }

            if (value < 0f)
            {
                return Mathf.Approximately(limits.x, 0f) ? 0f : -Mathf.Clamp01(value / Mathf.Abs(limits.x));
            }

            return 0f;
        }

        private void OnSpeedBoostMultiplierChanged(float multiplier)
        {
            if (movingState != null)
            {
                movingState.SetSpeedMultiplier(multiplier);
                // Debug.Log($"Movement speed multiplier updated to: {multiplier}x");
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif

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
