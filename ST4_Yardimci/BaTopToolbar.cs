using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Eski yazısız simge şeridi — açılmaz; AutoCAD hatırlarsa kapatılır.
    /// </summary>
    internal static class BaTopToolbar
    {
        const string Title = "BETONARME";
        const int Icon = 32;
        const int Gap = 4;
        const int Pad = 6;
        const int BarThick = 44;

        /// <summary>Sabit Guid — BAPANEL / STA paletleriyle kimlik çakışmasın.</summary>
        static readonly Guid StripGuid = new Guid("E7A2D4F1-9C08-4B3E-A15D-6F80B2C39E57");

        static PaletteSet _strip;
        static BaIconStripControl _icons;
        static bool _suppressSave;
        static bool _normalizing;

        public static void Hide()
        {
            // COM toolbar Delete ve BAUST PaletteSet oluşturma ofiste erişim ihlali yapıyor.
            if (_strip == null) return;
            try { _strip.Visible = false; } catch { }
        }

        public static void Show()
        {
            Hide();
        }

        static void EnsureStrip()
        {
            if (_strip != null) return;
            try
            {

                // "BETONARME" tek başına komut değil; açılışta Unknown command olmasın.
                // ShowTabForSingle yok — sağdaki boş/sekme (simgesiz) tutamak çıkmasın.
                _strip = new PaletteSet(Title, "BAUST", StripGuid)
                {
                    Style = PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.UsePaletteNameAsTitleForSingle,
                    MinimumSize = new Size(BarThick, BarThick),
                    KeepFocus = false,
                    DockEnabled = DockSides.Top | DockSides.Bottom | DockSides.Left | DockSides.Right
                };
                _icons = new BaIconStripControl();
                _strip.Add(Title, _icons);
                TryHideNameTab(_strip);
                _strip.SizeChanged += (s, e) =>
                {
                    if (_normalizing) return;
                    NormalizeThickness();
                    Reflow();
                    SaveLayout();
                };
                _strip.StateChanged += (s, e) => { NormalizeThickness(); Reflow(); SaveLayout(); };
                _strip.PaletteSetHostMoved += (s, e) => SaveLayout();

                _suppressSave = true;
                try
                {
                    _strip.Visible = false;
                }
                finally { _suppressSave = false; }
            }
            catch { }
        }

        public static void Shutdown()
        {
            // AutoCAD kapanırken PaletteSet.Dispose süreç düşürebilir — yalnız bırak.
            try { SaveLayout(); } catch { }
            _strip = null;
            _icons = null;
        }

        static void TryHideNameTab(PaletteSet ps)
        {
            if (ps == null) return;
            foreach (string name in new[] { "NameTabIsVisible", "ShowNameTab" })
            {
                try
                {
                    var p = ps.GetType().GetProperty(name);
                    if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) continue;
                    p.SetValue(ps, false, null);
                    return;
                }
                catch { }
            }
        }

        static string LayoutPath => Path.Combine(BaIni.Dir(), "iconbar_layout.txt");

        static bool IsVertical()
        {
            if (_strip == null) return false;
            try
            {
                DockSides d = _strip.Dock;
                return d == DockSides.Left || d == DockSides.Right;
            }
            catch { return false; }
        }

        static void Reflow()
        {
            if (_icons == null) return;
            try { _icons.ApplyOrientation(IsVertical()); } catch { }
        }

        /// <summary>Simgeler 32px; şerit kalınlığı sabit. AutoCAD paleti şişirse geri alınır.</summary>
        static void NormalizeThickness()
        {
            if (_strip == null || _normalizing) return;
            try
            {
                _normalizing = true;
                DockSides d = _strip.Dock;
                int n = IconCount();
                int along = Pad * 2 + n * Icon + Math.Max(0, n - 1) * Gap;
                if (d == DockSides.Left || d == DockSides.Right)
                {
                    if (_strip.Size.Width != BarThick)
                        _strip.Size = new Size(BarThick, Math.Max(_strip.Size.Height, along));
                }
                else if (d == DockSides.Top || d == DockSides.Bottom || d == DockSides.None)
                {
                    if (_strip.Size.Height != BarThick)
                        _strip.Size = new Size(Math.Max(_strip.Size.Width, along), BarThick);
                }
            }
            catch { }
            finally { _normalizing = false; }
        }

        static int IconCount()
        {
            try
            {
                if (_icons != null) return Math.Max(1, _icons.Controls.Count);
            }
            catch { }
            return Math.Max(1, BaCommandIcons.Tools.Length);
        }

        static void PlaceDefault()
        {
            if (_strip == null) return;
            int n = IconCount();
            int along = Pad * 2 + n * Icon + Math.Max(0, n - 1) * Gap;
            try { _strip.Dock = DockSides.Top; } catch { }
            try { _strip.Size = new Size(along, BarThick); } catch { }
            Reflow();
        }

        static bool TryRestore()
        {
            if (_strip == null || !File.Exists(LayoutPath)) return false;
            try
            {
                int x = 0, y = 0, w = 0, h = 0;
                DockSides dock = DockSides.None;
                bool got = false;
                foreach (string raw in File.ReadAllLines(LayoutPath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    if (key == "dock" && Enum.TryParse(val, out DockSides d))
                    {
                        dock = d;
                        got = true;
                    }
                    else if (key == "x") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                    else if (key == "y") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                    else if (key == "w") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out w);
                    else if (key == "h") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
                }
                if (!got) return false;
                int n = IconCount();
                int along = Pad * 2 + n * Icon + Math.Max(0, n - 1) * Gap;
                if (dock == DockSides.None)
                {
                    if (w < 40) w = along;
                    if (h < 40) h = BarThick;
                    try { _strip.Dock = DockSides.None; } catch { }
                    _strip.Location = new Point(x, y);
                    _strip.Size = new Size(w, BarThick);
                }
                else
                {
                    try { _strip.Dock = dock; }
                    catch { return false; }
                    if (dock == DockSides.Left || dock == DockSides.Right)
                        _strip.Size = new Size(BarThick, along);
                    else
                        _strip.Size = new Size(Math.Max(w, along), BarThick);
                }
                return true;
            }
            catch { return false; }
        }

        static void SaveLayout()
        {
            if (_suppressSave || _strip == null || !_strip.Visible) return;
            try
            {
                DockSides dock = DockSides.None;
                try { dock = _strip.Dock; } catch { }
                var loc = _strip.Location;
                var sz = _strip.Size;
                File.WriteAllText(LayoutPath,
                    "dock=" + dock + "\r\n" +
                    "x=" + loc.X.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "y=" + loc.Y.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "w=" + sz.Width.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "h=" + sz.Height.ToString(CultureInfo.InvariantCulture) + "\r\n");
            }
            catch { }
        }

        /// <summary>COM tb.Delete ofiste yakalanamayan erişim ihlali yapıyor — çağrılmaz.</summary>
        static void TryDeleteLegacyToolbar()
        {
        }
    }

    internal sealed class BaIconStripControl : DarkPaletteUserControl
    {
        readonly PictureBox[] _pics;

        public BaIconStripControl()
        {
            AutoScroll = false;
            var tip = new ToolTip();
            var list = new System.Collections.Generic.List<PictureBox>();
            for (int i = 0; i < BaCommandIcons.Tools.Length; i++)
            {
                BaTool tool = BaCommandIcons.Tools[i];
                Image img = BaCommandIcons.Load(tool.Cmd, tool.Resource);
                if (img == null) continue; // simgesiz düğme eklenmez
                var pic = new PictureBox
                {
                    Size = new Size(32, 32),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent,
                    Image = img,
                    Cursor = Cursors.Hand,
                    Tag = tool.Cmd
                };
                tip.SetToolTip(pic, tool.Label);
                pic.Click += OnClick;
                Controls.Add(pic);
                list.Add(pic);
            }
            _pics = list.ToArray();
            ApplyOrientation(false);
        }

        public void ApplyOrientation(bool vertical)
        {
            const int icon = 32;
            const int gap = 4;
            const int pad = 6;
            for (int i = 0; i < _pics.Length; i++)
            {
                _pics[i].Size = new Size(icon, icon);
                _pics[i].Location = vertical
                    ? new Point(pad, pad + i * (icon + gap))
                    : new Point(pad + i * (icon + gap), pad);
            }
        }

        static void OnClick(object sender, EventArgs e)
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
