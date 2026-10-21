// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FissionShow.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// The clock and the picture of the fission animation. <see cref="Tick"/> moves the clock, and
    /// <see cref="Evaluate"/> turns the clock and a <see cref="FissionShowPlan"/> into a <see cref="FissionFrame"/>
    /// of blobs. It is slowed down enormously and not to scale in time, and the sizes follow the radius of a nucleus,
    /// which grows as the cube root of the mass number. The picture is event level like the rest of the lab: one
    /// split, with the lighter fragment moving faster because both fragments carry the same momentum.
    /// <para>
    /// Stage units: the stage is 2 units tall, y runs from -1 at the top to 1 at the bottom, and x runs from -aspect
    /// on the left to aspect on the right, where aspect is the width over the height. The nucleus starts at (0, 0).
    /// </para>
    /// Has no UnityEngine references. Not thread safe.
    /// </summary>
    public sealed class FissionShow
    {
        /// <summary>Seconds the neutron takes to reach the nucleus.</summary>
        public const float ApproachSeconds = 1.0f;
        /// <summary>Seconds of absorption after the neutron arrives.</summary>
        public const float AbsorbSeconds = 0.3f;
        /// <summary>Seconds a nucleus that fissions by itself waits before it starts to wobble.</summary>
        public const float RestSeconds = 0.5f;
        /// <summary>Seconds of wobbling before the nucleus splits.</summary>
        public const float WobbleSeconds = 1.8f;
        /// <summary>Seconds of the Split phase, after the split.</summary>
        public const float SplitSeconds = 0.5f;
        /// <summary>Seconds from the split until the show ends.</summary>
        public const float FlyOutSeconds = 2.5f;

        private const float Unit = 0.056f;
        private const float NeutronSeconds = 1.4f;
        private const float TwoPi = 6.2831855f;

        private FissionShowPlan plan;
        private bool playing;
        private float time;
        private float absorbStart;
        private float absorbEnd;
        private float scissionTime;
        private float duration;
        private float blastScale;
        private float compoundRadius;
        private float neutronRadius;
        private float lightRadius;
        private float heavyRadius;
        private float lightScission;
        private float heavyScission;
        private float lightShare;
        private float touch;
        private float axisX;
        private float axisY;
        private float incomingX;
        private float incomingY;

        /// <summary>True when a plan is loaded.</summary>
        public bool HasPlan
        {
            get { return plan != null; }
        }

        /// <summary>The plan that is loaded, or null.</summary>
        public FissionShowPlan Plan
        {
            get { return plan; }
        }

        /// <summary>True while the clock runs.</summary>
        public bool IsPlaying
        {
            get { return playing; }
        }

        /// <summary>True when a plan is loaded and has not started.</summary>
        public bool IsReady
        {
            get { return plan != null && !playing && time <= 0f; }
        }

        /// <summary>Seconds since the show started.</summary>
        public float Time
        {
            get { return time; }
        }

        /// <summary>Length of the show in seconds, or 0 with no plan.</summary>
        public float Duration
        {
            get { return duration; }
        }

        /// <summary>The second of the split. 0 with no plan.</summary>
        public float ScissionTime
        {
            get { return scissionTime; }
        }

        /// <summary>The phase the clock is in.</summary>
        public FissionPhase Phase
        {
            get { return PhaseAt(); }
        }

        /// <summary>The caption of the current phase, or a hint when no plan is loaded.</summary>
        public string Caption
        {
            get { return plan == null ? "Press Split one atom." : plan.Captions[(int)PhaseAt()]; }
        }

        /// <summary>Loads a plan and leaves the clock at 0, stopped. The stage then shows the nucleus and the neutron waiting.</summary>
        public void Prepare(FissionShowPlan next)
        {
            if (next == null)
            {
                throw new ArgumentNullException("next");
            }
            plan = next;
            playing = false;
            time = 0f;
            Layout();
        }

        /// <summary>Loads a plan and starts the clock.</summary>
        public void Start(FissionShowPlan next)
        {
            Prepare(next);
            playing = true;
        }

        /// <summary>Starts the loaded plan again from the beginning. Does nothing with no plan.</summary>
        public void Replay()
        {
            if (plan == null)
            {
                return;
            }
            time = 0f;
            playing = true;
        }

        /// <summary>Drops the plan. The stage shows a hint.</summary>
        public void Clear()
        {
            plan = null;
            playing = false;
            time = 0f;
            scissionTime = 0f;
            duration = 0f;
        }

        /// <summary>
        /// Moves the clock on by <paramref name="seconds"/> while it runs. A negative or non-finite step is ignored.
        /// The clock stops at the end of the show.
        /// </summary>
        public void Tick(float seconds)
        {
            if (!playing || !(seconds > 0f) || float.IsInfinity(seconds))
            {
                return;
            }
            time += seconds;
            if (time >= duration)
            {
                time = duration;
                playing = false;
            }
        }

        /// <summary>Puts the clock at <paramref name="seconds"/>, clamped to the length of the show, and stops it.</summary>
        public void Seek(float seconds)
        {
            if (plan == null)
            {
                return;
            }
            playing = false;
            time = seconds > 0f ? Clamp01(seconds / duration) * duration : 0f;
        }

        /// <summary>Fills <paramref name="frame"/> with the picture at the current time, for a stage <paramref name="aspect"/> times wider than tall.</summary>
        public void Evaluate(float aspect, FissionFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }
            frame.Clear();
            if (plan == null)
            {
                frame.Caption = Caption;
                return;
            }
            if (!(aspect >= 0.5f))
            {
                aspect = 0.5f;
            }
            FissionPhase phase = PhaseAt();
            frame.Phase = phase;
            frame.Caption = plan.Captions[(int)phase];
            if (time < scissionTime)
            {
                DrawBeforeSplit(aspect, frame);
            }
            else
            {
                DrawAfterSplit(aspect, frame);
            }
        }

        private FissionPhase PhaseAt()
        {
            if (plan == null || (!playing && time <= 0f))
            {
                return FissionPhase.Ready;
            }
            if (time >= duration)
            {
                return FissionPhase.Done;
            }
            if (time < absorbStart)
            {
                return FissionPhase.Approach;
            }
            if (time < absorbEnd)
            {
                return FissionPhase.Absorb;
            }
            if (time < scissionTime)
            {
                return FissionPhase.Wobble;
            }
            return time < scissionTime + SplitSeconds ? FissionPhase.Split : FissionPhase.Aftermath;
        }

        // Sizes, the timeline and the directions of one plan. Done once, when the plan is loaded.
        private void Layout()
        {
            absorbStart = plan.Induced ? ApproachSeconds : 0f;
            absorbEnd = absorbStart + (plan.Induced ? AbsorbSeconds : RestSeconds);
            scissionTime = absorbEnd + WobbleSeconds;
            duration = scissionTime + FlyOutSeconds;
            blastScale = (float)NuclearLabBlast.ScaleKev(plan.BlastKev);

            compoundRadius = Radius(Math.Max(1, plan.Compound.A));
            neutronRadius = Radius(1);
            lightRadius = Radius(Math.Max(1, plan.Light.A));
            heavyRadius = Radius(Math.Max(1, plan.Heavy.A));

            // The two fragments together fill the volume of the nucleus when they touch. Neutrons are boiled off later,
            // so the fragments are a little larger at the split than the nuclei they end as.
            float volume = lightRadius * lightRadius * lightRadius + heavyRadius * heavyRadius * heavyRadius;
            float fill = compoundRadius / (float)Math.Pow(volume, 1.0 / 3.0);
            lightScission = lightRadius * fill;
            heavyScission = heavyRadius * fill;
            touch = lightScission + heavyScission;

            float light = Math.Max(1, plan.Light.A);
            float heavy = Math.Max(1, plan.Heavy.A);
            lightShare = Math.Min(0.95f, Math.Max(0.05f, light / (light + heavy)));

            axisX = (float)Math.Cos(plan.AxisAngle);
            axisY = (float)Math.Sin(plan.AxisAngle);
            incomingX = (float)Math.Cos(plan.IncomingAngle);
            incomingY = (float)Math.Sin(plan.IncomingAngle);
        }

        private void DrawBeforeSplit(float aspect, FissionFrame frame)
        {
            if (plan.Induced && time < absorbStart)
            {
                float u = time / absorbStart;
                float start = aspect * 0.92f / incomingX;
                float dist = Lerp(start, compoundRadius + neutronRadius, u);
                DrawNucleus(frame, compoundRadius, 0.12f);
                DrawNeutron(frame, -incomingX * dist, -incomingY * dist, 1f, u);
                frame.AddLabel(plan.Target.ToString(), StageTint.Nucleus, 0f, compoundRadius + 0.12f, 1f);
                return;
            }

            if (time < absorbEnd)
            {
                float v = (time - absorbStart) / (absorbEnd - absorbStart);
                float pulse = plan.Induced ? 1f + 0.1f * (float)Math.Sin(Math.PI * v) : 1f;
                DrawNucleus(frame, compoundRadius * pulse, 0.12f + 0.1f * v);
                if (plan.Induced)
                {
                    float dist = (compoundRadius + neutronRadius) * (1f - v);
                    DrawNeutron(frame, -incomingX * dist, -incomingY * dist, 1f - v, 1f);
                    frame.Add(StageKind.Ring, StageTint.Shock, 1f, 0f, 0f, Lerp(compoundRadius, compoundRadius * 1.7f, v), 0.7f * (1f - v));
                    frame.AddLabel(plan.Target.ToString(), StageTint.Nucleus, 0f, compoundRadius + 0.12f, 1f - v);
                }
                frame.AddLabel(plan.Compound + (plan.Induced ? "*" : string.Empty), StageTint.Nucleus, 0f, compoundRadius + 0.12f, plan.Induced ? v : 1f);
                return;
            }

            DrawWobble(frame, (time - absorbEnd) / WobbleSeconds);
        }

        private void DrawNucleus(FissionFrame frame, float radius, float glow)
        {
            frame.Add(StageKind.Glow, StageTint.Nucleus, 0f, 0f, 0f, compoundRadius * 2.0f, glow);
            frame.Add(StageKind.Disc, StageTint.Nucleus, 0f, 0f, 0f, radius, 1f);
        }

        // The incoming neutron with a short trail on the side it came from. The trail is longest at the start.
        private void DrawNeutron(FissionFrame frame, float x, float y, float alpha, float progress)
        {
            for (int k = 3; k >= 1; k--)
            {
                float back = 0.05f * k * (1f - progress * 0.5f);
                frame.Add(StageKind.Glow, StageTint.Neutron, 1f, x - incomingX * back, y - incomingY * back,
                    neutronRadius * (2.4f - 0.4f * k), alpha * 0.30f / k);
            }
            frame.Add(StageKind.Disc, StageTint.Neutron, 1f, x, y, neutronRadius, alpha);
            frame.AddLabel("n", StageTint.Neutron, x, y - neutronRadius - 0.1f, alpha);
        }

        // w runs from 0 to 1 over the wobble. The round nucleus swings, stretches into two lobes that
        // reach each other, and then the waist between them closes while the lobes take the colour of their fragments.
        private void DrawWobble(FissionFrame frame, float w)
        {
            w = Clamp01(w);
            float swing = 0.10f * (float)Math.Sin(TwoPi * 3f * w) * (1f - w);
            float e = Clamp01((float)Math.Pow(w, 1.4) + swing);

            float lightLobe = Lerp(compoundRadius, lightScission, e);
            float heavyLobe = Lerp(compoundRadius, heavyScission, e);
            float d = Smooth(Clamp01(e / 0.6f)) * touch;
            float xl = -d * (1f - lightShare);
            float xh = d * lightShare;
            float pinch = Clamp01((e - 0.45f) / 0.55f);
            float waist = 0.85f * Math.Min(lightLobe, heavyLobe) * (float)Math.Pow(1f - pinch, 0.7);
            float mix = Clamp01((w - 0.7f) / 0.3f);

            frame.Add(StageKind.Glow, StageTint.Nucleus, 0f, 0f, 0f, compoundRadius * 2.0f, 0.10f + 0.30f * w);
            for (int k = 1; k <= 5; k++)
            {
                float x = Lerp(xl, xh, k / 6f);
                frame.Add(StageKind.Disc, StageTint.Nucleus, 0f, axisX * x, axisY * x, waist, 1f);
            }
            frame.Add(StageKind.Disc, StageTint.Light, mix, axisX * xl, axisY * xl, lightLobe, 1f);
            frame.Add(StageKind.Disc, StageTint.Heavy, mix, axisX * xh, axisY * xh, heavyLobe, 1f);

            // The neck glows white just before it breaks.
            float spark = Clamp01((w - 0.85f) / 0.15f);
            if (spark > 0f)
            {
                frame.Add(StageKind.Glow, StageTint.Flash, 1f, 0f, 0f, compoundRadius * (0.6f + 0.6f * spark), spark);
            }
            frame.AddLabel(plan.Compound + "*", StageTint.Nucleus, 0f, compoundRadius + 0.12f, 1f - w);
        }

        private void DrawAfterSplit(float aspect, FissionFrame frame)
        {
            float tau = time - scissionTime;
            float g = EaseOut(tau / FlyOutSeconds);
            float shrink = Smooth(Clamp01(tau / 0.8f));
            float lightNow = Lerp(lightScission, lightRadius, shrink);
            float heavyNow = Lerp(heavyScission, heavyRadius, shrink);

            // Equal and opposite momentum: the displacements run as the inverse of the masses, so the lighter
            // fragment goes further. The travel is the largest that keeps both fragments, at their size at the
            // split, inside the stage.
            float roomLight = 0.96f * ExtentAlong(axisX, axisY, aspect, lightScission);
            float roomHeavy = 0.96f * ExtentAlong(axisX, axisY, aspect, heavyScission);
            float travel = Math.Max(0.2f, Math.Min(roomLight / (1f - lightShare), roomHeavy / lightShare) - touch);
            float spread = touch + travel * g;
            float xl = -(1f - lightShare) * spread;
            float xh = lightShare * spread;

            frame.Add(StageKind.Glow, StageTint.Light, 1f, axisX * xl, axisY * xl, lightNow * 2.3f, 0.12f + 0.35f * (1f - g));
            frame.Add(StageKind.Glow, StageTint.Heavy, 1f, axisX * xh, axisY * xh, heavyNow * 2.3f, 0.12f + 0.35f * (1f - g));

            DrawNeutronsOut(frame, aspect, tau);

            frame.Add(StageKind.Disc, StageTint.Light, 1f, axisX * xl, axisY * xl, lightNow, 1f);
            frame.Add(StageKind.Disc, StageTint.Heavy, 1f, axisX * xh, axisY * xh, heavyNow, 1f);
            float names = Clamp01((tau - 0.25f) / 0.35f);
            AddLabelNear(frame, plan.Light.ToString(), StageTint.Light, axisX * xl, axisY * xl, lightNow, names);
            AddLabelNear(frame, plan.Heavy.ToString(), StageTint.Heavy, axisX * xh, axisY * xh, heavyNow, names);

            DrawBlast(frame, tau);
        }

        private void DrawNeutronsOut(FissionFrame frame, float aspect, float tau)
        {
            float labelAlpha = Clamp01((tau - 0.5f) / 0.4f);
            for (int i = 0; i < plan.NeutronAngles.Length; i++)
            {
                float dx = (float)Math.Cos(plan.NeutronAngles[i]);
                float dy = (float)Math.Sin(plan.NeutronAngles[i]);
                float reach = 0.88f * plan.NeutronReach[i] * ExtentAlong(dx, dy, aspect, neutronRadius + 0.15f);
                for (int k = 3; k >= 1; k--)
                {
                    float earlier = tau - 0.045f * k;
                    if (earlier > 0f)
                    {
                        float at = reach * EaseOut(earlier / NeutronSeconds);
                        frame.Add(StageKind.Glow, StageTint.Neutron, 1f, dx * at, dy * at, neutronRadius * (2.4f - 0.4f * k), 0.30f / k);
                    }
                }
                float now = reach * EaseOut(tau / NeutronSeconds);
                frame.Add(StageKind.Disc, StageTint.Neutron, 1f, dx * now, dy * now, neutronRadius, 1f);
                AddLabelNear(frame, "n", StageTint.Neutron, dx * now, dy * now, neutronRadius, labelAlpha);
            }
        }

        // The flash, the core of it, two shock rings and the white wash. All of them scale with the energy the
        // plan stands for, on the log scale of NuclearLabBlast.
        private void DrawBlast(FissionFrame frame, float tau)
        {
            float size = 0.8f + 0.9f * blastScale;
            float flashSeconds = 0.55f + 0.6f * blastScale;
            if (tau < flashSeconds)
            {
                float fade = (float)Math.Pow(1f - tau / flashSeconds, 1.6);
                frame.Add(StageKind.Glow, StageTint.Flash, 1f, 0f, 0f, size * (0.45f + 0.75f * Math.Min(1f, tau / 0.5f)), fade);
            }
            if (tau < 0.3f)
            {
                frame.Add(StageKind.Glow, StageTint.Flash, 1f, 0f, 0f, size * 0.55f * (1f - 0.5f * tau / 0.3f), 1f - tau / 0.3f);
            }

            float ringSeconds = 1.2f + 0.8f * blastScale;
            if (tau < ringSeconds)
            {
                float u = tau / ringSeconds;
                frame.Add(StageKind.Ring, StageTint.Shock, 1f, 0f, 0f, size * 1.5f * EaseOut(u), 0.9f * (1f - u));
            }
            float later = tau - 0.12f;
            if (later > 0f && later < ringSeconds)
            {
                float u = later / ringSeconds;
                frame.Add(StageKind.Ring, StageTint.Shock, 1f, 0f, 0f, size * 1.15f * EaseOut(u), 0.5f * (1f - u));
            }

            float quick = 1f - Clamp01(tau / 0.25f);
            frame.Wash = (0.04f + 0.58f * blastScale) * quick * quick;
        }

        // How far a disc of the given radius can move out from the centre along a direction before it touches the edge of the stage.
        private static float ExtentAlong(float dx, float dy, float aspect, float radius)
        {
            float ax = Math.Abs(dx);
            float ay = Math.Abs(dy);
            float tx = ax > 1e-4f ? (aspect - radius) / ax : float.MaxValue;
            float ty = ay > 1e-4f ? (1f - radius) / ay : float.MaxValue;
            return Math.Max(0.1f, Math.Min(tx, ty));
        }

        // A label just above a disc, or just below it when the disc is so high that the text would leave the stage.
        private static void AddLabelNear(FissionFrame frame, string text, StageTint tint, float x, float y, float radius, float alpha)
        {
            float above = y - radius - 0.1f;
            frame.AddLabel(text, tint, x, above < -0.9f ? y + radius + 0.1f : above, alpha);
        }

        // The radius of a nucleus of mass number a: proportional to the cube root of a.
        private static float Radius(int a)
        {
            return Unit * (float)Math.Pow(a, 1.0 / 3.0);
        }

        private static float EaseOut(float x)
        {
            float rest = 1f - Clamp01(x);
            return 1f - (float)Math.Pow(rest, 2.2);
        }

        private static float Smooth(float x)
        {
            return x * x * (3f - 2f * x);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp01(float x)
        {
            return x < 0f ? 0f : (x > 1f ? 1f : x);
        }
    }
}
