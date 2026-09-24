using System;
using System.Drawing;
using System.Windows.Forms;

namespace ST4Yardimci
{
    /// <summary>
    /// AutoCAD PaletteSet WinForms host'u boş alanı beyaz bırakır; koyu zemin zorlanır.
    /// </summary>
    internal class DarkPaletteUserControl : UserControl
    {
        public DarkPaletteUserControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Opaque,
                true);
            DoubleBuffered = true;
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = AcadPaletteTheme.Text;
            Dock = DockStyle.Fill;

            ParentChanged += (s, e) => ApplyHostDark();
            HandleCreated += (s, e) => ApplyHostDark();
            VisibleChanged += (s, e) => { if (Visible) ApplyHostDark(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(AcadPaletteTheme.Bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(AcadPaletteTheme.Bg);
            base.OnPaint(e);
        }

        protected void EnsureDarkFiller()
        {
            // Dock=Fill panel boş alanı boyar (satırların altında kalan beyaz).
            var filler = new Panel
            {
                Name = "_darkFiller",
                Dock = DockStyle.Fill,
                BackColor = AcadPaletteTheme.Bg
            };
            Controls.Add(filler);
            filler.SendToBack();
        }

        private void ApplyHostDark()
        {
            try
            {
                BackColor = AcadPaletteTheme.Bg;
                for (Control p = Parent; p != null; p = p.Parent)
                {
                    try { p.BackColor = AcadPaletteTheme.Bg; } catch { }
                    // PaletteSet host birkaç seviye üstte; çok yukarı çıkma.
                    if (p.Parent == null) break;
                    if (string.Equals(p.GetType().Name, "Palette", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            catch { }
        }
    }
}
