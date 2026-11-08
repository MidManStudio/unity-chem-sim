// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleChainSettings.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The values an author sets for a whole chain, in the ranges of <see cref="JiggleParams"/>. Every bone gets
    /// these, with the stiffness scaled along the chain by <see cref="stiffnessAlong"/>.
    /// </summary>
    [Serializable]
    public sealed class JiggleChainSettings
    {
        /// <summary>How hard each bone is pulled back to its pose.</summary>
        [Range(0f, 1f)] public float stiffness = 0.8f;
        /// <summary>Multiplier on the stiffness along the chain: 0 is the first bone, 1 the last.</summary>
        public AnimationCurve stiffnessAlong = AnimationCurve.Constant(0f, 1f, 1f);
        /// <summary>How much the pull fades near the pose.</summary>
        [Range(0f, 1f)] public float soften = 0f;
        /// <summary>How far the first bone may move away from its pose position.</summary>
        [Range(0f, 1f)] public float rootStretch = 0f;
        /// <summary>Whether the angle limit applies.</summary>
        public bool angleLimited = false;
        /// <summary>Largest swing from the pose, where 1 is 90 degrees.</summary>
        [Range(0f, 1f)] public float angleLimit = 0.5f;
        /// <summary>How softly the angle limit corrects.</summary>
        [Range(0f, 1f)] public float angleLimitSoften = 0f;
        /// <summary>How much a bone's length may change.</summary>
        [Range(0f, 1f)] public float stretch = 0.1f;
        /// <summary>Friction against the parent bone.</summary>
        [Range(0f, 1f)] public float drag = 0.1f;
        /// <summary>Friction against moving through the world.</summary>
        [Range(0f, 1f)] public float airDrag = 0f;
        /// <summary>Multiplier on the gravity of the manager.</summary>
        public float gravity = 1f;
        /// <summary>Share of the swing applied to the bone rotations.</summary>
        [Range(0f, 1f)] public float blend = 1f;
        /// <summary>Share of the anchor's motion that carries the bones along.</summary>
        [Range(0f, 1f)] public float ignoreRootMotion = 0f;
        /// <summary>Relative mass for two way coupling, 0 for none. Only ratios between neighbouring bones matter.</summary>
        [Min(0f)] public float mass = 0f;

        /// <summary>Settings equal to <see cref="JiggleParams.Default"/>.</summary>
        public JiggleChainSettings()
        {
        }

        /// <summary>The parameters of bone <paramref name="bone"/> (0 is the first) of a chain of <paramref name="bones"/> bones.</summary>
        public JiggleParams ToParams(int bone, int bones)
        {
            JiggleParams p = JiggleParams.Default();
            float along = bones > 1 ? (float)bone / (float)(bones - 1) : 0f;
            float scale = stiffnessAlong != null && stiffnessAlong.length > 0 ? stiffnessAlong.Evaluate(along) : 1f;
            p.Stiffness = Mathf.Clamp01(stiffness * scale);
            p.Soften = soften;
            p.RootStretch = rootStretch;
            p.AngleLimited = angleLimited;
            p.AngleLimit = angleLimit;
            p.AngleLimitSoften = angleLimitSoften;
            p.Stretch = stretch;
            p.Drag = drag;
            p.AirDrag = airDrag;
            p.Gravity = gravity;
            p.Blend = blend;
            p.IgnoreRootMotion = ignoreRootMotion;
            p.Mass = mass;
            return p;
        }

        /// <summary>The parameters of every bone of a chain.</summary>
        public JiggleParams[] ToParams(int bones)
        {
            JiggleParams[] all = new JiggleParams[bones];
            for (int i = 0; i < bones; i++)
            {
                all[i] = ToParams(i, bones);
            }
            return all;
        }
    }
}
