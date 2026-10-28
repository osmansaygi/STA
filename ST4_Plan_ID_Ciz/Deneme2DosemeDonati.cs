using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4Yardimci;

namespace ST4PlanIdCiz
{
    /// <summary>DENEME2: donatılı kopyada bir döşemenin eksen artısı merkezi ve X / Y donatı doğrultuları (rad, WCS).</summary>
    public readonly struct Deneme2EksenKaydi
    {
        public Deneme2EksenKaydi(int copyNo, int slabId, double x, double y, double angleX, double angleY, double areaCm2, Point2d[] quad,
            double thicknessCm = 0, double? ustKotM = null, bool donatiYok = false)
        {
            DonatiYok = donatiYok;
            AreaCm2 = areaCm2;
            Quad = quad;
            ThicknessCm = thicknessCm;
            UstKotM = ustKotM;
            CopyNo = copyNo;
            SlabId = slabId;
            X = x;
            Y = y;
            AngleX = angleX;
            AngleY = angleY;
        }
        public int CopyNo { get; }
        public int SlabId { get; }
        public double X { get; }
        public double Y { get; }
        public double AngleX { get; }
        public double AngleY { get; }
        /// <summary>Döşeme ham eksen dörtgeni alanı: büyük döşemenin şeridi önce çizilir (sıra çizgisi merkezinden geçsin).</summary>
        public double AreaCm2 { get; }
        /// <summary>Döşeme ham eksen dörtgeni (aks–aks, kapalı halka): çizilmiş döşemeye sonraki şeritler girmez.</summary>
        public Point2d[] Quad { get; }
        /// <summary>ST4 döşeme kalınlığı (cm).</summary>
        public double ThicknessCm { get; }
        /// <summary>ST4 döşeme üst kotu (m): süreklilik (L/4) etiket/kot yazısı yerine bununla.</summary>
        public double? UstKotM { get; }
        /// <summary>Merdivene bağlı ara sahanlık: donatı çizilmez, şeritler bu döşemede durur.</summary>
        public bool DonatiYok { get; }
    }

    /// <summary>
    /// DENEME2: kalıp planı commit edildikten sonra Yardımcı DOSEMEDONATI çekirdeği ile gerçek döşeme donatısı.
    /// Her kopya ve yön için döşemeler sırayla gezilir; artı merkezinden geçen çizgi kat sınırı içinde kat boyunca uzatılır,
    /// önceki şeritlerde çizilen ve eksen doğrultusu farklı döşemelerin aks dörtgeninde kesilir. Sonra GPR yazı/kılavuz çizgileri silinir.
    /// </summary>
    internal static class Deneme2DosemeDonati
    {
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerAltDuzAlan = "ALT DUZ ALAN (BEYKENT)";
        private const string LayerKatSiniri = "KAT SINIRI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiYazisi = "DONATI YAZISI (BEYKENT)";
        /// <summary>Konsol/kenar düzü kat sınırına kadar ulaşsın: çizgi kat sınırından bu kadar taşar (cm).</summary>
        private const double KatSiniriTasmaCm = 30.0;
        /// <summary>Çizilmiş açıklık yüzünden bu kadar önce durulur: kiriş geçilir, komşu açıklığa girilmez (cm).</summary>
        private const double CizilmisYuzPayCm = 2.0;
        /// <summary>Yardımcı DosemeDonatiRunner.MinSpanCm ile aynı.</summary>
        private const double MinCizgiCm = 40.0;
        /// <summary>Yardımcı ParkBelowCm: eşleşmeyen ilave bu kadar aşağı taşınır; burada yerine geri alınır.</summary>
        private const double ParkCm = 10000.0;
        /// <summary>Tanı satırları yalnız ST4_DENEME2_TANI=1 ortam değişkeniyle (komut satırını doldurmasın).</summary>
        private static bool Tani => Environment.GetEnvironmentVariable("ST4_DENEME2_TANI") == "1";

        private sealed class Ring
        {
            public Point2d[] Pts;
            public double MinX, MinY, MaxX, MaxY;

            public bool Contains(double x, double y)
            {
                if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
                bool inside = false;
                for (int i = 0, j = Pts.Length - 1; i < Pts.Length; j = i++)
                {
                    Point2d a = Pts[i], b = Pts[j];
                    if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
                return inside;
            }

            /// <summary>Çizgi (p + t·u) ile kenar kesişimlerinin sıralı t değerleri.</summary>
            public List<double> Crossings(Point2d p, Vector2d u)
            {
                var ts = new List<double>();
                for (int i = 0, j = Pts.Length - 1; i < Pts.Length; j = i++)
                {
                    Point2d a = Pts[j], b = Pts[i];
                    Vector2d e = b - a;
                    double den = u.X * e.Y - u.Y * e.X;
                    if (Math.Abs(den) < 1e-12) continue;
                    Vector2d w = a - p;
                    double t = (w.X * e.Y - w.Y * e.X) / den;
                    double s = (w.X * u.Y - w.Y * u.X) / den;
                    if (s < -1e-9 || s > 1 + 1e-9) continue;
                    ts.Add(t);
                }
                ts.Sort();
                return ts;
            }
        }

