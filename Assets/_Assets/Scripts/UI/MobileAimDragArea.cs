using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hanzo.UI
{
    public class MobileAimDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("Input")]
        [SerializeField]
        private float pixelsForFullInput = 120f;

        [SerializeField]
        private bool invertHorizontal = false;

        [SerializeField]
        private bool invertVertical = false;

        [Header("Raycast")]
        [SerializeField]
        private Graphic raycastGraphic;

        private int activePointerId = int.MinValue;
        private Vector2 lastPointerPosition;
        private Vector2 accumulatedDrag;

        public bool IsDragging => activePointerId != int.MinValue;

        private void Awake()
        {
            CacheGraphic();
        }

        private void OnDisable()
        {
            activePointerId = int.MinValue;
            accumulatedDrag = Vector2.zero;
        }

        public void SetInputEnabled(bool inputEnabled)
        {
            CacheGraphic();

            if (raycastGraphic != null)
            {
                raycastGraphic.raycastTarget = inputEnabled;
            }
        }

        public Vector2 ConsumeDragInput()
        {
            if (pixelsForFullInput <= Mathf.Epsilon)
                return Vector2.zero;

            Vector2 input = accumulatedDrag / pixelsForFullInput;
            accumulatedDrag = Vector2.zero;

            if (invertHorizontal)
            {
                input.x = -input.x;
            }

            if (invertVertical)
            {
                input.y = -input.y;
            }

            return Vector2.ClampMagnitude(input, 1f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (activePointerId != int.MinValue)
                return;

            activePointerId = eventData.pointerId;
            lastPointerPosition = eventData.position;
            accumulatedDrag = Vector2.zero;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId)
                return;

            Vector2 currentPosition = eventData.position;
            accumulatedDrag += currentPosition - lastPointerPosition;
            lastPointerPosition = currentPosition;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId)
                return;

            activePointerId = int.MinValue;
        }

        private void CacheGraphic()
        {
            if (raycastGraphic == null)
            {
                raycastGraphic = GetComponent<Graphic>();
            }
        }
    }
}
