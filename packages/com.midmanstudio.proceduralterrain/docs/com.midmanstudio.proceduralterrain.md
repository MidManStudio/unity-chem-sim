# com.midmanstudio.proceduralterrain

Procedural Terrain is a Unity package that builds low poly landscapes from a seed. This file covers the Core, which is drop 1:
the noise, the height field, the two ways to triangulate (a jittered lattice for endless chunked worlds, Poisson-disc points
with a Delaunay triangulation for one island), the terraces, the colouring, the scatter and the world that ties them together.
The Core builds meshes as plain arrays and has no Unity references. Nothing here has run inside Unity yet (see Known limits).

## Contents

- Plan of the drops
- Using the package
- Layout and conventions
- Runtime/Core: TerraHash.cs, TerraColor.cs, TerraCurve.cs, TerraNoise.cs, TerraFalloff.cs, TerraStamp.cs, TerraBiome.cs,
  TerraHeightField.cs, TerraTriangulation.cs, TerraLattice.cs, TerraPoisson.cs, TerraDelaunay.cs, TerraMeshData.cs,
  TerraColoring.cs, TerraTerrace.cs, TerraScatter.cs, TerraWorld.cs, TerraPresets.cs
- Assembly definitions
- Tests/Editor/TerraBasicsTests.cs
- Tests/Editor/TerraHeightTests.cs
- Tests/Editor/TerraGeometryTests.cs
- Tests/Editor/TerraWorldTests.cs
- Tests/Editor/TerraTerraceTests.cs
- Tests/Editor/TerraScatterTests.cs
- Tests/Editor/TerraPresetTests.cs
- Tests~/DotnetHarness
- Tests~/UnityStubs
- CI and workflows
- Origins
- Known limits
- Fixes and Problems

## Plan of the drops

1. **Core** (this drop). Everything above, tested under .NET, no Unity types.
2. **Unity layer.** Components that turn a `TerraMeshData` into a `Mesh` and a chunk object, a streaming manager that builds
   chunks on worker threads and picks the level of detail by distance, a URP shader that reads the vertex colours, an editor
   window with the settings and a preview, and a baker that writes a finished area to assets. Written against stubs of the
   Unity API and compiled in CI, with the same limits as the other packages of the repository.
3. **The game side.** A Grand Theft Grimoire preset and its use in the world plan. That lives in the game repository, not here:
   this package does not know any game.

## Using the package

A world is a `TerraWorldSettings` and a `TerraWorld` compiled from it:

```csharp
TerraWorldSettings settings = TerraPresets.RollingHills();
settings.seed = 424242;
TerraWorld world = new TerraWorld(settings);
TerraChunkResult chunk = world.BuildChunk(1, 1, 1);     // chunk x, chunk z, LOD step
TerraScatterPoint[] trees = world.ScatterChunk(1, 1);
double y = world.HeightAt(300.0, 300.0);
```

`TerraWorld` is immutable once built: the height field, the lattice, the colouring, the terracer and the scatter all hold
only data derived from the settings, so one world can be called from many threads at once. Do not edit the settings objects
while a world built from them is in use. Pad heights, for one, are worked out when the world is built. Build a new world
after editing.

Two ways to make a mesh:

- `BuildChunk(cx, cz, step)` for the endless lattice world. A chunk is `spacing * cellsPerChunk` metres wide and is
  addressed by its integer chunk coordinates. `step` is the LOD step (1 is full detail, and it must divide `cellsPerChunk`).
  `StepForDistance(distance)` turns a distance into a step from `settings.lod`.
- `BuildIsland()` for one island in one mesh: Poisson-disc points over `settings.island`, a Delaunay triangulation of them,
  and the scatter of all of it. Not chunked, so there is no level of detail.

The two paths share the height field, the colouring, the terraces and the skirts.

## Layout and conventions

**True coordinates.** Positions in the world are `double` metres: x east, y up, z north. Every function of the height field
takes true coordinates, never chunk local ones. A chunk's mesh vertices are `float`, relative to the chunk origin in x and z
(`TerraMeshData.originX`, `originZ`) so they stay precise when the chunk is a hundred kilometres from the origin; y is the true
height. A game that shifts its origin (the Grand Theft Grimoire world plan does) adds `origin - shift` to the transform of the
chunk object and changes nothing else.

**Pure functions.** The height at a point depends on the seed, the settings and the point. It does not depend on the chunk that
asks, the level of detail, the order of calls or any state. That is what makes borders match. Every place that could have
broken it is named below (normalisation, jitter on chunk lines, cut points of terraces, scatter).

