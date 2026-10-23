using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// TEMELDONATI 5. plan: radye zımbalama donatısı. Kolon altında X/Y doğrultusunda kapalı etriye merdiveni
    /// (4ø12 montaj + ø/s etriye), etiketler, ILAVE_OLCU kol ölçüleri ve kolon düşeydeki gibi etriye açılımı.
    /// Etriye çap/aralığı ve gerekli Asb GPR zımbalama tablosundan; kol boyu en az 100 cm, Asb sağlanana dek uzar.
    /// Montaj çubukları kolon kenarından en az 5 cm içeride; etriye montajın 1 cm dışına çıkar.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double ZimPaspayCm = 5.0;
        private const int ZimMinEtriyeCapMm = 10;
        private const double ZimMontajYariMaxCm = 11.5;   // etriye eni 25
        private const double ZimMontajYariMinCm = 4.0;
        private const double ZimEtriyeTasmaCm = 1.0;
        private const double ZimKolBoyuCm = 100.0;
        private const double ZimKolBoyuMaxCm = 300.0;
        private const double ZimUzunKolonOrani = 2.5;
        private const double ZimYaziCm = 12.0;
        private const double ZimOlcuOfsetCm = 20.0;
        private const string ZimMontajYazi = "4ø12";
        private const string ZimOlcuStili = "ILAVE_OLCU";

        private sealed class ZimMerdiven
        {
            public Vector2d U;        // merdiven doğrultusu
            public Vector2d V;        // enine
            public Point2d Orta;
            public double Ic;         // ortadan kolon yüzüne (U)
            public double VMin, VMax; // kolon sınırı (V, ortaya göre)
            public double R;          // montaj yarı aralığı
            public int Kenar;         // 0: kolon ekseninde; ±1: uç merdiveni (V yönünde kolon ucuna kaymış)
            public double KolEksi;    // -U kolu (kolon yüzünden)
            public double KolArti;
            public int CapMm;
            public int AralikCm;

            public double EtriyeEni => Math.Round(2.0 * (R + ZimEtriyeTasmaCm));
            public double Boy => 2.0 * Ic + KolEksi + KolArti;
            public Point2d Nokta(double t, double s) => Orta + U * t + V * s;
            /// <summary>STA4 sayımı: kol boyunca s aralıkla kolon yüzü ve uç dahil floor(kol/s)+1 (100/20 → 6, 100/15 → 7).</summary>
            public static int KolEtriye(double kol, int s) => kol < 2.0 * ZimPaspayCm ? 0 : (int)Math.Floor(kol / s + 1e-6) + 1;
            public int Etriye => KolEtriye(KolEksi, AralikCm) + KolEtriye(KolArti, AralikCm);
            public bool Yatay => Math.Abs(U.X) >= Math.Abs(U.Y);
            /// <summary>Dünya +X (yatayda +Y) yönündeki kol işareti.</summary>
            public int Yon(bool artiDunya) => (Yatay ? U.X : U.Y) >= 0 ? (artiDunya ? 1 : -1) : (artiDunya ? -1 : 1);
            public double Kol(int yon) => yon < 0 ? KolEksi : KolArti;
        }

        /// <summary>5. plan kopyasına zımbalama donatısı. Çizilen kolon sayısı.</summary>
        public int DrawZimbalamaPlan(Database db, Editor ed, TemelPlanCopyLayout copy, IList<GprZimbalamaKolon> kolonlar)
        {
            if (db == null || kolonlar == null || kolonlar.Count == 0 || _model?.Floors == null || _model.Floors.Count == 0) return 0;
            var gf = NtsGeometryServices.Instance.CreateGeometryFactory();
            Geometry icTemel = null;
            try
            {
                if (copy.TemelGeom != null && !copy.TemelGeom.IsEmpty)
                    icTemel = copy.TemelGeom.Buffer(-ZimPaspayCm);
            }
            catch { icTemel = null; }

            FloorInfo floor = _model.Floors[0];
            int cizilen = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                EnsurePlanLayer(tr, db, LayerDonatiGovde, 4, LineWeight.LineWeight040, useDashed: false);
                EnsurePlanLayer(tr, db, LayerEtriye, 150, LineWeight.LineWeight035, useDashed: false);
                EnsurePlanLayer(tr, db, LayerKesitGorunus, 253, LineWeight.LineWeight020, useDashed: true);
                EnsurePlanLayer(tr, db, LayerDonatiYazisiPerde, 3, LineWeight.LineWeight020, useDashed: false);
                EnsurePlanLayer(tr, db, LayerEtiketCizgisi, 64, LineWeight.LineWeight015, useDashed: false);
                EnsurePlanLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, useDashed: false);
                ObjectId dimStyleId = ZimOlcuStil(tr, db);

                foreach (GprZimbalamaKolon z in kolonlar)
                {
                    // Uygulama tercihi: hesapta ø8 çıksa da etriye en az ø10.
                    int staCap = z.CapMm;
                    if (z.CapMm < ZimMinEtriyeCapMm) z.CapMm = ZimMinEtriyeCapMm;
                    if (!TryZimKolon(floor, z, copy, out Point2d merkez, out double hw, out double hh, out double aci))
                    {
                        ed?.WriteMessage("\nTEMELDONATI: {0} zimbalama kolonu ST4'te bulunamadi, atlandi.", z.Ad);
                        continue;
                    }
                    double h = ZimRadyeKalinlik(copy, merkez, gf, z.DCm);
                    double etriyeH = Math.Max(10.0, h - 2.0 * ZimPaspayCm);
                    List<ZimMerdiven> merdivenler = ZimMerdivenler(merkez, hw, hh, aci, z, icTemel, gf);
                    if (merdivenler.Count == 0)
                    {
                        ed?.WriteMessage("\nTEMELDONATI: {0} radye icinde merdiven kolu sigmadi.", z.Ad);
                        continue;
                    }
                    GetRotatedRectBounds(hw, hh, aci, out double kMinX, out double kMaxX, out double kMinY, out _);
                    double hook = Math.Max(10.0, EtriyeAcilimKancaCm(z.CapMm));

                    // Etriye tipleri (en farklıysa ayrı açılım/etiket).
                    var tipler = merdivenler.GroupBy(m => m.EtriyeEni)
                        .Select(g => (En: g.Key, Adet: g.Sum(m => m.Etriye),
                            L: EtriyeAcilimKesimBoyCm(g.Key, etriyeH, hook)))
                        .OrderByDescending(t => t.En).ToList();

                    foreach (ZimMerdiven m in merdivenler)
                        CizMerdiven(tr, btr, m);
                    CizZimEtiketler(tr, btr, db, merdivenler, tipler, z, merkez);
                    foreach (ZimMerdiven m in merdivenler)
                        CizZimOlcu(tr, btr, db, m, dimStyleId);
                    double acilimX = merkez.X + kMaxX + 35.0;
                    double acilimUst = Math.Min(merkez.Y + kMinY, ZimYatayAltY(merdivenler, merkez)) - 75.0;
                    foreach (var t in tipler)
                        acilimX = CizZimEtriyeAcilim(tr, btr, db, acilimX, acilimUst, t.En, etriyeH, hook, z.CapMm, t.L) + 30.0;

                    int adet = tipler.Sum(t => t.Adet);
                    double saglanan = adet * 2.0 * Math.PI * z.CapMm * z.CapMm / 400.0;
                    ed?.WriteMessage("\n  {0}: {1} etriye ø{2}/{3} ({4}), As={5:0.0} cm2 >= Asb={6:0.00} (STA {7}ø{9}), h={8:0} cm.",
                        z.Ad, adet, z.CapMm, z.AralikCm,
                        string.Join(", ", tipler.Select(t => string.Format(CultureInfo.InvariantCulture, "{0}x{1:0} L={2:0}", t.Adet, t.En, t.L))),
                        saglanan, z.AsbCm2, z.Adet, h, staCap);
                    cizilen++;
                }
                tr.Commit();
            }
            return cizilen;
        }

        private bool TryZimKolon(FloorInfo floor, GprZimbalamaKolon z, TemelPlanCopyLayout copy,
            out Point2d merkez, out double hw, out double hh, out double aci)
        {
            merkez = Point2d.Origin;
            hw = hh = aci = 0;
            int step = _model.ColumnFloorKeyStep > 0 ? _model.ColumnFloorKeyStep : (z.Numara >= 1000 ? 1000 : 100);
            int colNo = z.Numara % step;
            foreach (ColumnAxisInfo col in _model.Columns)
            {
                if (col.ColumnNo != colNo) continue;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) return false;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                var dim = sectionId > 0 && _model.ColumnDimsBySectionId.ContainsKey(sectionId)
                    ? _model.ColumnDimsBySectionId[sectionId]
                    : (W: 40.0, H: 40.0);
                hw = dim.W / 2.0;
                hh = dim.H / 2.0;
                Vector2d offsetLocal;
                if (col.ColumnType == 2)
                {
                    offsetLocal = ComputeColumnOffsetCircle(col.OffsetXRaw, col.OffsetYRaw);
                    hw = hh = Math.Max(hw, hh);
                }
                else
                    offsetLocal = ComputeColumnOffset(col.OffsetXRaw, col.OffsetYRaw, hw, hh);
                Vector2d offsetGlobal = Rotate(offsetLocal, col.AngleDeg);
                merkez = new Point2d(axisNode.X + offsetGlobal.X + copy.OffsetX, axisNode.Y + offsetGlobal.Y + copy.OffsetY);
                aci = col.AngleDeg;
                return true;
            }
            return false;
        }

        /// <summary>Kolon merkezindeki radye kalınlığı; bulunamazsa d + 7 (5'e yuvarlı).</summary>
        private double ZimRadyeKalinlik(TemelPlanCopyLayout copy, Point2d merkez, GeometryFactory gf, double dCm)
        {
            try
            {
                Geometry nokta = gf.CreatePoint(new Coordinate(merkez.X, merkez.Y)).Buffer(1.0);
                double h = TryGetSlabThicknessAtPoint(copy.OffsetX, copy.OffsetY, nokta, gf);
                if (h > 1.0) return h;
            }
            catch { }
            return dCm > 0 ? Math.Ceiling((dCm + 7.0) / 5.0) * 5.0 : 50.0;
        }

        /// <summary>Kare/dikdörtgen kolonda çapraz iki merdiven; uzun kolonda (perde) uzun doğrultuda bir, uçlarda iki enine.</summary>
        private static List<ZimMerdiven> ZimMerdivenler(Point2d merkez, double hw, double hh, double aci, GprZimbalamaKolon z, Geometry icTemel, GeometryFactory gf)
        {
            Vector2d ux = Rotate(new Vector2d(1, 0), aci);
            Vector2d uy = Rotate(new Vector2d(0, 1), aci);
            var list = new List<ZimMerdiven>();
            // ic: ortadan kolon yüzüne (U); dik: kolonun V yarı boyu; kay: ortanın V kayması.
            void Ekle(Vector2d u, Vector2d v, double ic, double dik, double kay, int kenar)
            {
                double vMin = -dik - kay, vMax = dik - kay;
                double r = Math.Min(ZimMontajYariMaxCm, Math.Min(vMax, -vMin) - ZimPaspayCm);
                if (r < ZimMontajYariMinCm) r = ZimMontajYariMinCm;
                list.Add(new ZimMerdiven
                {
                    U = u, V = v, Orta = merkez + v * kay, Ic = ic, VMin = vMin, VMax = vMax, R = r, Kenar = kenar,
                    CapMm = z.CapMm, AralikCm = z.AralikCm
                });
            }
            // Uç merdiveni montajı kolon ucundan 5 cm içeride: kayma = yarı boy − 5 − r.
            double UcKay(double yari) => yari - ZimPaspayCm - ZimMontajYariMaxCm;

            if (hh >= ZimUzunKolonOrani * hw && UcKay(hh) > ZimMontajYariMaxCm)
            {
                Ekle(uy, ux, hh, hw, 0, 0);
                Ekle(ux, uy, hw, hh, UcKay(hh), 1);
                Ekle(ux, uy, hw, hh, -UcKay(hh), -1);
            }
            else if (hw >= ZimUzunKolonOrani * hh && UcKay(hw) > ZimMontajYariMaxCm)
            {
                Ekle(ux, uy, hw, hh, 0, 0);
                Ekle(uy, ux, hh, hw, UcKay(hw), 1);
                Ekle(uy, ux, hh, hw, -UcKay(hw), -1);
            }
            else
            {
                Ekle(ux, uy, hw, hh, 0, 0);
                Ekle(uy, ux, hh, hw, 0, 0);
            }

            // Kol boyu: en az 100 cm; toplam etriye alanı Asb'yi karşılayana dek 5 cm adımla uzar, radye paspayında kesilir.
            double tekAlan = 2.0 * Math.PI * z.CapMm * z.CapMm / 400.0;
            int gerekli = z.AsbCm2 > 0 ? (int)Math.Ceiling(z.AsbCm2 / tekAlan - 1e-6) : 0;
            for (double hedef = ZimKolBoyuCm; ; hedef += 5.0)
            {
                int toplam = 0;
                foreach (ZimMerdiven m in list)
                {
                    m.KolEksi = ZimKolSigdir(m, -1, hedef, icTemel, gf);
                    m.KolArti = ZimKolSigdir(m, +1, hedef, icTemel, gf);
                    toplam += m.Etriye;
                }
                if (toplam >= gerekli || hedef >= ZimKolBoyuMaxCm) break;
            }
            list.RemoveAll(m => m.KolEksi <= 0 && m.KolArti <= 0);
            return list;
        }

        /// <summary>Kol ucu (etriye köşeleri) radye paspayı içinde kalacak en uzun 5 cm katı kol; sığmazsa 0.</summary>
        private static double ZimKolSigdir(ZimMerdiven m, int yon, double hedef, Geometry icTemel, GeometryFactory gf)
        {
            if (icTemel == null || icTemel.IsEmpty) return hedef;
            double b2 = m.EtriyeEni / 2.0;
            for (double kol = hedef; kol >= 2.0 * ZimPaspayCm + 10.0; kol -= 5.0)
            {
                double t = yon * (m.Ic + kol);
                Point2d p1 = m.Nokta(t, -b2), p2 = m.Nokta(t, b2);
                try
                {
                    if (icTemel.Covers(gf.CreatePoint(new Coordinate(p1.X, p1.Y))) &&
                        icTemel.Covers(gf.CreatePoint(new Coordinate(p2.X, p2.Y))))
                        return kol;
                }
                catch { return hedef; }
            }
            return 0;
        }

        private static int ZimMontajBoy(ZimMerdiven m) => (int)(Math.Ceiling(m.Boy / 5.0 - 1e-6) * 5.0);

        /// <summary>
        /// Etriye konumları (U): STA4 adedi kolon yüzünden 5 cm sonra başlayıp uca 5 cm kala biten
        /// aralığa eşit dağıtılır (aralık ≤ s; kolon donatısı ve paspay korunur).
        /// </summary>
        private static List<double> ZimEtriyeKonumlari(ZimMerdiven m, int yon)
        {
            var list = new List<double>();
            double kol = m.Kol(yon);
            int n = ZimMerdiven.KolEtriye(kol, m.AralikCm);
            double adim = n > 1 ? (kol - 2.0 * ZimPaspayCm) / (n - 1) : 0.0;
            for (int k = 0; k < n; k++)
                list.Add(yon * (m.Ic + ZimPaspayCm + k * adim));
            return list;
        }

        /// <summary>Montaj (DONATI) iki çizgi; etriyeler (ETRIYE) montajın 1 cm dışına taşar.</summary>
        private void CizMerdiven(Transaction tr, BlockTableRecord btr, ZimMerdiven m)
        {
            double t0 = -(m.Ic + m.KolEksi), t1 = m.Ic + m.KolArti;
            ZimCizgi(tr, btr, m.Nokta(t0, -m.R), m.Nokta(t1, -m.R), LayerDonatiGovde);
            ZimCizgi(tr, btr, m.Nokta(t0, m.R), m.Nokta(t1, m.R), LayerDonatiGovde);
            double e = m.EtriyeEni / 2.0;
            for (int yon = -1; yon <= 1; yon += 2)
                foreach (double t in ZimEtriyeKonumlari(m, yon))
                    ZimCizgi(tr, btr, m.Nokta(t, -e), m.Nokta(t, e), LayerEtriye);
        }

        private static double ZimYatayAltY(List<ZimMerdiven> merdivenler, Point2d merkez)
        {
            double alt = double.MaxValue;
            foreach (ZimMerdiven m in merdivenler)
            {
                if (!m.Yatay) continue;
                double e = m.EtriyeEni / 2.0;
                alt = Math.Min(alt, Math.Min(m.Nokta(0, -e).Y, m.Nokta(0, e).Y));
            }
            return alt == double.MaxValue ? merkez.Y : alt;
        }

        /// <summary>
        /// Etiketler: donatıya değen uçta dolu nokta, gerekirse 45° kırık kılavuz, yatay raf üstünde yazı.
        /// En düşey merdivenin üst kolunda sağa yatay raf (etriye + montaj); diğer merdivenlerde sağ kolda 45° kırık raf.
        /// Her merdiven yalnız kendi etriye adedini yazar (etiketlerin toplamı = çizilen etriye; metraj doğru çıkar).
        /// Çizgiler ETIKET CIZGISI, yazılar DONATI YAZISI. Poz numarası metraj komutuna bırakılır.
        /// </summary>
        private void CizZimEtiketler(Transaction tr, BlockTableRecord btr, Database db, List<ZimMerdiven> merdivenler,
            List<(double En, int Adet, double L)> tipler, GprZimbalamaKolon z, Point2d merkez)
        {
            string EtriyeYazi(ZimMerdiven m)
            {
                var t = tipler.First(x => Math.Abs(x.En - m.EtriyeEni) < 0.1);
                return string.Format(CultureInfo.InvariantCulture, "{0}ø{1}/{2} L={3:0}", m.Etriye, z.CapMm, z.AralikCm, t.L);
            }

            ZimMerdiven dus = null;
            foreach (ZimMerdiven m in merdivenler)
                if (!m.Yatay && (dus == null || m.Kenar == 0)) dus = m;

            if (dus != null)
            {
                int ust = dus.Yon(true);
                List<double> konum = ZimEtriyeKonumlari(dus, ust);
                int sagV = dus.V.X >= 0 ? 1 : -1;
                if (konum.Count > 0)
                {
                    // Etriye: uca en yakın 2. etriyenin sağ ucu; montaj: en az 35 cm aşağıda, iki etriye arasında.
                    int ie = Math.Max(0, konum.Count - 2);
                    ZimRafYazi(tr, btr, db, dus.Nokta(konum[ie], sagV * dus.EtriyeEni / 2.0), 0, EtriyeYazi(dus));
                    double adim = konum.Count > 1 ? Math.Abs(konum[1] - konum[0]) : dus.AralikCm;
                    double geri = (Math.Ceiling(35.0 / adim - 0.5) + 0.5) * adim;
                    double tm = konum[ie] - ust * geri;
                    if (ust * tm < dus.Ic + ZimPaspayCm) tm = konum[ie] - ust * 0.5 * adim;
                    ZimRafYazi(tr, btr, db, dus.Nokta(tm, sagV * dus.R), 0, MontajYazi(dus));
                }
            }

            foreach (ZimMerdiven m in merdivenler)
            {
                if (ReferenceEquals(m, dus)) continue;
                int sag = m.Yon(true);
                if (m.Kol(sag) <= 0) sag = -sag;
                double kol = m.Kol(sag);
                // Alt uç merdiveninde etiket aşağı, diğerlerinde yukarı.
                bool asagi = m.Orta.Y < merkez.Y - 1.0;
                int dikV = (m.V.Y >= 0) == !asagi ? 1 : -1;
                int egim = asagi ? -1 : 1;
                ZimRafYazi(tr, btr, db, m.Nokta(sag * (m.Ic + 0.55 * kol), dikV * m.R), egim * 22.0, MontajYazi(m));
                List<double> konum = ZimEtriyeKonumlari(m, sag);
                if (konum.Count > 0)
                {
                    double t = konum[Math.Min(konum.Count - 1, 1)];
                    ZimRafYazi(tr, btr, db, m.Nokta(t, dikV * m.EtriyeEni / 2.0), egim * 52.0, EtriyeYazi(m));
                }
            }
        }

        private static string MontajYazi(ZimMerdiven m) =>
            string.Format(CultureInfo.InvariantCulture, "{0} L={1}", ZimMontajYazi, ZimMontajBoy(m));

        /// <summary>Dolu nokta (sıfır iç çaplı halka): kılavuzun donatıya değdiği yer.</summary>
        private static void ZimNokta(Transaction tr, BlockTableRecord btr, Point2d p, double r)
        {
            var pl = new Polyline { Layer = LayerEtiketCizgisi };
            pl.AddVertexAt(0, new Point2d(p.X - r / 2.0, p.Y), 1.0, r, r);
            pl.AddVertexAt(1, new Point2d(p.X + r / 2.0, p.Y), 1.0, r, r);
            pl.Closed = true;
            AppendEntity(tr, btr, pl);
        }

        /// <summary>
        /// Donatıdan rafa etiket. yukseklik 0: donatı noktasından doğrudan sağa yatay raf;
        /// değilse 45° kırılıp sağa raf. Yazı rafın üstünde, sola dayalı; raf yazı bitimine kadar.
        /// </summary>
        private void ZimRafYazi(Transaction tr, BlockTableRecord btr, Database db, Point2d donati, double yukseklik, string yazi)
        {
            ZimNokta(tr, btr, donati, 2.4);
            bool kirikli = Math.Abs(yukseklik) > 1e-6;
            Point2d kirik = new Point2d(donati.X + Math.Abs(yukseklik), donati.Y + yukseklik);
            if (kirikli)
                ZimCizgi(tr, btr, donati, kirik, LayerEtiketCizgisi);
            double xYazi = kirik.X + (kirikli ? 4.0 : 18.0);
            var t = new DBText
            {
                Layer = LayerDonatiYazisiPerde,
                Height = ZimYaziCm,
                TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(yazi),
                Position = new Point3d(xYazi, kirik.Y + 2.5, 0)
            };
            ObjectId id = AppendEntityReturnId(tr, btr, t);
            double son = xYazi + yazi.Length * ZimYaziCm * 0.6;
            try
            {
                if (!id.IsNull) son = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents.MaxPoint.X;
            }
            catch { }
            ZimCizgi(tr, btr, kirik, new Point2d(son + 3.0, kirik.Y), LayerEtiketCizgisi);
        }

        /// <summary>
        /// Montaj ucu ile kolon köşesi arası (ILAVE_OLCU). Yatay merdivende sol kol, düşeyde üst kol;
        /// uç merdivenlerinde ölçü kolonun iç yanına, diğerlerinde yatayda alta / düşeyde sola.
        /// </summary>
        private void CizZimOlcu(Transaction tr, BlockTableRecord btr, Database db, ZimMerdiven m, ObjectId dimStyleId)
        {
            int yon = m.Yatay ? m.Yon(false) : m.Yon(true);
            if (m.Kol(yon) <= 0) yon = -yon;
            double kol = m.Kol(yon);
            if (kol <= 0) return;
            int yan;
            if (m.Kenar != 0) yan = -m.Kenar;
            else if (m.Yatay) yan = m.V.Y <= 0 ? 1 : -1;
            else yan = m.V.X <= 0 ? 1 : -1;
            // Uç merdiveninin iç yanında kolon sürer; ölçü montaj hizasında kolon yüzüne alınır.
            double kose = m.Kenar != 0 ? m.R : (yan > 0 ? m.VMax : -m.VMin);
            Point2d uc = m.Nokta(yon * (m.Ic + kol), yan * m.R);
            Point2d kolonKose = m.Nokta(yon * m.Ic, yan * kose);
            Point2d cizgi = m.Nokta(yon * (m.Ic + 0.5 * kol), yan * (Math.Max(kose, m.EtriyeEni / 2.0) + ZimOlcuOfsetCm));
            double rot = Math.Atan2(m.U.Y, m.U.X);
            var dim = new RotatedDimension(rot, new Point3d(uc.X, uc.Y, 0), new Point3d(kolonKose.X, kolonKose.Y, 0),
                new Point3d(cizgi.X, cizgi.Y, 0), "", dimStyleId) { Layer = LayerOlcu };
            AppendEntity(tr, btr, dim);
            try
            {
                // AppendEntity PLAN_OLCU hassasiyetini basar; ILAVE_OLCU tam sayıdır.
                dim.Dimrnd = 1.0;
                dim.Dimdec = 0;
                dim.Dimtdec = 0;
            }
            catch { }
        }

        /// <summary>Yardımcı'daki ILAVE_OLCU ile aynı: PLAN_OLCU tabanı, yazı 10, tam sayı.</summary>
        private static ObjectId ZimOlcuStil(Transaction tr, Database db)
        {
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(ZimOlcuStili)) return dst[ZimOlcuStili];
            ObjectId id = GetOrCreatePlanOlcuDimStyle(tr, db, 10.0, 1.0, ZimOlcuStili);
            try
            {
                var rec = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                rec.Dimrnd = 1.0;
                rec.Dimdec = 0;
                rec.Dimtdec = 0;
                rec.Dimzin = 12;
                BeykentOrtak.BeykentStilUygula.OlcuFarkiniUygula(tr, db, rec);
            }
            catch { }
            return id;
        }

        /// <summary>
        /// Kolon düşeydeki etriye açılımı: kapalı hat KESIT GORUNUS, açık ağız ETRIYE; sol yükseklik, iç alt en,
        /// kanca yazısı ve altında ø/L. Dönüş: açılımın sağ kenarı (sonraki açılım için).
        /// </summary>
        private double CizZimEtriyeAcilim(Transaction tr, BlockTableRecord btr, Database db,
            double x0, double y1, double w, double h, double hook, int capMm, double L)
        {
            double rad = KolonKesitEtriyeRadiusCm;
            const double txtH = 8.0, tagH = 10.0, txtOff = 7.0;
            y1 -= KesitEtriyeAcikAgizLiftCm(w, rad);
            double x1 = x0 + w, y0 = y1 - h;
            AppendKolonKesitEtriyePline(tr, btr, x0, y0, x1, y1, rad, hook, LayerKesitGorunus);
            AppendKolonKesitEtriyeAcikAgiz(tr, btr, x0, y0, x1, y1, rad, hook);

            ObjectId idKol = DrawKesitAcilimDonatiYazi(tr, btr, db, new Point3d(x0 - txtOff, 0.5 * (y0 + y1), 0),
                h.ToString("0", CultureInfo.InvariantCulture), txtH, Math.PI / 2.0);
            SnapTextBoxRightLeftOf(tr, idKol, x0, 4.0);
            ObjectId idAlt = DrawKesitAcilimDonatiYazi(tr, btr, db, new Point3d(0.5 * (x0 + x1), y0 + txtH, 0),
                w.ToString("0", CultureInfo.InvariantCulture), txtH, 0.0);
            SnapTextBoxBottomAbove(tr, idAlt, y0, 4.0);
            PlaceEtriyeKancaYaziRadiusOrtasi(tr, btr, db, x0, y0, x1, y1, rad, hook, txtH);

            string tag = string.Format(CultureInfo.InvariantCulture, "ø{0} L={1:0}", capMm, L);
            ObjectId idTag = DrawKesitAcilimDonatiYazi(tr, btr, db, new Point3d(0.5 * (x0 + x1), y0 - txtOff - 6.0, 0), tag, tagH, 0.0);
            SnapTextBoxTopBelow(tr, idTag, y0, 5.0);
            double sag = x1;
            try
            {
                if (!idTag.IsNull) sag = Math.Max(sag, ((Entity)tr.GetObject(idTag, OpenMode.ForRead)).GeometricExtents.MaxPoint.X);
            }
            catch { }
            return sag;
        }

        private static void ZimCizgi(Transaction tr, BlockTableRecord btr, Point2d a, Point2d b, string layer)
        {
            if (a.GetDistanceTo(b) < 1e-3) return;
            AppendEntity(tr, btr, new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)) { Layer = layer });
        }
    }
}
