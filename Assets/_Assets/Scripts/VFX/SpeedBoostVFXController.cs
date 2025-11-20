using System.Collections;
using UnityEngine;
using Photon.Pun; 

namespace Hanzo.VFX
{
    /// <summary>
    /// Manages Speed Boost visual effects including aura, particles, and material effects
    /// Monitors SPEEDBOOST animator parameter for automatic playback
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeedBoostVFXController : MonoBehaviour
    {
        [Header("Prefab & Setup")]
        [Tooltip("Prefab containing speed boost particle systems (aura, speed lines, energy burst).")]
        [SerializeField]
        private GameObject speedBoostVFXPrefab;

        [Tooltip("Local offset from player root where VFX spawns.")]
        [SerializeField]
        private Vector3 localOffset = new Vector3(0f, 0.5f, 0f);

        [Header("Continuous Effects")]
        [Tooltip("Emit speed lines continuously while boost is active.")]
        [SerializeField]
        private bool emitWhileBoosting = true;

        [Tooltip("Particles to emit per interval.")]
        [SerializeField]
        private int speedLinesEmitCount = 3;

        [Tooltip("Interval between continuous emissions.")]
        [SerializeField]
        private float emitInterval = 0.1f;

        [Header("Material Effects (Optional)")]
        [Tooltip("Apply glow/emission to player materials during boost.")]
        [SerializeField]
        private bool applyMaterialGlow = true;

        [Tooltip("Glow color for boosted state.")]
        [SerializeField]
        private Color glowColor = new Color(1f, 0.8f, 0f, 1f);

        [Tooltip("Emission intensity multiplier.")]
        [SerializeField]
        private float emissionIntensity = 2f;

        [Header("Camera Effects")]
        [Tooltip("Optional camera FX controller for zoom and screen glow.")]
        [SerializeField]
        private SpeedBoostCameraFX cameraFX;

        private GameObject vfxInstance;
        private ParticleSystem[] particleSystems;
        private Animator animator;
        private int isSpeedBoostHash;
        private bool lastBoostingState = false;
        private Coroutine emitCoroutine;

        // Material glow tracking
        private Renderer[] playerRenderers;
        private MaterialPropertyBlock propertyBlock;
        private bool glowActive = false;

        public bool IsSpeedBoostVFXActive => vfxInstance != null && vfxInstance.activeInHierarchy;
        private PhotonView photonView;





        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            if (speedBoostVFXPrefab == null)
            {
                Debug.LogError("SpeedBoostVFXController: speedBoostVFXPrefab is not assigned!");
                return;
            }

            // Instantiate VFX as child
            vfxInstance = Instantiate(speedBoostVFXPrefab, transform);
            vfxInstance.transform.localPosition = localOffset;
            vfxInstance.transform.localRotation = Quaternion.identity;
            vfxInstance.SetActive(false);

            // Cache particle systems
            particleSystems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);