**Units.** Metres for distances and heights, degrees for slopes and yaw, 0..1 for the normalised value (the height between the
base and the base plus the scale). Biomes, terrace levels and scatter ranges all read that normalised value of the final height,
so a carved river turns the colour of deep water by itself.

**Winding.** Unity draws a triangle as a front face when its corners run clockwise as seen from the front. Triangulations are
stored counter-clockwise in (x, z) (`TerraGeom.Cross` positive), the opposite, and the mesh builder turns them. A test checks
that every triangle of every kind of mesh faces the way its stored normal points.

**Colours** are gamma-space values, the ones a colour picker shows. The shader of drop 2 converts them to linear.

**Indices.** `TerraMeshData.indices` is `int[]`. `RequiresUInt32` says whether 16 bits are enough. A flat shaded chunk of 64 by
64 cells has 26 112 vertices with skirts, which fits; a 256 m chunk at 2 m spacing (128 cells) has about 100 000 and does not.
A single island of 1 km by 1 km at a 6 m radius has 18 600 points and 36 500 triangles: flat shaded that is about 114 000 vertices (32 bit indices), smooth shaded about 22 700 (16 bit is enough).

**Language level.** C# 9 only, the level of Unity 2022.3. No `record`, no `init`, no target-typed `new`, no file-scoped namespaces.

**Namespace and prefix.** `MidManStudio.ProceduralTerrain`, types start with `Terra`.

**Notice header.** Every source file starts with a header that points to its section of this file.

## Runtime/Core

### TerraHash.cs

`TerraHash` is the one source of randomness. It is a hash, not a generator with state: `Hash(seed, a, b, c)` mixes the seed and
up to three integers with the splitmix64 finalizer and returns 64 bits. `ToUnit` makes a number in [0, 1) from the top 53 bits,
`ToSigned` one in [-1, 1), `Key(a, b)` packs two integers into one dictionary key, `SeedFrom(int)` widens a user's `int` seed.
Everything the package does at random (gradient choice, jitter, quad diagonals, scatter priority) is such a hash of integer
lattice coordinates, which is why a chunk can be rebuilt on its own. All arithmetic is `unchecked`.

`TerraRng` is a small generator (splitmix64 stepping a 64 bit state) for the few places that need a stream: the Poisson sampler
and the range measurement. It is a struct, so a copy continues the sequence on its own.

### TerraColor.cs

`TerraColor` is four floats and a few helpers (`FromHex`, `Lerp`, `Scaled`, `White`, `Gray`). It exists so the Core does not
need `UnityEngine.Color`. `TerraGradient` is a list of stops (position 0..1, colour); `Evaluate(t)` clamps t, finds the two
stops around it and interpolates, and `Solid` and `Two` build the common cases. The Unity layer converts to and from
`UnityEngine.Color` and `Gradient`.

### TerraCurve.cs

`TerraCurve` shapes the normalised noise into a normalised height, like the animation curve of the usual island generators.
Keys are (time, value) pairs. With `smooth` on, the curve between two keys is a cubic Hermite spline whose slopes are the ones
of Fritsch and Butland: zero at a local extreme, the weighted harmonic mean of the two neighbouring secants elsewhere. That
choice keeps the curve between the values of its two neighbouring keys, so a curve through monotone keys never overshoots
(a test samples one thousand points of a steep curve). The ends use the three point one sided slope with the usual monotone
limits. With `smooth` off, it is piecewise linear. `Compile()` builds `TerraCurveEval`, which holds the slopes; build it once,
evaluate it many times, read it from any thread. Before the first key and after the last, the end values hold.

### TerraNoise.cs

`TerraNoise(seed, settings)` is layered noise. One octave is gradient noise (a hashed unit vector at each lattice corner, one
of sixteen written out as numbers so no platform `sin` or `cos` can change a result, with the quintic fade of Perlin) or value
noise (a hashed number at each corner, blended with the same fade). Each octave is moved by a hashed offset so the octaves do
not line up at the origin.

The sum of octaves `n_i * a_i` is divided by the sum of the amplitudes `a_i`, which is the largest value it can reach, so
the result lies in [0, 1] and **no look at the generated data is needed**. This is the part of common island generators that
breaks a chunked world: they find the lowest and highest value of the whole map and stretch to those, and two chunks never find
the same ones. Here the divisor is a number fixed by the settings.

Gradient noise with unit gradients is bounded by the square root of 2 over 2 in two dimensions, so an octave is divided by that
bound and moved to 0..1. The harness measured the range actually used (300 by 300 points, seed 7, 100 m scale):

