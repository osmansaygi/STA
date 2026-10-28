using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Plan ID ve Yardımcı komutlarında çizim 5 sn'yi aşınca "Çiziliyor..." penceresi. Komut başlayınca ayrı bir STA iş parçacığı
    /// çizim süresini sayar: nokta/seçim/değer istemi (Editor Prompting/Prompted olayları) ve kalıcı diyalog (AutoCAD ana penceresi
    /// devre dışı) sırasında sayaç durur ve pencere gizlenir; çizim süresi 5 sn'yi aşınca pencere açılır, komut bitince kapanır.
    /// (Windows "yanıt vermiyor" ölçütü kullanılmaz: AutoCAD uzun işte ESC için mesaj kuyruğuna bakar.) Kalıp planında
    /// <see cref="KalipIlerleme"/> yüzdesi, diğerlerinde kayan çubuk. İzleyici AutoCAD nesnelerine dokunmaz.
    /// </summary>
    internal static class CizimIzleyici
    {
        /// <summary>Komutun çizim süresi (giriş beklemesi hariç) bunu aşınca pencere açılır (ms).</summary>
        private const long GosterSuresiMs = 5000;

        private static readonly object _logKilit = new object();
        private static readonly string _logYolu = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ST4_CizimIzleyici.log");

        /// <summary>Tanı günlüğü (%TEMP%ST4_CizimIzleyici.log, 256 KB üstünde baştan). Hata yutar.</summary>
        internal static void Log(string s)
        {
            try
            {
                lock (_logKilit)
                {
                    var fi = new System.IO.FileInfo(_logYolu);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    System.IO.File.AppendAllText(_logYolu, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + s + Environment.NewLine);
                }
            }
            catch { }
        }

        private static bool _kurulu;
        private static IntPtr _acadHwnd;
        private static Izleme _izleme;
        private static int _komutDerinligi;
        private static HashSet<string> _bizimKomutlar;
        private static int _taranmisAssemblySayisi = -1;

        /// <summary>Plan Initialize'dan (ana iş parçacığı) çağrılır. Core Console'da kurulmaz.</summary>
        internal static void Kur()
        {
            if (_kurulu) return;
            try
            {
                if (Process.GetCurrentProcess().ProcessName.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0) return;
                DocumentCollection dm = AcApp.DocumentManager;
                foreach (Document d in dm) Bagla(d);
                dm.DocumentCreated += OnDocumentCreated;
                _kurulu = true;
            }
            catch (System.Exception ex) { Log("Kur hata: " + ex.Message); }
            Log("Kur kurulu=" + _kurulu);
        }

        internal static void Kaldir()
        {
            if (!_kurulu) return;
            try
            {
                DocumentCollection dm = AcApp.DocumentManager;
                dm.DocumentCreated -= OnDocumentCreated;
                foreach (Document d in dm) Coz(d);
            }
            catch { }
            Durdur();
            _kurulu = false;
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e) => Bagla(e.Document);

        private static void Bagla(Document d)
        {
            if (d == null) return;
            Coz(d);
            d.CommandWillStart += OnCommandWillStart;
            d.CommandEnded += OnCommandBitti;
            d.CommandCancelled += OnCommandBitti;
            d.CommandFailed += OnCommandBitti;
            Editor ed = d.Editor;
            ed.PromptingForAngle += Bekle; ed.PromptedForAngle += Surdur;
            ed.PromptingForCorner += Bekle; ed.PromptedForCorner += Surdur;
            ed.PromptingForDistance += Bekle; ed.PromptedForDistance += Surdur;
            ed.PromptingForDouble += Bekle; ed.PromptedForDouble += Surdur;
            ed.PromptingForEntity += Bekle; ed.PromptedForEntity += Surdur;
            ed.PromptingForInteger += Bekle; ed.PromptedForInteger += Surdur;
            ed.PromptingForKeyword += Bekle; ed.PromptedForKeyword += Surdur;
            ed.PromptingForNestedEntity += Bekle; ed.PromptedForNestedEntity += Surdur;
            ed.PromptingForPoint += Bekle; ed.PromptedForPoint += Surdur;
            ed.PromptingForSelection += Bekle; ed.PromptedForSelection += Surdur;
            ed.PromptingForString += Bekle; ed.PromptedForString += Surdur;
        }

        /// <summary>Kullanıcıdan giriş bekleniyor: çizim süresi saymaz, pencere gizlenir.</summary>
        private static void Bekle(object sender, EventArgs e) => _izleme?.GirisBekliyor(true);

        private static void Surdur(object sender, EventArgs e) => _izleme?.GirisBekliyor(false);

        private static void Coz(Document d)
        {
            if (d == null) return;
            try
            {
                d.CommandWillStart -= OnCommandWillStart;
                d.CommandEnded -= OnCommandBitti;
                d.CommandCancelled -= OnCommandBitti;
                d.CommandFailed -= OnCommandBitti;
                Editor ed = d.Editor;
                ed.PromptingForAngle -= Bekle; ed.PromptedForAngle -= Surdur;
                ed.PromptingForCorner -= Bekle; ed.PromptedForCorner -= Surdur;
                ed.PromptingForDistance -= Bekle; ed.PromptedForDistance -= Surdur;
                ed.PromptingForDouble -= Bekle; ed.PromptedForDouble -= Surdur;
                ed.PromptingForEntity -= Bekle; ed.PromptedForEntity -= Surdur;
                ed.PromptingForInteger -= Bekle; ed.PromptedForInteger -= Surdur;
                ed.PromptingForKeyword -= Bekle; ed.PromptedForKeyword -= Surdur;
                ed.PromptingForNestedEntity -= Bekle; ed.PromptedForNestedEntity -= Surdur;
                ed.PromptingForPoint -= Bekle; ed.PromptedForPoint -= Surdur;
                ed.PromptingForSelection -= Bekle; ed.PromptedForSelection -= Surdur;
                ed.PromptingForString -= Bekle; ed.PromptedForString -= Surdur;
            }
            catch { }
        }

        private static void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            try
            {
                if (_izleme != null) { _komutDerinligi++; return; }
                bool bizim = BizimKomut(e.GlobalCommandName);
                Log("komut " + e.GlobalCommandName + " bizim=" + bizim);
                if (!bizim) return;
                if (_acadHwnd == IntPtr.Zero) _acadHwnd = AcadHwnd();
                if (_acadHwnd == IntPtr.Zero) return;
                _komutDerinligi = 1;
                _izleme = new Izleme(_acadHwnd, e.GlobalCommandName);
            }
            catch (System.Exception ex) { Log("komut basla hata: " + ex.Message); }
        }

        private static void OnCommandBitti(object sender, CommandEventArgs e)
        {
            if (_izleme == null) return;
            if (--_komutDerinligi > 0) return;
            Durdur();
        }

        private static bool _kalipBaslatti;

        /// <summary>
        /// <see cref="KalipIlerleme"/> başladı (istemler bitti, çizim sürüyor): komut olayından izleme yoksa burada başlatılır.
        /// Ana iş parçacığı. Core Console'da bir şey yapmaz.
        /// </summary>
        internal static void KalipBasladi(string baslik)
        {
            try
            {
                Log("kalip basladi " + baslik + " izleme=" + (_izleme != null));
                if (_izleme != null) { _izleme.GirisBekliyor(false); return; }
                if (Process.GetCurrentProcess().ProcessName.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0) return;
                if (_acadHwnd == IntPtr.Zero) _acadHwnd = AcadHwnd();
                if (_acadHwnd == IntPtr.Zero) return;
                _izleme = new Izleme(_acadHwnd, baslik);
                _kalipBaslatti = true;
            }
            catch (System.Exception ex) { Log("kalip basla hata: " + ex.Message); }
        }

        /// <summary><see cref="KalipIlerleme"/> bitti: izlemeyi bu sınıf başlattıysa kapatır (komut olayıyla başlayan komut sonunda kapanır).</summary>
        internal static void KalipBitti()
        {
            if (!_kalipBaslatti) return;
            _kalipBaslatti = false;
            Durdur();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static IntPtr AcadHwnd() => AcApp.MainWindow.Handle;

        private static void Durdur()
        {
            if (_izleme != null) Log("durdur");
            Izleme iz = _izleme;
            _izleme = null;
            _komutDerinligi = 0;
            _kalipBaslatti = false;
            iz?.Bitir();
        }

        /// <summary>Komut Plan ID veya Yardımcı DLL'inde mi (CommandMethod adları; yeni DLL yüklendikçe yeniden taranır).</summary>
        private static bool BizimKomut(string ad)
        {
            if (string.IsNullOrEmpty(ad)) return false;
            Assembly[] asm = AppDomain.CurrentDomain.GetAssemblies();
            if (_bizimKomutlar == null || asm.Length != _taranmisAssemblySayisi)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Assembly a in asm)
                {
                    string n = a.GetName().Name ?? string.Empty;
                    if (!n.StartsWith("ST4_Plan_ID_Ciz", StringComparison.OrdinalIgnoreCase)
                        && !n.StartsWith("ST4_Yardimci", StringComparison.OrdinalIgnoreCase)) continue;
                    Type[] types;
                    try { types = a.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    foreach (Type t in types)
                    {
                        if (t == null) continue;
                        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                            foreach (CommandMethodAttribute c in m.GetCustomAttributes(typeof(CommandMethodAttribute), false))
                                if (!string.IsNullOrEmpty(c.GlobalName)) set.Add(c.GlobalName);
                    }
                }
                _bizimKomutlar = set;
                _taranmisAssemblySayisi = asm.Length;
            }
            return _bizimKomutlar.Contains(ad);
        }

        /// <summary>Bir komut çalışması: kendi iş parçacığı ve mesaj döngüsü.</summary>
        private sealed class Izleme
        {
            private readonly IntPtr _acad;
            private readonly string _komut;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private volatile bool _bitti;
            private volatile bool _girisBekliyor;
            // Giriş beklemesi ve kalıcı diyalog dışındaki (çizim) süre; yalnız izleyici iş parçacığı yazar.
            private long _cizimMs;
            private readonly Thread _thread;

            internal Izleme(IntPtr acad, string komut)
            {
                _acad = acad;
                _komut = komut;
                _thread = new Thread(Calis) { IsBackground = true, Name = "CizimIzleyici" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }

            internal void GirisBekliyor(bool b) => _girisBekliyor = b;

            internal void Bitir()
            {
                _bitti = true;
                try { _thread.Join(1500); } catch { }
            }

            private void Calis()
            {
                CiziliyorFormu form = null;
                try
                {
                    var ctx = new ApplicationContext();
                    long onceki = _sw.ElapsedMilliseconds, sonLog = -1000;
                    var timer = new System.Windows.Forms.Timer { Interval = 200 };
                    timer.Tick += (s, e) =>
                    {
                        try
                        {
                            if (_bitti) { timer.Stop(); form?.Close(); ctx.ExitThread(); return; }
                            long simdi = _sw.ElapsedMilliseconds;
                            // Nokta/seçim/değer istemi veya kalıcı diyalog (ana pencere devre dışı): kullanıcı bekleniyor, süre saymaz.
                            bool bekliyor = _girisBekliyor || !IsWindowEnabled(_acad);
                            if (!bekliyor) Interlocked.Add(ref _cizimMs, simdi - onceki);
                            onceki = simdi;
                            if (simdi - sonLog >= 1000)
                            {
                                sonLog = simdi;
                                Log(string.Format(CultureInfo.InvariantCulture, "izle {0} t={1:0.0} giris={2} etkin={3} cizim={4:0.0} gorunur={5}",
                                    _komut, simdi / 1000.0, _girisBekliyor, IsWindowEnabled(_acad), Interlocked.Read(ref _cizimMs) / 1000.0, form != null && form.Visible));
                            }
                            if (!bekliyor && Interlocked.Read(ref _cizimMs) >= GosterSuresiMs)
                            {
                                if (form == null)
                                    form = new CiziliyorFormu(_komut, _sw, () => Interlocked.Read(ref _cizimMs) / 1000.0, PencereDortgeni(_acad));
                                if (!form.Visible)
                                {
                                    form.Show();
                                    Log("goster " + form.Bounds);
                                }
                                // AutoCAD (veya üstte açılan palet) önüne geçsin: her yenilemede odak almadan en üste.
                                SetWindowPos(form.Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                                form.Invalidate();
                            }
                            else if (bekliyor && form != null && form.Visible)
                            {
                                form.Hide();
                                Log("gizle");
                            }
                        }
                        catch (System.Exception ex) { Log("tick hata: " + ex); }
                    };
                    timer.Start();
                    Log("izleme basladi " + _komut);
                    System.Windows.Forms.Application.Run(ctx);
                    timer.Dispose();
                }
                catch (System.Exception ex) { Log("izleme hata: " + ex); }
                finally
                {
                    try { form?.Dispose(); } catch { }
                }
            }
        }

        /// <summary>Kenarlıksız, odak almayan, üstte kalan küçük pencere; yalnız izleyici iş parçacığında yaşar.</summary>
        private sealed class CiziliyorFormu : Form
        {
            private static readonly Color Zemin = Color.FromArgb(235, 235, 235);
            private readonly string _komut;
            private readonly Stopwatch _sw;
            private readonly Func<double> _cizimSn;
            private readonly Font _buyuk = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            private readonly Font _kucuk = new Font("Segoe UI", 8.5f);

            internal CiziliyorFormu(string komut, Stopwatch sw, Func<double> cizimSn, Rectangle acad)
            {
                _cizimSn = cizimSn;
                _komut = komut;
                _sw = sw;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                BackColor = Zemin;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(420, 96);
                DoubleBuffered = true;
                Rectangle ekran = acad.Width > 0 ? acad : Screen.PrimaryScreen.WorkingArea;
                Location = new Point(ekran.Left + (ekran.Width - Width) / 2, ekran.Top + (ekran.Height - Height) / 2);
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    // WS_EX_TOPMOST burada: ShowWithoutActivation ile TopMost özelliği uygulanmıyor, pencere AutoCAD arkasında kalıyordu.
                    cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
                    return cp;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var kenar = new Pen(Color.FromArgb(205, 205, 205)))
                    g.DrawRectangle(kenar, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

                bool yuzdeli = KalipIlerleme.TryDurum(out double oran, out string baslik, out string asama);
                var cubuk = new RectangleF(20, 18, ClientSize.Width - 40, 22);
                const float r = 4f;

                // Boş kısım: açık gri geçiş.
                using (GraphicsPath bos = Yuvarlak(cubuk, r))
                using (var b = new LinearGradientBrush(cubuk, Color.FromArgb(226, 226, 226), Color.FromArgb(196, 196, 196), LinearGradientMode.Vertical))
                    g.FillPath(b, bos);

                // Dolu kısım: klasik mavi geçiş. Yüzde yoksa kayan blok.
                RectangleF dolu;
                if (yuzdeli)
                    dolu = new RectangleF(cubuk.X, cubuk.Y, (float)(cubuk.Width * Math.Max(0.0, Math.Min(1.0, oran))), cubuk.Height);
                else
                {
                    float w = cubuk.Width * 0.3f;
                    double t = (_sw.Elapsed.TotalSeconds % 1.6) / 1.6;
                    float x = cubuk.X - w + (float)t * (cubuk.Width + w);
                    float x0 = Math.Max(cubuk.X, x), x1 = Math.Min(cubuk.Right, x + w);
                    dolu = new RectangleF(x0, cubuk.Y, Math.Max(0, x1 - x0), cubuk.Height);
                }
                if (dolu.Width >= 1f)
                {
                    using (GraphicsPath ic = Yuvarlak(dolu, Math.Min(r, dolu.Width / 2f)))
                    using (var b = new LinearGradientBrush(cubuk, Color.FromArgb(92, 170, 226), Color.FromArgb(58, 134, 200), LinearGradientMode.Vertical))
                        g.FillPath(b, ic);
                }

                string ana = yuzdeli
                    ? string.Format(CultureInfo.InvariantCulture, "Çiziliyor...  %{0}", (int)Math.Floor(oran * 100.0 + 1e-9))
                    : "Çiziliyor...";
                string alt = string.Format(CultureInfo.InvariantCulture, "{0}{1}  ·  {2:0} sn",
                    yuzdeli ? baslik : _komut,
                    yuzdeli && !string.IsNullOrEmpty(asama) ? "  " + asama : string.Empty,
                    _cizimSn != null ? _cizimSn() : _sw.Elapsed.TotalSeconds);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                using (var koyu = new SolidBrush(Color.FromArgb(45, 45, 45)))
                using (var gri = new SolidBrush(Color.FromArgb(110, 110, 110)))
                {
                    g.DrawString(ana, _buyuk, koyu, new RectangleF(10, 47, ClientSize.Width - 20, 22), sf);
                    g.DrawString(alt, _kucuk, gri, new RectangleF(10, 70, ClientSize.Width - 20, 18), sf);
                }
            }

            private static GraphicsPath Yuvarlak(RectangleF rc, float r)
            {
                var p = new GraphicsPath();
                float d = Math.Max(0.5f, Math.Min(r * 2f, Math.Min(rc.Width, rc.Height)));
                p.AddArc(rc.X, rc.Y, d, d, 180, 90);
                p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
                p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
                p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _buyuk.Dispose();
                    _kucuk.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private static Rectangle PencereDortgeni(IntPtr h)
        {
            try { return GetWindowRect(h, out RECT r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty; }
            catch { return Rectangle.Empty; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
