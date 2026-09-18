using System;
using UnityEngine.UIElements;
using MidManStudio.DragDrop.Core;

namespace MidManStudio.DragDrop.UIToolkit
{
    /// <summary>
    /// UI Toolkit adapter for the drop TARGET side. Plain C#, mirrors
    /// UGuiDropTargetZone: reports pointer enter/leave on the wrapped
    /// VisualElement to the given DragDropController as hover in/out.
    ///
    /// PointerEnterEvent/PointerLeaveEvent are specifically the two pointer
    /// events UI Toolkit's pointer-capture mechanism does NOT redirect to
    /// the currently-capturing element (confirmed against Unity's own
    /// PointerCaptureHelper.CapturePointer documentation: "all pointer
    /// events -- except PointerOverEvent, PointerOutEvent, PointerEnterEvent,
    /// PointerLeaveEvent -- are sent to the [capturing] element"). That's
    /// exactly why they're the right pair to use here: they keep reporting
    /// real hover state on the elements underneath a dragging source even
    /// while that source holds pointer capture for its own Move/Up events.
    ///
    /// DragDropController.UpdateHover is a no-op outside an active drag, so
    /// this is safe to receive ordinary pointer-enter/exit events at any
    /// time, not just mid-drag -- no need to gate on "is a drag even
    /// happening" here.
    /// </summary>
    public sealed class UIToolkitDropTargetZone
    {
        private readonly VisualElement _element;
        private readonly IDropTarget _dropTarget;
        private readonly DragDropController _controller;

        public UIToolkitDropTargetZone(VisualElement element, IDropTarget dropTarget, DragDropController controller)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
            _dropTarget = dropTarget ?? throw new ArgumentNullException(nameof(dropTarget));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            _element.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            _element.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        private void OnPointerEnter(PointerEnterEvent evt) => _controller.UpdateHover(_dropTarget);

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (ReferenceEquals(_controller.CurrentHoverTarget, _dropTarget))
                _controller.UpdateHover(null);
        }
    }
}
