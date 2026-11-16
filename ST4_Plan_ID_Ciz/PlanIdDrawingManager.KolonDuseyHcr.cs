using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>
        /// GPR kolon betonarme tablosu yokken perde Hcr kat-kolon anahtarları (TBDY 2018 7.6.2.2, STA4 işaretiyle kalibre).
        /// Hcr = max(min(max(ℓw, Hw/6), 2ℓw), taban kat yüksekliği); ℓw perde uzun boyu (poligonda perde yönlerinin büyüğü);
        /// Hw = perde tepe kotu − Hbodrum (ST4 10. satır 6. alan). Bodrum katı (üst ≤ Hbodrum) hep Hcr; diğer kat alt kotu
        /// &lt; Hbodrum + Hcr ise Hcr. Alt kot: COL I/J ile bağlı alttaki perdenin üstü (kat atlayan perde); altında eleman
        /// yoksa ST4 /Columns Data/ 2. satır alt kotu (temelden çıkan çok katlı perde).
        /// 67 GPR'li projede 65'i birebir, 2199 perde-katta eksik 0 / fazla 2 (E_UN_01 SZ-22, a_tok_10_d1 SZ-12).
        /// </summary>
        private HashSet<string> HesaplaKolonDuseyHcrTbdy(string st4Path)
        {
            if (string.IsNullOrEmpty(st4Path) || !File.Exists(st4Path) || _model?.Floors == null) return null;
            string[] l;
            try { l = File.ReadAllLines(st4Path, System.Text.Encoding.ASCII); }
            catch { return null; }
            var inv = CultureInfo.InvariantCulture;
            double D(string s) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, inv, out double d) ? d : 0;
            string F(string[] p, int i) => i < p.Length ? p[i] : null;
            int Bolum(string ad) => Array.FindIndex(l, s => s.StartsWith(ad, StringComparison.OrdinalIgnoreCase));

            double hb = l.Length > 9 ? D(F(l[9].Split(','), 5)) : 0;
            var kot = new List<double>();
            int k0 = Bolum("/Story/");
            if (k0 >= 0)
                for (int i = k0 + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                    kot.Add(D(l[i + 2].Split(',')[0]));
            if (kot.Count < 2) return null;
            double Kot(int k) => k >= 0 && k < kot.Count ? kot[k] : 0;

            // Kolon kayıtları: COL/SMX sırası = /Columns Data/ + /Polygon columns/.
            var no = new List<int>(); var bx = new List<double>(); var by = new List<double>();
            var ustK = new List<double>(); var altK = new List<double>(); var poli = new List<bool>(); var kisa = new List<double>();
            int kc = Bolum("/Columns Data/");
            if (kc >= 0)
                for (int i = kc + 1; i + 2 < l.Length && !l[i].StartsWith("/"); i += 3)
                {
                    var f = l[i].Split(',');
                    int id = (int)Math.Round(D(F(f, 0)));
                    if (id == 0) break;
                    var g = l[i + 1].Split(',');
                    no.Add(id); bx.Add(D(F(f, 1))); by.Add(D(F(f, 2)));
                    ustK.Add(D(F(g, 6))); altK.Add(D(F(g, 7))); poli.Add(false); kisa.Add(0);
                }
            var kesit = new Dictionary<int, List<(double x, double y)>>();
            int ks = Bolum("/Polygon section/");
            if (ks >= 0)
                for (int i = ks + 1; i < l.Length && !l[i].StartsWith("/"); i++)
                {
                    var f = l[i].Split(',');
                    int id = (int)Math.Round(D(F(f, 2)));
                    if (id == 0) continue;
                    if (!kesit.TryGetValue(id, out var p)) kesit[id] = p = new List<(double, double)>();
                    p.Add((D(f[0]), D(F(f, 1))));
                }
            int kp = Bolum("/Polygon columns/");
            if (kp >= 0)
                for (int i = kp + 1; i + 1 < l.Length && !l[i].StartsWith("/"); i += 2)
                {
                    var f = l[i].Split(',');
                    int id = (int)Math.Round(D(F(f, 0)));
                    if (id == 0) break;
                    var g = l[i + 1].Split(',');
                    kesit.TryGetValue((int)Math.Round(D(F(f, 1))), out var p);
                    p = p ?? new List<(double x, double y)>();
                    double kk = double.MaxValue;
                    for (int j = 0; j < p.Count; j++)
                    {
                        var a = p[j]; var b = p[(j + 1) % p.Count];
                        double u = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                        if (u > 0.5) kk = Math.Min(kk, u);
                    }
                    no.Add(id);
                    bx.Add(p.Count > 0 ? Math.Round(p.Max(q => q.x) - p.Min(q => q.x)) : 0);
                    by.Add(p.Count > 0 ? Math.Round(p.Max(q => q.y) - p.Min(q => q.y)) : 0);
                    ustK.Add(D(F(g, 2))); altK.Add(D(F(g, 3))); poli.Add(true); kisa.Add(kk == double.MaxValue ? 0 : kk);
                }
            int n = no.Count;
            if (n == 0) return null;
            int taban = no.Max() / 100 >= Math.Max(kot.Count, 1) ? 1000 : 100;
            int[] kat = no.Select(x => x / taban).ToArray();
            int[] yerel = no.Select(x => x % taban).ToArray();
            bool PerdeX(int r) => kisa[r] > 0 && bx[r] >= 6 * kisa[r];
            bool PerdeY(int r) => kisa[r] > 0 && by[r] >= 6 * kisa[r];
            // Poligon: kollar toplamı ≥ 6 × et (UCB_C_03 SB-53: L 120×110, et 25 → STA Hcr). Dairesel (By = 0) perde değil.
            bool Perde(int r) => poli[r]
                ? kisa[r] > 0 && bx[r] + by[r] >= 6 * kisa[r]
                : Math.Min(bx[r], by[r]) > 0 && Math.Max(bx[r], by[r]) >= 6 * Math.Min(bx[r], by[r]);

            // COL üst/alt düğüm (+0/+2 int16; kayıt V14 900, V15 1124 bayt).
            int[] ni = null, nj = null;
            string colPath = Path.ChangeExtension(st4Path, ".COL");
            try
            {
                if (File.Exists(colPath))
                {
                    byte[] cb = File.ReadAllBytes(colPath);
                    int rec = BeykentOrtak.Sta4KayitDuzeni.Col(cb.Length, st4Path, n).Boy;
                    if (cb.Length / rec >= n)
                    {
                        ni = new int[n]; nj = new int[n];
                        for (int r = 0; r < n; r++) { ni[r] = BitConverter.ToInt16(cb, r * rec); nj[r] = BitConverter.ToInt16(cb, r * rec + 2); }
                    }
                }
            }
            catch { ni = nj = null; }
            var byTop = new Dictionary<int, int>();
            if (ni != null) for (int r = 0; r < n; r++) if (ni[r] > 0) byTop[ni[r]] = r;
            int Below(int r) => ni != null && byTop.TryGetValue(nj[r], out int b) && b != r ? b : -1;

            var floorIdxByNo = new Dictionary<int, int>();
            for (int fi = 0; fi < _model.Floors.Count; fi++)
                if (!floorIdxByNo.ContainsKey(_model.Floors[fi].FloorNo)) floorIdxByNo[_model.Floors[fi].FloorNo] = fi;

            var sonuc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perdeR = Enumerable.Range(0, n).Where(Perde).ToList();
            var perdeSet = new HashSet<int>(perdeR);
            foreach (var grp in perdeR.GroupBy(r => (yerel[r], poli[r])))
            {
                var parca = grp.OrderBy(r => kat[r]).ToList();
                int tb = parca.FirstOrDefault(r => Kot(kat[r]) > hb + 0.01);
                if (!parca.Any(r => Kot(kat[r]) > hb + 0.01)) tb = parca[0];
                double tepe = Kot(kat[parca[parca.Count - 1]]);
                double hw = tepe - hb;
                double tabanKat = Kot(kat[tb]) - Kot(kat[tb] - 1);
                double lw;
                if (poli[tb])
                {
                    var ls = new List<double>();
                    if (PerdeX(tb)) ls.Add(bx[tb] / 100.0);
                    if (PerdeY(tb)) ls.Add(by[tb] / 100.0);
                    lw = ls.Count > 0 ? ls.Max() : Math.Max(bx[tb], by[tb]) / 100.0;
                }
                else lw = Math.Max(bx[tb], by[tb]) / 100.0;
                double hcr = Math.Max(Math.Min(Math.Max(lw, hw / 6.0), 2.0 * lw), tabanKat);
                foreach (int r in parca)
                {
                    double alt = Kot(kat[r] - 1), ust = Kot(kat[r]);
                    if (!poli[r])
                    {
                        int b = Below(r);
                        if (b >= 0 && perdeSet.Contains(b) && Kot(kat[b]) < alt - 0.01) alt = Kot(kat[b]);
                    }
                    bool altBos = Below(r) < 0 && !Enumerable.Range(0, n).Any(c => yerel[c] == yerel[r] && kat[c] == kat[r] - 1);
                    if (altBos && ustK[r] > altK[r] && altK[r] < alt - 0.01 && altK[r] >= -0.05) alt = altK[r];
                    bool bodrum = ust <= hb + 0.01;
                    if (!bodrum && alt >= hb + hcr - 0.01) continue;
                    if (!floorIdxByNo.TryGetValue(kat[r], out int fIdx)) continue;
                    sonuc.Add(KolonDonatiTableDrawer.FormatStoryColumnId(_model.Floors, fIdx, yerel[r]));
                }
            }
            return sonuc;
        }

        /// <summary>GPR'de kolon tablosu yoksa (SMX yedeği / boş) Hcr'yi TBDY kuralıyla doldur.</summary>
        private void TryFillKolonDuseyHcrFromTbdy(string st4SourcePath, Editor ed, bool gprKolonTablosuVar)
        {
            if (gprKolonTablosuVar) return;
            var h = HesaplaKolonDuseyHcrTbdy(st4SourcePath);
            if (h == null) return;
            _kolonDuseyGprHcr = h;
            ed?.WriteMessage("\nKOLONDUSEY: GPR kolon tablosu yok, perde Hcr TBDY 2018 7.6.2.2 ile hesaplandi ({0} kat-perde).", h.Count);
        }
    }
}
