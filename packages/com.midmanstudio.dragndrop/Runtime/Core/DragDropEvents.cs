using System;

namespace MidManStudio.DragDrop.Core
{
    public sealed class DragStartedEventArgs : EventArgs
    {
        public object Payload { get; }
        public IDraggable Source { get; }

        public DragStartedEventArgs(object payload, IDraggable source)
        {
            Payload = payload;
            Source = source;
        }
    }

    public sealed class DropTargetChangedEventArgs : EventArgs
    {
        public IDropTarget? Previous { get; }
        public IDropTarget? Current { get; }

        public DropTargetChangedEventArgs(IDropTarget? previous, IDropTarget? current)
        {
            Previous = previous;
            Current = current;
        }
    }

    public sealed class DragEndedEventArgs : EventArgs
    {
        public object Payload { get; }
        public bool WasDropped { get; }
        public IDropTarget? Target { get; }

        public DragEndedEventArgs(object payload, bool wasDropped, IDropTarget? target)
        {
            Payload = payload;
            WasDropped = wasDropped;
            Target = target;
        }
    }
}
