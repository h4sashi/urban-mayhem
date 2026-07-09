using Cinemachine;
using UnityEngine;

namespace Tarodev {

    public class Missile : MonoBehaviour {
        [Header("REFERENCES")]
        [SerializeField] private Rigidbody _rb;
        [SerializeField] private Target _target;
        [SerializeField] private GameObject _explosionPrefab;
        [SerializeField] private ParticleSystem missileParticleFX;

        [Header("MOVEMENT")]
        [SerializeField] private float _speed = 15;
        [SerializeField] private float _rotateSpeed = 95;

        [Header("FORWARD LAUNCH")]
        [SerializeField] private bool _forwardLaunchBeforeVertical = true;
        [SerializeField] private float _forwardLaunchDistance = 1.5f;
        [SerializeField] private float _forwardLaunchSpeed = 15;
        [SerializeField] private float _forwardLaunchCompletionDistance = 0;

        [Header("VERTICAL LAUNCH")]
        [SerializeField] private bool _verticalLaunchBeforeHoming = true;
        [SerializeField] private float _verticalLaunchHeight = 7;
        [SerializeField] private float _verticalLaunchSpeed = 15;
        [SerializeField] private float _verticalLaunchCompletionDistance = 0;

        [Header("CAMERA")]
        [Tooltip("Optional. Assign this manually to enable missile-cam takeover. Leave empty to keep the normal player camera flow.")]
        [SerializeField] private CinemachineVirtualCamera _missileCamera;
        [SerializeField] private int _missileCameraActivePriority = 500;
        [SerializeField] private int _missileCameraInactivePriority = -10;
        [SerializeField] private bool _disableMissileCameraObjectWhenInactive = true;

        [Header("TARGETING")]
        [SerializeField] private bool _autoAcquireTarget = true;
        [SerializeField] private float _targetAcquireRadius = 35;
        [SerializeField] private LayerMask _targetAcquireLayerMask = ~0;
        [SerializeField] private float _targetReacquireInterval = 0.25f;

        [Header("PREDICTION")]
        [SerializeField] private float _maxDistancePredict = 100;
        [SerializeField] private float _minDistancePredict = 5;
        [SerializeField] private float _maxTimePrediction = 5;
        private Vector3 _standardPrediction, _deviatedPrediction;

        [Header("DEVIATION")]
        [SerializeField] private float _deviationAmount = 50;
        [SerializeField] private float _deviationSpeed = 2;

        private const int MaxTargetQueryResults = 32;
        private readonly Collider[] _targetQueryBuffer = new Collider[MaxTargetQueryResults];
        private Transform _ownerRoot;
        private float _nextTargetAcquireTime;
        private FlightPhase _flightPhase = FlightPhase.Homing;
        private Vector3 _forwardLaunchStartPosition;
        private Vector3 _forwardLaunchDirection = Vector3.forward;
        private float _verticalLaunchTargetY;
        private Vector3 _fallbackHomingDirection = Vector3.forward;
        private bool _missileCameraActive;

        public Target CurrentTarget => _target;

        private enum FlightPhase {
            Homing,
            ForwardLaunch,
            VerticalLaunch
        }

        public void Launch(
            Target target,
            Vector3 launchDirection,
            float launchSpeed,
            Transform ownerRoot = null,
            bool activateMissileCamera = false
        ) {
            CacheRigidbody();

            _ownerRoot = ownerRoot != null ? ownerRoot.root : null;
            _target = target;
            _fallbackHomingDirection = launchDirection.sqrMagnitude > Mathf.Epsilon
                ? launchDirection.normalized
                : transform.forward;
            IgnoreOwnerCollisions();

            if (launchSpeed > 0)
                _speed = launchSpeed;

            PlayMissileParticleFX();
            SetMissileCameraActive(activateMissileCamera);

            _flightPhase = FlightPhase.Homing;

            if (_rb != null) {
                PrepareRigidbodyForLaunch();

                if (ShouldUseForwardLaunch()) {
                    BeginForwardLaunch();
                } else if (ShouldUseVerticalLaunch()) {
                    BeginVerticalLaunch();
                } else {
                    SetRigidbodyVelocity(_fallbackHomingDirection * _speed);
                }
            }

            if (_flightPhase == FlightPhase.Homing && _target == null && _autoAcquireTarget)
                AcquireClosestTarget();
        }

