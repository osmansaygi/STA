using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace ST4Yardimci
{
    internal static partial class KapakRunner
    {
        /// <summary>Kapak çizim yüzeyi: aynı yerleşim (<see cref="Ciz"/>) hem DWG'ye hem doğrudan PDF'e çizilir. Ölçüler kâğıt mm, y yukarıdan aşağı.</summary>
        private interface IKapakCizici
        {
            void Sinir(double x, double y, double w, double h);
            void Kutu(double x, double y, double w, double h);
            void Cizgi(double xa, double ya, double xb, double yb, bool ince);
            void Dolgu(double x, double y, double w, double h);
            void Logo(double cx, double cy, double w);
            void Yazi(double x, double y, double punto, string metin, int hiza, bool kalin, AcColor renk, double enFazla);
            /// <summary>Yalnız PDF tasarımında kullanılan süs zemini/çizgisi (DWG'de çizilmez). <paramref name="vurgu"/>: yeşil vurgu, değilse açık zemin.</summary>
            void PdfSus(double x, double y, double w, double h, bool vurgu);
        }

        /// <summary>Kâğıt (PDF) renkleri: ekran renkleri koyu baskı renklerine; ACI 7 siyah.</summary>
        private static AcColor BaskiRengi(AcColor c)
        {
            if (ReferenceEquals(c, Gri) || ReferenceEquals(c, Koyu)) return BaskiEtiket;
            if (ReferenceEquals(c, InceCizgi)) return BaskiInce;
            if (ReferenceEquals(c, BaslikRenk)) return Beyaz;
            return c;
        }

        /// <summary>
        /// Kapağı AutoCAD çizimine dokunmadan doğrudan A4 PDF dosyasına yazar (vektör çizgi/logo, gömülü Arial alt kümesi).
        /// Yerleşim ve renkler DWG çizimiyle aynıdır (<see cref="Ciz"/>, baskı renkleri); ekran dolguları basılmaz.
        /// </summary>
        internal static void PdfYaz(KapakVeri v, string yol)
        {
            var c = new PdfCizici();
            Ciz(c, v);
            byte[] pdf = c.Bitir();
            File.WriteAllBytes(yol, pdf);
        }

        private sealed class PdfCizici : IKapakCizici
        {
            private const double KatmanKalinlik = 0.30, InceKalinlik = 0.20; // mm (KAPAK katmanı 0,30; ince çizgi 0,20)
            private readonly StringBuilder _ak = new StringBuilder();
            private readonly TtfAltKume _normal, _kalin;
            private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            public PdfCizici()
            {
                string klasor = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                _normal = new TtfAltKume(Path.Combine(klasor, "arial.ttf"));
                _kalin = new TtfAltKume(Path.Combine(klasor, "arialbd.ttf"));
                // 1 birim = 1 mm, başlangıç sol alt (y yukarı).
                _ak.Append("2.834646 0 0 2.834646 0 0 cm\n1 J 1 j\n");
            }

            private static string F(double d) => Math.Round(d, 4).ToString("0.####", Inv);
            private static double Py(double y) => 297 - y;

            // PDF tasarımı "A — Hafif dolgu" (kullanıcı seçimi 24.11.2026, output/beykent-kapak-claude/pdf-tasarim-secenekleri.html):
            // lacivert-gri çerçeve, açık gri-mavi etiket zeminleri, lacivert başlık, başlık altında ince yeşil çizgi.
            private const int PdfCerceve = 0x4A5A6E, PdfInce = 0xB8C2CE, PdfEtiket = 0x33475E, PdfBaslik = 0x1F3550, PdfZemin = 0xEEF2F6, PdfDeger = 0x000000;

            private static int PdfRengi(AcColor c)
            {
                if (ReferenceEquals(c, Gri) || ReferenceEquals(c, Koyu)) return PdfEtiket;
                if (ReferenceEquals(c, BaslikRenk)) return PdfBaslik;
                if (ReferenceEquals(c, InceCizgi)) return PdfInce;
                if (ReferenceEquals(c, Beyaz)) return PdfDeger;
                if (c.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci && (c.ColorIndex == 7 || c.ColorIndex == 0)) return 0;
                return (c.Red << 16) | (c.Green << 8) | c.Blue;
            }

            private void Renk(AcColor c, bool dolgu) => Renk(PdfRengi(c), dolgu);

            private void Renk(int rgb, bool dolgu)
            {
                _ak.Append(F(((rgb >> 16) & 255) / 255.0)).Append(' ').Append(F(((rgb >> 8) & 255) / 255.0)).Append(' ').Append(F((rgb & 255) / 255.0))
                   .Append(dolgu ? " rg\n" : " RG\n");
            }

            private void Dikdortgen(double x, double y, double w, double h, int rgb)
            {
                Renk(rgb, true);
                _ak.Append(F(x)).Append(' ').Append(F(Py(y + h))).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re f\n");
            }

            public void Sinir(double x, double y, double w, double h) { } // yalnız DWG'de (yazdırılmayan katman)
            /// <summary>Etiket/başlık zemini: ekrandaki koyu saydam dolgu yerine kâğıtta açık gri-mavi.</summary>
            public void Dolgu(double x, double y, double w, double h) => Dikdortgen(x, y, w, h, PdfZemin);
            public void PdfSus(double x, double y, double w, double h, bool vurgu) => Dikdortgen(x, y, w, h, vurgu ? 0x60B644 : PdfZemin);

            public void Kutu(double x, double y, double w, double h)
            {
                Renk(PdfCerceve, false);
                _ak.Append(F(KatmanKalinlik)).Append(" w ")
                   .Append(F(x)).Append(' ').Append(F(Py(y + h))).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re S\n");
            }

            public void Cizgi(double xa, double ya, double xb, double yb, bool ince)
            {
                Renk(ince ? PdfInce : PdfCerceve, false);
                _ak.Append(F(ince ? InceKalinlik : KatmanKalinlik)).Append(" w ")
                   .Append(F(xa)).Append(' ').Append(F(Py(ya))).Append(" m ").Append(F(xb)).Append(' ').Append(F(Py(yb))).Append(" l S\n");
            }

            public void Logo(double cx, double cy, double w)
            {
                double birim = w / KapakLogoVeri.En, x0 = cx - w / 2, y0 = Py(cy + birim / 2);
                foreach (var parca in new[] { new { Veri = KapakLogoVeri.Yesil, Renk = LogoYesil }, new { Veri = KapakLogoVeri.Mavi, Renk = LogoMavi } })
                {
                    Renk(parca.Renk, true);
                    foreach (double[] halka in parca.Veri)
                    {
                        for (int i = 0; i + 1 < halka.Length; i += 2)
                            _ak.Append(F(x0 + halka[i] * birim)).Append(' ').Append(F(y0 + halka[i + 1] * birim)).Append(i == 0 ? " m\n" : " l\n");
                        _ak.Append("h\n");
                    }
                    _ak.Append("f*\n"); // tarama "Normal" stili: iç halkalar boşluk
                }
            }

            public void Yazi(double x, double y, double punto, string metin, int hiza, bool kalin, AcColor renk, double enFazla)
            {
                if (string.IsNullOrWhiteSpace(metin)) return;
                TtfAltKume f = kalin ? _kalin : _normal;
                double boy = punto, oran = 1;
                double gen = f.Genislik(metin) * boy;
                // DWG ile aynı sığdırma: önce daralt (en çok 0,6), yetmezse küçült.
                if (enFazla > 0 && gen > enFazla && gen > 0)
                {
                    oran = Math.Max(0.6, enFazla / gen);
                    if (gen * oran > enFazla) boy *= enFazla / (gen * oran);
                }
                gen = f.Genislik(metin) * boy * oran;
                double bx = hiza == 1 ? x - gen / 2 : hiza == 2 ? x - gen : x;
                Renk(renk, true);
                _ak.Append("BT /").Append(kalin ? "F2" : "F1").Append(' ').Append(F(boy)).Append(" Tf ")
                   .Append(F(oran * 100)).Append(" Tz ")
                   .Append(F(bx)).Append(' ').Append(F(Py(y))).Append(" Td <").Append(f.Kodla(metin)).Append("> Tj ET\n");
            }

            public byte[] Bitir()
            {
                var w = new PdfYazici();
                int katalog = w.Ayir(), sayfalar = w.Ayir(), sayfa = w.Ayir(), icerik = w.Ayir();
                int f1 = _normal.Yaz(w, "AAAAAB"), f2 = _kalin.Yaz(w, "AAAAAC");
                w.Nesne(katalog, "<< /Type /Catalog /Pages " + sayfalar + " 0 R >>");
                w.Nesne(sayfalar, "<< /Type /Pages /Kids [" + sayfa + " 0 R] /Count 1 >>");
                w.Nesne(sayfa, "<< /Type /Page /Parent " + sayfalar + " 0 R /MediaBox [0 0 595.2756 841.8898] /Contents " + icerik +
                    " 0 R /Resources << /Font << /F1 " + f1 + " 0 R /F2 " + f2 + " 0 R >> >> >>");
                w.Akis(icerik, "", Encoding.ASCII.GetBytes(_ak.ToString()));
                int bilgi = w.Ayir();
                w.Nesne(bilgi, "<< /Producer (BEYKENT PROJE KAPAK) /Title (Proje Kapagi) >>");
                return w.Bitir(katalog, bilgi);
            }
        }

        /// <summary>Basit PDF nesne yazıcısı (xref, FlateDecode akışlar).</summary>
        private sealed class PdfYazici
        {
            private readonly MemoryStream _m = new MemoryStream();
            private readonly Dictionary<int, long> _konum = new Dictionary<int, long>();
            private int _son;

            public PdfYazici() { Yaz("%PDF-1.6\n%âãÏÓ\n"); }

            private void Yaz(string s) { byte[] b = Encoding.GetEncoding(28591).GetBytes(s); _m.Write(b, 0, b.Length); }
            public int Ayir() => ++_son;

            public void Nesne(int no, string govde)
            {
                _konum[no] = _m.Position;
                Yaz(no + " 0 obj\n" + govde + "\nendobj\n");
            }

            public void Akis(int no, string ekSozluk, byte[] veri)
            {
                byte[] z = Sikistir(veri);
                _konum[no] = _m.Position;
                Yaz(no + " 0 obj\n<< /Length " + z.Length + " /Filter /FlateDecode " + ekSozluk + ">>\nstream\n");
                _m.Write(z, 0, z.Length);
                Yaz("\nendstream\nendobj\n");
            }

            public byte[] Bitir(int katalog, int bilgi)
            {
                long xref = _m.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 ").Append(_son + 1).Append("\n0000000000 65535 f \n");
                for (int i = 1; i <= _son; i++)
                    sb.Append((_konum.TryGetValue(i, out long k) ? k : 0).ToString("D10", CultureInfo.InvariantCulture)).Append(_konum.ContainsKey(i) ? " 00000 n \n" : " 00000 f \n");
                sb.Append("trailer\n<< /Size ").Append(_son + 1).Append(" /Root ").Append(katalog).Append(" 0 R /Info ").Append(bilgi)
                  .Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
                Yaz(sb.ToString());
                return _m.ToArray();
            }

            /// <summary>zlib (RFC 1950) sarmalı deflate.</summary>
            internal static byte[] Sikistir(byte[] veri)
            {
                using (var cikti = new MemoryStream())
                {
                    cikti.WriteByte(0x78); cikti.WriteByte(0x9C);
                    using (var d = new DeflateStream(cikti, CompressionLevel.Optimal, true)) d.Write(veri, 0, veri.Length);
                    uint a = 1, b = 0;
                    foreach (byte x in veri) { a = (a + x) % 65521; b = (b + a) % 65521; }
                    uint adler = (b << 16) | a;
                    cikti.WriteByte((byte)(adler >> 24)); cikti.WriteByte((byte)(adler >> 16)); cikti.WriteByte((byte)(adler >> 8)); cikti.WriteByte((byte)adler);
                    return cikti.ToArray();
                }
            }
        }

        /// <summary>
        /// TrueType yazı tipi: karakter → glif eşlemesi, ilerleme genişlikleri ve kullanılan gliflerin alt kümesi (glif numaraları
        /// korunur, kullanılmayan glifler boşaltılır) olarak PDF'e gömme (Type0 / CIDFontType2, Identity-H).
        /// </summary>
        private sealed class TtfAltKume
        {
            private readonly byte[] _d;
            private readonly Dictionary<string, (int Ofset, int Boy)> _tablo = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            private readonly int _birim, _hmSayi, _glifSayi, _locaBicim;
            private readonly Dictionary<int, int> _cmap = new Dictionary<int, int>();
            private readonly SortedDictionary<int, int> _kullanilan = new SortedDictionary<int, int>(); // glif → unicode
            private readonly string _ad;

            public TtfAltKume(string yol)
            {
                _d = File.ReadAllBytes(yol);
                int n = U16(4);
                for (int i = 0; i < n; i++)
                {
                    int r = 12 + i * 16;
                    _tablo[Encoding.ASCII.GetString(_d, r, 4)] = ((int)U32(r + 8), (int)U32(r + 12));
                }
                _birim = U16(T("head") + 18);
                _locaBicim = S16(T("head") + 50);
                _hmSayi = U16(T("hhea") + 34);
                _glifSayi = U16(T("maxp") + 4);
                CmapOku();
                _ad = AdOku() ?? Path.GetFileNameWithoutExtension(yol);
                _kullanilan[0] = 0;
            }

            private int T(string ad) => _tablo.TryGetValue(ad, out var t) ? t.Ofset : throw new InvalidDataException("TrueType tablosu yok: " + ad);
            private int U16(int o) => (_d[o] << 8) | _d[o + 1];
            private short S16(int o) => (short)U16(o);
            private uint U32(int o) => ((uint)_d[o] << 24) | ((uint)_d[o + 1] << 16) | ((uint)_d[o + 2] << 8) | _d[o + 3];

            private void CmapOku()
            {
                int c = T("cmap"), n = U16(c + 2), sec = -1;
                for (int i = 0; i < n; i++)
                {
                    int p = U16(c + 4 + i * 8), e = U16(c + 6 + i * 8);
                    int o = c + (int)U32(c + 8 + i * 8);
                    if (p == 3 && e == 1 && U16(o) == 4) { sec = o; break; }
                    if (p == 0 && U16(o) == 4 && sec < 0) sec = o;
                }
                if (sec < 0) throw new InvalidDataException("TrueType cmap (biçim 4) yok.");
                int seg = U16(sec + 6) / 2, bitis = sec + 14, bas = bitis + seg * 2 + 2, delta = bas + seg * 2, aralik = delta + seg * 2;
                for (int s = 0; s < seg; s++)
                {
                    int son = U16(bitis + s * 2), ilk = U16(bas + s * 2), dl = S16(delta + s * 2), ro = U16(aralik + s * 2);
                    if (ilk == 0xFFFF) continue;
                    for (int ch = ilk; ch <= son; ch++)
                    {
                        int g;
                        if (ro == 0) g = (ch + dl) & 0xFFFF;
                        else
                        {
                            int a = aralik + s * 2 + ro + (ch - ilk) * 2;
                            g = U16(a);
                            if (g != 0) g = (g + dl) & 0xFFFF;
                        }
                        if (g != 0 && !_cmap.ContainsKey(ch)) _cmap[ch] = g;
                    }
                }
            }

            private string AdOku()
            {
                if (!_tablo.ContainsKey("name")) return null;
                int t = T("name"), n = U16(t + 2), depo = t + U16(t + 4);
                for (int i = 0; i < n; i++)
                {
                    int r = t + 6 + i * 12;
                    if (U16(r) == 3 && U16(r + 6) == 6)
                    {
                        string s = Encoding.BigEndianUnicode.GetString(_d, depo + U16(r + 10), U16(r + 8));
                        return new string(s.Where(ch => ch > 32 && ch < 127 && "[](){}<>/%#".IndexOf(ch) < 0).ToArray());
                    }
                }
                return null;
            }

            private int Glif(char ch) => _cmap.TryGetValue(ch, out int g) ? g : 0;
            private int Ilerleme(int g) => U16(T("hmtx") + Math.Min(g, _hmSayi - 1) * 4);

            /// <summary>Punto 1 için metin genişliği (em).</summary>
            public double Genislik(string s) => s.Sum(ch => (double)Ilerleme(Glif(ch))) / _birim;

            /// <summary>Identity-H onaltılık glif dizisi; kullanılan glifler alt kümeye işlenir.</summary>
            public string Kodla(string s)
            {
                var sb = new StringBuilder(s.Length * 4);
                foreach (char ch in s)
                {
                    int g = Glif(ch);
                    if (!_kullanilan.ContainsKey(g) || _kullanilan[g] == 0) _kullanilan[g] = ch;
                    sb.Append(g.ToString("X4", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }

            private (int Bas, int Son) GlifAraligi(int g)
            {
                int l = T("loca");
                return _locaBicim == 0 ? (U16(l + g * 2) * 2, U16(l + g * 2 + 2) * 2) : ((int)U32(l + g * 4), (int)U32(l + g * 4 + 4));
            }

            /// <summary>Bileşik gliflerin parçaları da alt kümeye eklenir.</summary>
            private HashSet<int> TumGlifler()
            {
                var set = new HashSet<int>(_kullanilan.Keys);
                var yigin = new Stack<int>(set);
                int glyf = T("glyf");
                while (yigin.Count > 0)
                {
                    int g = yigin.Pop();
                    if (g >= _glifSayi) continue;
                    var (bas, son) = GlifAraligi(g);
                    if (son <= bas || S16(glyf + bas) >= 0) continue;
                    int o = glyf + bas + 10;
                    while (true)
                    {
                        int bayrak = U16(o), parca = U16(o + 2);
                        if (set.Add(parca)) yigin.Push(parca);
                        o += 4 + ((bayrak & 1) != 0 ? 4 : 2);
                        if ((bayrak & 8) != 0) o += 2; else if ((bayrak & 0x40) != 0) o += 4; else if ((bayrak & 0x80) != 0) o += 8;
                        if ((bayrak & 0x20) == 0) break;
                    }
                }
                return set;
            }

            private byte[] AltKumeDosyasi()
            {
                HashSet<int> set = TumGlifler();
                int glyf = T("glyf");
                var yeniGlyf = new MemoryStream();
                var loca = new uint[_glifSayi + 1];
                for (int g = 0; g < _glifSayi; g++)
                {
                    loca[g] = (uint)yeniGlyf.Position;
                    if (!set.Contains(g)) continue;
                    var (bas, son) = GlifAraligi(g);
                    if (son > bas) yeniGlyf.Write(_d, glyf + bas, son - bas);
                    while (yeniGlyf.Position % 4 != 0) yeniGlyf.WriteByte(0);
                }
                loca[_glifSayi] = (uint)yeniGlyf.Position;
                var locaB = new byte[loca.Length * 4];
                for (int i = 0; i < loca.Length; i++) { locaB[i * 4] = (byte)(loca[i] >> 24); locaB[i * 4 + 1] = (byte)(loca[i] >> 16); locaB[i * 4 + 2] = (byte)(loca[i] >> 8); locaB[i * 4 + 3] = (byte)loca[i]; }
                byte[] head = Kopya("head");
                head[8] = head[9] = head[10] = head[11] = 0; // checkSumAdjustment
                head[50] = 0; head[51] = 1;                  // uzun loca

                var tablolar = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["head"] = head, ["hhea"] = Kopya("hhea"), ["maxp"] = Kopya("maxp"), ["hmtx"] = Kopya("hmtx"),
                    ["loca"] = locaB, ["glyf"] = yeniGlyf.ToArray(),
                };
                foreach (string ek in new[] { "cvt ", "fpgm", "prep" })
                    if (_tablo.ContainsKey(ek)) tablolar[ek] = Kopya(ek);

                var m = new MemoryStream();
                int n = tablolar.Count, us = 1, kuvvet = 0;
                while (us * 2 <= n) { us *= 2; kuvvet++; }
                void W16(int v) { m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
                void W32(uint v) { m.WriteByte((byte)(v >> 24)); m.WriteByte((byte)(v >> 16)); m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
                W32(0x00010000); W16(n); W16(us * 16); W16(kuvvet); W16(n * 16 - us * 16);
                uint ofset = (uint)(12 + n * 16);
                foreach (var kv in tablolar)
                {
                    m.Write(Encoding.ASCII.GetBytes(kv.Key), 0, 4);
                    W32(Toplam(kv.Value)); W32(ofset); W32((uint)kv.Value.Length);
                    ofset += (uint)((kv.Value.Length + 3) & ~3);
                }
                foreach (var kv in tablolar)
                {
                    m.Write(kv.Value, 0, kv.Value.Length);
                    while (m.Position % 4 != 0) m.WriteByte(0);
                }
                return m.ToArray();
            }

            private byte[] Kopya(string ad)
            {
                var t = _tablo[ad];
                var b = new byte[t.Boy];
                Buffer.BlockCopy(_d, t.Ofset, b, 0, t.Boy);
                return b;
            }

            private static uint Toplam(byte[] b)
            {
                uint s = 0;
                for (int i = 0; i < b.Length; i += 4)
                {
                    uint v = 0;
                    for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < b.Length ? b[i + k] : 0u);
                    s += v;
                }
                return s;
            }

            private int Olc(int v) => (int)Math.Round(v * 1000.0 / _birim);

            /// <summary>Type0 yazı tipini ve bağlı nesneleri yazar; Type0 nesne numarasını döndürür.</summary>
            public int Yaz(PdfYazici w, string etiket)
            {
                int type0 = w.Ayir(), cid = w.Ayir(), tanim = w.Ayir(), dosya = w.Ayir(), uni = w.Ayir();
                string ad = etiket + "+" + _ad;
                byte[] font = AltKumeDosyasi();
                w.Akis(dosya, "/Length1 " + font.Length + " ", font);

                int head = T("head"), hhea = T("hhea");
                int capH = Olc(716 * _birim / 1000);
                if (_tablo.ContainsKey("OS/2") && U16(T("OS/2")) >= 2) capH = Olc(S16(T("OS/2") + 88));
                w.Nesne(tanim, "<< /Type /FontDescriptor /FontName /" + ad + " /Flags 32 /FontBBox [" +
                    Olc(S16(head + 36)) + " " + Olc(S16(head + 38)) + " " + Olc(S16(head + 40)) + " " + Olc(S16(head + 42)) +
                    "] /ItalicAngle 0 /Ascent " + Olc(S16(hhea + 4)) + " /Descent " + Olc(S16(hhea + 6)) + " /CapHeight " + capH +
                    " /StemV 80 /FontFile2 " + dosya + " 0 R >>");

                var gen = new StringBuilder();
                foreach (int g in _kullanilan.Keys) gen.Append(g).Append(" [").Append(Olc(Ilerleme(g))).Append("] ");
                w.Nesne(cid, "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /" + ad +
                    " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor " + tanim +
                    " 0 R /DW 1000 /W [" + gen + "] /CIDToGIDMap /Identity >>");

                var cm = new StringBuilder();
                cm.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
                  .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
                var esler = _kullanilan.Where(kv => kv.Value != 0).ToList();
                for (int i = 0; i < esler.Count; i += 100)
                {
                    var parca = esler.Skip(i).Take(100).ToList();
                    cm.Append(parca.Count).Append(" beginbfchar\n");
                    foreach (var kv in parca) cm.Append('<').Append(kv.Key.ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(kv.Value.ToString("X4", CultureInfo.InvariantCulture)).Append(">\n");
                    cm.Append("endbfchar\n");
                }
                cm.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
                w.Akis(uni, "", Encoding.ASCII.GetBytes(cm.ToString()));

                w.Nesne(type0, "<< /Type /Font /Subtype /Type0 /BaseFont /" + ad + " /Encoding /Identity-H /DescendantFonts [" + cid +
                    " 0 R] /ToUnicode " + uni + " 0 R >>");
                return type0;
            }
        }
    }
}
