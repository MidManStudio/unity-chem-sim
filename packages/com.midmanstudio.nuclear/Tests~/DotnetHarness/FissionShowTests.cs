// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Tests~/DotnetHarness/FissionShowTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.Nuclear.Samples.NuclearLab.Tests
{
    /// <summary>
    /// Checks the model half of the fission animation: the energy scale, the shapes, the random stream, the plan
    /// drawn from the library's real channels, the clock and the frames. Runs only in the .NET harness. The
    /// drawing itself is not covered here, because it needs UnityEngine.
    /// </summary>
    [TestFixture]
    public class FissionShowTests
    {
        private static readonly float[] Aspects = { 1.0f, 2.8f, 5.0f };

        private static FissionInfo Info(string nucleus, double energyEv)
        {
            using (FissionLab lab = new FissionLab(8, 10))
            {
                return lab.Query(Nuclide.Parse(nucleus), energyEv);
            }
        }

        private static FissionShow Show(FissionInfo info, ulong seed, double blastKev, ulong events)
        {
            FissionShow show = new FissionShow();
            show.Prepare(FissionShowPlan.Build(info, new NuclearLabRandom(seed), blastKev, events));
            return show;
        }

        private static FissionFrame FrameAt(FissionShow show, float seconds, float aspect)
        {
            FissionFrame frame = new FissionFrame();
            show.Seek(seconds);
            show.Evaluate(aspect, frame);
            return frame;
        }

        private static bool Finite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static int CountDiscs(FissionFrame frame, StageTint tint, float mix)
        {
            int n = 0;
            foreach (StageBlob b in frame.Blobs)
            {
                if (b.Kind == StageKind.Disc && b.Tint == tint && b.Mix == mix)
                {
                    n++;
                }
            }
            return n;
        }

        private static StageBlob Disc(FissionFrame frame, StageTint tint)
        {
            foreach (StageBlob b in frame.Blobs)
            {
                if (b.Kind == StageKind.Disc && b.Tint == tint)
                {
                    return b;
                }
            }
            Assert.Fail("no " + tint + " disc in the frame");
            return default(StageBlob);
        }

        [Test]
        public void BlastScaleAndTntTextFollowTheEnergy()
        {
            Assert.AreEqual(0.0, NuclearLabBlast.Scale(0.0), 0.0);
            Assert.AreEqual(0.0, NuclearLabBlast.Scale(-5.0), 0.0);
            Assert.AreEqual(0.0, NuclearLabBlast.Scale(double.NaN), 0.0);
            Assert.AreEqual(0.0, NuclearLabBlast.Scale(NuclearLabBlast.MinJoules), 0.0);
            Assert.AreEqual(1.0, NuclearLabBlast.Scale(NuclearLabBlast.MaxJoules), 0.0);
            Assert.AreEqual(1.0, NuclearLabBlast.Scale(1.0e30), 0.0);
            Assert.AreEqual(1.0, NuclearLabBlast.Scale(double.PositiveInfinity), 0.0);

            double previous = -1.0;
            for (double joules = 1.0e-10; joules < 1.0e6; joules *= 10.0)
            {
                double scale = NuclearLabBlast.Scale(joules);
                Assert.IsTrue(scale > previous, "the scale must rise with the energy at " + joules + " J");
                previous = scale;
            }

            // One U-235 fission, about 200 MeV, sits just above the bottom of the scale, and a mole of them
            // is far past the top.
            double one = NuclearLabBlast.ScaleKev(202790.0);
            Assert.IsTrue(one > 0.0 && one < 0.05, "one fission draws a small flash, found " + one);
            Assert.AreEqual(1.0, NuclearLabBlast.ScaleKev(202790.0 * 6.0e23), 0.0);

            Assert.AreEqual("about 1 g of TNT", NuclearLabBlast.TntText(4184.0));
            Assert.AreEqual("about 1 kg of TNT", NuclearLabBlast.TntText(4.184e6));
            Assert.AreEqual("about 4.184 kg of TNT", NuclearLabBlast.TntText(4184.0 * 4184.0));
            Assert.AreEqual("about 1 t of TNT", NuclearLabBlast.TntText(4.184e9));
            Assert.AreEqual("about 1 kt of TNT", NuclearLabBlast.TntText(4.184e12));
            Assert.AreEqual("about 15 Mt of TNT", NuclearLabBlast.TntText(15.0 * 4.184e15));
            Assert.AreEqual("about 2 Gt of TNT", NuclearLabBlast.TntText(2.0 * 4.184e18));
            Assert.AreEqual("about 1,250 Gt of TNT", NuclearLabBlast.TntText(1250.0 * 4.184e18));
            Assert.AreEqual("about 7.648e-9 g of TNT", NuclearLabBlast.TntText(3.2e-5));
            Assert.AreEqual(string.Empty, NuclearLabBlast.TntText(0.0));
            Assert.AreEqual(string.Empty, NuclearLabBlast.TntText(-1.0));
            Assert.AreEqual(string.Empty, NuclearLabBlast.TntText(double.NaN));
            Assert.AreEqual(string.Empty, NuclearLabBlast.TntText(double.PositiveInfinity));
        }

        [Test]
        public void ShapesAreSolidAtTheCentreAndEmptyOutside()
        {
            foreach (StageKind kind in new[] { StageKind.Disc, StageKind.Glow, StageKind.Ring })
            {
                Assert.AreEqual(0f, NuclearLabShape.Alpha(kind, 1f), 0f);
                Assert.AreEqual(0f, NuclearLabShape.Alpha(kind, 1.5f), 0f);
                Assert.AreEqual(0f, NuclearLabShape.Alpha(kind, float.NaN), 0f);
                for (float d = 0f; d <= 1.2f; d += 0.01f)
                {
                    float a = NuclearLabShape.Alpha(kind, d);
                    Assert.IsTrue(a >= 0f && a <= 1f, kind + " alpha out of range at " + d);
                }
            }
            Assert.AreEqual(1f, NuclearLabShape.Alpha(StageKind.Disc, 0f), 0f);
            Assert.AreEqual(1f, NuclearLabShape.Alpha(StageKind.Disc, 0.5f), 0f);
            Assert.AreEqual(1f, NuclearLabShape.Alpha(StageKind.Disc, 0.85f), 0f);
            Assert.AreEqual(0.5f, NuclearLabShape.Alpha(StageKind.Disc, 0.94f), 1e-6f);
            Assert.AreEqual(1f, NuclearLabShape.Alpha(StageKind.Glow, 0f), 0f);
            Assert.AreEqual(0.25f, NuclearLabShape.Alpha(StageKind.Glow, 0.5f), 1e-6f);
            Assert.AreEqual(0f, NuclearLabShape.Alpha(StageKind.Ring, 0.5f), 0f);
            Assert.AreEqual(1f, NuclearLabShape.Alpha(StageKind.Ring, 0.89f), 0.02f);

            for (float d = 0.01f; d < 1f; d += 0.01f)
            {
                Assert.IsTrue(NuclearLabShape.Alpha(StageKind.Glow, d) <= NuclearLabShape.Alpha(StageKind.Glow, d - 0.01f), "a glow only fades outward");
                Assert.IsTrue(NuclearLabShape.Alpha(StageKind.Disc, d) <= NuclearLabShape.Alpha(StageKind.Disc, d - 0.01f), "a disc only fades outward");
            }

            byte[] disc = NuclearLabShape.Bake(StageKind.Disc, 32);
            Assert.AreEqual(32 * 32, disc.Length);
            Assert.AreEqual((byte)255, disc[16 * 32 + 16]);
            Assert.AreEqual((byte)0, disc[0]);
            Assert.AreEqual((byte)0, disc[32 * 32 - 1]);
            byte[] ring = NuclearLabShape.Bake(StageKind.Ring, 64);
            Assert.AreEqual((byte)0, ring[32 * 64 + 32]);
            Assert.IsTrue(ring[32 * 64 + 61] > 100, "the ring has a band near the edge");
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Assert.AreEqual(disc[y * 32 + x], disc[y * 32 + (31 - x)], "the disc is symmetric left to right");
                    Assert.AreEqual(disc[y * 32 + x], disc[(31 - y) * 32 + x], "the disc is symmetric top to bottom");
                }
            }
            Assert.Throws<ArgumentOutOfRangeException>(delegate { NuclearLabShape.Bake(StageKind.Disc, 1); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { NuclearLabShape.Alpha((StageKind)9, 0.5f); });
        }

        [Test]
        public void RandomStreamIsReproducible()
        {
            NuclearLabRandom a = new NuclearLabRandom(42UL);
            NuclearLabRandom b = new NuclearLabRandom(42UL);
            NuclearLabRandom c = new NuclearLabRandom(43UL);
            bool differs = false;
            double sum = 0.0;
            for (int i = 0; i < 20000; i++)
            {
                double x = a.NextDouble();
                Assert.AreEqual(x, b.NextDouble(), 0.0);
                Assert.IsTrue(x >= 0.0 && x < 1.0);
                differs |= x != c.NextDouble();
                sum += x;
            }
            Assert.IsTrue(differs, "a different seed gives a different stream");
            Assert.AreEqual(0.5, sum / 20000.0, 0.01);

            // The reference output of splitmix64 from seed 0.
            NuclearLabRandom zero = new NuclearLabRandom(0UL);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, zero.NextBits());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, zero.NextBits());

            NuclearLabRandom range = new NuclearLabRandom(7UL);
            for (int i = 0; i < 1000; i++)
            {
                double r = range.Range(-2.0, 3.0);
                Assert.IsTrue(r >= -2.0 && r < 3.0);
            }

            Assert.AreNotEqual(NuclearLabRandom.Mix(5UL, 0UL), NuclearLabRandom.Mix(5UL, 1UL));
            Assert.AreEqual(NuclearLabRandom.Mix(5UL, 3UL), NuclearLabRandom.Mix(5UL, 3UL));
            Assert.AreNotEqual(NuclearLabRandom.Mix(5UL, 3UL), NuclearLabRandom.Mix(6UL, 3UL));
        }

        [Test]
        public void PlanDrawsChannelsInProportionToTheirProbability()
        {
            FissionInfo info = Info("U-235", 0.0253);
            Assert.IsTrue(info.AllChannels.Length >= info.TopChannels.Length);
            Assert.AreEqual(info.Summary.ChannelCount, info.AllChannels.Length);
            for (int i = 1; i < info.AllChannels.Length; i++)
            {
                Assert.IsTrue(info.AllChannels[i - 1].Probability >= info.AllChannels[i].Probability, "AllChannels is sorted, likeliest first");
            }

            double total = 0.0;
            foreach (FissionChannel c in info.AllChannels)
            {
                total += c.Probability;
            }

            // Draw a lot of plans and compare the share of the three likeliest channels with their probability.
            int draws = 60000;
            int[] hits = new int[3];
            NuclearLabRandom rng = new NuclearLabRandom(2026UL);
            for (int i = 0; i < draws; i++)
            {
                FissionShowPlan plan = FissionShowPlan.Build(info, rng, 0.0, 1UL);
                for (int k = 0; k < 3; k++)
                {
                    FissionChannel c = info.AllChannels[k];
                    if (plan.Light == c.Light && plan.Heavy == c.Heavy && plan.Neutrons == c.Neutrons)
                    {
                        hits[k]++;
                    }
                }
            }
            for (int k = 0; k < 3; k++)
            {
                double p = info.AllChannels[k].Probability / total;
                double sigma = Math.Sqrt(p * (1.0 - p) / draws);
                Assert.AreEqual(p, hits[k] / (double)draws, 5.0 * sigma + 1e-9, "channel " + k + " of " + info.Target);
            }
        }

        [Test]
        public void PlanIsCompleteAndReproducible()
        {
            foreach (string[] case_ in new[] { new[] { "U-235", "0.0253" }, new[] { "U-238", "2e6" }, new[] { "Cf-252", "0" }, new[] { "Pu-239", "0.0253" } })
            {
                double energy = double.Parse(case_[1], System.Globalization.CultureInfo.InvariantCulture);
                FissionInfo info = Info(case_[0], energy);
                for (ulong seed = 1UL; seed <= 40UL; seed++)
                {
                    FissionShowPlan one = FissionShowPlan.Build(info, new NuclearLabRandom(seed), 0.0, 1UL);
                    FissionShowPlan two = FissionShowPlan.Build(info, new NuclearLabRandom(seed), 0.0, 1UL);
                    Assert.AreEqual(one.Equation, two.Equation);
                    Assert.AreEqual(one.AxisAngle, two.AxisAngle, 0.0);
                    Assert.AreEqual(one.IncomingAngle, two.IncomingAngle, 0.0);
                    Assert.AreEqual(one.Induced, energy > 0.0);
                    Assert.AreEqual(info.Summary.Compound, one.Compound);
                    Assert.AreEqual(info.Target, one.Target);
                    Assert.IsTrue(one.Light.A <= one.Heavy.A, case_[0] + " light " + one.Light + " is the lighter fragment");
                    Assert.AreEqual(one.Compound.A, one.Light.A + one.Heavy.A + one.Neutrons, one.Equation + " conserves nucleons");
                    Assert.IsTrue(one.EventKev > 100000.0 && one.EventKev < 300000.0, one.Equation + " energy " + one.EventKev);
                    Assert.AreEqual(one.EventKev, one.BlastKev, 0.0);
                    Assert.AreEqual(12, FissionShowPlan.MaxNeutronsShown);
                    Assert.AreEqual(Math.Min(one.Neutrons, 12), one.NeutronAngles.Length);

                    // The energy is the one the library gives for the channel that was drawn.
                    bool matched = false;
                    foreach (FissionChannel c in info.AllChannels)
                    {
                        if (c.Light == one.Light && c.Heavy == one.Heavy && c.Neutrons == one.Neutrons)
                        {
                            matched |= one.EventKev == (c.QKnown ? c.QKev : info.Summary.MeanQKev) && one.EventEnergyIsMean == !c.QKnown;
                        }
                    }
                    Assert.IsTrue(matched, one.Equation + " carries the energy of its channel");
                    Assert.AreEqual(one.NeutronAngles.Length, one.NeutronReach.Length);
                    Assert.AreEqual(7, one.Captions.Length);
                    foreach (string caption in one.Captions)
                    {
                        Assert.IsTrue(caption != null && caption.Length > 5);
                        foreach (char ch in caption)
                        {
                            Assert.IsTrue(ch < 128, "captions are ASCII: " + caption);
                        }
                    }
                    Assert.IsTrue(one.Equation.Contains(one.Light.ToString()) && one.Equation.Contains(one.Heavy.ToString()));
                    Assert.AreEqual(energy > 0.0, one.Equation.StartsWith("n + " + case_[0]), one.Equation);

                    // No neutron flies along the line of the fragments.
                    for (int i = 0; i < one.NeutronAngles.Length; i++)
                    {
                        double off = Math.Abs(Math.IEEERemainder(one.NeutronAngles[i] - one.AxisAngle, Math.PI));
                        Assert.IsTrue(off >= 0.39, "neutron " + i + " is " + off + " rad off the fragment line");
                    }
                }
            }

            FissionInfo any = Info("U-235", 0.0253);
            FissionShowPlan big = FissionShowPlan.Build(any, new NuclearLabRandom(1UL), 5.0e12, 77000UL);
            Assert.AreEqual(5.0e12, big.BlastKev, 0.0);
            Assert.AreEqual(77000UL, big.Events);
            Assert.IsTrue(big.Captions[(int)FissionPhase.Done].Contains("77,000 fissions"));
            Assert.IsTrue(big.Captions[(int)FissionPhase.Done].Contains("TNT"));
            FissionShowPlan pair = FissionShowPlan.Build(any, new NuclearLabRandom(1UL), 0.0, 2UL);
            Assert.IsTrue(pair.Captions[(int)FissionPhase.Done].Contains("stands for 2 fissions"));
            FissionShowPlan single = FissionShowPlan.Build(any, new NuclearLabRandom(1UL), 0.0, 1UL);
            Assert.IsTrue(single.Captions[(int)FissionPhase.Done].StartsWith("Done. One fission released"));

            Assert.Throws<ArgumentNullException>(delegate { FissionShowPlan.Build(null, new NuclearLabRandom(1UL), 0.0, 1UL); });
            Assert.Throws<ArgumentNullException>(delegate { FissionShowPlan.Build(any, null, 0.0, 1UL); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { FissionShowPlan.Build(any, new NuclearLabRandom(1UL), 0.0, 0UL); });
            FissionInfo empty = new FissionInfo();
            empty.Target = Nuclide.Parse("U-235");
            empty.AllChannels = new FissionChannel[0];
            Assert.Throws<InvalidOperationException>(delegate { FissionShowPlan.Build(empty, new NuclearLabRandom(1UL), 0.0, 1UL); });
        }

        [Test]
        public void ClockWalksThroughThePhasesInOrder()
        {
            FissionInfo info = Info("U-235", 0.0253);
            FissionShow show = new FissionShow();
            Assert.IsFalse(show.HasPlan);
            Assert.AreEqual(FissionPhase.Ready, show.Phase);
            Assert.AreEqual("Press Split one atom.", show.Caption);
            show.Tick(1f);
            show.Seek(1f);
            show.Replay();
            Assert.IsFalse(show.IsPlaying);
            Assert.Throws<ArgumentNullException>(delegate { show.Prepare(null); });
            Assert.Throws<ArgumentNullException>(delegate { show.Evaluate(2f, null); });

            FissionShowPlan plan = FissionShowPlan.Build(info, new NuclearLabRandom(9UL), 0.0, 1UL);
            show.Prepare(plan);
            Assert.IsTrue(show.HasPlan);
            Assert.IsTrue(show.IsReady);
            Assert.IsFalse(show.IsPlaying);
            Assert.AreEqual(FissionPhase.Ready, show.Phase);
            show.Tick(1f);
            Assert.AreEqual(0f, show.Time, 0f);

            float expectedScission = FissionShow.ApproachSeconds + FissionShow.AbsorbSeconds + FissionShow.WobbleSeconds;
            Assert.AreEqual(expectedScission, show.ScissionTime, 1e-5f);
            Assert.AreEqual(expectedScission + FissionShow.FlyOutSeconds, show.Duration, 1e-5f);

            show.Start(plan);
            Assert.IsTrue(show.IsPlaying);
            Assert.IsFalse(show.IsReady);
            show.Tick(-1f);
            show.Tick(float.NaN);
            show.Tick(float.PositiveInfinity);
            show.Tick(0f);
            Assert.AreEqual(0f, show.Time, 0f);

            FissionPhase last = FissionPhase.Ready;
            bool[] seen = new bool[7];
            int steps = 0;
            while (show.IsPlaying && steps < 5000)
            {
                show.Tick(1f / 60f);
                steps++;
                FissionPhase phase = show.Phase;
                Assert.IsTrue(phase >= last, "phases never go backwards: " + last + " then " + phase);
                last = phase;
                seen[(int)phase] = true;
            }
            Assert.IsFalse(show.IsPlaying);
            Assert.AreEqual(FissionPhase.Done, show.Phase);
            Assert.AreEqual(show.Duration, show.Time, 0f);
            Assert.IsTrue(steps >= (int)(show.Duration * 60f) - 1 && steps <= (int)(show.Duration * 60f) + 2, "took " + steps + " steps");
            for (int i = 1; i < 7; i++)
            {
                Assert.IsTrue(seen[i], "phase " + (FissionPhase)i + " was never reached");
            }
            Assert.AreEqual(plan.Captions[(int)FissionPhase.Done], show.Caption);
            show.Tick(10f);
            Assert.AreEqual(show.Duration, show.Time, 0f);

            show.Replay();
            Assert.IsTrue(show.IsPlaying);
            Assert.AreEqual(0f, show.Time, 0f);
            show.Seek(1e9f);
            Assert.AreEqual(show.Duration, show.Time, 0f);
            Assert.IsFalse(show.IsPlaying);
            show.Seek(float.NaN);
            Assert.AreEqual(0f, show.Time, 0f);
            show.Seek(-3f);
            Assert.AreEqual(0f, show.Time, 0f);

            show.Clear();
            Assert.IsFalse(show.HasPlan);
            Assert.AreEqual(0f, show.Duration, 0f);
            Assert.AreEqual("Press Split one atom.", show.Caption);

            // A nucleus that splits by itself has no approach phase.
            FissionShow alone = Show(Info("Cf-252", 0.0), 3UL, 0.0, 1UL);
            Assert.AreEqual(FissionShow.RestSeconds + FissionShow.WobbleSeconds, alone.ScissionTime, 1e-5f);
            alone.Seek(0.01f);
            Assert.AreEqual(FissionPhase.Absorb, alone.Phase);
            alone.Seek(alone.ScissionTime + 0.1f);
            Assert.AreEqual(FissionPhase.Split, alone.Phase);
            alone.Seek(alone.ScissionTime + FissionShow.SplitSeconds + 0.1f);
            Assert.AreEqual(FissionPhase.Aftermath, alone.Phase);
        }

        [Test]
        public void FramesAreFiniteAndFragmentsStayOnTheStage()
        {
            string[][] cases = { new[] { "U-235", "0.0253" }, new[] { "U-238", "2e6" }, new[] { "Cf-252", "0" }, new[] { "Pu-239", "0.0253" } };
            foreach (string[] c in cases)
            {
                double energy = double.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture);
                FissionInfo info = Info(c[0], energy);
                for (ulong seed = 1UL; seed <= 12UL; seed++)
                {
                    foreach (float aspect in Aspects)
                    {
                        FissionShow show = Show(info, seed, seed % 2UL == 0UL ? 1.0e12 : 0.0, seed % 2UL == 0UL ? 5000UL : 1UL);
                        FissionFrame frame = new FissionFrame();
                        for (float t = 0f; t <= show.Duration + 0.001f; t += 0.04f)
                        {
                            show.Seek(t);
                            show.Evaluate(aspect, frame);
                            Assert.IsTrue(frame.Blobs.Count > 0 && frame.Caption.Length > 0);
                            Assert.IsTrue(Finite(frame.Wash) && frame.Wash >= 0f && frame.Wash <= 1f, "wash " + frame.Wash);
                            foreach (StageBlob b in frame.Blobs)
                            {
                                Assert.IsTrue(Finite(b.X) && Finite(b.Y) && Finite(b.Radius) && Finite(b.Alpha) && Finite(b.Mix), c[0] + " blob not finite at " + t);
                                Assert.IsTrue(b.Radius >= 0f, "radius " + b.Radius);
                                Assert.IsTrue(b.Alpha >= 0f && b.Alpha <= 1.0001f, "alpha " + b.Alpha + " of " + b.Kind + " at " + t);
                                Assert.IsTrue(b.Mix >= 0f && b.Mix <= 1f, "mix " + b.Mix);
                            }
                            foreach (StageLabel l in frame.Labels)
                            {
                                Assert.IsTrue(Finite(l.X) && Finite(l.Y) && l.Alpha >= 0f && l.Alpha <= 1.0001f && l.Text.Length > 0, "label " + l.Text + " at " + t);
                            }
                        }

                        // At the end both fragments and every neutron are inside the stage.
                        show.Seek(show.Duration);
                        show.Evaluate(aspect, frame);
                        StageBlob light = Disc(frame, StageTint.Light);
                        StageBlob heavy = Disc(frame, StageTint.Heavy);
                        Assert.IsTrue(Math.Abs(light.X) + light.Radius <= aspect + 1e-3f && Math.Abs(light.Y) + light.Radius <= 1f + 1e-3f,
                            c[0] + " seed " + seed + " aspect " + aspect + ": light fragment at " + light.X + ", " + light.Y);
                        Assert.IsTrue(Math.Abs(heavy.X) + heavy.Radius <= aspect + 1e-3f && Math.Abs(heavy.Y) + heavy.Radius <= 1f + 1e-3f,
                            c[0] + " seed " + seed + " aspect " + aspect + ": heavy fragment at " + heavy.X + ", " + heavy.Y);
                        foreach (StageBlob b in frame.Blobs)
                        {
                            if (b.Kind == StageKind.Disc && b.Tint == StageTint.Neutron)
                            {
                                Assert.IsTrue(Math.Abs(b.X) <= aspect && Math.Abs(b.Y) <= 1f, "a neutron left the stage at " + b.X + ", " + b.Y);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void FragmentsMoveInInverseProportionToTheirMass()
        {
            FissionInfo info = Info("U-235", 0.0253);
            for (ulong seed = 1UL; seed <= 30UL; seed++)
            {
                FissionShow show = Show(info, seed, 0.0, 1UL);
                FissionShowPlan plan = show.Plan;
                foreach (float t in new[] { 0.2f, 0.6f, 1.5f, 2.5f })
                {
                    FissionFrame frame = FrameAt(show, show.ScissionTime + t, 2.8f);
                    StageBlob light = Disc(frame, StageTint.Light);
                    StageBlob heavy = Disc(frame, StageTint.Heavy);
                    double lightAway = Math.Sqrt(light.X * light.X + light.Y * light.Y);
                    double heavyAway = Math.Sqrt(heavy.X * heavy.X + heavy.Y * heavy.Y);
                    Assert.AreEqual(plan.Light.A * lightAway, plan.Heavy.A * heavyAway, 1e-3, "momentum balance at " + t + " s, " + plan.Equation);
                    if (plan.Light.A < plan.Heavy.A)
                    {
                        Assert.IsTrue(lightAway > heavyAway, "the lighter fragment is further out");
                    }
                    // They fly apart, on opposite sides of the centre.
                    Assert.IsTrue(light.X * heavy.X + light.Y * heavy.Y < 0.0, "opposite directions");
                }
                // The heavier nucleus is the bigger disc, and the radii follow the cube root of the mass.
                FissionFrame end = FrameAt(show, show.Duration, 2.8f);
                StageBlob l = Disc(end, StageTint.Light);
                StageBlob h = Disc(end, StageTint.Heavy);
                Assert.AreEqual(Math.Pow(plan.Heavy.A / (double)plan.Light.A, 1.0 / 3.0), h.Radius / l.Radius, 1e-3);

                // Volume follows the mass number. At the split the two fragments fill the nucleus they came from. The
                // neutrons leave afterwards, so the fragments end smaller, by the share of the mass they lost.
                double rc = Disc(FrameAt(show, FissionShow.ApproachSeconds + FissionShow.AbsorbSeconds - 0.0001f, 2.8f), StageTint.Nucleus).Radius;
                StageBlob l0 = Disc(FrameAt(show, show.ScissionTime + 0.001f, 2.8f), StageTint.Light);
                StageBlob h0 = Disc(FrameAt(show, show.ScissionTime + 0.001f, 2.8f), StageTint.Heavy);
                Assert.AreEqual(rc * rc * rc, Math.Pow(l0.Radius, 3.0) + Math.Pow(h0.Radius, 3.0), 0.02 * rc * rc * rc, "fragments fill the nucleus at the split, " + plan.Equation);
                double share = (plan.Light.A + plan.Heavy.A) / (double)plan.Compound.A;
                Assert.AreEqual(rc * rc * rc * share, Math.Pow(l.Radius, 3.0) + Math.Pow(h.Radius, 3.0), 0.006 * rc * rc * rc, "fragments end at the size of their mass, " + plan.Equation);
            }
        }

        [Test]
        public void NucleusStretchesIntoADumbbellBeforeItSplits()
        {
            FissionShow show = Show(Info("U-235", 0.0253), 5UL, 0.0, 1UL);
            float wobbleStart = FissionShow.ApproachSeconds + FissionShow.AbsorbSeconds;

            // At the start of the wobble the nucleus is one round shape. Just before the split two lobes of
            // fragment size sit side by side and the waist between them has almost closed.
            FissionFrame early = FrameAt(show, wobbleStart - 0.001f, 2.8f);
            float rc = Disc(early, StageTint.Nucleus).Radius;
            FissionFrame late = FrameAt(show, show.ScissionTime - 0.01f, 2.8f);
            float lightLobe = 0f;
            float heavyLobe = 0f;
            float widestWaist = 0f;
            float sparks = 0f;
            foreach (StageBlob b in late.Blobs)
            {
                if (b.Kind == StageKind.Disc && b.Tint == StageTint.Light) { lightLobe = b.Radius; }
                if (b.Kind == StageKind.Disc && b.Tint == StageTint.Heavy) { heavyLobe = b.Radius; }
                if (b.Kind == StageKind.Disc && b.Tint == StageTint.Nucleus) { widestWaist = Math.Max(widestWaist, b.Radius); }
                if (b.Kind == StageKind.Glow && b.Tint == StageTint.Flash) { sparks = Math.Max(sparks, b.Alpha); }
            }
            Assert.IsTrue(lightLobe < rc && heavyLobe < rc, "lobes are smaller than the nucleus they came from");
            Assert.IsTrue(lightLobe < heavyLobe || show.Plan.Light.A == show.Plan.Heavy.A, "the lighter fragment has the smaller lobe");
            Assert.IsTrue(widestWaist < 0.1f * rc, "the waist is nearly closed, found " + widestWaist + " of " + rc);
            Assert.IsTrue(sparks > 0.9f, "the neck glows white just before it breaks");

            // The lobes just touch when the split comes.
            StageBlob lateLight = Disc(late, StageTint.Light);
            StageBlob lateHeavy = Disc(late, StageTint.Heavy);
            float lateGap = (float)Math.Sqrt((lateLight.X - lateHeavy.X) * (lateLight.X - lateHeavy.X) + (lateLight.Y - lateHeavy.Y) * (lateLight.Y - lateHeavy.Y));
            Assert.AreEqual(lightLobe + heavyLobe, lateGap, 0.02f);

            // The lobes' volumes add up to the nucleus: r_l^3 + r_h^3 = r_c^3 at the split.
            Assert.AreEqual(rc * rc * rc, lightLobe * lightLobe * lightLobe + heavyLobe * heavyLobe * heavyLobe, 0.03 * rc * rc * rc);

            // The lobes drift apart and never overlap by much more than they have to.
            float previousSeparation = -1f;
            for (float t = wobbleStart; t < show.ScissionTime; t += 0.05f)
            {
                FissionFrame f = FrameAt(show, t, 2.8f);
                StageBlob l = Disc(f, StageTint.Light);
                StageBlob h = Disc(f, StageTint.Heavy);
                float separation = (float)Math.Sqrt((l.X - h.X) * (l.X - h.X) + (l.Y - h.Y) * (l.Y - h.Y));
                Assert.IsTrue(separation <= l.Radius + h.Radius + 1e-3f, "lobes never come apart before the split");
                Assert.IsTrue(separation >= 0f);
                if (t > wobbleStart + 1.2f)
                {
                    Assert.IsTrue(separation >= previousSeparation - 0.12f, "the stretch keeps growing near the end");
                }
                previousSeparation = separation;
            }
        }

        [Test]
        public void FlashGrowsWithTheEnergyAndFadesOut()
        {
            FissionInfo info = Info("U-235", 0.0253);
            FissionShow small = Show(info, 11UL, 0.0, 1UL);
            FissionShow big = Show(info, 11UL, 2.6e28, 1000000UL);
            Assert.AreEqual(small.Plan.Equation, big.Plan.Equation);

            float FlashRadius(FissionShow s, float tau)
            {
                FissionFrame f = FrameAt(s, s.ScissionTime + tau, 2.8f);
                float r = 0f;
                foreach (StageBlob b in f.Blobs)
                {
                    if (b.Kind == StageKind.Glow && b.Tint == StageTint.Flash)
                    {
                        r = Math.Max(r, b.Radius);
                    }
                }
                return r;
            }

            float RingRadius(FissionShow s, float tau)
            {
                FissionFrame f = FrameAt(s, s.ScissionTime + tau, 2.8f);
                float r = 0f;
                foreach (StageBlob b in f.Blobs)
                {
                    if (b.Kind == StageKind.Ring)
                    {
                        r = Math.Max(r, b.Radius);
                    }
                }
                return r;
            }

            Assert.IsTrue(FlashRadius(big, 0.4f) > 1.5f * FlashRadius(small, 0.4f), "a bigger energy draws a bigger flash");
            Assert.IsTrue(RingRadius(big, 0.8f) > 1.5f * RingRadius(small, 0.8f), "and a bigger ring");
            Assert.IsTrue(FlashRadius(small, 0.4f) > 0f, "even one fission draws a flash");

            FissionFrame zero = FrameAt(big, big.ScissionTime, 2.8f);
            FissionFrame zeroSmall = FrameAt(small, small.ScissionTime, 2.8f);
            Assert.IsTrue(zero.Wash > zeroSmall.Wash && zeroSmall.Wash > 0f, "the white wash scales with the energy too");
            Assert.IsTrue(zero.Wash > 0.6f, "a very large energy nearly whites out the stage, found " + zero.Wash);

            float previousRing = -1f;
            for (float tau = 0.05f; tau < 1.0f; tau += 0.05f)
            {
                float r = RingRadius(big, tau);
                Assert.IsTrue(r > previousRing, "the shock ring only grows, at " + tau);
                previousRing = r;
            }

            // The flash fades: its total opacity never rises after the first quarter second.
            float previous = float.MaxValue;
            for (float tau = 0.3f; tau < 1.8f; tau += 0.05f)
            {
                FissionFrame f = FrameAt(big, big.ScissionTime + tau, 2.8f);
                float alpha = 0f;
                foreach (StageBlob b in f.Blobs)
                {
                    if (b.Kind == StageKind.Glow && b.Tint == StageTint.Flash)
                    {
                        alpha = Math.Max(alpha, b.Alpha);
                    }
                }
                Assert.IsTrue(alpha <= previous + 1e-6f, "the flash alpha does not rise at " + tau);
                previous = alpha;
            }

            // At the end nothing of the flash is left and the picture holds still.
            FissionFrame end = FrameAt(big, big.Duration, 2.8f);
            Assert.AreEqual(0f, end.Wash, 0f);
            foreach (StageBlob b in end.Blobs)
            {
                Assert.IsTrue(b.Tint != StageTint.Flash && b.Kind != StageKind.Ring, "no flash and no ring at the end");
            }
            FissionFrame again = FrameAt(big, big.Duration, 2.8f);
            Assert.AreEqual(end.Blobs.Count, again.Blobs.Count);
        }

        [Test]
        public void NeutronsArriveThenLeaveInTheirOwnDirections()
        {
            FissionInfo info = Info("U-235", 0.0253);
            for (ulong seed = 1UL; seed <= 20UL; seed++)
            {
                FissionShow show = Show(info, seed, 0.0, 1UL);
                FissionShowPlan plan = show.Plan;

                // Before the absorption there is one neutron, coming from the left, and it moves towards the nucleus.
                FissionFrame start = FrameAt(show, 0.01f, 2.8f);
                FissionFrame nearer = FrameAt(show, 0.9f, 2.8f);
                StageBlob first = Disc(start, StageTint.Neutron);
                StageBlob second = Disc(nearer, StageTint.Neutron);
                Assert.IsTrue(first.X < -2.0f, "the neutron starts near the left edge, found " + first.X);
                Assert.IsTrue(second.X > first.X && second.X < 0f, "and moves towards the nucleus");
                Assert.AreEqual(1, CountDiscs(start, StageTint.Neutron, 1f));

                // After the split there is one disc for every neutron of the channel, up to the cap.
                FissionFrame end = FrameAt(show, show.Duration, 2.8f);
                Assert.AreEqual(Math.Min(plan.Neutrons, FissionShowPlan.MaxNeutronsShown), CountDiscs(end, StageTint.Neutron, 1f));
                int labels = 0;
                foreach (StageLabel l in end.Labels)
                {
                    if (l.Text == "n")
                    {
                        labels++;
                        Assert.AreEqual(1f, l.Alpha, 0f);
                    }
                }
                Assert.AreEqual(plan.NeutronAngles.Length, labels);

                // A third of a second after the split every neutron is clear of the place the nucleus was.
                FissionFrame soon = FrameAt(show, show.ScissionTime + 0.3f, 2.8f);
                foreach (StageBlob b in soon.Blobs)
                {
                    if (b.Kind == StageKind.Disc && b.Tint == StageTint.Neutron)
                    {
                        Assert.IsTrue(Math.Sqrt(b.X * b.X + b.Y * b.Y) > 0.15f, "neutron at " + b.X + ", " + b.Y);
                    }
                }
            }

            // Spontaneous fission has no incoming neutron at any time.
            FissionShow alone = Show(Info("Cf-252", 0.0), 2UL, 0.0, 1UL);
            for (float t = 0f; t < alone.ScissionTime; t += 0.05f)
            {
                Assert.AreEqual(0, CountDiscs(FrameAt(alone, t, 2.8f), StageTint.Neutron, 1f), "no neutron before the split at " + t);
            }
        }

        [Test]
        public void EnergiesInCaptionsAreTheLibrarysNumbers()
        {
            FissionInfo info = Info("U-235", 0.0253);
            FissionShowPlan plan = FissionShowPlan.Build(info, new NuclearLabRandom(4UL), 0.0, 1UL);
            string done = plan.Captions[(int)FissionPhase.Done];
            Assert.IsTrue(done.Contains(NuclearLabFormat.Energy(plan.EventKev)), done);
            Assert.IsTrue(done.Contains(NuclearLabFormat.Joules(plan.EventKev)), done);
            Assert.IsTrue(done.Contains("TNT"), done);
            Assert.IsTrue(plan.Captions[(int)FissionPhase.Split].Contains(plan.Equation));
            Assert.IsTrue(plan.Captions[(int)FissionPhase.Approach].Contains("U-235"));
            Assert.IsTrue(plan.Captions[(int)FissionPhase.Absorb].Contains("U-236"));
            Assert.IsTrue(plan.Captions[(int)FissionPhase.Aftermath].Contains(plan.Light.ToString()));

            FissionInfo spontaneous = Info("Cf-252", 0.0);
            FissionShowPlan alone = FissionShowPlan.Build(spontaneous, new NuclearLabRandom(4UL), 0.0, 1UL);
            Assert.IsFalse(alone.Induced);
            Assert.IsFalse(alone.Equation.StartsWith("n + "));
            Assert.IsTrue(alone.Captions[(int)FissionPhase.Absorb].Contains("Cf-252"));
        }
    }
}
