using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    /// <summary>KOLONREVIZE: GPR yerine çizimden okunan değerlerle kesit kurallarını çalıştırır.</summary>
    public sealed partial class PlanIdDrawingManager
    {
        private int _revizeDuseyAdet;
        private int _revizeDuseyDiaMm;
        private int _revizeEtriyeDiaMm;

        /// <summary>
        /// Kesit (etriye, 2./3. etriye, çiroz, düşey çemberler) + etriye/çiroz açılımı.
        /// Sınırlar çizim birimindedir; olcek = çizim birimi / cm (1:25 → 2).
        /// </summary>
        /// <param name="yuzXs">Görünüşteki uzun yüz donatı X'leri (çizim birimi).</param>
        internal static bool KolonRevizeKesitCiz(
            Transaction tr, BlockTableRecord btr,
            double minX, double minY, double maxX, double maxY, double olcek,
            int adet, int diaMm, int etriyeDiaMm, int etriyeAdet, bool perdeBasligi,
            out List<double> yuzXs)
        {
            yuzXs = new List<double>();
            if (tr == null || btr == null || olcek < 0.5) return false;
            var m = new PlanIdDrawingManager(new St4Model())
            {
                _kolonDuseyOlcek25 = olcek > 1.5,
                _revizeDuseyAdet = adet,
                _revizeDuseyDiaMm = diaMm,
                _revizeEtriyeDiaMm = etriyeDiaMm
            };
            double k = m._kolonDuseyOlcek25 ? 2.0 : 1.0;
            var e = new Envelope(minX / k, maxX / k, minY / k, maxY / k);
            var before = SnapshotKolon50BtrIds(btr);
            var boxes = new List<(double midX, double w, double h)>();
            var stems = new List<(double stem, bool govde)>();
            m.DrawKolonKesitEtriye(tr, btr, null, e, -1, 0, perdeBasligi, boxes, stems);

            double yEtTop = e.MinY - m.KolonDuseyOlcuCizimCm(20.0) - KolonKesitEtriyeAcilimGapCm;
            double yCirozTop = yEtTop;
            if (boxes.Count > 0)
                m.DrawKolonKesitEtriyeAcilim(tr, btr, e, boxes, etriyeDiaMm, etriyeAdet, 10, false, out _, out yCirozTop, yEtTop);
            if (stems.Count > 0)
                m.DrawKolonKesitCirozAcilim(tr, btr, e, stems, etriyeDiaMm, etriyeAdet, false, -1, 0, 0, 0,
                    e.MinX, yCirozTop - KolonKesitEtriyeAcilimAltGapCm);

            double inset = KolonKesitPaspayiCm, rad = KolonKesitEtriyeRadiusCm;
            if (e.Width - 2.0 * inset > 2.0 * rad + 2.0 && e.Height - 2.0 * inset > 2.0 * rad + 2.0)
            {
                m.ResolveKolonKesitBarCounts(e.MinX + inset, e.MinY + inset, e.MaxX - inset, e.MaxY - inset, rad, -1, 0,
                    out var cTl, out var cTr, out _, out _, out int top, out int bot, out _, out _);
                foreach (double x in CollectEdgeCoords(cTl, cTr, System.Math.Max(top, bot), horizontal: true))
                    yuzXs.Add(x * k);
            }

            if (m._kolonDuseyOlcek25)
                m.ApplyKolonDuseyOlcek25(tr, btr.Database, btr, Point3d.Origin, before);
            return true;
        }

        internal static double KolonRevizeLbCm(int diaMm, double fck) => Ts500KenetlenmeLbCm(diaMm, fck, 420.0);

        /// <summary>Orta 1/3 bindirme: kolon alt kotundan başlangıç/bitiş ofsetleri (cm).</summary>
        internal static void KolonRevizeOrtUcBindirme(double hnCm, double lapCm, out double offBot, out double offTop)
        {
            KolonOrtUcdeBindirme(0.0, hnCm, lapCm, out offBot, out offTop);
        }

        internal static bool KolonRevizeEtriyeAdetAralik(double L, int sCode, out int adet, out double sAct) =>
            TryKolonEtriyeAdetAralik(L, sCode, out adet, out sAct);

        internal static double KolonRevizeKesitDonatiRadiusCm => KolonKesitDuseyDonatiRadiusCm;
    }
}
