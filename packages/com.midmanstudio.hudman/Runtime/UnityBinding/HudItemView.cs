using UnityEngine;
using MidManStudio.HudMan.Core;
using MidManStudio.HudMan.Generated;
using System;

namespace MidManStudio.HudMan.UnityBinding
{
    /// <summary>
    /// Binds one HudItemType to an actual RectTransform/CanvasGroup and
    /// keeps a HudLayoutRuntime in sync with it. Direct replacement for
    /// what HudDragDropItem.ApplyData/GetCurrentData and
    /// HudItemLoader.ApplyConfiguration used to hand-roll per element.
    ///
    /// Call order matters: call <see cref="Bind"/> for every HudItemView
    /// in the scene BEFORE calling <see cref="HudLayoutRuntime.LoadFromMdix"/>
    /// or <see cref="HudLayoutRuntime.RestoreState"/> on the runtime they
    /// share. Loaded rows apply to whatever's already registered -- a row
    /// for a type nothing has bound yet is skipped, not queued, which is
    /// the deliberate alternative to the retry-coroutine approach the
    /// original game code used to paper over the same ordering problem.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HudItemView : MonoBehaviour
    {
        [SerializeField] private HudItemType itemType;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private HudLayoutRuntime _runtime;

        public HudItemType ItemType => itemType;

        /// <summary>
        /// Null until <see cref="Bind"/> has actually been called. A
        /// sibling component (e.g. <see cref="HudItemDraggableAdapter"/>)
        /// whose own Awake() may run before or after Bind() should
        /// subscribe to <see cref="Bound"/> rather than read this directly
        /// in its own Awake().
        /// </summary>
        public HudLayoutRuntime Runtime => _runtime;

        /// <summary>Fired once, at the end of <see cref="Bind"/>, once <see cref="Runtime"/> is safe to read.</summary>
        public event EventHandler? Bound;

        public void Bind(HudLayoutRuntime runtime)
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _runtime = runtime;
            _runtime.Register(itemType, Capture());
            _runtime.ItemDataChanged += OnRuntimeItemDataChanged;
            Bound?.Invoke(this, EventArgs.Empty);
        }

        private void OnDestroy()
        {
            if (_runtime != null) _runtime.ItemDataChanged -= OnRuntimeItemDataChanged;
        }

        /// <summary>Call after a drag ends, or after a scale/opacity control changes, to push the current on-screen state back into the runtime. HudItemDraggableAdapter already does this automatically after a drag.</summary>
        public void ReportCurrentState()
        {
            if (_runtime != null) _runtime.SetData(itemType, Capture());
        }

        private void OnRuntimeItemDataChanged(object sender, HudItemChangedEventArgs e)
        {
            if (e.ItemType == itemType) Apply(e.Data);
        }

        private HudItemData Capture() => new(
            itemType,
            _rectTransform.anchoredPosition.x,
            _rectTransform.anchoredPosition.y,
            _rectTransform.localScale.x,
            _rectTransform.localScale.y,
            _canvasGroup.alpha,
            gameObject.activeInHierarchy);

        private void Apply(HudItemData data)
        {
            _rectTransform.anchoredPosition = new Vector2(data.PositionX, data.PositionY);
            _rectTransform.localScale = new Vector3(data.ScaleX, data.ScaleY, 1f);
            _canvasGroup.alpha = data.Opacity;
            gameObject.SetActive(data.IsVisible);
        }
    }
}
