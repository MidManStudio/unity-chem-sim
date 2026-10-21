# com.midmanstudio.nuclear

Unity bindings for the `nuclear_core` Rust library. The design of the library is in `rust_lib/nuclear_core/docs/nuclear_core.md`. This file covers the package: the C# files, the tests, the native plugins and the known problems. Each source file points at the section here that carries the name of its path.

## Overview

The package is a thin, typed layer over the C interface of `nuclear_core` (interface version 1, 64 functions). It has no dependencies and no Unity engine references (`noEngineReferences` in the asmdef), so the files compile under plain .NET, which is how CI runs them. Reactions stay at event level: the caller says how many events happen, and rates, fluxes and chain reactions are the game's to build.

Types fall in three groups. Value structs mirror the native structs byte for byte with explicit offsets. Static classes (`NuclearData`, `NuclearFusion`) answer questions that need no pile. Handle classes (`NuclearContext`, `NuclearSample`, `NuclearAmounts`) own native objects and are disposable, with a finalizer as a safety net.

## Runtime/Core/NuclearNative.cs
**What it does:** Every `DllImport`, the native buffer protocols as `ReadList` and `ReadText`, `Check` that turns a negative status into a `NuclearException`, and `NuclearLibrary.Initialize`.

**Decisions:**
- All P/Invoke lives here, as in the Alembic package. iOS and WebGL use `__Internal` because the static library is linked into the player. Everything else uses the name `nuclear_core`.
- `NuclearLibrary.Initialize` runs before any other call and checks the interface version and the size of all 16 structs against the native `nuc_struct_size`. A stale binary committed by an old CI run therefore fails with a message that names the cause, instead of corrupting memory.
- Lists and text follow the native protocols: call with capacity 0 to get the count, then again to fill, and retry once if the count grew in between.

## Runtime/Core/NuclearStructs.cs
**What it does:** The enums and the explicit-layout value structs that cross the boundary.

**Decisions:**
- `LayoutKind.Explicit` with `FieldOffset` and `Size`, as in the Alembic package, so no padding rule differs between Mono, IL2CPP and CoreCLR. Native structs carry explicit reserved fields where C would pad, and the C# side leaves those gaps empty.
- Native arrays become numbered fields (`emitted0` to `emitted3`, three product slots), because C# has no inline arrays that work across the Unity versions in use.
- A `bool` is a native `u8` and a property here. Nuclides are `uint` keys with a `Nuclide` property.
- Warnings 0649 and 0169 are switched off for the file, because native code fills the fields.

## Runtime/Core/Nuclide.cs
**What it does:** `Nuclide`, a readonly struct of protons and neutrons with the 32-bit key, parsing and printing through the native library, ordering that matches the native tables, and constants for the neutron, H-1, H-2, H-3, He-3 and He-4.

## Runtime/Core/NuclearException.cs
**What it does:** `NuclearStatus`, the status codes, and `NuclearException`, thrown for every negative status with the native last-error text as its message.

## Runtime/Core/NuclearData.cs
**What it does:** Static queries that need no pile: element symbols, the list of 3558 nuclides, mass excess, binding energy, reaction Q-values, half-lives, abundance, decay outcomes, decay energies, chains to stability, neutron branching, capture energy and the nucleus that splits after a delayed fission.

**Decisions:** Queries that can reasonably find nothing have a `Try` form (`TryGetHalfLife`, `TryGetNeutronBranching`). The rest throw.

## Runtime/Core/NuclearFusion.cs
**What it does:** The seven fusion channels by index: description, name, thermal reactivity at a temperature, the channels of a pair, and the share of events per channel.

## Runtime/Core/NuclearContext.cs
**What it does:** The shared state of a simulation: the random stream, the decay cache and the fission cache. It also answers the fission queries, because those use its cache.

**Decisions:**
- The generator state can be read and set as four 64-bit words, so a saved game continues the same random stream.
- One context serves many piles. A context is not thread safe. Separate contexts can run on separate threads.

## Runtime/Core/NuclearSample.cs
**What it does:** A pile of whole atoms: add, count, list, totals, `Advance`, `ResolveFissions`, `Irradiate` and `Fuse`. Outcomes are exact draws.

**Decisions:** `Irradiate` has an overload that takes the authored fission probability from the neutron table and one that takes the caller's number, for nuclei the table does not cover. A failed call leaves the pile unchanged, as in the Rust API.

## Runtime/Core/NuclearAmounts.cs
**What it does:** The same operations on fractional amounts, giving the exact expected result instead of a draw.

## Runtime/Core/AssemblyInfo.cs
**What it does:** Lets the editor test assembly see the internal bindings, which the field-by-field layout check needs.