            // Find animator
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("SpeedBoostVFXController: No Animator found. Manual Play() required.");
            }
            else
            {
                isSpeedBoostHash = Animator.StringToHash("SPEEDBOOST");
            }

            // Setup material glow if enabled
            if (applyMaterialGlow)
            {
                SetupMaterialGlow();
            }

            // Auto-find camera FX if not assigned
            if (cameraFX == null)
            {
                cameraFX = GetComponent<SpeedBoostCameraFX>();
            }
        }

        private void SetupMaterialGlow()
        {
            // Find all renderers except the VFX itself
            playerRenderers = GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            lastBoostingState = false;
        }

        private void Update()
        {
            if (photonView == null || !photonView.IsMine) return;
    
            if (animator == null)
                return;

            bool isBoosting = animator.GetBool(isSpeedBoostHash);

            // Rising edge - start boost
            if (!lastBoostingState && isBoosting)
            {
                Play();
            }

            // Falling edge - stop boost
            if (lastBoostingState && !isBoosting)
            {
                Stop();
            }

            lastBoostingState = isBoosting;
        }

        /// <summary>
        /// Start speed boost VFX
        /// </summary>
        public void Play()
        {
            if (vfxInstance == null)
                return;

            vfxInstance.SetActive(true);

            // Play all particle systems
            foreach (var ps in particleSystems)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            // Apply material glow
            if (applyMaterialGlow)
            {
                ApplyGlow();
            }

            // Start camera effects
            if (cameraFX != null)
            {
                cameraFX.StartBoostFX();
            }

            // Start continuous emission
            if (emitWhileBoosting && emitCoroutine == null)
            {
                emitCoroutine = StartCoroutine(EmitWhileBoosting());
            }

            Debug.Log("SpeedBoostVFX: Started");
        }

        /// <summary>
        /// Stop speed boost VFX
        /// </summary>
        public void Stop()
        {
            if (vfxInstance == null)
                return;

            // Stop camera effects
            if (cameraFX != null)
            {
                cameraFX.StopBoostFX();
            }

            // Stop continuous emission
            if (emitCoroutine != null)
            {
                StopCoroutine(emitCoroutine);
                emitCoroutine = null;
            }

            // Stop particle systems
            foreach (var ps in particleSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // Remove material glow
            if (applyMaterialGlow)
            {
                RemoveGlow();
            }

            // Auto-disable after particles finish
            StartCoroutine(DisableWhenDone());

            Debug.Log("SpeedBoostVFX: Stopped");
        }

        private void ApplyGlow()
        {
            if (playerRenderers == null || propertyBlock == null)
                return;

            glowActive = true;

            foreach (var renderer in playerRenderers)
            {
                // Skip VFX renderers
                if (renderer.transform.IsChildOf(vfxInstance.transform))
                    continue;

                renderer.GetPropertyBlock(propertyBlock);

                // Set emission color (for URP Lit shader)
                propertyBlock.SetColor("_EmissionColor", glowColor * emissionIntensity);

                // Alternative: Set base color tint
                propertyBlock.SetColor("_BaseColor", glowColor);

                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void RemoveGlow()
        {
            if (!glowActive || playerRenderers == null || propertyBlock == null)
                return;

            glowActive = false;

            foreach (var renderer in playerRenderers)
            {
                if (renderer.transform.IsChildOf(vfxInstance.transform))
                    continue;

                // Clear property block to restore original material
                renderer.SetPropertyBlock(null);
            }
        }

        private IEnumerator EmitWhileBoosting()
        {
            while (true)
            {
                // Find speed lines system and emit
                foreach (var ps in particleSystems)
                {
                    // Emit on systems with "Speed" or "Lines" in name
                    if (ps.name.Contains("Speed") || ps.name.Contains("Lines"))
                    {
                        ps.Emit(speedLinesEmitCount);
                    }
                }

                yield return new WaitForSeconds(emitInterval);
            }
        }

        private IEnumerator DisableWhenDone()
        {
            // Wait for longest particle system to finish
            float maxLifetime = 0f;
            foreach (var ps in particleSystems)
            {
                var main = ps.main;
                float lifetime = main.duration + main.startLifetime.constantMax;
                if (lifetime > maxLifetime)
                    maxLifetime = lifetime;
            }

            yield return new WaitForSeconds(maxLifetime + 0.1f);

            if (vfxInstance != null)
                vfxInstance.SetActive(false);
        }

        private void OnDestroy()
        {
            if (emitCoroutine != null)
            {
                StopCoroutine(emitCoroutine);
            }

            if (applyMaterialGlow)
            {
                RemoveGlow();
            }
        }

        private void OnDisable()
        {
            // Clean up when component is disabled
            if (emitCoroutine != null)
            {
                StopCoroutine(emitCoroutine);
                emitCoroutine = null;
            }

            if (applyMaterialGlow)
            {
                RemoveGlow();
            }
        }
    }
}