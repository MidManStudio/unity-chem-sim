// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleChain.cs"
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// One jiggling chain of bones. Add it to the first bone that should move, not to the transform above it: the
    /// parent of this transform is the anchor that holds the chain, and the chain goes down from here through the
    /// first child of each bone until it runs out of children, reaches <see cref="endBone"/> or reaches
    /// <see cref="maxBones"/>. The component only describes the chain. The <see cref="JiggleManager"/> simulates it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Chain")]
    public sealed class JiggleChain : MonoBehaviour
    {
        /// <summary>The values of the bones.</summary>
        public JiggleChainSettings settings = new JiggleChainSettings();

        /// <summary>Where the pose of the chain comes from. See <see cref="JigglePoseSource"/>.</summary>
        public JigglePoseSource poseSource = JigglePoseSource.Bind;

        /// <summary>The last bone of the chain, which must lie on the way down. Empty means as far as it goes.</summary>
        public Transform endBone;

        /// <summary>The most bones the chain takes. A chain is at most 126 bones.</summary>
        [Range(1, JiggleConst.MaxParticles - 2)] public int maxBones = 32;

        /// <summary>
        /// How far past the last bone the virtual end sits, in lengths of the last bone. It sets how far the last bone
        /// turns for a sideways move of its end.
        /// </summary>
        [Min(0.01f)] public float tipScale = 1f;

        private Transform[] bones = new Transform[0];
        private Vector3[] bindPos = new Vector3[0];
        private Quaternion[] bindRot = new Quaternion[0];
        private Quaternion[] bindLocal = new Quaternion[0];
        private bool bound;
        private JiggleRuntime runtime;
        private int rig = -1;

        // The pose source the rig was built with, so a change in the inspector is noticed and the rig is built again.
        internal JigglePoseSource BuiltSource;

        /// <summary>The transform that holds the chain, the parent of this one. Null when there is none.</summary>
        public Transform Anchor
        {
            get { return transform.parent; }
        }

        /// <summary>How many bones the chain has as simulated, 0 while it is not.</summary>
        public int BoneCount
        {
            get { return IsSimulating ? bones.Length : 0; }
        }

        /// <summary>Whether the manager simulates this chain now.</summary>
        public bool IsSimulating
        {
            get { return runtime != null && rig >= 0; }
        }

        /// <summary>The bone with the given index, 0 for the first, or null outside the chain.</summary>
        public Transform GetBone(int index)
        {
            return index >= 0 && index < bones.Length ? bones[index] : null;
        }

        internal JiggleRuntime Runtime
        {
            get { return runtime; }
        }

        internal int RigIndex
        {
            get { return rig; }
        }

        internal Transform[] BonesInternal
        {
            get { return bones; }
        }

        internal Vector3[] BindPos
        {
            get { return bindPos; }
        }

        internal Quaternion[] BindRot
        {
            get { return bindRot; }
        }

        internal void Attach(JiggleRuntime owner, int rigIndexInOwner)
        {
            runtime = owner;
            rig = rigIndexInOwner;
        }

        internal void Detach()
        {
            runtime = null;
            rig = -1;
        }

        private void OnEnable()
        {
            JiggleManager m = JiggleManager.Instance;
            if (m != null)
            {
                m.Register(this);
            }
        }

        private void OnDisable()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null)
            {
                m.Unregister(this);
            }
            Detach();
            RestoreBind();
        }

        private void OnValidate()
        {
            maxBones = Mathf.Clamp(maxBones, 1, JiggleConst.MaxParticles - 2);
            if (tipScale < 0.01f)
            {
                tipScale = 0.01f;
            }
            if (Application.isPlaying && isActiveAndEnabled)
            {
                SettingsChanged();
            }
        }

        /// <summary>
        /// Tells the manager the settings, the tip scale or the bones changed, so it bakes them again before the next
        /// frame. The inspector does it by itself. A script that edits <see cref="settings"/> calls it.
        /// </summary>
        public void SettingsChanged()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null && isActiveAndEnabled)
            {
                m.NotifyChanged(this);
            }
        }

        /// <summary>
        /// Makes the chain snap to its pose on the next frame, without a swing. Use it after moving the chain
        /// somewhere without the anchor travelling there, for example when an object is recycled.
        /// </summary>
        public void ResetSimulation()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null && isActiveAndEnabled)
            {
                m.RequestReset(this);
            }
        }

        /// <summary>
        /// Forgets the bind pose and takes the pose the bones have now as the new one, on the next frame. Call it
        /// when the bones were moved on purpose. The bones are put back to the old bind rotations first, so a chain
        /// that is mid swing does not bind its swing.
        /// </summary>
        public void Rebind()
        {
            RestoreBind();
            bound = false;
            JiggleManager m = JiggleManager.TryGet();
            if (m != null && isActiveAndEnabled)
            {
                m.NotifyChanged(this);
            }
        }

        /// <summary>
        /// The world position of a particle of the simulated chain after the last frame: 0 is the anchor, 1 to
        /// <see cref="BoneCount"/> are the bones and the one after is the virtual end. False when the chain is
        /// not simulated or the index is outside it.
        /// </summary>
        public bool TryGetParticle(int index, out Vector3 world)
        {
            world = Vector3.zero;
            if (!IsSimulating || index < 0 || index > bones.Length + 1)
            {
                return false;
            }
            JVec3 p = runtime.Host.WorldPos(rig, index);
            world = new Vector3(p.X, p.Y, p.Z);
            return true;
        }

        /// <summary>
        /// Walks the chain down from this transform and checks it. Takes the bind pose when there is none or the
        /// bones are not the ones it had. Returns false with the reason when the chain cannot be simulated.
        /// </summary>
        internal bool Prepare(out string problem)
        {
            problem = null;
            if (settings == null)
            {
                settings = new JiggleChainSettings();
            }
            Transform anchor = transform.parent;
            if (anchor == null)
            {
                problem = "it has no parent to use as its anchor. Add the component to the first bone, below the transform that carries the chain.";
                return false;
            }
            List<Transform> walk = new List<Transform>(8);
            if (!TryWalk(walk, out problem))
            {
                return false;
            }
            if (!bound || !SameBones(walk))
            {
                RestoreBind();
                bones = walk.ToArray();
                CaptureBind(anchor);
            }
            return true;
        }

        /// <summary>
        /// Walks the chain down from this transform into <paramref name="bones"/> as the simulation would, without
        /// changing anything. False with the reason when the walk does not end at <see cref="endBone"/>.
        /// </summary>
        public bool TryWalk(List<Transform> bones, out string problem)
        {
            problem = null;
            bones.Clear();
            Transform t = transform;
            int limit = Mathf.Clamp(maxBones, 1, JiggleConst.MaxParticles - 2);
            for (int i = 0; i < limit; i++)
            {
                bones.Add(t);
                if (t == endBone || t.childCount == 0)
                {
                    break;
                }
                t = t.GetChild(0);
            }
            if (endBone != null && bones[bones.Count - 1] != endBone)
            {
                problem = "the end bone '" + endBone.name + "' is not on the way down through the first children, or is farther than max bones.";
                return false;
            }
            return true;
        }

        private bool SameBones(List<Transform> walk)
        {
            if (walk.Count != bones.Length)
            {
                return false;
            }
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != walk[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void CaptureBind(Transform anchor)
        {
            int n = bones.Length;
            bindPos = new Vector3[n + 1];
            bindRot = new Quaternion[n + 1];
            bindLocal = new Quaternion[n];
            bindRot[0] = Quaternion.identity;
            Quaternion inverse = Quaternion.Inverse(anchor.rotation);
            for (int k = 1; k <= n; k++)
            {
                Transform b = bones[k - 1];
                bindPos[k] = anchor.InverseTransformPoint(b.position);
                bindRot[k] = inverse * b.rotation;
                bindLocal[k - 1] = b.localRotation;
            }
            bound = true;
        }

        // Puts the bones back to the rotations they had when the chain was bound. Only in bind mode, where nothing
        // else sets the bones, so an animated chain is not given a stale pose.
        private void RestoreBind()
        {
            if (!bound || poseSource != JigglePoseSource.Bind)
            {
                return;
            }
            for (int i = 0; i < bones.Length && i < bindLocal.Length; i++)
            {
                if (bones[i] != null)
                {
                    bones[i].localRotation = bindLocal[i];
                }
            }
        }

        /// <summary>The rest pose in world space for building the rig: the anchor, then each bone.</summary>
        internal JVec3[] RestWorld()
        {
            int n = bones.Length;
            Transform anchor = transform.parent;
            JVec3[] rest = new JVec3[n + 1];
            Vector3 a = anchor.position;
            rest[0] = new JVec3(a.x, a.y, a.z);
            for (int k = 1; k <= n; k++)
            {
                Vector3 w = poseSource == JigglePoseSource.Bind ? anchor.TransformPoint(bindPos[k]) : bones[k - 1].position;
                rest[k] = new JVec3(w.x, w.y, w.z);
            }
            return rest;
        }
    }
}