## Tests/Editor/NuclearNativeTests.cs
**What it does:** Thirteen NUnit tests: interface version, layout of every struct field by field, nuclide parsing, masses and energies, decay data, neutron branching, fission and fusion queries, determinism by seed, saved generator state, reactions with their error cases, amounts, and disposed handles.

**Decisions:**
- The layout test asks the native library for a struct filled with known values (`nuc_layout_probe`) and reads every field through the C# declaration, ordered by offset. The k-th named field must hold k, or k + 0.25 for a double. That proves each offset and not only the total size.
- The file uses only classic NUnit assertions and no Unity types, so it compiles under plain .NET with the small NUnit stand-in in `Tests~/DotnetHarness`.
- The tests need the native library. In the Editor they run once the build workflow has committed the binaries for the Editor platform. The Linux binary has its importer's Editor switch off, so run the tests on macOS or Windows, or rely on CI.

## Samples~/NuclearLab
**What it does:** A drop-in Play Mode panel with four tabs: Nuclide (look up one nuclide), Decay (step a pile of atoms through time), Fission (outcomes of a nucleus, an animation of one split with a flash sized to its energy, and a neutron irradiation) and Fusion (the channels, reactivity against temperature, and a pile fused at a temperature). The user adds `NuclearLabView` to an empty GameObject and presses Play. The folder also has an asmdef, a README for the person who imports it, and the files listed in the sections below.

**Decisions:**
- Plain IMGUI. The scene needs no camera, canvas, material or input handling, and the sample has no asset files, so it imports as source only.
- The folder follows the convention of the Alembic Playground: it sits under `Samples~`, has its own asmdef that references the package asmdef by name, and has its own README. Unlike the Playground it is registered in the `samples` array of `package.json`, so Package Manager offers an Import button.
- Model and view are separate files. The model files (`NuclearLabFormat`, `NuclearLabInput`, `NuclearLabQueue`, `NuclearLabRandom`, `NuclearLabBlast`, `NuclearLabShape`, `NuclideReport`, `DecayLab`, `FissionLab`, `FissionFrame`, `FissionShowPlan`, `FissionShow`, `FusionLab`) hold every call into the package, every number the panel shows and every position and size of the animation, and they have no UnityEngine reference. The view files (`NuclearLabStyles`, `NuclearLabDraw`, `NuclearLabStage`, `NuclearLabTab`, the four tabs and `NuclearLabView`) only draw and collect input. That split lets `Tests~/DotnetHarness` compile and run the model files under plain .NET.
- The fission animation is computed in the model files and only painted in the view. `FissionShow` turns a clock into a `FissionFrame`, a list of discs, glows and rings in stage units, and `NuclearLabStage` paints that list. Every timing, position and size is therefore testable without Unity.
- The content is generic and tied to no game. The preset buttons are common teaching cases (Cs-137, Sr-90, U-235 and so on) and every one is a plain text field the user can overwrite.
- Every piece of work a click starts goes through `NuclearLabQueue` and runs from `Update`. See the section for that file.
- Event level only. The Fission and Fusion tabs say how many events happen and show what the library returns. They model no geometry, flux or chain reaction. The fission animation shows one event, drawn from the channels the library publishes, and its flash is sized to the energy the library reports.

## Samples~/NuclearLab/MidManStudio.Nuclear.Samples.NuclearLab.asmdef
**What it does:** The assembly of the sample. It references `MidManStudio.Nuclear` by name and is auto-referenced, so the files compile in the project that imports them. `noEngineReferences` is false, because the view files use UnityEngine.

## Samples~/NuclearLab/README.md
**What it does:** The page for a person who has imported the sample: what each tab shows, what the sample needs (a native library for the Editor platform), the three setup steps, the controls and the limits of the checks. The note on the Input System comes from a Unity issue report and is worded as a report, because it was not reproduced here.

## Samples~/NuclearLab/NuclearLabFormat.cs
**What it does:** Static text helpers, with no UnityEngine reference: numbers, durations, counts, energies (eV, keV, MeV), joules, kelvin, percentages, half-life text, the names of the 31 decay modes, chain end reasons, energy sources, yield sources and fit kinds, the bar fraction used by the plots, and tolerant parsing of numbers and counts.

