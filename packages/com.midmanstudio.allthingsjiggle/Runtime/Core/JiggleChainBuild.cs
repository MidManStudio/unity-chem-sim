// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleChainBuild.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// What adding a chain computes, in one place, so <see cref="JiggleWorld"/> and <see cref="JiggleHost"/> cannot
    /// drift apart: the baked definitions, the virtual end particle and the extent of the chain.
    /// </summary>
    internal static class JiggleChainBuild
    {
        /// <summary>Throws when the rest pose and the parameters do not make a chain.</summary>
        internal static void Validate(JVec3[] restWorld, JiggleParams[] p)
        {
            if (restWorld == null || p == null || restWorld.Length < 2 || p.Length != restWorld.Length - 1)
            {
                throw new ArgumentException("a chain needs an anchor and one parameter set per bone");
            }
        }

        /// <summary>A tip scale that is not a positive number counts as 1.</summary>
        internal static float CleanTipScale(float tipScale)
        {
            return (JiggleMath.IsFinite(tipScale) && tipScale > 0.0f) ? tipScale : 1.0f;
        }

        /// <summary>
        /// The definitions of a chain: the anchor, one root or plain particle per bone and the virtual end, which is
        /// baked from the last bone's parameters without collision. The first bone is a root particle.
        /// </summary>
        internal static JiggleParticleDef[] BakeDefs(JiggleParams[] p)
        {
            int bones = p.Length;
            JiggleParticleDef[] defs = new JiggleParticleDef[bones + 2];
            defs[0] = JiggleParams.AnchorDef();
            for (int b = 0; b < bones; b++)
            {
                uint flags = b == 0 ? JiggleConst.FlagRoot : 0u;
                defs[b + 1] = p[b].Bake(b, flags);
            }
            JiggleParams tip = p[bones - 1];
            tip.Collide = false;
            defs[bones + 1] = tip.Bake(bones, JiggleConst.FlagVirtual);
            return defs;
        }

        /// <summary>Where the virtual end goes: past the last bone, along its direction, scale bone lengths away.</summary>
        internal static JVec3 TipPosition(JVec3 last, JVec3 before, float scale)
        {
            return last + (last - before) * scale;
        }

        /// <summary>The anchor and the bones of the rest pose followed by the virtual end: bones + 2 positions.</summary>
        internal static JVec3[] PoseWithTip(JVec3[] restWorld, float tipScale)
        {
            int n = restWorld.Length + 1;
            JVec3[] worldPose = new JVec3[n];
            Array.Copy(restWorld, worldPose, restWorld.Length);
            worldPose[n - 1] = TipPosition(restWorld[n - 2], restWorld[n - 3], tipScale);
            return worldPose;
        }

        /// <summary>The farthest distance of any position from the first one, which is the anchor.</summary>
        internal static float Extent(JVec3[] worldPose)
        {
            float extent = 0.0f;
            for (int i = 0; i < worldPose.Length; i++)
            {
                float d = (worldPose[i] - worldPose[0]).Length();
                if (d > extent)
                {
                    extent = d;
                }
            }
            return extent;
        }
    }
}
