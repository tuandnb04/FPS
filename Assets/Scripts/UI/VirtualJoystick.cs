using UnityEngine;
using UnityEngine.EventSystems;

namespace TopDownShooter.UI
{
    /// <summary>
    /// Mobile twin-stick touch joystick supporting drag clamping, deadzone filtering,
    /// clean pointer release recentering, and deterministic simulation for Play-Mode checks.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform backgroundRect;
        [SerializeField] private RectTransform handleRect;
        [SerializeField] private float handleRange = 52f;
        [SerializeField] private float deadZone = 0.12f;

        private Vector2 liveInput;
        private bool pointerActive;

        private bool hasSimulation;
        private Vector2 simulatedInput;
        private bool simulatedHeld;

        public Vector2 Value => hasSimulation ? simulatedInput : liveInput;
        public bool IsHeld => hasSimulation ? simulatedHeld : pointerActive;

        public void BindRects(RectTransform bg, RectTransform handle, float range = 52f)
        {
            backgroundRect = bg;
            handleRect = handle;
            handleRange = range;
        }

        public void SetSimulatedVector(Vector2 value, bool held)
        {
            hasSimulation = true;
            simulatedInput = Vector2.ClampMagnitude(value, 1f);
            simulatedHeld = held;
            if (handleRect != null)
            {
                handleRect.anchoredPosition = simulatedInput * handleRange;
            }
        }

        public void ClearSimulation()
        {
            hasSimulation = false;
            simulatedInput = Vector2.zero;
            simulatedHeld = false;
            ResetHandleVisual();
        }

        private void OnDisable()
        {
            pointerActive = false;
            liveInput = Vector2.zero;
            ResetHandleVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerActive = true;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (backgroundRect == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backgroundRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPos))
            {
                Vector2 halfSize = backgroundRect.sizeDelta * 0.5f;
                float radius = Mathf.Max(20f, Mathf.Min(halfSize.x, halfSize.y));
                Vector2 normalized = localPos / radius;
                if (normalized.magnitude > 1f)
                {
                    normalized.Normalize();
                }

                liveInput = normalized.magnitude < deadZone ? Vector2.zero : normalized;
                if (handleRect != null)
                {
                    handleRect.anchoredPosition = normalized * handleRange;
                }
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointerActive = false;
            liveInput = Vector2.zero;
            ResetHandleVisual();
        }

        private void ResetHandleVisual()
        {
            if (handleRect != null)
            {
                handleRect.anchoredPosition = Vector2.zero;
            }
        }
    }
}
