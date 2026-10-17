// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Tests~/DotnetHarness/NuclearLabModelTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.Nuclear.Samples.NuclearLab.Tests
{
    /// <summary>
    /// Checks the model half of the Nuclear Lab sample (everything except the
    /// OnGUI view) against the real native library. Runs only in the .NET
    /// harness, because the sample lives in Samples~ and Unity never compiles it here.
    /// </summary>
    [TestFixture]
    public class NuclearLabModelTests
    {
        private static Nuclide N(string text)
        {
            return Nuclide.Parse(text);
        }

        private static double AmountOf(PileRow[] rows, Nuclide nuclide)
        {
            foreach (PileRow row in rows)
            {
                if (row.Nuclide == nuclide)
                {
                    return row.Amount;
                }
            }
            return 0.0;
        }

        [Test]
        public void NumbersAndDurationsReadWell()
        {
            Assert.AreEqual("0", NuclearLabFormat.Number(0.0));
            Assert.AreEqual("5.27", NuclearLabFormat.Number(5.27));
            Assert.AreEqual("1,235", NuclearLabFormat.Number(1234.5));
            Assert.AreEqual("-1,234", NuclearLabFormat.Number(-1234.0));
            Assert.AreEqual("0.5", NuclearLabFormat.Number(0.5));
            Assert.AreEqual("1e+6", NuclearLabFormat.Number(1.0e6));
            Assert.AreEqual("2.222e+16", NuclearLabFormat.Number(2.2216e16));
            Assert.AreEqual("1.2e-5", NuclearLabFormat.Number(1.2e-5));
            Assert.AreEqual("NaN", NuclearLabFormat.Number(double.NaN));
            Assert.AreEqual("inf", NuclearLabFormat.Number(double.PositiveInfinity));

            Assert.AreEqual("0 s", NuclearLabFormat.Duration(0.0));
            Assert.AreEqual("30 s", NuclearLabFormat.Duration(30.0));
            Assert.AreEqual("1.5 min", NuclearLabFormat.Duration(90.0));
            Assert.AreEqual("1 h", NuclearLabFormat.Duration(3600.0));
            Assert.AreEqual("1 d", NuclearLabFormat.Duration(86400.0));
            Assert.AreEqual("5.27 y", NuclearLabFormat.Duration(5.27 * NuclearLabFormat.SecondsPerYear));
            Assert.AreEqual("24,110 y", NuclearLabFormat.Duration(24110.0 * NuclearLabFormat.SecondsPerYear));
            Assert.AreEqual("7.04e+8 y", NuclearLabFormat.Duration(7.04e8 * NuclearLabFormat.SecondsPerYear));
            Assert.AreEqual("2 ms", NuclearLabFormat.Duration(0.002));
            Assert.AreEqual("25 us", NuclearLabFormat.Duration(2.5e-5));
            Assert.AreEqual("500 ns", NuclearLabFormat.Duration(5.0e-7));
            Assert.AreEqual("3.2e-15 s", NuclearLabFormat.Duration(3.2e-15));

            Assert.AreEqual(1.0, NuclearLabFormat.UnitSeconds(0), 0.0);
            Assert.AreEqual(NuclearLabFormat.SecondsPerYear, NuclearLabFormat.UnitSeconds(4), 0.0);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { NuclearLabFormat.UnitSeconds(5); });
            Assert.AreEqual(NuclearLabFormat.StepUnits.Length, 5);
        }

        [Test]
        public void EnergiesCountsAndLabelsReadWell()
        {
            Assert.AreEqual("0 keV", NuclearLabFormat.Energy(0.0));
            Assert.AreEqual("500 eV", NuclearLabFormat.Energy(0.5));
            Assert.AreEqual("511 keV", NuclearLabFormat.Energy(511.0));
            Assert.AreEqual("17.59 MeV", NuclearLabFormat.Energy(17589.3));
            Assert.AreEqual("1.602e-13 J", NuclearLabFormat.Joules(1000.0));
            Assert.AreEqual("1.16e+7 K", NuclearLabFormat.Kelvin(1.0));
            Assert.AreEqual("99.27 %", NuclearLabFormat.Percent(99.27));
            Assert.AreEqual("0 %", NuclearLabFormat.Percent(0.0));
            Assert.AreEqual("1,000,000", NuclearLabFormat.Count(1000000UL));
            Assert.AreEqual("1,000,000", NuclearLabFormat.Amount(1000000.0));
            Assert.AreEqual("0.5", NuclearLabFormat.Amount(0.5));
            Assert.AreEqual("alpha", NuclearLabFormat.ModeName(DecayMode.Alpha));
            Assert.AreEqual("spontaneous fission", NuclearLabFormat.ModeName(DecayMode.SpontaneousFission));
            Assert.AreEqual("99", NuclearLabFormat.ModeName((DecayMode)99));
            Assert.AreEqual("stable", NuclearLabFormat.EndReasonText(ChainEndReason.Stable));
            Assert.AreEqual("measured", NuclearLabFormat.SourceText(EnergySource.Measured));
            Assert.AreEqual("Bosch-Hale fit", NuclearLabFormat.FitText(FitKind.BoschHale));
            Assert.AreEqual("stable", NuclearLabFormat.HalfLifeText(NuclearData.GetHalfLife(N("Fe-56"))));
            Assert.AreEqual("30.04 y", NuclearLabFormat.HalfLifeText(NuclearData.GetHalfLife(N("Cs-137"))));
        }

        [Test]
        public void TextIsParsedIntoNumbers()
        {
            ulong count;
            Assert.IsTrue(NuclearLabFormat.TryParseCount("1000000", out count));
            Assert.AreEqual(1000000UL, count);
            Assert.IsTrue(NuclearLabFormat.TryParseCount("1,000,000", out count));
            Assert.AreEqual(1000000UL, count);
            Assert.IsTrue(NuclearLabFormat.TryParseCount("1e6", out count));
            Assert.AreEqual(1000000UL, count);
            Assert.IsTrue(NuclearLabFormat.TryParseCount("2.5e3", out count));
            Assert.AreEqual(2500UL, count);
            Assert.IsTrue(NuclearLabFormat.TryParseCount("  42  ", out count));
            Assert.AreEqual(42UL, count);
            Assert.IsTrue(NuclearLabFormat.TryParseCount("18446744073709551615", out count));
            Assert.AreEqual(ulong.MaxValue, count);
            Assert.IsFalse(NuclearLabFormat.TryParseCount("-5", out count));
            Assert.IsFalse(NuclearLabFormat.TryParseCount("1.5", out count));
            Assert.IsFalse(NuclearLabFormat.TryParseCount("1e30", out count));
            Assert.IsFalse(NuclearLabFormat.TryParseCount("abc", out count));
            Assert.IsFalse(NuclearLabFormat.TryParseCount("", out count));
            Assert.IsFalse(NuclearLabFormat.TryParseCount(null, out count));

            double number;
            Assert.IsTrue(NuclearLabFormat.TryParseNumber("0.0253", out number));
            Assert.AreEqual(0.0253, number, 0.0);
            Assert.IsTrue(NuclearLabFormat.TryParseNumber("2e6", out number));
            Assert.AreEqual(2.0e6, number, 0.0);
            Assert.IsFalse(NuclearLabFormat.TryParseNumber("NaN", out number));
            Assert.IsFalse(NuclearLabFormat.TryParseNumber("Infinity", out number));
            Assert.IsFalse(NuclearLabFormat.TryParseNumber("1e999", out number));
            Assert.IsFalse(NuclearLabFormat.TryParseNumber("x", out number));

            Assert.AreEqual(0.5, NuclearLabFormat.BarFraction(5.0, 0.0, 10.0, false), 1e-12);
            Assert.AreEqual(1.0, NuclearLabFormat.BarFraction(20.0, 0.0, 10.0, false), 0.0);
            Assert.AreEqual(0.5, NuclearLabFormat.BarFraction(100.0, 1.0, 10000.0, true), 1e-12);
            Assert.AreEqual(0.0, NuclearLabFormat.BarFraction(0.0, 1.0, 10000.0, true), 0.0);
            Assert.AreEqual(0.0, NuclearLabFormat.BarFraction(5.0, 10.0, 10.0, false), 0.0);
        }

        [Test]
        public void BadInputNamesTheField()
        {
            Assert.AreEqual(N("U-235"), NuclearLabInput.ParseNuclide(" U-235 ", "Target"));
            FormatException bad = Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseNuclide("xyz", "Target"); });
            Assert.IsTrue(bad.Message.StartsWith("Target 'xyz'"), bad.Message);
            Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseNuclide("", "Target"); });
            Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseNuclide(null, "Target"); });

            Assert.AreEqual(5UL, NuclearLabInput.ParseCount("5", "Atoms", 1UL));
            FormatException small = Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseCount("0", "Atoms", 1UL); });
            Assert.IsTrue(small.Message.Contains("at least 1"), small.Message);
            Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseCount("1.5", "Atoms", 1UL); });

            Assert.AreEqual(0.0253, NuclearLabInput.ParseNumber("0.0253", "Energy", true), 0.0);
            FormatException zero = Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseNumber("0", "Energy", true); });
            Assert.IsTrue(zero.Message.Contains("greater than zero"), zero.Message);
            Assert.AreEqual(0.0, NuclearLabInput.ParseNumber("0", "Energy", false), 0.0);
            FormatException negative = Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseNumber("-1", "Energy", false); });
            Assert.IsTrue(negative.Message.Contains("must not be negative"), negative.Message);

            Assert.AreEqual(172800.0, NuclearLabInput.ParseStepSeconds("2", 3), 0.0);
            Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseStepSeconds("1e308", 4); });
            Assert.Throws<FormatException>(delegate { NuclearLabInput.ParseStepSeconds("-1", 0); });
        }

        [Test]
        public void NuclideReportsMatchTheTables()
        {
            NuclideReport cs = NuclideReport.Build(N("Cs-137"));
            Assert.IsTrue(cs.HasHalfLife);
            Assert.AreEqual(1, cs.Decays.Length);
            Assert.AreEqual(N("Ba-137"), cs.Decays[0].Outcome.Daughter);
            Assert.IsTrue(cs.Decays[0].HasQ);
            Assert.AreEqual(1175.6, cs.Decays[0].Q.Kev, 1.0);
            Assert.AreEqual(1, cs.ChainLines.Length);
            Assert.AreEqual("Ba-137: stable", cs.ChainEnd);
            Assert.AreEqual("beta- -> Ba-137", NuclideReport.DescribeOutcome(cs.Decays[0].Outcome));

            NuclideReport u238 = NuclideReport.Build(N("U-238"));
            Assert.AreEqual(14, u238.ChainLines.Length);
            Assert.IsTrue(u238.ChainLines[0].StartsWith("U-238 -> Th-234"), u238.ChainLines[0]);
            Assert.AreEqual("Pb-206: stable", u238.ChainEnd);
            Assert.AreEqual(99.27, u238.AbundancePercent, 0.01);

            NuclideReport u235 = NuclideReport.Build(N("U-235"));
            bool sawFissionWithoutEnergy = false;
            foreach (DecayLine line in u235.Decays)
            {
                if (line.Outcome.Mode == DecayMode.SpontaneousFission)
                {
                    sawFissionWithoutEnergy = !line.HasQ;
                }
            }
            Assert.IsTrue(sawFissionWithoutEnergy, "spontaneous fission has no decay energy in the library");

            NuclideReport fe = NuclideReport.Build(N("Fe-56"));
            Assert.AreEqual(0, fe.Decays.Length);
            Assert.AreEqual(0, fe.ChainLines.Length);
            Assert.AreEqual("Fe-56: stable", fe.ChainEnd);
            Assert.AreEqual(8790.0, fe.Binding.PerNucleonKev, 1.0);

            NuclideReport neutron = NuclideReport.Build(Nuclide.Neutron);
            Assert.AreEqual(Nuclide.Neutron, neutron.Nuclide);
        }

        [Test]
        public void DecayLabConservesNucleonsInBothKinds()
        {
            foreach (PileKind kind in new[] { PileKind.Atoms, PileKind.Amounts })
            {
                using (DecayLab lab = new DecayLab())
                {
                    Assert.IsFalse(lab.HasPile);
                    Assert.Throws<InvalidOperationException>(delegate { lab.Step(1.0); });
                    lab.Reset(N("Sr-90"), 1000000UL, kind, 7UL);
                    Assert.IsTrue(lab.HasPile);
                    double halfLife;
                    Assert.IsTrue(lab.TryGetStartHalfLife(out halfLife));
                    Assert.AreEqual(28.91, halfLife / NuclearLabFormat.SecondsPerYear, 0.05);
                    lab.Step(halfLife);

                    PileRow[] rows = lab.Rows();
                    double tolerance = kind == PileKind.Atoms ? 5000.0 : 1.0;
                    Assert.AreEqual(500000.0, AmountOf(rows, N("Sr-90")), tolerance);
                    // Y-90 (64 hours) is still in transit on its way to stable Zr-90.
                    Assert.Greater(AmountOf(rows, N("Y-90")), 0.0);
                    Assert.AreEqual(500000.0, AmountOf(rows, N("Zr-90")) + AmountOf(rows, N("Y-90")), tolerance);
                    Assert.AreEqual(lab.StartBaryons, lab.Baryons, 1e-6);
                    Assert.AreEqual(1000000.0, lab.AtomTotal, 1e-6);
                    Assert.AreEqual(halfLife, lab.ElapsedSeconds, 1e-6);
                    Assert.IsTrue(lab.EnergyKnown);
                    Assert.Greater(lab.EnergyReleasedKev, 0.0);
                    Assert.Greater(lab.LastChanged, 0.0);
                    Assert.AreEqual(0, lab.Pending().Length);
                    for (int i = 1; i < rows.Length; i++)
                    {
                        Assert.IsTrue(rows[i - 1].Amount >= rows[i].Amount, "rows are sorted by amount");
                    }
                    Assert.AreEqual(rows[0].Amount / 1000000.0, rows[0].PerStartAtom, 1e-12);
                }
            }
        }

        [Test]
        public void DecayLabSameSeedGivesTheSamePileAndErrorsKeepTheOldOne()
        {
            using (DecayLab first = new DecayLab())
            using (DecayLab second = new DecayLab())
            {
                first.Reset(N("I-131"), 500000UL, PileKind.Atoms, 11UL);
                second.Reset(N("I-131"), 500000UL, PileKind.Atoms, 11UL);
                first.Step(3.0 * 86400.0);
                second.Step(3.0 * 86400.0);
                PileRow[] a = first.Rows();
                PileRow[] b = second.Rows();
                Assert.AreEqual(a.Length, b.Length);
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.AreEqual(a[i].Nuclide, b[i].Nuclide);
                    Assert.AreEqual(a[i].Amount, b[i].Amount);
                }

                Assert.Throws<ArgumentOutOfRangeException>(delegate { first.Reset(N("I-131"), 0UL, PileKind.Atoms, 1UL); });
                Assert.IsTrue(first.HasPile);
                Assert.AreEqual(N("I-131"), first.Start);
                Assert.AreEqual(a.Length, first.Rows().Length);
                NuclearException negative = Assert.Throws<NuclearException>(delegate { first.Step(-1.0); });
                Assert.AreEqual(NuclearStatus.Invalid, negative.Status);
                double none;
                first.Reset(N("Fe-56"), 10UL, PileKind.Atoms, 1UL);
                Assert.IsFalse(first.TryGetStartHalfLife(out none));
            }
        }

        [Test]
        public void DecayLabResolvesPendingFissions()
        {
            foreach (PileKind kind in new[] { PileKind.Atoms, PileKind.Amounts })
            {
                using (DecayLab lab = new DecayLab())
                {
                    lab.Reset(N("Cf-252"), 2000000UL, kind, 5UL);
                    lab.Step(3.0 * NuclearLabFormat.SecondsPerYear);
                    Assert.Greater(lab.LastNewPending, 0.0);
                    PileRow[] pending = lab.Pending();
                    Assert.IsTrue(pending.Length > 0);
                    double waiting = 0.0;
                    foreach (PileRow row in pending)
                    {
                        waiting += row.Amount;
                    }
                    Assert.AreEqual(lab.StartBaryons, lab.Baryons, 1e-6);
                    lab.ResolveFissions();
                    Assert.AreEqual(waiting, lab.ResolvedFissions, 1e-6);
                    Assert.Greater(lab.NeutronsFromFission, waiting);
                    Assert.AreEqual(0, lab.Pending().Length);
                    Assert.AreEqual(lab.StartBaryons, lab.Baryons, 1e-6);
                    Assert.Greater(AmountOf(lab.Rows(), Nuclide.Neutron), 0.0);
                }
            }
        }

        [Test]
        public void FissionQueriesAreSortedAndConsistent()
        {
            using (FissionLab lab = new FissionLab(8, 10))
            {
                FissionInfo u235 = lab.Query(N("U-235"), 0.0253);
                Assert.AreEqual(N("U-236"), u235.Summary.Compound);
                Assert.AreEqual(8, u235.TopChannels.Length);
                Assert.AreEqual(10, u235.TopYields.Length);
                for (int i = 1; i < u235.TopChannels.Length; i++)
                {
                    Assert.IsTrue(u235.TopChannels[i - 1].Probability >= u235.TopChannels[i].Probability, "channels are sorted");
                }
                for (int i = 1; i < u235.TopYields.Length; i++)
                {
                    Assert.IsTrue(u235.TopYields[i - 1].Amount >= u235.TopYields[i].Amount, "yields are sorted");
                }
                Assert.AreEqual(575, u235.ProductCount);
                double total = 0.0;
                foreach (double y in u235.MassYields)
                {
                    total += y;
                }
                Assert.AreEqual(2.0, total, 1e-3);
                Assert.Greater(u235.MassYields.Length, 50);
                Assert.IsTrue(u235.MassMin > 50 && u235.MassMin < 100);
                Assert.IsTrue(u235.HasBranching);
                Assert.AreEqual(0.855, u235.FissionProbability, 1e-9);
                Assert.IsTrue(u235.HasCaptureQ);
                Assert.Greater(u235.CaptureQKev, 0.0);

                FissionInfo cf = lab.Query(N("Cf-252"), 0.0);
                Assert.IsFalse(cf.HasBranching);
                Assert.IsFalse(cf.HasCaptureQ);
                Assert.Greater(cf.Summary.MeanNeutrons, 3.0);

                NuclearException notFissionable = Assert.Throws<NuclearException>(delegate { lab.Query(N("Fe-56"), 0.0); });
                Assert.AreEqual(NuclearStatus.NotFissionable, notFissionable.Status);
                Assert.Throws<NuclearException>(delegate { lab.Query(N("U-235"), -1.0); });
                Assert.Throws<ArgumentOutOfRangeException>(delegate { new FissionLab(0, 1); });
            }
            FissionLab disposed = new FissionLab(1, 1);
            disposed.Dispose();
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(delegate { disposed.Query(N("U-235"), 0.0253); });
        }

        [Test]
        public void FissionRunConservesNucleons()
        {
            using (FissionLab lab = new FissionLab(8, 10))
            {
                FissionRun run = lab.Run(N("U-235"), 100000UL, 50000UL, 0.0253, null, 9UL);
                Assert.AreEqual(50000UL, run.Report.Fissions + run.Report.Captures);
                Assert.Greater(run.Report.NeutronsReleased, 100000UL);
                Assert.AreEqual(100000.0 * 235.0 + 50000.0, run.StartBaryons, 0.0);
                Assert.AreEqual(run.StartBaryons, (double)run.Totals.Baryons, 0.0);
                Assert.IsTrue(run.Products.Length > 50);
                for (int i = 1; i < run.Products.Length; i++)
                {
                    Assert.IsTrue(run.Products[i - 1].Count >= run.Products[i].Count, "products are sorted");
                }

                FissionRun all = lab.Run(N("U-235"), 1000UL, 1000UL, 0.0253, 1.0, 9UL);
                Assert.AreEqual(1000UL, all.Report.Fissions);
                Assert.AreEqual(0UL, all.Report.Captures);

                NuclearException notInTable = Assert.Throws<NuclearException>(delegate
                {
                    lab.Run(N("Fe-56"), 10UL, 1UL, 0.0253, null, 1UL);
                });
                Assert.AreEqual(NuclearStatus.NotInTable, notInTable.Status);
                NuclearException tooMany = Assert.Throws<NuclearException>(delegate
                {
                    lab.Run(N("U-235"), 10UL, 20UL, 0.0253, null, 1UL);
                });
                Assert.AreEqual(NuclearStatus.NotEnough, tooMany.Status);
            }
        }

        [Test]
        public void FusionQueriesAndCurves()
        {
            FusionRow[] rows = FusionLab.AllChannels();
            Assert.AreEqual(7, rows.Length);
            Assert.AreEqual("D + T -> He-4 + n", rows[4].Name);
            Assert.AreEqual(17589.3, rows[4].QKev, 0.1);
            Assert.AreEqual(FitKind.BoschHale, rows[4].Fit);
            Assert.AreEqual(100.0, rows[4].FitHighKev, 0.0);

            FusionPair dt = FusionLab.QueryPair(Nuclide.H2, Nuclide.H3, 10.0);
            Assert.AreEqual(1, dt.Channels.Length);
            Assert.AreEqual(1.0, dt.Channels[0].Share, 1e-12);
            Assert.AreEqual(1.136e-16, dt.Channels[0].Reactivity, 1e-19);
            Assert.IsTrue(dt.Channels[0].InsideFit);
            Assert.IsFalse(FusionLab.QueryPair(Nuclide.H2, Nuclide.H3, 500.0).Channels[0].InsideFit);
            Assert.IsFalse(FusionLab.QueryPair(Nuclide.H2, Nuclide.H3, 0.05).Channels[0].InsideFit);

            FusionPair dd = FusionLab.QueryPair(Nuclide.H2, Nuclide.H2, 10.0);
            Assert.AreEqual(2, dd.Channels.Length);
            Assert.AreEqual(1.0, dd.Channels[0].Share + dd.Channels[1].Share, 1e-9);
            Assert.AreEqual(0, FusionLab.QueryPair(Nuclide.H3, Nuclide.H3, 10.0).Channels.Length);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { FusionLab.QueryPair(Nuclide.H2, Nuclide.H3, 0.0); });

            double[] temperatures = FusionLab.Temperatures(0.1, 1000.0, 41);
            Assert.AreEqual(41, temperatures.Length);
            Assert.AreEqual(0.1, temperatures[0], 0.0);
            Assert.AreEqual(1000.0, temperatures[40], 0.0);
            for (int i = 1; i < temperatures.Length; i++)
            {
                Assert.AreEqual(Math.Pow(10.0, 0.1), temperatures[i] / temperatures[i - 1], 1e-9);
            }
            Assert.Throws<ArgumentOutOfRangeException>(delegate { FusionLab.Temperatures(0.0, 10.0, 5); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { FusionLab.Temperatures(10.0, 1.0, 5); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { FusionLab.Temperatures(1.0, 10.0, 1); });

            double[] curve = FusionLab.ReactivityCurve(4, temperatures);
            int peak = 0;
            for (int i = 1; i < curve.Length; i++)
            {
                if (curve[i] > curve[peak])
                {
                    peak = i;
                }
            }
            Assert.IsTrue(temperatures[peak] > 30.0 && temperatures[peak] < 130.0, "D-T peaks near 64 keV, found " + temperatures[peak]);
        }

        [Test]
        public void FusionRunConservesNucleons()
        {
            FusionRun dt = FusionLab.Run(Nuclide.H2, 1000000UL, Nuclide.H3, 1000000UL, 300000UL, 10.0, 3UL);
            Assert.AreEqual(300000UL, dt.Report.Fusions);
            Assert.AreEqual(300000UL, dt.Report.NeutronsReleased);
            Assert.AreEqual(17589.3, dt.Report.EnergyReleasedKev / 300000.0, 1.0);
            Assert.AreEqual(1000000.0 * 2.0 + 1000000.0 * 3.0, dt.StartBaryons, 0.0);
            Assert.AreEqual(dt.StartBaryons, (double)dt.Totals.Baryons, 0.0);
            Assert.IsTrue(dt.Products.Length >= 3);

            FusionRun dd = FusionLab.Run(Nuclide.H2, 1000000UL, Nuclide.H2, 777UL, 400000UL, 10.0, 3UL);
            Assert.AreEqual(400000UL, dd.Report.Fusions);
            Assert.AreEqual(2000000.0, dd.StartBaryons, 0.0);
            Assert.AreEqual(dd.StartBaryons, (double)dd.Totals.Baryons, 0.0);

            NuclearException noReaction = Assert.Throws<NuclearException>(delegate
            {
                FusionLab.Run(Nuclide.H3, 10UL, Nuclide.H3, 0UL, 1UL, 10.0, 1UL);
            });
            Assert.AreEqual(NuclearStatus.NoReaction, noReaction.Status);
            NuclearException tooMany = Assert.Throws<NuclearException>(delegate
            {
                FusionLab.Run(Nuclide.H2, 10UL, Nuclide.H3, 10UL, 100UL, 10.0, 1UL);
            });
            Assert.AreEqual(NuclearStatus.NotEnough, tooMany.Status);
        }

        [Test]
        public void QueueRunsInOrderAndDefersWhatRunningAdds()
        {
            NuclearLabQueue queue = new NuclearLabQueue();
            string order = string.Empty;
            queue.Add(delegate
            {
                order += "a";
                queue.Add(delegate { order += "c"; });
            });
            queue.Add(delegate { order += "b"; });
            Assert.AreEqual(2, queue.Count);
            queue.RunAll();
            Assert.AreEqual("ab", order);
            Assert.AreEqual(1, queue.Count);
            queue.RunAll();
            Assert.AreEqual("abc", order);
            Assert.AreEqual(0, queue.Count);
            queue.RunAll();
            Assert.Throws<ArgumentNullException>(delegate { queue.Add(null); });

            int runs = 0;
            queue.Add(delegate
            {
                runs++;
                throw new InvalidOperationException("boom");
            });
            Assert.Throws<InvalidOperationException>(delegate { queue.RunAll(); });
            queue.RunAll();
            Assert.AreEqual(1, runs);
        }

        [Test]
        public void DisposedLabsAreRefused()
        {
            DecayLab lab = new DecayLab();
            lab.Reset(N("C-14"), 100UL, PileKind.Atoms, 1UL);
            lab.Dispose();
            lab.Dispose();
            Assert.IsFalse(lab.HasPile);
            Assert.Throws<InvalidOperationException>(delegate { lab.Rows(); });
        }
    }
}
