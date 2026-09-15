using System;
using System.Collections.Generic;
using MidManStudio.Questly.Generated;

namespace MidManStudio.Questly.Core
{
    public sealed class QuestStateChangedEventArgs : EventArgs
    {
        public string QuestId { get; }
        public QuestState PreviousState { get; }
        public QuestState NewState { get; }

        public QuestStateChangedEventArgs(string questId, QuestState previousState, QuestState newState)
        {
            QuestId = questId;
            PreviousState = previousState;
            NewState = newState;
        }
    }

    public sealed class QuestObjectiveProgressEventArgs : EventArgs
    {
        public string QuestId { get; }
        public string ObjectiveId { get; }
        public float Value { get; }
        public ObjectiveState State { get; }

        public QuestObjectiveProgressEventArgs(string questId, string objectiveId, float value, ObjectiveState state)
        {
            QuestId = questId;
            ObjectiveId = objectiveId;
            Value = value;
            State = state;
        }
    }

    public sealed class QuestCompletedEventArgs : EventArgs
    {
        public string QuestId { get; }
        public IReadOnlyList<RewardDefinition> Rewards { get; }

        public QuestCompletedEventArgs(string questId, IReadOnlyList<RewardDefinition> rewards)
        {
            QuestId = questId;
            Rewards = rewards;
        }
    }

    /// <summary>Fired by <see cref="QuestRuntime.TryClaimReward"/> the moment a reward is marked claimed -- this is the hook a host's economy/inventory system reacts to when claiming is UI-driven rather than automatic on completion.</summary>
    public sealed class RewardClaimedEventArgs : EventArgs
    {
        public string QuestId { get; }
        public RewardDefinition Reward { get; }

        public RewardClaimedEventArgs(string questId, RewardDefinition reward)
        {
            QuestId = questId;
            Reward = reward;
        }
    }

    /// <summary>
    /// Fired by <see cref="QuestRuntime.TryClaimCycleReward"/> -- the
    /// per-cycle-track counterpart to <see cref="RewardClaimedEventArgs"/>,
    /// used by a RECURRING quest with <c>StopAfterFirstCompletion</c> false
    /// (e.g. a capped login-streak track). Never fired for the ordinary
    /// whole-quest reward-claim path, and vice versa.
    /// </summary>
    public sealed class CycleRewardClaimedEventArgs : EventArgs
    {
        public string QuestId { get; }
        public int CycleIndex { get; }
        public RewardDefinition Reward { get; }

        public CycleRewardClaimedEventArgs(string questId, int cycleIndex, RewardDefinition reward)
        {
            QuestId = questId;
            CycleIndex = cycleIndex;
            Reward = reward;
        }
    }
}
