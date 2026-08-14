Burst-batched FFI marshaling goes here — same threshold pattern as
MidManStudio_Unity's `ProjectileSystem/Runtime/Adapters/BatchSpawnHelper.cs`:
small batches filled on the managed side, larger ones filled by a
`[BurstCompile] IJobParallelFor` before crossing into Rust.
