using System.Text;
using StatikRaporClaude.UI;

namespace StatikRaporClaude
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Doğrulama: StatikRaporClaude --png <st4> <klasör> [sayfa sıraları]
            if (args.Length >= 3 && args[0] == "--png")
            {
                Dogrulama.PngYaz(args[1], args[2], args.Skip(3).Select(int.Parse).ToArray());
                return;
            }
            // Doğrulama: StatikRaporClaude --yan-yana <st4> <çıktı.pdf> — seçili varsayılan bölümler, sol GPR / sağ bu program
            if (args.Length >= 3 && args[0] == "--yan-yana")
            {
                Dogrulama.YanYanaYaz(args[1], args[2]);
                return;
            }
            // Doğrulama: StatikRaporClaude --pdf <st4> <çıktı.pdf> — arayüzdeki varsayılan seçimle rapor üretir
            if (args.Length >= 2 && args[0] == "--resimler") { Dogrulama.ResimleriYaz(args[1]); return; }
            if (args.Length >= 3 && args[0] == "--dokum") { Dogrulama.OgeDokumu(args[1], args[2]); return; }
            if (args.Length >= 3 && args[0] == "--pdf")
            {
                Dogrulama.PdfYaz(args[1], args[2], args.Length > 3 ? args[3] : null);
                return;
            }
            // Doğrulama: StatikRaporClaude --pdf-gprsiz <st4> <çıktı.pdf> — GPR okunmadan, yalnız sonuç dosyalarından
            if (args.Length >= 3 && args[0] == "--pdf-gprsiz")
            {
                Dogrulama.PdfGprsiz(args[1], args[2], args.Length > 3 && args[3] == "hepsi");
                return;
            }
            // Doğrulama: StatikRaporClaude --uretec <st4> <rapor.txt> [kalem süzgeci] — GPR'siz metin üreteçleri ile GPR satırları
            if (args.Length >= 3 && args[0] == "--uretec")
            {
                Dogrulama.UretecKarsilastir(args[1], args[2], args.Length > 3 ? args[3] : null);
                return;
            }
            // Doğrulama: StatikRaporClaude --veri <st4> <rapor.txt> — dosyadan üretilen tablolar ile GPR karşılaştırması
            if (args.Length >= 3 && args[0] == "--veri")
            {
                var p = Proje.ProjeKlasoru.Ac(args[1]);
                var satirlar = Veri.GprKarsilastirma.Hepsi(Veri.StaVeri.Ac(args[1]), Gpr.GprBelgesi.Oku(p.GprYolu));
                File.WriteAllLines(args[2], satirlar);
                return;
            }
            ApplicationConfiguration.Initialize();
            // Doğrulama: StatikRaporClaude --ekran <st4> <png> — pencereyi açar, görüntüsünü kaydeder, kapanır
            if (args.Length >= 3 && args[0] == "--ekran")
            {
                AnaForm.AyarKaydetme = true;
                var f = new AnaForm(args[1]);
                f.Shown += async (s, e) =>
                {
                    await Task.Delay(1500);
                    using var bmp = new System.Drawing.Bitmap(f.Width, f.Height);
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[2]);
                    f.Close();
                };
                Application.Run(f);
                return;
            }
            Application.Run(new AnaForm(args.Length > 0 ? args[0] : null));
        }
    }
}
