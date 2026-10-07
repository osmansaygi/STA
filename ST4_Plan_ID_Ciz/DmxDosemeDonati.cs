using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// STA4CAD .DMX: döşeme donatısı (250 bayt). Ofset 96'dan çap/aralık (cm).
    /// X: düz, bayrak, pilye, sol, sağ. Y: düz, bayrak, pilye, sağ, sol.
    /// Kayıt +140/+142: konstrüktif montaj (hesaptan bağımsız; mantar plakta seçilir).
    /// Bu alan doluysa aynı çap/aralık tüm döşemelere X montaj olarak uygulanır.
    /// Çap 0 olan çift yazılmaz (pilye aralığı kopyası).
    /// Yol: yalnız ST4 ile aynı adlı <c>.DMX</c>/<c>.dmx</c>.
    /// </summary>
    public static class DmxDosemeDonati
    {
        public const int RecordBytes = 250;
        public const int RebarOffset = 96;
        /// <summary>Kayıt içi montaj çap ofseti (rebar +44 → dosya +140).</summary>
        public const int MontajRecordOffset = 140;

        public struct Bar
        {
            public int Dia;
            public int Spacing;
            public DosemeDonatiKind Kind;
        }

        public struct Rec
        {
            public List<Bar> X;
            public List<Bar> Y;
        }

        private static readonly Regex RxCapAralik = new Regex(
            @"(?:[Øø\u00D8\u00F8]\s*)?(\d+)\s*/\s*(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string ResolvePath(string st4SourcePath)
        {
            if (string.IsNullOrEmpty(st4SourcePath)) return null;
            string dir = Path.GetDirectoryName(st4SourcePath);
            if (string.IsNullOrEmpty(dir)) return null;
            string baseName = Path.GetFileNameWithoutExtension(st4SourcePath);
            string p1 = Path.Combine(dir, baseName + ".DMX");
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(dir, baseName + ".dmx");
            if (File.Exists(p2)) return p2;
            return null;
        }

        public static List<Rec> Read(string path)
        {
            var list = new List<Rec>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            byte[] b = File.ReadAllBytes(path);
            int n = b.Length / RecordBytes;
            Bar? projectMontaj = null;
            for (int r = 0; r < n; r++)
            {
                int o = r * RecordBytes + RebarOffset;
                if (o + 40 > b.Length) break;
                var rec = new Rec { X = new List<Bar>(5), Y = new List<Bar>(4) };
                Add(rec.X, b, o, DosemeDonatiKind.Duz);
                Add(rec.X, b, o + 6, DosemeDonatiKind.Pilye);
                Add(rec.X, b, o + 10, DosemeDonatiKind.SolIlave);
                Add(rec.X, b, o + 14, DosemeDonatiKind.SagIlave);
                Add(rec.Y, b, o + 18, DosemeDonatiKind.Duz);
                Add(rec.Y, b, o + 24, DosemeDonatiKind.Pilye);
                Add(rec.Y, b, o + 32, DosemeDonatiKind.SolIlave);
                Add(rec.Y, b, o + 28, DosemeDonatiKind.SagIlave);
                if (TryReadBar(b, r * RecordBytes + MontajRecordOffset, DosemeDonatiKind.Montaj, out Bar montaj)
                    && !projectMontaj.HasValue)
                    projectMontaj = montaj;
                list.Add(rec);
            }

            // Konstrüktif montaj proje geneli: DMX'te en az bir kayıtta varsa tüm döşeme X'ine ekle.
            if (projectMontaj.HasValue)
            {
                Bar m = projectMontaj.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    if (HasSameBar(list[i].X, m)) continue;
                    list[i].X.Add(m);
                }
            }
            return list;
        }

        private static bool HasSameBar(List<Bar> bars, Bar bar)
        {
            if (bars == null) return false;
            for (int i = 0; i < bars.Count; i++)
            {
                if (bars[i].Dia == bar.Dia && bars[i].Spacing == bar.Spacing && bars[i].Kind == bar.Kind)
                    return true;
            }
            return false;
        }

        private static bool TryReadBar(byte[] b, int o, DosemeDonatiKind kind, out Bar bar)
        {
            bar = default;
            if (o + 4 > b.Length) return false;
            int dia = BitConverter.ToInt16(b, o);
            int sp = BitConverter.ToInt16(b, o + 2);
            if (dia < 6 || dia > 40 || sp < 1 || sp > 200) return false;
            bar = new Bar { Dia = dia, Spacing = sp, Kind = kind };
            return true;
        }

        /// <summary>
        /// GPR hücresi DMX ile aynı sırada ve aynı çap/aralık ise true.
        /// Tablo kenarında kesilmiş GPR, DMX'in başı ise de true (eksik parça DMX'te durur).
        /// </summary>
        public static bool CellAgrees(string gprCell, IReadOnlyList<Bar> dmx, out bool gprShorter)
        {
            gprShorter = false;
            var gpr = ParseCell(gprCell);
            if (gpr.Count == 0) return true;
            if (dmx == null || gpr.Count > dmx.Count) return false;
            for (int i = 0; i < gpr.Count; i++)
            {
                if (gpr[i].Dia != dmx[i].Dia || gpr[i].Spacing != dmx[i].Spacing) return false;
                if (gpr[i].Kind != DosemeDonatiKind.Unknown && gpr[i].Kind != dmx[i].Kind) return false;
            }
            gprShorter = gpr.Count < dmx.Count;
            return true;
        }

        public static string Format(IReadOnlyList<Bar> bars)
        {
            if (bars == null || bars.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < bars.Count; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append('\u00F8');
                sb.Append(bars[i].Dia.ToString(CultureInfo.InvariantCulture));
                sb.Append('/');
                sb.Append(bars[i].Spacing.ToString(CultureInfo.InvariantCulture));
                sb.Append('(');
                sb.Append(Label(bars[i].Kind));
                sb.Append(')');
            }
            return sb.ToString();
        }

        private static void Add(List<Bar> list, byte[] b, int o, DosemeDonatiKind kind)
        {
            int dia = BitConverter.ToInt16(b, o);
            int sp = BitConverter.ToInt16(b, o + 2);
            if (dia < 6 || dia > 40 || sp < 1 || sp > 200) return;
            list.Add(new Bar { Dia = dia, Spacing = sp, Kind = kind });
        }

        private static List<Bar> ParseCell(string cell)
        {
            var list = new List<Bar>();
            if (string.IsNullOrWhiteSpace(cell)) return list;
            foreach (DosemeDonatiParca p in DosemeDonatiParcalari.SplitDonatiCell(cell))
            {
                var m = RxCapAralik.Match(p.Raw ?? string.Empty);
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dia)) continue;
                if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sp)) continue;
                list.Add(new Bar { Dia = dia, Spacing = sp, Kind = p.Kind });
            }
            return list;
        }

        private static string Label(DosemeDonatiKind kind)
        {
            switch (kind)
            {
                case DosemeDonatiKind.Pilye: return "pil";
                case DosemeDonatiKind.SolIlave: return "sol";
                case DosemeDonatiKind.SagIlave: return "sa\u011F";
                case DosemeDonatiKind.Montaj: return "Mon.";
                default: return "d\u00FCz";
            }
        }
    }
}
