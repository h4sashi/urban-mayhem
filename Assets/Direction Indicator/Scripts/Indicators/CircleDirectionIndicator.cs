using UnityEngine;

namespace DIndicator
{
    /// <summary>
    /// Rotates the arrow 360 degrees depending on the target
    /// </summary>
    public class CircleDirectionIndicator : DirectionIndicator
    {
        private enum Dimensions
        {
            _2D,
            _3D
        }

        [SerializeField] private Dimensions _dimensionTracking;

        protected virtual void Update()
        {
            if (TargetTransform == null) { HideIndicator(); return; }

            if (_dimensionTracking.Equals(Dimensions._2D)) _2DTracking(); else _3DTracking();
        }

        private void _3DTracking()
        {
            Vector3 diraction = PlayerCamera.transform.position - TargetTransform.position;
            Quaternion indicatorRotation = Quaternion.LookRotation(diraction);

            indicatorRotation.z = -indicatorRotation.y;
            indicatorRotation.x = 0.0f;
            indicatorRotation.y = 0.0f;

            Quaternion playerRotation = Quaternion.Euler(new Vector3(0.0f, 0.0f, PlayerCamera.transform.eulerAngles.y + 180f));

            _directionIndicatorImage.transform.rotation = indicatorRotation * playerRotation;
        }

        private void _2DTracking()
        {
            Vector3 screenPosition = PlayerCamera.WorldToScreenPoint(TargetTransform.position);
            Vector3 halfScreen = new Vector3(Screen.width, Screen.height) / 2;
            Vector3 screenCenterPosition = new Vector3(screenPosition.x, screenPosition.y) - halfScreen;

            if (screenPosition.z < 0) screenCenterPosition = -screenCenterPosition;

            _directionIndicatorImage.transform.rotation = Quaternion.FromToRotation(Vector3.up, screenCenterPosition);
        }
    }
}