        internal static void Run(Document doc, IReadOnlyList<Deneme2EksenKaydi> kayitlar, long startHandle,
            IReadOnlyDictionary<int, (DmxDosemeDonati.Rec Rec, double? UstKotM)> dmxBySlabId = null)
        {
            // Şeritler boyunca model alanı dizini bir kez (taramalar yalnız ilgili katman/kutu); hata olsa da bırakılır.
            DosemeDonatiRunner.BeginAutoSession();
            using KalipIlerleme ilerleme = KalipIlerleme.BaslatYoksa("DENEME2 donati", doc?.Editor);
            try { RunCore(doc, kayitlar, startHandle, dmxBySlabId); }
            finally
            {
                DosemeDonatiRunner.EndAutoSession();
                DosemeDonatiRunner.AutoTimes = null;
            }
        }

        private static void RunCore(Document doc, IReadOnlyList<Deneme2EksenKaydi> kayitlar, long startHandle,
            IReadOnlyDictionary<int, (DmxDosemeDonati.Rec Rec, double? UstKotM)> dmxBySlabId)
        {
            if (doc == null || kayitlar == null || kayitlar.Count == 0) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            if (!DosemeDonatiRunner.PreloadForAuto(ed))
            {
                ed.WriteMessage("\nDENEME2: Doseme donatisi icin geometri kutuphaneleri yuklenemedi; donati cizilmedi.");
                return;
            }

            DosemeDonatiRunner.AutoTimes = Tani ? new long[11] : null;
            DosemeDonatiRunner.AutoGenislikOnbellekTemizle();
            List<Ring> katAll = ReadRings(db, LayerKatSiniri, startHandle);
            List<(string Name, Point2d Pos)> etiketler = ReadSlabLabels(db, startHandle);
            int seritSayisi = 0, aciklikSayisi = 0;
            int ilerlemeToplam = Math.Max(1, kayitlar.Count * 2), ilerlemeIslenen = 0;
            var atlanan = new List<string>();
            var cizilmeyen = new List<string>();
            var kirinti = new List<string>();
            foreach (var kopya in kayitlar.GroupBy(k => k.CopyNo).OrderBy(g => g.Key))
            {
                List<Ring> katRings = katAll.Where(r => kopya.Any(k => r.Contains(k.X, k.Y))).ToList();
                // Döşeme adı (DOSEME ISMI): Yardımcı çekirdeği çizdiği açıklığı bu adla bildirir.
                var adlar = new Dictionary<int, string>();
                var halka = new Dictionary<int, Ring>();
                foreach (Deneme2EksenKaydi k in kopya)
                {
                    Ring r = ToRing(k.Quad);
                    if (r == null) continue;
                    halka[k.SlabId] = r;
                    string ad = etiketler
                        .Where(e => r.Contains(e.Pos.X, e.Pos.Y))
                        .OrderBy(e => e.Pos.GetDistanceTo(new Point2d(k.X, k.Y)))
                        .Select(e => e.Name)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(ad)) adlar[k.SlabId] = ad;
                }
                // Kimlik: etiket adı yalnız tekse; etiketsiz / aynı adı paylaşan döşeme "#id" (döşeme etiketten değil ST4 alanından tanınır).
                var adSayisi = adlar.Values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                foreach (int sid in adlar.Keys.ToList())
                    if (adSayisi[adlar[sid]] > 1) adlar.Remove(sid);
                foreach (Deneme2EksenKaydi k in kopya)
                    if (!adlar.ContainsKey(k.SlabId)) adlar[k.SlabId] = "#" + k.SlabId;
                string AdOf(Deneme2EksenKaydi k) => adlar.TryGetValue(k.SlabId, out string s) ? s : "#" + k.SlabId;

                if (Tani)
                    foreach (Deneme2EksenKaydi k in kopya)
                        ed.WriteMessage("\nDENEME2_TANI kayit kopya={0} id={1} ad={2} x={3:0} y={4:0}", k.CopyNo, k.SlabId, AdOf(k), k.X, k.Y);
                var alanlar = new List<DosemeDonatiRunner.AutoSlabArea>();
                foreach (Deneme2EksenKaydi k in kopya)
                {
                    if (!halka.TryGetValue(k.SlabId, out Ring hr)) continue;
                    alanlar.Add(new DosemeDonatiRunner.AutoSlabArea
                    {
                        Ring = hr.Pts,
                        Name = AdOf(k),
                        ThicknessCm = k.ThicknessCm,
                        UstKotM = k.UstKotM,
                        MinX = hr.MinX,
                        MinY = hr.MinY,
                        MaxX = hr.MaxX,
                        MaxY = hr.MaxY,
                        Area = k.AreaCm2
                    });
                }

                // DMX donatısı döşeme adına göre (çekirdek açıklığı DOSEME ISMI adıyla tanır). Yoksa GPR yazısı yolu.
                Dictionary<string, DosemeDonatiRunner.AutoSlabSteel> celikX = null, celikY = null;
                if (dmxBySlabId != null && dmxBySlabId.Count > 0)
                {
                    celikX = new Dictionary<string, DosemeDonatiRunner.AutoSlabSteel>(StringComparer.OrdinalIgnoreCase);
                    celikY = new Dictionary<string, DosemeDonatiRunner.AutoSlabSteel>(StringComparer.OrdinalIgnoreCase);
                    var donatisiz = new HashSet<int>(kopya.Where(k => k.DonatiYok).Select(k => k.SlabId));
                    foreach (var kv in adlar)
                    {
                        if (donatisiz.Contains(kv.Key) || !dmxBySlabId.TryGetValue(kv.Key, out var d)) continue;
                        celikX[kv.Value] = ToSteel(d.Rec.X, d.UstKotM);
                        celikY[kv.Value] = ToSteel(d.Rec.Y, d.UstKotM);
                    }
                }

                foreach (DosemeDonatiRunner.DonatiYonu yon in new[] { DosemeDonatiRunner.DonatiYonu.X, DosemeDonatiRunner.DonatiYonu.Y })
                {
                    bool xYon = yon == DosemeDonatiRunner.DonatiYonu.X;
                    var cizilenAd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var cizilmis = new List<Ring>();
                    // Merdivene bağlı ara sahanlık: çizilmiş sayılır (donatı yok), şeritler aks dörtgeninde durur.
                    foreach (Deneme2EksenKaydi k in kopya)
                    {
                        if (!k.DonatiYok) continue;
                        cizilenAd.Add(AdOf(k));
                        if (halka.TryGetValue(k.SlabId, out Ring ar)) cizilmis.Add(ar);
                    }
                    // Büyük döşeme önce: sıra çizgisi ana döşemelerin artı merkezinden geçer, küçükler kalan boşlukları doldurur.
                    IEnumerable<Deneme2EksenKaydi> sirali = kopya
                        .OrderByDescending(k => Math.Round(k.AreaCm2 / 10000.0, 1))
                        .ThenBy(k => xYon ? k.Y : k.X)
                        .ThenBy(k => xYon ? k.X : k.Y);
                    // Çizilmiş adlı açıklık, henüz çizilmemiş (küçük kırıntı olmayan) bir döşemenin merkezinden geçmiyorsa tekrar çizilmez.
                    bool TekrarMi(string ad, Point3d li, Point3d ri)
                    {
                        if (!cizilenAd.Contains(ad)) return false;
                        foreach (Deneme2EksenKaydi o in kopya)
                        {
                            if (o.AreaCm2 < KirintiAlanCm2 || cizilenAd.Contains(AdOf(o)) || !halka.TryGetValue(o.SlabId, out Ring orr)) continue;
                            if (AciklikMerkezdenGecer(li, ri, orr, new Point2d(o.X, o.Y), xYon)) return false;
                        }
                        return true;
                    }
                    foreach (Deneme2EksenKaydi k in sirali)
                    {
                        KalipIlerleme.Bildir((double)ilerlemeIslenen++ / ilerlemeToplam, "donati " + (xYon ? "X " : "Y ") + AdOf(k));
                        if (cizilenAd.Contains(AdOf(k))) continue;
                        double ang = xYon ? k.AngleX : k.AngleY;
                        var p = new Point2d(k.X, k.Y);
                        var u = new Vector2d(Math.Cos(ang), Math.Sin(ang));
                        var nrm = new Vector2d(-u.Y, u.X);
                        // 1/6 ve 1/2 hizası alt düz (90° üstü eğik Y: ilave) çubuğa göre: çekirdek alt düzü şerit çizgisinden bu kadar kaydırır.
                        Vector2d duzK = AltDuzKaymasi(u, xYon);
                        bool seritAcildi = false;
                        // Şerit yalnız aynı eksen doğrultusundaki döşemelerden geçer: farklı doğrultulu döşemenin kirişinde durur.
                        // merkezEngeli: çizgi döşemenin hiza bandından (alt/sağ kenardan 0,15–1/2) geçmiyorsa o döşemeye girilmez;
                        // köşesinden geçilen döşeme kendi şeridiyle çizilir.
                        List<Ring> Engeller(Point2d q0, bool merkezEngeli)
                        {
                            var engel = new List<Ring>(cizilmis);
                            foreach (Deneme2EksenKaydi o in kopya)
                            {
                                if (o.SlabId == k.SlabId || !halka.TryGetValue(o.SlabId, out Ring orr)) continue;
                                if (!AyniDogrultu(ang, xYon ? o.AngleX : o.AngleY)) { engel.Add(orr); continue; }
                                if (!merkezEngeli || cizilenAd.Contains(AdOf(o))) continue;
                                if (!HizaBandinda(orr, new Point2d(o.X, o.Y), q0 + duzK, HedefYonu(nrm, xYon))) engel.Add(orr);
                            }
                            return engel;
                        }
                        // 1. deneme: sıra hizası — çizgi bu döşemenin orta bandı içinde, aynı sıradaki en çok döşemeyi kendi bandından geçirecek ofsete kayar
                        // (komşu döşemelerin donatısı tek hatta; tercih alt/sağ 1/6 hizası). 2. deneme (çizilemediyse): kendi 1/6 hizası, 3. deneme: merkez;
                        // 2–3. denemede yalnız çizilmiş ve farklı doğrultulu döşeme engel.
                        bool merkezDenendi = false;
                        // 90° üstü eğik Y (hiza ilaveye göre): üçü de açıklık vermezse 4. deneme alt düz 1/6 hizası (yamuk döşemede ilave hizası kenara çarpabilir).
                        Vector2d altDuzK = AltDuzKaymasiDuz(u, xYon);
                        int denemeSayisi = (duzK - altDuzK).Length > 1e-6 ? 4 : 3;
                        for (int deneme = 0; deneme < denemeSayisi && !cizilenAd.Contains(AdOf(k)); deneme++)
                        {
                            bool merkezEngeli = deneme == 0;
                            Point2d pc = p;
                            Vector2d hedefYon = HedefYonu(nrm, xYon);
                            if (deneme == 3)
                            {
                                if (!halka.TryGetValue(k.SlabId, out Ring kr3)) break;
                                pc = p + nrm * (HedefOfset(kr3, p, p, nrm, hedefYon) - altDuzK.DotProduct(nrm));
                                if (!kr3.Contains(pc.X, pc.Y)) break;
                            }
                            if (deneme == 1 && halka.TryGetValue(k.SlabId, out Ring kr1))
                            {
                                // 2. deneme: kendi alt/sağ 1/6 hizası (döşeme içindeyse ve şerit açılıyorsa), yoksa merkez.
                                Point2d ph = p + nrm * (HedefOfset(kr1, p, p, nrm, hedefYon) - duzK.DotProduct(nrm));
                                if (kr1.Contains(ph.X, ph.Y) && TrySerit(ph, u, katRings, Engeller(ph, false), out _, out _)) pc = ph;
                            }
                            if (merkezEngeli && halka.TryGetValue(k.SlabId, out Ring kr))
                            {
                                double t = EnIyiSiraOfseti(kr, p, u, nrm, hedefYon, duzK, xYon, katRings, q0 => Engeller(q0, true),
                                    kopya.Where(o => o.SlabId != k.SlabId && o.AreaCm2 >= KirintiAlanCm2 && !cizilenAd.Contains(AdOf(o))
                                            && AyniDogrultu(ang, xYon ? o.AngleX : o.AngleY) && halka.ContainsKey(o.SlabId))
                                        .Select(o => (halka[o.SlabId], new Point2d(o.X, o.Y))).ToList(),
                                    out int sirada);
                                pc = p + nrm * t;
                                if (Tani && Math.Abs(t) > 0.5)
                                    ed.WriteMessage("\nDENEME2_TANI hiza {0} {1} ofset={2:0} sirada={3}", xYon ? "X" : "Y", AdOf(k), t, sirada);
                            }
                            if (!merkezEngeli)
                            {
                                // Merkez (engelsiz) bir kez denenir: 2. denemede 1/6 hizası açılmadıysa 3. deneme tekrar etmez.
                                bool merkezde = pc.GetDistanceTo(p) < 0.5;
                                if (merkezde && merkezDenendi) continue;
                                if (merkezde) merkezDenendi = true;
                            }
                            List<Ring> engel = Engeller(pc, merkezEngeli);
                            if (!TrySerit(pc, u, katRings, engel, out double lo, out double hi)) continue;
                            Point2d a2 = pc + u * lo, b2 = pc + u * hi;
                            var cizilen = new List<string>();
                            var acikliklar = new List<(string Name, Point3d LeftInner, Point3d RightInner)>();
                            int n;
                            try
                            {
                                n = DosemeDonatiRunner.PlaceStripAuto(doc, new Point3d(a2.X, a2.Y, 0), new Point3d(b2.X, b2.Y, 0), yon, cizilen,
                                    xYon ? celikX : celikY, alanlar, TekrarMi, acikliklar);
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage("\nDENEME2: doseme {0} {1} seridi hata: {2}", AdOf(k), xYon ? "X" : "Y", ex.Message);
                                break;
                            }
                            seritAcildi = true;
                            seritSayisi++;
                            aciklikSayisi += n;
                            if (Tani)
                                ed.WriteMessage("\nDENEME2_TANI serit {0} {1} deneme={5} lo={2:0} hi={3:0} cizilen=[{4}]",
                                    xYon ? "X" : "Y", AdOf(k), lo, hi, string.Join(",", cizilen), deneme + 1);
                            foreach (string ad in cizilen.Where(s => !string.IsNullOrEmpty(s)))
                            {
                                if (!cizilenAd.Add(ad)) continue;
                                foreach (var kv in adlar)
                                    if (string.Equals(kv.Value, ad, StringComparison.OrdinalIgnoreCase) && halka.TryGetValue(kv.Key, out Ring hr))
                                        cizilmis.Add(hr);
                            }
                            // Kirişsiz komşu (yalnız aksla ayrılan) döşeme: çizilen açıklık merkezinden geçiyorsa kapsanmış sayılır.
                            foreach (Deneme2EksenKaydi o in kopya)
                            {
                                if (cizilenAd.Contains(AdOf(o)) || !halka.TryGetValue(o.SlabId, out Ring orr)) continue;
                                if (!AyniDogrultu(ang, xYon ? o.AngleX : o.AngleY)) continue;
                                if (!acikliklar.Any(sp => AciklikMerkezdenGecer(sp.LeftInner, sp.RightInner, orr, new Point2d(o.X, o.Y), xYon))) continue;
                                cizilenAd.Add(AdOf(o));
                                cizilmis.Add(orr);
                                if (Tani) ed.WriteMessage("\nDENEME2_TANI kapsandi {0} {1}", xYon ? "X" : "Y", AdOf(o));
                            }
                        }
                        if (!seritAcildi)
                        {
                            atlanan.Add((xYon ? "X:" : "Y:") + AdOf(k));
                            cizilenAd.Add(AdOf(k));
                            continue;
                        }
                        // Kendi şeridinde çizilemeyen döşeme tekrar denenmesin (sonraki şeritler yine buradan geçmez).
                        if (!cizilenAd.Contains(AdOf(k)))
                        {
                            (k.AreaCm2 < KirintiAlanCm2 ? kirinti : cizilmeyen).Add((xYon ? "X:" : "Y:") + AdOf(k));
                            cizilenAd.Add(AdOf(k));
                        }
                    }
                }
            }

