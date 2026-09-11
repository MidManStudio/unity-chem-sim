using System;
using System.Collections.Generic;
using UnityEngine;
using MidManStudio.Questly.Generated;

namespace MidManStudio.Questly.Core
{
    [Serializable]
    public sealed class BakedQuestIdentity
    {
        public string id = string.Empty;
        public QuestType type;
        public string titleKey = string.Empty;
        public string giverId = string.Empty;
        public List<string> categoryTags = new();
    }

    [Serializable]
    public sealed class BakedObjective
    {
        public string id = string.Empty;
        public ObjectiveKind kind;
        public string labelKey = string.Empty;
        public float target;
        public float durationSeconds;
        public List<string> childIds = new();
        public string evaluatorKey = string.Empty;
        public string paramsJson = "{}";
    }

    [Serializable]
    public sealed class BakedPrereq
    {
        public PrereqKind kind;
        public string questId = string.Empty;
        public string objectiveId = string.Empty;
        public bool expected;
        public int minValue;
        public string evaluatorKey = string.Empty;
        public string paramsJson = "{}";
    }

    [Serializable]
    public sealed class BakedReward
    {
        public string rewardId = string.Empty;
        public string payloadJson = "{}";
    }

    [Serializable]
    public sealed class BakedQuest
    {
        public BakedQuestIdentity identity = new();
        public List<BakedObjective> objectives = new();
        public List<BakedPrereq> prereqs = new();
        public List<BakedReward> rewards = new();
    }

    /// <summary>
    /// Baked quest database for shipped builds — populated via Questly
    /// Studio's Bake tab (or <see cref="QuestDataBaker"/> directly) from a
    /// live <c>MdixDatabase</c>, then shipped as a plain ScriptableObject
    /// asset with no mdix-ffi/native dependency at runtime. Plain public
    /// fields throughout (not properties) — this is exactly what Unity's
    /// own serializer needs to see to persist it into a .asset file.
    /// </summary>
    public sealed class QuestDataAsset : ScriptableObject
    {
        public List<BakedQuest> quests = new();
    }
}
