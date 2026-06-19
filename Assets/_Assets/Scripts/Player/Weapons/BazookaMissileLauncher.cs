using Photon.Pun;
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

        [SerializeField]
        private float muzzleForwardOffset = 0.35f;

        [SerializeField]
        private float launchSpeed = 28f;

        [SerializeField]
        private float fireCooldown = 0.35f;

        [Header("Networking")]
        [Tooltip("Use only when the missile prefab exists under a Resources folder for PhotonNetwork.Instantiate.")]
        [SerializeField]
        private bool instantiateThroughPhoton = false;

        [SerializeField]
        private string photonResourcePrefabName;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugInfo = false;

        private float nextAllowedFireTime;

        public bool Fire(Vector3 direction)
        {
            Transform origin = muzzleTransform != null ? muzzleTransform : transform;
            Vector3 spawnPosition = origin.position + origin.forward * muzzleForwardOffset;
            return Fire(spawnPosition, direction);
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

            LaunchRigidbody(missile, launchDirection);
            nextAllowedFireTime = Time.time + fireCooldown;

            if (showDebugInfo)
            {
                Debug.Log($"{nameof(BazookaMissileLauncher)} fired {missile.name}.", this);
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
        }
    }
}
