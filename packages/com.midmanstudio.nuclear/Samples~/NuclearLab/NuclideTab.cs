// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclideTab.cs"
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Looks up one nuclide: mass, binding energy, half-life, abundance, how it
    /// decays and the chain it follows to a stable nuclide.
    /// </summary>
    internal sealed class NuclideTab : NuclearLabTab
    {
        private const int ChainLineLimit = 30;
        private static readonly float[] DecayWidths = { 95f, 300f, 130f };
        private static readonly string[] Presets =
        {
            "n", "H-3", "C-14", "K-40", "Co-60", "Sr-90", "I-131",
            "Cs-137", "Ra-226", "U-235", "U-238", "Fe-56", "He-4",
        };

        private string input = "Cs-137";
        private string[] facts = new string[0];
        private string[][] decayRows = new string[0][];
        private string decayNote = string.Empty;
        private string[] chainLines = new string[0];
        private string chainEnd = string.Empty;
        private int chainHidden;
        private bool hasReport;

        public NuclideTab(NuclearLabQueue queue) : base(queue)
        {
            Attempt(Inspect);
        }

        public override string Title
        {
            get { return "Nuclide"; }
        }

        public override void Draw(NuclearLabStyles styles)
        {
            GUILayout.Label("Look up a nuclide", styles.Heading);
            GUILayout.BeginHorizontal();
            input = GUILayout.TextField(input, GUILayout.Width(140f));
            if (GUILayout.Button("Inspect", GUILayout.Width(100f)))
            {
                Do(Inspect);
            }
            GUILayout.EndHorizontal();
            int preset = PresetButtons(Presets, 7, 70f);
            if (preset >= 0)
            {
                string chosen = Presets[preset];
                Do(delegate
                {
                    input = chosen;
                    Inspect();
                });
            }
            ShowError(styles);
            if (!hasReport)
            {
                GUILayout.Label("Type a name such as U-235, H-2 or n, then press Inspect.", styles.Hint);
                return;
            }

            GUILayout.Space(6f);
            foreach (string fact in facts)
            {
                GUILayout.Label(fact, styles.Body);
            }

            GUILayout.Space(6f);
            GUILayout.Label("How it decays", styles.Heading);
            if (decayRows.Length == 0)
            {
                GUILayout.Label(decayNote, styles.Body);
            }
            else
            {
                Cells(styles.HeaderCell, DecayWidths, "Share", "Mode", "Energy released");
                foreach (string[] row in decayRows)
                {
                    Cells(styles.Cell, DecayWidths, row);
                }
                GUILayout.Label("A star marks a share the tables do not measure.", styles.Hint);
            }

            GUILayout.Space(6f);
            GUILayout.Label("Chain to a stable nuclide (most likely decay at each step)", styles.Heading);
            foreach (string line in chainLines)
            {
                GUILayout.Label(line, styles.Body);
            }
            if (chainHidden > 0)
            {
                GUILayout.Label("... and " + chainHidden + " more steps", styles.Hint);
            }
            GUILayout.Label("Ends at " + chainEnd, styles.Body);
        }

        private void Inspect()
        {
            Nuclide nuclide = NuclearLabInput.ParseNuclide(input, "Nuclide");
            NuclideReport report = NuclideReport.Build(nuclide);

            List<string> lines = new List<string>();
            string symbol = nuclide.Symbol;
            lines.Add(nuclide + (symbol.Length > 0 ? "  (" + symbol + ")" : string.Empty)
                + "   protons " + nuclide.Z + ", neutrons " + nuclide.N + ", mass number " + nuclide.A);
            lines.Add("Mass excess: " + NuclearLabFormat.Energy(report.MassExcess.Kev)
                + " (" + NuclearLabFormat.SourceText(report.MassExcess.Source) + ")");
            lines.Add("Binding energy: " + NuclearLabFormat.Energy(report.Binding.TotalKev)
                + " in total, " + NuclearLabFormat.Energy(report.Binding.PerNucleonKev)
                + " per nucleon (" + NuclearLabFormat.SourceText(report.Binding.Source) + ")");
            lines.Add("Half-life: " + (report.HasHalfLife
                ? NuclearLabFormat.HalfLifeText(report.HalfLife)
                : "no decay record in the tables"));
            if (report.AbundancePercent > 0.0)
            {
                lines.Add("Natural abundance: " + NuclearLabFormat.Percent(report.AbundancePercent));
            }

            string[][] rows = new string[report.Decays.Length][];
            for (int i = 0; i < rows.Length; i++)
            {
                DecayLine line = report.Decays[i];
                rows[i] = new[]
                {
                    NuclearLabFormat.Percent(line.Outcome.Percent) + (line.Outcome.Assumed ? " *" : string.Empty),
                    NuclideReport.DescribeOutcome(line.Outcome),
                    line.HasQ ? NuclearLabFormat.Energy(line.Q.Kev) : "not in the tables",
                };
            }

            int shown = System.Math.Min(ChainLineLimit, report.ChainLines.Length);
            string[] chain = new string[shown];
            System.Array.Copy(report.ChainLines, chain, shown);

            facts = lines.ToArray();
            decayRows = rows;
            decayNote = report.HasHalfLife && report.HalfLife.State == HalfLifeState.Stable
                ? "Stable: it does not decay."
                : "The tables list no decay modes for this nuclide.";
            chainLines = chain;
            chainHidden = report.ChainLines.Length - shown;
            chainEnd = report.ChainEnd;
            hasReport = true;
        }
    }
}
