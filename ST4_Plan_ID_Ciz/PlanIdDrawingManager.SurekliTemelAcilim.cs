using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>Sürekli temel açılımı: kiriş yolu; montaj alt yüz, düz üst yüz.</summary>
        private bool _surekliTemelAcilim;
        private List<BeamInfo> _surekliTemelBeams;
        private Dictionary<BeamInfo, ContinuousFoundationInfo> _surekliTemelCfByBeam;
        /// <summary>GPR+TMX sırasıyla her kolon-arası (bay) donatı kaydı.</summary>
        private List<SurekliTemelSpanKayit> _surekliTemelSpanKayitlar;

        private sealed class SurekliTemelSpanKayit
        {
            public int TNo;
            public double L_m;
            public bool UcStub;
            public KirisAcilimMontajBilgi Bilgi;
        }

        /// <summary>Kirişte montaj üstte; sürekli temelde montaj altta.</summary>
        private bool KirisAcilimMontajUstYuz => !_surekliTemelAcilim;

        /// <summary>Açılım şeridi: temelde montaj aşağıda (kirişin tersi), düz yukarıda.</summary>
        private double SurekliAcilimMontajAsagiCm =>
            _surekliTemelAcilim ? KirisAcilimDuzAcilimCm : KirisAcilimMontajAcilimCm;

        private double SurekliAcilimDuzAsagiCm =>
            _surekliTemelAcilim ? KirisAcilimMontajAcilimCm : KirisAcilimDuzAcilimCm;

        /// <summary>Temelde üst ilave (düz yüz) yukarı şerit; alt ilave (montaj yüz) aşağı şerit.</summary>
        private double SurekliAcilimIlaveAsagiCm(bool ustYuz) =>
            !_surekliTemelAcilim
                ? (ustYuz ? KirisAcilimIlaveAcilimCm : KirisAcilimAltIlaveAcilimCm)
                : (ustYuz ? KirisAcilimIlaveAcilimCm : KirisAcilimAltIlaveAcilimCm);

        internal bool DrawSurekliTemelAcilimlariSecenekli(
            Database db, Editor ed, Point3d insert, string path, KirisAcilimSecenekleri options)
        {
            if (_model?.ContinuousFoundations == null || _model.ContinuousFoundations.Count == 0)
            {
                ed?.WriteMessage("\nSUREKLITEMELACILIMLARI: Surekli temel yok.");
                return false;
            }

            _surekliTemelAcilim = true;
            try
            {
                _surekliTemelBeams = BuildSurekliTemelSyntheticBeams(out _surekliTemelCfByBeam);
                // Onaylı beton/kolon geometrisi korunur; donatı bu istasyonlara bağlanır.
                return DrawSurekliTemelGorunusleri(db, ed, insert, path, options);
            }
            finally
            {
                _surekliTemelAcilim = false;
                _surekliTemelBeams = null;
                _surekliTemelCfByBeam = null;
                _surekliTemelSpanKayitlar = null;
            }
        }

        private List<BeamInfo> BuildSurekliTemelSyntheticBeams(
            out Dictionary<BeamInfo, ContinuousFoundationInfo> map)
        {
            map = new Dictionary<BeamInfo, ContinuousFoundationInfo>();
            var list = new List<BeamInfo>();
            FloorInfo floor = SurekliTemelDestekKati();
            int floorNo = floor?.FloorNo ?? 0;
            int step = _model.BeamFloorKeyStep > 0 ? _model.BeamFloorKeyStep : 1000;
            for (int i = 0; i < _model.ContinuousFoundations.Count; i++)
            {
                ContinuousFoundationInfo cf = _model.ContinuousFoundations[i];
                if (cf == null || cf.HeightCm <= 1.0 || cf.WidthCm <= 1.0) continue;
                int no = GetContinuousFoundationLabelNumber(cf, i);
                if (no < 1) no = i + 1;
                // GetBeamFloorNo / GetBeamNumero ile uyumlu kimlik.
                int beamId = floorNo * step + (no % step);
                if (beamId == floorNo * step) beamId += no;
                double topKotCm = cf.BottomKotBinaGoreCm + cf.HeightCm;
                if (floor != null)
                    topKotCm -= floor.ElevationM * 100.0;
                var beam = new BeamInfo
                {
                    BeamId = beamId,
                    FixedAxisId = cf.FixedAxisId,
                    StartAxisId = cf.StartAxisId,
                    EndAxisId = cf.EndAxisId,
                    WidthCm = cf.WidthCm,
                    HeightCm = cf.HeightCm,
                    OffsetRaw = cf.OffsetRaw,
                    Point1KotCm = topKotCm,
                    Point2KotCm = topKotCm,
                    IsWallFlag = 0
                };
                list.Add(beam);
                map[beam] = cf;
            }
            return list;
        }

        private FloorInfo SurekliTemelDestekKati()
        {
            FloorInfo best = null;
            if (_model?.Floors == null) return null;
            foreach (var f in _model.Floors)
            {
                if (f == null) continue;
                if (best == null || f.ElevationM < best.ElevationM) best = f;
            }
            return best;
        }

        private IEnumerable<BeamInfo> SurekliTemelAcilimBeamKaynagi(int floorNo)
        {
            if (_surekliTemelBeams == null) yield break;
            FloorInfo dest = SurekliTemelDestekKati();
            int want = dest?.FloorNo ?? floorNo;
            if (floorNo != want) yield break;
            foreach (var b in _surekliTemelBeams)
                yield return b;
        }

        /// <summary>
        /// Önce CF düzeyinde yedek donatı; asıl dağıtım <see cref="SurekliTemelChainsBayDonatiAta"/> ile
        /// her kolon arası (TMX/GPR span) ayrı BeamInfo anahtarına yazılır.
        /// </summary>
        private Dictionary<BeamInfo, KirisAcilimMontajBilgi> SurekliTemelReadDonati(string st4Path)
        {
            var map = new Dictionary<BeamInfo, KirisAcilimMontajBilgi>();
            _surekliTemelSpanKayitlar = SurekliTemelLoadSpanKayitlar(st4Path);
            if (_surekliTemelBeams == null) return map;

            // Tn başına ilk anlamlı (en uzun) span → CF yedek.
            var byT = new Dictionary<int, KirisAcilimMontajBilgi>();
            if (_surekliTemelSpanKayitlar != null)
            {
                foreach (var g in GroupSpansByT(_surekliTemelSpanKayitlar))
                {
                    SurekliTemelSpanKayit best = null;
                    foreach (var s in g.Value)
                    {
                        if (s.Bilgi.Adet <= 0 && s.Bilgi.DuzAdet <= 0) continue;
                        if (best == null || s.L_m > best.L_m) best = s;
                    }
                    if (best != null) byT[g.Key] = best.Bilgi;
                }
            }

            for (int i = 0; i < _surekliTemelBeams.Count; i++)
            {
                BeamInfo beam = _surekliTemelBeams[i];
                int no = GetBeamNumero(beam.BeamId);
                if (byT.TryGetValue(no, out KirisAcilimMontajBilgi bil))
                    map[beam] = bil;
            }
            return map;
        }

        private static Dictionary<int, List<SurekliTemelSpanKayit>> GroupSpansByT(List<SurekliTemelSpanKayit> all)
        {
            var map = new Dictionary<int, List<SurekliTemelSpanKayit>>();
            if (all == null) return map;
            foreach (var s in all)
            {
                if (s == null || s.TNo < 1) continue;
                if (!map.TryGetValue(s.TNo, out var list))
                {
                    list = new List<SurekliTemelSpanKayit>();
                    map[s.TNo] = list;
                }
                list.Add(s);
            }
            return map;
        }

        /// <summary>
        /// Ara kolonlarla bölünmüş zincirde her açıklığa TMX/GPR span donatısını verir;
        /// her bay ayrı BeamInfo (kiriş gibi) olur.
        /// </summary>
        private void SurekliTemelChainsBayDonatiAta(
            List<List<KirisAcilimSpan>> chains,
            Dictionary<BeamInfo, KirisAcilimMontajBilgi> montaj)
        {
            if (chains == null || montaj == null || _surekliTemelSpanKayitlar == null) return;
            var byT = GroupSpansByT(_surekliTemelSpanKayitlar);
            foreach (var chain in chains)
            {
                if (chain == null || chain.Count == 0) continue;
                BeamInfo parent = chain[0]?.Beam;
                if (parent == null) continue;
                int tNo = GetBeamNumero(parent.BeamId);
                if (!byT.TryGetValue(tNo, out var kayitlar) || kayitlar.Count == 0) continue;

                var assigned = SurekliTemelMatchBayKayitlar(chain, kayitlar);
                ContinuousFoundationInfo cf = null;
                _surekliTemelCfByBeam?.TryGetValue(parent, out cf);

                for (int i = 0; i < chain.Count; i++)
                {
                    var span = chain[i];
                    if (span == null) continue;
                    KirisAcilimMontajBilgi bil = assigned[i];
                    if (bil.Adet <= 0 && bil.DuzAdet <= 0 && montaj.TryGetValue(parent, out var yedek))
                        bil = yedek;

                    var bay = CloneSurekliTemelBayBeam(parent);
                    span.Beam = bay;
                    montaj[bay] = bil;
                    if (cf != null) _surekliTemelCfByBeam[bay] = cf;
                }
            }
        }

        private static BeamInfo CloneSurekliTemelBayBeam(BeamInfo parent)
        {
            return new BeamInfo
            {
                // Aynı Tn etiketi; sözlük anahtarı nesne referansı.
                BeamId = parent.BeamId,
                FixedAxisId = parent.FixedAxisId,
                StartAxisId = parent.StartAxisId,
                EndAxisId = parent.EndAxisId,
                WidthCm = parent.WidthCm,
                HeightCm = parent.HeightCm,
                OffsetRaw = parent.OffsetRaw,
                Point1KotCm = parent.Point1KotCm,
                Point2KotCm = parent.Point2KotCm,
                IsWallFlag = parent.IsWallFlag
            };
        }

        /// <summary>Zincir açıklıklarını GPR/TMX L sırasıyla eşler (eşit sayıda 1:1, değilse L yakınlığı).</summary>
        private static KirisAcilimMontajBilgi[] SurekliTemelMatchBayKayitlar(
            List<KirisAcilimSpan> chain, List<SurekliTemelSpanKayit> kayitlar)
        {
            int n = chain.Count;
            var result = new KirisAcilimMontajBilgi[n];
            if (kayitlar == null || kayitlar.Count == 0) return result;
            // Uzun/kaçıklıklı kolonun merkezinden ölçülen mesafe, analiz düğümü
            // mesafesi değildir. Uç uzatma kayıtlarını çıkarıp iç açıklık sırasını
            // koru; T23 gibi uzun kolonlarda komşu TMX kayıtları yer değiştirmesin.
            var internalSpans = kayitlar.FindAll(k => !k.UcStub);
            if (internalSpans.Count == n)
            {
                for (int i = 0; i < n; i++) result[i] = internalSpans[i].Bilgi;
                return result;
            }

            if (n == kayitlar.Count)
            {
                for (int i = 0; i < n; i++) result[i] = kayitlar[i].Bilgi;
                return result;
            }

            var used = new bool[kayitlar.Count];
            for (int i = 0; i < n; i++)
            {
                double lenM = chain[i].World0.GetDistanceTo(chain[i].World1) / 100.0;
                int best = -1;
                double bestDiff = double.MaxValue;
                for (int k = 0; k < kayitlar.Count; k++)
                {
                    if (used[k]) continue;
                    double d = Math.Abs(kayitlar[k].L_m - lenM);
                    if (d < bestDiff) { bestDiff = d; best = k; }
                }
                if (best < 0)
                {
                    // Hepsi kullanıldıysa sıradaki kayda sar.
                    result[i] = kayitlar[Math.Min(i, kayitlar.Count - 1)].Bilgi;
                }
                else
                {
                    used[best] = true;
                    result[i] = kayitlar[best].Bilgi;
                }
            }
            return result;
        }

        /// <summary>
        /// GPR TEMEL BETONARME tablo sırası + aynı indeksteki TMX donatı.
        /// Her kayıt bir kolon arası (veya uç stub) = bir “kiriş” donatısı.
        /// </summary>
        private static List<SurekliTemelSpanKayit> SurekliTemelLoadSpanKayitlar(string st4Path)
        {
            var list = new List<SurekliTemelSpanKayit>();
            var tmx = SurekliTemelReadTmx(st4Path);
            var gprMeta = SurekliTemelParseGprSpanMeta(st4Path);
            if (gprMeta.Count == 0 && tmx.Count > 0)
                gprMeta = SurekliTemelParseStatikSpanMeta(st4Path, tmx.Count);
            int n = Math.Max(tmx.Count, gprMeta.Count);
            byte[] analysis = null;
            string thePath = Path.ChangeExtension(st4Path, ".THE");
            if (File.Exists(thePath))
            {
                var bytes = File.ReadAllBytes(thePath);
                if (bytes.Length == n * 700) analysis = bytes;
            }
            for (int i = 0; i < n; i++)
            {
                var kay = new SurekliTemelSpanKayit();
                if (analysis != null)
                    kay.UcStub = BitConverter.ToInt16(analysis, i * 700 + 10) == 0
                        || BitConverter.ToInt16(analysis, i * 700 + 12) == 0;
                if (i < gprMeta.Count)
                {
                    kay.TNo = gprMeta[i].TNo;
                    kay.L_m = gprMeta[i].L_m;
                }
                if (i < tmx.Count && tmx.Count == gprMeta.Count) kay.Bilgi = tmx[i];
                else if (i < gprMeta.Count) kay.Bilgi = gprMeta[i].Bilgi;
                if (kay.TNo < 1 && i < tmx.Count)
                {
                    // Kimliği doğrulanmayan TMX temel numarası gibi yorumlanmaz.
                    kay.TNo = 0;
                }
                if (kay.Bilgi.Adet > 0 || kay.Bilgi.DuzAdet > 0 || kay.TNo > 0)
                    list.Add(kay);
            }
            return list;
        }

        private struct SurekliTemelGprSpanMeta
        {
            public int TNo;
            public double L_m;
            public KirisAcilimMontajBilgi Bilgi;
        }

        // Bazı projelerin GPR'sinde yalnız TEMEL STATİK tablosu bulunur.
        // GPR T numarası + THE uç koordinatları, TMX ile aynı açıklık sırasındadır.
        private static List<SurekliTemelGprSpanMeta> SurekliTemelParseStatikSpanMeta(string st4Path, int count)
        {
            var result = new List<SurekliTemelGprSpanMeta>();
            string gpr = Path.ChangeExtension(st4Path, ".GPR"), the = Path.ChangeExtension(st4Path, ".THE");
            if (!File.Exists(gpr) || !File.Exists(the)) return result;
            WindowsAnsiEncodings.EnsureRegistered();
            string text = File.ReadAllText(gpr, Encoding.GetEncoding(857));
            byte[] data = File.ReadAllBytes(the);
            if (count <= 0 || data.Length != count * 700) return result;
            int start = text.IndexOf("TEMEL STAT", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return result;
            int tNo = 0;
            foreach (string line in text.Substring(start).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.IndexOf("Bo/D", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var match = Regex.Match(line, @"\bT(\d+)\b");
                    if (match.Success) tNo = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                if (tNo <= 0 || line.IndexOf("Xac=", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int offset = result.Count * 700;
                if (offset + 48 > data.Length) return new List<SurekliTemelGprSpanMeta>();
                double dx = BitConverter.ToSingle(data, offset + 40) - BitConverter.ToSingle(data, offset + 32);
                double dy = BitConverter.ToSingle(data, offset + 44) - BitConverter.ToSingle(data, offset + 36);
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (double.IsNaN(length) || double.IsInfinity(length) || length > 10000)
                    return new List<SurekliTemelGprSpanMeta>();
                result.Add(new SurekliTemelGprSpanMeta { TNo = tNo, L_m = length });
            }
            return result.Count == count ? result : new List<SurekliTemelGprSpanMeta>();
        }

        private static List<KirisAcilimMontajBilgi> SurekliTemelReadTmx(string st4Path)
        {
            var list = new List<KirisAcilimMontajBilgi>();
            if (string.IsNullOrEmpty(st4Path)) return list;
            string path = Path.ChangeExtension(st4Path, ".TMX");
            if (!File.Exists(path))
            {
                path = Path.ChangeExtension(st4Path, ".tmx");
                if (!File.Exists(path)) return list;
            }
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch { return list; }
            const int rec = 250;
            int n = data.Length / rec;
            for (int i = 0; i < n; i++)
            {
                int o = i * rec;
                short Read(int off) => o + off + 1 < data.Length ? BitConverter.ToInt16(data, o + off) : (short)0;
                var bil = new KirisAcilimMontajBilgi
                {
                    Adet = Read(100),
                    Cap = Read(102),
                    DuzAdet = Read(104),
                    DuzCap = Read(106),
                    GovdeAdet = Read(112),
                    GovdeCap = Read(114),
                    SolUstAdet = Read(128),
                    SolUstCap = Read(130),
                    SolAltAdet = Read(124),
                    SolAltCap = Read(126),
                    SagUstAdet = 0,
                    SagUstCap = 0,
                    SagAltAdet = 0,
                    SagAltCap = 0,
                    EtriyeCap = Read(134),
                    EtriyeOrtaCm = Read(136),
                    EtriyeSikCm = Read(138)
                };
                if (bil.EtriyeSikCm <= 0) bil.EtriyeSikCm = bil.EtriyeOrtaCm;
                list.Add(bil); // Boş kayıt da sıra tutar; GPR/THE ile indeks kaymasın.
            }
            return list;
        }

        /// <summary>GPR TEMEL BETONARME: her L= satırı bir span; sıra TMX ile aynı.</summary>
        private static List<SurekliTemelGprSpanMeta> SurekliTemelParseGprSpanMeta(string st4Path)
        {
            var list = new List<SurekliTemelGprSpanMeta>();
            if (string.IsNullOrEmpty(st4Path)) return list;
            string gpr = Path.ChangeExtension(st4Path, ".GPR");
            if (!File.Exists(gpr))
            {
                gpr = Path.ChangeExtension(st4Path, ".gpr");
                if (!File.Exists(gpr)) return list;
            }
            string text;
            try
            {
                WindowsAnsiEncodings.EnsureRegistered();
                text = File.ReadAllText(gpr, Encoding.GetEncoding(857));
            }
            catch
            {
                try { text = File.ReadAllText(gpr, Encoding.Default); }
                catch { return list; }
            }

            text = GprRadyeAyir.YapidanOnce(text);
            int start = text.LastIndexOf("TEMEL BETONARME HESAP", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = text.IndexOf("TEMEL BETONARME HESAP", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return list;
            string chunk = text.Substring(start); // Uzun raporda son temeller kesilmesin.
            string[] lines = chunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var monRx = new Regex(@"(\d+)\s*[°ØøO]\s*(\d+)\s*\(\s*mon", RegexOptions.IgnoreCase);
            var duzRx = new Regex(@"(\d+)\s*[°ØøO]\s*(\d+)\s*\(\s*d", RegexOptions.IgnoreCase);
            var ilaRx = new Regex(@"(\d+)\s*[°ØøO]\s*(\d+)\s*\(([^)]*ila[^)]*)\)", RegexOptions.IgnoreCase);
            var etrRx = new Regex(@"(?:(\d+)\s*)?[°ØøO]\s*(\d+)\s*/\s*(\d+)(?:\s*/\s*(\d+))?\s*\(\s*etriye",
                RegexOptions.IgnoreCase);

            int curT = 0;
            bool expectL = false;
            int bodyStart = -1;
            for (int li = 0; li < lines.Length; li++)
            {
                string ln = lines[li];
                if (ln.IndexOf("Bo/D", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Match tm = Regex.Match(ln, @"(?:^|\s)T(\d+)\s", RegexOptions.IgnoreCase);
                    if (tm.Success &&
                        int.TryParse(tm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tn) &&
                        tn > 0)
                        curT = tn;
                }

                if (ln.IndexOf("altMsol", StringComparison.OrdinalIgnoreCase) >= 0
                    || (ln.IndexOf("Mac.", StringComparison.OrdinalIgnoreCase) >= 0
                        && ln.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    expectL = true;
                    bodyStart = li;
                }

                if (!expectL) continue;
                Match lm = Regex.Match(ln, @"L=\s*([0-9]+(?:[.,][0-9]+)?)\s*m", RegexOptions.IgnoreCase);
                if (!lm.Success) continue;

                string ls = lm.Groups[1].Value.Replace(',', '.');
                if (!double.TryParse(ls, NumberStyles.Float, CultureInfo.InvariantCulture, out double L)) L = 0;

                // Bu tablo bloğunun DONATI sütunu: L satırı + sonraki ~12 satır.
                int from = bodyStart >= 0 ? bodyStart : li;
                int to = Math.Min(lines.Length, li + 12);
                var sb = new StringBuilder();
                for (int j = from; j < to; j++) sb.AppendLine(lines[j]);
                string body = sb.ToString();

                var bil = new KirisAcilimMontajBilgi();
                Match mon = monRx.Match(body);
                if (mon.Success)
                {
                    bil.Adet = int.Parse(mon.Groups[1].Value, CultureInfo.InvariantCulture);
                    bil.Cap = int.Parse(mon.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                Match duz = duzRx.Match(body);
                if (duz.Success)
                {
                    bil.DuzAdet = int.Parse(duz.Groups[1].Value, CultureInfo.InvariantCulture);
                    bil.DuzCap = int.Parse(duz.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                foreach (Match ila in ilaRx.Matches(body))
                {
                    int adet = int.Parse(ila.Groups[1].Value, CultureInfo.InvariantCulture);
                    int cap = int.Parse(ila.Groups[2].Value, CultureInfo.InvariantCulture);
                    string face = ila.Groups[3].Value;
                    if (face.IndexOf("alt", StringComparison.OrdinalIgnoreCase) >= 0)
                    { bil.SolAltAdet = adet; bil.SolAltCap = cap; }
                    else if (face.IndexOf("st", StringComparison.OrdinalIgnoreCase) >= 0)
                    { bil.SolUstAdet = adet; bil.SolUstCap = cap; }
                }
                Match etr = etrRx.Match(body);
                if (etr.Success)
                {
                    bil.EtriyeCap = int.Parse(etr.Groups[2].Value, CultureInfo.InvariantCulture);
                    bil.EtriyeOrtaCm = int.Parse(etr.Groups[3].Value, CultureInfo.InvariantCulture);
                    if (etr.Groups[4].Success && etr.Groups[4].Length > 0)
                        bil.EtriyeSikCm = int.Parse(etr.Groups[4].Value, CultureInfo.InvariantCulture);
                    else
                        bil.EtriyeSikCm = bil.EtriyeOrtaCm;
                }

                list.Add(new SurekliTemelGprSpanMeta { TNo = curT, L_m = L, Bilgi = bil });
                expectL = false;
                bodyStart = -1;
            }
            return list;
        }

        private double KirisAcilimCelikY(double topY, double heightCm, bool montajCubugu)
        {
            bool ustYuz = montajCubugu == KirisAcilimMontajUstYuz;
            if (ustYuz) return topY - KirisAcilimMontajPaspayiCm;
            return topY - heightCm + KirisAcilimDuzPaspayiCm;
        }

        /// <summary>
        /// TMX/GPR: SolUst/SagUst → üst yüz (düz yanı); SolAlt/SagAlt → alt yüz (montaj yanı).
        /// Aynı bayde sol/sağ uç aynı slotlardan beslenir (KMX uç eşlemesi yok).
        /// </summary>
        private static void SurekliTemelIlavePick(KirisAcilimMontajBilgi src, bool ustYuz, out int adet, out int cap)
        {
            int a1 = ustYuz ? src.SolUstAdet : src.SolAltAdet;
            int c1 = ustYuz ? src.SolUstCap : src.SolAltCap;
            int a2 = ustYuz ? src.SagUstAdet : src.SagAltAdet;
            int c2 = ustYuz ? src.SagUstCap : src.SagAltCap;
            // Çap×adet As yaklaşık: büyük As'yi seç.
            double as1 = a1 > 0 && c1 > 0 ? a1 * c1 * c1 : 0;
            double as2 = a2 > 0 && c2 > 0 ? a2 * c2 * c2 : 0;
            if (as2 > as1) { adet = a2; cap = c2; }
            else { adet = a1; cap = c1; }
        }

        /// <summary>
        /// Aynı isimli sürekli temel şeridini üzerine oturan her kolonda böler.
        /// Böylece ara kolonlar da mesnet (sup[]) olur; ilave Ln her açıklıkta ayrı hesaplanır.
        /// </summary>
        private void SurekliTemelChainAraKolonBol(List<KirisAcilimSpan> chain, int floorNo)
        {
            if (chain == null || chain.Count == 0 || _model?.Columns == null) return;
            var rebuilt = new List<KirisAcilimSpan>();
            foreach (var span in chain)
            {
                if (span?.Beam == null)
                {
                    rebuilt.Add(span);
                    continue;
                }
                Point2d a = span.World0;
                Point2d b = span.World1;
                Vector2d v = b - a;
                double len = v.Length;
                if (len < 5.0)
                {
                    rebuilt.Add(span);
                    continue;
                }
                Vector2d u = v.GetNormal();
                var cuts = new List<(double T, Point2d W, int Cross)>
                {
                    (0.0, a, span.Cross0),
                    (len, b, span.Cross1)
                };
                foreach (var col in _model.Columns)
                {
                    if (col == null) continue;
                    if (!_axisService.TryIntersect(col.AxisXId, col.AxisYId, out Point2d axisNode)) continue;
                    double tAlong = (axisNode.X - a.X) * u.X + (axisNode.Y - a.Y) * u.Y;
                    if (tAlong < -30.0 || tAlong > len + 30.0) continue;
                    // Orta kolonlar şerit başından uzak; origin'i kolon izdüşümüne al (mesafe filtresi geçsin).
                    Point2d origin = new Point2d(a.X + u.X * tAlong, a.Y + u.Y * tAlong);
                    if (!TryKirisAcilimColumnInterval(floorNo, col, axisNode, origin, u, span.Beam, out double lo, out double hi))
                        continue;
                    if (hi - lo <= 2.0) continue;
                    double t = tAlong + 0.5 * (lo + hi);
                    if (t < 15.0 || t > len - 15.0) continue;
                    Point2d w = new Point2d(a.X + u.X * t, a.Y + u.Y * t);
                    int cross = span.Beam.FixedAxisId >= 1001 && span.Beam.FixedAxisId <= 1999
                        ? col.AxisYId : col.AxisXId;
                    bool near = false;
                    for (int i = 0; i < cuts.Count; i++)
                    {
                        if (Math.Abs(cuts[i].T - t) < 8.0) { near = true; break; }
                    }
                    if (!near) cuts.Add((t, w, cross));
                }
                cuts.Sort((p, q) => p.T.CompareTo(q.T));
                for (int i = 1; i < cuts.Count; i++)
                {
                    double t0 = cuts[i - 1].T, t1 = cuts[i].T;
                    if (t1 - t0 < 2.0) continue;
                    double u0 = t0 / len, u1 = t1 / len;
                    rebuilt.Add(new KirisAcilimSpan
                    {
                        Beam = span.Beam,
                        World0 = cuts[i - 1].W,
                        World1 = cuts[i].W,
                        Dir = u,
                        Cross0 = cuts[i - 1].Cross,
                        Cross1 = cuts[i].Cross,
                        Kot0 = span.Kot0 + (span.Kot1 - span.Kot0) * u0,
                        Kot1 = span.Kot0 + (span.Kot1 - span.Kot0) * u1
                    });
                }
            }
            if (rebuilt.Count == 0) return;
            chain.Clear();
            chain.AddRange(rebuilt);
        }

        /// <summary>ST4 Start/EndExtensionCm: aks kesişiminden şerit uçlarını uzat.</summary>
        private void SurekliTemelSegUzat(KirisAcilimSeg seg)
        {
            if (seg?.Beam == null || _surekliTemelCfByBeam == null) return;
            if (!_surekliTemelCfByBeam.TryGetValue(seg.Beam, out ContinuousFoundationInfo cf) || cf == null)
                return;
            double startExt = Math.Max(0, cf.StartExtensionCm);
            double endExt = Math.Max(0, cf.EndExtensionCm);
            if (startExt < 0.5 && endExt < 0.5) return;
            Vector2d dir = seg.Dir;
            if (dir.Length < 1e-9) return;
            // P0 StartAxis, P1 EndAxis varsayımı (seg oluşturulurken).
            bool p0Start = seg.Cross0 == seg.Beam.StartAxisId;
            if (p0Start)
            {
                seg.P0 = new Point2d(seg.P0.X - dir.X * startExt, seg.P0.Y - dir.Y * startExt);
                seg.P1 = new Point2d(seg.P1.X + dir.X * endExt, seg.P1.Y + dir.Y * endExt);
            }
            else
            {
                seg.P0 = new Point2d(seg.P0.X - dir.X * endExt, seg.P0.Y - dir.Y * endExt);
                seg.P1 = new Point2d(seg.P1.X + dir.X * startExt, seg.P1.Y + dir.Y * startExt);
            }
            seg.Len = seg.P0.GetDistanceTo(seg.P1);
            if (seg.Len > 1e-9) seg.Dir = (seg.P1 - seg.P0).GetNormal();
        }

        /// <summary>
        /// Açılım şeridinde gönye yönü: montaj yukarı, düz aşağı (görünüşteki “kesite giren” yön ters çevrilir).
        /// </summary>
        private static void SurekliTemelAcilimGonyeYonCevir(List<Point2d> pts, int ana, bool montajAcilim)
        {
            if (pts == null || pts.Count < 3 || ana < 0 || ana + 1 >= pts.Count) return;
            double yAna = 0.5 * (pts[ana].Y + pts[ana + 1].Y);
            // Sol gönye
            for (int j = ana - 1; j >= 0; j--)
            {
                if (Math.Abs(pts[j].Y - yAna) < 1.0 && Math.Abs(pts[j + 1].Y - yAna) < 1.0) continue;
                // Ana koldan ayrılan uçların Y'sini bar eksenine göre aynala → istenen yön
                double tipY = pts[0].Y;
                bool nowUp = tipY > yAna + 0.5;
                bool wantUp = montajAcilim;
                if (nowUp != wantUp)
                {
                    for (int k = 0; k <= j + 1 && k < pts.Count; k++)
                    {
                        if (k >= ana) break;
                        pts[k] = new Point2d(pts[k].X, 2.0 * yAna - pts[k].Y);
                    }
                }
                break;
            }
            // Sağ gönye
            for (int j = ana + 1; j + 1 < pts.Count; j++)
            {
                if (Math.Abs(pts[j].Y - yAna) < 1.0 && Math.Abs(pts[j + 1].Y - yAna) < 1.0) continue;
                double tipY = pts[pts.Count - 1].Y;
                bool nowUp = tipY > yAna + 0.5;
                bool wantUp = montajAcilim;
                if (nowUp != wantUp)
                {
                    for (int k = j; k < pts.Count; k++)
                        pts[k] = new Point2d(pts[k].X, 2.0 * yAna - pts[k].Y);
                }
                break;
            }
        }
    }
}