            DosemeDonatiRunner.AutoGenislikOnbellekTemizle();
            if (DosemeDonatiRunner.AutoTimes != null)
            {
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                string[] adSlot = { "tarama-diger", "NTS-model", "aciklik-cizim", "ilave+poligon", "commit",
                    "kesisim+mesnet", "sinir-kenar", "kot-cache", "sinir-egri", "gpr", "genislik" };
                ed.WriteMessage("\nDENEME2_TANI sure(ms): " + string.Join(" ",
                    DosemeDonatiRunner.AutoTimes.Select((t, i) => adSlot[i] + "=" + (t * f).ToString("0"))));
                DosemeDonatiRunner.AutoTimes = null;
            }
            KalipIlerleme.Bildir(1.0, "temizlik");
            TemizleGprVeParki(db, startHandle, katAll, out int silinenGpr, out int geriAlinan);
            ed.WriteMessage(
                "\nDENEME2: doseme donatisi {0} seritte {1} aciklik; GPR yazi/cizgi ve kontrol poligonu {2} silindi; eslesmeyen {3} nesne asagidan yerine alindi (kirmizi).",
                seritSayisi, aciklikSayisi, silinenGpr, geriAlinan);
            if (cizilmeyen.Count > 0)
                ed.WriteMessage("\nDENEME2: {0} doseme/yon icin donati acikligi olusmadi: {1}",
                    cizilmeyen.Count, string.Join(" ", cizilmeyen.Take(40)));
            if (kirinti.Count > 0)
                ed.WriteMessage("\nDENEME2: {0} kucuk (<1,5 m2, kiris kesisimi altinda) doseme/yon acikliksiz: {1}",
                    kirinti.Count, string.Join(" ", kirinti.Take(40)));
            if (atlanan.Count > 0)
                ed.WriteMessage("\nDENEME2: {0} serit kat siniri disinda/kisa kaldi: {1}",
                    atlanan.Count, string.Join(" ", atlanan.Take(40)));
        }

