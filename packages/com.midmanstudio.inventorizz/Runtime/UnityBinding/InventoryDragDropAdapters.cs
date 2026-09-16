using MidManStudio.DragDrop.Core;
using MidManStudio.Inventorizz.Core;
using UnityEngine;

namespace MidManStudio.Inventorizz.UnityBinding
{
    /// <summary>
    /// Pairs with dragndrop's <c>UGuiDraggableSource</c> on the same
    /// GameObject (that component owns the visual "where's the drag ghost"
    /// side; this one owns "what's actually being dragged"). Reads its
    /// address from the sibling <see cref="InventorySlotView"/> rather than
    /// duplicating it, so the two can never drift out of sync.
    /// </summary>
    [RequireComponent(typeof(InventorySlotView))]
    public sealed class InventorySlotDraggableAdapter : MonoBehaviour, IDraggable
    {
        private InventorySlotView? _view;
        private void Awake() => _view = GetComponent<InventorySlotView>();

        public object GetPayload() => new InventorySlotPayload(_view!.ContainerInstanceId, _view!.SlotIndex);
    }

    /// <summary>Pairs with dragndrop's <c>UGuiDropTargetZone</c> on the same GameObject as an <see cref="InventorySlotView"/> -- exact-slot drop target for a grid-style inventory UI.</summary>
    [RequireComponent(typeof(InventorySlotView))]
    public sealed class InventorySlotDropTarget : MonoBehaviour, IDropTarget
    {
        private InventorySlotView? _view;
        private InventorySlotDropTargetLogic? _logic;

        /// <summary>Must be called once before use (there's no ambient way to find "the" InventorizzRuntime, same reason DragDropControllerHost takes an explicit resolve path rather than a singleton).</summary>
        public void Initialize(InventorizzRuntime runtime)
        {
            _view ??= GetComponent<InventorySlotView>();
            _logic = new InventorySlotDropTargetLogic(runtime);
        }

        public bool CanAccept(object payload)
        {
            if (_logic == null || _view == null) return false;
            _logic.ContainerInstanceId = _view.ContainerInstanceId;
            _logic.SlotIndex = _view.SlotIndex;
            return _logic.CanAccept(payload);
        }

        public void OnDrop(object payload)
        {
            if (_logic == null || _view == null) return;
            _logic.ContainerInstanceId = _view.ContainerInstanceId;
            _logic.SlotIndex = _view.SlotIndex;
            _logic.OnDrop(payload);
        }
    }

    /// <summary>Whole-container drop target for a flat-bag inventory panel (or as a catch-all for a fixed-grid one). Set <see cref="ContainerInstanceId"/> to whichever container this panel represents.</summary>
    public sealed class InventoryContainerDropTarget : MonoBehaviour, IDropTarget
    {
        private InventoryContainerDropTargetLogic? _logic;
        public string ContainerInstanceId { get; set; } = string.Empty;

        public void Initialize(InventorizzRuntime runtime) => _logic = new InventoryContainerDropTargetLogic(runtime);

        public bool CanAccept(object payload)
        {
            if (_logic == null) return false;
            _logic.ContainerInstanceId = ContainerInstanceId;
            return _logic.CanAccept(payload);
        }

        public void OnDrop(object payload)
        {
            if (_logic == null) return;
            _logic.ContainerInstanceId = ContainerInstanceId;
            _logic.OnDrop(payload);
        }
    }
}
