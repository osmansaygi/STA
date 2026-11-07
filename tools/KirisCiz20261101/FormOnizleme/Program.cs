using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public static class Application
    {
        public static object GetSystemVariable(string name) => (short)1;
        public static DialogResult ShowModalDialog(Form f) => DialogResult.Cancel;
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        if (args.Length == 2 && args[0] == "--test")
        {
            // Yazım testi: girdi=beklenen satırları; çıktı dosyası args[1].test.txt
            var satirlar = new System.Collections.Generic.List<string>();
            int hata = 0;
            foreach (var l in File.ReadAllLines(args[1], System.Text.Encoding.UTF8))
            {
                var p = l.Split('=');
                if (p.Length != 2) continue;
                bool et = p[0].StartsWith("E:");
                string girdi = et ? p[0].Substring(2) : p[0];
                string sonuc = et ? ST4PlanIdCiz.KirisManuelVeri.EtriyeBicim(girdi) : ST4PlanIdCiz.KirisManuelVeri.DonatiBicim(girdi);
                bool ok = sonuc == p[1];
                if (!ok) hata++;
                satirlar.Add((ok ? "PASS " : "FAIL ") + "'" + girdi + "' -> '" + sonuc + "' (beklenen '" + p[1] + "')");
            }
            satirlar.Add(hata == 0 ? "COMPLETE" : "HATA " + hata);
            File.WriteAllLines(args[1] + ".sonuc.txt", satirlar, System.Text.Encoding.UTF8);
            return;
        }
        string cikti = args.Length > 0 ? args[0] : "onizleme.png";
        var tip = typeof(ST4PlanIdCiz.KirisManuelForm);
        var form = (Form)Activator.CreateInstance(tip, true);
        if (args.Length > 1)
        {
            // İkinci argüman: .kiris dosyası → formu bu veriyle doldur.
            var veri = ST4PlanIdCiz.KirisManuelVeri.Oku(File.ReadAllText(args[1]));
            tip.GetMethod("Yukle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { veri });
        }
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(20, 20);
        form.Shown += (s, e) =>
        {
            var t = new Timer { Interval = 700 };
            t.Tick += (s2, e2) =>
            {
                t.Stop();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(cikti, ImageFormat.Png);
                }
                form.Close();
            };
            t.Start();
        };
        Application.Run(form);
    }
}
