// Complete FFI layer for chemistry_core Rust native library.
// ALL P/Invoke bindings live here. Nothing else uses DllImport.
//
// Platform DLL resolution:
//   iOS / WebGL : "__Internal" — resolved at link time by Xcode / Emscripten.
//   All others  : "chemistry_core" — loaded at runtime from Plugins/Native/.
//
// Struct size reference (must match Rust repr(C) exactly):
//   AtomHandle    = 8 bytes
//   AtomState     = 48 bytes
//   BondGeometry  = 8 bytes
//
// Rust bool ABI note, worth being explicit about: chemistry_core's Rust
// side returns `bool` directly from several FFI functions (despawn, get,
// is-bonded, the two bond accessors). Rust's `bool` is a guaranteed
// 1-byte value, always 0 or 1 — but C#'s DEFAULT P/Invoke marshaling for
// a `bool` return type is the 4-byte Win32 BOOL convention, not 1 byte,
// unless told otherwise. Getting this wrong is a well-documented P/Invoke
// footgun: the marshaler would expect 4 bytes where Rust only wrote 1,
// and whether that happens to still produce the right answer depends on
// register zero-extension behavior that varies by platform/ABI — exactly
// the kind of bug that works by accident on desktop and breaks on WebGL.
// Every such function here is declared returning `byte` instead (Rust's
// bool unambiguously IS a byte, no marshaling attribute needed either
// way), with a public bool-returning wrapper doing the `!= 0` conversion.
// Same avoid-raw-bool-across-FFI approach MidManStudio_Unity's own
// ProjectileLib.cs already uses (see e.g. its `IsAlive => Alive != 0`).
//
// Float triplets (position/velocity/force) are separate FieldOffset
// floats, not a nested Vector3/float3 field — same reason
// ProjectileLib.cs's own 3D structs do it that way: explicit-offset
// layout with a nested struct field is more surface area for a subtle
// mismatch than three floats, for zero benefit (the convenience
// properties below give the ergonomic access back). float3
// (Unity.Mathematics), not UnityEngine.Vector3, for those convenience
// properties specifically: this package already depends on
// com.unity.mathematics and states Burst-batched marshaling as a design
// goal (see Runtime/Adapters' own NOTE.md) — float3 is Burst/Job-system
// safe, Vector3 isn't.

