using System.Drawing;

namespace Beykent.Monolit
{
    /// <summary>Formlar daima açık; panelin AutoCAD tema tercihi bağımsızdır.</summary>
    internal static class StandardFormTheme
    {
        internal static Color Bg => Color.FromArgb(240, 240, 240);
        internal static Color Surface => SystemColors.Control;
        internal static Color BgAlt => Surface;
        internal static Color Hover => Color.FromArgb(229, 241, 251);
        internal static Color BgHover => Hover;
        internal static Color Pressed => Color.FromArgb(204, 228, 247);
        internal static Color BgPressed => Pressed;
        internal static Color Border => Color.FromArgb(205, 205, 205);
        internal static Color InputBorder => Border;
        internal static Color Text => SystemColors.ControlText;
        internal static Color Muted => SystemColors.GrayText;
        internal static Color TextMuted => Muted;
        internal static Color Accent => SystemColors.Highlight;
        internal static Color Input => Color.White;
        internal static Color InputBg => Input;
    }
}
