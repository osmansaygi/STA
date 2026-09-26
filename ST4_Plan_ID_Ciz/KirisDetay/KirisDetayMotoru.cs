using System;
using System.Collections.Generic;

namespace ST4PlanIdCiz.KirisDetay
{
    /// <summary>
    /// Kiriş donatı detay motoru (TS 500:2000 + TBDY 2018).
    /// Çizim kodu <see cref="Hesapla"/> sonucundaki kesit çubuklarını, boyuna açılımı ve etriye konumlarını kullanır.
    /// Mevcut plan çizimi (kiriş konturu) ve KIRISDUZELT bu hesabı yapmaz; birimler burada mm'dir.
    /// Ayrıntı: docs/kiris_detay_kurallari.md
    /// </summary>
    public static class KirisDetayMotoru
    {
        sealed class LbKaydi
        {
            public DonatiRolu Rol;
            public DonatiGrubu Grup;
            public bool KonumI;
            public bool KOrtu;
            public double Lb0Ham;
            public double LbHam;
            public double Lb;
        }

        /// <summary>
        /// Girdideki geometri ve çubuk adet/çaplarından kesit yerleşimi, kenetlenme, kesim boyu ve etriyeyi hesaplar.
        /// </summary>
        public static KirisDetaySonuc Hesapla(KirisDetayGirdi girdi)
        {
            var sonuc = new KirisDetaySonuc();
            if (girdi == null)
            {
                Ekle(sonuc, KirisKuralKodu.Girdi, "", KontrolSeviyesi.Hata, "Girdi boş.");
                return Bitir(sonuc, null);
            }

            KirisDetayAyarlari ayar = girdi.Ayarlar ?? new KirisDetayAyarlari();
            KirisDonatiGirdisi don = girdi.Donati ?? new KirisDonatiGirdisi();
            if (ayar.Suneklik == null)
            {
                Ekle(sonuc, KirisKuralKodu.Girdi, "TBDY 7.4 / 7.8", KontrolSeviyesi.Hata,
                    "Süneklik düzeyi (DY yüksek / sınırlı) proje girdisidir; ayarlanmadan hesap yapılmaz.");
            }
            if (ayar.Dts == null)
            {
                Ekle(sonuc, KirisKuralKodu.Girdi, "TBDY 7.4.2.3", KontrolSeviyesi.Hata,
                    "Deprem tasarım sınıfı (DTS) proje girdisidir; ayarlanmadan hesap yapılmaz.");
            }
            if (sonuc.Kontroller.Exists(k => k.Seviye == KontrolSeviyesi.Hata))
            {
                OkumaNotlariniAl(sonuc, girdi);
                return Bitir(sonuc, girdi);
            }
            OkumaNotlariniAl(sonuc, girdi);

            SuneklikDuzeyi dy = ayar.Suneklik.Value;
            DepremTasarimSinifi dts = ayar.Dts.Value;

            double fck, fctk, ec, k1;
            KirisFormuller.BetonSatiri(ayar.Beton, out fck, out fctk, out ec, out k1);
            double fctdFormul;
            double fctd = KirisFormuller.Fctd(ayar.Beton, ayar.FctdKaynagi, ayar.GammaMc, out fctdFormul);
            double fcd = fck / ayar.GammaMc;
            double fyd = KirisFormuller.Fyd(ayar.Celik, ayar.GammaMs);
            double fywd = KirisFormuller.Fyd(ayar.EtriyeCelik, ayar.GammaMs);
            double rhoMin = KirisFormuller.RhoMin(fctd, fyd);
            double rhoB = KirisFormuller.RhoB(k1, fcd, fyd);

            sonuc.Malzeme = new MalzemeSonuc
            {
                Beton = ayar.Beton,
                Celik = ayar.Celik,
                Fck = fck,
                Fcd = fcd,
                Fctk = fctk,
                Fctd = fctd,
                FctdFormul = fctdFormul,
                Ec = ec,
                K1 = k1,
                Fyk = KirisFormuller.Fyk(ayar.Celik),
                Fyd = fyd,
                Fywd = fywd,
                RhoMin = rhoMin,
                RhoB = rhoB,
                CevreSinifiAdi = CevreAdi(ayar.Cevre)
            };

            // TBDY 7.2.5.1
            if (ayar.TbdyKapsaminda && ayar.Beton == BetonSinifi.C20)
            {
                Ekle(sonuc, KirisKuralKodu.Mat, "TBDY 7.2.5.1", KontrolSeviyesi.Hata,
                    "TBDY kapsamındaki binada C25'ten düşük beton kullanılamaz.");
            }

            // TBDY 4.3.4
            if (dy == SuneklikDuzeyi.Sinirli && !KirisFormuller.DySinirliIzinli(dts))
            {
                Ekle(sonuc, KirisKuralKodu.G12, "TBDY 4.3.4.1, 4.3.4.3", KontrolSeviyesi.Hata,
                    "Süneklik düzeyi sınırlı çerçeve yalnız DTS 3 ve 4'te kullanılabilir.");
            }

            double ccMin = KirisFormuller.CcMin(ayar.Cevre);
            double cc = ayar.CcMm;
            if (cc < ccMin - 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.Cc, "TS500 7.3, 9.5.1 Çizelge 9.3", KontrolSeviyesi.Uyari,
                    "Net örtü " + cc.ToString("0.#") + " mm, minimum " + ccMin.ToString("0") + " mm. Minimum uygulandı.");
                cc = ccMin;
            }
            sonuc.CcKullanilanMm = cc;

            bool istisna = ayar.MafsalliKiris || ayar.BagKirisi || ayar.IkincilKiris;
            GeometriKontrol(sonuc, girdi, ayar, fck, istisna);

            CapKontrol(sonuc, don);
            if (don.EtriyeCapMm < 8 - 1e-9)
            {
                Ekle(sonuc, KirisKuralKodu.StCap, "TBDY 7.4.4, 7.8.4", KontrolSeviyesi.Hata,
                    "Etriye çapı en az 8 mm olmalıdır. Girilen çap değiştirilmedi.");
            }
            if (don.Montaj == null || don.Montaj.Adet < 2)
            {
                Ekle(sonuc, KirisKuralKodu.SurekliAdet, "TBDY 7.4.2.2", KontrolSeviyesi.Hata,
                    "Üstte kiriş boyunca en az 2 sürekli çubuk (montaj) olmalıdır.");
            }
            if (don.AltDuz == null || don.AltDuz.Adet < 2)
            {
                Ekle(sonuc, KirisKuralKodu.SurekliAdet, "TBDY 7.4.2.2", KontrolSeviyesi.Hata,
                    "Altta kiriş boyunca en az 2 sürekli çubuk (düz) olmalıdır.");
            }

            if (girdi.BwMm <= 0 || girdi.HMm <= 0 || girdi.LnMm <= 0)
                return Bitir(sonuc, girdi);

            bool kOrtuUst = CcPhiKucuk(cc, don.Montaj) || CcPhiKucuk(cc, don.UstSolIlave) || CcPhiKucuk(cc, don.UstSagIlave);
            bool kOrtuAlt = CcPhiKucuk(cc, don.AltDuz) || CcPhiKucuk(cc, don.AltSolIlave) || CcPhiKucuk(cc, don.AltSagIlave);

            List<LbKaydi> kayitlar = null;
            bool ustBirlesti = false;
            bool altBirlesti = false;
            var ilave = new IlaveBoySonuc();

            for (int pass = 0; pass < 3; pass++)
            {
                kayitlar = LbHesapla(don, ayar, fyd, fctd, kOrtuUst, kOrtuAlt);
                ilave = IlaveHesapla(girdi, ayar, don, kayitlar);
                ustBirlesti = ilave.UstBirlesti;
                altBirlesti = ilave.AltBirlesti;
                sonuc.Kesitler = KesitleriYerlestir(girdi, ayar, don, cc, ustBirlesti, altBirlesti);
                bool yeniUst = kOrtuUst || YuzdeKOrtu(sonuc.Kesitler, true);
                bool yeniAlt = kOrtuAlt || YuzdeKOrtu(sonuc.Kesitler, false);
                if (yeniUst == kOrtuUst && yeniAlt == kOrtuAlt)
                    break;
                kOrtuUst = yeniUst;
                kOrtuAlt = yeniAlt;
            }

            sonuc.Ilave = ilave;
            double dGov = double.MaxValue;
            for (int i = 0; i < sonuc.Kesitler.Count; i++)
            {
                if (sonuc.Kesitler[i].DCekmeMm > 0)
                    dGov = Math.Min(dGov, sonuc.Kesitler[i].DCekmeMm);
            }
            if (double.IsInfinity(dGov) || dGov > 1e8) dGov = girdi.HMm - cc;
            sonuc.DMm = dGov;

            for (int i = 0; i < sonuc.Kesitler.Count; i++)
            {
                KesitSonuc k = sonuc.Kesitler[i];
                if ((k.Ust != null && k.Ust.Sigmadi) || (k.Alt != null && k.Alt.Sigmadi))
                {
                    Ekle(sonuc, KirisKuralKodu.Sp, "TS500 9.5.2", KontrolSeviyesi.Hata,
                        KesitAdi(k.Yer) + " kesitinde çubuklar net aralığa sığmıyor. Adet değiştirilmedi.");
                }
                YuzUyari(sonuc, k, true);
                YuzUyari(sonuc, k, false);
            }

            OranKontrol(sonuc, girdi, ayar, don, dts, rhoMin, rhoB, ustBirlesti, altBirlesti, ilave);
            sonuc.Kenetlenme = KenetlenmeKur(girdi, ayar, don, kayitlar, ilave);
            KenarKontrol(sonuc, girdi);

            sonuc.Govde = GovdeHesapla(sonuc, girdi, ayar, don, cc, istisna, dGov);
            GovdeYerlestir(sonuc, girdi, cc, don.EtriyeCapMm);

            sonuc.Etriye = EtriyeHesapla(sonuc, girdi, ayar, don, dy, cc, dGov, fctd, fywd);
            sonuc.BoyunaCubuklar = BoyunaCubuklar(girdi, don, sonuc.Kenetlenme, ilave, kayitlar);
            GovdeBoyunaEkle(sonuc, girdi, ayar, cc, fyd, fctd, kayitlar);
            EkleriEkle(sonuc, girdi, ayar, kayitlar);
            if (ayar.DolayliMesnet)
            {
                Ekle(sonuc, "R-G-08", "TS500 8.1.6", KontrolSeviyesi.Uyari,
                    "Dolaylı mesnet var. Askı donatısı düzenlenmeli; TS500 miktar formülü vermez.");
            }

            return Bitir(sonuc, girdi);
        }

