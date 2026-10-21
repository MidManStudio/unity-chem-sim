// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabStage.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Draws the picture of the fission animation. It bakes three small textures (a disc, a glow and a ring) from the
    /// opacity functions of <see cref="NuclearLabShape"/> when it is created, so the sample imports no image,
    /// and it draws every blob of a <see cref="FissionFrame"/> with <c>GUI.DrawTexture</c> and a tint. The picture is
    /// clipped to its own rectangle. Owns its textures: call <see cref="Dispose"/>.
    /// </summary>
    internal sealed class NuclearLabStage : IDisposable
    {
        private const int DiscSize = 64;
        private const int GlowSize = 64;
        private const int RingSize = 128;

        private static readonly Color Backdrop = new Color(0.03f, 0.05f, 0.09f, 1f);
        private static readonly Color NucleusColor = new Color(1.00f, 0.72f, 0.25f, 1f);
        private static readonly Color LightColor = new Color(0.35f, 0.85f, 1.00f, 1f);
        private static readonly Color HeavyColor = new Color(0.75f, 0.45f, 1.00f, 1f);
        private static readonly Color NeutronColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color FlashColor = new Color(1.00f, 0.93f, 0.60f, 1f);
        private static readonly Color ShockColor = new Color(1.00f, 0.80f, 0.45f, 1f);

        private readonly FissionFrame frame = new FissionFrame();
        private Texture2D disc;
        private Texture2D glow;
        private Texture2D ring;

        public NuclearLabStage()
        {
            disc = Bake(StageKind.Disc, DiscSize);
            glow = Bake(StageKind.Glow, GlowSize);
            ring = Bake(StageKind.Ring, RingSize);
        }

        /// <summary>
        /// Reserves a strip as wide as the panel and <paramref name="height"/> tall, and during the Repaint event
        /// draws the show in it. With no plan loaded it draws a hint. The strip is reserved on every event, so
        /// the layout is the same in every pass.
        /// </summary>
        public void Draw(FissionShow show, float height, NuclearLabStyles styles)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint || disc == null)
            {
                return;
            }
            Color saved = GUI.color;
            GUI.color = Backdrop;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            if (!show.HasPlan)
            {
                GUI.color = Color.white;
                GUI.Label(rect, show.Caption, styles.Stage);
                GUI.color = saved;
                return;
            }

            show.Evaluate(rect.width / rect.height, frame);
            float half = rect.height * 0.5f;
            float cx = rect.width * 0.5f;
            float cy = rect.height * 0.5f;
            GUI.BeginGroup(rect);
            for (int i = 0; i < frame.Blobs.Count; i++)
            {
                StageBlob b = frame.Blobs[i];
                float r = b.Radius * half;
                if (r < 0.25f || b.Alpha < 0.003f)
                {
                    continue;
                }
                Color tint = Color.Lerp(NucleusColor, TintColor(b.Tint), b.Mix);
                tint.a = b.Alpha;
                GUI.color = tint;
                GUI.DrawTexture(new Rect(cx + b.X * half - r, cy + b.Y * half - r, 2f * r, 2f * r), TextureFor(b.Kind));
            }
            if (frame.Wash > 0.003f)
            {
                GUI.color = new Color(1f, 1f, 1f, frame.Wash);
                GUI.DrawTexture(new Rect(0f, 0f, rect.width, rect.height), Texture2D.whiteTexture);
            }
            for (int i = 0; i < frame.Labels.Count; i++)
            {
                StageLabel l = frame.Labels[i];
                if (l.Alpha < 0.02f)
                {
                    continue;
                }
                Color c = TintColor(l.Tint);
                c.a = l.Alpha;
                GUI.color = c;
                GUI.Label(new Rect(cx + l.X * half - 40f, cy + l.Y * half - 9f, 80f, 18f), l.Text, styles.Stage);
            }
            GUI.EndGroup();
            GUI.color = saved;
        }

        /// <summary>Destroys the textures. Safe to call more than once.</summary>
        public void Dispose()
        {
            Release(ref disc);
            Release(ref glow);
            Release(ref ring);
        }

        private Texture2D TextureFor(StageKind kind)
        {
            switch (kind)
            {
                case StageKind.Glow:
                    return glow;
                case StageKind.Ring:
                    return ring;
                default:
                    return disc;
            }
        }

        private static Color TintColor(StageTint tint)
        {
            switch (tint)
            {
                case StageTint.Neutron:
                    return NeutronColor;
                case StageTint.Light:
                    return LightColor;
                case StageTint.Heavy:
                    return HeavyColor;
                case StageTint.Flash:
                    return FlashColor;
                case StageTint.Shock:
                    return ShockColor;
                default:
                    return NucleusColor;
            }
        }

        // White pixels with the opacity of the shape. The tint of GUI.color supplies the colour.
        private static Texture2D Bake(StageKind kind, int size)
        {
            byte[] alpha = NuclearLabShape.Bake(kind, size);
            Color32[] pixels = new Color32[alpha.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, alpha[i]);
            }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void Release(ref Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }
    }
}