        /// <summary>Aynı eksen doğrultusu sayılan en büyük açı farkı (rad, 1°). Grup artıları aynı açıyı paylaşır.</summary>
        private const double AyniDogrultuTolRad = Math.PI / 180.0;
        /// <summary>
        /// Hiza bandı (alt/sağ kenardan, şerite dik genişliğe oranla): çizgi döşemenin bu aralığından geçmiyorsa o döşemeye girilmez
        /// (köşeden geçilmez; hizalanınca 1/2'yi aşan döşeme hizaya katılmaz, kendi şeridiyle çizilir). Tercih 1/6.
        /// </summary>
        private const double HizaMinOrani = 0.15;
        private const double HizaMaxOrani = 0.50;
        /// <summary>Sıra hizası ofset arama adımı (cm).</summary>
        private const double SiraOfsetAdimCm = 5.0;
        /// <summary>Bundan küçük (cm², 1,5 m²) döşeme kiriş kesişimi altındaki kırıntı sayılır: başka döşemenin açıklığını yeniden çizdirmez.</summary>
        private const double KirintiAlanCm2 = 15000.0;

        /// <summary>
        /// Çizilen açıklık (iç yüzler arası) döşemenin içinden, döşemenin açıklık yönündeki uzunluğunun en az %30'u boyunca
        /// ve hiza bandından (<see cref="HizaMinOrani"/>–<see cref="HizaMaxOrani"/>, alt/sağ kenardan) geçiyor mu.
        /// </summary>
        private static bool AciklikMerkezdenGecer(Point3d a3, Point3d b3, Ring ring, Point2d merkez, bool xYon)
        {
            var a = new Point2d(a3.X, a3.Y);
            Vector2d ab = new Point2d(b3.X, b3.Y) - a;
            double len = ab.Length;
            if (len < 1.0) return false;
            Vector2d u = ab / len;
            var nrm = new Vector2d(-u.Y, u.X);
            double uMin = double.MaxValue, uMax = double.MinValue;
            foreach (Point2d q in ring.Pts)
            {
                double du = (q - a).DotProduct(u);
                if (du < uMin) uMin = du;
                if (du > uMax) uMax = du;
            }
            if (!HizaBandinda(ring, merkez, a + AltDuzKaymasi(u, xYon), HedefYonu(nrm, xYon))) return false;
            List<double> ts = ring.Crossings(a, u);
            double icte = 0;
            for (int i = 0; i + 1 < ts.Count; i += 2)
                icte += Math.Max(0, Math.Min(ts[i + 1], len) - Math.Max(ts[i], 0));
            return icte >= 0.3 * (uMax - uMin);
        }

