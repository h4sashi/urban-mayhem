using UnityEngine;
using UnityEngine.UI;

namespace DIndicator
{
    /// <summary>
    /// Shows the distance from the player to the target
    /// </summary>
    [RequireComponent(typeof(Text), typeof(Animation))]
    public class TextDistance : MonoBehaviour
    {
        [SerializeField] private DirectionIndicator _directionIndicator;
        // Number unit
        [SerializeField] private string _unit = "M";
        // Range adjustment distance
        [SerializeField] private float _factor = 0.5f;

        // Names of animations that are used in the "Animation" component
        [Header("Animation names")]
        [SerializeField] private string _showNameAnimation = "ShowTextDistance";
        [SerializeField] private string _hideNameAnimation = "HideTextDistance";

        private Text _text;
        private Color _startColor;
        private Animation _animation;

        #region MONO

        private void Awake()
        {
            _text = this.GetComponent<Text>();
            _animation = this.GetComponent<Animation>();

            _directionIndicator.ShowDirectionIndicator += OnShowTextDistance;
        }

        #endregion

        private void Update()
        {
            if (_directionIndicator.TargetTransform == null || _directionIndicator.PlayerTransform == null) return;

            _text.text = CalculateDistance();
        }

        private string CalculateDistance()
        {
            return (int)(Vector3.Distance(
                _directionIndicator.TargetTransform.position,
                _directionIndicator.PlayerTransform.position) * _factor) + _unit;
        }

        #region CALLBACK

        private void OnShowTextDistance(bool isShow)
        {
            if (isShow) _animation.Play(_showNameAnimation); else _animation.Play(_hideNameAnimation);
        }

        #endregion
    }
}
