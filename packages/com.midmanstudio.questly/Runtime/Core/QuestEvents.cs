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
}