using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace MidManStudio.Alembic.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  FFI structs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FFI-safe atom identity. Opaque beyond equality — never construct one
    /// by hand, only ever hold one returned by <see cref="ChemistryLib.chem_spawn_atom"/>.
    /// A stale handle (already despawned, or from a different context) isn't
    /// undefined behaviour to pass back in — every FFI call that takes one
    /// returns a clear "nothing there" result instead of crashing.
    /// 8 bytes — must match Rust AtomHandle repr(C) exactly.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct AtomHandle : IEquatable<AtomHandle>
    {
        [FieldOffset(0)] public uint Index;
        [FieldOffset(4)] public uint Generation;

        public bool Equals(AtomHandle other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is AtomHandle other && Equals(other);
        public override int GetHashCode() => (int)(Index ^ (Generation << 16) ^ (Generation >> 16));
        public static bool operator ==(AtomHandle a, AtomHandle b) => a.Equals(b);
        public static bool operator !=(AtomHandle a, AtomHandle b) => !a.Equals(b);
        public override string ToString() => $"AtomHandle(index={Index}, gen={Generation})";
    }

    /// <summary>
    /// C-compatible atom state — position, velocity, force, mass, radius,
    /// element. Read via <see cref="ChemistryLib.TryGetAtom"/> (one atom, by
    /// handle) or <see cref="ChemistryLib.chem_atoms_ptr"/> (the whole live
    /// array, zero-copy, for rendering — see that method's own doc for the
    /// "re-fetch every frame" rule before touching it).
    /// 48 bytes — must match Rust AtomState repr(C) exactly.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct AtomState
    {
        [FieldOffset(0)]  public float PositionX;
        [FieldOffset(4)]  public float PositionY;
        [FieldOffset(8)]  public float PositionZ;
        [FieldOffset(12)] public float VelocityX;
        [FieldOffset(16)] public float VelocityY;
        [FieldOffset(20)] public float VelocityZ;
        [FieldOffset(24)] public float ForceX;
        [FieldOffset(28)] public float ForceY;
        [FieldOffset(32)] public float ForceZ;
        [FieldOffset(36)] public float Mass;
        [FieldOffset(40)] public float Radius;
        [FieldOffset(44)] public int   AtomicNumber;

        public float3 Position => new float3(PositionX, PositionY, PositionZ);
        public float3 Velocity => new float3(VelocityX, VelocityY, VelocityZ);
        public float3 Force    => new float3(ForceX, ForceY, ForceZ);
    }

    /// <summary>
    /// Rest length and current live separation of one bond edge — enough to
    /// draw a stick between two bonded atoms, or show one straining before
    /// it snaps. 8 bytes — must match Rust BondGeometry repr(C) exactly.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct BondGeometry
    {
        [FieldOffset(0)] public float EquilibriumLength;
        [FieldOffset(4)] public float CurrentLength;

        /// <summary>
        /// (CurrentLength - EquilibriumLength) / EquilibriumLength. Not
        /// computed on the Rust side on purpose (see
        /// <see cref="ChemistryLib.chem_bond_geometry_at"/>'s own doc) — one
        /// line here instead of baking a "how should straining be
        /// visualized/thresholded" opinion into the FFI boundary.
        /// </summary>
        public float Strain => (CurrentLength - EquilibriumLength) / EquilibriumLength;
    }

    /// <summary>
    /// Plain data container for authoring a custom element — a
    /// ScriptableObject field, a JSON entry, whatever your own tooling
    /// wants to build one from. Not FFI-facing itself (no explicit layout
    /// needed) — <see cref="ChemistryLib.RegisterElement(CustomElementDefinition)"/>
    /// unpacks it into the same individual-argument call the direct
    /// overload uses. See <see cref="ChemistryLib.RegisterElement(int,float,float,float,float,float,float,float)"/>
    /// for the full contract (atomic number >= 1000, non-negative mass/
    /// radius/sigma).
    /// </summary>
    [Serializable]
    public struct CustomElementDefinition
    {
        public int AtomicNumber;
        public float MassAmu;
        public float RadiusVdwPm;
        public float LjSigmaA;
        public float LjEpsEv;
        public float Electronegativity;
        public float IonizationEnergyKjMol;
        public float ElectronAffinityKjMol;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  P/Invoke bindings
    // ─────────────────────────────────────────────────────────────────────────

    public static class ChemistryLib
    {
        // ── DLL name resolution ─────────────────────────────────────────────
        //
        // iOS  : static lib linked by Xcode → __Internal
        // WebGL: static lib linked by Emscripten → __Internal
        //        (Unity WebGL P/Invoke resolves via the same __Internal mechanism)
        // All others: runtime loaded from Plugins/Native/<platform>/
        //
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string DLL = "__Internal";
#else
        private const string DLL = "chemistry_core";
#endif

        // ── Context lifecycle ───────────────────────────────────────────────

        /// <summary>
        /// Create a persistent simulation context: owns the atom array, the
        /// spatial hash grid, and the scratch buffers the force kernels need.
        /// Create once (e.g. Awake), reuse for the simulation's lifetime,
        /// free exactly once with <see cref="chem_context_destroy"/> when
        /// done (e.g. OnDestroy). <paramref name="cutoffHint"/> just sizes
        /// the initial grid — 0f is fine, Step falls back to 10f either way.
        /// </summary>
        [DllImport(DLL)] public static extern IntPtr chem_context_create(float cutoffHint);

        /// <summary>
        /// Frees a context created by <see cref="chem_context_create"/>, and
        /// every atom still alive in it. Passing IntPtr.Zero is a no-op.
        /// Never call this twice on the same pointer, and never touch the
        /// pointer again afterward — same rules as any native free().
        /// </summary>
        [DllImport(DLL)] public static extern void chem_context_destroy(IntPtr ctx);

        // ── Atoms ────────────────────────────────────────────────────────────

        /// <summary>
        /// Spawn one atom of element <paramref name="atomicNumber"/> at
        /// (x, y, z). Mass and (rendering) radius are sourced from Rust's
        /// element-data table automatically. Returns a handle valid until
        /// the atom is despawned.
        /// </summary>
        [DllImport(DLL)] public static extern AtomHandle chem_spawn_atom(IntPtr ctx, int atomicNumber, float x, float y, float z);

        [DllImport(DLL)] private static extern byte chem_despawn_atom(IntPtr ctx, AtomHandle handle);
        /// <summary>Despawn an atom by handle. False if the handle was already stale.</summary>
        public static bool DespawnAtom(IntPtr ctx, AtomHandle handle) => chem_despawn_atom(ctx, handle) != 0;

        /// <summary>Current number of live atoms in the context.</summary>
        [DllImport(DLL)] public static extern int chem_atom_count(IntPtr ctx);

        [DllImport(DLL)] private static extern byte chem_get_atom(IntPtr ctx, AtomHandle handle, out AtomState state);
        /// <summary>Look up one atom's current state by handle. False (state left default) if the handle is stale.</summary>
        public static bool TryGetAtom(IntPtr ctx, AtomHandle handle, out AtomState state) => chem_get_atom(ctx, handle, out state) != 0;

        /// <summary>
        /// Read-only pointer into Rust's own dense atom array — for zero-copy
        /// rendering, <see cref="chem_atom_count"/> entries of
        /// <see cref="AtomState"/> starting here. Valid only until the next
        /// SpawnAtom/DespawnAtom/Step call on this context (a spawn can
        /// reallocate, a despawn reorders via swap-remove) — re-fetch every
        /// frame, never cache across a frame boundary. Array order is NOT
        /// stable across despawns either — fine for "draw N points
        /// somewhere", not fine for tracking a specific atom's array slot
        /// across frames (use <see cref="AtomHandle"/> + <see cref="TryGetAtom"/>
        /// for that instead).
        /// </summary>
        [DllImport(DLL)] public static extern IntPtr chem_atoms_ptr(IntPtr ctx);

        /// <summary>
        /// Read-only pointer into a dense <see cref="AtomHandle"/> array,
        /// same order and length as <see cref="chem_atoms_ptr"/>'s (index
        /// <c>i</c> here is the same atom as index <c>i</c> there) — for
        /// walking every live atom's identity (e.g. to then query bonds
        /// for each one) instead of just its physical state. Same
        /// "re-fetch every frame, don't cache across a frame boundary" and
        /// "order not stable across despawns" contract as
        /// <see cref="chem_atoms_ptr"/>.
        /// </summary>
        [DllImport(DLL)] public static extern IntPtr chem_handles_ptr(IntPtr ctx);

        /// <summary>
        /// Initialise every currently-live atom's velocity from a Maxwell-
        /// Boltzmann distribution at <paramref name="temperatureK"/>, zero
        /// their force accumulators. Call once after spawning your initial
        /// atoms.
        /// </summary>
        [DllImport(DLL)] public static extern void chem_init(IntPtr ctx, float temperatureK, ulong seed);

        /// <summary>
        /// Advance the simulation by <paramref name="dt"/> FEMTOSECONDS, not
        /// seconds — do not wire this straight to Time.deltaTime without
        /// converting; a per-frame femtosecond step needs to be a small,
        /// deliberately-chosen number, not seconds-to-femtoseconds at real
        /// framerate scale. <paramref name="cutoff"/> = 0f uses 10f Angstroms.
        /// </summary>
        [DllImport(DLL)] public static extern void chem_step(IntPtr ctx, float dt, float cutoff);

        /// <summary>Total kinetic energy of all currently-live atoms, in eV.</summary>
        [DllImport(DLL)] public static extern float chem_kinetic_energy(IntPtr ctx);

        /// <summary>Current temperature estimate in Kelvin (from equipartition).</summary>
        [DllImport(DLL)] public static extern float chem_temperature(IntPtr ctx);

        // ── Bonds ────────────────────────────────────────────────────────────

        [DllImport(DLL)] private static extern byte chem_is_bonded(IntPtr ctx, AtomHandle handle);
        /// <summary>Is this atom currently bonded to anything? False for a stale handle too, not a crash.</summary>
        public static bool IsBonded(IntPtr ctx, AtomHandle handle) => chem_is_bonded(ctx, handle) != 0;

        /// <summary>
        /// How many bonds this atom currently holds. 0 for a stale handle or
        /// an unbonded atom — same "nothing there" value either way,
        /// deliberately not distinguished. Bonds are unbounded per atom
        /// (chemistry_core's own simulation.rs docs cover why) — iterate
        /// <c>0..ChemBondCount(...)</c> with <see cref="TryGetBondPartner"/>
        /// / <see cref="TryGetBondGeometry"/> to walk every one.
        /// </summary>
        [DllImport(DLL)] public static extern int chem_bond_count(IntPtr ctx, AtomHandle handle);

        [DllImport(DLL)] private static extern byte chem_bond_partner_at(IntPtr ctx, AtomHandle handle, int index, out AtomHandle partner);
        /// <summary>
        /// This atom's <paramref name="index"/>-th bond partner
        /// (<c>0 .. chem_bond_count(...)</c>). False (partner left default)
        /// for a stale handle, an unbonded atom, or an out-of-range index —
        /// all the same "nothing there" case, deliberately not distinguished.
        /// Order isn't semantically meaningful (formation order, not
        /// distance or anything else), just stable within a single frame.
        /// </summary>
        public static bool TryGetBondPartner(IntPtr ctx, AtomHandle handle, int index, out AtomHandle partner) =>
            chem_bond_partner_at(ctx, handle, index, out partner) != 0;

        [DllImport(DLL)] private static extern byte chem_bond_geometry_at(IntPtr ctx, AtomHandle handle, int index, out BondGeometry geometry);
        /// <summary>
        /// Rest length and current live separation of this atom's
        /// <paramref name="index"/>-th bond edge — same indexing as
        /// <see cref="TryGetBondPartner"/>. <see cref="BondGeometry.CurrentLength"/>
        /// reflects whatever the last <see cref="chem_step"/> left it at, no
        /// extra call needed to refresh it.
        /// </summary>
        public static bool TryGetBondGeometry(IntPtr ctx, AtomHandle handle, int index, out BondGeometry geometry) =>
            chem_bond_geometry_at(ctx, handle, index, out geometry) != 0;

        // ── Custom elements ──────────────────────────────────────────────────
        //
        // Global, not per-context — see chem_register_element's own Rust-side
        // doc for why. None of these four take an IntPtr ctx at all, unlike
        // everything above; that's deliberate, not an oversight.
        //
        // This is the actual mechanism behind "end users can add elements
        // without recompiling Rust": register a fictional reagent (GTG's
        // Void-Carbon, Fae-Radon, Adrenium, or anything like it) purely from
        // C#/Editor tooling, no native rebuild involved. Real elements
        // (atomic number 1-118) are always rejected, on purpose — never
        // overridable through this path. Custom atomic numbers must be
        // >= 1000 — a deliberately large gap so "custom" is unambiguous at a
        // glance in any bug report, never confusable with a real-element typo.

        [DllImport(DLL)] private static extern byte chem_register_element(
            int atomicNumber, float massAmu, float radiusVdwPm, float ljSigmaA, float ljEpsEv,
            float electronegativity, float ionizationEnergyKjMol, float electronAffinityKjMol);

        /// <summary>
        /// Register (or overwrite) a custom element's simulation parameters
        /// at runtime. False (nothing registered) for: <paramref name="atomicNumber"/>
        /// under 1000 (real elements 1-118, and the reserved gap above them,
        /// are always rejected), or any of <paramref name="massAmu"/>/
        /// <paramref name="radiusVdwPm"/>/<paramref name="ljSigmaA"/> negative
        /// (physically nonsensical). <paramref name="electronegativity"/>/
        /// <paramref name="ionizationEnergyKjMol"/>/<paramref name="electronAffinityKjMol"/>
        /// have no such guard — 0f is already the correct "don't care" value
        /// for all three if you just want a physical LJ presence and don't
        /// care about reactivity. Persists for the process's lifetime once
        /// registered, including across multiple Editor Play Mode sessions —
        /// see <see cref="ClearCustomElements"/> for a clean slate.
        /// </summary>
        public static bool RegisterElement(
            int atomicNumber, float massAmu, float radiusVdwPm, float ljSigmaA, float ljEpsEv,
            float electronegativity = 0f, float ionizationEnergyKjMol = 0f, float electronAffinityKjMol = 0f) =>
            chem_register_element(atomicNumber, massAmu, radiusVdwPm, ljSigmaA, ljEpsEv,
                electronegativity, ionizationEnergyKjMol, electronAffinityKjMol) != 0;

        /// <summary>
        /// Convenience overload for data-driven authoring (a ScriptableObject
        /// custom-element asset, a JSON list, etc.) — same contract as the
        /// direct overload above.
        /// </summary>
        public static bool RegisterElement(CustomElementDefinition def) =>
            RegisterElement(def.AtomicNumber, def.MassAmu, def.RadiusVdwPm, def.LjSigmaA, def.LjEpsEv,
                def.Electronegativity, def.IonizationEnergyKjMol, def.ElectronAffinityKjMol);

        [DllImport(DLL)] private static extern byte chem_is_element_registered(int atomicNumber);
        /// <summary>True if this atomic number is either a real element or a previously-registered custom one.</summary>
        public static bool IsElementRegistered(int atomicNumber) => chem_is_element_registered(atomicNumber) != 0;

        [DllImport(DLL)] private static extern byte chem_unregister_element(int atomicNumber);
        /// <summary>Remove one previously-registered custom element. No-op (false) for a real element's atomic number.</summary>
        public static bool UnregisterElement(int atomicNumber) => chem_unregister_element(atomicNumber) != 0;

        /// <summary>Clear every custom-registered element at once. Real elements are never affected.</summary>
        [DllImport(DLL)] public static extern void chem_clear_custom_elements();
        public static void ClearCustomElements() => chem_clear_custom_elements();

        // ── Layout validation ───────────────────────────────────────────────

        [DllImport(DLL)] private static extern int chem_struct_size();
        [DllImport(DLL)] private static extern int chem_handle_size();
        [DllImport(DLL)] private static extern int chem_bond_geometry_size();

        private static bool? _isAvailable;

        /// <summary>
        /// True if the native library actually loaded on this platform/
        /// architecture. Checked once and cached for the rest of the
        /// session. Callers that can't function without the native lib
        /// should check this BEFORE calling any other extern method, and
        /// degrade gracefully (log + return) instead of letting the
        /// exception propagate — same pattern
        /// MidManStudio_Unity's own ProjectileLib.IsAvailable already uses.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;

                try
                {
                    _ = chem_struct_size();
                    _isAvailable = true;
                }
                catch (Exception ex) when (
                    ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    Debug.LogError(
                        $"[ChemistryLib] Native library '{DLL}' failed to load on this platform/" +
                        $"architecture: {ex.GetType().Name} — {ex.Message}. The chemistry system " +
                        "will be disabled on this device. Check the Plugin Importer CPU setting for " +
                        "Plugins/Native/Android/<abi>/libchemistry_core.so (per-ABI meta files need an " +
                        "explicit Android platform entry with the matching CPU tag, or Unity silently " +
                        "drops the .so from that ABI's build).");
                    _isAvailable = false;
                }

                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// Verify every C# struct size matches the compiled Rust library.
        /// Throws InvalidOperationException on mismatch, or if the native
        /// library isn't available on this platform/architecture at all.
        /// Call ONCE on startup (e.g. Awake) before any other FFI call.
        /// </summary>
        public static void ValidateStructSizes()
        {
            if (!IsAvailable)
                throw new InvalidOperationException(
                    $"[ChemistryLib] Native library '{DLL}' is not available on this platform/" +
                    "architecture — see the earlier error log for the underlying exception. " +
                    "All P/Invoke calls are unsafe until this is fixed.");

            bool ok = true;

            ok &= Check("AtomState", Marshal.SizeOf<AtomState>(), chem_struct_size(), 48);
            ok &= Check("AtomHandle", Marshal.SizeOf<AtomHandle>(), chem_handle_size(), 8);
            ok &= Check("BondGeometry", Marshal.SizeOf<BondGeometry>(), chem_bond_geometry_size(), 8);

            if (!ok)
                throw new InvalidOperationException(
                    "[ChemistryLib] One or more struct size mismatches detected. " +
                    "Check the Unity console for details. " +
                    "All P/Invoke calls are unsafe until the layout is corrected.");
        }

        private static bool Check(string name, int csharpSize, int rustSize, int expected)
        {
            bool ok = (csharpSize == rustSize) && (csharpSize == expected);
            if (!ok)
                Debug.LogError(
                    $"[ChemistryLib] STRUCT SIZE MISMATCH — {name}\n" +
                    $"  C# Marshal.SizeOf  = {csharpSize} bytes\n" +
                    $"  Rust sizeof        = {rustSize} bytes\n" +
                    $"  Expected           = {expected} bytes");
            return ok;
        }
    }
}
