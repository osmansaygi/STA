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
        private const double ZimPaspayCm = 5.0;           // radye
        private const double ZimPaspayMantarCm = 3.0;     // mantar döşeme
        private const int ZimMinEtriyeCapMm = 10;
        private const double ZimMontajYariMaxCm = 11.5;   // etriye eni 25
        private const double ZimMontajYuzMantarCm = 13.0;  // mantar: montaj ile kolon yüzü arası en fazla (büyük kolonda etriye genişler)
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
            public double Paspay = ZimPaspayCm;
            /// <summary>Montaj ucu (ortadan, U): kollu tarafta Ic + kol; kolsuz tarafta kolon yüzü, sınır paspayını aşarsa geri çekilir.</summary>
            public double UcEksi, UcArti;

            public double EtriyeEni => Math.Round(2.0 * (R + ZimEtriyeTasmaCm));
            public double Boy => UcEksi + UcArti;
            public Point2d Nokta(double t, double s) => Orta + U * t + V * s;
            /// <summary>STA4 sayımı: kol boyunca s aralıkla kolon yüzü ve uç dahil floor(kol/s)+1 (100/20 → 6, 100/15 → 7).</summary>
            public static int KolEtriye(double kol, int s, double paspay) => kol < 2.0 * paspay ? 0 : (int)Math.Floor(kol / s + 1e-6) + 1;
            public int Etriye => KolEtriye(KolEksi, AralikCm, Paspay) + KolEtriye(KolArti, AralikCm, Paspay);
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
            _zimEngel = new List<Extents3d>();
            _zimEtiketKutu = new List<Extents3d>();
            _zimPlanYazi = null;
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
                    if (CizZimKolon(tr, btr, db, ed, z, staCap, merkez, hw, hh, aci, h, icTemel, gf, dimStyleId, "TEMELDONATI", "radye"))
                        cizilen++;
                }
                tr.Commit();
            }
            return cizilen;
        }

        /// <summary>Tek kolon: merdivenler, etiketler, ölçüler, etriye açılımı ve komut satırı özeti. Çizildiyse true.</summary>
        private bool CizZimKolon(Transaction tr, BlockTableRecord btr, Database db, Editor ed, GprZimbalamaKolon z, int staCap,
            Point2d merkez, double hw, double hh, double aci, double h, Geometry icSinir, GeometryFactory gf, ObjectId dimStyleId,
            string cmdTag, string elemanAdi, double paspay = ZimPaspayCm, double kolMin = ZimKolBoyuCm, double montajYuzMax = 0.0)
        {
            double etriyeH = Math.Max(10.0, h - 2.0 * paspay);
            List<ZimMerdiven> merdivenler = ZimMerdivenler(merkez, hw, hh, aci, z, icSinir, gf, paspay, kolMin, montajYuzMax);
            if (merdivenler.Count == 0)
            {
                ed?.WriteMessage("\n{0}: {1} {2} icinde merdiven kolu sigmadi.", cmdTag, z.Ad, elemanAdi);
                return false;
            }
            GetRotatedRectBounds(hw, hh, aci, out double kMinX, out double kMaxX, out double kMinY, out double kMaxY);
            double hook = Math.Max(10.0, EtriyeAcilimKancaCm(z.CapMm));

            // Etriye tipleri (en farklıysa ayrı açılım/etiket).
            var tipler = merdivenler.GroupBy(m => m.EtriyeEni)
                .Select(g => (En: g.Key, Adet: g.Sum(m => m.Etriye),
                    L: EtriyeAcilimKesimBoyCm(g.Key, etriyeH, hook)))
                .OrderByDescending(t => t.En).ToList();

            // Etiketler en son: merdiven, kolon, ölçü ve açılım alanları dolu sayılır; yazı bunlara binmez.
            if (_zimEngel == null) _zimEngel = new List<Extents3d>();
            _zimEngel.Add(ZimKutu(merkez.X + kMinX, merkez.Y + kMinY, merkez.X + kMaxX, merkez.Y + kMaxY));
            foreach (ZimMerdiven m in merdivenler)
            {
                CizMerdiven(tr, btr, m);
                _zimEngel.Add(ZimMerdivenKutu(m));
            }
            foreach (ZimMerdiven m in merdivenler)
                CizZimOlcu(tr, btr, db, m, dimStyleId);
            double acilimX = merkez.X + kMaxX + 35.0;
            double acilimX0 = acilimX;
            double acilimUst = Math.Min(merkez.Y + kMinY, ZimYatayAltY(merdivenler, merkez)) - 75.0;
            foreach (var t in tipler)
                acilimX = CizZimEtriyeAcilim(tr, btr, db, acilimX, acilimUst, t.En, etriyeH, hook, z.CapMm, t.L) + 30.0;
            _zimEngel.Add(ZimKutu(acilimX0 - 25.0, acilimUst - etriyeH - 30.0, acilimX - 30.0, acilimUst + 15.0));
            CizZimEtiketler(tr, btr, db, merdivenler, tipler, z, merkez);

            int adet = tipler.Sum(t => t.Adet);
            double saglanan = adet * 2.0 * Math.PI * z.CapMm * z.CapMm / 400.0;
            ed?.WriteMessage("\n  {0}: {1} etriye ø{2}/{3} ({4}), As={5:0.0} cm2 >= Asb={6:0.00} (STA {7}ø{9}), h={8:0} cm.",
                z.Ad, adet, z.CapMm, z.AralikCm,
                string.Join(", ", tipler.Select(t => string.Format(CultureInfo.InvariantCulture, "{0}x{1:0} L={2:0}", t.Adet, t.En, t.L))),
                saglanan, z.AsbCm2, z.Adet, h, staCap);
            return true;
        }

        /// <summary>
        /// MANTARDOSEME zımbalama kopyası: bu kat grubunun GPR "KOLON ZIMBALAMA KONTROLU" donatısı gereken kolonları.
        /// Benzer katlarda aynı kolon birden çok kez geçerse en büyük Asb (eşitse en çok etriye) alınır.
        /// Paspayı 3 cm: kollar ve montaj uçları kat eleman birleşiminin 3 cm içinde kalır; etriye yüksekliği GPR D − 2×3.
        /// Kol boyu GPR kroki "Lk" (yoksa 100 cm); Asb sağlanmazsa 5 cm adımla uzar.
        /// Dönüş: çizilen kolon sayısı.
        /// </summary>
        private int DrawMantarZimbalamaOnCopy(Transaction tr, BlockTableRecord btr, Database db, Editor ed,
            IList<FloorInfo> katlar, IList<GprZimbalamaKolon> tum, double offsetX, double offsetY, Geometry elemUnion)
        {
            List<GprZimbalamaKolon> kolonlar = MantarZimbalamaKatKolonlari(katlar, tum);
            if (kolonlar.Count == 0) return 0;
            FloorInfo floor = katlar[0];
            var gf = _ntsDrawFactory ?? NtsGeometryServices.Instance.CreateGeometryFactory();
            Geometry icSinir = null;
            try
            {
                if (elemUnion != null && !elemUnion.IsEmpty)
                    icSinir = NetTopologySuite.Geometries.Utilities.AffineTransformation
                        .TranslationInstance(offsetX, offsetY).Transform(elemUnion).Buffer(-ZimPaspayMantarCm);
            }
            catch { icSinir = null; }
            _zimEngel = new List<Extents3d>();
            _zimEtiketKutu = new List<Extents3d>();
            _zimPlanYazi = null;
            try
            {
                if (elemUnion != null && !elemUnion.IsEmpty)
                {
                    Envelope env = elemUnion.EnvelopeInternal;
                    _zimPlanYazi = ZimPlanYazilari(tr, btr, ZimKutu(env.MinX + offsetX - 100.0, env.MinY + offsetY - 100.0,
                        env.MaxX + offsetX + 100.0, env.MaxY + offsetY + 100.0));
                    _zimEngel.AddRange(_zimPlanYazi);
                }
            }
            catch { _zimPlanYazi = null; }

            EnsurePlanLayer(tr, db, LayerDonatiGovde, 4, LineWeight.LineWeight040, useDashed: false);
            EnsurePlanLayer(tr, db, LayerEtriye, 150, LineWeight.LineWeight035, useDashed: false);
            EnsurePlanLayer(tr, db, LayerKesitGorunus, 253, LineWeight.LineWeight020, useDashed: true);
            EnsurePlanLayer(tr, db, LayerDonatiYazisiPerde, 3, LineWeight.LineWeight020, useDashed: false);
            EnsurePlanLayer(tr, db, LayerEtiketCizgisi, 64, LineWeight.LineWeight015, useDashed: false);
            EnsurePlanLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, useDashed: false);
            ObjectId dimStyleId = ZimOlcuStil(tr, db);

            int cizilen = 0;
            foreach (GprZimbalamaKolon z in kolonlar)
            {
                int staCap = z.CapMm;
                if (z.CapMm < ZimMinEtriyeCapMm) z.CapMm = ZimMinEtriyeCapMm;
                if (!TryMantarZimKolon(floor, z.KolonNo, offsetX, offsetY, out Point2d merkez, out double hw, out double hh, out double aci))
                {
                    ed?.WriteMessage("\nMANTARDOSEME: {0} zimbalama kolonu {1} katinda bulunamadi, atlandi.", z.Ad, floor.Name);
                    continue;
                }
                double h = z.KalinlikCm > 1.0 ? z.KalinlikCm : (z.DCm > 0 ? z.DCm + 3.0 : 25.0);
                double kolMin = z.LkCm >= 2.0 * ZimPaspayMantarCm + 10.0 ? z.LkCm : ZimKolBoyuCm;
                if (CizZimKolon(tr, btr, db, ed, z, staCap, merkez, hw, hh, aci, h, icSinir, gf, dimStyleId, "MANTARDOSEME", "doseme",
                        ZimPaspayMantarCm, kolMin, ZimMontajYuzMantarCm))
                    cizilen++;
            }
            return cizilen;
        }

        /// <summary>Zımbalama kopyası başlığı: kat başlığının üstünde (TEMELDONATI kopya başlığı gibi).</summary>
        private void DrawMantarZimbalamaCopyTitle(Transaction tr, BlockTableRecord btr, double offsetX, double offsetY,
            (double Xmin, double Xmax, double Ymin, double Ymax) ext, bool benzerKatVar)
        {
            var pos = new Point3d(offsetX + (ext.Xmin + ext.Xmax) / 2.0, offsetY + ext.Ymax + 85.0 + (benzerKatVar ? 10.0 : 0.0), 0);
            AppendEntity(tr, btr, MakeCenteredText(tr, btr.Database, LayerBaslik, 20 * TemelFoundationAnnotMul, "ZIMBALAMA DONATISI", pos));
        }

        /// <summary>Kat grubundaki katların (kısa adına göre) zımbalama kolonları; aynı kolonda en elverişsizi.</summary>
        internal static List<GprZimbalamaKolon> MantarZimbalamaKatKolonlari(IList<FloorInfo> katlar, IList<GprZimbalamaKolon> tum)
        {
            var sonuc = new List<GprZimbalamaKolon>();
            if (katlar == null || tum == null || tum.Count == 0) return sonuc;
            string Norm(string s) => (s ?? string.Empty).Trim().TrimEnd('-').Trim();
            var adlar = new HashSet<string>(katlar.Where(f => f != null).Select(f => Norm(f.ShortName)).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            foreach (var g in tum.Where(z => adlar.Contains(Norm(z.KatKisa))).GroupBy(z => z.KolonNo))
            {
                GprZimbalamaKolon s = g.OrderByDescending(z => z.AsbCm2).ThenByDescending(z => z.Adet).First();
                // Çizim sırasında çap en az ø10'a çekilir; GPR kaydı paylaşılmasın.
                sonuc.Add(new GprZimbalamaKolon
                {
                    Ad = s.Ad, Numara = s.Numara, DCm = s.DCm, Adet = s.Adet, CapMm = s.CapMm, AralikCm = s.AralikCm,
                    AsbCm2 = s.AsbCm2, UpSaglanan = s.UpSaglanan, UpGerekli = s.UpGerekli, KatKisa = s.KatKisa,
                    KolonNo = s.KolonNo, KalinlikCm = s.KalinlikCm, LkCm = s.LkCm
                });
            }
            return sonuc.OrderBy(z => z.KolonNo).ToList();
        }

        /// <summary>Kattaki kolon (kalıp planı DrawColumns ile aynı konum); kolon o katta yoksa false.</summary>
        private bool TryMantarZimKolon(FloorInfo floor, int colNo, double offsetX, double offsetY,
            out Point2d merkez, out double hw, out double hh, out double aci)
        {
            merkez = Point2d.Origin;
            hw = hh = aci = 0;
            foreach (ColumnAxisInfo col in _model.Columns)
            {
                if (col.ColumnNo != colNo) continue;
                if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                int sectionId = ResolveColumnSectionId(floor.FloorNo, col.ColumnNo);
                if (sectionId <= 0 || !_model.ColumnDimsBySectionId.ContainsKey(sectionId)) continue;
                var dim = _model.ColumnDimsBySectionId[sectionId];
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
                merkez = new Point2d(axisNode.X + offsetGlobal.X + offsetX, axisNode.Y + offsetGlobal.Y + offsetY);
                aci = col.AngleDeg;
                return true;
            }
            return false;
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
        private static List<ZimMerdiven> ZimMerdivenler(Point2d merkez, double hw, double hh, double aci, GprZimbalamaKolon z, Geometry icTemel, GeometryFactory gf,
            double paspay = ZimPaspayCm, double kolMin = ZimKolBoyuCm, double montajYuzMax = 0.0)
        {
            Vector2d ux = Rotate(new Vector2d(1, 0), aci);
            Vector2d uy = Rotate(new Vector2d(0, 1), aci);
            var list = new List<ZimMerdiven>();
            // ic: ortadan kolon yüzüne (U); dik: kolonun V yarı boyu; kay: ortanın V kayması.
            void Ekle(Vector2d u, Vector2d v, double ic, double dik, double kay, int kenar)
            {
                double vMin = -dik - kay, vMax = dik - kay;
                // montajYuzMax > 0 (mantar): büyük kolonda montaj kolon yüzüne en fazla bu kadar uzak; etriye eni buna göre büyür.
                double yari = Math.Min(vMax, -vMin);
                double rMax = montajYuzMax > 0 ? Math.Max(ZimMontajYariMaxCm, yari - montajYuzMax) : ZimMontajYariMaxCm;
                double r = Math.Min(rMax, yari - paspay);
                if (r < ZimMontajYariMinCm) r = ZimMontajYariMinCm;
                list.Add(new ZimMerdiven
                {
                    U = u, V = v, Orta = merkez + v * kay, Ic = ic, VMin = vMin, VMax = vMax, R = r, Kenar = kenar,
                    CapMm = z.CapMm, AralikCm = z.AralikCm, Paspay = paspay
                });
            }
            // Uç merdiveni montajı kolon ucundan 5 cm içeride: kayma = yarı boy − 5 − r.
            double UcKay(double yari) => yari - paspay - ZimMontajYariMaxCm;

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
            for (double hedef = kolMin; ; hedef += 5.0)
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
            foreach (ZimMerdiven m in list)
            {
                m.UcEksi = ZimMontajUcu(m, -1, icTemel, gf);
                m.UcArti = ZimMontajUcu(m, +1, icTemel, gf);
            }
            return list;
        }

        /// <summary>
        /// Montaj ucu (ortadan |t|): kollu tarafta Ic + kol. Kolsuz tarafta kolon yüzü; kolon sınıra (kenar/döşeme ucu)
        /// paspayından yakınsa montaj iki çizgisi sınırın paspayı içinde kalana dek 1 cm adımla geri çekilir.
        /// </summary>
        private static double ZimMontajUcu(ZimMerdiven m, int yon, Geometry icTemel, GeometryFactory gf)
        {
            double kol = m.Kol(yon);
            if (kol > 0) return m.Ic + kol;
            if (icTemel == null || icTemel.IsEmpty) return m.Ic;
            for (double t = Math.Floor(m.Ic); t > 0; t -= 1.0)
            {
                Point2d p1 = m.Nokta(yon * t, -m.R), p2 = m.Nokta(yon * t, m.R);
                try
                {
                    if (icTemel.Covers(gf.CreatePoint(new Coordinate(p1.X, p1.Y))) &&
                        icTemel.Covers(gf.CreatePoint(new Coordinate(p2.X, p2.Y))))
                        return t == Math.Floor(m.Ic) ? m.Ic : t;
                }
                catch { return m.Ic; }
            }
            return 0;
        }

        /// <summary>Kol ucu (etriye köşeleri) radye paspayı içinde kalacak en uzun 5 cm katı kol; sığmazsa 0.</summary>
        private static double ZimKolSigdir(ZimMerdiven m, int yon, double hedef, Geometry icTemel, GeometryFactory gf)
        {
            if (icTemel == null || icTemel.IsEmpty) return hedef;
            double b2 = m.EtriyeEni / 2.0;
            for (double kol = hedef; kol >= 2.0 * m.Paspay + 10.0; kol -= 5.0)
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
            int n = ZimMerdiven.KolEtriye(kol, m.AralikCm, m.Paspay);
            double adim = n > 1 ? (kol - 2.0 * m.Paspay) / (n - 1) : 0.0;
            for (int k = 0; k < n; k++)
                list.Add(yon * (m.Ic + m.Paspay + k * adim));
            return list;
        }

        /// <summary>Montaj (DONATI) iki çizgi; etriyeler (ETRIYE) montajın 1 cm dışına taşar.</summary>
        private void CizMerdiven(Transaction tr, BlockTableRecord btr, ZimMerdiven m)
        {
            double t0 = -m.UcEksi, t1 = m.UcArti;
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
                if (dus.Kol(ust) <= 0) ust = -ust; // üst kol sınırda düştüyse alt kola yaz
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
                    if (ust * tm < dus.Ic + dus.Paspay) tm = konum[ie] - ust * 0.5 * adim;
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
                // Etiket kolonun solundaki kolda ise sola kırılır (yazı kolon/düşey merdiven üstüne taşmaz).
                int yatay = m.Nokta(sag * (m.Ic + 0.5 * kol), 0).X >= merkez.X - 1.0 ? 1 : -1;
                List<double> konum = ZimEtriyeKonumlari(m, sag);
                // İki yerleşim denenir, toplam çakışması az olan çizilir:
                // 0: montaj kolun dış ucunda, etriye kolona yakın; 1: montaj kol ortasında, etriye dış uçta.
                int CizYerlesim(int y, List<ObjectId> izler)
                {
                    double tm = m.Ic + (y == 0 ? 0.8 : 0.55) * kol;
                    // Asıl yönde yer yoksa merdivenin karşı yüzünden ters yöne yazılabilir.
                    int puan = ZimRafYazi(tr, btr, db, m.Nokta(sag * tm, dikV * m.R), egim * 22.0, MontajYazi(m), yatay,
                        m.Nokta(sag * tm, -dikV * m.R), izler);
                    if (konum.Count > 0)
                    {
                        double t = y == 0 ? konum[Math.Min(konum.Count - 1, 1)] : konum[Math.Max(0, konum.Count - 2)];
                        puan += ZimRafYazi(tr, btr, db, m.Nokta(t, dikV * m.EtriyeEni / 2.0), egim * 52.0, EtriyeYazi(m), yatay,
                            m.Nokta(t, -dikV * m.EtriyeEni / 2.0), izler);
                    }
                    return puan;
                }
                int enIyi = 0, enIyiPuan = int.MaxValue;
                for (int y = 0; y < 2; y++)
                {
                    int nEngel = _zimEngel.Count, nEtiket = _zimEtiketKutu?.Count ?? 0;
                    var izler = new List<ObjectId>();
                    int puan = CizYerlesim(y, izler);
                    foreach (ObjectId id in izler)
                        try { tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                    _zimEngel.RemoveRange(nEngel, _zimEngel.Count - nEngel);
                    if (_zimEtiketKutu != null) _zimEtiketKutu.RemoveRange(nEtiket, _zimEtiketKutu.Count - nEtiket);
                    if (puan < enIyiPuan) { enIyiPuan = puan; enIyi = y; }
                }
                CizYerlesim(enIyi, null);
            }
        }

        private static string MontajYazi(ZimMerdiven m) =>
            string.Format(CultureInfo.InvariantCulture, "{0} L={1}", ZimMontajYazi, ZimMontajBoy(m));

        /// <summary>Dolu nokta (sıfır iç çaplı halka): kılavuzun donatıya değdiği yer.</summary>
        private static ObjectId ZimNokta(Transaction tr, BlockTableRecord btr, Point2d p, double r)
        {
            var pl = new Polyline { Layer = LayerEtiketCizgisi };
            pl.AddVertexAt(0, new Point2d(p.X - r / 2.0, p.Y), 1.0, r, r);
            pl.AddVertexAt(1, new Point2d(p.X + r / 2.0, p.Y), 1.0, r, r);
            pl.Closed = true;
            return AppendEntityReturnId(tr, btr, pl);
        }

        /// <summary>
        /// Donatıdan rafa etiket. yukseklik 0: donatı noktasından doğrudan yatay raf; değilse 45° kırılıp raf.
        /// yatay +1: raf sağa, yazı sola dayalı; −1: raf sola, yazı sağa dayalı. Yazı rafın üstünde, raf yazı boyunca.
        /// Yazı+raf kutusu önceki yazı, merdiven, kolon, ölçü, açılım veya plan yazısına binerse ya da kılavuz bir yazının
        /// üstünden geçerse kırık yüksekliği adım adım artırılır; karsiDonati verilirse her adımda merdivenin karşı
        /// yüzünden ters yön de denenir. Boş yer yoksa en az çakışan deneme çizilir.
        /// </summary>
        private int ZimRafYazi(Transaction tr, BlockTableRecord btr, Database db, Point2d donatiAsil, double yukseklik, string yazi, int yatay = 1,
            Point2d? karsiDonati = null, List<ObjectId> izler = null)
        {
            if (_zimEngel == null) _zimEngel = new List<Extents3d>();
            double yaziH = ZimYaziCm * TemelFoundationAnnotMul;
            double adim = yaziH + 6.0;
            int isaret = yukseklik < 0 ? -1 : 1;
            int sx = yatay < 0 ? -1 : 1;
            int deneme = karsiDonati.HasValue ? 16 : 10;

            // ciz=false: yalnız çakışma puanı (yazı silinir); ciz=true: çizer ve engellere ekler.
            int Dene(int i, bool ciz)
            {
                int k = karsiDonati.HasValue ? i / 2 : i;
                bool ters = karsiDonati.HasValue && i % 2 == 1;
                Point2d donati = ters ? karsiDonati.Value : donatiAsil;
                double y = (ters ? -1 : 1) * (yukseklik + isaret * k * adim);
                bool kirikli = Math.Abs(y) > 1e-6;
                Point2d kirik = new Point2d(donati.X + sx * Math.Abs(y), donati.Y + y);
                double bosluk = kirikli ? 4.0 : 18.0;
                var t = new DBText
                {
                    Layer = LayerDonatiYazisiPerde,
                    Height = yaziH,
                    TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, db),
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(yazi),
                    Position = new Point3d(kirik.X + sx * bosluk, kirik.Y + 2.5, 0)
                };
                if (sx < 0)
                {
                    t.HorizontalMode = TextHorizontalMode.TextRight;
                    t.AlignmentPoint = new Point3d(kirik.X - bosluk, kirik.Y + 2.5, 0);
                }
                ObjectId id = AppendEntityReturnId(tr, btr, t);
                double tahmin = yazi.Length * yaziH * 0.6;
                double x0 = sx > 0 ? kirik.X + bosluk : kirik.X - bosluk - tahmin;
                double x1 = sx > 0 ? kirik.X + bosluk + tahmin : kirik.X - bosluk;
                double yUst = kirik.Y + 2.5 + yaziH;
                try
                {
                    if (!id.IsNull)
                    {
                        var ent = (DBText)tr.GetObject(id, OpenMode.ForWrite);
                        if (sx < 0) ent.AdjustAlignment(db);
                        Extents3d ex = ent.GeometricExtents;
                        x0 = ex.MinPoint.X;
                        x1 = ex.MaxPoint.X;
                        yUst = ex.MaxPoint.Y;
                    }
                }
                catch { }
                // Kutu kırık noktasının 1,5 cm ötesinden başlar (rafın değdiği kendi merdiveni çakışma sayılmaz).
                Extents3d kutu = sx > 0
                    ? ZimKutu(kirik.X + 1.5, kirik.Y - 1.0, x1 + 3.0, yUst + 1.0)
                    : ZimKutu(x0 - 3.0, kirik.Y - 1.0, kirik.X - 1.5, yUst + 1.0);
                if (!ciz)
                {
                    int puan = _zimEngel.Count(e => ZimKesisir(e, kutu)) * 10;
                    if (kirikli)
                        puan += ZimKilavuzNoktalari(donati, kirik).Count(p =>
                            (_zimEtiketKutu != null && _zimEtiketKutu.Any(e => ZimIcinde(e, p))) ||
                            (_zimPlanYazi != null && _zimPlanYazi.Any(e => ZimIcinde(e, p))));
                    try { if (!id.IsNull) tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                    return puan;
                }
                ObjectId idNokta = ZimNokta(tr, btr, donati, 2.4);
                izler?.Add(idNokta);
                izler?.Add(id);
                if (kirikli)
                {
                    ObjectId idKilavuz = ZimCizgi(tr, btr, donati, kirik, LayerEtiketCizgisi);
                    izler?.Add(idKilavuz);
                }
                ObjectId idRaf = ZimCizgi(tr, btr, kirik, new Point2d(sx > 0 ? x1 + 3.0 : x0 - 3.0, kirik.Y), LayerEtiketCizgisi);
                izler?.Add(idRaf);
                _zimEngel.Add(kutu);
                (_zimEtiketKutu ??= new List<Extents3d>()).Add(kutu);
                // Kılavuz boyunca küçük kutular: sonraki etiket yazıları kılavuzun üstüne gelmesin.
                if (kirikli)
                    foreach (Point2d p in ZimKilavuzNoktalari(donati, kirik).Skip(2))
                        _zimEngel.Add(ZimKutu(p.X - 1.5, p.Y - 1.5, p.X + 1.5, p.Y + 1.5));
                return 0;
            }

            int enIyi = 0, enIyiPuan = int.MaxValue;
            for (int i = 0; i < deneme; i++)
            {
                int puan = Dene(i, false);
                if (puan < enIyiPuan) { enIyiPuan = puan; enIyi = i; }
                if (puan == 0) break;
            }
            Dene(enIyi, true);
            // Puan: çakışma ağırlıklı, eşitlikte kısa kılavuz (küçük deneme sırası) tercih.
            return enIyiPuan * 100 + enIyi;
        }

        /// <summary>Etiket çakışma kontrolü için dolu alanlar (yazı, merdiven, kolon, ölçü, açılım); kopya/komut başında sıfırlanır.</summary>
        private List<Extents3d> _zimEngel;
        /// <summary>Yalnız etiket yazı+raf kutuları (kılavuz çizgisi kontrolü için).</summary>
        private List<Extents3d> _zimEtiketKutu;

        /// <summary>Kılavuz üzerinde 3 cm aralıklı noktalar (başlangıç dahil).</summary>
        private static IEnumerable<Point2d> ZimKilavuzNoktalari(Point2d a, Point2d b)
        {
            double L = a.GetDistanceTo(b);
            int n = Math.Max(1, (int)Math.Ceiling(L / 3.0));
            for (int i = 0; i <= n; i++)
                yield return a + (b - a) * (i / (double)n);
        }

        private static bool ZimIcinde(Extents3d e, Point2d p) =>
            p.X > e.MinPoint.X && p.X < e.MaxPoint.X && p.Y > e.MinPoint.Y && p.Y < e.MaxPoint.Y;

        private static Extents3d ZimKutu(double x0, double y0, double x1, double y1) =>
            new Extents3d(new Point3d(Math.Min(x0, x1), Math.Min(y0, y1), 0), new Point3d(Math.Max(x0, x1), Math.Max(y0, y1), 0));

        private static bool ZimKesisir(Extents3d a, Extents3d b) =>
            a.MinPoint.X < b.MaxPoint.X && b.MinPoint.X < a.MaxPoint.X && a.MinPoint.Y < b.MaxPoint.Y && b.MinPoint.Y < a.MaxPoint.Y;

        /// <summary>Merdivenin montaj uçları ve etriye eni kadar dörtgeni (dünya eksenli sınır kutusu).</summary>
        private static Extents3d ZimMerdivenKutu(ZimMerdiven m)
        {
            double e = m.EtriyeEni / 2.0;
            Point2d a = m.Nokta(-m.UcEksi, -e), b = m.Nokta(-m.UcEksi, e), c = m.Nokta(m.UcArti, -e), d = m.Nokta(m.UcArti, e);
            return ZimKutu(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
                Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)), Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
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
            int yan0;
            if (m.Kenar != 0) yan0 = -m.Kenar;
            else if (m.Yatay) yan0 = m.V.Y <= 0 ? 1 : -1;
            else yan0 = m.V.X <= 0 ? 1 : -1;
            // Ölçü plan yazısına (kolon adı vb.) binerse, uç merdiveni değilse karşı yana alınır.
            int deneme = m.Kenar != 0 || _zimPlanYazi == null || _zimPlanYazi.Count == 0 ? 1 : 2;
            for (int d = 0; d < deneme; d++)
            {
                int yan = d == 0 ? yan0 : -yan0;
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
                Extents3d? ex = null;
                try
                {
                    dim.RecomputeDimensionBlock(true);
                    ex = dim.GeometricExtents;
                }
                catch { }
                if (d + 1 < deneme && ex.HasValue && _zimPlanYazi.Any(p => ZimKesisir(p, ex.Value)))
                {
                    try { dim.Erase(); } catch { }
                    continue;
                }
                if (ex.HasValue) _zimEngel?.Add(ex.Value);
                return;
            }
        }

        /// <summary>Plan yazıları (kolon/döşeme/kiriş adı, kot vb.): ölçü yan seçimi ve etiket çakışması için.</summary>
        private List<Extents3d> _zimPlanYazi;

        /// <summary>Verilen alandaki model uzayı DBText/MText kutuları (zımbalama çizimi öncesi plan yazıları).</summary>
        private static List<Extents3d> ZimPlanYazilari(Transaction tr, BlockTableRecord btr, Extents3d alan)
        {
            var list = new List<Extents3d>();
            foreach (ObjectId id in btr)
            {
                if (!id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(DBText))) &&
                    !id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MText)))) continue;
                try
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    Extents3d x = e.GeometricExtents;
                    if (ZimKesisir(x, alan)) list.Add(x);
                }
                catch { }
            }
            return list;
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

        private static ObjectId ZimCizgi(Transaction tr, BlockTableRecord btr, Point2d a, Point2d b, string layer)
        {
            if (a.GetDistanceTo(b) < 1e-3) return ObjectId.Null;
            return AppendEntityReturnId(tr, btr, new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)) { Layer = layer });
        }
    }
}
