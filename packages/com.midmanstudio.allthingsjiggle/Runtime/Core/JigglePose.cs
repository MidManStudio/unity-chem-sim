// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JigglePose.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The two conversions between the transforms of a host and the records of the solver, on pointers with no
    /// managed allocation so a Burst job can run them: the pose the host gives the solver before a frame, and the
    /// local rotations it takes from the output after it.
    /// </summary>
    public static unsafe class JigglePose
    {
        /// <summary>
        /// Writes the pose of one rig for the coming frame. worldPos holds the world positions of the anchor and the
        /// bones, ParticleCount - 1 entries. The virtual end particle continues the last bone, tipScale bone lengths
        /// away. rots holds the world rotations of the pose in the same order, rotCount entries, and a particle past
        /// them, or all of them when rots is null, gets the identity. posePrev, poseCurr and poseRot point at the
        /// first particle of the rig. The old pose moves to posePrev and the anchor move since the last frame goes into
        /// the rig header.
        /// </summary>
        public static void Write(
            JiggleRig* rig, JVec4* posePrev, JVec4* poseCurr, JVec4* poseRot, float tipScale,
            JVec3* worldPos, JQuat* rots, int rotCount)
        {
            int n = (int)rig->ParticleCount;
            if (n < 3)
            {
                return;
            }
            JVec3 anchor = worldPos[0];
            JVec3 oldAnchor = rig->AnchorWorld;
            JVec3 tip = JiggleChainBuild.TipPosition(worldPos[n - 2], worldPos[n - 3], tipScale);
            for (int i = 0; i < n; i++)
            {
                posePrev[i] = poseCurr[i];
                JVec3 w = i < n - 1 ? worldPos[i] : tip;
                JVec3 rel = w - anchor;
                rel.Write4(ref poseCurr[i]);
                poseCurr[i].W = 0.0f;
                JQuat q = (rots != null && i < rotCount) ? rots[i] : JQuat.Identity;
                poseRot[i] = q.To4();
            }
            JVec3 delta = anchor - oldAnchor;
            rig->AnchorDelta = delta;
            rig->AnchorWorld = anchor;
        }

        /// <summary>
        /// The local rotation of every bone of a rig after resolve, so the host can set them in any order: bone b
        /// gets the rotation that, applied under the parent's resolved world rotation, gives its own resolved world
        /// rotation. The first bone's parent is the anchor, which keeps its pose rotation. local[b - 1] is bone b, for
        /// b from 1 to bones. poseRot and output point at the first particle of the rig.
        /// </summary>
        public static void LocalRotations(JVec4* poseRot, JiggleParticleOut* output, int bones, JQuat* local)
        {
            JQuat parent = JQuat.From4(poseRot[0]);
            for (int b = 1; b <= bones; b++)
            {
                JQuat world = JQuat.From4(output[b].Rot);
                local[b - 1] = JQuat.Mul(parent.Conjugate(), world).Normalized();
                parent = world;
            }
        }
    }
}
