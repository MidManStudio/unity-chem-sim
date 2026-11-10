// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleBodyColliders.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>Which parts of the body <see cref="JiggleBodyColliders"/> makes colliders for.</summary>
    [Flags]
    public enum JiggleBodyPartMask
    {
        /// <summary>No part.</summary>
        None = 0,
        /// <summary>The head sphere.</summary>
        Head = 1,
        /// <summary>The neck capsule.</summary>
        Neck = 2,
        /// <summary>The trunk capsule.</summary>
        Torso = 4,
        /// <summary>The capsules of the upper arms and the forearms.</summary>
        Arms = 8,
        /// <summary>The spheres of the hands.</summary>
        Hands = 16,
        /// <summary>The capsules of the thighs and the shins.</summary>
        Legs = 32,
        /// <summary>The capsules of the feet. Needs the toe joints.</summary>
        Feet = 64,
        /// <summary>Every part.</summary>
        All = 127
    }

    /// <summary>
    /// The joints of a body that the colliders are fitted to. Left and right are those of the character, as in the humanoid
    /// rig of Unity. A joint that is empty leaves out the parts that need it.
    /// </summary>
    [Serializable]
    public sealed class JiggleBodyJoints
    {
        /// <summary>The hips, the root of the body.</summary>
        public Transform hips;
        /// <summary>The base of the neck. Optional: without it the trunk runs most of the way up to the head.</summary>
        public Transform neck;
        /// <summary>The head joint, at the base of the skull.</summary>
        public Transform head;
        /// <summary>The left shoulder joint, where the upper arm starts.</summary>
        public Transform leftUpperArm;
        /// <summary>The left elbow.</summary>
        public Transform leftLowerArm;
        /// <summary>The left wrist.</summary>
        public Transform leftHand;
        /// <summary>The right shoulder joint, where the upper arm starts.</summary>
        public Transform rightUpperArm;
        /// <summary>The right elbow.</summary>
        public Transform rightLowerArm;
        /// <summary>The right wrist.</summary>
        public Transform rightHand;
        /// <summary>The left hip joint, where the thigh starts.</summary>
        public Transform leftUpperLeg;
        /// <summary>The left knee.</summary>
        public Transform leftLowerLeg;
        /// <summary>The left ankle.</summary>
        public Transform leftFoot;
        /// <summary>The left toes. Optional.</summary>
        public Transform leftToes;
        /// <summary>The right hip joint, where the thigh starts.</summary>
        public Transform rightUpperLeg;
        /// <summary>The right knee.</summary>
        public Transform rightLowerLeg;
        /// <summary>The right ankle.</summary>
        public Transform rightFoot;
        /// <summary>The right toes. Optional.</summary>
        public Transform rightToes;

        /// <summary>A copy that holds the same transforms.</summary>
        public JiggleBodyJoints Copy()
        {
            JiggleBodyJoints c = new JiggleBodyJoints();
            c.hips = hips;
            c.neck = neck;
            c.head = head;
            c.leftUpperArm = leftUpperArm;
            c.leftLowerArm = leftLowerArm;
            c.leftHand = leftHand;
            c.rightUpperArm = rightUpperArm;
            c.rightLowerArm = rightLowerArm;
            c.rightHand = rightHand;
            c.leftUpperLeg = leftUpperLeg;
            c.leftLowerLeg = leftLowerLeg;
            c.leftFoot = leftFoot;
            c.leftToes = leftToes;
            c.rightUpperLeg = rightUpperLeg;
            c.rightLowerLeg = rightLowerLeg;
            c.rightFoot = rightFoot;
            c.rightToes = rightToes;
            return c;
        }

        private static int Take(ref Transform slot, Animator animator, HumanBodyBones bone)
        {
            if (slot != null)
            {
                return 0;
            }
            slot = animator.GetBoneTransform(bone);
            return slot != null ? 1 : 0;
        }

        /// <summary>
        /// Fills the empty joints from the humanoid rig of an animator and leaves the ones that are set. Returns how many it
        /// filled. An animator that is null or has no humanoid avatar fills nothing. A humanoid rig has no neck or toes unless
        /// the avatar maps them, and those stay empty then.
        /// </summary>
        public int FillFrom(Animator animator)
        {
            if (animator == null || !animator.isHuman)
            {
                return 0;
            }
            int n = 0;
            n += Take(ref hips, animator, HumanBodyBones.Hips);
            n += Take(ref neck, animator, HumanBodyBones.Neck);
            n += Take(ref head, animator, HumanBodyBones.Head);
            n += Take(ref leftUpperArm, animator, HumanBodyBones.LeftUpperArm);
            n += Take(ref leftLowerArm, animator, HumanBodyBones.LeftLowerArm);
            n += Take(ref leftHand, animator, HumanBodyBones.LeftHand);
            n += Take(ref rightUpperArm, animator, HumanBodyBones.RightUpperArm);
            n += Take(ref rightLowerArm, animator, HumanBodyBones.RightLowerArm);
            n += Take(ref rightHand, animator, HumanBodyBones.RightHand);
            n += Take(ref leftUpperLeg, animator, HumanBodyBones.LeftUpperLeg);
            n += Take(ref leftLowerLeg, animator, HumanBodyBones.LeftLowerLeg);
            n += Take(ref leftFoot, animator, HumanBodyBones.LeftFoot);
            n += Take(ref leftToes, animator, HumanBodyBones.LeftToes);
            n += Take(ref rightUpperLeg, animator, HumanBodyBones.RightUpperLeg);
            n += Take(ref rightLowerLeg, animator, HumanBodyBones.RightLowerLeg);
            n += Take(ref rightFoot, animator, HumanBodyBones.RightFoot);
            n += Take(ref rightToes, animator, HumanBodyBones.RightToes);
            return n;
        }
    }

    /// <summary>One collider <see cref="JiggleBodyColliders"/> would make: what it is, where it hangs, and its shape in world space.</summary>
    public struct JiggleBodyPlan
    {
        /// <summary>The name the collider object gets, after the prefix: the side and the part.</summary>
        public string Label;
        /// <summary>The part of the body.</summary>
        public JiggleBodyPart Part;
        /// <summary>The joint the collider object is made a child of, so it follows the animation.</summary>
        public Transform Parent;
        /// <summary>The shape in world space.</summary>
        public JiggleBodyShape Shape;
    }

    /// <summary>
    /// Fits <see cref="JiggleColliderShape"/> components to the joints of a character: a sphere for the head and the hands,
    /// capsules between the joints for the rest. Hair, a tail or a chest then stay out of the body without anyone placing
    /// colliders by hand. The colliders are children of the joints, so they follow the animation, and cost nothing in the
    /// physics engine. Add the component to the character, set the animator or the joints, and build in the pose the
    /// character stands in when it is bound (a T pose or an A pose): the sizes come from the distances between joints at the
    /// moment of the build, and a character that is crouched or reaching builds a smaller or lopsided body. The proportions
    /// are those of an average adult and are meant to be scaled with <see cref="scale"/>. A uniform scale on the joints is
    /// assumed, the usual case.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Body Colliders")]
    public sealed class JiggleBodyColliders : MonoBehaviour
    {
        /// <summary>The prefix of the names of the objects this component makes.</summary>
        public const string NamePrefix = "JiggleBody_";

        /// <summary>The animator whose humanoid rig fills the empty joints. Empty looks for one on this object or below it.</summary>
        public Animator animator;

        /// <summary>The joints. Fill the empty ones from the animator with <see cref="AutoAssign"/>, or set them by hand for a rig that is not humanoid.</summary>
        public JiggleBodyJoints joints = new JiggleBodyJoints();

        /// <summary>The parts to make colliders for.</summary>
        public JiggleBodyPartMask parts = JiggleBodyPartMask.All;

        /// <summary>Multiplier on the radius of every part. 1 is an average adult build.</summary>
        [Min(0.05f)] public float scale = 1f;

        /// <summary>The height of the body in meters, which sets the sizes. 0 takes it from the joints.</summary>
        [Min(0f)] public float height = 0f;

        /// <summary>The layer of the collider objects. A chain ignores the layers set in its Ignore Layers.</summary>
        [Range(0, 31)] public int colliderLayer = 0;

        /// <summary>
        /// Make the colliders when the object wakes up, if there are none. For characters made at run time that never had
        /// colliders baked in. Leave it off for a character that was built in the editor.
        /// </summary>
        public bool buildOnAwake = false;

        [SerializeField, HideInInspector] private List<JiggleColliderShape> made = new List<JiggleColliderShape>();

        /// <summary>How many colliders this component has made and that still exist.</summary>
        public int BuiltCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < made.Count; i++)
                {
                    if (made[i] != null)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        /// <summary>Puts the colliders this component made, the ones that still exist, into <paramref name="into"/>.</summary>
        public void GetBuilt(List<JiggleColliderShape> into)
        {
            into.Clear();
            for (int i = 0; i < made.Count; i++)
            {
                if (made[i] != null)
                {
                    into.Add(made[i]);
                }
            }
        }

        private void Awake()
        {
            if (buildOnAwake && BuiltCount == 0)
            {
                Build();
            }
        }

        private void OnValidate()
        {
            if (joints == null)
            {
                joints = new JiggleBodyJoints();
            }
            if (made == null)
            {
                made = new List<JiggleColliderShape>();
            }
            if (scale < 0.05f)
            {
                scale = 0.05f;
            }
            if (height < 0f)
            {
                height = 0f;
            }
            colliderLayer = Mathf.Clamp(colliderLayer, 0, 31);
        }

        private Animator ResolveAnimator()
        {
            return animator != null ? animator : GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// Fills the empty joints from the humanoid rig of the animator and keeps the ones that are set. Returns how many it
        /// filled. See <see cref="JiggleBodyJoints.FillFrom"/>.
        /// </summary>
        public int AutoAssign()
        {
            if (joints == null)
            {
                joints = new JiggleBodyJoints();
            }
            return joints.FillFrom(ResolveAnimator());
        }

        // The joints as a build would use them: the set ones, and the empty ones filled from the animator. Changes nothing.
        private JiggleBodyJoints Resolved()
        {
            JiggleBodyJoints j = joints != null ? joints.Copy() : new JiggleBodyJoints();
            j.FillFrom(ResolveAnimator());
            return j;
        }

        private static JVec3 P(Transform t)
        {
            Vector3 v = t.position;
            return new JVec3(v.x, v.y, v.z);
        }

        private bool Has(JiggleBodyPartMask part)
        {
            return (parts & part) != 0;
        }

        /// <summary>
        /// The height of the body a build would use now, in meters: <see cref="height"/> when it is set, else from the head
        /// joint and the ankles, else from the hips and the head joint. 0 when the joints do not give one.
        /// </summary>
        public float ResolveHeight()
        {
            return HeightOf(Resolved());
        }

        private float HeightOf(JiggleBodyJoints j)
        {
            if (height > 0f)
            {
                return height;
            }
            if (j.head == null)
            {
                return 0f;
            }
            JVec3 head = P(j.head);
            if (j.leftFoot != null && j.rightFoot != null)
            {
                return JiggleBodyFit.Height(head, (P(j.leftFoot) + P(j.rightFoot)) * 0.5f);
            }
            if (j.leftFoot != null || j.rightFoot != null)
            {
                return JiggleBodyFit.Height(head, P(j.leftFoot != null ? j.leftFoot : j.rightFoot));
            }
            return j.hips != null ? JiggleBodyFit.HeightFromTorso(P(j.hips), head) : 0f;
        }

        private static void Add(List<JiggleBodyPlan> plan, string label, JiggleBodyPart part, Transform parent, in JiggleBodyShape shape)
        {
            JiggleBodyPlan item = new JiggleBodyPlan();
            item.Label = label;
            item.Part = part;
            item.Parent = parent;
            item.Shape = shape;
            plan.Add(item);
        }

        private void Arm(List<JiggleBodyPlan> plan, string side, Transform upper, Transform lower, Transform hand, float h)
        {
            JiggleBodyShape s;
            if (Has(JiggleBodyPartMask.Arms) && upper != null && lower != null
                && JiggleBodyFit.Capsule(P(upper), P(lower), JiggleBodyFit.Radius(JiggleBodyPart.UpperArm, h, scale), out s))
            {
                Add(plan, side + "UpperArm", JiggleBodyPart.UpperArm, upper, s);
            }
            if (Has(JiggleBodyPartMask.Arms) && lower != null && hand != null
                && JiggleBodyFit.Capsule(P(lower), P(hand), JiggleBodyFit.Radius(JiggleBodyPart.LowerArm, h, scale), out s))
            {
                Add(plan, side + "LowerArm", JiggleBodyPart.LowerArm, lower, s);
            }
            if (Has(JiggleBodyPartMask.Hands) && lower != null && hand != null
                && JiggleBodyFit.Hand(P(lower), P(hand), h, scale, out s))
            {
                Add(plan, side + "Hand", JiggleBodyPart.Hand, hand, s);
            }
        }

        private void Leg(List<JiggleBodyPlan> plan, string side, Transform upper, Transform lower, Transform foot, Transform toes, float h)
        {
            JiggleBodyShape s;
            if (Has(JiggleBodyPartMask.Legs) && upper != null && lower != null
                && JiggleBodyFit.Capsule(P(upper), P(lower), JiggleBodyFit.Radius(JiggleBodyPart.UpperLeg, h, scale), out s))
            {
                Add(plan, side + "UpperLeg", JiggleBodyPart.UpperLeg, upper, s);
            }
            if (Has(JiggleBodyPartMask.Legs) && lower != null && foot != null
                && JiggleBodyFit.Capsule(P(lower), P(foot), JiggleBodyFit.Radius(JiggleBodyPart.LowerLeg, h, scale), out s))
            {
                Add(plan, side + "LowerLeg", JiggleBodyPart.LowerLeg, lower, s);
            }
            if (Has(JiggleBodyPartMask.Feet) && foot != null && toes != null
                && JiggleBodyFit.Capsule(P(foot), P(toes), JiggleBodyFit.Radius(JiggleBodyPart.Foot, h, scale), out s))
            {
                Add(plan, side + "Foot", JiggleBodyPart.Foot, foot, s);
            }
        }

        /// <summary>
        /// Works out the colliders a build would make, from the joints as they are now, without making anything. Returns false
        /// with the reason when there would be none. The editor draws this as a preview.
        /// </summary>
        public bool Plan(List<JiggleBodyPlan> plan, out string problem)
        {
            plan.Clear();
            problem = null;
            JiggleBodyJoints j = Resolved();
            float h = HeightOf(j);
            if (!(h > 0f))
            {
                problem = "the height of the body is not known. Set the head joint and either the feet or the hips, or give the height.";
                return false;
            }
            JiggleBodyShape s;
            if (Has(JiggleBodyPartMask.Head) && j.head != null
                && JiggleBodyFit.Head(j.neck != null ? P(j.neck) : P(j.head), P(j.head), h, scale, out s))
            {
                Add(plan, "Head", JiggleBodyPart.Head, j.head, s);
            }
            if (Has(JiggleBodyPartMask.Neck) && j.neck != null && j.head != null
                && JiggleBodyFit.Capsule(P(j.neck), P(j.head), JiggleBodyFit.Radius(JiggleBodyPart.Neck, h, scale), out s))
            {
                Add(plan, "Neck", JiggleBodyPart.Neck, j.neck, s);
            }
            if (Has(JiggleBodyPartMask.Torso) && j.hips != null && (j.neck != null || j.head != null))
            {
                JVec3 hips = P(j.hips);
                JVec3 top = j.neck != null ? P(j.neck) : hips + (P(j.head) - hips) * 0.85f;
                if (JiggleBodyFit.Torso(hips, top, h, scale, out s))
                {
                    Add(plan, "Torso", JiggleBodyPart.Torso, j.hips, s);
                }
            }
            Arm(plan, "Left", j.leftUpperArm, j.leftLowerArm, j.leftHand, h);
            Arm(plan, "Right", j.rightUpperArm, j.rightLowerArm, j.rightHand, h);
            Leg(plan, "Left", j.leftUpperLeg, j.leftLowerLeg, j.leftFoot, j.leftToes, h);
            Leg(plan, "Right", j.rightUpperLeg, j.rightLowerLeg, j.rightFoot, j.rightToes, h);
            if (plan.Count == 0)
            {
                problem = "no part can be made from the joints that are set and the parts that are selected.";
                return false;
            }
            return true;
        }

        private static void Discard(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        /// <summary>Removes the colliders this component made.</summary>
        public void Clear()
        {
            for (int i = 0; i < made.Count; i++)
            {
                if (made[i] != null)
                {
                    Discard(made[i].gameObject);
                }
            }
            made.Clear();
        }

        /// <summary>
        /// Removes the colliders this component made and makes them again from the joints as they are now. The empty joints are
        /// filled from the animator first, so the filled ones stay on the component. Returns how many colliders it made, 0 with a
        /// warning in the console when the joints do not give a body.
        /// </summary>
        public int Build()
        {
            Clear();
            AutoAssign();
            List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>(16);
            string problem;
            if (!Plan(plan, out problem))
            {
                Debug.LogWarning("Jiggle Body Colliders on '" + name + "': " + problem, this);
                return 0;
            }
            for (int i = 0; i < plan.Count; i++)
            {
                JiggleColliderShape c = Place(plan[i]);
                if (c != null)
                {
                    made.Add(c);
                }
            }
            return made.Count;
        }

        // Makes the collider object of one planned shape, a child of its joint. A capsule is turned so its local Y axis runs
        // along the shape, and a uniform scale on the joint is taken out of the sizes, because the collider scales them again.
        private JiggleColliderShape Place(in JiggleBodyPlan item)
        {
            if (item.Parent == null)
            {
                return null;
            }
            JiggleBodyShape s = item.Shape;
            GameObject go = new GameObject(NamePrefix + item.Label);
            go.layer = colliderLayer;
            Transform t = go.transform;
            t.SetParent(item.Parent, false);
            t.position = new Vector3(s.Center.X, s.Center.Y, s.Center.Z);
            if (s.Kind == JiggleBodyShapeKind.Capsule)
            {
                t.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(s.Axis.X, s.Axis.Y, s.Axis.Z));
            }
            Vector3 ls = t.lossyScale;
            float k = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
            if (!(k > 1e-6f))
            {
                Discard(go);
                return null;
            }
            float inverse = 1f / k;
            JiggleColliderShape c = go.AddComponent<JiggleColliderShape>();
            c.radius = s.Radius * inverse;
            if (s.Kind == JiggleBodyShapeKind.Capsule)
            {
                c.shape = JiggleShapeKind.Capsule;
                c.axis = JiggleShapeAxis.Y;
                c.height = s.Length * inverse;
            }
            else
            {
                c.shape = JiggleShapeKind.Sphere;
            }
            return c;
        }
    }
}
