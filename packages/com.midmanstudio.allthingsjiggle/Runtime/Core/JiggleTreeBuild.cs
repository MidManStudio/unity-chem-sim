// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleTreeBuild.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// What adding a tree rig computes, in one place, so <see cref="JiggleWorld"/> and <see cref="JiggleHost"/> cannot drift
    /// apart. A tree is the anchor followed by particles that each name an earlier particle as their parent, so it can
    /// branch, and it has no virtual end particle. A lattice is built this way (see <see cref="JiggleLatticeGrid"/>), with
    /// its middle control point as the anchor.
    /// </summary>
    internal static class JiggleTreeBuild
    {
        /// <summary>
        /// Throws when the rest pose, the parents and the parameters do not make a tree: there must be the anchor and at least
        /// one more particle, at most <see cref="JiggleConst.MaxParticles"/> in all, one parent and one parameter set for each
        /// particle after the anchor, and every parent must be a particle that comes before its child.
        /// </summary>
        internal static void Validate(JVec3[] restWorld, int[] parent, JiggleParams[] p)
        {
            if (restWorld == null || parent == null || p == null || restWorld.Length < 2
                || parent.Length != restWorld.Length - 1 || p.Length != parent.Length)
            {
                throw new ArgumentException("a tree needs an anchor and one parent and one parameter set for each particle after it");
            }
            if (restWorld.Length > JiggleConst.MaxParticles)
            {
                throw new ArgumentException("a rig has at most " + JiggleConst.MaxParticles + " particles, the anchor included");
            }
            for (int i = 0; i < parent.Length; i++)
            {
                if (parent[i] < 0 || parent[i] > i)
                {
                    throw new ArgumentException("the parent of particle " + (i + 1) + " must be an earlier particle");
                }
            }
        }

        /// <summary>
        /// The definitions of a tree: the anchor, then one particle per entry. With <paramref name="pinRoots"/> a particle whose
        /// parent is the anchor is a root particle, held to its pose position by its root stretch the way the first bone of a
        /// chain is. Without it that particle is an ordinary one that hangs on the anchor like any other particle on its parent,
        /// and that is what a lattice uses. There is no virtual end, so every particle is a real point.
        /// </summary>
        internal static JiggleParticleDef[] BakeDefs(int[] parent, JiggleParams[] p, bool pinRoots)
        {
            JiggleParticleDef[] defs = new JiggleParticleDef[p.Length + 1];
            defs[0] = JiggleParams.AnchorDef();
            for (int i = 0; i < p.Length; i++)
            {
                uint flags = (pinRoots && parent[i] == 0) ? JiggleConst.FlagRoot : 0u;
                defs[i + 1] = p[i].Bake(parent[i], flags);
            }
            return defs;
        }

        /// <summary>Whether a rig is a tree: a chain always ends in a virtual end particle and a tree never has one.</summary>
        internal static bool IsTree(JiggleParticleDef last)
        {
            return (last.Flags & JiggleConst.FlagVirtual) == 0;
        }
    }
}