**Decisions:**
- `Number` prints four significant digits. Values from 0.001 up to 1000 use the general format, values from 1000 up to a million print as whole numbers with thousands separators, and everything else prints in scientific form. Invariant culture throughout, so a phone set to another locale prints the same text.
- `Amount` prints a count when the value is whole to within 1e-6, and a number otherwise. A pile of expected amounts therefore shows fractions, and a pile of whole atoms shows counts.
- `Duration` picks the unit that reads best, from nanoseconds to years, with a year of 31557600 s (365.25 days).
- `BarFraction` clamps to 0..1 and returns 0 for a value a log scale cannot place (zero or negative), so a missing bar draws as an empty bar and never throws.
- `TryParseCount` accepts `1000000`, `1,000,000` and `1e6`, and refuses negative and fractional values.

## Samples~/NuclearLab/NuclearLabInput.cs
**What it does:** Turns the text of a field into a typed value: a nuclide, a count, a number and a step length in seconds.

**Decisions:** Every failure throws a `FormatException` whose message starts with the name of the field (`Atoms must be a whole number such as 1000000 or 1e6.`), so the red line under a tab tells the user which box to fix. `ParseNuclide` goes through `Nuclide.Parse`, so it accepts exactly what the package accepts: `U-235`, `H-2`, `n`. `D` and `T` are not names the library parses.

## Samples~/NuclearLab/NuclearLabQueue.cs
**What it does:** A first-in first-out list of actions. Tabs add an action when a button is clicked, and `NuclearLabView.Update` runs the list.

**Decisions:**
- IMGUI draws in two passes per frame (Layout, then Repaint, with input events between). A click that changes how many controls the next pass draws, such as the first pile appearing or a tab switching, makes the layout of that frame disagree with the controls and Unity reports a layout error. Running the change from `Update` puts it between frames, where no pass is open.
- `RunAll` empties the list before it runs the first action, so an action that throws is not run again. Actions added while the list runs wait for the next call.
- The class has no UnityEngine reference, so the harness tests its ordering and its deferral rule.

## Samples~/NuclearLab/NuclideReport.cs
**What it does:** Collects everything the Nuclide tab shows into one object: mass excess, binding energy, half-life, natural abundance, each decay outcome with its decay energy, and the chain to a stable nuclide as text lines with the reason it ends. `DescribeOutcome` turns an outcome into text such as `beta- -> Ba-137`.

**Decisions:** The decay energy of an outcome is looked up per outcome and a `NuclearException` becomes "no energy", because the tables have none for some modes (spontaneous fission has none). A nuclide with no decay modes gives an empty outcome list, and the tab then says whether it is stable or simply not listed.

## Samples~/NuclearLab/DecayLab.cs
**What it does:** One pile, either whole atoms (`NuclearSample`, exact draws) or fractional amounts (`NuclearAmounts`, exact expectation), with the context that steps it. It offers `Reset`, `Step`, `ResolveFissions`, the rows of the pile sorted by amount, the nuclides waiting for fission, the totals and the elapsed time.

**Decisions:**
- Both kinds report through the same members, so the tab does not care which one runs.
- `Reset` builds the new pile first and swaps it in only when that worked, so a failed Start (a bad nuclide, zero atoms) leaves the old pile on screen.
- Spontaneous fission does not resolve by itself. A step moves the fissioning atoms to a pending list and the user presses Resolve fissions, as the package defines.
- `Baryons` is the nucleon count of the pile now, pending fissions included, and the tab compares it with `StartBaryons` after every step. A mismatch shows as "NOT conserved". The tests assert the match for both kinds.
- The amounts kind reports energy in keV times the amount unit. When the amounts count atoms, that is total keV, which is what the tab prints.

## Samples~/NuclearLab/FissionLab.cs
**What it does:** `Query` reads the summary, the likeliest channels, the likeliest products and the whole yield list of a nucleus at a neutron energy (0 eV means spontaneous), and sums the yields into a histogram by mass number. It also reads the authored fission share and the capture energy when the tables have them. `FissionInfo.AllChannels` keeps every channel, the likeliest first, and `TopChannels` is the head of the same list. The animation draws its outcome from `AllChannels`. `Run` builds a pile of the nucleus, sends neutrons at it and returns the report, the totals and the products.

**Decisions:**
- Queries use a context of their own, because the package answers fission queries through the context's cache. The seed of that context does not matter to a query. Each `Run` builds and frees its own context and pile, so a run depends only on its arguments.
- `Run` takes an optional fission share. Without one, the authored table decides, and a nucleus outside the table is refused by the package with a status the tab turns into a red line. With one, any nucleus can be tried.
- The histogram sums the yield of every product with the same mass number. Two fragments leave each fission, so the sum over all masses is 2. The test checks that.

## Samples~/NuclearLab/FusionLab.cs
**What it does:** The static `FusionLab`: the seven channels as rows (`AllChannels`), the channels of a pair with their share of events and reactivity at a temperature (`QueryPair`), log-spaced temperatures (`Temperatures`), the reactivity of one channel over those temperatures (`ReactivityCurve`) and a pile run (`Run`).

