# Changelog

## 0.1.0

Drop 1: the Core. No Unity layer, no editor and no shader yet. Nothing in this drop has run inside Unity: the Core is tested
under .NET 8 (72 tests in the harness).

- **Noise.** `TerraNoise`: gradient and value noise, Fbm, Billow and Ridged modes, persistence choices, per octave offsets.
  Normalised by the sum of the amplitudes (a fixed number), then a fixed remap. `MeasureRange` finds the practical range
- **Falloff.** `TerraFalloff`: radial and edge
- **Height field.** `TerraHeightField`: noise, locks, falloff, curve, base and scale, patches, then stamps in list order.
  Pure in a true position, so any chunk and any thread gets the same height
- **Stamps, patches, locks.** `TerraStamp` (pad, road, carve, mound; circle, box or polyline; falloff, paint, scatter
  blocking), `TerraPatch` (a height image over a rectangle), `TerraLock` (a region with a seed of its own)
- **Lattice.** `TerraLattice`: jittered square lattice, nodes on chunk lines kept on the lines, hashed quad diagonals, LOD steps
- **Poisson and Delaunay.** `TerraPoisson` (Bridson, with fixed border points) and `TerraDelaunay` (Bowyer-Watson with neighbour
  links), for a single island mesh
- **Terraces.** `TerraTerracer`: triangles are cut at the band edges, pieces are put on their level and vertical walls close
  the gaps. Uniform or biome bands, tilt, flat or following lowest band
- **Colour.** `TerraBiomeSet`, `TerraColoring`: biome bands with gradients, slope colour, wall colour, stamp paint, variation
- **Meshes.** `TerraMeshData` and `TerraMeshBuilder`: plain arrays, positions relative to the chunk origin, Unity winding,
  flat or smooth shading, skirts under the edges
- **Scatter.** `TerraScatter`: layers with height range, slope, density, minimum distance, biome edge margin, stamps blocking.
  Order independent selection, so chunk borders do not matter
- **World.** `TerraWorld`, `TerraWorldSettings`: `BuildChunk`, `ScatterChunk`, `BuildIsland`, `StepForDistance`
- **Presets.** `TerraPresets`: rolling hills, archipelago, highlands, mesas, tree and rock layers
- The dotnet harness, the compile projects for the asmdefs, and the workflow `terrain-core.yml`
