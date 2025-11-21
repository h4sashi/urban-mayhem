using UnityEngine;
using UnityEngine.UI;

using System;
using System.Collections;

namespace DIndicator
{
    /// <summary>
    /// The class implements the main parameters of the indicator
    /// </summary>
    [RequireComponent(typeof(Image), typeof(Animation))]
    public abstract class DirectionIndicator : MonoBehaviour
    {
        #region EVENTS

        //Called when the indicator is about to disappear or reappear
        //When isShow is true, the indicator appears
        public Action<bool> ShowDirectionIndicator;

        //Called when an object is deleted
        public Action DestroyDirectionIndicator;

        #endregion

        //Names of animations that are used in the "Animation" component
        [Header("Animation names")]
        [SerializeField] private string _showNameAnimation = "ShowDiractionIndicator";
        [SerializeField] private string _hideNameAnimation = "HideDiractionIndicator";

        // Current indicator type
        [HideInInspector] public DirectionIndicatorType IndicatorType;

        //The object we are monitoring
        public Transform TargetTransform { get; private set; }
        //The player from whom the indicator rotation is calculated
        public Transform PlayerTransform { get; private set; }
        //The camera that is or is watching the player
        public Camera PlayerCamera { get; private set; }

        protected RectTransform _directionIndicatorTransform;
        protected Image _directionIndicatorImage;
        private Vector3 _startScale;
        private Animation _animation;
        private bool _isShown = true;

        /// <summary>
        /// Initializes the data required for the indicator to work.
        /// ALWAYS CALL WHEN INSTANTIATE INDICATOR!
        /// </summary>
        public virtual void InitIndicator(Transform target, Transform player, Camera playerCamera)
        {
            this.TargetTransform = target;
            this.PlayerTransform = player;
            this.PlayerCamera = playerCamera;

            _directionIndicatorTransform = this.GetComponent<RectTransform>();
            _directionIndicatorImage = this.GetComponent<Image>();
            _animation = this.GetComponent<Animation>();
            _startScale = _directionIndicatorTransform.localScale;

            ShowIndicator();
        }

        public virtual void DestroyIndicator()
        {
            StartCoroutine(DestroyProcess(0));
        }

        public virtual void ShowIndicator()
        {
            StopAllCoroutines();

            if (!_isShown) return;

            _animation.Play(_showNameAnimation);
            ShowDirectionIndicator?.Invoke(true);

            _isShown = false;
        }

        protected virtual void HideIndicator()
        {
            if (_isShown) return;

            _animation.Play(_hideNameAnimation);
            ShowDirectionIndicator?.Invoke(false);

            _isShown = true;
        }

        protected virtual IEnumerator DestroyProcess(float timeToDestroy)
        {
            yield return new WaitForSeconds(timeToDestroy);
            HideIndicator();

            yield return new WaitForSeconds(_animation.GetClip(_hideNameAnimation).length);
            DestroyDirectionIndicator?.Invoke();
            Destroy(this.gameObject);
        }
    }
}