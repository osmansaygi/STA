using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// CizimIzleyici penceresini AutoCAD'siz dener: 1) çizimi PNG'ye, 2) çizim süresi sayacı (giriş beklemesi hariç 5 sn).
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string dll = args[0], outDir = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string p = Path.Combine(@"C:\Program Files\Autodesk\AutoCAD 2027", new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        var asm = Assembly.LoadFrom(dll);
        var iz = asm.GetType("ST4PlanIdCiz.CizimIzleyici", true);
        var formT = iz.GetNestedType("CiziliyorFormu", BindingFlags.NonPublic);
        var kal = asm.GetType("ST4PlanIdCiz.KalipIlerleme", true);
        var log = new System.Text.StringBuilder();
        Render(formT, Path.Combine(outDir, "pencere_kayan.png"), 6.2);
        var bas = kal.GetMethod("BaslatYoksa", BindingFlags.NonPublic | BindingFlags.Static);
        var bil = kal.GetMethod("Bildir", BindingFlags.NonPublic | BindingFlags.Static);
        bas.Invoke(null, new object[] { "DENEME2", null });
        bil.Invoke(null, new object[] { 0.62, "kat 1/2 kopya 2/2 doseme" });
        Render(formT, Path.Combine(outDir, "pencere_yuzde.png"), 7.4);

        // Sahte AutoCAD penceresi (etkin, yanıt veriyor).
        var hazir = new ManualResetEvent(false);
        var t = new Thread(() =>
        {
            var f = new Form { Text = "SahteAcad", Size = new Size(900, 600), StartPosition = FormStartPosition.CenterScreen };
            f.Shown += (s, e) => { _sahte = f.Handle; hazir.Set(); };
            Application.Run(f);
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        hazir.WaitOne();
        var izT = iz.GetNestedType("Izleme", BindingFlags.NonPublic);
        var giris = izT.GetMethod("GirisBekliyor", BindingFlags.NonPublic | BindingFlags.Instance);
        var sw = Stopwatch.StartNew();
        object izleme = Activator.CreateInstance(izT, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { _sahte, "DENEME2" }, null);
        for (int i = 0; i < 24; i++)
        {
            double sn = sw.Elapsed.TotalSeconds;
            if (i == 6) giris.Invoke(izleme, new object[] { true });   // 3,0 sn: nokta istemi
            if (i == 12) giris.Invoke(izleme, new object[] { false }); // 6,0 sn: çizim sürüyor
            if (i == 20) giris.Invoke(izleme, new object[] { true });  // 10,0 sn: yeniden istem → gizlenmeli
            Thread.Sleep(500);
            bil.Invoke(null, new object[] { 0.62 + i * 0.01, "kat 1/2 kesit" });
            log.AppendLine($"{sw.Elapsed.TotalSeconds:0.0}s bekliyor={(i >= 6 && i < 12) || i >= 20} gorunur={Gorunur()}");
        }
        izT.GetMethod("Bitir", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(izleme, null);
        log.AppendLine($"bitti {sw.Elapsed.TotalSeconds:0.0}s gorunur={Gorunur()}");
        File.WriteAllText(Path.Combine(outDir, "pencere_test.txt"), log.ToString());
        Environment.Exit(0); // KalipIlerleme.Dispose Editor (acmgd) yükler; AutoCAD dışında çağrılmaz.
    }

    static IntPtr _sahte;

    static bool Gorunur()
    {
        bool var = false;
        EnumWindows((h, l) =>
        {
            if (h != _sahte && IsWindowVisible(h))
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == (uint)Environment.ProcessId) var = true;
            }
            return true;
        }, IntPtr.Zero);
        return var;
    }

    static void Render(Type formT, string path, double sn)
    {
        var sw = Stopwatch.StartNew();
        var f = (Form)Activator.CreateInstance(formT, BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { "BEYKENT_KOMUT", sw, (Func<double>)(() => sn), Rectangle.Empty }, null);
        var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(path);
        f.Dispose();
    }

    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
