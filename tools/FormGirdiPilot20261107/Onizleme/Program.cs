using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;

namespace ST4Yardimci
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string mod = args.Length > 0 ? args[0] : "";
            if (mod == "--test") return Testler.Calistir();
            if (mod == "--shots") { Goruntu(args.Length > 1 ? args[1] : "."); return 0; }

            // Etkileşimli: eski ve yeni form yan yana; düğmeler çizim yapmaz.
            var eski = (Form)Activator.CreateInstance(typeof(EskiTemelHatiliForm), true);
            var yeni = Yeni();
            Autodesk.AutoCAD.ApplicationServices.Document.Gonderildi = k => MessageBox.Show(yeni,
                "Önizleme: '" + k.Trim() + "' çalıştırılmaz.\n\nRunner'a gidecek değerler:\n" + Ozet(), "Yeni form");
            yeni.FormClosed += (s, e) => { };
            var ekran = Screen.PrimaryScreen.WorkingArea;
            eski.StartPosition = FormStartPosition.Manual; eski.Text = "ESKİ — " + eski.Text;
            yeni.StartPosition = FormStartPosition.Manual; yeni.Text = "YENİ — " + yeni.Text;
            eski.Location = new Point(ekran.Left + 80, ekran.Top + 80);
            yeni.Shown += (s, e) => yeni.Location = new Point(eski.Right + 30, ekran.Top + 80);
            eski.Show();
            var demo = CapIsaretiDemo();
            demo.StartPosition = FormStartPosition.Manual;
            demo.Location = new Point(eski.Left, eski.Bottom + 20);
            demo.Show();
            Application.Run(yeni);
            return 0;
        }

        /// <summary>Canlı TemelHatiliForm (özel kurucu). Önizlemede gizlenince tekrar komut alabilsin diye _starting sıfırlanır.</summary>
        internal static TemelHatiliForm Yeni()
        {
            var f = (TemelHatiliForm)Activator.CreateInstance(typeof(TemelHatiliForm), true);
            f.VisibleChanged += (s, e) => SifirlaBasla(f);
            return f;
        }

        internal static void SifirlaBasla(TemelHatiliForm f) =>
            typeof(TemelHatiliForm).GetField("_starting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(f, false);

        internal static string Ozet() =>
            "Temel H=" + TemelHatiliSettings.TemelYuksekligiCm + "  Kot=" + TemelHatiliSettings.TemelUstKotuCm +
            "  Döşeme=" + TemelHatiliSettings.DosemeKalinligiCm + "\nHatıl " + TemelHatiliSettings.HatilYuksekligiCm + "×" +
            TemelHatiliSettings.HatilKalinligiCm + "  Pas=" + TemelHatiliSettings.PaspayiCm +
            "\nDüşey ø" + TemelHatiliSettings.DikeyCapMm + "/" + TemelHatiliSettings.DikeyAralikCm +
            "  Yatay ø" + TemelHatiliSettings.YatayCapMm + "/" + TemelHatiliSettings.YatayAralikCm;

        static void Kaydet(Form f, string yol)
        {
            f.PerformLayout(); Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(yol, ImageFormat.Png);
            }
            Console.WriteLine("PNG " + yol + " " + f.Width + "x" + f.Height);
        }

        static void Goruntu(string klasor)
        {
            Directory.CreateDirectory(klasor);
            using (var eski = (Form)Activator.CreateInstance(typeof(EskiTemelHatiliForm), true))
            {
                eski.StartPosition = FormStartPosition.Manual; eski.Location = new Point(-32000, -32000);
                eski.Show(); Application.DoEvents();
                Kaydet(eski, Path.Combine(klasor, "1-eski.png"));
            }
            using (var f = Yeni())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-32000, -32000);
                f.Show(); Application.DoEvents();
                Kaydet(f, Path.Combine(klasor, "2-yeni.png"));
                f.Dusey.Text = "12/35";
                Kaydet(f, Path.Combine(klasor, "3-yeni-uyari.png"));
                f.Dusey.Text = "ø10/20"; f.Pas.Text = "2,5,";
                Kaydet(f, Path.Combine(klasor, "4-yeni-yazim-hatasi.png"));
                f.Pas.Text = "5"; f.HatilW.Text = "15"; f.Dusey.Text = "ø16/15";
                Kaydet(f, Path.Combine(klasor, "5-yeni-sigmiyor.png"));
            }
            using (var d = CapIsaretiDemo())
            {
                d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-32000, -32000);
                d.Show(); Application.DoEvents();
                Kaydet(d, Path.Combine(klasor, "6-cap-isareti.png"));
            }
        }

        /// <summary>Donatı Boyu / Metraj "Fi işareti": eski serbest kutu ve yeni liste yan yana.</summary>
        internal static Form CapIsaretiDemo()
        {
            var f = new Form { Text = "Çap işareti — Donatı Boyu / Metraj", ClientSize = new Size(380, 120),
                FormBorderStyle = FormBorderStyle.FixedToolWindow, Font = new Font("Segoe UI", 9f), ShowInTaskbar = false,
                BackColor = Color.FromArgb(240, 240, 240) };
            f.Controls.Add(new Label { Text = "Eski: elle yazılır", Location = new Point(12, 16), AutoSize = true });
            f.Controls.Add(new TextBox { Text = "ø", Location = new Point(160, 12), Width = 200 });
            f.Controls.Add(new Label { Text = "Yeni: listeden seçilir", Location = new Point(12, 52), AutoSize = true });
            var c = new CapIsaretiSecim("ø") { Location = new Point(160, 48), Width = 200 };
            f.Controls.Add(c);
            var on = new Label { Location = new Point(12, 88), AutoSize = true, ForeColor = Color.FromArgb(109, 109, 109) };
            void Guncelle() => on.Text = "Çizimde: 4" + c.Deger + "12  L=350";
            c.SelectedIndexChanged += (s, e) => Guncelle(); Guncelle();
            f.Controls.Add(on);
            return f;
        }
    }

    static class Testler
    {
        static int _hata;
        static void Esit(object beklenen, object gercek, string ad)
        {
            if (Equals(beklenen, gercek)) return;
            _hata++; Console.WriteLine("FAIL " + ad + ": beklenen " + beklenen + ", gelen " + gercek);
        }

        internal static int Calistir()
        {
            foreach (var (metin, cap, ara) in new[] { ("ø12/15", 12, 15.0), ("12/15", 12, 15.0), ("Ø 12 / 15", 12, 15.0),
                ("12-15", 12, 15.0), ("10 20", 10, 20.0), ("φ8/12,5", 8, 12.5), ("ø8/12.5", 8, 12.5),
                ("f12/15", 12, 15.0), ("fi12/15", 12, 15.0), ("F 12 15", 12, 15.0), ("%%c12/15", 12, 15.0), ("%%C 12/15", 12, 15.0) })
            {
                string h = DonatiKutusu.Ayristir(metin, 8, 32, 5, 50, out int c, out double a);
                Esit(null, h, "ayrıştır " + metin); Esit(cap, c, "çap " + metin); Esit(ara, a, "aralık " + metin);
            }
            foreach (var kotu in new[] { "", "12", "ø13/15", "ø12/3", "ø12/60", "abc", "ø40/15", "ø12/15/20" })
                if (DonatiKutusu.Ayristir(kotu, 8, 32, 5, 50, out _, out _) == null) { _hata++; Console.WriteLine("FAIL kabul edilmemeliydi: " + kotu); }
            Esit("7,54", GirdiTema.Yaz(DonatiKutusu.AlanCm2m(12, 15), 2), "As ø12/15");
            Esit("12/12,5", DonatiKutusu.KutuMetni(12, 12.5), "kutu metni ø içermez");
            Esit("ø12/12,5", DonatiKutusu.Yazim(12, 12.5), "yazım ø içerir");
            using (var c = new CapIsaretiSecim("%%c")) Esit("%%c", c.Deger, "çap işareti %%c");
            using (var c = new CapIsaretiSecim("")) Esit("ø", c.Deger, "boş → ø");
            using (var c = new CapIsaretiSecim("Ф")) Esit("Ф", c.Deger, "eski özel işaret korunur");
            Esit("3,93", GirdiTema.Yaz(DonatiKutusu.AlanCm2m(10, 20), 2), "As ø10/20");
            foreach (var (t, v) in new[] { ("2,5", 2.5), ("2.5", 2.5), (" -80 ", -80.0), ("+3", 3.0) })
            { Esit(true, GirdiTema.SayiOku(t, out double d), "sayı " + t); Esit(v, d, "değer " + t); }
            foreach (var t in new[] { "2,5,", "1.000,5", "", "1e3", "--2" })
                Esit(false, GirdiTema.SayiOku(t, out _), "red " + t);

            using (var f = Program.Yeni())
            {
                f.Show(); Application.DoEvents();
                Esit(true, f.Denetci.Uygun, "varsayılan uygun");
                f.Pas.Text = "2,5,"; Esit(false, f.Denetci.Uygun, "yazım hatası engeller");
                Esit(10.0, f.HatilW.Deger > 0 ? 10.0 : 0, "sanity");
                f.Pas.Text = "25"; Esit(false, f.Pas.Gecerli, "sınır dışı paspayı");
                f.Pas.Text = "3"; Esit(true, f.Denetci.Uygun, "düzeltince uygun");
                f.HatilH.Text = "15"; Esit(false, f.Denetci.Uygun, "hatıl ≤ döşeme");
                f.HatilH.Text = "120"; f.HatilW.Text = "10"; f.Dusey.Text = "ø16/15";
                Esit(false, f.Denetci.Uygun, "donatı sığmıyor");
                f.HatilW.Text = "20"; f.Dusey.Text = "ø12/35";
                Esit(true, f.Denetci.Uygun, "uyarı engellemez"); Esit(1, f.Denetci.Uyarilar.Count, "aralık uyarısı");
                string gonderilen = null; Autodesk.AutoCAD.ApplicationServices.Document.Gonderildi = k => gonderilen = k;
                f.Dusey.Text = "ø12/15"; f.Yatay.Text = "8/20"; f.Kot.Text = "-120";
                f.BtnManuel.PerformClick();
                Esit("TEMELHATILICIZ ", gonderilen, "komut");
                Esit(12.0, TemelHatiliSettings.DikeyCapMm, "kayıt çap"); Esit(15.0, TemelHatiliSettings.DikeyAralikCm, "kayıt aralık");
                Esit(8.0, TemelHatiliSettings.YatayCapMm, "kayıt yatay"); Esit(-120.0, TemelHatiliSettings.TemelUstKotuCm, "kayıt kot");
                Esit(true, BaIni.Yazilan.ContainsKey("temelhatili.ini"), "ini yazıldı");
                Program.SifirlaBasla(f);
                gonderilen = null; f.Kot.Text = "x"; f.BtnPlan.PerformClick();
                Esit(null, gonderilen, "hatada komut gitmez");
                Esit(-120.0, TemelHatiliSettings.TemelUstKotuCm, "hatada kayıt bozulmaz");
                f.Denetci.VarsayilanDugmesi.PerformClick();
                Esit(true, f.Denetci.Uygun, "varsayılan sonrası uygun"); Esit(10, f.Dusey.CapMm, "varsayılan çap");
                f.Olcek.Secili = 1; Esit(true, BaPaftaOlcek.Yuz("temelhatili"), "pafta 1/100 kaydı");
                f.Yatay.Text = "fi14 25"; Esit(14, f.Yatay.CapMm, "fi öneki"); Esit(25.0, f.Yatay.AralikCm, "boşluklu aralık");
            }
            Console.WriteLine(_hata == 0 ? "PASS tüm testler" : "FAIL " + _hata);
            return _hata == 0 ? 0 : 1;
        }
    }
}
