using UnityEngine;
using MidManStudio.DragDrop.Core;
using MidManStudio.DragDrop.UGUI;

namespace MidManStudio.HudMan.UnityBinding
{
    /// <summary>
    /// Plugs a <see cref="HudItemView"/> into com.midmanstudio.dragndrop's
    /// drag mechanic. HUD layout editing has no "drop target" concept --
    /// an item is just repositioned freely within canvas bounds, which
    /// <see cref="UGuiDraggableSource"/> already does unconditionally in
    /// OnDrag regardless of what EndDrag eventually resolves to -- so the
    /// payload here is inert. It exists to satisfy IDraggable's contract,
    /// not because anything inspects it; a game wanting snap-to-slot HUD
    /// editing later can register its own IDropTarget zones and start
    /// using this same payload for real without touching this class.
    /// </summary>
    [RequireComponent(typeof(HudItemView))]
    [RequireComponent(typeof(UGuiDraggableSource))]
    public class HudItemDraggableAdapter : MonoBehaviour, IDraggable
    {
        private HudItemView _view;
        private DragDropController _controller;

        private void Awake()
        {
            _view = GetComponent<HudItemView>();
            _controller = DragDropControllerHost.Resolve(this);
            _controller.DragEnded += OnDragEnded;
        }

        private void OnDestroy()
        {
            if (_controller != null) _controller.DragEnded -= OnDragEnded;
        }

        public object GetPayload() => _view;

        private void OnDragEnded(object sender, DragEndedEventArgs e)
        {
            if (ReferenceEquals(e.Payload, _view)) _view.ReportCurrentState();
        }
    }
}
