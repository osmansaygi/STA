using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>Kesim listesi ve çizimsiz kontrol metni. Motor çubuk adedini burada değiştirmez.</summary>
    public static class KirisOzet
    {
        public static List<CubukDokumu> Dokum(KirisDetayGirdi g, KirisDetaySonuc s)
        {
            var list = new List<CubukDokumu>();
            if (s == null || s.BoyunaCubuklar == null) return list;
            var ekler = new List<EkYeri>();
            if (s.Ekler != null)
            {
                for (int i = 0; i < s.Ekler.Count; i++) ekler.Add(s.Ekler[i]);
            }
            double bw = g != null ? g.BwMm : 0;
            int no = 1;
            for (int i = 0; i < s.BoyunaCubuklar.Count; i++)
            {
                BoyunaCubukCizim c = s.BoyunaCubuklar[i];
                string ek = EkAl(ekler, c);
                if (c.XMm.HasValue)
                {
                    list.Add(Tek(no++, c, c.Sira > 0 ? c.Sira : 1, c.XMm.Value, c.YAlttanMm ?? 0, ek, bw));
                    continue;
                }
                List<YerlesenCubuk> yerler = Konumlar(s, c.Rol);
                int n = c.Adet > 0 ? c.Adet : Math.Max(1, yerler.Count);
                for (int k = 0; k < n; k++)
                {
                    int sira = 1;
                    double x = 0;
                    double y = 0;
                    string not = ek;
                    if (k < yerler.Count)
                    {
                        sira = yerler[k].Sira;
                        x = yerler[k].XMm;
                        y = yerler[k].YAlttanMm;
                    }
                    else
                    {
                        not = "Kesitte koordinat yok. " + ek;
                    }
                    list.Add(Tek(no++, c, sira, x, y, not, bw));
                }
            }
            return list;
        }

        public static string Metin(KirisDetayGirdi g, KirisDetaySonuc s)
        {
            var b = new StringBuilder();
            if (s == null)
            {
                b.AppendLine("Sonuç yok.");
                return b.ToString().TrimEnd();
            }
            string ad = g != null && !string.IsNullOrWhiteSpace(g.Ad) ? g.Ad.Trim() : "Kiriş";
            b.Append(ad);
            if (g != null)
            {
                b.Append("  b = ");
                b.Append(Mm(g.BwMm));
                b.Append(" mm  h = ");
                b.Append(Mm(g.HMm));
                b.Append(" mm  ln = ");
                b.Append(Mm(g.LnMm));
                b.Append(" mm");
            }
            b.AppendLine();
            if (g != null && g.Ayarlar != null)
            {
                b.Append("DY ");
                b.Append(DyAd(g.Ayarlar.Suneklik));
                b.Append(", ");
                b.Append(DtsAd(g.Ayarlar.Dts));
                b.Append(", ");
                b.Append(g.Ayarlar.Beton);
                b.Append(", ");
                b.Append(g.Ayarlar.Celik);
                b.Append(", örtü ");
                b.Append(Mm(s.CcKullanilanMm > 0 ? s.CcKullanilanMm : g.Ayarlar.CcMm));
                b.AppendLine(" mm");
            }
            b.AppendLine("Girilen çubuk adedi, çapı ve etriye aralığı değiştirilmedi. Uymayanlar aşağıda uyarı veya hata olarak durur.");
            b.AppendLine();
            DonatiOzet(b, g);
            b.AppendLine("Çubuklar");
            if (s.Cubuklar == null || s.Cubuklar.Count == 0)
            {
                b.AppendLine("  (kesim listesi yok)");
            }
            else
            {
                for (int i = 0; i < s.Cubuklar.Count; i++)
                    CubukSatiri(b, s.Cubuklar[i]);
            }
            b.AppendLine();
            EtriyeOzet(b, s.Etriye);
            if (s.Govde != null && (s.Govde.Gerekli || s.Govde.KullanilanCapMm > 0))
            {
                b.Append("Gövde: ");
                if (s.Govde.Gerekli) b.Append("yönetmelik istiyor. ");
                else b.Append("yönetmelik zorunlu saymıyor; verilen çubuk yerleştirildi. ");
                b.Append("Sol yüz ");
                b.Append(s.Govde.KullanilanAdetSol);
                b.Append(", sağ yüz ");
                b.Append(s.Govde.KullanilanAdetSag);
                b.Append(", çap ");
                b.Append(Mm(s.Govde.KullanilanCapMm));
                b.Append(" mm.");
                if (s.Govde.Gerekli && s.Govde.OnerilenAdetYuz > 0)
                {
                    b.Append(" Öneri yüz başına ");
                    b.Append(s.Govde.OnerilenAdetYuz);
                    b.Append("φ");
                    b.Append(Mm(s.Govde.OnerilenCapMm));
                    b.Append(" (uygulanmadı).");
                }
                b.AppendLine();
            }
            b.AppendLine();
            KontrolOzet(b, s, KontrolSeviyesi.Hata, "Hatalar");
            KontrolOzet(b, s, KontrolSeviyesi.Uyari, "Uyarılar");
            return b.ToString().TrimEnd();
        }

        static void DonatiOzet(StringBuilder b, KirisDetayGirdi g)
        {
            b.AppendLine("Girilen donatı");
            if (g == null || g.Donati == null)
            {
                b.AppendLine("  (yok)");
                b.AppendLine();
                return;
            }
            KirisDonatiGirdisi d = g.Donati;
            SatirGrup(b, "Montaj", d.Montaj);
            SatirGrup(b, "Alt düz", d.AltDuz);
            SatirGrup(b, "Sol üst ilave", d.UstSolIlave);
            SatirGrup(b, "Sağ üst ilave", d.UstSagIlave);
            SatirGrup(b, "Sol alt ilave", d.AltSolIlave);
            SatirGrup(b, "Sağ alt ilave", d.AltSagIlave);
            if (d.GovdeToplamAdet > 0 && d.Govde != null && d.Govde.CapMm > 0)
            {
                b.Append("  Gövde: toplam ");
                b.Append(d.GovdeToplamAdet);
                b.Append("φ");
                b.Append(Mm(d.Govde.CapMm));
                b.AppendLine(" (iki yüze bölünür)");
            }
            else if (d.Govde != null && d.Govde.Var)
            {
                b.Append("  Gövde: yüz başına ");
                b.Append(d.Govde.Adet);
                b.Append("φ");
                b.AppendLine(Mm(d.Govde.CapMm));
            }
            b.Append("  Etriye: φ");
            b.Append(Mm(d.EtriyeCapMm));
            if (g.Ayarlar != null && g.Ayarlar.KullaniciOrtaAralikMm.HasValue)
            {
                b.Append(", orta ");
                b.Append(Mm(g.Ayarlar.KullaniciOrtaAralikMm.Value));
                b.Append(" mm");
            }
            if (g.Ayarlar != null && g.Ayarlar.KullaniciSarilmaAraligiMm.HasValue)
            {
                b.Append(", sarılma ");
                b.Append(Mm(g.Ayarlar.KullaniciSarilmaAraligiMm.Value));
                b.Append(" mm");
            }
            b.AppendLine();
            b.AppendLine();
        }

        static void SatirGrup(StringBuilder b, string ad, DonatiGrubu g)
        {
            if (g == null || !g.Var) return;
            b.Append("  ");
            b.Append(ad);
            b.Append(": ");
            b.Append(g.Adet);
            b.Append("φ");
            b.Append(Mm(g.CapMm));
            b.AppendLine();
        }

        static void CubukSatiri(StringBuilder b, CubukDokumu c)
        {
            b.Append(c.No);
            b.Append(". ");
            b.Append(string.IsNullOrEmpty(c.Ad) ? RolAdi(c.Rol) : c.Ad);
            b.Append("  φ");
            b.Append(Mm(c.CapMm));
            b.Append("  sıra ");
            b.Append(c.Sira);
            b.Append("  kesitte x=");
            b.Append(Mm(c.XMm));
            b.Append(" mm  y=");
            b.Append(Mm(c.YAlttanMm));
            b.AppendLine(" mm (alttan)");
            b.Append("   Kiriş boyu ");
            b.Append(Mm(c.BaslangicMm));
            b.Append(" … ");
            b.Append(Mm(c.BitisMm));
            b.Append(" mm. Düz boy ");
            b.Append(Mm(c.DuzBoyMm));
            b.Append(" mm.");
            if (c.SolKancaBMm > 0 || c.SolYayMm > 0)
            {
                b.Append(" Sol kanca ");
                b.Append(Mm(c.SolKancaBMm));
                b.Append(" mm, yay ");
                b.Append(Mm(c.SolYayMm));
                b.Append(" mm.");
            }
            if (c.SagKancaBMm > 0 || c.SagYayMm > 0)
            {
                b.Append(" Sağ kanca ");
                b.Append(Mm(c.SagKancaBMm));
                b.Append(" mm, yay ");
                b.Append(Mm(c.SagYayMm));
                b.Append(" mm.");
            }
            b.Append(" Kesim boyu ");
            b.Append(Mm(c.ToplamKesimMm));
            b.AppendLine(" mm.");
            if (!string.IsNullOrWhiteSpace(c.EkNotu))
            {
                b.Append("   Ek: ");
                b.AppendLine(c.EkNotu.Trim());
            }
        }

        static void EtriyeOzet(StringBuilder b, EtriyeSonuc e)
        {
            b.AppendLine("Etriye");
            if (e == null || e.PhiWMm <= 0)
            {
                b.AppendLine("  (yok)");
                return;
            }
            b.Append("  φ");
            b.Append(Mm(e.PhiWMm));
            b.Append(", sarılma bölgesi uzunluğu ");
            b.Append(Mm(e.LSarMm));
            b.AppendLine(" mm (2·h).");
            b.Append("  Sarılma aralığı ");
            b.Append(Mm(e.SSarMm));
            b.Append(" mm (üst sınır ");
            b.Append(Mm(e.SSarMaxHamMm));
            b.AppendLine(" mm).");
            b.Append("  Orta bölge aralığı ");
            b.Append(Mm(e.SOrtaMm));
            b.Append(" mm (üst sınır ");
            b.Append(Mm(e.SOrtaMaxHamMm));
            b.AppendLine(" mm).");
            b.Append("  Bir uçta ");
            b.Append(e.NSarBirUc);
            b.Append(" adet, ortada ");
            b.Append(e.NOrta);
            b.Append(", toplam ");
            b.Append(e.ToplamAdet);
            b.Append(" adet. Kol ");
            b.Append(e.NKol);
            b.Append(", kanca ");
            b.Append(Mm(e.KancaAci));
            b.Append("°, uç ");
            b.Append(Mm(e.KancaUcMm));
            b.AppendLine(" mm.");
        }

        static void KontrolOzet(StringBuilder b, KirisDetaySonuc s, KontrolSeviyesi seviye, string baslik)
        {
            int n = 0;
            if (s.Kontroller != null)
            {
                for (int i = 0; i < s.Kontroller.Count; i++)
                {
                    if (s.Kontroller[i].Seviye == seviye) n++;
                }
            }
            b.Append(baslik);
            b.Append(" (");
            b.Append(n);
            b.AppendLine(")");
            if (n == 0)
            {
                b.AppendLine("  yok");
                return;
            }
            for (int i = 0; i < s.Kontroller.Count; i++)
            {
                KuralKontrol k = s.Kontroller[i];
                if (k.Seviye != seviye) continue;
                b.Append("  ");
                b.Append(k.Kod);
                b.Append("  ");
                b.AppendLine(k.Mesaj);
            }
        }

        static CubukDokumu Tek(int no, BoyunaCubukCizim c, int sira, double x, double y, string ek, double bw)
        {
            double duz = c.BitisMm - c.BaslangicMm;
            double solB = c.SolKanca ? c.SolKancaBMm : 0;
            double sagB = c.SagKanca ? c.SagKancaBMm : 0;
            double solYay = c.SolKanca ? KirisFormuller.Yay90(c.CapMm) : 0;
            double sagYay = c.SagKanca ? KirisFormuller.Yay90(c.CapMm) : 0;
            string ad = RolAdi(c.Rol);
            if (c.Rol == DonatiRolu.Govde && bw > 0)
                ad = x <= bw / 2.0 ? "Gövde (sol yüz)" : "Gövde (sağ yüz)";
            return new CubukDokumu
            {
                No = no,
                Ad = ad,
                Rol = c.Rol,
                Sira = sira,
                CapMm = c.CapMm,
                XMm = x,
                YAlttanMm = y,
                BaslangicMm = c.BaslangicMm,
                BitisMm = c.BitisMm,
                DuzBoyMm = duz,
                SolKancaBMm = solB,
                SagKancaBMm = sagB,
                SolYayMm = solYay,
                SagYayMm = sagYay,
                ToplamKesimMm = duz + solB + sagB + solYay + sagYay,
                EkNotu = ek ?? ""
            };
        }

        static string EkAl(List<EkYeri> ekler, BoyunaCubukCizim c)
        {
            for (int i = 0; i < ekler.Count; i++)
            {
                EkYeri e = ekler[i];
                if (e.Rol != c.Rol) continue;
                if (Math.Abs(e.CapMm - c.CapMm) > 0.2) continue;
                ekler.RemoveAt(i);
                if (e.Manson) return e.Aciklama ?? "Manşon.";
                var t = new StringBuilder();
                if (e.L0Mm > 0)
                {
                    t.Append("Bindirme ℓ0 = ");
                    t.Append(Mm(e.L0Mm));
                    t.Append(" mm");
                    if (e.BitisMm > e.BaslangicMm)
                    {
                        t.Append(", bölge ");
                        t.Append(Mm(e.BaslangicMm));
                        t.Append("–");
                        t.Append(Mm(e.BitisMm));
                        t.Append(" mm");
                    }
                    t.Append('.');
                }
                if (!string.IsNullOrWhiteSpace(e.Aciklama))
                {
                    if (t.Length > 0) t.Append(' ');
                    t.Append(e.Aciklama.Trim());
                }
                return t.ToString();
            }
            return "";
        }

        static List<YerlesenCubuk> Konumlar(KirisDetaySonuc s, DonatiRolu rol)
        {
            var list = new List<YerlesenCubuk>();
            KesitYeri yer = KesitYeri.Aciklik;
            bool ust = true;
            bool govde = rol == DonatiRolu.Govde;
            if (rol == DonatiRolu.UstSolIlave || rol == DonatiRolu.AltSolIlave) yer = KesitYeri.SolMesnet;
            else if (rol == DonatiRolu.UstSagIlave || rol == DonatiRolu.AltSagIlave) yer = KesitYeri.SagMesnet;
            if (rol == DonatiRolu.AltDuz || rol == DonatiRolu.AltSolIlave || rol == DonatiRolu.AltSagIlave || rol == DonatiRolu.AltIlaveSurekli)
                ust = false;
            KesitSonuc k = s.Kesit(yer);
            if (k == null) return list;
            if (govde)
            {
                if (k.Govde != null) list.AddRange(k.Govde);
                return list;
            }
            YuzYerlesim yuz = ust ? k.Ust : k.Alt;
            if (yuz == null || yuz.Cubuklar == null) return list;
            for (int i = 0; i < yuz.Cubuklar.Count; i++)
            {
                if (yuz.Cubuklar[i].Rol == rol) list.Add(yuz.Cubuklar[i]);
            }
            return list;
        }

        public static string RolAdi(DonatiRolu rol)
        {
            switch (rol)
            {
                case DonatiRolu.Montaj: return "Montaj";
                case DonatiRolu.AltDuz: return "Alt düz";
                case DonatiRolu.UstSolIlave: return "Sol üst ilave";
                case DonatiRolu.UstSagIlave: return "Sağ üst ilave";
                case DonatiRolu.AltSolIlave: return "Sol alt ilave";
                case DonatiRolu.AltSagIlave: return "Sağ alt ilave";
                case DonatiRolu.Govde: return "Gövde";
                case DonatiRolu.UstIlaveSurekli: return "Üst ilave (sürekli)";
                case DonatiRolu.AltIlaveSurekli: return "Alt ilave (sürekli)";
                default: return rol.ToString();
            }
        }

        static string DyAd(SuneklikDuzeyi? d)
        {
            if (d == null) return "verilmedi";
            return d.Value == SuneklikDuzeyi.Yuksek ? "yüksek" : "sınırlı";
        }

        static string DtsAd(DepremTasarimSinifi? d)
        {
            if (d == null) return "DTS verilmedi";
            string s = d.Value.ToString();
            if (s.StartsWith("Dts", StringComparison.Ordinal)) s = s.Substring(3);
            if (s.EndsWith("a", StringComparison.Ordinal) && s.Length > 1)
                return "DTS " + s.Substring(0, s.Length - 1) + "a";
            return "DTS " + s;
        }

        static string Mm(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
