// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FissionFrame.cs"
// ============================================================================
using System.Collections.Generic;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>The stage of the fission animation. Phases follow each other in this order.</summary>
    public enum FissionPhase
    {
        /// <summary>A show is loaded and has not started.</summary>
        Ready = 0,
        /// <summary>A neutron travels towards the nucleus.</summary>
        Approach = 1,
        /// <summary>The neutron is absorbed, or a nucleus that fissions by itself waits.</summary>
        Absorb = 2,
        /// <summary>The nucleus wobbles and stretches into a dumbbell.</summary>
        Wobble = 3,
        /// <summary>The first half second after the split, while the flash is brightest.</summary>
        Split = 4,
        /// <summary>The fragments and neutrons fly apart.</summary>
        Aftermath = 5,
        /// <summary>The show has ended and the last picture stays.</summary>
        Done = 6,
    }

    /// <summary>The shape a blob is drawn with.</summary>
    public enum StageKind
    {
        /// <summary>A filled disc with a soft edge.</summary>
        Disc = 0,
        /// <summary>A disc that fades smoothly from the centre to nothing.</summary>
        Glow = 1,
        /// <summary>A thin ring.</summary>
        Ring = 2,
    }

    /// <summary>What a blob stands for, which picks its colour.</summary>
    public enum StageTint
    {
        /// <summary>The nucleus.</summary>
        Nucleus = 0,
        /// <summary>A neutron.</summary>
        Neutron = 1,
        /// <summary>The lighter fragment.</summary>
        Light = 2,
        /// <summary>The heavier fragment.</summary>
        Heavy = 3,
        /// <summary>The flash.</summary>
        Flash = 4,
        /// <summary>A shock ring.</summary>
        Shock = 5,
    }

    /// <summary>One shape of a frame. Positions and sizes are in stage units, see <see cref="FissionShow"/>.</summary>
    public struct StageBlob
    {
        /// <summary>The shape.</summary>
        public StageKind Kind;
        /// <summary>The colour family.</summary>
        public StageTint Tint;
        /// <summary>How far the colour has moved from the nucleus colour to the colour of <see cref="Tint"/>, 0 to 1. Only the nucleus itself stays at 0.</summary>
        public float Mix;
        /// <summary>Centre, to the right.</summary>
        public float X;
        /// <summary>Centre, downwards.</summary>
        public float Y;
        /// <summary>Radius.</summary>
        public float Radius;
        /// <summary>Opacity, 0 to 1.</summary>
        public float Alpha;
    }

    /// <summary>A short text on the stage, centred on a point.</summary>
    public struct StageLabel
    {
        /// <summary>The text.</summary>
        public string Text;
        /// <summary>The colour family.</summary>
        public StageTint Tint;
        /// <summary>Centre, to the right, in stage units.</summary>
        public float X;
        /// <summary>Centre, downwards, in stage units.</summary>
        public float Y;
        /// <summary>Opacity, 0 to 1.</summary>
        public float Alpha;
    }

    /// <summary>
    /// What one moment of the animation looks like: the blobs from back to front, the labels, the white wash over the
    /// whole stage and the caption. <see cref="FissionShow.Evaluate"/> fills it, and the view draws it. Reused from
    /// frame to frame, so a frame allocates only while its lists are still growing. Has no UnityEngine references.
    /// </summary>
    public sealed class FissionFrame
    {
        /// <summary>The blobs, the one at the back first.</summary>
        public readonly List<StageBlob> Blobs = new List<StageBlob>(64);

        /// <summary>The labels.</summary>
        public readonly List<StageLabel> Labels = new List<StageLabel>(16);

        /// <summary>Opacity of the white wash over the whole stage, 0 to 1.</summary>
        public float Wash;

        /// <summary>The line that says what is happening.</summary>
        public string Caption = string.Empty;

        /// <summary>The phase of this moment.</summary>
        public FissionPhase Phase;

        /// <summary>Empties the frame.</summary>
        public void Clear()
        {
            Blobs.Clear();
            Labels.Clear();
            Wash = 0f;
            Caption = string.Empty;
            Phase = FissionPhase.Ready;
        }

        /// <summary>Adds a blob in front of the others.</summary>
        public void Add(StageKind kind, StageTint tint, float mix, float x, float y, float radius, float alpha)
        {
            StageBlob blob;
            blob.Kind = kind;
            blob.Tint = tint;
            blob.Mix = mix;
            blob.X = x;
            blob.Y = y;
            blob.Radius = radius;
            blob.Alpha = alpha;
            Blobs.Add(blob);
        }

        /// <summary>Adds a label.</summary>
        public void AddLabel(string text, StageTint tint, float x, float y, float alpha)
        {
            StageLabel label;
            label.Text = text;
            label.Tint = tint;
            label.X = x;
            label.Y = y;
            label.Alpha = alpha;
            Labels.Add(label);
        }
    }
}
