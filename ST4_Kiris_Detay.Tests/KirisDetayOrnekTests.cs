using System.Linq;
using ST4PlanIdCiz.KirisDetay;
using Xunit;

namespace ST4KirisDetay.Tests
{
    /// <summary>docs/kiris_detay_kurallari.md bölüm 11 ve kenar durumları.</summary>
    public class KirisDetayOrnekTests
    {
        static string Hatalar(KirisDetaySonuc s)
        {
            return string.Join(" | ", s.Kontroller.Where(k => k.Seviye == KontrolSeviyesi.Hata).Select(k => k.Kod + " " + k.Mesaj));
        }

        static KirisDetayGirdi Temel(double dmax, double ln, bool kenar)
        {
            return KirisDetayGirdi.OrnekC30B420C(dmax, ln, kenar);
        }

        [Fact]
        public void Malzeme_C30_B420C()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, true));
            Assert.True(s.Gecerli, Hatalar(s));
            Assert.Equal(20, s.Malzeme.Fcd, 2);
            Assert.Equal(1.267, s.Malzeme.Fctd, 3);
            Assert.Equal(365.22, s.Malzeme.Fyd, 2);
            Assert.InRange(s.Malzeme.RhoMin, 0.0027, 0.00285);
            Assert.InRange(s.Malzeme.RhoB, 0.023, 0.0245);
        }

        [Fact]
        public void Kenetlenme_phi16_alt554_ust775()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, true));
            Assert.True(s.Gecerli, Hatalar(s));
            Assert.Equal(554, s.Kenetlenme.LbAltMm, 0);
            Assert.Equal(775, s.Kenetlenme.LbUstMm, 0);
            Assert.Equal(415, s.Kenetlenme.LbkAltMm, 0);
        }

        [Fact]
        public void B500C_lb0_yaklasik_659()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Ayarlar.Celik = CelikSinifi.B500C;
            g.Ayarlar.EtriyeCelik = CelikSinifi.B500C;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(659, s.Kenetlenme.LbAltMm, 0);
            Assert.True(s.Kenetlenme.LbUstMm > s.Kenetlenme.LbAltMm);
        }

        [Fact]
        public void Ara_mesnet_alt_uzatma_800()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, false));
            Assert.True(s.Gecerli, Hatalar(s));
            Assert.Equal(800, s.Kenetlenme.AraSolAltUzatmaMm, 0);
            Assert.Equal(800, s.Kenetlenme.AraSagAltUzatmaMm, 0);
            Assert.False(s.Kenetlenme.SolAlt.KancaVar);
        }

        [Fact]
        public void Kenar_mesnet_kanca_boylari()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, true));
            Assert.True(s.Gecerli, Hatalar(s));
            Assert.Equal(357, s.Kenetlenme.SolAlt.AMm, 0);
            Assert.Equal(222, s.Kenetlenme.SolAlt.AMinMm, 0);
            Assert.Equal(200, s.Kenetlenme.SolAlt.BMm, 0);
            Assert.Equal(310, s.Kenetlenme.SolUst.AMinMm, 0);
            Assert.Equal(420, s.Kenetlenme.SolUst.BMm, 0);
            Assert.True(s.Kenetlenme.SolUst.KancaVar);
            Assert.True(s.Kenetlenme.SolUst.AsagiKivrilir);
            Assert.False(s.Kenetlenme.SolAlt.AsagiKivrilir);
        }

        [Fact]
        public void Siraya_sigan_Dmax16_alti_Dmax22_bes()
        {
            KirisDetayGirdi g16 = Temel(16, 4000, true);
            g16.Donati.AltDuz = new DonatiGrubu(6, 16);
            KirisDetaySonuc s16 = KirisDetayMotoru.Hesapla(g16);
            KesitSonuc acik16 = s16.Kesit(KesitYeri.Aciklik);
            Assert.Equal(6, acik16.Alt.NMax);
            Assert.Equal(26.8, acik16.Alt.SNetMm, 1);

            KirisDetayGirdi g22 = Temel(22, 4000, true);
            g22.Donati.AltDuz = new DonatiGrubu(5, 16);
            KirisDetaySonuc s22 = KirisDetayMotoru.Hesapla(g22);
            KesitSonuc acik22 = s22.Kesit(KesitYeri.Aciklik);
            Assert.Equal(5, acik22.Alt.NMax);
            Assert.Equal(37.5, acik22.Alt.SNetMm, 1);
        }

        [Fact]
        public void Yedi_cubuk_ikinci_sira_y2_84()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Donati.AltSolIlave = new DonatiGrubu(4, 16);
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.True(s.Gecerli, Hatalar(s));
            KesitSonuc sol = s.Kesit(KesitYeri.SolMesnet);
            Assert.Equal(6, sol.Alt.NMax);
            int sira1 = sol.Alt.Cubuklar.Count(c => c.Sira == 1);
            int sira2 = sol.Alt.Cubuklar.Count(c => c.Sira == 2);
            Assert.Equal(6, sira1);
            Assert.Equal(1, sira2);
            YerlesenCubuk ikinci = sol.Alt.Cubuklar.First(c => c.Sira == 2);
            Assert.Equal(84, ikinci.YYuzdenMm, 0);
            Assert.Equal(DonatiRolu.AltSolIlave, ikinci.Rol);
            Assert.Contains(sol.Alt.Cubuklar, c => c.Sira == 1 && c.Kose && c.Rol == DonatiRolu.AltDuz);
            double x2 = ikinci.XMm;
            Assert.Contains(sol.Alt.Cubuklar, c => c.Sira == 1 && System.Math.Abs(c.XMm - x2) < 0.1);
        }

        [Fact]
        public void Etriye_sarilma_yuksek_ve_sinirli()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            KirisDetaySonuc yuksek = KirisDetayMotoru.Hesapla(g);
            Assert.True(yuksek.Gecerli, Hatalar(yuksek));
            Assert.Equal(457, yuksek.DMm, 0);
            Assert.Equal(1000, yuksek.Etriye.LSarMm, 0);
            Assert.Equal(114, yuksek.Etriye.SSarMaxHamMm, 0);
            Assert.Equal(100, yuksek.Etriye.SSarMm, 0);
            Assert.Equal(10, yuksek.Etriye.NSarBirUc);
            Assert.Equal(200, yuksek.Etriye.SOrtaMm, 0);
            Assert.Contains(yuksek.Etriye.KonumlarMm, x => System.Math.Abs(x - 50) < 0.1);
            Assert.Contains(yuksek.Etriye.KonumlarMm, x => System.Math.Abs(x - 950) < 0.1);

            g.Ayarlar.Suneklik = SuneklikDuzeyi.Sinirli;
            g.Ayarlar.Dts = DepremTasarimSinifi.Dts3;
            KirisDetaySonuc sinirli = KirisDetayMotoru.Hesapla(g);
            Assert.True(sinirli.Gecerli, Hatalar(sinirli));
            Assert.Equal(125, sinirli.Etriye.SSarMaxHamMm, 0);
            Assert.Equal(125, sinirli.Etriye.SSarMm, 0);
        }

        [Fact]
        public void Etriye_kanca_ve_kol()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, true));
            Assert.Equal(135, s.Etriye.KancaAci, 0);
            Assert.Equal(80, s.Etriye.KancaUcMm, 0);
            Assert.Equal(50, s.Etriye.KancaIcCapMm, 0);
            Assert.Equal(2, s.Etriye.NKol);
            Assert.Equal(240, s.Etriye.EksenAraligiMm, 0);
            Assert.False(s.Govde.Gerekli);
        }

        [Fact]
        public void Govde_yuksek_kiriste_gerekir()
        {
            KirisDetayGirdi g = Temel(16, 2000, true);
            g.HMm = 700;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.True(s.Govde.Ts500Tetik);
            Assert.True(s.Govde.TbdyTetik);
            Assert.True(s.Govde.Gerekli);
            Assert.Equal(12, s.Govde.PhiMinMm, 0);
            Assert.True(s.Govde.OnerilenAdetYuz >= 1);
            KesitSonuc acik = s.Kesit(KesitYeri.Aciklik);
            Assert.Equal(s.Govde.OnerilenAdetYuz * 2, acik.Govde.Count);
        }

        [Fact]
        public void Paspayi_minimum_zorlanir()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Ayarlar.CcMm = 10;
            g.Ayarlar.Cevre = KirisCevre.Dis;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(25, s.CcKullanilanMm, 0);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.Cc && k.Seviye == KontrolSeviyesi.Uyari);

            g.Ayarlar.Cevre = KirisCevre.ZeminleTemas;
            g.Ayarlar.CcMm = 30;
            s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(50, s.CcKullanilanMm, 0);
        }

        [Fact]
        public void Cap_ve_adet_hatalari()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Donati.Montaj = new DonatiGrubu(2, 10);
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.False(s.Gecerli);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.Cap && k.Seviye == KontrolSeviyesi.Hata);

            g = Temel(16, 4000, true);
            g.Donati.Montaj = new DonatiGrubu(1, 16);
            s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.SurekliAdet);
        }

        [Fact]
        public void Dts_alt_ust_orani()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Donati.Montaj = new DonatiGrubu(2, 16);
            g.Donati.UstSolIlave = new DonatiGrubu(4, 16);
            g.Donati.UstSagIlave = new DonatiGrubu(4, 16);
            g.Donati.AltDuz = new DonatiGrubu(2, 16);
            g.Ayarlar.Dts = DepremTasarimSinifi.Dts1;
            g.Ayarlar.KisaAciklikL0Carpani = 0;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.MesnetOran && k.Seviye == KontrolSeviyesi.Hata);

            g.Ayarlar.Dts = DepremTasarimSinifi.Dts4;
            g.Ayarlar.Suneklik = SuneklikDuzeyi.Sinirli;
            s = KirisDetayMotoru.Hesapla(g);
            Assert.DoesNotContain(s.Kontroller, k => k.Kod == KirisKuralKodu.MesnetOran && k.Seviye == KontrolSeviyesi.Hata);
        }

        [Fact]
        public void Dy_sinirli_yalniz_dts3_ve_4()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Ayarlar.Suneklik = SuneklikDuzeyi.Sinirli;
            g.Ayarlar.Dts = DepremTasarimSinifi.Dts1;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.G12 && k.Seviye == KontrolSeviyesi.Hata);
        }

        [Fact]
        public void Bw_250_altinda_hata()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.BwMm = 200;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.G01 && k.Seviye == KontrolSeviyesi.Hata);
        }

        [Fact]
        public void C20_tbdy_kapsaminda_hata()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Ayarlar.Beton = BetonSinifi.C20;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.Mat && k.Seviye == KontrolSeviyesi.Hata);
        }

        [Fact]
        public void Bindirme_boylari_bolum_11()
        {
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(Temel(16, 4000, true));
            Assert.Equal(692, s.Kenetlenme.L0AltR05Mm, 0);
            Assert.Equal(830, s.Kenetlenme.L0AltR1Mm, 0);
            Assert.Equal(1163, s.Kenetlenme.L0UstR1Mm, 0);
        }

        [Fact]
        public void Buyuk_cap_katsayisi_phi36()
        {
            double fyd = KirisFormuller.Fyd(CelikSinifi.B420C, 1.15);
            double fctdFormul;
            double fctd = KirisFormuller.Fctd(BetonSinifi.C30, FctdKaynagi.Tablo, 1.5, out fctdFormul);
            double lb0 = KirisFormuller.Lb0(fyd, fctd, 36, true);
            double lb = KirisFormuller.LbHam(lb0, 36, false, false, true, false, 1);
            double beklenen = lb0 * (100.0 / (132.0 - 36));
            Assert.Equal(beklenen, lb, 3);
            Assert.True(lb > lb0);
        }

        [Fact]
        public void Moment_diyagrami_kesim_lb_dahil()
        {
            KirisDetayGirdi g = Temel(16, 6000, true);
            g.Donati.UstSolIlave = new DonatiGrubu(2, 16);
            g.KuramsalUstSolMm = 400;
            g.Ayarlar.KisaAciklikL0Carpani = 0;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.True(s.Gecerli, Hatalar(s));
            // kuramsal 400 + max(d, 20φ, ℓb=775) = 1175 → 1200 mm
            Assert.Equal(1200, s.Ilave.UstSolAciklikMm, 0);
        }

        [Fact]
        public void Ln_bolu_4_ayarlanabilir()
        {
            KirisDetayGirdi g = Temel(16, 8000, true);
            g.Donati.UstSolIlave = new DonatiGrubu(2, 16);
            g.Ayarlar.KisaAciklikL0Carpani = 0;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(2000, s.Ilave.UstSolAciklikMm, 0);

            g.Ayarlar.KLn = 0.5;
            s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(4000, s.Ilave.UstSolAciklikMm, 0);

            g.Ayarlar.KLn = 0.25;
            g.Sol.Tur = MesnetTuru.Ara;
            g.LnKomsuSolMm = 12000;
            s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(12000, s.Ilave.LnRefSolMm, 0);
            Assert.Equal(3000, s.Ilave.UstSolAciklikMm, 0);
        }

        [Fact]
        public void Kisa_aciklikta_ilaveler_birlesir()
        {
            KirisDetayGirdi g = Temel(16, 1500, true);
            g.Donati.UstSolIlave = new DonatiGrubu(2, 16);
            g.Donati.UstSagIlave = new DonatiGrubu(2, 16);
            g.Ayarlar.KisaAciklikL0Carpani = 0;
            g.Ayarlar.KisaAciklikBoslukMinMm = 500;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.True(s.Ilave.UstBirlesti);
            Assert.Contains(s.BoyunaCubuklar, c => c.Rol == DonatiRolu.UstIlaveSurekli && c.Surekli);
        }

        [Fact]
        public void Kose_kivrim_n_max_duser()
        {
            KirisDetayGirdi g = Temel(16, 4000, true);
            g.Ayarlar.KoseKivrimDuzeltmesi = true;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Equal(5, s.Kesit(KesitYeri.Aciklik).Alt.NMax);
            double x = KirisFormuller.KoseEksenIcYuzden(16, 10, 5);
            Assert.Equal(12.98, x, 2);
        }

        [Fact]
        public void Uzun_cubukta_ek_yasak_bolge_disinda()
        {
            KirisDetayGirdi g = Temel(16, 14000, false);
            g.Ayarlar.StokBoyMm = 12000;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.True(s.Gecerli, Hatalar(s));
            EkYeri alt = s.Ekler.First(e => e.Rol == DonatiRolu.AltDuz);
            EkYeri ust = s.Ekler.First(e => e.Rol == DonatiRolu.Montaj);
            double orta1 = 14000 / 3.0;
            double orta2 = 2 * 14000 / 3.0;
            Assert.True(alt.BitisMm <= orta1 + 1 || alt.BaslangicMm >= orta2 - 1);
            Assert.InRange((ust.BaslangicMm + ust.BitisMm) / 2.0, 2000, 12000);
            Assert.False(ust.OzelEtriyeGerekli);
            Assert.True(alt.OzelEtriyeGerekli);
        }

        [Fact]
        public void Phi30_ustu_manson()
        {
            KirisDetayGirdi g = Temel(16, 14000, false);
            g.Donati.Montaj = new DonatiGrubu(2, 32);
            g.Donati.AltDuz = new DonatiGrubu(2, 32);
            g.Ayarlar.StokBoyMm = 12000;
            g.Ayarlar.DmaxMm = 16;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.Contains(s.Ekler, e => e.Manson);
            Assert.Contains(s.Kontroller, k => k.Kod == KirisKuralKodu.Lap30);
        }

        [Fact]
        public void Dy_ve_dts_bos_ise_hesap_yapilmaz()
        {
            var g = new KirisDetayGirdi();
            g.BwMm = 300;
            g.HMm = 500;
            g.LnMm = 4000;
            g.Ayarlar.Suneklik = null;
            g.Ayarlar.Dts = null;
            KirisDetaySonuc s = KirisDetayMotoru.Hesapla(g);
            Assert.False(s.Gecerli);
            Assert.Equal(2, s.Kontroller.Count(k => k.Seviye == KontrolSeviyesi.Hata));
        }

        [Fact]
        public void Formul_smin_phi_25_dmax()
        {
            Assert.Equal(25, KirisFormuller.SMin(16, 16, 25), 1);
            Assert.Equal(29.3, KirisFormuller.SMin(16, 22, 25), 1);
            Assert.Equal(32, KirisFormuller.SMin(32, 16, 25), 0);
            Assert.Equal(33.3, KirisFormuller.EsdegerCap(16, 3), 1);
            Assert.Equal(1.58, KirisFormuller.BirimAgirlikKgM(16), 2);
        }
    }
}
