using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Hanzo.Player.Controllers
{
    [DefaultExecutionOrder(-1000)]
    public class BazookaRigController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private PlayerMovementController movementController;

        [Tooltip("Optional player Animator reference. It is not forced to be the Animator on the RigBuilder object.")]
        [SerializeField]
        private Animator animator;

        [SerializeField]
        private RigBuilder rigBuilder;

        [Header("Bazooka Arm/Hip Rigs")]
        [Tooltip("Assign the arm IK and hip alignment rigs that should blend on while the bazooka is active.")]
        [SerializeField]
        private Rig[] bazookaRigs;

        [SerializeField, Range(0f, 1f)]
        private float activeRigWeight = 1f;

        [SerializeField, Range(0f, 1f)]
        private float inactiveRigWeight = 0f;

        [SerializeField]
        private float rigBlendSpeed = 8f;

        [Tooltip("Keeps the RigBuilder graph fully disabled until the bazooka rig is actually needed.")]
        [SerializeField]
        private bool disableRigBuilderWhenInactive = true;

        [Tooltip("Enable in Play Mode to keep the bazooka rig active while positioning IK targets and the weapon.")]
        [SerializeField]
        private bool forceRigActiveForSetup = false;

        [Header("Hip Aim Offset")]
        [Tooltip("Optional Multi-Aim Constraint used to add a small hips/back adjustment while aiming the bazooka.")]
        [SerializeField]
        private MultiAimConstraint hipsAimConstraint;

        [SerializeField]
        private bool autoFindHipsAimConstraint = true;

        [Tooltip("Extra offset applied only while the bazooka rig is active.")]
        [SerializeField]
        private Vector3 activeHipsNeutralOffset = Vector3.zero;

        [Tooltip("Offset applied at full left/right bazooka aim. X/Y/Z are Multi-Aim offset degrees.")]
        [SerializeField]
        private Vector3 hipsOffsetFromYaw = new Vector3(0f, 14f, -4f);

        [Tooltip("Offset applied at full up/down bazooka aim. X/Y/Z are Multi-Aim offset degrees.")]
        [SerializeField]
        private Vector3 hipsOffsetFromPitch = new Vector3(-7f, 0f, 0f);

        [SerializeField]
        private float hipsOffsetBlendSpeed = 10f;

        [Header("Validation")]
        [SerializeField]
        private bool rebuildRigBuilderOnStart = false;

        [Tooltip("Rebuilds the Animation Rigging graph after re-enabling the bazooka rig.")]
        [SerializeField]
        private bool rebuildRigBuilderOnActivation = true;

        [Tooltip("Prevents Animation Rigging jobs from running when IK references cannot be resolved by the Animator.")]
        [SerializeField]
        private bool disableRigBuilderWhenInvalid = true;

        [Tooltip("Targets and hints used by Two Bone IK must be children of the RigBuilder Animator hierarchy.")]
        [SerializeField]
        private bool requireIkTargetsUnderAnimator = true;

        [SerializeField]
        private bool showDebugInfo = false;

        private readonly List<Rig> runtimeRigs = new List<Rig>();
        private bool isBazookaActive;
        private bool rigSetupValid;
        private float currentWeight;
        private bool subscribedToMovementController;
        private bool lastForceRigActiveForSetup;
        private Animator rigBuilderAnimator;
        private bool rigSystemEnabled = true;
        private bool hasCachedHipsAimOffset;
        private Vector3 defaultHipsAimOffset;
        private Vector3 currentHipsAimOffset;

        private void Awake()
        {
            CacheReferences();
            isBazookaActive = movementController != null && movementController.IsBazookaHolding;
            InitializeRigState(false);
            lastForceRigActiveForSetup = forceRigActiveForSetup;
        }

        private void OnEnable()
        {
            CacheReferences();
            SubscribeToMovementController();

            isBazookaActive = movementController != null && movementController.IsBazookaHolding;
            InitializeRigState(true);
            lastForceRigActiveForSetup = forceRigActiveForSetup;
        }

        private void Start()
        {
            if (!ShouldRigBeActive())
            {
                DeactivateRigSystem();
                return;
            }

            if (rebuildRigBuilderOnStart)
            {
                RebuildRigBuilder(false);
            }
        }

        private void OnDisable()
        {
            DeactivateRigSystem();
            UnsubscribeFromMovementController();
            RestoreHipsAimOffset();
        }

        private void OnValidate()
        {
            activeRigWeight = Mathf.Clamp01(activeRigWeight);
            inactiveRigWeight = Mathf.Clamp01(inactiveRigWeight);
            rigBlendSpeed = Mathf.Max(0f, rigBlendSpeed);
            hipsOffsetBlendSpeed = Mathf.Max(0f, hipsOffsetBlendSpeed);

            if (!Application.isPlaying)
            {
                hasCachedHipsAimOffset = false;
                CacheReferences();
                CollectRuntimeRigs();
                ApplyRigWeight(forceRigActiveForSetup ? activeRigWeight : inactiveRigWeight);
            }
        }

        private void Update()
        {
            if (forceRigActiveForSetup != lastForceRigActiveForSetup)
            {
                lastForceRigActiveForSetup = forceRigActiveForSetup;

                if (ShouldRigBeActive())
                {
                    ActivateRigSystem(true, rebuildRigBuilderOnActivation);
                }
                else
                {
                    DeactivateRigSystem();
                }
            }

            if (!ShouldRigBeActive())
            {
                DeactivateRigSystem();
                return;
            }

            if (!rigSetupValid)
                return;

            float targetWeight = GetTargetRigWeight();
            currentWeight = rigBlendSpeed <= 0f
                ? targetWeight
                : Mathf.MoveTowards(currentWeight, targetWeight, rigBlendSpeed * Time.deltaTime);

            ApplyRigWeight(currentWeight);
            UpdateHipsAimOffset(Time.deltaTime);
        }

        [ContextMenu("Validate Rig Setup")]
        public void ValidateRigSetupFromContext()
        {
            CacheReferences();
            rigSetupValid = ValidateRigSetup(true);
            ConfigureRigBuilderForValidity();
        }

        [ContextMenu("Rebuild Rig Builder")]
        public void RebuildRigBuilder()
        {
            RebuildRigBuilder(true);
        }

        [ContextMenu("Force Rig On For Setup")]
        public void ForceRigOnForSetup()
        {
            forceRigActiveForSetup = true;
            lastForceRigActiveForSetup = forceRigActiveForSetup;
            currentWeight = activeRigWeight;
            ActivateRigSystem(true, rebuildRigBuilderOnActivation);
            ApplyRigWeight(currentWeight);
        }

        [ContextMenu("Force Rig Off For Setup")]
        public void ForceRigOffForSetup()
        {
            forceRigActiveForSetup = false;
            lastForceRigActiveForSetup = forceRigActiveForSetup;
            DeactivateRigSystem();
        }

        private void HandleBazookaHoldChanged(bool active)
        {
            isBazookaActive = active;

            if (active)
            {
                ActivateRigSystem(true, rebuildRigBuilderOnActivation);
            }
            else
            {
                DeactivateRigSystem();
            }
        }

        private void RebuildRigBuilder(bool logWarnings)
        {
            CacheReferences();

            if (!ShouldRigBeActive())
            {
                DeactivateRigSystem();
                return;
            }

            SetRigLayersActive(true);
            rigSetupValid = ValidateRigSetup(logWarnings);
            ConfigureRigBuilderForValidity();

            if (!rigSetupValid || rigBuilder == null || !Application.isPlaying)
                return;

            RebuildRigBuilderGraph(logWarnings);
            ApplyRigWeight(currentWeight);
        }

        private void RebuildRigBuilderGraph(bool logWarnings)
        {
            if (rigBuilder == null || !Application.isPlaying)
                return;

            rigBuilder.Clear();

            if (!rigBuilder.Build() && logWarnings)
            {
                Debug.LogWarning($"{nameof(BazookaRigController)} on {name}: RigBuilder could not build the bazooka rig graph.", this);
            }
        }

        private void InitializeRigState(bool logWarnings)
        {
            currentWeight = GetTargetRigWeight();

            if (ShouldRigBeActive())
            {
                ActivateRigSystem(logWarnings, false);
                ApplyRigWeight(currentWeight);
                return;
            }

            DeactivateRigSystem();
        }

        private void CacheReferences()
        {
            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();

                if (movementController == null)
                {
                    movementController = GetComponentInParent<PlayerMovementController>();
                }
            }

            if (rigBuilder == null)
            {
                rigBuilder = GetComponent<RigBuilder>();

                if (rigBuilder == null)
                {
                    rigBuilder = GetComponentInChildren<RigBuilder>(true);
                }

                if (rigBuilder == null && movementController != null)
                {
                    rigBuilder = movementController.GetComponentInChildren<RigBuilder>(true);
                }
            }

            rigBuilderAnimator = rigBuilder != null ? rigBuilder.GetComponent<Animator>() : null;

            if (animator == null)
            {
                if (movementController != null)
                {
                    animator = movementController.Animator;
                }

                if (animator == null)
                {
                    animator = GetComponent<Animator>();
                }

                if (animator == null)
                {
                    animator = GetComponentInChildren<Animator>(true);
                }
            }

            if (rigBuilder == null && animator != null)
            {
                rigBuilder = animator.GetComponent<RigBuilder>();

                if (rigBuilder == null)
                {
                    rigBuilder = animator.GetComponentInChildren<RigBuilder>(true);
                }

                rigBuilderAnimator = rigBuilder != null ? rigBuilder.GetComponent<Animator>() : null;
            }

            CollectRuntimeRigs();
            CacheHipsAimConstraint();
            CacheHipsAimOffset();
        }

        private void CollectRuntimeRigs()
        {
            runtimeRigs.Clear();

            if (bazookaRigs != null)
            {
                for (int i = 0; i < bazookaRigs.Length; i++)
                {
                    AddRuntimeRig(bazookaRigs[i]);
                }
            }

            if (rigBuilder == null)
                return;

            List<RigLayer> layers = rigBuilder.layers;
            for (int i = 0; i < layers.Count; i++)
            {
                AddRuntimeRig(layers[i].rig);
            }
        }

        private void AddRuntimeRig(Rig rig)
        {
            if (rig == null || runtimeRigs.Contains(rig))
                return;

            runtimeRigs.Add(rig);
        }

        private void SubscribeToMovementController()
        {
            if (subscribedToMovementController || movementController == null)
                return;

            movementController.OnBazookaHoldChanged += HandleBazookaHoldChanged;
            subscribedToMovementController = true;
        }

        private void UnsubscribeFromMovementController()
        {
            if (!subscribedToMovementController)
                return;

            if (movementController != null)
            {
                movementController.OnBazookaHoldChanged -= HandleBazookaHoldChanged;
            }

            subscribedToMovementController = false;
        }

        private float GetTargetRigWeight()
        {
            return ShouldRigBeActive() ? activeRigWeight : inactiveRigWeight;
        }

        private bool ShouldRigBeActive()
        {
            return isBazookaActive || forceRigActiveForSetup;
        }

        private void ApplyRigWeight(float weight)
        {
            CollectRuntimeRigs();

            for (int i = 0; i < runtimeRigs.Count; i++)
            {
                if (runtimeRigs[i] != null)
                {
                    runtimeRigs[i].weight = weight;
                }
            }
        }

        private void ActivateRigSystem(bool logWarnings, bool rebuildGraph)
        {
            SetRigLayersActive(true);
            SetRigSystemEnabled(true);
            currentWeight = activeRigWeight;
            ApplyRigWeight(currentWeight);

            rigSetupValid = ValidateRigSetup(logWarnings);
            ConfigureRigBuilderForValidity();

            if (!rigSetupValid)
                return;

            if (rebuildGraph || disableRigBuilderWhenInactive)
            {
                RebuildRigBuilderGraph(logWarnings);
                ApplyRigWeight(currentWeight);
            }
        }

        private void DeactivateRigSystem()
        {
            if (disableRigBuilderWhenInactive
                && Application.isPlaying
                && !rigSystemEnabled
                && Mathf.Approximately(currentWeight, inactiveRigWeight))
            {
                return;
            }

            currentWeight = inactiveRigWeight;
            ApplyRigWeight(currentWeight);
            SetRigLayersActive(false);
            RestoreHipsAimOffset();

            if (!disableRigBuilderWhenInactive || !Application.isPlaying)
                return;

            rigSetupValid = false;
            SetRigSystemEnabled(false);
        }

        private void SetRigSystemEnabled(bool enabled)
        {
            CollectRuntimeRigs();

            for (int i = 0; i < runtimeRigs.Count; i++)
            {
                if (runtimeRigs[i] != null && runtimeRigs[i].enabled != enabled)
                {
                    runtimeRigs[i].enabled = enabled;
                }
            }

            if (rigBuilder != null)
            {
                if (enabled)
                {
                    if (!rigBuilder.enabled)
                    {
                        rigBuilder.enabled = true;
                    }
                }
                else if (rigBuilder.enabled)
                {
                    if (Application.isPlaying)
                    {
                        rigBuilder.Clear();
                    }

                    rigBuilder.enabled = false;
                }
            }

            rigSystemEnabled = enabled;
        }

        private void SetRigLayersActive(bool active)
        {
            if (rigBuilder == null)
                return;

            List<RigLayer> layers = rigBuilder.layers;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null)
                {
                    layers[i].active = active;
                }
            }
        }

        private void CacheHipsAimConstraint()
        {
            if (!autoFindHipsAimConstraint || hipsAimConstraint != null)
                return;

            Transform searchRoot = rigBuilder != null ? rigBuilder.transform : transform;
            MultiAimConstraint[] constraints = searchRoot.GetComponentsInChildren<MultiAimConstraint>(true);
            if (constraints.Length == 0)
                return;

            for (int i = 0; i < constraints.Length; i++)
            {
                MultiAimConstraint candidate = constraints[i];
                if (candidate != null && candidate.name.ToLowerInvariant().Contains("hip"))
                {
                    hipsAimConstraint = candidate;
                    return;
                }
            }

            hipsAimConstraint = constraints[0];
        }

        private void CacheHipsAimOffset()
        {
            if (hipsAimConstraint == null || hasCachedHipsAimOffset)
                return;

            defaultHipsAimOffset = hipsAimConstraint.data.offset;
            currentHipsAimOffset = defaultHipsAimOffset;
            hasCachedHipsAimOffset = true;
        }

        private void UpdateHipsAimOffset(float deltaTime)
        {
            if (hipsAimConstraint == null)
                return;

            CacheHipsAimOffset();

            Vector3 targetOffset = defaultHipsAimOffset;
            bool shouldApplyAimOffset = isBazookaActive || forceRigActiveForSetup;

            if (shouldApplyAimOffset)
            {
                Vector2 aim = movementController != null ? movementController.NormalizedBazookaAim : Vector2.zero;
                targetOffset += activeHipsNeutralOffset;
                targetOffset += hipsOffsetFromYaw * aim.x;
                targetOffset += hipsOffsetFromPitch * aim.y;
            }

            float blend = hipsOffsetBlendSpeed <= 0f
                ? 1f
                : 1f - Mathf.Exp(-hipsOffsetBlendSpeed * deltaTime);

            currentHipsAimOffset = Vector3.Lerp(currentHipsAimOffset, targetOffset, blend);
            ApplyHipsAimOffset(currentHipsAimOffset);
        }

        private void ApplyHipsAimOffset(Vector3 offset)
        {
            if (hipsAimConstraint == null)
                return;

            MultiAimConstraintData data = hipsAimConstraint.data;
            data.offset = offset;
            hipsAimConstraint.data = data;
        }

        private void RestoreHipsAimOffset()
        {
            if (!hasCachedHipsAimOffset || hipsAimConstraint == null)
                return;

            currentHipsAimOffset = defaultHipsAimOffset;
            ApplyHipsAimOffset(defaultHipsAimOffset);
        }

        private void ConfigureRigBuilderForValidity()
        {
            if (rigBuilder == null)
                return;

            if (!ShouldRigBeActive())
            {
                SetRigSystemEnabled(false);
                return;
            }

            if (!rigSetupValid && disableRigBuilderWhenInvalid)
            {
                if (Application.isPlaying)
                {
                    rigBuilder.Clear();
                }

                SetRigSystemEnabled(false);
                return;
            }

            SetRigSystemEnabled(true);
        }

        private bool ValidateRigSetup(bool logWarnings)
        {
            CollectRuntimeRigs();

            bool valid = true;
            Animator bindingAnimator = GetRigBuilderAnimator();

            if (movementController == null)
            {
                Warn(logWarnings, "No PlayerMovementController is assigned or found.");
            }

            if (rigBuilder == null)
            {
                Warn(logWarnings, "No RigBuilder is assigned or found.");
                return false;
            }

            if (bindingAnimator == null)
            {
                Warn(logWarnings, "The RigBuilder does not have an Animator on its GameObject. Unity's RigBuilder requires that Animator for binding.");
                return false;
            }

            if (runtimeRigs.Count == 0)
            {
                Warn(logWarnings, "No bazooka rigs are assigned, and no rigs were found on the RigBuilder layers.");
                valid = false;
            }

            for (int i = 0; i < runtimeRigs.Count; i++)
            {
                Rig rig = runtimeRigs[i];

                if (rig == null)
                {
                    Warn(logWarnings, "A bazooka rig slot is empty.");
                    valid = false;
                    continue;
                }

                if (!rig.transform.IsChildOf(bindingAnimator.transform))
                {
                    Warn(logWarnings, $"Rig '{rig.name}' is outside the RigBuilder Animator hierarchy. Move the rig under '{bindingAnimator.name}'.");
                    valid = false;
                }

                valid &= ValidateRigConstraints(rig, logWarnings);
            }

            if (valid && showDebugInfo)
            {
                Debug.Log($"{nameof(BazookaRigController)} on {name}: bazooka rig setup is valid.", this);
            }

            return valid;
        }

        private bool ValidateRigConstraints(Rig rig, bool logWarnings)
        {
            bool valid = true;
            IRigConstraint[] constraints = rig.GetComponentsInChildren<IRigConstraint>();

            if (constraints.Length == 0)
            {
                Warn(logWarnings, $"Rig '{rig.name}' has no valid active constraint components under it.");
                return false;
            }

            for (int i = 0; i < constraints.Length; i++)
            {
                IRigConstraint constraint = constraints[i];

                if (constraint == null)
                    continue;

                Component component = constraint.component;

                if (!constraint.IsValid())
                {
                    Warn(logWarnings, $"Constraint '{GetComponentPath(component)}' has incomplete or invalid data.");
                    valid = false;
                }

                if (component is TwoBoneIKConstraint twoBoneIk)
                {
                    valid &= ValidateTwoBoneIkConstraint(twoBoneIk, logWarnings);
                }
            }

            return valid;
        }

        private bool ValidateTwoBoneIkConstraint(TwoBoneIKConstraint constraint, bool logWarnings)
        {
            bool valid = true;
            TwoBoneIKConstraintData data = constraint.data;
            string constraintPath = GetComponentPath(constraint);

            valid &= ValidateRequiredIkTransform(data.root, "Root", constraintPath, logWarnings);
            valid &= ValidateRequiredIkTransform(data.mid, "Mid", constraintPath, logWarnings);
            valid &= ValidateRequiredIkTransform(data.tip, "Tip", constraintPath, logWarnings);
            valid &= ValidateRequiredIkTransform(data.target, "Target", constraintPath, logWarnings);

            if (data.hint != null)
            {
                valid &= ValidateIkTransformUnderAnimator(data.hint, "Hint", constraintPath, logWarnings);
            }

            if (data.root != null && data.mid != null && !data.mid.IsChildOf(data.root))
            {
                Warn(logWarnings, $"Constraint '{constraintPath}' has a Mid transform that is not below Root.");
                valid = false;
            }

            if (data.mid != null && data.tip != null && !data.tip.IsChildOf(data.mid))
            {
                Warn(logWarnings, $"Constraint '{constraintPath}' has a Tip transform that is not below Mid.");
                valid = false;
            }

            return valid;
        }

        private bool ValidateRequiredIkTransform(Transform value, string label, string constraintPath, bool logWarnings)
        {
            if (value == null)
            {
                Warn(logWarnings, $"Constraint '{constraintPath}' is missing its {label} transform.");
                return false;
            }

            return ValidateIkTransformUnderAnimator(value, label, constraintPath, logWarnings);
        }

        private bool ValidateIkTransformUnderAnimator(Transform value, string label, string constraintPath, bool logWarnings)
        {
            Animator bindingAnimator = GetRigBuilderAnimator();

            if (bindingAnimator == null || value == null)
                return false;

            if (!requireIkTargetsUnderAnimator && (label == "Target" || label == "Hint"))
                return true;

            if (value.IsChildOf(bindingAnimator.transform))
                return true;

            Warn(logWarnings, $"Constraint '{constraintPath}' {label} transform '{value.name}' is outside the RigBuilder Animator hierarchy. Move it under '{bindingAnimator.name}' so the Animation Rigging job can resolve it.");
            return false;
        }

        private string GetComponentPath(Component component)
        {
            if (component == null)
                return "<missing component>";

            Animator bindingAnimator = GetRigBuilderAnimator();
            Transform current = component.transform;
            string path = current.name;

            while (current.parent != null && (bindingAnimator == null || current.parent != bindingAnimator.transform))
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        private Animator GetRigBuilderAnimator()
        {
            if (rigBuilder == null)
                return animator;

            if (rigBuilderAnimator == null)
            {
                rigBuilderAnimator = rigBuilder.GetComponent<Animator>();
            }

            return rigBuilderAnimator != null ? rigBuilderAnimator : animator;
        }

        private void Warn(bool shouldLog, string message)
        {
            if (!shouldLog)
                return;

            Debug.LogWarning($"{nameof(BazookaRigController)} on {name}: {message}", this);
        }
    }
}
