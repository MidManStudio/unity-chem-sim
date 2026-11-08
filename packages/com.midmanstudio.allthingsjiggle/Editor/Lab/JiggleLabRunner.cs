// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLabRunner.cs"
// ============================================================================
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>One measured row, with the same columns as the Rust scene bench prints (times are wall clock here).</summary>
    internal sealed class JiggleBenchRow
    {
        public string Backend;
        public string Scene;
        public int Threads;
        public int Rigs;
        public int Bones;
        public int Particles;
        public int Frames;
        public double WallUs;
        public double PoseUs;
        public double StepUs;
        public double ResolveUs;
        public double NsPerParticle;
        public ulong Checksum;
        public int Asleep;
        public bool Finite;

        public string Csv()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "CSV,v1,{0},{1},{2},{3},{4},{5},{6},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:x16}",
                Backend, Scene, Threads, Rigs, Bones, Particles, Frames, WallUs, PoseUs, StepUs, ResolveUs, NsPerParticle, Checksum);
        }

        public const string CsvHeader =
            "CSV,v1,backend,scene,threads,rigs,bones,particles,frames,wall_us,pose_us,step_us,resolve_us,ns_per_particle,checksum";
    }

    /// <summary>The comparison of the three backends on one scene.</summary>
    internal struct JiggleParityResult
    {
        public string Scene;
        public int Frames;
        public bool NativeAvailable;
        public string NativeError;
        /// <summary>Frames in which the native output differed from the managed output in any bit. Expected 0.</summary>
        public int NativeFramesDiffering;
        /// <summary>Largest difference of a position component between Burst and managed, meters.</summary>
        public double BurstMaxPosition;
        /// <summary>Largest difference of a rotation component between Burst and managed.</summary>
        public double BurstMaxRotation;
        /// <summary>Frames in which the Burst output differed from the managed output in any bit.</summary>
        public int BurstFramesDiffering;
        public bool Finite;
    }

    /// <summary>
    /// Runs the scenes of <see cref="JiggleBenchScenes"/> on the Burst, native and managed backends, in this
    /// process on this machine, and compares their results. A scene is built once per run, warmed up, and then timed
    /// frame by frame the way the Rust scene bench does it.
    /// </summary>
    internal static unsafe class JiggleLabRunner
    {
        // One phase (step or resolve) of a backend over every rig, split into windows for the threaded cases.
        private sealed class Phase
        {
            public JiggleHost Host;
            public JiggleRuntimeBackend Backend;
            public int Workers;
            public int RigsPerBatch;
            public long ArgBlock;
            public long CfgAddr;
            public long Status;
            public int StatusCount;
            public int[] Codes;

            public void Run(bool resolve)
            {
                JiggleHost h = Host;
                int rigs = h.RigCount;
                int code;
                if (Backend == JiggleRuntimeBackend.Burst)
                {
                    int* status = (int*)Status;
                    code = 0;
                    JiggleBurstSolver.Schedule(
                        h, RigsPerBatch, status, resolve ? JiggleBurstSolver.PhaseResolve : JiggleBurstSolver.PhaseStep,
                        default(JobHandle)).Complete();
                    code = JiggleBurstSolver.Worst(status, StatusCount);
                }
                else
                {
                    JiggleBackend be = Backend == JiggleRuntimeBackend.Native ? JiggleBackend.Native : JiggleBackend.Managed;
                    if (Workers <= 1)
                    {
                        code = h.Run(be, 0, (uint)rigs, resolve);
                    }
                    else
                    {
                        JiggleSolverArgs* blocks = (JiggleSolverArgs*)ArgBlock;
                        for (int w = 0; w < Workers; w++)
                        {
                            h.FillArgs(blocks + w);
                        }
                        int per = (rigs + Workers - 1) / Workers;
                        long cfgAddr = CfgAddr;
                        long argBlock = ArgBlock;
                        int[] codes = Codes;
                        Parallel.For(0, Workers, delegate(int w)
                        {
                            int first = w * per;
                            int count = Math.Min(per, rigs - first);
                            codes[w] = count > 0
                                ? JiggleHost.Call(
                                    be, (JiggleStepConfig*)cfgAddr, (JiggleSolverArgs*)argBlock + w, (uint)first, (uint)count, resolve)
                                : 0;
                        });
                        code = 0;
                        for (int w = 0; w < Workers; w++)
                        {
                            if (codes[w] != 0)
                            {
                                code = codes[w];
                            }
                        }
                    }
                }
                if (code != JiggleConst.Ok)
                {
                    throw new InvalidOperationException("the solver answered " + code);
                }
            }
        }

        private static string BackendName(JiggleRuntimeBackend b)
        {
            return b == JiggleRuntimeBackend.Burst ? "burst" : (b == JiggleRuntimeBackend.Native ? "native" : "managed");
        }

        /// <summary>Whether the native library loads and has this package's interface, with the reason when it does not.</summary>
        public static bool NativeAvailable(out string error)
        {
            try
            {
                JiggleLibrary.Verify();
                error = "";
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Times one scene. threads is the number of worker threads for the native and managed backends. The Burst
        /// backend uses the job system and reports its worker count. scale multiplies the frame count.
        /// </summary>
        public static JiggleBenchRow Run(JiggleBenchSpec s, JiggleRuntimeBackend backend, int threads, double scale, int rigsPerBatch)
        {
            int frames = Math.Max(3, (int)((double)s.Frames * scale));
            JiggleBenchRow row = new JiggleBenchRow();
            row.Backend = BackendName(backend);
            row.Scene = s.Name;
            row.Rigs = s.Rigs;
            row.Bones = s.Bones;
            row.Particles = s.Particles;
            row.Frames = frames;
            using (JiggleHost h = JiggleBenchScenes.Build(s))
            {
                Phase phase = new Phase();
                phase.Host = h;
                phase.Backend = backend;
                phase.RigsPerBatch = rigsPerBatch < 1 ? 1 : rigsPerBatch;
                phase.CfgAddr = (long)h.Cfg;
                int workers = backend == JiggleRuntimeBackend.Burst ? 0 : Math.Max(1, Math.Min(threads, h.RigCount));
                phase.Workers = workers;
                IntPtr argBlock = IntPtr.Zero;
                int* status = null;
                try
                {
                    if (workers > 1)
                    {
                        argBlock = Marshal.AllocHGlobal(sizeof(JiggleSolverArgs) * workers);
                        phase.ArgBlock = (long)argBlock;
                        phase.Codes = new int[workers];
                    }
                    if (backend == JiggleRuntimeBackend.Burst)
                    {
                        phase.StatusCount = JiggleBurstSolver.BatchCount(h.RigCount, phase.RigsPerBatch);
                        long bytes = (long)phase.StatusCount * sizeof(int);
                        status = (int*)UnsafeUtility.Malloc(bytes, sizeof(int), Allocator.Persistent);
                        UnsafeUtility.MemClear(status, bytes);
                        phase.Status = (long)status;
                        row.Threads = JobsUtility.JobWorkerCount;
                    }
                    else
                    {
                        row.Threads = workers;
                    }

                    long poseTicks = 0;
                    long stepTicks = 0;
                    long resolveTicks = 0;
                    long wallStart = 0;
                    long wallEnd = 0;
                    for (int f = 0; f < s.Warmup + frames; f++)
                    {
                        if (f == s.Warmup)
                        {
                            wallStart = Stopwatch.GetTimestamp();
                        }
                        long t0 = Stopwatch.GetTimestamp();
                        JiggleBenchScenes.WritePose(h, s, f);
                        long t1 = Stopwatch.GetTimestamp();
                        h.Cfg->Time = (float)f / 60.0f;
                        h.BeginFixed(1);
                        phase.Run(false);
                        long t2 = Stopwatch.GetTimestamp();
                        phase.Run(true);
                        long t3 = Stopwatch.GetTimestamp();
                        if (f >= s.Warmup)
                        {
                            poseTicks += t1 - t0;
                            stepTicks += t2 - t1;
                            resolveTicks += t3 - t2;
                        }
                    }
                    wallEnd = Stopwatch.GetTimestamp();
                    double perFrame = 1.0e6 / (double)Stopwatch.Frequency / (double)frames;
                    row.WallUs = (double)(wallEnd - wallStart) * perFrame;
                    row.PoseUs = (double)poseTicks * perFrame;
                    row.StepUs = (double)stepTicks * perFrame;
                    row.ResolveUs = (double)resolveTicks * perFrame;
                    row.NsPerParticle = (row.StepUs + row.ResolveUs) * 1000.0 / (double)s.Particles;
                    Summarise(h, row);
                }
                finally
                {
                    if (argBlock != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(argBlock);
                    }
                    if (status != null)
                    {
                        UnsafeUtility.Free(status, Allocator.Persistent);
                    }
                }
            }
            return row;
        }

        // The checksum is the one of the Rust bench: a sum, so it does not depend on how the rigs were split.
        private static void Summarise(JiggleHost h, JiggleBenchRow row)
        {
            ulong sum = 0;
            bool finite = true;
            float* o = (float*)h.Out;
            for (int i = 0; i < h.ParticleCount; i++)
            {
                ulong gi = (ulong)i;
                for (int k = 0; k < 8; k++)
                {
                    float v = o[i * 8 + k];
                    finite &= JiggleMath.IsFinite(v);
                    uint bits = *(uint*)&v;
                    unchecked
                    {
                        sum += ((ulong)bits ^ (gi * 0x9e3779b97f4a7c15UL)) * 0x0100000001b3UL;
                    }
                }
            }
            int asleep = 0;
            for (int r = 0; r < h.RigCount; r++)
            {
                if ((h.Rigs[r].Flags & JiggleConst.RigAsleep) != 0)
                {
                    asleep++;
                }
            }
            row.Checksum = sum;
            row.Finite = finite;
            row.Asleep = asleep;
        }

        private static void StepOnce(JiggleHost h, JiggleBenchSpec s, int frame, JiggleRuntimeBackend backend, int rigsPerBatch)
        {
            JiggleBenchScenes.WritePose(h, s, frame);
            h.Cfg->Time = (float)frame / 60.0f;
            if (backend == JiggleRuntimeBackend.Burst)
            {
                h.BeginFixed(1);
                int code = JiggleBurstSolver.RunNow(h, rigsPerBatch);
                if (code != JiggleConst.Ok)
                {
                    throw new InvalidOperationException("the Burst solver answered " + code);
                }
                h.EndFrame();
            }
            else
            {
                int code = h.StepFixed(1, backend == JiggleRuntimeBackend.Native ? JiggleBackend.Native : JiggleBackend.Managed);
                if (code != JiggleConst.Ok)
                {
                    throw new InvalidOperationException("the solver answered " + code);
                }
            }
        }

        private static bool SameBits(JiggleHost a, JiggleHost b)
        {
            long bytes = (long)a.ParticleCount * sizeof(JiggleParticleOut);
            byte* x = (byte*)a.Out;
            byte* y = (byte*)b.Out;
            for (long i = 0; i < bytes; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Runs a scene on all three backends frame for frame with the same poses and compares every frame: native
        /// against managed bit for bit, Burst against managed by the largest difference.
        /// </summary>
        public static JiggleParityResult Parity(JiggleBenchSpec s, int frames, int rigsPerBatch)
        {
            JiggleParityResult r = new JiggleParityResult();
            r.Scene = s.Name;
            r.Frames = frames;
            r.Finite = true;
            string nativeError;
            r.NativeAvailable = NativeAvailable(out nativeError);
            r.NativeError = nativeError;
            using (JiggleHost managed = JiggleBenchScenes.Build(s))
            using (JiggleHost native = r.NativeAvailable ? JiggleBenchScenes.Build(s) : null)
            using (JiggleHost burst = JiggleBenchScenes.Build(s))
            {
                for (int f = 0; f < frames; f++)
                {
                    StepOnce(managed, s, f, JiggleRuntimeBackend.Managed, rigsPerBatch);
                    if (native != null)
                    {
                        StepOnce(native, s, f, JiggleRuntimeBackend.Native, rigsPerBatch);
                        if (!SameBits(managed, native))
                        {
                            r.NativeFramesDiffering++;
                        }
                    }
                    StepOnce(burst, s, f, JiggleRuntimeBackend.Burst, rigsPerBatch);
                    if (!SameBits(managed, burst))
                    {
                        r.BurstFramesDiffering++;
                    }
                    CompareBurst(managed, burst, ref r);
                }
            }
            return r;
        }

        private static double Gap(float a, float b)
        {
            return Math.Abs((double)a - (double)b);
        }

        private static double Sum(float a, float b)
        {
            return Math.Abs((double)a + (double)b);
        }

        private static void CompareBurst(JiggleHost managed, JiggleHost burst, ref JiggleParityResult r)
        {
            for (int i = 0; i < managed.ParticleCount; i++)
            {
                JiggleParticleOut a = managed.Out[i];
                JiggleParticleOut b = burst.Out[i];
                r.Finite &= JiggleMath.IsFinite(b.Pos.X + b.Pos.Y + b.Pos.Z + b.Rot.X + b.Rot.Y + b.Rot.Z + b.Rot.W);
                r.BurstMaxPosition = Math.Max(r.BurstMaxPosition, Math.Max(Gap(a.Pos.X, b.Pos.X), Math.Max(Gap(a.Pos.Y, b.Pos.Y), Gap(a.Pos.Z, b.Pos.Z))));
                // q and -q are the same rotation, so the smaller of the two distances counts.
                double same = Math.Max(Math.Max(Gap(a.Rot.X, b.Rot.X), Gap(a.Rot.Y, b.Rot.Y)), Math.Max(Gap(a.Rot.Z, b.Rot.Z), Gap(a.Rot.W, b.Rot.W)));
                double flipped = Math.Max(Math.Max(Sum(a.Rot.X, b.Rot.X), Sum(a.Rot.Y, b.Rot.Y)), Math.Max(Sum(a.Rot.Z, b.Rot.Z), Sum(a.Rot.W, b.Rot.W)));
                r.BurstMaxRotation = Math.Max(r.BurstMaxRotation, Math.Min(same, flipped));
            }
        }
    }
}
