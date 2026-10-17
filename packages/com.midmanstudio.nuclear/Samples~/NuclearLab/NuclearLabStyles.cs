// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabStyles.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// The few text styles the panel uses, built from the default label style.
    /// Must be created inside OnGUI, because <c>GUI.skin</c> only exists there.
    /// </summary>
    internal sealed class NuclearLabStyles
    {
        public static readonly Color HeadingColor = new Color(0.55f, 0.80f, 1.00f, 1f);
        public static readonly Color ErrorColor = new Color(1.00f, 0.45f, 0.40f, 1f);
        public static readonly Color HintColor = new Color(0.72f, 0.72f, 0.72f, 1f);

        /// <summary>The panel title.</summary>
        public GUIStyle Title;
        /// <summary>A section heading.</summary>
        public GUIStyle Heading;
        /// <summary>Ordinary wrapped text.</summary>
        public GUIStyle Body;
        /// <summary>Ordinary wrapped text that understands rich text tags.</summary>
        public GUIStyle RichBody;
        /// <summary>Small grey wrapped text.</summary>
        public GUIStyle Hint;
        /// <summary>Wrapped red text for a failed action.</summary>
        public GUIStyle Error;
        /// <summary>One table cell: no wrapping, so the column width decides.</summary>
        public GUIStyle Cell;
        /// <summary>A table header cell.</summary>
        public GUIStyle HeaderCell;

        public static NuclearLabStyles Create()
        {
            NuclearLabStyles styles = new NuclearLabStyles();
            styles.Title = Make(18, FontStyle.Bold, Color.white, true);
            styles.Heading = Make(14, FontStyle.Bold, HeadingColor, true);
            styles.Body = Make(12, FontStyle.Normal, Color.white, true);
            styles.RichBody = Make(12, FontStyle.Normal, Color.white, true);
            styles.RichBody.richText = true;
            styles.Hint = Make(11, FontStyle.Normal, HintColor, true);
            styles.Error = Make(12, FontStyle.Bold, ErrorColor, true);
            styles.Cell = Make(12, FontStyle.Normal, Color.white, false);
            styles.HeaderCell = Make(12, FontStyle.Bold, HintColor, false);
            return styles;
        }

        private static GUIStyle Make(int size, FontStyle fontStyle, Color color, bool wrap)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = size;
            style.fontStyle = fontStyle;
            style.wordWrap = wrap;
            style.normal.textColor = color;
            return style;
        }
    }
}
