using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>AutoCAD Tool Palettes (All Palettes) satır görünümü: 32px ikon + yazı.</summary>
    internal static class ToolPaletteUi
    {
        public const int RowH = 40;
        public const int IconSize = 32;
        public const int HeaderH = 22;

        public static int AddGroupHeader(Control parent, string title, int top)
        {
            var lbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(8, top),
                Size = new Size(Math.Max(parent.Width - 24, 120), 18),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            parent.Controls.Add(lbl);
            return top + HeaderH;
        }

        public static int AddToolRow(Control parent, int top, string label, Image icon, string cmd)
        {
            int rowW = Math.Max(parent.ClientSize.Width - 16, 160);
            var row = new Panel
            {
                Location = new Point(4, top),
                Size = new Size(rowW, RowH),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BackColor = AcadPaletteTheme.Bg,
                Cursor = Cursors.Hand,
                Tag = cmd
            };

            var pic = new PictureBox
            {
                Size = new Size(IconSize, IconSize),
                Location = new Point(6, 4),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent,
                Image = icon
            };

            var txt = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(46, 10),
                Size = new Size(Math.Max(rowW - 54, 80), 20),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            row.Controls.Add(pic);
            row.Controls.Add(txt);

            void Hover(object s, EventArgs e) => row.BackColor = AcadPaletteTheme.BgHover;
            void Leave(object s, EventArgs e) => row.BackColor = AcadPaletteTheme.Bg;
            void Down(object s, EventArgs e) => row.BackColor = AcadPaletteTheme.BgPressed;
            void Up(object s, EventArgs e) => row.BackColor = AcadPaletteTheme.BgHover;
            void Click(object s, EventArgs e) => RunCmd(row);

            row.MouseEnter += Hover;
            row.MouseLeave += Leave;
            row.MouseDown += Down;
            row.MouseUp += Up;
            row.Click += Click;
            pic.MouseEnter += Hover;
            txt.MouseEnter += Hover;
            pic.Click += Click;
            txt.Click += Click;

            parent.Controls.Add(row);
            return top + RowH + 2;
        }

        private static void RunCmd(Control row)
        {
            try
            {
                if (!(row.Tag is string cmd) || string.IsNullOrWhiteSpace(cmd)) return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute(cmd + " ", true, false, false);
            }
            catch { }
        }
    }

    /// <summary>AutoCAD tarzı gri + ACI 94 yeşil vurgu ile 32×32 komut ikonları.</summary>
    internal static class StaToolIcons
    {
        private static readonly Color Gray = Color.FromArgb(0x66, 0x66, 0x66);
        /// <summary>AutoCAD ACI 94 ≈ RGB(153,204,51).</summary>
        private static readonly Color Accent = Color.FromArgb(153, 204, 51);
        private static readonly Color Light = Color.FromArgb(0xC8, 0xCD, 0xD4);

        public static Image ForCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return GlyphPlan();
            string c = cmd.ToUpperInvariant();
            if (c.Contains("KALIP")) return GlyphPlan();
            if (c.Contains("KOLON") && c.Contains("DUSEY")) return GlyphColumnElev();
            if (c.Contains("KOLON")) return GlyphColumn();
            if (c.Contains("TEMEL") || c.Contains("DONATI") && c.Contains("TEMEL")) return GlyphFoundation();
            if (c.Contains("TEMELDONATI") || c.StartsWith("RD")) return GlyphFoundation();
            if (c.Contains("ISKELE") && c.Contains("KESIT")) return GlyphScaffoldSection();
            if (c.Contains("ISKELE")) return GlyphScaffold();
            if (c.Contains("KESIT")) return GlyphSection();
            if (c.Contains("KATMAN")) return GlyphLayers();
            if (c.Contains("METRAJ")) return GlyphTable();
            if (c.Contains("KIRIS")) return GlyphBeam();
            if (c.Contains("KAPAMA") || c.Contains("PERDE")) return GlyphWall();
            if (c.Contains("REVIZE") || c.Contains("DATA") || c.Contains("PLANID") || c.Contains("DENEME"))
                return GlyphGear();
            return GlyphPlan();
        }

        private static Bitmap NewBmp()
        {
            var b = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
            }
            return b;
        }

        private static void Rect(Graphics g, int x, int y, int w, int h, Color c)
        {
            using (var br = new SolidBrush(c))
                g.FillRectangle(br, x, y, w, h);
        }

        private static Image GlyphPlan()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 6, 6, 20, 20, Gray);
                Rect(g, 8, 8, 16, 16, Color.FromArgb(40, 45, 52));
                Rect(g, 10, 14, 12, 2, Accent);
                Rect(g, 14, 10, 2, 12, Accent);
            }
            return b;
        }

        private static Image GlyphColumn()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 11, 5, 10, 22, Gray);
                Rect(g, 13, 7, 6, 18, Accent);
            }
            return b;
        }

        private static Image GlyphColumnElev()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 8, 4, 16, 24, Gray);
                Rect(g, 10, 6, 4, 20, Accent);
                Rect(g, 18, 6, 4, 20, Accent);
            }
            return b;
        }

        private static Image GlyphFoundation()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 12, 6, 8, 10, Gray);
                Rect(g, 6, 16, 20, 8, Gray);
                Rect(g, 8, 18, 16, 4, Accent);
            }
            return b;
        }

        private static Image GlyphScaffold()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 7, 5, 2, 22, Gray);
                Rect(g, 23, 5, 2, 22, Gray);
                Rect(g, 7, 10, 18, 2, Accent);
                Rect(g, 7, 18, 18, 2, Accent);
            }
            return b;
        }

        private static Image GlyphScaffoldSection()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 6, 6, 20, 2, Gray);
                Rect(g, 6, 15, 20, 2, Gray);
                Rect(g, 6, 24, 20, 2, Gray);
                Rect(g, 15, 6, 2, 20, Accent);
            }
            return b;
        }

        private static Image GlyphSection()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 5, 8, 22, 16, Gray);
                Rect(g, 7, 10, 18, 12, Color.FromArgb(40, 45, 52));
                Rect(g, 5, 14, 22, 2, Accent);
            }
            return b;
        }

        private static Image GlyphLayers()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 6, 8, 20, 4, Light);
                Rect(g, 6, 14, 20, 4, Gray);
                Rect(g, 6, 20, 20, 4, Accent);
            }
            return b;
        }

        private static Image GlyphTable()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 6, 6, 20, 20, Gray);
                Rect(g, 8, 8, 16, 4, Accent);
                Rect(g, 8, 14, 16, 2, Light);
                Rect(g, 8, 18, 16, 2, Light);
                Rect(g, 8, 22, 16, 2, Light);
            }
            return b;
        }

        private static Image GlyphBeam()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 5, 12, 22, 8, Gray);
                Rect(g, 7, 14, 18, 4, Accent);
            }
            return b;
        }

        private static Image GlyphWall()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 8, 5, 16, 22, Gray);
                Rect(g, 10, 7, 4, 18, Accent);
                Rect(g, 18, 7, 4, 18, Accent);
            }
            return b;
        }

        private static Image GlyphGear()
        {
            var b = NewBmp();
            using (var g = Graphics.FromImage(b))
            {
                Rect(g, 12, 6, 8, 20, Gray);
                Rect(g, 6, 12, 20, 8, Gray);
                Rect(g, 13, 13, 6, 6, Accent);
            }
            return b;
        }
    }
}
