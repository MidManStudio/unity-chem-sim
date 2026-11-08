// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleBurstSolver.cs"
// ============================================================================
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Runs the managed solver of a <see cref="JiggleHost"/> in Burst compiled jobs, one job index per batch of
    /// rigs. The Core code is the code that runs, compiled by Burst from the Core assembly, so it is the same kernel
    /// as the managed backend, held to tolerance parity rather than to the same bits.
    /// </summary>
    public static unsafe class JiggleBurstSolver
    {
        /// <summary>Run the step and then the resolve in one job.</summary>
        public const int PhaseBoth = 0;
        /// <summary>Run the step only.</summary>
        public const int PhaseStep = 1;
        /// <summary>Run the resolve only.</summary>
        public const int PhaseResolve = 2;

        /// <summary>How many job indices a host with the given rig count needs.</summary>
        public static int BatchCount(int rigs, int rigsPerBatch)
        {
            int b = rigsPerBatch < 1 ? 1 : rigsPerBatch;
            return (rigs + b - 1) / b;
        }

        /// <summary>
        /// Schedules the solver over every rig of the host and returns the handle. status points at
        /// <see cref="BatchCount"/> ints that must stay alive until the job is complete. The addresses of the host
        /// must not change meanwhile, so nothing may be added to it.
        /// </summary>
        public static JobHandle Schedule(JiggleHost host, int rigsPerBatch, int* status, int phase, JobHandle dependsOn)
        {
            int rigs = host.RigCount;
            int batch = rigsPerBatch < 1 ? 1 : rigsPerBatch;
            int batches = BatchCount(rigs, batch);
            if (batches == 0)
            {
                return dependsOn;
            }
            host.RefreshArgs();
            JiggleSolveJob job = new JiggleSolveJob();
            job.Cfg = host.Cfg;
            job.Args = host.Args;
            job.Status = status;
            job.RigCount = rigs;
            job.Batch = batch;
            job.Phase = phase;
            return job.Schedule(batches, 1, dependsOn);
        }

        /// <summary>The first failure in the status array, or 0 when every batch was fine.</summary>
        public static int Worst(int* status, int batches)
        {
            for (int i = 0; i < batches; i++)
            {
                if (status[i] != JiggleConst.Ok)
                {
                    return status[i];
                }
            }
            return JiggleConst.Ok;
        }

        /// <summary>Schedules, waits and returns the status. Allocates a small status array each call.</summary>
        public static int RunNow(JiggleHost host, int rigsPerBatch, int phase = PhaseBoth)
        {
            int batches = BatchCount(host.RigCount, rigsPerBatch);
            if (batches == 0)
            {
                return JiggleConst.Ok;
            }
            int* status = (int*)UnsafeUtility.Malloc((long)batches * sizeof(int), sizeof(int), Allocator.TempJob);
            UnsafeUtility.MemClear(status, (long)batches * sizeof(int));
            Schedule(host, rigsPerBatch, status, phase, default(JobHandle)).Complete();
            int code = Worst(status, batches);
            UnsafeUtility.Free(status, Allocator.TempJob);
            return code;
        }

        /// <summary>
        /// Whether Burst compiles the jobs of this package right now. It is false in a player built without Burst,
        /// with Burst switched off in the editor, or while the editor still compiles asynchronously.
        /// </summary>
        public static bool IsBurstCompiled()
        {
            int* result = (int*)UnsafeUtility.Malloc(sizeof(int), sizeof(int), Allocator.TempJob);
            *result = -1;
            JiggleProbeJob job = new JiggleProbeJob();
            job.Result = result;
            job.Schedule().Complete();
            bool compiled = *result == 1;
            UnsafeUtility.Free(result, Allocator.TempJob);
            return compiled;
        }
    }

    /// <summary>Runs the step and the resolve of a batch of rigs. Compiled synchronously, so the first run waits for Burst.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    internal unsafe struct JiggleSolveJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public JiggleStepConfig* Cfg;
        [NativeDisableUnsafePtrRestriction] public JiggleSolverArgs* Args;
        [NativeDisableUnsafePtrRestriction] public int* Status;
        public int RigCount;
        public int Batch;
        public int Phase;

        public void Execute(int batch)
        {
            int first = batch * Batch;
            int count = RigCount - first;
            if (count > Batch)
            {
                count = Batch;
            }
            if (count <= 0)
            {
                Status[batch] = JiggleConst.Ok;
                return;
            }
            int code = JiggleConst.Ok;
            if (Phase != JiggleBurstSolver.PhaseResolve)
            {
                code = JiggleSolver.StepRigs(Cfg, Args, (uint)first, (uint)count);
            }
            if (code == JiggleConst.Ok && Phase != JiggleBurstSolver.PhaseStep)
            {
                code = JiggleSolver.ResolveRigs(Cfg, Args, (uint)first, (uint)count);
            }
            Status[batch] = code;
        }
    }

    /// <summary>Tells whether it runs compiled: the discarded call only happens in the managed version.</summary>
    [BurstCompile]
    internal unsafe struct JiggleProbeJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public int* Result;

        public void Execute()
        {
            bool managed = false;
            MarkManaged(ref managed);
            *Result = managed ? 0 : 1;
        }

        [BurstDiscard]
        private static void MarkManaged(ref bool value)
        {
            value = true;
        }
    }
}
