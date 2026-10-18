# Changelog

## Unreleased

- `Samples~/NuclearLab`: a Play Mode panel with four tabs (nuclide, decay,
  fission, fusion), drawn with plain IMGUI and registered as a sample in
  `package.json`
- `Tests~/DotnetHarness`: 14 tests for the model half of the sample, run against
  the native library like the existing ones

## 0.1.0

First release. C# bindings for nuclear_core 0.0.1 (C interface version 1):

- nuclide, mass, binding energy and reaction energy queries
- decay half-lives, outcomes, decay energies and chains
- `NuclearContext`, `NuclearSample` (exact draws) and `NuclearAmounts` (exact
  expectation), with time steps, fission resolution, neutron irradiation and fusion
- fission outcome, yield and fusion channel queries
- layout and interface version checks at first use