        /// <summary>
        /// Döşemenin artı merkezinden <paramref name="hedefYon"/> doğrultusundaki yerel genişliği: merkezden geriye (lo ≤ 0) ve
        /// hedef (alt/sağ) kenara (hi &gt; 0) uzaklık. Eğik/yamuk döşemede zarf yerine merkez kesiti (kenar eğikse çizgi döşeme dışına taşmaz).
        /// </summary>
        private static void YerelKesit(Ring ring, Point2d merkez, Vector2d hedefYon, out double lo, out double hi)
        {
            List<double> ts = ring.Crossings(merkez, hedefYon);
            lo = ts.Where(t => t <= 0).DefaultIfEmpty(double.NaN).Max();
            hi = ts.Where(t => t > 0).DefaultIfEmpty(double.NaN).Min();
            if (!double.IsNaN(lo) && !double.IsNaN(hi) && hi - lo >= 1.0) return;
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (Point2d q in ring.Pts)
            {
                double d = (q - merkez).DotProduct(hedefYon);
                if (d < mn) mn = d;
                if (d > mx) mx = d;
            }
            lo = mn;
            hi = Math.Max(mx, mn + 1.0);
        }

        /// <summary>
        /// <paramref name="q0"/> noktasından geçen çizgi döşemenin hiza bandında mı: merkez kesitinde <paramref name="hedefYon"/> (alt/sağ)
        /// kenarından uzaklık / kesit genişliği ∈ [<see cref="HizaMinOrani"/>, <see cref="HizaMaxOrani"/>].
        /// </summary>
        private static bool HizaBandinda(Ring ring, Point2d merkez, Point2d q0, Vector2d hedefYon)
        {
            YerelKesit(ring, merkez, hedefYon, out double lo, out double hi);
            double f = (hi - (q0 - merkez).DotProduct(hedefYon)) / (hi - lo);
            return f >= HizaMinOrani - 1e-6 && f <= HizaMaxOrani + 1e-6;
        }

