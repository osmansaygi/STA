using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>BETONARME TOOL — AutoCAD Tool Palettes koyu görünümünde yardımcı komut paleti.</summary>
    internal static class St4LispPaletteManager
    {
        private const int PaletteWidth = 220;
        private const int PaletteHeight = 420;
        private const int MarginX = 12;
        private const int MarginY = 80;

        private static PaletteSet _palette;
        private static bool _suppressSave;
        private static int _lastFullHeight = PaletteHeight;

        public static void Show()
        {
            try
            {
                if (_palette != null)
                {
                    try
                    {
                        // Zaten açıksa sadece görünür yap; EnsureOnScreen her seferinde titreşim yapar.
                        _palette.Visible = true;
                        return;
                    }
                    catch
                    {
                        try { _palette.Dispose(); } catch { }
                        _palette = null;
                    }
                }

                _palette = new PaletteSet("BETONARME TOOL")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    MinimumSize = new Size(160, 160),
                    KeepFocus = false,
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Bottom
                };

                _palette.Add("Araçlar", new St4LispPaletteControl());
                _palette.SizeChanged += (s, e) => SaveLayout();
                _palette.PaletteSetHostMoved += (s, e) => SaveLayout();
                _palette.StateChanged += (s, e) => SaveLayout();

                _suppressSave = true;
                try
                {
                    _palette.Size = new Size(PaletteWidth, PaletteHeight);
                    _palette.Visible = true;
                    if (!TryRestoreLayout())
                        PlaceDefault();
                    EnsureOnScreen();
                }
                finally
                {
                    _suppressSave = false;
                }

                try
                {
                    AcApp.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage("\nBETONARME TOOL paleti acildi.");
                }
                catch { }
            }
            catch (System.Exception ex)
            {
                try
                {
                    AcApp.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage("\nBETONARME TOOL palet hatasi: {0}", ex.Message);
                }
                catch { }
            }
        }

        public static void Shutdown()
        {
            if (_palette == null) return;
            SaveLayout();
            try { _palette.Visible = false; } catch { }
            try { _palette.Dispose(); } catch { }
            _palette = null;
        }

        private static string LayoutPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4_Yardimci");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "palette_layout.txt");
            }
        }

        private static void SaveLayout()
        {
            if (_suppressSave || _palette == null) return;
            try
            {
                int w = _palette.Size.Width;
                int h = _palette.Size.Height;
                if (w < 40) return;
                bool rolled = false;
                try { rolled = _palette.RolledUp; } catch { }
                if (!rolled && h >= 80)
                    _lastFullHeight = h;
                else
                    h = _lastFullHeight > 0 ? _lastFullHeight : PaletteHeight;

                var loc = _palette.Location;
                string dock = DockSides.None.ToString();
                try { dock = _palette.Dock.ToString(); } catch { }

                string text =
                    "dock=" + dock + "\r\n" +
                    "x=" + loc.X.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "y=" + loc.Y.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "w=" + w.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "h=" + h.ToString(CultureInfo.InvariantCulture) + "\r\n";
                File.WriteAllText(LayoutPath, text);
            }
            catch { }
        }

        private static bool TryRestoreLayout()
        {
            if (_palette == null) return false;
            try
            {
                string path = LayoutPath;
                if (!File.Exists(path)) return false;
                int x = 0, y = 0, w = 0, h = 0;
                DockSides dock = DockSides.None;
                bool gotDock = false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    if (key == "dock" && Enum.TryParse(val, out DockSides d))
                    {
                        dock = d;
                        gotDock = true;
                    }
                    else if (key == "x") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                    else if (key == "y") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                    else if (key == "w") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out w);
                    else if (key == "h") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
                }
                if (!gotDock || w < 80 || h < 80) return false;

                var rect = new Rectangle(x, y, w, h);
                if (dock == DockSides.None && !TitleBarOnAnyScreen(rect))
                    return false;

                _palette.Size = new Size(w, h);
                _lastFullHeight = h;
                if (dock == DockSides.None)
                {
                    try { _palette.Dock = DockSides.None; } catch { }
                    _palette.Location = new Point(x, y);
                    _palette.Size = new Size(w, h);
                }
                else
                {
                    try { _palette.Dock = dock; } catch { return false; }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PlaceDefault()
        {
            if (_palette == null) return;
            try
            {
                try { _palette.Dock = DockSides.None; } catch { }
                Rectangle host = GetAcadWindowBounds();
                int palW = PaletteWidth;
                int palH = PaletteHeight;
                int x = host.Right - palW - MarginX;
                int y = host.Top + MarginY;
                if (x < host.Left + MarginX) x = host.Left + MarginX;
                if (y + palH > host.Bottom - 40) y = Math.Max(host.Top + MarginX, host.Bottom - palH - 40);
                _palette.Location = new Point(x, y);
                _palette.Size = new Size(palW, palH);
                _lastFullHeight = palH;
            }
            catch { }
        }

        private static void EnsureOnScreen()
        {
            if (_palette == null) return;
            try
            {
                DockSides dock = DockSides.None;
                try { dock = _palette.Dock; } catch { }
                if (dock != DockSides.None) return;

                var loc = _palette.Location;
                var sz = _palette.Size;
                if (sz.Width < 80) sz = new Size(PaletteWidth, sz.Height);
                if (sz.Height < 80) sz = new Size(sz.Width, PaletteHeight);
                var rect = new Rectangle(loc, sz);
                if (TitleBarOnAnyScreen(rect)) return;
                PlaceDefault();
            }
            catch
            {
                PlaceDefault();
            }
        }

        private static bool TitleBarOnAnyScreen(Rectangle rect)
        {
            var title = new Rectangle(rect.Left, rect.Top, Math.Max(rect.Width, 48), 32);
            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(title))
                        return true;
                }
            }
            catch { }
            return false;
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

    internal sealed class St4LispPaletteControl : DarkPaletteUserControl
    {
        private const string OtoOlcuIconResource = "ST4Yardimci.Icons.oto_olcu_32.png";
        /// <summary>Seçilen simge: A1 (sık basamak + burun).</summary>
        private const string MerdivenIconResource = "ST4Yardimci.Icons.merdiven_v10_01.png";
        /// <summary>Seçilen simge: D3 çift yön, çerçevesiz.</summary>
        private const string DosemeIconResource = "ST4Yardimci.Icons.doseme_03.png";
        /// <summary>Seçilen simge: R1 kesit üst+alt.</summary>
        private const string RadyeIconResource = "ST4Yardimci.Icons.radye_01.png";
        /// <summary>Seçilen simge: B23 nokta örnek.</summary>
        private const string DonatiBoyuIconResource = "ST4Yardimci.Icons.donatiboyu_23.png";
        /// <summary>Seçilen simge: M3 başlıklı tablo.</summary>
        private const string DonatiMetrajIconResource = "ST4Yardimci.Icons.donatimetraj_03.png";

        public St4LispPaletteControl()
        {
            AutoScroll = true;
            Padding = new Padding(6, 8, 6, 8);

            int y = 8;
            y = AddToolRow(y, "Oto Ölçü", LoadIcon(OtoOlcuIconResource), "OTOOLCU");
            y = AddToolRow(y, "Merdiven", LoadIcon(MerdivenIconResource), "MERDIVEN");
            y = AddToolRow(y, "Döşeme Donatı", LoadIcon(DosemeIconResource), "DOSEMEDONATI");
            y = AddToolRow(y, "Radye Temel Donatı", LoadIcon(RadyeIconResource), "RADYETEMEL");
            y = AddToolRow(y, "Donatı Boyu", LoadIcon(DonatiBoyuIconResource), "DONATIBOYU");
            y = AddToolRow(y, "Donatı Metraj", LoadIcon(DonatiMetrajIconResource), "DONATIMETRAJ");
            EnsureDarkFiller();
        }

        // Grup başlıkları şimdilik yok — sonra gruplanır.

        private int AddToolRow(int top, string label, Image icon, string cmd)
        {
            var row = new Panel
            {
                Location = new Point(4, top),
                Size = new Size(Math.Max(Width - 20, 180), 40),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BackColor = AcadPaletteTheme.Bg,
                Cursor = Cursors.Hand,
                Tag = cmd
            };

            var pic = new PictureBox
            {
                Size = new Size(32, 32),
                Location = new Point(6, 4),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent,
                Image = icon
            };

            var txt = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(46, 10),
                Size = new Size(Math.Max(row.Width - 54, 100), 20),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            row.Controls.Add(pic);
            row.Controls.Add(txt);
            row.MouseEnter += (s, e) => row.BackColor = AcadPaletteTheme.BgHover;
            row.MouseLeave += (s, e) => row.BackColor = AcadPaletteTheme.Bg;
            row.MouseDown += (s, e) => row.BackColor = AcadPaletteTheme.BgPressed;
            row.MouseUp += (s, e) => row.BackColor = AcadPaletteTheme.BgHover;
            row.Click += OnToolClick;
            pic.MouseEnter += (s, e) => row.BackColor = AcadPaletteTheme.BgHover;
            txt.MouseEnter += (s, e) => row.BackColor = AcadPaletteTheme.BgHover;
            pic.MouseLeave += (s, e) => row.BackColor = AcadPaletteTheme.Bg;
            txt.MouseLeave += (s, e) => row.BackColor = AcadPaletteTheme.Bg;
            pic.Click += (s, e) => OnToolClick(row, e);
            txt.Click += (s, e) => OnToolClick(row, e);

            Controls.Add(row);
            return top + 44;
        }

        private static void OnToolClick(object sender, EventArgs e)
        {
            try
            {
                Control c = sender as Control;
                while (c != null && !(c.Tag is string))
                    c = c.Parent;
                if (c?.Tag is string cmd && !string.IsNullOrWhiteSpace(cmd))
                {
                    var doc = AcApp.DocumentManager.MdiActiveDocument;
                    if (doc == null) return;
                    doc.SendStringToExecute(cmd + " ", true, false, false);
                }
            }
            catch { }
        }

        private static Image LoadIcon(string resourceName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return Image.FromStream(stream);
                }
            }
            catch { }
            return null;
        }
    }
}
