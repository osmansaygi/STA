using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISACILIMLARI: kirişin ana aksı boyunca düşey kesit (açılım).
    /// Yatay kirişe planda aşağıdan yukarı, düşey kirişe sağdan sola bakılır.
    /// Aynı kiriş numarası tek çizilir. ≤25° kırılan komşu kirişler aynı şeritte kalır.
    /// Düşey örtüşme 20 cm’den azsa (kotlar ayrık veya birleşim inceyse) ayrı açılım çizilir.
    /// Serbest uçta başka kiriş başlıyorsa o uç, kesen kiriş kesitinin dış kenarında biter.
    /// Kiriş ve kolon tek kontur (kesişen iç çizgi yok). Kolon genişliği, kirişin içinden geçtiği kalınlıktır; kolonun tüm köşe izdüşümü değil. Kolon kütüğü kiriş çizgisinden 20 cm uzar; kesik ağız yalnız kesit sınırıdır.
    /// Yandan kesişen kirişin kesik izi çizilmez. Konsol uçtaki yan kiriş ile saplama çifti, KESIT GORUNUS katmanında kendi en × boy kesitiyle çizilir.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double KirisAcilimMaxDonusDerece = 25.0;
        private const double KirisAcilimUcToleransCm = 5.0;
        /// <summary>Aynı doğru üzerinde, kolon aralığı kadar boşluk (uçlar 5 cm’de birleşmese de) aynı açılıma girer.</summary>
        private const double KirisAcilimAyniHatBoslukCm = 100.0;
        private const double KirisAcilimAyniHatSapmaCm = 10.0;
        /// <summary>İki kirişin düşeyde bu kadardan az örtüşürse (veya aralarında boşluk varsa) aynı açılıma girmez.</summary>
        private const double KirisAcilimMinBirlestirmeCm = 20.0;
        /// <summary>1/20 kâğıtta 5 mm başlık.</summary>
        private const double KirisAcilimBaslikYukseklikCm = 10.0;
        private const double KirisAcilimAksYaziCm = 10.0;
        private const double KirisAcilimBoyYaziCm = 4.0;
        private const double KirisAcilimBalonYaricapCm = 10.0;
        /// <summary>Şeritler arası boşluk (1/20’de 20 mm kâğıt).</summary>
        private const double KirisAcilimSeritAraCm = 40.0;
        private const double KirisAcilimKatAraCm = 80.0;
        /// <summary>Kolon kütüğü ve kiriş izi çıkıntısı, kiriş çizgisinden bu kadar gösterilip kesit sınırıyla kesilir.</summary>
        private const double KirisAcilimKutupCm = 20.0;
        /// <summary>Kolon aralığı düğüme bu kadar yakınsa mesnet sayılır.</summary>
        private const double KirisAcilimKolonYakinCm = 20.0;
        /// <summary>Montaj donatısı net paspayı (cm). Çubuk merkezi = paspayı (dış sıra) ve paspayı + çap.</summary>
        private const double KirisAcilimMontajPaspayiCm = 4.0;
        /// <summary>Montaj donatısı etiketinin kiriş üst çizgisinden aşağı mesafesi (cm).</summary>
        private const double KirisAcilimMontajEtiketUsttenCm = 12.0;
        /// <summary>Yüzdeki iki çubuk sırası arası (cm). TBDY: boyuna çubuklar arası ≥ 2.5 cm.</summary>
        private const double KirisAcilimCubukAraCm = 2.5;
        /// <summary>Üst ve alt donatı seviyeleri arasında kalan gövdeye bu kadar pay bırakılır (cm).</summary>
        private const double KirisAcilimGovdeKenarPayiCm = 2.0;
        /// <summary>Alt düz donatı net paspayı (cm).</summary>
        private const double KirisAcilimDuzPaspayiCm = 4.0;
        /// <summary>Alt düz donatı etiketi merkezinin kiriş alt çizgisinden yukarı mesafesi (cm).</summary>
        private const double KirisAcilimDuzEtiketAlttanCm = 12.0;
        /// <summary>Montaj donatısı çizgi katmanı (EnsureLayers'ta tanımlı).</summary>
        private const string KirisAcilimDonatiLayer = "DONATI (BEYKENT)";
        /// <summary>Montaj donatısı etiket katmanı (EnsureLayers'ta tanımlı).</summary>
        private const string KirisAcilimDonatiYaziLayer = "DONATI YAZISI (BEYKENT)";

        /// <summary>Montaj donatısı rengi (ACI 4, camgöbeği).</summary>
        private const short KirisAcilimRenkMontaj = 4;
        /// <summary>Alt düz donatı rengi (ACI 3, yeşil).</summary>
        private const short KirisAcilimRenkDuz = 3;
        /// <summary>Gövde donatısı etiket/çizgi rengi (ACI 6, magenta).</summary>
        private const short KirisAcilimRenkGovde = 6;
        /// <summary>İlave donatı rengi. Üst: 1 sol üst ACI 5, 3 sağ üst ACI 2. Alt: 2 sol alt ACI 30, 4 sağ alt ACI 1.</summary>
        private const short KirisAcilimRenkIlaveSolUst = 5;
        private const short KirisAcilimRenkIlaveSagUst = 2;
        private const short KirisAcilimRenkIlaveSolAlt = 30;
        private const short KirisAcilimRenkIlaveSagAlt = 1;

        /// <summary>
        /// Kenetlenme: lb/φ, C30 + B420C (TS 500 9.1.2.1, STA çizimiyle aynı).
        /// lb = max(0.12·fyd/fctd·φ, 20φ). fyd=365.2, fctd=1.2667 → 34.60·φ; konum I ×1.4 → 48.44·φ.
        /// </summary>
        private const double KirisAcilimLbPhiKonumI = 48.44;
        private const double KirisAcilimLbPhiKonumII = 34.60;
        /// <summary>TS 500 7.3: nervürlü ilave kesimden aşım max(d, 20φ).</summary>
        private const double KirisAcilimKesimAsimPhi = 20.0;
        /// <summary>TBDY 7.4.3.1(a): mesnet üst donatısının en az 1/4'ü açıklık boyunca sürekli devam eder (montaj).</summary>
        private const double KirisAcilimUstSurekliOran = 0.25;
        /// <summary>STA OPS "Lk = 50Ø, Ln/4": üst ilave uzatma tabanı. TBDY 7.4.3.1(b)/(c) ile uyumlu.</summary>
        private const double KirisAcilimUstIlavePhi = 50.0;
        /// <summary>TBDY 7.4.3.1(c): alt donatı açıklığa max(lb, 50φ).</summary>
        private const double KirisAcilimAltIlavePhi = 50.0;
        /// <summary>TBDY 7.4.3.1(b): kenar mesnette kanca yatay ≥ 0.4·lb, düşey ≥ 12φ.</summary>
        private const double KirisAcilimKenarYatayOran = 0.4;
        private const double KirisAcilimKenarDuseyPhi = 12.0;
        /// <summary>TS 500 9.1.1: konum II üst sınırı; serbest üst yüzden bu kadar aşağıdaki yatay çubuk konum II sayılır (cm).</summary>
        private const double KirisAcilimKonumIIUsttenCm = 30.0;
        /// <summary>Ölçü yazılarını 5 cm'ye yuvarla (STA çizim alışkanlığı).</summary>
        private const double KirisAcilimYuvarlamaCm = 5.0;

        private double KirisAcilimEfektifYukseklikCm(double h, double capCm)
        {
            return h - KirisAcilimMontajPaspayiCm - 0.8 - capCm * 0.5;
        }

        /// <summary>5 cm katına yuvarlar.</summary>
        private static double KirisAcilimYuvarla(double x)
        {
            if (x <= 0) return x;
            return Math.Round(x / KirisAcilimYuvarlamaCm) * KirisAcilimYuvarlamaCm;
        }

        /// <summary>
        /// Üst mesnet ilave kesim mesafesi (cm). TBDY 7.4.3.1(a) Şekil 7.7 + STA OPS "Lk = 50Ø, Ln/4":
        /// L = max(ln_net/4, 50φ, lb_I, d+20φ). <paramref name="lnNetCm"/> NET açıklıktır (kolon/perde
        /// yüzleri arası); ln/4 TBDY Şekil 7.7 üst donatı uzatması; 50φ STA OPS ve TBDY 7.4.3.1(b)/(c)
        /// pratiği; lb_I TS 500 9.1.2.1 konum I; d+20φ TS 500 7.3 kesim aşımı.
        /// Mesnet payı burada DÜŞÜLMEZ: boy zaten net açıklığa göre hesaplanır (eski hatalı davranış:
        /// ln/4 alıp sonra mesnet genişliğinin tamamını çıkarmak, perde/duvar mesnetlerde boyu sebepsiz kısaltıyordu).
        /// </summary>
        private double KirisAcilimUstIlaveKesimCm(double lnNetCm, double h, double capCm, double capMm)
        {
            double lb = KirisAcilimLbPhiKonumI * capMm / 10.0;
            double asim = KirisAcilimEfektifYukseklikCm(h, capCm) + KirisAcilimKesimAsimPhi * capMm / 10.0;
            double phi50 = KirisAcilimUstIlavePhi * capMm / 10.0;
            double vals = KirisAcilimUstSurekliOran * lnNetCm;
            if (phi50 > vals) vals = phi50;
            if (lb > vals) vals = lb;
            if (asim > vals) vals = asim;
            return KirisAcilimYuvarla(vals);
        }

        /// <summary>Alt ilave kesim mesafesi (cm): max(lb_II, 50φ) — TBDY 7.4.3.1(c).</summary>
        private double KirisAcilimAltIlaveKesimCm(double capMm)
        {
            double lb = KirisAcilimLbPhiKonumII * capMm / 10.0;
            double phi50 = KirisAcilimAltIlavePhi * capMm / 10.0;
            return KirisAcilimYuvarla(lb > phi50 ? lb : phi50);
        }

        /// <summary>Çubuk konum sınıfı: serbest üst yüzden 30 cm'den fazla aşağıda veya eğimli ise II, değilse I.</summary>
        private static string KirisAcilimKonumSinifi(bool ust)
        {
            return ust ? "I" : "II";
        }

        private sealed class KirisAcilimSeg
        {
            public BeamInfo Beam;
            public Point2d P0;
            public Point2d P1;
            public double Kot0;
            public double Kot1;
            public int Cross0;
            public int Cross1;
            public Vector2d Dir;
            public double Len;
        }

        private sealed class KirisAcilimSpan
        {
            public BeamInfo Beam;
            public double X0;
            public double X1;
            public double Kot0;
            public double Kot1;
            public Point2d World0;
            public Point2d World1;
            public Vector2d Dir;
            public int Cross0;
            public int Cross1;
        }

        private struct KirisAcilimSupport
        {
            public double X0;
            public double X1;
            public bool IsWall;
            public bool ExtendUp;
            public bool ExtendDown;
            public int ColumnNo;
        }

        private struct KirisAcilimKolonTrim
        {
            public double X0;
            public double X1;
            public double YBot;
            public double YTop;
            public bool TrimTop;
            public bool TrimBot;
        }

        /// <summary>Saplanan kirişin görünüşteki yeri: kaçıklıklı şerit kesişimi ve kendi kot/yüksekliği.</summary>
        private struct KirisAcilimSaplama
        {
            public int BeamId;
            public double X0;
            public double X1;
            public double KotTop;
            public double KotBot;
            /// <summary>Bu kesit, bir kirişin çizimde SOL ucunun (X0) mesneti ise true → o uca 1-2 yazılır.</summary>
            public bool SolUc;
            /// <summary>Bu kesit, bir kirişin çizimde SAĞ ucunun (X1) mesneti ise true → o uca 3-4 yazılır.</summary>
            public bool SagUc;
            /// <summary>true → izden aşağı izdüşüm çizilir ve köşe numarası yazılır. Ana kirişe ortadan saplanan tali kiriş kesitinde false (kesit çizilir, izdüşüm/numara yok).</summary>
            public bool IzdusumVar;
        }

        private struct KirisAcilimUcBilgi
        {
            public short Code;
            public short JointStart;
            public short JointEnd;
        }

        /// <summary>KMX ofset 108 montaj, 124 gövde, 128 sol üst ilave, 132 sağ alt, 136 sağ üst, 140 sol alt: adet + çap (mm).</summary>
        private struct KirisAcilimMontajBilgi
        {
            public int Adet;
            public int Cap;
            public int DuzAdet;
            public int DuzCap;
            public int GovdeAdet;
            public int GovdeCap;
            public int SolUstAdet;
            public int SolUstCap;
            public int SagUstAdet;
            public int SagUstCap;
            public int SolAltAdet;
            public int SolAltCap;
            public int SagAltAdet;
            public int SagAltCap;
        }

        public bool DrawKirisAcilimlari(Database db, Editor ed, Point3d insert, string st4Path)
        {
            if (db == null || ed == null || _model?.Beams == null || _axisService == null)
            {
                ed?.WriteMessage("\nKIRISACILIMLARI: Model okunamadi.");
                return false;
            }

            var floors = new List<FloorInfo>(_model.Floors ?? new List<FloorInfo>());
            floors.Sort((a, b) => a.ElevationM.CompareTo(b.ElevationM));
            if (floors.Count == 0)
            {
                ed.WriteMessage("\nKIRISACILIMLARI: Kat bulunamadi.");
                return false;
            }

            int maxNo = 0;
            foreach (var b in _model.Beams)
            {
                if (b == null || b.IsWallFlag == 1) continue;
                int n = GetBeamNumero(b.BeamId);
                if (n > maxNo) maxNo = n;
            }
            int pad = GetLabelPadWidth(maxNo);

            int chainCount = 0;
            int spanCount = 0;
            double cursorY = insert.Y;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                EnsureLayers(tr, db);
                EnsurePlanLayer(tr, db, LayerKesitGorunus, 8, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerIzdusum, 253, LineWeight.LineWeight020, useDashed: true);
                var ucTipi = KirisAcilimReadUcTipi(st4Path);
                var montaj = KirisAcilimReadMontaj(st4Path);

                foreach (var floor in floors)
                {
                    var chains = BuildKirisAcilimChains(floor.FloorNo);
                    if (chains.Count == 0) continue;

                    double floorTop = cursorY;
                    foreach (var chain in chains)
                    {
                        DrawKirisAcilimChain(tr, ms, db, floor, chain, insert.X, ref cursorY, pad, ucTipi, montaj);
                        cursorY += KirisAcilimSeritAraCm;
                        chainCount++;
                        spanCount += chain.Count;
                    }

                    string kat = string.IsNullOrEmpty(floor.ShortName) ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : floor.ShortName;
                    string title = string.Format(CultureInfo.InvariantCulture, "{0}  KIRIS ACILIMI  (1/20)", kat);
                    DrawBeamLabel(tr, ms, db, new Point3d(insert.X, cursorY + 6.0, 0), title, 12.0, 0.0, LayerBaslik, bottomLeftAligned: true);
                    if (cursorY + 24.0 > floorTop) floorTop = cursorY + 24.0;
                    cursorY = floorTop + KirisAcilimKatAraCm;
                }

                tr.Commit();
            }

            if (chainCount == 0)
            {
                ed.WriteMessage("\nKIRISACILIMLARI: Cizilecek kiris yok.");
                return false;
            }

            ed.WriteMessage(
                "\nKIRISACILIMLARI: {0} serit, {1} aciklik (1/20, cm). Yatay kiris alttan, dusey kiris sagdan. Ayni isim tek cizim. Donati yok.",
                chainCount, spanCount);
            return true;
        }

        private List<List<KirisAcilimSpan>> BuildKirisAcilimChains(int floorNo)
        {
            var segs = new List<KirisAcilimSeg>();
            var seen = new HashSet<string>();
            foreach (var beam in GetRawBeamsOnFloor(floorNo))
            {
                if (beam == null || beam.IsWallFlag == 1 || beam.HeightCm <= 1.0) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p0)) continue;
                if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p1)) continue;
                Vector2d v = p1 - p0;
                if (v.Length < 1.0) continue;
                int a = Math.Min(beam.StartAxisId, beam.EndAxisId);
                int b = Math.Max(beam.StartAxisId, beam.EndAxisId);
                string key = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", beam.FixedAxisId, a, b);
                if (!seen.Add(key)) continue;
                segs.Add(new KirisAcilimSeg
                {
                    Beam = beam,
                    P0 = p0,
                    P1 = p1,
                    Kot0 = beam.Point1KotCm,
                    Kot1 = beam.Point2KotCm,
                    Cross0 = beam.StartAxisId,
                    Cross1 = beam.EndAxisId,
                    Dir = v.GetNormal(),
                    Len = v.Length
                });
            }

            var pieces = KirisAcilimSameNamePieces(segs);
            int n = pieces.Count;
            var linkSeg = new int[n, 2];
            var linkEnd = new int[n, 2];
            var bestAng = new double[n, 2];
            var bestDist = new double[n, 2];
            var bestSeg = new int[n, 2];
            var bestEnd = new int[n, 2];
            for (int i = 0; i < n; i++)
            {
                linkSeg[i, 0] = linkSeg[i, 1] = -1;
                linkEnd[i, 0] = linkEnd[i, 1] = -1;
                bestAng[i, 0] = bestAng[i, 1] = 999.0;
                bestDist[i, 0] = bestDist[i, 1] = double.MaxValue;
                bestSeg[i, 0] = bestSeg[i, 1] = -1;
                bestEnd[i, 0] = bestEnd[i, 1] = -1;
            }

            for (int i = 0; i < n; i++)
            {
                for (int e = 0; e < 2; e++)
                {
                    Point2d pi = KirisAcilimPieceEnd(pieces[i], e, out Vector2d di);
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;
                        for (int f = 0; f < 2; f++)
                        {
                            Point2d pj = KirisAcilimPieceEnd(pieces[j], f, out Vector2d dj);
                            double dist = pi.GetDistanceTo(pj);
                            if (!KirisAcilimUclarYakin(pi, di, pj, dist)) continue;
                            if (!KirisAcilimUclarBirlestir(pieces[i], e, pieces[j], f)) continue;
                            double ang = KirisAcilimHatAcisiDerece(di, dj);
                            if (ang > KirisAcilimMaxDonusDerece + 1e-6) continue;
                            if (ang < bestAng[i, e] - 1e-6
                                || (Math.Abs(ang - bestAng[i, e]) <= 1e-6 && dist < bestDist[i, e]))
                            {
                                bestAng[i, e] = ang;
                                bestDist[i, e] = dist;
                                bestSeg[i, e] = j;
                                bestEnd[i, e] = f;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int e = 0; e < 2; e++)
                {
                    int j = bestSeg[i, e];
                    int f = bestEnd[i, e];
                    if (j < 0) continue;
                    if (bestSeg[j, f] != i || bestEnd[j, f] != e) continue;
                    linkSeg[i, e] = j;
                    linkEnd[i, e] = f;
                }
            }

            var used = new bool[n];
            var chains = new List<List<KirisAcilimSpan>>();
            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                int entryEnd = linkSeg[i, 0] < 0 ? 0 : (linkSeg[i, 1] < 0 ? 1 : 0);
                var chain = new List<KirisAcilimSpan>();
                int cur = i;
                int enter = entryEnd;
                while (cur >= 0 && !used[cur])
                {
                    used[cur] = true;
                    var piece = pieces[cur];
                    if (enter == 1) KirisAcilimReverseSpans(piece);
                    chain.AddRange(piece);
                    int exit = enter == 0 ? 1 : 0;
                    int nxt = linkSeg[cur, exit];
                    enter = linkEnd[cur, exit];
                    cur = nxt;
                }
                if (chain.Count == 0) continue;
                KirisAcilimOrientToView(chain);
                chains.Add(chain);
            }

            chains.Sort(KirisAcilimSheetOrder);
            return chains;
        }

        /// <summary>Aynı kiriş numarası tek parça. Farklı numaralar sonra ≤25° ile birleşir.</summary>
        private static List<List<KirisAcilimSpan>> KirisAcilimSameNamePieces(List<KirisAcilimSeg> segs)
        {
            var pieces = new List<List<KirisAcilimSpan>>();
            var groups = new Dictionary<int, List<KirisAcilimSeg>>();
            foreach (var s in segs)
            {
                if (!groups.TryGetValue(s.Beam.BeamId, out var list))
                {
                    list = new List<KirisAcilimSeg>();
                    groups[s.Beam.BeamId] = list;
                }
                list.Add(s);
            }

            foreach (var kv in groups)
            {
                var list = kv.Value;
                KirisAcilimSeg longest = list[0];
                for (int i = 1; i < list.Count; i++)
                    if (list[i].Len > longest.Len) longest = list[i];
                Vector2d u = longest.Dir;
                var ordered = new List<KirisAcilimSpan>();
                foreach (var s in list)
                {
                    double t0 = s.P0.X * u.X + s.P0.Y * u.Y;
                    double t1 = s.P1.X * u.X + s.P1.Y * u.Y;
                    bool rev = t1 < t0;
                    ordered.Add(KirisAcilimSpanFrom(s, rev));
                }
                ordered.Sort((a, b) =>
                {
                    double ta = a.World0.X * u.X + a.World0.Y * u.Y;
                    double tb = b.World0.X * u.X + b.World0.Y * u.Y;
                    return ta.CompareTo(tb);
                });
                pieces.Add(ordered);
            }
            return pieces;
        }

        private static KirisAcilimSpan KirisAcilimSpanFrom(KirisAcilimSeg s, bool rev)
        {
            return new KirisAcilimSpan
            {
                Beam = s.Beam,
                Kot0 = rev ? s.Kot1 : s.Kot0,
                Kot1 = rev ? s.Kot0 : s.Kot1,
                World0 = rev ? s.P1 : s.P0,
                World1 = rev ? s.P0 : s.P1,
                Dir = rev ? -s.Dir : s.Dir,
                Cross0 = rev ? s.Cross1 : s.Cross0,
                Cross1 = rev ? s.Cross0 : s.Cross1
            };
        }

        private static void KirisAcilimReverseSpans(List<KirisAcilimSpan> spans)
        {
            spans.Reverse();
            foreach (var s in spans)
            {
                double k = s.Kot0; s.Kot0 = s.Kot1; s.Kot1 = k;
                Point2d w = s.World0; s.World0 = s.World1; s.World1 = w;
                int c = s.Cross0; s.Cross0 = s.Cross1; s.Cross1 = c;
                s.Dir = -s.Dir;
            }
        }

        private static Point2d KirisAcilimPieceEnd(List<KirisAcilimSpan> piece, int end, out Vector2d dir)
        {
            if (end == 0)
            {
                dir = piece[0].Dir;
                return piece[0].World0;
            }
            dir = piece[piece.Count - 1].Dir;
            return piece[piece.Count - 1].World1;
        }

        /// <summary>
        /// Yatay kiriş: planda aşağıdan yukarı bakılır, solda küçük X.
        /// Düşey kiriş: planda sağdan sola bakılır, solda küçük Y.
        /// </summary>
        private static void KirisAcilimOrientToView(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
            bool reverse = horizontal ? dx < -1.0 : dy < -1.0;
            if (reverse) KirisAcilimReverseSpans(chain);

            double x = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0)
                {
                    double gap = chain[i - 1].World1.GetDistanceTo(chain[i].World0);
                    if (gap > 1.0) x += gap;
                }
                double len = chain[i].World0.GetDistanceTo(chain[i].World1);
                chain[i].X0 = x;
                chain[i].X1 = x + len;
                x += len;
            }
        }

        private static int KirisAcilimSheetOrder(List<KirisAcilimSpan> a, List<KirisAcilimSpan> b)
        {
            int fa = KirisAcilimIsHorizontal(a) ? 0 : 1;
            int fb = KirisAcilimIsHorizontal(b) ? 0 : 1;
            if (fa != fb) return fa.CompareTo(fb);
            if (fa == 0)
                return KirisAcilimMid(a).Y.CompareTo(KirisAcilimMid(b).Y);
            return KirisAcilimMid(b).X.CompareTo(KirisAcilimMid(a).X);
        }

        private static bool KirisAcilimIsHorizontal(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            return Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        }

        private static Point2d KirisAcilimMid(List<KirisAcilimSpan> chain)
        {
            Point2d a = chain[0].World0;
            Point2d b = chain[chain.Count - 1].World1;
            return new Point2d(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
        }

        private static double KirisAcilimHatAcisiDerece(Vector2d u, Vector2d v)
        {
            double d = Math.Abs(u.X * v.X + u.Y * v.Y);
            if (d > 1.0) d = 1.0;
            return Math.Acos(d) * (180.0 / Math.PI);
        }

        private void DrawKirisAcilimChain(
            Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor,
            List<KirisAcilimSpan> chain, double x0, ref double cursorY, int pad,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            int m = chain.Count;
            var sup = new KirisAcilimSupport[m + 1];
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            double baseCm = (_model.BuildingBaseKotu + floor.ElevationM) * 100.0;

            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan left, out KirisAcilimSpan right);
                Point2d world = right != null ? right.World0 : left.World1;
                Vector2d dir = right != null ? right.Dir : left.Dir;
                int cross = right != null ? right.Cross0 : left.Cross1;
                int cross2 = left != null ? left.Cross1 : cross;
                double kot = KirisAcilimJointTop(left, right);
                double soffit = KirisAcilimJointSoffit(left, right, kot);
                BeamInfo hostBeam = right != null ? right.Beam : left.Beam;
                BeamInfo otherBeam = left != null ? left.Beam : null;
                bool has = TryKirisAcilimSupportWidth(
                    floor.FloorNo, world, dir, cross, cross2,
                    baseCm + kot, baseCm + soffit, hostBeam, otherBeam,
                    out double xLo, out double xHi, out bool isWall, out bool extendUp, out bool extendDown, out int columnNo);
                sup[i] = new KirisAcilimSupport
                {
                    X0 = has ? xLo : 0,
                    X1 = has ? xHi : 0,
                    IsWall = isWall,
                    ExtendUp = has && extendUp,
                    ExtendDown = has && extendDown,
                    ColumnNo = has ? columnNo : 0
                };
                double yLow = soffit - (sup[i].ExtendDown ? KirisAcilimKutupCm : 0);
                if (has)
                {
                    double izBot = yLow - 10.0 - 200.0;
                    if (izBot < minY) minY = izBot;
                }
                double yHigh = kot + (sup[i].ExtendUp ? KirisAcilimKutupCm : 0);
                if (yLow - 8.0 < minY) minY = yLow - 8.0;
                if (yHigh + 28.0 > maxY) maxY = yHigh + 28.0;
            }

            var gorunus = KirisAcilimCollectKesitGorunus(floor.FloorNo, chain, ucTipi);
            foreach (var spm in gorunus)
            {
                double izAlt = KirisAcilimIzAlt(chain, spm);
                double izBot = izAlt - 10.0 - 200.0;
                if (izBot < minY) minY = izBot;
                if (spm.KotTop + 8.0 > maxY) maxY = spm.KotTop + 8.0;
            }

            double yShift = cursorY - minY;
            double chainTop = yShift + maxY;
            KirisAcilimSerbestUcUzatma(chain, gorunus, out double extStart, out double extEnd);
            var kolonGrow0 = new double[m];
            var kolonGrow1 = new double[m];
            KirisAcilimKolonaUzat(chain, sup, kolonGrow0, kolonGrow1);
            double minS = chain[0].X0;
            double back0 = extStart;
            if (kolonGrow0[0] > back0) back0 = kolonGrow0[0];
            if (chain[0].X0 - back0 < minS) minS = chain[0].X0 - back0;
            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan leftS, out KirisAcilimSpan rightS);
                double stationS = rightS != null ? rightS.X0 : leftS.X1;
                if (sup[i].X1 - sup[i].X0 > 2.0)
                {
                    double leftEdge = stationS + sup[i].X0;
                    if (leftEdge < minS) minS = leftEdge;
                }
            }
            foreach (KirisAcilimSaplama izS in gorunus)
                if (izS.X0 < minS) minS = izS.X0;
            if (minS < -0.5) x0 -= minS;

            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var concrete = new List<Geometry>();
            int run = 0;
            while (run < m)
            {
                int runEnd = run;
                while (runEnd + 1 < m && chain[runEnd + 1].Beam.BeamId == chain[run].Beam.BeamId)
                    runEnd++;
                KirisAcilimDrawRun(tr, ms, db, chain, run, runEnd, x0, yShift, pad, gf, concrete, sup, ucTipi, extStart, extEnd, kolonGrow0, kolonGrow1, montaj);
                run = runEnd + 1;
            }

            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan leftC, out KirisAcilimSpan rightC);
                double stationC = rightC != null ? rightC.X0 : leftC.X1;
                double kotC = KirisAcilimJointTop(leftC, rightC);
                double soffitC = KirisAcilimJointSoffit(leftC, rightC, kotC);
                KirisAcilimSupport spC = sup[i];
                if (spC.X1 - spC.X0 <= 2.0) continue;
                double yTopC = yShift + kotC;
                double yBotC = yShift + soffitC;
                double xLc = x0 + stationC + spC.X0;
                double xRc = x0 + stationC + spC.X1;
                Polygon colPoly = KirisAcilimRect(gf, xLc, yBotC, xRc, yTopC);
                if (colPoly != null) concrete.Add(colPoly);
            }

            var kolonTrim = new List<KirisAcilimKolonTrim>();
            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan leftT, out KirisAcilimSpan rightT);
                double stationT = rightT != null ? rightT.X0 : leftT.X1;
                KirisAcilimSupport spT = sup[i];
                if (spT.X1 - spT.X0 <= 2.0) continue;
                kolonTrim.Add(new KirisAcilimKolonTrim
                {
                    X0 = x0 + stationT + spT.X0,
                    X1 = x0 + stationT + spT.X1,
                    YBot = yShift + KirisAcilimJointSoffit(leftT, rightT, KirisAcilimJointTop(leftT, rightT)),
                    YTop = yShift + KirisAcilimJointTop(leftT, rightT),
                    TrimTop = spT.ExtendUp,
                    TrimBot = spT.ExtendDown
                });
            }
            foreach (KirisAcilimSaplama iz in gorunus)
            {
                if (!KirisAcilimIzKirisSiniri(chain, iz, out double beamBot, out double beamTop)) continue;
                bool above = iz.KotTop > beamTop + 0.5;
                bool below = iz.KotBot < beamBot - 0.5;
                if (!above && !below) continue;
                kolonTrim.Add(new KirisAcilimKolonTrim
                {
                    X0 = x0 + iz.X0,
                    X1 = x0 + iz.X1,
                    YBot = yShift + beamBot,
                    YTop = yShift + beamTop,
                    TrimTop = above,
                    TrimBot = below
                });
            }

            if (concrete.Count > 0)
            {
                Geometry united = null;
                try
                {
                    united = concrete.Count == 1 ? concrete[0] : CascadedPolygonUnion.Union(concrete);
                    if (united != null && !united.IsEmpty) united = united.Buffer(0);
                }
                catch { united = null; }
                if (united != null && !united.IsEmpty)
                    KirisAcilimDrawBeton(tr, ms, united, kolonTrim);
                else
                {
                    foreach (var g in concrete)
                        KirisAcilimDrawBeton(tr, ms, g, kolonTrim);
                }
            }

            foreach (KirisAcilimSaplama spm in gorunus)
            {
                KirisAcilimDrawIz(tr, ms, chain, spm, x0, yShift);
                // Ana kirişe ortadan saplanan tali kiriş kesiti: iz çizilir ama aşağı izdüşüm + numara çizilmez.
                if (!spm.IzdusumVar) continue;
                double yIz = yShift + KirisAcilimIzAlt(chain, spm) - 10.0;
                double xa = x0 + spm.X0;
                double xb = x0 + spm.X1;
                KirisAcilimCizgi(tr, ms, xa, yIz, xa, yIz - 200.0, LayerIzdusum);
                KirisAcilimCizgi(tr, ms, xb, yIz, xb, yIz - 200.0, LayerIzdusum);
                // Köşe numarası kirişe bakan (iç) yüzde, HOST kirişin üst/alt hizasında: sol uçta 1-2, sağ uçta 3-4.
                // Numara kesitin (diğer kirişin) kotunda değil, bu açılımın kirişinin kotunda yazılır (kolon mesnetiyle aynı kural).
                if (spm.SolUc)
                {
                    // sol uç: kiriş sağda → iç yüz kesitin sağ kenarı (X1), numara kirişe doğru (sağa).
                    double xIc = x0 + spm.X1;
                    double top = yShift + chain[0].Kot0;
                    double bot = top - chain[0].Beam.HeightCm;
                    KirisAcilimNoktaNo(tr, ms, db, xIc, top, "1", saga: true, ust: true);
                    KirisAcilimNoktaNo(tr, ms, db, xIc, bot, "2", saga: true, ust: false);
                }
                if (spm.SagUc)
                {
                    // sağ uç: kiriş solda → iç yüz kesitin sol kenarı (X0), numara kirişe doğru (sola).
                    double xIc = x0 + spm.X0;
                    double top = yShift + chain[chain.Count - 1].Kot1;
                    double bot = top - chain[chain.Count - 1].Beam.HeightCm;
                    KirisAcilimNoktaNo(tr, ms, db, xIc, top, "3", saga: false, ust: true);
                    KirisAcilimNoktaNo(tr, ms, db, xIc, bot, "4", saga: false, ust: false);
                }
            }

            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan left, out KirisAcilimSpan right);
                double station = right != null ? right.X0 : left.X1;
                double kot = KirisAcilimJointTop(left, right);
                double soffit = KirisAcilimJointSoffit(left, right, kot);
                double yTop = yShift + kot;
                double yBot = yShift + soffit;
                KirisAcilimSupport sp = sup[i];
                if (sp.X1 - sp.X0 <= 2.0) continue;
                double xL = x0 + station + sp.X0;
                double xR = x0 + station + sp.X1;
                if (sp.ExtendUp)
                {
                    double yCut = yTop + KirisAcilimKutupCm;
                    KirisAcilimKirisCizgi(tr, ms, xL, yTop, xL, yCut);
                    KirisAcilimKirisCizgi(tr, ms, xR, yTop, xR, yCut);
                    KirisAcilimKesitSiniri(tr, ms, xL, xR, yCut, 0);
                }
                if (sp.ExtendDown)
                {
                    double yCut = yBot - KirisAcilimKutupCm;
                    KirisAcilimKirisCizgi(tr, ms, xL, yBot, xL, yCut);
                    KirisAcilimKirisCizgi(tr, ms, xR, yBot, xR, yCut);
                    KirisAcilimKesitSiniri(tr, ms, xL, xR, yCut, 0);
                }
                double yKolonAlt = sp.ExtendDown ? yBot - KirisAcilimKutupCm : yBot;
                double yIz0 = yKolonAlt - 10.0;
                KirisAcilimCizgi(tr, ms, xL, yIz0, xL, yIz0 - 200.0, LayerIzdusum);
                KirisAcilimCizgi(tr, ms, xR, yIz0, xR, yIz0 - 200.0, LayerIzdusum);
                if (sp.ColumnNo > 0)
                    KirisAcilimKolonYaz(tr, ms, db, floor, sp.ColumnNo, xR, yBot);
            }

            if (chainTop > cursorY) cursorY = chainTop;
        }

        private static void KirisAcilimSpanAt(List<KirisAcilimSpan> chain, int i, out KirisAcilimSpan left, out KirisAcilimSpan right)
        {
            left = i > 0 ? chain[i - 1] : null;
            right = i < chain.Count ? chain[i] : null;
        }

        private static double KirisAcilimJointTop(KirisAcilimSpan left, KirisAcilimSpan right)
        {
            if (left != null && right != null) return Math.Max(left.Kot1, right.Kot0);
            return right != null ? right.Kot0 : left.Kot1;
        }

        /// <summary>
        /// Uçlar 5 cm içindeyse birleşir. Aynı doğru üzerinde kolon kadar boşluk (100 cm’ye kadar, yanal sapma ≤ 10 cm) da birleşir.
        /// </summary>
        private static bool KirisAcilimUclarYakin(Point2d pi, Vector2d di, Point2d pj, double dist)
        {
            if (dist <= KirisAcilimUcToleransCm) return true;
            if (dist > KirisAcilimAyniHatBoslukCm || di.Length < 1e-9) return false;
            Vector2d u = di.GetNormal();
            double lat = Math.Abs((pj.X - pi.X) * u.Y - (pj.Y - pi.Y) * u.X);
            return lat <= KirisAcilimAyniHatSapmaCm;
        }

        /// <summary>
        /// Altta kalan kirişin üst kotu, üsttekinin alt kotundan aşağıdaysa örtüşme yoktur.
        /// Örtüşme 20 cm’den kısaysa aynı şeride bağlanmaz.
        /// </summary>
        private static bool KirisAcilimUclarBirlestir(List<KirisAcilimSpan> a, int endA, List<KirisAcilimSpan> b, int endB)
        {
            KirisAcilimUcKot(a, endA, out double topA, out double botA);
            KirisAcilimUcKot(b, endB, out double topB, out double botB);
            double birlesim = Math.Min(topA, topB) - Math.Max(botA, botB);
            return birlesim >= KirisAcilimMinBirlestirmeCm - 1e-6;
        }

        private static void KirisAcilimUcKot(List<KirisAcilimSpan> piece, int end, out double top, out double bot)
        {
            KirisAcilimSpan s = end == 0 ? piece[0] : piece[piece.Count - 1];
            top = end == 0 ? s.Kot0 : s.Kot1;
            bot = top - s.Beam.HeightCm;
        }

        /// <summary>
        /// Kolon kiriş ucunun dışında kaldıysa (eksen düğümü kolon yüzüne yetişmiyor) betonu 1 cm kolonun içine alır.
        /// Aralık 20 cm’den büyükse uzatılmaz; açıklıktaki boşluk betonla doldurulmaz.
        /// </summary>
        private static void KirisAcilimKolonaUzat(
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, double[] grow0, double[] grow1)
        {
            int m = chain.Count;
            for (int i = 0; i <= m; i++)
            {
                if (sup[i].X1 - sup[i].X0 <= 2.0) continue;
                double station = i < m ? chain[i].X0 : chain[m - 1].X1;
                double c0 = station + sup[i].X0;
                double c1 = station + sup[i].X1;
                if (i < m)
                {
                    double b0 = chain[i].X0;
                    if (b0 > c1 - 0.5 && b0 - c1 <= KirisAcilimKolonYakinCm + 1.0)
                    {
                        double need = b0 - (c1 - 1.0);
                        if (need > grow0[i]) grow0[i] = need;
                    }
                }
                if (i > 0)
                {
                    double b1 = chain[i - 1].X1;
                    if (c0 > b1 + 0.5 && c0 - b1 <= KirisAcilimKolonYakinCm + 1.0)
                    {
                        double need = (c0 + 1.0) - b1;
                        if (need > grow1[i - 1]) grow1[i - 1] = need;
                    }
                }
            }
        }

        /// <summary>
        /// Saplama ve konsol ucunu, o uçtaki kiriş izinin dış yüzüne kadar uzatır.
        /// İzin iç yüzünde bırakmaz; izden öteye geçmez.
        /// </summary>
        private static void KirisAcilimSerbestUcUzatma(
            List<KirisAcilimSpan> chain, List<KirisAcilimSaplama> izler,
            out double extStart, out double extEnd)
        {
            extStart = extEnd = 0;
            if (chain == null || chain.Count == 0 || izler == null) return;
            double xBas = chain[0].X0;
            double xSon = chain[chain.Count - 1].X1;
            foreach (var sp in izler)
            {
                double half = 0.5 * (sp.X1 - sp.X0);
                double mid = 0.5 * (sp.X0 + sp.X1);
                if (Math.Abs(mid - xSon) <= half + 2.0 && sp.X1 > xSon + 0.5)
                {
                    double hang = sp.X1 - xSon;
                    if (hang > extEnd) extEnd = hang;
                }
                if (Math.Abs(mid - xBas) <= half + 2.0 && sp.X0 < xBas - 0.5)
                {
                    double hang = xBas - sp.X0;
                    if (hang > extStart) extStart = hang;
                }
            }
        }

        private static double KirisAcilimJointSoffit(KirisAcilimSpan left, KirisAcilimSpan right, double top)
        {
            double soffit = top;
            if (left != null) soffit = Math.Min(soffit, left.Kot1 - left.Beam.HeightCm);
            if (right != null) soffit = Math.Min(soffit, right.Kot0 - right.Beam.HeightCm);
            if (left == null && right != null) soffit = right.Kot0 - right.Beam.HeightCm;
            if (right == null && left != null) soffit = left.Kot1 - left.Beam.HeightCm;
            return soffit;
        }

        private void KirisAcilimDrawRun(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift, int pad,
            GeometryFactory gf, List<Geometry> concrete, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi,
            double extStartCm, double extEndCm, double[] kolonGrow0, double[] kolonGrow1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            int part = i0;
            while (part <= i1)
            {
                int j = part;
                while (j + 1 <= i1 && chain[j + 1].X0 - chain[j].X1 < 2.0)
                    j++;
                var pts = new List<Point2d>();
                KirisAcilimSpan first = chain[part];
                double back = part == 0 ? extStartCm : 0;
                if (kolonGrow0 != null && kolonGrow0[part] > back) back = kolonGrow0[part];
                double xBas = x0 + first.X0 - back;
                pts.Add(new Point2d(xBas, yShift + first.Kot0 - first.Beam.HeightCm));
                for (int k = part; k <= j; k++)
                {
                    KirisAcilimSpan s = chain[k];
                    double fwd = k == j && j == chain.Count - 1 ? extEndCm : 0;
                    if (k == j && kolonGrow1 != null && kolonGrow1[j] > fwd) fwd = kolonGrow1[j];
                    double x = x0 + s.X1 + fwd;
                    pts.Add(new Point2d(x, yShift + s.Kot1 - s.Beam.HeightCm));
                }
                for (int k = j; k >= part; k--)
                {
                    KirisAcilimSpan s = chain[k];
                    double fwd = k == j && j == chain.Count - 1 ? extEndCm : 0;
                    if (k == j && kolonGrow1 != null && kolonGrow1[j] > fwd) fwd = kolonGrow1[j];
                    double x = x0 + s.X1 + fwd;
                    pts.Add(new Point2d(x, yShift + s.Kot1));
                }
                pts.Add(new Point2d(xBas, yShift + first.Kot0));
                Polygon beamPoly = KirisAcilimPolygon(gf, pts);
                if (beamPoly != null) concrete.Add(beamPoly);
                part = j + 1;
            }

            double net0 = chain[i0].X0;
            double net1 = chain[i1].X1;
            // Kolon, sağdaki açıklığın başlangıcına (yoksa bu açıklığın bitişine) çizilir.
            // İki açıklık aksı arasında boşluk varsa X1 ile sonraki X0 aynı nokta değildir;
            // numara ve donatı bu boşluğu değil, çizilen kolon yüzünü kullanır.
            if (sup != null && i0 < sup.Length && sup[i0].X1 - sup[i0].X0 > 2.0)
            {
                double face = chain[i0].X0 + sup[i0].X1;
                if (face < net1 - 5.0) net0 = face;
            }
            if (sup != null && i1 + 1 < sup.Length && sup[i1 + 1].X1 - sup[i1 + 1].X0 > 2.0)
            {
                double station = i1 + 1 < chain.Count ? chain[i1 + 1].X0 : chain[i1].X1;
                double face = station + sup[i1 + 1].X0;
                if (face > net0 + 5.0) net1 = face;
            }
            double xMid = 0.5 * (net0 + net1);
            double yLine = yShift + KirisAcilimKotAt(chain, i0, i1, xMid);
            DrawBeamLabel(tr, ms, db,
                new Point3d(x0 + xMid, yLine, 0),
                KirisAcilimEtiket(chain[i0].Beam, pad),
                KirisAcilimBaslikYukseklikCm, 0.0, LayerKirisYazisi, useMiddleCenter: true);
            KirisAcilimYaziKutusuUstune(tr, yLine + 3.0);
            KirisAcilimUcNoktalari(tr, ms, db, chain, i0, i1, sup, ucTipi, x0, yShift, net0, net1);
            KirisAcilimMontajDonatiYedek(tr, ms, db, chain, i0, i1, x0, yShift, net0, net1, montaj);
            KirisAcilimDuzDonatiYedek(tr, ms, db, chain, i0, i1, x0, yShift, net0, net1, montaj);
            KirisAcilimGovdeDonati(tr, ms, db, chain, i0, i1, x0, yShift, net0, net1, montaj);
            KirisAcilimIlaveDonati(tr, ms, db, chain, i0, i1, x0, yShift, net0, net1, montaj);
        }

        /// <summary>Gövde donatısı: 1 ve 2 ile 3 ve 4 arasında, montaj ile alt düz arasını eşit böler.</summary>
        private void KirisAcilimGovdeDonati(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            if (m.GovdeAdet <= 0 || m.GovdeCap <= 0) return;

            double xA = x0 + net0;
            double xB = x0 + net1;
            if (xB - xA < 1.0) return;

            double h = beam.HeightCm;
            double ust = KirisAcilimMontajPaspayiCm;
            double alt = h - KirisAcilimDuzPaspayiCm;
            int gorunen = (m.GovdeAdet + 1) / 2;
            if (gorunen < 1) gorunen = 1;
            double n = gorunen + 1.0;
            for (int k = 1; k <= gorunen; k++)
            {
                double ofs = ust + (alt - ust) * k / n;
                double yA = yShift + KirisAcilimKotAt(chain, i0, i1, net0) - ofs;
                double yB = yShift + KirisAcilimKotAt(chain, i0, i1, net1) - ofs;
                KirisAcilimCizgi(tr, ms, xA, yA, xB, yB, KirisAcilimDonatiLayer, KirisAcilimRenkGovde);
            }

            int sag = gorunen;
            int sol = m.GovdeAdet - gorunen;
            if (sol < 1) sol = sag;
            string lbl = sag.ToString(CultureInfo.InvariantCulture) + "\u00F8" + m.GovdeCap.ToString(CultureInfo.InvariantCulture)
                + " / " + sol.ToString(CultureInfo.InvariantCulture) + "\u00F8" + m.GovdeCap.ToString(CultureInfo.InvariantCulture);
            double chainMid = 0.5 * (net0 + net1);
            double yMid = yShift + KirisAcilimKotAt(chain, i0, i1, chainMid) - h * 0.5;
            DrawBeamLabel(tr, ms, db, new Point3d(x0 + chainMid, yMid, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
        }

        /// <summary>
        /// Kolon çizgisinin kirişi kestiği dört köşe. Numara çizimdeki soldan sağa:
        /// 1 sol üst, 2 sol alt, 3 sağ üst, 4 sağ alt. KMX sol/sağ bu numaralar değildir;
        /// KMX solu kirişin ST4 başlangıcı, sağı bitişidir. Bakış açısı bunları yer değiştirir.
        /// KB-062: çizimde sol SB-52 = bitiş = sağ üst ilave 2Ø12; sağ SB-38 = başlangıç = sol üst ilave 5Ø14.
        /// </summary>
        private void KirisAcilimUcNoktalari(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, KirisAcilimSupport[] sup,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi,
            double x0, double yShift, double net0, double net1)
        {
            double h = chain[i0].Beam.HeightCm;
            bool solKolon = sup != null && i0 < sup.Length && sup[i0].X1 - sup[i0].X0 > 2.0;
            bool sagKolon = sup != null && i1 + 1 < sup.Length && sup[i1 + 1].X1 - sup[i1 + 1].X0 > 2.0;

            // BEA düğümü 0 ise uç saplama/konsol/split noktasıdır; o uç kesit ile numaralanır, kolon numarası tekrar yazılmaz.
            // Sol uç için ilk açıklığın, sağ uç için son açıklığın kaydı kullanılır (split ana kirişte uçlar farklı kayıtlardadır).
            if (ucTipi != null)
            {
                BeamInfo solBeam = chain[i0].Beam;
                if (solKolon && solBeam != null && ucTipi.TryGetValue(solBeam, out KirisAcilimUcBilgi solUc))
                {
                    bool solBaslangic = chain[i0].Cross0 == solBeam.StartAxisId;
                    if ((solBaslangic ? solUc.JointStart : solUc.JointEnd) == 0) solKolon = false;
                }
                BeamInfo sagBeam = chain[i1].Beam;
                if (sagKolon && sagBeam != null && ucTipi.TryGetValue(sagBeam, out KirisAcilimUcBilgi sagUc))
                {
                    bool sagBaslangic = chain[i1].Cross1 == sagBeam.StartAxisId;
                    if ((sagBaslangic ? sagUc.JointStart : sagUc.JointEnd) == 0) sagKolon = false;
                }
            }

            if (solKolon)
            {
                double top = yShift + KirisAcilimKotAt(chain, i0, i1, net0);
                KirisAcilimNoktaNo(tr, ms, db, x0 + net0, top, "1", saga: true, ust: true);
                KirisAcilimNoktaNo(tr, ms, db, x0 + net0, top - h, "2", saga: true, ust: false);
            }
            if (sagKolon)
            {
                double top = yShift + KirisAcilimKotAt(chain, i0, i1, net1);
                KirisAcilimNoktaNo(tr, ms, db, x0 + net1, top, "3", saga: false, ust: true);
                KirisAcilimNoktaNo(tr, ms, db, x0 + net1, top - h, "4", saga: false, ust: false);
            }
        }

        /// <summary>İlave donatı seviyeleri. Üst ilaveler kiriş üstünden, alt ilaveler altından 4 cm içeride; çift sıra 1 çap aralıkla.</summary>
        private static double[] KirisAcilimIlaveSeviye(double h, bool ust)
        {
            double y = ust ? KirisAcilimMontajPaspayiCm : h - KirisAcilimDuzPaspayiCm;
            return new[] { y, y - 1.2 };
        }

        /// <summary>
        /// İlave donatı: düğüm noktasından kiriş içine, yönetmelik boyunda.
        /// Üst ilave: max(ln/4, 50φ, lb_I, d+20φ) — TBDY 7.4.3.1(a) Şekil 7.7, STA OPS, TS 500 7.3/9.1.2.1.
        /// Alt ilave: max(lb_II, 50φ) — TBDY 7.4.3.1(c). Kenar mesnette yatay ≤ 0.4·lb ise 12φ kanca payı düşülür.
        /// Renk köşe numarasına göredir: 1 sol üst ACI 5, 3 sağ üst ACI 2, 2 sol alt ACI 30, 4 sağ alt ACI 1.
        /// KMX solu kirişin ST4 başlangıç (StartAxisId), sağı bitiş ucudur. Bakış açısı (yatayda soldan sağa,
        /// düşeyde alttan üste) uçları çevirdiği için sabit "sol=sağ ilave" eşlemesi yapılmaz; çizimin sol ucu
        /// hangi uca (başlangıç/bitiş) denk geliyorsa ilave ona göre yerleştirilir.
        /// </summary>
        private void KirisAcilimIlaveDonati(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> ilave)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || ilave == null || !ilave.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;

            double L = chain[i1].X1 - chain[i0].X0;
            if (L < 1.0) return;
            double h = beam.HeightCm;
            double xA = x0 + net0;
            double xB = x0 + net1;
            double topA = yShift + KirisAcilimKotAt(chain, i0, i1, net0);
            double topB = yShift + KirisAcilimKotAt(chain, i0, i1, net1);
            double destekSol = Math.Abs(net0 - chain[i0].X0);
            double destekSag = Math.Abs(chain[i1].X1 - net1);

            // İlave boyu NET açıklığın ln/4'üdür (TBDY 7.4.3.1a): kolon/perde yüzleri arası.
            // Eskiden L/4 alınıp mesnet genişliği bir kez daha çıkarılıyordu; perde/duvar mesnetlerde
            // bu, boyu sebepsiz kısaltıyordu. Artık önce net açıklık bulunur, mesnet payı tekrar düşülmez.
            double lnNet = L - destekSol - destekSag;
            if (lnNet < 1.0) lnNet = L;   // mesnetler açıklığı yutmuşsa eksen-eksen açıklığa dön

            // Çizimin sol ucu ST4 başlangıcı mı? KMX solu başlangıç, sağı bitiş ucu verir.
            // Kirişin plandaki yönü/açısı bakış açısını çevirdiğinden uçlar kirişe göre değişir.
            bool solUcBaslangic = chain[i0].Cross0 == beam.StartAxisId;

            int luAdet, luCap, laAdet, laCap, ruAdet, ruCap, raAdet, raCap;
            if (solUcBaslangic)
            {
                luAdet = m.SolUstAdet; luCap = m.SolUstCap;
                laAdet = m.SolAltAdet; laCap = m.SolAltCap;
                ruAdet = m.SagUstAdet; ruCap = m.SagUstCap;
                raAdet = m.SagAltAdet; raCap = m.SagAltCap;
            }
            else
            {
                luAdet = m.SagUstAdet; luCap = m.SagUstCap;
                laAdet = m.SagAltAdet; laCap = m.SagAltCap;
                ruAdet = m.SolUstAdet; ruCap = m.SolUstCap;
                raAdet = m.SolAltAdet; raCap = m.SolAltCap;
            }

            double luU = KirisAcilimUstIlaveKesimCm(lnNet, h, luCap / 10.0, luCap);
            double laU = KirisAcilimAltIlaveKesimCm(laCap);
            double ruU = KirisAcilimUstIlaveKesimCm(lnNet, h, ruCap / 10.0, ruCap);
            double raU = KirisAcilimAltIlaveKesimCm(raCap);

            KirisAcilimIlaveParca(tr, ms, db, topA, topA - h, xA, xA + luU, luAdet, luCap, true, KirisAcilimRenkIlaveSolUst);
            KirisAcilimIlaveParca(tr, ms, db, topA, topA - h, xA, xA + laU, laAdet, laCap, false, KirisAcilimRenkIlaveSolAlt);
            KirisAcilimIlaveParca(tr, ms, db, topB, topB - h, xB - ruU, xB, ruAdet, ruCap, true, KirisAcilimRenkIlaveSagUst);
            KirisAcilimIlaveParca(tr, ms, db, topB, topB - h, xB - raU, xB, raAdet, raCap, false, KirisAcilimRenkIlaveSagAlt);
        }

        private void KirisAcilimIlaveParca(
            Transaction tr, BlockTableRecord ms, Database db,
            double topY, double botY, double xA, double xB,
            int adet, int cap, bool ust, short renk)
        {
            if (adet <= 0 || cap <= 0) return;
            if (xB - xA < 1.0) return;
            double capCm = cap / 10.0;
            double p = ust ? KirisAcilimMontajPaspayiCm : KirisAcilimDuzPaspayiCm;
            double yUst = ust ? topY - p : botY + p;
            double yIc = ust ? topY - (p + capCm) : botY + (p + capCm);
            KirisAcilimCizgi(tr, ms, xA, yUst, xB, yUst, KirisAcilimDonatiLayer, renk);
            KirisAcilimCizgi(tr, ms, xA, yIc, xB, yIc, KirisAcilimDonatiLayer, renk);
            double uzunluk = Math.Abs(xB - xA);
            string lbl = adet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + cap.ToString(CultureInfo.InvariantCulture)
                + " L=" + uzunluk.ToString("0", CultureInfo.InvariantCulture);
            double xm = 0.5 * (xA + xB);
            double ym = ust ? yUst - 4.0 : yUst + 4.0;
            DrawBeamLabel(tr, ms, db, new Point3d(xm, ym, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
        }
        private void KirisAcilimNoktaNo(
            Transaction tr, BlockTableRecord ms, Database db, double x, double y, string no, bool saga, bool ust)
        {
            DrawBeamLabel(tr, ms, db, new Point3d(x, y, 0), no, 8.0, 0.0, LayerYazi, bottomLeftAligned: true);
            if (tr == null || _lastBeamLabelId.IsNull) return;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return;
            Extents3d ext;
            try { ext = txt.GeometricExtents; }
            catch { return; }
            double dx = saga ? x - ext.MinPoint.X : x - ext.MaxPoint.X;
            double dy = ust ? (y - 4.0) - ext.MaxPoint.Y : (y + 4.0) - ext.MinPoint.Y;
            if (Math.Abs(dx) < 0.05 && Math.Abs(dy) < 0.05) return;
            txt.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
        }

        /// <summary>Basit dikdörtgen çubuk: 4 cm paspayı içeride, kendi renginde.</summary>
        private static void KirisAcilimCubuk(
            Transaction tr, BlockTableRecord ms, double topY, double botY,
            double xA, double xB, bool ust, short renk)
        {
            double p = KirisAcilimMontajPaspayiCm;
            double y = ust ? topY - p : botY + p;
            KirisAcilimCizgi(tr, ms, xA, y, xB, y, KirisAcilimDonatiLayer, renk);
        }

        /// <summary>
        /// Montaj donatısı (sürekli üst çubuk): 1 (sol üst) ile 3 (sağ üst) arasına,
        /// kiriş üstünden net paspayı + yarıçap içeride tek çizgi. Etiket "nøφ" çizginin altında.
        /// </summary>
        private void KirisAcilimMontajDonatiYedek(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            if (m.Adet <= 0 || m.Cap <= 0) return;

            double xA = x0 + net0;
            double xB = x0 + net1;
            if (xB - xA < 1.0) return;

            double top0 = yShift + KirisAcilimKotAt(chain, i0, i1, net0);
            double top1 = yShift + KirisAcilimKotAt(chain, i0, i1, net1);
            double yA = top0 - KirisAcilimMontajPaspayiCm;
            double yB = top1 - KirisAcilimMontajPaspayiCm;
            KirisAcilimCizgi(tr, ms, xA, yA, xB, yB, KirisAcilimDonatiLayer, KirisAcilimRenkMontaj);
        }

        /// <summary>
        /// Alt düz donatının eski tek çizgi sürümü. Yeni sürüm 2–4 arasını yüz başına böler.
        /// </summary>
        private void KirisAcilimDuzDonatiYedek(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            if (m.DuzAdet <= 0 || m.DuzCap <= 0) return;

            double xA = x0 + net0;
            double xB = x0 + net1;
            if (xB - xA < 1.0) return;

            double h = beam.HeightCm;
            double bot0 = yShift + KirisAcilimKotAt(chain, i0, i1, net0) - h;
            double bot1 = yShift + KirisAcilimKotAt(chain, i0, i1, net1) - h;
            KirisAcilimCizgi(tr, ms, xA, bot0 + KirisAcilimDuzPaspayiCm, xB, bot1 + KirisAcilimDuzPaspayiCm, KirisAcilimDonatiLayer, KirisAcilimRenkDuz);
        }

        private static double KirisAcilimKotAt(List<KirisAcilimSpan> chain, int i0, int i1, double x)
        {
            for (int i = i0; i <= i1; i++)
            {
                KirisAcilimSpan s = chain[i];
                if (x < s.X0 - 1.0 || x > s.X1 + 1.0) continue;
                double span = s.X1 - s.X0;
                double u = span < 1.0 ? 0.0 : (x - s.X0) / span;
                if (u < 0) u = 0;
                if (u > 1) u = 1;
                return s.Kot0 + (s.Kot1 - s.Kot0) * u;
            }
            return chain[i0].Kot0;
        }

        /// <summary>Kolon adı: yazı kutusunun solu kolon sağının 2 cm sağında, üstü kiriş alt çizgisinin 7 cm altında.</summary>
        private void KirisAcilimKolonYaz(
            Transaction tr, BlockTableRecord ms, Database db, FloorInfo floor, int colNo, double xSag, double yAlt)
        {
            string ad = KirisAcilimKolonAdi(floor, colNo);
            if (string.IsNullOrEmpty(ad)) return;
            DrawBeamLabel(tr, ms, db, new Point3d(xSag, yAlt, 0), ad, 8.0, 0.0, LayerKolonIsmi, bottomLeftAligned: true);
            if (tr == null || _lastBeamLabelId.IsNull) return;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return;
            Extents3d ext;
            try { ext = txt.GeometricExtents; }
            catch { return; }
            double dx = (xSag + 2.0) - ext.MinPoint.X;
            double dy = (yAlt - 7.0) - ext.MaxPoint.Y;
            if (Math.Abs(dx) < 0.05 && Math.Abs(dy) < 0.05) return;
            txt.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
        }

        private string KirisAcilimKolonAdi(FloorInfo floor, int colNo)
        {
            if (colNo <= 0 || floor == null || _model?.Floors == null) return null;
            int idx = 0;
            for (int i = 0; i < _model.Floors.Count; i++)
            {
                if (_model.Floors[i].FloorNo == floor.FloorNo) { idx = i; break; }
            }
            string full = KolonDonatiTableDrawer.FormatKolonPerdeKesitEtiket(_model.Floors, idx, colNo, 1, 1);
            int par = full.LastIndexOf(" (", StringComparison.Ordinal);
            return par > 0 ? full.Substring(0, par) : full;
        }

        /// <summary>Yazı kutusunun alt kenarını kiriş üst çizgisinin hemen üzerine alır.</summary>
        private void KirisAcilimYaziKutusuUstune(Transaction tr, double yAlt)
        {
            if (tr == null || _lastBeamLabelId.IsNull) return;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return;
            Extents3d ext;
            try { ext = txt.GeometricExtents; }
            catch { return; }
            double dy = yAlt - ext.MinPoint.Y;
            double dx = 0;
            if (Math.Abs(dy) < 0.05 && Math.Abs(dx) < 0.05) return;
            txt.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
        }

        private static Polygon KirisAcilimRect(GeometryFactory gf, double x0, double y0, double x1, double y1)
        {
            double xa = Math.Min(x0, x1);
            double xb = Math.Max(x0, x1);
            double ya = Math.Min(y0, y1);
            double yb = Math.Max(y0, y1);
            if (xb - xa < 0.5 || yb - ya < 0.5 || gf == null) return null;
            try
            {
                return gf.CreatePolygon(new[]
                {
                    new Coordinate(xa, ya),
                    new Coordinate(xb, ya),
                    new Coordinate(xb, yb),
                    new Coordinate(xa, yb),
                    new Coordinate(xa, ya)
                });
            }
            catch { return null; }
        }

        private static Polygon KirisAcilimPolygon(GeometryFactory gf, List<Point2d> pts)
        {
            if (gf == null || pts == null || pts.Count < 3) return null;
            var coords = new List<Coordinate>();
            foreach (var p in pts)
            {
                if (coords.Count > 0)
                {
                    double dx = p.X - coords[coords.Count - 1].X;
                    double dy = p.Y - coords[coords.Count - 1].Y;
                    if (dx * dx + dy * dy < 0.04) continue;
                }
                coords.Add(new Coordinate(p.X, p.Y));
            }
            if (coords.Count < 3) return null;
            if (coords[0].Distance(coords[coords.Count - 1]) > 0.2)
                coords.Add(new Coordinate(coords[0].X, coords[0].Y));
            try
            {
                var poly = gf.CreatePolygon(coords.ToArray());
                if (poly == null || poly.IsEmpty) return null;
                if (!poly.IsValid)
                {
                    var fixedPoly = poly.Buffer(0);
                    return fixedPoly as Polygon;
                }
                return poly;
            }
            catch { return null; }
        }

        /// <summary>Kapalı dikdörtgen. Görünüş dışı iz kiriş katmanında, içi kesit görünüşte.</summary>
        private static void KirisAcilimDrawKutu(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1, string layer)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { double t = y0; y0 = y1; y1 = t; }
            if (x1 - x0 < 0.5 || y1 - y0 < 0.5) return;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            AppendEntity(tr, ms, pl);
        }

        /// <summary>İzin kirişin dışında kalanı KIRIS, içinde kalanı KESIT GORUNUS.</summary>
        private static void KirisAcilimDrawIz(
            Transaction tr, BlockTableRecord ms, List<KirisAcilimSpan> chain, KirisAcilimSaplama iz, double x0, double yShift)
        {
            double xa = x0 + iz.X0;
            double xb = x0 + iz.X1;
            if (!KirisAcilimIzKirisSiniri(chain, iz, out double beamBot, out double beamTop))
            {
                KirisAcilimDrawKutu(tr, ms, xa, yShift + iz.KotBot, xb, yShift + iz.KotTop, LayerKiris);
                return;
            }
            bool above = iz.KotTop > beamTop + 0.5;
            bool below = iz.KotBot < beamBot - 0.5;
            double icBot = Math.Max(iz.KotBot, beamBot);
            double icTop = Math.Min(iz.KotTop, beamTop);
            double yb = yShift + icBot;
            double yt = yShift + icTop;
            if (icTop - icBot >= 0.5)
            {
                if (!above && !below)
                    KirisAcilimDrawKutu(tr, ms, xa, yb, xb, yt, LayerKesitGorunus);
                else
                {
                    KirisAcilimCizgi(tr, ms, xa, yb, xa, yt, LayerKesitGorunus);
                    KirisAcilimCizgi(tr, ms, xb, yb, xb, yt, LayerKesitGorunus);
                    if (!above) KirisAcilimCizgi(tr, ms, xa, yt, xb, yt, LayerKesitGorunus);
                    if (!below) KirisAcilimCizgi(tr, ms, xa, yb, xb, yb, LayerKesitGorunus);
                }
            }
            if (above)
                KirisAcilimDisIz(tr, ms, xa, xb, yShift + beamTop, yShift + iz.KotTop, up: true);
            if (below)
                KirisAcilimDisIz(tr, ms, xa, xb, yShift + beamBot, yShift + iz.KotBot, up: false);
        }

        /// <summary>Kiriş çizgisinden 20 cm’den fazla taşan iz 20 cm’de kesilir, kesit sınırı çizilir.</summary>
        private static void KirisAcilimDisIz(Transaction tr, BlockTableRecord ms, double xa, double xb, double yBeam, double yFar, bool up)
        {
            double limit = up ? yBeam + KirisAcilimKutupCm : yBeam - KirisAcilimKutupCm;
            bool cut = up ? yFar > limit + 0.5 : yFar < limit - 0.5;
            double yEnd = cut ? limit : yFar;
            if (cut)
            {
                KirisAcilimCizgi(tr, ms, xa, yBeam, xa, yEnd, LayerKiris);
                KirisAcilimCizgi(tr, ms, xb, yBeam, xb, yEnd, LayerKiris);
                KirisAcilimKesitSiniri(tr, ms, xa, xb, yEnd, 0);
                return;
            }
            KirisAcilimAcik(
                tr, ms, LayerKiris,
                new Point2d(xa, yBeam),
                new Point2d(xa, yEnd),
                new Point2d(xb, yEnd),
                new Point2d(xb, yBeam));
        }

        private static void KirisAcilimCizgi(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1, string layer)
        {
            KirisAcilimCizgi(tr, ms, x0, y0, x1, y1, layer, 0);
        }

        /// <summary>aci = 0 ise katmanın rengi; değilse çizgi rengi geçersiz kılınır (katman aynı kalır).</summary>
        private static void KirisAcilimCizgi(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1, string layer, short aci)
        {
            if (Math.Abs(x1 - x0) < 0.2 && Math.Abs(y1 - y0) < 0.2) return;
            var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)) { Layer = layer };
            if (aci > 0)
                ln.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
            AppendEntity(tr, ms, ln);
        }

        private static void KirisAcilimAcik(Transaction tr, BlockTableRecord ms, string layer, params Point2d[] pts)
        {
            if (pts == null || pts.Length < 2) return;
            var pl = new Polyline();
            for (int i = 0; i < pts.Length; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = false;
            pl.Layer = layer;
            AppendEntity(tr, ms, pl);
        }

        private static void KirisAcilimKirisCizgi(Transaction tr, BlockTableRecord ms, double x0, double y0, double x1, double y1)
        {
            if (Math.Abs(x1 - x0) < 0.2 && Math.Abs(y1 - y0) < 0.2) return;
            AppendEntity(tr, ms, new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)) { Layer = LayerKiris });
        }

        /// <summary>Kolon çizgileri arasındaki kiriş üst ve alt çizgisini atlar.</summary>
        private static void KirisAcilimDrawBeton(Transaction tr, BlockTableRecord ms, Geometry geom, List<KirisAcilimKolonTrim> trims)
        {
            if (geom == null || geom.IsEmpty) return;
            if (geom is Polygon poly)
            {
                if (poly.ExteriorRing != null) KirisAcilimHalkaCiz(tr, ms, poly.ExteriorRing, trims);
                return;
            }
            if (geom is GeometryCollection col)
            {
                for (int i = 0; i < col.NumGeometries; i++)
                    KirisAcilimDrawBeton(tr, ms, col.GetGeometryN(i), trims);
            }
        }

        private static void KirisAcilimHalkaCiz(Transaction tr, BlockTableRecord ms, LineString ring, List<KirisAcilimKolonTrim> trims)
        {
            Coordinate[] c = ring?.Coordinates;
            if (c == null || c.Length < 2) return;
            var chain = new List<Point2d>();
            void Flush()
            {
                if (chain.Count >= 2)
                {
                    var pl = new Polyline();
                    for (int k = 0; k < chain.Count; k++)
                        pl.AddVertexAt(k, chain[k], 0, 0, 0);
                    pl.Layer = LayerKiris;
                    AppendEntity(tr, ms, pl);
                }
                chain.Clear();
            }
            for (int i = 0; i < c.Length - 1; i++)
            {
                List<(Point2d A, Point2d B)> bits = KirisAcilimKenarParca(c[i], c[i + 1], trims);
                if (bits.Count == 0) { Flush(); continue; }
                foreach (var bit in bits)
                {
                    if (chain.Count == 0)
                        chain.Add(bit.A);
                    else if (chain[chain.Count - 1].GetDistanceTo(bit.A) > 0.4)
                    {
                        Flush();
                        chain.Add(bit.A);
                    }
                    chain.Add(bit.B);
                }
            }
            Flush();
        }

        private static List<(Point2d A, Point2d B)> KirisAcilimKenarParca(Coordinate a, Coordinate b, List<KirisAcilimKolonTrim> trims)
        {
            var whole = new List<(Point2d A, Point2d B)>();
            var pa = new Point2d(a.X, a.Y);
            var pb = new Point2d(b.X, b.Y);
            double dx = b.X - a.X;
            if (Math.Abs(dx) < 0.4 || trims == null || trims.Count == 0)
            {
                whole.Add((pa, pb));
                return whole;
            }
            double dy = b.Y - a.Y;
            double xMin = Math.Min(a.X, b.X);
            double xMax = Math.Max(a.X, b.X);
            var cuts = new List<(double Lo, double Hi)>();
            foreach (KirisAcilimKolonTrim t in trims)
            {
                double lo = Math.Max(xMin, Math.Min(t.X0, t.X1));
                double hi = Math.Min(xMax, Math.Max(t.X0, t.X1));
                if (hi - lo < 0.4) continue;
                double yLo = a.Y + (lo - a.X) / dx * dy;
                double yHi = a.Y + (hi - a.X) / dx * dy;
                bool onBot = t.TrimBot && Math.Abs(yLo - t.YBot) <= 2.0 && Math.Abs(yHi - t.YBot) <= 2.0;
                bool onTop = t.TrimTop && Math.Abs(yLo - t.YTop) <= 2.0 && Math.Abs(yHi - t.YTop) <= 2.0;
                if (onBot || onTop) cuts.Add((lo, hi));
            }
            if (cuts.Count == 0)
            {
                whole.Add((pa, pb));
                return whole;
            }
            cuts.Sort((p, q) => p.Lo.CompareTo(q.Lo));
            var merged = new List<(double Lo, double Hi)>();
            foreach (var cut in cuts)
            {
                if (merged.Count == 0 || cut.Lo > merged[merged.Count - 1].Hi + 0.2)
                    merged.Add(cut);
                else
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.Lo, Math.Max(last.Hi, cut.Hi));
                }
            }
            var spans = new List<(double Lo, double Hi)>();
            double at = xMin;
            foreach (var m in merged)
            {
                if (m.Lo - at > 0.4) spans.Add((at, m.Lo));
                at = Math.Max(at, m.Hi);
            }
            if (xMax - at > 0.4) spans.Add((at, xMax));
            bool forward = b.X >= a.X;
            if (!forward) spans.Reverse();
            foreach (var s in spans)
            {
                double xA = forward ? s.Lo : s.Hi;
                double xB = forward ? s.Hi : s.Lo;
                double yA = a.Y + (xA - a.X) / dx * dy;
                double yB = a.Y + (xB - a.X) / dx * dy;
                whole.Add((new Point2d(xA, yA), new Point2d(xB, yB)));
            }
            return whole;
        }

        private static void KirisAcilimKesitSiniri(Transaction tr, BlockTableRecord ms, double xL, double xR, double y, double overhang)
        {
            if (xR < xL) { double t = xL; xL = xR; xR = t; }
            AppendEntity(tr, ms, new Line(
                new Point3d(xL - overhang, y, 0),
                new Point3d(xR + overhang, y, 0))
            { Layer = LayerKesitSiniri });
        }

        /// <summary>Kiriş ucundaki iz. Açıklık ortasındaki saplama izi değil.</summary>
        private static bool KirisAcilimUcIz(List<KirisAcilimSpan> chain, KirisAcilimSaplama iz)
        {
            if (chain == null || chain.Count == 0) return false;
            double half = 0.5 * (iz.X1 - iz.X0);
            double mid = 0.5 * (iz.X0 + iz.X1);
            double xBas = chain[0].X0;
            double xSon = chain[chain.Count - 1].X1;
            return Math.Abs(mid - xSon) <= half + 2.0 || Math.Abs(mid - xBas) <= half + 2.0;
        }

        /// <summary>İzin hizasına denk gelen kirişin alt ve üst kotu.</summary>
        /// <summary>Çizilen kiriş izinin en alt kotu. 20 cm’de kesilen iz orada biter.</summary>
        private static double KirisAcilimIzAlt(List<KirisAcilimSpan> chain, KirisAcilimSaplama iz)
        {
            if (!KirisAcilimIzKirisSiniri(chain, iz, out double beamBot, out _))
                return iz.KotBot;
            if (iz.KotBot < beamBot - 0.5)
            {
                double limit = beamBot - KirisAcilimKutupCm;
                return iz.KotBot < limit - 0.5 ? limit : iz.KotBot;
            }
            return Math.Max(iz.KotBot, beamBot);
        }

        private static bool KirisAcilimIzKirisSiniri(List<KirisAcilimSpan> chain, KirisAcilimSaplama iz, out double beamBot, out double beamTop)
        {
            beamTop = double.MinValue;
            beamBot = double.MaxValue;
            bool hit = false;
            for (int i = 0; i < chain.Count; i++)
            {
                KirisAcilimSpan s = chain[i];
                if (s?.Beam == null || s.X1 < iz.X0 - 2.0 || s.X0 > iz.X1 + 2.0) continue;
                beamTop = Math.Max(beamTop, Math.Max(s.Kot0, s.Kot1));
                beamBot = Math.Min(beamBot, Math.Min(s.Kot0, s.Kot1) - s.Beam.HeightCm);
                hit = true;
            }
            return hit && beamTop - beamBot >= 0.5;
        }

        /// <summary>
        /// Diğer yöndeki kirişin şeridi (genişlik + kaçıklık) bu kirişin şeridiyle kesişiyorsa
        /// görünüşte o aralığa, saplanan kirişin kendi üst ve alt kotuyla oturtulur.
        /// </summary>
        private List<KirisAcilimSaplama> KirisAcilimCollectSaplanan(int floorNo, List<KirisAcilimSpan> chain)
        {
            var list = new List<KirisAcilimSaplama>();
            if (chain == null || chain.Count == 0) return list;
            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var inChain = new HashSet<int>();
            var hostPoly = new Polygon[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i]?.Beam != null) inChain.Add(chain[i].Beam.BeamId);
                if (TryKirisAcilimBeamStrip(chain[i].Beam, gf, out Polygon hp, out _, out _, out _, out _))
                    hostPoly[i] = hp;
            }

            var seenAxis = new HashSet<string>();
            foreach (var other in GetRawBeamsOnFloor(floorNo))
            {
                if (other == null || other.IsWallFlag == 1 || other.HeightCm <= 1.0) continue;
                if (inChain.Contains(other.BeamId)) continue;
                int ax0 = Math.Min(other.StartAxisId, other.EndAxisId);
                int ax1 = Math.Max(other.StartAxisId, other.EndAxisId);
                string axisKey = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", other.FixedAxisId, ax0, ax1, other.OffsetRaw);
                if (!seenAxis.Add(axisKey)) continue;
                if (!TryKirisAcilimBeamStrip(other, gf, out Polygon otherPoly, out Point2d oa, out Point2d ob, out double kotA, out double kotB))
                    continue;
                Vector2d ov = ob - oa;
                if (ov.Length < 1.0) continue;
                Vector2d ou = ov.GetNormal();

                for (int i = 0; i < chain.Count; i++)
                {
                    if (hostPoly[i] == null) continue;
                    KirisAcilimSpan host = chain[i];
                    Vector2d hv = host.World1 - host.World0;
                    if (hv.Length < 1.0) continue;
                    Vector2d hu = hv.GetNormal();
                    if (KirisAcilimHatAcisiDerece(hu, ou) <= KirisAcilimMaxDonusDerece + 1e-6) continue;
                    if (Math.Abs(hu.X * ou.Y - hu.Y * ou.X) < 0.20) continue;

                    Geometry inter;
                    try { inter = hostPoly[i].Intersection(otherPoly); }
                    catch { continue; }
                    if (inter == null || inter.IsEmpty || inter.Coordinates == null || inter.Coordinates.Length == 0)
                        continue;

                    double tMin = double.MaxValue;
                    double tMax = double.MinValue;
                    double sx = 0, sy = 0;
                    int n = 0;
                    foreach (Coordinate c in inter.Coordinates)
                    {
                        if (c == null) continue;
                        double t = (c.X - host.World0.X) * hu.X + (c.Y - host.World0.Y) * hu.Y;
                        if (t < tMin) tMin = t;
                        if (t > tMax) tMax = t;
                        sx += c.X;
                        sy += c.Y;
                        n++;
                    }
                    if (n == 0 || tMax - tMin < 2.0) continue;

                    double along = (sx / n - oa.X) * ou.X + (sy / n - oa.Y) * ou.Y;
                    double u = along / ov.Length;
                    if (u < 0) u = 0;
                    if (u > 1) u = 1;
                    double top = kotA + (kotB - kotA) * u;
                    double bot = top - other.HeightCm;
                    double mid = host.X0 + 0.5 * (tMin + tMax);
                    double half = other.WidthCm * 0.5;
                    KirisAcilimAddSaplama(list, other.BeamId, mid - half, mid + half, top, bot, false, false, true);
                }
            }
            return list;
        }

        private static void KirisAcilimAddSaplama(List<KirisAcilimSaplama> list, int beamId, double x0, double x1, double top, double bot, bool solUc, bool sagUc, bool izdusumVar)
        {
            if (x1 < x0) { double t = x0; x0 = x1; x1 = t; }
            if (top < bot) { double t = top; top = bot; bot = t; }
            if (x1 - x0 < 2.0 || top - bot < 2.0) return;
            double mid = 0.5 * (x0 + x1);
            double half = 0.5 * (x1 - x0);
            for (int i = 0; i < list.Count; i++)
            {
                KirisAcilimSaplama s = list[i];
                if (s.BeamId != beamId) continue;
                double sm = 0.5 * (s.X0 + s.X1);
                if (Math.Abs(sm - mid) > half + 40.0) continue;
                double c = 0.5 * (sm + mid);
                double h = 0.5 * ((s.KotTop - s.KotBot) + (top - bot));
                double tTop = 0.5 * (s.KotTop + top);
                s.X0 = c - half;
                s.X1 = c + half;
                s.KotTop = tTop;
                s.KotBot = tTop - h;
                s.SolUc = s.SolUc || solUc;
                s.SagUc = s.SagUc || sagUc;
                s.IzdusumVar = s.IzdusumVar || izdusumVar;
                list[i] = s;
                return;
            }
            list.Add(new KirisAcilimSaplama { BeamId = beamId, X0 = x0, X1 = x1, KotTop = top, KotBot = bot, SolUc = solUc, SagUc = sagUc, IzdusumVar = izdusumVar });
        }

        /// <summary>
        /// KESIT GORUNUS: konsol uçtaki yan kiriş, saplama kirişin saplandığı kiriş ve o kirişe saplanan kiriş.
        /// Ölçü, kesişen kirişin kendi eni × yüksekliği. Açıklık ortası olmayan kesişmeler çizilmez.
        /// </summary>
        private List<KirisAcilimSaplama> KirisAcilimCollectKesitGorunus(
            int floorNo, List<KirisAcilimSpan> chain, Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            var list = new List<KirisAcilimSaplama>();
            if (chain == null || chain.Count == 0) return list;
            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            var inChain = new HashSet<int>();
            var hostPoly = new Polygon[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i]?.Beam != null) inChain.Add(chain[i].Beam.BeamId);
                if (TryKirisAcilimBeamStrip(chain[i].Beam, gf, out Polygon hp, out _, out _, out _, out _))
                    hostPoly[i] = hp;
            }

            var seenAxis = new HashSet<string>();
            foreach (var other in GetRawBeamsOnFloor(floorNo))
            {
                if (other == null || other.IsWallFlag == 1 || other.HeightCm <= 1.0 || other.WidthCm <= 1.0) continue;
                if (inChain.Contains(other.BeamId)) continue;
                int ax0 = Math.Min(other.StartAxisId, other.EndAxisId);
                int ax1 = Math.Max(other.StartAxisId, other.EndAxisId);
                string axisKey = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", other.FixedAxisId, ax0, ax1, other.OffsetRaw);
                if (!seenAxis.Add(axisKey)) continue;
                if (!TryKirisAcilimBeamStrip(other, gf, out Polygon otherPoly, out Point2d oa, out Point2d ob, out double kotA, out double kotB))
                    continue;
                Vector2d ov = ob - oa;
                if (ov.Length < 1.0) continue;
                Vector2d ou = ov.GetNormal();

                for (int i = 0; i < chain.Count; i++)
                {
                    if (hostPoly[i] == null) continue;
                    KirisAcilimSpan host = chain[i];
                    Vector2d hv = host.World1 - host.World0;
                    if (hv.Length < 1.0) continue;
                    Vector2d hu = hv.GetNormal();
                    if (KirisAcilimHatAcisiDerece(hu, ou) <= KirisAcilimMaxDonusDerece + 1e-6) continue;
                    if (Math.Abs(hu.X * ou.Y - hu.Y * ou.X) < 0.20) continue;

                    Geometry inter;
                    try { inter = hostPoly[i].Intersection(otherPoly); }
                    catch { continue; }
                    if (inter == null || inter.IsEmpty || inter.Coordinates == null || inter.Coordinates.Length == 0)
                        continue;

                    double tMin = double.MaxValue;
                    double tMax = double.MinValue;
                    int n = 0;
                    foreach (Coordinate c in inter.Coordinates)
                    {
                        if (c == null) continue;
                        double t = (c.X - host.World0.X) * hu.X + (c.Y - host.World0.Y) * hu.Y;
                        if (t < tMin) tMin = t;
                        if (t > tMax) tMax = t;
                        n++;
                    }
                    if (n == 0 || tMax - tMin < 2.0)
                    {
                        // Offset yüzünden iki şerit yalnız köşede dokunabilir (alan kesişimi 0): uç noktası öteki kirişin şeridindeyse kesit yine çizilir.
                        if (!KirisAcilimNoktaKiriste(other, host.World0) && !KirisAcilimNoktaKiriste(other, host.World1))
                            continue;
                    }
                    if (ucTipi == null || host.Beam == null || !ucTipi.TryGetValue(host.Beam, out KirisAcilimUcBilgi uc))
                        continue;
                    bool world0Start = host.Cross0 == host.Beam.StartAxisId;
                    // Aynı numaranın devamı = ana kirişin bölündüğü ara uç. Başka numara veya zincir sonu = kirişin kendi ucu.
                    bool solAyni = i > 0 && chain[i - 1]?.Beam != null && host.Beam.BeamId == chain[i - 1].Beam.BeamId;
                    bool sagAyni = i + 1 < chain.Count && chain[i + 1]?.Beam != null && host.Beam.BeamId == chain[i + 1].Beam.BeamId;
                    KirisAcilimKesitUcuEkle(list, other, oa, ou, ov.Length, kotA, kotB, host.World0, host.X0, world0Start, solUc: true, !solAyni, uc, hu);
                    KirisAcilimKesitUcuEkle(list, other, oa, ou, ov.Length, kotA, kotB, host.World1, host.X1, !world0Start, solUc: false, !sagAyni, uc, hu);
                }
            }
            return list;
        }

        /// <summary>
        /// Düğümü 0 olan uç bu kirişin üstündeyse kesit oturtulur.
        /// İzdüşüm + numara yalnız kirişin kendi ucunda (komşu parça başka numara, ya da zincir sonu):
        /// saplandığı kiriş izi mesnettir. Aynı numaranın bölündüğü ara uçta kesit kalır, izdüşüm yok.
        /// Kolon düğümü (≠0) kesit çizmez.
        /// Kesitin yeri, kesiti çizilen kirişin kaçıklığı kadardır; eksene ortalanmaz.
        /// </summary>
        private void KirisAcilimKesitUcuEkle(
            List<KirisAcilimSaplama> list, BeamInfo other,
            Point2d axisA, Vector2d axisU, double axisLen, double kotA, double kotB,
            Point2d world, double station, bool isStart, bool solUc, bool kendiUcu, KirisAcilimUcBilgi uc, Vector2d hostU)
        {
            if (other == null || axisLen < 1.0) return;
            short joint = isStart ? uc.JointStart : uc.JointEnd;
            if (joint != 0) return;
            if (!KirisAcilimNoktaKiriste(other, world)) return;

            // Code 1 çoğu kayıtta iki düğümü de 0 bırakır; sağ uç da kirişe saplanır. Ayrım kod değil, kendi ucu mu.
            bool izdusumVar = kendiUcu;

            double along = (world.X - axisA.X) * axisU.X + (world.Y - axisA.Y) * axisU.Y;
            double u = along / axisLen;
            if (u < 0) u = 0;
            if (u > 1) u = 1;
            double top = kotA + (kotB - kotA) * u;
            double half = other.WidthCm * 0.5;
            Vector2d perp = new Vector2d(-axisU.Y, axisU.X);
            double shift = (hostU.X * perp.X + hostU.Y * perp.Y) * KirisAcilimKaciklikMerkez(other);
            double mid = station + shift;
            KirisAcilimAddSaplama(list, other.BeamId, mid - half, mid + half, top, top - other.HeightCm, solUc, !solUc, izdusumVar);
        }

        /// <summary>Kaçıklığın kiriş ekseninden merkez kayması (cm), +perp yönünde. 0 ise ortada.</summary>
        private static double KirisAcilimKaciklikMerkez(BeamInfo beam)
        {
            double hw = Math.Max(beam.WidthCm, 1.0) * 0.5;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upper, out double lower);
            return 0.5 * (upper + lower);
        }

        /// <summary>Nokta kiriş şeridinin içinde, uçlardan 15 cm taşmaya kadar.</summary>
        private bool KirisAcilimNoktaKiriste(BeamInfo beam, Point2d p)
        {
            if (!KirisAcilimBeamEkseni(beam, out Point2d a, out Point2d b, out _, out _, out Vector2d axisU, out Vector2d perp))
                return false;
            double len = (b - a).Length;
            double along = (p.X - a.X) * axisU.X + (p.Y - a.Y) * axisU.Y;
            double lat = (p.X - a.X) * perp.X + (p.Y - a.Y) * perp.Y;
            double hw = Math.Max(beam.WidthCm, 1.0) * 0.5;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upper, out double lower);
            if (lat < Math.Min(lower, upper) - 8.0 || lat > Math.Max(lower, upper) + 8.0) return false;
            return along >= -15.0 && along <= len + 15.0;
        }

        private bool KirisAcilimBeamEkseni(
            BeamInfo beam, out Point2d a, out Point2d b, out double kotA, out double kotB, out Vector2d axisU, out Vector2d perp)
        {
            a = b = default;
            kotA = kotB = 0;
            axisU = perp = default;
            if (beam == null || _axisService == null) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d start)) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d end)) return false;
            a = start;
            b = end;
            NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
            bool aIsStart = a.GetDistanceTo(start) <= a.GetDistanceTo(end);
            kotA = aIsStart ? beam.Point1KotCm : beam.Point2KotCm;
            kotB = aIsStart ? beam.Point2KotCm : beam.Point1KotCm;
            Vector2d dir = b - a;
            if (dir.Length < 1.0) return false;
            axisU = dir.GetNormal();
            perp = new Vector2d(-axisU.Y, axisU.X);
            return true;
        }

        /// <summary>ST4 kiriş sırası = BEA kaydı. Ofset 0 ve 8 düğüm no, ofset 36 uç tipi.</summary>
        private Dictionary<BeamInfo, KirisAcilimUcBilgi> KirisAcilimReadUcTipi(string st4Path)
        {
            var map = new Dictionary<BeamInfo, KirisAcilimUcBilgi>();
            if (string.IsNullOrEmpty(st4Path) || _model?.Beams == null) return map;
            string beaPath = Path.ChangeExtension(st4Path, ".BEA");
            if (!File.Exists(beaPath))
            {
                beaPath = Path.ChangeExtension(st4Path, ".bea");
                if (!File.Exists(beaPath)) return map;
            }
            byte[] data;
            try { data = File.ReadAllBytes(beaPath); }
            catch { return map; }
            const int rec = 900;
            int n = Math.Min(_model.Beams.Count, data.Length / rec);
            for (int i = 0; i < n; i++)
            {
                if (_model.Beams[i] == null) continue;
                map[_model.Beams[i]] = new KirisAcilimUcBilgi
                {
                    JointStart = BitConverter.ToInt16(data, i * rec),
                    JointEnd = BitConverter.ToInt16(data, i * rec + 8),
                    Code = BitConverter.ToInt16(data, i * rec + 36)
                };
            }
            return map;
        }

        /// <summary>
        /// KMX montaj donatısı: ST4 kiriş sırası = KMX kayıt sırası = BEA kayıt sırası.
        /// Ofset 108 adet, 110 çap (mm); kayıt 300 bayt.
        /// </summary>
        private Dictionary<BeamInfo, KirisAcilimMontajBilgi> KirisAcilimReadMontaj(string st4Path)
        {
            var map = new Dictionary<BeamInfo, KirisAcilimMontajBilgi>();
            if (string.IsNullOrEmpty(st4Path) || _model?.Beams == null) return map;
            string kmxPath = Path.ChangeExtension(st4Path, ".KMX");
            if (!File.Exists(kmxPath))
            {
                kmxPath = Path.ChangeExtension(st4Path, ".kmx");
                if (!File.Exists(kmxPath)) return map;
            }
            byte[] data;
            try { data = File.ReadAllBytes(kmxPath); }
            catch { return map; }
            const int rec = 300;
            const int montajOffset = 108;
            int n = Math.Min(_model.Beams.Count, data.Length / rec);
            for (int i = 0; i < n; i++)
            {
                if (_model.Beams[i] == null) continue;
                map[_model.Beams[i]] = new KirisAcilimMontajBilgi
                {
                    Adet = BitConverter.ToInt16(data, i * rec + montajOffset),
                    Cap = BitConverter.ToInt16(data, i * rec + montajOffset + 2),
                    DuzAdet = BitConverter.ToInt16(data, i * rec + 112),
                    DuzCap = BitConverter.ToInt16(data, i * rec + 114),
                    GovdeAdet = BitConverter.ToInt16(data, i * rec + 124),
                    GovdeCap = BitConverter.ToInt16(data, i * rec + 126),
                    SolUstAdet = BitConverter.ToInt16(data, i * rec + 128),
                    SolUstCap = BitConverter.ToInt16(data, i * rec + 130),
                    SagAltAdet = BitConverter.ToInt16(data, i * rec + 132),
                    SagAltCap = BitConverter.ToInt16(data, i * rec + 134),
                    SagUstAdet = BitConverter.ToInt16(data, i * rec + 136),
                    SagUstCap = BitConverter.ToInt16(data, i * rec + 138),
                    SolAltAdet = BitConverter.ToInt16(data, i * rec + 140),
                    SolAltCap = BitConverter.ToInt16(data, i * rec + 142)
                };
            }
            return map;
        }

        private bool TryKirisAcilimBeamStrip(
            BeamInfo beam, GeometryFactory gf, out Polygon poly,
            out Point2d a, out Point2d b, out double kotA, out double kotB)
        {
            poly = null;
            a = b = default;
            kotA = kotB = 0;
            if (beam == null || gf == null || _axisService == null) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d start)) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d end)) return false;
            a = start;
            b = end;
            NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
            bool aIsStart = a.GetDistanceTo(start) <= a.GetDistanceTo(end);
            kotA = aIsStart ? beam.Point1KotCm : beam.Point2KotCm;
            kotB = aIsStart ? beam.Point2KotCm : beam.Point1KotCm;
            Vector2d dir = b - a;
            if (dir.Length < 1.0) return false;
            Vector2d u = dir.GetNormal();
            Vector2d perp = new Vector2d(-u.Y, u.X);
            double hw = beam.WidthCm * 0.5;
            if (hw < 0.5) return false;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upper, out double lower);
            Point2d q1 = a + perp.MultiplyBy(upper);
            Point2d q2 = b + perp.MultiplyBy(upper);
            Point2d q3 = b + perp.MultiplyBy(lower);
            Point2d q4 = a + perp.MultiplyBy(lower);
            try
            {
                poly = gf.CreatePolygon(new[]
                {
                    new Coordinate(q1.X, q1.Y),
                    new Coordinate(q2.X, q2.Y),
                    new Coordinate(q3.X, q3.Y),
                    new Coordinate(q4.X, q4.Y),
                    new Coordinate(q1.X, q1.Y)
                });
                return poly != null && !poly.IsEmpty && poly.Area > 1.0;
            }
            catch
            {
                poly = null;
                return false;
            }
        }

        /// <summary>Mesnet genişliği, aks noktasından kiriş doğrultusuna göre (cm). Kolon varsa perdeye bakılmaz.</summary>
        private bool TryKirisAcilimSupportWidth(
            int floorNo, Point2d node, Vector2d beamDir,
            int crossAxis, int crossAxis2,
            double beamTopAbsCm, double beamBotAbsCm,
            BeamInfo hostBeam, BeamInfo otherBeam,
            out double xLo, out double xHi, out bool isWall, out bool extendUp, out bool extendDown, out int columnNo)
        {
            xLo = xHi = 0;
            isWall = false;
            extendUp = extendDown = false;
            columnNo = 0;
            if (beamDir.Length < 1e-9 || hostBeam == null) return false;
            Vector2d u = beamDir.GetNormal();
            if (otherBeam != null && ReferenceEquals(otherBeam, hostBeam)) otherBeam = null;

            if (_model.Columns != null)
            {
                double best = double.MaxValue;
                bool found = false;
                double blo = 0, bhi = 0;
                ColumnAxisInfo matchedCol = null;
                foreach (var col in _model.Columns)
                {
                    if (col == null) continue;
                    if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                    bool hit = TryKirisAcilimColumnInterval(floorNo, col, axisNode, node, u, hostBeam, out double lo, out double hi);
                    if (otherBeam != null
                        && TryKirisAcilimColumnInterval(floorNo, col, axisNode, node, u, otherBeam, out double lo2, out double hi2)
                        && (!hit || Math.Abs(0.5 * (lo2 + hi2)) < Math.Abs(0.5 * (lo + hi))))
                    {
                        lo = lo2;
                        hi = hi2;
                        hit = true;
                    }
                    if (!hit || hi - lo <= 2.0) continue;
                    double d = Math.Abs(0.5 * (lo + hi));
                    if (d < best)
                    {
                        best = d;
                        blo = lo;
                        bhi = hi;
                        matchedCol = col;
                        found = true;
                    }
                }
                if (found && bhi - blo > 2.0)
                {
                    xLo = blo;
                    xHi = bhi;
                    columnNo = matchedCol != null ? matchedCol.ColumnNo : 0;
                    KirisAcilimColumnExtend(floorNo, matchedCol, beamTopAbsCm, beamBotAbsCm, out extendUp, out extendDown);
                    return true;
                }
            }

            if (TryKirisAcilimWallInterval(floorNo, node, u, crossAxis, out double wLo, out double wHi)
                || (crossAxis2 != crossAxis && TryKirisAcilimWallInterval(floorNo, node, u, crossAxis2, out wLo, out wHi)))
            {
                xLo = wLo;
                xHi = wHi;
                isWall = true;
                extendUp = true;
                extendDown = true;
                return true;
            }
            return false;
        }

        private void KirisAcilimColumnExtend(
            int floorNo, ColumnAxisInfo col, double beamTopAbsCm, double beamBotAbsCm,
            out bool extendUp, out bool extendDown)
        {
            extendUp = true;
            extendDown = true;
            if (col == null) return;
            bool upperFloor = KirisAcilimColumnOnAnotherFloor(floorNo, col, above: true);
            bool lowerFloor = KirisAcilimColumnOnAnotherFloor(floorNo, col, above: false);
            double altCm, ustCm;
            if (!TryKirisAcilimColumnAbsCm(floorNo, col, out altCm, out ustCm))
                return;
            // Üst katta aynı kolon varsa kütük çıkar. Bu katın üst kotu çoğu zaman kiriş üstüne eşittir.
            extendUp = upperFloor || ustCm > beamTopAbsCm + 5.0;
            extendDown = lowerFloor || altCm < beamBotAbsCm - 5.0;
        }

        private bool KirisAcilimColumnOnAnotherFloor(int floorNo, ColumnAxisInfo col, bool above)
        {
            if (col == null || _model?.Floors == null) return false;
            FloorInfo here = null;
            foreach (var f in _model.Floors)
            {
                if (f.FloorNo == floorNo) { here = f; break; }
            }
            if (here == null) return false;
            foreach (var f in _model.Floors)
            {
                if (f.FloorNo == floorNo) continue;
                if (above && f.ElevationM <= here.ElevationM + 0.02) continue;
                if (!above && f.ElevationM >= here.ElevationM - 0.02) continue;
                if (HasColumnOnFloor(f, col)) return true;
            }
            return false;
        }

        private bool TryKirisAcilimColumnAbsCm(int floorNo, ColumnAxisInfo col, out double altCm, out double ustCm)
        {
            altCm = ustCm = 0;
            double baseK = _model.BuildingBaseKotu;
            if (col.ColumnType == 3)
            {
                int posId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
                if (posId > 0 && _model.PolygonColumnKotMFromBinaTabaniByPositionId.TryGetValue(posId, out var pk))
                {
                    altCm = (pk.altM + baseK) * 100.0;
                    ustCm = (pk.ustM + baseK) * 100.0;
                    return ustCm > altCm + 1.0;
                }
                return false;
            }

            int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
            if ((sectionId <= 0 || !_model.ColumnKotMFromBinaTabaniBySectionId.ContainsKey(sectionId))
                && col.ColumnId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;
            if (sectionId > 0 && _model.ColumnKotMFromBinaTabaniBySectionId.TryGetValue(sectionId, out var kotM))
            {
                altCm = (kotM.altM + baseK) * 100.0;
                ustCm = (kotM.ustM + baseK) * 100.0;
                return ustCm > altCm + 1.0;
            }
            return false;
        }

        /// <summary>
        /// Kolonun kiriş doğrultusundaki genişliği: kiriş şeridinin içinden geçen en uzun kiriş-paralel kiriş.
        /// Tüm köşelerin izdüşümü uzun kolonun gölgesini (ör. 305 cm'nin 120 cm'lik izi) yazar; kiriş o gölgenin içinden geçmez.
        /// </summary>
        private bool TryKirisAcilimColumnInterval(
            int floorNo, ColumnAxisInfo col, Point2d axisNode, Point2d origin, Vector2d u, BeamInfo beam,
            out double lo, out double hi)
        {
            lo = hi = 0;
            if (beam == null || u.Length < 1e-9) return false;
            int sectionId = ResolveColumnSectionId(floorNo, col.ColumnNo);
            int polygonSectionId = ResolvePolygonPositionSectionId(floorNo, col.ColumnNo);
            if (col.ColumnType != 3 && (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId))
                && col.ColumnId > 0 && _model.ColumnDimsBySectionId.ContainsKey(col.ColumnId))
                sectionId = col.ColumnId;

            bool hasDim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId);
            if (col.ColumnType != 3 && !hasDim && _model.Floors != null)
            {
                foreach (var f in _model.Floors)
                {
                    if (f.FloorNo == floorNo) continue;
                    int sid = ResolveColumnSectionId(f.FloorNo, col.ColumnNo);
                    if (sid > 0 && _model.ColumnDimsBySectionId.ContainsKey(sid))
                    {
                        sectionId = sid;
                        hasDim = true;
                        break;
                    }
                }
            }
            if (col.ColumnType != 3 && !hasDim) return false;
            var dim = hasDim ? _model.ColumnDimsBySectionId[sectionId] : (W: 40.0, H: 40.0);
            double hw = dim.W / 2.0;
            double hh = dim.H / 2.0;
            var offsetLocal = col.ColumnType == 2
                ? ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw)
                : ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
            var offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
            var center = new Point2d(axisNode.X + offsetGlobal.X, axisNode.Y + offsetGlobal.Y);

            double reach = col.ColumnType == 2
                ? Math.Max(hw, hh) + Math.Abs(offsetGlobal.X) + Math.Abs(offsetGlobal.Y)
                : Math.Sqrt(hw * hw + hh * hh) + Math.Abs(offsetGlobal.X) + Math.Abs(offsetGlobal.Y);
            if (col.ColumnType != 3 && axisNode.GetDistanceTo(origin) > reach + beam.WidthCm + 40.0)
                return false;

            if (col.ColumnType == 2)
                return TryKirisAcilimCircleChord(center, Math.Max(hw, hh), origin, u, beam, out lo, out hi);

            Point2d[] pts = null;
            if (col.ColumnType == 3)
            {
                if (!TryGetPolygonColumn(polygonSectionId, center, col.AngleDeg, out pts) || pts == null || pts.Length < 3)
                    return false;
                double far = 0;
                foreach (var p in pts)
                {
                    double d = p.GetDistanceTo(axisNode);
                    if (d > far) far = d;
                }
                if (axisNode.GetDistanceTo(origin) > far + beam.WidthCm + 40.0) return false;
            }
            else
            {
                pts = BuildRect(center, hw, hh, col.AngleDeg);
            }

            return TryKirisAcilimColumnChord(pts, origin, u, beam, out lo, out hi);
        }

        private bool TryKirisAcilimBeamStrip(BeamInfo beam, out Point2d axisPt, out Vector2d perp, out double sMin, out double sMax)
        {
            axisPt = default;
            perp = default;
            sMin = sMax = 0;
            if (beam == null) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d p0)) return false;
            if (!_axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d p1)) return false;
            Point2d a = p0, b = p1;
            NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
            Vector2d d = b - a;
            if (d.Length < 1e-6) return false;
            Vector2d axisU = d.GetNormal();
            perp = new Vector2d(-axisU.Y, axisU.X);
            double hw = Math.Max(beam.WidthCm, 1.0) * 0.5;
            ComputeBeamEdgeOffsets(beam.OffsetRaw, hw, out double upper, out double lower);
            axisPt = a;
            sMin = Math.Min(lower, upper);
            sMax = Math.Max(lower, upper);
            return sMax > sMin + 0.5;
        }

        private bool TryKirisAcilimCircleChord(
            Point2d center, double radius, Point2d joint, Vector2d elevU, BeamInfo beam,
            out double lo, out double hi)
        {
            lo = hi = 0;
            if (radius < 1.0) return false;
            if (!TryKirisAcilimBeamStrip(beam, out Point2d axisPt, out Vector2d perp, out double sMin, out double sMax))
                return false;
            elevU = elevU.GetNormal();
            double sc = (center.X - axisPt.X) * perp.X + (center.Y - axisPt.Y) * perp.Y;
            double sLo = Math.Max(sMin, sc - radius);
            double sHi = Math.Min(sMax, sc + radius);
            if (sHi < sLo - 1e-6) return false;
            double s = sc;
            if (s < sLo) s = sLo;
            if (s > sHi) s = sHi;
            double radial = s - sc;
            double half = Math.Sqrt(Math.Max(0.0, radius * radius - radial * radial));
            if (half < 1.0) return false;
            Vector2d axisU = new Vector2d(-perp.Y, perp.X);
            var linePt = new Point2d(axisPt.X + s * perp.X, axisPt.Y + s * perp.Y);
            double along = (center.X - linePt.X) * axisU.X + (center.Y - linePt.Y) * axisU.Y;
            var mid = new Point2d(linePt.X + along * axisU.X, linePt.Y + along * axisU.Y);
            var pA = new Point2d(mid.X - half * axisU.X, mid.Y - half * axisU.Y);
            var pB = new Point2d(mid.X + half * axisU.X, mid.Y + half * axisU.Y);
            double tA = (pA.X - joint.X) * elevU.X + (pA.Y - joint.Y) * elevU.Y;
            double tB = (pB.X - joint.X) * elevU.X + (pB.Y - joint.Y) * elevU.Y;
            lo = Math.Min(tA, tB);
            hi = Math.Max(tA, tB);
            return hi - lo > 2.0 && hi >= -KirisAcilimKolonYakinCm && lo <= KirisAcilimKolonYakinCm;
        }

        private bool TryKirisAcilimColumnChord(
            Point2d[] poly, Point2d joint, Vector2d elevU, BeamInfo beam,
            out double lo, out double hi)
        {
            lo = hi = 0;
            if (poly == null || poly.Length < 3) return false;
            if (!TryKirisAcilimBeamStrip(beam, out Point2d axisPt, out Vector2d perp, out double sMin, out double sMax))
                return false;
            elevU = elevU.GetNormal();
            var clip = KirisAcilimClipStrip(poly, axisPt, perp, sMin, sMax);
            if (clip.Count < 2) return false;

            double axisS = (joint.X - axisPt.X) * perp.X + (joint.Y - axisPt.Y) * perp.Y;
            double bestLen = -1;
            double bestLat = double.MaxValue;
            double bestLo = 0, bestHi = 0;
            for (int i = 0; i < clip.Count; i++)
            {
                if (!TryKirisAcilimLineChord(poly, clip[i], joint, elevU, out double cLo, out double cHi)) continue;
                double len = cHi - cLo;
                if (len < 1.0) continue;
                double s = (clip[i].X - axisPt.X) * perp.X + (clip[i].Y - axisPt.Y) * perp.Y;
                double lat = Math.Abs(s - axisS);
                if (len > bestLen + 1.0 || (len >= bestLen - 1.0 && lat < bestLat))
                {
                    bestLen = len;
                    bestLat = lat;
                    bestLo = cLo;
                    bestHi = cHi;
                }
            }
            if (bestLen < 2.0) return false;
            if (bestHi < -KirisAcilimKolonYakinCm || bestLo > KirisAcilimKolonYakinCm) return false;
            lo = bestLo;
            hi = bestHi;
            return true;
        }

        /// <summary>Doğru <paramref name="through"/> + s·u ile poligon kenarlarının kesişimi; s düğüme göredir.</summary>
        private static bool TryKirisAcilimLineChord(
            Point2d[] poly, Point2d through, Point2d joint, Vector2d u,
            out double lo, out double hi)
        {
            lo = double.MaxValue;
            hi = double.MinValue;
            int n = poly.Length;
            for (int i = 0; i < n; i++)
            {
                Point2d a = poly[i];
                Point2d b = poly[(i + 1) % n];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double den = u.X * ey - u.Y * ex;
                if (Math.Abs(den) < 1e-8) continue;
                double dx = a.X - through.X, dy = a.Y - through.Y;
                double r = (dx * u.Y - dy * u.X) / den;
                if (r < -1e-6 || r > 1.0 + 1e-6) continue;
                double s = (dx * ey - dy * ex) / den;
                if (s < lo) lo = s;
                if (s > hi) hi = s;
            }
            if (hi - lo < 1.0) return false;
            double baseT = (through.X - joint.X) * u.X + (through.Y - joint.Y) * u.Y;
            lo += baseT;
            hi += baseT;
            return true;
        }

        private static List<Point2d> KirisAcilimClipStrip(Point2d[] poly, Point2d origin, Vector2d perp, double sMin, double sMax)
        {
            var cur = new List<Point2d>(poly);
            cur = KirisAcilimClipHalf(cur, origin, perp, sMax, keepBelow: true);
            return KirisAcilimClipHalf(cur, origin, perp, sMin, keepBelow: false);
        }

        private static List<Point2d> KirisAcilimClipHalf(List<Point2d> poly, Point2d origin, Vector2d perp, double limit, bool keepBelow)
        {
            var result = new List<Point2d>();
            int n = poly == null ? 0 : poly.Count;
            if (n == 0) return result;
            for (int i = 0; i < n; i++)
            {
                Point2d a = poly[i];
                Point2d b = poly[(i + 1) % n];
                double sa = (a.X - origin.X) * perp.X + (a.Y - origin.Y) * perp.Y;
                double sb = (b.X - origin.X) * perp.X + (b.Y - origin.Y) * perp.Y;
                bool ain = keepBelow ? sa <= limit + 1e-6 : sa >= limit - 1e-6;
                bool bin = keepBelow ? sb <= limit + 1e-6 : sb >= limit - 1e-6;
                if (ain && bin)
                    result.Add(b);
                else if (ain != bin)
                {
                    double den = sb - sa;
                    double t = Math.Abs(den) < 1e-9 ? 0.0 : (limit - sa) / den;
                    result.Add(new Point2d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
                    if (!ain && bin) result.Add(b);
                }
            }
            return result;
        }

        private bool TryKirisAcilimWallInterval(
            int floorNo, Point2d node, Vector2d beamU, int crossAxisId,
            out double lo, out double hi)
        {
            lo = hi = 0;
            bool found = false;
            double best = double.MaxValue;
            foreach (var wall in GetRawBeamsOnFloor(floorNo))
            {
                if (wall == null || wall.IsWallFlag != 1 || wall.WidthCm <= 1.0) continue;
                if (wall.FixedAxisId != crossAxisId) continue;
                if (!_axisService.TryIntersect(wall.FixedAxisId, wall.StartAxisId, out Point2d a)) continue;
                if (!_axisService.TryIntersect(wall.FixedAxisId, wall.EndAxisId, out Point2d b)) continue;
                Vector2d wv = b - a;
                if (wv.Length < 1.0) continue;
                Vector2d wu = wv.GetNormal();
                double sin = Math.Abs(wu.X * beamU.Y - wu.Y * beamU.X);
                if (sin < 0.25) continue;
                double t = (node.X - a.X) * wu.X + (node.Y - a.Y) * wu.Y;
                if (t < -15.0 || t > wv.Length + 15.0) continue;
                double lat = Math.Abs((node.X - a.X) * (-wu.Y) + (node.Y - a.Y) * wu.X);
                if (lat > wall.WidthCm * 0.5 + 20.0) continue;
                double half = (wall.WidthCm * 0.5) / sin;
                if (half < best)
                {
                    best = half;
                    lo = -half;
                    hi = half;
                    found = true;
                }
            }
            return found;
        }

        private string KirisAcilimEtiket(BeamInfo beam, int pad)
        {
            int floorNo = GetBeamFloorNo(beam.BeamId);
            FloorInfo floor = null;
            if (_model.Floors != null)
            {
                foreach (var f in _model.Floors)
                {
                    if (f.FloorNo == floorNo) { floor = f; break; }
                }
            }
            string kat = floor != null && !string.IsNullOrEmpty(floor.ShortName)
                ? floor.ShortName
                : floorNo.ToString(CultureInfo.InvariantCulture);
            int no = GetBeamNumero(beam.BeamId);
            string num = no.ToString("D" + pad, CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, "K{0}{1} ({2}/{3})",
                kat, num, (int)Math.Round(beam.WidthCm), (int)Math.Round(beam.HeightCm));
        }

        private string KirisAcilimAksYazi(int axisId)
        {
            if (_model.AxisX != null)
            {
                for (int i = 0; i < _model.AxisX.Count; i++)
                {
                    if (_model.AxisX[i].Id != axisId) continue;
                    if (_model.GprAxisXLabelByRow.TryGetValue(i + 1, out var gx) && !string.IsNullOrWhiteSpace(gx))
                        return gx.Trim();
                    return (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
            if (_model.AxisY != null)
            {
                for (int j = 0; j < _model.AxisY.Count; j++)
                {
                    if (_model.AxisY[j].Id != axisId) continue;
                    if (_model.GprAxisYLabelByRow.TryGetValue(j + 1, out var gy) && !string.IsNullOrWhiteSpace(gy))
                        return gy.Trim();
                    return j < 26
                        ? ((char)('A' + j)).ToString()
                        : "A" + (j - 25).ToString(CultureInfo.InvariantCulture);
                }
            }
            return axisId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
