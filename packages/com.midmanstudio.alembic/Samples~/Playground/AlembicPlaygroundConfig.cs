// AlembicPlaygroundConfig.cs
//
// Data-driven scenario for AlembicPlaygroundController — everything about
// "what gets spawned and how the sim starts" lives here as a ScriptableObject
// asset, not hardcoded in the controller, so testing a different scenario
// (a tight water-ish cloud vs. a sparse noble-gas spread vs. a custom
// fictional reagent) is an asset swap + Inspector edit, never a recompile.
//
// This is the concrete "ScriptableObject field... unpacks it into
// RegisterElement" use case ChemistryLib.CustomElementDefinition's own doc
// comment already calls out by name — this asset is that field.
//
// Deliberately lives in Samples~, not Runtime/ — this is a test-harness
// concern, not something the package itself should carry into every
// consuming game.

using System;
using System.Collections.Generic;
using UnityEngine;
using MidManStudio.Alembic.Core;

namespace MidManStudio.Alembic.Samples.Playground
{
    [CreateAssetMenu(
        fileName = "AlembicPlaygroundConfig",
        menuName = "MidManStudio/Alembic/Playground Config",
        order = 0)]
    public sealed class AlembicPlaygroundConfig : ScriptableObject
    {
        [Header("Simulation")]
        [Tooltip("Maxwell-Boltzmann velocity-init temperature, Kelvin. Fed to chem_init.")]
        public float TemperatureK = 300f;

        [Tooltip("Seed for chem_init's velocity randomization. Widens to ulong at the call site.")]
        public uint Seed = 42u;

        [Tooltip("Neighbor-search cutoff, Angstroms, for both spatial hashing and bonding range. " +
                 "0 falls back to chemistry_core's own 10A default (see chem_step's own doc).")]
        public float CutoffAngstrom = 10f;

        [Tooltip("chem_step's dt is FEMTOSECONDS, not seconds and NOT Time.deltaTime — wiring " +
                 "Time.deltaTime straight in runs the sim at ~0.016 fs/frame (stable, but " +
                 "glacially slow to watch). See chemistry_core/docs/architecture.md's " +
                 "'Energy units' section before changing this casually.")]
        public float DtFemtoseconds = 1f;

        [Tooltip("Physics steps run per Unity frame. Raise this for faster-looking dynamics " +
                 "without inflating dt itself — a large dt alone risks destabilizing velocity " +
                 "Verlet, more steps at a modest dt is the safer knob to turn first.")]
        [Range(1, 32)]
        public int StepsPerFrame = 4;

        [Tooltip("Start stepping immediately in Play Mode. Turn off to inspect the initial " +
                 "spawn layout frozen, then resume manually (Space, by default) once you've " +
                 "looked at it.")]
        public bool RunOnStart = true;

        [Header("Explicit starting atoms (optional)")]
        [Tooltip("If non-empty, this exact layout is spawned instead of the random cloud below " +
                 "— use this for a specific test case (e.g. two hydrogens either side of an " +
                 "oxygen, close enough to bond on the first few steps).")]
        public List<AtomSpawnEntry> ExplicitAtoms = new List<AtomSpawnEntry>();

        [Header("Random cloud (used when ExplicitAtoms is empty)")]
        [Tooltip("Each spawned atom's element is picked uniformly at random from this pool — " +
                 "repeat an atomic number to bias the odds (e.g. {1,1,8} skews 2:1 toward " +
                 "hydrogen, roughly water-flavored without hand-placing anything).")]
        public int[] CloudElementPool = { 1, 1, 8 };

        [Tooltip("How many atoms the random cloud spawns.")]
        [Range(1, 4000)]
        public int CloudAtomCount = 40;

        [Tooltip("Cloud atoms spawn at a random point inside a sphere of this radius, centered " +
                 "on this GameObject's position. Keep it within CutoffAngstrom-ish range of " +
                 "itself or most pairs will start out of interaction range.")]
        public float CloudRadius = 6f;

        [Header("Custom elements (optional — registered before anything spawns)")]
        [Tooltip("Fictional/custom reagents (atomic number >= 1000 — see " +
                 "ChemistryLib.RegisterElement's own doc for the exact contract). Registered " +
                 "once on startup and again on Reset, since a fresh context doesn't remember " +
                 "them — chem_register_element itself is global/process-lifetime, but Reset " +
                 "here only recreates the context, so re-registering is just belt-and-braces, " +
                 "cheap and idempotent either way.")]
        public List<CustomElementDefinition> CustomElements = new List<CustomElementDefinition>();

        [Header("Quick-spawn palette (Playground HUD)")]
        [Tooltip("Number keys 1-9 spawn these atomic numbers at the camera's forward point, in " +
                 "order. Must already be real (1-118) or listed in CustomElements above — " +
                 "anything else is silently skipped with a console warning at spawn time " +
                 "(chemistry_core would otherwise hand back a zero-mass/zero-radius ghost atom " +
                 "for an unregistered number rather than reject it outright — this config-side " +
                 "check exists so the playground never demonstrates that footgun by accident).")]
        public int[] QuickSpawnElements = { 1, 6, 7, 8, 26, 79 };

        [Tooltip("Distance in front of the camera that a quick-spawned atom appears, Angstroms " +
                 "— same position units as everything else in the sim.")]
        public float QuickSpawnDistance = 5f;

        [Serializable]
        public struct AtomSpawnEntry
        {
            public int AtomicNumber;
            public Vector3 Position;
        }
    }
}
