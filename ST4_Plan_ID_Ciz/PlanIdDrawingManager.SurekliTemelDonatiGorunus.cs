using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;
using NetTopologySuite.Geometries;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private Geometry BuildSurekliTemelEnKesit(ContinuousFoundationInfo cf)
        {
            var pieces = new List<Geometry> { KirisAcilimRect(_ntsDrawFactory, 0, -cf.HeightCm, cf.WidthCm, 0) };
            if (cf.AmpatmanWidthCm > cf.WidthCm + 0.1 && cf.AmpatmanHeightCm > 0.1
                && _axisService.TryIntersect(cf.FixedAxisId, cf.StartAxisId, out Point2d a)
                && _axisService.TryIntersect(cf.FixedAxisId, cf.EndAxisId, out Point2d b) && a.GetDistanceTo(b) > 1)
            {
                var u = (b - a).GetNormal(); var normal = new Vector2d(-u.Y, u.X);
                int offset = cf.FixedAxisId >= 1001 && cf.FixedAxisId <= 1999 ? -cf.OffsetRaw : cf.OffsetRaw;
                ComputeBeamEdgeOffsets(offset, cf.WidthCm * 0.5, out _, out double lower);
                var amp = AmpatmanFootprintPoly(cf);
                if (amp != null && !amp.IsEmpty)
                {
                    SurekliGorunusAralik(amp, a, normal, out double lo, out double hi);
                    pieces.Add(KirisAcilimRect(_ntsDrawFactory, lo - lower, -cf.HeightCm, hi - lower,
                        -cf.HeightCm + Math.Min(cf.HeightCm, cf.AmpatmanHeightCm)));
                }
            }
            return NtsOverlaySafe.CascadedUnionSafe(pieces);
        }

        // Ortak kiriş çizicisinin 5.5 cm boyuna / 4 cm etriye varsayımını
        // yalnız sentetik temel kopyasında uyarla. Kaynak ve kirişler değişmez.
        private sealed class SurekliTemelOrtuUyarlamasi : IDisposable
        {
            private readonly List<KirisAcilimSpan> spans;
            private readonly List<BeamInfo> beams;
            private readonly double inset;
            private readonly bool width;
            public SurekliTemelOrtuUyarlamasi(List<KirisAcilimSpan> chain, double delta, bool shrinkWidth = false)
            {
                spans = chain; beams = chain.Select(s => s.Beam).Distinct().ToList(); inset = delta; width = shrinkWidth;
                foreach (var beam in beams) { beam.HeightCm -= 2 * inset; if (width) beam.WidthCm -= 2; }
                foreach (var span in spans) { span.Kot0 -= inset; span.Kot1 -= inset; }
            }
            public void Dispose()
            {
                foreach (var beam in beams) { beam.HeightCm += 2 * inset; if (width) beam.WidthCm += 2; }
                foreach (var span in spans) { span.Kot0 += inset; span.Kot1 += inset; }
            }
        }
        /// <summary>
        /// Temelin kolon arası mesnetleri: oturan kolonlar ve saplandığı temeller. Her iki
        /// mesnet arası ayrı temel kirişidir; kenardaki kolonsuz çıkıntı ayrı açıklık sayılmaz.
        /// </summary>
        private List<SurekliGorunusKolon> SurekliGorunusMesnetleri(SurekliGorunusTakim takim, SurekliGorunusTemel t)
        {
            var list = takim.Kolonlar.Where(c => t.Kolonlar.Contains(c.Kaynak.ColumnNo)).ToList();
            foreach (var s in takim.Saplamalar.Where(s => s.Temel == t))
                list.Add(new SurekliGorunusKolon { Plan = _ntsDrawFactory.CreatePoint(new Coordinate(s.Dugum.X, s.Dugum.Y)),
                    X0 = s.X0, X1 = s.X1 });
            // Tek mesnetli/mesnetsiz temelde (ör. MS_OTP_01 T54) uç düğümleri açıklık sınırıdır.
            if (list.Count < 2)
                foreach (var node in new[] { t.A, t.B })
                {
                    var point = _ntsDrawFactory.CreatePoint(new Coordinate(node.X, node.Y));
                    if (list.Any(c => c.Plan.Distance(point) < 0.1
                        || (c.Kaynak != null && _axisService.TryIntersect(c.Kaynak.AxisXId, c.Kaynak.AxisYId, out Point2d axis)
                            && axis.GetDistanceTo(node) < 0.1))) continue;
                    double station = SurekliGorunusIstasyonu(t, node);
                    list.Add(new SurekliGorunusKolon { Plan = point, X0 = station, X1 = station });
                }
            return list.OrderBy(c => c.X0).ToList();
        }

        /// <summary>
        /// THE düğüm koordinatlarıyla eşleme (sıra/boy değil). Kaydın ilavesi bitiş düğümünün düştüğü mesnede
        /// (kolon izdüşümü ±30 cm); kolon olmayan ara düğümün (CK T14 düğüm 83) ilavesi çizilmez.
        /// Açıklığa, orta bölümünü en çok kaplayan kaydın donatısı verilir. Kayıt yönü iki yönde denenir.
        /// </summary>
        private bool SurekliTemelGeometrikAta(List<KirisAcilimSpan> spans, List<SurekliGorunusKolon> mesnetler,
            SurekliGorunusTemel t, BeamInfo parent, Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            if (spans.Count == 0 || mesnetler.Count < 2 || _surekliTemelSpanKayitlar == null) return false;
            if (!GroupSpansByT(_surekliTemelSpanKayitlar).TryGetValue(GetBeamNumero(parent.BeamId), out var kayitlar)
                || kayitlar.Count == 0) return false;
            // 1) GPR temel statik satırındaki sol/sağ kolon adları (TMX/THE ile aynı sıra).
            FloorInfo destek = SurekliTemelDestekKati();
            bool adli = kayitlar.Any(k => !string.IsNullOrEmpty(k.SolKolon) || !string.IsNullOrEmpty(k.SagKolon));
            string Ad(SurekliGorunusKolon m) => m?.Kaynak == null ? null : KirisAcilimKolonAdi(destek, m.Kaynak.ColumnNo);
            bool Ayni(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            // 2) Ad yoksa (saplama, serbest uç, adsız kayıt) THE düğüm koordinatları.
            bool koordinatli = kayitlar.All(k => k.Koordinatli);
            if (!adli && !koordinatli) return false;
            Func<double, double> ist = null;
            Func<Point2d, double> R = p => 0;
            var ic = kayitlar.FindAll(k => !k.UcStub);
            Point2d o = ic.Count > 0 ? ic[0].P1 : default;
            Vector2d d = ic.Count > 0 ? ic[ic.Count - 1].P2 - o : default;
            double boyCm = 100.0 * d.Length;
            if (koordinatli && boyCm >= 1.0)
            {
                d = d.GetNormal();
                R = p => 100.0 * (p - o).DotProduct(d);
                // Kayıt ekseni → görünüş istasyonu; A ucundan ileri ya da geri.
                double Ileri(double r) => SurekliGorunusIstasyonu(t, t.A + t.Yon.MultiplyBy(r));
                double Geri(double r) => SurekliGorunusIstasyonu(t, t.A + t.Yon.MultiplyBy(boyCm - r));
                double Hata(Func<double, double> f) => mesnetler.Sum(m =>
                    kayitlar.Min(k => Math.Abs(f(R(k.P2)) - (m.X0 + m.X1) * 0.5)));
                ist = Hata(Ileri) <= Hata(Geri) ? (Func<double, double>)Ileri : Geri;
            }

            (int A, int C, int UA, int UC) Bitis(SurekliTemelSpanKayit k) =>
                k == null ? default : (k.Bilgi.SolAltAdet, k.Bilgi.SolAltCap, k.Bilgi.SolUstAdet, k.Bilgi.SolUstCap);
            (int A, int C, int UA, int UC) Baslangic(SurekliTemelSpanKayit k) =>
                k == null ? default : (k.BasAltAdet, k.BasAltCap, k.BasUstAdet, k.BasUstCap);
            // Mesnet ilavesi = önceki kaydın bitiş (124/128) ∪ sonraki kaydın başlangıç (116/120) alanı;
            // ikisi de doluysa As'ı büyük olan (CK'da hiç birlikte dolu değil).
            (int A, int C, int UA, int UC) IlaveAt(SurekliGorunusKolon m)
            {
                SurekliTemelSpanKayit biten = null, baslayan = null;
                string ad = Ad(m);
                if (adli && ad != null)
                {
                    biten = kayitlar.FirstOrDefault(k => Ayni(k.SagKolon, ad));
                    baslayan = kayitlar.FirstOrDefault(k => Ayni(k.SolKolon, ad));
                }
                if (biten == null && baslayan == null && ist != null)
                {
                    double bestB = double.MaxValue, bestS = double.MaxValue, mid = (m.X0 + m.X1) * 0.5;
                    bool Icinde(double s) => s >= Math.Min(m.X0, mid - 30.0) - 30.0 && s <= Math.Max(m.X1, mid + 30.0) + 30.0;
                    foreach (var k in kayitlar)
                    {
                        if (k.N2 != 0) { double s = ist(R(k.P2)); if (Icinde(s) && Math.Abs(s - mid) < bestB) { bestB = Math.Abs(s - mid); biten = k; } }
                        if (k.N1 != 0) { double s = ist(R(k.P1)); if (Icinde(s) && Math.Abs(s - mid) < bestS) { bestS = Math.Abs(s - mid); baslayan = k; } }
                    }
                }
                var e = Bitis(biten); var b = Baslangic(baslayan);
                (int, int) Buyuk(int a1, int c1, int a2, int c2) => a2 * c2 * c2 > a1 * c1 * c1 ? (a2, c2) : (a1, c1);
                var alt = Buyuk(e.A, e.C, b.A, b.C);
                var ust = Buyuk(e.UA, e.UC, b.UA, b.UC);
                return (alt.Item1, alt.Item2, ust.Item1, ust.Item2);
            }
            SurekliGorunusKolon MesnetAt(double station) =>
                mesnetler.OrderBy(m => Math.Abs((m.X0 + m.X1) * 0.5 - station)).First();

            foreach (var span in spans)
            {
                if (!donati.TryGetValue(span.Beam, out var bil)) bil = default;
                double lo = Math.Min(span.X0, span.X1), hi = Math.Max(span.X0, span.X1);
                string adSol = Ad(MesnetAt(span.X0)), adSag = Ad(MesnetAt(span.X1));
                // Açıklık kaydı: iki uç kolon adı (iki yönde) tutan kayıt; yoksa koordinatla en çok örtüşen.
                SurekliTemelSpanKayit best = adli ? kayitlar.FirstOrDefault(k =>
                    (Ayni(k.SolKolon, adSol) && Ayni(k.SagKolon, adSag)) || (Ayni(k.SolKolon, adSag) && Ayni(k.SagKolon, adSol))) : null;
                double bestOrtak = 1.0;
                if (best == null && ist != null)
                    foreach (var k in kayitlar)
                    {
                        double a = ist(R(k.P1)), b = ist(R(k.P2));
                        double ortak = Math.Min(hi, Math.Max(a, b)) - Math.Max(lo, Math.Min(a, b));
                        if (ortak > bestOrtak) { bestOrtak = ortak; best = k; }
                    }
                if (best != null && (best.Bilgi.Adet > 0 || best.Bilgi.DuzAdet > 0)) bil = best.Bilgi;
                var s = IlaveAt(MesnetAt(span.X0));
                var r = IlaveAt(MesnetAt(span.X1));
                bil.SolAltAdet = s.A; bil.SolAltCap = s.C; bil.SolUstAdet = s.UA; bil.SolUstCap = s.UC;
                bil.SagAltAdet = r.A; bil.SagAltCap = r.C; bil.SagUstAdet = r.UA; bil.SagUstCap = r.UC;
                donati[span.Beam] = bil;
            }
            return true;
        }

        /// <summary>
        /// TMX/GPR açıklık kaydındaki ilave (124 alt, 128 üst) kaydın BİTİŞ düğümündeki mesnede aittir
        /// (CK T6: 0→31 kaydı 2Ø12 = SB-31; 31→32 kaydı 5Ø20 = SB-32). Her bay: Sol* = başlangıç mesnedi
        /// (önceki kayıt, uç kaydı dahil), Sag* = bitiş mesnedi (kendi kaydı). Görünüş tersse yer değişir.
        /// </summary>
        private void SurekliTemelIlaveMesneteAta(List<KirisAcilimSpan> kaynakSirasi, BeamInfo parent,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati, bool ters)
        {
            if (kaynakSirasi.Count == 0 || _surekliTemelSpanKayitlar == null) return;
            GroupSpansByT(_surekliTemelSpanKayitlar).TryGetValue(GetBeamNumero(parent.BeamId), out var kayitlar);
            var ic = kayitlar?.FindAll(k => !k.UcStub);
            var sol = new (int A, int C, int UA, int UC)[kaynakSirasi.Count];
            var sag = new (int A, int C, int UA, int UC)[kaynakSirasi.Count];
            (int, int, int, int) Ilave(KirisAcilimMontajBilgi b) => (b.SolAltAdet, b.SolAltCap, b.SolUstAdet, b.SolUstCap);
            for (int i = 0; i < kaynakSirasi.Count; i++)
            {
                if (!donati.TryGetValue(kaynakSirasi[i].Beam, out var bil)) continue;
                if (ic != null && ic.Count == kaynakSirasi.Count)
                {
                    int at = kayitlar.IndexOf(ic[i]);
                    sag[i] = Ilave(ic[i].Bilgi);
                    if (at > 0) sol[i] = Ilave(kayitlar[at - 1].Bilgi);
                    // Başlangıç düğümü ilavesi (TMX 116/120) bitiş alanı boşsa mesnete eklenir.
                    if (sol[i].A <= 0) { sol[i].A = ic[i].BasAltAdet; sol[i].C = ic[i].BasAltCap; }
                    if (sol[i].UA <= 0) { sol[i].UA = ic[i].BasUstAdet; sol[i].UC = ic[i].BasUstCap; }
                }
                else
                {
                    // Kayıt sayısı açıklıkla eşleşmiyorsa: atanan kayıt bitiş mesnedi, önceki bay başlangıç.
                    sag[i] = Ilave(bil);
                    if (i > 0) sol[i] = sag[i - 1];
                }
            }
            for (int i = 0; i < kaynakSirasi.Count; i++)
            {
                var key = kaynakSirasi[i].Beam;
                if (!donati.TryGetValue(key, out var bil)) continue;
                var s = ters ? sag[i] : sol[i];
                var r = ters ? sol[i] : sag[i];
                bil.SolAltAdet = s.A; bil.SolAltCap = s.C; bil.SolUstAdet = s.UA; bil.SolUstCap = s.UC;
                bil.SagAltAdet = r.A; bil.SagAltCap = r.C; bil.SagUstAdet = r.UA; bil.SagUstCap = r.UC;
                donati[key] = bil;
            }
        }

        /// <summary>
        /// Kiriş etriye çizicisinin gövde altında bıraktığı çift sıra etriye ölçüleri ve etriye yazıları
        /// temelde üst yüze göre aynalanır: ölçüler temel üst yüzünün hemen üstüne, yazılar onların üstüne.
        /// Yalnız etriye çağrısının oluşturduğu handle aralığı açılır; modelspace taranmaz.
        /// </summary>
        private void SurekliGorunusEtriyeOlculeriUste(Transaction tr, Database db, SurekliGorunusTakim takim,
            double y, List<(long A, long B)> araliklar)
        {
            var govde = takim.Temeller.SelectMany(t => t.Parcalar).Where(p => !p.Cikinti).ToList();
            if (govde.Count == 0 || araliklar.Count == 0) return;
            double alt = y + govde.Min(p => p.Alt);
            double ust = y + govde.Max(p => p.Ust);
            foreach (var (a, b) in araliklar)
                for (long h = a; h < b; h++)
                {
                    if (!db.TryGetObjectId(new Handle(h), out ObjectId id) || id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is RotatedDimension dim && dim.Layer == LayerOlcu)
                    {
                        double d = alt - dim.DimLinePoint.Y;
                        if (d < 0) continue; // yalnız gövde altındaki sıralar
                        dim.UpgradeOpen();
                        double yeni = ust + d;
                        dim.XLine1Point = new Point3d(dim.XLine1Point.X, ust, 0);
                        dim.XLine2Point = new Point3d(dim.XLine2Point.X, ust, 0);
                        dim.DimLinePoint = new Point3d(dim.DimLinePoint.X, yeni, 0);
                    }
                    else if (ent is DBText txt && txt.Layer == KirisAcilimDonatiYaziLayer)
                    {
                        bool hizali = txt.HorizontalMode != TextHorizontalMode.TextLeft || txt.VerticalMode != TextVerticalMode.TextBase;
                        double ref0 = hizali ? txt.AlignmentPoint.Y : txt.Position.Y;
                        double d = alt - ref0;
                        if (d < 0 || d > 45.0) continue; // açılım satırlarındaki yazılar yerinde kalır
                        // Yazı ortası alttaki uzaklığın aynası + 6 cm: iki ölçü sırasının üstünde kalır.
                        txt.UpgradeOpen();
                        txt.TransformBy(Matrix3d.Displacement(new Vector3d(0, ust + d + 6.0 - ref0, 0)));
                    }
                }
        }

        /// <summary>Birleşen ilave grubunda çizilmeyecek uçlar: (bay, üst mü, sağ uç mu).</summary>
        private readonly HashSet<(BeamInfo, bool, bool)> _surekliIlaveIptal = new HashSet<(BeamInfo, bool, bool)>();
        /// <summary>
        /// Büyük As'lı çubuğun yeni uçları (zincir X): Ic = kendi açıklığı yönü, Dis = komşu yönü;
        /// IcGonye/DisGonye: uç beton sınırında kesildi, mevcut gönye kuralıyla gönyelenir.
        /// </summary>
        private readonly Dictionary<(BeamInfo, bool, bool), (double? Ic, double? Dis, bool IcGonye, bool DisGonye)> _surekliIlaveUzat =
            new Dictionary<(BeamInfo, bool, bool), (double? Ic, double? Dis, bool IcGonye, bool DisGonye)>();

        /// <summary>
        /// Aynı yüzdeki komşu mesnet ilaveleri birbirine değiyor/biniyor ya da arada ≤10 cm kalıyorsa tek çubuk:
        /// grubun büyük As'lı (n·φ²) çubuğu çizilir ve grubun iki dış ucuna uzatılır, diğerleri çizilmez.
        /// Uç hesabı KirisAcilimIlaveDonati ile aynı (komşu açıklıkların büyüğünün ln/4'ü, karşı mesnet kesimi).
        /// Uç beton sınırını (temel ucu − 6 cm örtü) aşarsa orada kesilir ve gönyelenir (CK T5 SB-30 konsolu).
        /// Mesnet k'nın sahibi: k&gt;0 → bay k−1 sağ ucu; k=0 → bay 0 sol ucu (ortak mesnet tek çizim kuralı).
        /// </summary>
        private void SurekliTemelIlaveBirlestir(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            int n = chain.Count;
            if (n == 0) return;
            var f0 = new double[n]; var f1 = new double[n]; var ln = new double[n];
            for (int i = 0; i < n; i++)
            {
                KirisAcilimKolonYuzu(chain, i, i, sup, out f0[i], out f1[i]);
                ln[i] = Math.Max(0, f1[i] - f0[i]);
            }
            // Dış mesnetler beton ucuna kadar uzatılmış ve 0,5 cm içeri alınmıştır; çizici 5,5 cm daha örtü bırakır.
            double solSinir = chain[0].X0 + sup[0].X0 + KirisAcilimMontajPaspayiCm;
            double sagSinir = chain[n - 1].X1 + sup[n].X1 - KirisAcilimMontajPaspayiCm;
            double Gen(int k) => k >= 0 && k < sup.Length ? Math.Max(0, sup[k].X1 - sup[k].X0) : 0;
            foreach (bool ust in new[] { false, true })
            {
                var bars = new List<(int K, int Adet, int Cap, double L, double R, BeamInfo Sahip, bool Sag)>();
                for (int k = 0; k <= n; k++)
                {
                    BeamInfo sahip = k > 0 ? chain[k - 1].Beam : chain[0].Beam;
                    if (sahip == null || !donati.TryGetValue(sahip, out var m)) continue;
                    int adet = k > 0 ? (ust ? m.SagUstAdet : m.SagAltAdet) : (ust ? m.SolUstAdet : m.SolAltAdet);
                    int cap = k > 0 ? (ust ? m.SagUstCap : m.SagAltCap) : (ust ? m.SolUstCap : m.SolAltCap);
                    if (adet <= 0 || cap <= 0) continue;
                    // Her taraf kendi açıklığının max(ln/4, 50φ, ℓb)'si.
                    double sagUc = k < n ? f0[k] + SurekliTemelIlaveKarsiKes(KirisAcilimIlaveYuzdenCm(ln[k], 0, cap, ust), ln[k], Gen(k + 1), cap, ust) : f1[n - 1];
                    double solUc = k > 0 ? f1[k - 1] - SurekliTemelIlaveKarsiKes(KirisAcilimIlaveYuzdenCm(ln[k - 1], 0, cap, ust), ln[k - 1], Gen(k - 1), cap, ust) : f0[0];
                    bars.Add((k, adet, cap, solUc, sagUc, sahip, k > 0));
                }
                for (int g0 = 0; g0 < bars.Count;)
                {
                    int g1 = g0;
                    double sagMax = bars[g0].R;
                    while (g1 + 1 < bars.Count && bars[g1 + 1].K == bars[g1].K + 1 && bars[g1 + 1].L <= sagMax + 10.0)
                    {
                        g1++;
                        sagMax = Math.Max(sagMax, bars[g1].R);
                    }
                    var grup = bars.GetRange(g0, g1 - g0 + 1);
                    var buyuk = grup.OrderByDescending(b => (double)b.Adet * b.Cap * b.Cap).ThenBy(b => b.K).First();
                    double lg = grup.Min(b => b.L), rg = grup.Max(b => b.R);
                    bool solKes = lg < solSinir - 0.5, sagKes = rg > sagSinir + 0.5;
                    if (solKes) lg = solSinir;
                    if (sagKes) rg = sagSinir;
                    foreach (var b in grup)
                        if (b.K != buyuk.K) _surekliIlaveIptal.Add((b.Sahip, ust, b.Sag));
                    if (grup.Count > 1 || solKes || sagKes)
                    {
                        // Sağ uç sahibi: kendi açıklığı solda (Ic), komşu sağda (Dis); sol uç sahibi tersi.
                        bool solDegisti = solKes || lg < buyuk.L - 0.5, sagDegisti = sagKes || rg > buyuk.R + 0.5;
                        double? sol = solDegisti ? lg : (double?)null, sag = sagDegisti ? rg : (double?)null;
                        var uz = buyuk.Sag ? (sol, sag, solKes, sagKes) : (sag, sol, sagKes, solKes);
                        if (uz.Item1.HasValue || uz.Item2.HasValue) _surekliIlaveUzat[(buyuk.Sahip, ust, buyuk.Sag)] = uz;
                    }
                    g0 = g1 + 1;
                }
            }
        }

        /// <summary>KirisAcilimIlaveDonati içinden: birleşen gruptaki küçük uçları iptal eder, büyüğü uzatır/kesip gönyeler.</summary>
        private void SurekliTemelIlaveUygula(BeamInfo bay, double x0,
            ref int luAdet, ref int laAdet, ref int ruAdet, ref int raAdet,
            ref double xLu, ref double xLa, ref double xRu, ref double xRa,
            ref double xSolUst, ref double xSolAlt, ref double xSagUst, ref double xSagAlt,
            out (bool LuIc, bool LuDis, bool LaIc, bool LaDis, bool RuIc, bool RuDis, bool RaIc, bool RaDis) gonye)
        {
            gonye = default;
            if (bay == null) return;
            if (_surekliIlaveIptal.Contains((bay, true, false))) luAdet = 0;
            if (_surekliIlaveIptal.Contains((bay, false, false))) laAdet = 0;
            if (_surekliIlaveIptal.Contains((bay, true, true))) ruAdet = 0;
            if (_surekliIlaveIptal.Contains((bay, false, true))) raAdet = 0;
            if (_surekliIlaveUzat.TryGetValue((bay, true, false), out var su))
            { if (su.Ic.HasValue) xLu = x0 + su.Ic.Value; if (su.Dis.HasValue) xSolUst = x0 + su.Dis.Value; gonye.LuIc = su.IcGonye; gonye.LuDis = su.DisGonye; }
            if (_surekliIlaveUzat.TryGetValue((bay, false, false), out var sa))
            { if (sa.Ic.HasValue) xLa = x0 + sa.Ic.Value; if (sa.Dis.HasValue) xSolAlt = x0 + sa.Dis.Value; gonye.LaIc = sa.IcGonye; gonye.LaDis = sa.DisGonye; }
            if (_surekliIlaveUzat.TryGetValue((bay, true, true), out var ru))
            { if (ru.Ic.HasValue) xRu = x0 + ru.Ic.Value; if (ru.Dis.HasValue) xSagUst = x0 + ru.Dis.Value; gonye.RuIc = ru.IcGonye; gonye.RuDis = ru.DisGonye; }
            if (_surekliIlaveUzat.TryGetValue((bay, false, true), out var ra))
            { if (ra.Ic.HasValue) xRa = x0 + ra.Ic.Value; if (ra.Dis.HasValue) xSagAlt = x0 + ra.Dis.Value; gonye.RaIc = ra.IcGonye; gonye.RaDis = ra.DisGonye; }
        }

        /// <summary>Kısa açıklıkta sürekli takım tarafından karşılandığı için düzü çizilmeyen baylar.</summary>
        private readonly HashSet<BeamInfo> _surekliDuzIptal = new HashSet<BeamInfo>();
        /// <summary>Düz çubuk ucunun zorunlu yeri (zincir X): (bay, sağ uç mu) → x.</summary>
        private readonly Dictionary<(BeamInfo, bool), double> _surekliDuzUc = new Dictionary<(BeamInfo, bool), double>();

        /// <summary>
        /// Kısa açıklık düz yoğunluğu (kullanıcı seçeneği A): iki komşunun düzü yüzden ℓb uzantısıyla bu açıklıkta
        /// çakışıyorsa (ℓb_sol + ℓb_sağ ≥ ℓn), sol komşu/açıklık/sağ komşu düzlerinden As'ı en büyük takım kısa açıklığı
        /// boydan boya geçer ve iki yandaki açıklıklara ℓb girer; diğer komşu kısa açıklığa girmeden kendi yüzünde biter
        /// (sürekli takımla kendi açıklığında ℓb bindirir); açıklığın kendi düzü sürekli takım değilse çizilmez.
        /// </summary>
        private void SurekliTemelDuzKisaAciklik(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            int n = chain.Count;
            if (n < 3) return;
            var f0 = new double[n]; var f1 = new double[n];
            var adet = new int[n]; var cap = new int[n]; var lb = new double[n];
            for (int i = 0; i < n; i++)
            {
                KirisAcilimKolonYuzu(chain, i, i, sup, out f0[i], out f1[i]);
                if (chain[i].Beam != null && donati.TryGetValue(chain[i].Beam, out var m) && m.DuzAdet > 0 && m.DuzCap > 0)
                { adet[i] = m.DuzAdet; cap[i] = m.DuzCap; lb[i] = KirisAcilimYuvarlaYukari(_lbPhiKonumI * m.DuzCap / 10.0); }
            }
            double As(int i) => (double)adet[i] * cap[i] * cap[i];
            // Çizilecek gerçek boy: temel ucunda örtü 6 + gönye, iç mesnette yüzden ℓb ya da (SurekliTemelDuzMesnetBindirme)
            // mesnet ortasından max ℓb/2; yüzde sabit biten komşuyla max ℓb bindirme. 5 cm'ye yuvarlanmış boy 1200'ü geçemez.
            const double ortu = 6.0, maxBoy = 1200.0;
            double uc0 = chain[0].X0 + sup[0].X0 - 0.5 + ortu, ucN = chain[n - 1].X1 + sup[n].X1 + 0.5 - ortu;
            double Gonye(int k) => Math.Max(0, Math.Min(KirisAcilimUstIlavePhi * cap[k] / 10.0, chain[k].Beam.HeightCm - 2 * ortu));
            double Lap(int a, int b) => Math.Max(lb[a], lb[b]);
            double Bas(int k) => k > 0 ? Math.Max(chain[k - 1].X0, Math.Min(f0[k] - lb[k], 0.5 * (f1[k - 1] + f0[k]) - 0.5 * Lap(k - 1, k))) : uc0;
            double Son(int k) => k + 1 < n ? Math.Min(chain[k + 1].X1, Math.Max(f1[k] + lb[k], 0.5 * (f1[k] + f0[k + 1]) + 0.5 * Lap(k, k + 1))) : ucN;
            for (int i = 1; i + 1 < n; i++)
            {
                double ln = f1[i] - f0[i];
                if (adet[i - 1] <= 0 || adet[i + 1] <= 0 || ln <= 0 || lb[i - 1] + lb[i + 1] < ln) continue;
                double Boy(int k)
                {
                    double l = k == i - 1 ? Bas(k) : f1[i - 1] - Lap(k, i - 1);
                    double r = k == i + 1 ? Son(k) : Math.Min(chain[i + 1].X1, f0[i + 1] + Lap(k, i + 1));
                    double b = r - l + (k == 0 ? Gonye(k) : 0) + (k == n - 1 ? Gonye(k) : 0);
                    return Math.Ceiling(b / 5.0 - 1e-9) * 5.0;
                }
                bool Sigar(int k) => adet[k] > 0 && Boy(k) <= maxBoy + 0.5;
                // As eşitse (≤%1 fark) sürekli takım, uzayınca daha kısa kalan taraf (12 m çubuk boyu).
                int s = i;
                foreach (int k in new[] { i - 1, i + 1 })
                {
                    if (As(k) > As(s) * 1.01) s = k;
                    else if (As(k) >= As(s) * 0.99 && Boy(k) < Boy(s)) s = k;
                }
                if (As(s) < As(i)) s = i;
                if (!Sigar(s))
                {
                    // Sürekli takım 1200'ü aşıyor: kısa açıklık As'ını karşılayan (≥ As(i)) ve sığan en büyük As'lı aday.
                    int eski = s; s = -1;
                    foreach (int k in new[] { i - 1, i + 1, i })
                    {
                        if (!Sigar(k) || As(k) < As(i) * 0.99) continue;
                        if (s < 0 || As(k) > As(s) * 1.01 || (As(k) >= As(s) * 0.99 && Boy(k) < Boy(s))) s = k;
                    }
                    KirisAcilimTani(string.Format(CultureInfo.InvariantCulture,
                        "KISA_ACIKLIK_1200 i={0} eski={1} boy={2:0} yeni={3} boy={4:0}", i, eski - i, Boy(eski), s < 0 ? "yok" : (s - i).ToString(CultureInfo.InvariantCulture), s < 0 ? 0 : Boy(s)));
                    if (s < 0) continue;
                }
                BeamInfo sol = chain[i - 1].Beam, orta = chain[i].Beam, sag = chain[i + 1].Beam;
                if (s != i) _surekliDuzIptal.Add(orta);
                if (s == i - 1)
                {
                    // Sol komşu sürekli: kısa açıklığı geçer, sağ komşuya ℓb girer; sağ komşu kendi sol yüzünde başlar.
                    _surekliDuzUc[(sol, true)] = Math.Min(chain[i + 1].X1, f0[i + 1] + lb[s]);
                    _surekliDuzUc[(sag, false)] = f0[i + 1];
                }
                else if (s == i + 1)
                {
                    _surekliDuzUc[(sag, false)] = Math.Max(chain[i - 1].X0, f1[i - 1] - lb[s]);
                    _surekliDuzUc[(sol, true)] = f1[i - 1];
                }
                else
                {
                    // Açıklığın kendi düzü sürekli (mevcut ℓb uzantısı); komşular kısa açıklığa girmez.
                    _surekliDuzUc[(sol, true)] = f1[i - 1];
                    _surekliDuzUc[(sag, false)] = f0[i + 1];
                }
            }
        }

        /// <summary>Birleşik düz grubu: grubun ilk bayı → (son bay indeksi, adet, çap). Diğer baylar <see cref="_surekliDuzGrupIc"/>.</summary>
        private readonly Dictionary<BeamInfo, (int Son, int Adet, int Cap)> _surekliDuzGrup = new Dictionary<BeamInfo, (int, int, int)>();
        private readonly HashSet<BeamInfo> _surekliDuzGrupIc = new HashSet<BeamInfo>();

        /// <summary>
        /// Aynı adet ve çaptaki komşu açıklık düzleri tek çubukta birleşir (uçlarda komşuya ℓb, temel ucunda gönye).
        /// Birleşik çubuk gönyesiyle 1200 cm'yi geçmez ve ağırlığı (adet·Ø²·boy) ayrı çubukların (her biri komşuya ℓb
        /// bindirmeli) toplamını aşmaz; aynı takımda bindirmeler kalktığı için hep azalır.
        /// Kısa açıklık kuralının yerini belirlediği uç grubun dış ucu olabilir, iç birleşim olamaz; çizilmeyen baylar ve
        /// yükseklik/kot değişen geçişler birleşmez.
        /// </summary>
        private void SurekliTemelDuzBirlestir(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            _surekliDuzGrup.Clear();
            _surekliDuzGrupIc.Clear();
            int n = chain.Count;
            if (n < 2) return;
            var f0 = new double[n]; var f1 = new double[n];
            var adet = new int[n]; var cap = new int[n];
            var uygun = new bool[n];
            for (int i = 0; i < n; i++)
            {
                KirisAcilimKolonYuzu(chain, i, i, sup, out f0[i], out f1[i]);
                var b = chain[i].Beam;
                if (b == null || !donati.TryGetValue(b, out var m) || m.DuzAdet <= 0 || m.DuzCap <= 0) continue;
                adet[i] = m.DuzAdet; cap[i] = m.DuzCap;
                uygun[i] = !_surekliDuzIptal.Contains(b);
            }
            // Kısa açıklık kuralının yeri belirlediği uç (SurekliTemelDuzKisaAciklik) grubun içinde kalamaz; diğer ucu birleşebilir
            // (CK T-5: kısa açıklık sağ komşusu 14ø14 L=760 sağdaki 14ø14 L=250 ile).
            double? UcSabit(int k, bool sag) => _surekliDuzUc.TryGetValue((chain[k].Beam, sag), out double x) ? x : (double?)null;
            const double ortu = 6.0, maxBoy = 1200.0;
            double solUc = chain[0].X0 + sup[0].X0 - 0.5 + ortu, sagUc = chain[n - 1].X1 + sup[n].X1 + 0.5 - ortu;
            double Lb(int c) => KirisAcilimYuvarlaYukari(_lbPhiKonumI * c / 10.0);
            double Gonye(int k, int c) => Math.Max(0, Math.Min(KirisAcilimUstIlavePhi * c / 10.0, chain[k].Beam.HeightCm - 2 * ortu));
            // Mesnette komşu düzle en az ℓb bindirme (SurekliTemelDuzMesnetBindirme): uç mesnet ortasından ℓb/2 öteye kadar.
            double LbKomsu(int k, int c) => Math.Max(Lb(c), cap[k] > 0 ? Lb(cap[k]) : 0);
            // [g0, g1] açıklıklarını c çaplı tek çubuk kaplarsa boyu (gönyeler dahil).
            double Boy(int g0, int g1, int c)
            {
                double sol = UcSabit(g0, false) ?? (g0 > 0 ? Math.Max(chain[g0 - 1].X0, Math.Min(f0[g0] - Lb(c), 0.5 * (f1[g0 - 1] + f0[g0]) - 0.5 * LbKomsu(g0 - 1, c))) : solUc);
                double sag = UcSabit(g1, true) ?? (g1 + 1 < n ? Math.Min(chain[g1 + 1].X1, Math.Max(f1[g1] + Lb(c), 0.5 * (f1[g1] + f0[g1 + 1]) + 0.5 * LbKomsu(g1 + 1, c))) : sagUc);
                return sag - sol + (g0 == 0 ? Gonye(0, c) : 0) + (g1 == n - 1 ? Gonye(n - 1, c) : 0);
            }
            double As(int k) => (double)adet[k] * cap[k] * cap[k];
            bool AyniKesit(int a, int b) => Math.Abs(chain[a].Beam.HeightCm - chain[b].Beam.HeightCm) < 0.5
                && Math.Abs(chain[a].Kot1 - chain[b].Kot0) < 2.0;
            int Secim(int g0, int g1)
            {
                int s = g0;
                for (int k = g0 + 1; k <= g1; k++) if (As(k) > As(s) * 1.001) s = k;
                return s;
            }
            bool Birlesir(int g0, int g1)
            {
                int s = Secim(g0, g1);
                double boy = Boy(g0, g1, cap[s]);
                if (boy > maxBoy + 0.5) return false;
                double ayri = 0;
                for (int k = g0; k <= g1; k++) ayri += As(k) * Boy(k, k, cap[k]);
                return As(s) * boy <= ayri + 1e-6;
            }
            for (int g0 = 0; g0 < n;)
            {
                if (!uygun[g0]) { g0++; continue; }
                int g1 = g0;
                while (g1 + 1 < n && uygun[g1 + 1] && !UcSabit(g1, true).HasValue && !UcSabit(g1 + 1, false).HasValue
                    && adet[g1 + 1] == adet[g0] && cap[g1 + 1] == cap[g0]
                    && AyniKesit(g1, g1 + 1) && Birlesir(g0, g1 + 1)) g1++;
                if (g1 > g0)
                {
                    int s = Secim(g0, g1);
                    _surekliDuzGrup[chain[g0].Beam] = (g1, adet[s], cap[s]);
                    for (int k = g0 + 1; k <= g1; k++) _surekliDuzGrupIc.Add(chain[k].Beam);
                }
                g0 = g1 + 1;
            }
        }

        /// <summary>Mesnette ℓb bindirme için düz çubuk ucunun yeni yeri (zincir X): (bay, sağ uç mu) → x. Tek bay ve birleşik grup uçları.</summary>
        private readonly Dictionary<(BeamInfo, bool), double> _surekliDuzMesnetUc = new Dictionary<(BeamInfo, bool), double>();

        /// <summary>
        /// İç mesnette iki yanın düz çubukları ayrıysa veya ℓb'den (iki çapın büyüğü, konum I) az biniyorsa uçlar uzatılır:
        /// yüzünde bitmesi kısa açıklık kuralıyla sabitlenmiş uç yerinde kalır, diğeri uzar; ikisi de serbestse eşit paylaşılır.
        /// Uçlar komşu açıklığın karşı mesnet eksenini geçmez. Çizimdeki uç kuralıyla (yüzden ℓb, kısa açıklık, birleşik grup) aynı hesap.
        /// </summary>
        private void SurekliTemelDuzMesnetBindirme(List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            _surekliDuzMesnetUc.Clear();
            int n = chain.Count;
            if (n < 2) return;
            var f0 = new double[n]; var f1 = new double[n];
            for (int i = 0; i < n; i++) KirisAcilimKolonYuzu(chain, i, i, sup, out f0[i], out f1[i]);
            double Lb(int c) => KirisAcilimYuvarlaYukari(_lbPhiKonumI * c / 10.0);
            var bars = new List<(int I0, int I1, int Cap, double Lo, double Hi, bool SolSabit, bool SagSabit)>();
            for (int i = 0; i < n; i++)
            {
                var b = chain[i].Beam;
                if (b == null || _surekliDuzIptal.Contains(b) || _surekliDuzGrupIc.Contains(b)) continue;
                if (!donati.TryGetValue(b, out var m) || m.DuzAdet <= 0 || m.DuzCap <= 0) continue;
                int i1 = i, c = m.DuzCap;
                if (_surekliDuzGrup.TryGetValue(b, out var g)) { i1 = g.Son; c = g.Cap; }
                double lo = i > 0 ? Math.Max(chain[i - 1].X0, f0[i] - Lb(c)) : double.NegativeInfinity;
                double hi = i1 + 1 < n ? Math.Min(chain[i1 + 1].X1, f1[i1] + Lb(c)) : double.PositiveInfinity;
                bool solSabit = false, sagSabit = false;
                if (_surekliDuzUc.TryGetValue((b, false), out double u0)) { lo = u0; solSabit = Math.Abs(u0 - f0[i]) < 0.5; }
                if (_surekliDuzUc.TryGetValue((chain[i1].Beam, true), out double u1)) { hi = u1; sagSabit = Math.Abs(u1 - f1[i1]) < 0.5; }
                bars.Add((i, i1, c, lo, hi, solSabit, sagSabit));
            }
            for (int k = 1; k < n; k++)
            {
                double fL = f1[k - 1], fR = f0[k];
                // Sol: mesnede soldan en uzaktan gelen çubuk; sağ: sağ yüzden önce başlayıp ondan daha sağa devam eden.
                // (Mesnedi geçen kısa açıklık çubuğu, yüzde biten komşusuyla eşlenir.)
                int l = -1, r = -1;
                for (int j = 0; j < bars.Count; j++)
                    if (bars[j].Lo < fL - 1 && bars[j].Hi > fL - 1 && (l < 0 || bars[j].Lo < bars[l].Lo)) l = j;
                if (l < 0) continue;
                for (int j = 0; j < bars.Count; j++)
                    if (j != l && bars[j].Lo > bars[l].Lo && bars[j].Lo < fR + 1 && bars[j].Hi > Math.Max(bars[l].Hi, fR + 1)
                        && (r < 0 || bars[j].Lo < bars[r].Lo)) r = j;
                if (r < 0) continue;
                var L = bars[l]; var R = bars[r];
                double lap = Math.Max(Lb(L.Cap), Lb(R.Cap));
                double eksik = lap - (L.Hi - R.Lo);
                if (eksik <= 0.5) continue;
                double maxHi = chain[Math.Min(n - 1, L.I1 + 1)].X1, minLo = chain[Math.Max(0, R.I0 - 1)].X0;
                double payL = L.SagSabit && !R.SolSabit ? 0 : R.SolSabit && !L.SagSabit ? eksik : 0.5 * eksik;
                double yeniHi = Math.Min(maxHi, L.Hi + payL);
                double yeniLo = Math.Max(minLo, R.Lo - (eksik - (yeniHi - L.Hi)));
                if (yeniHi - yeniLo < lap - 0.5) yeniHi = Math.Min(maxHi, yeniLo + lap);
                KirisAcilimTani(string.Format(CultureInfo.InvariantCulture,
                    "DUZ_MESNET_BINDIRME k={0} yuz={1:0.#}~{2:0.#} lb={3:0.#} once={4:0.#} sonra={5:0.#}", k, fL, fR, lap, L.Hi - R.Lo, yeniHi - yeniLo));
                if (yeniHi > L.Hi + 0.01) { _surekliDuzMesnetUc[(chain[L.I1].Beam, true)] = yeniHi; L.Hi = yeniHi; bars[l] = L; }
                if (yeniLo < R.Lo - 0.01) { _surekliDuzMesnetUc[(chain[R.I0].Beam, false)] = yeniLo; R.Lo = yeniLo; bars[r] = R; }
            }
        }

        /// <summary>Gövde çubuğunca yutulan temel bayları (BeamId tüm baylarda aynı olduğundan nesne ile).</summary>
        private readonly HashSet<BeamInfo> _surekliGovdeYutulanBay = new HashSet<BeamInfo>();

        // Beton çizicisine dokunmadan, aynı kolon istasyonlarından donatı açıklıkları.
        private List<List<KirisAcilimSpan>> BuildSurekliGorunusDonatiChains(SurekliGorunusTakim takim,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            var spans = new List<KirisAcilimSpan>();
            double baseCm = (_model.BuildingBaseKotu + (SurekliTemelDestekKati()?.ElevationM ?? 0)) * 100.0;
            foreach (var t in takim.Temeller)
            {
                var parent = _surekliTemelBeams.FirstOrDefault(b => _surekliTemelCfByBeam[b] == t.Kaynak);
                if (parent == null) continue;
                var columns = SurekliGorunusMesnetleri(takim, t);
                var local = new List<KirisAcilimSpan>();
                bool forward = t.Yon.DotProduct(t.GorunusYonu) > 0;
                for (int i = 1; i < columns.Count; i++)
                {
                    var a = columns[i - 1]; var b = columns[i];
                    double x0 = (a.X0 + a.X1) * 0.5, x1 = (b.X0 + b.X1) * 0.5;
                    if (x1 - x0 < 1) continue;
                    var ca = a.Plan.Centroid.Coordinate; var cb = b.Plan.Centroid.Coordinate;
                    local.Add(new KirisAcilimSpan { Beam = parent, X0 = x0, X1 = x1,
                        Kot0 = t.Parcalar[0].Ust - baseCm, Kot1 = t.Parcalar[0].Ust - baseCm,
                        World0 = new Point2d(ca.X, ca.Y), World1 = new Point2d(cb.X, cb.Y), Dir = t.GorunusYonu,
                        Cross0 = forward ? parent.StartAxisId : parent.EndAxisId,
                        Cross1 = forward ? parent.EndAxisId : parent.StartAxisId });
                }
                // TMX/GPR sırası kaynak başlangıcından gider; görünüş tersse kayıt
                // atamasını kaynak sırasında yap, ardından açılım sırasını geri kur.
                if (!forward) local.Reverse();
                SurekliTemelChainsBayDonatiAta(new List<List<KirisAcilimSpan>> { local }, donati);
                if (!SurekliTemelGeometrikAta(local, columns, t, parent, donati))
                    SurekliTemelIlaveMesneteAta(local, parent, donati, !forward);
                if (!forward) local.Reverse();
                spans.AddRange(local);
            }
            var chains = new List<List<KirisAcilimSpan>>();
            foreach (var span in spans.OrderBy(s => s.X0).ThenBy(s => s.X1))
            {
                var last = chains.LastOrDefault();
                if (last == null || Math.Abs(last[last.Count - 1].X1 - span.X0) > 0.1)
                    chains.Add(last = new List<KirisAcilimSpan>());
                last.Add(span);
            }
            return chains;
        }

        private void DrawSurekliGorunusDonati(Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            SurekliGorunusTakim takim, double x, double y, Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati)
        {
            var chains = BuildSurekliGorunusDonatiChains(takim, donati);
            KirisKesitMontajHazirla(donati);
            KirisAcilimKesitTakimBaslat();
            // Etriye ölçü/yazısı ve temel adı üstte; izdüşüm ve açılım satırları 50 cm (1/25 nihai 100 cm) yukarıda.
            _kirisAcilimKesimY = y + takim.Temeller.Min(t => t.Parcalar[0].Alt) - 15;
            double baseCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;
            double yGorunus = y;
            var etriyeAraliklari = new List<(long A, long B)>();
            y += baseCm; // Mevcut donatı/kesit çizicileri kat referanslı Kot bekler.
            _kirisAcilimIlaveSira = _kirisAcilimMontajSira = _kirisAcilimGovdeSira = _kirisAcilimDuzSira = 0;
            _kirisAcilimSatirSayaci.Clear();
            _kirisGorunusEtiketKutulari.Clear();
            _kirisAcilimIzdusumXs.Clear();
            _kirisAcilimMontajBindirmeXs.Clear();
            // Kirişteki gibi mesnet (kolon ve saplanılan temel) yüzlerinden aşağı izdüşüm.
            double izUst = _kirisAcilimKesimY - 10.0;
            // Dağıtma açılımı varsa izdüşüm onun satırına kadar 25 (1/25'te 50 cm) uzar.
            double izBoy = KirisAcilimIzdusumBoyCm
                + (_surekliDagitmaCap > 0 && SurekliTemelAmpatmanAraliklari(takim, x).Count > 0 ? 25.0 : 0.0);
            foreach (var face in takim.Kolonlar.SelectMany(c => new[] { c.X0, c.X1 })
                .Concat(takim.Saplamalar.SelectMany(s => new[] { s.X0, s.X1 })).OrderBy(f => f))
            {
                if (_kirisAcilimIzdusumXs.Any(f => Math.Abs(f - x - face) < 0.5)) continue;
                _kirisAcilimIzdusumXs.Add(x + face);
                KirisAcilimCizgi(tr, ms, x + face, izUst, x + face, izUst - izBoy, LayerIzdusum);
            }
            var uc = new Dictionary<BeamInfo, KirisAcilimUcBilgi>();
            var gorunus = new List<KirisAcilimSaplama>();
            var yutulan = new HashSet<int>();
            _surekliGovdeYutulanBay.Clear();
            var mesnetCizilen = new HashSet<long>();
            foreach (var chain in chains)
            {
                int count = chain.Count;
                var sup = new KirisAcilimSupport[count + 1];
                for (int i = 0; i <= count; i++)
                {
                    double station = i < count ? chain[i].X0 : chain[count - 1].X1;
                    var col = takim.Kolonlar.FirstOrDefault(c => Math.Abs((c.X0 + c.X1) * 0.5 - station) < 0.1);
                    if (col != null) sup[i] = new KirisAcilimSupport { X0 = col.X0 - station, X1 = col.X1 - station,
                        ColumnNo = col.Kaynak.ColumnNo, ExtendUp = true };
                    else
                    {
                        // Saplanılan temel: genişliği kadar mesnet, kolon gibi yukarı uzamaz.
                        var sap = takim.Saplamalar.FirstOrDefault(s => Math.Abs((s.X0 + s.X1) * 0.5 - station) < 0.1);
                        if (sap != null) sup[i] = new KirisAcilimSupport { X0 = sap.X0 - station, X1 = sap.X1 - station };
                    }
                }
                // Dış gönyeler onaylı beton ucundan paspayı içeride biter;
                // iç mesnet yüzleri ve net açıklıklar gerçek kolon yüzünde kalır.
                var left = takim.Temeller.First(t => t.Kaynak == _surekliTemelCfByBeam[chain[0].Beam]);
                var right = takim.Temeller.First(t => t.Kaynak == _surekliTemelCfByBeam[chain[count - 1].Beam]);
                SurekliGorunusParcaAraligi(left, left.Parcalar[0].Plan, out double lo, out _);
                SurekliGorunusParcaAraligi(right, right.Parcalar[0].Plan, out _, out double hi);
                sup[0].X0 = Math.Min(sup[0].X0, lo - chain[0].X0);
                sup[count].X1 = Math.Max(sup[count].X1, hi - chain[count - 1].X1);
                // Mevcut çizici dış mesnetten 5.5 cm alır; temel ucu 6 cm olsun.
                sup[0].X0 += 0.5; sup[count].X1 -= 0.5;
                _surekliIlaveIptal.Clear();
                _surekliIlaveUzat.Clear();
                SurekliTemelIlaveBirlestir(chain, sup, donati);
                _surekliDuzIptal.Clear();
                _surekliDuzUc.Clear();
                SurekliTemelDuzKisaAciklik(chain, sup, donati);
                SurekliTemelDuzBirlestir(chain, sup, donati);
                SurekliTemelDuzMesnetBindirme(chain, sup, donati);
                bool hasData = false;
                using (new SurekliTemelOrtuUyarlamasi(chain, 0.5))
                {
                for (int i = 0; i < count; i++)
                {
                    var s = chain[i];
                    if (!donati.TryGetValue(s.Beam, out var data)
                        || (data.Adet <= 0 && data.DuzAdet <= 0 && data.EtriyeCap <= 0)) continue;
                    hasData = true;
                    KirisAcilimKolonYuzu(chain, i, i, sup, out double net0, out double net1);
                    long etA = KirisYerlesimSonrakiHandle(db);
                    using (new SurekliTemelOrtuUyarlamasi(chain, 0.5, shrinkWidth: true))
                        KirisAcilimEtriyeGorunus(tr, ms, db, chain, i, i, x, y, net0, net1, donati, uc, gorunus, gorunus, sup);
                    etriyeAraliklari.Add((etA, KirisYerlesimSonrakiHandle(db)));
                    if (_surekliDuzGrup.TryGetValue(s.Beam, out var grup))
                    {
                        // Birleşik düz: grup tek çağrıda, büyük As takımıyla (geçici olarak ilk bayın düzü).
                        KirisAcilimKolonYuzu(chain, i, grup.Son, sup, out double g0, out double g1);
                        var asil = donati[s.Beam];
                        var gecici = asil; gecici.DuzAdet = grup.Adet; gecici.DuzCap = grup.Cap;
                        donati[s.Beam] = gecici;
                        try { KirisAcilimDuzDonatiYedek(tr, ms, db, chain, i, grup.Son, x, y, g0, g1, donati, sup, gorunus, uc); }
                        finally { donati[s.Beam] = asil; }
                    }
                    else if (!_surekliDuzGrupIc.Contains(s.Beam))
                        KirisAcilimDuzDonatiYedek(tr, ms, db, chain, i, i, x, y, net0, net1, donati, sup, gorunus, uc);
                    KirisAcilimGovdeDonati(tr, ms, db, chain, i, i, x, y, net0, net1, donati, sup, gorunus, yutulan);
                    KirisAcilimIlaveDonati(tr, ms, db, chain, i, i, x, y, net0, net1, donati, sup, gorunus, uc);
                }
                if (hasData) KirisAcilimMontajBirlesik(tr, ms, db, chain, x, y, sup, donati, gorunus, uc);
                if (hasData)
                {
                    long mA = KirisYerlesimSonrakiHandle(db);
                    using (new SurekliTemelOrtuUyarlamasi(chain, 0.5, shrinkWidth: true))
                        SurekliTemelMesnetEtriye(tr, ms, db, chain, sup, x, y, donati, mesnetCizilen);
                    etriyeAraliklari.Add((mA, KirisYerlesimSonrakiHandle(db)));
                }
                }
                if (hasData) KirisKesitMesnetBindirmeTani(chain, sup, x);
                // Kiriş açılımındaki gibi klasik kot: temel üst yüzünde, her temel kirişinin Ln·3/4'ünde;
                // üst yüze taşınacak etriye/mesnet ölçülerinin uzatma çizgilerinden en az 15 cm (nihai) uzakta.
                var olcuX = new List<double>();
                foreach (var (a, b) in etriyeAraliklari)
                    for (long h = a; h < b; h++)
                        if (db.TryGetObjectId(new Handle(h), out ObjectId oid) && !oid.IsErased
                            && tr.GetObject(oid, OpenMode.ForRead) is RotatedDimension od && od.Layer == LayerOlcu)
                        { olcuX.Add(od.XLine1Point.X); olcuX.Add(od.XLine2Point.X); }
                KirisAcilimKotlariCiz(tr, ms, db, floor, chain, sup, gorunus, x, y, herAciklik: true, engelX: olcuX);
                if (!hasData) continue;
                // Her kolon arası ayrı kesit; aynı imzalı kesitler çizimde birleşir.
                for (int i = 0; i < count; i++)
                    KirisAcilimKesitTopla(floor, new List<KirisAcilimSpan> { chain[i] },
                        new[] { sup[i], sup[i + 1] }, gorunus, donati, x, y, 1, uc);
            }
            SurekliGorunusEtriyeOlculeriUste(tr, db, takim, yGorunus, etriyeAraliklari);
            KirisAcilimMontajBindirmeOlcuCiz(tr, ms, db);
            double rightEdge = takim.Temeller.Max(t => {
                SurekliGorunusParcaAraligi(t, t.Parcalar[0].Plan, out _, out double hi); return hi;
            });
            SurekliTemelAmpatmanAdetAta(takim, chains, x);
            KirisAcilimKesitleriCiz(tr, ms, db, x + rightEdge + 80, y + takim.Temeller.Max(t => t.Parcalar[0].Ust) - baseCm);
            SurekliTemelDagitmaAcilim(tr, ms, db, takim, x);
        }

        /// <summary>Kolon/hatıl/perde çıkıntısı temel üstünden (2× öncesi; 1/25'te 75 cm). Mesnet etriye yazısı içine sığar.</summary>
        private const double SurekliGorunusCikintiCm = 37.5;

        /// <summary>
        /// Mesnet içi etriyeleri: kolon/saplama mesnedinde yüzlerden 5 cm içeriden (temel ucunda uçtan 5 cm) eşit aralıkla,
        /// aralık komşu açıklıkların orta aralığı (küçüğü; sıklaştırma değil), çap büyüğü. Adet yazısı açıklık etriye
        /// yazılarıyla aynı yükseklikte mesnet ortasında. Ortak mesnet takımda bir kez çizilir.
        /// </summary>
        private void SurekliTemelMesnetEtriye(Transaction tr, BlockTableRecord ms, Database db, List<KirisAcilimSpan> chain,
            KirisAcilimSupport[] sup, double x, double y, Dictionary<BeamInfo, KirisAcilimMontajBilgi> donati,
            HashSet<long> cizilen)
        {
            int n = chain.Count;
            // Açıklık etriye yazısıyla aynı: gövde altında (alt − 27, kutu üstü alt − 22) çizilir,
            // SurekliGorunusEtriyeOlculeriUste aynı kuralla gövde üstüne taşır.
            double olcuAlt = double.MaxValue;
            foreach (var c in chain) olcuAlt = Math.Min(olcuAlt, y + Math.Min(c.Kot0, c.Kot1) - c.Beam.HeightCm);
            for (int i = 0; i <= n; i++)
            {
                var s = sup[i];
                // Kolon/saplama mesnedi istasyonun iki yanına taşar; desteksiz serbest uçta X1 = 0 kalır.
                if (s.X0 > -0.5 || s.X1 < 0.5) continue;
                double station = i < n ? chain[i].X0 : chain[n - 1].X1;
                double lo = station + s.X0 + (i == 0 ? 4.5 : 5.0), hi = station + s.X1 - (i == n ? 4.5 : 5.0);
                if (hi - lo < 1.0 || !cizilen.Add((long)Math.Round(x + 0.5 * (lo + hi)))) continue;
                int cap = 0, aralik = 0;
                KirisAcilimSpan span = chain[Math.Min(i, n - 1)];
                foreach (int j in new[] { i - 1, i })
                {
                    if (j < 0 || j >= n || donati == null || !donati.TryGetValue(chain[j].Beam, out var m)) continue;
                    if (m.EtriyeCap <= 0 || m.EtriyeOrtaCm <= 0) continue;
                    cap = Math.Max(cap, m.EtriyeCap);
                    aralik = aralik == 0 ? m.EtriyeOrtaCm : Math.Min(aralik, m.EtriyeOrtaCm);
                }
                if (cap <= 0 || aralik <= 0) continue;
                int adim = Math.Max(1, (int)Math.Ceiling((hi - lo) / aralik - 1e-9));
                double h = span.Beam.HeightCm;
                for (int k = 0; k <= adim; k++)
                {
                    double xe = lo + (hi - lo) * k / adim;
                    double top = y + KirisAcilimKotAt(chain, 0, n - 1, xe);
                    if (h > 8.0) KirisAcilimCizgi(tr, ms, x + xe, top - h + 4.0, x + xe, top - 4.0, LayerEtriye);
                }
                var kutular = KirisKesitEtriyeKutulari(span.Beam.WidthCm);
                if (kutular.Count == 0 || db == null) continue;
                double boy = EtriyeAcilimKesimBoyCm(kutular[0].Hi - kutular[0].Lo, h - 8.0, Math.Max(10.0, EtriyeAcilimKancaCm(cap)));
                string label = KirisKesitEtriyeCarpan(kutular.Count) + string.Format(CultureInfo.InvariantCulture,
                    "{0}ø{1}/{2} L={3:0}", adim + 1, cap, aralik, boy);
                DrawBeamLabel(tr, ms, db, new Point3d(x + 0.5 * (lo + hi), olcuAlt - 27.0, 0), label, 5.0, 0.0,
                    KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                KirisAcilimYaziKutusuKenar(tr, olcuAlt - 22.0, ustKenar: true);
            }
        }

        /// <summary>Takımda ampatmanlı temellerin görünüş aralıkları (dünya X), değen/binenler birleşik.</summary>
        private static List<(double Lo, double Hi)> SurekliTemelAmpatmanAraliklari(SurekliGorunusTakim takim, double x)
        {
            var araliklar = new List<(double Lo, double Hi)>();
            foreach (var temel in takim.Temeller)
            {
                var cf = temel.Kaynak;
                if (cf == null || cf.AmpatmanWidthCm <= cf.WidthCm + 0.1 || cf.AmpatmanHeightCm <= 0.1) continue;
                foreach (var p in temel.Parcalar.Where(p => !p.Cikinti))
                {
                    SurekliGorunusParcaAraligi(temel, p.Plan, out double lo, out double hi);
                    if (hi - lo > 1.0) araliklar.Add((x + lo, x + hi));
                }
            }
            araliklar.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            var birlesik = new List<(double Lo, double Hi)>();
            foreach (var a in araliklar)
            {
                if (birlesik.Count > 0 && a.Lo <= birlesik[birlesik.Count - 1].Hi + 1.0)
                {
                    var son = birlesik[birlesik.Count - 1];
                    birlesik[birlesik.Count - 1] = (son.Lo, Math.Max(son.Hi, a.Hi));
                }
                else birlesik.Add(a);
            }
            return birlesik;
        }

        /// <summary>
        /// Ampatman U adedi: ampatman boyunca (uçlarda 5 cm, mesnetler dahil) eşit aralıkla (≤ kesitteki aralık) dizilen
        /// U'lar, bulundukları açıklığın (kolon merkezinden kolon merkezine) kesitine sayılır; açıklık dışı uçtaki U en yakın kesite.
        /// </summary>
        private void SurekliTemelAmpatmanAdetAta(SurekliGorunusTakim takim, List<List<KirisAcilimSpan>> chains, double x)
        {
            var kesitler = _kirisKesitler.Where(k => k.AmpCap > 0 && k.AmpAralik > 0).ToList();
            foreach (var k in _kirisKesitler) k.AmpAdet = 0;
            if (kesitler.Count == 0) return;
            var baylar = chains.SelectMany(c => c).Select(s => (Lo: x + s.X0, Hi: x + s.X1)).ToList();
            foreach (var r in SurekliTemelAmpatmanAraliklari(takim, x))
            {
                double a = r.Lo + 5.0, b = r.Hi - 5.0;
                var icinde = kesitler.Where(k => k.X >= r.Lo - 1.0 && k.X <= r.Hi + 1.0).ToList();
                if (icinde.Count == 0 || b - a < 1.0) continue;
                int aralik = icinde.Min(k => k.AmpAralik);
                int m = (int)Math.Ceiling((b - a) / aralik - 1e-9);
                for (int j = 0; j <= m; j++)
                {
                    double p = a + (b - a) * j / Math.Max(1, m);
                    var bay = baylar.FirstOrDefault(t => p >= t.Lo - 0.01 && p <= t.Hi + 0.01);
                    KirisKesitBilgi hedef = null;
                    if (bay.Hi > bay.Lo) hedef = icinde.FirstOrDefault(k => k.X >= bay.Lo && k.X <= bay.Hi);
                    if (hedef == null) hedef = icinde.OrderBy(k => Math.Abs(k.X - p)).First();
                    hedef.AmpAdet++;
                }
            }
        }

        /// <summary>Dağıtma açılımı, kolon kesiminin bu kadar altında (cm, 2× öncesi): alt ilave (160) + 25 → 1/25'te 50 cm.</summary>
        private const double SurekliDagitmaAcilimCm = KirisAcilimAltIlaveAcilimCm + 25.0;

        /// <summary>
        /// Ampatman dağıtma çubuklarının temel boyunca açılımı: ampatmanlı temellerin görünüş aralıkları birleşir,
        /// her uçta 5 cm örtü. 1200 cm'yi aşan boy 1200'lük parçalara bölünür, son parça kalan boy; bindirme Konum II ℓb (5 cm'ye yukarı, ≥ 30 cm),
        /// parçalar 5 cm şaşırtılır ve bindirmeler ölçülendirilir. Adet: aralıktaki kesitlerin dağıtma adedi (en büyüğü).
        /// </summary>
        private void SurekliTemelDagitmaAcilim(Transaction tr, BlockTableRecord ms, Database db, SurekliGorunusTakim takim, double x)
        {
            if (_surekliDagitmaCap <= 0 || _kirisAcilimKesimY > 1e8) return;
            var birlesik = SurekliTemelAmpatmanAraliklari(takim, x);
            if (birlesik.Count == 0) return;
            const double ortu = 5.0, maxBoy = 1200.0;
            int cap = _surekliDagitmaCap;
            double lap = Math.Max(30.0, KirisAcilimYuvarlaYukari(SurekliTemelLbCm(cap, false)));
            ObjectId stil = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, "PLAN_OLCU_25");
            double yBar = _kirisAcilimKesimY - SurekliDagitmaAcilimCm;
            foreach (var r in birlesik)
            {
                int adet = _kirisKesitler.Where(k => k.X >= r.Lo - 1.0 && k.X <= r.Hi + 1.0)
                    .Select(k => k.DagitmaAdet).DefaultIfEmpty(0).Max();
                double a = r.Lo + ortu, b = r.Hi - ortu, toplam = b - a;
                if (adet <= 0 || toplam < 10.0) continue;
                int n = toplam <= maxBoy ? 1 : (int)Math.Ceiling((toplam - lap) / (maxBoy - lap) - 1e-9);
                // Bindirme yeri önemsiz: tam 12 m parçalar, son parça kalan boy.
                double parca = n == 1 ? toplam : maxBoy;
                double x0 = a;
                for (int i = 0; i < n; i++)
                {
                    double x1 = i == n - 1 ? b : Math.Min(b, x0 + parca);
                    double yy = yBar - (i % 2) * KirisAcilimMontajSasirtmaCm;
                    KirisAcilimDonatiPl(tr, ms, 0, new List<Point2d> { new Point2d(x0, yy), new Point2d(x1, yy) });
                    string lbl = adet.ToString(CultureInfo.InvariantCulture) + "ø" + cap.ToString(CultureInfo.InvariantCulture)
                        + " L=" + Math.Round(x1 - x0).ToString("0", CultureInfo.InvariantCulture) + " DAĞITMA";
                    double yYazi = yy + KirisAcilimAcilimYaziUstCm;
                    DrawBeamLabel(tr, ms, db, new Point3d(0.5 * (x0 + x1), yYazi, 0), lbl, 5.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
                    KirisAcilimYaziKutusuUstune(tr, yYazi);
                    if (i == n - 1) break;
                    // Bindirme ölçüsü üst sıradaki çubuğun üstünde.
                    AppendEntity(tr, ms, new RotatedDimension(0.0, new Point3d(x1 - lap, yBar, 0), new Point3d(x1, yBar, 0),
                        new Point3d(x1 - 0.5 * lap, yBar + KirisAcilimAcilimOlcuUzakCm, 0), "", stil)
                    { Layer = LayerOlcu, Dimexo = 0.0, Dimse1 = false, Dimse2 = false });
                    x0 = x1 - lap;
                }
            }
        }
    }
}