| noise | octaves | 1st percentile | 99th percentile |
|---|---|---|---|
| gradient Fbm | 1 | 0.168 | 0.832 |
| gradient Fbm | 4 | 0.289 | 0.712 |
| gradient Fbm | 6 | 0.299 | 0.703 |
| gradient Billow | 4 | 0.059 | 0.534 |
| gradient Ridged | 4 | 0.466 | 0.941 |
| value Fbm | 4 | 0.193 | 0.793 |

Adding octaves pulls the sum toward the middle, so with four octaves the biomes at the two ends of the list would almost never
appear. `remapMin` and `remapMax` of `TerraNoiseSettings` stretch the practical range: `Sample01` returns
`clamp((raw - remapMin) / (remapMax - remapMin))`. They are fixed numbers in the settings. `MeasureRange(lowShare, highShare,
samples, out low, out high)` finds them: it samples a fixed scatter of points and returns the values below which the given shares
of them fall. Run it once, in the editor or a test, and type the numbers in. Nothing is measured while chunks are built. The
presets use measured numbers and a test measures them again and compares.

Modes: `Fbm` uses the octave as it is, `Billow` its distance from the middle value `|2n - 1|` (rounded ridges and creases),
`Ridged` one minus that (sharp ridges). Persistence is `Custom` (a number) or one of the four fixed choices `Quarter`, `Half`,
`InverseSqrt` and `One`. Octaves are limited to 1..16, the scale and the lacunarity to a tiny positive number.

### TerraFalloff.cs

Two ways to close the map into an island, both in true coordinates and both applied after the noise and the locks:

- **Radial:** the weight is `1 - (d / radius)^exponent`, clamped, with `d` the distance from the centre. The noise is moved
  toward `groundValue` by one minus the weight. This is the falloff of the island generator of Glazycheva.
- **Edge:** the distance to the edge of a rectangle of half extents `halfExtentX`, `halfExtentZ` is divided by `border`; the
  weight is that to the power `exponent`, 1 inside and 0 at the edge. This is the border falloff of Tivolt's terrain.

`TerraFalloff.Apply` is `ground + (value - ground) * weight`. With `ground` 0 the shore is at the lowest ground.

### TerraStamp.cs

`TerraPoint` is an (x, z) pair. `TerraShape` has the distance functions: the signed distance to a rotated box, the distance to a
polyline with its nearest point, a smoothstep, and the weight `FalloffWeight(signedDistance, falloff)`, 1 inside the shape and
easing to 0 across the falloff.

`TerraStamp` is a deliberate change to the height field. `Influence(x, z, ...)` gives its weight at a point, 0..1; the height
field multiplies it by `strength`. The kinds:

- **Pad:** the ground is blended toward one height. The height is the ground at the stamp's centre *before this stamp* when
  `autoHeight` is on (worked out when the field is built, so a pad sits on a mound applied before it), or `targetHeight`; plus `heightOffset`.
- **Road:** a polyline of a given `width` with a falloff. The target height at a point is the ground at the **nearest point of
  the line**, before this stamp, plus `heightOffset`. So a road follows the ground along its length and is level across it, and
  where it meets a pad it takes the pad's height.
- **Carve:** lowers the ground by `amount` times the weight. A circle or box is a pit or a lake. With two or more points it is a
  channel along a polyline, a river.
- **Mound:** raises the ground by `amount` times the weight. A hill, a plateau, a platform for a tower.

Stamps apply in list order after the noise and the patches, so a later stamp sees the result of an earlier one. A stamp can
paint a colour (`paint`, `paintColor`, `paintThreshold`) and keep scatter off its ground (`blocksScatter`, true by default).

`TerraPatch` is a hand painted height image over a rectangle: `width * height` floats in 0..1, row by row from the minimum z,
mapped to `minHeight..maxHeight`, read bilinearly. It fades into the ground over `feather` metres inward from its edge. Its
mode is `Replace` (blend toward the image), `Add`, `Max` or `Min`. It is the hand made part of an otherwise generated landscape,
and the editor of drop 2 will paint and import them.

### TerraBiome.cs

`TerraBiome` is a band of the normalised value ending at `upTo`, with a gradient. A set holds them in order and
`IndexAt(value)` finds the band. `ColorAt(band, value)` evaluates the gradient across the band. The set also holds the
colours that belong to no band: `slopeColor` (blended in between `slopeStartDegrees` and `slopeFullDegrees`, by smoothstep),
`wallColor` for terrace walls, and `colorVariation`, the share by which each triangle's brightness is scaled at random
(0.04 by default, 0 turns it off). `CreateDefault()` gives a seven band palette from deep water to snow so a new set is never
empty. The last band always ends at 1.

