using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MidManStudio.Chemistry
{
    /// <summary>
    /// C-compatible atom state struct.
    /// Must match Rust <c>AtomState</c> exactly (48 bytes, sequential layout).
    /// Verified at runtime via <see cref="FFIBridge.ValidateStructSizes"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AtomData
    {
        /// <summary>World-space position in Angstroms.</summary>
        public float3 position;       // offset  0 — matches Rust [f32; 3]
        public float3 velocity;       // offset 12
        /// <summary>Accumulated LJ force. Cleared by Rust at the start of each chem_step.</summary>
        public float3 force;          // offset 24
        /// <summary>Atomic mass in unified atomic mass units (amu).</summary>
        public float  mass;           // offset 36
        /// <summary>Van der Waals radius in picometres. Used for visual billboard scaling.</summary>
        public float  radius;         // offset 40
        /// <summary>Atomic number Z. Rust uses this to look up LJ epsilon/sigma from the DB.</summary>
        public int    atomicNumber;   // offset 44
    }
}
