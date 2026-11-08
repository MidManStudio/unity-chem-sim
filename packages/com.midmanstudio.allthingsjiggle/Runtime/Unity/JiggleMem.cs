// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleMem.cs"
// ============================================================================
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>Zeroed persistent arrays in unmanaged memory, the way the jobs of this package hold their data.</summary>
    internal static unsafe class JiggleMem
    {
        /// <summary>An array of count entries (at least one), all bytes zero.</summary>
        public static T* Alloc<T>(int count) where T : unmanaged
        {
            long size = (long)UnsafeUtility.SizeOf<T>() * (long)(count < 1 ? 1 : count);
            void* p = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
            UnsafeUtility.MemClear(p, size);
            return (T*)p;
        }

        /// <summary>Frees an array and sets the pointer to null. A null pointer is ignored.</summary>
        public static void Free<T>(ref T* p) where T : unmanaged
        {
            if (p != null)
            {
                UnsafeUtility.Free(p, Allocator.Persistent);
                p = null;
            }
        }
    }
}
