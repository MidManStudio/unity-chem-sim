using System;
using UnityEngine;
using MidManStudio.DragDrop.Core;
using MidManStudio.DragDrop.UGUI;
using MidManStudio.HudMan.Core;

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
    ///
    /// Dragging is only actually possible while
    /// <see cref="HudLayoutRuntime.IsEditMode"/> is true -- enforced by
    /// disabling the sibling <see cref="UGuiDraggableSource"/> component,
    /// which is what actually stops Unity's EventSystem from ever calling
    /// OnBeginDrag on it. Off by default, since a shipped game should
    /// never let a player reposition their HUD outside of an explicit
    /// edit mode (e.g. gensettings' "editHUD" button example). Because
    /// <see cref="HudItemView.Bind"/> is called explicitly by host
    /// bootstrap code rather than from its own Awake() -- and so may run
    /// before or after this component's Awake() -- the runtime reference
    /// is picked up via <see cref="HudItemView.Bound"/> rather than read
    /// directly here.
    /// </summary>
    [RequireComponent(typeof(HudItemView))]
    [RequireComponent(typeof(UGuiDraggableSource))]
    public class HudItemDraggableAdapter : MonoBehaviour, IDraggable
    {
        private HudItemView _view;
        private UGuiDraggableSource _draggableSource;
        private DragDropController _controller;
        private HudLayoutRuntime _runtime;

        private void Awake()
        {
            _view = GetComponent<HudItemView>();
            _draggableSource = GetComponent<UGuiDraggableSource>();
            _controller = DragDropControllerHost.Resolve(this);
            _controller.DragEnded += OnDragEnded;

            _draggableSource.enabled = false; // stays off until we know IsEditMode is actually true
            _view.Bound += OnViewBound;
        }

        private void OnViewBound(object sender, EventArgs e)
        {
            _runtime = _view.Runtime;
            _runtime.EditModeChanged += OnEditModeChanged;
            _draggableSource.enabled = _runtime.IsEditMode; // picks up edit mode already being on, e.g. a HUD item added while the editor is already open
        }

        private void OnEditModeChanged(object sender, HudEditModeChangedEventArgs e)
        {
            _draggableSource.enabled = e.IsEditMode;

            // Editing switched off mid-drag on this exact item -- don't leave
            // it stuck (raycasts disabled, alpha dimmed) waiting for a
            // pointer-up that may never come once the drag surface itself
            // just got disabled; cancel cleanly instead.
            if (!e.IsEditMode && ReferenceEquals(_controller.CurrentSource, this))
                _controller.CancelDrag();
        }

        private void OnDestroy()
        {
            if (_controller != null) _controller.DragEnded -= OnDragEnded;
            if (_view != null) _view.Bound -= OnViewBound;
            if (_runtime != null) _runtime.EditModeChanged -= OnEditModeChanged;
        }

        public object GetPayload() => _view;

        private void OnDragEnded(object sender, DragEndedEventArgs e)
        {
            if (ReferenceEquals(e.Payload, _view)) _view.ReportCurrentState();
        }
    }
}

