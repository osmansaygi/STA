using System.Linq;
using ST4PlanIdCiz.KirisDetay;
using Xunit;

namespace ST4KirisDetay.Tests
{
    /// <summary>
    /// Kiriş tablosu yazısı. KB-02 satırındaki B, D ve donatı tablodan;
    /// açıklık ve kolon boyutları tabloda yok, testte açıkça verildi.
    /// </summary>
    public class KirisDonatiMetinTests
    {
        const char Dia = '\u00F8';
        const char DiaBuyuk = '\u00D8';

        /// <summary>
        /// Tablodaki dört satır. Çap işareti U+00F8.
        /// ℓn = 5000 mm, iki uç kenar kolon hc = 400 mm, bdik = 400 mm.
        /// DY yüksek, DTS 1, C30, B420C, örtü 25 mm, Dmax 22 mm. Bunlar tabloda yok.
        /// </summary>
        static string Kb02Metni()
        {
            return "2" + Dia + "12(mon.)+2" + Dia + "12(g\u00f6v.)\n"
                + "2" + Dia + "14(d\u00fcz)\n"
                + "1" + Dia + "14(sol \u00fcst ila.)\n"
                + Dia + "8/20/9(etriye)";
        }

        static KirisDetayGirdi Kb02()
        {
            var ayar = new KirisDetayAyarlari();
            ayar.Suneklik = SuneklikDuzeyi.Yuksek;
            ayar.Dts = DepremTasarimSinifi.Dts1;
            var g = KirisDonatiMetni.TabloSatiri(
                "KB-02",
                25,
                60,
                Kb02Metni(),
                5000,
                KirisDetayGirdi.MesnetOrnek(MesnetTuru.Kenar),
                KirisDetayGirdi.MesnetOrnek(MesnetTuru.Kenar),
                ayar);
            return g;
        }

        [Fact]
        public void KB02_tablodaki_donati_ve_aralik_degistirilmez()
        {
            KirisDetayGirdi g = Kb02();
            Assert.Equal(250, g.BwMm, 0);
            Assert.Equal(600, g.HMm, 0);
            Assert.Equal(5000, g.LnMm, 0);
            Assert.True(g.OkumaHatalari == null || g.OkumaHatalari.Count == 0, string.Join(" | ", g.OkumaHatalari ?? new System.Collections.Generic.List<string>()));
            Assert.Equal(2, g.Donati.Montaj.Adet);
            Assert.Equal(12, g.Donati.Montaj.CapMm, 0);
            Assert.Equal(2, g.Donati.AltDuz.Adet);
            Assert.Equal(14, g.Donati.AltDuz.CapMm, 0);
            Assert.Equal(1, g.Donati.UstSolIlave.Adet);
            Assert.Equal(14, g.Donati.UstSolIlave.CapMm, 0);
            Assert.False(g.Donati.UstSagIlave.Var);
            Assert.Equal(2, g.Donati.GovdeToplamAdet);
            Assert.Equal(0, g.Donati.Govde.Adet);
            Assert.Equal(12, g.Donati.Govde.CapMm, 0);
            Assert.Equal(8, g.Donati.EtriyeCapMm, 0);
            Assert.Equal(200, g.Ayarlar.KullaniciOrtaAralikMm.Value, 0);
            Assert.Equal(90, g.Ayarlar.KullaniciSarilmaAraligiMm.Value, 0);
            Assert.Contains(g.OkumaNotlari, n => n.Contains("orta") && n.Contains("sar"));

            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(2, g.Donati.Montaj.Adet);
            Assert.Equal(2, g.Donati.AltDuz.Adet);
            Assert.Equal(1, g.Donati.UstSolIlave.Adet);
            Assert.Equal(2, g.Donati.GovdeToplamAdet);
            Assert.Equal(90, s.Etriye.SSarMm, 0);
            Assert.Equal(200, s.Etriye.SOrtaMm, 0);
            Assert.True(s.Etriye.SSarMm <= s.Etriye.SSarMaxHamMm + 0.1);
            Assert.True(s.Etriye.SOrtaMm <= s.Etriye.SOrtaMaxHamMm + 0.1);
            Assert.DoesNotContain(s.Kontroller, k => k.Kod == KirisKuralKodu.StAralik);
            Assert.Equal(1200, s.Etriye.LSarMm, 0);
            Assert.Equal(1, s.Govde.KullanilanAdetSol);
            Assert.Equal(1, s.Govde.KullanilanAdetSag);
            Assert.False(s.Govde.Gerekli);
            Assert.Equal(2, s.Kesit(KesitYeri.Aciklik).Govde.Count);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.RhoMin && k.Seviye == KontrolSeviyesi.Hata);
            Assert.False(s.Gecerli);

