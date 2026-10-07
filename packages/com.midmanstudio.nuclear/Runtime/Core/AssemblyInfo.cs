// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/AssemblyInfo.cs"
// ============================================================================
using System.Runtime.CompilerServices;

// The editor tests check the native layout field by field and need the
// internal bindings for that.
[assembly: InternalsVisibleTo("MidManStudio.Nuclear.Tests.Editor")]