### TerraHeightField.cs

The compiled height. For a point:

1. the noise (`TerraNoise.Sample01`), then every **lock** whose weight is above 0 moves it toward the noise of the lock's own
   seed by that weight, then the **falloff**;
2. the curve, then `baseHeight + curve * heightScale`;
3. the **patches**, in order;
4. the **stamps**, in order.

`TerraLock` is a circle or box with a seed of its own. Inside it the noise comes from the lock's seed, so changing the world
seed changes everything except the land inside the lock. It blends into the surrounding noise over its falloff. It is how a
hand picked valley survives a reroll of the world.

Each stamp has a bounding box worked out when the field is built, and a point outside it skips the stamp at once (a test
compares the shortcut with the full evaluation). Roads read the height before themselves at the nearest point on the line, which
costs a few extra evaluations per vertex near a road.

Other calls: `ValueOfHeight` and `HeightOfValue` (the normalised value of a height and back), `Normal(x, z, step, ...)`
(central differences of the field, the same in every chunk for a point, which is what makes smooth shading seamless),
`SlopeDegrees`, `ApplyPaint(x, z, baseColor)` (blends the paint of every painting stamp in order) and `BlocksScatter`.

### TerraTriangulation.cs

A set of points and triangles over them, counter-clockwise, and `TerraGeom` with `Cross` (twice the signed area) and `InCircle`
(the Delaunay predicate). Both triangulators return this.

### TerraLattice.cs

The jittered square lattice of chunked worlds. Node `(i, j)` is at `(i * spacing, j * spacing)` plus a hashed offset of up to
`jitter * spacing` in each axis. The offset is a function of the integer index and nothing else.

The one rule that makes borders match: **a node on a chunk line (an index that is a multiple of `cellsPerChunk`) is not moved
across that line.** So a chunk is exactly the rectangle between its four lines, the vertices on its side are the nodes of the
line, and the chunk on the other side computes the same nodes with the same arithmetic, to the last bit. Along the line a node
still moves, by its own hashed offset, so the border is not a straight regular row.

With `jitter` up to 0.25 of the spacing no triangle can fold over: in the worst case a corner of a quad moves 0.25 either way
and the smallest triangle keeps `s^2 (1 - 4j)` of area, positive below 0.25. Up to `MaxJitter` 0.45 is allowed; above 0.25 a
few triangles can fold, and they still draw (the builder orients each triangle from its positions).

The two triangles of a quad are cut along a diagonal chosen by a hash of the quad's fine index, so the pattern looks random but
is stable. `BuildChunk(cx, cz, step)` takes every `step`-th node, so a coarse vertex is exactly a fine one and the border of a
coarse chunk lies on the border of a fine one. The two borders still differ between the coarse vertices (the fine one has more
vertices), which is the crack the skirts hide. `step` must divide `cellsPerChunk`.

`ChunkOrigin(cx, cz)` is the south west corner, `ChunkOf(x, z)` the chunk of a point, `Node(i, j)` a node.

### TerraPoisson.cs

Bridson's Poisson-disc sampling over a rectangle: points no closer than `radius`, with no visible grid. A background grid of cell
`radius / sqrt 2` holds at most one point per cell, each active point tries `attempts` candidates in the ring between one and two
radii, and a candidate is kept when no point lies within a radius of it.

For a mesh that covers a rectangle exactly the border is seeded first: the four corners and points along each side no closer than
one radius (`floor(length / radius)` divisions, so a little more than a radius apart). The free points keep a radius from those, too.
The result depends on the seed and the arguments only. A rectangle with no area throws, and so does a radius so small that the
background grid would hold more than 40 million cells.

### TerraDelaunay.cs

Delaunay triangulation by the Bowyer-Watson method. The triangulator is written for the package. It keeps three vertices and three
neighbours for each triangle:

1. a super triangle 60 times the extent of the points is the start;
2. to insert a point, walk from the last triangle toward it (stepping across any edge the point is to the right of) to find its
   triangle;
3. flood outward from there across neighbours whose circumscribed circle holds the point: those triangles are the hole;
4. check that the point is strictly to the left of every edge of the hole's boundary. An edge it is not strictly left of (it
   lies on it, or rounding fooled the circle test) makes the triangle behind that edge part of the hole, and the check runs again;
5. remove the hole's triangles, join each edge of its boundary to the point, and link the new triangles to each other and to the
   triangles outside.

