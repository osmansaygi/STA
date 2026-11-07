using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        // Yerel kesit koordinatları; çizim sonunda 2x büyütüldüğünden 10 birim = nihai 20 cm.
        private static List<(double X0, double Y0, double X1, double Y1, double Lane)> KirisKesitYanOlculeri(
            double w, double h, (double Ust, double Alt)? sol, (double Ust, double Alt)? sag)
        {
            var result = new List<(double, double, double, double, double)>();
            double Kontur(double face, double tip, (double Ust, double Alt)? slab, double yy)
                => slab.HasValue && yy >= slab.Value.Alt - 1e-7 && yy <= slab.Value.Ust + 1e-7 ? tip : face;
            void Zincir(bool left, (double Ust, double Alt)? slab)
            {
                if (!slab.HasValue) return;
                double face = left ? 0 : w, tip = face + (left ? -15 : 15);
                double lane = tip + (left ? -10 : 10);
                var levels = new[] { -h, Math.Max(-h, Math.Min(0, slab.Value.Alt)),
                    Math.Max(-h, Math.Min(0, slab.Value.Ust)), 0.0 }.Distinct().OrderBy(v => v).ToList();
                for (int i = 1; i < levels.Count; i++)
                {
                    double a = levels[i - 1], b = levels[i];
                    if (b - a < 1e-7) continue;
                    result.Add((Kontur(face, tip, slab, a), a, Kontur(face, tip, slab, b), b, lane));
                }
            }
            Zincir(true, sol); Zincir(false, sag);
            result.Add((Kontur(w, w + 15, sag, -h), -h, Kontur(w, w + 15, sag, 0), 0,
                sag.HasValue ? w + 35 : w + 10));
            return result;
        }

        private sealed class KirisKesitDonati
        {
            public List<Point2d> Yol;
            public int Adet, Cap;
            public string Tur;
        }
        private sealed class KirisKesitSira
        {
            public int Adet, Cap;
            public string Tur;
            public double Derinlik;
        }
        private sealed class KirisKesitCubuk
        {
            public KirisKesitSira Donati;
            public Point2d Merkez;
            public int Sira;
        }

        private static List<double> KirisKesitSiraXleri(double b, int count, double diameter, List<double> fixedXs = null, double cover = 5)
        {
            var anchors = fixedXs == null
                ? KirisKesitEtriyeKutulari(b, cover - 1).SelectMany(v => new[] { v.Lo + 1, v.Hi - 1 }).OrderBy(v => v).ToList()
                : fixedXs.Concat(new[] { cover, b - cover }).Distinct().OrderBy(v => v).ToList();
            if (count <= 0 || anchors.Count == 0) return new List<double>();
            double pitch = 4.0 + diameter;
            if (count < anchors.Count)
            {
                int n = Math.Min(count, Math.Max(1, (int)Math.Floor((b - 2 * cover) / pitch + 1e-8) + 1));
                return Enumerable.Range(0, n).Select(i => n == 1 ? cover : cover + (b - 2 * cover) * i / (n - 1)).ToList();
            }
            var extra = new int[anchors.Count - 1];
            int placed = anchors.Count;
            bool Sigar(int i) => (anchors[i + 1] - anchors[i]) / (extra[i] + 2) >= pitch - 1e-8;
            while (placed < count)
            {
                int best = -1; double bestStep = 0;
                for (int i = 0; i < extra.Length; i++)
                {
                    double step = (anchors[i + 1] - anchors[i]) / (extra[i] + 2);
                    if (step >= pitch - 1e-8 && step > bestStep) { best = i; bestStep = step; }
                }
                if (best < 0) break;
                // Simetri: ara çubuk ayna açıklıkla çift eklenir; tek kalan çubuk ortadaki açıklığa.
                int ayna = extra.Length - 1 - best, orta = extra.Length / 2;
                if (ayna != best && count - placed >= 2 && Sigar(ayna)) { extra[best]++; extra[ayna]++; placed += 2; continue; }
                if (ayna != best && count - placed == 1 && extra.Length % 2 == 1 && Sigar(orta)) { extra[orta]++; placed++; continue; }
                extra[best]++; placed++;
            }
            var xs = new List<double> { anchors[0] };
            for (int i = 0; i < extra.Length; i++)
            {
                for (int j = 1; j <= extra[i]; j++) xs.Add(anchors[i] + (anchors[i + 1] - anchors[i]) * j / (extra[i] + 1));
                xs.Add(anchors[i + 1]);
            }
            return xs;
        }

        private static List<KirisKesitCubuk> KirisKesitCubukYerlesimi(double b, double h, List<KirisKesitSira> bars, bool monUst = true, double cover = 5)
        {
            var result = new List<KirisKesitCubuk>();
            foreach (bool top in new[] { true, false })
            {
                // Kiriş: mon üst / düz alt. Sürekli temel: mon alt / düz üst.
                var pending = bars.Where(r =>
                    {
                        if (r.Tur == "gov") return false;
                        bool faceTop = r.Tur == "mon" ? monUst
                            : r.Tur == "duz" ? !monUst
                            : r.Derinlik < h * 0.5;
                        return faceTop == top;
                    })
                    .OrderBy(r => r.Tur == "mon" || r.Tur == "duz" ? 0 : 1)
                    .SelectMany(r => Enumerable.Repeat(r, Math.Max(0, r.Adet))).ToList();
                double depth = cover, previousDia = 0;
                int row = 0;
                while (pending.Count > 0)
                {
                    double maxDia = pending.Max(r => r.Cap) / 10.0;
                    int mainCount = pending.Count(r => r.Tur == "mon" || r.Tur == "duz");
                    var mainXs = mainCount > 0 ? KirisKesitSiraXleri(b, mainCount, maxDia, cover: cover) : new List<double>();
                    var xs = mainCount > 0 && mainXs.Count == mainCount && pending.Count > mainCount
                        ? KirisKesitSiraXleri(b, pending.Count, maxDia, mainXs, cover)
                        : KirisKesitSiraXleri(b, pending.Count, maxDia, cover: cover);
                    if (xs.Count == 0) break;
                    // Tek taşan çubuk yalnız sol köşeye düşmesin: 1. sıradan bir eksilt, 2. sırada iki köşe.
                    if (row == 0 && pending.Count > xs.Count && pending.Count - xs.Count == 1 && xs.Count >= 3)
                    {
                        int want = xs.Count - 1;
                        xs = mainCount > 0 && mainXs.Count == mainCount && want > mainCount
                            ? KirisKesitSiraXleri(b, want, maxDia, mainXs, cover)
                            : KirisKesitSiraXleri(b, want, maxDia, cover: cover);
                        if (xs.Count == 0) break;
                    }
                    var corners = KirisKesitEtriyeKutulari(b, cover - 1).SelectMany(v => new[] { v.Lo + 1, v.Hi - 1 }).ToList();
                    // Önce montaj/düz çubuklar tüm etriye köşelerini doldurur.
                    var slots = xs.OrderBy(xx => mainXs.Any(c => Math.Abs(c - xx) < 1e-7) ? 0 : 1)
                        .ThenBy(xx => corners.Any(c => Math.Abs(c - xx) < 1e-7) ? 0 : 1).ThenBy(xx => xx).ToList();
                    var selected = pending.Take(slots.Count).ToList();
                    double rowDia = selected.Max(r => r.Cap) / 10.0;
                    if (row > 0) depth += previousDia * 0.5 + 3.0 + rowDia * 0.5;
                    for (int i = 0; i < selected.Count; i++)
                        result.Add(new KirisKesitCubuk { Donati = selected[i], Sira = row,
                            Merkez = new Point2d(slots[i], top ? -depth : -h + depth) });
                    pending.RemoveRange(0, selected.Count);
                    previousDia = rowDia; row++;
                }
            }
            foreach (var web in bars.Where(r => r.Tur == "gov"))
                foreach (var p in KirisKesitCubukMerkezleri(b, h, web.Adet, web.Tur, web.Derinlik, cover))
                    result.Add(new KirisKesitCubuk { Donati = web, Merkez = p, Sira = 0 });
            return result;
        }
        private sealed class KirisKesitBilgi
        {
            public string Ad;
            public double B, H, Kot, X, Y;
            public int Etriye;
            /// <summary>Sürekli temel ampatman U donatısı çap (mm) / aralık (cm); yoksa 0.</summary>
            public int AmpCap, AmpAralik;
            /// <summary>Kesitte çizilen ampatman dağıtma çubuğu adedi (açılım için; kesit çizildikten sonra dolar).</summary>
            public int DagitmaAdet;
            /// <summary>Bu kesitin açıklığına düşen ampatman U adedi; AmpAdetGrup aynı harfli kesitlerin toplamı.</summary>
            public int AmpAdet, AmpAdetGrup;
            public string AdetNotu;
            public (double Ust, double Alt)? Sol, Sag;
            public Geometry TemelKesit;
            public List<KirisKesitSira> Siralar = new List<KirisKesitSira>();
        }
        private readonly List<KirisKesitDonati> _kirisKesitDonatilari = new List<KirisKesitDonati>();
        private readonly List<KirisKesitBilgi> _kirisKesitler = new List<KirisKesitBilgi>();
        /// <summary>Çizilen kiriş zincirinin kiriş üstü kolon aralıkları (zincir X); kesit bu hizadan geçmez.</summary>
        private readonly List<(double Lo, double Hi)> _kirisUstuKolonAraliklari = new List<(double Lo, double Hi)>();
        private readonly Dictionary<BeamInfo, string> _kirisKesitAdetNotlari = new Dictionary<BeamInfo, string>();
        private readonly List<(SlabInfo Slab, Geometry Poly)> _kirisKesitDosemeler = new List<(SlabInfo, Geometry)>();

        private void KirisAcilimKesitBaslat(FloorInfo floor)
        {
            _ntsDrawFactory = _ntsDrawFactory ?? NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            _kirisKesitler.Clear();
            _kirisKesitDosemeler.Clear();
            if (_model.Slabs == null) return;
            foreach (var slab in _model.Slabs)
            {
                if (GetSlabFloorNo(slab.SlabId) != floor.FloorNo) continue;
                var poly = NtsOverlaySafe.PrepareForOverlay(SlabFootprintPoly(slab));
                if (poly != null && !poly.IsEmpty) _kirisKesitDosemeler.Add((slab, poly));
            }
        }

        private void KirisAcilimKesitDonatiKaydet(List<Point2d> pts, int adet, int cap, string tur)
        {
            if (pts == null || pts.Count < 2 || adet <= 0 || cap <= 0) return;
            _kirisKesitDonatilari.Add(new KirisKesitDonati { Yol = new List<Point2d>(pts), Adet = adet, Cap = cap, Tur = tur });
        }

        private void KirisAcilimKesitTakimBaslat()
        {
            _kirisKesitler.Clear();
            _kirisKesitDonatilari.Clear();
        }

        /// <summary>Temel kesitleri arası (2× öncesi): komşu dış ölçü çizgileri (ampatmansız solda gövde etiketi) arası 30 → nihai 60 cm.</summary>
        private const double TemelKesitAraCm = 30.0;
        /// <summary>"A - A KESİTİ" başlığı yarı genişliği (2× öncesi, 5 yükseklik): dar kesitte payı belirler.</summary>
        private const double TemelKesitBaslikYariCm = 21.0;

        private static double KirisKesitSonrakiX(double x, double b)
        {
            // Gerçek sağ ölçü ve sol açıklama payları; arada nihai yaklaşık 50 cm.
            return x + b + 38.0 + 19.0 + 50.0 / 2.0;
        }

        private static double KirisKesitKartYuksekligi(double h, double slabRise, int names)
        {
            return 2.0 * h + 230.0 + Math.Max(0, slabRise) + names * 8.0;
        }

        // Çizimden önce üst sınır ayır: benzerlik birleşince gereken alan yalnız küçülür.
        // Yeni DBText/Dimension/Hatch nesnelerini extents okuyarak yeniden taşımaya gerek kalmaz.
        private static double KirisKesitYerlesimAlti(double minKot, double h, double slabRise, int count)
        {
            return minKot - KirisKesitKartYuksekligi(h, slabRise, count);
        }

        private static double KirisKesitIstasyon(double yuz0, double yuz1, bool konsol = false) => yuz0 + (yuz1 - yuz0) * (konsol ? 0.5 : 2.0 / 3.0);

        /// <summary>Montaj bindirme bölgesine denk gelirse kesiti sola kaydır (10–15 cm adım).</summary>
        private static double KirisKesitIstasyonBindirmedenKac(
            double yuz0, double yuz1, double station, Func<double, int> montajKesAdedi)
        {
            if (montajKesAdedi == null || yuz1 - yuz0 < 40.0) return station;
            const double adim = 12.5;
            double minS = yuz0 + (yuz1 - yuz0) * 0.15;
            double s = station;
            int guard = 0;
            while (s > minS + 0.5 && montajKesAdedi(s) >= 2 && guard++ < 20)
                s -= adim;
            if (s < minS) s = minS;
            return s;
        }

        /// <summary>
        /// Kesit istasyonu: ilave donatı ve bindirme aralıklarının dışında, tercihe (Ln·2/3, konsolda 1/2) en yakın yer.
        /// Kaçılamazsa ihlal ağırlığı (ilave 2, bindirme 1), sonra kesişen aralık sayısı en az olan yer seçilir.
        /// Aralıklar 5 cm paydan da uzak tutulmaya çalışılır. Arama net açıklığın uçtan %10'u (en az 5 cm) içinde.
        /// </summary>
        private static double KirisKesitIstasyonSec(double yuz0, double yuz1, double tercih,
            IList<(double Lo, double Hi)> ilave, IList<(double Lo, double Hi)> bindirme, IList<(double Lo, double Hi)> kolon = null)
        {
            double L = yuz1 - yuz0;
            if (L < 20.0 || ((ilave == null || ilave.Count == 0) && (bindirme == null || bindirme.Count == 0)
                && (kolon == null || kolon.Count == 0))) return tercih;
            const double pay = 5.0;
            double kenar = Math.Max(5.0, 0.1 * L);
            double lo = yuz0 + kenar, hi = yuz1 - kenar;
            int Say(IList<(double Lo, double Hi)> z, double s, double p)
            {
                int n = 0;
                if (z != null) foreach (var r in z) if (s > r.Lo - p && s < r.Hi + p) n++;
                return n;
            }
            // Kiriş üstü kolon hizası (ağırlık 4) ilaveden (2) ve bindirmeden (1) önce gelir.
            (int, int, int, double) Maliyet(double s)
            {
                int i = Say(ilave, s, 0), b = Say(bindirme, s, 0), k = Say(kolon, s, 0);
                int ip = Say(ilave, s, pay), bp = Say(bindirme, s, pay), kp = Say(kolon, s, pay);
                return ((k > 0 ? 4 : 0) + (i > 0 ? 2 : 0) + (b > 0 ? 1 : 0), k + i + b,
                    (kp > 0 ? 4 : 0) + (ip > 0 ? 2 : 0) + (bp > 0 ? 1 : 0), Math.Abs(s - tercih));
            }
            double enIyi = tercih;
            var enIyiM = Maliyet(tercih);
            for (double s = lo; s <= hi + 1e-6; s += 1.0)
            {
                var m = Maliyet(s);
                if (m.CompareTo(enIyiM) < 0) { enIyi = s; enIyiM = m; }
            }
            return enIyi;
        }

        /// <summary>
        /// Görünüşe kaydedilen çubuklardan yerel (x0'a göre) ilave ve bindirme X aralıkları. Bindirme: aynı türde
        /// (montaj/düz) iki ayrı çubuğun aynı hizada (≤4 cm) yatay örtüşmesi ve montaj bindirme ölçüsü kayıtları.
        /// </summary>
        private void KirisKesitEngelAraliklari(double x0, out List<(double Lo, double Hi)> ilave, out List<(double Lo, double Hi)> bindirme)
        {
            ilave = new List<(double Lo, double Hi)>();
            bindirme = new List<(double Lo, double Hi)>();
            IEnumerable<(double Lo, double Hi, double YLo, double YHi)> Yataylar(KirisKesitDonati d)
            {
                for (int i = 1; i < d.Yol.Count; i++)
                {
                    var p = d.Yol[i - 1]; var q = d.Yol[i];
                    if (Math.Abs(q.X - p.X) < 0.001) continue;
                    yield return p.X < q.X ? (p.X, q.X, p.Y, q.Y) : (q.X, p.X, q.Y, p.Y);
                }
            }
            foreach (var d in _kirisKesitDonatilari)
                if (d?.Tur != null && d.Tur.StartsWith("ila", StringComparison.Ordinal))
                    foreach (var s in Yataylar(d)) ilave.Add((s.Lo - x0, s.Hi - x0));
            var ekli = _kirisKesitDonatilari.Where(d => d != null && (d.Tur == "mon" || d.Tur == "duz")).ToList();
            for (int i = 0; i < ekli.Count; i++)
                for (int j = i + 1; j < ekli.Count; j++)
                {
                    if (ekli[i].Tur != ekli[j].Tur) continue;
                    foreach (var s in Yataylar(ekli[i]))
                        foreach (var t in Yataylar(ekli[j]))
                        {
                            double lo = Math.Max(s.Lo, t.Lo), hi = Math.Min(s.Hi, t.Hi);
                            if (hi - lo < 2.5) continue;
                            double xm = 0.5 * (lo + hi);
                            double ys = s.YLo + (s.YHi - s.YLo) * (xm - s.Lo) / (s.Hi - s.Lo);
                            double yt = t.YLo + (t.YHi - t.YLo) * (xm - t.Lo) / (t.Hi - t.Lo);
                            if (Math.Abs(ys - yt) <= 4.0) bindirme.Add((lo - x0, hi - x0));
                        }
                }
            foreach (var e in _kirisAcilimMontajBindirmeXs) bindirme.Add((e.X0 - x0, e.X1 - x0));
        }

        /// <summary>
        /// Tanı: sürekli temel zincirinin her iç mesnedinde aynı türden (montaj/düz) çubukların bindirmesi.
        /// Mesnedi tek çubuk geçiyorsa "gecer"; aksi halde soldan gelen en sağ uç ile sağdan gelen en sol uç farkı.
        /// </summary>
        private void KirisKesitMesnetBindirmeTani(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, double x0)
        {
            for (int k = 1; k < chain.Count; k++)
            {
                KirisAcilimKolonYuzu(chain, k - 1, k - 1, sup, out _, out double fL);
                KirisAcilimKolonYuzu(chain, k, k, sup, out double fR, out _);
                foreach (string tur in new[] { "mon", "duz" })
                {
                    var bars = _kirisKesitDonatilari.Where(d => d?.Tur == tur && d.Yol.Count > 1)
                        .Select(d => (Lo: d.Yol.Min(p => p.X) - x0, Hi: d.Yol.Max(p => p.X) - x0, d.Adet, d.Cap)).ToList();
                    string Ad((double Lo, double Hi, int Adet, int Cap) b) => b.Adet + "ø" + b.Cap;
                    var gecen = bars.Where(b => b.Lo < fL - 1 && b.Hi > fR + 1).ToList();
                    var sol = bars.Where(b => b.Lo < fL - 1 && b.Hi >= fL - 1 && b.Hi <= fR + 1 + 400).Except(gecen).ToList();
                    var sag = bars.Where(b => b.Hi > fR + 1 && b.Lo <= fR + 1 && b.Lo >= fL - 1 - 400).Except(gecen).ToList();
                    string durum;
                    if (gecen.Count > 0 && sol.Count == 0 && sag.Count == 0) durum = "gecer " + Ad(gecen[0]);
                    else
                    {
                        // SurekliTemelDuzMesnetBindirme ile aynı eşleme: soldan en uzaktan gelen ↔ sağ yüzden önce başlayıp daha sağa giden.
                        var l = bars.Where(b => b.Lo < fL - 1 && b.Hi > fL - 1).OrderBy(b => b.Lo).FirstOrDefault();
                        var r = l.Adet == 0 ? default : bars.Where(b => b.Lo > l.Lo && b.Lo < fR + 1 && b.Hi > Math.Max(l.Hi, fR + 1))
                            .OrderBy(b => b.Lo).FirstOrDefault();
                        if (l.Adet == 0 || r.Adet == 0) durum = "eksik";
                        else
                        {
                            double lb = KirisAcilimYuvarlaYukari(_lbPhiKonumI * Math.Max(l.Cap, r.Cap) / 10.0);
                            durum = string.Format(CultureInfo.InvariantCulture, "bindirme={0:0.#} lb={1:0.#} sol={2} {3:0.#}~{4:0.#} sag={5} {6:0.#}~{7:0.#}",
                                l.Hi - r.Lo, lb, Ad(l), l.Lo, l.Hi, Ad(r), r.Lo, r.Hi);
                        }
                    }
                    KirisAcilimTani(string.Format(CultureInfo.InvariantCulture, "MESNET_BINDIRME kiris={0} k={1} yuz={2:0.#}~{3:0.#} {4} {5}",
                        KirisAcilimEtiket(chain[k].Beam, 1), k, fL, fR, tur, durum));
                }
            }
        }

        private static bool KirisKesitAraliktaMi(IList<(double Lo, double Hi)> z, double s)
        {
            foreach (var r in z) if (s > r.Lo && s < r.Hi) return true;
            return false;
        }

        private int KirisKesitMontajKesAdedi(double xWorld)
        {
            int n = 0;
            foreach (var don in _kirisKesitDonatilari)
            {
                if (don == null || don.Tur != "mon") continue;
                if (KirisKesitYolKes(don.Yol, xWorld, out _)) n++;
            }
            return n;
        }

        private static string KirisKesitEtriyeCarpan(int count)
        {
            return count > 1 ? count.ToString(CultureInfo.InvariantCulture) + "x" : "";
        }

        private void KirisAcilimKotlariCiz(Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus, double x0, double yShift,
            bool herAciklik = false, IList<double> engelX = null)
        {
            // Kot kapladığı yatay aralık (2× öncesi; ölçek 0,5): uzatma solda 10, yazı sağda ~24.
            const double kotSol = 10.0, kotSag = 24.0, olcuBosluk = 7.5;
            double Bosluk(double s)
            {
                double min = double.MaxValue;
                foreach (double e in engelX)
                {
                    double ex = e - x0;
                    double d = ex < s - kotSol ? s - kotSol - ex : ex > s + kotSag ? ex - s - kotSag : -1.0;
                    min = Math.Min(min, d);
                }
                return min;
            }
            // Sürekli temelde her kolon arası ayrı temel kirişi: kot her açıklığın Ln·3/4'ünde.
            for (int i0 = 0; i0 < chain.Count;)
            {
                int i1 = i0;
                while (!herAciklik && i1 + 1 < chain.Count && chain[i1 + 1].Beam.BeamId == chain[i0].Beam.BeamId) i1++;
                KirisAcilimKolonYuzu(chain, i0, i1, sup, out double a, out double b);
                KirisAcilimNumaraAraligi(chain, i0, i1, a, b, gorunus, out a, out b);
                double station = a + (b - a) * 0.75;
                if (engelX != null && engelX.Count > 0 && Bosluk(station) < olcuBosluk)
                {
                    // Ölçü uzatma çizgilerinden en az 7,5 (nihai 15 cm) uzak, Ln·3/4'e en yakın temiz yer; açıklıkta
                    // yoksa (sık ölçülü kısa açıklık) temel boyunca sağda/solda; o da yoksa ölçülere en uzak yer.
                    double enIyi = station, enIyiBosluk = Bosluk(station), enIyiUzak = double.MaxValue;
                    void Ara(double s0, double s1)
                    {
                        for (double s = s0; s <= s1 + 1e-6; s += 1.0)
                        {
                            double bos = Bosluk(s), uzak = Math.Abs(s - station);
                            if (bos >= olcuBosluk ? (enIyiBosluk < olcuBosluk || uzak < enIyiUzak) : (enIyiBosluk < olcuBosluk && bos > enIyiBosluk))
                            { enIyi = s; enIyiBosluk = bos; enIyiUzak = bos >= olcuBosluk ? uzak : double.MaxValue; }
                        }
                    }
                    Ara(a + kotSol, b - kotSag);
                    if (enIyiBosluk < olcuBosluk) Ara(chain[0].X0 + kotSol, chain[chain.Count - 1].X1 - kotSag);
                    station = enIyi;
                }
                int j = i0;
                while (j < i1 && station > chain[j].X1) j++;
                // Komşu açıklığa kayan kot oradaki üst yüze oturur.
                if (station < chain[i0].X0 || station > chain[i1].X1)
                    for (j = 0; j + 1 < chain.Count && station > chain[j].X1; j++) { }
                double top = KirisAcilimKotAt(chain, j, j, station);
                const double scale = 0.5;
                DrawKesitKotClassicSymbol(tr, ms, x0 + station, yShift + top, 0, scale);
                AppendKesitKotElevationDbText(tr, ms, db, GetOrCreateYaziBeykentTextStyle(tr, db),
                    FormatKesitKotElevationString((_model.BuildingBaseKotu + floor.ElevationM) * 100.0 + top),
                    x0 + station + KesitKotTriHalfWidthCm * scale,
                    yShift + top + (KesitKotTriHeightCm + KesitKotTextAboveExtensionCm) * scale, 0, KesitKotTextHeightCm * scale);
                i0 = i1 + 1;
            }
        }

        private static List<(double Lo, double Hi)> KirisKesitEtriyeKutulari(double b, double cover = 4)
        {
            double width = b - 2 * cover;
            var boxes = new List<(double Lo, double Hi)>();
            if (width <= 2.0) return boxes;
            int count = 1;
            while (width / (2 * count - 1) > 35.0 + 1e-8) count++;
            double step = width / (2 * count - 1);
            for (int i = 0; i < count; i++) boxes.Add((cover + i * step, cover + (i + count) * step));
            return boxes;
        }

        /// <summary>Gövde çubukları arası düşey en az bu kadar (cm); altına düşerse bir üst çapa tahvil.</summary>
        private const double KirisGovdeMinAralikCm = 12.0;

        /// <summary>Bir yüzdeki gövde çubukları arası: montaj ile düz arasındaki yükseklik (yüz başına (n+1)/2 çubuk) eşit bölünür.</summary>
        private static double KirisGovdeAralik(double h, int adet, double paspayToplam)
            => (h - paspayToplam) / ((adet + 1) / 2 + 1);

        /// <summary>
        /// Gövde aralığı 12 cm altındaysa projedeki bir üst çapa geçer, adet alanı karşılayacak (iki yüze simetrik, çift) şekilde
        /// yeniden hesaplanır; aralık yeterli olana veya en büyük çapa ulaşılana kadar tekrarlanır. Tekrar çağrı sonucu değiştirmez.
        /// </summary>
        private static (int Adet, int Cap) KirisGovdeTahvil(int adet, int cap, double h, double paspayToplam, IList<int> caplar)
        {
            if (adet <= 0 || cap <= 0 || h - paspayToplam <= 0 || caplar == null) return (adet, cap);
            double alan = adet * (double)cap * cap;
            while (KirisGovdeAralik(h, adet, paspayToplam) < KirisGovdeMinAralikCm - 1e-6)
            {
                int ust = caplar.Where(c => c > cap).DefaultIfEmpty(0).Min();
                if (ust <= 0) break;
                int yeni = (int)Math.Ceiling(alan / ((double)ust * ust) - 1e-9);
                if (yeni % 2 == 1) yeni++;
                if (yeni < 2) yeni = 2;
                adet = yeni; cap = ust;
            }
            return (adet, cap);
        }

        private void KirisKesitMontajHazirla(Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            _kirisKesitAdetNotlari.Clear();
            // Projede kullanılan boyuna donatı çapları (gövde tahvili yalnız bunlara geçer).
            var caplar = new SortedSet<int>();
            foreach (var v in montaj.Values)
                foreach (var (n, c) in new[] { (v.Adet, v.Cap), (v.DuzAdet, v.DuzCap), (v.GovdeAdet, v.GovdeCap),
                    (v.SolUstAdet, v.SolUstCap), (v.SagUstAdet, v.SagUstCap), (v.SolAltAdet, v.SolAltCap), (v.SagAltAdet, v.SagAltCap) })
                    if (n > 0 && c >= 8) caplar.Add(c);
            var capList = caplar.ToList();
            foreach (var beam in montaj.Keys.ToList())
            {
                var m = montaj[beam];
                var gov = KirisGovdeTahvil(m.GovdeAdet, m.GovdeCap, beam.HeightCm,
                    KirisAcilimMontajPaspayiCm + KirisAcilimDuzPaspayiCm, capList);
                if (gov.Adet != m.GovdeAdet || gov.Cap != m.GovdeCap)
                {
                    KirisAcilimTani(string.Format(CultureInfo.InvariantCulture, "GOVDE_TAHVIL h={0:0.#} {1}ø{2}→{3}ø{4}",
                        beam.HeightCm, m.GovdeAdet, m.GovdeCap, gov.Adet, gov.Cap));
                    m.GovdeAdet = gov.Adet; m.GovdeCap = gov.Cap;
                    montaj[beam] = m;
                }
                if (m.EtriyeCap <= 0) continue;
                int need = 2 * KirisKesitEtriyeKutulari(beam.WidthCm).Count;
                string note = "";
                if (m.Cap > 0 && m.Adet > 0 && m.Adet < need)
                { note = "MON " + m.Adet + "→" + need; m.Adet = need; }
                if (m.DuzCap > 0 && m.DuzAdet > 0 && m.DuzAdet < need)
                { note += (note.Length == 0 ? "" : ", ") + "DUZ " + m.DuzAdet + "→" + need; m.DuzAdet = need; }
                if (note.Length > 0) _kirisKesitAdetNotlari[beam] = note;
                montaj[beam] = m;
            }
        }

        private static (double Ust, double Alt) KirisKesitDosemeKotlari(double katKotCm, double offsetCm, double kalinlikCm, double kirisKotCm)
        {
            double ust = katKotCm + offsetCm - kirisKotCm;
            return (ust, ust - kalinlikCm);
        }

        private static bool KirisKesitYolKes(List<Point2d> pts, double x, out double y)
        {
            y = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                if (Math.Abs(b.X - a.X) < 0.001) continue; // Uç gönyesini boyuna çubuk sayma.
                if (x < Math.Min(a.X, b.X) - 0.001 || x > Math.Max(a.X, b.X) + 0.001) continue;
                y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                return true;
            }
            return false;
        }

        private void KirisAcilimKesitTopla(FloorInfo floor, List<KirisAcilimSpan> chain,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj, double x0, double yShift, int pad,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            for (int i0 = 0; i0 < chain.Count;)
            {
                int i1 = i0;
                while (i1 + 1 < chain.Count && chain[i1 + 1].Beam.BeamId == chain[i0].Beam.BeamId) i1++;
                KirisAcilimKolonYuzu(chain, i0, i1, sup, out double a, out double b);
                KirisAcilimNumaraAraligi(chain, i0, i1, a, b, gorunus, out a, out b);
                KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out _, out _);
                double tercih = KirisKesitIstasyon(a, b, solSerbest || sagSerbest);
                // Önceki kural yalnız tanı karşılaştırması için: montaj kesişimi ≥2 ise sola kaydır.
                double eski = KirisKesitIstasyonBindirmedenKac(a, b, tercih, local => KirisKesitMontajKesAdedi(x0 + local));
                KirisKesitEngelAraliklari(x0, out var ilaveZ, out var bindirmeZ);
                var kolonZ = _surekliTemelAcilim ? new List<(double Lo, double Hi)>() : _kirisUstuKolonAraliklari;
                double s = KirisKesitIstasyonSec(a, b, tercih, ilaveZ, bindirmeZ, kolonZ);
                string Iz(double v) => (KirisKesitAraliktaMi(ilaveZ, v) ? "I" : "-") + (KirisKesitAraliktaMi(bindirmeZ, v) ? "B" : "-");
                string Z(List<(double Lo, double Hi)> z) => string.Join(",", z.Where(r => r.Hi > a && r.Lo < b)
                    .Select(r => string.Format(CultureInfo.InvariantCulture, "{0:0.#}~{1:0.#}", Math.Max(a, r.Lo), Math.Min(b, r.Hi))));
                KirisAcilimTani(string.Format(CultureInfo.InvariantCulture,
                    "KESIT_ISTASYON temel={0} kiris={1} a={2:0.#} b={3:0.#} eski={4:0.#}[{5}] yeni={6:0.#}[{7}] I={8} B={9} K={10}",
                    _surekliTemelAcilim ? 1 : 0, KirisAcilimEtiket(chain[i0].Beam, pad), a, b, eski, Iz(eski), s, Iz(s),
                    Z(ilaveZ), Z(bindirmeZ), Z(kolonZ)));
                int j = i0;
                while (j < i1 && s > chain[j].X1) j++;
                var span = chain[j];
                double top = yShift + KirisAcilimKotAt(chain, j, j, s);
                var info = new KirisKesitBilgi { B = span.Beam.WidthCm, H = span.Beam.HeightCm,
                    Kot = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0 + top - yShift,
                    X = x0 + s, Y = top, Ad = KirisAcilimEtiket(span.Beam, pad) };
                if (montaj != null && montaj.TryGetValue(span.Beam, out var m))
                {
                    info.Etriye = m.EtriyeCap;
                    if (_surekliTemelAcilim && m.AmpCap > 0 && m.AmpAralikCm > 0) { info.AmpCap = m.AmpCap; info.AmpAralik = m.AmpAralikCm; }
                }
                _kirisKesitAdetNotlari.TryGetValue(span.Beam, out info.AdetNotu);
                foreach (var don in _kirisKesitDonatilari)
                {
                    if (!KirisKesitYolKes(don.Yol, x0 + s, out double y)) continue;
                    double derinlik = top - y;
                    if (derinlik < 4.0 || derinlik > info.H - 4.0) continue;
                    info.Siralar.Add(new KirisKesitSira { Adet = don.Adet, Cap = don.Cap, Tur = don.Tur, Derinlik = derinlik });
                }
                // World0/1 aks üzerinde; kesit, gerçek kaçıklıklı gövdenin iki yüzünden döşemeye bakar.
                double u = (s - span.X0) / Math.Max(0.001, span.X1 - span.X0);
                var dir = (span.World1 - span.World0).GetNormal();
                var perp = new Vector2d(-dir.Y, dir.X);
                var p = span.World0 + (span.World1 - span.World0) * u;
                if (KirisAcilimBeamEkseni(span.Beam, out _, out _, out _, out _, out _, out var fizikselPerp))
                    p += fizikselPerp * KirisAcilimKaciklikMerkez(span.Beam);
                (double Ust, double Alt)? Doseme(double sign)
                {
                    var probe = p + perp * (sign * (info.B * 0.5 + 1.0));
                    var pt = _ntsDrawFactory.CreatePoint(new Coordinate(probe.X, probe.Y));
                    (double Ust, double Alt)? best = null;
                    foreach (var entry in _kirisKesitDosemeler)
                    {
                        if (!entry.Poly.Covers(pt)) continue;
                        var kot = KirisKesitDosemeKotlari((_model.BuildingBaseKotu + floor.ElevationM) * 100.0,
                            entry.Slab.OffsetFromFloorCm, entry.Slab.ThicknessCm, info.Kot);
                        double ust = kot.Ust, alt = kot.Alt;
                        if (entry.Slab.ThicknessCm <= 0 || alt >= 0 || ust <= -info.H) continue;
                        if (!best.HasValue || ust > best.Value.Ust) best = (ust, alt);
                    }
                    return best;
                }
                info.Sol = Doseme(-1); info.Sag = Doseme(1);
                if (_surekliTemelAcilim && _surekliTemelCfByBeam != null
                    && _surekliTemelCfByBeam.TryGetValue(span.Beam, out var temel))
                {
                    info.TemelKesit = BuildSurekliTemelEnKesit(temel);
                    info.Sol = info.Sag = null;
                }
                if (b > a && info.B > 11 && info.H > 11) _kirisKesitler.Add(info);
                i0 = i1 + 1;
            }
        }

        private static string KirisKesitHarf(int index)
        {
            string result = "";
            do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
            return result;
        }

        private static List<Point2d> KirisKesitCubukMerkezleri(double b, double h, int n, string tur, double derinlik, double cover = 5)
        {
            var pts = new List<Point2d>();
            if (b <= 11 || h <= 11 || n <= 0) return pts;
            var xs = new List<double>();
            if (tur == "mon" || tur == "duz")
            {
                foreach (var box in KirisKesitEtriyeKutulari(b)) { xs.Add(box.Lo + 1.0); xs.Add(box.Hi - 1.0); }
                xs.Sort();
                if (n < xs.Count) xs.Clear();
                if (xs.Count > 0 && xs.Count < n)
                {
                    var extra = new int[xs.Count - 1];
                    for (int k = xs.Count; k < n; k++)
                    {
                        int gap = 0;
                        for (int i = 1; i < extra.Length; i++)
                            if ((xs[i + 1] - xs[i]) / (extra[i] + 1) > (xs[gap + 1] - xs[gap]) / (extra[gap] + 1) + 1e-8) gap = i;
                        extra[gap]++;
                    }
                    var spread = new List<double> { xs[0] };
                    for (int i = 0; i < extra.Length; i++)
                    {
                        for (int j = 1; j <= extra[i]; j++) spread.Add(xs[i] + (xs[i + 1] - xs[i]) * j / (extra[i] + 1));
                        spread.Add(xs[i + 1]);
                    }
                    xs = spread;
                }
            }
            for (int j = 0; j < n; j++)
            {
                if (tur == "gov")
                    pts.Add(new Point2d(j % 2 == 0 ? cover : b - cover, -cover - (h - 2 * cover) * (j / 2 + 1) / ((n + 1) / 2 + 1)));
                else
                {
                    double d = tur == "mon" ? 5.0 : tur == "duz" ? h - 5.0
                        : derinlik + (derinlik < h * 0.5 ? -0.5 : 0.5);
                    pts.Add(new Point2d(xs.Count == n ? xs[j] : 5.0 + (b - 10) * (n == 1 ? 0.5 : (double)j / (n - 1)), -d));
                }
            }
            return pts;
        }

        private static string KirisKesitImza(KirisKesitBilgi k)
        {
            string N(double n) => Math.Round(n, 2).ToString("0.##", CultureInfo.InvariantCulture);
            string S((double Ust, double Alt)? s) => s.HasValue ? N(s.Value.Ust) + ":" + N(s.Value.Alt) : "-";
            return N(k.B) + "/" + N(k.H) + "/" + N(k.Kot) + "/" + k.Etriye + "/" + k.AmpCap + ":" + k.AmpAralik + "/" + S(k.Sol) + "/" + S(k.Sag) + "/" + k.TemelKesit?.AsText()
                + "/" + string.Join(";", k.Siralar.GroupBy(r => r.Tur + ":" + r.Cap + ":" + N(r.Derinlik))
                    .Select(g => g.Key + ":" + g.Sum(r => r.Adet)).OrderBy(t => t, StringComparer.Ordinal));
        }

        private double KirisAcilimKesitleriCiz(Transaction tr, BlockTableRecord ms, Database db, double x, double y)
        {
            var groups = _kirisKesitler.GroupBy(KirisKesitImza).Select(g => g.ToList()).ToList();
            if (groups.Count == 0) return y;
            double slabRise = groups.Max(g => Math.Max(g[0].Sol?.Ust ?? 0, g[0].Sag?.Ust ?? 0));
            double sectionX = x, temelOncekiSag = double.NaN;
            for (int i = 0; i < groups.Count; i++)
            {
                string harf = KirisKesitHarf(i);
                foreach (var cut in groups[i])
                {
                    // Mevcut konuma nihai +45/-55 cm ek taşıma; çizim sonunda 2× ölçeklenir.
                    // Temelde üst yüzde etriye ölçüleri ve yazısı var; üst işaret onların üstüne.
                    DrawKesitEtiketDxfStyle(tr, ms, db, new Point3d(cut.X, cut.Y + (10.0 + 45.0) / 2.0 + (cut.TemelKesit != null ? 15.0 : 0.0), 0),
                        new Vector2d(0, 1), new Vector2d(-1, 0), false, harf, 0.3, fullLetter: true);
                    // Temelde alt yüzün altında grobeton, etriye yazısı ve yüz ölçüleri var; işaret onların altına.
                    double temelAlti = cut.TemelKesit != null ? 7.5 : 0.0;
                    DrawKesitEtiketDxfStyle(tr, ms, db, new Point3d(cut.X, cut.Y - cut.H - (20.0 + 55.0) / 2.0 - temelAlti, 0),
                        new Vector2d(0, 1), new Vector2d(-1, 0), true, harf, 0.3, fullLetter: true);
                }
                var section = groups[i][0];
                double leftInset = Math.Min(0, section.TemelKesit?.EnvelopeInternal.MinX ?? 0);
                section.AmpAdetGrup = groups[i].Sum(c => c.AmpAdet);
                double temelSagPay = 0;
                if (section.TemelKesit != null)
                {
                    // Temel: komşu kesitlerin dış ölçü çizgileri arası TemelKesitAraCm (nihai 60 cm).
                    // Pay: ampatmanlı yanda iki ölçü sırası (24), yoksa sağda tek ölçü (12) / solda gövde etiketi (15);
                    // başlık (gövde ortasında) taşarsa o.
                    var env = section.TemelKesit.EnvelopeInternal;
                    double orta = section.B * 0.5 - env.MinX;
                    double solPay = Math.Max(env.MinX < -0.1 ? 24.0 : 15.0, TemelKesitBaslikYariCm - orta);
                    temelSagPay = Math.Max(env.MaxX > section.B + 0.1 ? 24.0 : 12.0, TemelKesitBaslikYariCm - (env.Width - orta));
                    if (!double.IsNaN(temelOncekiSag)) sectionX = temelOncekiSag + TemelKesitAraCm + solPay;
                }
                KirisAcilimTekKesitCiz(tr, ms, db, section, sectionX - leftInset, y, harf,
                    groups[i].Select(k => k.Ad + (string.IsNullOrEmpty(k.AdetNotu) ? "" : " (" + k.AdetNotu + ")")).Distinct().ToList());
                foreach (var cut in groups[i]) cut.DagitmaAdet = section.DagitmaAdet;
                if (section.TemelKesit != null) temelOncekiSag = sectionX + section.TemelKesit.EnvelopeInternal.Width + temelSagPay;
                sectionX = KirisKesitSonrakiX(sectionX, Math.Max(section.B, section.TemelKesit?.EnvelopeInternal.Width ?? section.B));
            }
            return y + Math.Max(0, slabRise) + 35.0;
        }

        /// <summary>
        /// Temel kesiti ölçüleri; uzatma çizgileri kesit köşelerinden (Dimexo 0). Ampatmanlı yanda iki sıra düşey ölçü
        /// (ampatman + gövde zinciri, dışta toplam); ampatman hiç yoksa sağda tek toplam. Altta toplam genişlik;
        /// ampatman varsa üstte ampatman ve gövde genişlikleri zinciri.
        /// </summary>
        private void TemelKesitOlculeri(Transaction tr, BlockTableRecord ms, Geometry kesit, double w, double h,
            double x, double y, double gro, ObjectId stil)
        {
            const double serit = 12.0;
            void Olcu(double rot, double x0, double y0, double x1, double y1, double lx, double ly)
                => AppendEntity(tr, ms, new RotatedDimension(rot, new Point3d(x + x0, y + y0, 0), new Point3d(x + x1, y + y1, 0),
                    new Point3d(x + lx, y + ly, 0), "", stil) { Layer = LayerOlcu, Dimexo = 0.0 });
            var sol = kesit.Coordinates.Where(c => c.X < -0.1).ToList();
            var sag = kesit.Coordinates.Where(c => c.X > w + 0.1).ToList();
            double ampLo = sol.Count > 0 ? sol.Min(c => c.X) : 0, ampHi = sag.Count > 0 ? sag.Max(c => c.X) : w;
            // Alt toplam genişlik: 2× öncesi 22.5 → nihai 45 cm grobeton altından.
            Olcu(0, ampLo, -h, ampHi, -h, 0, -h - gro - 22.5);
            if (sol.Count > 0)
            {
                double step = sol.Max(c => c.Y);
                Olcu(Math.PI / 2, ampLo, -h, ampLo, step, ampLo - serit, 0);
                Olcu(Math.PI / 2, ampLo, step, 0, 0, ampLo - serit, 0);
                Olcu(Math.PI / 2, ampLo, -h, 0, 0, ampLo - 2 * serit, 0);
                Olcu(0, ampLo, step, 0, 0, 0, 22.5);
            }
            if (sag.Count > 0)
            {
                double step = sag.Max(c => c.Y);
                Olcu(Math.PI / 2, ampHi, -h, ampHi, step, ampHi + serit, 0);
                Olcu(Math.PI / 2, ampHi, step, w, 0, ampHi + serit, 0);
                Olcu(Math.PI / 2, ampHi, -h, w, 0, ampHi + 2 * serit, 0);
                Olcu(0, w, 0, ampHi, step, 0, 22.5);
            }
            if (sol.Count > 0 || sag.Count > 0) Olcu(0, 0, 0, w, 0, 0, 22.5);
            else Olcu(Math.PI / 2, w, -h, w, 0, w + serit, 0);
        }

        private void KirisAcilimTekKesitCiz(Transaction tr, BlockTableRecord ms, Database db,
            KirisKesitBilgi k, double x, double y, string harf, List<string> adlar)
        {
            double w = k.B, h = k.H;
            Geometry Rect(double l, double r, double bot, double top) => _ntsDrawFactory.CreatePolygon(new[] {
                new Coordinate(l, bot), new Coordinate(r, bot), new Coordinate(r, top), new Coordinate(l, top), new Coordinate(l, bot) });
            var rects = new List<Geometry> { Rect(x, x + w, y - h, y) };
            if (k.Sol.HasValue) rects.Add(Rect(x - 15, x, y + k.Sol.Value.Alt, y + k.Sol.Value.Ust));
            if (k.Sag.HasValue) rects.Add(Rect(x + w, x + w + 15, y + k.Sag.Value.Alt, y + k.Sag.Value.Ust));
            var shape = TryCascadedPolygonUnionSafe(rects);
            void Outline(Geometry g)
            {
                if (g is Polygon poly)
                {
                    var c = poly.ExteriorRing.Coordinates;
                    for (int i = 1; i < c.Length; i++)
                    {
                        if (Math.Abs(c[i].X - c[i - 1].X) < 0.001 &&
                            (Math.Abs(c[i].X - (x - 15)) < 0.001 || Math.Abs(c[i].X - (x + w + 15)) < 0.001)) continue;
                        KirisAcilimCizgi(tr, ms, c[i - 1].X, c[i - 1].Y, c[i].X, c[i].Y, LayerKiris);
                    }
                }
                else if (g is GeometryCollection col) for (int i = 0; i < col.NumGeometries; i++) Outline(col.GetGeometryN(i));
            }
            if (k.TemelKesit != null)
                DrawGeometryRingsAsPolylines(tr, ms,
                    NetTopologySuite.Geometries.Utilities.AffineTransformation.TranslationInstance(x, y).Transform(k.TemelKesit), LayerTemelBeykent);
            else if (shape != null) Outline(shape);
            void SlabEnd((double Ust, double Alt)? slab, double at)
            {
                if (!slab.HasValue) return;
                // Finiş = döşeme kalınlığı; yukarı/aşağı taşmasın.
                KirisAcilimCizgi(tr, ms, at, y + slab.Value.Alt, at, y + slab.Value.Ust, LayerKesitSiniri);
            }
            SlabEnd(k.Sol, x - 15); SlabEnd(k.Sag, x + w + 15);
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10, 1, "PLAN_OLCU_25");
            double ampLo = k.TemelKesit?.EnvelopeInternal.MinX ?? 0, ampHi = k.TemelKesit?.EnvelopeInternal.MaxX ?? w;
            // Temel kesiti: temel planı kesitleriyle aynı 10 cm grobeton; alt ölçü ve başlık grobeton altına.
            double gro = k.TemelKesit != null ? GrobetonUnderFoundationKesitHeightCm : 0.0;
            if (gro > 0)
            {
                DrawGeometryRingsAsPolylines(tr, ms, Rect(x + ampLo, x + ampHi, y - h - gro, y - h), LayerGrobeton, addHatch: true,
                    hatchAngleRad: 0, applySmallTriangleTrim: true, hatchPatternName: GrobetonHatchPatternName,
                    hatchPatternScale: GrobetonHatchPatternScale, hatchLayerOverride: LayerTarama);
                DrawZeminSingleLineAlongFoundationSection(tr, ms, x + ampLo, x + ampHi, y - h - gro, y - h, true, false, SurekliTemelZeminCizgiUzantisiCm);
            }
            if (k.TemelKesit != null) TemelKesitOlculeri(tr, ms, k.TemelKesit, w, h, x, y, gro, stil);
            else
            {
                // Alt en ölçüsü: 2× öncesi 22.5 → nihai 45 cm (eski 30 → 60).
                AppendEntity(tr, ms, new RotatedDimension(0, new Point3d(x, y - h, 0), new Point3d(x + w, y - h, 0),
                    new Point3d(x, y - h - 22.5, 0), "", stil) { Layer = LayerOlcu });
                foreach (var dim in KirisKesitYanOlculeri(w, h, k.Sol, k.Sag))
                {
                    AppendEntity(tr, ms, new RotatedDimension(Math.PI / 2,
                        new Point3d(x + dim.X0, y + dim.Y0, 0), new Point3d(x + dim.X1, y + dim.Y1, 0),
                        new Point3d(x + dim.Lane, y, 0), "", stil) { Layer = LayerOlcu });
                }
            }
            Text(x + w * 0.5, y - h - gro - 36, harf + " - " + harf + " KESİTİ", LayerKesitIsmi);
            for (int i = 0; i < adlar.Count; i++) Text(x + w * 0.5, y - h - gro - 48 - i * 8, adlar[i], LayerKirisYazisi);
            void Text(double xx, double yy, string s, string layer = KirisAcilimDonatiYaziLayer)
                => DrawBeamLabel(tr, ms, db, new Point3d(xx, yy, 0), s, 5, 0, layer, useMiddleCenter: true);
            void Leader(List<Point2d> points, double lineY, string label, bool web, double textDy = 0)
            {
                if (points.Count == 0) return;
                var left = points.OrderBy(p => p.X).First();
                var right = points.OrderBy(p => p.X).Last();
                KirisAcilimCizgi(tr, ms, x + left.X, lineY, x + right.X, lineY, LayerEtiketCizgisi);
                foreach (var point in points)
                    KirisAcilimCizgi(tr, ms, x + point.X, y + point.Y, x + point.X, lineY, LayerEtiketCizgisi);
                if (web)
                {
                    double labelRight = x - 5.0 / 2.0;
                    KirisAcilimCizgi(tr, ms, labelRight, lineY, x + left.X, lineY, LayerEtiketCizgisi);
                    var text = new DBText { TextString = label, Height = 5,
                        Layer = KirisAcilimDonatiYaziLayer, TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                        HorizontalMode = TextHorizontalMode.TextRight, VerticalMode = TextVerticalMode.TextBottom,
                        Position = new Point3d(labelRight, lineY + 1, 0), AlignmentPoint = new Point3d(labelRight, lineY + 1, 0) };
                    AppendEntity(tr, ms, text);
                    text.AdjustAlignment(db);
                }
                else Text(x + (left.X + right.X) * 0.5, lineY + textDy, label);
            }
            var labelRows = k.Siralar.GroupBy(r => r.Tur + ":" + r.Cap + ":" + Math.Round(r.Derinlik, 2))
                .Select(g => new KirisKesitSira { Tur = g.First().Tur, Cap = g.First().Cap,
                    Adet = g.Sum(r => r.Adet), Derinlik = g.First().Derinlik })
                .OrderBy(r => r.Tur == "mon" || r.Tur == "duz" ? 0 : 1).ToList();
            bool monUst = KirisAcilimMontajUstYuz;
            var layout = KirisKesitCubukYerlesimi(w, h, labelRows, monUst, _surekliTemelAcilim ? 6 : 5);
            int topLabels = 0, bottomLabels = 0;
            foreach (var r in labelRows)
            {
                var points = layout.Where(c => ReferenceEquals(c.Donati, r)).Select(c => c.Merkez).ToList();
                if (points.Count == 0) continue;
                string label = points.Count.ToString(CultureInfo.InvariantCulture) + "ø" + r.Cap;
                foreach (var p in points) DrawTwoArcCirclePline(tr, ms, x + p.X, y + p.Y, 0.5, LayerDonatiGovde);
                if (r.Tur == "gov") Leader(points, y + points.Min(p => p.Y) - 4, label, true);
                else
                {
                    bool top = r.Tur == "mon" ? monUst
                        : r.Tur == "duz" ? !monUst
                        : r.Derinlik < h * 0.5;
                    int index = top ? topLabels++ : bottomLabels++;
                    // Temel kesitinde alt yüz etiketi grobetonun altına: 2× öncesi 7.5 (nihai 15 cm) daha aşağı.
                    double lineY = index == 0 ? (top ? y + 7 : y - h - 6 - (k.TemelKesit != null ? 7.5 : 0.0))
                        : top ? y + points.Min(p => p.Y) - 5 - (index - 1) * 10
                        : y + points.Max(p => p.Y) + 5 + (index - 1) * 10;
                    Leader(points, lineY, label, false, (top == (index == 0)) ? 5 : -5);
                }
            }
            double acilimAlt = y - h - 72 - adlar.Count * 8;
            if (k.Etriye > 0)
            {
                // Kolon etriyesinin aynı 135° kancalı geometri ve açılım yolu.
                double cover = _surekliTemelAcilim ? 5 : 4;
                var boxes = KirisKesitEtriyeKutulari(w, cover);
                foreach (var box in boxes)
                    AppendKolonKesitEtriyePline(tr, ms, x + box.Lo, y - h + cover, x + box.Hi, y - cover, 1.0, Math.Max(10.0, EtriyeAcilimKancaCm(k.Etriye)));
                var oldCols = _kolonDuseyGrupColNos; int oldKat = _kolonDuseyBenzerKatSayisi; bool old25 = _kolonDuseyOlcek25;
                try
                {
                    _kolonDuseyGrupColNos = null; _kolonDuseyBenzerKatSayisi = 1; _kolonDuseyOlcek25 = false;
                    acilimAlt = DrawKolonKesitEtriyeAcilim(tr, ms, new Envelope(x, x + w, y - h, y),
                        new List<(double midX, double w, double h)> { (x + w * 0.5, boxes[0].Hi - boxes[0].Lo, h - 2 * cover) },
                        k.Etriye, 1, 0, false, out _, out _, yTopOverride: y - h - 72 - adlar.Count * 8,
                        minHookCm: 10.0, showQuantity: false, radiusCm: 1.0,
                        textHeightCm: 5.0, labelPrefix: KirisKesitEtriyeCarpan(boxes.Count));
                }
                finally { _kolonDuseyGrupColNos = oldCols; _kolonDuseyBenzerKatSayisi = oldKat; _kolonDuseyOlcek25 = old25; }
            }
            if (k.TemelKesit != null && k.AmpCap > 0 && ampHi - ampLo > w + 0.1)
            {
                // Gövde alt sırasının uç çubukları: dağıtma çubukları bunlara en çok 35 cm aralıkla bağlanır.
                var alt = layout.Where(c => c.Merkez.Y < -h * 0.5).Select(c => c.Merkez).ToList();
                double altY = alt.Count > 0 ? alt.Min(p => p.Y) : 0;
                alt = alt.Where(p => p.Y < altY + 1.5).ToList();
                double altX0 = alt.Count > 0 ? alt.Min(p => p.X) : 6.0, altX1 = alt.Count > 0 ? alt.Max(p => p.X) : w - 6.0;
                SurekliTemelAmpatmanDonatiCiz(tr, ms, db, k, x, y, ampLo, ampHi, acilimAlt, altX0, altX1);
            }
        }

        /// <summary>Dağıtma için en büyük agrega varsayımı (mm): proje vermiyor; yalnız en küçük net boşluk hesabında.</summary>
        private const double SurekliDagitmaDmaxVarsayimMm = 22.0;
        /// <summary>Tek doğrultulu plak dağıtma donatısı üst aralığı (cm). Kaynak: kullanıcı notu (TS 500 §11.2.3); birincil metin doğrulanmadı.</summary>
        private const double SurekliDagitmaMaxAralikCm = 30.0;

        /// <summary>
        /// Ampatman dağıtma donatısı değişkenleri (ayrı ayrı; uygunluk kararı değildir). Ana donatı Øa/s → As_ana (cm²/m);
        /// gerekli dağıtma alanı As_ana/5 (cm²/m); bu alanı veren eksen aralığı; üst aralık min(30, alan aralığı);
        /// en küçük net boşluk max(Ø, 25 mm, 4·Dmax/3) ve buna karşılık gelen en küçük eksen aralığı (net + Ø).
        /// Kaynak: kullanıcı aktarım notu (TS 500 §9.5.2, §11.2.3, §10.4.3); madde metni birincil kaynaktan doğrulanmadı.
        /// </summary>
        private static (double AsAna, double AsGerek, double SAlan, double SMax, double NetMin, double EksenMin) SurekliDagitmaKurali(
            int anaCapMm, int anaAralikCm, int dagCapMm, double dMaxMm)
        {
            double Alan(int c) => Math.PI * c * c / 400.0;
            double asAna = anaCapMm > 0 && anaAralikCm > 0 ? Alan(anaCapMm) * 100.0 / anaAralikCm : 0;
            double asGerek = asAna / 5.0;
            double sAlan = asGerek > 0 ? Alan(dagCapMm) / asGerek * 100.0 : double.PositiveInfinity;
            double sMax = Math.Min(SurekliDagitmaMaxAralikCm, sAlan);
            double netMin = Math.Max(dagCapMm / 10.0, Math.Max(2.5, 4.0 * dMaxMm / 30.0));
            return (asAna, asGerek, sAlan, sMax, netMin, netMin + dagCapMm / 10.0);
        }

        /// <summary>
        /// Ampatman U donatısı: ampatman uçlarından ve altından 5 cm paspay, bacaklar ampatman üstünden 5 cm aşağıda.
        /// Kesitte kanat içinde çap/aralık; açılımı (bacak, taban, L = 2·bacak + taban) etriye açılımının altında.
        /// </summary>
        private void SurekliTemelAmpatmanDonatiCiz(Transaction tr, BlockTableRecord ms, Database db,
            KirisKesitBilgi k, double x, double y, double ampLo, double ampHi, double acilimAlt, double altX0, double altX1)
        {
            const double pas = 5.0, yaziH = 5.0, bosluk = 4.0;
            double w = k.B, h = k.H;
            double ampUst = k.TemelKesit.Coordinates.Where(c => c.X < -0.1 || c.X > w + 0.1).Max(c => c.Y);
            double ux0 = ampLo + pas, ux1 = ampHi - pas, uy0 = -h + pas, uy1 = ampUst - pas;
            double taban = ux1 - ux0, bacak = uy1 - uy0;
            if (taban < 1.0 || bacak < 1.0) return;
            SurekliTemelAmpatmanUPline(tr, ms, x + ux0, x + ux1, y + uy0, y + uy1);
            string ad = "ø" + k.AmpCap.ToString(CultureInfo.InvariantCulture) + "/" + k.AmpAralik.ToString(CultureInfo.InvariantCulture);
            // Etiket sağ kanadın ortasında (sol yanda gövde etiketi var); sağ kanat yoksa solda.
            bool sol = ampHi - w < 20.0;
            double kx = sol ? 0.5 * ampLo : 0.5 * (w + ampHi);
            bool solKanat = ampLo < -1.0, sagKanat = ampHi > w + 1.0;
            // Dağıtma yazısı diğer kanatta; tek kanatta ikisi alt alta.
            bool dagAyriKanat = sol ? sagKanat : solKanat;
            double ymid = 0.5 * (uy0 + uy1);
            DrawBeamLabel(tr, ms, db, new Point3d(x + kx, y + ymid + (dagAyriKanat ? 0 : 4.0), 0), ad, yaziH, 0,
                KirisAcilimDonatiYaziLayer, useMiddleCenter: true);

            // Dağıtma (ana U donatısına dik, temel boyunca): her kanatta U'nun bacak ucu ve alt köşesi sabit; bacakta ve
            // alt kolda gövde alt sırasının uç çubuğuna kadar eksen aralığı ≤ SMax (30 cm ve As_ana/5 alan aralığı).
            // Net boşluk sınırının altına düşen nokta konmaz. Çap: sürekli temellerin en küçük boyuna çapı.
            if (_surekliDagitmaCap > 0)
            {
                var kural = SurekliDagitmaKurali(k.AmpCap, k.AmpAralik, _surekliDagitmaCap, SurekliDagitmaDmaxVarsayimMm);
                double dagMax = kural.SMax;
                var dag = new List<Point2d>();
                double sGercek = 0;
                void Ekle(Point2d p)
                {
                    if (dag.Count > 0 && dag[dag.Count - 1].GetDistanceTo(p) < kural.EksenMin - 1e-6) return;
                    dag.Add(p);
                }
                void Ara(Point2d a, Point2d b, bool uclarDahil)
                {
                    int n = Math.Max(1, (int)Math.Ceiling(a.GetDistanceTo(b) / dagMax - 1e-6));
                    sGercek = Math.Max(sGercek, a.GetDistanceTo(b) / n);
                    for (int i = uclarDahil ? 0 : 1; i <= (uclarDahil ? n : n - 1); i++)
                        Ekle(a + (b - a) * ((double)i / n));
                }
                double yb = uy0 + 1.0, yt = uy1 - 1.0;
                if (solKanat) { Ara(new Point2d(ux0 + 1.0, yt), new Point2d(ux0 + 1.0, yb), true); Ara(new Point2d(ux0 + 1.0, yb), new Point2d(altX0, yb), false); }
                if (sagKanat) { Ara(new Point2d(ux1 - 1.0, yt), new Point2d(ux1 - 1.0, yb), true); Ara(new Point2d(ux1 - 1.0, yb), new Point2d(altX1, yb), false); }
                KirisAcilimTani(string.Format(CultureInfo.InvariantCulture,
                    "DAGITMA {0} ana=ø{1}/{2} AsAna={3:0.00} AsGerek={4:0.00}cm2/m dag=ø{5} sAlan={6:0.0} sMax={7:0.0} netMin={8:0.00} (Dmax varsayim {9:0}mm) eksenMin={10:0.00} adet={11} sCizim={12:0.0}",
                    k.Ad, k.AmpCap, k.AmpAralik, kural.AsAna, kural.AsGerek, _surekliDagitmaCap,
                    kural.SAlan, kural.SMax, kural.NetMin, SurekliDagitmaDmaxVarsayimMm, kural.EksenMin, dag.Count, sGercek));
                foreach (var p in dag) DrawTwoArcCirclePline(tr, ms, x + p.X, y + p.Y, 0.5, LayerDonatiGovde);
                k.DagitmaAdet = dag.Count;
                if (dag.Count > 0)
                {
                    string dagAd = dag.Count.ToString(CultureInfo.InvariantCulture) + "ø"
                        + _surekliDagitmaCap.ToString(CultureInfo.InvariantCulture) + " dağ.";
                    double dx = dagAyriKanat ? (sol ? 0.5 * (w + ampHi) : 0.5 * ampLo) : kx;
                    DrawBeamLabel(tr, ms, db, new Point3d(x + dx, y + ymid - (dagAyriKanat ? 0 : 4.0), 0), dagAd, yaziH, 0,
                        KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                }
            }

            double cx = x + w * 0.5;
            double ax0 = cx - taban * 0.5, ax1 = cx + taban * 0.5;
            double ay1 = acilimAlt - 12.0, ay0 = ay1 - bacak;
            SurekliTemelAmpatmanUPline(tr, ms, ax0, ax1, ay0, ay1);
            ObjectId idBacak = DrawKesitAcilimDonatiYazi(tr, ms, db, new Point3d(ax0 - 7.0, 0.5 * (ay0 + ay1), 0),
                bacak.ToString("0", CultureInfo.InvariantCulture), yaziH, Math.PI / 2.0);
            SnapTextBoxRightLeftOf(tr, idBacak, ax0, bosluk);
            ObjectId idTaban = DrawKesitAcilimDonatiYazi(tr, ms, db, new Point3d(cx, ay0 + yaziH, 0),
                taban.ToString("0", CultureInfo.InvariantCulture), yaziH, 0.0);
            SnapTextBoxBottomAbove(tr, idTaban, ay0, bosluk);
            double boy = 2.0 * bacak + taban;
            ObjectId idAd = DrawKesitAcilimDonatiYazi(tr, ms, db, new Point3d(cx, ay0 - 13.0, 0),
                (k.AmpAdetGrup > 0 ? k.AmpAdetGrup.ToString(CultureInfo.InvariantCulture) : "") + ad
                + " L=" + boy.ToString("0", CultureInfo.InvariantCulture) + " AMPATMAN", yaziH, 0.0);
            SnapTextBoxTopBelow(tr, idAd, ay0, 5.0);
        }

        /// <summary>Yukarı açık U, DONATI katmanında; alt köşeler 1 birim (2× sonrası nihai 2 cm) yarıçaplı.</summary>
        private static void SurekliTemelAmpatmanUPline(Transaction tr, BlockTableRecord ms, double x0, double x1, double y0, double y1)
        {
            const double r = 1.0;
            double bulge = Math.Tan(Math.PI / 8.0); // 90° yay, sola dönüş
            var pl = new Polyline(6);
            pl.AddVertexAt(0, new Point2d(x0, y1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x0, y0 + r), bulge, 0, 0);
            pl.AddVertexAt(2, new Point2d(x0 + r, y0), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1 - r, y0), bulge, 0, 0);
            pl.AddVertexAt(4, new Point2d(x1, y0 + r), 0, 0, 0);
            pl.AddVertexAt(5, new Point2d(x1, y1), 0, 0, 0);
            pl.Layer = KirisAcilimDonatiLayer;
            pl.ColorIndex = 256;
            AppendEntity(tr, ms, pl);
        }
    }
}
