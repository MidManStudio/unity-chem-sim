namespace MidManStudio.DragDrop.Core
{
    /// <summary>
    /// Something that can be picked up and dragged. Zero UI-framework
    /// dependency — works identically whether the concrete interaction is
    /// driven by uGUI's EventSystem or UI Toolkit's pointer events, since
    /// neither adapter touches this interface's shape.
    /// </summary>
    public interface IDraggable
    {
        /// <summary>
        /// The opaque payload being dragged — Inventorz drags an item
        /// stack reference, the chemistry bench drags a reagent reference,
        /// drag_n_drop itself never inspects either.
        /// </summary>
        object GetPayload();
    }

    /// <summary>Something a drag can be dropped onto.</summary>
    public interface IDropTarget
    {
        bool CanAccept(object payload);
        void OnDrop(object payload);
    }
}
