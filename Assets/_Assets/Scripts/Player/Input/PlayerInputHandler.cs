using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
using Hanzo.UI;
using Photon.Pun;

namespace Hanzo.Player.Input
{
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField] private float inputDeadzone = 0.1f;

        [Header("Keyboard Controls")]
        [SerializeField] private Key bazookaHoldKey = Key.G;
        
        [Header("Mobile Controls")]
        [SerializeField] private bool useMobileControls = true;
        [SerializeField] private FloatingJoystick mobileJoystick;
        [SerializeField] private MobileActionButton bulletButton;
        [SerializeField] private GameObject mobileControlsUI;
        [SerializeField] private string bulletButtonName = "BulletButton";
        [SerializeField] private MobileAimDragArea bazookaAimDragArea;
        [SerializeField] private string bazookaAimDragAreaName = "BazookaAimDragArea";
        [SerializeField] private bool autoCreateBazookaAimDragArea = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        public PlayerInputActions inputActions;
        
        // Events for input
        public event Action<Vector2> OnMoveInput;
        public event Action OnDashInput;
        public event Action OnSpeedBoostInput;
        public event Action OnBazookaHoldStarted;
        public event Action OnBazookaHoldEnded;
        
        public Vector2 MoveInput { get; private set; }
        public Vector2 BazookaAimInput { get; private set; }
        
        private bool isMobilePlatform;
        private bool shouldUseMobileControls;
        private PhotonView photonView;
        private bool isBazookaHoldButtonActive;
        private bool isBazookaHoldKeyboardActive;
        private bool isBazookaHoldInputActive;
        
        // AI Detection
        private bool isAIControlled = false;
        
        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            
            // Check if this is AI-controlled
            isAIControlled = GetComponent<Hanzo.AI.AIPlayerController>() != null;
            
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
                
                if (showDebugInfo)
                    Debug.Log("[LOCAL PLAYER] Mobile controls enabled");
            }
            else
            {
                if (showDebugInfo)
                    Debug.Log("[LOCAL PLAYER] Keyboard/Gamepad controls enabled");
            }

            if (shouldUseMobileControls)
            {
                SetupBulletButton();
                SetupBazookaAimDragArea();
            }
            else
            {
                DisableBulletButton();
                DisableBazookaAimDragArea();
            }
        }
        
        private void DisableMobileControlsForRemotePlayer()
        {
            if (mobileControlsUI != null)
            {
                mobileControlsUI.SetActive(false);
                if (showDebugInfo)
                    Debug.Log("[REMOTE PLAYER/AI] Mobile controls disabled");
            }
            
            if (mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove -= OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased -= OnMobileJoystickReleased;
            }

            DisableBulletButton();
            DisableBazookaAimDragArea();
            
            shouldUseMobileControls = false;
        }
        
        private void OnEnable()
        {
            // Enable input for local player (not AI-controlled)
            if (!isAIControlled && photonView != null && photonView.IsMine)
            {
                inputActions?.Enable();

                if (shouldUseMobileControls && bazookaAimDragArea != null)
                {
                    bazookaAimDragArea.SetInputEnabled(true);
                }
            }
        }
        
        private void OnDisable()
        {
            ClearBazookaHoldInputs();
            BazookaAimInput = Vector2.zero;

            if (bazookaAimDragArea != null)
            {
                bazookaAimDragArea.SetInputEnabled(false);
            }

            inputActions?.Disable();
        }
        
        private void OnDestroy()
        {
            if (mobileJoystick != null)
            {
                mobileJoystick.OnJoystickMove -= OnMobileJoystickMove;
                mobileJoystick.OnJoystickReleased -= OnMobileJoystickReleased;
            }

            DisableBulletButton();
            DisableBazookaAimDragArea();
            
            inputActions?.Dispose();
        }

        private void SetupBulletButton()
        {
            bulletButton = FindBulletButton();
            if (bulletButton == null)
            {
                if (showDebugInfo)
                    Debug.LogWarning($"[LOCAL PLAYER] {bulletButtonName} not found under mobile controls.");
                return;
            }

            bulletButton.enabled = true;
            bulletButton.SetEnabled(true);
            bulletButton.Initialize(TriggerBazookaHoldStart, TriggerBazookaHoldEnd);
        }

        private MobileActionButton FindBulletButton()
        {
            if (bulletButton != null)
            {
                return bulletButton;
            }

            Transform searchRoot = mobileControlsUI != null ? mobileControlsUI.transform : transform;
            Transform[] children = searchRoot.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && string.Equals(child.name, bulletButtonName, StringComparison.Ordinal))
                {
                    MobileActionButton actionButton = child.GetComponent<MobileActionButton>();
                    return actionButton != null ? actionButton : child.gameObject.AddComponent<MobileActionButton>();
                }
            }

            return null;
        }

        private void DisableBulletButton()
        {
            if (isBazookaHoldButtonActive)
            {
                SetBazookaHoldButtonInput(false);
            }

            if (bulletButton == null)
            {
                return;
            }

            bulletButton.Initialize(null, null);
            bulletButton.SetEnabled(false);
            bulletButton.enabled = false;
        }

        private void SetupBazookaAimDragArea()
        {
            bazookaAimDragArea = FindBazookaAimDragArea();

            if (bazookaAimDragArea == null && autoCreateBazookaAimDragArea)
            {
                bazookaAimDragArea = CreateBazookaAimDragArea();
            }

            if (bazookaAimDragArea == null)
            {
                if (showDebugInfo)
                    Debug.LogWarning($"[LOCAL PLAYER] {bazookaAimDragAreaName} not found under mobile controls.");
                return;
            }

            bazookaAimDragArea.enabled = true;
            bazookaAimDragArea.SetInputEnabled(true);
        }

        private MobileAimDragArea FindBazookaAimDragArea()
        {
            if (bazookaAimDragArea != null)
            {
                return bazookaAimDragArea;
            }

            GameObject searchRootObject = EnsureMobileControlsUI();
            Transform searchRoot = searchRootObject != null ? searchRootObject.transform : transform;
            Transform[] children = searchRoot.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && string.Equals(child.name, bazookaAimDragAreaName, StringComparison.Ordinal))
                {
                    MobileAimDragArea dragArea = child.GetComponent<MobileAimDragArea>();
                    return dragArea != null ? dragArea : child.gameObject.AddComponent<MobileAimDragArea>();
                }
            }

            return null;
        }

        private MobileAimDragArea CreateBazookaAimDragArea()
        {
            GameObject root = EnsureMobileControlsUI();
            if (root == null)
                return null;

            GameObject dragAreaObject = new GameObject(
                bazookaAimDragAreaName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(MobileAimDragArea)
            );

            dragAreaObject.transform.SetParent(root.transform, false);
            dragAreaObject.transform.SetAsFirstSibling();

            RectTransform rectTransform = dragAreaObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.42f, 0f);
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = dragAreaObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;

            return dragAreaObject.GetComponent<MobileAimDragArea>();
        }

        private GameObject EnsureMobileControlsUI()
        {
            if (mobileControlsUI != null)
            {
                return mobileControlsUI;
            }

            if (!autoCreateBazookaAimDragArea)
                return null;

            GameObject canvasObject = new GameObject("MobileInputCanvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            mobileControlsUI = canvasObject;
            return mobileControlsUI;
        }

        private void DisableBazookaAimDragArea()
        {
            BazookaAimInput = Vector2.zero;

            if (bazookaAimDragArea == null)
                return;

            bazookaAimDragArea.SetInputEnabled(false);
            bazookaAimDragArea.enabled = false;
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

            UpdateBazookaAimDragInput();
            UpdateBazookaHoldKeyboardInput();
        }

        private void UpdateBazookaAimDragInput()
        {
            BazookaAimInput = shouldUseMobileControls && bazookaAimDragArea != null
                ? bazookaAimDragArea.ConsumeDragInput()
                : Vector2.zero;
        }

        private void UpdateBazookaHoldKeyboardInput()
        {
            bool shouldBeActive = false;

            if (bazookaHoldKey != Key.None && Keyboard.current != null)
            {
                var keyControl = Keyboard.current[bazookaHoldKey];
                shouldBeActive = keyControl != null && keyControl.isPressed;
            }

            SetBazookaHoldKeyboardInput(shouldBeActive);
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

        public void ResetMovementInput(bool notifyListeners = false)
        {
            MoveInput = Vector2.zero;

            if (mobileJoystick != null)
                mobileJoystick.ResetInputImmediate();

            if (notifyListeners)
                OnMoveInput?.Invoke(MoveInput);
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
            
            if (showDebugInfo)
                Debug.Log("Dash input received (Keyboard/Gamepad)");
            OnDashInput?.Invoke();
        }
        
        private void OnSpeedBoostPerformed(InputAction.CallbackContext context)
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            if (showDebugInfo)
                Debug.Log("Speed Boost input received (Keyboard/Gamepad)");
            OnSpeedBoostInput?.Invoke();
        }
        
        // Public methods to trigger abilities from mobile UI buttons
        public void TriggerDash()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            if (showDebugInfo)
                Debug.Log("Dash Triggered (Mobile Button)");
            OnDashInput?.Invoke();
        }
        
        public void TriggerSpeedBoost()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            
            if (showDebugInfo)
                Debug.Log("Speed Boost Triggered (Mobile Button)");
            OnSpeedBoostInput?.Invoke();
        }

        public void TriggerBazookaHoldStart()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            SetBazookaHoldButtonInput(true);
        }

        public void TriggerBazookaHoldEnd()
        {
            if (isAIControlled || photonView == null || !photonView.IsMine) return;
            SetBazookaHoldButtonInput(false);
        }

        private void SetBazookaHoldButtonInput(bool active)
        {
            if (isBazookaHoldButtonActive == active)
                return;

            isBazookaHoldButtonActive = active;

            if (showDebugInfo)
                Debug.Log(active ? "Bazooka Hold Started (Bullet Button)" : "Bazooka Hold Ended (Bullet Button)");

            RefreshBazookaHoldInputState();
        }

        private void SetBazookaHoldKeyboardInput(bool active)
        {
            if (isBazookaHoldKeyboardActive == active)
                return;

            isBazookaHoldKeyboardActive = active;

            if (showDebugInfo)
                Debug.Log(active ? $"Bazooka Hold Started ({bazookaHoldKey} Key)" : $"Bazooka Hold Ended ({bazookaHoldKey} Key)");

            RefreshBazookaHoldInputState();
        }

        private void RefreshBazookaHoldInputState()
        {
            bool shouldBeActive = isBazookaHoldButtonActive || isBazookaHoldKeyboardActive;
            if (isBazookaHoldInputActive == shouldBeActive)
                return;

            isBazookaHoldInputActive = shouldBeActive;

            if (isBazookaHoldInputActive)
                OnBazookaHoldStarted?.Invoke();
            else
                OnBazookaHoldEnded?.Invoke();
        }

        private void ClearBazookaHoldInputs()
        {
            if (!isBazookaHoldInputActive && !isBazookaHoldButtonActive && !isBazookaHoldKeyboardActive)
                return;

            isBazookaHoldButtonActive = false;
            isBazookaHoldKeyboardActive = false;
            RefreshBazookaHoldInputState();
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