            Assert.Equal(1250, s.Ilave.UstSolAciklikMm, 0);
            Assert.False(s.Ilave.UstBirlesti);

            Assert.Equal(7, s.Cubuklar.Count);
            CubukDokumu ilave = s.Cubuklar.Single(c => c.Rol == DonatiRolu.UstSolIlave);
            Assert.Equal(1, ilave.Sira);
            Assert.Equal(1250, ilave.BitisMm, 0);
            Assert.True(ilave.BaslangicMm < 0);
            Assert.True(ilave.SolKancaBMm > 0);
            Assert.True(ilave.ToplamKesimMm > ilave.DuzBoyMm);
            Assert.Equal(ilave.DuzBoyMm, ilave.BitisMm - ilave.BaslangicMm, 1);

            var govdeler = s.Cubuklar.Where(c => c.Rol == DonatiRolu.Govde).ToList();
            Assert.Equal(2, govdeler.Count);
            Assert.Contains(govdeler, c => c.XMm < 125);
            Assert.Contains(govdeler, c => c.XMm > 125);
            foreach (CubukDokumu c in govdeler)
            {
                Assert.Equal(12, c.CapMm, 0);
                Assert.InRange(c.YAlttanMm, 290, 320);
                Assert.Equal(230, c.SolKancaBMm, 0);
                Assert.Equal(230, c.SagKancaBMm, 0);
                Assert.Equal(5718, c.DuzBoyMm, 1);
                Assert.InRange(c.ToplamKesimMm, 6305, 6315);
                Assert.True(c.SolYayMm > 0);
            }
            Assert.Contains(s.Kenetlenme.Gruplar, gr => gr.Rol == DonatiRolu.Govde && gr.KonumI);

            Assert.False(string.IsNullOrWhiteSpace(s.OzetMetni));
            Assert.Contains("KB-02", s.OzetMetni);
            Assert.Contains("Sarılma aralığı 90", s.OzetMetni);
            Assert.Contains("Orta bölge aralığı 200", s.OzetMetni);
            Assert.Contains("değiştirilmedi", s.OzetMetni);
            Assert.Contains("Hatalar", s.OzetMetni);
        }

