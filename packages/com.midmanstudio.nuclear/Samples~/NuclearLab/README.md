# Nuclear Lab (sample)

A drop-in Play Mode panel for `com.midmanstudio.nuclear`. It has four tabs, and
each one calls the package and shows what comes back:

| Tab | What it shows |
| --- | --- |
| Nuclide | Mass excess, binding energy, half-life, abundance, the decay modes with their energies, and the chain to a stable nuclide. |
| Decay | A pile of atoms stepped through time, as exact random draws or as exact expectations, with the nucleon count checked after every step. |
| Fission | The outcomes of a fissioning nucleus, an animation of one split (a neutron hits the nucleus, it wobbles and breaks in two, fragments and neutrons fly out, a flash is sized to the energy released), a mass-yield histogram with the two humps, and a neutron irradiation of a pile. |
| Fusion | The seven channels, reactivity against temperature for a pair of nuclei, and a pile fused at the chosen temperature. |

Every reaction stays at event level: the lab says how many events happen and the
library says what comes out. Nothing here models geometry, flux or a chain
reaction. It is a sandbox, not weapon design.

## Prerequisites

- Unity 2022.3 or later. The sample draws with plain IMGUI (`OnGUI`), so it needs
  no render pipeline, canvas, material or input handling.
- The native `nuclear_core` library for the platform the Editor runs on. The
  binaries under `Runtime/Plugins/Native` are built and committed by the
  `build-nuclear-rust-libs` workflow. The macOS and Windows libraries are enabled
  for the Editor. The Linux library has its Editor switch off, so a Linux Editor
  needs it switched on in the importer.
- Mouse input in the Editor works with any "Active Input Handling" setting. Touch
  on a device is another matter: a Unity issue report (2019, marked "By Design")
  says IMGUI touch is tied to the old input manager and does nothing when only the
  Input System package is active. If taps do nothing in a phone build, set
  "Active Input Handling" to "Input Manager (Old)" in Project Settings, Player.
  "Both" was reported to be unreliable in that thread, so it is the second try.

## Setup (about a minute)

1. Add the package with Package Manager, then open it, select "Samples" and press
   "Import" next to "Nuclear Lab". Unity copies the folder into
   `Assets/Samples/`. Copying `Samples~/NuclearLab` into `Assets/` by hand does
   the same.
2. In a scene, choose GameObject > Create Empty and add the `NuclearLabView`
   component.
3. Press Play.

No camera is needed for the panel. It draws over whatever the Game view shows.

## Controls

- Type a nuclide as `Symbol-Mass` (`U-235`, `Sr-90`, `H-3`) or `n` for the
  neutron. `D` and `T` are not accepted, use `H-2` and `H-3`.
- The preset buttons under a field fill it in and run the lookup.
- Piles and seeds: the same seed always gives the same pile.
- Decay, Exact draws: every step is a random draw. Expected amounts: every step is
  the exact average, so counts are fractions and the curves are smooth.
- Fission, Watch it split: Split one atom plays one fission. The channel is drawn at random from the library's own list, weighted by its probability, and every press draws the next outcome of the same seed. Replay plays the same one again. After an Irradiate run with fissions, the stage plays one of them and sizes the flash to the energy of all of them. The animation is slowed down enormously and is not to scale in time.
- Fission, Irradiate: leave the fission share blank to use the authored table
  (U-233, U-235, U-238 and Pu-238 to Pu-242 have one). Any other nucleus needs a
  number between 0 and 1.
- Fusion, temperature: the slider is logarithmic, 0.1 to 1000 keV. A Bosch-Hale
  fit is only valid inside the range it was published for, and the pair table
  marks every channel that is outside it as extrapolated.

## Layout of the folder

The model files hold all the logic and use no UnityEngine type. The view files
only draw. `Tests~/DotnetHarness/NuclearLabModelTests.cs` and
`FissionShowTests.cs` run the model files under plain .NET.

| Files | Role |
| --- | --- |
| `NuclearLabFormat.cs`, `NuclearLabInput.cs`, `NuclearLabQueue.cs`, `NuclearLabRandom.cs` | Text formatting, input parsing, the deferred action queue, the seeded random stream |
| `NuclideReport.cs`, `DecayLab.cs`, `FissionLab.cs`, `FusionLab.cs` | One class per tab, calling the package |
| `NuclearLabBlast.cs`, `NuclearLabShape.cs`, `FissionFrame.cs`, `FissionShowPlan.cs`, `FissionShow.cs` | The animation: flash size and TNT text, shape profiles, the frame as data, the plan of one run, the clock and the picture |
| `NuclearLabStyles.cs`, `NuclearLabDraw.cs`, `NuclearLabStage.cs`, `NuclearLabTab.cs` | IMGUI styles, the bar plot, the animation stage, the shared tab base |
| `NuclideTab.cs`, `DecayTab.cs`, `FissionTab.cs`, `FusionTab.cs` | The four tabs |
| `NuclearLabView.cs` | The MonoBehaviour that owns the tabs |

The design notes for each file are in `docs/com.midmanstudio.nuclear.md`.

## What was and was not checked

The model files were compiled with the .NET 8 harness (C# 9, warnings as errors)
and run against the committed Linux library: 26 sample tests (14 for the tabs, 12
for the animation) and the 13 package tests, all 39 passing, covering nucleon
conservation in every pile kind, momentum and volume in the animation, and the
share of each fission channel against its probability. The IMGUI files were
compiled against hand-written stand-ins for UnityEngine, drawn by a software
rasterizer and driven through simulated clicks and a full run of the animation:
6771 checks, with frames rendered and looked at. None of this ran inside the
Unity Editor. The import through Package Manager, the real IMGUI layout (the fixed
height of the caption in particular), texture blending, frame rate on a phone, the
real scroll and touch behaviour, and the macOS library on an old macOS are not
verified.