        /// <summary>
        /// Sıra hizası: çizgi döşemenin (<paramref name="kr"/>, merkez <paramref name="p"/>) hiza bandında kaydırılır; aynı doğrultulu,
        /// henüz çizilmemiş komşulardan en çoğunu kendi hiza bandından geçiren ofset seçilir (eşitlikte 1/6 sapma toplamı, sonra kendi 1/6'sına yakınlık).
        /// </summary>
        private static double EnIyiSiraOfseti(Ring kr, Point2d p, Vector2d u, Vector2d nrm, Vector2d hedefYon, Vector2d duzK, bool xYon, List<Ring> katRings,
            Func<Point2d, List<Ring>> engelFn, List<(Ring Ring, Point2d Merkez)> adaylar, out int sirada)
        {
            sirada = 0;
            YerelKesit(kr, p, hedefYon, out double kLo, out double kHi);
            double kW = kHi - kLo;
            // Ofsetler şerit çizgisi içindir; hiza (1/6 hedef, 0,15–1/2 bandı) alt düz (90° üstü eğik Y: ilave) çubukta (çizgi + duzK) ölçülür.
            double dk = duzK.DotProduct(nrm);
            double kHedef = HedefOfset(kr, p, p, nrm, hedefYon) - dk;
            double s = hedefYon.DotProduct(nrm);
            double t1 = kHedef + s * (HedefKenarOrani - HizaMinOrani) * kW;
            double t2 = kHedef + s * (HedefKenarOrani - HizaMaxOrani) * kW;
            double tLo = Math.Min(t1, t2), tHi = Math.Max(t1, t2);
            var ts = new List<double> { kHedef, 0.0 };
            for (double t = tLo; t <= tHi + 1e-6; t += SiraOfsetAdimCm) ts.Add(t);
            foreach (var a in adaylar)
            {
                double h = HedefOfset(a.Ring, a.Merkez, p, nrm, hedefYon) - dk;
                if (h >= tLo && h <= tHi) ts.Add(h);
            }
            double bestT = kHedef, bestPen = double.MaxValue;
            int bestN = -1;
            foreach (double t in ts)
            {
                Point2d q0 = p + nrm * t;
                if (!kr.Contains(q0.X, q0.Y) || !HizaBandinda(kr, p, q0 + duzK, hedefYon)) continue;
                if (!TrySerit(q0, u, katRings, engelFn(q0), out double lo, out double hi)) continue;
                var a3 = new Point3d(q0.X + u.X * lo, q0.Y + u.Y * lo, 0);
                var b3 = new Point3d(q0.X + u.X * hi, q0.Y + u.Y * hi, 0);
                int n = 0;
                // Ceza: her döşemede çizginin alt/sağ 1/6 hizasına uzaklığı (merkez kesit genişliğine oranla).
                double pen = Math.Abs(t - kHedef) / kW;
                foreach (var a in adaylar)
                {
                    if (!AciklikMerkezdenGecer(a3, b3, a.Ring, a.Merkez, xYon)) continue;
                    YerelKesit(a.Ring, a.Merkez, hedefYon, out double aLo, out double aHi);
                    n++;
                    pen += Math.Abs(HedefOfset(a.Ring, a.Merkez, q0 + duzK, nrm, hedefYon)) / (aHi - aLo);
                }
                bool better = n > bestN
                    || (n == bestN && pen < bestPen - 1e-9)
                    || (n == bestN && Math.Abs(pen - bestPen) <= 1e-9 && Math.Abs(t - kHedef) < Math.Abs(bestT - kHedef));
                if (!better) continue;
                bestN = n;
                bestPen = pen;
                bestT = t;
            }
            sirada = Math.Max(bestN, 0);
            return bestT;
        }

