using UnityEngine;
using Hanzo.Core.Interfaces;
using Photon.Pun;
using Hanzo.VFX;

namespace Hanzo.Player.Abilities
{
    public class SpeedBoostAbility : IAbility
    {
        private IMovementController controller;
        private AbilitySettings settings;
        private TrailRenderer trailRenderer;
        private Animator animator;
        private PhotonView photonView;
        private SpeedBoostVFXController vfxController;
        
        private bool isActive;
        private float activeTimer;
        private float cooldownTimer;
        
        // Runtime speed multiplier (applied to MovingState via event)
        private float currentSpeedMultiplier = 1f;
        
        // STACKING SYSTEM
        private int stackLevel = 1;
        
        private static readonly int IsSpeedBoostHash = Animator.StringToHash("SPEEDBOOST");
        
        public string AbilityName => "Speed Boost";
        public bool CanActivate => !isActive && cooldownTimer <= 0f;
        public bool IsActive => isActive;
        public float CooldownRemaining => Mathf.Max(0f, cooldownTimer);
        public int StackLevel => stackLevel;
        public float CurrentSpeedMultiplier => currentSpeedMultiplier;
        
        // Event to notify MovingState of speed changes
        public event System.Action<float> OnSpeedMultiplierChanged;
        
        public SpeedBoostAbility(AbilitySettings abilitySettings)
        {
            settings = abilitySettings;
        }
        
        public void SetVFXController(SpeedBoostVFXController vfx)
        {
            vfxController = vfx;
        }
        
        public void Initialize(IMovementController movementController)
        {
            controller = movementController;
            photonView = controller.Transform.GetComponent<PhotonView>();
            
            animator = controller.Transform.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError("SpeedBoostAbility: No Animator found on player!");
            }
            
            if (vfxController == null && controller.Transform != null)
            {
                vfxController = controller.Transform.GetComponentInChildren<SpeedBoostVFXController>(true);
            }
            
            SetupTrailRenderer();
        }
        
        private void SetupTrailRenderer()
        {
            GameObject trailObject = new GameObject("SpeedBoostTrail");
            trailObject.transform.SetParent(controller.Transform, false);
            trailObject.transform.localPosition = Vector3.zero;
            
            trailRenderer = trailObject.AddComponent<TrailRenderer>();
            trailRenderer.time = settings.SpeedBoostTrailTime;
            trailRenderer.startWidth = settings.SpeedBoostTrailWidth;
            trailRenderer.endWidth = 0f;
            trailRenderer.colorGradient = settings.SpeedBoostTrailColor;
            trailRenderer.material = settings.TrailMaterial != null 
                ? settings.TrailMaterial 
                : CreateDefaultTrailMaterial();
            trailRenderer.emitting = false;
            
            trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trailRenderer.receiveShadows = false;
        }
        
