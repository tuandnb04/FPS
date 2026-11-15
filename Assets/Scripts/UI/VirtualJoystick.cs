using UnityEngine;
using UnityEngine.EventSystems;

namespace UI
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

        private Vector2 _liveInput;
        private bool _pointerActive;

        private bool _hasSimulation;
        private Vector2 _simulatedInput;
        private bool _simulatedHeld;

        public Vector2 Value => _hasSimulation ? _simulatedInput : _liveInput;
        public bool IsHeld => _hasSimulation ? _simulatedHeld : _pointerActive;

        public void BindRects(RectTransform bg, RectTransform handle, float range = 52f)
        {
            backgroundRect = bg;
            handleRect = handle;
            handleRange = range;
        }

        public void SetSimulatedVector(Vector2 value, bool held)
        {
            _hasSimulation = true;
            _simulatedInput = Vector2.ClampMagnitude(value, 1f);
            _simulatedHeld = held;
            if (handleRect != null)
            {
                handleRect.anchoredPosition = _simulatedInput * handleRange;
            }
        }

        public void ClearSimulation()
        {
            _hasSimulation = false;
            _simulatedInput = Vector2.zero;
            _simulatedHeld = false;
            ResetHandleVisual();
        }

        private void OnDisable()
        {
            _pointerActive = false;
            _liveInput = Vector2.zero;
            ResetHandleVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerActive = true;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (backgroundRect == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backgroundRect,
                eventData.position,
                eventData.pressEventCamera,
                out var localPos))
            {
                var halfSize = backgroundRect.sizeDelta * 0.5f;
                var radius = Mathf.Max(20f, Mathf.Min(halfSize.x, halfSize.y));
                var normalized = localPos / radius;
                if (normalized.magnitude > 1f)
                {
                    normalized.Normalize();
                }

                _liveInput = normalized.magnitude < deadZone ? Vector2.zero : normalized;
                if (handleRect != null)
                {
                    handleRect.anchoredPosition = normalized * handleRange;
                }
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pointerActive = false;
            _liveInput = Vector2.zero;
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
