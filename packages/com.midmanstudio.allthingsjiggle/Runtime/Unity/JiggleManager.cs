// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleManager.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Simulates every enabled <see cref="JiggleChain"/> once a frame, in LateUpdate after the animation, and holds
    /// the settings that apply to all chains: the solver, sleeping, the level of detail, and the colliders and force
    /// fields of the scene, which register themselves. There is one. A chain that is enabled finds the one in the scene or
    /// makes one, so a scene only needs to place it to change the settings.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Manager")]
    public sealed unsafe class JiggleManager : MonoBehaviour
    {
        private static JiggleManager instance;
        private static bool quitting;

        // How much bigger than the rig the volume is that is tested against the view of the camera, in meters.
        private const float VisibleMargin = 0.5f;

        /// <summary>Which solver runs the chains. Native falls back to Burst when the library does not load.</summary>
        public JiggleRuntimeBackend backend = JiggleRuntimeBackend.Burst;

        /// <summary>Gravity in world space, meters per second squared. Each chain scales it with its own gravity setting.</summary>
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        /// <summary>Fixed steps per second. The frame time is cut into whole steps of this length.</summary>
        [Min(1f)] public float fixedHz = 60f;

        /// <summary>Most fixed steps one frame runs. Time past it is dropped, so a hitch does not become a storm of steps.</summary>
        [Range(1, 32)] public int maxSteps = 8;

        /// <summary>Passes of the two way coupling in each step. Chains with no mass ignore it.</summary>
        [Range(1, 16)] public int couplingPasses = 1;

        /// <summary>An anchor that moves farther than this in one frame snaps its chain to the pose. 0 turns it off.</summary>
        [Min(0f)] public float teleportDistance = 5f;

        /// <summary>Frames at rest before a chain falls asleep and costs nothing. 0 turns sleeping off.</summary>
        [Min(0)] public int sleepFrames = 0;

        /// <summary>A chain is at rest only while every particle moves slower than this, in meters per step.</summary>
        [Min(0f)] public float sleepSpeed = 0.0005f;

        /// <summary>A chain is at rest only while every particle is closer to its pose than this, in meters.</summary>
        [Min(0f)] public float sleepDeviation = 0.002f;

        /// <summary>A sleeping chain wakes when its anchor or pose moves farther than this in one frame, in meters.</summary>
        [Min(0f)] public float wakeMotion = 0.002f;

        /// <summary>Rigs one Burst job index handles. Smaller spreads better over threads, larger costs less to schedule.</summary>
        [Range(1, 256)] public int rigsPerBatch = 8;

        /// <summary>
        /// How far past its reach a collider still counts as near a chain, in meters. It covers bones that stretch past the
        /// pose. Raise it for chains that stretch a lot.
        /// </summary>
        [Range(0f, 2f)] public float colliderMargin = 0.25f;

        /// <summary>The camera the level of detail measures from. Empty uses the main camera. With no camera there is no level of detail.</summary>
        public Camera lodCamera;

        /// <summary>Farther than this from the camera chains stop testing colliders. 0 means never.</summary>
        [Min(0f)] public float lodCollisionDistance = 0f;

        /// <summary>Farther than this from the camera chains are not simulated and show their pose. 0 means never.</summary>
        [Min(0f)] public float lodCullDistance = 0f;

        /// <summary>
        /// How much closer than a distance limit a chain has to come before it goes back to its higher level, in meters.
        /// It keeps a chain on a limit from flickering between two levels.
        /// </summary>
        [Min(0f)] public float lodHysteresis = 2f;

        /// <summary>Whether chains the camera does not see are not simulated.</summary>
        public bool lodCullOffscreen = false;

        /// <summary>How long the camera must not see a chain before it is not simulated, in seconds.</summary>
        [Min(0f)] public float lodOffscreenDelay = 0.5f;

        private readonly List<JiggleChain> chains = new List<JiggleChain>();
        private readonly List<JiggleChain> changed = new List<JiggleChain>();
        private readonly List<JiggleChain> resets = new List<JiggleChain>();
        private readonly List<JiggleColliderShape> colliders = new List<JiggleColliderShape>();
        private readonly List<JiggleForceField> fields = new List<JiggleForceField>();
        private readonly JiggleColliderSet colliderSet = new JiggleColliderSet();
        private readonly JiggleFieldQueue fieldQueue = new JiggleFieldQueue();
        private JVec3[] anchors = new JVec3[16];
        private uint[] ignore = new uint[16];
        private readonly Plane[] viewPlanes = new Plane[6];
        private readonly JVec4[] viewPlaneData = new JVec4[6];
        private JiggleRuntime runtime;
        private bool dirty;
        private JiggleFrameStats stats;
        private bool nativeChecked;
        private bool nativeOk;
        private string nativeError = "";
        private int lastLoggedStatus;

        /// <summary>The manager of the scene. Makes one if there is none, which it does not outside play mode or while quitting.</summary>
        public static JiggleManager Instance
        {
            get
            {
                if (instance == null && !quitting && Application.isPlaying)
                {
                    instance = FindFirstObjectByType<JiggleManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("Jiggle Manager");
                        instance = go.AddComponent<JiggleManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        /// <summary>The manager if there is one, without making it.</summary>
        public static JiggleManager TryGet()
        {
            return instance;
        }

        /// <summary>What the last frame did.</summary>
        public JiggleFrameStats Stats
        {
            get { return stats; }
        }

        /// <summary>The backend that runs now: the one asked for, or Burst when Native could not be used.</summary>
        public JiggleRuntimeBackend ActiveBackend
        {
            get
            {
                if (backend == JiggleRuntimeBackend.Native && !EnsureNative())
                {
                    return JiggleRuntimeBackend.Burst;
                }
                return backend;
            }
        }

        /// <summary>Why the native library cannot be used, empty while it can or was not tried.</summary>
        public string NativeError
        {
            get { return nativeError; }
        }

        /// <summary>The number of chains that are registered, simulated or not.</summary>
        public int ChainCount
        {
            get { return chains.Count; }
        }

        /// <summary>The number of <see cref="JiggleColliderShape"/> components that are enabled.</summary>
        public int ColliderShapeCount
        {
            get { return colliders.Count; }
        }

        /// <summary>The number of <see cref="JiggleForceField"/> components that are enabled.</summary>
        public int ForceFieldCount
        {
            get { return fields.Count; }
        }

        /// <summary>Number of one shot impulses waiting for a frame that runs a fixed step.</summary>
        public int PendingImpulses
        {
            get { return fieldQueue.PendingCount; }
        }

        /// <summary>Copies the registered chains into <paramref name="into"/>, which is cleared first. For tools that list them.</summary>
        public void GetChains(List<JiggleChain> into)
        {
            into.Clear();
            into.AddRange(chains);
        }

        /// <summary>
        /// Gives every chain within <paramref name="radius"/> of <paramref name="point"/> a single shove away from it, a change
        /// of velocity of up to <paramref name="velocityChange"/> meters per second that fades to nothing at the edge by
        /// <paramref name="falloff"/> (0 does not fade, 1 is linear). A negative value pulls toward the point. The shove is
        /// given on the first fixed step of the next frame that runs one. Chains scale it by their impulse response.
        /// </summary>
        public void AddBurst(Vector3 point, float radius, float velocityChange, int falloff = 1)
        {
            fieldQueue.Enqueue(JiggleShapes.Burst(
                new JVec3(point.x, point.y, point.z), radius, velocityChange, (uint)Mathf.Clamp(falloff, 0, 4)));
        }

        /// <summary>
        /// Gives every chain a single shove along <paramref name="velocityChange"/>, in meters per second. With a
        /// <paramref name="radius"/> above 0 only chains within it of <paramref name="center"/> take it, fading to the edge
        /// by <paramref name="falloff"/>. The shove is given on the first fixed step of the next frame that runs one.
        /// </summary>
        public void AddShove(Vector3 velocityChange, Vector3 center, float radius = 0f, int falloff = 1)
        {
            JVec3 v = new JVec3(velocityChange.x, velocityChange.y, velocityChange.z);
            float speed = v.Length();
            if (!(speed > 0f) || float.IsInfinity(speed))
            {
                return;
            }
            fieldQueue.Enqueue(JiggleShapes.Field(
                JiggleConst.FieldDirectional, JiggleConst.FieldImpulse, new JVec3(center.x, center.y, center.z), v, 0f, speed,
                radius, (uint)Mathf.Clamp(falloff, 0, 4), 0f, 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("There is more than one Jiggle Manager. The extra one is removed.", this);
                Destroy(this);
                return;
            }
            instance = this;
        }

        private void OnApplicationQuit()
        {
            quitting = true;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            if (runtime != null)
            {
                for (int i = 0; i < runtime.Chains.Count; i++)
                {
                    if (runtime.Chains[i] != null)
                    {
                        runtime.Chains[i].Detach();
                    }
                }
                runtime.Dispose();
                runtime = null;
            }
        }

        internal void Register(JiggleChain chain)
        {
            if (!chains.Contains(chain))
            {
                chains.Add(chain);
                dirty = true;
            }
        }

        internal void Unregister(JiggleChain chain)
        {
            if (chains.Remove(chain))
            {
                dirty = true;
            }
        }

        internal void Register(JiggleColliderShape shape)
        {
            if (!colliders.Contains(shape))
            {
                colliders.Add(shape);
            }
        }

        internal void Unregister(JiggleColliderShape shape)
        {
            colliders.Remove(shape);
        }

        internal void Register(JiggleForceField field)
        {
            if (!fields.Contains(field))
            {
                fields.Add(field);
            }
        }

        internal void Unregister(JiggleForceField field)
        {
            fields.Remove(field);
        }

        internal void Enqueue(JiggleField impulse)
        {
            fieldQueue.Enqueue(impulse);
        }

        internal void NotifyChanged(JiggleChain chain)
        {
            if (!changed.Contains(chain))
            {
                changed.Add(chain);
            }
        }

        internal void RequestReset(JiggleChain chain)
        {
            if (!resets.Contains(chain))
            {
                resets.Add(chain);
            }
        }

        private bool EnsureNative()
        {
            if (!nativeChecked)
            {
                nativeChecked = true;
                try
                {
                    JiggleLibrary.Verify();
                    nativeOk = true;
                }
                catch (Exception e)
                {
                    nativeOk = false;
                    nativeError = e.Message;
                    Debug.LogWarning("The native jiggle backend cannot be used, Burst runs instead: " + e.Message);
                }
            }
            return nativeOk;
        }

        private void Rebuild()
        {
            dirty = false;
            JiggleRuntime old = runtime;
            runtime = JiggleRuntime.Build(chains, old);
            if (old != null)
            {
                old.Dispose();
            }
            stats.Rebuilds++;
        }

        // Settings edited on a chain: bake them again in place when the bones are the same, rebuild when they are not.
        private void ApplyChanges()
        {
            for (int i = 0; i < changed.Count; i++)
            {
                JiggleChain c = changed[i];
                if (c == null || !c.isActiveAndEnabled)
                {
                    continue;
                }
                string problem;
                Transform[] before = c.BonesInternal;
                int count = before.Length;
                bool wasSimulating = c.IsSimulating;
                if (!c.Prepare(out problem) || !wasSimulating || c.BonesInternal != before || c.BonesInternal.Length != count
                    || c.BuiltSource != c.poseSource)
                {
                    dirty = true;
                    continue;
                }
                runtime.Host.SetParams(c.RigIndex, c.settings.ToParams(count));
                runtime.Host.SetTipScale(c.RigIndex, c.tipScale);
            }
            changed.Clear();
        }

        // The settings of the manager go into the host before each frame, so a change in the inspector shows at once.
        private void ApplySettings(JiggleHost h)
        {
            h.FixedDt = 1.0f / (fixedHz < 1f ? 1f : fixedHz);
            h.MaxSteps = (uint)(maxSteps < 1 ? 1 : maxSteps);
            JiggleStepConfig* cfg = h.Cfg;
            cfg->Gravity = new JVec3(gravity.x, gravity.y, gravity.z);
            cfg->TeleportDistance = teleportDistance;
            cfg->SleepFrames = (uint)(sleepFrames < 0 ? 0 : sleepFrames);
            cfg->SleepSpeed = sleepSpeed;
            cfg->SleepDeviation = sleepDeviation;
            cfg->WakeMotion = wakeMotion;
            cfg->CouplingPasses = (uint)couplingPasses;
        }

        private void ApplyResets()
        {
            for (int i = 0; i < resets.Count; i++)
            {
                JiggleChain c = resets[i];
                if (c != null && c.IsSimulating && object.ReferenceEquals(c.Runtime, runtime))
                {
                    runtime.Host.Reset(c.RigIndex);
                }
            }
            resets.Clear();
        }

        // ------------------------------------------------------------------ per frame scene work

        private void EnsureRigArrays(int rigs)
        {
            if (anchors.Length < rigs)
            {
                int n = anchors.Length;
                while (n < rigs)
                {
                    n *= 2;
                }
                anchors = new JVec3[n];
                ignore = new uint[n];
            }
        }

        private static float Distance(Vector3 camera, JVec3 point)
        {
            float dx = camera.x - point.X;
            float dy = camera.y - point.Y;
            float dz = camera.z - point.Z;
            return JiggleMath.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void ReadViewPlanes(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, viewPlanes);
            for (int i = 0; i < 6; i++)
            {
                Vector3 n = viewPlanes[i].normal;
                viewPlaneData[i] = new JVec4(n.x, n.y, n.z, viewPlanes[i].distance);
            }
        }

        // Reads the anchors, decides the level of detail of every rig, switches rigs off and on, keeps chains that must not sleep
        // awake and fills the layers each rig ignores. Runs before the colliders are written, because it decides who tests them.
        private void PrepareRigs(JiggleRuntime rt, float frameDt)
        {
            JiggleHost h = rt.Host;
            int rigs = h.RigCount;
            EnsureRigArrays(rigs);
            Camera cam = lodCamera != null ? lodCamera : Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            bool planesRead = false;
            JiggleLodSettings limits = new JiggleLodSettings();
            limits.CollisionDistance = lodCollisionDistance;
            limits.CullDistance = lodCullDistance;
            limits.Hysteresis = lodHysteresis;
            limits.CullOffscreen = lodCullOffscreen;
            limits.OffscreenDelay = lodOffscreenDelay;

            int noCollision = 0;
            int off = 0;
            for (int r = 0; r < rigs; r++)
            {
                JiggleChain c = rt.Chains[r];
                Transform anchorTransform = c.Anchor;
                Vector3 ap = anchorTransform != null ? anchorTransform.position : c.transform.position;
                anchors[r] = new JVec3(ap.x, ap.y, ap.z);

                JiggleLodLevel level = JiggleLodLevel.Full;
                JiggleLodSettings own = c.ResolveLod(limits, lodHysteresis);
                bool limited = own.CollisionDistance > 0f || own.CullDistance > 0f || own.CullOffscreen;
                if (cam != null && limited)
                {
                    bool visible = true;
                    if (own.CullOffscreen)
                    {
                        if (!planesRead)
                        {
                            ReadViewPlanes(cam);
                            planesRead = true;
                        }
                        visible = JiggleLod.SphereInFrustum(viewPlaneData, 6, anchors[r], h.Rigs[r].Extent + VisibleMargin);
                    }
                    level = JiggleLod.Decide(own, ref c.LodTracker, Distance(camPos, anchors[r]), visible, frameDt);
                }
                else
                {
                    c.LodTracker = new JiggleLodTracker();
                }

                // The flag in the host is the truth, so a rebuild that copied it and a level that changed agree.
                bool isOff = h.IsDisabled(r);
                if (level == JiggleLodLevel.Off && !isOff)
                {
                    h.SetDisabled(r, true);
                }
                else if (level != JiggleLodLevel.Off && isOff)
                {
                    h.SetDisabled(r, false);
                }
                if (level == JiggleLodLevel.Off)
                {
                    off++;
                }
                else if (level == JiggleLodLevel.NoCollision)
                {
                    noCollision++;
                }
                ignore[r] = level == JiggleLodLevel.Full ? unchecked((uint)c.ignoreLayers.value) : uint.MaxValue;

                if (!c.allowSleep)
                {
                    h.Wake(r);
                }
            }
            stats.LodNoCollision = noCollision;
            stats.LodOff = off;
        }

        // Writes the colliders and the fields of the scene, and the one shot impulses, into the host.
        private void WriteScene(JiggleRuntime rt)
        {
            JiggleHost h = rt.Host;
            colliderSet.Clear();
            colliderSet.Margin = colliderMargin;
            for (int i = 0; i < colliders.Count; i++)
            {
                JiggleColliderShape s = colliders[i];
                if (s == null || !s.isActiveAndEnabled)
                {
                    continue;
                }
                bool moved;
                JiggleCollider c = s.Build(out moved);
                colliderSet.Add(c, s.gameObject.layer, moved);
            }
            colliderSet.Write(h, anchors, ignore);

            fieldQueue.Clear();
            for (int i = 0; i < fields.Count; i++)
            {
                JiggleForceField f = fields[i];
                if (f == null || !f.isActiveAndEnabled)
                {
                    continue;
                }
                fieldQueue.Add(f.Build());
            }
            fieldQueue.Write(h);
        }

        private int CountAsleep(JiggleHost h)
        {
            int asleep = 0;
            for (int r = 0; r < h.RigCount; r++)
            {
                if ((h.Rigs[r].Flags & JiggleConst.RigAsleep) != 0)
                {
                    asleep++;
                }
            }
            return asleep;
        }

        private void LateUpdate()
        {
            Simulate(Time.deltaTime);
        }

        /// <summary>
        /// Runs one frame for frameDt seconds now: rebuilds if chains came or went, bakes edited settings, decides the level
        /// of detail, writes the colliders and fields of the scene, reads the transforms, solves and writes the bone
        /// rotations. The manager calls it every LateUpdate with the frame time. A script that wants to step by hand, for a
        /// replay or a test, calls it too and does not rely on the LateUpdate of that frame.
        /// </summary>
        public void Simulate(float frameDt)
        {
            if (dirty || runtime == null)
            {
                Rebuild();
            }
            if (changed.Count > 0)
            {
                ApplyChanges();
                if (dirty)
                {
                    Rebuild();
                }
            }
            JiggleRuntime rt = runtime;
            if (rt == null || rt.Host.RigCount == 0)
            {
                stats.Rigs = 0;
                stats.Particles = 0;
                stats.Steps = 0;
                stats.FrameMs = 0f;
                stats.Colliders = 0;
                stats.ColliderRefs = 0;
                stats.Fields = 0;
                stats.LodNoCollision = 0;
                stats.LodOff = 0;
                stats.Asleep = 0;
                return;
            }
            ApplySettings(rt.Host);
            ApplyResets();
            PrepareRigs(rt, frameDt);
            WriteScene(rt);
            JiggleRuntimeBackend active = ActiveBackend;
            rt.Host.Cfg->Steps = 0;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int code = rt.Frame(frameDt, active, rigsPerBatch);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            // An impulse is given on the first step of a frame, so a frame that ran none, or failed, keeps it for the next.
            fieldQueue.Commit(code == JiggleConst.Ok ? rt.Host.Cfg->Steps : 0u);
            stats.Rigs = rt.Host.RigCount;
            stats.Particles = rt.Host.ParticleCount;
            stats.Steps = rt.Host.Cfg->Steps;
            stats.FrameMs = (float)((double)(t1 - t0) * 1000.0 / (double)System.Diagnostics.Stopwatch.Frequency);
            stats.Backend = active;
            stats.Status = code;
            stats.Colliders = rt.Host.ColliderCount;
            stats.ColliderRefs = rt.Host.RefCount;
            stats.Fields = rt.Host.FieldCount;
            stats.Asleep = CountAsleep(rt.Host);
            if (code != JiggleConst.Ok && code != lastLoggedStatus)
            {
                Debug.LogError("The jiggle solver refused a frame with status " + code + ". The chains keep their last result.", this);
            }
            lastLoggedStatus = code;
        }
    }
}
