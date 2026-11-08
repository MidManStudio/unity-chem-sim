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
    /// the settings that apply to all chains. There is one. A chain that is enabled finds the one in the scene or
    /// makes one, so a scene only needs to place it to change the settings.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("MidMan Studio/Jiggle Manager")]
    public sealed unsafe class JiggleManager : MonoBehaviour
    {
        private static JiggleManager instance;
        private static bool quitting;

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

        private readonly List<JiggleChain> chains = new List<JiggleChain>();
        private readonly List<JiggleChain> changed = new List<JiggleChain>();
        private readonly List<JiggleChain> resets = new List<JiggleChain>();
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

        private void LateUpdate()
        {
            Simulate(Time.deltaTime);
        }

        /// <summary>
        /// Runs one frame for frameDt seconds now: rebuilds if chains came or went, bakes edited settings, reads the
        /// transforms, solves and writes the bone rotations. The manager calls it every LateUpdate with the frame
        /// time. A script that wants to step by hand, for a replay or a test, calls it too and does not rely on the
        /// LateUpdate of that frame.
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
                return;
            }
            ApplySettings(rt.Host);
            ApplyResets();
            JiggleRuntimeBackend active = ActiveBackend;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int code = rt.Frame(frameDt, active, rigsPerBatch);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            stats.Rigs = rt.Host.RigCount;
            stats.Particles = rt.Host.ParticleCount;
            stats.Steps = rt.Host.Cfg->Steps;
            stats.FrameMs = (float)((double)(t1 - t0) * 1000.0 / (double)System.Diagnostics.Stopwatch.Frequency);
            stats.Backend = active;
            stats.Status = code;
            if (code != JiggleConst.Ok && code != lastLoggedStatus)
            {
                Debug.LogError("The jiggle solver refused a frame with status " + code + ". The chains keep their last result.", this);
            }
            lastLoggedStatus = code;
        }
    }
}
