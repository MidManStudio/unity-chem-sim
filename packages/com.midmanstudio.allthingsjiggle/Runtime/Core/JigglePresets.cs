// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JigglePresets.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>The built in presets: starting values for the kinds of chain a character usually has.</summary>
    public enum JigglePresetKind
    {
        /// <summary>A strand of hair: loose, a low swing limit, a lot of damping.</summary>
        Hair = 0,
        /// <summary>A tail: long and smooth, coupled so its weight drags it.</summary>
        Tail = 1,
        /// <summary>An ear or another short flap: springy, quick to come back.</summary>
        Ear = 2,
        /// <summary>The chest: a short chain with a strong pull to the pose and a low swing limit, coupled.</summary>
        Chest = 3,
        /// <summary>A horn: nearly rigid, moves only a little and mostly when hit.</summary>
        Horn = 4,
        /// <summary>A wing: large, floppy, takes a lot of wind.</summary>
        Wing = 5
    }

    /// <summary>
    /// The values of one preset. They follow <see cref="JiggleParams"/> and add what makes them independent of the step
    /// rate: the two frictions are given per second (see <see cref="JiggleDamping"/>) and become per step only when the
    /// values are turned into parameters. <see cref="StiffnessAtTip"/> scales the stiffness of the last bone against the
    /// first, with a straight line between them.
    /// </summary>
    [Serializable]
    public struct JigglePresetValues
    {
        /// <summary>Pull to the pose of the first bone, 0 to 1.</summary>
        public float Stiffness;
        /// <summary>Multiplier on the stiffness at the last bone. 1 is the same all along, below 1 loosens the tip.</summary>
        public float StiffnessAtTip;
        /// <summary>How much the pull fades near the pose, 0 to 1.</summary>
        public float Soften;
        /// <summary>How far the first bone may move from its pose position, 0 to 1.</summary>
        public float RootStretch;
        /// <summary>Whether the angle limit applies.</summary>
        public bool AngleLimited;
        /// <summary>Largest swing from the pose, where 1 is 90 degrees.</summary>
        public float AngleLimit;
        /// <summary>How softly the angle limit corrects, 0 to 1.</summary>
        public float AngleLimitSoften;
        /// <summary>How much a bone's length may change, 0 to 1.</summary>
        public float Stretch;
        /// <summary>Friction against the parent, as the share of the motion lost in one second.</summary>
        public float DampingPerSecond;
        /// <summary>Friction against moving through the world, as the share of the motion lost in one second.</summary>
        public float AirDampingPerSecond;
        /// <summary>Multiplier on gravity.</summary>
        public float Gravity;
        /// <summary>Share of the swing applied to the rotation, 0 to 1.</summary>
        public float Blend;
        /// <summary>Share of the anchor's motion that carries the bones along, 0 to 1.</summary>
        public float IgnoreRootMotion;
        /// <summary>Relative mass for two way coupling, 0 for none.</summary>
        public float Mass;
        /// <summary>Whether the bones collide.</summary>
        public bool Collide;
        /// <summary>Thickness of a bone for collisions, in meters.</summary>
        public float CollisionRadius;
        /// <summary>Whether the whole bone collides and not only its end.</summary>
        public bool SegmentCollision;
        /// <summary>Multiplier on the acceleration of fields.</summary>
        public float WindResponse;
        /// <summary>Multiplier on hits and impulses.</summary>
        public float ImpulseResponse;
        /// <summary>How far past the last bone the virtual end sits, in bone lengths.</summary>
        public float TipScale;
    }

    /// <summary>
    /// The built in presets and what turns them into solver parameters. The numbers are starting points. The hair and chest
    /// ones follow the technique level findings of the MMD rig study in the crate documentation: a swing limit near 20
    /// degrees and a damping of 0.999 per second for hair, a low limit with a strong pull to the pose for the chest. The
    /// others are reasoned from what the kind of chain does. None of them has been judged by eye, so tune them in the lab.
    /// It has no Unity references.
    /// </summary>
    public static class JigglePresets
    {
        /// <summary>Number of built in presets.</summary>
        public const int Count = 6;

        /// <summary>The name of a preset, as the inspector shows it.</summary>
        public static string Name(JigglePresetKind kind)
        {
            switch (kind)
            {
                case JigglePresetKind.Hair:
                    return "Hair";
                case JigglePresetKind.Tail:
                    return "Tail";
                case JigglePresetKind.Ear:
                    return "Ear";
                case JigglePresetKind.Chest:
                    return "Chest";
                case JigglePresetKind.Horn:
                    return "Horn";
                case JigglePresetKind.Wing:
                    return "Wing";
                default:
                    return "Hair";
            }
        }

        /// <summary>A preset with the values every kind starts from. The kinds change what they need.</summary>
        private static JigglePresetValues Base()
        {
            JigglePresetValues v = new JigglePresetValues();
            v.Stiffness = 0.5f;
            v.StiffnessAtTip = 1.0f;
            v.Soften = 0.0f;
            v.RootStretch = 0.0f;
            v.AngleLimited = true;
            v.AngleLimit = 0.5f;
            v.AngleLimitSoften = 0.3f;
            v.Stretch = 0.05f;
            v.DampingPerSecond = 0.995f;
            v.AirDampingPerSecond = 0.4f;
            v.Gravity = 1.0f;
            v.Blend = 1.0f;
            v.IgnoreRootMotion = 0.0f;
            v.Mass = 0.0f;
            v.Collide = false;
            v.CollisionRadius = 0.03f;
            v.SegmentCollision = false;
            v.WindResponse = 1.0f;
            v.ImpulseResponse = 1.0f;
            v.TipScale = 1.0f;
            return v;
        }

        /// <summary>The values of a built in preset. A kind that does not exist gives the hair preset.</summary>
        public static JigglePresetValues Get(JigglePresetKind kind)
        {
            JigglePresetValues v = Base();
            switch (kind)
            {
                case JigglePresetKind.Tail:
                    v.Stiffness = 0.45f;
                    v.StiffnessAtTip = 0.55f;
                    v.Soften = 0.1f;
                    v.AngleLimit = 0.55f;
                    v.Stretch = 0.08f;
                    v.DampingPerSecond = 0.995f;
                    v.AirDampingPerSecond = 0.4f;
                    v.Gravity = 1.0f;
                    v.Mass = 1.0f;
                    v.Collide = true;
                    v.CollisionRadius = 0.04f;
                    v.WindResponse = 1.2f;
                    break;
                case JigglePresetKind.Ear:
                    v.Stiffness = 0.7f;
                    v.StiffnessAtTip = 0.8f;
                    v.Soften = 0.1f;
                    v.AngleLimit = 0.35f;
                    v.AngleLimitSoften = 0.2f;
                    v.Stretch = 0.03f;
                    v.DampingPerSecond = 0.99f;
                    v.AirDampingPerSecond = 0.3f;
                    v.Gravity = 0.6f;
                    v.WindResponse = 0.8f;
                    v.ImpulseResponse = 1.3f;
                    break;
                case JigglePresetKind.Chest:
                    v.Stiffness = 0.85f;
                    v.StiffnessAtTip = 1.0f;
                    v.RootStretch = 0.15f;
                    v.AngleLimit = 0.12f;
                    v.AngleLimitSoften = 0.4f;
                    v.Stretch = 0.02f;
                    v.DampingPerSecond = 0.9995f;
                    v.AirDampingPerSecond = 0.3f;
                    v.Gravity = 0.5f;
                    v.Mass = 1.0f;
                    v.WindResponse = 0.2f;
                    v.ImpulseResponse = 1.3f;
                    break;
                case JigglePresetKind.Horn:
                    v.Stiffness = 0.97f;
                    v.StiffnessAtTip = 1.0f;
                    v.AngleLimit = 0.05f;
                    v.AngleLimitSoften = 0.5f;
                    v.Stretch = 0.0f;
                    v.DampingPerSecond = 0.999f;
                    v.AirDampingPerSecond = 0.5f;
                    v.Gravity = 0.1f;
                    v.WindResponse = 0.1f;
                    v.ImpulseResponse = 0.6f;
                    break;
                case JigglePresetKind.Wing:
                    v.Stiffness = 0.4f;
                    v.StiffnessAtTip = 0.6f;
                    v.Soften = 0.2f;
                    v.AngleLimit = 0.6f;
                    v.Stretch = 0.05f;
                    v.DampingPerSecond = 0.995f;
                    v.AirDampingPerSecond = 0.6f;
                    v.Gravity = 0.8f;
                    v.WindResponse = 2.0f;
                    break;
                default:
                    // Hair: loose, a swing limit near 20 degrees, damping of 0.999 per second, and a little more gravity
                    // than 1 because the slow swing of a weighted strand is made with gravity and drag in a one way solver.
                    v.Stiffness = 0.3f;
                    v.StiffnessAtTip = 0.7f;
                    v.Soften = 0.15f;
                    v.AngleLimit = 0.22f;
                    v.AngleLimitSoften = 0.3f;
                    v.Stretch = 0.05f;
                    v.DampingPerSecond = 0.999f;
                    v.AirDampingPerSecond = 0.5f;
                    v.Gravity = 1.4f;
                    v.Collide = true;
                    v.CollisionRadius = 0.02f;
                    break;
            }
            return v;
        }

        private static float Clamp01(float v)
        {
            if (!JiggleMath.IsFinite(v) || v < 0.0f)
            {
                return 0.0f;
            }
            return v > 1.0f ? 1.0f : v;
        }

        private static float NonNegative(float v)
        {
            return (JiggleMath.IsFinite(v) && v > 0.0f) ? v : 0.0f;
        }

        /// <summary>
        /// The parameters of bone <paramref name="bone"/> (0 is the first) of a chain of <paramref name="bones"/> bones, for a
        /// solver that steps <paramref name="fixedHz"/> times a second. Every value is clamped into its range.
        /// </summary>
        public static JiggleParams ToParams(in JigglePresetValues v, int bone, int bones, float fixedHz)
        {
            float along = bones > 1 ? (float)bone / (float)(bones - 1) : 0.0f;
            float tip = NonNegative(v.StiffnessAtTip);
            float scale = 1.0f + (tip - 1.0f) * along;
            JiggleParams p = JiggleParams.Default();
            p.Stiffness = Clamp01(v.Stiffness * scale);
            p.Soften = Clamp01(v.Soften);
            p.RootStretch = Clamp01(v.RootStretch);
            p.AngleLimited = v.AngleLimited;
            p.AngleLimit = Clamp01(v.AngleLimit);
            p.AngleLimitSoften = Clamp01(v.AngleLimitSoften);
            p.Stretch = Clamp01(v.Stretch);
            p.Drag = JiggleDamping.PerStep(v.DampingPerSecond, fixedHz);
            p.AirDrag = JiggleDamping.PerStep(v.AirDampingPerSecond, fixedHz);
            p.Gravity = JiggleMath.IsFinite(v.Gravity) ? v.Gravity : 1.0f;
            p.Blend = Clamp01(v.Blend);
            p.IgnoreRootMotion = Clamp01(v.IgnoreRootMotion);
            p.Mass = NonNegative(v.Mass);
            p.Collide = v.Collide;
            p.CollisionRadius = NonNegative(v.CollisionRadius);
            p.SegmentCollision = v.SegmentCollision;
            p.WindResponse = NonNegative(v.WindResponse);
            p.ImpulseResponse = NonNegative(v.ImpulseResponse);
            return p;
        }

        /// <summary>The parameters of every bone of a chain.</summary>
        public static JiggleParams[] ToParams(in JigglePresetValues v, int bones, float fixedHz)
        {
            JiggleParams[] all = new JiggleParams[bones];
            for (int i = 0; i < bones; i++)
            {
                all[i] = ToParams(v, i, bones, fixedHz);
            }
            return all;
        }

        /// <summary>The tip scale of a preset, as a chain takes it: a value that is not a positive number counts as 1.</summary>
        public static float CleanTipScale(in JigglePresetValues v)
        {
            return (JiggleMath.IsFinite(v.TipScale) && v.TipScale > 0.0f) ? v.TipScale : 1.0f;
        }
    }
}