        /// <summary>Tercih edilen donatı hizası: döşemenin <paramref name="hedefYon"/> tarafındaki kenarından kesit genişliğinin 1/6'sı içeride.</summary>
        private const double HedefKenarOrani = 1.0 / 6.0;

        /// <summary>
        /// <paramref name="p"/> noktasından geçen çizgiye göre (dik yön <paramref name="nrm"/> ofseti) döşemenin alt/sağ 1/6 hizası
        /// (merkez kesitinde). <paramref name="hedefYon"/>: X donatısında aşağı, Y donatısında sağ (±nrm).
        /// </summary>
        private static double HedefOfset(Ring ring, Point2d merkez, Point2d p, Vector2d nrm, Vector2d hedefYon)
        {
            YerelKesit(ring, merkez, hedefYon, out double lo, out double hi);
            double hedef = hi - HedefKenarOrani * (hi - lo);
            Point2d h = merkez + hedefYon * hedef;
            return (h - p).DotProduct(nrm);
        }


        /// <summary>1/6 hizası ölçülen çubuğun şerit çizgisinden kayması (cm): alt düz; 90° üstü eğik Y donatısında mesnet ilavesi.</summary>
        private static Vector2d AltDuzKaymasi(Vector2d u, bool xYon)
        {
            Vector3d k = DosemeDonatiRunner.AutoHizaKaymasi(new Vector3d(u.X, u.Y, 0),
                xYon ? DosemeDonatiRunner.DonatiYonu.X : DosemeDonatiRunner.DonatiYonu.Y);
            return new Vector2d(k.X, k.Y);
        }

        /// <summary>Çekirdeğin alt düz çubuğunun şerit çizgisinden kayması (cm); ilave hizası açıklık vermezse son deneme.</summary>
        private static Vector2d AltDuzKaymasiDuz(Vector2d u, bool xYon)
        {
            Vector3d k = DosemeDonatiRunner.AutoAltDuzKaymasi(new Vector3d(u.X, u.Y, 0),
                xYon ? DosemeDonatiRunner.DonatiYonu.X : DosemeDonatiRunner.DonatiYonu.Y);
            return new Vector2d(k.X, k.Y);
        }

        /// <summary>X donatısında döşeme altı (−Y tarafı), Y donatısında sağı (+X tarafı) gösteren birim dik.</summary>
        private static Vector2d HedefYonu(Vector2d nrm, bool xYon)
        {
            if (xYon) return nrm.Y > 0 ? -nrm : nrm;
            return nrm.X < 0 ? -nrm : nrm;
        }

        /// <summary>İki doğrultu (yönsüz çizgi, mod π) aynı mı.</summary>
        private static bool AyniDogrultu(double a, double b)
        {
            double d = Math.Abs(a - b) % Math.PI;
            return Math.Min(d, Math.PI - d) <= AyniDogrultuTolRad;
        }

        /// <summary>DMX çubuk listesi (tek yön) → çekirdek çeliği; aynı türden ilk kayıt.</summary>
        private static DosemeDonatiRunner.AutoSlabSteel ToSteel(List<DmxDosemeDonati.Bar> bars, double? ustKotM)
        {
            var s = new DosemeDonatiRunner.AutoSlabSteel { UstKotM = ustKotM };
            if (bars == null) return s;
            foreach (DmxDosemeDonati.Bar b in bars)
            {
                switch (b.Kind)
                {
                    case DosemeDonatiKind.Duz: if (s.DuzCap == 0) { s.DuzCap = b.Dia; s.DuzAr = b.Spacing; } break;
                    case DosemeDonatiKind.Pilye: if (s.PilCap == 0) { s.PilCap = b.Dia; s.PilAr = b.Spacing; } break;
                    case DosemeDonatiKind.SolIlave: if (s.SolCap == 0) { s.SolCap = b.Dia; s.SolAr = b.Spacing; } break;
                    case DosemeDonatiKind.SagIlave: if (s.SagCap == 0) { s.SagCap = b.Dia; s.SagAr = b.Spacing; } break;
                    case DosemeDonatiKind.Montaj: if (s.MonCap == 0) { s.MonCap = b.Dia; s.MonAr = b.Spacing; } break;
                }
            }
            return s;
        }

        private static Ring ToRing(Point2d[] pts)
        {
            if (pts == null || pts.Length < 3) return null;
            return new Ring
            {
                Pts = pts,
                MinX = pts.Min(q => q.X),
                MinY = pts.Min(q => q.Y),
                MaxX = pts.Max(q => q.X),
                MaxY = pts.Max(q => q.Y)
            };
        }

