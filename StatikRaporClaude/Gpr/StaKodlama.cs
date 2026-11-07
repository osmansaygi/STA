using System.Text;

namespace StatikRaporClaude.Gpr
{
    /// <summary>
    /// STA4CAD GPR metni karışık kodludur: Türkçe harfler Windows-1254 konumunda,
    /// tablo çerçevesi DOS (437/857) konumunda. Coursta.ttf bu baytları doğrudan çizer;
    /// okunur metin için çerçeve baytları Unicode kutu karakterlerine çevrilir.
    /// </summary>
    public static class StaKodlama
    {
        private static readonly Dictionary<char, char> Cerceve = new Dictionary<char, char>
        {
            ['Ä'] = '─', // C4
            ['¡'] = '│', // A1 — STA tablolarında dikey ayraç
            ['Å'] = '┼', // C5
            ['Á'] = '┴', // C1
            ['Â'] = '┬', // C2
            ['´'] = '┤', // B4
            ['Ã'] = '├', // C3
            ['Ú'] = '┌', // DA
            ['¿'] = '┐', // BF
            ['À'] = '└', // C0
            ['Ù'] = '┘', // D9
            ['Í'] = '═', // CD
            ['º'] = '║', // BA
            ['ø'] = 'ø', // F8 çap
            ['ò'] = '✓', // F2 STA onay işareti
            ['ï'] = 'ρ', // EF donatı oranı (Türkçede ï yok)
            ['ì'] = 'ω', // EC açısal frekans
            // STA yazı tipinde Yunan/matematik harfleri (Türkçede kullanılmayan Latin-1 konumları)
            ['ä'] = 'Σ', // E4 toplam (Σ= 99.21, ΣAs/Ac)
            ['â'] = 'φ', // E2 (Φx=0, Σ(mi·φxi²))
            ['ô'] = 'Δ', // F4 (ΔY düst)
            ['ë'] = 'δ', // EB sehim (δt = δi + δig·λ)
            ['è'] = 'λ', // E8
            ['é'] = 'θ', // E9 (mθi, θir)
            ['Ë'] = '≤', // CB
            ['È'] = '≥', // C8
            ['ß'] = 'β', // DF (βx, β deprem yükü oranı)
            ['à'] = 'α', // E0 (αm perde taban moment oranı)
            ['Ó'] = 'α', // D3 (Coulomb duvar açısı)
            ['û'] = '√', // FB √(2×uiX²)
            ['ã'] = 'Π', // E3 θΠi
            ['í'] = 'ø', // ED etriye çapı (Etriye:ø10/8)
            ['ñ'] = '✗', // F1 uygun değil
            ['å'] = 'σ', // E5 zemin gerilmesi
            ['»'] = '→', // BB en büyük değerin elemanı (0.0028377→ S1-03)
        };

        public static string Unicode(string ham)
        {
            if (string.IsNullOrEmpty(ham)) return ham;
            var sb = new StringBuilder(ham.Length);
            foreach (char c in ham) sb.Append(Cerceve.TryGetValue(c, out var r) ? r : c);
            return sb.ToString();
        }

        /// <summary>Başlık karşılaştırması için: büyük harf, Türkçe harfler ASCII'ye, fazla boşluk tek.</summary>
        public static string Anahtar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder();
            foreach (char c0 in s.ToUpperInvariant())
            {
                char c = c0 switch
                {
                    'İ' => 'I', 'I' => 'I', 'Ş' => 'S', 'Ğ' => 'G', 'Ü' => 'U', 'Ö' => 'O', 'Ç' => 'C', 'ı' => 'I',
                    _ => c0
                };
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return sb.ToString().Trim();
        }
    }
}
