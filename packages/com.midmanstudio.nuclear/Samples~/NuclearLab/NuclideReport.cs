// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclideReport.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>One way a nuclide decays, with the energy it releases when the library can give it.</summary>
    public sealed class DecayLine
    {
        /// <summary>The outcome from the decay table.</summary>
        public DecayOutcome Outcome;
        /// <summary>True when <see cref="Q"/> is a value. Spontaneous fission has none.</summary>
        public bool HasQ;
        /// <summary>The energy released by this mode.</summary>
        public EnergyValue Q;
    }

    /// <summary>
    /// Everything the nuclide tab shows for one nuclide, read from the library
    /// in one go: mass, binding energy, half-life, abundance, decay modes and
    /// the chain to a stable nuclide.
    /// </summary>
    public sealed class NuclideReport
    {
        /// <summary>The nuclide that was looked up.</summary>
        public Nuclide Nuclide;
        /// <summary>Atomic mass excess.</summary>
        public EnergyValue MassExcess;
        /// <summary>Total and per-nucleon binding energy.</summary>
        public BindingValue Binding;
        /// <summary>True when the decay tables have a record for the nuclide.</summary>
        public bool HasHalfLife;
        /// <summary>The ground-state half-life. Only meaningful when <see cref="HasHalfLife"/>.</summary>
        public HalfLife HalfLife;
        /// <summary>Natural abundance in percent, 0 when the nuclide is not found in nature.</summary>
        public double AbundancePercent;
        /// <summary>The decay modes, empty for a stable nuclide.</summary>
        public DecayLine[] Decays;
        /// <summary>One text line per step of the chain to stability.</summary>
        public string[] ChainLines;
        /// <summary>The last line of the chain: where it ends and why.</summary>
        public string ChainEnd;

        /// <summary>Reads the report from the native library.</summary>
        public static NuclideReport Build(Nuclide nuclide)
        {
            NuclideReport report = new NuclideReport();
            report.Nuclide = nuclide;
            report.MassExcess = NuclearData.GetMassExcess(nuclide);
            report.Binding = NuclearData.GetBindingEnergy(nuclide);
            report.HasHalfLife = NuclearData.TryGetHalfLife(nuclide, out report.HalfLife);
            report.AbundancePercent = NuclearData.GetAbundancePercent(nuclide);

            DecayOutcome[] outcomes = NuclearData.GetDecayOutcomes(nuclide);
            report.Decays = new DecayLine[outcomes.Length];
            for (int i = 0; i < outcomes.Length; i++)
            {
                report.Decays[i] = MakeLine(nuclide, outcomes[i]);
            }

            DecayChain chain = NuclearData.GetChainToStability(nuclide);
            List<string> lines = new List<string>();
            for (int i = 0; i < chain.Steps.Length; i++)
            {
                lines.Add(ChainLine(chain.Steps[i]));
            }
            report.ChainLines = lines.ToArray();
            report.ChainEnd = chain.End.Nuclide + ": " + NuclearLabFormat.EndReasonText(chain.End.Reason);
            return report;
        }

        private static DecayLine MakeLine(Nuclide nuclide, DecayOutcome outcome)
        {
            DecayLine line = new DecayLine();
            line.Outcome = outcome;
            try
            {
                Nuclide? cluster = outcome.HasCluster ? outcome.Cluster : (Nuclide?)null;
                line.Q = NuclearData.GetDecayQValue(nuclide, outcome.Mode, cluster);
                line.HasQ = true;
            }
            catch (NuclearException)
            {
                line.HasQ = false;
            }
            return line;
        }

        private static string ChainLine(ChainStep step)
        {
            string target;
            DecayOutcome outcome = step.Outcome;
            if (outcome.HasDaughter)
            {
                target = outcome.Daughter.ToString();
            }
            else
            {
                target = "(" + NuclearLabFormat.ModeName(outcome.Mode) + ")";
            }
            HalfLife halfLife;
            string life = NuclearData.TryGetHalfLife(step.Parent, out halfLife)
                ? NuclearLabFormat.HalfLifeText(halfLife)
                : "unknown";
            return step.Parent + " -> " + target + "   " + NuclearLabFormat.ModeName(outcome.Mode) + ", half-life " + life;
        }

        /// <summary>The text for the decay mode of a line: the mode and what it leaves behind.</summary>
        public static string DescribeOutcome(DecayOutcome outcome)
        {
            string text = NuclearLabFormat.ModeName(outcome.Mode);
            if (outcome.HasCluster)
            {
                text += " (" + outcome.Cluster + ")";
            }
            if (outcome.HasDaughter)
            {
                text += " -> " + outcome.Daughter;
            }
            return text;
        }
    }
}