**Decisions:**
- `QueryPair` refuses a temperature of zero or less. The package does not throw there, so the sample checks it, because a log slider and a log plot are meaningless at 0.
- `InsideFit` is true for any channel without a Bosch-Hale fit and, for the others, when the temperature lies inside the published range. The tab prints "outside the published fit, extrapolated" for the rest, so a reader does not mistake an extrapolated number for data.
- `Temperatures` returns the two end points exactly, so they match the limits of the slider.
- `Run` adds one kind of atom when both nuclei are the same, and the nucleon count at the start follows that rule.

## Samples~/NuclearLab/NuclearLabRandom.cs
**What it does:** A small seeded random stream (splitmix64): `NextBits`, `NextDouble` in [0, 1), `Range(min, max)` and the static `Mix(seed, index)`, which gives the n-th independent stream of one seed.

**Decisions:**
- The animation needs only a few random choices (which channel, a direction, a spread), so it has its own stream and never touches the package's context. A show then depends on its seed and nothing else, and the same seed draws the same show on every platform.
- `Mix` lets the Split one atom button draw the next outcome of the same seed on every press and still be reproducible: press n uses `Mix(seed, n)`.
- The first two outputs of seed 0 are fixed in the tests (0xE220A8397B1DCDAF, 0x6E789E6AA1B965F4). They are the splitmix64 reference values, recomputed independently of the C# code.

## Samples~/NuclearLab/NuclearLabBlast.cs
**What it does:** Turns an energy into the size of the flash and into words. `Scale(joules)` and `ScaleKev(kev)` return 0 to 1, and `TntText(joules)` returns text such as `about 2.5 g of TNT`.

**Decisions:**
- The scale is logarithmic between `MinJoules` (1e-11 J, about one fission) and `MaxJoules` (4.184e6 J, one kilogram of TNT), clamped to 0..1. One fission and a lab-sized pile both fit on one picture that way. A linear scale would show one fission as nothing at all.
- One gram of TNT is 4184 J by the thermochemical definition, so the text agrees with the number printed beside it. `TntText` returns an empty string for a value that is zero, negative or infinite, and the caption then leaves the TNT clause out.
- The text climbs the units g, kg, t, kt, Mt and Gt. The flash stops growing at 1 kg, but the text keeps counting, so a large pile is described honestly.

## Samples~/NuclearLab/NuclearLabShape.cs
**What it does:** The profile of the three shapes of the stage as a function of the distance from the centre: `Alpha(kind, d)` for one distance and `Bake(kind, size)` for a square grid of bytes.

**Decisions:**
- Disc: solid with a soft edge of 0.12 of the radius. Glow: `(1 - d)^2`, a soft blob for the flash. Ring: a band from 0.78 to 1 of the radius, for the shock ring. Everything outside `d` = 1 is 0.
- The view bakes the bytes into one texture per shape. Keeping the function in the model means the tests can check the shapes (solid at the centre, empty outside) without a GPU.

## Samples~/NuclearLab/FissionFrame.cs
**What it does:** The picture of one moment, as data. A `FissionFrame` holds a list of `StageBlob` (shape, tint, colour mix, position, radius, alpha), a list of `StageLabel`, the strength of the screen wash, the caption and the phase. It also defines the enums `FissionPhase` (Ready, Approach, Absorb, Wobble, Split, Aftermath, Done), `StageKind` (Disc, Glow, Ring) and `StageTint` (Nucleus, Neutron, Light, Heavy, Flash, Shock).

**Decisions:**
- The frame is reused: `Clear` empties the lists and keeps their storage, so each repaint fills the same object again.
- Colours are named, not stored. The view maps each `StageTint` to a colour, so the model has no colour type. `Mix` blends from the nucleus colour towards the tint. The two lobes of a splitting nucleus raise it from 0 to 1, so the colour changes as the fragments take shape, and the neutrons, the flash and the fragments after the split use 1.
- Positions are in stage units (see `FissionShow`), so the same frame draws at any size of the panel.

## Samples~/NuclearLab/FissionShowPlan.cs
**What it does:** Everything one run of the animation needs: the target and the compound nucleus, whether a neutron arrives first, the chosen light and heavy fragment and their neutron count, the energy of that fission, the energy the flash stands for, the nuclear equation, the directions and reach of the neutrons, the direction of the split and one caption for each phase. `Build(info, rng, blastKev, events)` chooses a channel at random, weighted by the probability the library gives it, and fills the rest.