        private Material CreateDefaultTrailMaterial()
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.yellow);
            return mat;
        }
        
        public void AddStack()
        {
            if (stackLevel < 3)
            {
                stackLevel++;
                Debug.Log($"Speed Boost stack increased to level {stackLevel}");
                UpdateTrailForStack();
            }
        }
        
        public void ResetStacks()
        {
            stackLevel = 1;
            UpdateTrailForStack();
        }
        
        private void UpdateTrailForStack()
        {
            if (trailRenderer == null) return;
            
            switch (stackLevel)
            {
                case 1:
                    trailRenderer.time = settings.SpeedBoostTrailTime;
                    trailRenderer.startWidth = settings.SpeedBoostTrailWidth;
                    break;
                case 2:
                    trailRenderer.time = settings.SpeedBoostTrailTime * 1.3f;
                    trailRenderer.startWidth = settings.SpeedBoostTrailWidth * 1.2f;
                    break;
                case 3:
                    trailRenderer.time = settings.SpeedBoostTrailTime * 1.6f;
                    trailRenderer.startWidth = settings.SpeedBoostTrailWidth * 1.5f;
                    break;
            }
        }
        
        public bool TryActivate()
        {
            if (!CanActivate) return false;
            
            isActive = true;
            activeTimer = 0f;
            
            // Calculate speed multiplier based on stack
            currentSpeedMultiplier = GetSpeedMultiplierForStack();
            
            // Notify movement system of speed change
            OnSpeedMultiplierChanged?.Invoke(currentSpeedMultiplier);

            // Start trail (only for stack 3)
             if (stackLevel == 1 && trailRenderer != null)
            {
                trailRenderer.emitting = true;
                trailRenderer.Clear();
            }

            if (stackLevel == 3 && trailRenderer != null)
            {
                trailRenderer.emitting = true;
                trailRenderer.Clear();
            }
            
            // Set animation locally
            if (animator != null)
            {
                animator.SetBool(IsSpeedBoostHash, true);
                Debug.Log($"✅ SPEEDBOOST animation SET TO TRUE (Stack {stackLevel})");
            }
            
            // Play VFX locally
            if (vfxController != null)
            {
                vfxController.Play();
            }
            
            // NETWORK SYNC: Tell other clients to play visuals
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlaySpeedBoostVisuals", RpcTarget.OthersBuffered, stackLevel);
            }
            
            Debug.Log($"Speed Boost activated! Stack: {stackLevel}, Multiplier: {currentSpeedMultiplier}x");
            
            return true;
        }
        
        private float GetSpeedMultiplierForStack()
        {
            switch (stackLevel)
            {
                case 1: return settings.SpeedBoostMultiplier;
                case 2: return settings.SpeedBoostMultiplier * 1.33f; // 2x speed
                case 3: return settings.SpeedBoostMultiplier * 1.67f; // 2.5x speed
                default: return settings.SpeedBoostMultiplier;
            }
        }
        
        private float GetDurationForStack()
        {
            switch (stackLevel)
            {
                case 1: return settings.SpeedBoostDuration;
                case 2: return settings.SpeedBoostDuration * 1.5f;
                case 3: return settings.SpeedBoostDuration * 1.3f;
                default: return settings.SpeedBoostDuration;
            }
        }
        
        public void Update()
        {
            if (cooldownTimer > 0f)
            {
                cooldownTimer -= Time.deltaTime;
            }
            
            if (isActive)
            {
                activeTimer += Time.deltaTime;
                
                float actualDuration = GetDurationForStack();
                float normalizedTime = activeTimer / actualDuration;
                
                // Apply speed curve (fade out effect)
                float curveValue = settings.SpeedBoostCurve.Evaluate(normalizedTime);
                currentSpeedMultiplier = GetSpeedMultiplierForStack() * curveValue;
                
                // Continuously update speed during boost
                OnSpeedMultiplierChanged?.Invoke(currentSpeedMultiplier);
                
                if (normalizedTime >= 1f)
                {
                    EndSpeedBoost();
                }
            }
        }
        
        private void EndSpeedBoost()
        {
            isActive = false;
            activeTimer = 0f;
            cooldownTimer = settings.SpeedBoostCooldown;
            
            // Reset speed multiplier
            currentSpeedMultiplier = 1f;
            OnSpeedMultiplierChanged?.Invoke(1f);
            
            // Stop trail
            if (trailRenderer != null) 
                trailRenderer.emitting = false;
            
            // Stop animation locally
            if (animator != null)
            {
                animator.SetBool(IsSpeedBoostHash, false);
                Debug.Log($"✅ SPEEDBOOST animation SET TO FALSE");
            }
            
            // Stop VFX locally
            if (vfxController != null)
            {
                vfxController.Stop();
            }
            
            // NETWORK SYNC: Tell other clients to stop visuals
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_StopSpeedBoostVisuals", RpcTarget.OthersBuffered);
            }
            
            Debug.Log($"Speed Boost ended. Cooldown: {cooldownTimer}s");
        }
        
        public void Cleanup()
        {
            if (trailRenderer != null)
            {
                Object.Destroy(trailRenderer.gameObject);
            }
            
            // Reset speed on cleanup
            currentSpeedMultiplier = 1f;
            OnSpeedMultiplierChanged?.Invoke(1f);
        }
    }
}