        public void SetTarget(Target target) => _target = target;

        private void Awake() {
            CacheRigidbody();
            SetMissileCameraActive(false);
        }

        private void OnEnable() {
            SetMissileCameraActive(false);
        }

        private void Start() {
            if (_flightPhase == FlightPhase.Homing && _target == null && _autoAcquireTarget)
                AcquireClosestTarget();
        }

        private void OnValidate() {
            CacheRigidbody();
        }

        private void FixedUpdate() {
            CacheRigidbody();
            if (_rb == null)
                return;

            switch (_flightPhase) {
                case FlightPhase.ForwardLaunch:
                    UpdateForwardLaunch();
                    return;
                case FlightPhase.VerticalLaunch:
                    UpdateVerticalLaunch();
                    return;
            }

            UpdateHomingFlight();
        }

        private void UpdateForwardLaunch() {
            if (GetForwardLaunchDistanceTraveled() >= _forwardLaunchDistance - _forwardLaunchCompletionDistance) {
                CompleteForwardLaunch();
                return;
            }

            ApplyForwardLaunchVelocity();
        }

        private void UpdateVerticalLaunch() {
            if (_rb.position.y >= _verticalLaunchTargetY - _verticalLaunchCompletionDistance) {
                CompleteVerticalLaunch();
                return;
            }

            ApplyVerticalLaunchVelocity();
        }

        private void UpdateHomingFlight() {
            SetRigidbodyVelocity(transform.forward * _speed);

            if (_target == null) {
                TryReacquireTarget();
                return;
            }

            var leadTimePercentage = Mathf.InverseLerp(_minDistancePredict, _maxDistancePredict, Vector3.Distance(transform.position, _target.Position));

            PredictMovement(leadTimePercentage);

            AddDeviation(leadTimePercentage);

            RotateRocket();
        }

        private bool ShouldUseForwardLaunch() => _forwardLaunchBeforeVertical && _forwardLaunchDistance > 0;

        private bool ShouldUseVerticalLaunch() => _verticalLaunchBeforeHoming && _verticalLaunchHeight > 0;

        private void BeginForwardLaunch() {
            _flightPhase = FlightPhase.ForwardLaunch;
            _forwardLaunchStartPosition = _rb.position;
            _forwardLaunchDirection = _fallbackHomingDirection.sqrMagnitude > Mathf.Epsilon
                ? _fallbackHomingDirection
                : transform.forward;
            _forwardLaunchDirection.Normalize();
            ApplyForwardLaunchVelocity();
        }

        private void ApplyForwardLaunchVelocity() {
            float forwardSpeed = _forwardLaunchSpeed > 0 ? _forwardLaunchSpeed : _speed;
            if (forwardSpeed <= Mathf.Epsilon) {
                CompleteForwardLaunch();
                return;
            }

            float remainingDistance = Mathf.Max(0, _forwardLaunchDistance - GetForwardLaunchDistanceTraveled());
            float cappedSpeed = Time.fixedDeltaTime > Mathf.Epsilon
                ? Mathf.Min(forwardSpeed, remainingDistance / Time.fixedDeltaTime)
                : forwardSpeed;

            SetRigidbodyVelocity(_forwardLaunchDirection * cappedSpeed);
            _rb.angularVelocity = Vector3.zero;
            _rb.MoveRotation(CreateLookRotation(_forwardLaunchDirection));
        }

        private float GetForwardLaunchDistanceTraveled() {
            return Vector3.Dot(_rb.position - _forwardLaunchStartPosition, _forwardLaunchDirection);
        }

