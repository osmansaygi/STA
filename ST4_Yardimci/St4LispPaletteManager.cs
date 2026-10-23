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
    /// BETONARME TOOL paleti. Boyut komut sayısına sabit (kaydırma yok).
    /// Konum / dock / auto-hide / yazı gizle kalıcı. Sağ tık → Yazıları gizle.
    /// </summary>
    internal static class St4LispPaletteManager
    {
        private const int RowPitch = 34;
        private const int IconOnlyWidth = 52;
        /// <summary>PaletteSet başlık payı; içerik satırlarının üstüne eklenir (alt boşluk olmasın diye düşük).</summary>
        private const int ChromeHeight = 28;
        private const int MarginX = 12;
        private const int MarginY = 80;
        private const int GWL_STYLE = -16;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>Sabit Guid — STA / üst şerit PaletteSet kimlikleriyle çakışmasın.</summary>
        private static readonly Guid PaletteGuid = new Guid("C4E91B02-7A35-4F8D-B6E1-3D9A0F52C814");

        private static PaletteSet _palette;
        private static St4LispPaletteControl _control;
        private static bool _suppressSave;
        private static bool _hideLabels;
        private static bool _autoHide;
        private static Timer _sizeLockTimer;
        private static bool _layoutCached;
        private static DockSides _cachedDock = DockSides.None;
        private static bool _cachedRolled;
        private static int _cachedX, _cachedY;

        public static bool HideLabels
        {
            get => _hideLabels;
            set
            {
                if (_hideLabels == value) return;
                _hideLabels = value;
                ApplyWidthAndLabels();
                SaveLayout();
            }
        }

        public static int PreferredWidth =>
            _hideLabels ? IconOnlyWidth : MeasureContentWidth();

        /// <summary>Yalnız komut satırları — yeni komut eklenince büyür.</summary>
        public static int PreferredHeight =>
            ChromeHeight + BaCommandIcons.Tools.Length * RowPitch;

        public static void Show()
        {
            if (!BaHomeRibbonCatalog.LegacyPaletteEnabled)
            {
                DisposePalette();
                return;
            }
            try
            {
                LoadPrefs();

                if (_palette != null)
                {
                    try
                    {
                        BringIntoView(unroll: true);
                        return;
                    }
                    catch
                    {
                        DisposePalette();
                    }
                }

                // cmd boşluksuz. "BETONARME TOOL" açılışta BETONARME + TOOL komutlarına bölünür.
                _palette = new PaletteSet("BETONARME TOOL", "BAPANEL", PaletteGuid)
                {
                    Style = PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.UsePaletteNameAsTitleForSingle,
                    KeepFocus = false,
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
                };
                LockPaletteSizeLimits();

                _control = new St4LispPaletteControl();
                _palette.Add("BETONARME TOOL", _control);
                TryHideNameTab(_palette);

                _palette.SizeChanged += (s, e) =>
                {
                    if (_suppressSave) return;
                    EnforceFixedSize();
                    ScheduleSizeLock();
                    SaveLayout();
                };
                _palette.StateChanged += (s, e) =>
                {
                    if (_suppressSave) return;
                    CaptureAutoHideFromUi();
                    SaveLayout();
                };
                _palette.PaletteSetHostMoved += (s, e) =>
                {
                    if (_suppressSave) return;
                    SaveLayout();
                };

                _suppressSave = true;
                try
                {
                    _palette.Size = new Size(PreferredWidth, PreferredHeight);
                    _palette.Visible = true;
                    if (!TryRestoreLayout())
                        PlaceDefault();
                    EnforceFixedSize();
                    _control?.ApplyHideLabels(_hideLabels);
                    BringIntoView(unroll: true);
                    ApplyAutoHideSetting();
                    DisablePaletteResizeGrip();
                }
                finally { _suppressSave = false; }

                SaveLayout();

                try
                {
                    AcApp.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage("\nBETONARME TOOL paleti acildi.");
                }
                catch { }
            }
            catch (Exception ex)
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
            _suppressSave = true;
            // AutoCAD kapanırken PaletteSet.Dock yerel çubuğu ölü oluyor.
            // IsFloating erişim ihlali catch ile yakalanmaz, süreç düşer.
            // Son bilinen yerleşim yazılır; yerli palete dokunulmaz.
            try { if (_layoutCached) WriteLayoutFile(_cachedDock, _cachedRolled, _cachedX, _cachedY); } catch { }
            StopSizeLockTimer();
            _palette = null;
            _control = null;
        }

        private static void DisposePalette()
        {
            StopSizeLockTimer();
            if (_palette == null) return;
            try { _palette.Visible = false; } catch { }
            // Dispose AutoCAD kapanışında göçürebilir — yalnız referansı bırak.
            _palette = null;
            _control = null;
        }

        private static void TryHideNameTab(PaletteSet ps)
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

        private static void ApplyWidthAndLabels()
        {
            if (_control != null)
                _control.ApplyHideLabels(_hideLabels);
            if (_palette == null) return;
            _suppressSave = true;
            try
            {
                LockPaletteSizeLimits();
                _palette.Size = new Size(PreferredWidth, PreferredHeight);
                DisablePaletteResizeGrip();
            }
            catch { }
            finally { _suppressSave = false; }
        }

        private static void LockPaletteSizeLimits()
        {
            if (_palette == null) return;
            var sz = new Size(PreferredWidth, PreferredHeight);
            try { _palette.MinimumSize = sz; } catch { }
            // MaximumSize bazı sürümlerde var — kenardan büyütmeyi keser
            try
            {
                var prop = _palette.GetType().GetProperty("MaximumSize");
                if (prop != null && prop.CanWrite)
                    prop.SetValue(_palette, sz, null);
            }
            catch { }
        }

        /// <summary>Kullanıcı sürükleyip büyütmesin; W/H her zaman komut listesine sabit.</summary>
        private static void EnforceFixedSize()
        {
            if (_palette == null) return;
            int wantW = PreferredWidth;
            int wantH = PreferredHeight;
            try
            {
                if (Rolled()) return;
                var sz = _palette.Size;
                if (Math.Abs(sz.Width - wantW) <= 1 && Math.Abs(sz.Height - wantH) <= 1)
                {
                    LockPaletteSizeLimits();
                    return;
                }
                _suppressSave = true;
                try
                {
                    LockPaletteSizeLimits();
                    _palette.Size = new Size(wantW, wantH);
                }
                finally { _suppressSave = false; }
            }
            catch { }
        }

        private static void ScheduleSizeLock()
        {
            StopSizeLockTimer();
            _sizeLockTimer = new Timer { Interval = 80 };
            _sizeLockTimer.Tick += (s, e) =>
            {
                StopSizeLockTimer();
                try
                {
                    EnforceFixedSize();
                    DisablePaletteResizeGrip();
                }
                catch { }
            };
            _sizeLockTimer.Start();
        }

        private static void StopSizeLockTimer()
        {
            if (_sizeLockTimer == null) return;
            try { _sizeLockTimer.Stop(); _sizeLockTimer.Dispose(); } catch { }
            _sizeLockTimer = null;
        }

        /// <summary>Kayan palet penceresinden WS_THICKFRAME kaldır — kenar tutamacı olmasın.</summary>
        private static void DisablePaletteResizeGrip()
        {
            // 64-bit AutoCAD'de GetWindowLong/SetWindowLong süreç düşürebilir.
        }

        private static IntPtr FindPaletteHwnd()
        {
            try
            {
                if (_control != null && _control.IsHandleCreated)
                {
                    IntPtr h = _control.Handle;
                    IntPtr main = IntPtr.Zero;
                    try { main = AcApp.MainWindow.Handle; } catch { }
                    for (int i = 0; i < 12 && h != IntPtr.Zero && h != main; i++)
                    {
                        var sb = new System.Text.StringBuilder(256);
                        if (Native.GetWindowText(h, sb, sb.Capacity) > 0 &&
                            sb.ToString().IndexOf("BETONARME TOOL", StringComparison.OrdinalIgnoreCase) >= 0)
                            return h;
                        h = Native.GetParent(h);
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private static bool Rolled()
        {
            try { return _palette != null && _palette.RolledUp; }
            catch { return false; }
        }

        private static void ApplyAutoHideSetting()
        {
            if (_palette == null) return;
            try { _palette.AutoRollUp = _autoHide; } catch { }
        }

        private static void CaptureAutoHideFromUi()
        {
            if (_palette == null) return;
            try { _autoHide = _palette.AutoRollUp; } catch { }
        }

        private static int MeasureContentWidth()
        {
            int max = 80;
            try
            {
                using (var font = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                {
                    foreach (BaTool t in BaCommandIcons.Tools)
                    {
                        Size s = TextRenderer.MeasureText(t.Label ?? "", font);
                        if (s.Width > max) max = s.Width;
                    }
                }
            }
            catch { max = 120; }
            return Math.Min(200, Math.Max(148, 4 + 28 + 4 + max + 14));
        }

        private static string LayoutPath => Path.Combine(BaIni.Dir(), "palette_layout.txt");

        private static void LoadPrefs()
        {
            try
            {
                var m = BaIni.Read("palette_prefs.txt");
                _hideLabels = BaIni.GetB(m, "HideLabels", _hideLabels);
                _autoHide = BaIni.GetB(m, "AutoHide", _autoHide);
            }
            catch { }
        }

        private static void SavePrefs()
        {
            BaIni.Write("palette_prefs.txt",
                "HideLabels=" + BaIni.B(_hideLabels),
                "AutoHide=" + BaIni.B(_autoHide));
        }

        private static void SaveLayout()
        {
            if (_suppressSave || _palette == null) return;
            try
            {
                DockSides dock = _cachedDock;
                bool rolled = _cachedRolled;
                int x = _cachedX;
                int y = _cachedY;
                try
                {
                    dock = _palette.Dock;
                    rolled = _palette.RolledUp;
                    var loc = _palette.Location;
                    x = loc.X;
                    y = loc.Y;
                    _cachedDock = dock;
                    _cachedRolled = rolled;
                    _cachedX = x;
                    _cachedY = y;
                    _layoutCached = true;
                }
                catch { }
                CaptureAutoHideFromUi();
                WriteLayoutFile(dock, rolled, x, y);
            }
            catch { }
        }

        private static void WriteLayoutFile(DockSides dock, bool rolled, int x, int y)
        {
            int w = PreferredWidth;
            int h = PreferredHeight;
            File.WriteAllText(LayoutPath,
                "dock=" + dock + "\r\n" +
                "roll=" + (rolled ? "1" : "0") + "\r\n" +
                "autohide=" + (_autoHide ? "1" : "0") + "\r\n" +
                "hidelabels=" + (_hideLabels ? "1" : "0") + "\r\n" +
                "x=" + x.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "y=" + y.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "w=" + w.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "h=" + h.ToString(CultureInfo.InvariantCulture) + "\r\n");
            SavePrefs();
        }

        private static bool TryRestoreLayout()
        {
            if (_palette == null) return false;
            try
            {
                string path = LayoutPath;
                if (!File.Exists(path)) return false;

                DockSides dock = DockSides.None;
                bool gotDock = false;
                bool rolled = false;
                int x = 0, y = 0;
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
                    else if (key == "roll") rolled = val == "1";
                    else if (key == "autohide") _autoHide = val == "1";
                    else if (key == "hidelabels") _hideLabels = val == "1";
                    else if (key == "x") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                    else if (key == "y") int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                }
                if (!gotDock) return false;

                int w = PreferredWidth;
                int h = PreferredHeight;

                if (dock == DockSides.None)
                {
                    if (!IsOnAnyScreen(x, y, w))
                        return false;
                    try { _palette.Dock = DockSides.None; } catch { }
                    _palette.Location = new Point(x, y);
                    _palette.Size = new Size(w, h);
                }
                else
                {
                    try { _palette.Dock = dock; } catch { return false; }
                    try { _palette.Size = new Size(w, h); } catch { }
                }

                try { if (rolled) _palette.RolledUp = true; } catch { }
                return true;
            }
            catch { return false; }
        }

        private static void BringIntoView(bool unroll)
        {
            if (_palette == null) return;
            bool prev = _suppressSave;
            _suppressSave = true;
            try
            {
                try { _palette.Visible = true; } catch { }
                if (unroll)
                {
                    try { _palette.RolledUp = false; } catch { }
                }

                DockSides dock = DockSides.None;
                try { dock = _palette.Dock; } catch { }
                if (dock == DockSides.None)
                {
                    var loc = _palette.Location;
                    if (!IsOnAnyScreen(loc.X, loc.Y, PreferredWidth))
                        PlaceDefault();
                }

                EnforceFixedSize();
                DisablePaletteResizeGrip();
            }
            finally { _suppressSave = prev; }
        }

        private static bool IsOnAnyScreen(int x, int y, int w)
        {
            var title = new Rectangle(x, y, Math.Max(w, 48), 36);
            try
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(title))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void PlaceDefault()
        {
            if (_palette == null) return;
            try { _palette.Dock = DockSides.None; } catch { }
            try { _palette.RolledUp = false; } catch { }
            Rectangle host = GetAcadWindowBounds();
            int palW = PreferredWidth;
            int palH = PreferredHeight;
            Rectangle wa;
            try { wa = Screen.FromPoint(new Point(host.Left + host.Width / 2, host.Top + 40)).WorkingArea; }
            catch { wa = Screen.PrimaryScreen.WorkingArea; }

            int x = wa.Right - palW - MarginX;
            int y = wa.Top + MarginY;
            if (x < wa.Left + MarginX) x = wa.Left + MarginX;
            if (y + Math.Min(palH, 200) > wa.Bottom - 40)
                y = Math.Max(wa.Top + MarginX, wa.Bottom - Math.Min(palH, 400) - 40);
            _palette.Location = new Point(x, y);
            _palette.Size = new Size(palW, palH);
        }

        private static Rectangle GetAcadWindowBounds()
        {
            try
            {
                IntPtr hwnd = AcApp.MainWindow.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var r = new Native.RECT();
                    if (Native.GetWindowRect(hwnd, out r))
                        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                }
            }
            catch { }
            try { return Screen.PrimaryScreen.WorkingArea; }
            catch { return new Rectangle(0, 0, 1280, 800); }
        }

        private static class Native
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr GetParent(IntPtr hWnd);

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left, Top, Right, Bottom;
            }
        }
    }

    internal sealed class St4LispPaletteControl : DarkPaletteUserControl
    {
        private readonly TableLayoutPanel _table;
        private readonly Label[] _labels;
        private readonly ToolTip _tips;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _mnuHideLabels;

        public St4LispPaletteControl()
        {
            AutoScroll = false;
            Padding = new Padding(0);

            int n = BaCommandIcons.Tools.Length;
            _labels = new Label[n];
            _tips = new ToolTip { ShowAlways = true };

            _mnuHideLabels = new ToolStripMenuItem("Yazıları gizle")
            {
                CheckOnClick = true,
                Checked = St4LispPaletteManager.HideLabels
            };
            _mnuHideLabels.CheckedChanged += (s, e) =>
            {
                St4LispPaletteManager.HideLabels = _mnuHideLabels.Checked;
            };
            _menu = new ContextMenuStrip();
            _menu.Items.Add(_mnuHideLabels);
            var iconMenu = new ToolStripMenuItem("Simge tasarımı");
            var monolitItem = new ToolStripMenuItem("Monolit");
            var classicItem = new ToolStripMenuItem("Klasik");
            monolitItem.Click += (s, e) => { BaCommandIcons.SetMonolit(true); ReloadIcons(); };
            classicItem.Click += (s, e) => { BaCommandIcons.SetMonolit(false); ReloadIcons(); };
            iconMenu.DropDownItems.Add(monolitItem);
            iconMenu.DropDownItems.Add(classicItem);
            _menu.Items.Add(iconMenu);
            _menu.Opening += (s, e) =>
            {
                _mnuHideLabels.Checked = St4LispPaletteManager.HideLabels;
                monolitItem.Checked = BaCommandIcons.UseMonolit;
                classicItem.Checked = !BaCommandIcons.UseMonolit;
            };
            ContextMenuStrip = _menu;

            _table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoScroll = false,
                ColumnCount = 1,
                RowCount = n,
                BackColor = AcadPaletteTheme.Bg,
                Padding = new Padding(2, 0, 2, 0),
                Margin = new Padding(0),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                ContextMenuStrip = _menu
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < n; i++)
            {
                _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
                BaTool tool = BaCommandIcons.Tools[i];
                _table.Controls.Add(MakeRow(tool, i), 0, i);
            }

            Controls.Add(_table);
            ApplyHideLabels(St4LispPaletteManager.HideLabels);
        }

        private void ReloadIcons()
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                if (_labels[i]?.Parent is Panel row && row.Controls[0] is PictureBox pic)
                {
                    var tool = BaCommandIcons.Tools[i];
                    Image old = pic.Image;
                    pic.Image = BaCommandIcons.Load(tool.Cmd, tool.Resource);
                    old?.Dispose();
                }
            }
        }

        public void ApplyHideLabels(bool hide)
        {
            if (_mnuHideLabels != null && _mnuHideLabels.Checked != hide)
                _mnuHideLabels.Checked = hide;

            for (int i = 0; i < _labels.Length; i++)
            {
                Label lb = _labels[i];
                if (lb == null) continue;
                lb.Visible = !hide;
                if (lb.Parent is Panel row && row.Controls.Count > 0)
                {
                    Control pic = row.Controls[0];
                    if (hide)
                        pic.Location = new Point(Math.Max(2, (row.ClientSize.Width - 28) / 2), 3);
                    else
                        pic.Location = new Point(4, 3);
                }
            }
        }

        private Panel MakeRow(BaTool tool, int index)
        {
            var row = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(1, 0, 1, 0),
                BackColor = AcadPaletteTheme.Bg,
                Cursor = Cursors.Hand,
                Tag = tool.Cmd,
                ContextMenuStrip = _menu
            };

            var pic = new PictureBox
            {
                Size = new Size(28, 28),
                Location = new Point(4, 3),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = BaCommandIcons.Load(tool.Cmd, tool.Resource),
                Cursor = Cursors.Hand,
                ContextMenuStrip = _menu
            };
            _tips.SetToolTip(pic, tool.Label);

            var txt = new Label
            {
                Text = tool.Label,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = AcadPaletteTheme.Text,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(36, 8),
                Height = 18,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                ContextMenuStrip = _menu
            };
            txt.Width = Math.Max(40, row.Width - 42);
            _labels[index] = txt;
            _tips.SetToolTip(txt, tool.Label);
            _tips.SetToolTip(row, tool.Label);

            row.Controls.Add(pic);
            row.Controls.Add(txt);
            row.Resize += (s, e) =>
            {
                txt.Width = Math.Max(40, row.Width - 42);
                if (St4LispPaletteManager.HideLabels)
                    pic.Location = new Point(Math.Max(2, (row.ClientSize.Width - 28) / 2), 3);
                else
                    pic.Location = new Point(4, 3);
            };

            void Enter(object s, EventArgs e) => row.BackColor = AcadPaletteTheme.BgHover;
            void Leave(object s, EventArgs e)
            {
                Point p = row.PointToClient(Cursor.Position);
                if (row.ClientRectangle.Contains(p)) return;
                row.BackColor = AcadPaletteTheme.Bg;
            }
            void Down(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    row.BackColor = AcadPaletteTheme.BgPressed;
            }
            void Up(object s, MouseEventArgs e)
            {
                Point p = row.PointToClient(Cursor.Position);
                row.BackColor = row.ClientRectangle.Contains(p)
                    ? AcadPaletteTheme.BgHover
                    : AcadPaletteTheme.Bg;
            }

            foreach (Control c in new Control[] { row, pic, txt })
            {
                c.MouseEnter += Enter;
                c.MouseLeave += Leave;
                c.MouseDown += Down;
                c.MouseUp += Up;
            }
            row.Click += OnToolClick;
            pic.Click += (s, e) => OnToolClick(row, e);
            txt.Click += (s, e) => OnToolClick(row, e);
            return row;
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
    }
}
