// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleNative.cs"
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Every P/Invoke binding to jiggle_core, and the helper that turns the text buffer protocol into a
    /// string. Nothing else in the package uses DllImport directly.
    /// </summary>
    internal static unsafe class JiggleNative
    {
        // iOS and WebGL link the static library into the player, so the symbols
        // are looked up in the executable itself. Everywhere else it is a
        // dynamic library named jiggle_core.
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        public const string Lib = "__Internal";
#else
        public const string Lib = "jiggle_core";
#endif

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_library_version([Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_last_error([Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_struct_size(int kind);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_layout_probe(int kind, [Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_step_rigs(JiggleStepConfig* cfg, JiggleSolverArgs* args, uint first, uint count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int jgl_resolve_rigs(JiggleStepConfig* cfg, JiggleSolverArgs* args, uint first, uint count);

        /// <summary>Reads one of the native text functions into a string.</summary>
        public static string ReadText(Func<byte[], int, int> call)
        {
            byte[] buffer = new byte[256];
            int length = call(buffer, buffer.Length);
            if (length >= buffer.Length)
            {
                buffer = new byte[length + 1];
                length = call(buffer, buffer.Length);
            }
            if (length <= 0)
            {
                return string.Empty;
            }
            return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
        }
    }

    /// <summary>Checks that the native library is the one this code was written for.</summary>
    public static class JiggleLibrary
    {
        /// <summary>The native interface version this code was written against.</summary>
        public const int ExpectedAbiVersion = JiggleConst.ExpectedAbiVersion;

        /// <summary>The version of the native library, for example 0.0.1.</summary>
        public static string Version
        {
            get { return JiggleNative.ReadText(JiggleNative.jgl_library_version); }
        }

        /// <summary>The text of the most recent failure of a native call on this thread.</summary>
        public static string LastError
        {
            get { return JiggleNative.ReadText(JiggleNative.jgl_last_error); }
        }

        /// <summary>
        /// Throws when the native library is missing a function, has another interface version, or lays a struct
        /// out at another size than the C# declarations. Call once before the first rig is stepped natively.
        /// </summary>
        public static void Verify()
        {
            int abi = JiggleNative.jgl_abi_version();
            if (abi != ExpectedAbiVersion)
            {
                throw new InvalidOperationException(
                    "jiggle_core has interface version " + abi + " but this package expects " + ExpectedAbiVersion);
            }
            CheckSize(JiggleConst.KindParticleDef, Marshal.SizeOf(typeof(JiggleParticleDef)), "JiggleParticleDef");
            CheckSize(JiggleConst.KindParticleState, Marshal.SizeOf(typeof(JiggleParticleState)), "JiggleParticleState");
            CheckSize(JiggleConst.KindRig, Marshal.SizeOf(typeof(JiggleRig)), "JiggleRig");
            CheckSize(JiggleConst.KindStepConfig, Marshal.SizeOf(typeof(JiggleStepConfig)), "JiggleStepConfig");
            CheckSize(JiggleConst.KindCollider, Marshal.SizeOf(typeof(JiggleCollider)), "JiggleCollider");
            CheckSize(JiggleConst.KindField, Marshal.SizeOf(typeof(JiggleField)), "JiggleField");
            CheckSize(JiggleConst.KindParticleOut, Marshal.SizeOf(typeof(JiggleParticleOut)), "JiggleParticleOut");
            CheckSize(JiggleConst.KindSolverArgs, Marshal.SizeOf(typeof(JiggleSolverArgs)), "JiggleSolverArgs");
        }

        private static void CheckSize(int kind, int managed, string name)
        {
            int native = JiggleNative.jgl_struct_size(kind);
            if (native != managed)
            {
                throw new InvalidOperationException(
                    name + " is " + managed + " bytes in C# but " + native + " bytes in jiggle_core");
            }
        }
    }
}
