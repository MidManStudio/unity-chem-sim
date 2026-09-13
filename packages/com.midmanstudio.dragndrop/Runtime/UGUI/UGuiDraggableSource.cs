using UnityEngine;
using UnityEngine.EventSystems;
using MidManStudio.DragDrop.Core;

namespace MidManStudio.DragDrop.UGUI
{
    /// <summary>
    /// uGUI adapter for the drag SOURCE side. Requires an <see cref="IDraggable"/>
    /// on the same GameObject (any sibling MonoBehaviour implementing it)
    /// and feeds BeginDrag/EndDrag into a <see cref="DragDropController"/>
    /// resolved via <see cref="DragDropControllerHost"/>. Owns the
    /// "where is the drag ghost right now" concern the Core state machine
    /// explicitly stays out of -- moving this RectTransform and,
    /// optionally, keeping it inside its parent Canvas's bounds.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UGuiDraggableSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Behaviour")]
        [Tooltip("Clamp this RectTransform's anchoredPosition to stay fully inside the parent Canvas while dragging.")]
        [SerializeField] private bool constrainToCanvasBounds = true;

        [Tooltip("CanvasGroup.alpha applied for the duration of the drag. Restored to whatever it was before the drag started on drop/cancel.")]
        [SerializeField] private float dragAlpha = 0.7f;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Canvas _parentCanvas;
        private IDraggable _draggable;
        private DragDropController _controller;
        private float _preDragAlpha = 1f;
        private bool _preDragBlocksRaycasts = true;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _parentCanvas = GetComponentInParent<Canvas>();
            _draggable = GetComponent<IDraggable>();
            _controller = DragDropControllerHost.Resolve(this);

            if (_draggable == null)
                Debug.LogWarning($"[UGuiDraggableSource] No IDraggable found on '{name}' -- this component needs a sibling MonoBehaviour implementing IDraggable to do anything.", this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_draggable == null) return;

            _controller.BeginDrag(_draggable);
            if (_controller.Phase != DragPhase.Dragging) return; // re-entrant BeginDrag calls are a no-op on the controller

            _preDragAlpha = _canvasGroup.alpha;
            _preDragBlocksRaycasts = _canvasGroup.blocksRaycasts;
            _canvasGroup.alpha = dragAlpha;
            _canvasGroup.blocksRaycasts = false; // let raycasts pass through to whatever's underneath, so drop targets can hover
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_controller.Phase != DragPhase.Dragging) return;

            float scale = _parentCanvas != null ? _parentCanvas.scaleFactor : 1f;
            Vector2 next = _rectTransform.anchoredPosition + eventData.delta / scale;
            _rectTransform.anchoredPosition = constrainToCanvasBounds ? ClampToCanvas(next) : next;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_controller.Phase != DragPhase.Dragging) return;

            _canvasGroup.alpha = _preDragAlpha;
            _canvasGroup.blocksRaycasts = _preDragBlocksRaycasts;
            _controller.EndDrag(); // resolves drop-or-cancel against whatever UGuiDropTargetZone last reported as hovered
        }

        /// <summary>
        /// The actual clamp math, pulled out as a pure static function so
        /// it's unit-testable without a live RectTransform/Canvas. The
        /// instance method just gathers current sizes and forwards here.
        /// </summary>
        internal static Vector2 ClampToCanvasBounds(
            Vector2 position, Vector2 canvasSize, Vector2 itemSize, Vector2 pivot)
        {
            Vector2 min = new Vector2(
                -canvasSize.x * 0.5f + itemSize.x * pivot.x,
                -canvasSize.y * 0.5f + itemSize.y * pivot.y);
            Vector2 max = new Vector2(
                canvasSize.x * 0.5f - itemSize.x * (1f - pivot.x),
                canvasSize.y * 0.5f - itemSize.y * (1f - pivot.y));

            return new Vector2(
                Mathf.Clamp(position.x, min.x, max.x),
                Mathf.Clamp(position.y, min.y, max.y));
        }

        private Vector2 ClampToCanvas(Vector2 position)
        {
            if (_parentCanvas == null) return position;
            var canvasRect = _parentCanvas.GetComponent<RectTransform>();
            if (canvasRect == null) return position;

            return ClampToCanvasBounds(position, canvasRect.sizeDelta, _rectTransform.sizeDelta, _rectTransform.pivot);
        }
    }
}
