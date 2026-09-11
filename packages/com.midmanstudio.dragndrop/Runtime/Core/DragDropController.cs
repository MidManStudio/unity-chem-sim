using System;

namespace MidManStudio.DragDrop.Core
{
    public enum DragPhase
    {
        None,
        Dragging,
    }

    /// <summary>
    /// Framework-agnostic drag session state machine. A uGUI adapter feeds
    /// this from IBeginDragHandler/IDragHandler/IEndDragHandler/IDropHandler;
    /// a UI Toolkit adapter feeds it from PointerDown/Move/Up manipulators.
    /// Neither adapter needs to know the other exists — all the actual
    /// "is this drag valid, what happens on drop" logic lives here exactly
    /// once, and Inventorz and the chemistry bench UI can share a single
    /// instance if they want cross-system dragging, or use separate ones if
    /// they don't.
    ///
    /// Deliberately excludes on-screen position/pointer tracking — "where
    /// is the drag ghost right now" is an adapter/rendering concern, not an
    /// accept-or-reject concern, so it stays out of this class entirely.
    /// </summary>
    public sealed class DragDropController
    {
        public DragPhase Phase { get; private set; } = DragPhase.None;
        public object? CurrentPayload { get; private set; }
        public IDraggable? CurrentSource { get; private set; }
        public IDropTarget? CurrentHoverTarget { get; private set; }

        public event EventHandler<DragStartedEventArgs>? DragStarted;
        public event EventHandler<DropTargetChangedEventArgs>? HoverTargetChanged;
        public event EventHandler<DragEndedEventArgs>? DragEnded;

        /// <summary>Starts a drag. Ignored if a drag is already in progress — re-entrant BeginDrag calls from an adapter are a no-op, not a reset.</summary>
        public void BeginDrag(IDraggable source)
        {
            if (Phase == DragPhase.Dragging) return;

            var payload = source.GetPayload();
            Phase = DragPhase.Dragging;
            CurrentSource = source;
            CurrentPayload = payload;
            CurrentHoverTarget = null;

            DragStarted?.Invoke(this, new DragStartedEventArgs(payload, source));
        }

        /// <summary>Called by an adapter whenever the pointer enters/leaves a candidate drop target while dragging. Pass null for "not currently over any drop target".</summary>
        public void UpdateHover(IDropTarget? target)
        {
            if (Phase != DragPhase.Dragging) return;
            if (ReferenceEquals(target, CurrentHoverTarget)) return;

            var previous = CurrentHoverTarget;
            CurrentHoverTarget = target;
            HoverTargetChanged?.Invoke(this, new DropTargetChangedEventArgs(previous, target));
        }

        /// <summary>Convenience for adapters rendering hover feedback (e.g. highlighting valid drop zones) without duplicating the accept check.</summary>
        public bool CanAcceptCurrentDrag(IDropTarget target) =>
            Phase == DragPhase.Dragging && CurrentPayload != null && target.CanAccept(CurrentPayload);

        /// <summary>Ends the drag over whatever's currently hovered. Drops onto the hover target if it accepts the payload, otherwise the drag is simply cancelled.</summary>
        public void EndDrag()
        {
            if (Phase != DragPhase.Dragging) return;

            var payload = CurrentPayload!;
            var target = CurrentHoverTarget;
            var wasDropped = false;

            if (target != null && target.CanAccept(payload))
            {
                target.OnDrop(payload);
                wasDropped = true;
            }

            DragEnded?.Invoke(this, new DragEndedEventArgs(payload, wasDropped, wasDropped ? target : null));
            Reset();
        }

        /// <summary>Explicit cancel (e.g. an adapter detects Escape pressed, or a pointer-capture loss) — always a non-drop, regardless of what's currently hovered.</summary>
        public void CancelDrag()
        {
            if (Phase != DragPhase.Dragging) return;

            var payload = CurrentPayload!;
            DragEnded?.Invoke(this, new DragEndedEventArgs(payload, false, null));
            Reset();
        }

        private void Reset()
        {
            Phase = DragPhase.None;
            CurrentPayload = null;
            CurrentSource = null;
            CurrentHoverTarget = null;
        }
    }
}