**Decisions:**
- The channel comes from `FissionInfo.AllChannels`, which is the library's own list for the nucleus and energy. The sample reimplements no physics. The tests draw 60000 plans and compare the share of each of the three likeliest channels with its probability.
- `BlastKev` of 0 or less means the energy of the one fission. A neutron irradiation passes the energy of all its fissions, so the flash is sized to the pile, and the caption says how many fissions the picture stands for.
- A channel without a known energy (`QKnown` is false) takes the mean energy over all channels, and `EventEnergyIsMean` is set so the caption says "about" and not a precise number.
- At most `MaxNeutronsShown` (12) neutrons are drawn. The directions are stratified (equal sectors with a random start and a small jitter) so they never clump, and any direction within 0.4 rad of the line of the fragments is nudged away, because a neutron drawn on top of a fragment hides it.
- The split axis is within 0.45 rad of horizontal, and the light fragment goes left or right with equal chance. The neutron comes in from the left within 0.3 rad of horizontal.
- `Build` throws `ArgumentNullException` for a missing argument, `ArgumentOutOfRangeException` for zero events and `InvalidOperationException` when no channel has a probability. The Fission tab turns the last one into its red line.

## Samples~/NuclearLab/FissionShow.cs
**What it does:** The clock and the picture. `Prepare`, `Start`, `Replay`, `Clear`, `Tick` and `Seek` move the clock, and `Evaluate(aspect, frame)` fills a `FissionFrame` for the current time. `Phase`, `Caption`, `Time`, `Duration` and `ScissionTime` report where the clock is.

**Decisions:**
- Timeline in seconds: Approach 1.0 (only when a neutron arrives), Absorb 0.3 (or Rest 0.5 for spontaneous fission), Wobble 1.8, then the split, Split 0.5 and FlyOut 2.5 counted from the split. A show with a neutron lasts 5.6 s.
- The show is slowed down enormously and says so in the tab. A real fission and its prompt neutrons take less than 1e-14 s.
- Stage units: the stage is 2 high, y from -1 at the top, x from -aspect to aspect. The radius of a nucleus is 0.056 times the cube root of its mass number, the usual R = r0 A^(1/3) scaling, so U-236 looks about 3.9 times as wide as He-4 and not 59 times.
- The wobble swings the nucleus into a dumbbell. A waist forms between two lobes and narrows until it breaks. At the split the two fragment discs are scaled so that their volumes add up to the volume of the compound nucleus, and they shrink to their own sizes over 0.8 s.
- Momentum: the light fragment travels `(1 - lightShare)` of the separation and the heavy one `lightShare`, so A_light times its distance equals A_heavy times its distance, and the lighter fragment moves faster. The tests check that ratio at several times. The edge of each fragment disc stays inside the stage at aspect ratios 1, 2.8 and 5 (square to very wide).
- The flash starts at the split. Its size is `0.8 + 0.9 * scale` of the stage and its strength follows the energy through `NuclearLabBlast`. It has a glow, two shock rings and a short wash of the whole stage, `(0.04 + 0.58 * scale) * (1 - t / 0.25)^2`. The tests check that the flash grows with the energy and fades to nothing.
- `Tick` ignores a negative, NaN or infinite step and stops at the end. `Seek` clamps, and a NaN or negative time gives 0. `IsReady` is true when a plan is loaded and the clock has not started, and the tab uses it so the first press plays the plan it prepared instead of drawing another.
- No UnityEngine reference, so the harness runs it.

## Samples~/NuclearLab/NuclearLabStage.cs
**What it does:** Paints a `FissionFrame` with IMGUI. It bakes the three shapes into textures once, reserves a rectangle on the page, draws a backdrop, the blobs, the wash and the labels, and frees the textures on `Dispose`.

**Decisions:**
- The textures are white with the shape in the alpha channel, and `GUI.color` tints them, so one texture serves every colour. The shapes are made in code and the sample needs no asset.
- It reserves its rectangle with `GUILayoutUtility.GetRect` on every event and draws only on Repaint, as `NuclearLabDraw` does, so the layout is the same in both passes. The drawing sits in a `GUI.BeginGroup` of the rectangle, which clips anything that leaves it.
- The aspect of the rectangle is passed to `FissionShow.Evaluate`, so the stage fills the width on any screen.
- Labels (the nuclide of each disc) sit above the disc, or below it when the disc is near the top edge.
- With no plan it draws a hint.

## Samples~/NuclearLab/NuclearLabStyles.cs
**What it does:** The GUIStyles of the panel (title, heading, body, rich-text body, hint, error, table cell, table header, stage label) and its three colours.

