// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Tests/Editor/NuclearNativeTests.cs"
// ============================================================================
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace MidManStudio.Nuclear.EditorTests
{
    /// <summary>
    /// Checks the C# bindings against the real native library: the interface
    /// version, the layout of every struct field by field, and the data and
    /// pile calls. The same file runs under plain .NET in CI (see
    /// Tests~/DotnetHarness), so it must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public class NuclearNativeTests
    {
        private static readonly Type[] StructKinds =
        {
            typeof(NuclideCount), typeof(NuclideAmount), typeof(EnergyValue), typeof(BindingValue),
            typeof(HalfLife), typeof(PileTotals), typeof(StepReport), typeof(AmountsStepReport),
            typeof(ReactionReport), typeof(AmountsReactionReport), typeof(DecayOutcome), typeof(ChainStep),
            typeof(ChainEnd), typeof(FissionSummary), typeof(FissionChannel), typeof(FusionChannel),
        };

        private static Nuclide N(string text)
        {
            return Nuclide.Parse(text);
        }

        [Test]
        public void LibraryVersionAndInterfaceAgree()
        {
            NuclearLibrary.Initialize();
            Assert.AreEqual(NuclearLibrary.ExpectedAbiVersion, NuclearNative.nuc_abi_version());
            Assert.IsTrue(NuclearLibrary.Version.Length > 0);
        }

        [Test]
        public void StructLayoutsMatchNativeFieldByField()
        {
            NuclearLibrary.Initialize();
            for (int i = 0; i < StructKinds.Length; i++)
            {
                int kind = i + 1;
                Type type = StructKinds[i];
                int size = NuclearNative.nuc_struct_size(kind);
                Assert.AreEqual(Marshal.SizeOf(type), size, type.Name + " size");
                byte[] bytes = new byte[size];
                Assert.AreEqual(size, NuclearNative.nuc_layout_probe(kind, bytes, size), type.Name + " probe");
                object value;
                GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    value = Marshal.PtrToStructure(pin.AddrOfPinnedObject(), type);
                }
                finally
                {
                    pin.Free();
                }
                int next = 1;
                CheckFields(value, ref next, type.Name);
            }
        }

        // The native probe fills the named fields in declaration order with 1, 2, 3
        // and so on, floating point fields as k + 0.25. Reading the fields back by
        // their C# offsets proves every offset, not only the total size.
        private static void CheckFields(object value, ref int next, string path)
        {
            FieldInfo[] fields = value.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => OffsetOf(a).CompareTo(OffsetOf(b)));
            foreach (FieldInfo field in fields)
            {
                object inner = field.GetValue(value);
                string name = path + "." + field.Name;
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive)
                {
                    CheckFields(inner, ref next, name);
                    continue;
                }
                double expected = field.FieldType == typeof(double) ? next + 0.25 : next;
                Assert.AreEqual(expected, Convert.ToDouble(inner), 0.0, name);
                next++;
            }
        }

        private static int OffsetOf(FieldInfo field)
        {
            object[] attributes = field.GetCustomAttributes(typeof(FieldOffsetAttribute), false);
            return ((FieldOffsetAttribute)attributes[0]).Value;
        }

        [Test]
        public void NuclidesParseAndPrint()
        {
            Nuclide u = N("U-235");
            Assert.AreEqual(92, u.Z);
            Assert.AreEqual(143, u.N);
            Assert.AreEqual(235, u.A);
            Assert.AreEqual("U-235", u.ToString());
            Assert.AreEqual("U", u.Symbol);
            Assert.AreEqual(Nuclide.Neutron, N("n"));
            Assert.AreEqual(u, Nuclide.FromKey(u.Key));
            Assert.IsTrue(Nuclide.H1 < Nuclide.He4);
            Nuclide none;
            Assert.IsFalse(Nuclide.TryParse("Xx-9", out none));
            Assert.Throws<FormatException>(delegate { Nuclide.Parse("not a nuclide"); });
            Assert.AreEqual(3558, NuclearData.NuclideCount);
            Assert.AreEqual(3558, NuclearData.AllNuclides().Length);
            Assert.AreEqual("Fe", NuclearData.ElementSymbol(26));
        }

        [Test]
        public void MassesBindingAndReactionEnergies()
        {
            BindingValue fe = NuclearData.GetBindingEnergy(N("Fe-56"));
            Assert.AreEqual(8790.0, fe.PerNucleonKev, 5.0);
            Assert.AreEqual(EnergySource.Measured, fe.Source);
            EnergyValue dt = NuclearData.GetQValue(
                new[] { Nuclide.H2, Nuclide.H3 }, new[] { Nuclide.He4, Nuclide.Neutron });
            Assert.AreEqual(17589.0, dt.Kev, 5.0);
            Assert.Throws<NuclearException>(delegate
            {
                NuclearData.GetQValue(new[] { Nuclide.H2, Nuclide.H3 }, new[] { Nuclide.He4 });
            });
        }

        [Test]
        public void DecayData()
        {
            HalfLife cs = NuclearData.GetHalfLife(N("Cs-137"));
            Assert.AreEqual(HalfLifeState.Exact, cs.State);
            Assert.IsTrue(cs.HasNumber);
            Assert.Greater(cs.Seconds, 9.4e8);
            Assert.Less(cs.Seconds, 9.6e8);
            HalfLife none;
            Assert.IsFalse(NuclearData.TryGetHalfLife(new Nuclide(200, 200), out none));

            DecayOutcome[] outcomes = NuclearData.GetDecayOutcomes(N("Cs-137"));
            double sum = 0;
            foreach (DecayOutcome o in outcomes)
            {
                sum += o.Percent;
            }
            Assert.AreEqual(100.0, sum, 0.01);
            Assert.AreEqual(DecayMode.BetaMinus, outcomes[0].Mode);
            Assert.AreEqual(N("Ba-137"), outcomes[0].Daughter);
            Assert.AreEqual(0, NuclearData.GetDecayOutcomes(N("Fe-56")).Length);

            DecayChain chain = NuclearData.GetChainToStability(N("U-238"));
            Assert.AreEqual(14, chain.Steps.Length);
            Assert.AreEqual(N("Pb-206"), chain.End.Nuclide);
            Assert.AreEqual(ChainEndReason.Stable, chain.End.Reason);
            Assert.AreEqual(N("U-238"), chain.Steps[0].Parent);

            EnergyValue alpha = NuclearData.GetDecayQValue(N("U-238"), DecayMode.Alpha);
            Assert.AreEqual(4270.0, alpha.Kev, 10.0);
            Assert.AreEqual(99.27, NuclearData.GetAbundancePercent(N("U-238")), 0.01);
        }

        [Test]
        public void NeutronBranchingAndCapture()
        {
            double p;
            Assert.IsTrue(NuclearData.TryGetNeutronBranching(N("U-235"), 0.0253, out p));
            Assert.AreEqual(0.855, p, 1e-9);
            Assert.IsTrue(NuclearData.TryGetNeutronBranching(N("U-238"), 0.0253, out p));
            Assert.AreEqual(0.0, p, 0.0);
            Assert.IsFalse(NuclearData.TryGetNeutronBranching(N("Fe-56"), 0.0253, out p));
            Assert.AreEqual(8, NuclearData.GetNeutronTable().Length);
            Assert.AreEqual(4806.0, NuclearData.GetCaptureQKev(N("U-238")), 10.0);
            Assert.AreEqual(N("Cm-240"), NuclearData.GetFissioningNucleus(N("Bk-240")));
        }

        [Test]
        public void FissionQueries()
        {
            using (NuclearContext context = new NuclearContext(1))
            {
                FissionSummary s = context.GetFissionSummary(N("U-235"), 0.0253);
                Assert.AreEqual(N("U-236"), s.Compound);
                Assert.AreEqual(N("U-235"), s.Reference);
                Assert.AreEqual(YieldSource.Evaluated, s.Source);
                Assert.Greater(s.MeanNeutrons, 2.3);
                Assert.Less(s.MeanNeutrons, 2.7);
                FissionChannel[] channels = context.GetFissionChannels(N("U-235"), 0.0253);
                Assert.AreEqual(s.ChannelCount, channels.Length);
                double sum = 0;
                foreach (FissionChannel c in channels)
                {
                    sum += c.Probability;
                    Assert.AreEqual(92, c.Light.Z + c.Heavy.Z);
                    Assert.AreEqual(236, c.Light.A + c.Heavy.A + c.Neutrons);
                }
                Assert.AreEqual(1.0, sum, 1e-9);
                double yields = 0;
                foreach (NuclideAmount y in context.GetFissionYields(N("U-235"), 0.0253))
                {
                    yields += y.Amount;
                }
                Assert.AreEqual(2.0, yields, 1e-9);
                Assert.AreEqual(YieldSource.Extended, context.GetFissionSummary(N("Cf-254")).Source);
                Assert.AreEqual(2, context.CachedFissionTables);
                context.ClearCaches();
                Assert.AreEqual(0, context.CachedFissionTables);
                NuclearException e = Assert.Throws<NuclearException>(delegate { context.GetFissionSummary(N("Fe-56")); });
                Assert.AreEqual(NuclearStatus.NotFissionable, e.Status);
            }
        }

        [Test]
        public void FusionQueries()
        {
            Assert.AreEqual(7, NuclearFusion.ReactionCount);
            int[] dt = NuclearFusion.GetChannelsFor(Nuclide.H3, Nuclide.H2);
            Assert.AreEqual(1, dt.Length);
            FusionChannel c = NuclearFusion.GetReaction(dt[0]);
            Assert.AreEqual(FitKind.BoschHale, c.Fit);
            Assert.AreEqual(0.2, c.FitLowKev, 1e-12);
            Assert.AreEqual(100.0, c.FitHighKev, 1e-12);
            Assert.AreEqual(2, c.ProductLength);
            Assert.AreEqual(17589.0, c.QKev, 5.0);
            Assert.IsTrue(NuclearFusion.GetName(dt[0]).Contains("D + T"));
            Assert.AreEqual(1.1362e-16, NuclearFusion.GetReactivity(dt[0], 10.0), 1e-19);
            double[] dd = NuclearFusion.GetBranching(Nuclide.H2, Nuclide.H2, 10.0);
            Assert.AreEqual(2, dd.Length);
            Assert.AreEqual(1.0, dd[0] + dd[1], 1e-12);
            Assert.AreEqual(0, NuclearFusion.GetChannelsFor(Nuclide.H3, Nuclide.H3).Length);
            Assert.Throws<NuclearException>(delegate { NuclearFusion.GetReaction(99); });
        }

        private static ulong[] Run(ulong seed)
        {
            using (NuclearContext context = new NuclearContext(seed))
            using (NuclearSample sample = new NuclearSample())
            {
                sample.Add(N("C-14"), 1000000);
                sample.Add(N("I-131"), 50000);
                sample.Advance(context, 3.0e5);
                sample.Advance(context, 1.0e11);
                NuclideCount[] counts = sample.GetCounts();
                ulong[] flat = new ulong[counts.Length * 2];
                for (int i = 0; i < counts.Length; i++)
                {
                    flat[2 * i] = counts[i].Nuclide.Key;
                    flat[2 * i + 1] = counts[i].Count;
                }
                return flat;
            }
        }

        [Test]
        public void TheSameSeedGivesTheSamePile()
        {
            ulong[] a = Run(5);
            ulong[] b = Run(5);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i], b[i]);
            }
            Assert.AreNotEqual(string.Join(",", Run(6)), string.Join(",", a));
        }

        [Test]
        public void SavedGeneratorStateContinuesTheStream()
        {
            using (NuclearContext context = new NuclearContext(77))
            using (NuclearSample first = new NuclearSample())
            {
                first.Add(N("C-14"), 5000000);
                first.Advance(context, 1.0e11);
                ulong[] state = context.RngState;
                Assert.AreEqual(4, state.Length);
                using (NuclearSample second = first.Clone())
                {
                    first.Advance(context, 1.0e11);
                    context.RngState = state;
                    second.Advance(context, 1.0e11);
                    NuclideCount[] a = first.GetCounts();
                    NuclideCount[] b = second.GetCounts();
                    Assert.AreEqual(a.Length, b.Length);
                    for (int i = 0; i < a.Length; i++)
                    {
                        Assert.AreEqual(a[i].Count, b[i].Count);
                    }
                }
            }
        }

        [Test]
        public void ReactionsOnASample()
        {
            using (NuclearContext context = new NuclearContext(9))
            using (NuclearSample sample = new NuclearSample())
            {
                sample.Add(N("U-235"), 100000);
                sample.Add(Nuclide.H2, 40000);
                sample.Add(Nuclide.H3, 40000);
                ReactionReport r = sample.Irradiate(context, N("U-235"), 50000, 0.0253);
                Assert.AreEqual(50000UL, r.Fissions + r.Captures);
                Assert.Greater(r.NeutronsReleased, 100000UL);
                Assert.IsTrue(r.EnergyKnown);
                ReactionReport f = sample.Fuse(context, Nuclide.H2, Nuclide.H3, 30000, 10.0);
                Assert.AreEqual(30000UL, f.Fusions);
                Assert.AreEqual(30000UL, f.NeutronsReleased);
                Assert.AreEqual(30000UL, sample.Count(Nuclide.He4));

                NuclideCount[] before = sample.GetCounts();
                NuclearException tooMany = Assert.Throws<NuclearException>(delegate
                {
                    sample.Irradiate(context, N("U-235"), 10000000, 0.0253);
                });
                Assert.AreEqual(NuclearStatus.NotEnough, tooMany.Status);
                NuclearException noReaction = Assert.Throws<NuclearException>(delegate
                {
                    sample.Fuse(context, Nuclide.H3, Nuclide.H3, 1, 10.0);
                });
                Assert.AreEqual(NuclearStatus.NoReaction, noReaction.Status);
                Assert.Throws<NuclearException>(delegate { sample.Advance(context, -1.0); });
                Assert.Throws<NuclearException>(delegate { sample.Irradiate(context, N("Fe-56"), 1, 0.0253); });
                Assert.AreEqual(before.Length, sample.GetCounts().Length);

                using (NuclearSample waiting = new NuclearSample())
                {
                    waiting.Add(N("Cf-252"), 2000000);
                    waiting.Advance(context, 1.0e10);
                    PileTotals t = waiting.GetTotals();
                    Assert.IsTrue(waiting.GetPendingFissions().Length > 0);
                    ReactionReport resolved = waiting.ResolveFissions(context);
                    Assert.Greater(resolved.Fissions, 0UL);
                    Assert.AreEqual(t.Baryons, waiting.GetTotals().Baryons);
                    Assert.AreEqual(0, waiting.GetPendingFissions().Length);
                }
            }
        }

        [Test]
        public void AmountsGiveTheExpectedResult()
        {
            using (NuclearContext context = new NuclearContext(1))
            using (NuclearAmounts amounts = new NuclearAmounts())
            {
                amounts.Add(N("U-235"), 100.0);
                AmountsReactionReport r = amounts.Irradiate(context, N("U-235"), 60.0, 0.0253, 0.855);
                Assert.AreEqual(51.3, r.Fissions, 1e-9);
                Assert.AreEqual(40.0, amounts.Amount(N("U-235")), 1e-9);
                Assert.AreEqual(100.0 * 235.0 + 60.0, amounts.BaryonNumber, 1e-6);
                amounts.Advance(context, 1.0e5);
                Assert.Greater(amounts.GetAmounts().Length, 50);
                amounts.Add(Nuclide.H2, 2.0);
                amounts.Add(Nuclide.H3, 2.0);
                Assert.AreEqual(1.0, amounts.Fuse(Nuclide.H2, Nuclide.H3, 1.0, 10.0).Fusions, 0.0);
                Assert.Throws<NuclearException>(delegate { amounts.Add(Nuclide.H2, -1.0); });
                using (NuclearAmounts copy = amounts.Clone())
                {
                    Assert.AreEqual(amounts.Amount(Nuclide.He4), copy.Amount(Nuclide.He4), 0.0);
                }
            }
        }

        [Test]
        public void DisposedHandlesAreRefused()
        {
            NuclearContext context = new NuclearContext(1);
            NuclearSample sample = new NuclearSample();
            context.Dispose();
            sample.Dispose();
            context.Dispose();
            Assert.Throws<ObjectDisposedException>(delegate { context.Reseed(2); });
            Assert.Throws<ObjectDisposedException>(delegate { sample.Count(Nuclide.H1); });
        }
    }
}
