// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleParams.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The values an author sets for one bone, in ranges that are easy to reason about, and how they
    /// are baked into the numbers the solver uses. The solver gets squares and cosines of them, computed
    /// once per rig by <see cref="Bake"/> and not every step.
    /// </summary>
    public struct JiggleParams
    {
        /// <summary>How hard the bone is pulled back to its pose, 0 to 1.</summary>
        public float Stiffness;
        /// <summary>How much the pull fades near the pose, 0 to 1.</summary>
        public float Soften;
        /// <summary>How far a root bone may move away from its pose position, 0 to 1.</summary>
        public float RootStretch;
        /// <summary>Largest swing from the pose, 0 to 1, where 1 is 90 degrees.</summary>
        public float AngleLimit;
        /// <summary>How softly the angle limit corrects, 0 to 1.</summary>
        public float AngleLimitSoften;
        /// <summary>How much the bone length may change, 0 to 1.</summary>
        public float Stretch;
        /// <summary>Friction against the parent, 0 to 1.</summary>
        public float Drag;
        /// <summary>Friction against moving through the world, 0 to 1.</summary>
        public float AirDrag;
        /// <summary>Multiplier on gravity.</summary>
        public float Gravity;
        /// <summary>Collision radius in world units.</summary>
        public float CollisionRadius;
        /// <summary>Share of the swing applied to the rotation, 0 to 1.</summary>
        public float Blend;
        /// <summary>Share of the anchor's motion that moves the bone along with it, 0 to 1.</summary>
        public float IgnoreRootMotion;
        /// <summary>Multiplier on acceleration fields.</summary>
        public float WindResponse;
        /// <summary>Multiplier on impulse fields.</summary>
        public float ImpulseResponse;
        /// <summary>
        /// Relative mass for two way coupling, 0 for none. A bone pulls its parent back only when both have a mass
        /// above 0. Only the ratio between neighbours matters. Values above <see cref="MaxMass"/> count as MaxMass.
        /// </summary>
        public float Mass;
        /// <summary>Whether the angle limit applies.</summary>
        public bool AngleLimited;
        /// <summary>Whether the bone collides.</summary>
        public bool Collide;
        /// <summary>Whether the whole bone segment collides, not only its end.</summary>
        public bool SegmentCollision;

        /// <summary>Largest mass <see cref="Bake"/> keeps. Only ratios matter, so this just keeps sums finite.</summary>
        public const float MaxMass = 1.0e6f;

        /// <summary>The same defaults as the Rust crate.</summary>
        public static JiggleParams Default()
        {
            JiggleParams p = new JiggleParams();
            p.Stiffness = 0.8f;
            p.Soften = 0.0f;
            p.RootStretch = 0.0f;
            p.AngleLimit = 0.5f;
            p.AngleLimitSoften = 0.0f;
            p.Stretch = 0.1f;
            p.Drag = 0.1f;
            p.AirDrag = 0.0f;
            p.Gravity = 1.0f;
            p.CollisionRadius = 0.1f;
            p.Blend = 1.0f;
            p.IgnoreRootMotion = 0.0f;
            p.WindResponse = 1.0f;
            p.ImpulseResponse = 1.0f;
            p.Mass = 0.0f;
            p.AngleLimited = false;
            p.Collide = false;
            p.SegmentCollision = false;
            return p;
        }

        private static float Clamp01(float v)
        {
            if (v < 0.0f)
            {
                return 0.0f;
            }
            if (v > 1.0f)
            {
                return 1.0f;
            }
            return v;
        }

        /// <summary>
        /// Turns the authoring values into the baked definition of a particle. parent is the index of the
        /// parent in the rig, -1 for the anchor. extraFlags adds FlagKinematic, FlagRoot or FlagVirtual.
        /// </summary>
        public JiggleParticleDef Bake(int parent, uint extraFlags)
        {
            float stiffness = Clamp01(Stiffness);
            float stretch = Clamp01(Stretch);
            float soften = Clamp01(Soften);
            float limit = Clamp01(AngleLimit) * (MathF.PI / 2.0f);
            uint flags = extraFlags;
            if (AngleLimited)
            {
                flags |= JiggleConst.FlagAngleLimit;
            }
            if (Collide)
            {
                flags |= JiggleConst.FlagCollide;
            }
            if (SegmentCollision)
            {
                flags |= JiggleConst.FlagSegment;
            }
            JiggleParticleDef d = new JiggleParticleDef();
            d.Parent = parent;
            d.Flags = flags;
            d.AngleElasticity = stiffness * stiffness;
            d.LengthElasticity = (1.0f - stretch) * (1.0f - stretch);
            d.Soften = soften * soften;
            d.RootElasticity = 1.0f - Clamp01(RootStretch);
            d.LimitCos = MathF.Cos(limit);
            d.LimitSin = MathF.Sin(limit);
            d.LimitSoften = Clamp01(AngleLimitSoften);
            d.Drag = Clamp01(Drag);
            d.AirDrag = Clamp01(AirDrag);
            d.Gravity = Gravity;
            d.CollisionRadius = CollisionRadius > 0.0f ? CollisionRadius : 0.0f;
            d.Blend = Clamp01(Blend);
            d.IgnoreRootMotion = Clamp01(IgnoreRootMotion);
            d.WindResponse = WindResponse;
            d.ImpulseResponse = ImpulseResponse;
            d.Mass = Mass > MaxMass ? MaxMass : (Mass > 0.0f ? Mass : 0.0f);
            return d;
        }

        /// <summary>The definition of the anchor particle: kinematic, no parent.</summary>
        public static JiggleParticleDef AnchorDef()
        {
            JiggleParticleDef d = new JiggleParticleDef();
            d.Parent = -1;
            d.Flags = JiggleConst.FlagKinematic;
            d.AngleElasticity = 1.0f;
            d.LengthElasticity = 1.0f;
            d.RootElasticity = 1.0f;
            d.LimitCos = 1.0f;
            d.Blend = 1.0f;
            return d;
        }
    }
}