        private void CompleteForwardLaunch() {
            if (ShouldUseVerticalLaunch()) {
                BeginVerticalLaunch();
                return;
            }

            BeginHoming();
        }

        private void BeginVerticalLaunch() {
            _flightPhase = FlightPhase.VerticalLaunch;
            _verticalLaunchTargetY = _rb.position.y + _verticalLaunchHeight;
            ApplyVerticalLaunchVelocity();
        }

        private void ApplyVerticalLaunchVelocity() {
            float climbSpeed = _verticalLaunchSpeed > 0 ? _verticalLaunchSpeed : _speed;
            if (climbSpeed <= Mathf.Epsilon) {
                CompleteVerticalLaunch();
                return;
            }

            float remainingHeight = Mathf.Max(0, _verticalLaunchTargetY - _rb.position.y);
            float cappedSpeed = Time.fixedDeltaTime > Mathf.Epsilon
                ? Mathf.Min(climbSpeed, remainingHeight / Time.fixedDeltaTime)
                : climbSpeed;

            SetRigidbodyVelocity(Vector3.up * cappedSpeed);
            _rb.angularVelocity = Vector3.zero;
            _rb.MoveRotation(CreateLookRotation(Vector3.up));
        }

        private void CompleteVerticalLaunch() {
            BeginHoming();
        }

        private void BeginHoming() {
            _flightPhase = FlightPhase.Homing;

            if (_target == null && _autoAcquireTarget)
                AcquireClosestTarget();

            Vector3 initialHomingDirection = GetInitialHomingDirection();
            if (initialHomingDirection.sqrMagnitude <= Mathf.Epsilon)
                return;

            initialHomingDirection.Normalize();
            SetRigidbodyVelocity(initialHomingDirection * _speed);
            _rb.MoveRotation(CreateLookRotation(initialHomingDirection));
        }

        private Vector3 GetInitialHomingDirection() {
            if (_target != null)
                return _target.Position - _rb.position;

            return _fallbackHomingDirection.sqrMagnitude > Mathf.Epsilon
                ? _fallbackHomingDirection
                : transform.forward;
        }

        private Quaternion CreateLookRotation(Vector3 direction) {
            Vector3 normalizedDirection = direction.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;

            return Quaternion.LookRotation(normalizedDirection, up);
        }

        private void PredictMovement(float leadTimePercentage) {
            var predictionTime = Mathf.Lerp(0, _maxTimePrediction, leadTimePercentage);

            _standardPrediction = _target.Position + _target.Velocity * predictionTime;
        }

        private void AddDeviation(float leadTimePercentage) {
            var deviation = new Vector3(Mathf.Cos(Time.time * _deviationSpeed), 0, 0);

            var predictionOffset = transform.TransformDirection(deviation) * _deviationAmount * leadTimePercentage;

            _deviatedPrediction = _standardPrediction + predictionOffset;
        }

        private void RotateRocket() {
            var heading = _deviatedPrediction - transform.position;
            if (heading.sqrMagnitude <= Mathf.Epsilon)
                return;

            var rotation = CreateLookRotation(heading);
            _rb.MoveRotation(Quaternion.RotateTowards(transform.rotation, rotation, _rotateSpeed * Time.deltaTime));
        }

        private void TryReacquireTarget() {
            if (!_autoAcquireTarget || Time.time < _nextTargetAcquireTime)
                return;

            AcquireClosestTarget();
        }

        private void AcquireClosestTarget() {
            _nextTargetAcquireTime = Time.time + _targetReacquireInterval;

            var closestDistance = Mathf.Infinity;
            Target closestTarget = null;

            int targetCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                _targetAcquireRadius,
                _targetQueryBuffer,
                _targetAcquireLayerMask,
                QueryTriggerInteraction.Collide
            );

            for (int i = 0; i < targetCount; i++) {
                var col = _targetQueryBuffer[i];
                if (col == null)
                    continue;

                var candidate = col.GetComponentInParent<Target>();
                if (candidate == null || !IsValidTarget(candidate))
                    continue;

                var sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance >= closestDistance)
                    continue;

