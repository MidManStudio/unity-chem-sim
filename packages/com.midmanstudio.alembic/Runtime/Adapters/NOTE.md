Burst-batched FFI marshaling lives here — see `BondBatchAdapter.cs` for
the concrete implementation, same threshold pattern as
MidManStudio_Unity's `ProjectileSystem/Runtime/Adapters/BatchSpawnHelper.cs`:
small batches computed on the managed side, larger ones computed by a
`[BurstCompile] IJobParallelFor` — `BondBatchAdapter.BurstThreshold`
(mirrors `BatchSpawnHelper.BurstThreshold`'s own value, not yet
independently profiled).

Replaces `BondRenderer`'s old per-bond P/Invoke walk with one bulk fetch
via `ChemistryLib.chem_bonds_ptr` — see that method's own doc, and
`BondBatchAdapter`'s class doc, for the rest.