        /// <summary>Yardımcı CollectSlabLabelTexts ile aynı ad kuralı: DOSEME ISMI metninin başındaki D..-nn.</summary>
        private static readonly System.Text.RegularExpressions.Regex RxSlabName =
            new System.Text.RegularExpressions.Regex(@"^\s*(D[A-Za-z]?-?\d+(?:-\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static List<(string Name, Point2d Pos)> ReadSlabLabels(Database db, long minHandle)
        {
            var list = new List<(string, Point2d)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.Handle.Value < minHandle) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is DBText t) || t.IsErased) continue;
                    if (!string.Equals(t.Layer, LayerDosemeIsmi, StringComparison.OrdinalIgnoreCase)) continue;
                    var m = RxSlabName.Match(t.TextString ?? string.Empty);
                    if (!m.Success) continue;
                    Point2d pos;
                    try
                    {
                        Extents3d ex = t.GeometricExtents;
                        pos = new Point2d(0.5 * (ex.MinPoint.X + ex.MaxPoint.X), 0.5 * (ex.MinPoint.Y + ex.MaxPoint.Y));
                    }
                    catch { pos = new Point2d(t.Position.X, t.Position.Y); }
                    list.Add((m.Groups[1].Value, pos));
                }
                tr.Commit();
            }
            return list;
        }

        /// <summary>Artı merkezinden geçen çizginin kat sınırı içindeki aralığı; çizilmiş açıklıklara girmeden.</summary>
        private static bool TrySerit(Point2d p, Vector2d u, List<Ring> katRings, List<Ring> cizilmis, out double lo, out double hi)
        {
            lo = double.NegativeInfinity;
            hi = double.PositiveInfinity;
            bool found = false;
            foreach (Ring r in katRings)
            {
                if (!r.Contains(p.X, p.Y)) continue;
                List<double> ts = r.Crossings(p, u);
                double l = ts.Where(t => t <= 0).DefaultIfEmpty(double.NaN).Max();
                double h = ts.Where(t => t > 0).DefaultIfEmpty(double.NaN).Min();
                if (double.IsNaN(l) || double.IsNaN(h)) continue;
                lo = l - KatSiniriTasmaCm;
                hi = h + KatSiniriTasmaCm;
                found = true;
                break;
            }
            if (!found) return false;
            foreach (Ring r in cizilmis)
            {
                List<double> ts = r.Crossings(p, u);
                if (ts.Count == 0) continue;
                double first = ts[0], last = ts[ts.Count - 1];
                if (last <= 0) lo = Math.Max(lo, last + CizilmisYuzPayCm);
                else if (first >= 0) hi = Math.Min(hi, first - CizilmisYuzPayCm);
                else
                {
                    // Merkez çizilmiş açıklığın dışında ama çizgi içinden geçiyor (girintili poligon): merkeze en yakın kenarlarda dur.
                    double neg = ts.Where(t => t <= 0).Max();
                    double pos = ts.Where(t => t > 0).Min();
                    lo = Math.Max(lo, neg + CizilmisYuzPayCm);
                    hi = Math.Min(hi, pos - CizilmisYuzPayCm);
                }
            }
            return hi - lo >= MinCizgiCm && lo < 0 && hi > 0;
        }

        /// <summary>Bu komutla oluşturulan (handle ≥ <paramref name="minHandle"/>) kapalı polyline halkaları.</summary>
        private static List<Ring> ReadRings(Database db, string layer, long minHandle)
        {
            var list = new List<Ring>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.Handle.Value < minHandle) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || pl.IsErased) continue;
                    if (!string.Equals(pl.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                    if (pl.NumberOfVertices < 3) continue;
                    if (!pl.Closed && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) > 0.5) continue;
                    var pts = new Point2d[pl.NumberOfVertices];
                    for (int i = 0; i < pts.Length; i++) pts[i] = pl.GetPoint2dAt(i);
                    list.Add(new Ring
                    {
                        Pts = pts,
                        MinX = pts.Min(q => q.X),
                        MinY = pts.Min(q => q.Y),
                        MaxX = pts.Max(q => q.X),
                        MaxY = pts.Max(q => q.Y)
                    });
                }
                tr.Commit();
            }
            return list;
        }

        /// <summary>
        /// Bu komutun GPR yazı ve kılavuz çizgilerini siler (kullanıcı kararı: donatı çizildikten sonra GPR kalmaz).
        /// Yardımcı'nın GPR'de eşleşmeyen ilaveyi 10000 cm aşağı park etmesi geri alınır: kırmızı olarak yerinde kalır.
        /// </summary>
        private static void TemizleGprVeParki(Database db, long minHandle, List<Ring> katRings, out int silinen, out int geriAlinan)
        {
            silinen = 0;
            geriAlinan = 0;
            double katMinY = katRings.Count > 0 ? katRings.Min(r => r.MinY) : double.NaN;
            var up = Matrix3d.Displacement(new Vector3d(0, ParkCm, 0));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.Handle.Value < minHandle) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || ent.IsErased) continue;
                    string lyr = ent.Layer ?? string.Empty;
                    // GPR yazı/kılavuz ve Yardımcı kontrol poligonları (ALT DUZ ALAN) bu çizimde kalmaz.
                    if (lyr.IndexOf("GPR", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(lyr, LayerAltDuzAlan, StringComparison.OrdinalIgnoreCase))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                        silinen++;
                        continue;
                    }
                    if (double.IsNaN(katMinY)) continue;
                    if (!string.Equals(lyr, LayerDonati, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(lyr, LayerDonatiYazisi, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Extents3d ex;
                    try { ex = ent.GeometricExtents; }
                    catch { continue; }
                    if (ex.MaxPoint.Y >= katMinY - 0.5 * ParkCm) continue;
                    ent.UpgradeOpen();
                    ent.TransformBy(up);
                    geriAlinan++;
                }
                tr.Commit();
            }
        }
    }
}