**Decisions:** Styles copy `GUI.skin.label`, so they can exist only inside `OnGUI`, and the view builds them there, once. Each style sets its own text colour, so the look does not depend on the colour of the skin's label.

## Samples~/NuclearLab/NuclearLabDraw.cs
**What it does:** A bar plot made of rectangles: one or several series, linear or logarithmic, with an optional marker bar. The plots of the Fission tab (product yield by mass number) and the Fusion tab (reactivity against temperature) use it.

**Decisions:** It draws with `GUI.DrawTexture` and the built-in `Texture2D.whiteTexture`, so the sample needs no texture asset. It draws only during the Repaint event, but it reserves its height with `GUILayoutUtility.GetRect` on every event, so the layout is the same in every pass. Bars shorter than half a point are skipped.

## Samples~/NuclearLab/NuclearLabTab.cs
**What it does:** The base class of the four tabs. It holds the queue and the tab's current error line. It provides `Do` (queue an action and show any failure as a red line), `Attempt` (run an action now with the same error handling, used by a tab's start-up), `ShowError`, `Cells` (a table row with fixed widths), `Field` (a label with a text box), `PresetButtons` (rows of buttons that return the index pressed) and `Tick`, an empty virtual method that the view calls once per frame with the unscaled frame time, for a tab that animates.

**Decisions:** The failures the base class catches are `NuclearException`, `FormatException`, `ArgumentException` and `InvalidOperationException`: the ones the package and the model files throw on purpose. Anything else is a defect and is left to reach the Unity console.

## Samples~/NuclearLab/NuclideTab.cs
**What it does:** Draws the Nuclide tab. A text box and an Inspect button, preset buttons (n, H-3, C-14, K-40, Co-60, Sr-90, I-131, Cs-137, Ra-226, U-235, U-238, Fe-56, He-4), the facts, the decay table and the chain to a stable nuclide.

**Decisions:** The chain shows at most 30 lines and says how many more it left out. A star on a decay share marks a share the tables do not measure.

## Samples~/NuclearLab/DecayTab.cs
**What it does:** Draws the Decay tab. Fields for the nuclide, the atom count and the seed, a switch between exact draws and expected amounts, Start pile, a step length with its unit, Advance, "Advance 1 half-life", Resolve fissions (shown only while something is pending), the summary lines, the 14 largest rows of the pile and the pending table.

**Decisions:** The summary always shows the nucleon check. The half-life button uses the half-life of the start nuclide, and says so when that nuclide has none. The defaults are 10^6 atoms of Sr-90 and a step of one year.

## Samples~/NuclearLab/FissionTab.cs
**What it does:** Draws the Fission tab. A lookup (nucleus, neutron energy in eV, presets for U-235 thermal, U-238 at 2 MeV and Cf-252 spontaneous), the summary lines, the Watch it split section (the stage, a caption, the Split one atom and Replay buttons), the Irradiate section with atoms, neutrons, seed and an optional fission share, the product histogram by mass number with a log scale switch, the eight likeliest channels and the ten likeliest products.

**Decisions:** The histogram starts on a log scale, so that the valley between the two humps and the thin tails stay visible, and a switch changes it to linear. A blank fission share means the authored table, and the tab says when the table has no entry for the nucleus. The result shows the nucleon check and the twelve largest products.
- A lookup loads the first outcome of the seed into the stage and leaves it waiting, so the stage is never empty after a lookup. Split one atom plays that outcome first, and every press after it draws the next outcome from the same seed (`Mix(seed, n)`), so a session can be replayed. Replay plays the same outcome again.
- A nucleus the library has no channels for clears the stage and shows the hint.
- An irradiation that produces fissions plays one of them, chosen by the same weighted draw, and sizes the flash to the energy of all of them. The caption says how many fissions the flash stands for and, when the TNT text applies, the TNT equivalent. A run with no fissions clears the stage.
- The Tick override moves the clock with the real frame time. The stage and the caption height are fixed (230 and 52 points), so the buttons below do not move while the caption changes.

## Samples~/NuclearLab/FusionTab.cs
**What it does:** Draws the Fusion tab. The table of channels, a pair lookup with presets (D + T, D + D, D + He-3, p + p, He-3 + He-3), a logarithmic temperature slider from 0.1 to 1000 keV with the temperature in kelvin beside it, the pair table, a log-log reactivity plot of 41 points with a marker at the chosen temperature, and the Fuse section with atoms of each nucleus, events and seed.

**Decisions:** Moving the slider re-reads the pair but does not rebuild the curves, because the curves do not depend on the temperature. A pair the library has no channel for shows a message and no plot. The identical-pair case ignores the second count, as the label under the Fuse heading says.

