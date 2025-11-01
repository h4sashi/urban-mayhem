using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

namespace Hanzo.UI
{
    /// <summary>
    /// Floating joystick that appears where the user touches
    /// </summary>
    public class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [Header("Joystick Components")]
        [SerializeField] private RectTransform joystickContainer;
        [SerializeField] private RectTransform joystickBackground;
        [SerializeField] private RectTransform joystickHandle;

        [Header("Settings")]
        [SerializeField] private float handleRange = 50f;
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private bool fadeWhenNotUsed = true;
        [SerializeField] private float fadeDuration = 0.3f;
        
        [Header("Invert Settings")]
        [SerializeField] private bool invertHorizontal = false;
        [SerializeField] private bool invertVertical = true;

        [Header("Visual Feedback")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float activeAlpha = 1f;
        [SerializeField] private float inactiveAlpha = 0.3f;

        // Events
        public event Action<Vector2> OnJoystickMove;
        public event Action OnJoystickReleased;

        // State
        private Vector2 inputVector;
        private bool isActive = false;
        private Canvas parentCanvas;
        private Camera mainCamera;
        private float currentAlpha;

        public Vector2 InputVector => inputVector;
        public bool IsActive => isActive;

        private void Awake()
        {
            parentCanvas = GetComponentInParent<Canvas>();
            
            if (canvasGroup == null)
            {
                canvasGroup = joystickContainer.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = joystickContainer.gameObject.AddComponent<CanvasGroup>();
                }
            }

            // Start invisible
            if (fadeWhenNotUsed)
            {
                canvasGroup.alpha = 0f;
            }
            else
            {
                canvasGroup.alpha = inactiveAlpha;
            }

            // Hide joystick at start
            joystickContainer.gameObject.SetActive(false);
        }

        private void Start()
        {
            mainCamera = Camera.main;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Position joystick at touch location
            Vector2 touchPosition = eventData.position;
            
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform,
                touchPosition,
                eventData.pressEventCamera,
                out Vector2 localPoint))
            {
                joystickContainer.localPosition = localPoint;
            }

            // Show and activate joystick
            joystickContainer.gameObject.SetActive(true);
            isActive = true;

            // Fade in
            if (fadeWhenNotUsed)
            {
                StopAllCoroutines();
                StartCoroutine(FadeJoystick(activeAlpha));
            }

            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isActive) return;

            Vector2 touchPosition = eventData.position;
            Vector2 joystickCenter = RectTransformUtility.WorldToScreenPoint(
                eventData.pressEventCamera,
                joystickBackground.position);

            Vector2 direction = touchPosition - joystickCenter;
            float distance = direction.magnitude;

            // Normalize and clamp
            Vector2 normalizedDirection = direction.normalized;
            float clampedDistance = Mathf.Min(distance, handleRange);

            // Position handle
            joystickHandle.anchoredPosition = normalizedDirection * clampedDistance;

            // Calculate input vector
            inputVector = normalizedDirection * (clampedDistance / handleRange);

            // Apply inversion
            if (invertHorizontal)
            {
                inputVector.x = -inputVector.x;
            }
            if (invertVertical)
            {
                inputVector.y = -inputVector.y;
            }

            // Apply deadzone
            if (inputVector.magnitude < deadZone)
            {
                inputVector = Vector2.zero;
            }

            // Invoke event
            OnJoystickMove?.Invoke(inputVector);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // Reset joystick
            joystickHandle.anchoredPosition = Vector2.zero;
            inputVector = Vector2.zero;
            isActive = false;

            // Fade out or hide
            if (fadeWhenNotUsed)
            {
                StopAllCoroutines();
                StartCoroutine(FadeJoystick(0f, () => 
                {
                    joystickContainer.gameObject.SetActive(false);
                }));
            }
            else
            {
                StartCoroutine(FadeJoystick(inactiveAlpha));
            }

            // Invoke event
            OnJoystickReleased?.Invoke();
        }

        private System.Collections.IEnumerator FadeJoystick(float targetAlpha, Action onComplete = null)
        {
            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
            onComplete?.Invoke();
        }

        // Public methods to get input
        public float GetHorizontal()
        {
            return inputVector.x;
        }

        public float GetVertical()
        {
            return inputVector.y;
        }

        public Vector2 GetInput()
        {
            return inputVector;
        }

        // Optional: Visualize handle range in editor
        private void OnDrawGizmosSelected()
        {
            if (joystickBackground != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(joystickBackground.position, handleRange);
            }
        }
    }
}