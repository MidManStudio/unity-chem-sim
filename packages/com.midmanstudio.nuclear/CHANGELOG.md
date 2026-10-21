# Changelog

## Unreleased

- `Samples~/NuclearLab`: a Play Mode panel with four tabs (nuclide, decay,
  fission, fusion), drawn with plain IMGUI and registered as a sample in
  `package.json`
- `Samples~/NuclearLab`, Fission tab: an animation of one split. A neutron hits
  the nucleus, it wobbles and breaks in two, the fragments and the prompt
  neutrons fly out, and a flash and a shock ring are sized to the energy released.
  The channel is drawn from the library's own channel list, weighted by
  probability. An irradiation run plays one of its fissions with a flash sized to
  the whole run. Event level, not to scale in time, no chain reaction
- `Tests~/DotnetHarness`: 26 tests for the model half of the sample (14 for the
  tabs, 12 for the animation), run against the native library like the existing
  ones. `NuclearHarness.csproj` now lists the animation's model files

## 0.1.0

First release. C# bindings for nuclear_core 0.0.1 (C interface version 1):

- nuclide, mass, binding energy and reaction energy queries
- decay half-lives, outcomes, decay energies and chains
- `NuclearContext`, `NuclearSample` (exact draws) and `NuclearAmounts` (exact
  expectation), with time steps, fission resolution, neutron irradiation and fusion
- fission outcome, yield and fusion channel queries
- layout and interface version checks at first use
