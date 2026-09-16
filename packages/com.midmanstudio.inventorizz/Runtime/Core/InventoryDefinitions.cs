using System.Collections.Generic;
using MidManStudio.Inventorizz.Generated;

namespace MidManStudio.Inventorizz.Core
{
    /// <summary>
    /// Mirrors <c>builders.createItemDefinition(...)</c>. Item taxonomy
    /// (potion/ore/contraband/whatever) deliberately isn't an enum -- it
    /// lives in <see cref="Tags"/>, the same "structural concepts only get
    /// an enum, domain concepts stay free-form tags" rule Questly's
    /// QuestType/CategoryTags split already follows.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayKey { get; set; } = string.Empty;
        public float Weight { get; set; }

        /// <summary>Meaningless for UNIQUE items -- always 1 there by the schema's own documented convention, never 0.</summary>
        public int MaxStack { get; set; } = 1;
        public StackPolicy StackPolicy { get; set; } = StackPolicy.STACKABLE;
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Mirrors <c>builders.createContainerType(...)</c>. A TYPE/template
    /// (e.g. "backpack"), not an instance -- <see cref="ContainerInstanceRow"/>
    /// is the runtime-created, per-owner thing an inventorizz host actually
    /// creates and mutates. <see cref="MaxWeight"/>/<see cref="MaxSlots"/>
    /// are neutral-zeroed for whichever axis <see cref="CapacityKind"/>
    /// doesn't use, same convention as every other definition family in
    /// this ecosystem.
    /// </summary>
    public sealed class ContainerTypeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayKey { get; set; } = string.Empty;
        public CapacityKind CapacityKind { get; set; } = CapacityKind.UNLIMITED;
        public float MaxWeight { get; set; }
        public int MaxSlots { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
