using UnityEngine;
using UnityEngine.EventSystems;
using MidManStudio.DragDrop.Core;

namespace MidManStudio.DragDrop.UGUI
{
    /// <summary>
    /// uGUI adapter for the drop TARGET side. Requires an <see cref="IDropTarget"/>
    /// on the same GameObject and reports hover in/out to a
    /// <see cref="DragDropController"/> resolved via <see cref="DragDropControllerHost"/>.
    /// <see cref="DragDropController.UpdateHover"/> is a no-op outside an
    /// active drag, so this is safe to receive ordinary pointer-enter/exit
    /// events at any time, not just mid-drag -- no need to gate on
    /// "is a drag even happening" here.
    /// </summary>
    public class UGuiDropTargetZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private IDropTarget _dropTarget;
        private DragDropController _controller;

        private void Awake()
        {
            _dropTarget = GetComponent<IDropTarget>();
            _controller = DragDropControllerHost.Resolve(this);

            if (_dropTarget == null)
                Debug.LogWarning($"[UGuiDropTargetZone] No IDropTarget found on '{name}' -- this component needs a sibling MonoBehaviour implementing IDropTarget to do anything.", this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_dropTarget != null) _controller.UpdateHover(_dropTarget);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_dropTarget != null && ReferenceEquals(_controller.CurrentHoverTarget, _dropTarget))
                _controller.UpdateHover(null);
        }
    }
}
