// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleChainLod.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>How a chain gets its level of detail.</summary>
    public enum JiggleLodMode
    {
        /// <summary>The limits of the <see cref="JiggleManager"/>.</summary>
        Inherit = 0,
        /// <summary>Always simulated in full, whatever the distance and the camera.</summary>
        AlwaysSimulate = 1,
        /// <summary>The limits below instead of the manager's.</summary>
        Custom = 2
    }

    /// <summary>
    /// The level of detail of one chain: it follows the manager, never goes down, or has limits of its own. A distance of
    /// 0 turns that limit off. Distances are from the camera of the manager to the anchor of the chain, in meters.
    /// </summary>
    [Serializable]
    public sealed class JiggleChainLod
    {
        /// <summary>Whose limits apply.</summary>
        public JiggleLodMode mode = JiggleLodMode.Inherit;
        /// <summary>Custom: farther than this the chain stops testing colliders. 0 means never.</summary>
        [Min(0f)] public float collisionDistance = 0f;
        /// <summary>Custom: farther than this the chain is not simulated. 0 means never.</summary>
        [Min(0f)] public float cullDistance = 0f;
        /// <summary>Custom: whether the chain is not simulated while the camera does not see it.</summary>
        public bool cullOffscreen = false;
        /// <summary>Custom: how long the camera must not see the chain before it is not simulated, in seconds.</summary>
        [Min(0f)] public float offscreenDelay = 0.5f;

        /// <summary>The limits this chain runs with, given the hysteresis and the limits of the manager.</summary>
        public JiggleLodSettings Resolve(JiggleLodSettings managerLimits, float hysteresis)
        {
            switch (mode)
            {
                case JiggleLodMode.AlwaysSimulate:
                    return JiggleLodSettings.Always();
                case JiggleLodMode.Custom:
                {
                    JiggleLodSettings s = new JiggleLodSettings();
                    s.CollisionDistance = collisionDistance;
                    s.CullDistance = cullDistance;
                    s.Hysteresis = hysteresis;
                    s.CullOffscreen = cullOffscreen;
                    s.OffscreenDelay = offscreenDelay;
                    return s;
                }
                default:
                    return managerLimits;
            }
        }
    }
}