        static KirisDetaySonuc Bitir(KirisDetaySonuc sonuc, KirisDetayGirdi girdi)
        {
            sonuc.Gecerli = true;
            for (int i = 0; i < sonuc.Kontroller.Count; i++)
            {
                if (sonuc.Kontroller[i].Seviye == KontrolSeviyesi.Hata)
                {
                    sonuc.Gecerli = false;
                    break;
                }
            }
            sonuc.Cubuklar = KirisOzet.Dokum(girdi, sonuc);
            sonuc.OzetMetni = KirisOzet.Metin(girdi, sonuc);
            return sonuc;
        }

        static void OkumaNotlariniAl(KirisDetaySonuc sonuc, KirisDetayGirdi girdi)
        {
            if (girdi.OkumaHatalari != null)
            {
                for (int i = 0; i < girdi.OkumaHatalari.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(girdi.OkumaHatalari[i]))
                        Ekle(sonuc, KirisKuralKodu.Girdi, "", KontrolSeviyesi.Hata, girdi.OkumaHatalari[i]);
                }
            }
            if (girdi.OkumaNotlari != null)
            {
                for (int i = 0; i < girdi.OkumaNotlari.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(girdi.OkumaNotlari[i]))
                        Ekle(sonuc, KirisKuralKodu.Girdi, "", KontrolSeviyesi.Bilgi, girdi.OkumaNotlari[i]);
                }
            }
        }

        static void Ekle(KirisDetaySonuc sonuc, string kod, string madde, KontrolSeviyesi seviye, string mesaj)
        {
            for (int i = 0; i < sonuc.Kontroller.Count; i++)
            {
                KuralKontrol varOlan = sonuc.Kontroller[i];
                if (varOlan.Kod == kod && varOlan.Seviye == seviye && varOlan.Mesaj == mesaj)
                    return;
            }
            sonuc.Kontroller.Add(new KuralKontrol
            {
                Kod = kod,
                Madde = madde,
                Seviye = seviye,
                Mesaj = mesaj
            });
        }

        static void YuzUyari(KirisDetaySonuc sonuc, KesitSonuc k, bool ust)
        {
            YuzYerlesim y = ust ? k.Ust : k.Alt;
            if (y == null) return;
            string ad = KesitAdi(k.Yer) + (ust ? " üst" : " alt");
            if (y.SiraSayisi > 1)
            {
                Ekle(sonuc, KirisKuralKodu.Sp, "TS500 9.5.2", KontrolSeviyesi.Uyari,
                    ad + " yüzünde çubuklar tek sıraya sığmadı (" + y.SiraSayisi + " sıra). Adet değiştirilmedi.");
            }
            if (y.SiraSayisi > 2)
            {
                Ekle(sonuc, KirisKuralKodu.Sp, "TS500 7.3", KontrolSeviyesi.Uyari,
                    ad + " yüzünde ikiden fazla sıra var; faydalı yükseklik azalır.");
            }
            if (!KoseSurekli(y))
            {
                Ekle(sonuc, KirisKuralKodu.SurekliAdet, "TBDY 7.4.2.2", KontrolSeviyesi.Uyari,
                    ad + " yüzünde köşe çubuğu sürekli donatı değil. Adet değiştirilmedi.");
            }
        }

        static bool KoseSurekli(YuzYerlesim y)
        {
            if (y == null || y.Cubuklar == null || y.Cubuklar.Count == 0) return true;
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            int n = 0;
            for (int i = 0; i < y.Cubuklar.Count; i++)
            {
                if (y.Cubuklar[i].Sira != 1) continue;
                n++;
                if (y.Cubuklar[i].XMm < minX) minX = y.Cubuklar[i].XMm;
                if (y.Cubuklar[i].XMm > maxX) maxX = y.Cubuklar[i].XMm;
            }
            if (n == 0) return true;
            bool sol = false;
            bool sag = false;
            for (int i = 0; i < y.Cubuklar.Count; i++)
            {
                YerlesenCubuk c = y.Cubuklar[i];
                if (c.Sira != 1 || !c.Surekli || !c.Kose) continue;
                if (Math.Abs(c.XMm - minX) < 0.2) sol = true;
                if (Math.Abs(c.XMm - maxX) < 0.2) sag = true;
            }
            if (n == 1) return sol;
            return sol && sag;
        }

        static string CevreAdi(KirisCevre c)
        {
            if (c == KirisCevre.Ic) return "yapı içi";
            if (c == KirisCevre.ZeminleTemas) return "zeminle temas";
            return "dış etkilere açık";
        }

        static string KesitAdi(KesitYeri y)
        {
            if (y == KesitYeri.SolMesnet) return "Sol mesnet";
            if (y == KesitYeri.SagMesnet) return "Sağ mesnet";
            return "Açıklık";
        }

        static void GeometriKontrol(KirisDetaySonuc sonuc, KirisDetayGirdi g, KirisDetayAyarlari ayar, double fck, bool istisna)
        {
            // TBDY 7.4.1.1(a) bw ≥ 250; TS500 7.3 bw ≥ 200. İstisnada (d) TBDY zorunlu değil.
            if (g.BwMm < 200 - 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.G01, "TS500 7.3", KontrolSeviyesi.Hata,
                    "Gövde genişliği 200 mm'den küçük.");
            }
            else if (g.BwMm < 250 - 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.G01, "TBDY 7.4.1.1(a)", istisna ? KontrolSeviyesi.Bilgi : KontrolSeviyesi.Hata,
                    "Gövde genişliği 250 mm'den küçük.");
            }

            double hMin = Math.Max(300, 3 * g.TMm);
            if (g.HMm < hMin - 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.G03, "TBDY 7.4.1.1(b)", istisna ? KontrolSeviyesi.Bilgi : KontrolSeviyesi.Hata,
                    "Yükseklik en az " + hMin.ToString("0") + " mm olmalı (max(300, 3t)).");
            }
            if (g.BwMm > 0 && g.HMm > 3.5 * g.BwMm + 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.G04, "TBDY 7.4.1.1(b)", istisna ? KontrolSeviyesi.Bilgi : KontrolSeviyesi.Hata,
                    "Yükseklik 3.5·bw değerini aşıyor.");
            }

            BwMaxKontrol(sonuc, g, g.Sol, "Sol", istisna);
            BwMaxKontrol(sonuc, g, g.Sag, "Sağ", istisna);

            if (g.HMm > 0 && g.LnMm < 2.5 * g.HMm)
            {
                Ekle(sonuc, KirisKuralKodu.G05, "TS500 7.3", KontrolSeviyesi.Uyari,
                    "ℓn < 2.5·h. Sürekli yüksek kiriş olarak ayrıca tasarlanır; bu motorun kapsamı dışında.");
            }

            if (g.EksenelKuvvetN.HasValue && g.BwMm > 0 && g.HMm > 0)
            {
                double ac = g.BwMm * g.HMm;
                double limit = 0.10 * ac * fck;
                if (g.EksenelKuvvetN.Value > limit)
                {
                    Ekle(sonuc, KirisKuralKodu.G06, "TBDY 7.4.1.2", KontrolSeviyesi.Hata,
                        "Eksenel kuvvet 0.10·Ac·fck sınırını aşıyor; eleman kolon olarak boyutlandırılır.");
                }
            }
        }

        static void BwMaxKontrol(KirisDetaySonuc sonuc, KirisDetayGirdi g, KirisMesnet m, string ad, bool istisna)
        {
            if (m == null || m.BDikMm <= 0) return;
            double ust = g.HMm + m.BDikMm;
            if (g.BwMm > ust + 1e-6)
            {
                Ekle(sonuc, KirisKuralKodu.G02, "TBDY 7.4.1.1(a)", istisna ? KontrolSeviyesi.Bilgi : KontrolSeviyesi.Hata,
                    ad + " mesnette bw, h + kolon dik genişliğini aşıyor.");
            }
        }

        static void CapKontrol(KirisDetaySonuc sonuc, KirisDonatiGirdisi don)
        {
            DonatiGrubu[] gruplar = { don.Montaj, don.AltDuz, don.UstSolIlave, don.UstSagIlave, don.AltSolIlave, don.AltSagIlave };
            string[] adlar = { "Montaj", "Alt düz", "Üst sol ilave", "Üst sağ ilave", "Alt sol ilave", "Alt sağ ilave" };
            for (int i = 0; i < gruplar.Length; i++)
            {
                DonatiGrubu gr = gruplar[i];
                if (gr == null || !gr.Var) continue;
                if (gr.CapMm < 12 - 1e-9)
                {
                    Ekle(sonuc, KirisKuralKodu.Cap, "TS500 7.3, TBDY 7.4.2.2", KontrolSeviyesi.Hata,
                        adlar[i] + " çapı 12 mm'den küçük.");
                }
                if (gr.Demet > 3)
                {
                    Ekle(sonuc, "R-SP-06", "TS500 9.5.3", KontrolSeviyesi.Hata,
                        adlar[i] + " demetinde en çok 3 çubuk olabilir.");
                }
                if (gr.Demet > 1 && gr.CapMm > 0)
                {
                    Ekle(sonuc, "R-SP-06", "TS500 9.5.3", KontrolSeviyesi.Bilgi,
                        adlar[i] + " demet eşdeğer çap φe = " + gr.GeometrikCapMm.ToString("0.#") + " mm.");
                }
            }
        }

        static bool CcPhiKucuk(double cc, DonatiGrubu g)
        {
            return g != null && g.Var && cc < g.GeometrikCapMm - 1e-9;
        }

        static bool YuzdeKOrtu(List<KesitSonuc> kesitler, bool ust)
        {
            for (int i = 0; i < kesitler.Count; i++)
            {
                YuzYerlesim y = ust ? kesitler[i].Ust : kesitler[i].Alt;
                if (y == null || !y.SNetVar) continue;
                for (int c = 0; c < y.Cubuklar.Count; c++)
                {
                    if (y.SNetMm < 1.5 * y.Cubuklar[c].CapMm - 1e-6)
                        return true;
                }
            }
            return false;
        }

        static List<LbKaydi> LbHesapla(
            KirisDonatiGirdisi don,
            KirisDetayAyarlari ayar,
            double fyd,
            double fctd,
            bool kOrtuUst,
            bool kOrtuAlt)
        {
            var list = new List<LbKaydi>();
            EkleLb(list, DonatiRolu.Montaj, don.Montaj, true, kOrtuUst, ayar, fyd, fctd);
            EkleLb(list, DonatiRolu.UstSolIlave, don.UstSolIlave, true, kOrtuUst, ayar, fyd, fctd);
            EkleLb(list, DonatiRolu.UstSagIlave, don.UstSagIlave, true, kOrtuUst, ayar, fyd, fctd);
            EkleLb(list, DonatiRolu.AltDuz, don.AltDuz, false, kOrtuAlt, ayar, fyd, fctd);
            EkleLb(list, DonatiRolu.AltSolIlave, don.AltSolIlave, false, kOrtuAlt, ayar, fyd, fctd);
            EkleLb(list, DonatiRolu.AltSagIlave, don.AltSagIlave, false, kOrtuAlt, ayar, fyd, fctd);
            return list;
        }

        static void EkleLb(
            List<LbKaydi> list,
            DonatiRolu rol,
            DonatiGrubu grup,
            bool konumI,
            bool kOrtu,
            KirisDetayAyarlari ayar,
            double fyd,
            double fctd)
        {
            if (grup == null || !grup.Var) return;
            double phi = grup.GeometrikCapMm;
            bool kAs = ayar.KAsAzaltmaUygula && ayar.Suneklik == SuneklikDuzeyi.Sinirli;
            double lb0 = KirisFormuller.Lb0(fyd, fctd, phi, ayar.Nervurlu);
            double ham = KirisFormuller.LbHam(lb0, phi, konumI, kOrtu, ayar.KonumKatsayisiUygula, kAs, ayar.KAs);
            list.Add(new LbKaydi
            {
                Rol = rol,
                Grup = grup,
                KonumI = konumI,
                KOrtu = kOrtu,
                Lb0Ham = lb0,
                LbHam = ham,
                Lb = KirisFormuller.EnYakinMm(ham)
            });
        }

        static LbKaydi Bul(List<LbKaydi> list, DonatiRolu rol)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Rol == rol) return list[i];
            }
            return null;
        }

        static IlaveBoySonuc IlaveHesapla(
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            List<LbKaydi> kayitlar)
        {
            var ilave = new IlaveBoySonuc();
            bool solAra = g.Sol != null && g.Sol.Tur == MesnetTuru.Ara;
            bool sagAra = g.Sag != null && g.Sag.Tur == MesnetTuru.Ara;
            ilave.LnRefSolMm = solAra && g.LnKomsuSolMm.HasValue ? Math.Max(g.LnMm, g.LnKomsuSolMm.Value) : g.LnMm;
            ilave.LnRefSagMm = sagAra && g.LnKomsuSagMm.HasValue ? Math.Max(g.LnMm, g.LnKomsuSagMm.Value) : g.LnMm;

            double phiRef = 16;
            if (don.Montaj != null && don.Montaj.Var) phiRef = don.Montaj.GeometrikCapMm;
            double d = g.HMm - ayar.CcMm - don.EtriyeCapMm - phiRef / 2.0;
            if (d < 1) d = g.HMm * 0.9;

            LbKaydi ustSol = Bul(kayitlar, DonatiRolu.UstSolIlave);
            LbKaydi ustSag = Bul(kayitlar, DonatiRolu.UstSagIlave);
            LbKaydi altSol = Bul(kayitlar, DonatiRolu.AltSolIlave);
            LbKaydi altSag = Bul(kayitlar, DonatiRolu.AltSagIlave);

            if (ustSol != null)
                ilave.UstSolAciklikMm = UstKesim(ayar, ustSol, ilave.LnRefSolMm, g.KuramsalUstSolMm, d);
            if (ustSag != null)
                ilave.UstSagAciklikMm = UstKesim(ayar, ustSag, ilave.LnRefSagMm, g.KuramsalUstSagMm, d);
            if (altSol != null)
                ilave.AltSolAciklikMm = AltKesim(ayar, altSol, g.KuramsalAltSolMm, d);
            if (altSag != null)
                ilave.AltSagAciklikMm = AltKesim(ayar, altSag, g.KuramsalAltSagMm, d);

            double l0Ust = 0;
            if (ustSol != null) l0Ust = Math.Max(l0Ust, L0Degeri(ustSol, 1, ayar));
            if (ustSag != null) l0Ust = Math.Max(l0Ust, L0Degeri(ustSag, 1, ayar));
            double l0Alt = 0;
            if (altSol != null) l0Alt = Math.Max(l0Alt, L0Degeri(altSol, 1, ayar));
            if (altSag != null) l0Alt = Math.Max(l0Alt, L0Degeri(altSag, 1, ayar));

            ilave.UstBirlesti = Birlestir(g.LnMm, ayar, ilave.UstSolAciklikMm, ilave.UstSagAciklikMm,
                don.UstSolIlave != null && don.UstSolIlave.Var, don.UstSagIlave != null && don.UstSagIlave.Var, l0Ust);
            ilave.AltBirlesti = Birlestir(g.LnMm, ayar, ilave.AltSolAciklikMm, ilave.AltSagAciklikMm,
                don.AltSolIlave != null && don.AltSolIlave.Var, don.AltSagIlave != null && don.AltSagIlave.Var, l0Alt);

            if (ayar.IkinciSiraAyriLn && ustSol != null && !g.KuramsalUstSolMm.HasValue)
            {
                double eski = ayar.KLn;
                ayar.KLn = ayar.IkinciSiraKLn;
                ilave.UstSira2SolMm = UstKesim(ayar, ustSol, ilave.LnRefSolMm, null, d);
                ayar.KLn = eski;
            }
            return ilave;
        }

        static double UstKesim(KirisDetayAyarlari ayar, LbKaydi lb, double lnRef, double? kuramsal, double d)
        {
            double phi = lb.Grup.GeometrikCapMm;
            double L;
            if (kuramsal.HasValue)
            {
                double carpan = ayar.Nervurlu ? ayar.KesimPhiCarpanNervurlu : ayar.KesimPhiCarpanDuz;
                double asim = Math.Max(d, carpan * phi);
                if (ayar.KesimdeLbDahil) asim = Math.Max(asim, lb.Lb);
                L = kuramsal.Value + asim;
            }
            else
            {
                L = Math.Max(ayar.KLn * lnRef, lb.Lb);
                if (ayar.LMinKullaniciMm > 0) L = Math.Max(L, ayar.LMinKullaniciMm);
            }
            return KirisFormuller.YukariYuvarla(L, ayar.KesimBoyuYuvarlamaMm);
        }

        static double AltKesim(KirisDetayAyarlari ayar, LbKaydi lb, double? kuramsal, double d)
        {
            double phi = lb.Grup.GeometrikCapMm;
            double L;
            if (kuramsal.HasValue)
            {
                double carpan = ayar.Nervurlu ? ayar.KesimPhiCarpanNervurlu : ayar.KesimPhiCarpanDuz;
                double asim = Math.Max(d, carpan * phi);
                if (ayar.KesimdeLbDahil) asim = Math.Max(asim, lb.Lb);
                L = kuramsal.Value + asim;
            }
            else
            {
                L = Math.Max(lb.Lb, ayar.AltIlavePhiCarpan * phi);
            }
            return KirisFormuller.YukariYuvarla(L, ayar.KesimBoyuYuvarlamaMm);
        }

        static bool Birlestir(double ln, KirisDetayAyarlari ayar, double lSol, double lSag, bool solVar, bool sagVar, double l0)
        {
            if (!solVar || !sagVar) return false;
            double bosluk = ln - lSol - lSag;
            if (bosluk <= 0) return true;
            if (bosluk < ayar.KisaAciklikBoslukMinMm) return true;
            if (ayar.KisaAciklikL0Carpani > 0 && l0 > 0 && bosluk < ayar.KisaAciklikL0Carpani * l0) return true;
            return false;
        }

        static double L0Degeri(LbKaydi lb, double r, KirisDetayAyarlari ayar)
        {
            double alpha = KirisFormuller.Alpha1(r, ayar.TamKesitCekme);
            double ham = KirisFormuller.L0Ham(lb.LbHam, alpha, false, lb.Grup.Demet > 1);
            return KirisFormuller.EnYakinMm(ham);
        }

        static List<KesitSonuc> KesitleriYerlestir(
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            double cc,
            bool ustBirlesti,
            bool altBirlesti)
        {
            var list = new List<KesitSonuc>();
            list.Add(TekKesit(KesitYeri.SolMesnet, g, ayar, don, cc, ustBirlesti, altBirlesti, true, false));
            list.Add(TekKesit(KesitYeri.Aciklik, g, ayar, don, cc, ustBirlesti, altBirlesti, false, false));
            list.Add(TekKesit(KesitYeri.SagMesnet, g, ayar, don, cc, ustBirlesti, altBirlesti, false, true));
            return list;
        }

        static KesitSonuc TekKesit(
            KesitYeri yer,
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            double cc,
            bool ustBirlesti,
            bool altBirlesti,
            bool solIlave,
            bool sagIlave)
        {
            var kesit = new KesitSonuc { Yer = yer };
            var ug = new List<DonatiGrubu>();
            var ur = new List<DonatiRolu>();
            var us = new List<bool>();
            EkleG(ug, ur, us, don.Montaj, DonatiRolu.Montaj, true);
            if (ustBirlesti)
                EkleG(ug, ur, us, BirlestirGrup(don.UstSolIlave, don.UstSagIlave), DonatiRolu.UstIlaveSurekli, true);
            else
            {
                if (solIlave) EkleG(ug, ur, us, don.UstSolIlave, DonatiRolu.UstSolIlave, false);
                if (sagIlave) EkleG(ug, ur, us, don.UstSagIlave, DonatiRolu.UstSagIlave, false);
            }

            var ag = new List<DonatiGrubu>();
            var ar = new List<DonatiRolu>();
            var asu = new List<bool>();
            EkleG(ag, ar, asu, don.AltDuz, DonatiRolu.AltDuz, true);
            if (altBirlesti)
                EkleG(ag, ar, asu, BirlestirGrup(don.AltSolIlave, don.AltSagIlave), DonatiRolu.AltIlaveSurekli, true);
            else
            {
                if (solIlave) EkleG(ag, ar, asu, don.AltSolIlave, DonatiRolu.AltSolIlave, false);
                if (sagIlave) EkleG(ag, ar, asu, don.AltSagIlave, DonatiRolu.AltSagIlave, false);
            }

            kesit.Ust = KirisYerlesim.Yerlestir(ug, ur, us, g.BwMm, g.HMm, cc, don.EtriyeCapMm, ayar.DmaxMm,
                ayar.NetAralikMutlakMinMm, ayar.KoseKivrimDuzeltmesi, ayar.EtriyeKancaIcCapCarpan, true);
            kesit.Alt = KirisYerlesim.Yerlestir(ag, ar, asu, g.BwMm, g.HMm, cc, don.EtriyeCapMm, ayar.DmaxMm,
                ayar.NetAralikMutlakMinMm, ayar.KoseKivrimDuzeltmesi, ayar.EtriyeKancaIcCapCarpan, false);
            kesit.DCekmeMm = yer == KesitYeri.Aciklik ? kesit.Alt.DMm : kesit.Ust.DMm;
            return kesit;
        }

        static void EkleG(List<DonatiGrubu> g, List<DonatiRolu> r, List<bool> s, DonatiGrubu gr, DonatiRolu rol, bool surekli)
        {
            if (gr == null || !gr.Var) return;
            g.Add(gr);
            r.Add(rol);
            s.Add(surekli);
        }

        static DonatiGrubu BirlestirGrup(DonatiGrubu a, DonatiGrubu b)
        {
            int adetA = a != null && a.Var ? a.Adet : 0;
            int adetB = b != null && b.Var ? b.Adet : 0;
            double capA = a != null && a.Var ? a.CapMm : 0;
            double capB = b != null && b.Var ? b.CapMm : 0;
            int demA = a != null ? a.Demet : 1;
            int demB = b != null ? b.Demet : 1;
            var gr = new DonatiGrubu(Math.Max(adetA, adetB), Math.Max(capA, capB));
            gr.DemetAdet = Math.Max(demA, demB);
            return gr;
        }

        static double AsGrup(DonatiGrubu g)
        {
            if (g == null || !g.Var) return 0;
            return g.AlanMm2;
        }

        static double AsUst(KirisDonatiGirdisi don, KesitYeri yer, bool birlesti)
        {
            double a = AsGrup(don.Montaj);
            if (birlesti) return a + AsGrup(BirlestirGrup(don.UstSolIlave, don.UstSagIlave));
            if (yer == KesitYeri.SolMesnet) a += AsGrup(don.UstSolIlave);
            if (yer == KesitYeri.SagMesnet) a += AsGrup(don.UstSagIlave);
            return a;
        }

        static double AsAlt(KirisDonatiGirdisi don, KesitYeri yer, bool birlesti)
        {
            double a = AsGrup(don.AltDuz);
            if (birlesti) return a + AsGrup(BirlestirGrup(don.AltSolIlave, don.AltSagIlave));
            if (yer == KesitYeri.SolMesnet) a += AsGrup(don.AltSolIlave);
            if (yer == KesitYeri.SagMesnet) a += AsGrup(don.AltSagIlave);
            return a;
        }

        static void OranKontrol(
            KirisDetaySonuc sonuc,
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            DepremTasarimSinifi dts,
            double rhoMin,
            double rhoB,
            bool ustBirlesti,
            bool altBirlesti,
            IlaveBoySonuc ilave)
        {
            double bw = g.BwMm;
            KesitYeri[] yerler = { KesitYeri.SolMesnet, KesitYeri.Aciklik, KesitYeri.SagMesnet };
            for (int i = 0; i < yerler.Length; i++)
            {
                KesitYeri yer = yerler[i];
                KesitSonuc kesit = sonuc.Kesit(yer);
                double dUst = kesit != null && kesit.Ust != null ? kesit.Ust.DMm : sonuc.DMm;
                double dAlt = kesit != null && kesit.Alt != null ? kesit.Alt.DMm : sonuc.DMm;
                double asU = AsUst(don, yer, ustBirlesti);
                double asA = AsAlt(don, yer, altBirlesti);
                RhoCifti(sonuc, KesitAdi(yer) + " üst", asU, asA, bw, dUst, rhoMin, rhoB);
                RhoCifti(sonuc, KesitAdi(yer) + " alt", asA, asU, bw, dAlt, rhoMin, rhoB);
            }

            double asUstSol = AsUst(don, KesitYeri.SolMesnet, ustBirlesti);
            double asUstSag = AsUst(don, KesitYeri.SagMesnet, ustBirlesti);
            double asMontaj = AsGrup(don.Montaj);
            double ceyrek = 0.25 * Math.Max(asUstSol, asUstSag);
            if (asMontaj + 1e-6 < ceyrek)
            {
                Ekle(sonuc, KirisKuralKodu.SurekliOran, "TBDY 7.4.3.1(a)", KontrolSeviyesi.Hata,
                    "Montaj donatısı, mesnet üst donatısının büyük olanının 1/4'ünden az.");
            }

            double asAciklikAlt = AsGrup(don.AltDuz);
            if (altBirlesti) asAciklikAlt = AsAlt(don, KesitYeri.Aciklik, true);
            else
            {
                if (ilave.AltSolAciklikMm >= g.LnMm / 2.0) asAciklikAlt += AsGrup(don.AltSolIlave);
                if (ilave.AltSagAciklikMm >= g.LnMm / 2.0) asAciklikAlt += AsGrup(don.AltSagIlave);
            }
            if (AsGrup(don.AltDuz) + 1e-6 < asAciklikAlt / 3.0)
            {
                Ekle(sonuc, KirisKuralKodu.AltUcTeBir, "TS500 7.3", KontrolSeviyesi.Hata,
                    "Açıklıktaki alt donatının en az 1/3'ü mesnede ulaşmıyor.");
            }

            double oran = KirisFormuller.DtsYuksekOran(dts) ? 0.50 : 0.30;
            KarsilastirMesnet(sonuc, "Sol", AsAlt(don, KesitYeri.SolMesnet, altBirlesti), asUstSol, oran);
            KarsilastirMesnet(sonuc, "Sağ", AsAlt(don, KesitYeri.SagMesnet, altBirlesti), asUstSag, oran);
        }

        static void RhoCifti(KirisDetaySonuc sonuc, string ad, double asCekme, double asBasinc, double bw, double d, double rhoMin, double rhoB)
        {
            if (asCekme <= 0 || bw <= 0 || d <= 0) return;
            double rho = asCekme / (bw * d);
            double rhoP = asBasinc / (bw * d);
            if (rho + 1e-9 < rhoMin)
            {
                Ekle(sonuc, KirisKuralKodu.RhoMin, "TS500 7.3 Denk. 7.3, TBDY 7.4.2.1", KontrolSeviyesi.Hata,
                    ad + " çekme oranı ρ=" + rho.ToString("0.00000") + " < ρmin=" + rhoMin.ToString("0.00000") + ".");
            }
            if (rho > 0.02 + 1e-9)
            {
                Ekle(sonuc, KirisKuralKodu.RhoMax, "TS500 Denk. 7.5, TBDY 7.4.2.4", KontrolSeviyesi.Hata,
                    ad + " çekme oranı %2'yi aşıyor.");
            }
            if (rho - rhoP > 0.85 * rhoB + 1e-9)
            {
                Ekle(sonuc, KirisKuralKodu.RhoMax, "TS500 Denk. 7.4", KontrolSeviyesi.Hata,
                    ad + " için ρ − ρ' , 0.85·ρb sınırını aşıyor.");
            }
        }

        static void KarsilastirMesnet(KirisDetaySonuc sonuc, string ad, double asAlt, double asUst, double oran)
        {
            if (asUst <= 0) return;
            if (asAlt + 1e-6 < oran * asUst)
            {
                Ekle(sonuc, KirisKuralKodu.MesnetOran, "TBDY 7.4.2.3", KontrolSeviyesi.Hata,
                    ad + " mesnette alt donatı, üst donatının " + (oran * 100).ToString("0") + "%'inden az.");
            }
        }

        static KenetlenmeSonuc KenetlenmeKur(
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            List<LbKaydi> kayitlar,
            IlaveBoySonuc ilave)
        {
            var k = new KenetlenmeSonuc();
            for (int i = 0; i < kayitlar.Count; i++)
            {
                LbKaydi lb = kayitlar[i];
                k.Gruplar.Add(new GrupKenetlenme
                {
                    Rol = lb.Rol,
                    CapMm = lb.Grup.GeometrikCapMm,
                    Lb0HamMm = lb.Lb0Ham,
                    LbHamMm = lb.LbHam,
                    LbMm = lb.Lb,
                    KonumI = lb.KonumI,
                    KOrtu = lb.KOrtu
                });
            }

            LbKaydi alt = Bul(kayitlar, DonatiRolu.AltDuz);
            LbKaydi ust = Bul(kayitlar, DonatiRolu.Montaj);
            if (alt != null)
            {
                k.LbAltMm = alt.Lb;
                k.LbkAltMm = KirisFormuller.EnYakinMm(0.75 * alt.LbHam);
                k.L0AltR05Mm = L0Degeri(alt, 0.5, ayar);
                k.L0AltR1Mm = L0Degeri(alt, 1, ayar);
            }
            if (ust != null)
            {
                k.LbUstMm = ust.Lb;
                k.L0UstR1Mm = L0Degeri(ust, 1, ayar);
            }

            double phiAlt = alt != null ? alt.Grup.GeometrikCapMm : 16;
            double phiUst = ust != null ? ust.Grup.GeometrikCapMm : 16;
            double lbAlt = alt != null ? alt.Lb : 0;
            double lbUst = ust != null ? ust.Lb : 0;

            k.SolAlt = UcKenetlenme(g.Sol, phiAlt, lbAlt, ayar, false);
            k.SagAlt = UcKenetlenme(g.Sag, phiAlt, lbAlt, ayar, false);
            k.SolUst = UcKenetlenme(g.Sol, phiUst, lbUst, ayar, true);
            k.SagUst = UcKenetlenme(g.Sag, phiUst, lbUst, ayar, true);

            if (g.Sol != null && g.Sol.Tur == MesnetTuru.Ara && alt != null)
                k.AraSolAltUzatmaMm = KirisFormuller.YukariYuvarla(Math.Max(lbAlt, ayar.AltIlavePhiCarpan * phiAlt), ayar.KesimBoyuYuvarlamaMm);
            if (g.Sag != null && g.Sag.Tur == MesnetTuru.Ara && alt != null)
                k.AraSagAltUzatmaMm = KirisFormuller.YukariYuvarla(Math.Max(lbAlt, ayar.AltIlavePhiCarpan * phiAlt), ayar.KesimBoyuYuvarlamaMm);

            return k;
        }

        static MesnetKenetlenme UcKenetlenme(KirisMesnet m, double phi, double lb, KirisDetayAyarlari ayar, bool ust)
        {
            var u = new MesnetKenetlenme();
            u.LbMm = lb;
            u.AsagiKivrilir = ust;
            u.BMinMm = ayar.Kanca90CarpanPhi * phi;
            u.AMinMm = KirisFormuller.EnYakinMm(0.4 * lb);
            if (m == null)
            {
                u.Tur = MesnetTuru.Kenar;
                return u;
            }
            u.Tur = m.Tur;
            double a = KirisFormuller.AMevcut(m.HcMm, m.CcMm, m.PhiWMm, phi, m.PhiBoyunaMm, m.BoyunaCapiDus);
            u.AMm = a;
            bool duz = false;
            if (m.Tur == MesnetTuru.Perde && a + 1e-6 >= lb)
                duz = true;
            if (a + 1e-6 >= Math.Max(lb, ayar.AltIlavePhiCarpan * phi))
                duz = true;
            if (m.Tur == MesnetTuru.Ara)
            {
                u.DuzKenetlenme = true;
                u.KancaVar = false;
                u.AMm = m.HcMm;
                u.BMm = 0;
                return u;
            }
            if (duz)
            {
                u.DuzKenetlenme = true;
                u.KancaVar = false;
                u.BMm = 0;
                return u;
            }
            u.KancaVar = true;
            double b = Math.Max(u.BMinMm, lb - a);
            u.BMm = KirisFormuller.YukariYuvarla(b, ayar.KancaBoyuYuvarlamaMm);
            return u;
        }

        static void KenarKontrol(KirisDetaySonuc sonuc, KirisDetayGirdi g)
        {
            KontrolUc(sonuc, "Sol üst", sonuc.Kenetlenme.SolUst, g.Sol);
            KontrolUc(sonuc, "Sol alt", sonuc.Kenetlenme.SolAlt, g.Sol);
            KontrolUc(sonuc, "Sağ üst", sonuc.Kenetlenme.SagUst, g.Sag);
            KontrolUc(sonuc, "Sağ alt", sonuc.Kenetlenme.SagAlt, g.Sag);
        }

        static void KontrolUc(KirisDetaySonuc sonuc, string ad, MesnetKenetlenme u, KirisMesnet m)
        {
            if (u == null || m == null) return;
            if (m.Tur == MesnetTuru.Ara) return;
            if (u.DuzKenetlenme) return;
            if (u.AMm + 1e-6 < 0.4 * u.LbMm)
            {
                Ekle(sonuc, KirisKuralKodu.KenarA, "TBDY 7.4.3.1(b)", KontrolSeviyesi.Hata,
                    ad + " kenetlenmesinde a = " + u.AMm.ToString("0") + " mm < 0.4·ℓb. Kolon boyutu yetersiz.");
            }
        }

        static GovdeSonuc GovdeHesapla(
            KirisDetaySonuc sonuc,
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            double cc,
            bool istisna,
            double d)
        {
            var gov = new GovdeSonuc();
            double hGovde = ayar.GovdeYuksekligiHKullan ? g.HMm : Math.Max(0, g.HMm - g.TMm);
            gov.Ts500Tetik = hGovde > ayar.GovdeTs500EsikMm;
            gov.TbdyTetik = g.HMm > g.LnMm / 4.0 && !istisna;
            gov.Gerekli = gov.Ts500Tetik || gov.TbdyTetik;
            gov.CirozDuseyMaxMm = ayar.GovdeCirozDuseyMaxMm;
            gov.CirozEksenMaxMm = ayar.GovdeCirozEksenMaxMm;

            KesitSonuc aciklik = sonuc.Kesit(KesitYeri.Aciklik);
            gov.HSerbestMm = HSerbest(aciklik, g.HMm, cc, don.EtriyeCapMm);

            int solAdet;
            int sagAdet;
            double capKullanici;
            bool kullanici = GovdeKullaniciAdet(don, out solAdet, out sagAdet, out capKullanici);

            gov.PhiMinMm = gov.TbdyTetik ? 12 : 10;
            int nAralik = gov.HSerbestMm > 0
                ? (int)Math.Ceiling(gov.HSerbestMm / ayar.GovdeAralikMaxMm - 1e-9) - 1
                : 0;
            if (nAralik < 0) nAralik = 0;

            if (gov.Gerekli)
            {
                double asTs = gov.Ts500Tetik ? 0.001 * g.BwMm * d : 0;
                double asTbdy = 0;
                if (gov.TbdyTetik)
                {
                    double solAs = AsUst(don, KesitYeri.SolMesnet, sonuc.Ilave.UstBirlesti) + AsAlt(don, KesitYeri.SolMesnet, sonuc.Ilave.AltBirlesti);
                    double sagAs = AsUst(don, KesitYeri.SagMesnet, sonuc.Ilave.UstBirlesti) + AsAlt(don, KesitYeri.SagMesnet, sonuc.Ilave.AltBirlesti);
                    asTbdy = 0.30 * Math.Max(solAs, sagAs);
                }
                gov.AsGerekliMm2 = Math.Max(asTs, asTbdy);
                double phiOneri = gov.PhiMinMm;
                if (kullanici && capKullanici + 1e-9 >= gov.PhiMinMm) phiOneri = capKullanici;
                double aPhi = KirisFormuller.TekCubukAlani(phiOneri);
                int nAlan = aPhi > 0 ? (int)Math.Ceiling(gov.AsGerekliMm2 / 2.0 / aPhi - 1e-9) : 0;
                gov.OnerilenCapMm = phiOneri;
                gov.OnerilenAdetYuz = Math.Max(nAlan, Math.Max(nAralik, 1));
            }

            if (kullanici)
            {
                gov.KullanilanAdetSol = solAdet;
                gov.KullanilanAdetSag = sagAdet;
                gov.KullanilanAdetYuz = solAdet == sagAdet ? solAdet : 0;
                gov.KullanilanCapMm = capKullanici;
                if (solAdet != sagAdet)
                {
                    Ekle(sonuc, KirisKuralKodu.Web, "TS500 7.3, TBDY 7.4.1.1(c)", KontrolSeviyesi.Uyari,
                        "Gövde çubukları iki yüze eşit dağılmıyor (sol " + solAdet + ", sağ " + sagAdet + "). Adet değiştirilmedi.");
                }
                if (!gov.Gerekli)
                {
                    Ekle(sonuc, KirisKuralKodu.Web, "TS500 7.3, TBDY 7.4.1.1(c)", KontrolSeviyesi.Bilgi,
                        "Gövde donatısı bu kirişte zorunlu değil. Verilen çubuklar yerleştirildi.");
                }
                else
                {
                    string madde = gov.TbdyTetik ? "TBDY 7.4.1.1(c)" : "TS500 7.3";
                    if (capKullanici + 1e-9 < gov.PhiMinMm)
                    {
                        Ekle(sonuc, KirisKuralKodu.Web, madde, KontrolSeviyesi.Uyari,
                            "Gövde çapı " + capKullanici.ToString("0.#") + " mm, en az " + gov.PhiMinMm.ToString("0") + " mm olmalı. Çap değiştirilmedi.");
                    }
                    double asVar = (solAdet + sagAdet) * KirisFormuller.TekCubukAlani(capKullanici);
                    if (asVar + 1e-6 < gov.AsGerekliMm2)
                    {
                        Ekle(sonuc, KirisKuralKodu.Web, "TS500 7.3 Denk. 7.6, TBDY 7.4.1.1(c)", KontrolSeviyesi.Uyari,
                            "Gövde alanı " + asVar.ToString("0") + " mm², gerekli " + gov.AsGerekliMm2.ToString("0") + " mm². Çubuk eklenmedi.");
                    }
                    if (solAdet < nAralik || sagAdet < nAralik)
                    {
                        Ekle(sonuc, KirisKuralKodu.Web, "TS500 7.3, TBDY 7.4.1.1(c)", KontrolSeviyesi.Uyari,
                            "Gövde çubuk aralığı " + ayar.GovdeAralikMaxMm.ToString("0") + " mm'yi aşıyor (sol " + solAdet + ", sağ " + sagAdet
                            + " adet; yüz başına en az " + nAralik + "). Adet değiştirilmedi.");
                    }
                }
            }
            else if (gov.Gerekli)
            {
                Ekle(sonuc, KirisKuralKodu.Web, "TS500 7.3, TBDY 7.4.1.1(c)", KontrolSeviyesi.Uyari,
                    "Gövde donatısı gerekli (" + GovdeNeden(gov) + "). Öneri: yüz başına "
                    + gov.OnerilenAdetYuz + "φ" + gov.OnerilenCapMm.ToString("0.#") + ". Çubuk eklenmedi.");
            }

            if (gov.TbdyTetik && gov.HSerbestMm > 0 && (gov.KullanilanAdetSol + gov.KullanilanAdetSag) > 0)
                gov.CirozAdetYukseklik = Math.Max(0, (int)Math.Ceiling(gov.HSerbestMm / ayar.GovdeCirozDuseyMaxMm - 1e-9) - 1);
            return gov;
        }

        static bool GovdeKullaniciAdet(KirisDonatiGirdisi don, out int sol, out int sag, out double cap)
        {
            sol = 0;
            sag = 0;
            cap = 0;
            if (don == null || don.Govde == null || don.Govde.CapMm <= 0) return false;
            if (don.GovdeToplamAdet > 0)
            {
                cap = don.Govde.CapMm;
                int n = don.GovdeToplamAdet;
                sol = (n + 1) / 2;
                sag = n - sol;
                return true;
            }
            if (don.Govde.Adet > 0)
            {
                cap = don.Govde.CapMm;
                sol = don.Govde.Adet;
                sag = don.Govde.Adet;
                return true;
            }
            return false;
        }

        static string GovdeNeden(GovdeSonuc gov)
        {
            if (gov.Ts500Tetik && gov.TbdyTetik) return "h > 600 mm ve h > ℓn/4";
            if (gov.Ts500Tetik) return "h > 600 mm";
            return "h > ℓn/4";
        }

        static double HSerbest(KesitSonuc kesit, double h, double cc, double phiW)
        {
            if (kesit == null || kesit.Ust == null || kesit.Alt == null) return h - 2 * (cc + phiW);
            double altIc = 0;
            double ustIc = h;
            bool altVar = false;
            bool ustVar = false;
            for (int i = 0; i < kesit.Alt.Cubuklar.Count; i++)
            {
                YerlesenCubuk c = kesit.Alt.Cubuklar[i];
                double ic = c.YAlttanMm + c.CapMm / 2.0;
                if (!altVar || ic > altIc) altIc = ic;
                altVar = true;
            }
            for (int i = 0; i < kesit.Ust.Cubuklar.Count; i++)
            {
                YerlesenCubuk c = kesit.Ust.Cubuklar[i];
                double ic = c.YAlttanMm - c.CapMm / 2.0;
                if (!ustVar || ic < ustIc) ustIc = ic;
                ustVar = true;
            }
            if (!altVar || !ustVar) return h - 2 * (cc + phiW);
            return Math.Max(0, ustIc - altIc);
        }

        static void GovdeYerlestir(KirisDetaySonuc sonuc, KirisDetayGirdi g, double cc, double phiW)
        {
            GovdeSonuc gov = sonuc.Govde;
            if (gov == null) return;
            int nSol = gov.KullanilanAdetSol;
            int nSag = gov.KullanilanAdetSag;
            if (nSol <= 0 && nSag <= 0 && gov.KullanilanAdetYuz > 0)
            {
                nSol = gov.KullanilanAdetYuz;
                nSag = gov.KullanilanAdetYuz;
            }
            if (nSol <= 0 && nSag <= 0) return;
            KesitSonuc aciklik = sonuc.Kesit(KesitYeri.Aciklik);
            if (aciklik == null || aciklik.Alt == null || aciklik.Ust == null) return;
            double altIc = 0;
            double ustIc = g.HMm;
            bool altVar = false;
            bool ustVar = false;
            for (int i = 0; i < aciklik.Alt.Cubuklar.Count; i++)
            {
                double ic = aciklik.Alt.Cubuklar[i].YAlttanMm + aciklik.Alt.Cubuklar[i].CapMm / 2.0;
                if (!altVar || ic > altIc) altIc = ic;
                altVar = true;
            }
            for (int i = 0; i < aciklik.Ust.Cubuklar.Count; i++)
            {
                double ic = aciklik.Ust.Cubuklar[i].YAlttanMm - aciklik.Ust.Cubuklar[i].CapMm / 2.0;
                if (!ustVar || ic < ustIc) ustIc = ic;
                ustVar = true;
            }
            if (!altVar || !ustVar) return;
            double hSer = ustIc - altIc;
            double xSol = cc + phiW + gov.KullanilanCapMm / 2.0;
            double xSag = g.BwMm - xSol;
            for (int s = 0; s < sonuc.Kesitler.Count; s++)
            {
                GovdeYuzDiz(sonuc.Kesitler[s], nSol, xSol, altIc, hSer, gov.KullanilanCapMm);
                GovdeYuzDiz(sonuc.Kesitler[s], nSag, xSag, altIc, hSer, gov.KullanilanCapMm);
            }
        }

        static void GovdeYuzDiz(KesitSonuc kesit, int n, double x, double altIc, double hSer, double cap)
        {
            for (int i = 1; i <= n; i++)
            {
                double y = altIc + hSer * i / (n + 1.0);
                kesit.Govde.Add(GovdeCubuk(x, y, cap, i));
            }
        }

        static YerlesenCubuk GovdeCubuk(double x, double y, double cap, int sira)
        {
            return new YerlesenCubuk
            {
                Rol = DonatiRolu.Govde,
                Sira = sira,
                CapMm = cap,
                XMm = x,
                YAlttanMm = y,
                YYuzdenMm = y,
                Surekli = true
            };
        }

        static EtriyeSonuc EtriyeHesapla(
            KirisDetaySonuc sonuc,
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            KirisDonatiGirdisi don,
            SuneklikDuzeyi dy,
            double cc,
            double d,
            double fctd,
            double fywd)
        {
            var e = new EtriyeSonuc();
            e.PhiWMm = don.EtriyeCapMm;
            e.LSarMm = 2.0 * g.HMm;
            e.PhiBoyunaMinMm = PhiMinBoyuna(don, ayar, sonuc.Govde);
            e.KancaAci = 135;
            e.KancaUcMm = Math.Max(ayar.EtriyeKancaUcCarpan * don.EtriyeCapMm, ayar.EtriyeKancaUcMinMm);
            e.KancaIcCapMm = ayar.EtriyeKancaIcCapCarpan * don.EtriyeCapMm;
            e.EksenAraligiMm = KirisFormuller.EksenAraligi(g.BwMm, cc, don.EtriyeCapMm);
            e.NKol = KirisFormuller.EtriyeKolSayisi(g.BwMm, cc, don.EtriyeCapMm, ayar.KolAralikMaxMm);
            e.AswMm2 = e.NKol * KirisFormuller.TekCubukAlani(don.EtriyeCapMm);
            e.AswBolumSGerekli = KirisFormuller.MinEtriyeAswBolumS(fctd, fywd, g.BwMm);

            if (e.PhiBoyunaMinMm <= 0) e.PhiBoyunaMinMm = 12;
            e.SSarMaxHamMm = KirisFormuller.SarilmaAralikMax(
                dy, d, g.HMm, e.PhiBoyunaMinMm, ayar.YuksekSarilmaMetinDBolum4,
                ayar.SarilmaUstSinirYuksekMm, ayar.SarilmaUstSinirSinirliMm);

            double sOrtaMax = ayar.VdUcVcrdenBuyuk ? d / 4.0 : d / 2.0;
            if (ayar.BurulmaVar)
            {
                double ue = 2.0 * ((g.BwMm - 2 * cc - don.EtriyeCapMm) + (g.HMm - 2 * cc - don.EtriyeCapMm));
                sOrtaMax = Math.Min(sOrtaMax, Math.Min(ue / 8.0, 300));
            }
            if (ayar.OrtaBolgePratikUstSinirMm > 0)
                sOrtaMax = Math.Min(sOrtaMax, ayar.OrtaBolgePratikUstSinirMm);
            if (e.AswBolumSGerekli > 0 && e.AswMm2 > 0)
                sOrtaMax = Math.Min(sOrtaMax, e.AswMm2 / e.AswBolumSGerekli);
            e.SOrtaMaxHamMm = sOrtaMax;

            double sSar = KullaniciAralik(
                sonuc, ayar.KullaniciSarilmaAraligiMm, e.SSarMaxHamMm, ayar.EtriyeAralikYuvarlamaMm, true, dy, e.LSarMm);
            double sOrta = KullaniciAralik(
                sonuc, ayar.KullaniciOrtaAralikMm, sOrtaMax, ayar.EtriyeAralikYuvarlamaMm, false, dy, e.LSarMm);

            if (sSar + 1e-6 < ayar.SarilmaPratikAltSinirMm)
            {
                Ekle(sonuc, "R-ST-02", "TBDY 7.4.4", KontrolSeviyesi.Bilgi,
                    "Sarılma aralığı pratik alt sınırın altında: " + sSar.ToString("0") + " mm.");
            }

            e.SSarMm = sSar;
            e.SOrtaMm = sOrta;

            double lOrta = g.LnMm - 2.0 * e.LSarMm;
            e.TumKirisSarilma = lOrta <= sOrta + 1e-6;
            EtriyeKonumlari(e, g.LnMm, ayar.IlkEtriyeMm);
            e.ToplamAdet = e.KonumlarMm.Count;
            KolXleri(e, sonuc.Kesit(KesitYeri.Aciklik), sonuc);
            return e;
        }

        static double PhiMinBoyuna(KirisDonatiGirdisi don, KirisDetayAyarlari ayar, GovdeSonuc gov)
        {
            double min = double.MaxValue;
            DonatiGrubu[] gruplar = { don.Montaj, don.AltDuz, don.UstSolIlave, don.UstSagIlave, don.AltSolIlave, don.AltSagIlave };
            for (int i = 0; i < gruplar.Length; i++)
            {
                if (gruplar[i] != null && gruplar[i].Var && gruplar[i].CapMm < min)
                    min = gruplar[i].CapMm;
            }
            if (ayar.PhiMinGovdeDahil && gov != null && gov.Gerekli && gov.KullanilanCapMm > 0 && gov.KullanilanCapMm < min)
                min = gov.KullanilanCapMm;
            if (min > 1e8) return 0;
            return min;
        }

        static void EtriyeKonumlari(EtriyeSonuc e, double ln, double x0)
        {
            var ham = new List<double>();
            if (e.SSarMm <= 0) return;
            if (e.TumKirisSarilma)
            {
                EkleUc(ham, ln, x0, e.SSarMm, ln / 2.0 + e.SSarMm);
                e.NSarBirUc = 0;
                for (int i = 0; i < ham.Count; i++)
                {
                    if (ham[i] <= ln / 2.0 + 0.1) e.NSarBirUc++;
                }
                e.NOrta = 0;
            }
            else
            {
                EkleUc(ham, ln, x0, e.SSarMm, e.LSarMm + 0.1);
                e.NSarBirUc = 0;
                for (int i = 0; i < ham.Count; i++)
                {
                    if (ham[i] <= e.LSarMm + 0.1) e.NSarBirUc++;
                }
                double solSon = x0;
                for (int i = 0; i < ham.Count; i++)
                {
                    if (ham[i] <= ln / 2.0 && ham[i] > solSon) solSon = ham[i];
                }
                double sagIlk = ln - solSon;
                if (e.SOrtaMm > 0)
                {
                    double x = solSon + e.SOrtaMm;
                    int guard = 0;
                    while (x < sagIlk - 0.5 && guard < 10000)
                    {
                        ham.Add(x);
                        x += e.SOrtaMm;
                        guard++;
                    }
                }
                e.NOrta = ham.Count - 2 * e.NSarBirUc;
                if (e.NOrta < 0) e.NOrta = 0;
            }
            ham.Sort();
            for (int i = 0; i < ham.Count; i++)
            {
                if (e.KonumlarMm.Count == 0 || Math.Abs(e.KonumlarMm[e.KonumlarMm.Count - 1] - ham[i]) > 0.5)
                    e.KonumlarMm.Add(ham[i]);
            }
        }

        static void EkleUc(List<double> ham, double ln, double x0, double s, double limit)
        {
            if (s <= 0) return;
            for (int i = 0; i < 10000; i++)
            {
                double x = x0 + i * s;
                if (x > limit + 1e-6) break;
                if (x > ln - x0 + 1e-6) break;
                ham.Add(x);
                double xr = ln - x;
                if (Math.Abs(xr - x) > 0.5) ham.Add(xr);
            }
        }

        static void KolXleri(EtriyeSonuc e, KesitSonuc aciklik, KirisDetaySonuc sonuc)
        {
            if (aciklik == null || aciklik.Alt == null) return;
            var xs = new List<double>();
            for (int i = 0; i < aciklik.Alt.Cubuklar.Count; i++)
            {
                if (aciklik.Alt.Cubuklar[i].Sira == 1 && aciklik.Alt.Cubuklar[i].Surekli)
                    xs.Add(aciklik.Alt.Cubuklar[i].XMm);
            }
            xs.Sort();
            if (xs.Count == 0) return;
            e.KolXMm.Add(xs[0]);
            if (xs.Count > 1) e.KolXMm.Add(xs[xs.Count - 1]);
            int ic = e.NKol - 2;
            if (ic <= 0) return;
            if (xs.Count <= 2)
            {
                Ekle(sonuc, "R-ST-03", "TBDY 7.4.4", KontrolSeviyesi.Uyari,
                    "İç etriye kolu için açıklık boyunca sürekli ara çubuk yok.");
                return;
            }
            // İç kollar ara sürekli çubuklara, ortaya yakın olanlardan başlayarak.
            var icAday = new List<int>();
            for (int i = 1; i < xs.Count - 1; i++) icAday.Add(i);
            icAday.Sort((a, b) =>
            {
                double da = Math.Abs(xs[a] - (xs[0] + xs[xs.Count - 1]) / 2.0);
                double db = Math.Abs(xs[b] - (xs[0] + xs[xs.Count - 1]) / 2.0);
                return da.CompareTo(db);
            });
            int n = Math.Min(ic, icAday.Count);
            for (int i = 0; i < n; i++) e.KolXMm.Add(xs[icAday[i]]);
            if (n < ic)
            {
                Ekle(sonuc, "R-ST-03", "TBDY 7.4.4", KontrolSeviyesi.Uyari,
                    "Kol aralığı için yeterli sürekli ara çubuk yok.");
            }
            e.KolXMm.Sort();
        }

        static List<BoyunaCubukCizim> BoyunaCubuklar(
            KirisDetayGirdi g,
            KirisDonatiGirdisi don,
            KenetlenmeSonuc ken,
            IlaveBoySonuc ilave,
            List<LbKaydi> kayitlar)
        {
            var list = new List<BoyunaCubukCizim>();
            LbKaydi montaj = Bul(kayitlar, DonatiRolu.Montaj);
            LbKaydi alt = Bul(kayitlar, DonatiRolu.AltDuz);
            if (montaj != null)
                list.Add(SurekliCubuk(DonatiRolu.Montaj, montaj.Grup, g, ken.SolUst, ken.SagUst, ken, true));
            if (alt != null)
                list.Add(SurekliCubuk(DonatiRolu.AltDuz, alt.Grup, g, ken.SolAlt, ken.SagAlt, ken, false));

            if (ilave.UstBirlesti)
            {
                DonatiGrubu gr = BirlestirGrup(don.UstSolIlave, don.UstSagIlave);
                list.Add(SurekliCubuk(DonatiRolu.UstIlaveSurekli, gr, g, ken.SolUst, ken.SagUst, ken, true));
            }
            else
            {
                EkleIlave(list, DonatiRolu.UstSolIlave, don.UstSolIlave, g, ken, ilave.UstSolAciklikMm, true, true);
                EkleIlave(list, DonatiRolu.UstSagIlave, don.UstSagIlave, g, ken, ilave.UstSagAciklikMm, false, true);
            }
            if (ilave.AltBirlesti)
            {
                DonatiGrubu gr = BirlestirGrup(don.AltSolIlave, don.AltSagIlave);
                list.Add(SurekliCubuk(DonatiRolu.AltIlaveSurekli, gr, g, ken.SolAlt, ken.SagAlt, ken, false));
            }
            else
            {
                EkleIlave(list, DonatiRolu.AltSolIlave, don.AltSolIlave, g, ken, ilave.AltSolAciklikMm, true, false);
                EkleIlave(list, DonatiRolu.AltSagIlave, don.AltSagIlave, g, ken, ilave.AltSagAciklikMm, false, false);
            }
            return list;
        }

        static BoyunaCubukCizim SurekliCubuk(
            DonatiRolu rol,
            DonatiGrubu grup,
            KirisDetayGirdi g,
            MesnetKenetlenme sol,
            MesnetKenetlenme sag,
            KenetlenmeSonuc ken,
            bool ust)
        {
            double bas = UcPayi(g.Sol, sol, ken, true, ust, g);
            double bit = g.LnMm + UcPayi(g.Sag, sag, ken, false, ust, g);
            var c = new BoyunaCubukCizim
            {
                Rol = rol,
                CapMm = grup.CapMm,
                Adet = grup.Adet,
                BaslangicMm = -bas,
                BitisMm = bit,
                Surekli = true,
                SolKanca = sol != null && sol.KancaVar,
                SagKanca = sag != null && sag.KancaVar,
                SolKancaBMm = sol != null ? sol.BMm : 0,
                SagKancaBMm = sag != null ? sag.BMm : 0,
                SolKancaAsagi = ust,
                SagKancaAsagi = ust
            };
            c.BoyMm = CubukBoyu(c, grup.GeometrikCapMm);
            return c;
        }

        static double UcPayi(KirisMesnet m, MesnetKenetlenme u, KenetlenmeSonuc ken, bool sol, bool ust, KirisDetayGirdi g)
        {
            if (m == null || u == null) return 0;
            if (m.Tur == MesnetTuru.Ara)
            {
                double uzatma = 0;
                if (!ust)
                    uzatma = sol ? ken.AraSolAltUzatmaMm : ken.AraSagAltUzatmaMm;
                return m.HcMm + uzatma;
            }
            if (u.KancaVar) return Math.Max(0, u.AMm);
            return Math.Max(0, u.AMm);
        }

        static void EkleIlave(
            List<BoyunaCubukCizim> list,
            DonatiRolu rol,
            DonatiGrubu grup,
            KirisDetayGirdi g,
            KenetlenmeSonuc ken,
            double aciklik,
            bool solTaraf,
            bool ust)
        {
            if (grup == null || !grup.Var) return;
            MesnetKenetlenme uc = solTaraf
                ? (ust ? ken.SolUst : ken.SolAlt)
                : (ust ? ken.SagUst : ken.SagAlt);
            KirisMesnet m = solTaraf ? g.Sol : g.Sag;
            double pay = UcPayi(m, uc, ken, solTaraf, ust, g);
            var c = new BoyunaCubukCizim
            {
                Rol = rol,
                CapMm = grup.CapMm,
                Adet = grup.Adet,
                Surekli = false,
                SolKancaAsagi = ust,
                SagKancaAsagi = ust
            };
            if (solTaraf)
            {
                c.BaslangicMm = -pay;
                c.BitisMm = aciklik;
                c.SolKanca = uc != null && uc.KancaVar;
                c.SolKancaBMm = uc != null ? uc.BMm : 0;
            }
            else
            {
                c.BaslangicMm = g.LnMm - aciklik;
                c.BitisMm = g.LnMm + pay;
                c.SagKanca = uc != null && uc.KancaVar;
                c.SagKancaBMm = uc != null ? uc.BMm : 0;
            }
            c.BoyMm = CubukBoyu(c, grup.GeometrikCapMm);
            list.Add(c);
        }

        static double CubukBoyu(BoyunaCubukCizim c, double phi)
        {
            double duz = c.BitisMm - c.BaslangicMm;
            double ek = 0;
            if (c.SolKanca) ek += c.SolKancaBMm + KirisFormuller.Yay90(phi);
            if (c.SagKanca) ek += c.SagKancaBMm + KirisFormuller.Yay90(phi);
            return duz + ek;
        }

        static double KullaniciAralik(
            KirisDetaySonuc sonuc,
            double? kullaniciMm,
            double ustSinirMm,
            double yuvarlamaMm,
            bool sarilma,
            SuneklikDuzeyi dy,
            double lSarMm)
        {
            if (!kullaniciMm.HasValue)
            {
                double s = KirisFormuller.AsagiYuvarla(ustSinirMm, yuvarlamaMm);
                if (s > ustSinirMm) s = ustSinirMm;
                return s;
            }
            double verilen = kullaniciMm.Value;
            string ad = sarilma ? "Sarılma bölgesi" : "Orta bölge";
            if (verilen <= 1e-9)
            {
                Ekle(sonuc, KirisKuralKodu.StAralik, sarilma ? "TBDY 7.4.4" : "TS500 8.1.6", KontrolSeviyesi.Hata,
                    ad + " etriye aralığı pozitif olmalıdır. Aralık uygulanamadı.");
                return 0;
            }
            if (verilen > ustSinirMm + 1e-6)
            {
                string kural = sarilma
                    ? (dy == SuneklikDuzeyi.Yuksek
                        ? "TBDY 7.4.4 min(d/4, 8φmin, 150 mm)"
                        : "TBDY 7.8.4 min(h/4, 8φmin, 200 mm)")
                    : "TS500 8.1.6 d/2 ve orta bölge üst sınırı";
                Ekle(sonuc, KirisKuralKodu.StAralik, kural, KontrolSeviyesi.Uyari,
                    ad + " etriye aralığı " + verilen.ToString("0.#") + " mm, üst sınır "
                    + ustSinirMm.ToString("0.#") + " mm (" + kural
                    + ", sarılma boyu " + lSarMm.ToString("0") + " mm). Aralık değiştirilmedi.");
            }
            return verilen;
        }

        static void GovdeBoyunaEkle(
            KirisDetaySonuc sonuc,
            KirisDetayGirdi g,
            KirisDetayAyarlari ayar,
            double cc,
            double fyd,
            double fctd,
            List<LbKaydi> kayitlar)
        {
            KesitSonuc ac = sonuc.Kesit(KesitYeri.Aciklik);
            if (ac == null || ac.Govde == null || kayitlar == null) return;
            bool kAs = ayar.KAsAzaltmaUygula && ayar.Suneklik == SuneklikDuzeyi.Sinirli;
            for (int i = 0; i < ac.Govde.Count; i++)
            {
                YerlesenCubuk bar = ac.Govde[i];
                bool ustYari = bar.YAlttanMm + 1e-6 >= g.HMm / 2.0;
                double ustten = g.HMm - bar.YAlttanMm;
                // TS500 9.1.1: Konum I yalnız üst yarıda ve serbest üst yüzden en çok 300 mm uzaktaysa.
                bool konumI = ustYari && ustten <= 300.0 + 1e-6;
                bool kOrtu = GovdeKOrtu(cc, bar, ac);
                double phi = bar.CapMm;
                double lb0 = KirisFormuller.Lb0(fyd, fctd, phi, ayar.Nervurlu);
                double ham = KirisFormuller.LbHam(lb0, phi, konumI, kOrtu, ayar.KonumKatsayisiUygula, kAs, ayar.KAs);
                double lb = KirisFormuller.EnYakinMm(ham);
                var grup = new DonatiGrubu(1, phi);
                kayitlar.Add(new LbKaydi
                {
                    Rol = DonatiRolu.Govde,
                    Grup = grup,
                    KonumI = konumI,
                    KOrtu = kOrtu,
                    Lb0Ham = lb0,
                    LbHam = ham,
                    Lb = lb
                });
                if (sonuc.Kenetlenme != null && sonuc.Kenetlenme.Gruplar != null)
                {
                    sonuc.Kenetlenme.Gruplar.Add(new GrupKenetlenme
                    {
                        Rol = DonatiRolu.Govde,
                        CapMm = phi,
                        Lb0HamMm = lb0,
                        LbHamMm = ham,
                        LbMm = lb,
                        KonumI = konumI,
                        KOrtu = kOrtu
                    });
                }
                MesnetKenetlenme sol = UcKenetlenme(g.Sol, phi, lb, ayar, ustYari);
                MesnetKenetlenme sag = UcKenetlenme(g.Sag, phi, lb, ayar, ustYari);
                KontrolUc(sonuc, "Gövde sol, y=" + bar.YAlttanMm.ToString("0") + " mm", sol, g.Sol);
                KontrolUc(sonuc, "Gövde sağ, y=" + bar.YAlttanMm.ToString("0") + " mm", sag, g.Sag);
                double basPay = GovdeUcPay(g.Sol, sol, lb, phi, ayar);
                double bitPay = GovdeUcPay(g.Sag, sag, lb, phi, ayar);
                var c = new BoyunaCubukCizim
                {
                    Rol = DonatiRolu.Govde,
                    CapMm = phi,
                    Adet = 1,
                    BaslangicMm = -basPay,
                    BitisMm = g.LnMm + bitPay,
                    Surekli = true,
                    SolKanca = sol != null && sol.KancaVar,
                    SagKanca = sag != null && sag.KancaVar,
                    SolKancaBMm = sol != null ? sol.BMm : 0,
                    SagKancaBMm = sag != null ? sag.BMm : 0,
                    SolKancaAsagi = ustYari,
                    SagKancaAsagi = ustYari,
                    XMm = bar.XMm,
                    YAlttanMm = bar.YAlttanMm,
                    Sira = bar.Sira
                };
                c.BoyMm = CubukBoyu(c, phi);
                sonuc.BoyunaCubuklar.Add(c);
            }
        }

        static double GovdeUcPay(KirisMesnet m, MesnetKenetlenme u, double lb, double phi, KirisDetayAyarlari ayar)
        {
            if (m == null || u == null) return 0;
            if (m.Tur == MesnetTuru.Ara)
            {
                double uz = KirisFormuller.YukariYuvarla(Math.Max(lb, ayar.AltIlavePhiCarpan * phi), ayar.KesimBoyuYuvarlamaMm);
                return m.HcMm + uz;
            }
            return Math.Max(0, u.AMm);
        }

        static bool GovdeKOrtu(double cc, YerlesenCubuk bar, KesitSonuc ac)
        {
            if (bar == null) return false;
            if (cc + 1e-9 < bar.CapMm) return true;
            double limit = 1.5 * bar.CapMm;
            double ustIc = 0;
            double altIc = 0;
            bool ustVar = false;
            bool altVar = false;
            if (ac.Ust != null)
            {
                for (int i = 0; i < ac.Ust.Cubuklar.Count; i++)
                {
                    double ic = ac.Ust.Cubuklar[i].YAlttanMm - ac.Ust.Cubuklar[i].CapMm / 2.0;
                    if (!ustVar || ic < ustIc) ustIc = ic;
                    ustVar = true;
                }
            }
            if (ac.Alt != null)
            {
                for (int i = 0; i < ac.Alt.Cubuklar.Count; i++)
                {
                    double ic = ac.Alt.Cubuklar[i].YAlttanMm + ac.Alt.Cubuklar[i].CapMm / 2.0;
                    if (!altVar || ic > altIc) altIc = ic;
                    altVar = true;
                }
            }
            if (ustVar && ustIc - (bar.YAlttanMm + bar.CapMm / 2.0) < limit - 1e-6) return true;
            if (altVar && (bar.YAlttanMm - bar.CapMm / 2.0) - altIc < limit - 1e-6) return true;
            if (ac.Govde != null)
            {
                for (int i = 0; i < ac.Govde.Count; i++)
                {
                    YerlesenCubuk d = ac.Govde[i];
                    if (Math.Abs(d.XMm - bar.XMm) > 1.0) continue;
                    if (Math.Abs(d.YAlttanMm - bar.YAlttanMm) < 0.1) continue;
                    double clear = Math.Abs(d.YAlttanMm - bar.YAlttanMm) - d.CapMm / 2.0 - bar.CapMm / 2.0;
                    if (clear < limit - 1e-6) return true;
                }
            }
            return false;
        }

        static void EkleriEkle(KirisDetaySonuc sonuc, KirisDetayGirdi g, KirisDetayAyarlari ayar, List<LbKaydi> kayitlar)
        {
            var govdeSirasi = new List<LbKaydi>();
            if (kayitlar != null)
            {
                for (int i = 0; i < kayitlar.Count; i++)
                {
                    if (kayitlar[i].Rol == DonatiRolu.Govde) govdeSirasi.Add(kayitlar[i]);
                }
            }
            int govdeIndex = 0;
            double lSar = sonuc.Etriye != null ? sonuc.Etriye.LSarMm : 0;
            for (int i = 0; i < sonuc.BoyunaCubuklar.Count; i++)
            {
                BoyunaCubukCizim c = sonuc.BoyunaCubuklar[i];
                if (c.BoyMm <= ayar.StokBoyMm + 1e-6) continue;
                LbKaydi lb;
                if (c.Rol == DonatiRolu.Govde)
                {
                    if (govdeIndex >= govdeSirasi.Count) continue;
                    lb = govdeSirasi[govdeIndex++];
                }
                else
                {
                    lb = LbRol(kayitlar, c.Rol);
                }
                if (lb == null) continue;
                bool alt = c.Rol == DonatiRolu.Govde ? !lb.KonumI : AltRol(c.Rol);
                bool manson = ayar.Phi30UstuBindirmeYasak && (c.CapMm > 30 || lb.Grup.GeometrikCapMm > 30);
                double r = c.Adet >= 2 ? 0.5 : 1;
                double l0 = L0Degeri(lb, r, ayar);
                var ek = new EkYeri
                {
                    Rol = c.Rol,
                    CapMm = c.CapMm,
                    L0Mm = l0,
                    R = r,
                    Manson = manson,
                    SasirtmaMinMm = c.Adet >= 2 ? 1.5 * l0 : 0,
                    OzelEtriyeGerekli = !manson && c.Rol != DonatiRolu.Montaj && c.Rol != DonatiRolu.UstIlaveSurekli,
                    OzelEtriyeAralikMm = Math.Min(g.HMm / 4.0, 100)
                };
                if (manson)
                {
                    ek.Aciklama = "φ>30 mm. Bindirme yerine manşon; komşu ek merkezleri en az 600 mm (TBDY 7.4.3.2(b)).";
                    Ekle(sonuc, KirisKuralKodu.Lap30, "TS500 9.2.6.1, TBDY 7.4.3.2(b)", KontrolSeviyesi.Uyari, ek.Aciklama);
                }
                double bas;
                double bit;
                if (EkAraligi(g.LnMm, lSar, alt, ayar.AciklikOrtasiAltEkOrani, l0, out bas, out bit))
                {
                    ek.BaslangicMm = bas;
                    ek.BitisMm = bit;
                    if (!manson)
                        ek.Aciklama = "Bindirme ℓ0 = " + l0.ToString("0") + " mm.";
                }
                else
                {
                    Ekle(sonuc, KirisKuralKodu.Lap, "TBDY 7.4.3.2(a)", KontrolSeviyesi.Hata,
                        c.Rol + " için stok boyu aşıldı ama izinli ek bölgesine ℓ0 sığmıyor.");
                }
                sonuc.Ekler.Add(ek);
                if (ek.OzelEtriyeGerekli)
                    Sikilastir(sonuc.Etriye, ek.BaslangicMm, ek.BitisMm, ek.OzelEtriyeAralikMm);
            }
        }

        static LbKaydi LbRol(List<LbKaydi> kayitlar, DonatiRolu rol)
        {
            if (rol == DonatiRolu.UstIlaveSurekli)
            {
                LbKaydi a = Bul(kayitlar, DonatiRolu.UstSolIlave);
                if (a != null) return a;
                return Bul(kayitlar, DonatiRolu.UstSagIlave);
            }
            if (rol == DonatiRolu.AltIlaveSurekli)
            {
                LbKaydi a = Bul(kayitlar, DonatiRolu.AltSolIlave);
                if (a != null) return a;
                return Bul(kayitlar, DonatiRolu.AltSagIlave);
            }
            return Bul(kayitlar, rol);
        }

        static bool AltRol(DonatiRolu rol)
        {
            return rol == DonatiRolu.AltDuz || rol == DonatiRolu.AltSolIlave || rol == DonatiRolu.AltSagIlave || rol == DonatiRolu.AltIlaveSurekli;
        }

        static bool EkAraligi(double ln, double lSar, bool alt, double ortaOran, double l0, out double bas, out double bit)
        {
            bas = 0;
            bit = 0;
            var bolgeler = new List<double[]>();
            double sol = lSar;
            double sag = ln - lSar;
            if (sag - sol < 1) return false;
            if (!alt)
            {
                bolgeler.Add(new[] { sol, sag });
            }
            else
            {
                double y1 = ln * (1.0 - ortaOran) / 2.0;
                double y2 = ln * (1.0 + ortaOran) / 2.0;
                if (y1 - sol > 1) bolgeler.Add(new[] { sol, y1 });
                if (sag - y2 > 1) bolgeler.Add(new[] { y2, sag });
            }
            double enIyi = -1;
            double secA = 0;
            double secB = 0;
            for (int i = 0; i < bolgeler.Count; i++)
            {
                double a = bolgeler[i][0];
                double b = bolgeler[i][1];
                double gen = b - a;
                if (gen > enIyi)
                {
                    enIyi = gen;
                    secA = a;
                    secB = b;
                }
            }
            if (enIyi < 0) return false;
            if (l0 > enIyi + 1e-6) return false;
            double merkez = (secA + secB) / 2.0;
            bas = merkez - l0 / 2.0;
            bit = merkez + l0 / 2.0;
            if (bas < secA)
            {
                bas = secA;
                bit = secA + l0;
            }
            if (bit > secB)
            {
                bit = secB;
                bas = secB - l0;
            }
            return true;
        }

        static void Sikilastir(EtriyeSonuc e, double bas, double bit, double s)
        {
            if (e == null || s <= 0 || bit <= bas) return;
            double x = bas;
            int guard = 0;
            while (x <= bit + 0.1 && guard < 10000)
            {
                bool var = false;
                for (int i = 0; i < e.KonumlarMm.Count; i++)
                {
                    if (Math.Abs(e.KonumlarMm[i] - x) < s * 0.45)
                    {
                        var = true;
                        break;
                    }
                }
                if (!var) e.KonumlarMm.Add(x);
                x += s;
                guard++;
            }
            e.KonumlarMm.Sort();
            e.ToplamAdet = e.KonumlarMm.Count;
        }
    }
}
