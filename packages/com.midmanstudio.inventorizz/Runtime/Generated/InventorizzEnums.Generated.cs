// AUTO-GENERATED (hand-traced) from Samples~/core/enums.mdix's @ENUMS block
// by MdixEnumCodeGenerator's real, deterministic algorithm -- confirmed
// against the same generator source already verified for Questly's own
// generated enums file. This package's native `dixscript-cli` build target
// (Rust 1.80+, DixScript-Rust) can't run in this sandbox (capped ~1.75),
// so this file is traced by hand from the real source text rather than
// invented. Re-run the real generator and diff against this file before
// trusting it beyond what's stated here.
//
// StackPolicy/CapacityKind are plain closed @ENUMS entries, not the
// ScriptableObject-provider-generated kind (contrast HudItemType /
// SettingsCategory) -- neither is a per-game catalog a consuming game
// needs to extend with its own values, both are structural/mechanical
// concepts owned entirely by this package, so the simpler 1:1 @ENUMS
// mirror is the correct tool here, not the provider pattern.

namespace MidManStudio.Inventorizz.Generated
{
    public enum StackPolicy
    {
        STACKABLE,
        UNIQUE
    }

    public enum CapacityKind
    {
        UNLIMITED,
        WEIGHT,
        SLOT_COUNT,
        WEIGHT_AND_SLOTS
    }
}
