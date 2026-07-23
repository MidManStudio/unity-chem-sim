# Architecture

## Layers

```
[DixScript chemistry_db]  ← element properties, LJ params, radii
        ↓ loaded at boot via ChemDataLoader
[C# AtomData NativeArray] ← Unity-managed, persistent allocation
        ↓ raw pointer (zero-copy FFI)
[Rust chemistry_core]     ← LJ forces, spatial hash, velocity Verlet
        ↓ positions written back into NativeArray
[C# AtomRenderer]         ← uploads to GraphicsBuffer
        ↓
[Graphics.DrawMesh]       ← single draw call, no GameObjects
```

## AtomState struct (48 bytes, both sides verified)

| Field | Type | Offset | Unit |
|---|---|---|---|
| position | float3 / [f32;3] | 0 | Angstroms |
| velocity | float3 / [f32;3] | 12 | Å/ps |
| force | float3 / [f32;3] | 24 | kcal/mol·Å |
| mass | float / f32 | 36 | amu |
| radius | float / f32 | 40 | pm (vdW) |
| atomicNumber | int / i32 | 44 | — |

## Rust simulation kernel

- Spatial hash grid rebuilt each frame (O(N), neighbour lookup O(1) within cutoff)
- LJ force: `4ε[(σ/r)¹²−(σ/r)⁶]`, cutoff at 10 Å
- Velocity Verlet integration: `v += F/m·dt ; x += v·dt`
- Per-element LJ parameters (ε, σ) loaded from the DixScript DB by atomic number Z

## Rendering

AtomRenderer reads positions from the NativeArray after each `chem_step`,
writes them into a `GraphicsBuffer`, then calls `Graphics.DrawMesh`.
Atoms are rendered as billboard quads (2 tris per atom) with CPK colouring
and size scaled from the vdW radius stored in `AtomData.radius`.

## DixScript data flow

The `elements_database.mdix` is compiled to JSON by the mdix CLI at build time.
`ChemDataLoader` reads the JSON, populates a `Dictionary<int, ElementData>` lookup,
and passes LJ parameters to Rust via a registration call.
