using Photon.Pun;
using Tarodev;
using UnityEngine;

namespace Hanzo.Player.Weapons
{
    public class BazookaMissileLauncher : MonoBehaviour
    {
        [Header("Missile")]
        [SerializeField]
        private GameObject missilePrefab;

        [SerializeField]
        private Transform muzzleTransform;

        [Tooltip("When enabled, missiles spawn exactly at muzzleTransform.position. Disable to apply muzzleForwardOffset from the muzzle forward axis.")]
        [SerializeField]
        private bool spawnDirectlyFromMuzzle = true;

        [SerializeField]
        private float muzzleForwardOffset = 0.35f;

        [SerializeField]
        private float launchSpeed = 28f;

        [SerializeField]
        private float fireCooldown = 0.35f;

        [Header("Targeting")]
        [SerializeField]
        private bool autoTargetNearestTarget = true;

        [SerializeField]
        private float targetSearchRadius = 35f;

        [SerializeField]
        private LayerMask targetLayerMask = ~0;

        [Header("Networking")]
        [Tooltip("Use only when the missile prefab exists under a Resources folder for PhotonNetwork.Instantiate.")]
        [SerializeField]
        private bool instantiateThroughPhoton = false;

        [SerializeField]
        private string photonResourcePrefabName;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        private const int MaxTargetQueryResults = 32;
        private readonly Collider[] targetQueryBuffer = new Collider[MaxTargetQueryResults];
        private float nextAllowedFireTime;

        public bool Fire(Vector3 direction)
        {
            Transform origin = muzzleTransform != null ? muzzleTransform : transform;
            Vector3 spawnPosition = origin.position;

            if (muzzleTransform == null || !spawnDirectlyFromMuzzle)
            {
                spawnPosition += origin.forward * muzzleForwardOffset;
            }

            Vector3 launchDirection = direction.sqrMagnitude > Mathf.Epsilon
                ? direction
                : origin.forward;

            return Fire(spawnPosition, launchDirection);
        }

        public bool Fire(Vector3 spawnPosition, Vector3 direction)
        {
            if (Time.time < nextAllowedFireTime)
                return false;

            if (missilePrefab == null)
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"{nameof(BazookaMissileLauncher)} on {name}: No missile prefab assigned.", this);
                }

                return false;
            }

            Vector3 launchDirection = direction.sqrMagnitude > Mathf.Epsilon
                ? direction.normalized
                : transform.forward;

            Quaternion spawnRotation = Quaternion.LookRotation(launchDirection, Vector3.up);
            GameObject missile = SpawnMissile(spawnPosition, spawnRotation);

            if (missile == null)
                return false;

            Target target = autoTargetNearestTarget
                ? FindClosestTarget(spawnPosition)
                : null;

            LaunchMissile(missile, launchDirection, target);
            nextAllowedFireTime = Time.time + fireCooldown;

            if (showDebugInfo)
            {
                string targetName = target != null ? target.name : "no target";
                Debug.Log($"{nameof(BazookaMissileLauncher)} fired {missile.name} toward {targetName}.", this);
            }

            return true;
        }

        private GameObject SpawnMissile(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (instantiateThroughPhoton && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                string prefabName = string.IsNullOrWhiteSpace(photonResourcePrefabName)
                    ? missilePrefab.name
                    : photonResourcePrefabName;

                return PhotonNetwork.Instantiate(prefabName, spawnPosition, spawnRotation);
            }

            return Instantiate(missilePrefab, spawnPosition, spawnRotation);
        }

        private Target FindClosestTarget(Vector3 origin)
        {
            float closestDistance = Mathf.Infinity;
            Target closestTarget = null;

            int targetCount = Physics.OverlapSphereNonAlloc(
                origin,
                targetSearchRadius,
                targetQueryBuffer,
                targetLayerMask,
                QueryTriggerInteraction.Collide
            );

            for (int i = 0; i < targetCount; i++)
            {
                Collider col = targetQueryBuffer[i];
                if (col == null)
                    continue;

                Target target = col.GetComponentInParent<Target>();
                if (!IsValidTarget(target))
                    continue;

                float sqrDistance = (target.transform.position - origin).sqrMagnitude;
                if (sqrDistance >= closestDistance)
                    continue;

                closestDistance = sqrDistance;
                closestTarget = target;
            }

            return closestTarget != null ? closestTarget : FindClosestTargetByComponent(origin);
        }

        private Target FindClosestTargetByComponent(Vector3 origin)
        {
            float closestDistance = targetSearchRadius * targetSearchRadius;
            Target closestTarget = null;
            Target[] targets = FindSceneTargets();

            for (int i = 0; i < targets.Length; i++)
            {
                Target target = targets[i];
                if (!IsValidTarget(target))
                    continue;

                float sqrDistance = (target.transform.position - origin).sqrMagnitude;
                if (sqrDistance >= closestDistance)
                    continue;

                closestDistance = sqrDistance;
                closestTarget = target;
            }

            return closestTarget;
        }

        private Target[] FindSceneTargets()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Target>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<Target>();
#endif
        }

        private bool IsValidTarget(Target target)
        {
            if (target == null)
                return false;

            return target.transform.root != transform.root;
        }

        private void LaunchMissile(GameObject missile, Vector3 launchDirection, Target target)
        {
            Missile homingMissile = missile.GetComponent<Missile>();
            if (homingMissile == null)
            {
                homingMissile = missile.GetComponentInChildren<Missile>(true);
            }

            if (homingMissile != null)
            {
                homingMissile.Launch(target, launchDirection, launchSpeed, transform.root, ShouldActivateMissileCamera());
                return;
            }

            LaunchRigidbody(missile, launchDirection);
        }

        private bool ShouldActivateMissileCamera()
        {
            PhotonView ownerView = GetComponentInParent<PhotonView>();
            return ownerView == null || !PhotonNetwork.IsConnected || ownerView.IsMine;
        }

        private void LaunchRigidbody(GameObject missile, Vector3 launchDirection)
        {
            Rigidbody missileRb = missile.GetComponent<Rigidbody>();
            if (missileRb == null)
            {
                missileRb = missile.GetComponentInChildren<Rigidbody>();
            }

            if (missileRb == null)
                return;

            missileRb.isKinematic = false;
            missileRb.velocity = launchDirection * launchSpeed;
            missileRb.angularVelocity = Vector3.zero;
            missileRb.WakeUp();
        }
    }
}
