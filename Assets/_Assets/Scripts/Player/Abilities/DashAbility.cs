using UnityEngine;
using Hanzo.Core.Interfaces;
using Hanzo.VFX;
using Photon.Pun;

namespace Hanzo.Player.Abilities
{
    public class DashAbility : IAbility
    {
        private IMovementController controller;
        private AbilitySettings settings;
        private TrailRenderer trailRenderer;
        private Animator animator;
        private DashVFXController vfxController;
        private PhotonView photonView;
        
        private bool isActive;
        private float dashTimer;
        private float cooldownTimer;
        private Vector3 dashDirection;
        
        // STACKING SYSTEM
        private int stackLevel = 1;
        private int chainDashesRemaining = 0;
        private float chainDashWindow = 0.5f;
        private float chainDashTimer = 0f;
        
        private static readonly int IsDashingHash = Animator.StringToHash("DASH");
        private static readonly int IsRunningHash = Animator.StringToHash("RUN");
        
        public string AbilityName => "Dash";
        public bool CanActivate => !isActive && (cooldownTimer <= 0f || chainDashesRemaining > 0);
        public bool IsActive => isActive;
        public float CooldownRemaining => Mathf.Max(0f, cooldownTimer);
        public int StackLevel => stackLevel;
        
        public DashAbility(AbilitySettings abilitySettings)
        {
            settings = abilitySettings;
        }

        public void SetVFXController(DashVFXController vfx)
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
                Debug.LogError("DashAbility: No Animator found on player!");
            }
            else
            {
                Debug.Log($"DashAbility: Animator found. Has DASH parameter? {HasParameter(animator, "DASH")}");
            }

            if (vfxController == null && controller.Transform != null)
            {
                vfxController = controller.Transform.GetComponentInChildren<DashVFXController>(true);
            }
            
