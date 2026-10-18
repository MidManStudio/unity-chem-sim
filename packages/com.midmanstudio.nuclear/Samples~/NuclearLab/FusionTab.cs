// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FusionTab.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Lists the fusion channels, shows how the reactivity of a pair of nuclei
    /// climbs with temperature, and fuses a pile of the pair at the chosen temperature.
    /// </summary>
    internal sealed class FusionTab : NuclearLabTab
    {
        private const float LowKev = 0.1f;
        private const float HighKev = 1000f;
        private const int CurvePoints = 41;
        private const int ProductLimit = 8;
        private static readonly float[] ChannelWidths = { 200f, 110f, 90f, 200f };
        private static readonly float[] ProductWidths = { 130f, 160f };
        private static readonly string[] PairLabels = { "D + T", "D + D", "D + He-3", "p + p", "He-3 + He-3" };
        private static readonly string[] PairA = { "H-2", "H-2", "H-2", "H-1", "He-3" };
        private static readonly string[] PairB = { "H-3", "H-2", "He-3", "H-1", "He-3" };
        private static readonly Color[] SeriesColors =
        {
            new Color(0.40f, 0.85f, 1.00f, 0.9f),
            new Color(1.00f, 0.65f, 0.25f, 0.9f),
        };

        private readonly double[] temperatures = FusionLab.Temperatures(LowKev, HighKev, CurvePoints);
        private string aText = "H-2";
        private string bText = "H-3";
        private float exponent = 1f;
        private string countAText = "1000000";
        private string countBText = "1000000";
        private string eventsText = "300000";
        private string seedText = "1";

        private string[][] allRows = new string[0][];
        private string[][] pairRows = new string[0][];
        private string pairNote = string.Empty;
        private string[] curveNames = new string[0];
        private double[][] curves = new double[0][];
        private double curveMax;
        private double curveFloor;
        private int marker = -1;
        private string[] runLines = new string[0];
        private string[][] runRows = new string[0][];

        public FusionTab(NuclearLabQueue queue) : base(queue)
        {
            Attempt(delegate
            {
                allRows = MakeAllRows();
                RebuildPair();
            });
        }

        public override string Title
        {
            get { return "Fusion"; }
        }

        private double Temperature
        {
            get { return Math.Pow(10.0, exponent); }
        }

        public override void Draw(NuclearLabStyles styles)
        {
            GUILayout.Label("Fusion channels in the library", styles.Heading);
            Cells(styles.HeaderCell, ChannelWidths, "Reaction", "Energy", "Fit", "Published range");
            foreach (string[] row in allRows)
            {
                Cells(styles.Cell, ChannelWidths, row);
            }

            GUILayout.Space(8f);
            GUILayout.Label("A pair of nuclei", styles.Heading);
            GUILayout.BeginHorizontal();
            aText = Field("First", aText, 40f, 80f);
            bText = Field("Second", bText, 55f, 80f);
            if (GUILayout.Button("Look up", GUILayout.Width(100f)))
            {
                Do(RebuildPair);
            }
            GUILayout.EndHorizontal();
            int preset = PresetButtons(PairLabels, 5, 100f);
            if (preset >= 0)
            {
                int chosen = preset;
                Do(delegate
                {
                    aText = PairA[chosen];
                    bText = PairB[chosen];
                    RebuildPair();
                });
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Temperature " + NuclearLabFormat.Number(Temperature) + " keV ("
                + NuclearLabFormat.Kelvin(Temperature) + ")", GUILayout.Width(320f));
            float picked = GUILayout.HorizontalSlider(exponent, -1f, 3f, GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            if (picked != exponent)
            {
                float value = picked;
                Do(delegate
                {
                    exponent = value;
                    RefreshPair();
                });
            }
            ShowError(styles);

            if (pairNote.Length > 0)
            {
                GUILayout.Label(pairNote, styles.Body);
            }
            if (pairRows.Length > 0)
            {
                Cells(styles.HeaderCell, ChannelWidths, "Reaction", "Share of events", "Reactivity cm^3/s", "Note");
                foreach (string[] row in pairRows)
                {
                    Cells(styles.Cell, ChannelWidths, row);
                }
                GUILayout.Space(4f);
                GUILayout.Label("Reactivity against temperature, " + LowKev + " to " + HighKev
                    + " keV, both axes logarithmic. The yellow line marks the temperature above.", styles.Hint);
                NuclearLabDraw.Bars(120f, curves, SeriesColors, curveFloor, curveMax, true, marker);
                GUILayout.BeginHorizontal();
                GUILayout.Label(LowKev + " keV", styles.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.Label(HighKev + " keV", styles.Hint);
                GUILayout.EndHorizontal();
                for (int i = 0; i < curveNames.Length; i++)
                {
                    string hex = i == 0 ? "66d9ff" : "ffa640";
                    GUILayout.Label("<color=#" + hex + ">" + curveNames[i] + "</color>", styles.RichBody);
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("Fuse a pile of the pair at this temperature", styles.Heading);
            GUILayout.Label("Each event uses up one atom of each nucleus, or two atoms when both are the same.", styles.Hint);
            GUILayout.BeginHorizontal();
            countAText = Field("Atoms of first", countAText, 95f, 90f);
            countBText = Field("Atoms of second", countBText, 110f, 90f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            eventsText = Field("Events", eventsText, 50f, 90f);
            seedText = Field("Seed", seedText, 35f, 50f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Fuse", GUILayout.Width(120f)))
            {
                Do(Fuse);
            }
            foreach (string line in runLines)
            {
                GUILayout.Label(line, styles.Body);
            }
            if (runRows.Length > 0)
            {
                Cells(styles.HeaderCell, ProductWidths, "Nuclide", "Atoms");
                foreach (string[] row in runRows)
                {
                    Cells(styles.Cell, ProductWidths, row);
                }
            }
        }

        private static string[][] MakeAllRows()
        {
            FusionRow[] rows = FusionLab.AllChannels();
            string[][] table = new string[rows.Length][];
            for (int i = 0; i < rows.Length; i++)
            {
                table[i] = new[]
                {
                    rows[i].Name,
                    NuclearLabFormat.Energy(rows[i].QKev),
                    NuclearLabFormat.FitText(rows[i].Fit),
                    rows[i].Fit == FitKind.BoschHale
                        ? NuclearLabFormat.Number(rows[i].FitLowKev) + " to " + NuclearLabFormat.Number(rows[i].FitHighKev) + " keV"
                        : "none",
                };
            }
            return table;
        }

        private void RebuildPair()
        {
            Nuclide a = NuclearLabInput.ParseNuclide(aText, "First nucleus");
            Nuclide b = NuclearLabInput.ParseNuclide(bText, "Second nucleus");
            FusionPair pair = FusionLab.QueryPair(a, b, Temperature);

            List<double[]> series = new List<double[]>();
            List<string> names = new List<string>();
            double max = 0.0;
            double floor = double.MaxValue;
            foreach (PairChannel channel in pair.Channels)
            {
                double[] curve = FusionLab.ReactivityCurve(channel.Row.Index, temperatures);
                series.Add(curve);
                names.Add(channel.Row.Name);
                foreach (double v in curve)
                {
                    if (v > max)
                    {
                        max = v;
                    }
                    if (v > 0.0 && v < floor)
                    {
                        floor = v;
                    }
                }
            }

            curves = series.ToArray();
            curveNames = names.ToArray();
            curveMax = max;
            curveFloor = floor == double.MaxValue ? max / 1.0e6 : Math.Max(floor, max * 1.0e-24);
            runLines = new string[0];
            runRows = new string[0][];
            ApplyPair(pair);
        }

        private void RefreshPair()
        {
            Nuclide a = NuclearLabInput.ParseNuclide(aText, "First nucleus");
            Nuclide b = NuclearLabInput.ParseNuclide(bText, "Second nucleus");
            ApplyPair(FusionLab.QueryPair(a, b, Temperature));
        }

        private void ApplyPair(FusionPair pair)
        {
            string[][] rows = new string[pair.Channels.Length][];
            for (int i = 0; i < rows.Length; i++)
            {
                PairChannel c = pair.Channels[i];
                rows[i] = new[]
                {
                    c.Row.Name,
                    NuclearLabFormat.Percent(c.Share * 100.0),
                    NuclearLabFormat.Number(c.Reactivity),
                    c.InsideFit ? string.Empty : "outside the published fit, extrapolated",
                };
            }
            pairRows = rows;
            pairNote = rows.Length == 0
                ? "The library has no fusion channel for " + pair.A + " + " + pair.B + "."
                : string.Empty;
            double position = (Math.Log10(pair.TemperatureKev) - Math.Log10(LowKev)) / (Math.Log10(HighKev) - Math.Log10(LowKev));
            marker = (int)Math.Round(position * (CurvePoints - 1));
        }

        private void Fuse()
        {
            Nuclide a = NuclearLabInput.ParseNuclide(aText, "First nucleus");
            Nuclide b = NuclearLabInput.ParseNuclide(bText, "Second nucleus");
            ulong countA = NuclearLabInput.ParseCount(countAText, "Atoms of first", 1UL);
            ulong countB = a == b ? 0UL : NuclearLabInput.ParseCount(countBText, "Atoms of second", 1UL);
            ulong events = NuclearLabInput.ParseCount(eventsText, "Events", 1UL);
            ulong seed = NuclearLabInput.ParseCount(seedText, "Seed", 0UL);
            double temperature = Temperature;

            FusionRun run = FusionLab.Run(a, countA, b, countB, events, temperature, seed);
            ReactionReport r = run.Report;
            List<string> lines = new List<string>();
            lines.Add(NuclearLabFormat.Count(r.Fusions) + " fusions at " + NuclearLabFormat.Number(temperature)
                + " keV, " + NuclearLabFormat.Count(r.NeutronsReleased) + " neutrons released");
            lines.Add("Energy released: " + NuclearLabFormat.Energy(r.EnergyReleasedKev)
                + " = " + NuclearLabFormat.Joules(r.EnergyReleasedKev)
                + (r.EnergyKnown ? string.Empty : " (a lower estimate: some masses are missing)"));
            double after = (double)run.Totals.Baryons;
            lines.Add("Nucleons: " + NuclearLabFormat.Amount(after) + " of " + NuclearLabFormat.Amount(run.StartBaryons)
                + (after == run.StartBaryons ? " (conserved)" : " (NOT conserved)"));

            int shown = Math.Min(ProductLimit, run.Products.Length);
            string[][] table = new string[shown][];
            for (int i = 0; i < shown; i++)
            {
                table[i] = new[] { run.Products[i].Nuclide.ToString(), NuclearLabFormat.Count(run.Products[i].Count) };
            }
            runLines = lines.ToArray();
            runRows = table;
        }
    }
}
