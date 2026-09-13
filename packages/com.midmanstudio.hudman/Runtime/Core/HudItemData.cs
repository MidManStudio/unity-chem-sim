using MidManStudio.HudMan.Generated;

namespace MidManStudio.HudMan.Core
{
    /// <summary>
    /// Per-item HUD state: where it is, how big, how visible. Position and
    /// scale are stored as flat floats rather than a Vector2 -- this is a
    /// Core type with zero UnityEngine dependency (same boundary
    /// com.midmanstudio.dragndrop draws around its own Core), and
    /// MdixSerializer has no native Vector2 support either, so this is
    /// the right shape for both reasons rather than a compromise for
    /// either one. <see cref="MidManStudio.HudMan.UnityBinding.HudItemView"/>
    /// is the thin adapter that reads/writes this to and from a real
    /// RectTransform/CanvasGroup.
    /// </summary>
    public sealed class HudItemData
    {
        public HudItemType ItemType { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public float Opacity { get; set; } = 1f;
        public bool IsVisible { get; set; } = true;

        public HudItemData() { }

        public HudItemData(
            HudItemType itemType,
            float positionX, float positionY,
            float scaleX, float scaleY,
            float opacity, bool isVisible)
        {
            ItemType = itemType;
            PositionX = positionX;
            PositionY = positionY;
            ScaleX = scaleX;
            ScaleY = scaleY;
            Opacity = opacity;
            IsVisible = isVisible;
        }
    }
}
