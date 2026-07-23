# Getting Started

## Prerequisites

- Unity 2022.3 LTS or later (tested on 2022.3 and 2023.2)
- Rust stable toolchain — `rustup install stable`
- Unity Package Manager: install `com.unity.burst`, `com.unity.mathematics`, `com.unity.collections`
- mdix CLI — see https://github.com/Mid-D-Man/DixScript-Rust for build instructions

## First run

```bash
# 1. Compile the Rust library
./scripts/build_rust.sh
# Produces Assets/Plugins/libchemistry_core.so (or .dll / .dylib)

# 2. Open the project in Unity
# Unity will auto-import the native plugin from Assets/Plugins/
```

## Struct validation

Add this to any MonoBehaviour `Awake()` that uses chemistry:

```csharp
FFIBridge.ValidateStructSizes();   // throws if Rust/C# layout mismatch
```

## Loading element data

```csharp
// ChemDataLoader parses elements_database.mdix (compiled to JSON)
await ChemDataLoader.LoadAsync("mdix_files/chemistry_db/elements_database.json");
```

## Allocating atoms

```csharp
var atoms = new NativeArray<AtomData>(atomCount, Allocator.Persistent);
unsafe {
    var ptr = (AtomData*)atoms.GetUnsafePtr();
    FFIBridge.Chem_Init(ptr, atomCount, temperatureK: 300f, seed: 42UL);
}
```

## Per-frame update

```csharp
void Update() {
    unsafe {
        var ptr = (AtomData*)atoms.GetUnsafePtr();
        FFIBridge.Chem_Step(ptr, atoms.Length, Time.deltaTime, cutoff: 10f);
    }
    atomRenderer.UploadToGPU(atoms);
}
```
