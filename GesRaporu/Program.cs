namespace GesRaporu
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Kapak okuma denetimi: GesRaporu.exe --kapak <cikti.txt> <dwg1> [dwg2 …]
            if (args.Length >= 3 && args[0] == "--kapak")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var yol in args.Skip(2))
                {
                    sb.AppendLine("# " + Path.GetFileName(yol));
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var b = DwgBilgi.Oku(yol);
                        foreach (var blok in b.Bloklar)
                        {
                            b.BlokSec(blok);
                            sb.AppendLine($"  [{(blok.Gorunen.Length > 0 ? blok.Gorunen : "-")}] İl={b.Il} İlçe={b.Ilce} plan={blok.Planlar.Count}");
                            foreach (var (k, v) in b.Kapak.OrderBy(k => k.Key)) sb.AppendLine($"     {k} = {v}");
                        }
                        sb.AppendLine($"  ({sw.ElapsedMilliseconds} ms) " + string.Join(" | ", b.Notlar));
                    }
                    catch (Exception ex) { sb.AppendLine("  HATA: " + ex.Message); }
                }
                File.WriteAllText(args[1], sb.ToString());
                return 0;
            }

            // Doğrulama: GesRaporu.exe --test <dwg> <cikti.pdf>  (arayüzsüz, varsayılan girdilerle)
            if (args.Length >= 3 && args[0] == "--test")
            {
                try
                {
                    var b = DwgBilgi.Oku(args[1]);
                    var secilenAd = Environment.GetEnvironmentVariable("GES_BLOK");
                    var blok = b.Bloklar.FirstOrDefault(x => string.Equals(x.Ad, secilenAd, StringComparison.OrdinalIgnoreCase)) ?? b.Bloklar[0];
                    b.BlokSec(blok);
                    var g = new Girdi { DwgYolu = args[1] };
                    string K(string a) => b.Kapak.TryGetValue(a, out var v) ? v : "";
                    g.Il = b.Il ?? ""; g.Ilce = b.Ilce ?? ""; g.Mahalle = K("MAHALLE");
                    g.Pafta = K("PAFTA"); g.Ada = K("ADA"); g.Parsel = K("PARSEL");
                    g.Blok = K("BLOK"); g.YapiCinsi = K("CİNSİ"); g.KatAdedi = K("KAT ADEDİ"); g.KullanimAmaci = K("KULLANIM AMACI");
                    g.YapiSahibi = args.Length > 3 ? args[3] : K("YAPI SAHİBİ");
                    if (b.SifirKotu != null) g.Rakim = Math.Round(b.SifirKotu.Value);
                    if (b.EnUstKot != null) g.BinaYuksekligi = b.EnUstKot.Value;
                    g.KarBolgesi = BolgeDeposu.Bolge(g.Il, g.Ilce) ?? g.KarBolgesi;
                    var baslik = b.VarsayilanCatiBasligi(blok.Planlar.Count > 0 ? blok.Planlar : b.PlanBasliklari);
                    g.CatiPlaniBasligi = baslik?.Metin ?? "";
                    var plan = b.PlanCiz(baslik, g.PanelA, g.PanelB);
                    var svg = Environment.GetEnvironmentVariable("GES_SVG");
                    if (!string.IsNullOrEmpty(svg) && plan != null)
                    {
                        // Doğrulama görseli (PNG): çıkarılan çatı planı çizgileri
                        double o = 1100 / Math.Max(plan.Genislik, plan.Yukseklik);
                        using var bmp = new System.Drawing.Bitmap((int)(plan.Genislik * o) + 20, (int)(plan.Yukseklik * o) + 20);
                        using (var gr = System.Drawing.Graphics.FromImage(bmp))
                        {
                            gr.Clear(System.Drawing.Color.White);
                            using var kalin = new System.Drawing.Pen(System.Drawing.Color.Black, 1.5f);
                            using var ince = new System.Drawing.Pen(System.Drawing.Color.Gray, 1f);
                            foreach (var c in plan.Cizgiler)
                                gr.DrawLine(c.Kalin ? kalin : ince, (float)((c.X1 - plan.MinX) * o + 10), (float)((plan.MaxY - c.Y1) * o + 10),
                                            (float)((c.X2 - plan.MinX) * o + 10), (float)((plan.MaxY - c.Y2) * o + 10));
                        }
                        bmp.Save(svg, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var egim = b.EgimTahmini(plan);
                    var bh = b.BinaYuksekligi(plan, out var hKaynak); if (bh != null) g.BinaYuksekligi = bh.Value;
                    b.Notlar.Add("Bina yüksekliği: " + (hKaynak ?? "bulunamadı"));
                    if (egim != null) g.CatiEgimi = Math.Round(egim.Value, 1);
                    var ek = Path.Combine(AppContext.BaseDirectory, "Varliklar", "ekler");
                    if (Directory.Exists(ek)) g.EkPdfler.AddRange(Directory.GetFiles(ek, "*.pdf").OrderBy(x => x));
                    if (Environment.GetEnvironmentVariable("GES_ZOR") == "1")
                    {
                        g.Rakim = 1800; g.KarBolgesi = 9; g.BinaYuksekligi = 120; g.SacKalinligi = 0.5; g.AsikAraligi = 2.5;
                        g.BulonCapi = 4; g.BaglantiProfilKalinligi = 1.0; g.VidaCapi = 3; g.ProfilVidaAdedi = 1; g.ProfilMesnetAraligi = 1.2;
                    }
                    var ilk = new Hesap(g);
                    var kg = Hesap.Kurtar(g, out var degisen);
                    Rapor.Uret(g, plan, args[2]);
                    var h = new Hesap(kg);
                    File.WriteAllText(Path.ChangeExtension(args[2], ".txt"),
                        $"Il={g.Il} Ilce={g.Ilce} Mah={g.Mahalle} Ada={g.Ada} Parsel={g.Parsel} Blok={g.Blok} Cins={g.YapiCinsi} Kat={g.KatAdedi} Kullanim={g.KullanimAmaci}\n" +
                        $"Ilk: Kaplama={ilk.KaplamaYeterli} Bulon={ilk.BulonYeterli} Vida={ilk.VidaYeterli} Profil={ilk.ProfilYeterli}\nDegisen: {string.Join(" ; ", degisen)}\n" +
                        $"Rakim={g.Rakim} Bolge={g.KarBolgesi} H={g.BinaYuksekligi} Egim={g.CatiEgimi} Sk={h.SkKn} q={h.QKn}\n" +
                        $"Plan={g.CatiPlaniBasligi} cizgi={plan?.Cizgiler.Count} {plan?.Genislik * plan?.BirimMetre:0.00}x{plan?.Yukseklik * plan?.BirimMetre:0.00} m panel={plan?.PanelAdedi}\n" +
                        $"Kaplama={h.KaplamaYeterli} {h.PKaplama:0.0}/{h.Ga:0} Bulon={h.BulonYeterli} {h.WBulon:0.0}/{h.NBulon:0.0} Vida={h.VidaYeterli} Profil={h.ProfilYeterli} {h.SMes:0.0} {h.Fx:0.0000}\n" +
                        "Notlar: " + string.Join(" | ", b.Notlar) + "\nBloklar: " + string.Join(" | ", b.Bloklar.Select(x => $"{x.Gorunen} ({x.Planlar.Count} plan; çatı: {string.Join(", ", x.Planlar.Where(p => p.Metin.ToUpperInvariant().Contains("ÇATI")).Select(p => p.Metin))})")));
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.ChangeExtension(args[2], ".txt"), ex.ToString());
                    return 1;
                }
            }

            ApplicationConfiguration.Initialize();
            if (args.Length >= 3 && args[0] == "--formtest")
            {
                var f = new AnaForm();
                f.Shown += async (s, e) =>
                {
                    try { File.WriteAllText(args[2], await f.FormTesti(args[1], args.Length > 3 ? args[3] : null)); }
                    catch (Exception ex) { File.WriteAllText(args[2], ex.ToString()); }
                    f.Close();
                };
                Application.Run(f);
                return 0;
            }
            Application.Run(new AnaForm());
            return 0;
        }
    }
}
