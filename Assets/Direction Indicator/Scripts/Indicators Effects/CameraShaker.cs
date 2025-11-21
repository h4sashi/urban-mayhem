using UnityEngine;

namespace DIndicator
{
    public class CameraShaker : MonoBehaviour
    {
        private Transform _camTransform;
        private float _duration;
        private float _currentDuration;
        private float _magnitude;
        private bool _isShake;
        private Vector3 _originalPos;

        private void Update()
        {
            if (_isShake)
            {
                if (_currentDuration > 0)
                {
                    _camTransform.localPosition = _originalPos + Random.insideUnitSphere * _magnitude;
                    _currentDuration -= Time.deltaTime;
                }
                else
                {
                    _currentDuration = _duration;
                    _camTransform.localPosition = _originalPos;
                    _isShake = false;
                }
            }
        }

        public void InitCameraShaker(float duration, float magnitude, Transform camTransform)
        {
            this._camTransform = camTransform;
            this._duration = duration;
            this._magnitude = magnitude;

            _originalPos = _camTransform.localPosition;

            ShakeCamera();
        }

        public void ShakeCamera()
        {
            _currentDuration = _duration;
            _isShake = true;
        }
    }
}
