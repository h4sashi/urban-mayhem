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
        private bool isPressed = false;
        private bool isEnabled = true;
        private Vector3 originalScale;
        
        private void Awake()
        {
            originalScale = transform.localScale;
            
            if (buttonImage == null)
            {
                buttonImage = GetComponent<Image>();
            }
            
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 0f;
            }
        }
        
        public void Initialize(System.Action callback)
        {
            onButtonPressed = callback;
        }
        
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isEnabled) return;
            
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
            if (!isEnabled) return;
            
            isPressed = false;
            
            if (buttonImage != null)
            {
                buttonImage.color = normalColor;
            }
            
            if (showPressEffect)
            {
                transform.localScale = originalScale;
            }
        }
        
        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            
            if (buttonImage != null)
            {
                buttonImage.color = enabled ? normalColor : disabledColor;
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