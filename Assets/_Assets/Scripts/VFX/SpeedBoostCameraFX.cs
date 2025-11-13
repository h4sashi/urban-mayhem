using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace Hanzo.VFX
{
    /// <summary>
    /// Handles camera effects for speed boost including FOV zoom and screen glow
    /// Mobile-optimized using UI overlay instead of post-processing
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeedBoostCameraFX : MonoBehaviour
    {
        [Header("Camera Zoom")]
        [Tooltip("Cinemachine virtual camera to apply FOV changes to.")]
        [SerializeField]
        private CinemachineVirtualCamera vcam;

        [Tooltip("Target FOV when speed boost is active.")]
        [SerializeField]
        private float boostedFOV = 70f;

        [Tooltip("Duration of FOV transition in seconds.")]
        [SerializeField]
        private float fovTransitionDuration = 0.3f;

        [Tooltip("Animation curve for FOV zoom in.")]
        [SerializeField]
        private AnimationCurve zoomInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Animation curve for FOV zoom out.")]
        [SerializeField]
        private AnimationCurve zoomOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Screen Glow (Mobile-Optimized)")]
        [Tooltip("Apply yellow glow vignette effect during boost.")]
        [SerializeField]
        private bool enableScreenGlow = true;

        [Tooltip("UI Image for screen overlay (will be created if null).")]
        [SerializeField]
        private Image glowOverlay;

        [Tooltip("Glow color (yellow/electric).")]
        [SerializeField]
        private Color glowColor = new Color(1f, 0.9f, 0.3f, 0.35f);

        [Tooltip("Maximum glow alpha intensity.")]
        [SerializeField, Range(0f, 1f)]
        private float maxGlowAlpha = 0.35f;

        [Tooltip("Duration of glow fade in/out.")]
        [SerializeField]
        private float glowTransitionDuration = 0.25f;

        [Tooltip("Pulsing effect frequency (0 = no pulse).")]
        [SerializeField]
        private float pulseFrequency = 2f;

        [Tooltip("Pulse intensity variation.")]
        [SerializeField]
        private float pulseAmount = 0.15f;

        [Header("Vignette Shape")]
        [Tooltip("Use radial gradient for vignette effect.")]
        [SerializeField]
        private bool useRadialGradient = true;

        [Tooltip("Vignette softness (0-1).")]
        [SerializeField]
        private float vignetteSoftness = 0.5f;

        // Public accessor for UI binding
        public float MaxGlowAlpha => maxGlowAlpha;

        private float originalFOV;
        private Coroutine fovCoroutine;
        private Coroutine glowCoroutine;
        private bool isActive = false;
        private Canvas overlayCanvas;
        private Material vignetteMaterial;

        private void Awake()
        {
            // Cache original FOV
            if (vcam != null)
            {
                originalFOV = vcam.m_Lens.FieldOfView;
            }

            // Setup UI overlay for screen glow
            if (enableScreenGlow && glowOverlay == null)
            {
                CreateGlowOverlay();
            }

            if (glowOverlay != null)
            {
                // Set initial color with zero alpha
                Color initialColor = glowColor;
                initialColor.a = 0f;
                glowOverlay.color = initialColor;

                // Setup vignette material if enabled
                if (useRadialGradient)
                {
                    SetupVignetteMaterial();
                }

                // Ensure overlay is initially inactive
                glowOverlay.gameObject.SetActive(false);
            }
        }

        private void CreateGlowOverlay()
        {
            // Find or create canvas
            overlayCanvas = FindObjectOfType<Canvas>();
            if (overlayCanvas == null)
            {
                GameObject canvasObj = new GameObject("SpeedBoost_ScreenOverlay");
                overlayCanvas = canvasObj.AddComponent<Canvas>();
                overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                overlayCanvas.sortingOrder = 9999; // Render on top

                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>().enabled = false;
            }

            // Create overlay image
            GameObject overlayObj = new GameObject("GlowOverlay");
            overlayObj.transform.SetParent(overlayCanvas.transform, false);

            glowOverlay = overlayObj.AddComponent<Image>();

            // Stretch to fill screen
            RectTransform rt = glowOverlay.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // Set to not block raycasts
            glowOverlay.raycastTarget = false;
        }

        private void SetupVignetteMaterial()
        {
            // Use UI default shader as fallback
            Shader vignetteShader = Shader.Find("UI/Default");
            vignetteMaterial = new Material(vignetteShader);

            // Create a radial gradient texture and assign it as sprite
            Texture2D vignetteTexture = CreateRadialGradientTexture(512, 512);
            glowOverlay.material = vignetteMaterial;
            glowOverlay.sprite = Sprite.Create(
                vignetteTexture,
                new Rect(0, 0, vignetteTexture.width, vignetteTexture.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        private Texture2D CreateRadialGradientTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];

            Vector2 center = new Vector2(width / 2f, height / 2f);
            float maxRadius = Mathf.Min(width, height) / 2f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float distance = Vector2.Distance(pos, center);
                    float normalizedDist = distance / maxRadius;

                    // Create vignette effect (dark edges, transparent center)
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Pow(normalizedDist, 1f - vignetteSoftness));

                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>
        /// Sets the virtual camera reference
        /// </summary>
        public void SetVirtualCamera(CinemachineVirtualCamera camera)
        {
            vcam = camera;
            if (vcam != null)
            {
                originalFOV = vcam.m_Lens.FieldOfView;
            }
        }

        /// <summary>
        /// Adjusts the maximum glow intensity at runtime (e.g., via settings slider)
        /// </summary>
        /// <param name="intensity">Expected range 0 - 1</param>
        public void SetGlowIntensity(float intensity)
        {
            maxGlowAlpha = Mathf.Clamp01(intensity);
            // If currently active, apply instantly to overlay (no coroutine jump)
            if (isActive && glowOverlay != null)
            {
                Color c = glowColor;
                c.a = maxGlowAlpha;
                glowOverlay.color = c;
            }
        }

        /// <summary>
        /// Activates speed boost camera effects
        /// </summary>
        public void StartBoostFX()
        {
            if (isActive) return;
            isActive = true;

            // Start FOV zoom
            if (vcam != null)
            {
                if (fovCoroutine != null) StopCoroutine(fovCoroutine);
                fovCoroutine = StartCoroutine(TransitionFOV(originalFOV, boostedFOV, zoomInCurve));
            }

            // Start screen glow
            if (enableScreenGlow && glowOverlay != null)
            {
                if (glowCoroutine != null) StopCoroutine(glowCoroutine);
                glowOverlay.gameObject.SetActive(true);
                glowCoroutine = StartCoroutine(TransitionGlow(true));
            }
        }

        /// <summary>
        /// Deactivates speed boost camera effects
        /// </summary>
        public void StopBoostFX()
        {
            if (!isActive) return;
            isActive = false;

            // Restore FOV
            if (vcam != null)
            {
                if (fovCoroutine != null) StopCoroutine(fovCoroutine);
                fovCoroutine = StartCoroutine(TransitionFOV(vcam.m_Lens.FieldOfView, originalFOV, zoomOutCurve));
            }

            // Remove screen glow
            if (enableScreenGlow && glowOverlay != null)
            {
                if (glowCoroutine != null) StopCoroutine(glowCoroutine);
                glowCoroutine = StartCoroutine(TransitionGlow(false));
            }
        }

        private IEnumerator TransitionFOV(float startFOV, float targetFOV, AnimationCurve curve)
        {
            float elapsed = 0f;

            while (elapsed < fovTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fovTransitionDuration);
                float curveValue = curve.Evaluate(t);

                vcam.m_Lens.FieldOfView = Mathf.Lerp(startFOV, targetFOV, curveValue);

                yield return null;
            }

            vcam.m_Lens.FieldOfView = targetFOV;
            fovCoroutine = null;
        }

        private IEnumerator TransitionGlow(bool fadeIn)
        {
            // Capture starting alpha each time (so editor changes mid-fade are respected)
            float startAlpha = glowOverlay.color.a;
            float elapsed = 0f;

            // Fade phase
            while (elapsed < glowTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / glowTransitionDuration);

                // Re-evaluate target alpha every frame so runtime/editor changes apply immediately
                float targetAlpha = fadeIn ? maxGlowAlpha : 0f;

                float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);

                // Add pulse on top of base alpha if fading in
                if (fadeIn && pulseFrequency > 0f)
                {
                    float pulse = Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) * pulseAmount;
                    alpha = Mathf.Clamp01(alpha + pulse);
                }

                Color baseColor = glowColor;
                baseColor.a = alpha;
                glowOverlay.color = baseColor;

                yield return null;
            }

            // Ensure final alpha reflects current maxGlowAlpha (in case it changed while transitioning)
            Color finalColor = glowColor;
            finalColor.a = fadeIn ? maxGlowAlpha : 0f;
            glowOverlay.color = finalColor;

            // Continue pulsing while active (only when fading in)
            if (fadeIn)
            {
                while (isActive)
                {
                    if (pulseFrequency > 0f)
                    {
                        float pulse = Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) * pulseAmount;
                        Color pulsedColor = glowColor;
                        pulsedColor.a = Mathf.Clamp01(maxGlowAlpha + pulse);
                        glowOverlay.color = pulsedColor;
                    }
                    else
                    {
                        // if no pulsing, ensure overlay matches maxGlowAlpha in case it was changed externally
                        Color liveColor = glowColor;
                        liveColor.a = maxGlowAlpha;
                        glowOverlay.color = liveColor;
                    }

                    yield return null;
                }
            }
            else
            {
                // fully faded out
                glowOverlay.gameObject.SetActive(false);
            }

            glowCoroutine = null;
        }

        private void OnDisable()
        {
            // Clean up effects
            if (fovCoroutine != null)
            {
                StopCoroutine(fovCoroutine);
                if (vcam != null) vcam.m_Lens.FieldOfView = originalFOV;
            }

            if (glowCoroutine != null)
            {
                StopCoroutine(glowCoroutine);
                if (glowOverlay != null)
                {
                    Color clearColor = glowOverlay.color;
                    clearColor.a = 0f;
                    glowOverlay.color = clearColor;
                    glowOverlay.gameObject.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up created objects
            if (glowOverlay != null && overlayCanvas != null)
            {
                Destroy(glowOverlay.gameObject);
            }

            if (vignetteMaterial != null)
            {
                Destroy(vignetteMaterial);
            }
        }
    }
}