At the end every triangle that touches a super vertex is dropped. When the points include the corners of a rectangle and
points along its sides, what is left is that rectangle, covered once (tests: the areas add up to the rectangle's to 1e-6, the
triangle count is `2n - 2 - h`, no point lies in the circumscribed circle of a triangle). A walk that goes round in a circle on
degenerate input falls back to a scan, and fewer than three points give no triangles. Eighteen thousand points take under a tenth of a second on the build machine.

### TerraMeshData.cs

`TerraMeshData` is a mesh as arrays: `positions`, `normals` (3 floats a vertex), `colors` (4), `indices`, the vertex count, the
local origin and the bounds. It carries no Unity type, so it can be built on a worker thread and read by tests.

`TerraMeshBuilder` collects vertices and triangles. `AddFlatTriangle` adds a triangle with three vertices of its own and the
normal of its face, turned to look toward a hint direction (a ground triangle hints up, a wall hints downhill, a skirt hints out),
and drops a triangle with no area. `AddVertex` and `AddIndexed` are for shared vertices. `SetBorder` gives the rectangle the mesh is
cut to, `NoteEdge` remembers an edge that lies exactly along a side (an exact comparison: border vertices are the same numbers on
both sides), and `AddSkirts(depth)` hangs a vertical wall of that depth from every remembered edge, facing out, coloured like the
triangle it hangs from. Where two chunks of different detail meet, the wall of the finer chunk hides the gap between the coarse
vertices.

### TerraColoring.cs

The colour of a face: the biome at the face's height (the gradient inside the band), blended toward `slopeColor` by the slope
of the face, then the stamps' paint at the face's centre, then a brightness variation by a hash of the position, quantised to
quarter metres so a triangle keeps one factor. Terrace walls use `wallColor` and the same variation. All of it is a function of the
position and the height, nothing of the chunk.

### TerraTerrace.cs

Terraces are flat steps with vertical walls. The algorithm cuts each triangle at the band edges instead of moving whole
vertices, which gives true vertical walls and no slanted ones.

A terrace has levels: the edges are heights `e_0 < e_1 < ...` and band `b` lies between `e_{b-1}` and `e_b`. The level of band
`b` is its lower edge `L_b` (band 0 has none, see below). For one triangle with vertex heights `h`:

1. For each band the triangle reaches, take the part of the triangle with its heights inside the band. It is a convex polygon of up
   to five corners whose corners are original vertices and **cut points** where a triangle edge crosses a band edge.
2. Put the polygon on its level: each corner at height `L_b + tilt * (h - L_b)`. With `tilt` 0 that is `L_b`, perfectly flat. With
   `tilt` 1 the corners keep their own heights and the terraces vanish.
3. For each band edge the triangle crosses, take the segment where the plane at that height cuts the triangle and stand a
   vertical wall on it from the level of the band below to the level of the band above. The wall faces the downhill direction of
   the triangle's own plane, so it is seen from the lower step.

**Seamless.** A cut point on an edge is `a + (b - a) t` with `t = (level - h_a) / (h_b - h_a)`, computed with the two ends in a fixed
order (by x, then z). Two triangles that share an edge compute the same point to the last bit, in one chunk or in two neighbouring
chunks. The polygons are built from the original triangle edges and not by clipping a clip, so no second rounding can move a point.
That is what makes terraced chunks meet without a crack (a test compares the border vertices of neighbours and another checks
that the steps cover the chunk once).

The lowest band follows the ground (its tilt is 1) unless `flattenLowest` is on, so a lake bed or river carved below the first
edge is not lifted onto a floor. Modes: `Uniform` (`bands` equal bands of the height range) and `Biomes` (the biome edges).
Terraced meshes are always flat shaded, since a wall needs a hard edge. A terraced chunk has about 1.6 times the triangles of a
plain one (measured: 14 206 against 8 704 with skirts for a chunk of 64 by 64 cells).

The approach follows the idea of the terrace algorithm of Tivolt (flatten to the band, a wall where bands meet) and is derived
independently; the paper flattens whole vertices and repairs triangles that span bands.

### TerraScatter.cs

Where things stand. A `TerraScatterLayer` has a normalised value range, a maximum slope, a density, a minimum distance, a scale
range, a number of variants, an edge margin, a height offset and two flags for the placer (`alignToNormal`, `randomYaw`).

Candidates are the nodes of the mesh, so everything stands on a vertex: the lattice nodes at full detail for a chunk (whatever LOD
the chunk is drawn at, so trees do not move when the level of detail changes), or the points of the Poisson set for an island. A
candidate must pass, in this order: a hashed draw against the density; the value range of its height; not under a stamp that
blocks scatter; the slope (measured over one metre) below the maximum; and, with an `edgeMargin`, the biome band at four points
that distance away being the same as at the node, so nothing stands on a shore line.

