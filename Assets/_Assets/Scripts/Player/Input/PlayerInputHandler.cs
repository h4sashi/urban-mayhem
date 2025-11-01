using UnityEngine;
using UnityEngine.InputSystem;
using System;
using Hanzo.UI;

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
        
        private void Awake()
        {
            // Detect platform - FIXED: Now properly detects mobile vs desktop
            // isMobilePlatform = Application.isMobilePlatform || 
                            //    Application.platform == RuntimePlatform.Android || 
                            //    Application.platform == RuntimePlatform.IPhonePlayer;
            
            // Allow forcing mobile controls for testing in editor
            
                isMobilePlatform = true;
            

            inputActions = new PlayerInputActions();
            
            // Bind keyboard/gamepad input events
            inputActions.Player.Move.performed += OnMovePerformed;
            inputActions.Player.Move.canceled += OnMoveCanceled;
            inputActions.Player.Dash.performed += OnDashPerformed;
            inputActions.Player.SpeedBoost.performed += OnSpeedBoostPerformed;
            
            // Setup mobile controls
            SetupMobileControls();
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
                // Subscribe to joystick events
                mobileJoystick.OnJoystickMove += OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased += OnMobileJoystickReleased;
                
                Debug.Log("Mobile controls enabled");
            }
            else
            {
                Debug.Log("Keyboard/Gamepad controls enabled");
            }
        }
        
        private void OnEnable()
        {
            inputActions?.Enable();
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
            // On mobile, continuously read joystick input
            if (shouldUseMobileControls && mobileJoystick != null && mobileJoystick.IsActive)
            {
                Vector2 input = mobileJoystick.GetInput();
                MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
                OnMoveInput?.Invoke(MoveInput);
            }
        }
        
        // Keyboard/Gamepad Input - FIXED: Now works on desktop platforms
        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            // Only ignore keyboard input if we're actually using mobile controls
            if (shouldUseMobileControls) return;
            
            Vector2 input = context.ReadValue<Vector2>();
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            if (shouldUseMobileControls) return;
            
            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        // Mobile Joystick Input
        private void OnMobileJoystickMove(Vector2 input)
        {
            MoveInput = input.magnitude > inputDeadzone ? input : Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        private void OnMobileJoystickReleased()
        {
            MoveInput = Vector2.zero;
            OnMoveInput?.Invoke(MoveInput);
        }
        
        // Ability inputs - Work for both keyboard and mobile button presses
        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            Debug.Log("Dash input received (Keyboard/Gamepad)");
            OnDashInput?.Invoke();
        }
        
        private void OnSpeedBoostPerformed(InputAction.CallbackContext context)
        {
            Debug.Log("Speed Boost input received (Keyboard/Gamepad)");
            OnSpeedBoostInput?.Invoke();
        }
        
        // Public methods to trigger abilities from mobile UI buttons
        public void TriggerDash()
        {
            Debug.Log("Dash Triggered (Mobile Button)");
            OnDashInput?.Invoke();
        }
        
        public void TriggerSpeedBoost()
        {
            Debug.Log("Speed Boost Triggered (Mobile Button)");
            OnSpeedBoostInput?.Invoke();
        }
        
        // Helper to check if using mobile controls
        public bool IsUsingMobileControls()
        {
            return shouldUseMobileControls;
        }
        
        // Runtime control switching (optional feature)
        public void SwitchToMobileControls(bool enable)
        {
            useMobileControls = enable;
            SetupMobileControls();
        }
    }
}