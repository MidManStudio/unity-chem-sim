// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/AssemblyInfo.cs"
// ============================================================================
using System.Runtime.CompilerServices;

// The editor tests of the lattice run the control and deform jobs and the mesh data on hand made arrays, and those are internal.
[assembly: InternalsVisibleTo("MidManStudio.AllThingsJiggle.Tests.Editor")]