Then **thinning**: a candidate is kept only when no better candidate of the same layer is within the minimum distance, "better"
being a hashed priority of its own index (ties broken by index). This rule looks only at the candidates near a point, not at an
order of visiting and not at the chunk, so it gives the same answer however it is asked. For a chunk, candidates are gathered in a
margin around it wider than the minimum distance and the jitter, and the chunk keeps the ones whose index lies inside it (an index
belongs to the chunk that holds it, half open). The test builds the same area with chunks of 16 and of 32 cells (no jitter, so the
nodes agree) and finds exactly the same points.

Layers do not look at each other: a tree and a rock may be close. The minimum distance is per layer.

### TerraWorld.cs

`TerraWorldSettings` has everything: seed, height settings, biomes, lattice, island, terrace, shading, LOD, skirt depth, water level
(for whatever draws water; the ground is not touched) and the scatter layers. `TerraWorld` compiles them: the field, the lattice, the
coloring, the terracer and the scatter.

`BuildChunk(cx, cz, step)`: the lattice triangulation of the chunk, a height for each vertex, then one of three paths, and last the
skirts. Terrace on: every triangle goes through the terracer. Smooth shading: vertices are shared, the normal and the colour are
taken at each vertex (the normal from the height field with a step of half the node spacing at that LOD), and triangles index
them in the order that gives Unity's winding. Flat shading: each triangle has its own vertices, the normal of its face, and one
colour taken at its centre.

`BuildIsland()`: Poisson-disc points over the island rectangle with a seed derived from the world seed, Delaunay, the same builder
with the rectangle as the border, and the scatter over the Poisson points. The mesh is relative to the centre of the rectangle. The
Archipelago preset over 1 km by 1 km took 254 ms here, with 1 022 scatter points.

`StepForDistance(distance)`: a chunk nearer than the first number of `lod.distances` gets 1, nearer than the second 2, then 4, and
so on, and the step is lowered until it divides the cells of a chunk. `HeightAt` is the field's height.

Timings on one core of a cloud machine (.NET 8, 64 by 64 cell chunks at 4 m, one chunk after warm up): flat 6 ms, smooth 10 ms, terraced
15 ms, with two stamps (a road and a pad) 5 ms; the scatter of a chunk takes 9 to 12 ms and finds about 380 points. A phone is several times slower; the manager of
drop 2 builds on worker threads.

### TerraPresets.cs

`RollingHills` (gradient Fbm, 240 m, four octaves, remap 0.29..0.71, with a tree and a rock layer), `Archipelago` (a radial
falloff over finer noise, built with `BuildIsland`), `Highlands` (Ridged noise, remap 0.47..0.94, 170 m of height) and `Mesas` (seven
uniform terraces). They are starting points: nobody has looked at them in an engine yet. `Trees` and `Rocks` build scatter layers.

## Assembly definitions

- `Runtime/MidManStudio.ProceduralTerrain.asmdef`: the Core. `noEngineReferences` is on, so a Unity type in it will not compile.
  No unsafe code.
- `Tests/Editor/MidManStudio.ProceduralTerrain.Tests.Editor.asmdef`: the tests, Edit Mode only, with the NUnit reference, behind
  `UNITY_INCLUDE_TESTS`.

Drop 2 adds `Runtime/Unity` and `Editor`.

## Tests

The tests are NUnit fixtures in `Tests/Editor` and run both in the Unity Test Runner and, through the stand in of `Tests~/DotnetHarness`,
under .NET.

### Tests/Editor/TerraBasicsTests.cs

The hash is deterministic, spreads (the mean of twenty thousand units is 0.5 to 0.01) and keys do not collide; the generator is
repeatable; gradients evaluate and clamp; the curve is the identity, never overshoots and stays monotone, and the linear form is linear;
the two falloffs give their documented weights; noise is pure (a call in between changes nothing), in range, different for another
seed, continuous (no jump larger than 0.03 over 800 m in steps of 0.1 m, both kinds), has the shapes of its modes exactly, resolves
persistence, stretches the measured range to fill 0..1, and clamps octaves.

### Tests/Editor/TerraHeightTests.cs

