using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// GPR döşeme donatı hücresindeki <c>ø10/20(düz)+ø8/30(sol ila)+...</c> ifadelerini
    /// <c>+</c> ile böler; parantez içi metne göre düz / pilye / montaj / sol ilave / sağ ilave sınıflandırır.
    /// KOLONDATA kolon donatı şemasına paralel; döşeme için X ve Y satırları ayrı işlenir.
    /// </summary>
    public enum DosemeDonatiKind
    {
        Unknown = 0,
        /// <summary>(düz)</summary>
        Duz = 1,
        /// <summary>(pil) pilye</summary>
        Pilye = 2,
        /// <summary>(Mon.) montaj</summary>
        Montaj = 3,
        /// <summary>(sol ila)</summary>
        SolIlave = 4,
        /// <summary>(sağ ila)</summary>
        SagIlave = 5
    }

    /// <summary>Tek bir + parçası: ham metin + tür.</summary>
    public readonly struct DosemeDonatiParca
    {
        public DosemeDonatiParca(string raw, DosemeDonatiKind kind)
        {
            Raw = raw ?? string.Empty;
            Kind = kind;
        }
        public string Raw { get; }
        public DosemeDonatiKind Kind { get; }
    }

    public static class DosemeDonatiParcalari
    {
        private static readonly Regex RxSonParantez = new Regex(@"\(([^)]*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        /// <summary>Satır kırığı <c>(s</c> / <c>(sa</c> gibi kapanmayan sağ ilave parçası.</summary>
        private static readonly Regex RxKirikSagIlaParcasi = new Regex(@"\(\s*sa?\s*$|\(\s*sag\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>UTF-8 / 1254 karışık metin için küçük harf + Türkçe harf yaklaşımı.</summary>
        public static string NormalizeForKindMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var b = new StringBuilder(s.Length);
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                switch (c)
                {
                    case 'ı': b.Append('i'); break;
                    case 'İ': b.Append('i'); break;
                    case 'ğ': b.Append('g'); break;
                    case 'ü': b.Append('u'); break;
                    case 'ş': b.Append('s'); break;
                    case 'ö': b.Append('o'); break;
                    case 'ç': b.Append('c'); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }

        /// <summary>Son <c>(...)</c> içeriğinden türü çıkarır (içerik boşsa Unknown).</summary>
        public static DosemeDonatiKind KindFromParenContent(string parenInner)
        {
            if (string.IsNullOrWhiteSpace(parenInner)) return DosemeDonatiKind.Unknown;
            string n = NormalizeForKindMatch(parenInner);

            // Önce ilave (sol / sağ) — "düz" ile karışmasın
            if (n.Contains("sol") && (n.Contains("ilave") || n.Contains("ila ") || n.EndsWith("ila") || n.Contains("ila")))
                return DosemeDonatiKind.SolIlave;
            // GPR kısa: (sol) → sol ilave (planda "(sol ila)" yazılır)
            if (n == "sol")
                return DosemeDonatiKind.SolIlave;
            // Sağ ilave: tam metin veya GPR satır sonu kırığı "s", "sa" (asıl anlam sağ ila)
            if ((n.Contains("sag") || n.Contains("sağ")) && (n.Contains("ilave") || n.Contains("ila ") || n.EndsWith("ila") || n.Contains("ila")))
                return DosemeDonatiKind.SagIlave;
            if ((n == "s" || n == "sa" || n == "sag") && !n.Contains("sol"))
                return DosemeDonatiKind.SagIlave;

            if (n.Contains("mon"))
                return DosemeDonatiKind.Montaj;

            if (n.Contains("pil"))
                return DosemeDonatiKind.Pilye;

            if (n.Contains("duz") || n.Contains("düz") || n == "dz")
                return DosemeDonatiKind.Duz;

            return DosemeDonatiKind.Unknown;
        }

        /// <summary>KALIP50 çiziminde gösterilecek metin: sağ/sol ilave etiketleri standartlaştırılır.</summary>
        public static string ToPlanDisplayText(string raw, DosemeDonatiKind kind)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
            string t = raw.Trim();
            switch (kind)
            {
                case DosemeDonatiKind.SagIlave:
                    return ReplaceTrailingParenOrOpenWith(t, "sağ ila");
                case DosemeDonatiKind.SolIlave:
                    return ReplaceTrailingParenOrOpenWith(t, "sol ila");
                default:
                    return t;
            }
        }

        /// <summary>Tüm <c>+</c> parçaları çizimde gösterim biçimine çevirir (sağ/sol ilave sabit etiket).</summary>
        public static string FormatCellForPlan(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell)) return cell?.Trim() ?? string.Empty;
            var parts = SplitDonatiCell(cell);
            if (parts == null || parts.Count == 0) return cell.Trim();
            var sb = new StringBuilder(cell.Length + 16);
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(ToPlanDisplayText(parts[i].Raw, parts[i].Kind));
            }
            return sb.ToString();
        }

        private static string ReplaceTrailingParenOrOpenWith(string t, string innerParenLabel)
        {
            var m = RxSonParantez.Match(t);
            if (m.Success)
                return t.Substring(0, m.Index) + "(" + innerParenLabel + ")" + t.Substring(m.Index + m.Length);
            int lp = t.LastIndexOf('(');
            if (lp >= 0)
                return t.Substring(0, lp) + "(" + innerParenLabel + ")";
            return t + "(" + innerParenLabel + ")";
        }

        /// <summary>KALIP50 GPR çiziminde gösterilecek sabit boy (cm).</summary>
        public const int KalipPlanDonatiVarsayilanBoyCm = 100;
        /// <summary><c>cap/aralik</c> biçiminde aralık çıkarılamazsa kullanılan cm değeri.</summary>
        public const int KalipPlanDonatiVarsayilanAralikCm = 30;

        private static readonly Regex RxCapVeAralik = new Regex(
            @"(?:[Øø]\s*)?(\d+)\s*/\s*(\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>GPR parçasında <c>…/30…</c> aralığını (cm) okur; örn. <c>ø8/30(düz)</c> → 30.</summary>
        public static bool TryParseDonatiAralikCm(string raw, out int aralikCm)
        {
            aralikCm = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var m = RxCapVeAralik.Match(raw);
            if (!m.Success) return false;
            return int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out aralikCm) && aralikCm > 0;
        }

        /// <summary>
        /// Kırmızı yazı: en uzun mavi çizgi / aralık; mavi yazı: en uzun kırmızı / aralık. Sonuç tavan (16,66→17).
        /// </summary>
        public static int HesaplaKalipDonatiAdet(double referansCizgiUzunlukCm, int aralikCm)
        {
            if (referansCizgiUzunlukCm <= 1e-6 || aralikCm <= 0) return 1;
            return Math.Max(1, (int)Math.Ceiling(referansCizgiUzunlukCm / aralikCm));
        }

        /// <summary>
        /// Tür etiketini (düz/pilye/ilave/montaj) metinden siler; <paramref name="referansCizgiCmForAdet"/> ile adet ekler, <c> L=…</c> yazar.
        /// Kırmızı hücrede <paramref name="referansCizgiCmForAdet"/> = en uzun mavi kırpık çizgi; mavi hücrede = en uzun kırmızı.
        /// </summary>
        public static string ToKalipPlanLengthLabel(string raw, DosemeDonatiKind kind, double referansCizgiCmForAdet, int boyCm = KalipPlanDonatiVarsayilanBoyCm)
        {
            _ = kind;
            int aralik = KalipPlanDonatiVarsayilanAralikCm;
            if (TryParseDonatiAralikCm(raw ?? string.Empty, out int a) && a > 0)
                aralik = a;
            int adet = HesaplaKalipDonatiAdet(referansCizgiCmForAdet, aralik);
            string adetStr = adet.ToString(CultureInfo.InvariantCulture);
            string boyStr = boyCm.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw))
                return adetStr + " L=" + boyStr;
            string t = raw.Trim();
            var m = RxSonParantez.Match(t);
            if (m.Success)
                t = t.Substring(0, m.Index).TrimEnd();
            else
            {
                int lp = t.LastIndexOf('(');
                if (lp >= 0)
                    t = t.Substring(0, lp).TrimEnd();
            }
            if (string.IsNullOrEmpty(t))
                return adetStr + " L=" + boyStr;
            return adetStr + t + " L=" + boyStr;
        }

        /// <summary>KALIP50: hücredeki tüm <c>+</c> parçalarını adet + gövde + <c>L=</c> ile birleştirir.</summary>
        public static string FormatKalipPlanCellDisplay(string cell, double referansCizgiCmForAdet)
        {
            if (string.IsNullOrWhiteSpace(cell))
                return "1 L=" + KalipPlanDonatiVarsayilanBoyCm.ToString(CultureInfo.InvariantCulture);
            var parts = SplitDonatiCell(cell);
            if (parts == null || parts.Count == 0)
                return ToKalipPlanLengthLabel(cell.Trim(), DosemeDonatiKind.Unknown, referansCizgiCmForAdet);
            var sb = new StringBuilder(cell.Length + 32);
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(ToKalipPlanLengthLabel(parts[i].Raw, parts[i].Kind, referansCizgiCmForAdet));
            }
            return sb.ToString();
        }

        /// <summary>Tek parça (örn. <c>ø10/20(düz)</c>).</summary>
        public static DosemeDonatiKind ClassifySegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return DosemeDonatiKind.Unknown;
            string t = segment.Trim();
            var m = RxSonParantez.Match(t);
            if (m.Success)
                return KindFromParenContent(m.Groups[1].Value);
            if (RxKirikSagIlaParcasi.IsMatch(t))
                return DosemeDonatiKind.SagIlave;
            return DosemeDonatiKind.Unknown;
        }

        /// <summary>GPR satır sonlarını kaldırır; parçalar tek satırda birleşir (örn. <c>(s</c> ile <c>ağ ila)</c> bitişik).</summary>
        public static string CollapseDonatiCellLines(string cell)
        {
            if (string.IsNullOrEmpty(cell)) return string.Empty;
            return Regex.Replace(cell.Trim(), @"[\r\n]+", string.Empty);
        }

        private static int ParenBalance(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int b = 0;
            foreach (char c in s)
            {
                if (c == '(') b++;
                else if (c == ')') b--;
            }
            return b;
        }

        /// <summary><c>+</c> yalnızca parantez dışındayken ayırır (iç içe + ifadeleri bozulmaz).</summary>
        private static List<string> SplitOnPlusOutsideParens(string cell)
        {
            var list = new List<string>();
            int depth = 0;
            var sb = new StringBuilder();
            foreach (char c in cell ?? string.Empty)
            {
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
                if (c == '+' && depth == 0)
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>Kapanmamış <c>(</c> nedeniyle bölünmüş parçaları <c>+</c> ile yeniden birleştirir.</summary>
        private static List<string> MergeUnbalancedParenRuns(List<string> parts)
        {
            var merged = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                string cur = parts[i].Trim();
                if (cur.Length == 0) continue;
                int bal = ParenBalance(cur);
                while (bal > 0 && i + 1 < parts.Count)
                {
                    i++;
                    cur = cur + "+" + parts[i].Trim();
                    bal = ParenBalance(cur);
                }
                merged.Add(cur);
            }
            return merged;
        }

        /// <summary>
        /// Donatı hücresini <c>+</c> je böler (parantez içi değil); satır sonları birleşir; kırık parantez parçaları birleştirilir.
        /// </summary>
        public static IReadOnlyList<DosemeDonatiParca> SplitDonatiCell(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell)) return Array.Empty<DosemeDonatiParca>();
            string collapsed = CollapseDonatiCellLines(cell);
            var raw = SplitOnPlusOutsideParens(collapsed);
            var merged = MergeUnbalancedParenRuns(raw);
            var list = new List<DosemeDonatiParca>(merged.Count);
            foreach (var seg0 in merged)
            {
                string seg = seg0.Trim();
                if (seg.Length == 0) continue;
                var kind = ClassifySegment(seg);
                list.Add(new DosemeDonatiParca(seg, kind));
            }
            return list;
        }

        /// <summary>X ve Y hücrelerinden ayrı ayrı parça listesi (plan/etiket için).</summary>
        public static (IReadOnlyList<DosemeDonatiParca> X, IReadOnlyList<DosemeDonatiParca> Y) SplitXyCells(string xCell, string yCell)
        {
            return (SplitDonatiCell(xCell ?? string.Empty), SplitDonatiCell(yCell ?? string.Empty));
        }
    }
}