            SetupTrailRenderer();
        }
        
        private bool HasParameter(Animator anim, string paramName)
        {
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
        }
        
        private void SetupTrailRenderer()
        {
            GameObject trailObject = new GameObject("DashTrail");
            trailObject.transform.SetParent(controller.Transform, false);
            trailObject.transform.localPosition = Vector3.zero;
            
            trailRenderer = trailObject.AddComponent<TrailRenderer>();
            trailRenderer.time = settings.TrailTime;
            trailRenderer.startWidth = settings.TrailWidth;
            trailRenderer.endWidth = 0f;
            trailRenderer.colorGradient = settings.TrailColor;
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
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.cyan);
            return mat;
        }
        
        public void AddStack()
        {
            if (stackLevel < 3)
            {
                stackLevel++;
                Debug.Log($"Dash stack increased to level {stackLevel}");
                UpdateTrailForStack();
            }
        }
        
        public void ResetStacks()
        {
            stackLevel = 1;
            chainDashesRemaining = 0;
            UpdateTrailForStack();
        }
        
        private void UpdateTrailForStack()
        {
            if (trailRenderer == null) return;
            
            switch (stackLevel)
            {
                case 1:
                    trailRenderer.time = settings.TrailTime;
                    trailRenderer.startWidth = settings.TrailWidth;
                    break;
                case 2:
                    trailRenderer.time = settings.TrailTime * 1.3f;
                    trailRenderer.startWidth = settings.TrailWidth * 1.2f;
                    break;
                case 3:
                    trailRenderer.time = settings.TrailTime * 1.5f;
                    trailRenderer.startWidth = settings.TrailWidth * 1.4f;
                    break;
            }
        }
        
        public bool TryActivate()
        {
            Debug.Log($"DashAbility.TryActivate - Stack: {stackLevel}, ChainRemaining: {chainDashesRemaining}");
            
            if (isActive) return false;
            
            bool canDashNormally = cooldownTimer <= 0f;
            bool canChainDash = chainDashesRemaining > 0 && chainDashTimer > 0f;
            
            if (!canDashNormally && !canChainDash) return false;
            
            // Get dash direction
            Vector3 horizontalVelocity = new Vector3(controller.Velocity.x, 0, controller.Velocity.z);
            
            if (horizontalVelocity.magnitude > 0.1f)
            {
                dashDirection = horizontalVelocity.normalized;
            }
            else
            {
                dashDirection = controller.Transform.forward;
            }
            
            // Consume chain dash if using it
            if (canChainDash)
            {
                chainDashesRemaining--;
                Debug.Log($"Chain dash used! Remaining: {chainDashesRemaining}");
            }
            
            isActive = true;
            dashTimer = 0f;
            
            // Start trail locally
            if (trailRenderer != null)
            {
                trailRenderer.emitting = true;
                trailRenderer.Clear();
            }
            
            // Set animation locally
            if (animator != null)
            {
                animator.SetBool(IsDashingHash, true);
                Debug.Log($"✅ DASH animation SET TO TRUE");
            }
            
            // Play VFX locally
            if (vfxController != null)
            {
                vfxController.Play();
            }
            
            // NETWORK SYNC: Tell other clients to play dash visuals (only if connected)
            if (photonView != null && photonView.IsMine && PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_PlayDashVisuals", RpcTarget.OthersBuffered);
            }
            
            return true;
        }
        
        public void Update()
        {
            if (cooldownTimer > 0f)
            {
                cooldownTimer -= Time.deltaTime;
            }
            
            if (chainDashesRemaining > 0)
            {
                chainDashTimer -= Time.deltaTime;
                if (chainDashTimer <= 0f)
                {
                    chainDashesRemaining = 0;
                    Debug.Log("Chain dash window expired");
                }
            }
            
            if (isActive)
            {
                dashTimer += Time.deltaTime;
                
                float actualDuration = GetDashDuration();
                float normalizedTime = dashTimer / actualDuration;
                
                if (normalizedTime >= 1f)
                {
                    EndDash();
                    return;
                }
                
                // Only local player controls movement
                if (photonView == null || photonView.IsMine)
                {
                    float curveValue = settings.DashSpeedCurve.Evaluate(normalizedTime);
                    float speedMultiplier = GetSpeedMultiplier();
                    Vector3 dashVelocity = dashDirection * (settings.DashSpeed * speedMultiplier * curveValue);
                    dashVelocity.y = controller.Velocity.y;
                    
                    controller.SetVelocity(dashVelocity);
                }
            }
        }
        
        private float GetDashDuration()
        {
            return stackLevel == 2 ? settings.DashDuration * 1.4f : settings.DashDuration;
        }
        
        private float GetSpeedMultiplier()
        {
            return stackLevel == 2 ? 1.5f : 1f;
        }
        
        private void EndDash()
        {
            isActive = false;
            cooldownTimer = settings.DashCooldown;
            
            if (stackLevel == 3 && chainDashesRemaining == 0)
            {
                chainDashesRemaining = 1;
                chainDashTimer = chainDashWindow;
                Debug.Log("Chain dash ready! Press dash again within 0.5s");
            }
            
            // Stop trail locally
            if (trailRenderer != null) 
                trailRenderer.emitting = false;
            
            // Stop animation locally
            if (animator != null)
            {
                animator.SetBool(IsDashingHash, false);
                Debug.Log($"✅ DASH animation SET TO FALSE");
            }
            
            // NETWORK SYNC: Tell other clients to stop dash visuals (only if connected)
            if (photonView != null && photonView.IsMine && PhotonNetwork.IsConnected)
            {
                photonView.RPC("RPC_StopDashVisuals", RpcTarget.OthersBuffered);
            }
            
            Debug.Log($"Dash ended. Cooldown: {cooldownTimer}s, Stack: {stackLevel}");
        }
        
        public void Cleanup()
        {
            if (trailRenderer != null)
            {
                Object.Destroy(trailRenderer.gameObject);
            }
        }
    }
}