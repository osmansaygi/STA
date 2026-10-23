using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using ST4PlanIdCiz;

internal static class Program
{
    private static int _hata;

    private static void Main()
    {
        if (Math.Abs(TemelIlaveKutuBol.MinDilimGenislikCm - 120) > 0.01)
            Yaz("varsayilan en az dilim genisligi 120 cm olmali");
        // Eski senaryolar 8 x 20 cm = 160 cm ile kuruldu.
        TemelIlaveKutuBol.MinDilimGenislikCm = 160;
        var gf = new GeometryFactory();
        Polygon genis = Dik(gf, 0, 0, 600, 400);
        Polygon lob = Dik(gf, 20, 300, 220, 700);
        Polygon ic = Dik(gf, 80, 40, 200, 180);
        Polygon ayri = Dik(gf, 2000, 2000, 2300, 2400);

        List<double[]> ayni = TemelIlaveKutuBol.ZarfDilimle(new[] { genis, lob }, new[] { 1, 1 }, true);
        if (ayni.Count != 1 || !Kutu(ayni[0], 0, 0, 600, 700))
            Yaz("ayni lejant tek zarf olmali");

        List<double[]> farkli = TemelIlaveKutuBol.ZarfDilimle(new[] { genis, lob }, new[] { 1, 2 }, true);
        if (farkli.Count != 2
            || !Kutu(farkli[0], 0, 0, 600, 300)
            || !Kutu(farkli[1], 0, 300, 600, 700))
            Yaz("lejant dilimleri tek zarfin tam boyu olmali");

        var duseyKademe = new List<int>();
        List<double[]> dusey = TemelIlaveKutuBol.ZarfDilimle(new[] { genis, lob }, new[] { 1, 2 }, false, null, null, duseyKademe);
        if (duseyKademe.Count != 3 || duseyKademe[0] != 1 || duseyKademe[1] != 2 || duseyKademe[2] != 1)
            Yaz("her dilimin lejant kademesi verilmeli (1,2,1): " + string.Join(",", duseyKademe));
        if (dusey.Count != 3
            || !Kutu(dusey[0], 0, 0, 20, 700)
            || !Kutu(dusey[1], 20, 0, 220, 700)
            || !Kutu(dusey[2], 220, 0, 600, 700))
            Yaz("dusey dilimler tek zarfin tam boyu olmali");

        List<double[]> icte = TemelIlaveKutuBol.ZarfDilimle(new[] { genis, ic }, new[] { 1, 1 }, true);
        if (icte.Count != 1 || !Kutu(icte[0], 0, 0, 600, 400))
            Yaz("icte kalan ayni lejant dis zarf olmali");

        List<List<int>> kumeler = TemelIlaveKutuBol.IcIceKumeler(
            new[] { genis, lob, ayri, ic },
            new[] { true, true, true, false });
        if (kumeler.Count != 1 || kumeler[0].Count != 2)
            Yaz("yalniz kesisen ayni dogrultu zarflari birlesmeli");

        Polygon uzun = Dik(gf, 0, 0, 1000, 200);
        Polygon dar = Dik(gf, 400, 0, 450, 200);
        List<double[]> genisleyen = TemelIlaveKutuBol.ZarfDilimle(
            new[] { uzun, dar },
            new[] { 1, 2 },
            false,
            new double[] { 10, 30 },
            new[] { 20, 20 });
        if (genisleyen.Count != 3
            || !Kutu(genisleyen[0], -15, 0, 345, 200)
            || !Kutu(genisleyen[1], 345, 0, 505, 200)
            || !Kutu(genisleyen[2], 505, 0, 1005, 200))
            Yaz("dar buyuk As sekiz araliga iki yana esit genislemeli");

        var esit = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 300, Kademe = 1, Alan = 10, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 300, A1 = 350, Kademe = 3, Alan = 40, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 350, A1 = 800, Kademe = 2, Alan = 20, AralikCm = 20 }
        };
        TemelIlaveKutuBol.LejantGenislet(esit);
        if (esit.Count != 3
            || Math.Abs(esit[0].A1 - 245) > 1.0
            || Math.Abs(esit[1].A0 - 245) > 1.0
            || Math.Abs(esit[1].A1 - 405) > 1.0
            || Math.Abs(esit[2].A0 - 405) > 1.0)
            Yaz("en buyuk As komsu As farkina bakmadan esit buyumeli");

        var dilimler = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 200, Kademe = 2, Alan = 30, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 200, A1 = 220, Kademe = 1, Alan = 10, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 220, A1 = 500, Kademe = 2, Alan = 30, AralikCm = 20 }
        };
        TemelIlaveKutuBol.LejantGenislet(dilimler);
        if (dilimler.Count != 1 || Math.Abs(dilimler[0].A1 - dilimler[0].A0 - 500) > 1.0 || dilimler[0].Kademe != 2)
            Yaz("dar kucuk As buyuk komsuya katilmali");

        var kucuk = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 200, Kademe = 3, Alan = 50, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 200, A1 = 400, Kademe = 1, Alan = 10, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 400, A1 = 430, Kademe = 2, Alan = 30, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 430, A1 = 700, Kademe = 1, Alan = 5, AralikCm = 0 }
        };
        TemelIlaveKutuBol.LejantGenislet(kucuk);
        if (kucuk.Count != 4
            || Math.Abs(kucuk[1].A1 - 400) > 1.0
            || Math.Abs(kucuk[2].A0 - 400) > 1.0
            || Math.Abs(kucuk[2].A1 - 560) > 1.0
            || Math.Abs(kucuk[3].A0 - 560) > 1.0)
            Yaz("en buyuk olmayan dilim daha kucuk yana buyumeli");

        double sb0, sb1;
        if (!TemelIlaveKutuBol.StandartBoyUydur(100, 280, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb1 - sb0 - 200) > 0.1
            || Math.Abs((sb0 + sb1) * 0.5 - 190) > 0.1)
            Yaz("isi 180 cm 200 e iki yana esit buyumeli");
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 250, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb1 - sb0 - 300) > 0.1
            || Math.Abs((sb0 + sb1) * 0.5 - 125) > 0.1)
            Yaz("250 cm 300 e esit pad");
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 400, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb0) > 0.1 || Math.Abs(sb1 - 400) > 0.1)
            Yaz("tam standart boy kalmali");
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 320, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb1 - sb0 - 350) > 0.1)
            Yaz("320 cm 350 ye cikmali");
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 420, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb1 - sb0 - 450) > 0.1)
            Yaz("420 cm 450 ye cikmali");
        if (TemelIlaveKutuBol.StandartBoyUydur(0, 180, -5, 185, out sb0, out sb1))
            Yaz("paspay standart boyu engellemeli");
        if (TemelIlaveKutuBol.StandartBoyUydur(0, 1300, double.NegativeInfinity, double.PositiveInfinity, out sb0, out sb1))
            Yaz("isidan kisa standart boy secilmemeli");
        // Alt paspay 5 cm: esit pay sinira carpar, kalan pay ust yana gecer, boy 200 kalir.
        if (!TemelIlaveKutuBol.StandartBoyUydur(10, 190, 5, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb0 - 5) > 0.1 || Math.Abs(sb1 - 205) > 0.1)
            Yaz("paspaya dayanan yan payi karsiya gecmeli, boy 200");
        // Isi paspay cizgisini asiyor (0..190, sinir 5): kutu 5..205, paspay seridine girmez.
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 190, 5, double.PositiveInfinity, out sb0, out sb1)
            || Math.Abs(sb0 - 5) > 0.1 || Math.Abs(sb1 - 205) > 0.1)
            Yaz("paspayi asan isi sinirda kesilip boy 200 olmali");
        // Ust paspay: 0..230, sinir1 225 -> kirpik 225 -> 240, 225-240=-15..225.
        if (!TemelIlaveKutuBol.StandartBoyUydur(0, 230, double.NegativeInfinity, 225, out sb0, out sb1)
            || Math.Abs(sb1 - 225) > 0.1 || Math.Abs(sb1 - sb0 - 240) > 0.1)
            Yaz("ust paspay: boy 240 ve ust sinir 225");
        // Iki yan paspay, pencere 230: 200 sigar, 240 sigmaz.
        if (!TemelIlaveKutuBol.StandartBoyUydur(-10, 190, 0, 230, out sb0, out sb1)
            || Math.Abs(sb0) > 0.1 || Math.Abs(sb1 - 200) > 0.1)
            Yaz("iki yan paspay: 200 pencere icinde");
        if (TemelIlaveKutuBol.StandartBoyUydur(-10, 240, 0, 230, out sb0, out sb1))
            Yaz("iki yan paspay: 240 pencereye sigmaz");

        // Hiza adayi: buyuk donatili orta kutu soldakinin isisini ortmez; soldaki kutu uce de uyar.
        var sol = Aday(-540, 0, -530, -10);
        var orta = Aday(-562, -22, -520, -30);
        var sag = Aday(-562, -22, -500, -40);
        double h0, h1;
        if (!TemelIlaveKutuBol.DuseyHizaBul(new[] { sol, orta, sag }, 1, 100, out h0, out h1)
            || !HepsiUyar(h0, h1, sol, orta, sag)
            || Math.Abs(h0 + 550) > 0.4)
            Yaz("uc kutu ortak hizaya gelmeli (-550), bulunan " + h0);
        // Esitlikte buyuk donatili referans konumu korunur.
        if (!TemelIlaveKutuBol.DuseyHizaBul(new[] { Aday(-540, 0, -500, -40), orta }, 1, 100, out h0, out h1)
            || Math.Abs(h0 + 562) > 0.1 || Math.Abs(h1 + 22) > 0.1)
            Yaz("esitlikte referans korunmali, bulunan " + h0);
        // Hicbir kutunun kendi konumu ortak degil; ara konum ikisini de ortmeli (resim 4).
        var birinci = Aday(-25, 525, 0, 500);
        var ikinci = Aday(15, 565, 40, 540);
        if (!TemelIlaveKutuBol.DuseyHizaBul(new[] { birinci, ikinci }, 0, 100, out h0, out h1)
            || !HepsiUyar(h0, h1, birinci, ikinci)
            || Math.Abs(h0 + 10) > 0.4 || Math.Abs(h1 - h0 - 550) > 0.1)
            Yaz("ayni boy kutular ara konumda hizalanmali (-10..540), bulunan " + h0 + ".." + h1);
        // Gercek cizim (GRUP 4 gunlugu): ikinci kutunun isisi 500.3, kutu 500. Dort kutu ona hizalanmali.
        var g0 = Aday(2665.8, 3165.8, 2713.0, 3118.7);
        var g1 = Aday(2664.7, 3164.7, 2664.5, 3164.8);
        var g2 = Aday(2683.6, 3183.6, 2712.6, 3154.5);
        var g3 = Aday(2709.6, 3109.6, 2720.6, 3098.7);
        g0.Sinir0 = g1.Sinir0 = g2.Sinir0 = g3.Sinir0 = 2087.2;
        g0.Sinir1 = g1.Sinir1 = g2.Sinir1 = g3.Sinir1 = 3697.2;
        if (!TemelIlaveKutuBol.DuseyHizaUyar(g1, g1.Y0, g1.Y1, 100))
            Yaz("isisi boyu 0.3 asan kutu kendi yerine hizalanabilmeli");
        if (!TemelIlaveKutuBol.DuseyHizaBul(new[] { g0, g1, g2, g3 }, 2, 100, out h0, out h1)
            || !HepsiUyar(h0, h1, g0, g1, g2, g3))
            Yaz("GRUP 4: dort kutu tek hizaya gelmeli, bulunan " + h0 + ".." + h1);
        // Paspay ortak konumu engellerse referans kendi yerinde kalir.
        var paspayli = ikinci;
        paspayli.Isi1 = 560;
        paspayli.Sinir0 = 15;
        if (!TemelIlaveKutuBol.DuseyHizaBul(new[] { birinci, paspayli }, 0, 100, out h0, out h1)
            || Math.Abs(h0 + 25) > 0.1)
            Yaz("paspay engelinde referans yerinde kalmali, bulunan " + h0);
        if (TemelIlaveKutuBol.DuseyHizaUyar(paspayli, 10, 560, 100))
            Yaz("paspaya giren hiza uygulanmamali");

        // Buyuyen kutular: A ile B girisir, B ile C girisir (zincir), D kenar temasi, E baska yon.
        var kA = new List<double[]> { new double[] { 0, 0, 460, 540 } };
        var kB = new List<double[]> { new double[] { 420, 30, 600, 600 }, new double[] { 600, 30, 990, 600 } };
        var kC = new List<double[]> { new double[] { 980, 0, 1100, 500 } };
        var kD = new List<double[]> { new double[] { 1100, 0, 1300, 500 } };
        var kE = new List<double[]> { new double[] { 0, 0, 2000, 2000 } };
        List<List<int>> cakisan = TemelIlaveKutuBol.KutuCakisanKumeler(
            new List<List<double[]>> { kA, kB, kC, kD, kE },
            new[] { false, false, false, false, true });
        if (cakisan.Count != 1 || cakisan[0].Count != 3 || cakisan[0][0] != 0 || cakisan[0][1] != 1 || cakisan[0][2] != 2)
            Yaz("girisen kutular zincirle birlesmeli; kenar temasi ve baska yon birlesmemeli");
        if (TemelIlaveKutuBol.KutuCakisanKumeler(new List<List<double[]>> { kC, kD }, new[] { false, false }).Count != 0)
            Yaz("kenar temasi birlestirmemeli");

        if (Math.Abs(TemelIlaveKutuBol.MinDilimGenislikCm - 160) > 0.01)
            Yaz("test ayari bozuldu");

        TemelIlaveKutuBol.MinDilimGenislikCm = 80;
        List<double[]> dort = TemelIlaveKutuBol.ZarfDilimle(
            new[] { uzun, dar },
            new[] { 1, 2 },
            false,
            new double[] { 10, 30 },
            new[] { 20, 20 });
        TemelIlaveKutuBol.MinDilimGenislikCm = 160;
        if (dort.Count != 3
            || !Kutu(dort[0], -15, 0, 385, 200)
            || !Kutu(dort[1], 385, 0, 465, 200)
            || !Kutu(dort[2], 465, 0, 1005, 200))
            Yaz("aralik adedi forma gore genislemeli");

        var kat = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 200, Kademe = 1, Alan = 10, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 200, A1 = 373, Kademe = 2, Alan = 30, AralikCm = 20 },
            new TemelIlaveKutuBol.LejantDilim { A0 = 373, A1 = 600, Kademe = 1, Alan = 10, AralikCm = 20 }
        };
        TemelIlaveKutuBol.LejantGenislet(kat);
        if (kat.Count != 3
            || Math.Abs((kat[1].A1 - kat[1].A0) - 180) > 0.1
            || Math.Abs((kat[0].A1 - kat[0].A0) - 200) > 0.1
            || Math.Abs((kat[2].A1 - kat[2].A0) - 240) > 0.1
            || Math.Abs(((kat[1].A0 + kat[1].A1) * 0.5) - 286.5) > 0.1)
            Yaz("lejant genisligi araligin kati olmali");

        if (!TemelIlaveKutuBol.DuseyBoyYakin(380, 400, 100) || TemelIlaveKutuBol.DuseyBoyYakin(200, 400, 100))
            Yaz("yakin dusey boy en buyuk As hizasina girmeli");

        var isiDisi = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 100, Kademe = 1, Alan = 20, AralikCm = 20 }
        };
        TemelIlaveKutuBol.LejantGenislet(isiDisi);
        if (isiDisi.Count != 1 || Math.Abs((isiDisi[0].A1 - isiDisi[0].A0) - 100) > 0.01)
            Yaz("sekiz aralik isi disina tasamamali");

        var kusur = new List<TemelIlaveKutuBol.LejantDilim>
        {
            new TemelIlaveKutuBol.LejantDilim { A0 = 0, A1 = 179.9985, Kademe = 1, Alan = 20, AralikCm = 20 }
        };
        TemelIlaveKutuBol.LejantGenislet(kusur);
        double kusurBoy = kusur[0].A1 - kusur[0].A0;
        if (kusur.Count != 1 || Math.Abs(kusurBoy - 180) > 0.0001 || Math.Abs(kusurBoy - Math.Round(kusurBoy)) > 0.0001)
            Yaz("kusuratli genislik aralik katina oturmali");

        if (!TemelIlaveKutuBol.HizaIsiyiOrter(100, 400, 120, 360)
            || TemelIlaveKutuBol.HizaIsiyiOrter(150, 400, 100, 360))
            Yaz("kucuk lejant isiya giren hizayi almamali");
        Environment.Exit(_hata == 0 ? 0 : 1);
    }

    private static bool Kutu(double[] k, double x0, double y0, double x1, double y1)
    {
        return Math.Abs(k[0] - x0) < 1.0
            && Math.Abs(k[1] - y0) < 1.0
            && Math.Abs(k[2] - x1) < 1.0
            && Math.Abs(k[3] - y1) < 1.0;
    }

    private static Polygon Dik(GeometryFactory gf, double x0, double y0, double x1, double y1)
    {
        return gf.CreatePolygon(new[]
        {
            new Coordinate(x0, y0),
            new Coordinate(x1, y0),
            new Coordinate(x1, y1),
            new Coordinate(x0, y1),
            new Coordinate(x0, y0)
        });
    }

    private static bool HepsiUyar(double h0, double h1, params TemelIlaveKutuBol.DuseyAday[] kutular)
    {
        foreach (TemelIlaveKutuBol.DuseyAday k in kutular)
            if (!TemelIlaveKutuBol.DuseyHizaUyar(k, h0, h1, 100)) return false;
        return true;
    }

    private static TemelIlaveKutuBol.DuseyAday Aday(double y0, double y1, double isi0, double isi1)
    {
        return new TemelIlaveKutuBol.DuseyAday
        {
            Y0 = y0,
            Y1 = y1,
            IsiVar = true,
            Isi0 = isi0,
            Isi1 = isi1,
            Sinir0 = double.NegativeInfinity,
            Sinir1 = double.PositiveInfinity
        };
    }

    private static void Yaz(string mesaj)
    {
        _hata++;
        Console.WriteLine("FAIL " + mesaj);
    }
}
