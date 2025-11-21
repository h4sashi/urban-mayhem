using UnityEngine;

namespace DIndicator
{
    /// <summary>
    /// Script that moves the arrow along the border of the screen
    /// </summary>
    public class BorderDirectionIndicator : DirectionIndicator
    {
        [Header("Border Direction Indicator Parameters")]
        //Offset arrow from screen border
        [SerializeField, Range(20f, 100f)] private float _arrowOffset;
        //Arrow size depending on the player's distance from the target
        [SerializeField] private bool _isScale;
        //Hide the arrow when looking at the target
        [SerializeField] private bool _isHideWhenLooking;

        private Renderer _targetRenderer;

        protected virtual void Update()
        {
            if (TargetTransform == null) { HideIndicator(); return; }

            Vector3 screenPosition = PlayerCamera.WorldToScreenPoint(GetTargetPosition());
            Vector3 halfScreen = new Vector3(Screen.width, Screen.height) / 2;
            Vector3 screenCenterPosition = new Vector3(screenPosition.x, screenPosition.y) - halfScreen;

            if (screenPosition.z < 0) screenCenterPosition = -screenCenterPosition;

            if (!TargetInCamera(screenPosition))
            {
                if (_isHideWhenLooking) ShowIndicator();

                _directionIndicatorImage.transform.rotation = Quaternion.FromToRotation(Vector3.up, screenCenterPosition);
                screenPosition = FindArrowPosition(screenPosition, halfScreen, screenCenterPosition);
            }
            else
            {
                if (_isHideWhenLooking) HideIndicator();

                _directionIndicatorImage.transform.rotation = Quaternion.Euler(new Vector3(0.0f, 0.0f, 180f));
            }

            SetPositionIndicator(screenPosition);
            SetScaleIndicator();
        }

        public override void InitIndicator(Transform target, Transform player, Camera playerCamera)
        {
            base.InitIndicator(target, player, playerCamera);
            TargetTransform.TryGetComponent(out Renderer _targetRenderer);
        }

        private Vector3 GetTargetPosition()
        {
            return (_targetRenderer != null) ?
                new Vector3(_targetRenderer.bounds.center.x,
                _targetRenderer.bounds.max.y,
                _targetRenderer.bounds.center.z) : TargetTransform.position;
        }

        private void SetPositionIndicator(Vector3 screenPosition)
        {
            screenPosition.x = Mathf.Clamp(screenPosition.x, _arrowOffset, Screen.width - _arrowOffset);
            screenPosition.y = Mathf.Clamp(screenPosition.y, _arrowOffset, Screen.height - _arrowOffset);

            _directionIndicatorImage.transform.position = screenPosition;
        }

        private void SetScaleIndicator()
        {
            if (_isScale == false) return;

            float distance = Vector3.Distance(PlayerCamera.transform.position, TargetTransform.position);
            float scale = Mathf.Clamp(5.0f / distance, 0.3f, 1f);
            _directionIndicatorImage.transform.localScale = new Vector3(scale, scale, scale);
        }

        private bool TargetInCamera(Vector3 screenPosition)
        {
            return !(screenPosition.z < 0 || screenPosition.x > Screen.width || screenPosition.x < 0
            || screenPosition.y > Screen.height || screenPosition.y < 0);
        }

        private Vector3 FindArrowPosition(Vector3 screenPosition, Vector3 halfScreen, Vector3 screenCenterPosition)
        {
            Vector3 NormalScreenCenterPosition = screenCenterPosition.normalized;

            if (NormalScreenCenterPosition.x == 0)
            {
                NormalScreenCenterPosition.x = 0.01f;
            }
            if (NormalScreenCenterPosition.y == 0)
            {
                NormalScreenCenterPosition.y = 0.01f;
            }

            Vector3 xScreenCenterPosition = NormalScreenCenterPosition * (halfScreen.x / Mathf.Abs(NormalScreenCenterPosition.x));
            Vector3 yScreenCenterPosition = NormalScreenCenterPosition * (halfScreen.y / Mathf.Abs(NormalScreenCenterPosition.y));

            if (xScreenCenterPosition.sqrMagnitude < yScreenCenterPosition.sqrMagnitude)
            {
                screenPosition = halfScreen + xScreenCenterPosition;
            }
            else
            {
                screenPosition = halfScreen + yScreenCenterPosition;
            }

            return screenPosition;
        }
    }
}
