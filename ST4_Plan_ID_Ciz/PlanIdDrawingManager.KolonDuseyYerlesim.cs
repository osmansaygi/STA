using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// KOLONDUSEY2 (antetli kat-kat): bir sayfanın kesit takımı (kesit + etriye açılımları + yazılar)
        /// varlıkları. Null = yakalama kapalı. Satır yerleşiminde takım açılımın yanına ya da üstüne taşınır.
        /// </summary>
        private HashSet<ObjectId> _kolonDuseyTakimCapture;
        /// <summary>Yakalama sırasında ilk kesit yazısının ebatı ("60/60"); poligonda null.</summary>
        private string _kolonDuseyKesitEbat;

        private void CaptureKolonDuseyTakim(BlockTableRecord btr, BtrIdSnapshot before)
        {
            if (_kolonDuseyTakimCapture == null || btr == null || before == null) return;
            foreach (ObjectId id in btr)
            {
                if (!id.IsNull && !before.Contains(id))
                    _kolonDuseyTakimCapture.Add(id);
            }
        }

        /// <summary>
        /// Sayfa kapsamı + ölçülerin tanım noktaları. <see cref="EnvelopeFromKolonDuseySheetIds"/> ölçüleri
        /// dışlar; kesit takımının dış kenarında yalnız ölçü varsa (poligon 180/120) iç antet onları
        /// kesiyordu. Ölçü yazısı ölçü çizgisinin dışında kalır; antet yan payı (yanPad) bunu karşılar.
        /// </summary>
        private Envelope KolonDuseySayfaKapsamOlculu(Transaction tr, int sheetKey)
        {
            var env = EnvelopeFromKolonDuseySheetIds(tr, sheetKey);
            if (_kolon50SheetEntityIds == null || !_kolon50SheetEntityIds.TryGetValue(sheetKey, out var ids) || ids == null)
                return env;
            var tum = KolonDuseyIdsKutu(tr, ids);
            if (tum == null) return env;
            if (env == null) return tum;
            var e = new Envelope(env);
            e.ExpandToInclude(tum);
            return e;
        }

        /// <summary>Varlık kutusu; ölçülerde tanım noktaları + yazı konumu (GeometricExtents güvenilmez).</summary>
        private static Envelope KolonDuseyIdsKutu(Transaction tr, IEnumerable<ObjectId> ids, HashSet<ObjectId> haric = null)
        {
            Envelope env = null;
            void Add(Point3d p)
            {
                if (!IsFinitePoint(p)) return;
                if (env == null) env = new Envelope(p.X, p.X, p.Y, p.Y);
                else env.ExpandToInclude(p.X, p.Y);
            }
            if (tr == null || ids == null) return null;
            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                if (haric != null && haric.Contains(id)) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null) continue;
                try
                {
                    // TextPosition yeniden hesaplanmamış ölçüde (0,0) dönebilir — kullanılmaz.
                    if (ent is RotatedDimension rd)
                    {
                        Add(rd.XLine1Point); Add(rd.XLine2Point); Add(rd.DimLinePoint);
                        continue;
                    }
                    if (ent is AlignedDimension ad)
                    {
                        Add(ad.XLine1Point); Add(ad.XLine2Point); Add(ad.DimLinePoint);
                        continue;
                    }
                    if (ent is Autodesk.AutoCAD.DatabaseServices.Dimension)
                        continue;
                    var ext = ent.GeometricExtents;
                    Add(ext.MinPoint);
                    Add(ext.MaxPoint);
                }
                catch { }
            }
            return env;
        }

        /// <summary>
        /// Kat satırındaki sayfalarda kesit takımını yerleştirir. Her sayfa için iki seçenek:
        /// yan (açılımın soluna, yakın; alt kenarı temel altının — TOPLAM yazısının — üstünde) ve
        /// üst (açılımın üstüne, ortalı). Ana antet sayfa genişliğini en aza indiren seçim uygulanır;
        /// üst yerleşim satır yüksekliğini aşmıyorsa genişlik kazanılır.
        /// </summary>
        private void ArrangeKolonDuseyKesitTakimlari(
            Transaction tr,
            List<(int wallNo, Envelope env)> rowSheets,
            Dictionary<int, double> antetAltY,
            Dictionary<int, List<ObjectId>> takimBySheet,
            IReadOnlyDictionary<int, string> isimler,
            double yanPad, double antetAra, double pad, double g, double altPay, double availH)
        {
            if (tr == null || rowSheets == null || rowSheets.Count == 0 || takimBySheet == null || takimBySheet.Count == 0)
                return;
            if (_kolon50SheetEntityIds == null) return;
            double gapYan = KolonDuseyOlcuCizimCm(40.0);
            double gapUst = KolonDuseyOlcuCizimCm(40.0);
            double clrAlt = KolonDuseyOlcuCizimCm(15.0);
            int n = rowSheets.Count;
            var envB = new Envelope[n];
            var envS = new Envelope[n];
            var dB = new (double dx, double dy)[n];
            var dS = new (double dx, double dy)[n];
            var hasTak = new bool[n];
            var yRef = new double[n];
            var takIds = new List<ObjectId>[n];
            for (int i = 0; i < n; i++)
            {
                int sk = rowSheets[i].wallNo;
                var env0 = rowSheets[i].env;
                envB[i] = envS[i] = env0;
                yRef[i] = antetAltY != null && antetAltY.TryGetValue(sk, out double ya) ? ya : env0.MinY;
                if (!takimBySheet.TryGetValue(sk, out var tak) || tak == null || tak.Count == 0) continue;
                if (!_kolon50SheetEntityIds.TryGetValue(sk, out var all) || all == null) continue;
                var takSet = new HashSet<ObjectId>(tak);
                var elev = KolonDuseyIdsKutu(tr, all, takSet);
                var tb = KolonDuseyIdsKutu(tr, tak);
                if (elev == null || tb == null || tb.Width < 1.0 || tb.Height < 1.0) continue;
                hasTak[i] = true;
                takIds[i] = tak;
                double elevTop = elev.MaxY;
                double yLo = yRef[i] + clrAlt;

                // Yan: sağ kenar açılımın solunda; dikeyde mümkünse olduğu yerde, temel altının üstünde
                // ve üst kenar açılım tepesini aşmayacak şekilde; sığmazsa alttan hizalı.
                double dxB = (elev.MinX - gapYan) - tb.MaxX;
                double lo = yLo - tb.MinY, hi = elevTop - tb.MaxY;
                double dyB = lo <= hi ? Math.Min(Math.Max(0.0, lo), hi) : lo;
                dB[i] = (dxB, dyB);
                var eb = new Envelope(elev);
                eb.ExpandToInclude(tb.MinX + dxB, tb.MinY + dyB);
                eb.ExpandToInclude(tb.MaxX + dxB, tb.MaxY + dyB);
                envB[i] = eb;

                // Üst: açılımın üstünde, ortalı (takım daha genişse sola hizalı).
                double dxS = tb.Width <= elev.Width
                    ? 0.5 * (elev.MinX + elev.MaxX) - 0.5 * (tb.MinX + tb.MaxX)
                    : elev.MinX - tb.MinX;
                double dyS = (elevTop + gapUst) - tb.MinY;
                dS[i] = (dxS, dyS);
                var es = new Envelope(elev);
                es.ExpandToInclude(tb.MinX + dxS, tb.MinY + dyS);
                es.ExpandToInclude(tb.MaxX + dxS, tb.MaxY + dyS);
                envS[i] = es;
            }
            if (!hasTak.Any(h => h)) return;

            double Reserve(int i, Envelope e) =>
                KolonDuseyAntetIsimPayi(isimler, rowSheets[i].wallNo, e, yanPad, pad, g);
            double W(Envelope e) => e.Width + 2.0 * yanPad;
            double H(int i, Envelope e) => (e.MaxY + pad + g + Reserve(i, e)) - (yRef[i] - altPay);

            // A: hepsi yanda.
            var stackA = new bool[n];
            // B: üst yerleşim yanın sınırını (satırdaki en yüksek yan yerleşim) aşmıyorsa üstte.
            double limitTop = double.MinValue;
            for (int i = 0; i < n; i++) limitTop = Math.Max(limitTop, H(i, envB[i]) + yRef[i] - altPay);
            var stackB = new bool[n];
            for (int i = 0; i < n; i++)
                stackB[i] = hasTak[i] && W(envS[i]) < W(envB[i]) - 1.0
                    && H(i, envS[i]) + yRef[i] - altPay <= limitTop + 0.5;
            // C: genişlik kazandıran her yerde üstte (satır paketleme yükseklikleri ayırır).
            var stackC = new bool[n];
            for (int i = 0; i < n; i++)
                stackC[i] = hasTak[i] && W(envS[i]) < W(envB[i]) - 1.0;

            double Score(bool[] st)
            {
                var ws = new List<double>(n);
                var hs = new List<double>(n);
                for (int i = 0; i < n; i++)
                {
                    var e = st[i] ? envS[i] : envB[i];
                    ws.Add(W(e));
                    hs.Add(H(i, e));
                }
                PackKolonDuseyAntetRows(ws, hs, availH, antetAra, out double maxW);
                return maxW;
            }
            double sA = Score(stackA), sB = Score(stackB), sC = Score(stackC);
            bool[] best = stackA;
            double bestS = sA;
            if (sB < bestS - 1.0) { best = stackB; bestS = sB; }
            if (sC < bestS - 1.0) { best = stackC; bestS = sC; }

            for (int i = 0; i < n; i++)
            {
                if (!hasTak[i]) continue;
                var d = best[i] ? dS[i] : dB[i];
                if (Math.Abs(d.dx) < 0.05 && Math.Abs(d.dy) < 0.05) continue;
                var disp = Matrix3d.Displacement(new Vector3d(d.dx, d.dy, 0));
                foreach (ObjectId id in takIds[i])
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent;
                    try { ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity; }
                    catch { continue; }
                    try { ent?.TransformBy(disp); } catch { }
                }
                int sk = rowSheets[i].wallNo;
                var envNew = KolonDuseySayfaKapsamOlculu(tr, sk);
                if (envNew != null && envNew.Width > 1.0 && envNew.Height > 1.0)
                    rowSheets[i] = (sk, envNew);
            }
        }

        /// <summary>Kolon antet tablosu sözlükleri (kat satırı süresince; ebat poligonda yok).</summary>
        private IReadOnlyDictionary<int, string> _kolonDuseyAntetEbat;
        private IReadOnlyDictionary<int, int> _kolonDuseyAntetAdet;

        // Tablo ölçüleri (son çizim cm; çizgiler KolonDuseyOlcuCizimCm ile, yazı boyları doğrudan).
        private const double KdTabloKenarCm = 20.0;
        private const double KdTabloBaslikHCm = 24.0;
        private const double KdTabloSatirHCm = 32.0;
        private const double KdTabloEtiketYaziCm = 12.0;
        private const double KdTabloIsimYaziCm = 18.0;
        private const double KdTabloDegerYaziCm = 20.0;
        private const double KdTabloHucrePayCm = 14.0;
        private const int KdTabloMaxSutun = 8;
        /// <summary>Bahnschrift Light Condensed: karakter genişliği ≈ 0,55 × yazı boyu (güvenli tahmin).</summary>
        private static double KdYaziGenislik(string s, double h) =>
            string.IsNullOrEmpty(s) ? 0.0 : s.Length * h * 0.55;

        private static List<string> KolonDuseyAntetIsimListesi(string adlar)
        {
            if (string.IsNullOrWhiteSpace(adlar)) return new List<string>();
            return adlar.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        }

        /// <summary>
        /// Kolon tablosu yerleşimi (son çizim cm): sütun genişlikleri, ızgara sütun/satır sayısı,
        /// toplam genişlik ve yükseklik. Çerçeve iç genişliğine sığacak kadar sütun (en çok 8).
        /// </summary>
        private static void KolonDuseyAntetTabloOlcu(
            List<string> isimler, string ebat, int adet, double frameWFinal,
            out double isimW, out double ebatW, out double adetW, out int nCol, out int nRow,
            out double tabloW, out double tabloH)
        {
            int n = Math.Max(1, isimler?.Count ?? 0);
            double maxIsim = 0.0;
            if (isimler != null)
                foreach (var s in isimler) maxIsim = Math.Max(maxIsim, KdYaziGenislik(s, KdTabloIsimYaziCm));
            isimW = Math.Max(maxIsim, KdYaziGenislik("S-00", KdTabloIsimYaziCm)) + KdTabloHucrePayCm;
            ebatW = string.IsNullOrEmpty(ebat) ? 0.0
                : Math.Max(KdYaziGenislik(ebat, KdTabloDegerYaziCm), KdYaziGenislik("EBAT", KdTabloEtiketYaziCm)) + KdTabloHucrePayCm;
            string adetS = Math.Max(1, adet).ToString(System.Globalization.CultureInfo.InvariantCulture);
            adetW = Math.Max(KdYaziGenislik(adetS, KdTabloDegerYaziCm), KdYaziGenislik("ADET", KdTabloEtiketYaziCm)) + KdTabloHucrePayCm;
            double kalan = frameWFinal - 2.0 * KdTabloKenarCm - ebatW - adetW;
            int sigan = (int)Math.Floor(kalan / Math.Max(1.0, isimW));
            nCol = Math.Max(1, Math.Min(Math.Min(n, KdTabloMaxSutun), sigan));
            nRow = (n + nCol - 1) / nCol;
            double isimBaslikMin = KdYaziGenislik("KOLONLAR", KdTabloEtiketYaziCm) + KdTabloHucrePayCm;
            if (nCol * isimW < isimBaslikMin) isimW = isimBaslikMin / nCol;
            tabloW = nCol * isimW + ebatW + adetW;
            tabloH = KdTabloBaslikHCm + nRow * KdTabloSatirHCm;
        }

        /// <summary>
        /// KAPAMADETAY perde hattı (katlar boyunca aynı yerdeki kapama perdesi parçaları) tablosu:
        /// sayfa anahtarı → alttan üste (kat adı, perde numaraları, kalınlık cm).
        /// </summary>
        private Dictionary<int, List<(string kat, string perde, int t)>> _kapamaAntetKatTablo;

        private const double KdKapamaKatYaziCm = 14.0;

        /// <summary>Kapama tablosu ölçüleri (son çizim cm): etiket sütunu, kat sütunları, kalınlık sütunu.</summary>
        private static void KapamaAntetTabloOlcu(List<(string kat, string perde, int t)> satir,
            out double etiketW, out double[] katW, out double kalinW, out string kalinYazi,
            out double tabloW, out double tabloH)
        {
            int n = satir?.Count ?? 0;
            etiketW = Math.Max(KdYaziGenislik("PERDE", KdTabloEtiketYaziCm), KdYaziGenislik("KAT", KdTabloEtiketYaziCm))
                + KdTabloHucrePayCm;
            katW = new double[n];
            for (int i = 0; i < n; i++)
                katW[i] = Math.Max(KdYaziGenislik(satir[i].kat, KdKapamaKatYaziCm),
                    KdYaziGenislik(satir[i].perde, KdTabloIsimYaziCm)) + KdTabloHucrePayCm;
            var ts = new List<int>();
            if (satir != null)
                foreach (var s in satir)
                    if (s.t > 0 && (ts.Count == 0 || ts[ts.Count - 1] != s.t)) ts.Add(s.t);
            kalinYazi = ts.Count == 0 ? "" : string.Join(" / ", ts.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            kalinW = string.IsNullOrEmpty(kalinYazi) ? 0.0
                : Math.Max(KdYaziGenislik(kalinYazi, KdTabloDegerYaziCm), KdYaziGenislik("KALINLIK", KdTabloEtiketYaziCm)) + KdTabloHucrePayCm;
            tabloW = etiketW + katW.Sum() + kalinW;
            tabloH = KdTabloBaslikHCm + KdTabloSatirHCm;
        }

        /// <summary>İmzada yok sayılan katmanlar: isimler (perde/kolon adı farklı olsa da çizim aynı).</summary>
        private static readonly HashSet<string> KapamaImzaHaricKatman = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PERDE ISMI (BEYKENT)", "KOLON ISMI (BEYKENT)", "KAPAMA PERDESI ISMI (BEYKENT)",
            "TEXT-175", "IC ANTET (BEYKENT)", "IC OLCEK (BEYKENT)"
        };

        /// <summary>
        /// Perde hattı sayfasının çizim imzası: varlık türü, katman, sayfa sol-alt köşesine göre noktalar
        /// (0,5 cm yuvarlak), yazılarda metin (isim katmanları hariç). Birebir aynı hatlar aynı imzayı verir.
        /// </summary>
        private static string KapamaHatImza(Transaction tr, List<ObjectId> ids, Envelope env)
        {
            if (tr == null || ids == null || env == null) return null;
            var parca = new List<string>(ids.Count);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string P(Point3d p) => (Math.Round((p.X - env.MinX) * 2.0) / 2.0).ToString("0.0", ci) + ","
                + (Math.Round((p.Y - env.MinY) * 2.0) / 2.0).ToString("0.0", ci);
            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                Entity ent;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { continue; }
                if (ent == null || KapamaImzaHaricKatman.Contains(ent.Layer ?? "")) continue;
                var sb = new System.Text.StringBuilder();
                sb.Append(ent.GetType().Name).Append('|').Append(ent.Layer).Append('|');
                try
                {
                    switch (ent)
                    {
                        case DBText t:
                            sb.Append(t.TextString).Append('|').Append(P(t.Position)).Append('|')
                              .Append(t.Height.ToString("0.0", ci));
                            break;
                        case MText m:
                            sb.Append(m.Contents).Append('|').Append(P(m.Location));
                            break;
                        case Line l:
                            sb.Append(P(l.StartPoint)).Append(';').Append(P(l.EndPoint));
                            break;
                        case Polyline pl:
                            for (int v = 0; v < pl.NumberOfVertices; v++)
                                sb.Append(P(pl.GetPoint3dAt(v))).Append('b')
                                  .Append(Math.Round(pl.GetBulgeAt(v), 3).ToString(ci)).Append(';');
                            break;
                        case Circle c:
                            sb.Append(P(c.Center)).Append('r').Append(c.Radius.ToString("0.0", ci));
                            break;
                        case Arc a:
                            sb.Append(P(a.Center)).Append('r').Append(a.Radius.ToString("0.0", ci))
                              .Append('a').Append(Math.Round(a.StartAngle, 3).ToString(ci))
                              .Append('-').Append(Math.Round(a.EndAngle, 3).ToString(ci));
                            break;
                        case RotatedDimension rd:
                            sb.Append(P(rd.XLine1Point)).Append(';').Append(P(rd.XLine2Point)).Append(';')
                              .Append(P(rd.DimLinePoint)).Append('|').Append(rd.DimensionText);
                            break;
                        case AlignedDimension ad:
                            sb.Append(P(ad.XLine1Point)).Append(';').Append(P(ad.XLine2Point)).Append(';')
                              .Append(P(ad.DimLinePoint)).Append('|').Append(ad.DimensionText);
                            break;
                        case BlockReference br:
                            sb.Append(br.Name).Append('|').Append(P(br.Position));
                            break;
                        default:
                            var ext = ent.GeometricExtents;
                            sb.Append(P(ext.MinPoint)).Append(';').Append(P(ext.MaxPoint));
                            break;
                    }
                }
                catch { }
                parca.Add(sb.ToString());
            }
            parca.Sort(StringComparer.Ordinal);
            return string.Join("\n", parca);
        }

        /// <summary>
        /// KAPAMADETAY: birebir aynı perde hatları tek antette. Tekrarların varlıkları silinir; temsilcinin
        /// donatı yazılarına adet çarpanı ("2x…") eklenir; tabloda kat bazında numaralar birleşir.
        /// </summary>
        private void MergeKapamaBenzerHatlar(Transaction tr, List<(int wallNo, Envelope env)> sheets,
            Dictionary<int, string> isimler, Autodesk.AutoCAD.EditorInput.Editor ed = null)
        {
            if (tr == null || sheets == null || sheets.Count < 2 || _kolon50SheetEntityIds == null) return;
            var gruplar = new List<List<int>>();
            var imzaIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < sheets.Count; i++)
            {
                if (!_kolon50SheetEntityIds.TryGetValue(sheets[i].wallNo, out var ids) || ids == null) continue;
                string imza = KapamaHatImza(tr, ids, sheets[i].env);
                if (string.IsNullOrEmpty(imza)) continue;
                if (imzaIdx.TryGetValue(imza, out int g)) gruplar[g].Add(i);
                else { imzaIdx[imza] = gruplar.Count; gruplar.Add(new List<int> { i }); }
            }
            var silinecek = new HashSet<int>();
            int nBirlesen = 0;
            foreach (var grp in gruplar)
            {
                if (grp.Count < 2) continue;
                int rep = sheets[grp[0]].wallNo;
                int n = grp.Count;
                nBirlesen += n - 1;
                // Tablo: aynı sıradaki katlarda numaraları birleştir.
                if (_kapamaAntetKatTablo != null && _kapamaAntetKatTablo.TryGetValue(rep, out var satir) && satir != null)
                {
                    var yeni = new List<(string kat, string perde, int t)>(satir);
                    for (int gi = 1; gi < grp.Count; gi++)
                    {
                        if (!_kapamaAntetKatTablo.TryGetValue(sheets[grp[gi]].wallNo, out var s2) || s2 == null) continue;
                        for (int r = 0; r < yeni.Count && r < s2.Count; r++)
                            if (!string.IsNullOrEmpty(s2[r].perde))
                                yeni[r] = (yeni[r].kat, yeni[r].perde + ", " + s2[r].perde, yeni[r].t);
                    }
                    _kapamaAntetKatTablo[rep] = yeni;
                }
                if (isimler != null && isimler.TryGetValue(rep, out string ad0))
                {
                    var adlar = new List<string> { ad0 };
                    for (int gi = 1; gi < grp.Count; gi++)
                        if (isimler.TryGetValue(sheets[grp[gi]].wallNo, out string adx)) adlar.Add(adx);
                    isimler[rep] = string.Join(", ", adlar);
                }
                // Donatı yazılarına adet çarpanı.
                string on = n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x";
                foreach (ObjectId id in _kolon50SheetEntityIds[rep])
                {
                    if (id.IsNull || id.IsErased) continue;
                    try
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead, false) is DBText t)) continue;
                        if (!string.Equals(t.Layer, LayerDonatiYazisiPerde, StringComparison.OrdinalIgnoreCase)) continue;
                        string s = t.TextString ?? "";
                        if (s.IndexOf('ø') < 0 && s.IndexOf("%%c", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        t.UpgradeOpen();
                        t.TextString = on + s;
                        if (t.HorizontalMode != TextHorizontalMode.TextLeft || t.VerticalMode != TextVerticalMode.TextBase)
                            t.AdjustAlignment(t.Database);
                    }
                    catch { }
                }
                // Tekrarlar silinir.
                for (int gi = 1; gi < grp.Count; gi++)
                {
                    int key = sheets[grp[gi]].wallNo;
                    if (_kolon50SheetEntityIds.TryGetValue(key, out var ids2) && ids2 != null)
                    {
                        foreach (ObjectId id in ids2)
                        {
                            if (id.IsNull || id.IsErased) continue;
                            try { (tr.GetObject(id, OpenMode.ForWrite, false) as Entity)?.Erase(); } catch { }
                        }
                    }
                    _kolon50SheetEntityIds.Remove(key);
                    _kapamaAntetKatTablo?.Remove(key);
                    isimler?.Remove(key);
                    silinecek.Add(grp[gi]);
                }
            }
            if (silinecek.Count == 0) return;
            var kalan = new List<(int wallNo, Envelope env)>();
            for (int i = 0; i < sheets.Count; i++)
                if (!silinecek.Contains(i)) kalan.Add(sheets[i]);
            sheets.Clear();
            sheets.AddRange(kalan);
            ed?.WriteMessage("\nKAPAMADETAY: {0} perde hatti birebir ayni olan hatla birlestirildi.", nBirlesen);
        }

        /// <summary>Kapama iç antet tepe payı ve gereken en küçük çerçeve genişliği (çizim cm).</summary>
        private void KapamaAntetTabloPayi(int sheetKey, double pad, double g, out double tepePayi, out double minCerceveW)
        {
            tepePayi = 0.0;
            minCerceveW = 0.0;
            if (_kapamaAntetKatTablo == null || !_kapamaAntetKatTablo.TryGetValue(sheetKey, out var satir) || satir == null || satir.Count == 0)
                return;
            KapamaAntetTabloOlcu(satir, out _, out _, out _, out _, out double tw, out double th);
            tepePayi = Math.Max(0.0, KolonDuseyOlcuCizimCm(KdTabloKenarCm + th + 10.0) - (pad + g));
            minCerceveW = KolonDuseyOlcuCizimCm(tw + 2.0 * KdTabloKenarCm);
        }

        /// <summary>
        /// Kapama iç antet sol üst tablo: KAT | kat adları… | KALINLIK (üst satır), PERDE | numaralar… | kalınlık.
        /// Kat adları beyaz (ACI 7), perde numaraları sarı (ACI 2), kalınlık yeşil (ACI 3), başlıklar gri (ACI 8).
        /// </summary>
        private void DrawKapamaAntetTablo(Transaction tr, BlockTableRecord btr, double ox0, double oy1,
            List<(string kat, string perde, int t)> satir)
        {
            if (tr == null || btr == null || satir == null || satir.Count == 0) return;
            KapamaAntetTabloOlcu(satir, out double etiketW, out double[] katW, out double kalinW, out string kalinYazi,
                out _, out _);
            double k = KolonDuseyOlcuCizimCm(1.0);
            double x0 = ox0 + k * KdTabloKenarCm;
            double y1 = oy1 - k * KdTabloKenarCm;
            double yM = y1 - k * KdTabloBaslikHCm;
            double y0 = yM - k * KdTabloSatirHCm;
            var xs = new List<double> { x0, x0 + k * etiketW };
            for (int i = 0; i < katW.Length; i++) xs.Add(xs[xs.Count - 1] + k * katW[i]);
            if (kalinW > 0.0) xs.Add(xs[xs.Count - 1] + k * kalinW);
            double x1 = xs[xs.Count - 1];
            var db = btr.Database;
            void Ln(double ax, double ay, double bx, double by)
            {
                var l = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                l.SetDatabaseDefaults();
                l.Layer = LayerIcOlcek;
                AppendEntity(tr, btr, l);
            }
            AppendClosedRect(tr, btr, x0, y0, x1, y1, LayerIcOlcek);
            Ln(x0, yM, x1, yM);
            for (int i = 1; i < xs.Count - 1; i++) Ln(xs[i], y0, xs[i], y1);

            const short aciEtiket = 8, aciKat = 7, aciPerde = 2, aciKalin = 3;
            double yUst = 0.5 * (yM + y1), yAlt = 0.5 * (y0 + yM);
            void Yaz(double cx, double cy, string s, double h, short aci)
            {
                if (string.IsNullOrEmpty(s)) return;
                DrawBeamLabel(tr, btr, db, new Point3d(cx, cy, 0), s, h, 0.0, LayerAntetText175,
                    useMiddleCenter: true, colorAci: aci);
            }
            Yaz(0.5 * (xs[0] + xs[1]), yUst, "KAT", KdTabloEtiketYaziCm, aciEtiket);
            Yaz(0.5 * (xs[0] + xs[1]), yAlt, "PERDE", KdTabloEtiketYaziCm, aciEtiket);
            for (int i = 0; i < satir.Count; i++)
            {
                double cx = 0.5 * (xs[i + 1] + xs[i + 2]);
                Yaz(cx, yUst, satir[i].kat, KdKapamaKatYaziCm, aciKat);
                Yaz(cx, yAlt, satir[i].perde, KdTabloIsimYaziCm, aciPerde);
            }
            if (kalinW > 0.0)
            {
                double cx = 0.5 * (xs[xs.Count - 2] + xs[xs.Count - 1]);
                Yaz(cx, yUst, "KALINLIK", KdTabloEtiketYaziCm, aciEtiket);
                Yaz(cx, yAlt, kalinYazi, KdTabloDegerYaziCm, aciKalin);
            }
        }

        /// <summary>
        /// İç antet tepe payı: kolon tablosu (sol üst) içerik üstündeki tepe payını (pad + g) aşarsa
        /// çerçeve bu kadar yükseltilir; tablo takım/açılımla çakışmaz.
        /// </summary>
        private double KolonDuseyAntetIsimPayi(IReadOnlyDictionary<int, string> isimler, int sheetKey,
            Envelope e, double yanPad, double pad, double g)
        {
            if (isimler == null || e == null || !isimler.TryGetValue(sheetKey, out string ad) || string.IsNullOrEmpty(ad))
                return 0.0;
            string ebat = null;
            _kolonDuseyAntetEbat?.TryGetValue(sheetKey, out ebat);
            int adet = 1;
            if (_kolonDuseyAntetAdet != null && _kolonDuseyAntetAdet.TryGetValue(sheetKey, out int a)) adet = a;
            double k = KolonDuseyOlcuCizimCm(1.0);
            double frameWFinal = (e.Width + 2.0 * yanPad) / k;
            KolonDuseyAntetTabloOlcu(KolonDuseyAntetIsimListesi(ad), ebat, adet, frameWFinal,
                out _, out _, out _, out _, out _, out _, out double tabloH);
            double band = KolonDuseyOlcuCizimCm(KdTabloKenarCm + tabloH + 10.0);
            return Math.Max(0.0, band - (pad + g));
        }

        /// <summary>
        /// İç antet sol üst kolon tablosu: başlık satırı KOLONLAR | EBAT | ADET; isimler ızgarada (sarı),
        /// ebat (yeşil) ve adet (kırmızı) isim satırlarını kaplayan birleşik hücrede. Poligonda EBAT yok.
        /// Dönen değer tablonun alt Y'si.
        /// </summary>
        private double DrawKolonDuseyAntetTablo(
            Transaction tr, BlockTableRecord btr, double ox0, double ox1, double oy1,
            string adlar, string ebat, int adet)
        {
            var isimler = KolonDuseyAntetIsimListesi(adlar);
            if (tr == null || btr == null || isimler.Count == 0) return oy1;
            double k = KolonDuseyOlcuCizimCm(1.0);
            KolonDuseyAntetTabloOlcu(isimler, ebat, adet, (ox1 - ox0) / k,
                out double isimW, out double ebatW, out double adetW, out int nCol, out int nRow,
                out double tabloW, out double tabloH);
            double x0 = ox0 + k * KdTabloKenarCm;
            double y1 = oy1 - k * KdTabloKenarCm;
            double hb = k * KdTabloBaslikHCm, hr = k * KdTabloSatirHCm;
            double yB = y1 - hb;                 // başlık altı
            double y0 = yB - nRow * hr;          // tablo altı
            double xIsimEnd = x0 + k * nCol * isimW;
            double xEbatEnd = xIsimEnd + k * ebatW;
            double x1 = xEbatEnd + k * adetW;
            var db = btr.Database;

            void Ln(double ax, double ay, double bx, double by)
            {
                var l = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                l.SetDatabaseDefaults();
                l.Layer = LayerIcOlcek;
                AppendEntity(tr, btr, l);
            }
            AppendClosedRect(tr, btr, x0, y0, x1, y1, LayerIcOlcek);
            Ln(x0, yB, x1, yB);
            if (ebatW > 0.0) Ln(xIsimEnd, y0, xIsimEnd, y1);
            Ln(xEbatEnd, y0, xEbatEnd, y1);
            for (int c = 1; c < nCol; c++)
            {
                double xc = x0 + k * c * isimW;
                Ln(xc, y0, xc, yB);
            }
            for (int r = 1; r < nRow; r++)
            {
                double yr = yB - r * hr;
                Ln(x0, yr, xIsimEnd, yr);
            }

            const short aciEtiket = 8, aciIsim = 2, aciEbat = 3, aciAdet = 1;
            double yBas = 0.5 * (yB + y1);
            DrawBeamLabel(tr, btr, db, new Point3d(x0 + k * 0.5 * KdTabloHucrePayCm, yBas, 0), "KOLONLAR",
                KdTabloEtiketYaziCm, 0.0, LayerAntetText175, bottomLeftAligned: true, colorAci: aciEtiket);
            try
            {
                if (!_lastBeamLabelId.IsNull && tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite, false) is DBText tKol)
                {
                    tKol.VerticalMode = TextVerticalMode.TextVerticalMid;
                    tKol.AlignmentPoint = new Point3d(x0 + k * 0.5 * KdTabloHucrePayCm, yBas, 0);
                    tKol.AdjustAlignment(db);
                }
            }
            catch { }
            if (ebatW > 0.0)
                DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (xIsimEnd + xEbatEnd), yBas, 0), "EBAT",
                    KdTabloEtiketYaziCm, 0.0, LayerAntetText175, useMiddleCenter: true, colorAci: aciEtiket);
            DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (xEbatEnd + x1), yBas, 0), "ADET",
                KdTabloEtiketYaziCm, 0.0, LayerAntetText175, useMiddleCenter: true, colorAci: aciEtiket);

            for (int i = 0; i < isimler.Count; i++)
            {
                int r = i / nCol, c = i % nCol;
                double cx = x0 + k * (c + 0.5) * isimW;
                double cy = yB - (r + 0.5) * hr;
                DrawBeamLabel(tr, btr, db, new Point3d(cx, cy, 0), isimler[i],
                    KdTabloIsimYaziCm, 0.0, LayerAntetText175, useMiddleCenter: true, colorAci: aciIsim);
            }
            double yDeg = 0.5 * (y0 + yB);
            if (ebatW > 0.0)
                DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (xIsimEnd + xEbatEnd), yDeg, 0), ebat,
                    KdTabloDegerYaziCm, 0.0, LayerAntetText175, useMiddleCenter: true, colorAci: aciEbat);
            DrawBeamLabel(tr, btr, db, new Point3d(0.5 * (xEbatEnd + x1), yDeg, 0),
                Math.Max(1, adet).ToString(System.Globalization.CultureInfo.InvariantCulture),
                KdTabloDegerYaziCm, 0.0, LayerAntetText175, useMiddleCenter: true, colorAci: aciAdet);
            return y0;
        }

        /// <summary>
        /// İç antetleri ana antet yüksekliğine (availH) sığan satırlara böler; en dar satır genişliğini
        /// verir. Eski yöntem (tüm satırlar en yüksek antet kadar) ile değişken yükseklikli yöntem
        /// (yüksekliğe göre sıralı, benzer yükseklikler aynı satırda) karşılaştırılır.
        /// </summary>
        private static List<List<int>> PackKolonDuseyAntetRows(
            IReadOnlyList<double> widths, IReadOnlyList<double> heights, double availH, double ara, out double maxRowW)
        {
            int n = widths.Count;
            maxRowW = 0.0;
            if (n == 0) return new List<List<int>>();
            double RowW(IEnumerable<int> idx)
            {
                double w = 0.0; int c = 0;
                foreach (int i in idx) { w += widths[i]; c++; }
                return w + Math.Max(0, c - 1) * ara;
            }
            double MaxW(List<List<int>> rows) => rows.Count == 0 ? 0.0 : rows.Max(r => RowW(r));

            // Eski: tek tip yükseklik.
            double hMax = heights.Max();
            int maxRowsFit = 1;
            for (int nr = 1; nr <= n; nr++)
            {
                if (nr * hMax + (nr - 1) * ara <= availH + 0.05) maxRowsFit = nr;
                else break;
            }
            int kRows = Math.Max(1, Math.Min(maxRowsFit, n));
            List<List<int>> best = kRows <= 1
                ? new List<List<int>> { Enumerable.Range(0, n).ToList() }
                : KapamaPackMinWidthRows(widths, kRows, ara).Where(r => r.Count > 0).ToList();
            double bestW = MaxW(best);

            // Değişken yükseklik: yüksekliğe göre azalan sıra, ardışık gruplara böl (DP: en küçük max genişlik).
            var ord = Enumerable.Range(0, n).OrderByDescending(i => heights[i]).ThenBy(i => i).ToArray();
            var pre = new double[n + 1];
            for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + widths[ord[i]];
            double Seg(int a, int b) => pre[b] - pre[a] + Math.Max(0, b - a - 1) * ara; // [a,b)
            for (int k = 2; k <= n; k++)
            {
                // dp[j][i]: ilk i öğe j gruba → en küçük max genişlik; yükseklik toplamı ayrıca tutulur.
                var dp = new double[k + 1, n + 1];
                var hs = new double[k + 1, n + 1];
                var cut = new int[k + 1, n + 1];
                for (int j = 0; j <= k; j++)
                    for (int i = 0; i <= n; i++) { dp[j, i] = double.MaxValue; hs[j, i] = double.MaxValue; }
                dp[0, 0] = 0.0; hs[0, 0] = 0.0;
                for (int j = 1; j <= k; j++)
                {
                    for (int i = j; i <= n; i++)
                    {
                        for (int a = j - 1; a < i; a++)
                        {
                            if (dp[j - 1, a] == double.MaxValue) continue;
                            double w = Math.Max(dp[j - 1, a], Seg(a, i));
                            double h = hs[j - 1, a] + heights[ord[a]] + (j > 1 ? ara : 0.0);
                            if (h > availH + 0.05) continue;
                            if (w < dp[j, i] - 1e-6 || (Math.Abs(w - dp[j, i]) < 1e-6 && h < hs[j, i]))
                            {
                                dp[j, i] = w; hs[j, i] = h; cut[j, i] = a;
                            }
                        }
                    }
                }
                if (dp[k, n] == double.MaxValue) continue;
                if (dp[k, n] < bestW - 1.0)
                {
                    var rows = new List<List<int>>();
                    int iEnd = n;
                    for (int j = k; j >= 1; j--)
                    {
                        int a = cut[j, iEnd];
                        var r = new List<int>();
                        for (int t = a; t < iEnd; t++) r.Add(ord[t]);
                        rows.Add(r);
                        iEnd = a;
                    }
                    best = rows;
                    bestW = dp[k, n];
                }
            }
            for (int r = 0; r < best.Count; r++)
                best[r] = best[r].OrderBy(i => i).ToList();
            best = best.Where(r => r.Count > 0).OrderBy(r => r.Min()).ToList();
            maxRowW = MaxW(best);
            return best;
        }
    }
}
