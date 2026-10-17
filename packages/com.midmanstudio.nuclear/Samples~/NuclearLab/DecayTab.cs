// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/DecayTab.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Starts a pile of one nuclide and advances it through time, as exact draws
    /// or as exact expectations, and lists what the pile has turned into.
    /// </summary>
    internal sealed class DecayTab : NuclearLabTab
    {
        private const int RowLimit = 14;
        private const int PendingLimit = 6;
        private static readonly float[] RowWidths = { 110f, 150f, 120f, 190f };
        private static readonly string[] KindLabels = { "Exact draws", "Expected amounts" };

        private readonly DecayLab lab = new DecayLab();
        private string nuclideText = "Sr-90";
        private string atomsText = "1000000";
        private string seedText = "1";
        private string stepText = "1";
        private int kind;
        private int stepUnit = 4;
        private string[] summary = new string[0];
        private string[][] rows = new string[0][];
        private string[][] pending = new string[0][];
        private int hiddenRows;
        private bool anyPending;

        public DecayTab(NuclearLabQueue queue) : base(queue)
        {
        }

        public override string Title
        {
            get { return "Decay"; }
        }

        public override void Draw(NuclearLabStyles styles)
        {
            GUILayout.Label("Decay a pile of atoms through time", styles.Heading);
            GUILayout.BeginHorizontal();
            nuclideText = Field("Nuclide", nuclideText, 55f, 90f);
            atomsText = Field("Atoms", atomsText, 45f, 110f);
            seedText = Field("Seed", seedText, 35f, 60f);
            GUILayout.EndHorizontal();
            kind = GUILayout.Toolbar(kind, KindLabels);
            GUILayout.Label(
                kind == 0
                    ? "Every step is a random draw: the same seed always gives the same pile."
                    : "Every step is the exact average, so counts are fractions and the curves are smooth.",
                styles.Hint);
            if (GUILayout.Button("Start pile", GUILayout.Width(120f)))
            {
                Do(Start);
            }
            ShowError(styles);
            if (summary.Length == 0)
            {
                GUILayout.Label("Pick a nuclide, an atom count and a seed, then press Start pile.", styles.Hint);
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Advance time", styles.Heading);
            GUILayout.BeginHorizontal();
            stepText = Field("Step", stepText, 35f, 70f);
            stepUnit = GUILayout.Toolbar(stepUnit, NuclearLabFormat.StepUnits, GUILayout.Width(200f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Advance", GUILayout.Width(110f)))
            {
                Do(Advance);
            }
            if (GUILayout.Button("Advance 1 half-life", GUILayout.Width(160f)))
            {
                Do(AdvanceHalfLife);
            }
            if (anyPending && GUILayout.Button("Resolve fissions", GUILayout.Width(140f)))
            {
                Do(Resolve);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            foreach (string line in summary)
            {
                GUILayout.Label(line, styles.Body);
            }

            GUILayout.Space(6f);
            GUILayout.Label("What is in the pile", styles.Heading);
            Cells(styles.HeaderCell, RowWidths, "Nuclide", "Amount", "Per start atom", "Half-life");
            foreach (string[] row in rows)
            {
                Cells(styles.Cell, RowWidths, row);
            }
            if (hiddenRows > 0)
            {
                GUILayout.Label("... and " + hiddenRows + " more nuclides", styles.Hint);
            }
            if (pending.Length > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Waiting for fission products", styles.Heading);
                foreach (string[] row in pending)
                {
                    Cells(styles.Cell, RowWidths, row);
                }
            }
        }

        public override void Dispose()
        {
            lab.Dispose();
        }

        private void Start()
        {
            Nuclide nuclide = NuclearLabInput.ParseNuclide(nuclideText, "Nuclide");
            ulong atoms = NuclearLabInput.ParseCount(atomsText, "Atoms", 1UL);
            ulong seed = NuclearLabInput.ParseCount(seedText, "Seed", 0UL);
            lab.Reset(nuclide, atoms, (PileKind)kind, seed);
            Refresh();
        }

        private void Advance()
        {
            lab.Step(NuclearLabInput.ParseStepSeconds(stepText, stepUnit));
            Refresh();
        }

        private void AdvanceHalfLife()
        {
            double seconds;
            if (!lab.TryGetStartHalfLife(out seconds))
            {
                throw new InvalidOperationException(lab.Start + " has no finite half-life to step by.");
            }
            lab.Step(seconds);
            Refresh();
        }

        private void Resolve()
        {
            lab.ResolveFissions();
            Refresh();
        }

        private void Refresh()
        {
            List<string> lines = new List<string>();
            double baryons = lab.Baryons;
            double startBaryons = lab.StartBaryons;
            bool conserved = Math.Abs(baryons - startBaryons) <= 1e-9 * startBaryons;
            lines.Add("Started with " + NuclearLabFormat.Count(lab.StartCount) + " x " + lab.Start
                + " (" + KindLabels[(int)lab.Kind].ToLowerInvariant() + ", seed " + lab.Seed + ")");
            lines.Add("Elapsed: " + NuclearLabFormat.Duration(lab.ElapsedSeconds));
            lines.Add("Atoms: " + NuclearLabFormat.Amount(lab.AtomTotal)
                + "   Nucleons: " + NuclearLabFormat.Amount(baryons) + " of " + NuclearLabFormat.Amount(startBaryons)
                + (conserved ? " (conserved)" : " (NOT conserved)"));
            lines.Add("Energy released: " + NuclearLabFormat.Energy(lab.EnergyReleasedKev)
                + " = " + NuclearLabFormat.Joules(lab.EnergyReleasedKev)
                + (lab.EnergyKnown ? string.Empty : " (a lower estimate: some masses are missing)"));
            lines.Add("Last step: " + NuclearLabFormat.Amount(lab.LastChanged) + " changed, "
                + NuclearLabFormat.Amount(lab.LastNewPending) + " moved to pending fission");
            if (lab.ResolvedFissions > 0.0)
            {
                lines.Add("Fissions resolved: " + NuclearLabFormat.Amount(lab.ResolvedFissions)
                    + ", neutrons released: " + NuclearLabFormat.Amount(lab.NeutronsFromFission));
            }

            PileRow[] all = lab.Rows();
            int shown = Math.Min(RowLimit, all.Length);
            string[][] table = new string[shown][];
            for (int i = 0; i < shown; i++)
            {
                table[i] = MakeRow(all[i]);
            }

            PileRow[] waiting = lab.Pending();
            int waitingShown = Math.Min(PendingLimit, waiting.Length);
            string[][] waitingTable = new string[waitingShown][];
            for (int i = 0; i < waitingShown; i++)
            {
                waitingTable[i] = MakeRow(waiting[i]);
            }

            summary = lines.ToArray();
            rows = table;
            hiddenRows = all.Length - shown;
            pending = waitingTable;
            anyPending = waiting.Length > 0;
        }

        private static string[] MakeRow(PileRow row)
        {
            HalfLife halfLife;
            string life = NuclearData.TryGetHalfLife(row.Nuclide, out halfLife)
                ? NuclearLabFormat.HalfLifeText(halfLife)
                : "-";
            return new[]
            {
                row.Nuclide.ToString(),
                NuclearLabFormat.Amount(row.Amount),
                NuclearLabFormat.Number(row.PerStartAtom),
                life,
            };
        }
    }
}
