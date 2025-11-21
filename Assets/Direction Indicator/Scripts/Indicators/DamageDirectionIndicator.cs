using UnityEngine;

using System.Collections;

namespace DIndicator
{
    /// <summary>
    /// Deleted after some time
    /// </summary>
    public class DamageDirectionIndicator : CircleDirectionIndicator
    {
        [Header("Damage Direction Indicator Parameters")]
        // Time after which the indicator is removed
        [SerializeField] private float _timeToDestroy;
        // Camera shake duration
        [SerializeField, Tooltip("Camera shake duration")] private float _duration;
        // Camera shake force
        [SerializeField, Tooltip("Camera shake force")] private float _magnitude;
        // If True then the camera will shake
        [SerializeField] private bool isShakeCamera;

        public override void DestroyIndicator()
        {
            StartCoroutine(DestroyProcess(_timeToDestroy));
        }

        public override void ShowIndicator()
        {
            base.ShowIndicator();

            ShakeCamera();
            DestroyIndicator();
        }

        private void ShakeCamera()
        {
            if (!isShakeCamera) return;

            if (PlayerCamera.TryGetComponent(out CameraShaker cameraShaker))
            {
                cameraShaker.ShakeCamera();
            }
            else
            {
                PlayerCamera.gameObject.AddComponent<CameraShaker>().InitCameraShaker(_duration,
                _magnitude, PlayerCamera.transform);
            }
        }
    }
}