        [Fact]
        public void Etriye_siniri_asan_aralik_uyari_verir_ama_kalir()
        {
            KirisDetayGirdi g = KirisDetayGirdi.OrnekC30B420C(16, 4000, true);
            g.Ayarlar.KullaniciSarilmaAraligiMm = 200;
            g.Ayarlar.KullaniciOrtaAralikMm = 180;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(200, s.Etriye.SSarMm, 0);
            Assert.Equal(180, s.Etriye.SOrtaMm, 0);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.StAralik && k.Seviye == KontrolSeviyesi.Uyari && k.Mesaj.Contains("200"));
            Assert.DoesNotContain(s.Kontroller, k => k.Kod == KirisKuralKodu.StAralik && k.Mesaj.Contains("180"));
        }

        [Fact]
        public void Cap_isareti_fi_ve_ascii_turler()
        {
            KirisDonatiMetinSonuc buyuk = KirisDonatiMetni.Ayikla("2" + DiaBuyuk + "12(MON.)+2" + DiaBuyuk + "14(DUZ)");
            Assert.True(buyuk.Tamam, string.Join(" | ", buyuk.Hatalar));
            Assert.Equal(2, buyuk.Donati.Montaj.Adet);
            Assert.Equal(12, buyuk.Donati.Montaj.CapMm, 0);
            Assert.Equal(2, buyuk.Donati.AltDuz.Adet);
            Assert.Equal(14, buyuk.Donati.AltDuz.CapMm, 0);

            KirisDonatiMetinSonuc fi = KirisDonatiMetni.Ayikla("2fi12(gov.) + 2 FI 14 ( Duz )");
            Assert.True(fi.Tamam, string.Join(" | ", fi.Hatalar));
            Assert.Equal(2, fi.Donati.GovdeToplamAdet);
            Assert.Equal(12, fi.Donati.Govde.CapMm, 0);
            Assert.Equal(2, fi.Donati.AltDuz.Adet);
            Assert.Equal(14, fi.Donati.AltDuz.CapMm, 0);

            KirisDonatiMetinSonuc phi = KirisDonatiMetni.Ayikla("1phi16(sol ust ila.)");
            Assert.True(phi.Tamam, string.Join(" | ", phi.Hatalar));
            Assert.Equal(1, phi.Donati.UstSolIlave.Adet);
            Assert.Equal(16, phi.Donati.UstSolIlave.CapMm, 0);
        }

        [Fact]
        public void Ilave_turleri_turkce_ve_ascii()
        {
            string metin = "1" + Dia + "14(sa\u011f \u00fcst ila.)+1" + Dia + "14(sol alt ila.)+1" + Dia + "16(sa\u011f alt ilave)+1fi18(sol \u00fcst ilave)";
            KirisDonatiMetinSonuc s = KirisDonatiMetni.Ayikla(metin);
            Assert.True(s.Tamam, string.Join(" | ", s.Hatalar));
            Assert.Equal(1, s.Donati.UstSagIlave.Adet);
            Assert.Equal(14, s.Donati.UstSagIlave.CapMm, 0);
            Assert.Equal(1, s.Donati.AltSolIlave.Adet);
            Assert.Equal(14, s.Donati.AltSolIlave.CapMm, 0);
            Assert.Equal(1, s.Donati.AltSagIlave.Adet);
            Assert.Equal(16, s.Donati.AltSagIlave.CapMm, 0);
            Assert.Equal(1, s.Donati.UstSolIlave.Adet);
            Assert.Equal(18, s.Donati.UstSolIlave.CapMm, 0);
        }

        [Fact]
        public void Etriye_tek_aralik_iki_bolgeye_yazilir()
        {
            KirisDonatiMetinSonuc s = KirisDonatiMetni.Ayikla("fi8/20(etriye)");
            Assert.True(s.Tamam, string.Join(" | ", s.Hatalar));
            Assert.Equal(8, s.Donati.EtriyeCapMm, 0);
            Assert.Equal(200, s.OrtaAralikMm.Value, 0);
            Assert.Equal(200, s.SarilmaAraligiMm.Value, 0);
            Assert.Contains(s.Notlar, n => n.Contains("tek aralık") || n.Contains("Tek aralık") || n.Contains("aynı"));
        }

        [Fact]
        public void Govde_uc_cubuk_iki_yuze_bolunur()
        {
            var ayar = new KirisDetayAyarlari();
            ayar.Suneklik = SuneklikDuzeyi.Yuksek;
            ayar.Dts = DepremTasarimSinifi.Dts1;
            KirisDetayGirdi g = KirisDonatiMetni.TabloSatiri(
                "KB-03",
                30,
                50,
                "2fi16(mon.)+3fi12(gov.)+3fi16(duz)+" + Dia + "8/15/10(etriye)",
                4000,
                KirisDetayGirdi.MesnetOrnek(MesnetTuru.Kenar),
                KirisDetayGirdi.MesnetOrnek(MesnetTuru.Kenar),
                ayar);
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(3, g.Donati.GovdeToplamAdet);
            Assert.Equal(2, s.Govde.KullanilanAdetSol);
            Assert.Equal(1, s.Govde.KullanilanAdetSag);
            Assert.Equal(3, s.Kesit(KesitYeri.Aciklik).Govde.Count);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.Web && k.Seviye == KontrolSeviyesi.Uyari && k.Mesaj.Contains("eşit"));
            Assert.Equal(100, s.Etriye.SSarMm, 0);
            Assert.Equal(150, s.Etriye.SOrtaMm, 0);
        }

        [Fact]
        public void Taninmayan_tur_hata_verir()
        {
            KirisDonatiMetinSonuc s = KirisDonatiMetni.Ayikla("2fi12(pilye)");
            Assert.False(s.Tamam);
            Assert.NotEmpty(s.Hatalar);
        }
    }
}
