using System.Drawing;
using Beykent.Monolit;

namespace ST4Yardimci
{
    /// <summary>AutoCAD temasını izleyen ortak Monolit renkleri.</summary>
    internal static class AcadPaletteTheme
    {
        public static Color Bg => MonolitTheme.Bg;
        public static Color BgAlt => MonolitTheme.Surface;
        public static Color BgHover => MonolitTheme.Hover;
        public static Color BgPressed => MonolitTheme.Pressed;
        public static Color Border => MonolitTheme.Border;
        public static Color Text => MonolitTheme.Text;
        public static Color TextMuted => MonolitTheme.Muted;
        public static Color Accent => MonolitTheme.Accent;
        public static Color InputBg => MonolitTheme.Input;
        public static Color InputBorder => MonolitTheme.Border;
    }
}