The height is pure; it follows the curve, base and scale; a pad flattens to the ground at its centre and leaves the rest alone; a
fixed height pad with an offset; a road is level across, follows the ground along and leaves the rest; carves and mounds move by their
amount (and strength); stamps apply in list order and a disabled stamp is skipped; patches replace, add and take the maximum with a
feather; a lock keeps its land when the world seed changes and the rest changes; the island falloff puts the edge on the base height;
normals are unit and point up, and the slope is their angle; paint and scatter blocking follow the stamp; the bounding box shortcut
skips nothing that has influence.

### Tests/Editor/TerraGeometryTests.cs

A lattice chunk is an exact rectangle of counter-clockwise triangles; neighbours share their border vertices exactly; the jitter stays
within its bound and leaves chunk lines alone; coarse nodes are fine nodes and a step that does not divide the chunk throws; without
jitter the lattice is regular; `ChunkOf`. Poisson keeps its distance, has the four corners and leaves no hole larger than two radii,
is deterministic and depends on the seed, and refuses an empty rectangle. Delaunay: empty circumscribed circles for 600 random points,
the areas of the triangles add up to the rectangle, the triangle count agrees with Euler's formula, collinear border points work,
degenerate input is handled, Poisson points triangulate to exactly their rectangle with every point used, and ten thousand points
triangulate within a generous time.

### Tests/Editor/TerraWorldTests.cs

A flat chunk has the expected triangles and winding, the right origin and a 16 bit index range; a smooth chunk shares its vertices and
all its normals point up; building twice gives the same arrays; neighbouring chunks, flat and smooth, east and south, have the same border vertices
(heights and positions); smooth normals and colours agree across the border; coarse border vertices are fine ones; skirts hang under the
border and face out; `StepForDistance`; stamps shape and paint the mesh (the town floor is level and painted); an island is
one mesh whose top area is the rectangle, flat and smooth; the 16 bit limit; and a chunk fifty thousand chunks from the origin is
precise.

### Tests/Editor/TerraTerraceTests.cs

Flat steps sit on the levels and walls are vertical and span whole steps; the steps tile the chunk exactly once for 2, 5 and 9 bands and walls
have no footprint; terraced neighbours meet; the winding of a tilted terrace is right; walls face downhill (all but about one in a hundred, since
a wall faces the downhill of its own coarse triangle); tilt 1 leaves the ground and no walls; the lowest band follows the ground unless
flattened; biome terraces use the biome edges; terraces force flat shading; terraces work on an island.

### Tests/Editor/TerraScatterTests.cs

Points keep their distance across chunk borders; four chunks of 16 cells hold exactly the points one chunk of 32 cells holds; points
belong to their chunk and are repeatable; the layer rules (value range, slope, offset, scale, variants, yaw, normal) hold; density
zero and a disabled layer place nothing and a tighter distance places fewer; stamps keep scatter off; biome edges are skipped; an island's
scatter keeps its distance.

### Tests/Editor/TerraPresetTests.cs

The remap numbers of the presets match a fresh measurement to 0.045, each preset reaches at least five of its biomes, and every preset builds.

## Tests~/DotnetHarness

`TerrainHarness.csproj` compiles `Runtime/Core` and `Tests/Editor` into a console program (net8.0, `LangVersion` 9.0, warnings as
errors, no unsafe) next to a small stand in for NUnit (`NUnitStubs.cs`, the same file the other packages of the repository use), and
`Program.cs` runs every `[Test]` method of every `[TestFixture]` class by reflection. An optional argument filters by name.

    cd Tests~/DotnetHarness
    dotnet run -c Release
    dotnet run -c Release -- TerraTerrace

Unity ignores the folder because its name ends in a tilde.

## Tests~/UnityStubs

`Core.csproj` compiles `Runtime/Core` with no references, as its asmdef does, so anything from Unity in it fails. `Tests.csproj` compiles
the tests against the Core and the NUnit stand in, as the test asmdef does. Drop 2 adds the projects for `Runtime/Unity` and `Editor` and the
stub of the Unity API.

## CI and workflows

`.github/workflows/terrain-core.yml` runs the harness and compiles the stub projects on every push that touches the package, and
after a successful manual run of Apply Replacements (those commits carry `[skip ci]`). It also checks that every asset has a `.meta` file.
The workflow is delivered next to the package, not inside it.

## Origins

The package is built from two public sources, the article of Glazycheva on island generation and the low poly terrain paper and
repository of Tivolt, and from standard algorithms. The code is written for this package and none was copied from the sources.

From Glazycheva's article: the radial falloff, the curve from noise to height and the named persistence choices.
From Tivolt's paper: the edge falloff and the terrace idea (flatten to the band, a wall where bands meet). Poisson-disc points with a Delaunay
triangulation and scattering on mesh vertices, away from biome edges, are in both. Tivolt's repository is MIT licensed.

