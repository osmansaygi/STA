using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace GesRaporu
{
    /// <summary>GES çatı statik hesap raporunu PDF olarak üretir.</summary>
    public static partial class Rapor
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private static string N(double x, int d = 2) => x.ToString("N" + d, Tr);
        private static string Up(string s) => (s ?? "").Trim().ToUpper(Tr);
        private static string Bos(string s, string yedek = "—") => string.IsNullOrWhiteSpace(s) ? yedek : s.Trim();


        // ------------------------------------------------------------------ 1
        private static void IsinKonusu(PdfYazici p, Girdi g, Hesap h)
        {
            p.Baslik("1. İŞİN KONUSU");
            string yer = $"{Up(g.Il)} ili {Up(g.Ilce)} ilçesi {Up(g.Mahalle)} Mah. " +
                         (string.IsNullOrWhiteSpace(g.Ada) ? "" : $"{g.Ada} ada, {Bos(g.Parsel, "-")} parselde ");
            string blok = string.IsNullOrWhiteSpace(g.Blok) ? "binanın" : $"{DwgBilgi.Etiketle(g.Blok)} binasının";
            string sahip = string.IsNullOrWhiteSpace(g.YapiSahibi) ? "" : $"{Up(g.YapiSahibi)} adına ";
            p.Paragraf($"Bu teknik rapor, {Bos(g.Hazirlayan)} tarafından {yer}yapılacak {blok} çatısına {sahip}kurulacak güneş enerji santralinin (GES) panellerini taşıyacak sistemin yapısal analiz ve tahkik işlemlerini içermektedir.", girinti: 0);
            if (!string.IsNullOrWhiteSpace(g.Blok))
                p.Paragraf($"Bu rapor yalnızca {DwgBilgi.Etiketle(g.Blok)} için hazırlanmıştır; aynı parseldeki diğer bloklar ayrı raporlarda değerlendirilir.", 10, italik: true);
            p.Bosluk(4);
            string yapi = $"Yapı {(string.IsNullOrWhiteSpace(g.YapiCinsi) ? "betonarme" : g.YapiCinsi.ToLower(Tr))} taşıyıcı sisteme sahip" +
                          (string.IsNullOrWhiteSpace(g.KatAdedi) ? "" : $", {g.KatAdedi}") +
                          (string.IsNullOrWhiteSpace(g.KullanimAmaci) ? " bir binadır." : $" {g.KullanimAmaci.ToLower(Tr)} binasıdır.");
            p.Paragraf($"{yapi} Güneş enerjisi panelleri mevcut çatı kaplaması üzerine alüminyum taşıyıcı profiller ve bağlantı elemanları ile monte edilecektir. Taşıyıcı sistem kar, rüzgar ve güneş panellerinden kaynaklı yükler altında tahkik edilmiştir." +
                       (g.PanelAdedi > 0 ? $" Çatıya {g.PanelAdedi} adet {g.PanelModeli} güneş paneli yerleştirilecek olup toplam kurulu güç {N(h.KuruluGucKwp, 2)} kWp'dir." : ""));
        }

        // ------------------------------------------------------------------ 2
        private static void Kriterler(PdfYazici p, Girdi g, Hesap h)
        {
            p.Baslik("2. GES PROJE TASARIM KRİTERLERİ");
            p.Baslik("2.1. ARSA VE YAPI BİLGİSİ", 11);
            p.Deger("İli / İlçesi", "=", $"{Up(g.Il)} / {Up(g.Ilce)}", xDeger: 330);
            p.Deger("Mahalle", "=", Up(g.Mahalle), xDeger: 330);
            p.Deger("Pafta / Ada / Parsel", "=", $"{Bos(g.Pafta, "-")} / {Bos(g.Ada)} / {Bos(g.Parsel)}", xDeger: 330);
            p.Deger("Rakım", "=", N(g.Rakim, 0), "m");
            p.Deger("Kar Bölgesi (TS 498:2021)", "=", h.KarBolge.ToString(), "");
            p.Deger("Bina Yüksekliği (zeminden)", "H =", N(g.BinaYuksekligi, 2), "m");
            p.Deger("Çatı Eğimi", "α =", N(g.CatiEgimi, 1), "°");

            p.Baslik("2.2. MALZEME BİLGİSİ", 11);
            p.Paragraf("Yük kabulleri TS 498'e uygun şekilde yapılmıştır. Alüminyum taşıyıcı elemanların tasarım kontrollerinde AASHTO LRFD \"7. Aluminum Structures\", çelik bağlantı elemanlarının kontrollerinde TS 648 şartları dikkate alınmıştır. Malzeme özellikleri aşağıda verilmiştir.");
            p.Baslik("2.2.1. Mevcut Çatı Kaplaması", 10.5);
            p.Deger("* Kaplama Bilgileri", "", $"{g.KaplamaTipi} sac kaplama", xDeger: 330);
            p.Deger("Sac kalınlığı", "t =", N(g.SacKalinligi, 2), "mm");
            p.Deger("Kaplama ağırlığı", "g =", N(g.KaplamaAgirligi, 2), "kg/m²");
            p.Deger("Aşık aralığı", "", N(g.AsikAraligi, 2), "m");

            p.Baslik("2.2.2. Yeni Malzeme Bilgisi", 10.5);
            p.Paragraf("* Güneş panel taşıyıcıları ve bağlantı elemanları alüminyum olacaktır. Alüminyum, teknik şartnamelere uygun olarak imal ve inşa edilmelidir.", italik: false);
            p.Deger($"Alüminyum {Aluminyum.Alasim}", "", "", "");
            p.Deger("Birim hacim ağırlığı", "γ =", N(Aluminyum.Gamma, 2), "gr/cm³");
            p.Deger("Akma sınırı", "σa =", N(Aluminyum.Akma / 10, 0), "N/mm²", $"= {N(Aluminyum.Akma, 0)} kg/cm²");
            p.Deger("Elastisite modülü", "E =", N(Aluminyum.E / 10, 0), "N/mm²", $"= {N(Aluminyum.E, 0)} kg/cm²");
            p.Deger("Emniyet gerilmesi", "σem =", N(Aluminyum.SigmaEm / 10, 0), "N/mm²", $"= {N(Aluminyum.SigmaEm, 0)} kg/cm²");
            p.Bosluk(3);
            p.Deger("* Güneş Panel Bilgileri", "", g.PanelModeli, "", "(EK-1)", xDeger: 300);
            p.Deger("Panel ebatları", "a x b =", $"{N(g.PanelA, 3)} x {N(g.PanelB, 3)}", "m", $"t = {N(g.PanelT, 0)} mm");
            p.Deger("Panel ağırlığı", "g =", N(g.PanelAgirlik, 1), "kg");

            p.Baslik("2.3. KULLANILAN STANDARTLAR", 11);
            foreach (var s in new[]
            {
                "* TS 498 \"Yapı Elemanlarının Boyutlandırılmasında Alınacak Yüklerin Hesap Değerleri\", 2021",
                "* TS 500 \"Betonarme Yapıların Tasarım ve Yapım Kuralları\", Şubat 2000",
                "* TS 648 \"Çelik Yapıların Hesap ve Yapım Kuralları\", 1980",
                "* Türkiye Bina Deprem Yönetmeliği, 2018",
                "* AASHTO LRFD Bridge Design Specifications \"7. Aluminum Structures\"",
            }) p.Paragraf(s, 10.5, yasla: false);
        }

        // ------------------------------------------------------------------ 3
        private static void Model(PdfYazici p, Girdi g, Hesap h, DwgBilgi.PlanCizimi plan)
        {
            p.Yer(300);
            p.Baslik("3. MODEL TANIMI VE ÇATI PLANI");
            p.Paragraf($"Güneş enerjisi ile elektrik üretimi gerçekleştirmek amacıyla {(string.IsNullOrWhiteSpace(g.YapiSahibi) ? "yapı sahibi" : Up(g.YapiSahibi))} adına bina çatısı üzerine güneş enerji santrali kurulması istenmiştir. Paneller, kısa kenarı çatı eğimine paralel olacak şekilde alüminyum taşıyıcı profiller üzerine yerleştirilecek; profiller çatı kaplaması hadvelerine vidalanarak mesnetlenecektir. Taşıyıcı sistem kar, rüzgar ve güneş panellerinden kaynaklı yükler altında tahkik edilmiştir.");
            p.Bosluk(6);
            if (plan == null || plan.Cizgiler.Count == 0)
            {
                p.Paragraf("(Çatı planı DWG'den okunamadı.)", italik: true);
                return;
            }
            double oran = plan.Yukseklik / Math.Max(plan.Genislik, 1e-9);
            double gen = p.Genislik, yuk = Math.Min(gen * oran, 380);
            if (yuk / oran < gen) gen = yuk / oran;
            p.Yer(yuk + 30);
            p.Cizim(yuk, (gx, r) =>
            {
                double s = Math.Min(r.Width / plan.Genislik, r.Height / plan.Yukseklik);
                double ox = r.X + (r.Width - plan.Genislik * s) / 2, oy = r.Y + (r.Height - plan.Yukseklik * s) / 2;
                var kalin = new XPen(Renk.Lacivert, 0.6);
                var ince = new XPen(XColor.FromArgb(120, 128, 140), 0.22);
                foreach (var c in plan.Cizgiler)
                    gx.DrawLine(c.Kalin ? kalin : ince,
                        ox + (c.X1 - plan.MinX) * s, oy + (plan.MaxY - c.Y1) * s,
                        ox + (c.X2 - plan.MinX) * s, oy + (plan.MaxY - c.Y2) * s);
            });
            p.Bosluk(4);
            p.Paragraf($"Şekil 1 – Çatı planı (mimari projeden{(string.IsNullOrWhiteSpace(g.CatiPlaniBasligi) ? "" : ": " + g.CatiPlaniBasligi)}). Plan boyutları ≈ {N(plan.Genislik * plan.BirimMetre, 2)} x {N(plan.Yukseklik * plan.BirimMetre, 2)} m." +
                       (plan.PanelAdedi > 0 ? $" Planda {plan.PanelAdedi} adet panel çizilidir." : ""), 9.5, italik: true, yasla: false);
        }

        // ------------------------------------------------------------------ 4
        private static void Yukler(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(260);
            p.Baslik("4. YÜK TANIMLARI");
            p.Kutucuklar(new (string, string, XColor?)[]
            {
                ($"{N(h.SkKn, 2)} kN/m²", $"Kar yükü Sk  ·  {h.KarBolge}. bölge", null),
                ($"{N(h.QKn, 2)} kN/m²", "Tasarım rüzgar basıncı q", null),
                ($"{N(g.Rakim, 0)} m", "Rakım", Renk.Lacivert),
                ($"{N(g.BinaYuksekligi, 2)} m", "Bina yüksekliği H", Renk.Lacivert),
            });
            p.Baslik("4.1. KAR YÜKÜ", 11);
            p.Paragraf("Kar yükü hesabında TS 498:2021 yükleme kriterleri uygulanmıştır. Madde 11 – Çizelge 3'teki karakteristik kar yükü değerleri dikkate alınmıştır.");
            p.Deger("Rakım", "=", N(g.Rakim, 0), "m", $"Kar Bölgesi = {h.KarBolge}");
            p.Deger("Çizelge 3 değeri", "Sk =", N(h.SkTablo, 2), "kN/m²", $"({KarTablosu.KotEtiket[h.KarSatir]} m satırı)");
            if (h.KarArtis > 1)
                p.Deger("1000 m üstü artış", "", $"x {N(h.KarArtis, 2)}", "", g.Rakim <= 1500 ? "(1500 m'ye kadar %10)" : "(1500 m üstü %15)");
            p.Deger("Kar yükü", "Pk =", N(h.SkKn, 2), "kN/m²", $"= {N(h.KarKg, 1)} kg/m²", true);
            p.Paragraf($"Bu yüzden hareketli yük olarak {N(h.KarKg, 1)} kg/m² alınmıştır.", kalin: true);
            p.Bosluk(4);
            var satir = new List<string[]>();
            for (int i = 0; i < KarTablosu.Kotlar.Length; i++)
            {
                var s = new string[10];
                s[0] = KarTablosu.KotEtiket[i];
                for (int b = 0; b < 9; b++) s[b + 1] = N(KarTablosu.Sk[i, b], 2);
                satir.Add(s);
            }
            satir.Add(new[] { ">1000", "1000 m'ye tekabül eden değerler, 1500 m'ye kadar %10, 1500 m'den yukarı %15 artırılır.", "", "", "", "", "", "", "", "" });
            p.Paragraf("Çizelge 3 — Karakteristik kar yükü Sk değerleri (kN/m²)", 10, true, yasla: false);
            p.Tablo(new[] { "m", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, satir.Take(9).ToList(),
                new[] { 70.0, 42, 42, 42, 42, 42, 42, 42, 42, 42 }, (r, c) => r == h.KarSatir && c == h.KarBolge, 9.5,
                new[] { "Rakım", "BÖLGELER" });
            p.Paragraf(">1000 m: 1000 m'ye tekabül eden değerler, 1500 m'ye kadar %10, 1500 m'den yukarı yüksekliklerde %15 artırılır. Kar yağmayan yerlerde kar yükü hesap değeri sıfır alınır.", 9, italik: true);

            p.Yer(250);
            p.Baslik("4.2. RÜZGAR YÜKÜ", 11);
            p.Paragraf("Rüzgar yükü hesabında TS 498 yükleme kriterleri (Çizelge 4 ve Şekil 2) uygulanmıştır.");
            p.Tablo(new[] { "Zeminden Yükseklik (m)", "Rüzgar Hızı v (m/s)", "Emme Basıncı q (kN/m²)" },
                RuzgarTablosu.Satirlar.Select(s => new[] { s.Aralik, N(s.Hiz, 0), N(s.Q, 1) }).ToList(),
                new[] { 140.0, 130, 140 }, (r, c) => r == h.RuzgarSatir && c >= 0, 9.5);
            p.Deger("Bina yüksekliği", "H =", N(g.BinaYuksekligi, 2), "m", $"→ q = {N(h.QTabloKn, 2)} kN/m²");
            p.Paragraf($"NOT: FV modüller ve bağlantı elemanları {N(g.RuzgarHiziKmSa, 0)} km/sa hızındaki rüzgara dayanabilecek kapasitede rüzgar direncine sahip olacaktır.", 10.5, italik: true);
            p.Deger("", "v =", N(g.RuzgarHiziKmSa, 0), "km/sa", $"= {N(h.VMs, 3)} m/s");
            p.Deger("q = v² / 1600", "=", N(h.QHizKn, 3), "kN/m²");
            p.Deger("Tasarım rüzgar basıncı", "q =", N(h.QKn, 3), "kN/m²", $"= {N(h.QKg, 2)} kg/m² (büyük)", true);
            p.Bosluk(4);
            p.Cizim(120, (gx, r) => RuzgarSekli(gx, r));
            p.Paragraf("Şekil 2 – Planda kare kesitli ve eğik çatılı kapalı yapılarda rüzgar (TS 498)", 9.5, italik: true, yasla: false);
        }

        private static void RuzgarSekli(XGraphics gx, XRect r)
        {
            var k = new XPen(Renk.Lacivert, 1.1);
            var ok = new XPen(Renk.Kobalt, 0.9);
            var f = PdfYazici.F(8.5, italik: true);
            double x0 = r.X + 120, w = 200, z = r.Y + r.Height - 8, duvar = 55, mahya = 40;
            var a = new XPoint(x0, z - duvar); var b = new XPoint(x0 + w / 2, z - duvar - mahya); var c = new XPoint(x0 + w, z - duvar);
            gx.DrawLine(k, x0, z, a.X, a.Y); gx.DrawLine(k, a, b); gx.DrawLine(k, b, c); gx.DrawLine(k, c.X, c.Y, x0 + w, z); gx.DrawLine(k, x0, z, x0 + w, z);
            for (int i = 1; i <= 3; i++)
            {
                double t = i / 4.0;
                double px = a.X + (b.X - a.X) * t, py = a.Y + (b.Y - a.Y) * t;
                gx.DrawLine(ok, px - 8, py - 14, px, py);
                double qx = b.X + (c.X - b.X) * t, qy = b.Y + (c.Y - b.Y) * t;
                gx.DrawLine(ok, qx, qy, qx + 8, qy - 14);
                double yy = z - duvar * t;
                gx.DrawLine(ok, x0 - 22, yy, x0, yy);
                gx.DrawLine(ok, x0 + w, yy, x0 + w + 22, yy);
            }
            gx.DrawString("(1,2 sinα − 0,4) q", f, XBrushes.Black, a.X - 70, b.Y - 4);
            gx.DrawString("− 0,4 q", f, XBrushes.Black, c.X - 20, b.Y - 2);
            gx.DrawString("+ 0,8 q", f, XBrushes.Black, x0 - 70, z - duvar / 2);
            gx.DrawString("− 0,4 q", f, XBrushes.Black, x0 + w + 26, z - duvar / 2);
            gx.DrawString("Rüzgar yönü →", f, XBrushes.Black, x0 - 110, z - 4);
        }

        // ------------------------------------------------------------------ 5
        private static void KaplamaTahkiki(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(330);
            p.Baslik("5. MEVCUT ÇATI KAPLAMASI TAHKİKİ");
            p.Baslik("Mevcut Çatı Kaplaması", 11, true);
            p.Deger("* Kaplama", "", g.KaplamaTipi, "", null, xDeger: 300);
            p.Deger("Sac kalınlığı", "=", N(g.SacKalinligi, 2), "mm");
            p.Deger("Kaplama ağırlığı", "=", N(g.KaplamaAgirligi, 1), "kg/m²");
            p.Baslik("Gelen Yükler", 11, true);
            p.Deger("Güneş panel ebatları", "a x b =", $"{N(g.PanelA, 3)} x {N(g.PanelB, 3)}", "m");
            p.Deger("Güneş panel ağırlığı", "g =", N(g.PanelAgirlik, 1), "kg");
            p.Deger("Bağlantı elemanları ağırlığı", "=", N(g.BaglantiAgirligi, 1), "kg/m");
            p.Baslik("Yük Analizi", 11, true);
            p.Deger("Kar yükü", "q =", N(h.KarKg, 3), "kg/m²");
            p.Deger("Güneş panel ağırlığı (g / a·b)", "=", N(h.GPanel, 3), "kg/m²");
            p.Deger("Bağlantı elemanları ağırlığı", "=", N(h.GBaglanti, 3), "kg/m²");
            p.Deger("Çatı kaplaması ağırlığı", "=", N(h.GKaplama, 3), "kg/m²");
            p.Deger("Çatıya gelen toplam yük", "g =", N(h.GToplam, 3), "kg/m²");
            p.Deger("P = 1,4 g + 1,6 q", "=", N(h.PKaplama, 2), "kg/m²", null, true);
            p.Baslik("Yük Tahkiki", 11, true);
            p.Deger("Aşık aralığı", "=", N(g.AsikAraligi, 2), "m");
            p.Deger("Kaplamanın taşıyabileceği yük", "Ga =", N(h.Ga, 0), "kg/m²",
                h.KaplamaTablodan ? (g.AsikAraligi < 1.0 - 1e-9 ? "(tablo 1,00 m değeri × 1,00/L)" : "(üretici yük taşıma tablosu)") : "(kullanıcı tanımlı)");
            p.Deger($"Ga = {N(h.Ga, 0)} kg/m²", h.KaplamaYeterli ? ">" : "<", $"P = {N(h.PKaplama, 2)}", "kg/m²",
                h.KaplamaYeterli ? "...Kesit Yeterli" : "...Kesit YETERSİZ", true);
            p.Sonuc(h.KaplamaYeterli
                ? "Güneş panellerinden kaynaklı yükler mevcut çatı kaplaması tarafından emniyetli şekilde taşınabilmektedir."
                : "Mevcut çatı kaplaması gelen yükleri taşıyamamaktadır; aşık aralığı azaltılmalı veya kaplama güçlendirilmelidir.", h.KaplamaYeterli);
            p.Paragraf("(Kaplama taşıma kapasitesi, eklerde yer alan katalog bilgilerinden elde edilmiştir.)", 10, italik: true);
        }

        // ------------------------------------------------------------------ 6
        private static void BulonTahkiki(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(400);
            p.Baslik("6. GÜNEŞ PANELİ BAĞLANTI BULONU TAHKİKİ");
            p.Paragraf("Rüzgar yükü hesabında TS 498 yükleme kriterleri uygulanmıştır (Çizelge 4, Şekil 2).");
            p.Deger("Çatı eğimi", "α =", N(g.CatiEgimi, 1), "°", $"sin α = {N(h.SinA, 3)}");
            p.Deger("Tasarım rüzgar basıncı", "q =", N(h.QKg, 2), "kg/m²");
            p.Deger("Rüzgar soldan eserse; W1 = (1,2 sin α − 0,4) q", "=", N(h.W1, 2), "kg/m²", null, false, 250, 330, 375);
            p.Deger("W2 = − 0,40 q", "=", N(h.W2, 2), "kg/m²", null, false, 250, 330, 375);
            p.Deger("En büyük emme (kaldırma) kuvveti", "W =", N(-h.WEmme, 2), "kg/m²", null, true, 250, 330, 375);
            p.Paragraf("* Kar yükü (karşı koyan kuvvet) alınmamıştır.", 10.5, true, true);
            p.Deger("Güneş panel ağırlığı", "=", N(h.GPanel, 2), "kg/m²");
            p.Deger("Bağlantı elemanları ağırlığı", "=", N(h.GBaglanti, 2), "kg/m²");
            p.Deger("Çatıya gelen toplam düşey yük", "P =", N(h.GDusey, 2), "kg/m²", $"{(h.KaldirmaEtkili ? "<" : ">")} |W| = {N(h.WEmme, 2)} kg/m²", true);
            p.Paragraf(h.KaldirmaEtkili
                ? "Bu durumda rüzgar kaldırma kuvveti etkili olacaktır. Buna göre bağlantı elemanları tahkik edilecektir (zati yükün karşı koyan etkisi güvenli tarafta kalınarak ihmal edilmiştir)."
                : "Zati yük kaldırma kuvvetinden büyüktür; buna rağmen bağlantı elemanları kaldırma kuvvetine göre güvenli tarafta tahkik edilmiştir.");
            p.Deger("Seçilen bağlantı profili kalınlığı", "t =", N(h.Tb, 2), "cm");
            p.Deger("Seçilen bulon çapı", "", $"M{N(g.BulonCapi, 0)}", "", $"d = {N(h.D, 2)} cm");
            p.Deger("Bulon emniyet gerilmeleri", "τem =", N(Baglanti.TauEm, 0), "kg/cm²", "Makaslama");
            p.Deger("", "σlem =", N(Baglanti.SigmaLEm, 0), "kg/cm²", "Ezilme");
            p.Deger("", "σzem =", N(Baglanti.SigmaZEm, 0), "kg/cm²", "Çekme");
            p.Deger("Makaslamaya göre", "N1 = τem·πd²/4 =", N(h.N1, 1), "kg", null, false, 120);
            p.Deger("Ezilmeye göre", "N2 = d·t·σlem =", N(h.N2, 1), "kg", null, false, 120);
            p.Deger("Çekmeye göre", "N3 = σzem·πd²/4 =", N(h.N3, 1), "kg", null, false, 120);
            p.Deger("Rüzgar yükü %15 artırılabilir", "1,15·W =", N(h.WTasarim, 2), "kg/m²", null, false, 160);
            p.Deger("Bir bulonun yük alanı", "A = a·b =", N(h.AlanBulon, 3), "m²", $"({N(g.PanelA, 3)} x {N(g.PanelB, 3)} m)", false, 160);
            p.Deger("Bir bulona gelen rüzgar yükü", "Wrüz =", N(h.WRuz, 2), "kg", "(1,15·W·A·0,5)", false, 160);
            p.Deger("Karşı koyan kuvvet", "Wkar =", N(0, 2), "kg", "Kar yükü 0 alınmıştır", false, 160);
            p.Deger($"Mesnette {g.MesnetBulonAdedi} adet bulon", "W =", N(h.WBulon, 2), "kg", null, false, 160);
            p.Deger($"N = min(N1,N2,N3) = {N(h.NBulon, 1)} kg", h.BulonYeterli ? ">" : "<", $"W = {N(h.WBulon, 2)}", "kg", null, true, 220, 330, 375);
            p.Sonuc(h.BulonYeterli ? "Birleşimdeki bulon yeterlidir." : "Birleşimdeki bulon YETERSİZDİR; bulon çapı veya adedi artırılmalıdır.", h.BulonYeterli);
        }

        // ------------------------------------------------------------------ 7
        private static void VidaTahkiki(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(260);
            p.Baslik("7. ALÜMİNYUM PROFİL – ÇATI KAPLAMASI BAĞLANTI (VİDA) TAHKİKİ");
            p.Paragraf($"En olumsuz yükleme durumu rüzgar emmesidir. Bir tutucuya gelen kaldırma kuvveti Wrüz = {N(h.WRuz, 2)} kg olup her mesnet kaplamaya {g.ProfilVidaAdedi} adet Ø{N(g.VidaCapi, 0)} mm vida ile sabitlenmiştir.");
            p.Deger("Bir vidaya gelen çekme kuvveti", "P1 = Wrüz / n =", N(h.PVida, 2), "kg", null, false, 190, 330, 375);
            p.Deger("Vida çekirdek alanı (π(0,8d)²/4)", "Av =", N(h.AVida, 3), "cm²", null, false, 190, 330, 375);
            p.Deger("Kesme", "τ = P1/Av =", N(h.TauVida, 1), "kg/cm²", $"{(h.TauVida <= Baglanti.VidaTauEm ? "<" : ">")} {N(Baglanti.VidaTauEm, 0)} kg/cm²", false, 190, 330, 375);
            p.Deger("Çekme", "σç = P1/Av =", N(h.SigmaZVida, 1), "kg/cm²", $"{(h.SigmaZVida <= Baglanti.VidaSigmaZEm ? "<" : ">")} {N(Baglanti.VidaSigmaZEm, 0)} kg/cm²", false, 190, 330, 375);
            p.Deger($"Ezilme (kaplama sacı t = {N(g.SacKalinligi, 2)} mm)", "σez = P1/(t·d) =", N(h.EzilmeVida, 1), "kg/cm²", $"{(h.EzilmeVida <= Baglanti.VidaEzilmeEm ? "<" : ">")} {N(Baglanti.VidaEzilmeEm, 0)} kg/cm²", false, 190, 330, 375);
            p.Sonuc(h.VidaYeterli ? "Alüminyum profil – çatı kaplaması bağlantı vidaları yeterlidir." : "Bağlantı vidaları YETERSİZDİR; vida adedi/çapı artırılmalıdır.", h.VidaYeterli);
        }

        // ------------------------------------------------------------------ 8
        private static void ProfilTahkiki(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(480);
            p.Baslik("8. GÜNEŞ PANEL TAŞIYICI ALÜMİNYUM PROFİLİN TAHKİKİ");
            p.Baslik("Kesit Özellikleri", 11, true);
            p.Deger("Seçilen kesit", "=", Aluminyum.Profil, "", null, true, 120, 260);
            p.Sutunlar(11, false, (0, "g ="), (60, $"{N(Aluminyum.ProfilG, 3)} kg/m"), (200, "t ="), (250, $"{N(Aluminyum.ProfilT, 1)} mm"));
            p.Sutunlar(11, false, (0, "Jx ="), (60, $"{N(Aluminyum.Jx, 2)} cm⁴"), (200, "Jy ="), (250, $"{N(Aluminyum.Jy, 2)} cm⁴"));
            p.Sutunlar(11, false, (0, "Wx ="), (60, $"{N(Aluminyum.Wx, 2)} cm³"), (200, "Wy ="), (250, $"{N(Aluminyum.Wy, 2)} cm³"));
            p.Sutunlar(11, false, (0, "E ="), (60, $"{N(Aluminyum.E, 0)} kg/cm²"), (200, "γ ="), (250, $"{N(Aluminyum.Gamma, 2)} gr/cm³"));
            p.Baslik("Yük Alanı", 11, true);
            p.Deger("Yük alanı (Lp/2)", "=", N(h.YukAlani, 3), "m", $"Panel uzunluğu Lp = {N(g.PanelA, 3)} m");
            p.Deger("Profil hesap açıklığı (mesnet arası)", "l =", N(h.L, 2), "m");
            p.Deger("Çatı eğimi", "α =", N(g.CatiEgimi, 1), "°", $"sin α = {N(h.SinA, 3)}   cos α = {N(h.CosA, 3)}");
            p.Baslik("Yükler", 11, true);
            p.Deger("Kar yükü", "=", N(h.QKarT, 4), "t/m²");
            p.Deger("Güneş panel ağırlığı", "=", N(h.QPanelT, 4), "t/m²");
            p.Deger("Gelen toplam düşey yük", "=", N(h.QAlanT, 4), "t/m²");
            p.Deger("Rüzgar emmesi (1,15·W)", "=", N(h.QRuzT, 4), "t/m²", h.RuzgarProfilBelirleyici ? "← belirleyici" : "(düşey yükten küçük)");
            p.Deger("q = max · yük alanı", "=", N(h.QHat, 4), "t/m");
            p.Deger("Profil ağırlığı", "=", N(h.QProfilT, 4), "t/m");
            p.Deger("q toplam", "=", N(h.QTop, 4), "t/m", null, true);
            p.Baslik("Gerilme Tahkiki", 11, true);
            p.Sutunlar(11, false, (0, "Yayılı yük qx = q·cos α ="), (190, $"{N(h.Qx, 4)} t/m"), (260, "qy = q·sin α ="), (360, $"{N(h.Qy, 4)} t/m"));
            p.Paragraf("Açıklık tahkiki;", 11, italik: true, kalin: true);
            p.Sutunlar(11, false, (0, "Mx = qx·l²/16 ="), (110, $"{N(h.MxAc * 1e5, 1)} kgcm"), (260, "My = qy·l²/16 ="), (360, $"{N(h.MyAc * 1e5, 1)} kgcm"));
            p.Sutunlar(11, false, (0, "σx = Mx/Wx ="), (110, $"{N(h.SxAc, 1)} kg/cm²"), (260, "σy = My/Wy ="), (360, $"{N(h.SyAc, 1)} kg/cm²"));
            p.Sutunlar(11, true, (0, "σ = σx + σy ="), (110, $"{N(h.SAc, 1)} kg/cm²"), (260, $"{(h.SAc <= Aluminyum.SigmaEm ? "<" : ">")} {N(Aluminyum.SigmaEm, 0)} kg/cm²  {(h.SAc <= Aluminyum.SigmaEm ? "...Kesit yeterli" : "...YETERSİZ")}"));
            p.Paragraf("Mesnet tahkiki;", 11, italik: true, kalin: true);
            p.Sutunlar(11, false, (0, "Mx = qx·l²/11 ="), (110, $"{N(h.MxMes * 1e5, 1)} kgcm"), (260, "My = qy·l²/11 ="), (360, $"{N(h.MyMes * 1e5, 1)} kgcm"));
            p.Sutunlar(11, false, (0, "σx = Mx/Wx ="), (110, $"{N(h.SxMes, 1)} kg/cm²"), (260, "σy = My/Wy ="), (360, $"{N(h.SyMes, 1)} kg/cm²"));
            p.Sutunlar(11, true, (0, "σ = σx + σy ="), (110, $"{N(h.SMes, 1)} kg/cm²"), (260, $"{(h.SMes <= Aluminyum.SigmaEm ? "<" : ">")} {N(Aluminyum.SigmaEm, 0)} kg/cm²  {(h.SMes <= Aluminyum.SigmaEm ? "...Kesit yeterli" : "...YETERSİZ")}"));
            p.Baslik("Sehim Tahkiki", 11, true);
            p.Sutunlar(11, false, (0, "δ = 5·q·l⁴ / (384·E·I)  (basit kiriş, güvenli taraf)"));
            p.Sutunlar(11, false, (0, "Düşey sehim  fx ="), (110, $"{N(h.Fx, 4)} cm"), (200, $"{(h.Fx <= h.FIzin ? "<" : ">")} l/300 = {N(h.FIzin, 3)} cm"), (360, h.Fx <= h.FIzin ? "...Kesit yeterli" : "...YETERSİZ"));
            p.Sutunlar(11, false, (0, "Yatay sehim  fy ="), (110, $"{N(h.Fy, 4)} cm"), (200, $"{(h.Fy <= h.FIzin ? "<" : ">")} l/300 = {N(h.FIzin, 3)} cm"), (360, h.Fy <= h.FIzin ? "...Kesit yeterli" : "...YETERSİZ"));
            p.Sonuc(h.ProfilYeterli ? "Güneş paneli taşıyıcı alüminyum profil gerilme ve sehim bakımından yeterlidir." : "Alüminyum taşıyıcı profil YETERSİZDİR; mesnet aralığı azaltılmalı veya kesit büyütülmelidir.", h.ProfilYeterli);
        }

        // ------------------------------------------------------------------ 9
        private static void Sonuc(PdfYazici p, Girdi g, Hesap h)
        {
            p.Yer(260);
            p.Baslik("9. DEĞERLENDİRME VE SONUÇ");
            double rK = h.PKaplama / Math.Max(h.Ga, 1e-9), rB = h.WBulon / Math.Max(h.NBulon, 1e-9);
            double rV = Math.Max(Math.Max(h.TauVida / Baglanti.VidaTauEm, h.SigmaZVida / Baglanti.VidaSigmaZEm), h.EzilmeVida / Baglanti.VidaEzilmeEm);
            double rP = Math.Max(Math.Max(h.SAc, h.SMes) / Aluminyum.SigmaEm, Math.Max(h.Fx, h.Fy) / h.FIzin);
            p.Kutucuklar(new (string, string, XColor?)[]
            {
                ($"%{N(rK * 100, 0)}", "Çatı kaplaması kullanım oranı", Renk.Uygun),
                ($"%{N(rB * 100, 0)}", "Bağlantı bulonu kullanım oranı", Renk.Uygun),
                ($"%{N(rV * 100, 0)}", "Bağlantı vidası kullanım oranı", Renk.Uygun),
                ($"%{N(rP * 100, 0)}", "Alüminyum profil kullanım oranı", Renk.Uygun),
            });
            string yer = $"{Up(g.Il)} ili, {Up(g.Ilce)} ilçesi {Up(g.Mahalle)} Mahallesi" + (string.IsNullOrWhiteSpace(g.Ada) ? "" : $" {g.Ada} ada {Bos(g.Parsel, "-")} parselde");
            string sahip = string.IsNullOrWhiteSpace(g.YapiSahibi) ? "" : $"{Up(g.YapiSahibi)} adına, ";
            p.Paragraf($"{yer} {sahip}çatı üzerine yapılacak olan çatı uygulamalı güneş enerji sistemi için yapılan bu hesap raporunda, gerekli tüm bilgi ve veriler mimari projeden elde edilmiş ve rapor bu bilgiler ışığında tamamlanmıştır. Hesaplar kapsamında yapılan tüm tahkikler en gayri müsait durumlara göre yapılmıştır.");
            p.Bosluk(4);
            var liste = new (string, bool)[]
            {
                ("Mevcut çatı kaplaması", h.KaplamaYeterli), ("Güneş paneli bağlantı bulonu", h.BulonYeterli),
                ("Profil – kaplama bağlantı vidaları", h.VidaYeterli), ("Alüminyum taşıyıcı profil", h.ProfilYeterli),
            };
            p.Tablo(new[] { "Tahkik", "Sonuç" }, liste.Select(x => new[] { x.Item1, x.Item2 ? "YETERLİ" : "YETERSİZ" }).ToList(), new[] { 260.0, 120 }, (r, c) => !liste[r].Item2 && c == 1);
            p.Bosluk(4);
            if (h.HepsiYeterli)
            {
                p.Paragraf("Mevcut kaplamalar ve yeni uygulanacak panel taşıyıcı alüminyum profiller için yapılan tahkikler sonucu; kaplamaların ve yeni uygulanacak panel taşıyıcı alüminyum profillerin, yapılacak olan güneş enerji sistemini güvenle taşıyacağı sonucu elde edilmiştir.");
                p.Paragraf("Bu nedenle güneş enerji sisteminin mevcut binanın çatısına yapılmasında herhangi bir sakınca yoktur.", kalin: true);
            }
            else
                p.Paragraf("Yukarıdaki tabloda YETERSİZ çıkan tahkikler için önerilen önlemler alınmadan güneş enerji sistemi uygulanmamalıdır.", kalin: true, renk: new XSolidBrush(XColor.FromArgb(190, 30, 30)));

            p.Yer(110);
            p.Bosluk(30);
            p.SeritAcik = false;
            p.Sutunlar(11, true, (300, "HAZIRLAYAN"));
            if (!string.IsNullOrWhiteSpace(g.HazirlayanMuhendis)) p.Sutunlar(10.5, false, (300, g.HazirlayanMuhendis));
            p.Paragraf(Bos(g.Hazirlayan, ""), 9.5, girinti: 300, yasla: false);
            p.Sutunlar(10.5, false, (300, DateTime.Now.ToString("dd.MM.yyyy", Tr)));
        }

        // ------------------------------------------------------------------ ekler
        /// <summary>Raporla birlikte her zaman verilen sabit ekler (başlık bantları kırpılıp rapor başlığıyla gömülür).</summary>
        private static readonly (string Dosya, string Baslik, double KesUst, double KesAlt)[] SabitEkler =
        {
            ("EK-1 Gunes paneli fiziksel ozellikler.pdf", "GÜNEŞ PANELİ FİZİKSEL ÖZELLİKLERİ (2095 × 1039 × 40 mm)", 0.116, 0.050),
            ("EK-2 GES altinda trapez sac.pdf", "GES ALTINDA TRAPEZ SAC — FİZİKSEL ÖZELLİKLER VE UYGULAMA", 0.116, 0.042),
        };

        private static void Ekler(PdfYazici p, Girdi g)
        {
            string kl = Path.Combine(AppContext.BaseDirectory, "Varliklar", "ekler-sabit");
            var liste = new List<(string Yol, string Baslik, double KesUst, double KesAlt)>();
            foreach (var (d, b, ku, ka) in SabitEkler)
                if (File.Exists(Path.Combine(kl, d))) liste.Add((Path.Combine(kl, d), b, ku, ka));
            foreach (var e in (g.EkPdfler ?? new()).Where(File.Exists))
                liste.Add((e, Path.GetFileNameWithoutExtension(e).ToUpper(Tr), 0, 0));

            // EKLER ayırıcı sayfası
            p.YeniSayfa();
            p.Y = 250;
            var G = p.Gfx;
            G.DrawRectangle(Renk.F(Renk.Turuncu), PdfYazici.Sol, p.Y + 4, 5, 46);
            p.Metin("EKLER", PdfYazici.P(34, true), Renk.Lacivert, PdfYazici.Sol + 16, p.Y);
            p.Y += 70;
            for (int i = 0; i < liste.Count; i++)
            {
                p.Metin($"EK-{i + 1}", PdfYazici.P(11, true), Renk.Kobalt, PdfYazici.Sol + 16, p.Y);
                p.Metin(liste[i].Baslik, PdfYazici.P(10.5), Renk.Koyu, PdfYazici.Sol + 70, p.Y);
                G.DrawLine(new XPen(Renk.Cizgi, 0.6), PdfYazici.Sol + 16, p.Y + 20, PdfYazici.Sol + p.Genislik, p.Y + 20);
                p.Y += 28;
            }
            if (liste.Count == 0) p.Paragraf("(Ek bulunmamaktadır.)", italik: true, yasla: false);

            for (int i = 0; i < liste.Count; i++)
            {
                try { p.EkGom($"EK-{i + 1}  {liste[i].Baslik}", liste[i].Yol, liste[i].KesUst, liste[i].KesAlt); }
                catch (Exception ex)
                {
                    p.YeniSayfa();
                    p.Paragraf($"EK-{i + 1} eklenemedi: {Path.GetFileName(liste[i].Yol)} — {ex.Message}", italik: true);
                }
            }
        }
    }
}
