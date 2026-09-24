using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Kolon / perde başlığı kesitinde düşey donatı halkaları arası net mesafe ≥ 4 cm.
    /// Sağlanmazsa aynı As ile ST4 yanındaki .BAR dosyasındaki bir büyük çaplara geçilir;
    /// hiçbiri kurtarmazsa kesitteki düşeyler kırmızı çizilir.
    /// </summary>
    public sealed partial class PlanIdDrawingManager
    {
        private const double KolonDuseyMinNetAralikCm = 4.0;
        private int _kolonKesitSonDuseyDiaMm = 14;
        private bool _kolonKesitDonatiKirmizi;

        /// <summary>
        /// En dar yüzde komşu halkalar arası net mesafe (cm). Halka çapı çizimdeki çember ile
        /// gerçek φ'nin büyüğü alınır.
        /// </summary>
        private static double KolonKesitMinNetAralikCm(double Lx, double Ly, int diaMm,
            int top, int bot, int left, int right)
        {
            double halka = Math.Max(diaMm / 10.0, 2.0 * KolonKesitDuseyDonatiRadiusCm);
            double net = double.MaxValue;
            void Face(double L, int extras)
            {
                if (L < 0.01) return;
                double v = L / (extras + 1) - halka;
                if (v < net) net = v;
            }
            Face(Lx, top);
            Face(Lx, bot);
            Face(Ly, left);
            Face(Ly, right);
            return net;
        }

        private static bool KolonDuseyNetAralikYeterli(double Lx, double Ly, int n, int diaMm)
        {
            AllocateKolonKesitFaces(n, diaMm, Lx, Ly, out int top, out int bot, out int left, out int right);
            return KolonKesitMinNetAralikCm(Lx, Ly, diaMm, top, bot, left, right) >= KolonDuseyMinNetAralikCm - 1e-6;
        }

        /// <summary>ST4 ile aynı isimli .BAR: ilk 4 satırdaki sıfır olmayan çaplar.</summary>
        private static List<int> ReadProjeBarCaplari(string st4SourcePath)
        {
            var caps = new SortedSet<int>();
            if (string.IsNullOrEmpty(st4SourcePath)) return caps.ToList();
            string path = Path.ChangeExtension(st4SourcePath, ".BAR");
            if (!File.Exists(path)) return caps.ToList();
            try
            {
                foreach (string line in File.ReadLines(path).Take(4))
                {
                    foreach (string tok in line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)
                            && d >= 6 && d <= 40)
                            caps.Add(d);
                    }
                }
            }
            catch { }
            return caps.ToList();
        }

        /// <summary>
        /// GPR kolon hücresinde düşey donatı net aralığı yetmiyorsa As korunarak .BAR'daki bir büyük
        /// çapa (o da yetmezse daha büyüğüne) geçer. Yalnız dikdörtgen kolon ve perde başlığı.
        /// </summary>
        private void ApplyKolonDuseyNetAralikCapBuyutme(string st4SourcePath, Editor ed)
        {
            if (_kolonDuseyGpr == null || _model?.Floors == null || _model.Columns == null) return;
            var caps = ReadProjeBarCaplari(st4SourcePath);
            double pay = 2.0 * KolonKesitPaspayiCm + 2.0 * KolonKesitEtriyeRadiusCm;
            for (int fi = 0; fi < _model.Floors.Count; fi++)
            {
                var floor = _model.Floors[fi];
                var dims = GetColumnDimensionsForFloor(floor);
                foreach (var col in _model.Columns)
                {
                    if (!HasColumnOnFloor(floor, col)) continue;
                    if (!dims.TryGetValue(col.ColumnNo, out var d) || d.columnType == 2 || d.columnType == 3) continue;
                    if (d.W < 1.0 || d.H < 1.0) continue;
                    if (IsDepremPerdeBoyOrani(Math.Max(d.W, d.H), Math.Min(d.W, d.H))) continue;
                    if (!KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(_kolonDuseyGpr, _model.Floors, fi, col.ColumnNo,
                            out _, out string donati, out _)
                        || !KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(donati, out var byDia)
                        || byDia.Count == 0)
                        continue;

                    int n = byDia.Values.Sum();
                    int dia = byDia.Keys.Max();
                    double asMm2 = byDia.Sum(kv => kv.Value * (double)kv.Key * kv.Key);
                    double Lx = d.W - pay, Ly = d.H - pay;
                    if (Lx < 2.0 || Ly < 2.0 || KolonDuseyNetAralikYeterli(Lx, Ly, n, dia)) continue;

                    string ad = floor.ShortName + " S" + col.ColumnNo.ToString(CultureInfo.InvariantCulture);
                    bool kurtardi = false;
                    foreach (int dNew in caps.Where(c => c > dia))
                    {
                        int nNew = (int)Math.Ceiling(asMm2 / (dNew * (double)dNew) - 1e-9);
                        if (nNew < 4) nNew = 4;
                        if (nNew % 2 != 0) nNew++;
                        if (!KolonDuseyNetAralikYeterli(Lx, Ly, nNew, dNew)) continue;
                        string yeni = nNew.ToString(CultureInfo.InvariantCulture) + "\u00F8"
                            + dNew.ToString(CultureInfo.InvariantCulture);
                        KolonDonatiTableDrawer.TrySetKolonBetonarmeDonati(_kolonDuseyGpr, _model.Floors, fi, col.ColumnNo, yeni);
                        ed?.WriteMessage("\nKOLONDUSEY: {0} {1} net aralik < 4 cm → {2} (As korundu).",
                            ad, KolonDonatiTableDrawer.NormalizeDiameterSymbol(donati).Trim(), yeni);
                        kurtardi = true;
                        break;
                    }
                    if (!kurtardi)
                        ed?.WriteMessage("\nKOLONDUSEY uyari: {0} {1} net aralik < 4 cm; .BAR'da kurtaran cap yok — kesit duseyleri kirmizi.",
                            ad, KolonDonatiTableDrawer.NormalizeDiameterSymbol(donati).Trim());
                }
            }
        }
    }
}
