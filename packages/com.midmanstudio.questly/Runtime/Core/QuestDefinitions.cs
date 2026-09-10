using System.Collections.Generic;
using MidManStudio.Questly.Generated;

namespace MidManStudio.Questly.Core
{
    /// <summary>
    /// Mirrors <c>core/builders.mdix</c>'s <c>createIdentity(...)</c> shape.
    /// Property names map to mdix fields via MdixSerializer's default
    /// PascalCase-to-snake_case convention (<c>TitleKey</c> -&gt;
    /// <c>title_key</c>), so no <c>[MdixProperty]</c> overrides are needed.
    /// </summary>
    public sealed class QuestIdentity
    {
        public string Id { get; set; } = string.Empty;
        public QuestType Type { get; set; }
        public string TitleKey { get; set; } = string.Empty;

        /// <summary>Opaque host-defined string (NPC id, faction id, or "" for none). Questly never interprets this.</summary>
        public string GiverId { get; set; } = string.Empty;

        /// <summary>Free-form host taxonomy layered on top of <see cref="Type"/> (e.g. "richard_arc", "heist").</summary>
        public List<string> CategoryTags { get; set; } = new();
    }

    /// <summary>
    /// Mirrors <c>builders.objective(...)</c> — one fixed shape for every
    /// <see cref="ObjectiveKind"/>; fields unused by a given kind sit at
    /// their neutral default.
    /// </summary>
    public sealed class ObjectiveDefinition
    {
        public string Id { get; set; } = string.Empty;
        public ObjectiveKind Kind { get; set; }
        public string LabelKey { get; set; } = string.Empty;

        public float Target { get; set; }
        public float DurationSeconds { get; set; }
        public List<string> ChildIds { get; set; } = new();
        public string EvaluatorKey { get; set; } = string.Empty;

        /// <summary>
        /// Raw JSON for the opaque <c>params</c> object. MdixSerializer's
        /// automatic mapping doesn't cover a fully dynamic <c>object</c>
        /// field, so <see cref="Core.MdixQuestTable"/> fills this in
        /// manually via <c>GetJson</c> after the main deserialize pass.
        /// </summary>
        public string ParamsJson { get; set; } = "{}";
    }

    /// <summary>Mirrors <c>builders.prereq(...)</c> — same "one shape, neutral defaults" rule as objectives.</summary>
    public sealed class PrereqDefinition
    {
        public PrereqKind Kind { get; set; }
        public string QuestId { get; set; } = string.Empty;
        public string ObjectiveId { get; set; } = string.Empty;
        public bool Expected { get; set; }
        public int MinValue { get; set; }
        public string EvaluatorKey { get; set; } = string.Empty;
        public string ParamsJson { get; set; } = "{}";
    }

    /// <summary>
    /// Mirrors <c>builders.reward(...)</c>. <see cref="RewardId"/> and the
    /// payload are entirely host-defined — Questly stores and hands these
    /// back on completion, it never interprets them.
    /// </summary>
    public sealed class RewardDefinition
    {
        public string RewardId { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
    }

    /// <summary>One fully-loaded quest: identity plus its objectives/prereqs/rewards.</summary>
    public sealed class QuestDefinition
    {
        public QuestIdentity Identity { get; }
        public IReadOnlyList<ObjectiveDefinition> Objectives { get; }
        public IReadOnlyList<PrereqDefinition> Prereqs { get; }
        public IReadOnlyList<RewardDefinition> Rewards { get; }

        public QuestDefinition(
            QuestIdentity identity,
            IReadOnlyList<ObjectiveDefinition> objectives,
            IReadOnlyList<PrereqDefinition> prereqs,
            IReadOnlyList<RewardDefinition> rewards)
        {
            Identity = identity;
            Objectives = objectives;
            Prereqs = prereqs;
            Rewards = rewards;
        }
    }
}