What the sources do not have, and this package adds, is everything that makes chunks agree: neither source builds a world in chunks
(Tivolt names removing the falloff as the way to get chunks, Glazycheva lists chunk generation as not done). The fixed normalisation by the sum of
amplitudes and the measured remap, the jittered lattice with its chunk-line rule, the terraces made by cutting triangles with cut points
that agree to the last bit, the order independent scatter, the stamps, patches, locks and paint, and the island and lattice builders
over one height field are this package's own.

Standard algorithms: Bridson's for Poisson-disc sampling, the Bowyer-Watson method, gradient noise with a quintic fade (Perlin), the monotone
cubic of Fritsch and Butland.

Triangle.NET was not used. Its readme says that the Triangle files it contains are of unclear licence and that the author of Triangle
recommends against commercial use of them, and the project is not published as a package for that reason. The Delaunay here is its own.

## Known limits

- **Nothing has run in Unity.** The Core is tested under .NET 8 only. The Unity layer of drop 2 will be compiled against stubs, which
  proves it is well formed and nothing more.
- **Level of detail pops.** A chunk jumps from one step to the next. Skirts hide the gap between levels but nothing morphs. A terraced
  chunk shows more: the cut line of a band edge through a coarse triangle lies in another place than through the fine ones, so a cliff
  moves a little when the level changes. Use terraces with fewer levels of detail or a long distance for the first change.
- **Jitter above 0.25** can fold a few triangles (they still draw, but may overlap).
- **An island is one mesh.** A kilometre at a 6 m radius is 114 000 flat shaded vertices (over 16 bits of index), there is no level of detail, and
  building it takes about a quarter of a second on the build machine (160 ms of that the Poisson points). Use the lattice for large worlds.
- **Scatter layers ignore each other** and a candidate is a mesh node, so nothing stands between nodes.
- **Terraces cost triangles** (about 1.6 times) and are flat shaded only.
- **Roads follow the ground along their line**, so across a steep slope the road has a steep sideways cut, not a switchback.
- **Stamp heights are fixed when the world is built.** Editing a stamp needs a new `TerraWorld`.
- **The height of the lattice is evaluated once per vertex per chunk build.** A chunk and its neighbour both evaluate the border. That is
  what makes them agree and it costs one row.
- **No water.** `waterLevel` is a number for whatever draws water.

## Fixes and Problems

- **Normalising by the minimum and maximum.** The usual island generator stretches the noise between the smallest and the largest value
  it generated. In a chunked world each chunk would find its own, and neighbouring chunks would disagree. The sum is divided by the sum of
  the amplitudes instead, and the stretch is a fixed remap measured once.
- **The remap range was a guess.** The first plan was a remap of 0..1. Measuring showed that four octaves of gradient noise stay in 0.29 to 0.71
  for 98 per cent of the points, so the grass and snow bands at the ends would almost never appear. `MeasureRange` and the preset test came out of that.
- **Triangle.NET was dropped** because of its licence (see Origins). The Delaunay triangulator is written here, with the check that makes a
  hole star shaped when rounding fools the circle test.
- **Terraces by cutting triangles.** The first idea was to flatten whole vertices to their band, which makes slanted walls one triangle wide. Cutting
  the triangle at the band edges gives true vertical walls and needs no repair pass. The cut points had to be computed from the original triangle
  edge with the ends in a fixed order, or two triangles that share an edge differ in the last bit and leave a hairline crack.
- **Scatter by minimum distance, not by neighbour graph.** The first design thinned candidates against the neighbours in the mesh graph. A rule
  on distance needs no graph and works across chunk borders with nothing but the hashed priorities of the nodes around.
- **Test mistakes found while writing the tests:** a key value written as a `float` compared at 1e-9; a patch expected at the wrong
  pixel position (a bilinear read at 15 m of 100 is 15 per cent along); the 16 bit test used the wrong cell count; the border comparison for
  flat meshes compared lists with repeats (each triangle repeats its vertex) instead of sets; the lowest band test picked a chunk with no
  low ground; walls were expected to face downhill everywhere (about one in two hundred disagreed with the field by a coarse triangle's error, so the test allows one in a hundred);
  and tilt 1 was expected to put cut points on the field (a cut point lies on the triangle, as the plain mesh does).
- **`dotnet` could not restore** in the build container (NuGet is not reachable there). Restoring against an empty local source
  (`dotnet restore -s <empty folder>`) works because nothing needs downloading. CI is not affected.
