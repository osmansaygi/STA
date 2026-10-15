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
        /// <summary>Boyuna donatı net paspayı (cm). Montaj, alt düz, ilave ve gövde dağılımı buna göre.</summary>
        private const double KirisAcilimMontajPaspayiCm = 5.5;
        /// <summary>Montaj donatısı etiketinin kiriş üst çizgisinden aşağı mesafesi (cm).</summary>
        private const double KirisAcilimMontajEtiketUsttenCm = 12.0;
        /// <summary>Yüzdeki iki çubuk sırası arası (cm). TBDY: boyuna çubuklar arası ≥ 2.5 cm.</summary>
        private const double KirisAcilimCubukAraCm = 2.5;
        /// <summary>Üst ve alt donatı seviyeleri arasında kalan gövdeye bu kadar pay bırakılır (cm).</summary>
        private const double KirisAcilimGovdeKenarPayiCm = 2.0;
        /// <summary>Alt düz donatı net paspayı (cm).</summary>
        private const double KirisAcilimDuzPaspayiCm = 5.5;
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
        /// <summary>Üst ilave rengi (ACI 40). ACI 5 mavi zeminde silik kalıyordu. Sol ve sağ aynı.</summary>
        private const short KirisAcilimRenkIlaveUst = 40;
        /// <summary>Alt ilave rengi (ACI 1). Sol ve sağ aynı.</summary>
        private const short KirisAcilimRenkIlaveAlt = 1;
        /// <summary>Üst ilave montajın bu kadar altında, alt ilave düz donatının bu kadar üstünde (cm). Yalnız çizim; boy hesabına girmez.</summary>
        private const double KirisAcilimIlaveAyirCm = 3.0;
        /// <summary>Gönye köşe yayı (cm). Çizim yarıçapı.</summary>
        private const double KirisAcilimGonyeYaricapCm = 3.0;
        /// <summary>Birinci gönye düşey bacağı en az bu kadar (cm). Kiriş yüksekliğinin yarısı daha büyükse o geçerli.</summary>
        private const double KirisAcilimGonyeMinCm = 20.0;
        /// <summary>Konsol serbest uç gönyesi en az bu kadar (cm). lb ve 12φ bu uçta aranmaz.</summary>
        private const double KirisAcilimKonsolGonyeMinCm = 30.0;

        /// <summary>
        /// Kenetlenme: lb/φ, C30 + B420C (TS 500 9.1.2.1, STA çizimiyle aynı).
        /// lb = max(0.12·fyd/fctd·φ, 20φ). fyd=365.2, fctd=1.2667 → 34.60·φ; konum I ×1.4 → 48.44·φ.
        /// </summary>
        private const double KirisAcilimLbPhiKonumI = 48.44;
        private const double KirisAcilimLbPhiKonumII = 34.60;
        /// <summary>TBDY 7.4.3.1(a): mesnetten sonra açıklığa Ln/4.</summary>
        private const double KirisAcilimUstSurekliOran = 0.25;
        /// <summary>hk + c ≥ 50φ. φ çarpanı; yüzden itibaren tek başına uygulanmaz.</summary>
        private const double KirisAcilimUstIlavePhi = 50.0;
        /// <summary>TBDY 7.4.3.1(b): kenar mesnette kanca yatay ≥ 0.4·lb, düşey ≥ 12φ.</summary>
        private const double KirisAcilimKenarYatayOran = 0.4;
        private const double KirisAcilimKenarDuseyPhi = 12.0;
        /// <summary>TS 500 9.1.1: konum II üst sınırı; serbest üst yüzden bu kadar aşağıdaki yatay çubuk konum II sayılır (cm).</summary>
        private const double KirisAcilimKonumIIUsttenCm = 30.0;
        /// <summary>Ölçü yazılarını 5 cm'ye yuvarla (STA çizim alışkanlığı).</summary>
        private const double KirisAcilimYuvarlamaCm = 5.0;
        /// <summary>Montaj çubuğu bu boyu geçmez (cm). Gönye boyu dahildir, köşe yayı düşülmez.</summary>
        private const double KirisAcilimMontajMaxBoyCm = 1200.0;
        /// <summary>1200 cm'ye tamamlarken bindirme Ln/2 dışına en fazla bu kadar (cm) taşabilir.</summary>
        private const double KirisAcilimMontajLnToleransCm = 20.0;
        /// <summary>Montaj açılımı, en alttaki kolon kesiminin bu kadar altında (cm).</summary>
        private const double KirisAcilimMontajAcilimCm = 45.0;
        /// <summary>İlave açılımı, en alttaki kolon kesiminin bu kadar altında (cm).</summary>
        private const double KirisAcilimIlaveAcilimCm = 30.0;
        /// <summary>Gövde açılımı, en alttaki kolon kesiminin bu kadar altında (cm).</summary>
        private const double KirisAcilimGovdeAcilimCm = 60.0;
        /// <summary>Yan yana açılımlar bu kadar şaşırtılır (cm).</summary>
        private const double KirisAcilimMontajSasirtmaCm = 5.0;
        private double _kirisAcilimKesimY;
        private int _kirisAcilimIlaveSira;
        private int _kirisAcilimMontajSira;
        private int _kirisAcilimGovdeSira;

        /// <summary>5 cm katına yuvarlar.</summary>
        private static double KirisAcilimYuvarla(double x)
        {
            if (x <= 0) return x;
            return Math.Round(x / KirisAcilimYuvarlamaCm) * KirisAcilimYuvarlamaCm;
        }

        /// <summary>
        /// Mesnet yüzünden açıklığa giren ilave boyu (cm), üst ve alt.
        /// c = Ln/4. 50φ ve lb yüzden itibaren değil, mesnet genişliği ile birlikte aranır:
        /// hk + c ≥ max(50φ, lb). Mesnet genişliği yetiyorsa çubuk yüzden yalnız Ln/4 gider;
        /// yetmiyorsa eksik kadar uzar. hk = 0 ise (serbest uç) taban max(50φ, lb) olur.
        /// </summary>
        private double KirisAcilimIlaveYuzdenCm(double lnNetCm, double hkCm, double capMm, bool ust)
        {
            if (capMm <= 0 || lnNetCm <= 0) return 0;
            double c = KirisAcilimUstSurekliOran * lnNetCm;
            double phi50 = KirisAcilimUstIlavePhi * capMm / 10.0;
            double lb = (ust ? KirisAcilimLbPhiKonumI : KirisAcilimLbPhiKonumII) * capMm / 10.0;
            double taban = (phi50 > lb ? phi50 : lb) - (hkCm > 0 ? hkCm : 0);
            if (taban > c) c = taban;
            if (c < 0) c = 0;
            return KirisAcilimYuvarla(c);
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

        /// <summary>KMX ofset 108 montaj, 124 gövde, 128 sol üst, 132 sol alt, 136 sağ üst, 140 sağ alt: adet + çap (mm).</summary>
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
                // İki ucu da bağlı parça ortaya denk gelir. Serbest uçtan başlanmazsa yürüyüş tek yöne gider
                // ve diğer komşu (KB-99 ← KB-100 → KB-104) ayrı açılımda kalır.
                int start = i;
                if (linkSeg[start, 0] >= 0 && linkSeg[start, 1] >= 0)
                {
                    int at = start;
                    int leave = 0;
                    var walked = new HashSet<int> { start };
                    for (int g = 0; g < n; g++)
                    {
                        int nxt = linkSeg[at, leave];
                        if (nxt < 0 || !walked.Add(nxt)) break;
                        int arrive = linkEnd[at, leave];
                        int nxtLeave = arrive == 0 ? 1 : 0;
                        if (linkSeg[nxt, nxtLeave] < 0)
                        {
                            start = nxt;
                            break;
                        }
                        at = nxt;
                        leave = nxtLeave;
                    }
                }
                int entryEnd = linkSeg[start, 0] < 0 ? 0 : (linkSeg[start, 1] < 0 ? 1 : 0);
                var chain = new List<KirisAcilimSpan>();
                int cur = start;
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
            _kirisAcilimKesimY = KirisAcilimKolonKesimY(chain, sup, yShift);
            _kirisAcilimIlaveSira = 0;
            _kirisAcilimMontajSira = 0;
            _kirisAcilimGovdeSira = 0;
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
            var govdeYutulan = new HashSet<int>();
            int run = 0;
            while (run < m)
            {
                int runEnd = run;
                while (runEnd + 1 < m && chain[runEnd + 1].Beam.BeamId == chain[run].Beam.BeamId)
                    runEnd++;
                KirisAcilimDrawRun(tr, ms, db, chain, run, runEnd, x0, yShift, pad, gf, concrete, sup, ucTipi, extStart, extEnd, kolonGrow0, kolonGrow1, montaj, gorunus, govdeYutulan);
                run = runEnd + 1;
            }

            KirisAcilimMontajBirlesik(tr, ms, db, chain, x0, yShift, sup, montaj, gorunus, ucTipi);

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

        /// <summary>En alttaki kolon kesim çizgisi. Aşağı uzayan kolon yoksa en alt kiriş altı.</summary>
        private static double KirisAcilimKolonKesimY(
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, double yShift)
        {
            double en = double.MaxValue;
            double alt = double.MaxValue;
            int m = chain == null ? 0 : chain.Count;
            for (int i = 0; i <= m; i++)
            {
                KirisAcilimSpanAt(chain, i, out KirisAcilimSpan left, out KirisAcilimSpan right);
                double kot = KirisAcilimJointTop(left, right);
                double soffit = KirisAcilimJointSoffit(left, right, kot);
                double yBot = yShift + soffit;
                if (yBot < alt) alt = yBot;
                if (sup == null || i >= sup.Length) continue;
                if (sup[i].X1 - sup[i].X0 <= 2.0 || !sup[i].ExtendDown) continue;
                double yCut = yBot - KirisAcilimKutupCm;
                if (yCut < en) en = yCut;
            }
            return en < 1e8 ? en : alt;
        }

        /// <summary>Tek çubuğun açılımı. Eksen, kolon kesiminin altında; yan yana olanlar 5 cm şaşar. Boy yazılır.</summary>
        private void KirisAcilimAcilimCiz(
            Transaction tr, BlockTableRecord ms, Database db,
            List<Point2d> pts, short renk, int adet, int cap, double boy,
            double asagiCm, ref int sira)
        {
            if (pts == null || pts.Count < 2 || adet <= 0 || cap <= 0 || boy < 1.0) return;
            if (_kirisAcilimKesimY > 1e8) return;
            int a = 0;
            int b = pts.Count - 1;
            if (pts.Count >= 3 && Math.Abs(pts[1].X - pts[0].X) < 1.0 && Math.Abs(pts[1].Y - pts[0].Y) > 1.0)
                a = 1;
            if (b - a >= 1 && Math.Abs(pts[b].X - pts[b - 1].X) < 1.0 && Math.Abs(pts[b].Y - pts[b - 1].Y) > 1.0)
                b--;
            double yEksen = 0.5 * (pts[a].Y + pts[b].Y);
            double xEt = 0.5 * (pts[a].X + pts[b].X);
            double yHedef = _kirisAcilimKesimY - asagiCm - (sira % 2) * KirisAcilimMontajSasirtmaCm;
            sira++;
            double dy = yHedef - yEksen;
            var ac = new List<Point2d>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                ac.Add(new Point2d(pts[i].X, pts[i].Y + dy));
            KirisAcilimDonatiPl(tr, ms, renk, ac);
            if (db == null) return;
            string lbl = adet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + cap.ToString(CultureInfo.InvariantCulture)
                + " L=" + Math.Round(boy).ToString("0", CultureInfo.InvariantCulture);
            DrawBeamLabel(tr, ms, db, new Point3d(xEt, yHedef, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            KirisAcilimYaziKutusuUstune(tr, yHedef + 3.0);
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
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            List<KirisAcilimSaplama> gorunus, HashSet<int> govdeYutulan)
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
                KirisAcilimDuzNoktaSil(pts, true);
                Polygon beamPoly = KirisAcilimPolygon(gf, pts);
                if (beamPoly != null) concrete.Add(beamPoly);
                part = j + 1;
            }

            double net0;
            double net1;
            KirisAcilimKolonYuzu(chain, i0, i1, sup, out net0, out net1);
            double xMid = 0.5 * (net0 + net1);
            double yLine = yShift + KirisAcilimKotAt(chain, i0, i1, xMid);
            DrawBeamLabel(tr, ms, db,
                new Point3d(x0 + xMid, yLine, 0),
                KirisAcilimEtiket(chain[i0].Beam, pad),
                KirisAcilimBaslikYukseklikCm, 0.0, LayerKirisYazisi, useMiddleCenter: true);
            KirisAcilimYaziKutusuUstune(tr, yLine + 3.0);
            KirisAcilimUcNoktalari(tr, ms, db, chain, i0, i1, sup, ucTipi, x0, yShift, net0, net1);
            // Donatı, çizilen 1-2 ve 3-4 numaralarının hizasından başlar (kolon yüzü veya saplama kesitinin iç yüzü).
            KirisAcilimNumaraAraligi(chain, i0, i1, net0, net1, gorunus, out double don0, out double don1);
            KirisAcilimDuzDonatiYedek(tr, ms, db, chain, i0, i1, x0, yShift, don0, don1, montaj, sup, gorunus, ucTipi);
            KirisAcilimGovdeDonati(tr, ms, db, chain, i0, i1, x0, yShift, don0, don1, montaj, sup, gorunus, govdeYutulan);
            KirisAcilimIlaveDonati(tr, ms, db, chain, i0, i1, x0, yShift, don0, don1, montaj, sup, gorunus, ucTipi);
        }

        /// <summary>
        /// Kolon, sağdaki açıklığın başlangıcına (yoksa bu açıklığın bitişine) çizilir.
        /// İki açıklık aksı arasında boşluk varsa X1 ile sonraki X0 aynı nokta değildir;
        /// numara ve donatı bu boşluğu değil, çizilen kolon yüzünü kullanır.
        /// </summary>
        private static void KirisAcilimKolonYuzu(
            List<KirisAcilimSpan> chain, int i0, int i1, KirisAcilimSupport[] sup,
            out double net0, out double net1)
        {
            net0 = chain[i0].X0;
            net1 = chain[i1].X1;
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
        }

        /// <summary>
        /// Sol uç 1-2, sağ uç 3-4. Kolon mesnette iç yüz; saplama/basit uçta numaralanan kesitin kirişe bakan kenarı.
        /// </summary>
        private static void KirisAcilimNumaraAraligi(
            List<KirisAcilimSpan> chain, int i0, int i1,
            double net0, double net1, List<KirisAcilimSaplama> gorunus,
            out double don0, out double don1)
        {
            don0 = net0;
            don1 = net1;
            if (gorunus == null || chain == null || i0 > i1) return;
            double leftStation = chain[i0].X0;
            double rightStation = chain[i1].X1;
            double bestSol = double.MaxValue;
            double bestSag = double.MaxValue;
            foreach (KirisAcilimSaplama spm in gorunus)
            {
                if (!spm.IzdusumVar) continue;
                double mid = 0.5 * (spm.X0 + spm.X1);
                double tol = 0.5 * (spm.X1 - spm.X0) + 40.0;
                if (spm.SolUc)
                {
                    double d = Math.Abs(mid - leftStation);
                    if (d <= tol && d < bestSol)
                    {
                        bestSol = d;
                        don0 = spm.X1;
                    }
                }
                if (spm.SagUc)
                {
                    double d = Math.Abs(mid - rightStation);
                    if (d <= tol && d < bestSag)
                    {
                        bestSag = d;
                        don1 = spm.X0;
                    }
                }
            }
            if (don1 - don0 < 1.0)
            {
                don0 = net0;
                don1 = net1;
            }
        }

        /// <summary>
        /// Gövde donatısı: montaj ile alt düz arasında eşit sıra.
        /// Aynı çap, adet, kesit ve kottaki yan yana kirişler, toplam 1200 cm'yi geçmiyorsa bindirmesiz tek çubuktur.
        /// Kenar mesnette max(50φ, lb) kadar içeri girer; sığmazsa 12φ gönye. Düzen değişince bindirme durur.
        /// </summary>
        private void KirisAcilimGovdeDonati(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            HashSet<int> govdeYutulan)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            if (m.GovdeAdet <= 0 || m.GovdeCap <= 0) return;

            double h = beam.HeightCm;
            double p = KirisAcilimMontajPaspayiCm;
            double ust = p;
            double alt = h - KirisAcilimDuzPaspayiCm;
            int gorunen = (m.GovdeAdet + 1) / 2;
            if (gorunen < 1) gorunen = 1;
            bool yutuldu = govdeYutulan != null && govdeYutulan.Contains(beam.BeamId);
            if (!yutuldu)
            {
                int son0 = i0;
                int son1 = i1;
                double kotRef = chain[i0].Kot0;
                for (int guard = 0; guard < 40; guard++)
                {
                    if (!KirisAcilimKomsuAralik(chain, son0, son1, false, out int j0, out int j1)) break;
                    if (chain[j0]?.Beam == null || !montaj.TryGetValue(chain[j0].Beam, out KirisAcilimMontajBilgi karsi)) break;
                    if (!KirisAcilimGovdeAyniDuzen(beam, m, chain[j0].Beam, karsi, kotRef, chain[j0].Kot0, chain[j1].Kot1, chain[son1].Kot1))
                        break;
                    bool sigar = true;
                    double nSatir = gorunen + 1.0;
                    double phiCm = m.GovdeCap / 10.0;
                    for (int k = 1; k <= gorunen; k++)
                    {
                        double ofs = ust + (alt - ust) * k / nSatir;
                        double boy = KirisAcilimGovdeGrupBoy(
                            chain, i0, i1, j0, j1, sup, gorunus, montaj, x0, yShift, m, h, ofs, phiCm);
                        if (boy > KirisAcilimMontajMaxBoyCm + 0.49)
                        {
                            sigar = false;
                            break;
                        }
                    }
                    if (!sigar) break;
                    if (govdeYutulan != null) govdeYutulan.Add(chain[j0].Beam.BeamId);
                    son0 = j0;
                    son1 = j1;
                }

                double n = gorunen + 1.0;
                double phi = m.GovdeCap / 10.0;
                for (int k = 1; k <= gorunen; k++)
                {
                    double ofs = ust + (alt - ust) * k / n;
                    KirisAcilimGovdeCubuk(
                        tr, ms, db, chain, i0, i1, son0, son1, sup, gorunus, montaj, x0, yShift, m, h, ofs, phi, k == 1);
                }
            }

            string lbl = m.GovdeAdet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + m.GovdeCap.ToString(CultureInfo.InvariantCulture);
            double chainMid = 0.5 * (net0 + net1);
            double yMid = yShift + KirisAcilimKotAt(chain, i0, i1, chainMid) - h * 0.5;
            DrawBeamLabel(tr, ms, db, new Point3d(x0 + chainMid, yMid, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
        }

        /// <summary>Aynı gövde düzeni: çap, adet, kesit ve kot.</summary>
        private static bool KirisAcilimGovdeAyniDuzen(
            BeamInfo kendi, KirisAcilimMontajBilgi mk, BeamInfo karsi, KirisAcilimMontajBilgi mc,
            double kotRef, double kotSol, double kotSag, double kotOnce)
        {
            if (kendi == null || karsi == null) return false;
            if (mk.GovdeAdet <= 0 || mc.GovdeAdet != mk.GovdeAdet || mc.GovdeCap != mk.GovdeCap) return false;
            if (Math.Abs(kendi.WidthCm - karsi.WidthCm) > 0.5) return false;
            if (Math.Abs(kendi.HeightCm - karsi.HeightCm) > 0.5) return false;
            if (Math.Abs(kotSol - kotRef) > 2.0 || Math.Abs(kotSag - kotRef) > 2.0) return false;
            if (Math.Abs(kotOnce - kotRef) > 2.0) return false;
            return true;
        }

        /// <summary>Soldaki kirişten sağdaki kirişin ucuna kadar gövde boyu. Gönye dahildir.</summary>
        private double KirisAcilimGovdeGrupBoy(
            List<KirisAcilimSpan> chain, int i0, int i1, int son0, int son1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            double x0, double yShift, KirisAcilimMontajBilgi m, double h, double ofs, double phiCm)
        {
            var pts = new List<Point2d>();
            KirisAcilimGovdeNokta(chain, i0, i1, son0, son1, sup, gorunus, montaj, x0, yShift, m, h, ofs, phiCm, pts);
            double boy = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].X - pts[i - 1].X;
                double dy = pts[i].Y - pts[i - 1].Y;
                boy += Math.Sqrt(dx * dx + dy * dy);
            }
            return boy;
        }

        private void KirisAcilimGovdeCubuk(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1, int son0, int son1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            double x0, double yShift, KirisAcilimMontajBilgi m, double h, double ofs, double phiCm, bool acil)
        {
            var pts = new List<Point2d>();
            KirisAcilimGovdeNokta(chain, i0, i1, son0, son1, sup, gorunus, montaj, x0, yShift, m, h, ofs, phiCm, pts);
            if (pts.Count < 2) return;
            KirisAcilimDonatiPl(tr, ms, KirisAcilimRenkGovde, pts);
            if (!acil) return;
            double boy = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].X - pts[i - 1].X;
                double dy = pts[i].Y - pts[i - 1].Y;
                boy += Math.Sqrt(dx * dx + dy * dy);
            }
            KirisAcilimAcilimCiz(tr, ms, db, pts, KirisAcilimRenkGovde, m.GovdeAdet, m.GovdeCap, boy, KirisAcilimGovdeAcilimCm, ref _kirisAcilimGovdeSira);
        }

        private void KirisAcilimGovdeNokta(
            List<KirisAcilimSpan> chain, int i0, int i1, int son0, int son1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            double x0, double yShift, KirisAcilimMontajBilgi m, double h, double ofs, double phiCm,
            List<Point2d> pts)
        {
            KirisAcilimKolonYuzu(chain, i0, i1, sup, out double net0, out double net1);
            KirisAcilimNumaraAraligi(chain, i0, i1, net0, net1, gorunus, out double don0, out _);
            KirisAcilimKolonYuzu(chain, son0, son1, sup, out double sonNet0, out double sonNet1);
            KirisAcilimNumaraAraligi(chain, son0, son1, sonNet0, sonNet1, gorunus, out _, out double don1);
            double xA = x0 + don0;
            double xB = x0 + don1;
            double yA = yShift + KirisAcilimKotAt(chain, i0, i1, don0) - ofs;
            double yB = yShift + KirisAcilimKotAt(chain, son0, son1, don1) - ofs;
            double boy = KirisAcilimGovdeBoy(phiCm, ofs);
            bool gonyeSol = KirisAcilimGovdeUc(
                chain, i0, i1, sup, gorunus, montaj, x0, yShift, true, xA, yA, m, boy, out double x1);
            bool gonyeSag = KirisAcilimGovdeUc(
                chain, son0, son1, sup, gorunus, montaj, x0, yShift, false, xB, yB, m, boy, out double x2);
            if (x2 - x1 < 1.0) return;
            if (gonyeSol) KirisAcilimGovdeGonye(pts, true, x1, yA, h, ofs, phiCm);
            pts.Add(new Point2d(x1, yA));
            pts.Add(new Point2d(x2, yB));
            if (gonyeSag) KirisAcilimGovdeGonye(pts, false, x2, yB, h, ofs, phiCm);
        }

        /// <summary>Gövde kenetlenmesi: 50φ ve lb'den büyük olan.</summary>
        private static double KirisAcilimGovdeBoy(double phiCm, double usttenCm)
        {
            if (phiCm <= 0) return 0;
            bool konumI = usttenCm <= KirisAcilimKonumIIUsttenCm;
            double lb = (konumI ? KirisAcilimLbPhiKonumI : KirisAcilimLbPhiKonumII) * phiCm;
            double phi50 = KirisAcilimUstIlavePhi * phiCm;
            return phi50 > lb ? phi50 : lb;
        }

        /// <summary>
        /// Gövde ucunun X'i. Ara ve aynı düzende bindirme mesnet ortasında.
        /// Ara ve farklı düzende kendi yüzünden boy kadar diğer tarafa. Kenarda paspayına kadar, yetmezse gönye.
        /// </summary>
        private bool KirisAcilimGovdeUc(
            List<KirisAcilimSpan> chain, int i0, int i1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            double x0, double yShift, bool sola, double xYuz, double yBar, KirisAcilimMontajBilgi kendi, double boy,
            out double xUc)
        {
            xUc = xYuz;
            double p = KirisAcilimMontajPaspayiCm;
            if (KirisAcilimKomsuAralik(chain, i0, i1, sola, out int j0, out int j1))
            {
                if (chain[j0]?.Beam != null)
                {
                    double xN = sola ? chain[j1].X1 : chain[j0].X0;
                    double nTop = yShift + KirisAcilimKotAt(chain, j0, j1, xN);
                    double nBot = nTop - chain[j0].Beam.HeightCm;
                    if (yBar > nTop - p || yBar < nBot + p)
                    {
                        // Komşu kesite sığmaz; yine de mesnete girer, boy yetmezse gönye mesnet içindedir.
                        double hkDis = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, sola);
                        double sigarDis = hkDis - p;
                        if (sigarDis < 0) sigarDis = 0;
                        double girDis = boy < sigarDis ? boy : sigarDis;
                        if (girDis < 0.5)
                            xUc = sola ? xYuz + p : xYuz - p;
                        else
                            xUc = sola ? xYuz - girDis : xYuz + girDis;
                        return girDis + 0.5 < boy;
                    }
                }
                KirisAcilimSpan yakin = sola ? chain[j1] : chain[j0];
                bool ayni = yakin?.Beam != null && montaj != null
                    && montaj.TryGetValue(yakin.Beam, out KirisAcilimMontajBilgi karsi)
                    && karsi.GovdeAdet == kendi.GovdeAdet && karsi.GovdeCap == kendi.GovdeCap
                    && karsi.GovdeAdet > 0;
                KirisAcilimKolonYuzu(chain, j0, j1, sup, out double n0, out double n1);
                KirisAcilimNumaraAraligi(chain, j0, j1, n0, n1, gorunus, out double d0, out double d1);
                double yuzKarsi = x0 + (sola ? d1 : d0);
                double ucKarsi = x0 + (sola ? d0 : d1);
                double orta = 0.5 * (xYuz + yuzKarsi);
                if (ayni)
                    xUc = sola ? orta - boy * 0.5 : orta + boy * 0.5;
                else
                    xUc = sola ? xYuz - boy : xYuz + boy;
                if (sola && xUc < ucKarsi + p) xUc = ucKarsi + p;
                if (!sola && xUc > ucKarsi - p) xUc = ucKarsi - p;
                return false;
            }

            double hk = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, sola);
            double sigar = hk - p;
            if (sigar < 0) sigar = 0;
            double gir = boy < sigar ? boy : sigar;
            if (gir < 0.5)
                xUc = sola ? xYuz + p : xYuz - p;
            else
                xUc = sola ? xYuz - gir : xYuz + gir;
            return gir + 0.5 < boy;
        }

        /// <summary>Gövde gönyesi 12φ. Üst yarı aşağı, alt yarı yukarı kıvrılır; paspayını geçmez.</summary>
        private static void KirisAcilimGovdeGonye(
            List<Point2d> pts, bool sola, double x, double y, double h, double usttenCm, double phiCm)
        {
            if (pts == null || phiCm <= 0) return;
            double b = KirisAcilimKenarDuseyPhi * phiCm;
            double p = KirisAcilimMontajPaspayiCm;
            bool asagi = usttenCm <= h * 0.5;
            double yHook = asagi ? y - b : y + b;
            double yTop = y + usttenCm - p;
            double yBot = yTop - h + 2.0 * p;
            if (yHook > yTop) yHook = yTop;
            if (yHook < yBot) yHook = yBot;
            if (Math.Abs(yHook - y) < 1.0) return;
            var hook = new Point2d(x, yHook);
            if (sola) pts.Insert(0, hook);
            else pts.Add(hook);
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

        /// <summary>İlave donatı seviyeleri. Üst ilaveler kiriş üstünden, alt ilaveler altından 5,5 cm içeride; çift sıra 1 çap aralıkla.</summary>
        private static double[] KirisAcilimIlaveSeviye(double h, bool ust)
        {
            double y = ust ? KirisAcilimMontajPaspayiCm : h - KirisAcilimDuzPaspayiCm;
            return new[] { y, y - 1.2 };
        }

        /// <summary>
        /// İlave donatı: düğüm noktasından kiriş içine, yönetmelik boyunda.
        /// Üst ve alt ilave: mesnet yüzünden Ln/4. 50φ, mesnet genişliği hk ile birlikte aranır (hk+c ≥ 50φ).
        /// Komşu kiriş varsa aynı kural komşu açıklıkta da uygulanır; çubuk gövdede paspayı kadar içeride kalamıyorsa uzamaz.
        /// Renk: üst ilavelerin hepsi aynı, alt ilavelerin hepsi aynı. Tek çizgi;
        /// üst ilave montajın 3 cm altında, alt ilave düz donatının 3 cm üstünde.
        /// KMX solu kirişin ST4 başlangıç (StartAxisId), sağı bitiş ucudur. Bakış açısı (yatayda soldan sağa,
        /// düşeyde alttan üste) uçları çevirdiği için sabit "sol=sağ ilave" eşlemesi yapılmaz; çizimin sol ucu
        /// hangi uca (başlangıç/bitiş) denk geliyorsa ilave ona göre yerleştirilir.
        /// </summary>
        private void KirisAcilimIlaveDonati(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> ilave,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
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

            // Parçalı kirişte sol uç ilk kaydın, sağ uç son kaydın KMX'indedir (KB-008 sağ 2Ø12 ikinci parçada).
            KirisAcilimMontajBilgi mSol = m;
            KirisAcilimMontajBilgi mSag = m;
            if (chain[i0].Beam != null && ilave.TryGetValue(chain[i0].Beam, out KirisAcilimMontajBilgi mIlk)) mSol = mIlk;
            if (chain[i1].Beam != null && ilave.TryGetValue(chain[i1].Beam, out KirisAcilimMontajBilgi mSon)) mSag = mSon;
            bool solBas = chain[i0].Cross0 == chain[i0].Beam.StartAxisId;
            bool sagBas = chain[i1].Beam != null && chain[i1].Cross1 == chain[i1].Beam.StartAxisId;

            int luAdet, luCap, laAdet, laCap, ruAdet, ruCap, raAdet, raCap;
            if (solBas)
            {
                luAdet = mSol.SolUstAdet; luCap = mSol.SolUstCap;
                laAdet = mSol.SolAltAdet; laCap = mSol.SolAltCap;
            }
            else
            {
                luAdet = mSol.SagUstAdet; luCap = mSol.SagUstCap;
                laAdet = mSol.SagAltAdet; laCap = mSol.SagAltCap;
            }
            if (sagBas)
            {
                ruAdet = mSag.SolUstAdet; ruCap = mSag.SolUstCap;
                raAdet = mSag.SolAltAdet; raCap = mSag.SolAltCap;
            }
            else
            {
                ruAdet = mSag.SagUstAdet; ruCap = mSag.SagUstCap;
                raAdet = mSag.SagAltAdet; raCap = mSag.SagAltCap;
            }

            double hkSol = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, true);
            double hkSag = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, false);
            double luU = KirisAcilimIlaveYuzdenCm(lnNet, hkSol, luCap, true);
            double laU = KirisAcilimIlaveYuzdenCm(lnNet, hkSol, laCap, false);
            double ruU = KirisAcilimIlaveYuzdenCm(lnNet, hkSag, ruCap, true);
            double raU = KirisAcilimIlaveYuzdenCm(lnNet, hkSag, raCap, false);

            double UstY(double x)
            {
                double span = xB - xA;
                double u = span < 1.0 ? 0.0 : (x - xA) / span;
                if (u < 0) u = 0;
                if (u > 1) u = 1;
                double top = topA + (topB - topA) * u;
                return top - KirisAcilimMontajPaspayiCm - KirisAcilimIlaveAyirCm;
            }
            double AltY(double x)
            {
                double span = xB - xA;
                double u = span < 1.0 ? 0.0 : (x - xA) / span;
                if (u < 0) u = 0;
                if (u > 1) u = 1;
                double top = topA + (topB - topA) * u;
                return top - h + KirisAcilimDuzPaspayiCm + KirisAcilimIlaveAyirCm;
            }

            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out double xSolGonye, out double xSagGonye);
            bool tekBasina = KirisAcilimKonsolTekBasina(chain, i0, i1, solSerbest, sagSerbest);
            if (tekBasina)
            {
                luAdet = 0; laAdet = 0; ruAdet = 0; raAdet = 0;
            }
            double xLu = sagSerbest ? xSagGonye : xA + luU;
            double xLa = sagSerbest ? xSagGonye : xA + laU;
            double xRu = solSerbest ? xSolGonye : xB - ruU;
            double xRa = solSerbest ? xSolGonye : xB - raU;
            double xYuzSol = solSerbest ? xSolGonye : xA;
            double xYuzSag = sagSerbest ? xSagGonye : xB;
            double xSolUst = KirisAcilimIlaveKomsuX(chain, i0, i1, sup, gorunus, x0, yShift, true, UstY(xA), xA, luCap, true);
            double xSolAlt = KirisAcilimIlaveKomsuX(chain, i0, i1, sup, gorunus, x0, yShift, true, AltY(xA), xA, laCap, false);
            double xSagUst = KirisAcilimIlaveKomsuX(chain, i0, i1, sup, gorunus, x0, yShift, false, UstY(xB), xB, ruCap, true);
            double xSagAlt = KirisAcilimIlaveKomsuX(chain, i0, i1, sup, gorunus, x0, yShift, false, AltY(xB), xB, raCap, false);
            bool solUstKonsol = KirisAcilimKonsolKomsuGonye(chain, i0, i1, ucTipi, gorunus, sup, x0, yShift, true, UstY(xA), out double xSolUstKonsol, out double hSolUstKonsol);
            bool solAltKonsol = KirisAcilimKonsolKomsuGonye(chain, i0, i1, ucTipi, gorunus, sup, x0, yShift, true, AltY(xA), out double xSolAltKonsol, out double hSolAltKonsol);
            bool sagUstKonsol = KirisAcilimKonsolKomsuGonye(chain, i0, i1, ucTipi, gorunus, sup, x0, yShift, false, UstY(xB), out double xSagUstKonsol, out double hSagUstKonsol);
            bool sagAltKonsol = KirisAcilimKonsolKomsuGonye(chain, i0, i1, ucTipi, gorunus, sup, x0, yShift, false, AltY(xB), out double xSagAltKonsol, out double hSagAltKonsol);
            if (solUstKonsol) xSolUst = xSolUstKonsol;
            if (solAltKonsol) xSolAlt = xSolAltKonsol;
            if (sagUstKonsol) xSagUst = xSagUstKonsol;
            if (sagAltKonsol) xSagAlt = xSagAltKonsol;
            double odaSolUst = KirisAcilimGonyeRoom(chain, i0, i1, yShift, true, true, true, UstY(xA), h, out double hSolUst);
            double odaSolAlt = KirisAcilimGonyeRoom(chain, i0, i1, yShift, true, false, true, AltY(xA), h, out double hSolAlt);
            double odaSagUst = KirisAcilimGonyeRoom(chain, i0, i1, yShift, false, true, true, UstY(xB), h, out double hSagUst);
            double odaSagAlt = KirisAcilimGonyeRoom(chain, i0, i1, yShift, false, false, true, AltY(xB), h, out double hSagAlt);
            KirisAcilimIlaveCubuk(tr, ms, db, xSolUst, xYuzSol, xLu, UstY, true, true, hkSol, h, luAdet, luCap, KirisAcilimRenkIlaveUst, odaSolUst, hSolUst, solSerbest, sagSerbest, odaSagUst, h, solUstKonsol, hSolUstKonsol);
            KirisAcilimIlaveCubuk(tr, ms, db, xSolAlt, xYuzSol, xLa, AltY, true, false, hkSol, h, laAdet, laCap, KirisAcilimRenkIlaveAlt, odaSolAlt, hSolAlt, solSerbest, sagSerbest, odaSagAlt, h, solAltKonsol, hSolAltKonsol);
            KirisAcilimIlaveCubuk(tr, ms, db, xSagUst, xYuzSag, xRu, UstY, false, true, hkSag, h, ruAdet, ruCap, KirisAcilimRenkIlaveUst, odaSagUst, hSagUst, sagSerbest, solSerbest, odaSolUst, h, sagUstKonsol, hSagUstKonsol);
            KirisAcilimIlaveCubuk(tr, ms, db, xSagAlt, xYuzSag, xRa, AltY, false, false, hkSag, h, raAdet, raCap, KirisAcilimRenkIlaveAlt, odaSagAlt, hSagAlt, sagSerbest, solSerbest, odaSolAlt, h, sagAltKonsol, hSagAltKonsol);
        }

        /// <summary>Tek başına konsol: bir uç serbest, sabit uçta devam eden kiriş yok.</summary>
        private static bool KirisAcilimKonsolTekBasina(
            List<KirisAcilimSpan> chain, int i0, int i1, bool solSerbest, bool sagSerbest)
        {
            if (solSerbest == sagSerbest) return false;
            bool sabitSola = sagSerbest;
            return !KirisAcilimKomsuAralik(chain, i0, i1, sabitSola, out _, out _);
        }

        /// <summary>
        /// Komşu açıklık konsolsa ve çubuk gövdesine sığıyorsa serbest uç gönyesinin X'i.
        /// İlave, komşu kirişin datasında olsa da konsolun içinden serbest uca gider.
        /// </summary>
        private bool KirisAcilimKonsolKomsuGonye(
            List<KirisAcilimSpan> chain, int i0, int i1,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi, List<KirisAcilimSaplama> gorunus,
            KirisAcilimSupport[] sup, double x0, double yShift, bool sola, double yBar,
            out double xGonye, out double hKomsu)
        {
            xGonye = 0;
            hKomsu = 0;
            if (!KirisAcilimKomsuAralik(chain, i0, i1, sola, out int j0, out int j1)) return false;
            KirisAcilimKonsolUclari(chain, j0, j1, ucTipi, gorunus, x0, out bool solS, out bool sagS, out double xSolG, out double xSagG);
            if (sola ? !solS : !sagS) return false;
            if (chain[j0]?.Beam == null) return false;
            hKomsu = chain[j0].Beam.HeightCm;
            xGonye = sola ? xSolG : xSagG;
            KirisAcilimKolonYuzu(chain, j0, j1, sup, out double n0, out double n1);
            KirisAcilimNumaraAraligi(chain, j0, j1, n0, n1, gorunus, out double d0, out double d1);
            double yuz = sola ? d1 : d0;
            if (!KirisAcilimIlaveSigar(chain, j0, j1, yShift, yuz, yBar)) return false;
            if (!KirisAcilimIlaveSigar(chain, j0, j1, yShift, xGonye - x0, yBar)) return false;
            return true;
        }

        /// <summary>Montaj veya alt düz ile o kirişin ilavelerinin alanı, büyük çapta yeniden adet.</summary>
        private static void KirisAcilimAsBirlestir(
            int nGovde, int cGovde, int nSol, int cSol, int nSag, int cSag,
            out int adet, out int cap)
        {
            long alan = 0;
            int buyuk = 0;
            void Ekle(int n, int c)
            {
                if (n <= 0 || c <= 0) return;
                alan += (long)n * c * c;
                if (c > buyuk) buyuk = c;
            }
            Ekle(nGovde, cGovde);
            Ekle(nSol, cSol);
            Ekle(nSag, cSag);
            adet = 0;
            cap = 0;
            if (buyuk <= 0 || alan <= 0) return;
            cap = buyuk;
            adet = (int)Math.Ceiling(alan / (double)(buyuk * buyuk));
        }

        /// <summary>
        /// BEA kod 3 sol (ST4 başlangıç) konsol, kod 4 sağ konsol. Çizim yönü uçları çevirebilir.
        /// Serbest uç gönyesi beton yüzünden paspayı kadar içeride.
        /// </summary>
        private static void KirisAcilimKonsolUclari(
            List<KirisAcilimSpan> chain, int i0, int i1,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi, List<KirisAcilimSaplama> gorunus, double x0,
            out bool solSerbest, out bool sagSerbest, out double xSolGonye, out double xSagGonye)
        {
            solSerbest = false;
            sagSerbest = false;
            xSolGonye = 0;
            xSagGonye = 0;
            if (chain == null || ucTipi == null) return;
            BeamInfo beam = chain[i0]?.Beam;
            if (beam == null || !ucTipi.TryGetValue(beam, out KirisAcilimUcBilgi uc)) return;
            bool solBaslangic = chain[i0].Cross0 == beam.StartAxisId;
            bool startSerbest = uc.Code == 3;
            bool endSerbest = uc.Code == 4;
            solSerbest = solBaslangic ? startSerbest : endSerbest;
            sagSerbest = solBaslangic ? endSerbest : startSerbest;
            KirisAcilimSerbestUcUzatma(chain, gorunus, out double extS, out double extE);
            double p = KirisAcilimMontajPaspayiCm;
            double tipSol = x0 + chain[i0].X0 - (i0 == 0 ? extS : 0);
            double tipSag = x0 + chain[i1].X1 + (i1 == chain.Count - 1 ? extE : 0);
            xSolGonye = tipSol + p;
            xSagGonye = tipSag - p;
            if (xSagGonye - xSolGonye < 10.0)
            {
                solSerbest = false;
                sagSerbest = false;
            }
        }

        /// <summary>Uçtaki kolon veya saplama kesitinin kiriş ekseni boyunca genişliği (cm).</summary>
        private static double KirisAcilimMesnetHk(
            List<KirisAcilimSpan> chain, int i0, int i1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus, bool sola)
        {
            double hk = 0;
            if (sola)
            {
                if (sup != null && i0 < sup.Length)
                {
                    double w = sup[i0].X1 - sup[i0].X0;
                    if (w > hk) hk = w;
                }
            }
            else if (sup != null && i1 + 1 < sup.Length)
            {
                double w = sup[i1 + 1].X1 - sup[i1 + 1].X0;
                if (w > hk) hk = w;
            }
            if (gorunus == null || chain == null) return hk;
            double station = sola ? chain[i0].X0 : chain[i1].X1;
            foreach (KirisAcilimSaplama spm in gorunus)
            {
                double w = spm.X1 - spm.X0;
                if (w < 2.0) continue;
                double mid = 0.5 * (spm.X0 + spm.X1);
                if (Math.Abs(mid - station) <= 0.5 * w + 40.0 && w > hk) hk = w;
            }
            return hk;
        }

        /// <summary>
        /// İlave çubuğun komşu açıklığa taşan ucu. Mesnet yüzünden sonra komşu net açıklığın 1/4'ü;
        /// hk + bu boy max(50φ, lb) altında kalıyorsa o tabana çekilir.
        /// Çubuk komşu gövdede paspayı kadar içeride kalamıyorsa uzatılmaz.
        /// </summary>
        private double KirisAcilimIlaveKomsuX(
            List<KirisAcilimSpan> chain, int i0, int i1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            double x0, double yShift, bool sola, double yBar, double xYuz, int capMm, bool ust)
        {
            if (!KirisAcilimKomsuAralik(chain, i0, i1, sola, out int j0, out int j1)) return xYuz;

            KirisAcilimKolonYuzu(chain, j0, j1, sup, out double n0, out double n1);
            KirisAcilimNumaraAraligi(chain, j0, j1, n0, n1, gorunus, out double d0, out double d1);
            double ln = d1 - d0;
            if (ln < 1.0) return xYuz;
            double hk = KirisAcilimMesnetHk(chain, j0, j1, sup, gorunus, !sola);
            double ext = KirisAcilimIlaveYuzdenCm(ln, hk, capMm, ust);
            if (ext < 1.0) return xYuz;

            double yakin = sola ? d1 : d0;
            double uzak = sola ? d0 : d1;
            double hedef = sola ? yakin - ext : yakin + ext;
            if (sola)
            {
                if (hedef < uzak) hedef = uzak;
            }
            else if (hedef > uzak) hedef = uzak;

            if (!KirisAcilimIlaveSigar(chain, j0, j1, yShift, yakin, yBar)) return xYuz;
            if (!KirisAcilimIlaveSigar(chain, j0, j1, yShift, hedef, yBar)) return xYuz;

            double xHedef = x0 + hedef;
            if (sola) return xHedef < xYuz ? xHedef : xYuz;
            return xHedef > xYuz ? xHedef : xYuz;
        }

        /// <summary>Çubuk kotu komşu kirişin içinde, her iki yüzden paspayı kadar içeride mi.</summary>
        private static bool KirisAcilimIlaveSigar(
            List<KirisAcilimSpan> chain, int j0, int j1, double yShift, double xChain, double yBar)
        {
            if (chain[j0]?.Beam == null) return false;
            double top = yShift + KirisAcilimKotAt(chain, j0, j1, xChain);
            double h = chain[j0].Beam.HeightCm;
            if (h < 1.0) return false;
            double bot = top - h;
            double p = KirisAcilimMontajPaspayiCm;
            return yBar <= top - p + 0.5 && yBar >= bot + p - 0.5;
        }

        /// <summary>Komşu açıklık aynı zincirde ve aradaki boşluk 200 cm'den kısaysa o run'ın aralığı.</summary>
        private static bool KirisAcilimKomsuAralik(
            List<KirisAcilimSpan> chain, int i0, int i1, bool sola, out int j0, out int j1)
        {
            j0 = -1;
            j1 = -1;
            if (chain == null) return false;
            if (sola)
            {
                if (i0 <= 0 || chain[i0 - 1]?.Beam == null) return false;
                j1 = i0 - 1;
                j0 = j1;
                while (j0 > 0 && chain[j0 - 1]?.Beam != null && chain[j0].Beam != null
                    && chain[j0 - 1].Beam.BeamId == chain[j0].Beam.BeamId)
                    j0--;
                return chain[i0].X0 - chain[j1].X1 <= 200.0;
            }
            if (i1 + 1 >= chain.Count || chain[i1 + 1]?.Beam == null) return false;
            j0 = i1 + 1;
            j1 = j0;
            while (j1 + 1 < chain.Count && chain[j1 + 1]?.Beam != null && chain[j0].Beam != null
                && chain[j1 + 1].Beam.BeamId == chain[j0].Beam.BeamId)
                j1++;
            return chain[j0].X0 - chain[i1].X1 <= 200.0;
        }

        /// <summary>
        /// Çubuk komşu gövdeye paspayı içinde girebiliyorsa komşunun bu mesnete bakan yüzü (zincir X).
        /// </summary>
        private static bool KirisAcilimDevamYuzu(
            List<KirisAcilimSpan> chain, int i0, int i1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            double yShift, bool sola, double yBar, out double yuzChain)
        {
            yuzChain = 0;
            if (!KirisAcilimKomsuAralik(chain, i0, i1, sola, out int j0, out int j1)) return false;
            KirisAcilimKolonYuzu(chain, j0, j1, sup, out double n0, out double n1);
            KirisAcilimNumaraAraligi(chain, j0, j1, n0, n1, gorunus, out double d0, out double d1);
            yuzChain = sola ? d1 : d0;
            return KirisAcilimIlaveSigar(chain, j0, j1, yShift, yuzChain, yBar);
        }

        /// <summary>
        /// Gönye düşey boşluğu. Yan kirişin kotu veya yüksekliği farklıysa iki kesitin
        /// birlikte kapladığı yükseklik kullanılır. İlave 3 cm'si hesaba katılmaz.
        /// </summary>
        private static double KirisAcilimGonyeRoom(
            List<KirisAcilimSpan> chain, int i0, int i1, double yShift,
            bool sola, bool ust, bool ilave, double yBar, double ownH, out double birlesikH)
        {
            double p = KirisAcilimMontajPaspayiCm;
            double kay = ilave ? KirisAcilimIlaveAyirCm : 0;
            double yHesap = ust ? yBar + kay : yBar - kay;
            double ownTop = ust ? yHesap + p : yHesap + ownH - p;
            double top = ownTop;
            double bot = ownTop - ownH;
            if (KirisAcilimKomsuAralik(chain, i0, i1, sola, out int j0, out int j1) && chain[j0]?.Beam != null)
            {
                double xN = sola ? chain[j1].X1 : chain[j0].X0;
                double nTop = yShift + KirisAcilimKotAt(chain, j0, j1, xN);
                double nBot = nTop - chain[j0].Beam.HeightCm;
                if (nTop > top) top = nTop;
                if (nBot < bot) bot = nBot;
            }
            birlesikH = top - bot;
            if (birlesikH < ownH) birlesikH = ownH;
            double room = ust ? yHesap - (bot + p) : (top - p) - yHesap;
            return room > 1.0 ? room : 1.0;
        }

        /// <summary>
        /// Kenar gönyesi. a mesnet dış yüzünden paspayı kadar içeride biter.
        /// Birinci gönye (b) en az 20 cm ve birleşik yüksekliğin yarısıdır; 12φ ve lb de alt sınır.
        /// İkinci gönye yalnız a+b, lb'yi tutmuyorsa eklenir; 50φ ikinci gönyeyi uzatmaz.
        /// </summary>
        private void KirisAcilimGonyeHesap(
            double phiCm, bool ust, double hkCm, double roomCm, double yukseklikCm, bool serbest, bool duzGiris,
            out double a, out double b, out double c)
        {
            a = 0;
            b = 0;
            c = 0;
            if (phiCm <= 0 && !serbest) return;
            if (roomCm < 1.0) roomCm = 1.0;
            if (!serbest && duzGiris)
            {
                double lbDuz = (ust ? KirisAcilimLbPhiKonumI : KirisAcilimLbPhiKonumII) * phiCm;
                double ihtiyac = KirisAcilimUstIlavePhi * phiCm;
                if (lbDuz > ihtiyac) ihtiyac = lbDuz;
                double sigar = hkCm - KirisAcilimMontajPaspayiCm;
                if (sigar + 0.5 >= ihtiyac && ihtiyac > 1.0)
                {
                    a = ihtiyac;
                    double aR = KirisAcilimYuvarlaYukari(a);
                    if (aR <= sigar + 0.05) a = aR;
                    return;
                }
            }
            if (serbest)
            {
                double bSerbest = KirisAcilimKonsolGonyeMinCm;
                double hYariSerbest = yukseklikCm * 0.5;
                if (hYariSerbest > bSerbest) bSerbest = hYariSerbest;
                b = bSerbest < roomCm ? bSerbest : roomCm;
                double bSerbestR = KirisAcilimYuvarlaYukari(b);
                if (bSerbestR <= roomCm + 0.05) b = bSerbestR;
                return;
            }
            double lb = (ust ? KirisAcilimLbPhiKonumI : KirisAcilimLbPhiKonumII) * phiCm;
            double phiMin = KirisAcilimKenarDuseyPhi * phiCm;
            double bMin = phiMin;
            if (KirisAcilimGonyeMinCm > bMin) bMin = KirisAcilimGonyeMinCm;
            double hYari = yukseklikCm * 0.5;
            if (hYari > bMin) bMin = hYari;
            double aMax = hkCm - KirisAcilimMontajPaspayiCm;
            if (aMax < 0) aMax = 0;
            a = aMax;
            if (roomCm < 1.0) roomCm = 1.0;
            double bWant = bMin > lb - a ? bMin : lb - a;
            b = bWant < roomCm ? bWant : roomCm;
            if (b < 1.0) b = roomCm < bMin ? roomCm : bMin;
            double bR = KirisAcilimYuvarlaYukari(b);
            if (bR <= roomCm + 0.05) b = bR;
            if (a + b + 0.5 < lb)
            {
                c = lb - a - b;
                if (c < phiMin) c = phiMin;
                c = KirisAcilimYuvarlaYukari(c);
            }
        }

        private static double KirisAcilimYuvarlaYukari(double x)
        {
            if (x <= 0) return 0;
            return Math.Ceiling(x / KirisAcilimYuvarlamaCm - 1e-6) * KirisAcilimYuvarlamaCm;
        }

        /// <summary>
        /// Gönye noktalarını çubuk polyline'ına ekler. sola ise yüzden önce (uca doğru), değilse yüze sonra.
        /// İkinci gönye kiriş içine döner ve xSinir'i geçmez.
        /// </summary>
        private double KirisAcilimGonyeEkle(
            List<Point2d> pts, bool sola, double xYuz, double y, bool ust,
            double hk, double h, int capMm, bool ilave, double xSinir, double roomCm, double yukseklikCm, bool serbest, bool duzGiris, out double eksik)
        {
            eksik = 0;
            if (pts == null || capMm <= 0) return 0;
            double oda = roomCm > 1.0 ? roomCm : h - KirisAcilimMontajPaspayiCm - KirisAcilimDuzPaspayiCm;
            double yuk = yukseklikCm > 1.0 ? yukseklikCm : h;
            if (serbest) hk = 0;
            KirisAcilimGonyeHesap(capMm / 10.0, ust, hk, oda, yuk, serbest, duzGiris, out double a, out double b, out double c);
            if (a < 1.0 && b < 1.0) return 0;
            // İlave yalnız çizimde 3 cm içeride. a ve b kısalır; açılım boyuna geri yazılır.
            double kay = ilave ? KirisAcilimIlaveAyirCm : 0.0;
            double aCiz = a - kay;
            if (aCiz < 0) aCiz = 0;
            double bCiz = b - 2.0 * kay;
            if (bCiz < 1.0) bCiz = b > 1.0 ? 1.0 : 0;
            double xBend = sola ? xYuz - aCiz : xYuz + aCiz;
            double yHook = ust ? y - bCiz : y + bCiz;
            double xTip = xBend;
            if (c >= 1.0)
            {
                xTip = sola ? xBend + c : xBend - c;
                if (sola && xTip > xSinir - 2.0) xTip = xSinir - 2.0;
                if (!sola && xTip < xSinir + 2.0) xTip = xSinir + 2.0;
                c = Math.Abs(xTip - xBend);
            }
            var hook = new List<Point2d>();
            if (sola)
            {
                if (c >= 1.0) hook.Add(new Point2d(xTip, yHook));
                if (bCiz >= 1.0) hook.Add(new Point2d(xBend, yHook));
                if (a >= 1.0 || bCiz >= 1.0) hook.Add(new Point2d(xBend, y));
                pts.InsertRange(0, hook);
            }
            else
            {
                if (a >= 1.0 || bCiz >= 1.0) pts.Add(new Point2d(xBend, y));
                if (bCiz >= 1.0) pts.Add(new Point2d(xBend, yHook));
                if (c >= 1.0) pts.Add(new Point2d(xTip, yHook));
            }
            eksik = (a - aCiz) + (b - bCiz);
            if (eksik < 0) eksik = 0;
            return a + b + c;
        }

        /// <summary>Montaj ve alt düz ucu: komşuya geçebiliyorsa aynı polyline'da düz devam, değilse gönye.</summary>
        private void KirisAcilimUcNokta(
            List<Point2d> pts,
            List<KirisAcilimSpan> chain, int i0, int i1,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            double x0, double yShift, bool sola, double xYuz, double y, bool ust,
            double hk, double h, int capMm, bool ilave, double xSinir, bool serbest)
        {
            if (pts == null || capMm <= 0) return;
            // Ara mesnette devam eden kiriş: montaj ve alt düz kendi yüzünde biter. Komşu açıklığa taşmaz, gönye olmaz.
            if (!serbest && KirisAcilimKomsuAralik(chain, i0, i1, sola, out _, out _))
                return;
            double oda = KirisAcilimGonyeRoom(chain, i0, i1, yShift, sola, ust, ilave, y, h, out double birlesikH);
            if (serbest) hk = 0;
            KirisAcilimGonyeEkle(pts, sola, xYuz, y, ust, hk, h, capMm, ilave, xSinir, oda, serbest ? h : birlesikH, serbest, !serbest, out double _);
        }

        /// <summary>
        /// İlave çubuk tek polyline: komşuya uzayan uç düz, uzayamayan uç gönyeli.
        /// xDis komşu uzamasının ucu, xYuz bu kirişin mesnet yüzü, xIc açıklık içindeki kesim.
        /// </summary>
        private void KirisAcilimIlaveCubuk(
            Transaction tr, BlockTableRecord ms, Database db,
            double xDis, double xYuz, double xIc, Func<double, double> yAt,
            bool sola, bool ust, double hk, double h, int adet, int cap, short renk, double roomCm, double yukseklikCm,
            bool serbestYuz, bool serbestIc, double odaSerbest, double hSerbest, bool serbestDis, double hDis)
        {
            if (adet <= 0 || cap <= 0 || yAt == null) return;
            if (Math.Abs(xIc - xYuz) < 1.0 && Math.Abs(xDis - xYuz) < 1.0 && !serbestDis) return;
            var pts = new List<Point2d>();
            bool uzadi = serbestDis || (!serbestYuz && (sola ? xDis < xYuz - 1.0 : xDis > xYuz + 1.0));
            double eksik = 0;
            double eksikSerbest = 0;
            double eksikDis = 0;
            double odaDis = hDis - KirisAcilimMontajPaspayiCm - KirisAcilimDuzPaspayiCm;
            if (odaDis < 1.0) odaDis = hDis > 1.0 ? hDis : 1.0;
            if (sola)
            {
                if (uzadi && serbestDis)
                    KirisAcilimGonyeEkle(pts, true, xDis, yAt(xDis), ust, 0, hDis > 1.0 ? hDis : h, cap, true, xYuz, odaDis, hDis, true, false, out eksikDis);
                if (uzadi) pts.Add(new Point2d(xDis, yAt(xDis)));
                else KirisAcilimGonyeEkle(pts, true, xYuz, yAt(xYuz), ust, hk, h, cap, true, xIc, roomCm, yukseklikCm, serbestYuz, !serbestYuz, out eksik);
                pts.Add(new Point2d(xYuz, yAt(xYuz)));
                pts.Add(new Point2d(xIc, yAt(xIc)));
                if (serbestIc)
                    KirisAcilimGonyeEkle(pts, false, xIc, yAt(xIc), ust, 0, h, cap, true, xYuz, odaSerbest, hSerbest, true, false, out eksikSerbest);
            }
            else
            {
                if (serbestIc)
                    KirisAcilimGonyeEkle(pts, true, xIc, yAt(xIc), ust, 0, h, cap, true, xYuz, odaSerbest, hSerbest, true, false, out eksikSerbest);
                pts.Add(new Point2d(xIc, yAt(xIc)));
                pts.Add(new Point2d(xYuz, yAt(xYuz)));
                if (!uzadi)
                    KirisAcilimGonyeEkle(pts, false, xYuz, yAt(xYuz), ust, hk, h, cap, true, xIc, roomCm, yukseklikCm, serbestYuz, !serbestYuz, out eksik);
                else
                {
                    pts.Add(new Point2d(xDis, yAt(xDis)));
                    if (serbestDis)
                        KirisAcilimGonyeEkle(pts, false, xDis, yAt(xDis), ust, 0, hDis > 1.0 ? hDis : h, cap, true, xYuz, odaDis, hDis, true, false, out eksikDis);
                }
            }
            eksik += eksikSerbest + eksikDis;
            double uzunluk = KirisAcilimDonatiPl(tr, ms, renk, pts) + eksik;
            if (uzunluk < 1.0) return;
            KirisAcilimAcilimCiz(tr, ms, db, pts, renk, adet, cap, uzunluk, KirisAcilimIlaveAcilimCm, ref _kirisAcilimIlaveSira);
            string lbl = adet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + cap.ToString(CultureInfo.InvariantCulture);
            double yA = yAt(xYuz);
            double yB = yAt(xIc);
            double xm = 0.5 * (xYuz + xIc);
            double midY = 0.5 * (yA + yB);
            DrawBeamLabel(tr, ms, db, new Point3d(xm, midY, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            if (tr == null || _lastBeamLabelId.IsNull) return;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return;
            Extents3d ext;
            try { ext = txt.GeometricExtents; }
            catch { return; }
            double hedef = ust ? midY - 4.0 : midY + 4.0;
            double yRef = ust ? ext.MaxPoint.Y : ext.MinPoint.Y;
            double dy = hedef - yRef;
            if (Math.Abs(dy) < 0.05) return;
            txt.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
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

        /// <summary>Basit dikdörtgen çubuk: 5,5 cm paspayı içeride, kendi renginde.</summary>
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
        private struct KirisAcilimMontajRun
        {
            public int I0;
            public int I1;
            public double XL;
            public double XR;
            public double YL;
            public double YR;
            public double ZoneL;
            public double ZoneR;
            public int Adet;
            public int Cap;
            public double H;
            public double HkSol;
            public double HkSag;
            public bool SolSerbest;
            public bool SagSerbest;
        }

        /// <summary>
        /// Aynı hizadaki montajlar tek çubuk dizisi. Boy, gönye dahil 1200 cm'yi geçmez.
        /// Bindirme lb, 5 cm katı, kiriş ortasındaki Ln/2 şeridinin içinde.
        /// Ek yeri, 12 m çubuktan en az fire verecek boya konur.
        /// As büyük olan, küçük As'li kirişin Ln/2 bölgesine uzar; lb'den sonra küçük As devam eder.
        /// </summary>
        private void KirisAcilimMontajBirlesik(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, double x0, double yShift,
            KirisAcilimSupport[] sup, Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            List<KirisAcilimSaplama> gorunus, Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            if (chain == null || chain.Count == 0 || montaj == null) return;
            var runs = new List<KirisAcilimMontajRun>();
            int i = 0;
            while (i < chain.Count)
            {
                int j = i;
                while (j + 1 < chain.Count && chain[j + 1].Beam != null && chain[i].Beam != null
                    && chain[j + 1].Beam.BeamId == chain[i].Beam.BeamId)
                    j++;
                if (KirisAcilimMontajRunOku(chain, i, j, x0, yShift, sup, montaj, gorunus, ucTipi, out KirisAcilimMontajRun run))
                    runs.Add(run);
                i = j + 1;
            }
            if (runs.Count == 0) return;

            int g0 = 0;
            while (g0 < runs.Count)
            {
                int g1 = g0;
                while (g1 + 1 < runs.Count && KirisAcilimMontajAyniHiza(runs[g1], runs[g1 + 1]))
                    g1++;
                KirisAcilimMontajGrupCiz(tr, ms, db, chain, x0, yShift, sup, gorunus, runs, g0, g1);
                for (int r = g0; r <= g1; r++)
                    KirisAcilimMontajEtiket(tr, ms, db, runs[r]);
                g0 = g1 + 1;
            }
        }

        private bool KirisAcilimMontajRunOku(
            List<KirisAcilimSpan> chain, int i0, int i1, double x0, double yShift,
            KirisAcilimSupport[] sup, Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            List<KirisAcilimSaplama> gorunus, Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi,
            out KirisAcilimMontajRun run)
        {
            run = default(KirisAcilimMontajRun);
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return false;
            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out double xSolGonye, out double xSagGonye);
            if (KirisAcilimKonsolTekBasina(chain, i0, i1, solSerbest, sagSerbest))
            {
                KirisAcilimMontajBilgi mSag = m;
                if (chain[i1].Beam != null && montaj.TryGetValue(chain[i1].Beam, out KirisAcilimMontajBilgi mSon)) mSag = mSon;
                bool solBas = chain[i0].Cross0 == beam.StartAxisId;
                bool sagBas = chain[i1].Beam != null && chain[i1].Cross1 == chain[i1].Beam.StartAxisId;
                int nSol = solBas ? m.SolUstAdet : m.SagUstAdet;
                int cSol = solBas ? m.SolUstCap : m.SagUstCap;
                int nSag = sagBas ? mSag.SolUstAdet : mSag.SagUstAdet;
                int cSag = sagBas ? mSag.SolUstCap : mSag.SagUstCap;
                KirisAcilimAsBirlestir(m.Adet, m.Cap, nSol, cSol, nSag, cSag, out int adetB, out int capB);
                if (adetB > 0 && capB > 0) { m.Adet = adetB; m.Cap = capB; }
            }
            if (m.Adet <= 0 || m.Cap <= 0) return false;

            KirisAcilimKolonYuzu(chain, i0, i1, sup, out double net0, out double net1);
            KirisAcilimNumaraAraligi(chain, i0, i1, net0, net1, gorunus, out double don0, out double don1);
            double xFaceL = x0 + don0;
            double xFaceR = x0 + don1;
            if (xFaceR - xFaceL < 1.0) return false;
            double yL = yShift + KirisAcilimKotAt(chain, i0, i1, don0) - KirisAcilimMontajPaspayiCm;
            double yR = yShift + KirisAcilimKotAt(chain, i0, i1, don1) - KirisAcilimMontajPaspayiCm;
            double ln = xFaceR - xFaceL;
            run = new KirisAcilimMontajRun
            {
                I0 = i0,
                I1 = i1,
                XL = solSerbest ? xSolGonye : xFaceL,
                XR = sagSerbest ? xSagGonye : xFaceR,
                YL = yL,
                YR = yR,
                ZoneL = xFaceL + 0.25 * ln,
                ZoneR = xFaceR - 0.25 * ln,
                Adet = m.Adet,
                Cap = m.Cap,
                H = beam.HeightCm,
                HkSol = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, true),
                HkSag = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, false),
                SolSerbest = solSerbest,
                SagSerbest = sagSerbest
            };
            KirisAcilimMontajSiganaUzat(chain, sup, gorunus, x0, yShift, ref run, true);
            KirisAcilimMontajSiganaUzat(chain, sup, gorunus, x0, yShift, ref run, false);
            return true;
        }

        /// <summary>
        /// Kot veya kesit farklı olsa da montaj komşu kirişin paspayı içine sığıyorsa gönyelenmez;
        /// kendi yüzünden max(50φ, lb) kadar komşuya girer.
        /// </summary>
        private void KirisAcilimMontajSiganaUzat(
            List<KirisAcilimSpan> chain, KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            double x0, double yShift, ref KirisAcilimMontajRun run, bool sola)
        {
            if (sola ? run.SolSerbest : run.SagSerbest) return;
            if (!KirisAcilimKomsuAralik(chain, run.I0, run.I1, sola, out int j0, out int j1)) return;
            if (chain[j0]?.Beam == null) return;
            double p = KirisAcilimMontajPaspayiCm;
            double xN = sola ? chain[j1].X1 : chain[j0].X0;
            double nTop = yShift + KirisAcilimKotAt(chain, j0, j1, xN);
            double nBot = nTop - chain[j0].Beam.HeightCm;
            double yBar = sola ? run.YL : run.YR;
            // Aynı hiza grupta zaten tek çubuk olarak devam eder. Uzatma yalnız kesiti farklı komşuya sığınca yapılır;
            // aksi halde uçlar üst üste biner, çizgi geri döner ve boy 1200'ü aşar.
            double yKarsi = nTop - p;
            if (Math.Abs(yBar - yKarsi) <= 2.0 && Math.Abs(run.H - chain[j0].Beam.HeightCm) <= 2.0) return;
            if (yBar > nTop - p || yBar < nBot + p) return;

            double phi = run.Cap / 10.0;
            double boy = Math.Max(KirisAcilimUstIlavePhi * phi, KirisAcilimLbPhiKonumI * phi);
            KirisAcilimKolonYuzu(chain, j0, j1, sup, out double n0, out double n1);
            KirisAcilimNumaraAraligi(chain, j0, j1, n0, n1, gorunus, out double d0, out double d1);
            double ucKarsi = x0 + (sola ? d0 : d1);
            if (sola)
            {
                double xHedef = run.XL - boy;
                if (xHedef < ucKarsi + p) xHedef = ucKarsi + p;
                if (xHedef < run.XL) run.XL = xHedef;
            }
            else
            {
                double xHedef = run.XR + boy;
                if (xHedef > ucKarsi - p) xHedef = ucKarsi - p;
                if (xHedef > run.XR) run.XR = xHedef;
            }
        }

        private static bool KirisAcilimMontajAyniHiza(KirisAcilimMontajRun a, KirisAcilimMontajRun b)
        {
            if (b.XL - a.XR > 200.0) return false;
            if (Math.Abs(a.H - b.H) > 2.0) return false;
            return Math.Abs(a.YR - b.YL) <= 2.0;
        }

        /// <summary>Montaj bindirmesi: konum I lb, yukarı 5 cm katı.</summary>
        private static double KirisAcilimMontajBindirme(int capMm)
        {
            if (capMm <= 0) return KirisAcilimYuvarlamaCm;
            double lb = KirisAcilimLbPhiKonumI * (capMm / 10.0);
            double r = KirisAcilimYuvarlaYukari(lb);
            return r >= KirisAcilimYuvarlamaCm ? r : KirisAcilimYuvarlamaCm;
        }

        /// <summary>Bindirme bütünüyle Ln/2 içindedir. fromLeft: büyük çubuk soldan girer, bindirme şeridin solundan başlar.</summary>
        private static void KirisAcilimMontajBindirmeYer(
            double zoneL, double zoneR, bool fromLeft, double lb, out double lap0, out double lap1)
        {
            double gen = zoneR - zoneL;
            if (gen < lb + 0.05)
            {
                lap0 = zoneL;
                lap1 = zoneR;
                return;
            }
            if (fromLeft)
            {
                lap0 = zoneL;
                lap1 = zoneL + lb;
            }
            else
            {
                lap1 = zoneR;
                lap0 = zoneR - lb;
            }
        }

        /// <summary>Çap değişiminde ek: büyük As, küçük kirişin Ln/2 kenarına lb girer. Kiriş doluysa diğerine kayar.</summary>
        private static void KirisAcilimMontajAsEk(
            List<KirisAcilimMontajRun> runs, bool[] birlesen, int i,
            out int hedef, out double lb, out double lap0, out double lap1)
        {
            KirisAcilimMontajRun a = runs[i];
            KirisAcilimMontajRun b = runs[i + 1];
            double asA = (double)a.Adet * a.Cap * a.Cap;
            double asB = (double)b.Adet * b.Cap * b.Cap;
            bool aBuyuk = asA > asB + 0.5 || (Math.Abs(asA - asB) <= 0.5 && a.Cap >= b.Cap);
            lb = KirisAcilimMontajBindirme(a.Cap > b.Cap ? a.Cap : b.Cap);
            hedef = aBuyuk ? i + 1 : i;
            int diger = hedef == i ? i + 1 : i;
            if (birlesen[hedef] && !birlesen[diger]) hedef = diger;
            bool hedefSigar = runs[hedef].ZoneR - runs[hedef].ZoneL >= lb - 0.2;
            bool digerSigar = runs[diger].ZoneR - runs[diger].ZoneL >= lb - 0.2;
            if (!hedefSigar && digerSigar && !birlesen[diger]) hedef = diger;
            KirisAcilimMontajBindirmeYer(runs[hedef].ZoneL, runs[hedef].ZoneR, hedef == i + 1, lb, out lap0, out lap1);
        }

        private void KirisAcilimMontajGrupCiz(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, double x0, double yShift,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            List<KirisAcilimMontajRun> runs, int g0, int g1)
        {
            double segX = runs[g0].XL;
            int segAdet = runs[g0].Adet;
            int segCap = runs[g0].Cap;
            bool hookL = true;
            int sira = 0;
            var birlesen = new bool[runs.Count];

            double Gonye(bool sola, int cap, KirisAcilimMontajRun run, double xUc)
            {
                if (cap <= 0 || run.H < 1.0) return 0;
                double y = KirisAcilimMontajY(runs, g0, g1, xUc);
                return KirisAcilimMontajGonyeBoy(chain, run, sup, gorunus, x0, yShift, sola, cap, xUc, y);
            }

            for (int i = g0; i < g1; i++)
            {
                KirisAcilimMontajRun a = runs[i];
                KirisAcilimMontajRun b = runs[i + 1];
                if (a.Adet == b.Adet && a.Cap == b.Cap) continue;
                KirisAcilimMontajAsEk(runs, birlesen, i, out int hedef, out double lb, out double lap0, out double lap1);

                int sagSon = i + 1;
                while (sagSon < g1 && runs[sagSon].Adet == b.Adet && runs[sagSon].Cap == b.Cap)
                    sagSon++;
                bool sagGrup = sagSon == g1;
                double parcaBit = runs[sagSon].XR;
                if (!sagGrup)
                {
                    KirisAcilimMontajAsEk(runs, birlesen, sagSon, out _, out _, out _, out double sonrakiLap1);
                    if (sonrakiLap1 > lap1 + 1.0) parcaBit = sonrakiLap1;
                }

                double gSol = hookL ? Gonye(true, segCap, runs[g0], segX) : 0;
                double gSag = sagGrup ? Gonye(false, b.Cap, runs[g1], runs[g1].XR) : 0;
                double boySol = KirisAcilimMontajBoy(runs, g0, g1, segX, lap1) + gSol;
                double boySag = KirisAcilimMontajBoy(runs, g0, g1, lap0, parcaBit) + gSag;
                double wSol = (double)segAdet * segCap * segCap;
                double wSag = (double)b.Adet * b.Cap * b.Cap;
                bool sagAgir = wSag > wSol + 0.5 || (Math.Abs(wSag - wSol) <= 0.5 && b.Cap >= segCap);
                int adetAgir = sagAgir ? b.Adet : segAdet;
                int capAgir = sagAgir ? b.Cap : segCap;
                double gSolAgir = hookL ? Gonye(true, capAgir, runs[g0], segX) : 0;
                double gSagAgir = sagGrup ? Gonye(false, capAgir, runs[g1], runs[g1].XR) : 0;
                double boyBir = KirisAcilimMontajBoy(runs, g0, g1, segX, parcaBit) + gSolAgir + gSagAgir;
                double wEk = boySol * wSol + boySag * wSag;
                double wTek = boyBir * (double)adetAgir * capAgir * capAgir;
                // lb iki çubukta da durur. Ağır çubuk hafif alanı da kaplayınca çelik azalıyor ve tek parça 1200'ü geçmiyorsa ek yok.
                if (wTek < wEk - 0.5 && boyBir <= KirisAcilimMontajMaxBoyCm + 0.49 && boyBir > 1.0)
                {
                    segAdet = adetAgir;
                    segCap = capAgir;
                    continue;
                }

                KirisAcilimMontajLapYuvarla(runs, g0, g1, segX, parcaBit, gSol, gSag, lb, boySol, boySag, ref lap0, ref lap1);
                int ekIx = KirisAcilimMontajEkKiris(runs, g0, g1, lap0, lap1);
                birlesen[ekIx >= 0 ? g0 + ekIx : hedef] = true;
                KirisAcilimMontajParcaCiz(tr, ms, db, chain, x0, yShift, sup, gorunus, runs, g0, g1, segX, lap1, segAdet, segCap, hookL, false, ref sira);
                segX = lap0;
                segAdet = b.Adet;
                segCap = b.Cap;
                hookL = false;
            }
            KirisAcilimMontajParcaCiz(tr, ms, db, chain, x0, yShift, sup, gorunus, runs, g0, g1, segX, runs[g1].XR, segAdet, segCap, hookL, true, ref sira);
        }

        /// <summary>
        /// 1200 cm'nin altındaki uzun parçayı 1200'e tamamlar. Kayma Ln/2 içinde kalır.
        /// Eksik 20 cm'yi geçmiyorsa bindirme Ln/2 dışına o kadar taşabilir. Karşı parça 240 cm'nin altına düşmez.
        /// </summary>
        private static void KirisAcilimMontajLapYuvarla(
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double segX, double parcaBit, double gSol, double gSag, double lb,
            double boySol, double boySag, ref double lap0, ref double lap1)
        {
            bool solAlt = boySol < KirisAcilimMontajMaxBoyCm - 0.49;
            bool sagAlt = boySag < KirisAcilimMontajMaxBoyCm - 0.49;
            if (!solAlt && !sagAlt) return;
            bool solaBuyut = solAlt && (!sagAlt || boySol >= boySag);
            double eksik = KirisAcilimMontajMaxBoyCm - (solaBuyut ? boySol : boySag);
            if (eksik <= 0.5) return;
            double digerOnce = solaBuyut ? boySag : boySol;
            if (digerOnce - eksik < 240.0 - 0.5 && digerOnce >= 240.0 - 0.5) return;

            double nLap0;
            double nLap1;
            if (solaBuyut)
            {
                nLap1 = KirisAcilimMontajIleri(runs, g0, g1, lap1, eksik);
                nLap0 = KirisAcilimMontajGeri(runs, g0, g1, nLap1, lb);
            }
            else
            {
                nLap0 = KirisAcilimMontajGeri(runs, g0, g1, lap0, eksik);
                nLap1 = KirisAcilimMontajIleri(runs, g0, g1, nLap0, lb);
            }
            if (nLap1 <= nLap0 + 1.0 || nLap0 < segX - 0.2 || nLap1 > parcaBit + 0.2) return;
            double tol = eksik <= KirisAcilimMontajLnToleransCm + 0.01 ? KirisAcilimMontajLnToleransCm : 0.2;
            if (!KirisAcilimMontajEkBolge(runs, g0, g1, nLap0, nLap1, tol)) return;
            double yeni = solaBuyut
                ? KirisAcilimMontajBoy(runs, g0, g1, segX, nLap1) + gSol
                : KirisAcilimMontajBoy(runs, g0, g1, nLap0, parcaBit) + gSag;
            if (Math.Abs(yeni - KirisAcilimMontajMaxBoyCm) > 1.5) return;
            lap0 = nLap0;
            lap1 = nLap1;
        }

        private struct KirisAcilimMontajKesim
        {
            public double Xa;
            public double Xb;
            public bool HookL;
            public bool HookR;
            public double Boy;
        }

        private sealed class KirisAcilimMontajCozum
        {
            public List<KirisAcilimMontajKesim> Parca;
        }

        /// <summary>Parça, gönye dahil 1200 cm'yi aşıyorsa Ln/2 içinde firesi en az olan bindirmeyi koyar.</summary>
        private void KirisAcilimMontajParcaCiz(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, double x0, double yShift,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double xa, double xb, int adet, int cap, bool hookL, bool hookR, ref int sira)
        {
            if (xb - xa < 1.0 || adet <= 0 || cap <= 0) return;
            KirisAcilimMontajCozum cozum = KirisAcilimMontajCoz(
                chain, x0, yShift, sup, gorunus, runs, g0, g1, xa, xb, cap, hookL, hookR);
            if (cozum == null || cozum.Parca == null || cozum.Parca.Count == 0)
            {
                KirisAcilimMontajCubuk(tr, ms, db, chain, x0, yShift, sup, gorunus, runs, g0, g1, xa, xb, adet, cap, hookL, hookR, sira);
                sira++;
                return;
            }
            for (int i = 0; i < cozum.Parca.Count; i++)
            {
                KirisAcilimMontajKesim p = cozum.Parca[i];
                KirisAcilimMontajCubuk(tr, ms, db, chain, x0, yShift, sup, gorunus, runs, g0, g1, p.Xa, p.Xb, adet, cap, p.HookL, p.HookR, sira);
                sira++;
            }
        }

        /// <summary>
        /// Gönye boyu (yaysız) dahil her çubuk 1200 cm'yi geçmez. Ek tam lb'dir.
        /// Parça boyu sırası: 1200, sonra 600/400/300/240, 240 altı olmasın, sonra 100'ün katı, en son küsürat.
        /// </summary>
        private KirisAcilimMontajCozum KirisAcilimMontajCoz(
            List<KirisAcilimSpan> chain, double x0, double yShift,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double xa, double xb, int cap, bool hookL, bool hookR)
        {
            double ySol = KirisAcilimMontajY(runs, g0, g1, xa);
            double ySag = KirisAcilimMontajY(runs, g0, g1, xb);
            double gSol = hookL
                ? KirisAcilimMontajGonyeBoy(chain, runs[g0], sup, gorunus, x0, yShift, true, cap, xa, ySol)
                : 0;
            double gSag = hookR
                ? KirisAcilimMontajGonyeBoy(chain, runs[g1], sup, gorunus, x0, yShift, false, cap, xb, ySag)
                : 0;
            double lb = KirisAcilimMontajBindirme(cap);
            List<KirisAcilimMontajKesim> plan = KirisAcilimMontajPlanla(
                runs, g0, g1, xa, xb, gSol, gSag, lb, hookL, hookR);
            if (plan != null && plan.Count > 0)
                return new KirisAcilimMontajCozum { Parca = plan };

            var parca = new List<KirisAcilimMontajKesim>();
            double x = xa;
            bool ilk = true;
            for (int guard = 0; guard < 80; guard++)
            {
                double hL = ilk ? gSol : 0;
                double tum = KirisAcilimMontajBoy(runs, g0, g1, x, xb) + hL + gSag;
                if (tum <= KirisAcilimMontajMaxBoyCm + 0.49)
                {
                    parca.Add(new KirisAcilimMontajKesim { Xa = x, Xb = xb, HookL = ilk && hookL, HookR = hookR, Boy = tum });
                    break;
                }
                if (!KirisAcilimMontajZorunluKes(runs, g0, g1, x, xb, hL, gSag, lb, out double lap0, out double lap1, out double solBoy))
                {
                    parca.Add(new KirisAcilimMontajKesim { Xa = x, Xb = xb, HookL = ilk && hookL, HookR = hookR, Boy = tum });
                    break;
                }
                parca.Add(new KirisAcilimMontajKesim { Xa = x, Xb = lap1, HookL = ilk && hookL, HookR = false, Boy = solBoy });
                double once = x;
                double xYeni = lap0 < once ? once : lap0;
                if (xYeni <= once + 0.5) xYeni = lap1;
                if (xYeni <= once + 0.5 || xYeni >= xb - 0.5)
                {
                    double kalanX = lap1 > once ? lap1 : once;
                    if (kalanX >= xb - 0.5) break;
                    double kalan = KirisAcilimMontajBoy(runs, g0, g1, kalanX, xb) + gSag;
                    if (kalan <= KirisAcilimMontajMaxBoyCm + 0.49)
                    {
                        parca.Add(new KirisAcilimMontajKesim { Xa = kalanX, Xb = xb, HookL = false, HookR = hookR, Boy = kalan });
                        break;
                    }
                    x = kalanX;
                }
                else
                    x = xYeni;
                ilk = false;
            }
            return new KirisAcilimMontajCozum { Parca = parca };
        }

        private struct KirisAcilimMontajSkor
        {
            public bool Dolu;
            public int Parca;
            public double Cm1200;
            public double CmBolen;
            public double CmKisa;
            public double CmYuz;
            public double CmSerbest;
            public double Ilk;

            public bool Iyi(KirisAcilimMontajSkor o)
            {
                if (!o.Dolu) return true;
                if (Parca != o.Parca) return Parca < o.Parca;
                if (Cm1200 != o.Cm1200) return Cm1200 > o.Cm1200;
                if (CmKisa != o.CmKisa) return CmKisa < o.CmKisa;
                if (CmBolen != o.CmBolen) return CmBolen > o.CmBolen;
                if (CmYuz != o.CmYuz) return CmYuz > o.CmYuz;
                if (CmSerbest != o.CmSerbest) return CmSerbest < o.CmSerbest;
                return Ilk > o.Ilk;
            }
        }

        /// <summary>Etikete yazılacak tam sayıya göre boy sınıfı. Az parça daha az çelik; sonra 1200, bölen, 240 altı, 100 katı.</summary>
        private static void KirisAcilimMontajBoySinif(double boy, ref KirisAcilimMontajSkor s)
        {
            s.Parca++;
            int g = (int)Math.Round(boy);
            if (g == 1200) { s.Cm1200 += boy; return; }
            if (g == 600 || g == 400 || g == 300 || g == 240) { s.CmBolen += boy; return; }
            if (g < 240) { s.CmKisa += boy; return; }
            if (g % 100 == 0) { s.CmYuz += boy; return; }
            s.CmSerbest += boy;
        }

        private static bool KirisAcilimMontajEkBolge(
            List<KirisAcilimMontajRun> runs, int g0, int g1, double lap0, double lap1, double tolerans = 0.2)
        {
            for (int i = g0; i <= g1; i++)
            {
                if (lap0 >= runs[i].ZoneL - tolerans && lap1 <= runs[i].ZoneR + tolerans)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tüm parçaları birlikte tarar. Bindirme yalnız kiriş ortasındaki Ln/2 içindedir.
        /// Bu koşulla önce 1200 cm, sonra bölen boylar. Aynı kirişe iki bindirme konmaz.
        /// 1200'ü aşmayan tek parça bölünmez.
        /// </summary>
        private static List<KirisAcilimMontajKesim> KirisAcilimMontajPlanla(
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double xa, double xb, double gSol, double gSag, double lb,
            bool hookL, bool hookR)
        {
            double duz = KirisAcilimMontajBoy(runs, g0, g1, xa, xb);
            if (duz < 1.0) return null;
            if (duz + gSol + gSag <= KirisAcilimMontajMaxBoyCm + 0.49)
            {
                return new List<KirisAcilimMontajKesim>
                {
                    new KirisAcilimMontajKesim { Xa = xa, Xb = xb, HookL = hookL, HookR = hookR, Boy = duz + gSol + gSag }
                };
            }
            int n = (int)Math.Ceiling(duz) + 3;
            if (n < 4) n = 4;
            if (n > 40000) return null;
            List<KirisAcilimMontajKesim> tek = KirisAcilimMontajPlanAra(
                runs, g0, g1, xa, xb, gSol, gSag, lb, hookL, hookR, duz, n, true);
            if (tek != null) return tek;
            return KirisAcilimMontajPlanAra(
                runs, g0, g1, xa, xb, gSol, gSag, lb, hookL, hookR, duz, n, false);
        }

        /// <summary>Bindirmenin orta noktası hangi kirişteyse o kiriş. Aralıkta ise en çok çakışan.</summary>
        private static int KirisAcilimMontajEkKiris(
            List<KirisAcilimMontajRun> runs, int g0, int g1, double lap0, double lap1)
        {
            double orta = 0.5 * (lap0 + lap1);
            int en = -1;
            double enPay = 0;
            for (int i = g0; i <= g1; i++)
            {
                if (orta >= runs[i].XL - 0.5 && orta <= runs[i].XR + 0.5)
                    return i - g0;
                double pay = Math.Min(lap1, runs[i].XR) - Math.Max(lap0, runs[i].XL);
                if (pay > enPay)
                {
                    enPay = pay;
                    en = i - g0;
                }
            }
            return enPay > 1.0 ? en : -1;
        }

        /// <summary>Parça ucu kirişin dış ucu değilse bu kirişte zaten bir birleşim vardır.</summary>
        private static int KirisAcilimMontajUcKiris(
            List<KirisAcilimMontajRun> runs, int g0, int g1, double x, bool sol)
        {
            for (int i = g0; i <= g1; i++)
            {
                if (x < runs[i].XL - 1.0 || x > runs[i].XR + 1.0) continue;
                bool dis = sol
                    ? i == g0 && x <= runs[i].XL + 2.0
                    : i == g1 && x >= runs[i].XR - 2.0;
                if (dis) return -1;
                return i - g0;
            }
            return -1;
        }

        private struct KirisAcilimMontajDurum
        {
            public double Arc;
            public long Mask;
            public KirisAcilimMontajSkor Skor;
            public int Prev;
            public double UsedL;
        }

        /// <summary>tekBirlesim: aynı kirişe ikinci bindirme konmaz. Sığmazsa çağıran serbest dener.</summary>
        private static List<KirisAcilimMontajKesim> KirisAcilimMontajPlanAra(
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double xa, double xb, double gSol, double gSag, double lb,
            bool hookL, bool hookR, double duz, int n, bool tekBirlesim)
        {
            long bas = 0;
            if (tekBirlesim)
            {
                int ka = KirisAcilimMontajUcKiris(runs, g0, g1, xa, true);
                int kb = KirisAcilimMontajUcKiris(runs, g0, g1, xb, false);
                if (ka >= 0 && ka < 62) bas |= 1L << ka;
                if (kb >= 0 && kb < 62) bas |= 1L << kb;
            }
            double[] hedefler = { 1200, 1100, 1000, 900, 800, 700, 600, 500, 400, 300, 240 };
            var tum = new List<KirisAcilimMontajDurum>();
            var kova = new List<KirisAcilimMontajDurum>[n];
            bool bul = false;
            KirisAcilimMontajSkor enIyi = default(KirisAcilimMontajSkor);
            int enIx = -1;
            double enKalan = 0;

            void Ekle(int j, KirisAcilimMontajDurum d)
            {
                if (j <= 0 || j >= n) return;
                if (kova[j] == null) kova[j] = new List<KirisAcilimMontajDurum>();
                kova[j].Add(d);
            }

            void Isle(KirisAcilimMontajDurum d, int self)
            {
                double arc = d.Arc;
                double hook = self == 0 ? gSol : 0;
                double kalan = duz - arc + hook + gSag;
                if (kalan > 0.5 && kalan <= KirisAcilimMontajMaxBoyCm + 0.49)
                {
                    KirisAcilimMontajSkor s = d.Skor;
                    KirisAcilimMontajBoySinif(kalan, ref s);
                    if (self == 0) s.Ilk = kalan;
                    if (!bul || s.Iyi(enIyi))
                    {
                        bul = true;
                        enIyi = s;
                        enIx = self;
                        enKalan = kalan;
                    }
                    return;
                }
                if (arc >= duz - 2.0) return;
                double xBas = arc <= 0.01 ? xa : KirisAcilimMontajIleri(runs, g0, g1, xa, arc);
                void Dene(double endArc, double nextArc, double L)
                {
                    if (endArc >= duz - 2.0 || nextArc <= arc + 1.0) return;
                    int j = (int)Math.Round(nextArc);
                    if (j <= 0 || j >= n) return;
                    double xLap1 = KirisAcilimMontajIleri(runs, g0, g1, xa, endArc);
                    double xLap0 = KirisAcilimMontajIleri(runs, g0, g1, xa, nextArc);
                    double tolEk = Math.Abs(L - KirisAcilimMontajMaxBoyCm) <= 0.49
                        ? KirisAcilimMontajLnToleransCm : 0.2;
                    if (!KirisAcilimMontajEkBolge(runs, g0, g1, xLap0, xLap1, tolEk)) return;
                    long mask = d.Mask;
                    if (tekBirlesim)
                    {
                        int k = KirisAcilimMontajEkKiris(runs, g0, g1, xLap0, xLap1);
                        if (k >= 0 && k < 62 && (mask & (1L << k)) != 0) return;
                        if (k >= 0 && k < 62) mask |= 1L << k;
                    }
                    KirisAcilimMontajSkor s = d.Skor;
                    KirisAcilimMontajBoySinif(L, ref s);
                    if (self == 0) s.Ilk = L;
                    Ekle(j, new KirisAcilimMontajDurum
                    {
                        Arc = nextArc,
                        Mask = mask,
                        Skor = s,
                        Prev = self,
                        UsedL = L
                    });
                }
                for (int h = 0; h < hedefler.Length; h++)
                {
                    double L = hedefler[h];
                    double straight = L - hook;
                    if (straight <= lb + 1.0) continue;
                    Dene(arc + straight, arc + straight - lb, L);
                }
                for (int r = g0; r <= g1; r++)
                {
                    double zL = runs[r].ZoneL;
                    double zR = runs[r].ZoneR;
                    if (zR - zL < lb - 0.2) continue;
                    if (tekBirlesim && r - g0 < 62 && (d.Mask & (1L << (r - g0))) != 0) continue;
                    double solU = KirisAcilimMontajIleri(runs, g0, g1, zL, lb);
                    if (solU > zR + 0.2) continue;
                    void DeneX(double x1)
                    {
                        if (x1 <= xBas + 1.0 || x1 > zR + 0.2) return;
                        double x0 = KirisAcilimMontajGeri(runs, g0, g1, x1, lb);
                        if (x0 < zL - 0.2) return;
                        double straightX = KirisAcilimMontajBoy(runs, g0, g1, xBas, x1);
                        double lx = straightX + hook;
                        if (lx > KirisAcilimMontajMaxBoyCm + 0.49 || straightX <= lb + 1.0) return;
                        Dene(arc + straightX, arc + straightX - lb, lx);
                    }
                    DeneX(solU);
                    DeneX(zR);
                    for (int h = 0; h < hedefler.Length; h++)
                    {
                        double straightH = hedefler[h] - hook;
                        if (straightH <= lb + 1.0) continue;
                        double ideal = KirisAcilimMontajIleri(runs, g0, g1, xa, arc + straightH);
                        if (ideal < solU) ideal = solU;
                        if (ideal > zR) ideal = zR;
                        DeneX(ideal);
                    }
                }
            }

            tum.Add(new KirisAcilimMontajDurum
            {
                Arc = 0,
                Mask = bas,
                Skor = new KirisAcilimMontajSkor { Dolu = true },
                Prev = -1
            });
            Isle(tum[0], 0);
            for (int cm = 1; cm < n; cm++)
            {
                if (kova[cm] == null) continue;
                var sec = new Dictionary<long, KirisAcilimMontajDurum>();
                for (int t = 0; t < kova[cm].Count; t++)
                {
                    KirisAcilimMontajDurum d = kova[cm][t];
                    if (!sec.TryGetValue(d.Mask, out KirisAcilimMontajDurum eski) || d.Skor.Iyi(eski.Skor))
                        sec[d.Mask] = d;
                }
                foreach (KirisAcilimMontajDurum d in sec.Values)
                {
                    int ix = tum.Count;
                    tum.Add(d);
                    Isle(tum[ix], ix);
                }
            }
            if (!bul || enIx < 0) return null;

            var boylar = new List<double>();
            int c = enIx;
            int guard = 0;
            while (c > 0 && guard++ < 80)
            {
                boylar.Add(tum[c].UsedL);
                c = tum[c].Prev;
            }
            boylar.Reverse();
            boylar.Add(enKalan);

            var parca = new List<KirisAcilimMontajKesim>();
            double yurur = 0;
            for (int k = 0; k < boylar.Count; k++)
            {
                bool ilkP = k == 0;
                bool sonP = k == boylar.Count - 1;
                double x0 = yurur <= 0.01 ? xa : KirisAcilimMontajIleri(runs, g0, g1, xa, yurur);
                double x1 = xb;
                if (!sonP)
                {
                    double bit = yurur + boylar[k] - (ilkP ? gSol : 0);
                    x1 = KirisAcilimMontajIleri(runs, g0, g1, xa, bit);
                    yurur = bit - lb;
                }
                if (x1 < x0 + 0.5) return null;
                parca.Add(new KirisAcilimMontajKesim
                {
                    Xa = x0,
                    Xb = x1,
                    HookL = ilkP && hookL,
                    HookR = sonP && hookR,
                    Boy = boylar[k]
                });
            }
            return parca;
        }

        /// <summary>1200 cm aşılıyorsa keser. Bindirme Ln/2 dışına çıkarsa kesim yok.</summary>
        private static bool KirisAcilimMontajZorunluKes(
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double x, double xb, double hL, double gSag, double lb,
            out double lap0, out double lap1, out double solBoy)
        {
            lap0 = lap1 = solBoy = 0;
            double duzTum = KirisAcilimMontajBoy(runs, g0, g1, x, xb);
            if (duzTum + hL + gSag <= KirisAcilimMontajMaxBoyCm + 0.5) return false;
            double duzHedef = KirisAcilimMontajMaxBoyCm - hL;
            if (duzHedef < 10.0) duzHedef = 10.0;
            double aday = KirisAcilimMontajIleri(runs, g0, g1, x, duzHedef);
            bool sona = aday >= xb - 2.0 || KirisAcilimMontajBoy(runs, g0, g1, x, aday) >= duzTum - 1.0;
            if (!sona)
            {
                lap1 = aday;
                lap0 = KirisAcilimMontajGeri(runs, g0, g1, lap1, lb);
            }
            else
            {
                double sagDuz = KirisAcilimMontajMaxBoyCm - gSag;
                if (sagDuz < 10.0) sagDuz = 10.0;
                if (sagDuz > duzTum - 10.0) sagDuz = Math.Max(10.0, duzTum * 0.5);
                lap0 = KirisAcilimMontajGeri(runs, g0, g1, xb, sagDuz);
                double bin = lb;
                if (bin > sagDuz - 5.0) bin = Math.Max(5.0, sagDuz * 0.5);
                lap1 = KirisAcilimMontajIleri(runs, g0, g1, lap0, bin);
            }
            if (lap0 < x) lap0 = x;
            if (lap1 <= x + 1.0) return false;
            if (lap1 >= xb - 0.5) lap1 = xb - Math.Max(5.0, Math.Min(lb, (xb - x) * 0.25));
            if (lap1 <= x + 1.0 || lap1 >= xb - 0.5) return false;
            if (lap0 > lap1) lap0 = x;
            solBoy = KirisAcilimMontajBoy(runs, g0, g1, x, lap1) + hL;
            if (solBoy > KirisAcilimMontajMaxBoyCm + 0.5)
            {
                lap1 = KirisAcilimMontajGeri(runs, g0, g1, lap1, solBoy - KirisAcilimMontajMaxBoyCm);
                if (lap1 <= x + 1.0 || lap1 >= xb - 0.5) return false;
                lap0 = KirisAcilimMontajGeri(runs, g0, g1, lap1, lb);
                if (lap0 < x) lap0 = x;
                if (lap0 > lap1) lap0 = x;
                solBoy = KirisAcilimMontajBoy(runs, g0, g1, x, lap1) + hL;
            }
            if (!(solBoy <= KirisAcilimMontajMaxBoyCm + 0.5 && lap1 > x + 1.0 && lap1 < xb - 0.5))
                return false;
            if (KirisAcilimMontajEkBolge(runs, g0, g1, lap0, lap1)) return true;
            return solBoy >= KirisAcilimMontajMaxBoyCm - KirisAcilimMontajLnToleransCm
                && KirisAcilimMontajEkBolge(runs, g0, g1, lap0, lap1, KirisAcilimMontajLnToleransCm);
        }

        /// <summary>Gönye boyu: a+b+c. Köşe yayı boydan düşülmez. Komşu kiriş varsa gönye yok, bindirme düzdür.</summary>
        private double KirisAcilimMontajGonyeBoy(
            List<KirisAcilimSpan> chain, KirisAcilimMontajRun run,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            double x0, double yShift, bool sola, int cap, double xYuz, double y)
        {
            if (cap <= 0 || run.H < 1.0) return 0;
            bool serbest = sola ? run.SolSerbest : run.SagSerbest;
            bool komsu = KirisAcilimKomsuAralik(chain, run.I0, run.I1, sola, out _, out _);
            bool kot = KirisAcilimMontajKotKesit(chain, run, yShift, sola);
            if (komsu && !kot) return 0;
            double hk = sola ? run.HkSol : run.HkSag;
            double oda = KirisAcilimGonyeRoom(chain, run.I0, run.I1, yShift, sola, true, false, y, run.H, out double birlesikH);
            if (serbest) hk = 0;
            bool duz = !serbest && !kot;
            KirisAcilimGonyeHesap(cap / 10.0, true, hk, oda, serbest ? run.H : birlesikH, serbest, duz, out double a, out double b, out double c);
            return a + b + c;
        }

        /// <summary>Montaj bu kotta komşu kesitin paspayı içine sığmıyorsa kenara uzayıp gönyelenir.</summary>
        private bool KirisAcilimMontajKotKesit(
            List<KirisAcilimSpan> chain, KirisAcilimMontajRun run, double yShift, bool sola)
        {
            if (!KirisAcilimKomsuAralik(chain, run.I0, run.I1, sola, out int j0, out int j1)) return false;
            if (chain[j0]?.Beam == null) return false;
            double p = KirisAcilimMontajPaspayiCm;
            double xN = sola ? chain[j1].X1 : chain[j0].X0;
            double nTop = yShift + KirisAcilimKotAt(chain, j0, j1, xN);
            double nBot = nTop - chain[j0].Beam.HeightCm;
            double yBar = sola ? run.YL : run.YR;
            return yBar > nTop - p || yBar < nBot + p;
        }

        private static double KirisAcilimMontajY(List<KirisAcilimMontajRun> runs, int g0, int g1, double x)
        {
            if (x <= runs[g0].XL) return runs[g0].YL;
            for (int i = g0; i <= g1; i++)
            {
                KirisAcilimMontajRun r = runs[i];
                if (x <= r.XR + 0.05)
                {
                    double den = r.XR - r.XL;
                    double u = den < 1.0 ? 0.0 : (x - r.XL) / den;
                    if (u < 0) u = 0;
                    if (u > 1) u = 1;
                    return r.YL + (r.YR - r.YL) * u;
                }
                if (i < g1 && x < runs[i + 1].XL)
                {
                    double den = runs[i + 1].XL - r.XR;
                    double u = den < 1.0 ? 0.0 : (x - r.XR) / den;
                    if (u < 0) u = 0;
                    if (u > 1) u = 1;
                    return r.YR + (runs[i + 1].YL - r.YR) * u;
                }
            }
            return runs[g1].YR;
        }

        private static void KirisAcilimMontajKnot(
            List<KirisAcilimMontajRun> runs, int g0, int g1, double xa, double xb, List<double> xs)
        {
            xs.Clear();
            if (xb < xa) { double t = xa; xa = xb; xb = t; }
            xs.Add(xa);
            xs.Add(xb);
            for (int i = g0; i <= g1; i++)
            {
                if (runs[i].XL > xa + 0.01 && runs[i].XL < xb - 0.01) xs.Add(runs[i].XL);
                if (runs[i].XR > xa + 0.01 && runs[i].XR < xb - 0.01) xs.Add(runs[i].XR);
            }
            xs.Sort();
            int w = 1;
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] - xs[w - 1] > 0.01) xs[w++] = xs[i];
            }
            if (w < xs.Count) xs.RemoveRange(w, xs.Count - w);
        }

        private static double KirisAcilimMontajSeg(List<KirisAcilimMontajRun> runs, int g0, int g1, double x0, double x1)
        {
            double dx = x1 - x0;
            double dy = KirisAcilimMontajY(runs, g0, g1, x1) - KirisAcilimMontajY(runs, g0, g1, x0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double KirisAcilimMontajBoy(List<KirisAcilimMontajRun> runs, int g0, int g1, double xa, double xb)
        {
            var xs = new List<double>();
            KirisAcilimMontajKnot(runs, g0, g1, xa, xb, xs);
            double boy = 0;
            for (int i = 1; i < xs.Count; i++)
                boy += KirisAcilimMontajSeg(runs, g0, g1, xs[i - 1], xs[i]);
            return boy;
        }

        private static double KirisAcilimMontajIleri(List<KirisAcilimMontajRun> runs, int g0, int g1, double xFrom, double dist)
        {
            if (dist <= 0.01) return xFrom;
            var xs = new List<double>();
            KirisAcilimMontajKnot(runs, g0, g1, xFrom, runs[g1].XR, xs);
            double kalan = dist;
            for (int i = 0; i < xs.Count - 1; i++)
            {
                double seg = KirisAcilimMontajSeg(runs, g0, g1, xs[i], xs[i + 1]);
                if (seg >= kalan - 1e-6)
                {
                    double t = seg < 1e-6 ? 0 : kalan / seg;
                    return xs[i] + (xs[i + 1] - xs[i]) * t;
                }
                kalan -= seg;
            }
            return xs.Count > 0 ? xs[xs.Count - 1] : xFrom;
        }

        private static double KirisAcilimMontajGeri(List<KirisAcilimMontajRun> runs, int g0, int g1, double xTo, double dist)
        {
            if (dist <= 0.01) return xTo;
            var xs = new List<double>();
            KirisAcilimMontajKnot(runs, g0, g1, runs[g0].XL, xTo, xs);
            double kalan = dist;
            for (int i = xs.Count - 1; i > 0; i--)
            {
                double seg = KirisAcilimMontajSeg(runs, g0, g1, xs[i - 1], xs[i]);
                if (seg >= kalan - 1e-6)
                {
                    double t = seg < 1e-6 ? 0 : kalan / seg;
                    return xs[i] - (xs[i] - xs[i - 1]) * t;
                }
                kalan -= seg;
            }
            return xs.Count > 0 ? xs[0] : xTo;
        }

        /// <summary>
        /// Etiket 5 cm katı değilse eksik santim, bindirme olmayan uçtaki gönyeye eklenir.
        /// lb kıpırdamaz. Gönye kesitin içinde kalmıyorsa boy değiştirilmez.
        /// </summary>
        private static void KirisAcilimMontajBesTamamla(
            List<Point2d> pts, ref double boy, bool hookL, bool hookR,
            double xa, double xb, double odaL, double odaR, double hkL, double hkR)
        {
            int g = (int)Math.Round(boy);
            if (g <= 0 || g >= (int)KirisAcilimMontajMaxBoyCm || g % (int)KirisAcilimYuvarlamaCm == 0) return;
            int hedef = g + (int)KirisAcilimYuvarlamaCm - g % (int)KirisAcilimYuvarlamaCm;
            if (hedef > (int)KirisAcilimMontajMaxBoyCm) hedef = (int)KirisAcilimMontajMaxBoyCm;
            double eksik = hedef - boy;
            if (eksik < 0.05) return;
            if (hookR && KirisAcilimMontajUcUzat(pts, false, xa, xb, eksik, odaR, hkR))
            {
                boy += eksik;
                return;
            }
            if (hookL && KirisAcilimMontajUcUzat(pts, true, xa, xb, eksik, odaL, hkL))
                boy += eksik;
        }

        /// <summary>Gönye kuyruğunu eksik kadar uzatır. Düşey bacak kesitte, yatay kuyruk kirişin içinde kalır.</summary>
        private static bool KirisAcilimMontajUcUzat(
            List<Point2d> pts, bool sol, double xa, double xb, double ek, double oda, double hk)
        {
            if (pts == null || pts.Count < 2 || ek < 0.05) return false;
            int iTip = sol ? 0 : pts.Count - 1;
            int iYan = sol ? 1 : pts.Count - 2;
            double dx = pts[iTip].X - pts[iYan].X;
            double dy = pts[iTip].Y - pts[iYan].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.2) return false;
            double slack;
            if (Math.Abs(dy) >= Math.Abs(dx))
                slack = oda - len;
            else
            {
                double ortaya = 0.5 * (xa + xb) - pts[iTip].X;
                bool ice = dx * ortaya > 0;
                if (ice)
                {
                    double diger = sol ? pts[pts.Count - 1].X : pts[0].X;
                    slack = Math.Abs(diger - pts[iTip].X) - 5.0;
                }
                else
                {
                    double sigar = hk - KirisAcilimMontajPaspayiCm;
                    slack = sigar - len;
                }
            }
            if (slack < ek - 0.05) return false;
            double ux = dx / len;
            double uy = dy / len;
            pts[iTip] = new Point2d(pts[iTip].X + ek * ux, pts[iTip].Y + ek * uy);
            return true;
        }

        private void KirisAcilimMontajCubuk(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, double x0, double yShift,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            List<KirisAcilimMontajRun> runs, int g0, int g1,
            double xa, double xb, int adet, int cap, bool hookL, bool hookR, int sira)
        {
            // Çapı farklı kirişe uzanan uç, o kirişin kotuna kırılmaz; kendi hizasında düz lb bindirmesi kalır.
            double Y(double x)
            {
                for (int i = g0; i <= g1; i++)
                {
                    KirisAcilimMontajRun r = runs[i];
                    if (x < r.XL - 0.05 || x > r.XR + 0.05) continue;
                    if (r.Cap == cap && r.Adet == adet) return KirisAcilimMontajY(runs, g0, g1, x);
                    for (int j = i - 1; j >= g0; j--)
                    {
                        if (runs[j].Cap == cap && runs[j].Adet == adet) return runs[j].YR;
                    }
                    for (int j = i + 1; j <= g1; j++)
                    {
                        if (runs[j].Cap == cap && runs[j].Adet == adet) return runs[j].YL;
                    }
                }
                return KirisAcilimMontajY(runs, g0, g1, x);
            }

            var pts = new List<Point2d>();
            KirisAcilimMontajRun sol = runs[g0];
            KirisAcilimMontajRun sag = runs[g1];
            bool solKomsu = KirisAcilimKomsuAralik(chain, sol.I0, sol.I1, true, out _, out _);
            bool sagKomsu = KirisAcilimKomsuAralik(chain, sag.I0, sag.I1, false, out _, out _);
            bool solKot = hookL && KirisAcilimMontajKotKesit(chain, sol, yShift, true);
            bool sagKot = hookR && KirisAcilimMontajKotKesit(chain, sag, yShift, false);
            if (hookL && solKot)
            {
                double odaL = KirisAcilimGonyeRoom(chain, sol.I0, sol.I1, yShift, true, true, false, Y(xa), sol.H, out double birL);
                KirisAcilimGonyeEkle(pts, true, xa, Y(xa), true, sol.HkSol, sol.H, cap, false, xb, odaL, birL, false, false, out double _);
            }
            else if (hookL && !solKomsu)
                KirisAcilimUcNokta(pts, chain, sol.I0, sol.I1, sup, gorunus, x0, yShift, true, xa, Y(xa), true, sol.HkSol, sol.H, cap, false, xb, sol.SolSerbest);
            pts.Add(new Point2d(xa, Y(xa)));
            var orta = new List<Point2d>();
            for (int i = g0; i <= g1; i++)
            {
                if (runs[i].XL > xa + 0.5 && runs[i].XL < xb - 0.5)
                    orta.Add(new Point2d(runs[i].XL, Y(runs[i].XL)));
                if (runs[i].XR > xa + 0.5 && runs[i].XR < xb - 0.5)
                    orta.Add(new Point2d(runs[i].XR, Y(runs[i].XR)));
            }
            orta.Sort((a, b) => a.X.CompareTo(b.X));
            double sonX = xa;
            for (int i = 0; i < orta.Count; i++)
            {
                if (orta[i].X <= sonX + 0.01) continue;
                pts.Add(orta[i]);
                sonX = orta[i].X;
            }
            if (xb > sonX + 0.01) pts.Add(new Point2d(xb, Y(xb)));
            if (hookR && sagKot)
            {
                double odaR = KirisAcilimGonyeRoom(chain, sag.I0, sag.I1, yShift, false, true, false, Y(xb), sag.H, out double birR);
                KirisAcilimGonyeEkle(pts, false, xb, Y(xb), true, sag.HkSag, sag.H, cap, false, xa, odaR, birR, false, false, out double _);
            }
            else if (hookR && !sagKomsu)
                KirisAcilimUcNokta(pts, chain, sag.I0, sag.I1, sup, gorunus, x0, yShift, false, xb, Y(xb), true, sag.HkSag, sag.H, cap, false, xa, sag.SagSerbest);
            double boy = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                double dx = pts[k].X - pts[k - 1].X;
                double dy = pts[k].Y - pts[k - 1].Y;
                boy += Math.Sqrt(dx * dx + dy * dy);
            }
            double odaSol = hookL
                ? KirisAcilimGonyeRoom(chain, sol.I0, sol.I1, yShift, true, true, false, Y(xa), sol.H, out _)
                : 0;
            double odaSag = hookR
                ? KirisAcilimGonyeRoom(chain, sag.I0, sag.I1, yShift, false, true, false, Y(xb), sag.H, out _)
                : 0;
            KirisAcilimMontajBesTamamla(pts, ref boy, hookL, hookR, xa, xb, odaSol, odaSag, sol.HkSol, sag.HkSag);
            KirisAcilimDonatiPl(tr, ms, KirisAcilimRenkMontaj, pts);
            KirisAcilimAcilimCiz(tr, ms, db, pts, KirisAcilimRenkMontaj, adet, cap, boy, KirisAcilimMontajAcilimCm, ref _kirisAcilimMontajSira);
        }

        private void KirisAcilimMontajEtiket(
            Transaction tr, BlockTableRecord ms, Database db, KirisAcilimMontajRun run)
        {
            if (run.Adet <= 0 || run.Cap <= 0) return;
            string lbl = run.Adet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + run.Cap.ToString(CultureInfo.InvariantCulture);
            double xm = 0.5 * (run.XL + run.XR);
            double yUst = 0.5 * (run.YL + run.YR) + KirisAcilimMontajPaspayiCm;
            DrawBeamLabel(tr, ms, db, new Point3d(xm, yUst - KirisAcilimMontajEtiketUsttenCm, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            KirisAcilimYaziKutusuKenar(tr, yUst - KirisAcilimMontajEtiketUsttenCm, ustKenar: true);
        }

        private void KirisAcilimMontajDonatiYedek(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbestOn, out bool sagSerbestOn, out _, out _);
            if (KirisAcilimKonsolTekBasina(chain, i0, i1, solSerbestOn, sagSerbestOn))
            {
                KirisAcilimMontajBilgi mSagB = m;
                if (chain[i1].Beam != null && montaj.TryGetValue(chain[i1].Beam, out KirisAcilimMontajBilgi mSonB)) mSagB = mSonB;
                bool solBasB = chain[i0].Cross0 == beam.StartAxisId;
                bool sagBasB = chain[i1].Beam != null && chain[i1].Cross1 == chain[i1].Beam.StartAxisId;
                int nSol = solBasB ? m.SolUstAdet : m.SagUstAdet;
                int cSol = solBasB ? m.SolUstCap : m.SagUstCap;
                int nSag = sagBasB ? mSagB.SolUstAdet : mSagB.SagUstAdet;
                int cSag = sagBasB ? mSagB.SolUstCap : mSagB.SagUstCap;
                KirisAcilimAsBirlestir(m.Adet, m.Cap, nSol, cSol, nSag, cSag, out int adetB, out int capB);
                if (adetB > 0 && capB > 0) { m.Adet = adetB; m.Cap = capB; }
            }
            if (m.Adet <= 0 || m.Cap <= 0) return;

            double xA = x0 + net0;
            double xB = x0 + net1;
            if (xB - xA < 1.0) return;

            double top0 = yShift + KirisAcilimKotAt(chain, i0, i1, net0);
            double top1 = yShift + KirisAcilimKotAt(chain, i0, i1, net1);
            double yA = top0 - KirisAcilimMontajPaspayiCm;
            double yB = top1 - KirisAcilimMontajPaspayiCm;
            double hkSol = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, true);
            double hkSag = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, false);
            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out double xSolGonye, out double xSagGonye);
            double xSol = solSerbest ? xSolGonye : xA;
            double xSag = sagSerbest ? xSagGonye : xB;
            var pts = new List<Point2d>();
            KirisAcilimUcNokta(pts, chain, i0, i1, sup, gorunus, x0, yShift, true, xSol, yA, true, hkSol, beam.HeightCm, m.Cap, false, xSag, solSerbest);
            pts.Add(new Point2d(xSol, yA));
            pts.Add(new Point2d(xSag, yB));
            KirisAcilimUcNokta(pts, chain, i0, i1, sup, gorunus, x0, yShift, false, xSag, yB, true, hkSag, beam.HeightCm, m.Cap, false, xSol, sagSerbest);
            KirisAcilimDonatiPl(tr, ms, KirisAcilimRenkMontaj, pts);
            string lbl = m.Adet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + m.Cap.ToString(CultureInfo.InvariantCulture);
            double xm = 0.5 * (xA + xB);
            double yUst = 0.5 * (top0 + top1);
            DrawBeamLabel(tr, ms, db, new Point3d(xm, yUst - KirisAcilimMontajEtiketUsttenCm, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            KirisAcilimYaziKutusuKenar(tr, yUst - KirisAcilimMontajEtiketUsttenCm, ustKenar: true);
        }

        /// <summary>
        /// Alt düz donatının eski tek çizgi sürümü. Yeni sürüm 2–4 arasını yüz başına böler.
        /// </summary>
        private void KirisAcilimDuzDonatiYedek(
            Transaction tr, BlockTableRecord ms, Database db,
            List<KirisAcilimSpan> chain, int i0, int i1,
            double x0, double yShift, double net0, double net1,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj,
            KirisAcilimSupport[] sup, List<KirisAcilimSaplama> gorunus,
            Dictionary<BeamInfo, KirisAcilimUcBilgi> ucTipi)
        {
            BeamInfo beam = chain[i0].Beam;
            if (beam == null || montaj == null || !montaj.TryGetValue(beam, out KirisAcilimMontajBilgi m)) return;
            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbestOn, out bool sagSerbestOn, out _, out _);
            if (KirisAcilimKonsolTekBasina(chain, i0, i1, solSerbestOn, sagSerbestOn))
            {
                KirisAcilimMontajBilgi mSagB = m;
                if (chain[i1].Beam != null && montaj.TryGetValue(chain[i1].Beam, out KirisAcilimMontajBilgi mSonB)) mSagB = mSonB;
                bool solBasB = chain[i0].Cross0 == beam.StartAxisId;
                bool sagBasB = chain[i1].Beam != null && chain[i1].Cross1 == chain[i1].Beam.StartAxisId;
                int nSol = solBasB ? m.SolAltAdet : m.SagAltAdet;
                int cSol = solBasB ? m.SolAltCap : m.SagAltCap;
                int nSag = sagBasB ? mSagB.SolAltAdet : mSagB.SagAltAdet;
                int cSag = sagBasB ? mSagB.SolAltCap : mSagB.SagAltCap;
                KirisAcilimAsBirlestir(m.DuzAdet, m.DuzCap, nSol, cSol, nSag, cSag, out int adetB, out int capB);
                if (adetB > 0 && capB > 0) { m.DuzAdet = adetB; m.DuzCap = capB; }
            }
            if (m.DuzAdet <= 0 || m.DuzCap <= 0) return;

            double xA = x0 + net0;
            double xB = x0 + net1;
            if (xB - xA < 1.0) return;

            double h = beam.HeightCm;
            double bot0 = yShift + KirisAcilimKotAt(chain, i0, i1, net0) - h;
            double bot1 = yShift + KirisAcilimKotAt(chain, i0, i1, net1) - h;
            double yA = bot0 + KirisAcilimDuzPaspayiCm;
            double yB = bot1 + KirisAcilimDuzPaspayiCm;
            double hkSol = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, true);
            double hkSag = KirisAcilimMesnetHk(chain, i0, i1, sup, gorunus, false);
            KirisAcilimKonsolUclari(chain, i0, i1, ucTipi, gorunus, x0, out bool solSerbest, out bool sagSerbest, out double xSolGonye, out double xSagGonye);
            double xSol = solSerbest ? xSolGonye : xA;
            double xSag = sagSerbest ? xSagGonye : xB;
            var pts = new List<Point2d>();
            KirisAcilimUcNokta(pts, chain, i0, i1, sup, gorunus, x0, yShift, true, xSol, yA, false, hkSol, h, m.DuzCap, false, xSag, solSerbest);
            pts.Add(new Point2d(xSol, yA));
            pts.Add(new Point2d(xSag, yB));
            KirisAcilimUcNokta(pts, chain, i0, i1, sup, gorunus, x0, yShift, false, xSag, yB, false, hkSag, h, m.DuzCap, false, xSol, sagSerbest);
            KirisAcilimDonatiPl(tr, ms, KirisAcilimRenkDuz, pts);
            string lbl = m.DuzAdet.ToString(CultureInfo.InvariantCulture) + "\u00F8" + m.DuzCap.ToString(CultureInfo.InvariantCulture);
            double xm = 0.5 * (xA + xB);
            double yAlt = 0.5 * (bot0 + bot1);
            DrawBeamLabel(tr, ms, db, new Point3d(xm, yAlt + KirisAcilimDuzEtiketAlttanCm, 0), lbl, 8.0, 0.0, KirisAcilimDonatiYaziLayer, useMiddleCenter: true);
            KirisAcilimYaziKutusuKenar(tr, yAlt + KirisAcilimDuzEtiketAlttanCm, ustKenar: false);
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
        /// <summary>Yazı kutusu: üst kenar (ustKenar) veya düşey orta hedef kota oturur.</summary>
        private void KirisAcilimYaziKutusuKenar(Transaction tr, double hedefY, bool ustKenar)
        {
            if (tr == null || _lastBeamLabelId.IsNull) return;
            var txt = tr.GetObject(_lastBeamLabelId, OpenMode.ForWrite) as DBText;
            if (txt == null) return;
            Extents3d ext;
            try { ext = txt.GeometricExtents; }
            catch { return; }
            double yRef = ustKenar ? ext.MaxPoint.Y : 0.5 * (ext.MinPoint.Y + ext.MaxPoint.Y);
            double dy = hedefY - yRef;
            if (Math.Abs(dy) < 0.05) return;
            txt.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
        }

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

        /// <summary>Aynı doğru üzerindeki ara noktaları siler. Kapalı poligonda ilk/son da bakılır.</summary>
        private static void KirisAcilimDuzNoktaSil(List<Point2d> pts, bool kapali)
        {
            if (pts == null || pts.Count < 3) return;
            bool degisti = true;
            while (degisti && pts.Count >= 3)
            {
                degisti = false;
                int n = pts.Count;
                for (int i = 0; i < n; i++)
                {
                    if (!kapali && (i == 0 || i == n - 1)) continue;
                    int ip = kapali ? (i - 1 + n) % n : i - 1;
                    int inx = kapali ? (i + 1) % n : i + 1;
                    if (ip < 0 || inx >= n || ip == inx) continue;
                    if (!KirisAcilimNoktaDuz(pts[ip], pts[i], pts[inx])) continue;
                    pts.RemoveAt(i);
                    degisti = true;
                    break;
                }
            }
        }

        private static bool KirisAcilimNoktaDuz(Point2d a, Point2d b, Point2d c)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double bcx = c.X - b.X;
            double bcy = c.Y - b.Y;
            double lab = Math.Sqrt(abx * abx + aby * aby);
            double lbc = Math.Sqrt(bcx * bcx + bcy * bcy);
            if (lab < 0.2 || lbc < 0.2) return true;
            double cross = abx * bcy - aby * bcx;
            return Math.Abs(cross) <= 0.02 * lab * lbc;
        }

        private static double KirisAcilimYayBoy(Point2d a, Point2d b, double bulge)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (Math.Abs(bulge) < 1e-8) return chord;
            double theta = 4.0 * Math.Atan(bulge);
            double sin = Math.Sin(theta * 0.5);
            if (Math.Abs(sin) < 1e-8) return chord;
            double radius = chord / (2.0 * Math.Abs(sin));
            return radius * Math.Abs(theta);
        }

        /// <summary>Gönye köşesine 3 cm yay koyar. Düz giden ara nokta yay olmaz.</summary>
        private static List<Point2d> KirisAcilimGonyeYayNokta(List<Point2d> src, out double[] bulge)
        {
            var clean = new List<Point2d>(src);
            KirisAcilimDuzNoktaSil(clean, false);
            var dst = new List<Point2d>();
            var bul = new List<double>();
            int n = clean.Count;
            double R = KirisAcilimGonyeYaricapCm;
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1)
                {
                    dst.Add(clean[i]);
                    bul.Add(0);
                    continue;
                }
                double v1x = clean[i].X - clean[i - 1].X;
                double v1y = clean[i].Y - clean[i - 1].Y;
                double v2x = clean[i + 1].X - clean[i].X;
                double v2y = clean[i + 1].Y - clean[i].Y;
                double l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                double cross = v1x * v2y - v1y * v2x;
                double dot = v1x * v2x + v1y * v2y;
                double ang = Math.Atan2(cross, dot);
                bool doksan = Math.Abs(Math.Abs(ang) - Math.PI * 0.5) < 0.35;
                double r = R;
                if (!doksan || l1 < 1.0 || l2 < 1.0 || r > l1 * 0.45 || r > l2 * 0.45)
                {
                    if (doksan && l1 >= 1.0 && l2 >= 1.0)
                    {
                        r = l1 * 0.45 < l2 * 0.45 ? l1 * 0.45 : l2 * 0.45;
                        if (r < 0.4) r = 0;
                    }
                    else r = 0;
                }
                if (r < 0.4)
                {
                    dst.Add(clean[i]);
                    bul.Add(0);
                    continue;
                }
                double u1x = v1x / l1;
                double u1y = v1y / l1;
                double u2x = v2x / l2;
                double u2y = v2y / l2;
                dst.Add(new Point2d(clean[i].X - u1x * r, clean[i].Y - u1y * r));
                bul.Add(Math.Tan(ang * 0.25));
                dst.Add(new Point2d(clean[i].X + u2x * r, clean[i].Y + u2y * r));
                bul.Add(0);
            }
            bulge = bul.ToArray();
            return dst;
        }

        private static double KirisAcilimDonatiPl(Transaction tr, BlockTableRecord ms, short aci, List<Point2d> pts)
        {
            if (pts == null || pts.Count < 2) return 0;
            double[] bulge;
            List<Point2d> yay = KirisAcilimGonyeYayNokta(pts, out bulge);
            if (yay.Count < 2) return 0;
            var pl = new Polyline();
            int n = 0;
            double len = 0;
            Point2d prev = default(Point2d);
            double prevBulge = 0;
            bool any = false;
            for (int i = 0; i < yay.Count; i++)
            {
                Point2d p = yay[i];
                if (any && Math.Abs(p.X - prev.X) < 0.2 && Math.Abs(p.Y - prev.Y) < 0.2) continue;
                if (any) len += KirisAcilimYayBoy(prev, p, prevBulge);
                pl.AddVertexAt(n, p, i < bulge.Length ? bulge[i] : 0, 0, 0);
                prevBulge = i < bulge.Length ? bulge[i] : 0;
                prev = p;
                any = true;
                n++;
            }
            if (n < 2) return 0;
            pl.Closed = false;
            pl.Layer = KirisAcilimDonatiLayer;
            if (aci > 0)
                pl.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
            AppendEntity(tr, ms, pl);
            return len;
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
                    SolAltAdet = BitConverter.ToInt16(data, i * rec + 132),
                    SolAltCap = BitConverter.ToInt16(data, i * rec + 134),
                    SagUstAdet = BitConverter.ToInt16(data, i * rec + 136),
                    SagUstCap = BitConverter.ToInt16(data, i * rec + 138),
                    SagAltAdet = BitConverter.ToInt16(data, i * rec + 140),
                    SagAltCap = BitConverter.ToInt16(data, i * rec + 142)
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
