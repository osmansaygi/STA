using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal static class CommandPaletteManager
    {
        private const int PaletteWidth = 220;
        private const int PaletteHeight = 640;
        private const int MarginX = 8;
        /// <summary>Komut satırı / durum çubuğu üstünde kalsın.</summary>
        private const int MarginBottom = 56;

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
                        _palette.Visible = true;
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
                _palette.SizeChanged += (s, e) => SaveLayout();
                _palette.PaletteSetHostMoved += (s, e) => SaveLayout();
                _palette.StateChanged += (s, e) => SaveLayout();

                _suppressSave = true;
                try
                {
                    _palette.Size = new Size(PaletteWidth, PaletteHeight);
                    _palette.Visible = true;
                    if (!TryRestoreLayout())
                        PlacePaletteBottomLeft();
                }
                finally
                {
                    _suppressSave = false;
                }
                // AutoRollUp açılış titreşimini azaltır (kapalı).
                try { if (_palette != null) _palette.AutoRollUp = false; } catch { }
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (_palette == null) return;
            SaveLayout();
            try { _palette.Visible = false; } catch { }
            try { _palette.Dispose(); } catch { }
            _palette = null;
        }

        private static void EnableAutoHide()
        {
            if (_palette == null) return;
            try { _palette.AutoRollUp = false; } catch { }
        }

        private static string LayoutPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4_Plan_ID_Ciz");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "palette_sta.txt");
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

    internal sealed class CommandPaletteControl : DarkPaletteUserControl
    {
        private static readonly (string label, string cmd)[] S50 =
        {
            ("Kal\u0131p Plan\u0131",    "KALIP50ST4"),
            ("Kolon Aplikasyon", "KOLON50ST4"),
            ("Kolon D\u00fc\u015fey", "KOLONDUSEY"),
            ("Temel Plan\u0131",   "TEMEL50ST4"),
            ("Rd. Temel Plan\u0131", "TEMELDONATI"),
        };
        private static readonly (string label, string cmd)[] S25 =
        {
            ("Kolon D\u00fc\u015fey", "KOLONDUSEY25"),
            ("Kolon D\u00fc\u015fey - 2", "KOLONDUSEY2"),
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
            ("Kapama Perde", "KAPAMADETAY"),
            ("Kolon Revize", "KOLONREVIZE"),
        };

        public CommandPaletteControl()
        {
            AutoScroll = true;
            Padding = new Padding(4, 6, 4, 6);

            int y = 6;
            y = AddGroup(y, "Ölçek 1:50", S50);
            y = AddGroup(y, "Ölçek 1:25", S25);
            y = AddGroup(y, "Ölçek 1:100", S100);
            y = AddGroup(y, "İskele", Isk);
            y = AddGroup(y, "Genel", Gen);
            y = AddGroup(y, "Deneme", AltDeneme);
            EnsureDarkFiller();
        }

        private int AddGroup(int y, string title, (string label, string cmd)[] items)
        {
            y = ToolPaletteUi.AddGroupHeader(this, title, y);
            foreach (var it in items)
                y = ToolPaletteUi.AddToolRow(this, y, it.label, StaToolIcons.ForCommand(it.cmd), it.cmd);
            return y + 6;
        }
    }

    /// <summary>STA kesit/kiris çizimini Beykent katman ve stillerine çevirir (KIRISDUZELT).</summary>
    internal sealed class KirisDuzeltPaletteControl : DarkPaletteUserControl
    {
        public KirisDuzeltPaletteControl()
        {
            AutoScroll = true;
            Padding = new Padding(4, 6, 4, 6);

            int y = 6;
            y = ToolPaletteUi.AddGroupHeader(this, "Kiriş / Metraj", y);
            y = ToolPaletteUi.AddToolRow(this, y, "Kiriş düzelt", StaToolIcons.ForCommand("KIRISDUZELT"), "KIRISDUZELT");
            y = ToolPaletteUi.AddToolRow(this, y, "Temel kiriş düzelt", StaToolIcons.ForCommand("TEMELKIRISDUZELT"), "TEMELKIRISDUZELT");
            y = ToolPaletteUi.AddToolRow(this, y, "Metraj", StaToolIcons.ForCommand("METRAJ"), "METRAJ");
            EnsureDarkFiller();
        }
    }
}
