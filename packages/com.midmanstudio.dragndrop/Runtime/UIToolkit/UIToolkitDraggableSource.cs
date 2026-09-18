using System;
using UnityEngine;
using UnityEngine.UIElements;
using MidManStudio.DragDrop.Core;

namespace MidManStudio.DragDrop.UIToolkit
{
    /// <summary>
    /// UI Toolkit adapter for the drag SOURCE side. Unlike UGuiDraggableSource,
    /// this is plain C# rather than a MonoBehaviour -- a VisualElement has no
    /// GameObject and no sibling-component concept, so this is constructed
    /// directly against the element, the IDraggable it wraps, and the shared
    /// DragDropController it should feed. There is deliberately no host/
    /// resolver step the way uGUI's DragDropControllerHost provides: UI
    /// Toolkit UIs are built in code by a presenter that already owns (or is
    /// handed) the controller, so passing it explicitly is the natural fit --
    /// there's no GameObject hierarchy to walk looking for one, and inventing
    /// one just to mirror uGUI would be solving a problem UI Toolkit doesn't
    /// have. Neither this class nor UGuiDraggableSource needs to know the
    /// other exists -- this assembly references only MidManStudio.DragDrop.Runtime.
    ///
    /// Owns the "where is the drag ghost right now" concern the Core state
    /// machine explicitly stays out of, exactly as UGuiDraggableSource does
    /// for RectTransform -- here via style.left/style.top, which requires
    /// the element to be absolutely positioned (`position: absolute`) in its
    /// stylesheet, the direct analogue of RectTransform-based dragging's own
    /// precondition.
    /// </summary>
    public sealed class UIToolkitDraggableSource
    {
        private readonly VisualElement _element;
        private readonly IDraggable _draggable;
        private readonly DragDropController _controller;

        /// <summary>Opacity applied to the element for the duration of the drag. Restored to whatever it was before the drag started, on drop or cancel.</summary>
        public float DragOpacity { get; set; } = 0.7f;

        /// <summary>Clamp the element's style.left/top to stay fully inside its parent's layout bounds while dragging.</summary>
        public bool ConstrainToParentBounds { get; set; } = true;

        private int _activePointerId = -1;
        private float _preDragOpacity = 1f;
        private PickingMode _preDragPickingMode = PickingMode.Position;

        public UIToolkitDraggableSource(VisualElement element, IDraggable draggable, DragDropController controller)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
            _draggable = draggable ?? throw new ArgumentNullException(nameof(draggable));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            _element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _element.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _element.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Deliberately NOT checking evt.target == _element here -- PointerDownEvent
            // bubbles, and a slot's icon/label children are common; a press starting on
            // a child should still be able to drag the slot it's inside.
            if (evt.button != 0) return;

            _controller.BeginDrag(_draggable);

            // NOTE: Core's DragDropController.Phase is always Dragging immediately
            // after any BeginDrag call (its own re-entrant-no-op path returns before
            // touching Phase, which was already Dragging) -- so a Phase check here
            // can never actually detect "someone else's drag is already active" the
            // way its comment implies. CurrentSource is the real check: it only
            // updates when a drag actually starts, so this is how a second source's
            // PointerDown during someone else's active drag is correctly ignored
            // instead of hijacking pointer capture/state that belongs to the drag
            // already in progress.
            if (!ReferenceEquals(_controller.CurrentSource, _draggable)) return;

            _activePointerId = evt.pointerId;
            _preDragOpacity = _element.style.opacity.value;
            _preDragPickingMode = _element.pickingMode;
            _element.style.opacity = DragOpacity;
            _element.pickingMode = PickingMode.Ignore; // let pointer events pass through to whatever's underneath, so drop targets can hover -- mirrors UGuiDraggableSource's canvasGroup.blocksRaycasts = false
            PointerCaptureHelper.CapturePointer(_element, evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_controller.Phase != DragPhase.Dragging || evt.pointerId != _activePointerId) return;

            float nextLeft = _element.style.left.value.value + evt.deltaPosition.x;
            float nextTop = _element.style.top.value.value + evt.deltaPosition.y;

            if (ConstrainToParentBounds && _element.parent != null)
            {
                var clamped = ClampToParentBounds(
                    new Vector2(nextLeft, nextTop),
                    new Vector2(_element.parent.layout.width, _element.parent.layout.height),
                    new Vector2(_element.layout.width, _element.layout.height));
                nextLeft = clamped.x;
                nextTop = clamped.y;
            }

            _element.style.left = nextLeft;
            _element.style.top = nextTop;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_controller.Phase != DragPhase.Dragging || evt.pointerId != _activePointerId) return;
            EndDrag();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // Capture lost some other way (OS focus loss, something else force-
            // releasing it) -- resolve the drag exactly like a normal pointer-up
            // rather than leaving the controller stuck in Dragging phase forever.
            if (_controller.Phase != DragPhase.Dragging || evt.pointerId != _activePointerId) return;
            EndDrag();
        }

        private void EndDrag()
        {
            _element.style.opacity = _preDragOpacity;
            _element.pickingMode = _preDragPickingMode;
            if (PointerCaptureHelper.HasPointerCapture(_element, _activePointerId))
                PointerCaptureHelper.ReleasePointer(_element, _activePointerId);
            _activePointerId = -1;
            _controller.EndDrag(); // resolves drop-or-cancel against whatever UIToolkitDropTargetZone last reported as hovered
        }

        /// <summary>
        /// The actual clamp math, pulled out as a pure static function so it's
        /// unit-testable without a live VisualElement/panel -- mirrors
        /// UGuiDraggableSource.ClampToCanvasBounds, just in top-left-anchored
        /// style.left/top space instead of a pivot-anchored
        /// RectTransform.anchoredPosition space.
        /// </summary>
        internal static Vector2 ClampToParentBounds(Vector2 position, Vector2 parentSize, Vector2 elementSize)
        {
            float maxX = Mathf.Max(0f, parentSize.x - elementSize.x);
            float maxY = Mathf.Max(0f, parentSize.y - elementSize.y);
            return new Vector2(
                Mathf.Clamp(position.x, 0f, maxX),
                Mathf.Clamp(position.y, 0f, maxY));
        }
    }
}