                closestDistance = sqrDistance;
                closestTarget = candidate;
            }

            if (closestTarget == null)
                closestTarget = FindClosestTargetByComponent();

            _target = closestTarget;
        }

        private Target FindClosestTargetByComponent() {
            var closestDistance = _targetAcquireRadius * _targetAcquireRadius;
            Target closestTarget = null;
            var targets = FindSceneTargets();

            for (int i = 0; i < targets.Length; i++) {
                var candidate = targets[i];
                if (!IsValidTarget(candidate))
                    continue;

                var sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance >= closestDistance)
                    continue;

                closestDistance = sqrDistance;
                closestTarget = candidate;
            }

            return closestTarget;
        }

        private Target[] FindSceneTargets() {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Target>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<Target>();
#endif
        }

        private bool IsValidTarget(Target candidate) {
            if (candidate == null)
                return false;

            if (_ownerRoot != null && candidate.transform.root == _ownerRoot)
                return false;

            return true;
        }

        private void CacheRigidbody() {
            if (_rb != null)
                return;

            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = GetComponentInParent<Rigidbody>();
            if (_rb == null)
                _rb = GetComponentInChildren<Rigidbody>();
        }

        private void SetMissileCameraActive(bool active) {
            if (_missileCamera == null) {
                _missileCameraActive = false;
                return;
            }

            _missileCameraActive = active;
            if (_disableMissileCameraObjectWhenInactive && _missileCamera.gameObject.activeSelf != active)
                _missileCamera.gameObject.SetActive(active);

            _missileCamera.Priority = active ? _missileCameraActivePriority : _missileCameraInactivePriority;
            _missileCamera.PreviousStateIsValid = false;
        }

        private void PlayMissileParticleFX() {
            if (missileParticleFX == null)
                return;

            if (!missileParticleFX.gameObject.activeSelf)
                missileParticleFX.gameObject.SetActive(true);

            ParticleSystem[] particleSystems = missileParticleFX.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++) {
                ParticleSystem particle = particleSystems[i];
                if (particle == null)
                    continue;

                if (!particle.gameObject.activeSelf)
                    particle.gameObject.SetActive(true);

                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
            }
        }

        private void PrepareRigidbodyForLaunch() {
            _rb.isKinematic = false;
            _rb.useGravity = false;
            _rb.detectCollisions = true;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.WakeUp();
        }

        private void SetRigidbodyVelocity(Vector3 velocity) {
            _rb.velocity = velocity;
            _rb.WakeUp();
        }

        private void IgnoreOwnerCollisions() {
            if (_ownerRoot == null)
                return;

            Collider[] missileColliders = GetComponentsInChildren<Collider>(true);
            Collider[] ownerColliders = _ownerRoot.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < missileColliders.Length; i++) {
                Collider missileCollider = missileColliders[i];
                if (missileCollider == null)
                    continue;

                for (int j = 0; j < ownerColliders.Length; j++) {
                    Collider ownerCollider = ownerColliders[j];
                    if (ownerCollider == null || ownerCollider == missileCollider)
                        continue;

                    Physics.IgnoreCollision(missileCollider, ownerCollider, true);
                }
            }
        }

        private void OnCollisionEnter(Collision collision) {
            if (_ownerRoot != null && collision.transform.root == _ownerRoot)
                return;

            SetMissileCameraActive(false);

            if(_explosionPrefab) Instantiate(_explosionPrefab, transform.position, Quaternion.identity);
            if (!collision.transform.TryGetComponent<IExplode>(out var ex))
                ex = collision.transform.GetComponentInParent<IExplode>();
            if (ex != null)
                ex.Explode();

            Destroy(gameObject);
        }

        private void OnDestroy() {
            if (_missileCameraActive)
                SetMissileCameraActive(false);
        }

        private void OnDrawGizmos() {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _standardPrediction);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(_standardPrediction, _deviatedPrediction);
        }
    }
}
