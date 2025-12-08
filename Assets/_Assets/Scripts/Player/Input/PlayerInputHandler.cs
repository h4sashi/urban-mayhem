using System;
using Hanzo.UI;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hanzo.Player.Input
{
    /// <summary>
    /// FIXED: AI players now completely ignore Unity's Input System
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField]
        private float inputDeadzone = 0.1f;

        [Header("Mobile Controls")]
        [SerializeField]
        private bool useMobileControls = true;

        [SerializeField]
        private FloatingJoystick mobileJoystick;

        [SerializeField]
        private GameObject mobileControlsUI;

        [Header("AI Settings")]
        [SerializeField]
        private bool isAIControlled = false;

        [Header("Debug")]
        [SerializeField]
        private bool debugLogging = true;

        public PlayerInputActions inputActions;

        // Events for input
        public event Action<Vector2> OnMoveInput;
        public event Action OnDashInput;
        public event Action OnSpeedBoostInput;

        public Vector2 MoveInput { get; private set; }

        private bool isMobilePlatform;
        private bool shouldUseMobileControls;
        private PhotonView photonView;
        private int inputCallCount = 0;

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();

            // CRITICAL: Check if AI-controlled FIRST
            var aiController = GetComponent<Hanzo.AI.AIPlayerController>();
            if (aiController != null && aiController.enabled)
            {
                isAIControlled = true;
                Debug.Log($"[InputHandler] 🤖 AI DETECTED - Disabling Unity Input System for this player");
            }

            isMobilePlatform = true;

            // CRITICAL: Only create input actions for NON-AI players
            if (!isAIControlled)
            {
                Debug.Log("[InputHandler] 👤 Human player - Setting up input actions");
                inputActions = new PlayerInputActions();

                inputActions.Player.Move.performed += OnMovePerformed;
                inputActions.Player.Move.canceled += OnMoveCanceled;
                inputActions.Player.Dash.performed += OnDashPerformed;
                inputActions.Player.SpeedBoost.performed += OnSpeedBoostPerformed;
            }
            else
            {
                Debug.Log("[InputHandler] 🤖 AI player - Input actions NOT created (this prevents reading human input)");
                // DO NOT create inputActions at all for AI
                inputActions = null;
            }

            // Setup controls
            if (isAIControlled)
            {
                SetupAIControls();
            }
            else if (photonView != null && photonView.IsMine)
            {
                SetupMobileControls();
            }
            else
            {
                DisableMobileControlsForRemotePlayer();
            }
        }

        private void SetupAIControls()
        {
            if (mobileControlsUI != null)
            {
                mobileControlsUI.SetActive(false);
            }

            shouldUseMobileControls = false;
            Debug.Log("[InputHandler] 🤖 AI controls configured - UI disabled, no input polling");
        }

        private void SetupMobileControls()
        {
            shouldUseMobileControls = useMobileControls && isMobilePlatform;

            if (mobileControlsUI != null)
            {
                mobileControlsUI.SetActive(shouldUseMobileControls);
            }

            if (shouldUseMobileControls && mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove += OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased += OnMobileJoystickReleased;

                Debug.Log("[InputHandler] 📱 Mobile controls enabled");
            }
            else
            {
                Debug.Log("[InputHandler] ⌨️ Keyboard/Gamepad controls enabled");
            }
        }

        private void DisableMobileControlsForRemotePlayer()
        {
            if (mobileControlsUI != null)
            {
                mobileControlsUI.SetActive(false);
                Debug.Log("[InputHandler] 🌐 Remote player - controls disabled");
            }

            if (mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove -= OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased -= OnMobileJoystickReleased;
            }

            shouldUseMobileControls = false;
        }

        private void OnEnable()
        {
            // CRITICAL: Only enable input actions for non-AI human players
            if (!isAIControlled && photonView != null && photonView.IsMine && inputActions != null)
            {
                inputActions.Enable();
                Debug.Log("[InputHandler] ✓ Input actions enabled (Human player)");
            }
        }

        private void OnDisable()
        {
            // Only disable if we actually have input actions
            if (!isAIControlled && inputActions != null)
            {
                inputActions.Disable();
            }
        }

        private void OnDestroy()
        {
            if (mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove -= OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased -= OnMobileJoystickReleased;
            }

            if (!isAIControlled && inputActions != null)
            {
                inputActions.Dispose();
            }
        }

        private void Update()
        {
            // CRITICAL: AI players skip Update entirely - they only use SendMoveInput()
            if (isAIControlled)
            {
                // AI doesn't poll for input - it's fed directly via SendMoveInput()
                return;
            }

            // Only process input for local player
            if (photonView == null || !photonView.IsMine)
                return;

            // On mobile, continuously read joystick input
            if (shouldUseMobileControls && mobileJoystick != null && mobileJoystick.IsActive)
            {
                Vector2 input = mobileJoystick.GetInput();
                MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
                OnMoveInput?.Invoke(MoveInput);
            }
        }

        #region Human Input Handlers

        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            // These should NEVER be called for AI players since inputActions is null
            if (isAIControlled)
            {
                Debug.LogError("[InputHandler] ❌ OnMovePerformed called for AI - this should not happen!");
                return;
            }
            
            if (photonView == null || !photonView.IsMine)
                return;
            if (shouldUseMobileControls)
                return;

            Vector2 input = context.ReadValue<Vector2>();
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
            
            if (debugLogging)
            {
                Debug.Log($"[InputHandler] 👤 Human input: {MoveInput}");
            }
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;
            if (shouldUseMobileControls)
                return;

            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }

        private void OnMobileJoystickMove(Vector2 input)
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;

            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }

        private void OnMobileJoystickReleased()
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;

            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }

        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;

            Debug.Log("[InputHandler] Dash input received (Keyboard/Gamepad)");
            OnDashInput?.Invoke();
        }

        private void OnSpeedBoostPerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;

            Debug.Log("[InputHandler] Speed Boost input received (Keyboard/Gamepad)");
            OnSpeedBoostInput?.Invoke();
        }

        #endregion

        #region Public Input Methods (for Mobile UI and AI)

        /// <summary>
        /// Send move input - ONLY used by AI and mobile UI
        /// This is the ONLY way AI feeds input into the system
        /// </summary>
        public void SendMoveInput(Vector2 input)
        {
            // For AI: ALWAYS accept input (no PhotonView check)
            // For humans: Check if it's the local player
            if (!isAIControlled && photonView != null && !photonView.IsMine)
            {
                return;
            }

            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            
            // DEBUG: Show AI input being processed
            if (isAIControlled && debugLogging)
            {
                inputCallCount++;
                if (inputCallCount % 60 == 0 && input.magnitude > 0.01f) // Log every 60 calls with movement
                {
                    Debug.Log($"[InputHandler] 🤖 AI SendMoveInput: Raw={input}, Processed={MoveInput}");
                    Debug.Log($"[InputHandler] 🤖 Invoking event to {OnMoveInput?.GetInvocationList().Length ?? 0} subscribers");
                }
            }
            
            // CRITICAL: Invoke the event to notify movement controller
            OnMoveInput?.Invoke(MoveInput);
        }

        /// <summary>
        /// Trigger dash (used by AI or mobile UI)
        /// </summary>
        public void TriggerDash()
        {
            if (!isAIControlled && photonView != null && !photonView.IsMine)
                return;

            if (debugLogging)
            {
                Debug.Log($"[InputHandler] 💨 Dash Triggered ({(isAIControlled ? "AI" : "Mobile Button")})");
            }
            OnDashInput?.Invoke();
        }

        /// <summary>
        /// Trigger speed boost (used by AI or mobile UI)
        /// </summary>
        public void TriggerSpeedBoost()
        {
            if (!isAIControlled && photonView != null && !photonView.IsMine)
                return;

            if (debugLogging)
            {
                Debug.Log($"[InputHandler] ⚡ Speed Boost Triggered ({(isAIControlled ? "AI" : "Mobile Button")})");
            }
            OnSpeedBoostInput?.Invoke();
        }

        #endregion

        #region Public Utility Methods

        public bool IsUsingMobileControls()
        {
            return shouldUseMobileControls;
        }

        public void SwitchToMobileControls(bool enable)
        {
            if (isAIControlled)
                return;
            if (photonView == null || !photonView.IsMine)
                return;

            useMobileControls = enable;
            SetupMobileControls();
        }

        /// <summary>
        /// Set whether this player is AI-controlled
        /// </summary>
        public void SetAIControlled(bool aiControlled)
        {
            isAIControlled = aiControlled;
            if (debugLogging)
            {
                Debug.Log($"[InputHandler] ✓ SetAIControlled: {aiControlled}");
            }
        }

        public bool IsAIControlled()
        {
            return isAIControlled;
        }

        #endregion

        private void OnGUI()
        {
            if (!debugLogging)
                return;

            // Show separate debug windows for AI vs Human
            if (isAIControlled)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 320, 370, 310, 180));
                GUILayout.Label("=== AI INPUT HANDLER ===");
                GUILayout.Label($"AI Controlled: {isAIControlled}");
                GUILayout.Label($"Input Actions: {(inputActions == null ? "NULL (correct for AI)" : "EXISTS (WRONG!)")}");
                GUILayout.Label($"Current Input: {MoveInput}");
                GUILayout.Label($"Input Magnitude: {MoveInput.magnitude:F3}");
                GUILayout.Label($"Deadzone: {inputDeadzone}");
                GUILayout.Label($"Event Subscribers: {OnMoveInput?.GetInvocationList().Length ?? 0}");
                GUILayout.Label($"Total Calls: {inputCallCount}");
                GUILayout.EndArea();
            }
            else if (photonView != null && photonView.IsMine)
            {
                GUILayout.BeginArea(new Rect(10, 920, 300, 120));
                GUILayout.Label("=== HUMAN INPUT HANDLER ===");
                GUILayout.Label($"Current Input: {MoveInput}");
                GUILayout.Label($"Input Actions Enabled: {inputActions?.Player.Move.enabled ?? false}");
                GUILayout.Label($"Using Mobile: {shouldUseMobileControls}");
                GUILayout.EndArea();
            }
        }
    }
}