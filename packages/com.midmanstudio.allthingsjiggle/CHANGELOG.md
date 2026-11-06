# Changelog

## 0.1.0

First drop: the core, with no Unity components yet. C# bindings and a managed
reference kernel for jiggle_core 0.0.1 (C interface version 1):

- verlet bone chain solver with shape, length and angle limit constraints, drag,
  air drag, gravity, a root spring, sleep and wake, teleport and reset
- sphere, capsule, plane, box and inside sphere colliders, with an optional
  bone segment test
- directional, radial and vortex fields, as acceleration or hit impulse, with
  falloff and gusts
- a managed kernel and the native library that give identical bits on the same
  buffers, checked field by field and scene by scene
- `JiggleWorld`, a managed model of the call sequence with a fixed step accumulator
- layout and interface version checks through `JiggleLibrary.Verify`
