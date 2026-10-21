// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FissionShowPlan.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Everything one run of the fission animation needs to know: which fission it shows, how much energy the
    /// flash stands for, and the few random choices that only make one run look different from another. Built by
    /// <see cref="Build"/> from the channels the library publishes for a nucleus. Has no UnityEngine references.
    /// </summary>
    public sealed class FissionShowPlan
    {
        /// <summary>The most neutrons the animation draws. A channel with more is drawn with this many.</summary>
        public const int MaxNeutronsShown = 12;

        /// <summary>The nucleus asked about.</summary>
        public Nuclide Target;
        /// <summary>The nucleus that fissions. The target plus a neutron, or the target itself for spontaneous fission.</summary>
        public Nuclide Compound;
        /// <summary>True when a neutron arrives first.</summary>
        public bool Induced;
        /// <summary>The lighter fragment.</summary>
        public Nuclide Light;
        /// <summary>The heavier fragment.</summary>
        public Nuclide Heavy;
        /// <summary>Prompt neutrons of the chosen channel.</summary>
        public int Neutrons;
        /// <summary>Energy released by the one fission shown, in keV.</summary>
        public double EventKev;
        /// <summary>True when the channel has no energy of its own and <see cref="EventKev"/> is the mean over all channels.</summary>
        public bool EventEnergyIsMean;
        /// <summary>The number of fissions this show stands for. 1 for a single atom.</summary>
        public ulong Events;
        /// <summary>The energy the flash is sized to, in keV. The one fission, or all the fissions of a run.</summary>
        public double BlastKev;
        /// <summary>The nuclear equation of the chosen channel, as text.</summary>
        public string Equation;
        /// <summary>Direction in which the lighter fragment leaves, in radians. Measured from the +x axis, y downwards.</summary>
        public float AxisAngle;
        /// <summary>Direction of the incoming neutron, in radians. It travels from the left towards the nucleus.</summary>
        public float IncomingAngle;
        /// <summary>The direction of every neutron that leaves, in radians. One for each neutron drawn.</summary>
        public float[] NeutronAngles;
        /// <summary>How far each neutron flies, as a share of the room it has, 0.65 to 0.95.</summary>
        public float[] NeutronReach;
        /// <summary>The caption of every phase, indexed by <see cref="FissionPhase"/>.</summary>
        public string[] Captions;

        /// <summary>
        /// Picks one channel from <paramref name="info"/> at random, weighted by the probability of each channel, and
        /// plans the animation of it. <paramref name="blastKev"/> is the energy the flash stands for, and 0 or less
        /// means the energy of the one fission. <paramref name="events"/> is how many fissions this show stands for.
        /// Throws <see cref="InvalidOperationException"/> when no channel has a probability.
        /// </summary>
        public static FissionShowPlan Build(FissionInfo info, NuclearLabRandom rng, double blastKev, ulong events)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }
            if (rng == null)
            {
                throw new ArgumentNullException("rng");
            }
            if (events < 1UL)
            {
                throw new ArgumentOutOfRangeException("events", "A show stands for at least one fission.");
            }

            FissionChannel[] channels = info.AllChannels;
            double total = 0.0;
            int last = -1;
            for (int i = 0; channels != null && i < channels.Length; i++)
            {
                if (channels[i].Probability > 0.0)
                {
                    total += channels[i].Probability;
                    last = i;
                }
            }
            if (last < 0)
            {
                throw new InvalidOperationException("No fission channel with a probability for " + info.Target + ".");
            }

            double pick = rng.NextDouble() * total;
            double run = 0.0;
            int chosen = last;
            for (int i = 0; i <= last; i++)
            {
                if (channels[i].Probability > 0.0)
                {
                    run += channels[i].Probability;
                    if (pick < run)
                    {
                        chosen = i;
                        break;
                    }
                }
            }
            FissionChannel channel = channels[chosen];

            FissionShowPlan plan = new FissionShowPlan();
            plan.Target = info.Target;
            plan.Compound = info.Summary.Compound;
            plan.Induced = info.EnergyEv > 0.0;
            plan.Light = channel.Light;
            plan.Heavy = channel.Heavy;
            plan.Neutrons = channel.Neutrons;
            plan.EventEnergyIsMean = !channel.QKnown;
            plan.EventKev = channel.QKnown ? channel.QKev : info.Summary.MeanQKev;
            plan.Events = events;
            plan.BlastKev = blastKev > 0.0 ? blastKev : plan.EventKev;

            plan.AxisAngle = (float)rng.Range(-0.45, 0.45) + (rng.NextDouble() < 0.5 ? 0f : (float)Math.PI);
            plan.IncomingAngle = (float)rng.Range(-0.3, 0.3);
            int shown = Math.Min(plan.Neutrons, MaxNeutronsShown);
            plan.NeutronAngles = new float[shown];
            plan.NeutronReach = new float[shown];
            double first = rng.Range(0.0, 2.0 * Math.PI);
            for (int i = 0; i < shown; i++)
            {
                double angle = first + 2.0 * Math.PI * i / shown + rng.Range(-0.25, 0.25);
                plan.NeutronAngles[i] = ClearOfAxis(angle, plan.AxisAngle);
                plan.NeutronReach[i] = (float)rng.Range(0.65, 0.95);
            }

            plan.Equation = BuildEquation(plan);
            plan.Captions = BuildCaptions(plan);
            return plan;
        }

        // The neutrons are spread round the circle, one slot each with a little jitter. One that would fly along the
        // line of the fragments would hide behind a fragment, so it is moved to 0.4 radians off that line.
        private static float ClearOfAxis(double angle, double axis)
        {
            double off = Math.IEEERemainder(angle - axis, Math.PI);
            if (Math.Abs(off) < 0.4)
            {
                angle += off >= 0.0 ? 0.4 - off : -0.4 - off;
            }
            return (float)angle;
        }

        private static string BuildEquation(FissionShowPlan plan)
        {
            string products = plan.Light + " + " + plan.Heavy + (plan.Neutrons > 0 ? " + " + plan.Neutrons + " n" : string.Empty);
            return plan.Induced
                ? "n + " + plan.Target + " -> " + plan.Compound + "* -> " + products
                : plan.Compound + " -> " + products;
        }

        private static string[] BuildCaptions(FissionShowPlan plan)
        {
            string[] captions = new string[7];
            captions[(int)FissionPhase.Ready] = "Ready. Press Split one atom.";
            captions[(int)FissionPhase.Approach] = "A neutron heads for a " + plan.Target + " nucleus.";
            captions[(int)FissionPhase.Absorb] = plan.Induced
                ? "The nucleus absorbs it and becomes " + plan.Compound + ", excited."
                : plan.Compound + " needs no neutron. Some nuclei split on their own.";
            captions[(int)FissionPhase.Wobble] = "The excited nucleus wobbles like a liquid drop and stretches into a dumbbell.";
            captions[(int)FissionPhase.Split] = "It splits: " + plan.Equation + ". The energy comes out.";
            captions[(int)FissionPhase.Aftermath] = "The two fragments push each other apart. The lighter one, " + plan.Light
                + ", moves faster, because both carry the same momentum. "
                + (plan.Neutrons > 0 ? plan.Neutrons + " prompt neutrons fly off." : "No prompt neutron comes out of this channel.");
            captions[(int)FissionPhase.Done] = DoneCaption(plan);
            return captions;
        }

        private static string DoneCaption(FissionShowPlan plan)
        {
            string tnt = NuclearLabBlast.TntText(plan.BlastKev * NuclearLabFormat.JoulesPerKev);
            if (plan.Events > 1UL)
            {
                return "Done. This split stands for " + NuclearLabFormat.Count(plan.Events) + " fissions. Together they released "
                    + NuclearLabFormat.Energy(plan.BlastKev) + " = " + NuclearLabFormat.Joules(plan.BlastKev)
                    + (tnt.Length > 0 ? ", " + tnt : string.Empty) + ". The flash is sized on a log scale of joules.";
            }
            return "Done. One fission released " + NuclearLabFormat.Energy(plan.EventKev)
                + (plan.EventEnergyIsMean ? " (the mean over all channels)" : string.Empty)
                + " = " + NuclearLabFormat.Joules(plan.EventKev) + (tnt.Length > 0 ? ", " + tnt : string.Empty)
                + ". Press Split one atom for another draw.";
        }
    }
}
