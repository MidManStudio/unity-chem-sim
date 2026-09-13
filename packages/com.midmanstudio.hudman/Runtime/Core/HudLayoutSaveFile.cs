using System.Collections.Generic;

namespace MidManStudio.HudMan.Core
{
    /// <summary>
    /// Host-owned persistence payload. <see cref="HudLayoutRuntime.CaptureState"/>
    /// returns this, <see cref="HudLayoutRuntime.RestoreState"/> takes one
    /// back. A plain POCO, not a file -- the host folds it into whatever
    /// save blob it already has, exactly the same relationship
    /// QuestProgressSnapshot has with QuestRuntime.
    /// </summary>
    public sealed class HudLayoutSnapshot
    {
        public string LayoutName { get; set; } = string.Empty;
        public List<HudItemData> Items { get; set; } = new();
    }

    /// <summary>
    /// Nested so serialization naturally produces dotted
    /// <c>layout.items</c> paths, the same reason QuestProgressGroup
    /// exists nested inside QuestProgressSaveFile.
    /// </summary>
    public sealed class HudLayoutGroup
    {
        public List<HudItemData> Items { get; set; } = new();
    }

    /// <summary>
    /// Package-owned persistence format. Only
    /// <see cref="HudLayoutRuntime.SaveToMdix"/> / <see cref="HudLayoutRuntime.LoadFromMdix"/>
    /// touch this type -- the host-owned snapshot path above never does.
    /// See <c>Samples~/example_layout_save.mdix</c> for the resulting
    /// on-disk shape.
    /// </summary>
    public sealed class HudLayoutSaveFile
    {
        public string LayoutName { get; set; } = string.Empty;

        /// <summary>
        /// Raw ISO-8601 text rather than a native timestamp type -- same
        /// call QuestProgressSaveFile.SavedAt makes, and for the same
        /// reason: keeps this POCO independent of exactly how
        /// MdixSerializer round-trips its own MdixTimestamp type.
        /// </summary>
        public string SavedAt { get; set; } = string.Empty;

        public HudLayoutGroup Layout { get; set; } = new();
    }
}
