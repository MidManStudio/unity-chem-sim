// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleLatticeSettings.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The values an author sets for a whole lattice, in the ranges of <see cref="JiggleParams"/>. Every control point gets
    /// these. The defaults are the numbers the response of a 3 by 3 by 3 lattice was measured with in the harness: after a
    /// jump of the object it is back at rest in about two seconds at 60 steps a second, and it has no gravity of its own.
    /// They were not judged by eye in Unity, so treat them as a starting point.
    /// </summary>
    [Serializable]
    public sealed class JiggleLatticeSettings
    {
        /// <summary>How hard each control point is pulled back to its place in the grid.</summary>
        [Range(0f, 1f)] public float stiffness = 0.4f;
        /// <summary>How much the pull fades near the rest place.</summary>
        [Range(0f, 1f)] public float soften = 0f;
        /// <summary>How much the distance between neighbouring control points may change.</summary>
        [Range(0f, 1f)] public float stretch = 0.3f;
        /// <summary>Friction against the neighbouring control point nearer the anchor. Raise it to settle sooner.</summary>
        [Range(0f, 1f)] public float drag = 0.2f;
        /// <summary>Friction against moving through the world.</summary>
        [Range(0f, 1f)] public float airDrag = 0f;
        /// <summary>Multiplier on the gravity of the manager. 0 by default: a prop that sags is a different effect from a wobble.</summary>
        public float gravity = 0f;
        /// <summary>Share of the motion of the anchor that carries the control points along, so they do not lag behind it.</summary>
        [Range(0f, 1f)] public float ignoreRootMotion = 0f;
        /// <summary>
        /// Relative mass for the two way coupling of neighbours, 0 for none. With a mass the control points pull each other and
        /// the whole lattice moves as one body. Without it each point only follows its own neighbour and the lattice
        /// ripples like a chain. Only ratios between neighbours matter.
        /// </summary>
        [Min(0f)] public float mass = 1f;
        /// <summary>Multiplier on the acceleration of <see cref="JiggleForceField"/> components, wind first of all.</summary>
        [Min(0f)] public float windResponse = 1f;
        /// <summary>Multiplier on hits and impulses: <see cref="JiggleLattice.AddImpulse"/>, bursts and fired fields.</summary>
        [Min(0f)] public float impulseResponse = 1f;

        /// <summary>Settings equal to the measured defaults.</summary>
        public JiggleLatticeSettings()
        {
        }

        /// <summary>The parameters of one control point. Every point of a lattice has the same.</summary>
        public JiggleParams ToParams()
        {
            JiggleParams p = JiggleParams.Default();
            p.Stiffness = stiffness;
            p.Soften = soften;
            p.RootStretch = 0f;
            p.AngleLimited = false;
            p.Stretch = stretch;
            p.Drag = drag;
            p.AirDrag = airDrag;
            p.Gravity = gravity;
            p.Blend = 1f;
            p.IgnoreRootMotion = ignoreRootMotion;
            p.Mass = mass;
            p.Collide = false;
            p.SegmentCollision = false;
            p.WindResponse = windResponse;
            p.ImpulseResponse = impulseResponse;
            return p;
        }

        /// <summary>The parameters of every control point after the anchor, as <see cref="JiggleHost.AddTree"/> takes them.</summary>
        public JiggleParams[] ToParams(int particles)
        {
            JiggleParams one = ToParams();
            JiggleParams[] all = new JiggleParams[particles < 0 ? 0 : particles];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = one;
            }
            return all;
        }
    }
}
