using System.Collections.Generic;
using MidManStudio.Questly.Generated;

namespace MidManStudio.Questly.Core
{
    /// <summary>Mirrors <c>builders.questProgress(...)</c>.</summary>
    public sealed class QuestProgressRow
    {
        public string QuestId { get; set; } = string.Empty;
        public QuestState State { get; set; }
    }

    /// <summary>Mirrors <c>builders.questObjectiveProgress(...)</c>.</summary>
    public sealed class QuestObjectiveProgressRow
    {
        public string QuestId { get; set; } = string.Empty;
        public string ObjectiveId { get; set; } = string.Empty;
        public ObjectiveState State { get; set; }
        public float Value { get; set; }
    }

    /// <summary>
    /// Host-owned persistence payload. <see cref="QuestRuntime.CaptureState"/>
    /// returns this, <see cref="QuestRuntime.RestoreState"/> takes one back.
    /// A plain POCO, not a file — the host folds it into whatever save blob
    /// it already has. Questly never opens a file on this path.
    /// </summary>
    public sealed class QuestProgressSnapshot
    {
        public List<QuestProgressRow> Quests { get; set; } = new();
        public List<QuestObjectiveProgressRow> Objectives { get; set; } = new();
    }

    /// <summary>
    /// Nested so serialization naturally produces the dotted
    /// <c>progress.quests</c> / <c>progress.objectives</c> paths — a nested
    /// C# object maps to a nested mdix path the same way <c>identity.core</c>
    /// does in the quest-definition schema.
    /// </summary>
    public sealed class QuestProgressGroup
    {
        public List<QuestProgressRow> Quests { get; set; } = new();
        public List<QuestObjectiveProgressRow> Objectives { get; set; } = new();
    }

    /// <summary>
    /// Package-owned persistence format — matches
    /// <c>Samples~/example_progress_save.mdix</c> field-for-field
    /// (<c>save_slot</c> / <c>saved_at</c> / <c>progress.quests::</c> /
    /// <c>progress.objectives::</c>). Only <see cref="QuestRuntime.SaveToMdix"/>
    /// / <see cref="QuestRuntime.LoadFromMdix"/> touch this type — the
    /// host-owned snapshot path above never does.
    /// </summary>
    public sealed class QuestProgressSaveFile
    {
        public string SaveSlot { get; set; } = string.Empty;

        /// <summary>
        /// Raw ISO-8601 text rather than a native timestamp type: this
        /// keeps the save-file POCO independent of exactly how
        /// MdixSerializer round-trips its own <c>MdixTimestamp</c> type,
        /// which wasn't part of the API surface actually exercised while
        /// writing this skeleton.
        /// </summary>
        public string SavedAt { get; set; } = string.Empty;

        public QuestProgressGroup Progress { get; set; } = new();
    }
}
