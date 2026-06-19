using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Hanzo.UI
{
    /// <summary>
    /// Mobile button for abilities (Dash, Speed Boost, etc.)
    /// </summary>
    public class MobileActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Button Settings")]
        [SerializeField] private Image buttonImage;
        [SerializeField] private Image cooldownOverlay;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color pressedColor = new Color(0.8f, 0.8f, 0.8f);
        [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f);
        
        [Header("Visual Feedback")]
        [SerializeField] private bool showPressEffect = true;
        [SerializeField] private float pressScale = 0.9f;
        
        private System.Action onButtonPressed;
        private System.Action onButtonReleased;
        private bool isPressed = false;
        private bool isEnabled = true;
        private Vector3 originalScale;
        private bool hasCachedOriginalScale;
        
        private void Awake()
        {
            CacheVisualState();
            
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 0f;
            }
        }
        
        public void Initialize(System.Action callback)
        {
            Initialize(callback, null);
        }

        public void Initialize(System.Action pressedCallback, System.Action releasedCallback)
        {
            onButtonPressed = pressedCallback;
            onButtonReleased = releasedCallback;
        }
        
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isEnabled) return;

            CacheVisualState();
            
            isPressed = true;
            
            if (buttonImage != null)
            {
                buttonImage.color = pressedColor;
            }
            
            if (showPressEffect)
            {
                transform.localScale = originalScale * pressScale;
            }
            
            // Trigger action
            onButtonPressed?.Invoke();
        }
        
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isPressed) return;
            
            isPressed = false;
            RestoreNormalVisualState();
            onButtonReleased?.Invoke();
        }

        private void OnDisable()
        {
            if (isPressed)
            {
                isPressed = false;
                onButtonReleased?.Invoke();
            }

            RestoreNormalVisualState();
        }

        public void SetEnabled(bool enabled)
        {
            CacheVisualState();
            isEnabled = enabled;
            
            if (buttonImage != null)
            {
                buttonImage.color = enabled ? normalColor : disabledColor;
            }
        }

        private void RestoreNormalVisualState()
        {
            CacheVisualState();

            if (buttonImage != null)
            {
                buttonImage.color = isEnabled ? normalColor : disabledColor;
            }
            
            if (showPressEffect)
            {
                transform.localScale = originalScale;
            }
        }

        private void CacheVisualState()
        {
            if (!hasCachedOriginalScale)
            {
                originalScale = transform.localScale;
                hasCachedOriginalScale = true;
            }
            
            if (buttonImage == null)
            {
                buttonImage = GetComponent<Image>();
            }
        }
        
        public void UpdateCooldown(float cooldownPercent)
        {
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = cooldownPercent;
            }
            
            SetEnabled(cooldownPercent <= 0f);
        }
    }
}
