using UnityEngine;
using UnityEngine.UI;

namespace Hanzo.Core.Utilities
{
    /// <summary>
    /// Mobile-optimized damage indicator that points toward active threats.
    /// Uses object pooling - never instantiated/destroyed at runtime.
    /// </summary>
    public class DamageIndicator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform pivotTransform;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image indicatorImage;
        [SerializeField] private Image urgencyFillImage; // Optional: fills as danger increases

        [Header("Visual Settings")]
        [SerializeField] private Color normalColor = new Color(1f, 0.8f, 0f, 0.8f);
        [SerializeField] private Color dangerColor = new Color(1f, 0.2f, 0f, 1f);
        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float minScale = 0.8f;
        [SerializeField] private float maxScale = 1.2f;

        // Cached references - avoid repeated lookups
        private Transform playerTransform;
        private Transform trackedTarget;
        private Camera mainCamera;
        private RectTransform rectTransform;
        private RectTransform canvasRect;
        
        // State
        private float remainingTime;
        private float totalDuration;
        private bool isActive;
        private float baseAlpha = 1f;
        private bool isInitialized;
        
        // Cached vectors - avoid allocations in Update
        private Vector3 directionToTarget;
        private Vector3 flatPlayerPos;
        private Vector3 flatTargetPos;

        public bool IsActive => isActive;
        public Transform TrackedTarget => trackedTarget;

        // Modified Initialize method to accept canvasRect parameter
        public void Initialize(RectTransform canvasRect)
        {
            if (isInitialized) return;
            
            this.canvasRect = canvasRect;
            mainCamera = Camera.main;
            rectTransform = GetComponent<RectTransform>();
            
            // Auto-reference pivotTransform if not assigned
            if (pivotTransform == null)
            {
                // Try to find child named "Pivot"
                Transform pivot = transform.Find("Pivot");
                if (pivot != null)
                {
                    pivotTransform = pivot.GetComponent<RectTransform>();
                }
                
                // Fallback: use own RectTransform
                if (pivotTransform == null)
                {
                    pivotTransform = rectTransform;
                    Debug.LogWarning($"[DamageIndicator] PivotTransform not assigned on {gameObject.name}, using self.");
                }
            }
            
            // Auto-reference canvasGroup if not assigned
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            
            // Auto-reference indicatorImage if not assigned
            if (indicatorImage == null)
            {
                indicatorImage = GetComponentInChildren<Image>();
            }
            
            isInitialized = true;
        }

        /// <summary>
        /// Activates the indicator to track a specific trap/threat.
        /// Called by DamageIndicatorManager from pool.
        /// </summary>
        public void Activate(Transform target, Transform player, float duration, float initialAlpha = 1f)
        {
            if (!isInitialized)
            {
                Debug.LogError("[DamageIndicator] Not initialized! Call Initialize() first.");
                return;
            }

            trackedTarget = target;
            playerTransform = player;
            totalDuration = duration;
            remainingTime = duration;
            baseAlpha = initialAlpha;
            isActive = true;

            canvasGroup.alpha = baseAlpha;
            gameObject.SetActive(true);
            
            // Reset scale
            pivotTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// Deactivates and returns to pool. No destruction.
        /// </summary>
        public void Deactivate()
        {
            isActive = false;
            trackedTarget = null;
            playerTransform = null; // Clear player reference too
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Call this when the tracked trap detonates for immediate removal.
        /// </summary>
        public void OnTargetDetonated()
        {
            Deactivate();
        }

        private void Update()
        {
            if (!isActive) return;

            // Early exit if critical references are missing
            if (trackedTarget == null || playerTransform == null)
            {
                Deactivate();
                return;
            }

            UpdateTimer();
            UpdateRotation();
            UpdateVisuals();
        }

        private void UpdateTimer()
        {
            remainingTime -= Time.deltaTime;
            if (remainingTime <= 0f)
            {
                Deactivate();
            }
        }

        private void UpdateRotation()
        {
            // Additional safety check
            if (playerTransform == null || trackedTarget == null || pivotTransform == null)
            {
                Deactivate();
                return;
            }

            // Flatten Y to get horizontal direction only
            flatPlayerPos.x = playerTransform.position.x;
            flatPlayerPos.y = 0f;
            flatPlayerPos.z = playerTransform.position.z;

            flatTargetPos.x = trackedTarget.position.x;
            flatTargetPos.y = 0f;
            flatTargetPos.z = trackedTarget.position.z;

            directionToTarget = (flatTargetPos - flatPlayerPos).normalized;

            // Calculate angle relative to player's forward
            float angle = Vector3.SignedAngle(directionToTarget, playerTransform.forward, Vector3.up);
            
            // Apply rotation (only Z axis for UI)
            pivotTransform.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        private void UpdateVisuals()
        {
            // Safety check
            if (pivotTransform == null || canvasGroup == null) return;

            float progress = 1f - (remainingTime / totalDuration); // 0 to 1 as danger increases
            float urgency = Mathf.Clamp01(progress);

            // Color lerp from warning to danger
            if (indicatorImage != null)
            {
                indicatorImage.color = Color.Lerp(normalColor, dangerColor, urgency);
            }

            // Urgency fill (optional radial or linear fill)
            if (urgencyFillImage != null)
            {
                urgencyFillImage.fillAmount = urgency;
            }

            // Pulse effect - faster as danger increases
            float currentPulseSpeed = Mathf.Lerp(pulseSpeed, pulseSpeed * 3f, urgency);
            float pulse = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(Time.time * currentPulseSpeed) + 1f) * 0.5f);
            pivotTransform.localScale = new Vector3(pulse, pulse, 1f);

            // Alpha fade based on urgency (more visible when dangerous)
            canvasGroup.alpha = Mathf.Lerp(baseAlpha * 0.6f, baseAlpha, urgency);
        }

        /// <summary>
        /// Updates remaining time externally (e.g., if trap countdown changes)
        /// </summary>
        public void UpdateRemainingTime(float newTime)
        {
            remainingTime = newTime;
        }

        /// <summary>
        /// Gets distance to target for priority sorting
        /// </summary>
        public float GetDistanceToPlayer()
        {
            if (playerTransform == null || trackedTarget == null) return float.MaxValue;
            return Vector3.Distance(playerTransform.position, trackedTarget.position);
        }
    }
}