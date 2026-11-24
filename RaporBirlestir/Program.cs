namespace RaporBirlestir
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Kapak algılama denetimi: RaporBirlestir.exe --kapakbul <cikti.txt> <pdf1> [pdf2 …]
            if (args.Length >= 3 && args[0] == "--kapakbul")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var yol in args.Skip(2))
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var p = Parca.Oku(yol, "Rapor");
                        sb.AppendLine($"{p.MevcutKapak} eski kapak / {p.SayfaSayisi} sayfa ({sw.ElapsedMilliseconds} ms)  {Path.GetFileName(yol)}");
                    }
                    catch (Exception ex) { sb.AppendLine($"HATA {Path.GetFileName(yol)}: {ex.Message}"); }
                }
                File.WriteAllText(args[1], sb.ToString());
                return 0;
            }

            // Arayüzsüz doğrulama: RaporBirlestir.exe --test <cikti.pdf> "Bölüm|Tür|yol|sayfalar" …
            if (args.Length >= 3 && args[0] == "--test")
            {
                try
                {
                    var bolumler = new List<(string Bolum, IList<Parca> Parcalar)>();
                    foreach (var a in args.Skip(2))
                    {
                        var s = a.Split('|');
                        var p = Parca.Oku(s[2], s[1].Replace("+P", ""));
                        p.PaspayKontrol = s[1].EndsWith("+P");
                        p.Sayfalar = s.Length > 3 ? s[3] : "";
                        int b = bolumler.FindIndex(x => x.Bolum == s[0]);
                        if (b < 0) { bolumler.Add((s[0], new List<Parca>())); b = bolumler.Count - 1; }
                        bolumler[b].Parcalar.Add(p);
                    }
                    // Arayüzdeki kural: bölümde yeni kapak varsa rapor/eklerin eski kapağı atlanır.
                    foreach (var (_, ps) in bolumler)
                    {
                        bool yeni = ps.Any(x => x.Tur == "Kapak");
                        foreach (var p in ps) p.KapakAtla = yeni && p.Tur != "Kapak";
                    }
                    var notlar = new List<string>();
                    var hedefDeger = Environment.GetEnvironmentVariable("RB_PASPAY") ?? PaspayDuzelt.Hedef;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int n = Birlestirici.Birlestir(bolumler, args[1], notlar, hedefDeger);
                    // Çıktıyı yeniden açıp sayfa ve yer imlerini raporla.
                    var sb = new System.Text.StringBuilder($"TAMAM {n} sayfa ({sw.ElapsedMilliseconds} ms)\n");
                    foreach (var nt in notlar) sb.AppendLine(nt);
                    using (var d = PdfSharp.Pdf.IO.PdfReader.Open(args[1], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                    {
                        sb.AppendLine($"yeniden okunan: {d.PageCount} sayfa");
                        var sayfaNo = new Dictionary<PdfSharp.Pdf.PdfPage, int>();
                        for (int i = 0; i < d.PageCount; i++) sayfaNo[d.Pages[i]] = i + 1;
                        void Yaz(PdfSharp.Pdf.PdfOutlineCollection c, string g)
                        {
                            foreach (var o in c)
                            {
                                sb.AppendLine($"{g}{o.Title}  (s.{(o.DestinationPage != null && sayfaNo.TryGetValue(o.DestinationPage, out var s) ? s : 0)})");
                                Yaz(o.Outlines, g + "  ");
                            }
                        }
                        Yaz(d.Outlines, "");
                    }
                    File.WriteAllText(args[1] + ".txt", sb.ToString());
                    return 0;
                }
                catch (Exception ex) { File.WriteAllText(args[1] + ".txt", "HATA " + ex); return 1; }
            }

            ApplicationConfiguration.Initialize();

            // Arayüz görüntüsü: RaporBirlestir.exe --ekran <cikti.png> <pdf1> [pdf2 …]
            if (args.Length >= 2 && args[0] == "--ekran")
            {
                AnaForm.KayitKapali = true;
                using var f = new AnaForm(args.Skip(2).ToArray());
                f.Show();
                Application.DoEvents();
                Application.DoEvents();
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(args[1]);
                return 0;
            }

            Application.Run(new AnaForm(args));
            return 0;
        }
    }
}
