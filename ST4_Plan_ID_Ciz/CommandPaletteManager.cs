using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal static class CommandPaletteManager
    {
        private const int PaletteWidth = 150;
        private const int PaletteHeight = 516;
        private const int MarginX = 8;
        /// <summary>Komut satırı / durum çubuğu üstünde kalsın.</summary>
        private const int MarginBottom = 56;

        private static PaletteSet _palette;

        public static void Show()
        {
            try
            {
                if (_palette != null)
                {
                    try
                    {
                        _palette.Visible = true;
                        PlacePaletteBottomLeft();
                        EnableAutoHide();
                        return;
                    }
                    catch
                    {
                        try { _palette.Dispose(); } catch { }
                        _palette = null;
                    }
                }

                _palette = new PaletteSet("STA Komut Paneli")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    MinimumSize = new Size(PaletteWidth, PaletteHeight),
                    KeepFocus = false,
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Bottom
                };

                _palette.Add("Komutlar", new CommandPaletteControl());
                _palette.Add("Kiri\u015f d\u00fczelt", new KirisDuzeltPaletteControl());

                _palette.Size = new Size(PaletteWidth, PaletteHeight);
                _palette.Visible = true;
                try { _palette.Dock = DockSides.None; } catch { }
                _palette.Size = new Size(PaletteWidth, PaletteHeight);
                PlacePaletteBottomLeft();
                EnableAutoHide();
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (_palette == null) return;
            try { _palette.Visible = false; } catch { }
            try { _palette.Dispose(); } catch { }
            _palette = null;
        }

        private static void EnableAutoHide()
        {
            if (_palette == null) return;
            try { _palette.AutoRollUp = true; } catch { }
        }

        private static void PlacePaletteBottomLeft()
        {
            if (_palette == null) return;
            try
            {
                try { _palette.Dock = DockSides.None; } catch { }
                int palW = _palette.Size.Width > 0 ? _palette.Size.Width : PaletteWidth;
                int palH = _palette.Size.Height > 0 ? _palette.Size.Height : PaletteHeight;
                Rectangle host = GetAcadWindowBounds();
                int x = host.Left + MarginX;
                int y = host.Bottom - palH - MarginBottom;
                if (y < host.Top + MarginX) y = host.Top + MarginX;
                _palette.Location = new Point(x, y);
                _palette.Size = new Size(palW, palH);
            }
            catch { }
        }

        private static Rectangle GetAcadWindowBounds()
        {
            try
            {
                IntPtr hwnd = AcApp.MainWindow.Handle;
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
                    return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch { }

            try { return Screen.PrimaryScreen.WorkingArea; }
            catch { return new Rectangle(0, 0, 1280, 800); }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal sealed class CommandPaletteControl : UserControl
    {
        private static readonly Color BgColor = Color.FromArgb(248, 249, 252);
        private static readonly Color SecBg = Color.FromArgb(238, 240, 245);
        private static readonly Color SecBrd = Color.FromArgb(205, 210, 222);
        private static readonly Color BtnNorm = Color.White;
        private static readonly Color BtnHov = Color.FromArgb(220, 232, 250);
        private static readonly Color BtnPrs = Color.FromArgb(195, 215, 245);
        private static readonly Color BtnBrd = Color.FromArgb(185, 192, 210);
        private static readonly Color TxDark = Color.FromArgb(38, 38, 48);
        private static readonly Color TxSec = Color.FromArgb(65, 75, 98);

        private const int BtnH = 24;
        private const int BtnGap = 2;
        private const int InnerPad = 2;
        private const int TitleH = 18;
        private const int SecGap = 4;
        private const int SidePad = 2;

        private static readonly (string label, string cmd)[] S50 =
        {
            ("Kal\u0131p Plan\u0131",    "KALIP50ST4"),
            ("Kolon Aplikasyon", "KOLON50ST4"),
            ("Temel Plan\u0131",   "TEMEL50ST4"),
            ("Rd. Temel Plan\u0131", "TEMELDONATI"),
        };
        private static readonly (string label, string cmd)[] S100 =
        {
            ("Kal\u0131p Plan\u0131",    "KALIP100ST4"),
            ("Kolon Aplikasyon", "KOLON100ST4"),
            ("Temel Plan\u0131",   "TEMEL100ST4"),
        };
        private static readonly (string label, string cmd)[] Isk =
        {
            ("\u0130skele Plan\u0131",  "ISKELECIZ"),
            ("\u0130skele Kesiti", "ISKELEKESIT"),
        };
        private static readonly (string label, string cmd)[] Gen =
        {
            ("ST4 Plan ID",  "ST4PLANID"),
            ("Kolon Data",   "KOLONDATA"),
            ("ST4 Kesit",    "ST4KESIT"),
            ("Katman temizle", "ST4KATMANTEMIZLE"),
        };
        private static readonly (string label, string cmd)[] AltDeneme =
        {
            ("Deneme1 (ilk 2 kat, sade)", "DENEME1"),
        };

        public CommandPaletteControl()
        {
            BackColor = BgColor;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(SidePad, 4, SidePad, 4);

            int y = 4;
            y = AddSection("\u00d6L\u00c7EK 1:50", S50, y);
            y = AddSection("\u00d6L\u00c7EK 1:100", S100, y);
            y = AddSection("\u0130SKELE", Isk, y);
            y = AddSection("GENEL", Gen, y);
            y = AddSection("DENEME", AltDeneme, y);
        }

        private int SecH(int n) => TitleH + InnerPad + n * (BtnH + BtnGap) - BtnGap + InnerPad;

        private int AddSection(string title, (string label, string cmd)[] items, int top)
        {
            int h = SecH(items.Length);
            var sp = new Panel
            {
                Location = new Point(SidePad, top),
                Size = new Size(Width - SidePad * 2 - 1, h),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BackColor = SecBg
            };
            sp.Paint += (s, e) =>
            {
                using (var pen = new Pen(SecBrd))
                    e.Graphics.DrawRectangle(pen, 0, 0, sp.Width - 1, sp.Height - 1);
            };

            sp.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = TxSec,
                Location = new Point(InnerPad + 1, 2),
                AutoSize = true,
                BackColor = Color.Transparent
            });

            int by = TitleH + InnerPad;
            foreach (var it in items)
            {
                var btn = MakeBtn(it.label, it.cmd);
                btn.Location = new Point(InnerPad, by);
                btn.Size = new Size(sp.Width - InnerPad * 2 - 1, BtnH);
                btn.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                sp.Controls.Add(btn);
                by += BtnH + BtnGap;
            }

            Controls.Add(sp);
            return top + h + SecGap;
        }

        private Button MakeBtn(string text, string cmd)
        {
            var b = new Button
            {
                Text = text,
                Tag = cmd,
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnNorm,
                ForeColor = TxDark,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            b.FlatAppearance.BorderColor = BtnBrd;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = BtnHov;
            b.FlatAppearance.MouseDownBackColor = BtnPrs;
            b.Click += OnCmd;
            return b;
        }

        private static void OnCmd(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is Control c) || !(c.Tag is string cmd) || string.IsNullOrWhiteSpace(cmd))
                    return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute(cmd + " ", true, false, false);
            }
            catch { }
        }
    }

    /// <summary>STA kesit/kiris çizimini Beykent katman ve stillerine çevirir (KIRISDUZELT).</summary>
    internal sealed class KirisDuzeltPaletteControl : UserControl
    {
        public KirisDuzeltPaletteControl()
        {
            BackColor = Color.FromArgb(248, 249, 252);
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(6, 8, 6, 8);

            var lbl = new Label
            {
                Text = "STA4CAD kiriş/kesit: x5000, INSERT/XREF BlockTransform yedek; blok içi + model + yerleşimde Color/LT/LW BYLAYER; DONATI, KES_DET, KOT.",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(55, 65, 85),
                AutoSize = false,
                Size = new Size(130, 120),
                Location = new Point(2, 4),
                BackColor = Color.Transparent
            };

            var btnKiris = MakeTabBtn("KIRISDUZELT", 128);
            var btnMetraj = MakeTabBtn("METRAJ", 162);

            Controls.Add(lbl);
            Controls.Add(btnKiris);
            Controls.Add(btnMetraj);
        }

        private static Button MakeTabBtn(string cmd, int y)
        {
            var btn = new Button
            {
                Text = cmd,
                Location = new Point(2, y),
                Size = new Size(130, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Tag = cmd
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(185, 192, 210);
            btn.Click += OnTabCmd;
            return btn;
        }

        private static void OnTabCmd(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is Control c) || !(c.Tag is string cmd) || string.IsNullOrWhiteSpace(cmd))
                    return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute(cmd + " ", true, false, false);
            }
            catch { }
        }
    }
}
