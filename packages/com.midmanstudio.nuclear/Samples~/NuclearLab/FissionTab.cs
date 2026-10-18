// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FissionTab.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Shows the fission outcomes of a nucleus, the double-humped mass
    /// distribution of its products, and runs a small neutron irradiation of a
    /// pile. Event level only: no geometry, no flux, no chain reaction.
    /// </summary>
    internal sealed class FissionTab : NuclearLabTab
    {
        private const int ProductLimit = 12;
        private static readonly float[] ChannelWidths = { 110f, 270f, 90f, 120f };
        private static readonly float[] ProductWidths = { 130f, 160f };
        private static readonly string[] PresetLabels = { "U-235 thermal", "U-238 fast (2 MeV)", "Cf-252 spontaneous" };
        private static readonly string[] PresetTargets = { "U-235", "U-238", "Cf-252" };
        private static readonly string[] PresetEnergies = { "0.0253", "2e6", "0" };

        private readonly FissionLab lab = new FissionLab(8, 10);
        private string targetText = "U-235";
        private string energyText = "0.0253";
        private string atomsText = "1000000";
        private string neutronsText = "100000";
        private string probabilityText = string.Empty;
        private string seedText = "1";
        private bool logScale = true;

        private string[] infoLines = new string[0];
        private string[][] channelRows = new string[0][];
        private string[][] yieldRows = new string[0][];
        private double[] massYields = new double[0];
        private double massMax;
        private double massFloor;
        private int massMin;

        private string[] runLines = new string[0];
        private string[][] runRows = new string[0][];

        public FissionTab(NuclearLabQueue queue) : base(queue)
        {
            Attempt(Query);
        }

        public override string Title
        {
            get { return "Fission"; }
        }

        public override void Draw(NuclearLabStyles styles)
        {
            GUILayout.Label("Fission outcomes", styles.Heading);
            GUILayout.BeginHorizontal();
            targetText = Field("Nucleus", targetText, 55f, 90f);
            energyText = Field("Neutron energy (eV, 0 = spontaneous)", energyText, 235f, 80f);
            GUILayout.EndHorizontal();
            int preset = PresetButtons(PresetLabels, 3, 180f);
            if (preset >= 0)
            {
                int chosen = preset;
                Do(delegate
                {
                    targetText = PresetTargets[chosen];
                    energyText = PresetEnergies[chosen];
                    Query();
                });
            }
            if (GUILayout.Button("Look up", GUILayout.Width(120f)))
            {
                Do(Query);
            }
            ShowError(styles);
            if (infoLines.Length == 0)
            {
                GUILayout.Label("Type a heavy nucleus such as U-235 and press Look up.", styles.Hint);
                return;
            }

            GUILayout.Space(6f);
            foreach (string line in infoLines)
            {
                GUILayout.Label(line, styles.Body);
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Product yield by mass number", styles.Heading);
            logScale = GUILayout.Toggle(logScale, "log scale");
            GUILayout.EndHorizontal();
            NuclearLabDraw.Bars(110f, massYields, NuclearLabStyles.HeadingColor, massFloor, massMax, logScale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("mass " + massMin, styles.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.Label("mass " + (massMin + massYields.Length - 1), styles.Hint);
            GUILayout.EndHorizontal();
            GUILayout.Label("Yield per fission, summed over all products of one mass number. Two products come out of each fission, so the yields add up to 2.", styles.Hint);

            GUILayout.Space(6f);
            GUILayout.Label("Likeliest channels", styles.Heading);
            Cells(styles.HeaderCell, ChannelWidths, "Probability", "Fragments", "Neutrons", "Energy");
            foreach (string[] row in channelRows)
            {
                Cells(styles.Cell, ChannelWidths, row);
            }

            GUILayout.Space(6f);
            GUILayout.Label("Most abundant products", styles.Heading);
            Cells(styles.HeaderCell, ProductWidths, "Nuclide", "Yield per fission");
            foreach (string[] row in yieldRows)
            {
                Cells(styles.Cell, ProductWidths, row);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Send neutrons at a pile of this nucleus", styles.Heading);
            GUILayout.Label("Event level: the neutrons come from outside and each goes into a different atom. Nothing models geometry, flux or a chain reaction.", styles.Hint);
            GUILayout.BeginHorizontal();
            atomsText = Field("Atoms", atomsText, 45f, 100f);
            neutronsText = Field("Neutrons", neutronsText, 65f, 100f);
            seedText = Field("Seed", seedText, 35f, 50f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            probabilityText = Field("Fission share of absorptions, 0 to 1 (blank = authored table)", probabilityText, 370f, 60f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Irradiate", GUILayout.Width(120f)))
            {
                Do(Irradiate);
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

        public override void Dispose()
        {
            lab.Dispose();
        }

        private void Query()
        {
            Nuclide target = NuclearLabInput.ParseNuclide(targetText, "Nucleus");
            double energy = NuclearLabInput.ParseNumber(energyText, "Neutron energy", false);
            FissionInfo info = lab.Query(target, energy);

            List<string> lines = new List<string>();
            FissionSummary s = info.Summary;
            lines.Add(energy > 0.0
                ? "n (" + NuclearLabFormat.Number(energy) + " eV) + " + target + " -> " + s.Compound
                : "Spontaneous fission of " + target);
            lines.Add("Prompt neutrons per fission: " + NuclearLabFormat.Number(s.MeanNeutrons)
                + "   Energy released per fission: " + NuclearLabFormat.Energy(s.MeanQKev));
            lines.Add(s.ChannelCount + " channels, " + info.ProductCount + " products. Yields "
                + NuclearLabFormat.YieldSourceText(s.Source) + " (" + s.Reference + ").");
            if (energy > 0.0)
            {
                if (info.HasBranching)
                {
                    lines.Add("Authored fission share of absorbed neutrons: "
                        + NuclearLabFormat.Percent(info.FissionProbability * 100.0)
                        + (info.HasCaptureQ ? ". A capture releases " + NuclearLabFormat.Energy(info.CaptureQKev) + "." : "."));
                }
                else
                {
                    lines.Add("No authored fission share for " + target + ", so the irradiation below needs one typed in.");
                }
            }

            string[][] channels = new string[info.TopChannels.Length][];
            for (int i = 0; i < channels.Length; i++)
            {
                FissionChannel c = info.TopChannels[i];
                channels[i] = new[]
                {
                    NuclearLabFormat.Percent(c.Probability * 100.0),
                    c.Light + " + " + c.Heavy,
                    c.Neutrons.ToString(),
                    c.QKnown ? NuclearLabFormat.Energy(c.QKev) : "unknown",
                };
            }

            string[][] yields = new string[info.TopYields.Length][];
            for (int i = 0; i < yields.Length; i++)
            {
                NuclideAmount y = info.TopYields[i];
                yields[i] = new[] { y.Nuclide.ToString(), NuclearLabFormat.Percent(y.Amount * 100.0) };
            }

            double max = 0.0;
            double floor = double.MaxValue;
            foreach (double v in info.MassYields)
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

            infoLines = lines.ToArray();
            channelRows = channels;
            yieldRows = yields;
            massYields = info.MassYields;
            massMin = info.MassMin;
            massMax = max;
            massFloor = floor == double.MaxValue ? max / 1000.0 : floor;
            runLines = new string[0];
            runRows = new string[0][];
        }

        private void Irradiate()
        {
            Nuclide target = NuclearLabInput.ParseNuclide(targetText, "Nucleus");
            double energy = NuclearLabInput.ParseNumber(energyText, "Neutron energy", true);
            ulong atoms = NuclearLabInput.ParseCount(atomsText, "Atoms", 1UL);
            ulong neutrons = NuclearLabInput.ParseCount(neutronsText, "Neutrons", 1UL);
            ulong seed = NuclearLabInput.ParseCount(seedText, "Seed", 0UL);
            double? probability = null;
            if (probabilityText.Trim().Length > 0)
            {
                double p = NuclearLabInput.ParseNumber(probabilityText, "Fission share", false);
                if (p > 1.0)
                {
                    throw new FormatException("Fission share must be between 0 and 1.");
                }
                probability = p;
            }

            FissionRun run = lab.Run(target, atoms, neutrons, energy, probability, seed);
            ReactionReport r = run.Report;
            List<string> lines = new List<string>();
            lines.Add(NuclearLabFormat.Count(r.Fissions) + " fissions, " + NuclearLabFormat.Count(r.Captures)
                + " captures, " + NuclearLabFormat.Count(r.NeutronsReleased) + " neutrons released");
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
            if (run.Products.Length > shown)
            {
                lines.Add("The " + shown + " most plentiful of " + run.Products.Length + " nuclides:");
            }
            runLines = lines.ToArray();
            runRows = table;
        }
    }
}
