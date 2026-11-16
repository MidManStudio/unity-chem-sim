# Procedural Terrain - Low Poly Worlds

`com.midmanstudio.proceduralterrain` builds low poly landscapes from a seed. The ground is one function of a seed, the
settings and a true position in metres, so a world of any size is made chunk by chunk, in any order, on any thread, and two
chunks that touch always agree along the border. There is no step that looks at the generated data as a whole.

What it does today:

- **Noise.** Gradient (Perlin style) or value noise, summed over octaves in three modes (smooth hills, billowy, ridged), with
  the persistence choices of the usual island generators. The sum is divided by the sum of the amplitudes, a fixed number, so
  nothing is ever stretched to the minimum and maximum of a chunk. A fixed remap stretches the practical range to the full
  range, with numbers you measure once (`TerraNoise.MeasureRange`) and type in
- **Island falloff.** Radial (the height fades with the distance from a centre) or edge (a border along a rectangle), applied
  to true coordinates so every chunk sees the same shore
- **Heights.** A curve from normalised noise to normalised height, a base and a scale in metres
- **Two ways to triangulate.**
  - the **jittered lattice**: a square grid of nodes with a hashed offset on each node, for endless worlds in chunks. The
    nodes on a chunk line are not moved across it, so a chunk is exactly a rectangle and neighbours share their border
    vertices to the last bit. Level of detail takes every second, fourth or eighth node, and a coarse vertex sits exactly on a fine one
  - **Poisson-disc points and Delaunay triangulation** for one island in one mesh. Both are written for this package
- **Terraces.** Flat steps with vertical walls, cut at the heights where one level ends and the next begins. Uniform bands or
  the biome edges, with an optional tilt and an optional flat lowest band. The cuts follow the triangle edges, so terraced
  chunks meet without a crack
- **Colour.** Biomes as bands of the normalised height with a gradient each, rock on steep ground, a colour for the terrace
  walls, paint from stamps and a small hashed variation. Flat shading (one colour and normal per triangle, the faceted look)
  or smooth shading (shared vertices, normals from the height field)
- **Stamps.** Pads (a town floor), roads (level across, following the ground along), carves (lakes and river channels) and
  mounds, each with a shape, a falloff, an order, optional paint and optional scatter blocking
- **Patches and locks.** A patch is a hand painted height image over a rectangle. A lock keeps a region of land when the world
  seed changes, because the noise inside it has a seed of its own
- **Scatter.** Trees, rocks and the like on the vertices, by height range, slope, density and a minimum distance, kept off
  stamps and off biome edges. The rule looks only at nearby candidates, so a chunk border cannot cut a forest in two
- **Skirts.** A wall under the edge of every chunk hides the crack between two levels of detail
- **Presets.** Rolling hills, archipelago, highlands and mesas, as plain settings objects to start from
- the Core has no Unity references, so the same files compile and run under plain .NET. That is how CI tests them

## State of this release

Version 0.1.0 is drop 1: the Core only. It builds the meshes as plain arrays (`TerraMeshData`) and has no Unity components,
no editor and no shader yet. **Nothing here has run inside Unity.** The Core is tested under .NET 8 (72 tests, which check
borders between chunks, terraces, scatter and the geometry). Drop 2 adds the Unity layer: chunk components and a streaming
manager, an editor window, a URP shader for the colours and the editor baking.

Read the known limits in `docs/com.midmanstudio.proceduralterrain.md`.

## Install

Add the package from the repository with Package Manager, "Add package from git URL":

    https://github.com/MidManStudio/unity-chem-sim.git?path=packages/com.midmanstudio.proceduralterrain

## Use

The Core is a library. A world is a settings object and a call:

```csharp
using MidManStudio.ProceduralTerrain;

TerraWorldSettings settings = TerraPresets.RollingHills();
settings.seed = 424242;

// A town floor at (300, 300), and a road from it to the north.
TerraStamp town = new TerraStamp();
town.kind = TerraStampKind.Pad;
town.x = 300.0;
town.z = 300.0;
town.radius = 60.0;
town.paint = true;
TerraStamp road = new TerraStamp();
road.kind = TerraStampKind.Road;
road.points = new[] { new TerraPoint(300.0, 300.0), new TerraPoint(320.0, 700.0) };
road.paint = true;
settings.height.stamps = new[] { town, road };

TerraWorld world = new TerraWorld(settings);

// One chunk of the endless world: chunk (1, 1) at full detail. The mesh is relative to the chunk's corner.
TerraChunkResult chunk = world.BuildChunk(1, 1, 1);
TerraMeshData mesh = chunk.mesh;            // positions, normals, colours, indices
TerraScatterPoint[] trees = world.ScatterChunk(1, 1);

// The height anywhere, from any thread: the same answer the mesh uses.
double y = world.HeightAt(300.0, 300.0);
```

Chunks of the lattice world are `lattice.spacing * lattice.cellsPerChunk` metres wide: 4 m and 64 cells make 256 m. Pick the
level of detail of a chunk with `world.StepForDistance(distance)` and pass it as the third argument. For one island in one
mesh, set `settings.island` and call `world.BuildIsland()`.

The coordinates, units and conventions are in `docs/com.midmanstudio.proceduralterrain.md`: true coordinates are doubles in
metres, mesh positions are floats relative to the chunk origin, colours are in gamma space, and triangles use Unity's winding.

## Tests

The Core and its tests run outside Unity:

    cd Tests~/DotnetHarness
    dotnet run -c Release

`Tests~/UnityStubs` compiles the Core and the tests the way their assembly definitions do. The editor tests also run in the
Unity Test Runner (Edit Mode).

## Layout

- `Runtime/Core` noise, height field, lattice, Poisson, Delaunay, terraces, colouring, scatter, the world and its settings
- `Tests/Editor` the NUnit tests
- `Tests~/DotnetHarness` runs those tests under .NET
- `Tests~/UnityStubs` compile projects, one per assembly definition
- `docs/com.midmanstudio.proceduralterrain.md` the design of each file, the known limits and the problems found

## Credits

The ideas come from public work: the island generation article of Glazycheva (the radial falloff, the curve, the named persistence choices) and the low poly
terrain paper and repository of Tivolt (the edge falloff, the terraces, Poisson-disc points with Delaunay triangulation). The code is written for
this package and none was copied. Neither source builds in chunks: the way chunks agree (the fixed normalisation, the jittered lattice, the cut
based terraces, the order independent scatter) is this package's own. No third party triangulation library is used (Triangle.NET was left out
because of its licence). Details are in the Origins section of `docs/com.midmanstudio.proceduralterrain.md`.