## Samples~/NuclearLab/NuclearLabView.cs
**What it does:** The only public MonoBehaviour. `OnEnable` initializes the library and builds the four tabs, `OnDisable` frees them, `Update` runs the queue and then ticks every tab with `Time.unscaledDeltaTime`, and `OnGUI` draws the title, a one-line header (library version and number of nuclides), the tab switch and the active tab inside a scroll view.

**Decisions:**
- The panel scales with `GUI.matrix`. `_uiScale` of 0 picks a scale from the screen height (height over 720, clamped to 1..3), which makes the controls usable on a phone. A positive value fixes the scale.
- A tab switch goes through the queue like every other change.
- When the library cannot start (usually a missing native binary for the platform), `OnEnable` catches the exception, shows the message and the cause in the panel and logs it, so the Game view explains itself instead of staying blank.
- Each tab keeps its own scroll position.
- The animation clock uses unscaled time, so a game that sets `Time.timeScale` to 0 does not freeze the panel.

## Tests~/DotnetHarness/NuclearLabModelTests.cs
**What it does:** Fourteen tests of the model files, run by the harness against the native library: formatting of numbers, durations, energies and counts; text parsing and the field names in error messages; the nuclide report against the tables; nucleon conservation of the decay lab in both pile kinds; the same seed giving the same pile and a failed start keeping the old one; resolving pending fissions; fission queries sorted and consistent (yields sum to 2); fission and fusion runs conserving nucleons; the fusion curves; the queue's ordering and deferral; and refusal of disposed labs.

**Decisions:** The file uses the classic NUnit assertions the stand-in supports and no Unity types. `NuclearHarness.csproj` compiles it together with the model files and leaves the view files out.

## Tests~/DotnetHarness/FissionShowTests.cs
**What it does:** Twelve tests of the animation model: the blast scale and the TNT text follow the energy; the shapes are solid at the centre and empty outside; the random stream is reproducible; a plan draws channels in proportion to their probability; a plan is complete and reproducible; the clock walks through the phases in order; every frame is finite and keeps the fragments and neutrons on the stage at aspect ratios 1, 2.8 and 5; the fragments move in inverse proportion to their mass; the nucleus stretches into a dumbbell before it splits; the flash grows with the energy and fades out; the neutrons arrive and then leave in their own directions; and the energies in the captions are the library's numbers.

**Decisions:** The tests run against the real native library, so a plan is built from real channels of U-235, Cf-252 and others. They assert measurable properties (conservation of the nucleon count over light, heavy and neutrons, equal momentum, volume of the fragments equal to the compound at the split) and not a picture, so they do not break when a colour or a duration is tuned. They were tried against 54 deliberate breaks of the model files. 51 failed a test at once, two more after assertions were added (the edge of the disc shape and the volume of the fragments), and one cannot be seen without Unity (see the problems list).

## Native plugins and CI
- `Runtime/Plugins/Native/<platform>` holds one `NOTE.md` per platform so the folders exist. `.github/workflows/build-nuclear-rust-libs.yml` builds six platforms (macOS universal, Windows, Linux, Android arm64-v8a, armeabi-v7a and x86_64, iOS device and simulator, WebGL) and commits the binaries with their `.meta` files.
- The `ffi` job of `.github/workflows/nuclear-core.yml` builds the Linux release library, compiles `Runtime/Core` and the editor tests with .NET 8 at language version 9 (what Unity 2022.3 compiles), and runs them. It also checks that every `DllImport` resolves to a symbol and that their number equals the number of `nuc_` exports, so a function added in Rust and forgotten here fails the build.
- `Tests~/DotnetHarness` ends in a tilde, so Unity ignores it.

## Fixes and Problems

### Runtime/Core/NuclearNative.cs
- The native binaries were not built when this package was written. Until the build workflow has run and committed them, every call throws `DllNotFoundException`. The workflow is a like-for-like port of the chemistry one, and its first real run is its verification.
- An iOS or WebGL player that links both `libnuclear_core.a` and `libchemistry_core.a` can fail with duplicate Rust runtime symbols. Android and desktop use separate dynamic libraries and are not affected. If both packages must ship on those platforms, merge the two crates into one static library.

### Runtime/Core/NuclearStructs.cs
- Struct sizes and all field offsets are checked by the native layout probe on the build host (x86_64 Linux). Other platforms are covered by the explicit reserved fields, which make every offset independent of the target. They were not run on 32-bit ARM.

### Runtime/Core/NuclearContext.cs
- Handles have finalizers, so a forgotten `Dispose` frees the native object at some later collection. Dispose them to free memory when you want it freed.

