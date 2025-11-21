using UnityEngine;

namespace DIndicator
{
    /// <summary>
    /// To the position of the target on the screen draws on top of the indicator that points to the target
    /// </summary>
    public class PointDirectionIndicator : DirectionIndicator
    {
        protected virtual void Update()
        {
            if (TargetTransform == null) { HideIndicator(); return; }

            Vector3 screenPosition = PlayerCamera.WorldToScreenPoint(TargetTransform.position);

            if (TargetInCamera(screenPosition))
            {
                _directionIndicatorImage.transform.position = screenPosition;

                ShowIndicator();
            }
            else HideIndicator();
        }

        private bool TargetInCamera(Vector3 screenPosition)
        {
            return !(screenPosition.z < 0 || screenPosition.x > Screen.width || screenPosition.x < 0
            || screenPosition.y > Screen.height || screenPosition.y < 0);
        }
    }
}
