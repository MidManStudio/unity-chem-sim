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

    /// <summary>
    /// Mirrors <c>builders.schedule(...)</c>. Optional per quest -- a quest
    /// with no <c>quests.{id}.schedule</c> path gets <see cref="Kind"/>
    /// <see cref="ScheduleKind.NONE"/> by <see cref="MdixQuestTable"/>'s
    /// missing-path convention, and behaves exactly as an unscheduled quest
    /// always has. Time is an opaque host-fed number (see
    /// <see cref="QuestRuntime.AdvanceTime"/>) -- Questly never assumes an
    /// epoch or a unit, same as <see cref="ObjectiveDefinition.DurationSeconds"/>
    /// already doesn't.
    /// </summary>
    public sealed class ScheduleDefinition
    {
        public ScheduleKind Kind { get; set; } = ScheduleKind.NONE;

        /// <summary>
        /// ONE_SHOT: absolute time the single window opens. RECURRING: the
        /// first cycle's window-open time (an anchor); later cycles open at
        /// <c>WindowStart + n * RecurrenceInterval</c>.
        /// </summary>
        public double WindowStart { get; set; }

        /// <summary>How long a window stays open, from its own open time. Unused when Kind is NONE.</summary>
        public double WindowDuration { get; set; }

        /// <summary>Time between cycle starts. RECURRING only; neutral 0 otherwise.</summary>
        public double RecurrenceInterval { get; set; }

        /// <summary>
        /// RECURRING only. True: the quest stops recurring for good the
        /// first time it's ever COMPLETED (its window-based re-arming logic
        /// goes permanently inert from then on). False: it keeps recurring
        /// after every completion, each cycle counted independently via
        /// <see cref="QuestRuntime.GetCyclesCompleted"/> -- the shape a
        /// capped, cycle-indexed reward track (see
        /// <see cref="QuestRuntime.CanClaimCycleReward"/>) is built on.
        /// Neutral false when Kind isn't RECURRING.
        /// </summary>
        public bool StopAfterFirstCompletion { get; set; }
    }

    /// <summary>One fully-loaded quest: identity plus its objectives/prereqs/rewards/schedule.</summary>
    public sealed class QuestDefinition
    {
        public QuestIdentity Identity { get; }
        public IReadOnlyList<ObjectiveDefinition> Objectives { get; }
        public IReadOnlyList<PrereqDefinition> Prereqs { get; }
        public IReadOnlyList<RewardDefinition> Rewards { get; }

        /// <summary>Never null -- an unscheduled quest gets a default instance with <see cref="ScheduleDefinition.Kind"/> NONE, same "always present, neutral default" rule as every other field family here.</summary>
        public ScheduleDefinition Schedule { get; }

        public QuestDefinition(
            QuestIdentity identity,
            IReadOnlyList<ObjectiveDefinition> objectives,
            IReadOnlyList<PrereqDefinition> prereqs,
            IReadOnlyList<RewardDefinition> rewards,
            ScheduleDefinition? schedule = null)
        {
            Identity = identity;
            Objectives = objectives;
            Prereqs = prereqs;
            Rewards = rewards;
            Schedule = schedule ?? new ScheduleDefinition();
        }
    }
}
