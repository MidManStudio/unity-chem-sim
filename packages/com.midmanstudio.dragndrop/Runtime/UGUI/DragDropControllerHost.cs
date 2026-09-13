using UnityEngine;
using MidManStudio.DragDrop.Core;

namespace MidManStudio.DragDrop.UGUI
{
    /// <summary>
    /// Owns one <see cref="DragDropController"/> and makes it discoverable
    /// by any <see cref="UGuiDraggableSource"/> / <see cref="UGuiDropTargetZone"/>
    /// underneath it in the hierarchy. Put one on a Canvas (or any shared
    /// ancestor) to give everything below it one drag session; put a
    /// second one lower down to scope a separate drag session to just
    /// that subtree -- e.g. a HUD layout canvas and an inventory canvas
    /// each get their own controller by default, but a game that wants
    /// cross-system dragging (drag a HUD element onto an inventory slot)
    /// can put a single host above both instead.
    /// </summary>
    [DisallowMultipleComponent]
    public class DragDropControllerHost : MonoBehaviour
    {
        public DragDropController Controller { get; } = new DragDropController();

        /// <summary>
        /// Finds the nearest <see cref="DragDropControllerHost"/> above
        /// <paramref name="from"/> in the hierarchy. If none exists, adds
        /// one to the root Canvas so everything under that Canvas ends up
        /// sharing a single controller by default -- the common case of
        /// "one HUD canvas, one drag session". Falls back to adding a
        /// host directly onto <paramref name="from"/>'s GameObject if no
        /// Canvas ancestor exists either, so this never returns null.
        /// </summary>
        public static DragDropController Resolve(Component from)
        {
            var existing = from.GetComponentInParent<DragDropControllerHost>();
            if (existing != null) return existing.Controller;

            var canvas = from.GetComponentInParent<Canvas>();
            var hostGameObject = canvas != null ? canvas.gameObject : from.gameObject;

            var host = hostGameObject.GetComponent<DragDropControllerHost>();
            if (host == null) host = hostGameObject.AddComponent<DragDropControllerHost>();
            return host.Controller;
        }
    }
}
