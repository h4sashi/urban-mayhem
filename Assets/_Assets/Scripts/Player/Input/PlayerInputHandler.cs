using UnityEngine;
using UnityEngine.InputSystem;
using System;
using Hanzo.UI;
using Photon.Pun;

namespace Hanzo.Player.Input
{
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField] private float inputDeadzone = 0.1f;
        
        [Header("Mobile Controls")]
        [SerializeField] private bool useMobileControls = true;
        [SerializeField] private FloatingJoystick mobileJoystick;
        [SerializeField] private GameObject mobileControlsUI;
        
        public PlayerInputActions inputActions;
        
        // Events for input
        public event Action<Vector2> OnMoveInput;
        public event Action OnDashInput;
        public event Action OnSpeedBoostInput;
        
        public Vector2 MoveInput { get; private set; }
        
        private bool isMobilePlatform;
        private bool shouldUseMobileControls;
        private PhotonView photonView;
        
        // AI Detection
        private bool isAIControlled = false;
        
        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            
            // Check if this is AI-controlled
            isAIControlled = GetComponent<Hanzo.AI.Enemies.AIPlayerController>() != null;
            
            // Detect platform
            isMobilePlatform = true; // Your current setup for testing
            
            inputActions = new PlayerInputActions();
            
            // Bind keyboard/gamepad input events
            inputActions.Player.Move.performed += OnMovePerformed;
            inputActions.Player.Move.canceled += OnMoveCanceled;
            inputActions.Player.Dash.performed += OnDashPerformed;
            inputActions.Player.SpeedBoost.performed += OnSpeedBoostPerformed;
            
            // Setup mobile controls ONLY for local player (not AI)
            if (!isAIControlled && photonView.IsMine)
            {
                SetupMobileControls();
            }
            else
            {
                DisableMobileControlsForRemotePlayer();
            }
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
                
                Debug.Log("[LOCAL PLAYER] Mobile controls enabled");
            }
            else
            {
                Debug.Log("[LOCAL PLAYER] Keyboard/Gamepad controls enabled");
            }
        }
        
        private void DisableMobileControlsForRemotePlayer()
        {
            if (mobileControlsUI != null)
            {
                mobileControlsUI.SetActive(false);
                Debug.Log("[REMOTE PLAYER/AI] Mobile controls disabled");
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
            // Enable input for local player (not AI-controlled)
            if (!isAIControlled && photonView != null && photonView.IsMine)
            {
                inputActions?.Enable();
            }
        }
        
        private void OnDisable()
        {
            inputActions?.Disable();
        }
        
        private void OnDestroy()
        {
            if (mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove -= OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased -= OnMobileJoystickReleased;
            }
            
            inputActions?.Dispose();
        }
        
        private void Update()
        {
            // Only process input for local player (not AI)
            if (isAIControlled || photonView == null || !photonView.IsMine) 
                return;
            
            // On mobile, continuously read joystick input
            if (shouldUseMobileControls && mobileJoystick != null && mobileJoystick.IsActive)
            {
                Vector2 input = mobileJoystick.GetInput();
                MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
                OnMoveInput?.Invoke(MoveInput);
            }
        }
        
        // ============================================
        // AI INPUT METHODS (PUBLIC)
        // ============================================
        
        /// <summary>
        /// Allows AI to programmatically set move input
        /// Bypasses player input checks for AI-controlled entities
        /// </summary>
        public void SetAIInput(Vector2 input)
        {
            if (!isAIControlled)
            {
                Debug.LogWarning("[PlayerInputHandler] SetAIInput called on non-AI entity!");
                return;
            }
            
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        /// <summary>
        /// Allows AI to trigger dash ability
        /// </summary>
        public void TriggerAIDash()
        {
            if (!isAIControlled)
            {
                Debug.LogWarning("[PlayerInputHandler] TriggerAIDash called on non-AI entity!");
                return;
            }
            
            OnDashInput?.Invoke();
        }
        
        /// <summary>
        /// Allows AI to trigger speed boost ability
        /// </summary>
        public void TriggerAISpeedBoost()
        {
            if (!isAIControlled)
            {
                Debug.LogWarning("[PlayerInputHandler] TriggerAISpeedBoost called on non-AI entity!");
                return;
            }
            
            OnSpeedBoostInput?.Invoke();
        }
        
        // ============================================
        // PLAYER INPUT METHODS (PRIVATE)
        // ============================================
        
        // Keyboard/Gamepad Input
        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            if (shouldUseMobileControls) return;
            
            Vector2 input = context.ReadValue<Vector2>();
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            if (shouldUseMobileControls) return;
            
            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        // Mobile Joystick Input
        private void OnMobileJoystickMove(Vector2 input)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        private void OnMobileJoystickReleased()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        // Ability inputs
        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            Debug.Log("Dash input received (Keyboard/Gamepad)");
            OnDashInput?.Invoke();
        }
        
        private void OnSpeedBoostPerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            Debug.Log("Speed Boost input received (Keyboard/Gamepad)");
            OnSpeedBoostInput?.Invoke();
        }
        
        // Public methods to trigger abilities from mobile UI buttons
        public void TriggerDash()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            Debug.Log("Dash Triggered (Mobile Button)");
            OnDashInput?.Invoke();
        }
        
        public void TriggerSpeedBoost()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            Debug.Log("Speed Boost Triggered (Mobile Button)");
            OnSpeedBoostInput?.Invoke();
        }
        
        public bool IsUsingMobileControls()
        {
            return shouldUseMobileControls;
        }
        
        public void SwitchToMobileControls(bool enable)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            useMobileControls = enable;
            SetupMobileControls();
        }
        
        /// <summary>
        /// Check if this entity is AI-controlled
        /// </summary>
        public bool IsAIControlled()
        {
            return isAIControlled;
        }
    }
}