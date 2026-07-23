using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace MidManStudio.Chemistry
{
    /// <summary>
    /// P/Invoke bridge to the <c>chemistry_core</c> Rust library.
    /// All methods are static and unsafe — call from managed C# via <c>unsafe</c> blocks.
    /// </summary>
    public static class FFIBridge
    {
        // DLL name resolves to:
        //   Windows:  chemistry_core.dll
        //   Linux:    libchemistry_core.so
        //   macOS:    libchemistry_core.dylib
        //   iOS:      linked into the binary (__Internal)
#if UNITY_IOS && !UNITY_EDITOR
        private const string Lib = "__Internal";
#else
        private const string Lib = "chemistry_core";
#endif

        [DllImport(Lib, EntryPoint = "chem_init")]
        public static extern unsafe void Chem_Init(
            AtomData* atoms, int count, float temperatureK, ulong seed);

        [DllImport(Lib, EntryPoint = "chem_step")]
        public static extern unsafe void Chem_Step(
            AtomData* atoms, int count, float dt, float cutoffAngstroms);

        [DllImport(Lib, EntryPoint = "chem_kinetic_energy")]
        public static extern unsafe float Chem_KineticEnergy(
            AtomData* atoms, int count);

        [DllImport(Lib, EntryPoint = "chem_temperature")]
        public static extern unsafe float Chem_Temperature(
            AtomData* atoms, int count);

        [DllImport(Lib, EntryPoint = "chem_struct_size")]
        public static extern int Chem_StructSize();

        /// <summary>
        /// Must be called before any other method (e.g. in MonoBehaviour.Awake).
        /// Throws if the Rust and C# struct sizes do not match.
        /// </summary>
        public static void ValidateStructSizes()
        {
            int rustBytes = Chem_StructSize();
            int csBytes   = UnsafeUtility.SizeOf<AtomData>();
            Debug.Assert(rustBytes == 48,
                $"[Chemistry] Rust AtomState size = {rustBytes}, expected 48");
            Debug.Assert(csBytes == 48,
                $"[Chemistry] C# AtomData size = {csBytes}, expected 48");
            if (rustBytes != csBytes)
                throw new InvalidOperationException(
                    $"AtomData size mismatch: Rust={rustBytes} C#={csBytes}. " +
                    "Check struct field order and padding.");
            Debug.Log("[Chemistry] Struct validation passed. AtomData = 48 bytes.");
        }
    }
}
