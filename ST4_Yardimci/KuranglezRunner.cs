using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Kuranglez donatı detayı, ölçek 1/25 (2 çizim birimi = 1 cm, DIMLFAC 0.5).
    /// Plan: kapama + üç taraf perde. Kesit: temel, grobeton, 5×5 kapak dişi.
    /// Donatı kapama perde gibi iki yüzlü; düşey filiz bindirmeli, yatay aralıklı.
    /// </summary>
    internal static class KuranglezRunner
    {
        const double Du = 2.0;
        const double Txt = 10.0;
        const double TitleH = 20.0;
        const double BendK = 0.41421356237;
        const double BendR = 1.5;
        /// <summary>Kesitte görülen çubuk: dolu daire çapı 3 cm (1/25, gerçek cm).</summary>
        const double DonatiNoktaCapCm = 3.0;
        const string DimStyle25 = "PLAN_OLCU_DONATI 25";

        const double KotTriH = 12.0;
        const double KotTriW = 12.0;
        const double KotExtSec = 20.0;
        const double KotExtSag = 26.0;
        const double KotYaziH = 10.0;
        const double KotYaziPay = 5.7;
        const short KotWedgeAci = 250;

        const string KesitSembolResource = "ST4Yardimci.KesitSembol.kesit_sembol.dwg";
        const double KesitSembolOx = 1660.7;
        const double KesitSembolOy = 1206.23;
        const double KesitSembolYaricap = 20.0;
        const double KesitSembolPayCm = 5.0;

        const string LayerPerde = "PERDE (BEYKENT)";
        const string LayerTemel = "TEMEL (BEYKENT)";
        const string LayerZemin = "ZEMIN (BEYKENT)";
        const string LayerTarama = "TARAMA (BEYKENT)";
        const string LayerDoseme = "DOSEME (BEYKENT)";
        const string LayerDonati = "DONATI (BEYKENT)";
        const string LayerDonatiIzi = "DONATI IZI (BEYKENT)";
        const string LayerEtriye = "ETRIYE (BEYKENT)";
        const string LayerYaziDon = "DONATI YAZISI (BEYKENT)";
        const string LayerEtiket = "ETIKET CIZGISI (BEYKENT)";
        const string LayerYazi = "YAZI (BEYKENT)";
        const string LayerOlcu = "OLCU (BEYKENT)";
        const string LayerKot = "KOT CIZGI (BEYKENT)";
        const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        const string LayerKesit = "KESIT SINIRI (BEYKENT)";
        const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";
        /// <summary>Perde kesit düşey etiket sapı (cm) — kolon düşey ile aynı.</summary>
        const double DuseyEtiketSapCm = 12.0;

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            try
            {
                KuranglezSettings.EnsureLoaded();
                string err = KuranglezSettings.Validate();
                if (err != null)
                {
                    ed.WriteMessage("\nKURANGLEZ: " + err);
                    return;
                }

                KuranglezForm.HideTemporarily();
                var opt = new PromptPointOptions("\nKuranglez detayı yerleşim noktası (plan sol-alt): ");
                opt.AllowNone = false;
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK) return;

                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var d = new Drawer(tr, doc.Database, ppr.Value);
                    d.DrawAll();
                    tr.Commit();
                }
                ed.WriteMessage(
                    "\nKURANGLEZ: {0:0}/{1:0} cm, ölçek 1/25.",
                    KuranglezSettings.EnCm, KuranglezSettings.BoyCm);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nKURANGLEZ: " + ex.Message);
            }
            finally
            {
                BaFormSession.EndPick("KURANGLEZ");
            }
        }

        sealed class Drawer
        {
            readonly Transaction _tr;
            readonly Database _db;
            readonly BlockTableRecord _btr;
            readonly Point3d _pick;
            readonly ObjectId _yazi;
            readonly ObjectId _donYazi;
            readonly ObjectId _kotStyle;
            readonly ObjectId _dimStyle;
            readonly HashSet<string> _dotKeys = new HashSet<string>();
            bool _inSection;

            double _secOx;
            double _secOy;

            public Drawer(Transaction tr, Database db, Point3d pick)
            {
                _tr = tr;
                _db = db;
                _pick = pick;
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                _btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                EnsureLayers();
                _yazi = Style("YAZI (BEYKENT)");
                _donYazi = Style("DONATI YAZISI (BEYKENT)");
                _kotStyle = Style("KOT (BEYKENT)");
                _dimStyle = ResolveOrCreatePlanOlcuDonati25();
            }

            public void DrawAll()
            {
                double kap = KuranglezSettings.KapamaCm;
                double perde = KuranglezSettings.PerdeCm;
                double en = KuranglezSettings.EnCm;
                double boy = KuranglezSettings.BoyCm;
                double ext = KuranglezSettings.KapamaTasmaCm;
                double planW = kap + en + perde;
                double planH = ext + perde + boy + perde + ext;
                _secOx = planW + 150.0;
                // Kesit bloğu (açılımlarla) 15 cm aşağı.
                _secOy = -KuranglezSettings.GrobetonCm - 15.0;

                DrawPlan(kap, perde, en, boy, ext, planW, planH);
                DrawSection();
                // Başlık: toplam çizim ortası (plan+kesit), altta.
                double xRsec = KuranglezSettings.TemelSolCm + kap + en + perde
                    + KuranglezSettings.TemelSagCm;
                double titleX = (_secOx + xRsec) * 0.5;
                Title(titleX, -90, string.Format(CultureInfo.InvariantCulture,
                    "%%U ({0:0}/{1:0}) KURANGLEZ DONATI DETAYI", en, boy));
                Text(LayerYazi, _yazi, "ÖLÇEK: 1/25", titleX, -106, Txt, 0, true);
            }

            void DrawPlan(double kap, double perde, double en, double boy, double ext, double planW, double planH)
            {
                double x0 = 0, x1 = kap, x2 = kap + en, x3 = planW;
                double y0 = 0, y1 = ext, y2 = y1 + perde, y3 = y2 + boy, y4 = y3 + perde, y5 = planH;
                double pas = KuranglezSettings.PaspayiCm;
                double pasKap = KuranglezSettings.KapamaPaspayiCm;
                double inset = pas + KuranglezSettings.KesitMerkezEkCm;
                double insetKap = pasKap + KuranglezSettings.KesitMerkezEkCm;
                double sD = KuranglezSettings.DuseyAralikCm;
                int yDia = KuranglezSettings.YatayCapMm;
                KuranglezSettings.GonyeBacak(yDia, perde, pas, out double hp1, out double hp2);
                double gonye = KuranglezSettings.GonyeCm(yDia);

                // Kapama perde katmanı PERDE. Uçlar kesit sınırı (duvar devam eder).
                Line(LayerKesit, x0, y0, x1, y0);
                Line(LayerPerde, x0, y0, x0, y5);
                Line(LayerKesit, x0, y5, x1, y5);
                Line(LayerPerde, x1, y0, x1, y1);
                Line(LayerPerde, x1, y2, x1, y3);
                Line(LayerPerde, x1, y4, x1, y5);
                Line(LayerPerde, x1, y1, x3, y1);
                Line(LayerPerde, x3, y1, x3, y4);
                Line(LayerPerde, x3, y4, x1, y4);
                Line(LayerPerde, x1, y3, x2, y3);
                Line(LayerPerde, x2, y3, x2, y2);
                Line(LayerPerde, x2, y2, x1, y2);

                // Köşe / birleşim: her yüzde iki sabit çubuk (2×2). Aradakiler aralığı aşmadan eşit.
                double c = inset;
                double cKap = insetKap;
                var kapY = FaceRun(sD, y0 + cKap, y1 + c, y2 - c, y3 + c, y4 - c, y5 - cKap);
                DotRowX(x0 + cKap, kapY, LayerDonatiIzi);
                DotRowX(x1 - cKap, kapY, LayerDonatiIzi);
                var longY = FaceRun(sD, y1 + c, y2 - c, y3 + c, y4 - c);
                DotRowX(x2 + c, longY, LayerDonati);
                DotRowX(x3 - c, longY, LayerDonati);
                var kisaX = FaceRun(sD, x1 + cKap, x2 + c, x3 - c);
                DotRowY(y1 + c, kisaX, LayerDonati);
                DotRowY(y2 - c, kisaX, LayerDonati);
                DotRowY(y3 + c, kisaX, LayerDonati);
                DotRowY(y4 - c, kisaX, LayerDonati);

                // Düşey kesit etiketleri (kolon düşey / perde kesit stili).
                // Kapama planda etiketlenmez. Üst/alt: kapama yanından başlar, uzun perde
                // iç köşesi (x2+c) ve dışı etiket dışı — köşe yalnız yanda.
                int dDia = KuranglezSettings.DuseyCapMm;
                string duseyLab(int n) => string.Format(CultureInfo.InvariantCulture,
                    "2x{0}\u00F8{1}/{2:0} DİKEY", n, dDia, sD);
                if (longY.Count > 0)
                    DrawDuseyKesitEtiketY(longY, x3 - c, +1, duseyLab(longY.Count), false);
                if (kisaX.Count > 0)
                {
                    double xIcKose = x2 + c;
                    var kisaEtiket = new List<double>();
                    foreach (double x in kisaX)
                    {
                        if (x < xIcKose - 0.5)
                            kisaEtiket.Add(x);
                    }
                    if (kisaEtiket.Count > 0)
                    {
                        DrawDuseyKesitEtiketX(kisaEtiket, y1 + c, -1, duseyLab(kisaEtiket.Count), false);
                        DrawDuseyKesitEtiketX(kisaEtiket, y4 - c, +1, duseyLab(kisaEtiket.Count), false);
                    }
                }

                // Kapama yatayları kesit sınırına kadar, gönyesiz (pas 4 cm).
                Line(LayerDonatiIzi, x0 + pasKap, y0, x0 + pasKap, y5);
                Line(LayerDonatiIzi, x1 - pasKap, y0, x1 - pasKap, y5);
                HorizFace(x2 + pas, y1 + pas, y4 - pas, hp1, hp2, +1, LayerEtriye);
                HorizFace(x3 - pas, y1 + pas, y4 - pas, hp1, hp2, -1, LayerEtriye);
                // Kısa perde yatayları kapama dış paspayına kadar girer.
                double xIn = x0 + pasKap;
                HorizFaceX(y1 + pas, xIn, x3 - pas, gonye, 0, hp1, hp2, +1, LayerEtriye);
                HorizFaceX(y2 - pas, xIn, x3 - pas, gonye, 0, hp1, hp2, -1, LayerEtriye);
                HorizFaceX(y3 + pas, xIn, x3 - pas, gonye, 0, hp1, hp2, +1, LayerEtriye);
                HorizFaceX(y4 - pas, xIn, x3 - pas, gonye, 0, hp1, hp2, -1, LayerEtriye);

                double yCut = y2 + boy / 5.0;
                PlaceKesitCifti(x0, x3, yCut);

                double yDimH = y3 - boy * 0.25;
                DimH(y3, yDimH, x0, x1);
                DimH(y3, yDimH, x1, x2);
                DimH(y3, yDimH, x2, x3);
                double xDimV = x2 - en * 0.25;
                DimV(x2, xDimV, y1, y2);
                DimV(x2, xDimV, y2, y3);
                DimV(x2, xDimV, y3, y4);

                DrawPlanAcilim(x0, x3, y1, y4, y5, pas, gonye, hp1, hp2, yDia);
            }

            void DrawSection()
            {
                _inSection = true;
                double gro = KuranglezSettings.GrobetonCm;
                double temel = KuranglezSettings.TemelCm;
                double yuk = KuranglezSettings.YukseklikCm;
                double kapUst = KuranglezSettings.KapamaUstCm;
                double kap = KuranglezSettings.KapamaCm;
                double en = KuranglezSettings.EnCm;
                double perde = KuranglezSettings.PerdeCm;
                double sol = KuranglezSettings.TemelSolCm;
                double sag = KuranglezSettings.TemelSagCm;
                double pas = KuranglezSettings.PaspayiCm;
                double pasKap = KuranglezSettings.KapamaPaspayiCm;
                double inset = pas + KuranglezSettings.KesitMerkezEkCm;
                double tPas = KuranglezSettings.TemelPasCm;
                double tInset = tPas + KuranglezSettings.KesitMerkezEkCm;
                double lap = KuranglezSettings.BindirmeCm;
                int dDia = KuranglezSettings.DuseyCapMm;
                KuranglezSettings.GonyeBacak(dDia, perde, pas, out double hp1, out double hp2);
                double fHook = KuranglezSettings.GonyeCm(dDia);
                double disX = KuranglezSettings.DisEnCm;
                double disY = KuranglezSettings.DisBoyCm;
                double kapak = KuranglezSettings.KapakCm;
                double sY = KuranglezSettings.YatayAralikCm;

                double yFb = gro;
                double yFt = gro + temel;
                double y0 = yFt + yuk;
                double yKap = y0 + kapUst;
                double xL = sol;
                double xKR = xL + kap;
                double xWL = xKR + en;
                double xWR = xWL + perde;
                double xR = xWR + sag;
                double xN = xWL + disX;
                double yN = y0 + disY;

                // Grobeton: alt ZEMIN, üst TEMEL. Yan finiş yalnız kesit sınırı.
                Line(LayerZemin, 0, 0, xR, 0);
                Line(LayerTemel, 0, yFb, xR, yFb);
                HatchRect(LayerTarama, 0, 0, xR, yFb, "AR-CONC", 0.05);
                TextS(LayerYazi, "GROBETON", xR * 0.5, yFb * 0.5, 0);

                // Temel üstü perde altlarında kesik. Yan finiş yok.
                Line(LayerTemel, 0, yFt, xL, yFt);
                Line(LayerTemel, xKR, yFt, xWL, yFt);
                Line(LayerTemel, xWR, yFt, xR, yFt);

                // Kapama: alt kenar yok. Üst uç kesit sınırı (duvar devam eder).
                Line(LayerPerde, xL, yFt, xL, yKap);
                Line(LayerKesit, xL, yKap, xKR, yKap);
                Line(LayerPerde, xKR, yKap, xKR, yFt);
                // Dış perde + 5×5 diş.
                Line(LayerPerde, xWL, yFt, xWL, y0);
                Line(LayerPerde, xWL, y0, xN, y0);
                Line(LayerPerde, xN, y0, xN, yN);
                Line(LayerPerde, xN, yN, xWR, yN);
                Line(LayerPerde, xWR, yN, xWR, yFt);

                if (kapak > 0.5)
                    Rect(LayerDoseme, xKR, y0, xN, y0 + kapak);

                // Kesit sınırları (temel devamı).
                Line(LayerKesit, 0, 0, 0, yFt);
                Line(LayerKesit, xR, 0, xR, yFt);

                double ek = KuranglezSettings.KesitMerkezEkCm;
                double insetKap = pasKap + ek;
                // Kapama düşey: kenardan pasKap (4 cm). Yatay kesit noktaları: insetKap (4+1,5).
                double xBarKapDis = xL + pasKap;
                double xBarKapIc = xKR - pasKap;
                double xBarPerDis = xWR - pas;
                double xBarPerIc = xWL + pas;

                // Düşey: filiz temel paspayından, gövde temel üstünden, üst gönye perde içine.
                double yFiliz0 = yFb + tPas;
                double yFiliz1 = yFt + lap;
                double yGovde1Per = y0 - pas;
                // Kapama yatayları üst kesitten paspayı ile; perde yatayları üst gönye merkezinden.
                double yYatayMin = yFt + pas;
                if (sY < 1) sY = 1;
                for (double y = yKap - pasKap; y >= yYatayMin - 0.05; y -= sY)
                {
                    Dot(xL + insetKap, y, true, LayerDonatiIzi);
                    Dot(xKR - insetKap, y, true, LayerDonatiIzi);
                }
                var yatayYs = CollectYatayFromUstRadius(yGovde1Per, yYatayMin, hp1, hp2, sY);
                YatayFromUstRadius(xBarPerIc, +1, yGovde1Per, yYatayMin, hp1, hp2, sY, LayerDonati);
                YatayFromUstRadius(xBarPerDis, -1, yGovde1Per, yYatayMin, hp1, hp2, sY, LayerDonati);
                // Perde yatay kesit etiket (kapama yok; plan düşey stili).
                if (yatayYs.Count > 0)
                {
                    int yDia = KuranglezSettings.YatayCapMm;
                    string yatayLab = string.Format(CultureInfo.InvariantCulture,
                        "2x{0}\u00F8{1}/{2:0} YATAY", yatayYs.Count, yDia, sY);
                    double rY = BendRadius(hp1, hp2);
                    double xEtiket = xBarPerDis - rY;
                    DrawDuseyKesitEtiketY(yatayYs, xEtiket, +1, yatayLab, section: true);
                }
                // Filiz gönyeleri ters (dışa bakar).
                BarV(_secOx, _secOy, xBarKapDis, yFiliz0, yFiliz1, fHook, 0, -1, true, LayerDonatiIzi);
                BarV(_secOx, _secOy, xBarKapIc, yFiliz0, yFiliz1, fHook, 0, +1, true, LayerDonatiIzi);
                BarV(_secOx, _secOy, xBarPerIc, yFiliz0, yFiliz1, fHook, 0, -1, true, LayerDonati);
                BarV(_secOx, _secOy, xBarPerDis, yFiliz0, yFiliz1, fHook, 0, +1, true, LayerDonati);
                // Kapama gövde: üst kesit sınırına kadar, gönyesiz.
                Line(LayerDonatiIzi, xBarKapDis, yFt, xBarKapDis, yKap, true);
                Line(LayerDonatiIzi, xBarKapIc, yFt, xBarKapIc, yKap, true);
                BarV(_secOx, _secOy, xBarPerIc, yFt, yGovde1Per, hp1, hp2, +1, false, LayerDonati);
                BarV(_secOx, _secOy, xBarPerDis, yFt, yGovde1Per, hp1, hp2, -1, false, LayerDonati);

                // Kapak U: üç yüz paspayı. Toplam merkez boyu 100 cm, kollar eşit.
                double uTop = yN - pas;
                double uR = xWR - pas;
                double uL = xN + pas;
                if (uR - uL < 2.0)
                {
                    double mid = (xN + xWR) * 0.5;
                    uL = mid - 1.0;
                    uR = mid + 1.0;
                }
                double uSpan = uR - uL;
                double uRds = BendR;
                if (uSpan < 2.2 * uRds) uRds = Math.Max(0.4, uSpan / 2.2);
                double uLeg = (100.0 - uSpan - uRds * (Math.PI - 4.0)) * 0.5;
                BarU(uL, uR, uTop, uTop - uLeg, uRds);

                // Temel donatısı her zaman çizilir.
                {
                    double yA = yFb + tPas;
                    double yB = yFt - tPas;
                    Line(LayerDonatiIzi, 0, yA, xR, yA, true);
                    Line(LayerDonatiIzi, 0, yB, xR, yB, true);
                    // Duvar altı 2×2 sabit (kapama: insetKap, kuranglez: inset); aradakiler sF.
                    double sF = KuranglezSettings.YatayAralikCm;
                    var fxs = FaceRun(sF, tInset,
                        xL + insetKap, xKR - insetKap,
                        xWL + inset, xWR - inset,
                        xR - tInset);
                    foreach (double xx in fxs)
                    {
                        Dot(xx, yFb + tInset, true, LayerDonatiIzi);
                        Dot(xx, yFt - tInset, true, LayerDonatiIzi);
                    }
                }

                double kotUstCm = KuranglezSettings.TemelUstKotM * 100.0;
                double apexX = xR + 15.0;
                Kot(apexX, yFb, kotUstCm - temel);
                Kot(apexX, yFt, kotUstCm);
                // Kuranglez üst = temel üst + yükseklik.
                Kot(apexX, y0, kotUstCm + yuk);

                DimVS(0, -18, 0, yFb);
                DimVS(0, -18, yFb, yFt);
                DimVS(0, -18, yFt, y0);
                // Bindirme ölçüsü yok (temel kalınlığı ile karışıyordu).
                DimHS(yN, yN + 12, xWL, xN);
                DimVS(xN, xN - 12, y0, yN);

                // Kesit yazısı kesitin altında (grobeton altı).
                KesitBaslik((xL + xWR) * 0.5, -12);
                DrawSectionAcilim(xR, yFb, yFt, pas, tPas, lap, yuk, hp1, hp2, fHook, dDia,
                    uSpan, uLeg, uRds, uTop);
                _inSection = false;
            }

            void DrawPlanAcilim(double x0, double x3, double y1, double y4, double y5,
                double pas, double gonye, double hp1, double hp2, int dia)
            {
                double sY = KuranglezSettings.YatayAralikCm;
                double yuk = KuranglezSettings.YukseklikCm;
                // Yatay seviye = kesitteki gibi; +1 kapak dişi kesitleri (açılımdan taşındı).
                int yuzYatay = CountYataySeviye(yuk - pas, pas, BendRadius(hp1, hp2), sY) + 1;
                // İki yüz arası = perde kalınlığı; yatayda 5 cm şaşırt.
                double sas = KuranglezSettings.PerdeCm;
                if (sas < 5.0) sas = 5.0;
                const double sasBoy = 5.0;
                double xa = x0 + pas;
                double xb = x3 - pas;
                // Üst açılım 15 cm aşağı (eski y5+36).
                double yU0 = y5 + 21.0;
                double yU1 = yU0 + sas;
                HorizFaceX(yU0, xa, xb, gonye, 0, hp1, hp2, +1, LayerEtriye);
                HorizFaceX(yU1, xa + sasBoy, xb + sasBoy, gonye, 0, hp1, hp2, -1, LayerEtriye);
                YazYatayAcilimCift(xa, xb, yU0, yU1, gonye, hp1, yuzYatay, dia, sY);
                // Alt açılım 15 cm yukarı (eski −42).
                double yA1 = -27.0;
                double yA0 = yA1 - sas;
                HorizFaceX(yA1, xa + sasBoy, xb + sasBoy, gonye, 0, hp1, hp2, -1, LayerEtriye);
                HorizFaceX(yA0, xa, xb, gonye, 0, hp1, hp2, +1, LayerEtriye);
                YazYatayAcilimCift(xa, xb, yA0, yA1, gonye, hp1, yuzYatay, dia, sY);
                // Yan açılım 15 cm sağa (eski x3+36).
                double xS = x3 + 51.0;
                double ya = y1 + pas;
                double yb = y4 - pas;
                HorizFace(xS, ya, yb, hp1, hp2, +1, LayerEtriye);
                HorizFace(xS + sas, ya + sasBoy, yb + sasBoy, hp1, hp2, -1, LayerEtriye);
                double hG = hp1 > 0.5 ? hp1 : gonye;
                YazDikeyAcilimCift(xS, xS + sas, ya, yb, hG, hG, yuzYatay, dia, sY);
            }

            void DrawSectionAcilim(double xR, double yFb, double yFt,
                double pas, double tPas, double lap, double yuk,
                double hp1, double hp2, double fHook, int dia,
                double uSpan, double uLeg, double uRds, double uTop)
            {
                double sD = KuranglezSettings.DuseyAralikCm;
                double ext = KuranglezSettings.KapamaTasmaCm;
                double perde = KuranglezSettings.PerdeCm;
                double boy = KuranglezSettings.BoyCm;
                double kap = KuranglezSettings.KapamaCm;
                double en = KuranglezSettings.EnCm;
                double c = pas + KuranglezSettings.KesitMerkezEkCm;
                double y1 = ext;
                double y2 = y1 + perde;
                double y3 = y2 + boy;
                double y4 = y3 + perde;
                double x1 = kap;
                double x2 = kap + en;
                double x3 = x2 + perde;
                // Plandaki kuranglez düşeyleri ile aynı adet (2x N).
                int yuz = CountKuranglezDuseyYuz(sD, c, y1, y2, y3, y4, x1, x2, x3);

                // Düşey/filiz 40 cm sağa; kapak 60 cm sola (xV+30 → xV−30).
                double xV = xR + 95.0;
                double yGov = yFt + yuk - pas;
                BarV(_secOx, _secOy, xV, yFt, yGov, hp1, hp2, +1, false, LayerDonati);
                YazDikeyAcilim(xV, yFt, yGov, +1, 0, 0, hp1, hp2, yuz, dia, sD, "(düşey)", true);

                double yFil0 = yFb + tPas;
                double yFil1 = yFt + lap;
                double yTemelOrta = (yFb + yFt) * 0.5;
                // Filiz gönye +X (ters); ana boy yazısı temel ortasında.
                BarV(_secOx, _secOy, xV - 5.0, yFil0, yFil1, fHook, 0, +1, true, LayerDonati);
                YazDikeyAcilim(xV - 5.0, yFil0, yFil1, +1, fHook, 0, 0, 0, yuz, dia, sD, "(filiz)", true,
                    anaBoyYCm: yTemelOrta);
                // Bindirme: düşey altı (temel üstü) ↔ filiz üstü, iki açılım arası.
                DimVS(xV - 2.5, xV + 14.0, yFt, yFil1);

                double xU = xV - 30.0;
                BarU(xU, xU + uSpan, uTop, uTop - uLeg, uRds, drawDots: false);
                YazKapakAcilim(xU, xU + uSpan, uTop, uLeg, yuz, dia, sD);
            }

            /// <summary>Çift yatay açılım. Yazı kutusu kenarı çizgiye 1 cm.</summary>
            void YazYatayAcilimCift(double xL, double xR, double yLo, double yHi,
                double hL, double hR, int yuzAdet, int dia, double aralik)
            {
                const double pay = 1.0;
                double boy = xR - xL;
                double toplam = boy + hL + hR;
                double xm = (xL + xR) * 0.5;
                double ym = (yLo + yHi) * 0.5;
                SnapYazi(EtiketYazi(yuzAdet, dia, aralik, "(yatay)", toplam),
                    xm, yHi + pay, 0, false, "bottom", yHi + pay);
                // Ana boy: üst çizginin 1+2=3 cm altında.
                SnapYazi(Cm(boy), xm, yHi - pay - 2.0, 0, false, "top", yHi - pay - 2.0);
                SnapYazi(Cm(hL), xL - pay, ym, Math.PI * 0.5, false, "right", xL - pay);
                SnapYazi(Cm(hR), xR + pay, ym, Math.PI * 0.5, false, "left", xR + pay);
            }

            /// <summary>
            /// Kesit düşey/filiz yazıları. Etiket ve ana boy çizgiye 1,5 cm.
            /// anaBoyYCm &gt; 0 ise ana boy o kota (ör. temel ortası).
            /// </summary>
            void YazDikeyAcilim(double x, double y0, double y1, int dir,
                double hAlt, double h2Alt, double hUst, double h2Ust,
                int yuzAdet, int dia, double aralik, string tur, bool section,
                double anaBoyYCm = 0)
            {
                const double pay = 1.5;
                double boy = y1 - y0;
                double toplam = boy + hAlt + h2Alt + hUst + h2Ust;
                double ym = (y0 + y1) * 0.5;
                double yAna = anaBoyYCm > 0.5 ? anaBoyYCm : ym;
                SnapYazi(EtiketYazi(yuzAdet, dia, aralik, tur, toplam),
                    x - pay, ym, Math.PI * 0.5, section, "right", x - pay);
                SnapYazi(Cm(boy), x + pay, yAna, Math.PI * 0.5, section, "left", x + pay);
                if (hAlt > 0.5)
                    SnapYazi(Cm(hAlt), x + dir * hAlt * 0.5, y0 - pay, 0, section, "top", y0 - pay);
                if (h2Alt > 0.5)
                    SnapYazi(Cm(h2Alt), x + dir * hAlt + dir * pay, y0 + h2Alt * 0.5, Math.PI * 0.5, section, "left", x + dir * hAlt + dir * pay);
                if (hUst > 0.5)
                    SnapYazi(Cm(hUst), x + dir * hUst * 0.5, y1 + pay, 0, section, "bottom", y1 + pay);
                if (h2Ust > 0.5)
                    SnapYazi(Cm(h2Ust), x + dir * hUst + dir * pay, y1 - h2Ust * 0.5, Math.PI * 0.5, section, "left", x + dir * hUst + dir * pay);
            }

            /// <summary>Çift düşey (plan yan). Ana boy 5 cm sola.</summary>
            void YazDikeyAcilimCift(double xL, double xR, double y0, double y1,
                double hAlt, double hUst, int yuzAdet, int dia, double aralik)
            {
                const double pay = 1.0;
                double boy = y1 - y0;
                double toplam = boy + hAlt + hUst;
                double ym = (y0 + y1) * 0.5;
                double xm = (xL + xR) * 0.5;
                SnapYazi(EtiketYazi(yuzAdet, dia, aralik, "(yatay)", toplam),
                    xL - pay, ym, Math.PI * 0.5, false, "right", xL - pay);
                SnapYazi(Cm(boy), xm - 5.0, ym, Math.PI * 0.5, false, "centerX", xm - 5.0);
                if (hAlt < 0.5) hAlt = hUst;
                if (hUst < 0.5) hUst = hAlt;
                if (hAlt > 0.5)
                    SnapYazi(Cm(hAlt), xm, y0 - pay, 0, false, "top", y0 - pay);
                if (hUst > 0.5)
                    SnapYazi(Cm(hUst), xm, y1 + pay, 0, false, "bottom", y1 + pay);
            }

            /// <summary>
            /// Kapak açılım yazıları. Etiket 90° (düşey), sol bacağın hemen solunda.
            /// Sol boy yok; sağ boy + üst span boy kalır.
            /// </summary>
            void YazKapakAcilim(double xL, double xR, double yTop, double leg, int adet, int dia, double aralik)
            {
                const double pay = 1.5;
                double xm = (xL + xR) * 0.5;
                double span = xR - xL;
                double ym = yTop - leg * 0.5;
                // Etiket düşey; yazı kutusu sağ kenarı sol bacağa.
                SnapYazi(EtiketYazi(adet, dia, aralik, "(kapak)", 100.0),
                    xL - pay, ym, Math.PI * 0.5, true, "right", xL - pay);
                SnapYazi(Cm(span), xm, yTop - pay, 0, true, "top", yTop - pay);
                SnapYazi(Cm(leg), xR + pay, ym, Math.PI * 0.5, true, "left", xR + pay);
            }

            /// <summary>Kesitteki yatay seviye adedi (üst radius merkezinden aşağı).</summary>
            static int CountYataySeviye(double yGovde, double yMin, double r, double step)
            {
                if (step < 1) step = 1;
                double yC = yGovde - r;
                int n = 0;
                for (double y = yC; y >= yMin - 0.05 && n < 80; y -= step) n++;
                return Math.Max(n, 1);
            }

            /// <summary>
            /// Plandaki kuranglez düşeyleri (kapama hariç): 2 uzun yüz + 4 kısa yüz, köşe bir kez.
            /// Etiket 2xN ⇒ N = benzersiz / 2.
            /// </summary>
            static int CountKuranglezDuseyYuz(double sD, double c,
                double y1, double y2, double y3, double y4,
                double x1, double x2, double x3)
            {
                var longY = FaceRun(sD, y1 + c, y2 - c, y3 + c, y4 - c);
                var kisa = FaceRun(sD, x1 + c, x2 + c, x3 - c);
                int total = 2 * longY.Count + 4 * kisa.Count - 8;
                if (total < 2) total = 2 * Math.Max(longY.Count, 1);
                return Math.Max(total / 2, 1);
            }

            static string Cm(double v)
            {
                return ((int)Math.Round(v, MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// Metraj etiketi. Form Adet &gt; 1 ise çarpan önek: ör. 3x2x14ø8/20 L=139 (yatay).
            /// </summary>
            static string EtiketYazi(int yuzAdet, int dia, double aralik, string tur, double toplam)
            {
                string core = string.Format(CultureInfo.InvariantCulture,
                    "2x{0}\u00F8{1}/{2:0} L={3} {4}", yuzAdet, dia, aralik, Cm(toplam), tur);
                int adet = KuranglezSettings.Adet;
                if (adet <= 1) return core;
                return adet.ToString(CultureInfo.InvariantCulture) + "x" + core;
            }

            void BoyYazi(string s, double x, double y, double rot, bool section)
            {
                if (string.IsNullOrEmpty(s)) return;
                if (section) TextS(LayerYaziDon, s, x, y, rot);
                else Text(LayerYaziDon, _donYazi, s, x, y, Txt, rot, true);
            }

            /// <summary>
            /// Yazıyı yerleştir; yazı kutusu kenarını hedefe oturt (cm).
            /// edge: bottom|top|left|right|center
            /// </summary>
            void SnapYazi(string s, double xCm, double yCm, double rot, bool section, string edge, double hedefCm)
            {
                if (string.IsNullOrEmpty(s)) return;
                double ox = section ? _secOx : 0;
                double oy = section ? _secOy : 0;
                var t = new DBText();
                t.SetDatabaseDefaults(_db);
                t.Layer = LayerYaziDon;
                t.Height = Txt;
                t.TextString = s;
                t.Rotation = rot;
                if (!_donYazi.IsNull) t.TextStyleId = _donYazi;
                Point3d p = W(ox + xCm, oy + yCm);
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = TextVerticalMode.TextVerticalMid;
                t.AlignmentPoint = p;
                t.Position = p;
                Add(t, LayerYaziDon);
                try
                {
                    t.AdjustAlignment(_db);
                    Extents3d e = t.GeometricExtents;
                    double dx = 0, dy = 0;
                    bool yatayHedef = edge == "left" || edge == "right" || edge == "centerX";
                    double hedef = section
                        ? (yatayHedef
                            ? _pick.X + (ox + hedefCm) * Du
                            : _pick.Y + (oy + hedefCm) * Du)
                        : (yatayHedef
                            ? _pick.X + hedefCm * Du
                            : _pick.Y + hedefCm * Du);
                    switch (edge)
                    {
                        case "bottom": dy = hedef - e.MinPoint.Y; break;
                        case "top": dy = hedef - e.MaxPoint.Y; break;
                        case "left": dx = hedef - e.MinPoint.X; break;
                        case "right": dx = hedef - e.MaxPoint.X; break;
                        case "centerX":
                            dx = hedef - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                            break;
                        case "center":
                            dy = hedef - (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                            break;
                    }
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }
                catch { }
            }

            void HorizFace(double x, double y0, double y1, double hook, double hook2, int dir, string layer = null)
            {
                if (y1 - y0 < 4 || hook < 1) return;
                if (string.IsNullOrEmpty(layer)) layer = LayerDonati;
                double r = BendRadius(hook, hook2);
                double k = -BendK * dir;
                if (hook2 > 0.5)
                {
                    double r2 = Math.Min(r, hook2 * 0.4);
                    Pline(layer, false,
                        (x + dir * hook, y0 + hook2, 0),
                        (x + dir * hook, y0 + r2, k),
                        (x + dir * (hook - r), y0, 0),
                        (x + dir * r, y0, k),
                        (x, y0 + r, 0),
                        (x, y1 - r, k),
                        (x + dir * r, y1, 0),
                        (x + dir * (hook - r), y1, k),
                        (x + dir * hook, y1 - r2, 0),
                        (x + dir * hook, y1 - hook2, 0));
                }
                else
                {
                    Pline(layer, false,
                        (x + dir * hook, y0, 0),
                        (x + dir * r, y0, k),
                        (x, y0 + r, 0),
                        (x, y1 - r, k),
                        (x + dir * r, y1, 0),
                        (x + dir * hook, y1, 0));
                }
            }

            void HorizFaceX(double y, double xL, double xR, double hL, double h2L, double hR, double h2R, int dir, string layer = null)
            {
                if (xR - xL < 4 || hL < 1 || hR < 1) return;
                if (string.IsNullOrEmpty(layer)) layer = LayerDonati;
                double k = BendK * dir;
                var pts = new List<(double x, double y, double bulge)>();
                if (h2L > 0.5)
                {
                    double r1 = BendRadius(hL, h2L);
                    double r2 = Math.Min(r1, h2L * 0.4);
                    pts.Add((xL + h2L, y + dir * hL, 0));
                    pts.Add((xL + r2, y + dir * hL, k));
                    pts.Add((xL, y + dir * (hL - r1), 0));
                    pts.Add((xL, y + dir * r1, k));
                    pts.Add((xL + r1, y, 0));
                }
                else
                {
                    double r1 = BendRadius(hL, 0);
                    pts.Add((xL, y + dir * hL, 0));
                    pts.Add((xL, y + dir * r1, k));
                    pts.Add((xL + r1, y, 0));
                }
                if (h2R > 0.5)
                {
                    double r1 = BendRadius(hR, h2R);
                    double r2 = Math.Min(r1, h2R * 0.4);
                    pts.Add((xR - r1, y, k));
                    pts.Add((xR, y + dir * r1, 0));
                    pts.Add((xR, y + dir * (hR - r1), k));
                    pts.Add((xR - r2, y + dir * hR, 0));
                    pts.Add((xR - h2R, y + dir * hR, 0));
                }
                else
                {
                    double r1 = BendRadius(hR, 0);
                    pts.Add((xR - r1, y, k));
                    pts.Add((xR, y + dir * r1, 0));
                    pts.Add((xR, y + dir * hR, 0));
                }
                Pline(layer, false, pts.ToArray());
            }

            static double BendRadius(double hook, double hook2)
            {
                double lim = hook;
                if (hook2 > 0.5) lim = Math.Min(lim, hook);
                double r = Math.Min(BendR, lim * 0.4);
                if (hook2 > 0.5 && hook < 2.2 * r)
                    r = hook / 2.2;
                if (r < 0.35) r = 0.35;
                return r;
            }

            void BarV(double ox, double oy, double x, double y0, double y1, double hook, double hook2, int dir, bool hookAtBottom, string layer = null)
            {
                if (y1 - y0 < 4 || hook < 1) return;
                if (string.IsNullOrEmpty(layer)) layer = LayerDonati;
                double r = BendRadius(hook, hook2);
                double k = -BendK * dir;
                if (hookAtBottom)
                {
                    PlineAbs(layer,
                        (ox + x + dir * hook, oy + y0, 0),
                        (ox + x + dir * r, oy + y0, k),
                        (ox + x, oy + y0 + r, 0),
                        (ox + x, oy + y1, 0));
                }
                else if (hook2 > 0.5)
                {
                    double r2 = Math.Min(r, hook2 * 0.4);
                    PlineAbs(layer,
                        (ox + x, oy + y0, 0),
                        (ox + x, oy + y1 - r, k),
                        (ox + x + dir * r, oy + y1, 0),
                        (ox + x + dir * (hook - r), oy + y1, k),
                        (ox + x + dir * hook, oy + y1 - r2, 0),
                        (ox + x + dir * hook, oy + y1 - hook2, 0));
                }
                else
                {
                    PlineAbs(layer,
                        (ox + x, oy + y0, 0),
                        (ox + x, oy + y1 - r, k),
                        (ox + x + dir * r, oy + y1, 0),
                        (ox + x + dir * hook, oy + y1, 0));
                }
            }

            /// <summary>
            /// Üst gönye radius merkezi (xBar+dir*r, yTop-r). Yatay kesitler bu noktadan
            /// aşağı, verilen aralıkla; son nokta temel üstü paspayının altında kalmaz.
            /// </summary>
            void YatayFromUstRadius(double xBar, int dir, double yTop, double yMin, double hook, double hook2, double step, string layer)
            {
                double r = BendRadius(hook, hook2);
                double xC = xBar + dir * r;
                foreach (double y in CollectYatayFromUstRadius(yTop, yMin, hook, hook2, step))
                    Dot(xC, y, true, layer);
            }

            static List<double> CollectYatayFromUstRadius(double yTop, double yMin, double hook, double hook2, double step)
            {
                var ys = new List<double>();
                if (step < 1) step = 1;
                double r = BendRadius(hook, hook2);
                double yC = yTop - r;
                int guard = 0;
                for (double y = yC; y >= yMin - 0.05 && guard < 80; y -= step, guard++)
                    ys.Add(y);
                return ys;
            }

            /// <summary>Kapak dişi U. Köşeler dış bükey. drawDots: kesitte radius merkezi; açılımda kapalı.</summary>
            void BarU(double xL, double xR, double yTop, double yBot, double r, bool drawDots = true)
            {
                if (r < 0.4) r = 0.4;
                double k = -BendK;
                Pline(LayerDonati, true,
                    (xL, yBot, 0),
                    (xL, yTop - r, k),
                    (xL + r, yTop, 0),
                    (xR - r, yTop, k),
                    (xR, yTop - r, 0),
                    (xR, yBot, 0));
                if (!drawDots) return;
                Dot(xL + r, yTop - r, true, LayerDonati);
                Dot(xR - r, yTop - r, true, LayerDonati);
            }

            void DotRowX(double x, List<double> ys, string layer)
            {
                foreach (double y in ys) Dot(x, y, false, layer);
            }

            void DotRowY(double y, List<double> xs, string layer)
            {
                foreach (double x in xs) Dot(x, y, false, layer);
            }

            /// <summary>
            /// Düşey/yatay kesit etiketi (X boyunca). Kolon düşey / perde kesit stili.
            /// stemSign +1 dışarı (yukarı), −1 aşağı.
            /// </summary>
            void DrawDuseyKesitEtiketX(List<double> xs, double yBar, int stemSign, string text, bool section)
            {
                if (xs == null || xs.Count < 1 || string.IsNullOrWhiteSpace(text)) return;
                var sorted = new List<double>(xs);
                sorted.Sort();
                double sg = stemSign >= 0 ? 1.0 : -1.0;
                double hy = yBar + sg * DuseyEtiketSapCm;
                double ax = sorted[0], bx = sorted[sorted.Count - 1];
                if (bx - ax < 0.5) { ax -= 1.0; bx += 1.0; }
                Pline(LayerEtiket, section,
                    (ax, yBar, 0),
                    (ax, hy, 0),
                    (bx, hy, 0),
                    (bx, yBar, 0));
                for (int i = 1; i < sorted.Count - 1; i++)
                    Line(LayerEtiket, sorted[i], yBar, sorted[i], hy, section);
                double txtOff = Txt * 0.5 + 1.0;
                double ox = section ? _secOx : 0;
                double oy = section ? _secOy : 0;
                TextAbs(LayerYaziDon, _donYazi, text,
                    ox + 0.5 * (ax + bx), oy + hy + sg * txtOff, Txt, 0, true);
            }

            /// <summary>
            /// Düşey/yatay kesit etiketi (Y boyunca). stemSign +1 sağa, −1 sola.
            /// </summary>
            void DrawDuseyKesitEtiketY(List<double> ys, double xBar, int stemSign, string text, bool section)
            {
                if (ys == null || ys.Count < 1 || string.IsNullOrWhiteSpace(text)) return;
                var sorted = new List<double>(ys);
                sorted.Sort();
                double sg = stemSign >= 0 ? 1.0 : -1.0;
                double hx = xBar + sg * DuseyEtiketSapCm;
                double ay = sorted[0], by = sorted[sorted.Count - 1];
                if (by - ay < 0.5) { ay -= 1.0; by += 1.0; }
                Pline(LayerEtiket, section,
                    (xBar, ay, 0),
                    (hx, ay, 0),
                    (hx, by, 0),
                    (xBar, by, 0));
                for (int i = 1; i < sorted.Count - 1; i++)
                    Line(LayerEtiket, xBar, sorted[i], hx, sorted[i], section);
                double txtOff = Txt * 0.5 + 1.0;
                double ox = section ? _secOx : 0;
                double oy = section ? _secOy : 0;
                TextAbs(LayerYaziDon, _donYazi, text,
                    ox + hx + sg * txtOff, oy + 0.5 * (ay + by), Txt, Math.PI * 0.5, true);
            }

            void Dot(double x, double y, bool section, string layer = null)
            {
                if (string.IsNullOrEmpty(layer)) layer = LayerDonati;
                string key = (section ? "s" : "p") + layer + ":"
                    + Math.Round(x, 1).ToString(CultureInfo.InvariantCulture) + ":"
                    + Math.Round(y, 1).ToString(CultureInfo.InvariantCulture);
                if (!_dotKeys.Add(key)) return;
                double ox = section ? _secOx : 0;
                double oy = section ? _secOy : 0;
                double rCm = DonatiNoktaCapCm * 0.25;
                double widthDu = DonatiNoktaCapCm * 0.5 * Du;
                var pl = new Polyline(2);
                pl.AddVertexAt(0, W2(ox + x - rCm, oy + y), 1, widthDu, widthDu);
                pl.AddVertexAt(1, W2(ox + x + rCm, oy + y), 1, widthDu, widthDu);
                pl.Closed = true;
                pl.ConstantWidth = widthDu;
                Add(pl, layer);
            }

            /// <summary>
            /// Kolon düşey / kesit kotu. Sembol çizim birimi (12/12/20/26, yazı 10);
            /// geometri zaten 2× olduğu için sembol tekrar büyütülmez. Tepe kesitin sağında.
            /// </summary>
            void Kot(double x, double y, double zCm)
            {
                double ax = _pick.X + (_secOx + x) * Du;
                double ay = _pick.Y + (_secOy + y) * Du;
                KotSeg(ax - KotExtSec, ay, ax, ay);
                var pA = new Point3d(ax, ay, 0);
                var pTL = new Point3d(ax - KotTriW, ay + KotTriH, 0);
                var pTM = new Point3d(ax, ay + KotTriH, 0);
                var pTR = new Point3d(ax + KotTriW, ay + KotTriH, 0);
                var pl = new Polyline(3);
                pl.AddVertexAt(0, new Point2d(pA.X, pA.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(pTL.X, pTL.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(pTM.X, pTM.Y), 0, 0, 0);
                pl.Closed = true;
                Add(pl, LayerKot);
                try
                {
                    var hatch = new Hatch();
                    hatch.SetDatabaseDefaults(_db);
                    hatch.Layer = LayerKot;
                    hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, KotWedgeAci);
                    _btr.AppendEntity(hatch);
                    _tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
                    try { hatch.EvaluateHatch(true); } catch { try { hatch.EvaluateHatch(false); } catch { } }
                }
                catch { }
                KotSeg(pTL.X, pTL.Y, pTR.X, pTR.Y);
                KotSeg(pA.X, pA.Y, pTR.X, pTR.Y);
                KotSeg(pTR.X, pTR.Y, pTM.X, pTM.Y);
                KotSeg(pTR.X, pTR.Y, pTR.X + KotExtSag, pTR.Y);

                var txt = new DBText();
                txt.SetDatabaseDefaults(_db);
                txt.Layer = LayerKotYazi;
                txt.Height = KotYaziH;
                txt.TextString = FormatKot(zCm);
                if (!_kotStyle.IsNull) txt.TextStyleId = _kotStyle;
                txt.HorizontalMode = TextHorizontalMode.TextLeft;
                txt.VerticalMode = TextVerticalMode.TextBottom;
                var tp = new Point3d(ax + KotTriW, ay + KotTriH, 0);
                txt.Position = tp;
                txt.AlignmentPoint = tp;
                Add(txt, LayerKotYazi);
                try { txt.AdjustAlignment(_db); } catch { }
                try
                {
                    Extents3d e = txt.GeometricExtents;
                    double dy = (ay + KotTriH + KotYaziPay) - e.MinPoint.Y;
                    if (Math.Abs(dy) > 1e-6)
                        txt.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
                }
                catch { }
            }

            void KotSeg(double x0, double y0, double x1, double y1)
            {
                var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
                Add(ln, LayerKot);
            }

            static string FormatKot(double zCm)
            {
                double m = Math.Round(zCm / 100.0, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(m) < 0.005) return "\u00B10.00";
                return string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00}", m);
            }

            void DimH(double yObj, double yDim, double x0, double x1)
            {
                if (Math.Abs(x1 - x0) < 1) return;
                Dim(W(x0, yObj), W(x1, yObj), W((x0 + x1) * 0.5, yDim));
            }

            void DimV(double xObj, double xDim, double y0, double y1)
            {
                if (Math.Abs(y1 - y0) < 1) return;
                Dim(W(xObj, y0), W(xObj, y1), W(xDim, (y0 + y1) * 0.5));
            }

            void DimHS(double yObj, double yDim, double x0, double x1)
            {
                if (Math.Abs(x1 - x0) < 1) return;
                Dim(SW(x0, yObj), SW(x1, yObj), SW((x0 + x1) * 0.5, yDim));
            }

            void DimVS(double xObj, double xDim, double y0, double y1)
            {
                if (Math.Abs(y1 - y0) < 1) return;
                Dim(SW(xObj, y0), SW(xObj, y1), SW(xDim, (y0 + y1) * 0.5));
            }

            void Dim(Point3d a, Point3d b, Point3d linePt)
            {
                OtoOlcuDimFactory.PlaceRotated(_tr, _btr, a, b, linePt, _dimStyle, LayerOlcu);
            }

            /// <summary>
            /// Kesit adı: A-A KESİTİ 1:25.
            /// yRefCm &lt; 0: yazı kutusu üst kenarı (kesit altı). Aksi halde alt kenar (eski üst konum).
            /// </summary>
            void KesitBaslik(double xCm, double yRefCm)
            {
                double x = _pick.X + (_secOx + xCm) * Du;
                double yRef = _pick.Y + (_secOy + yRefCm) * Du;
                bool below = yRefCm < 0;
                var t = new DBText();
                t.SetDatabaseDefaults(_db);
                t.Layer = LayerKesitIsmi;
                t.Height = 15.0;
                t.TextString = "A-A KESİTİ 1:25";
                if (!_yazi.IsNull) t.TextStyleId = _yazi;
                t.HorizontalMode = TextHorizontalMode.TextCenter;
                t.VerticalMode = below ? TextVerticalMode.TextTop : TextVerticalMode.TextBottom;
                t.AlignmentPoint = new Point3d(x, yRef, 0);
                t.Position = new Point3d(x, yRef, 0);
                Add(t, LayerKesitIsmi);
                try
                {
                    t.AdjustAlignment(_db);
                    Extents3d e = t.GeometricExtents;
                    double dx = x - (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                    double dy = below ? (yRef - e.MaxPoint.Y) : (yRef - e.MinPoint.Y);
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }
                catch { }
            }

            void PlaceKesitCifti(double xSol, double xSag, double yCut)
            {
                string srcPath = EnsureKesitSembolTemp();
                if (string.IsNullOrEmpty(srcPath)) return;
                double disariCm = (KesitSembolYaricap + KesitSembolPayCm * Du) / Du;
                try
                {
                    using (var src = new Database(false, true))
                    {
                        src.ReadDwgFile(srcPath, FileOpenMode.OpenForReadAndAllShare, true, null);
                        var srcIds = new ObjectIdCollection();
                        using (var trs = src.TransactionManager.StartTransaction())
                        {
                            var btSrc = (BlockTable)trs.GetObject(src.BlockTableId, OpenMode.ForRead);
                            var msSrc = (BlockTableRecord)trs.GetObject(btSrc[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                            foreach (ObjectId id in msSrc)
                            {
                                if (!id.IsValid || id.IsErased) continue;
                                if (trs.GetObject(id, OpenMode.ForRead) is Entity)
                                    srcIds.Add(id);
                            }
                            trs.Commit();
                        }
                        if (srcIds.Count == 0) return;
                        Point3d sol = W(xSol - disariCm, yCut);
                        Point3d sag = W(xSag + disariCm, yCut);
                        PlaceKesitSembol(src, srcIds, sol, 1, 0, 0, 1);
                        PlaceKesitSembol(src, srcIds, sag, -1, 0, 0, 1);
                    }
                }
                catch { }
            }

            /// <summary>Merdiven kesit sembolü. Kesik çizgi içeri bakar, üçgen bakışa döner, harf düz kalır.</summary>
            void PlaceKesitSembol(Database src, ObjectIdCollection srcIds, Point3d center, double inX, double inY, double bakX, double bakY)
            {
                var mapping = new IdMapping();
                _db.WblockCloneObjects(srcIds, _btr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
                var origin = new Point3d(KesitSembolOx, KesitSembolOy, 0);
                double ang = Math.Atan2(inY, inX);
                double triX = -inY, triY = inX;
                bool mirrorTri = triX * bakX + triY * bakY < 0;
                Matrix3d xf = Matrix3d.Displacement(center - origin)
                    * Matrix3d.Rotation(ang, Vector3d.ZAxis, origin);
                if (mirrorTri)
                    xf = xf * Matrix3d.Mirroring(new Plane(origin, Vector3d.YAxis));
                Line uzun = null;
                Polyline uzunPl = null;
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || !pair.Value.IsValid) continue;
                    if (!(_tr.GetObject(pair.Value, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased) continue;
                    if (ent is DBText txt)
                    {
                        txt.TextString = "A";
                        txt.Rotation = 0;
                        try { txt.AdjustAlignment(_db); } catch { }
                        try { txt.TransformBy(Matrix3d.Displacement(center - origin)); } catch { }
                        continue;
                    }
                    try { ent.TransformBy(xf); } catch { }
                    // Üçgen kenarlarına dokunma; yalnız şablonun uzun kesik çizgisi 30 cm olur.
                    if (ent is Line ln && ln.Length > 100.0 && (uzun == null || ln.Length > uzun.Length))
                        uzun = ln;
                    else if (ent is Polyline pln && pln.NumberOfVertices == 2 && pln.Length > 100.0
                        && (uzunPl == null || pln.Length > uzunPl.Length))
                        uzunPl = pln;
                }
                if (uzun != null)
                    KisaltKesitCizgisi(uzun, center, 30.0 * Du);
                else if (uzunPl != null)
                    KisaltKesitPline(uzunPl, center, 30.0 * Du);
            }

            static void KisaltKesitPline(Polyline pl, Point3d center, double hedef)
            {
                Point3d a = pl.GetPoint3dAt(0);
                Point3d b = pl.GetPoint3dAt(1);
                bool aYakin = a.DistanceTo(center) <= b.DistanceTo(center);
                int uzak = aYakin ? 1 : 0;
                Point3d yakin = aYakin ? a : b;
                Point3d far = aYakin ? b : a;
                Vector3d v = far - yakin;
                if (v.Length < 1.0) return;
                Point3d uc = yakin + v.GetNormal() * hedef;
                pl.SetPointAt(uzak, new Point2d(uc.X, uc.Y));
            }

            /// <summary>Şablon kesik çizgisi daireye yakın ucu sabit; boy 30 cm (ölçü 60 çizim birimi).</summary>
            static void KisaltKesitCizgisi(Line ln, Point3d center, double hedef)
            {
                Point3d a = ln.StartPoint;
                Point3d b = ln.EndPoint;
                bool aYakin = a.DistanceTo(center) <= b.DistanceTo(center);
                Point3d yakin = aYakin ? a : b;
                Point3d uzak = aYakin ? b : a;
                Vector3d v = uzak - yakin;
                if (v.Length < 1.0) return;
                Point3d uc = yakin + v.GetNormal() * hedef;
                if (aYakin) ln.EndPoint = uc;
                else ln.StartPoint = uc;
            }

            void Title(double x, double y, string text)
            {
                Text(LayerYazi, _yazi, text, x, y, TitleH, 0, true);
            }

            void Text(string layer, ObjectId style, string s, double x, double y, double h, double rot, bool center)
            {
                TextAbs(layer, style, s, x, y, h, rot, center);
            }

            void TextS(string layer, string s, double x, double y, double rot)
            {
                ObjectId style = layer == LayerYazi ? _yazi : _donYazi;
                TextAbs(layer, style, s, _secOx + x, _secOy + y, Txt, rot, true);
            }

            void TextAbs(string layer, ObjectId style, string s, double xCm, double yCm, double h, double rot, bool center)
            {
                var t = new DBText();
                t.SetDatabaseDefaults(_db);
                t.Layer = layer;
                t.Height = h;
                t.TextString = s ?? "";
                t.Rotation = rot;
                if (!style.IsNull) t.TextStyleId = style;
                Point3d p = W(xCm, yCm);
                if (center)
                {
                    t.HorizontalMode = TextHorizontalMode.TextCenter;
                    t.VerticalMode = TextVerticalMode.TextVerticalMid;
                    t.AlignmentPoint = p;
                }
                t.Position = p;
                Add(t, layer);
                try { t.AdjustAlignment(_db); } catch { }
            }

            void Rect(string layer, double x0, double y0, double x1, double y1)
            {
                Line(layer, x0, y0, x1, y0);
                Line(layer, x1, y0, x1, y1);
                Line(layer, x1, y1, x0, y1);
                Line(layer, x0, y1, x0, y0);
            }

            /// <summary>Kapalı dikdörtgen tarama. Sınır görünmez; koordinat gerçek cm.</summary>
            void HatchRect(string layer, double x0, double y0, double x1, double y1, string pattern, double scale)
            {
                double xmin = Math.Min(x0, x1);
                double xmax = Math.Max(x0, x1);
                double ymin = Math.Min(y0, y1);
                double ymax = Math.Max(y0, y1);
                if (xmax - xmin < 0.5 || ymax - ymin < 0.5) return;
                double ox = _inSection ? _secOx : 0;
                double oy = _inSection ? _secOy : 0;
                Polyline pl = null;
                try
                {
                    pl = new Polyline(4);
                    pl.AddVertexAt(0, W2(ox + xmin, oy + ymin), 0, 0, 0);
                    pl.AddVertexAt(1, W2(ox + xmax, oy + ymin), 0, 0, 0);
                    pl.AddVertexAt(2, W2(ox + xmax, oy + ymax), 0, 0, 0);
                    pl.AddVertexAt(3, W2(ox + xmin, oy + ymax), 0, 0, 0);
                    pl.Closed = true;
                    pl.SetDatabaseDefaults(_db);
                    pl.Layer = layer;
                    pl.Visible = false;
                    _btr.AppendEntity(pl);
                    _tr.AddNewlyCreatedDBObject(pl, true);

                    var hatch = new Hatch();
                    hatch.SetDatabaseDefaults(_db);
                    _btr.AppendEntity(hatch);
                    _tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.Normal = Vector3d.ZAxis;
                    hatch.Elevation = 0;
                    hatch.HatchStyle = HatchStyle.Normal;
                    hatch.Associative = false;
                    hatch.Layer = layer;
                    hatch.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                    hatch.PatternScale = scale;
                    hatch.PatternAngle = 0;
                    hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
                    hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
                    try { hatch.EvaluateHatch(true); }
                    catch { try { hatch.EvaluateHatch(false); } catch { } }
                }
                catch { }
                finally
                {
                    if (pl != null)
                    {
                        try
                        {
                            if (!pl.IsErased)
                            {
                                if (!pl.IsWriteEnabled) pl.UpgradeOpen();
                                pl.Erase();
                            }
                        }
                        catch { }
                    }
                }
            }

            void Line(string layer, double x0, double y0, double x1, double y1, bool section = false)
            {
                bool sec = section || _inSection;
                double ox = sec ? _secOx : 0;
                double oy = sec ? _secOy : 0;
                LineAbs(layer, ox + x0, oy + y0, ox + x1, oy + y1);
            }

            void LineAbs(string layer, double x0, double y0, double x1, double y1)
            {
                var ln = new Line(W(x0, y0), W(x1, y1));
                if (layer == LayerKesit)
                    ln.LinetypeScale = 12;
                Add(ln, layer);
            }

            void Pline(string layer, bool section, params (double x, double y, double bulge)[] v)
            {
                double ox = section ? _secOx : 0;
                double oy = section ? _secOy : 0;
                var abs = new (double x, double y, double bulge)[v.Length];
                for (int i = 0; i < v.Length; i++)
                    abs[i] = (ox + v[i].x, oy + v[i].y, v[i].bulge);
                PlineAbs(layer, abs);
            }

            void PlineAbs(string layer, params (double x, double y, double bulge)[] v)
            {
                if (v == null || v.Length < 2) return;
                var pl = new Polyline(v.Length);
                for (int i = 0; i < v.Length; i++)
                    pl.AddVertexAt(i, W2(v[i].x, v[i].y), v[i].bulge, 0, 0);
                Add(pl, layer);
            }

            Point3d W(double xCm, double yCm)
            {
                return new Point3d(_pick.X + xCm * Du, _pick.Y + yCm * Du, 0);
            }

            Point3d SW(double x, double y) => W(_secOx + x, _secOy + y);

            Point2d W2(double xCm, double yCm)
            {
                Point3d p = W(xCm, yCm);
                return new Point2d(p.X, p.Y);
            }

            void Add(Entity e, string layer)
            {
                e.Layer = layer;
                e.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                e.LineWeight = LineWeight.ByLayer;
                _btr.AppendEntity(e);
                _tr.AddNewlyCreatedDBObject(e, true);
            }

            ObjectId Style(string name)
            {
                var tst = (TextStyleTable)_tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has(name)) return tst[name];
                return _db.Textstyle;
            }

            void EnsureLayers()
            {
                ObjectId dashed = FindOrLoadDashed();
                Layer(LayerPerde, 6, LineWeight.LineWeight030, false, dashed);
                Layer(LayerTemel, 2, LineWeight.LineWeight030, false, dashed);
                Layer(LayerZemin, 43, LineWeight.LineWeight050, false, dashed);
                Layer(LayerTarama, 8, LineWeight.LineWeight020, false, dashed);
                Layer(LayerDoseme, 2, LineWeight.LineWeight030, false, dashed);
                Layer(LayerDonati, 4, LineWeight.LineWeight040, false, dashed);
                Layer(LayerDonatiIzi, 8, LineWeight.LineWeight025, false, dashed);
                Layer(LayerEtriye, 5, LineWeight.LineWeight035, false, dashed);
                Layer(LayerYaziDon, 3, LineWeight.LineWeight020, false, dashed);
                Layer(LayerEtiket, 64, LineWeight.LineWeight015, false, dashed);
                Layer(LayerYazi, 4, LineWeight.LineWeight020, false, dashed);
                Layer(LayerOlcu, 14, LineWeight.LineWeight020, false, dashed);
                Layer(LayerKot, 7, LineWeight.LineWeight020, false, dashed);
                Layer(LayerKotYazi, 7, LineWeight.LineWeight020, false, dashed);
                Layer(LayerKesit, 241, LineWeight.LineWeight020, true, dashed);
                Layer(LayerKesitIsmi, 6, LineWeight.LineWeight020, true, dashed);
            }

            ObjectId ResolveOrCreatePlanOlcuDonati25()
            {
                var dst = (DimStyleTable)_tr.GetObject(_db.DimStyleTableId, OpenMode.ForRead);
                if (dst.Has(DimStyle25))
                    return dst[DimStyle25];

                DimStyleTableRecord src = null;
                foreach (string tryName in new[] { "PLAN_OLCU_DONATI", "PLAN_OLCU", "Standard" })
                {
                    if (!dst.Has(tryName)) continue;
                    src = _tr.GetObject(dst[tryName], OpenMode.ForRead) as DimStyleTableRecord;
                    if (src != null) break;
                }

                dst.UpgradeOpen();
                var rec = new DimStyleTableRecord { Name = DimStyle25 };
                if (src != null)
                {
                    try { rec.CopyFrom(src); } catch { }
                    try { rec.Name = DimStyle25; } catch { }
                }
                try { if (!_yazi.IsNull) rec.Dimtxsty = _yazi; } catch { }
                try { rec.Dimlfac = 0.5; } catch { }
                try { rec.Dimscale = 1.0; } catch { }
                try { rec.Dimtxt = 10.0; } catch { }
                try { rec.Dimdec = 0; } catch { }
                try { rec.Dimtdec = 0; } catch { }
                try { rec.Dimtih = false; } catch { }
                try { rec.Dimtoh = false; } catch { }
                try { rec.Dimtad = 1; } catch { }
                ObjectId id = dst.Add(rec);
                _tr.AddNewlyCreatedDBObject(rec, true);
                dst.DowngradeOpen();
                return id;
            }

            void Layer(string name, short aci, LineWeight lw, bool dashed, ObjectId dashId)
            {
                var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(name)) return;
                lt.UpgradeOpen();
                var rec = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                    LineWeight = lw
                };
                if (dashed && !dashId.IsNull) rec.LinetypeObjectId = dashId;
                lt.Add(rec);
                _tr.AddNewlyCreatedDBObject(rec, true);
            }

            ObjectId FindOrLoadDashed()
            {
                var ltt = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("DASHED")) return ltt["DASHED"];
                if (ltt.Has("Dashed")) return ltt["Dashed"];
                try { _db.LoadLineTypeFile("DASHED", "acad.lin"); } catch { }
                ltt = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("DASHED")) return ltt["DASHED"];
                return ObjectId.Null;
            }
        }

        /// <summary>
        /// Sabit noktalar durur. Ardışık iki sabit arasında adım, verilen aralığı aşmayacak şekilde eşit bölünür.
        /// </summary>
        static List<double> FaceRun(double step, params double[] anchors)
        {
            var list = new List<double>();
            if (anchors == null || anchors.Length == 0) return list;
            if (step < 1) step = 1;
            var ax = new List<double>();
            foreach (double p in anchors)
            {
                bool dup = false;
                for (int i = 0; i < ax.Count; i++)
                {
                    if (Math.Abs(ax[i] - p) < 0.4) { dup = true; break; }
                }
                if (!dup) ax.Add(p);
            }
            ax.Sort();
            for (int i = 0; i < ax.Count; i++)
            {
                list.Add(ax[i]);
                if (i + 1 >= ax.Count) continue;
                double span = ax[i + 1] - ax[i];
                if (span < 1) continue;
                int n = (int)Math.Ceiling(span / step - 1e-6);
                if (n < 1) n = 1;
                for (int k = 1; k < n; k++)
                    list.Add(ax[i] + span * k / n);
            }
            return list;
        }

        static List<double> Stations(double a, double b, double pas, double step)
        {
            var list = new List<double>();
            if (step < 1) step = 1;
            double start = a + pas;
            double end = b - pas;
            if (end < start - 0.01)
            {
                list.Add((a + b) * 0.5);
                return list;
            }
            int guard = 0;
            for (double x = start; x <= end + 0.05 && guard < 400; x += step, guard++)
                list.Add(Math.Round(x, 3));
            if (list.Count == 0) list.Add((a + b) * 0.5);
            return list;
        }

        static string EnsureKesitSembolTemp()
        {
            Assembly asm = typeof(KuranglezRunner).Assembly;
            using (Stream stream = asm.GetManifestResourceStream(KesitSembolResource))
            {
                if (stream == null) return null;
                string path = Path.Combine(Path.GetTempPath(),
                    "ST4Yardimci_kesit_sembol_" + asm.ManifestModule.ModuleVersionId.ToString("N") + ".dwg");
                if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
                {
                    string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
                        stream.CopyTo(fs);
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(tmp, path);
                    }
                    catch
                    {
                        try { File.Delete(tmp); } catch { }
                        if (!File.Exists(path)) return null;
                    }
                }
                return path;
            }
        }
    }
}