### Tests~/DotnetHarness
- The root `.gitignore` ignores `*.csproj`, so `NuclearHarness.csproj` is not tracked by git commands that respect it. The file was added through the GitHub web UI, which does not read `.gitignore`. A local `git add` of an edited copy needs `-f`, or a negation line for this folder in `.gitignore`.
- The project builds with `dotnet build --source <empty folder>` where NuGet is unreachable. It needs no packages.

### Tests/Editor/NuclearNativeTests.cs
- The tests were not run inside the Unity Test Runner. They were run under .NET 8 against the native library, with a small stand-in for NUnit. A signature difference between that stand-in and the NUnit in Unity would show up as a compile error in the test assembly only. It cannot break the runtime assembly.

### Samples~/NuclearLab
- The sample was not run inside the Unity Editor. The model files were run under .NET 8 against the Linux build of the native library, 26 sample tests (14 of the lab and 12 of the animation) and the 13 package tests, all 39 passing. The view files were compiled at language version 9 with warnings as errors against hand-written stand-ins for the UnityEngine types and drawn by a software rasterizer behind those stand-ins, then driven through simulated clicks and 5.6 s of animation: 6771 checks on tab switching, the error paths (a bad nuclide, a negative step, zero atoms, a nucleus that cannot fission), the slider, the extrapolation note, the nucleon lines, the stage rectangle and a Split, Replay and Irradiate run, with every layout group closed. Frames of the animation were rendered to images and looked at. The stand-ins say nothing about how real IMGUI lays out, scrolls or blends textures. The first run in the Editor is its real test, and a defect found there belongs in this list.
- The import through Package Manager has not been tried. The `samples` entry in `package.json` follows the shape of the Alembic one.
- IMGUI touch input has been reported not to work when "Active Input Handling" is "Input System Package (New)" only (Unity issue tracker, 2019, marked "By Design"). Mouse input works. The sample's README says to try "Input Manager (Old)" in a phone build. This was not reproduced here.
- The panel needs the native library of the Editor platform. The macOS and Windows libraries have their Editor switch on. The Linux library has it off, as the tests section already notes. Whether the committed macOS library loads on an older macOS depends on its deployment target and was not checked.
- The fusion plot extrapolates outside the published range of a fit. The pair table says so in words, and the curve itself does not change colour there.

### Samples~/NuclearLab/FissionShow.cs
- The animation is a picture of one event and is not to scale in time. It says nothing about the angles or speeds of real fragments beyond equal momentum, and the neutron directions are random. The caption and the hint under the stage say so.
- The biggest flash the lab can reach is about 0.97 of the scale. The pile holds its nucleon total in 64 bits, which limits an irradiation to about 7.8e16 U-235 atoms. That is a few hundred grams of TNT, so the largest flash the tab can show is about 0.97 of the scale. The scale tops out at 1 kg of TNT and the text still counts above it.

### Samples~/NuclearLab/NuclearLabStage.cs
- The fixed height of the caption (`GUILayout.Height`) is not visible to the stand-ins, so a change of that call is not caught by the view check. It is the one deliberate break out of 54 that nothing observes, and it is checked in the Editor.
- The textures are `Texture2D` objects made at run time and destroyed in `Dispose`. A tab that is never disposed (a component that is removed in a way that skips `OnDisable`) leaves three small textures until the scene unloads.
- A flash drawn with `GUI.color` alpha depends on the blending of the real IMGUI material, which the stand-ins do not model. If it looks too faint or too hard in the Editor, the numbers to tune are in `FissionShow.DrawBlast`.

### Samples~/NuclearLab/NuclearLabQueue.cs
- Any change that alters how many controls a tab draws must go through `Do` or the queue. A tab that sets such a state directly inside `Draw` brings the IMGUI layout error back. Edits to a text field are not affected, because they do not change the control count.

### Samples~/NuclearLab/DecayLab.cs
- The decay lab assumes one start nuclide per pile. A pile mixing nuclides at the start is not offered.

### Tests~/DotnetHarness/NuclearLabModelTests.cs
- The sample's model files are listed one by one in `NuclearHarness.csproj`, and so is `FissionShowTests.cs`. A new model file in the sample must be added there by hand, and a view file must not be, because it needs UnityEngine. A missing entry shows as a compile error in the harness.
- The half-life of Sr-90 is asserted as 28.91 years (NUBASE2020). For the expected-amounts pile, the Zr-90 test counts Zr-90 and Y-90 together, because Y-90 has a half-life of 64 hours, so after one Sr-90 half-life about 126 of the 500,000 atoms that left Sr-90 are still Y-90.
