using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Adapters
{
    /// <summary>
    /// One bond edge, ready to draw — a TRS matrix spanning its two atoms
    /// plus a strain value. Doesn't carry color itself — same reason
    /// <see cref="BondRecord.Strain"/> isn't baked into the
    /// FFI struct either: "how should a straining bond be visualized" is
    /// a rendering opinion, left to <c>BondRenderer</c>.
    /// </summary>
    public struct BondDrawInfo
    {
        public float4x4 Trs;
        public float    Strain;
        /// <summary>
        /// False for a degenerate near-zero-length edge (both endpoints
        /// at ~the same position) — same case
        /// <c>BondRenderer.TryComputeBondTRS</c> used to guard against
        /// per-edge before this adapter existed. <see cref="Trs"/> is
        /// <c>float4x4.identity</c> when this is false, not meaningful,
        /// caller must skip.
        /// </summary>
        public bool Valid;
    }

    /// <summary>
    /// One bond's worth of pre-gathered input for <see cref="ComputeBondDrawJob"/> —
    /// both endpoint positions plus geometry, already resolved from
    /// <see cref="BondRecord"/>'s dense-array indices against
    /// this frame's <see cref="ChemistryLib.chem_atoms_ptr"/> array. A
    /// separate type from <see cref="BondRecord"/> on
    /// purpose: the job only ever needs positions, not indices, and
    /// keeping it self-contained means it never has to bounds-check
    /// against a second array internally.
    /// </summary>
    public struct BondDrawInput
    {
        public float3 PosA;
        public float3 PosB;
        public float  EquilibriumLength;
        public float  CurrentLength;
    }

    /// <summary>
    /// Burst-batched marshaling for chemistry_core's bulk bond accessor
    /// (<see cref="ChemistryLib.chem_bonds_ptr"/>) — same threshold
    /// pattern as MidManStudio_Unity's own
    /// <c>ProjectileSystem/Runtime/Adapters/BatchSpawnHelper.cs</c>: small
    /// batches computed on the managed side, larger ones computed by a
    /// <c>[BurstCompile] IJobParallelFor</c>.
    ///
    /// Genuinely different shape from <c>BatchSpawnHelper</c>'s own use,
    /// worth being explicit about: that helper Burst-fills data headed
    /// INTO Rust (spawn requests), before one FFI call. This one
    /// Burst-processes data that's already OUT of Rust (this frame's bond
    /// snapshot, fetched via <see cref="ChemistryLib.chem_bonds_ptr"/>/
    /// <see cref="ChemistryLib.chem_atoms_ptr"/>, both read-only
    /// accessors) — there's no further FFI call after this at all.
    ///
    /// This is the concrete fix <c>BondRenderer</c>'s own class doc
    /// flagged as the right move once bond counts got large: replacing a
    /// per-bond P/Invoke walk (<c>chem_bond_count</c> +
    /// <c>chem_bond_partner_at</c> + <c>chem_get_atom</c> ×2 +
    /// <c>chem_bond_geometry_at</c>, once per edge, every frame) with one
    /// bulk fetch, processed here.
    /// </summary>
    public static class BondBatchAdapter
    {
        /// <summary>
        /// Mirrors <c>BatchSpawnHelper.BurstThreshold</c>'s own value (8)
        /// rather than independently tuning a new one — worth revisiting
        /// with real profiling once this has actual bond-count data from
        /// a running game to tune against, same "tuning knob, not a
        /// physical constant" honesty chemistry_core's own
        /// MAX_INLINE_BONDS doc already models.
        /// </summary>
        public const int BurstThreshold = 8;

        // Growable managed scratch, reused frame to frame — same growth
        // policy (grow, never shrink) BondRenderer's own
        // _bondTrsScratch/_bondColorScratch already use.
        private static BondDrawInput[] _inputScratch = Array.Empty<BondDrawInput>();

        /// <summary>
        /// Fill <paramref name="output"/>[0 .. bondCount) with TRS +
        /// strain for every entry in <paramref name="bonds"/>[0 ..
        /// bondCount). <paramref name="bonds"/>/<paramref name="atoms"/>
        /// are expected to be this frame's raw
        /// <see cref="ChemistryLib.chem_bonds_ptr"/>/
        /// <see cref="ChemistryLib.chem_atoms_ptr"/> pointers, read-only
        /// for the duration of this call — nothing else (a spawn/despawn/
        /// step) may touch the context until this returns. Caller owns
        /// <paramref name="output"/>'s capacity (must be &gt;= bondCount);
        /// this never allocates or resizes it.
        /// </summary>
        public static unsafe void FillBatch(
            BondRecord* bonds, int bondCount,
            AtomState* atoms, int atomCount,
            BondDrawInfo[] output, float bondRadius)
        {
            EnsureInputCapacity(bondCount);

            // Cheap per-record gather — unsafe pointer reads only, no
            // P/Invoke, no marshaling. Same cost shape AtomRenderer/
            // BondRenderer already pay reading atoms[i].Position today,
            // just done once per bond here instead of once per atom
            // there. Deliberately not itself Burst-batched: this is a
            // handful of array reads per bond, not the actual math
            // (TRS/strain) that's worth parallelizing below.
            for (int i = 0; i < bondCount; i++)
            {
                BondRecord rec = bonds[i];
                if (rec.AtomAIndex >= (uint)atomCount || rec.AtomBIndex >= (uint)atomCount)
                {
                    // Defensive only — BondRecord's own doc guarantees
                    // valid dense positions as of the call that produced
                    // it, same instant this reads them. Zeroed
                    // EquilibriumLength here reads as a degenerate edge
                    // to ComputeBondDrawJob.Compute below (Strain guards
                    // on it), not a crash.
                    _inputScratch[i] = default;
                    continue;
                }
                _inputScratch[i] = new BondDrawInput
                {
                    PosA              = atoms[rec.AtomAIndex].Position,
                    PosB              = atoms[rec.AtomBIndex].Position,
                    EquilibriumLength = rec.EquilibriumLength,
                    CurrentLength     = rec.CurrentLength,
                };
            }

            if (bondCount >= BurstThreshold)
                FillBurst(bondCount, output, bondRadius);
            else
                FillManaged(bondCount, output, bondRadius);
        }

        private static void EnsureInputCapacity(int needed)
        {
            if (_inputScratch.Length < needed)
                _inputScratch = new BondDrawInput[Math.Max(needed, _inputScratch.Length * 2)];
        }

        // ── Managed fill (small batches) ────────────────────────────────

        private static void FillManaged(int count, BondDrawInfo[] output, float bondRadius)
        {
            for (int i = 0; i < count; i++)
                output[i] = ComputeBondDrawJob.Compute(_inputScratch[i], bondRadius);
        }

        // ── Burst fill (large batches) ──────────────────────────────────

        private static void FillBurst(int count, BondDrawInfo[] output, float bondRadius)
        {
            // NativeArray(T[], Allocator) copies the whole managed array,
            // including any slack past `count` left over from a prior,
            // larger frame — harmless (the job below only ever schedules
            // indices < count), but worth knowing this isn't a slice, same
            // as BatchSpawnHelper.FillBurst2D's own nativePts construction.
            using var nativeIn = new NativeArray<BondDrawInput>(_inputScratch, Allocator.TempJob);
            using var nativeOut = new NativeArray<BondDrawInfo>(count, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            new ComputeBondDrawJob
            {
                Input      = nativeIn,
                Out        = nativeOut,
                BondRadius = bondRadius,
            }.Schedule(count, 64).Complete();

            NativeArray<BondDrawInfo>.Copy(nativeOut, output, count);
        }
    }

    [BurstCompile]
    internal struct ComputeBondDrawJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<BondDrawInput> Input;
        [WriteOnly] public NativeArray<BondDrawInfo>  Out;
        public float BondRadius;

        public void Execute(int i) => Out[i] = Compute(Input[i], BondRadius);

        /// <summary>
        /// Shared math — called from both this Burst-compiled
        /// <see cref="Execute"/> and <c>BondBatchAdapter.FillManaged</c>'s
        /// plain C# loop for small batches, so the two paths can never
        /// quietly compute different answers. Same TRS construction
        /// <c>BondRenderer.TryComputeBondTRS</c> already used per-edge
        /// before this adapter existed, ported to Unity.Mathematics types
        /// throughout (not Vector3/Quaternion/Matrix4x4) since this has
        /// to compile inside a <c>[BurstCompile]</c> struct.
        /// </summary>
        public static BondDrawInfo Compute(BondDrawInput input, float bondRadius)
        {
            float3 delta  = input.PosB - input.PosA;
            float  length = math.length(delta);
            if (length < 1e-6f)
                return new BondDrawInfo { Trs = float4x4.identity, Strain = 0f, Valid = false };

            float3 mid   = (input.PosA + input.PosB) * 0.5f;
            quaternion rot = FromToRotationSafe(math.up(), delta / length);
            float3 scale = new float3(bondRadius * 2f, length * 0.5f, bondRadius * 2f);
            float4x4 trs = float4x4.TRS(mid, rot, scale);

            float strain = input.EquilibriumLength > 1e-6f
                ? (input.CurrentLength - input.EquilibriumLength) / input.EquilibriumLength
                : 0f;

            return new BondDrawInfo { Trs = trs, Strain = strain, Valid = true };
        }

        /// <summary>
        /// Unity.Mathematics has no built-in equivalent to
        /// <c>UnityEngine.Quaternion.FromToRotation</c> (checked directly
        /// against the real API surface before writing this, not
        /// assumed) — this is the standard shortest-arc construction:
        /// <c>q.xyz = cross(from, to)</c>, <c>q.w = 1 + dot(from, to)</c>,
        /// normalized. Both inputs must already be unit-length (true for
        /// every call site here: <c>math.up()</c> and a
        /// pre-normalized bond direction).
        ///
        /// Handles the two degenerate cases the simplified formula alone
        /// doesn't: `from`/`to` already aligned (returns identity rather
        /// than dividing by a near-zero magnitude), and `from`/`to`
        /// exactly opposed (cross product is ~zero too — genuinely
        /// undefined which perpendicular axis a 180-degree flip should
        /// use, so one is picked arbitrarily, same "pick *something*
        /// rather than propagate NaN" spirit as chemistry_core's own
        /// collinear-triple guard in <c>compute_angles</c>). A bond
        /// pointing straight down relative to <c>math.up()</c> is a real,
        /// unremarkable case in a 3D sim, not a corner case worth
        /// skipping.
        /// </summary>
        private static quaternion FromToRotationSafe(float3 from, float3 to)
        {
            float d = math.dot(from, to);
            if (d >= 1f - 1e-6f)
                return quaternion.identity;

            if (d <= -1f + 1e-6f)
            {
                float3 axis = math.cross(from, new float3(1f, 0f, 0f));
                if (math.lengthsq(axis) < 1e-6f)
                    axis = math.cross(from, new float3(0f, 0f, 1f));
                return quaternion.AxisAngle(math.normalize(axis), math.PI);
            }

            float3 c = math.cross(from, to);
            return math.normalize(new quaternion(c.x, c.y, c.z, 1f + d));
        }
    }
}
