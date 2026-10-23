using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Temel üstü hatıl detayı — kalıp kesiti (1:25 = gerçek cm ×2).
    /// Temel / ZEMIN / TEMEL HATILI / DOSEME yatayları; sol-sağ finiş KESIT SINIRI.
    /// Kotlar: KOLONDUSEY2 klasik kesit kotu.
    /// Hatıl içi (temel üstü) yatay donatı: 2xNøφ/s etiketi.
    /// </summary>
    internal static class TemelHatiliRunner
    {
        private const string LayerTemel = "TEMEL (BEYKENT)";
        private const string LayerHatil = "TEMEL HATILI (BEYKENT)";
        private const string LayerDoseme = "DOSEME (BEYKENT)";
        private const string LayerZemin = "ZEMIN (BEYKENT)";
        private const string LayerKesitSiniri = "KESIT SINIRI (BEYKENT)";
        private const string LayerOlcu = "OLCU (BEYKENT)";
        private const string LayerKotCizgi = "KOT CIZGISI (BEYKENT)";
        private const string LayerKotYazi = "KOT YAZI (BEYKENT)";
        private const string LayerDonati = "DONATI (BEYKENT)";
        private const string LayerDonatiIzi = "DONATI IZI (BEYKENT)";
        private const string LayerDonatiYazi = "DONATI YAZISI (BEYKENT)";
        private const string LayerEtiketCizgisi = "ETIKET CIZGISI (BEYKENT)";
        private const string LayerBlokaj = "BLOKAJ (BEYKENT)";
        private const string LayerTarama = "TARAMA (BEYKENT)";
        private const string LayerTemelHatiliIsmi = "TEMEL HATILI ISMI (BEYKENT)";
        private const string LayerDosemeIsmi = "DOSEME ISMI (BEYKENT)";
        private const string LayerKesitIsmi = "KESIT ISMI (BEYKENT)";
        private const string LayerIcAntet = "IC ANTET (BEYKENT)";
        private const string LayerAntetBaslik1 = "ANTET BASLIK 1 (BEYKENT)";
        private const string LayerKolon = "KOLON (BEYKENT)";
        private const string TextStyleYazi = "YAZI (BEYKENT)";

        private const string HatchArConc = "AR-CONC";
        private const double BlokajHatchScale = 0.1;
        private const double GrobetonHatchScale = 0.05;
        /// <summary>BLOKAJ katmanı şeffaflık (0=opak … 90=en şeffaf) — Plan ID ile aynı.</summary>
        private const int LayerBlokajTransparencyPercent = 90;
        /// <summary>Plandan çiz: ardışık tip sol kenarı = önceki sağ + bu (cm, gerçek).</summary>
        private const double PlanTipArasiCm = 100.0;
        /// <summary>Detay alt yazısı punto (çizim birimi).</summary>
        private const double DetailEtiketPunto = 20.0;
        /// <summary>Grobeton altı → etiket üst kenarı (cm, gerçek).</summary>
        private const double DetailEtiketAltBoslukCm = 8.0;
        /// <summary>Detay sağ kenarı → boy tablosu sol kenarı (cm, gerçek). Negatif = sola taşma.</summary>
        private const double AdetTabloDetayArasiCm = 15.0;
        /// <summary>Yatay boy etiketine eklenen bindirme (cm, gerçek). Düşey adet hesabına girmez.</summary>
        private const double YatayBindirmeCm = 60.0;
        /// <summary>Adet/boy yazı yüksekliği (cm, çizim).</summary>
        private const double AdetBoyPunto = 10.0;
        /// <summary>"N ADET TH-xx" başlık yüksekliği (cm, çizim).</summary>
        private const double AdetBaslikPunto = 12.0;
        /// <summary>Adet/boy satır yüksekliği — tablo satırı (cm, çizim).</summary>
        private const double AdetBoySatirAralikCm = 20.0;
        /// <summary>Tablo sol boş sütun genişliği (cm, çizim).</summary>
        private const double AdetTabloSolBosCm = 30.0;
        /// <summary>Sağ hücrede yazı sol kenarı ↔ ayırıcı (cm, çizim).</summary>
        private const double AdetTabloYaziSolPadCm = 10.0;
        /// <summary>Yazı sağ kenarı ↔ tablo sağ çizgisi (cm, çizim).</summary>
        private const double AdetTabloYaziSagPadCm = 10.0;
        /// <summary>Başlık altı ↔ tablo üstü (cm, çizim).</summary>
        private const double AdetBaslikTabloBoslukCm = 5.0;

        private const double GrobetonCm = 10.0;
        private const double YanCikintiCm = 80.0;
        /// <summary>Temel içi alt paspayı (cm) — sabit.</summary>
        private const double TemelAltPaspayiCm = 5.0;
        /// <summary>Temel üst/alt donatı izi paspayısı (cm).</summary>
        private const double TemelDonatiIziPasCm = 5.0;
        private const double Scale = 2.0;
        /// <summary>Donatı köşe radyusu (cm, gerçek).</summary>
        private const double DonatiRadiusCm = 1.5;
        /// <summary>Üst gönye merkezindeki yatay donatı kesit çapı (cm, gerçek).</summary>
        private const double YatayKesitCapCm = 1.5;
        /// <summary>Kutu çemberlerinin sola/sağa kopya adımı — temel (cm, gerçek).</summary>
        private const double YatayKesitKopyaAralikCm = 20.0;
        /// <summary>Subasman döşeme içi kopya adımı (cm, gerçek).</summary>
        private const double SubasmanKesitKopyaAralikCm = 15.0;
        /// <summary>Subasman kopya çember çapı (cm, gerçek).</summary>
        private const double SubasmanKesitCapCm = 1.0;
        /// <summary>Yatay donatı çemberi global width (kutu / temel).</summary>
        private const double YatayKesitGlobalWidth = 6.0;
        /// <summary>Subasman kopya çember global width.</summary>
        private const double SubasmanKesitGlobalWidth = 4.0;
        /// <summary>Subasman kopya: iz çizgisinin üstü / formülde (2×pas − bu) (cm, gerçek).</summary>
        private const double SubasmanKesitIziUstuCm = 1.0;
        private const double DonatiBulge90 = 0.41421356237; // tan(π/8)
        private const double DonatiYaziH = 10.0;
        /// <summary>Sol yatay donatı → etiket çizgisi yatay kol (cm, gerçek).</summary>
        private const double HatilYatayEtiketCizgiCm = 10.0;
        /// <summary>Etiket çizgisi ↔ yazı kutusu (çizgiye bakan kenar; cm, gerçek).</summary>
        private const double HatilYatayEtiketYaziBoslukCm = 2.0;
        /// <summary>Kesit sağı → kot apex (cm, gerçek).</summary>
        private const double KesitKotBoslukCm = 15.0;
        /// <summary>İki düşey açılım en yakın noktaları arası (cm, gerçek).</summary>
        private const double DuseyAcilimArasiCm = 5.0;
        /// <summary>Kot sağ ucu → ilk açılım gövdesi payı (cm, gerçek).</summary>
        private const double KotSonrasiAcilimPayCm = 35.0;
        /// <summary>Hatıl sağ yüz → dikey detay ölçü zinciri (cm, gerçek).</summary>
        private const double HatilOlcuDikeyOfsetCm = 20.0;
        /// <summary>Detay ölçü → toplam ölçü (temel, döşemesiz) ek sağa (cm, gerçek).</summary>
        private const double HatilOlcuToplamEkCm = 10.0;
        /// <summary>Döşeme üstü → yatay hatıl genişlik ölçüsü (cm, gerçek).</summary>
        private const double HatilOlcuUstOfsetCm = 10.0;
        private const string DimStylePlanOlcuDonati25 = "PLAN_OLCU_DONATI 25";

        private const double KesitKotTriHeightCm = 12.0;
        private const double KesitKotTriHalfWidthCm = 12.0;
        private const double KesitKotExtTowardSectionCm = 20.0;
        private const double KesitKotExtTopRightCm = 26.0;
        private const double KesitKotTextAboveExtensionCm = 0.0;
        private const double KesitKotTextHeightCm = 10.0;
        private const double KesitKotDatumGapCm = 22.0;
        private const short KesitKotWedgeSolidAci = 250;
        private const double DimTextH = 8.0;

        private static readonly Regex RxTh =
            new Regex(@"TH(?:-(\d+))?\s*\(\s*(\d+)\s*/\s*(\d+)\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        /// <summary>Radye etiket: RD-01 d=40cm</summary>
        private static readonly Regex RxRadyeD =
            new Regex(@"RD\s*-?\s*\d+\s+d\s*=\s*(\d+(?:[.,]\d+)?)\s*cm",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        /// <summary>Kot yazısı (m): ±0.00 / -0.20 / +0.40</summary>
        private static readonly Regex RxKotElevM =
            new Regex(@"^[+\-\u00B1]?\d+[.,]\d{1,3}$",
                RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static void RunFromPlan()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                TemelHatiliForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            try
            {
                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = "\nTemel alanını komple seçin (TEMEL HATILI ISMI yazıları dahil): ",
                    MessageForRemoval = "\nÇıkarılacak: ",
                    AllowDuplicates = false
                };
                PromptSelectionResult psr = ed.GetSelection(peo);
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nTEMELHATILIPLAN: seçim iptal.");
                    return;
                }

                ObjectId[] selIds = psr.Value.GetObjectIds();
                if (!TryCollectHatiliTypes(doc, selIds, out List<PlanHatilType> types, out string scanNote))
                {
                    ed.WriteMessage("\nTEMELHATILIPLAN: {0}", scanNote ?? "etiket okunamadı.");
                    return;
                }

                if (types == null || types.Count == 0)
                {
                    ed.WriteMessage("\nTEMELHATILIPLAN: seçimde TEMEL HATILI ISMI (BEYKENT) yazısı yok.");
                    return;
                }

                double temelH = TemelHatiliSettings.TemelYuksekligiCm;
                if (TryReadRadyeEtiketFromSelection(doc, selIds, out double rdH, out double rdUstKotCm, out string rdNote))
                {
                    temelH = rdH;
                    TemelHatiliSettings.TemelYuksekligiCm = rdH;
                    TemelHatiliSettings.TemelUstKotuCm = rdUstKotCm;
                    ed.WriteMessage("\nTEMELHATILIPLAN: radye etiket — {0}", rdNote);
                }
                else if (!string.IsNullOrEmpty(rdNote))
                    ed.WriteMessage("\nTEMELHATILIPLAN: radye etiket yok ({0}); form değerleri kullanılıyor.", rdNote);

                ed.WriteMessage("\nTEMELHATILIPLAN: {0} tip — {1}",
                    types.Count,
                    string.Join("; ", types.Select(t =>
                        string.Format(CultureInfo.InvariantCulture, "{0} ADET TH-{1:D2}", t.LabelCount, t.No))));

                var popt = new PromptPointOptions("\n1. tip detay — grobeton sol-alt köşesi: ")
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(popt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nTEMELHATILIPLAN: iptal.");
                    return;
                }

                int drawn = 0;
                var notes = new List<string>();
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var drawer = new Drawer(tr, doc.Database);
                    double x = ppr.Value.X;
                    double y = ppr.Value.Y;
                    for (int i = 0; i < types.Count; i++)
                    {
                        PlanHatilType tip = types[i];
                        if (!TryValidateDims(temelH, tip.W, tip.H, out string why))
                        {
                            ed.WriteMessage("\n  Atlandı TH-{0:D2} ({1}/{2}): {3}", tip.No, tip.W, tip.H, why);
                            continue;
                        }

                        string title = string.Format(CultureInfo.InvariantCulture,
                            "TH-{0} ({1}/{2}) DETAYI 1:25",
                            tip.No.ToString("D2", CultureInfo.InvariantCulture), tip.W, tip.H);

                        double toplamL = SumLengthsCm(tip.LengthsCm);
                        int duseyAdet = ComputeDuseyAdet(toplamL, TemelHatiliSettings.DikeyAralikCm);

                        drawer.DrawKalip(
                            new Point3d(x, y, 0),
                            out double rightEdgeX,
                            out string yatayEtiketOnEk,
                            out double detailTopY,
                            temelHOverride: temelH,
                            hatilWOverride: tip.W,
                            hatilHOverride: tip.H,
                            detailTitle: title,
                            duseyAdetOverride: duseyAdet);

                        double tableRightX = drawer.DrawAdetVeBoylar(
                            rightEdgeX, y, detailTopY, tip.No, tip.LengthsCm, yatayEtiketOnEk);

                        notes.Add(title);
                        drawn++;
                        x = Math.Max(rightEdgeX, tableRightX) + SGlobal(PlanTipArasiCm);
                    }
                    tr.Commit();
                }

                if (drawn == 0)
                    ed.WriteMessage("\nTEMELHATILIPLAN: geçerli tip çizilemedi.");
                else
                    ed.WriteMessage("\nTEMELHATILIPLAN: {0} tip — {1}", drawn, string.Join("; ", notes));
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nTEMELHATILIPLAN hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                TemelHatiliForm.Shutdown();
            }
        }

        /// <summary>
        /// Plan ID (SUREKLITEMELACILIMLARI) girişi: TEMELHATILIPLAN ile aynı detay + adet/boy tablosu, tipler soldan sağa.
        /// Paspayı/döşeme TemelHatiliSettings'ten; tip başına temel üst kotu ve (verilmişse, 0 değilse) STA4CAD donatısı:
        /// dikey çap/aralık, yatay çap ve yüz başına yatay adet. Ad: plan etiketi (ör. "TH (20/125)"), yoksa TH-nn.
        /// Dönüş: en sağ x (tip yoksa origin.X), enUst: detay üstü.
        /// </summary>
        internal static double DrawTipler(Transaction tr, Database db, Point3d origin,
            IList<(int No, double W, double H, double TemelH, double TemelUstKotCm, List<double> LengthsCm,
                double DikeyCapMm, double DikeyAralikCm, double YatayCapMm, int YatayAdet, string Ad)> tipler,
            List<string> notlar, out double enUst)
        {
            double x = origin.X, enSag = origin.X;
            enUst = origin.Y;
            if (tipler == null || tipler.Count == 0) return enSag;
            var drawer = new Drawer(tr, db);
            foreach (var tip in tipler)
            {
                if (!TryValidateDims(tip.TemelH, tip.W, tip.H, out string why))
                {
                    notlar?.Add(string.Format(CultureInfo.InvariantCulture, "TH-{0:D2} ({1}/{2}) atlandı: {3}", tip.No, tip.W, tip.H, why));
                    continue;
                }
                TemelHatiliSettings.TemelUstKotuCm = tip.TemelUstKotCm;
                if (tip.DikeyCapMm > 0) TemelHatiliSettings.DikeyCapMm = tip.DikeyCapMm;
                if (tip.DikeyAralikCm > 0) TemelHatiliSettings.DikeyAralikCm = tip.DikeyAralikCm;
                if (tip.YatayCapMm > 0) TemelHatiliSettings.YatayCapMm = tip.YatayCapMm;
                string title = !string.IsNullOrWhiteSpace(tip.Ad)
                    ? tip.Ad.Trim() + " DETAYI 1:25"
                    : string.Format(CultureInfo.InvariantCulture, "TH-{0} ({1}/{2}) DETAYI 1:25",
                    tip.No.ToString("D2", CultureInfo.InvariantCulture), tip.W, tip.H);
                int duseyAdet = ComputeDuseyAdet(SumLengthsCm(tip.LengthsCm), TemelHatiliSettings.DikeyAralikCm);
                drawer.DrawKalip(new Point3d(x, origin.Y, 0), out double rightEdgeX, out string yatayEtiketOnEk, out double detailTopY,
                    temelHOverride: tip.TemelH, hatilWOverride: tip.W, hatilHOverride: tip.H,
                    detailTitle: title, duseyAdetOverride: duseyAdet,
                    yatayAdetOverride: tip.YatayAdet > 0 ? tip.YatayAdet : (int?)null);
                double tableRightX = drawer.DrawAdetVeBoylar(rightEdgeX, origin.Y, detailTopY, tip.No, tip.LengthsCm, yatayEtiketOnEk, tip.Ad);
                notlar?.Add(title);
                enUst = Math.Max(enUst, detailTopY);
                enSag = Math.Max(rightEdgeX, tableRightX);
                x = enSag + SGlobal(PlanTipArasiCm);
            }
            return enSag;
        }

        private static double SGlobal(double realCm) => realCm * Scale;

        private static double SumLengthsCm(IList<double> lengthsCm)
        {
            if (lengthsCm == null || lengthsCm.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < lengthsCm.Count; i++)
                if (lengthsCm[i] >= 1.0) sum += lengthsCm[i];
            return sum;
        }

        /// <summary>Düşey donatı adedi = ⌈toplam L / düşey aralık⌉ (üst tamsayı).</summary>
        private static int ComputeDuseyAdet(double toplamLCm, double aralikCm)
        {
            if (aralikCm < 0.5) aralikCm = 0.5;
            if (toplamLCm < 1.0) return 1;
            int n = (int)Math.Ceiling(toplamLCm / aralikCm - 1e-9);
            return n < 1 ? 1 : n;
        }

        /// <summary>Tablo L etiketi: 5 cm katına yukarı (552 → 555).</summary>
        private static double CeilTo5Cm(double cm)
        {
            if (cm < 1.0) return 0;
            return Math.Ceiling(cm / 5.0 - 1e-9) * 5.0;
        }

        private sealed class PlanHatilType
        {
            public int No;
            public int W;
            public int H;
            /// <summary>Seçimde bu (W/H) için TH etiket adedi.</summary>
            public int LabelCount;
            /// <summary>Plandan ölçülen boylar (cm); eksik ölçüm 0.</summary>
            public List<double> LengthsCm = new List<double>();
        }

        /// <summary>
        /// Seçim / bbox içinden RD-xx d=NNcm (DOSEME ISMI) + yakın KOT YAZI üst/alt (m) okur.
        /// temelHCm = d; ustKotCm = üst kot (m×100).
        /// </summary>
        private static bool TryReadRadyeEtiketFromSelection(
            Document doc, ObjectId[] selIds,
            out double temelHCm, out double ustKotCm, out string note)
        {
            temelHCm = 0;
            ustKotCm = 0;
            note = null;
            if (doc == null || selIds == null || selIds.Length == 0)
            {
                note = "seçim boş";
                return false;
            }

            var rdList = new List<(Point3d Pos, double ThickCm, string Raw)>();
            var kotList = new List<(Point3d Pos, double ElevM)>();

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                bool haveBox = false;
                double minX = 0, minY = 0, maxX = 0, maxY = 0;
                var selected = new HashSet<ObjectId>(selIds);

                void Expand(Extents3d e)
                {
                    if (!haveBox)
                    {
                        minX = e.MinPoint.X; minY = e.MinPoint.Y;
                        maxX = e.MaxPoint.X; maxY = e.MaxPoint.Y;
                        haveBox = true;
                    }
                    else
                    {
                        if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                        if (e.MinPoint.Y < minY) minY = e.MinPoint.Y;
                        if (e.MaxPoint.X > maxX) maxX = e.MaxPoint.X;
                        if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                    }
                }

                void ConsiderText(Entity ent)
                {
                    if (ent == null || !(ent is DBText || ent is MText)) return;
                    if (!TryGetTextPose(ent, out Point3d pt, out _)) return;
                    string text = GetEntityText(ent);
                    if (string.IsNullOrWhiteSpace(text)) return;
                    string flat = FlattenMText(text);

                    if (LayerEquals(ent.Layer, LayerDosemeIsmi))
                    {
                        Match m = RxRadyeD.Match(flat);
                        if (m.Success
                            && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                            && d >= 10.0)
                            rdList.Add((pt, d, flat.Trim()));
                    }
                    else if (LayerEquals(ent.Layer, LayerKotYazi))
                    {
                        if (TryParseKotElevMeters(flat, out double elevM))
                            kotList.Add((pt, elevM));
                    }
                }

                for (int i = 0; i < selIds.Length; i++)
                {
                    if (selIds[i].IsNull) continue;
                    try
                    {
                        var ent = tr.GetObject(selIds[i], OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        try { Expand(ent.GeometricExtents); } catch { }
                        ConsiderText(ent);
                    }
                    catch { }
                }

                if (haveBox)
                {
                    double pad = 80.0;
                    minX -= pad; minY -= pad; maxX += pad; maxY += pad;
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (selected.Contains(id)) continue;
                        Entity ent;
                        try { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                        catch { continue; }
                        if (ent == null || !(ent is DBText || ent is MText)) continue;
                        if (!LayerEquals(ent.Layer, LayerDosemeIsmi) && !LayerEquals(ent.Layer, LayerKotYazi))
                            continue;
                        if (!TryGetTextPose(ent, out Point3d pt, out _)) continue;
                        if (pt.X < minX || pt.X > maxX || pt.Y < minY || pt.Y > maxY) continue;
                        ConsiderText(ent);
                    }
                }

                tr.Commit();
            }

            if (rdList.Count == 0)
            {
                note = "RD d=…cm yok";
                return false;
            }

            // En kalın / ilk RD
            rdList = rdList.OrderByDescending(r => r.ThickCm).ToList();
            var rd = rdList[0];
            temelHCm = Math.Round(rd.ThickCm, MidpointRounding.AwayFromZero);

            // RD yakınındaki kotlar (çizim birimi ~cm×ölçek)
            const double nearR = 250.0;
            var nearKots = kotList
                .Select(k => (k.Pos, k.ElevM, Dist: k.Pos.DistanceTo(rd.Pos)))
                .Where(k => k.Dist <= nearR)
                .OrderBy(k => k.Dist)
                .ToList();

            if (nearKots.Count < 2)
            {
                // Tüm seçim kotlarından dene
                nearKots = kotList
                    .Select(k => (k.Pos, k.ElevM, Dist: k.Pos.DistanceTo(rd.Pos)))
                    .OrderBy(k => k.Dist)
                    .Take(8)
                    .ToList();
            }

            if (nearKots.Count >= 2)
            {
                double elevMax = nearKots.Max(k => k.ElevM);
                double elevMin = nearKots.Min(k => k.ElevM);
                if (elevMax - elevMin < 0.01)
                {
                    // Tek değer — üst kot olarak kullan; kalınlık d='den
                    ustKotCm = Math.Round(elevMax * 100.0, MidpointRounding.AwayFromZero);
                    note = string.Format(CultureInfo.InvariantCulture,
                        "d={0:0}cm üst={1:+0.00;-0.00}m",
                        temelHCm, elevMax);
                    return true;
                }

                ustKotCm = Math.Round(elevMax * 100.0, MidpointRounding.AwayFromZero);
                double hFromKot = Math.Round((elevMax - elevMin) * 100.0, MidpointRounding.AwayFromZero);
                if (hFromKot >= 10.0 && Math.Abs(hFromKot - temelHCm) > 2.5)
                {
                    note = string.Format(CultureInfo.InvariantCulture,
                        "d={0:0}cm üst={1:+0.00;-0.00}m (kot kalınlığı {2:0}cm)",
                        temelHCm, elevMax, hFromKot);
                }
                else
                {
                    note = string.Format(CultureInfo.InvariantCulture,
                        "d={0:0}cm üst={1:+0.00;-0.00}m alt={2:+0.00;-0.00}m",
                        temelHCm, elevMax, elevMin);
                }
                return true;
            }

            // Kot yok — yalnız d=
            ustKotCm = TemelHatiliSettings.TemelUstKotuCm;
            note = string.Format(CultureInfo.InvariantCulture,
                "d={0:0}cm (kot okunamadı; üst kot form={1:0})",
                temelHCm, ustKotCm);
            return true;
        }

        private static string FlattenMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Basit MText kod temizliği
            string t = s.Replace("\\P", " ").Replace("\\p", " ");
            t = Regex.Replace(t, @"\\[A-Za-z][^;]*;", "");
            t = Regex.Replace(t, "[{}]", "");
            return t.Trim();
        }

        private static bool TryParseKotElevMeters(string raw, out double elevM)
        {
            elevM = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = FlattenMText(raw).Trim().Replace(',', '.');
            if (t.Length > 0 && t[0] == '\u00B1')
                t = "+" + t.Substring(1);
            else
                t = t.Replace("\u00B1", "+");
            if (!RxKotElevM.IsMatch(t))
                return false;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out elevM))
                return false;
            if (elevM < -50.0 || elevM > 50.0) return false;
            return true;
        }

        /// <summary>
        /// TEMEL HATILI ISMI yazıları + TEMEL HATILI poligonlarından boy ölçümü.
        /// </summary>
        private static bool TryCollectHatiliTypes(
            Document doc, ObjectId[] selIds,
            out List<PlanHatilType> types, out string note)
        {
            types = new List<PlanHatilType>();
            note = null;
            if (doc == null || selIds == null || selIds.Length == 0)
            {
                note = "seçim boş.";
                return false;
            }

            var instances = new List<(int No, int W, int H, Point3d Pos, double Rot)>();
            var parsedTextIds = new HashSet<ObjectId>();
            var hatilSegs = new List<Seg2d>();
            var kolonSegs = new List<Seg2d>();

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                bool haveBox = false;
                double minX = 0, minY = 0, maxX = 0, maxY = 0;
                var selected = new HashSet<ObjectId>(selIds);

                void Expand(Extents3d e)
                {
                    if (!haveBox)
                    {
                        minX = e.MinPoint.X; minY = e.MinPoint.Y;
                        maxX = e.MaxPoint.X; maxY = e.MaxPoint.Y;
                        haveBox = true;
                    }
                    else
                    {
                        if (e.MinPoint.X < minX) minX = e.MinPoint.X;
                        if (e.MinPoint.Y < minY) minY = e.MinPoint.Y;
                        if (e.MaxPoint.X > maxX) maxX = e.MaxPoint.X;
                        if (e.MaxPoint.Y > maxY) maxY = e.MaxPoint.Y;
                    }
                }

                for (int i = 0; i < selIds.Length; i++)
                {
                    if (selIds[i].IsNull) continue;
                    try
                    {
                        var ent = tr.GetObject(selIds[i], OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        try { Expand(ent.GeometricExtents); } catch { }
                        TryAddHatiliText(ent, parsedTextIds, instances);
                        TryAddCurveSegs(ent, LayerHatil, hatilSegs);
                        TryAddCurveSegs(ent, LayerKolon, kolonSegs);
                    }
                    catch { }
                }

                if (haveBox)
                {
                    double pad = 50.0;
                    minX -= pad; minY -= pad; maxX += pad; maxY += pad;
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        Entity ent;
                        try { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                        catch { continue; }
                        if (ent == null) continue;

                        if (ent is DBText || ent is MText)
                        {
                            if (selected.Contains(id)) continue;
                            if (!LayerEquals(ent.Layer, LayerTemelHatiliIsmi)) continue;
                            if (!TryGetTextPose(ent, out Point3d pt, out _)) continue;
                            if (pt.X < minX || pt.X > maxX || pt.Y < minY || pt.Y > maxY) continue;
                            TryAddHatiliText(ent, parsedTextIds, instances);
                        }
                        else if (ent is Polyline || ent is Line)
                        {
                            if (selected.Contains(id)) continue;
                            if (!TryEntityTouchesBox(ent, minX, minY, maxX, maxY)) continue;
                            TryAddCurveSegs(ent, LayerHatil, hatilSegs);
                            TryAddCurveSegs(ent, LayerKolon, kolonSegs);
                        }
                    }
                }

                tr.Commit();
            }

            if (instances.Count == 0)
            {
                note = "TEMEL HATILI ISMI yazısı yok.";
                return false;
            }

            // (W,H) → tip; no birleştir
            var byWh = new Dictionary<(int W, int H), PlanHatilType>();
            for (int i = 0; i < instances.Count; i++)
            {
                var inst = instances[i];
                var key = (inst.W, inst.H);
                if (!byWh.TryGetValue(key, out PlanHatilType tip))
                {
                    tip = new PlanHatilType { No = inst.No, W = inst.W, H = inst.H };
                    byWh[key] = tip;
                }
                else if (inst.No > 0 && (tip.No <= 0 || inst.No < tip.No))
                    tip.No = inst.No;

                if (TryMeasureHatilLengthAlongText(
                        inst.Pos, inst.Rot, inst.W, hatilSegs, kolonSegs, out double lenCm))
                    tip.LengthsCm.Add(lenCm);
                else
                    tip.LengthsCm.Add(0);
            }

            int nextAuto = 1;
            foreach (var tip in byWh.Values)
                if (tip.No > nextAuto) nextAuto = tip.No;
            nextAuto++;
            foreach (var tip in byWh.Values)
            {
                if (tip.No <= 0)
                {
                    tip.No = nextAuto;
                    nextAuto++;
                }
                tip.LabelCount = instances.Count(x => x.W == tip.W && x.H == tip.H);
                // Boyları büyükten küçüğe; 0 (ölçülemeyen) sonda
                tip.LengthsCm = tip.LengthsCm
                    .OrderByDescending(L => L > 0.5 ? L : -1.0)
                    .ToList();
                while (tip.LengthsCm.Count < tip.LabelCount)
                    tip.LengthsCm.Add(0);
                if (tip.LengthsCm.Count > tip.LabelCount)
                    tip.LengthsCm = tip.LengthsCm.Take(tip.LabelCount).ToList();
            }

            types = byWh.Values.OrderBy(t => t.No).ThenBy(t => t.W).ThenBy(t => t.H).ToList();
            return true;
        }

        private struct Seg2d
        {
            public Point2d A;
            public Point2d B;
            public Seg2d(Point2d a, Point2d b) { A = a; B = b; }
            public double Length => A.GetDistanceTo(B);
            public Vector2d Dir
            {
                get
                {
                    Vector2d v = B - A;
                    return v.Length > 1e-9 ? v.GetNormal() : new Vector2d(1, 0);
                }
            }
        }

        private static void TryAddHatiliText(
            Entity ent, HashSet<ObjectId> parsedIds, List<(int No, int W, int H, Point3d Pos, double Rot)> instances)
        {
            if (ent == null || parsedIds == null || instances == null) return;
            if (!(ent is DBText || ent is MText)) return;
            if (!LayerEquals(ent.Layer, LayerTemelHatiliIsmi)) return;
            if (!parsedIds.Add(ent.ObjectId)) return;
            if (!TryGetTextPose(ent, out Point3d pos, out double rot)) return;
            string text = GetEntityText(ent);
            if (string.IsNullOrWhiteSpace(text)) return;
            Match m = RxTh.Match(text);
            if (!m.Success) return;
            int no = 0;
            if (m.Groups[1].Success)
                int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out no);
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) return;
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return;
            if (w < 10 || h < 20) return;
            instances.Add((no, w, h, pos, rot));
        }

        private static void TryAddCurveSegs(Entity ent, string layer, List<Seg2d> segs)
        {
            if (ent == null || segs == null || string.IsNullOrEmpty(layer)) return;
            if (!LayerEquals(ent.Layer, layer)) return;
            if (ent is Line ln)
            {
                segs.Add(new Seg2d(
                    new Point2d(ln.StartPoint.X, ln.StartPoint.Y),
                    new Point2d(ln.EndPoint.X, ln.EndPoint.Y)));
                return;
            }
            if (ent is Polyline pl && pl.NumberOfVertices >= 2)
            {
                int n = pl.NumberOfVertices;
                for (int i = 0; i < n - 1; i++)
                    segs.Add(new Seg2d(pl.GetPoint2dAt(i), pl.GetPoint2dAt(i + 1)));
                if (pl.Closed)
                    segs.Add(new Seg2d(pl.GetPoint2dAt(n - 1), pl.GetPoint2dAt(0)));
            }
        }

        private static bool TryEntityTouchesBox(Entity ent, double minX, double minY, double maxX, double maxY)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                if (e.MaxPoint.X < minX || e.MinPoint.X > maxX) return false;
                if (e.MaxPoint.Y < minY || e.MinPoint.Y > maxY) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// L = yazı orta noktası + yazı açısı boyunca sanal merkez çizgi;
        /// iki yan TEMEL HATILI çizgisi (kalınlık aralığı) arasından geçer;
        /// uçlar kolon veya (yan olmayan) temel hatılı çizgisinde biter.
        /// </summary>
        private static bool TryMeasureHatilLengthAlongText(
            Point3d textPos, double textRotRad, int widthCm,
            List<Seg2d> hatilSegs, List<Seg2d> kolonSegs, out double lenCm)
        {
            lenCm = 0;
            if (widthCm < 1 || hatilSegs == null || hatilSegs.Count == 0) return false;

            Point2d p = new Point2d(textPos.X, textPos.Y);
            Vector2d u = new Vector2d(Math.Cos(textRotRad), Math.Sin(textRotRad));
            if (u.Length < 1e-9) u = new Vector2d(1, 0);
            else u = u.GetNormal();

            Seg2d sideA = default, sideB = default;
            bool haveSides = TryFindSideGuides(p, u, widthCm, hatilSegs, out sideA, out sideB, out Vector2d uRefined);
            if (haveSides)
                u = uRefined;

            // Engeller: kolon + (yan kılavuz olmayan) hatıl uç/kesişim çizgileri
            var obstacles = new List<Seg2d>();
            if (kolonSegs != null)
                obstacles.AddRange(kolonSegs);
            for (int i = 0; i < hatilSegs.Count; i++)
            {
                Seg2d s = hatilSegs[i];
                if (haveSides && (SegSame(s, sideA) || SegSame(s, sideB))) continue;
                double align = Math.Abs(s.Dir.DotProduct(u));
                if (align > Math.Cos(25.0 * Math.PI / 180.0) && s.Length >= widthCm * 0.8)
                {
                    double sd = SignedDistPointToLine(p, s.A, s.B);
                    if (Math.Abs(Math.Abs(sd) - widthCm * 0.5) < widthCm * 0.35)
                        continue;
                }
                obstacles.Add(s);
            }

            const double maxRay = 8000.0;
            const double eps = 0.5;
            if (!TryRayHit(p, u, obstacles, maxRay, eps, out double tPlus))
                return false;
            if (!TryRayHit(p, new Vector2d(-u.X, -u.Y), obstacles, maxRay, eps, out double tMinus))
                return false;

            double L = tPlus + tMinus;
            if (L < 1.0) return false;
            lenCm = Math.Round(L, MidpointRounding.AwayFromZero);
            return lenCm >= 1.0;
        }

        private static bool TryFindSideGuides(
            Point2d p, Vector2d u, int widthCm, List<Seg2d> hatilSegs,
            out Seg2d sidePos, out Seg2d sideNeg, out Vector2d uAlong)
        {
            sidePos = default;
            sideNeg = default;
            uAlong = u;

            double half = widthCm * 0.5;
            double distTol = Math.Max(3.0, widthCm * 0.35);
            double minSegLen = Math.Max(15.0, widthCm * 0.6);
            double cosPar = Math.Cos(22.0 * Math.PI / 180.0);

            Seg2d bestPos = default, bestNeg = default;
            double bestPosScore = double.MaxValue, bestNegScore = double.MaxValue;
            bool havePos = false, haveNeg = false;

            for (int i = 0; i < hatilSegs.Count; i++)
            {
                Seg2d s = hatilSegs[i];
                if (s.Length < minSegLen) continue;
                double align = Math.Abs(s.Dir.DotProduct(u));
                if (align < cosPar) continue;

                // Noktanın segmente izdüşümü segment üzerinde / yakınında mı?
                if (!TryProjectOnSegment(p, s.A, s.B, out double along, out double distAbs, out double signedDist))
                    continue;
                if (along < -widthCm || along > s.Length + widthCm) continue;
                if (Math.Abs(distAbs - half) > distTol) continue;

                double score = Math.Abs(distAbs - half) + (1.0 - align) * 10.0;
                if (signedDist > 0)
                {
                    if (score < bestPosScore) { bestPosScore = score; bestPos = s; havePos = true; }
                }
                else
                {
                    if (score < bestNegScore) { bestNegScore = score; bestNeg = s; haveNeg = true; }
                }
            }

            if (!havePos || !haveNeg) return false;
            sidePos = bestPos;
            sideNeg = bestNeg;

            // Yön: iki kenarın ortalaması (yazı ile aynı yarı düzleme çevir)
            Vector2d d1 = bestPos.Dir;
            Vector2d d2 = bestNeg.Dir;
            if (d1.DotProduct(u) < 0) d1 = new Vector2d(-d1.X, -d1.Y);
            if (d2.DotProduct(u) < 0) d2 = new Vector2d(-d2.X, -d2.Y);
            Vector2d avg = d1 + d2;
            if (avg.Length > 1e-9) uAlong = avg.GetNormal();
            else uAlong = u;
            return true;
        }

        private static bool TryProjectOnSegment(
            Point2d p, Point2d a, Point2d b,
            out double along, out double distAbs, out double signedDist)
        {
            along = distAbs = signedDist = 0;
            Vector2d ab = b - a;
            double len = ab.Length;
            if (len < 1e-9) return false;
            Vector2d u = ab / len;
            Vector2d ap = p - a;
            along = ap.DotProduct(u);
            // 2D signed distance: cross / len
            signedDist = (ab.X * ap.Y - ab.Y * ap.X) / len;
            distAbs = Math.Abs(signedDist);
            return true;
        }

        private static bool TryRayHit(
            Point2d origin, Vector2d dir, List<Seg2d> obstacles, double maxT, double minT, out double tHit)
        {
            tHit = 0;
            if (dir.Length < 1e-12) return false;
            Vector2d d = dir.GetNormal();
            double best = double.MaxValue;
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (!TryRaySegmentIntersection(origin, d, obstacles[i].A, obstacles[i].B, out double t))
                    continue;
                if (t < minT || t > maxT) continue;
                if (t < best) best = t;
            }
            if (best >= double.MaxValue * 0.5) return false;
            tHit = best;
            return true;
        }

        /// <summary>origin + t*dir = a + s*(b-a); t&gt;=0, s in [0,1].</summary>
        private static bool TryRaySegmentIntersection(
            Point2d origin, Vector2d dir, Point2d a, Point2d b, out double t)
        {
            t = 0;
            Vector2d ab = b - a;
            // 2D cross
            double den = dir.X * ab.Y - dir.Y * ab.X;
            if (Math.Abs(den) < 1e-12) return false; // parallel
            Vector2d ao = a - origin;
            t = (ao.X * ab.Y - ao.Y * ab.X) / den;
            double s = (ao.X * dir.Y - ao.Y * dir.X) / den;
            if (t < 0) return false;
            if (s < -1e-6 || s > 1.0 + 1e-6) return false;
            return true;
        }

        private static double SignedDistPointToLine(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double len = ab.Length;
            if (len < 1e-9) return p.GetDistanceTo(a);
            Vector2d ap = p - a;
            return (ab.X * ap.Y - ab.Y * ap.X) / len;
        }

        private static bool SegSame(Seg2d a, Seg2d b)
        {
            const double tol = 0.05;
            bool same =
                (a.A.GetDistanceTo(b.A) < tol && a.B.GetDistanceTo(b.B) < tol)
                || (a.A.GetDistanceTo(b.B) < tol && a.B.GetDistanceTo(b.A) < tol);
            return same;
        }

        private static string GetEntityText(Entity ent)
        {
            if (ent is DBText db)
                return db.TextString ?? "";
            if (ent is MText mt)
            {
                try { return mt.Contents ?? mt.Text ?? ""; }
                catch { return mt.Text ?? ""; }
            }
            return "";
        }

        private static bool TryGetTextPose(Entity ent, out Point3d pt, out double rotRad)
        {
            pt = Point3d.Origin;
            rotRad = 0;
            try
            {
                if (ent is DBText db)
                {
                    // Orta hizalı etiketlerde AlignmentPoint daha doğru merkez
                    try
                    {
                        if (db.HorizontalMode != TextHorizontalMode.TextLeft
                            || db.VerticalMode != TextVerticalMode.TextBase)
                            pt = db.AlignmentPoint;
                        else
                            pt = db.Position;
                    }
                    catch { pt = db.Position; }
                    rotRad = db.Rotation;
                    return true;
                }
                if (ent is MText mt)
                {
                    pt = mt.Location;
                    rotRad = mt.Rotation;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryGetTextPoint(Entity ent, out Point3d pt)
        {
            return TryGetTextPose(ent, out pt, out _);
        }

        private static bool LayerEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static void RunManual()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                TemelHatiliForm.Shutdown();
                return;
            }

            Editor ed = doc.Editor;
            try
            {
                if (!TryValidate(out string why))
                {
                    ed.WriteMessage("\nTEMELHATILICIZ: {0}", why);
                    return;
                }

                var opt = new PromptPointOptions("\nTemel hatılı detayı — grobeton sol-alt köşesi: ")
                {
                    AllowNone = false
                };
                PromptPointResult ppr = ed.GetPoint(opt);
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nTEMELHATILICIZ: iptal.");
                    return;
                }

                string note;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var drawer = new Drawer(tr, doc.Database);
                    string manTitle = string.Format(CultureInfo.InvariantCulture,
                        "TH ({0}/{1}) DETAYI 1:25",
                        (int)Math.Round(TemelHatiliSettings.HatilKalinligiCm),
                        (int)Math.Round(TemelHatiliSettings.HatilYuksekligiCm));
                    note = drawer.DrawKalip(ppr.Value, out _, out _, out _, detailTitle: manTitle);
                    tr.Commit();
                }
                ed.WriteMessage("\nTEMELHATILICIZ: {0}", note);
            }
            catch (System.Exception ex)
            {
                try { ed.WriteMessage("\nTEMELHATILICIZ hata: {0}", ex.Message); }
                catch { }
            }
            finally
            {
                // Çizim sonrası komuttan çık; form kapansın. TEMELHATILI tekrar açar.
                TemelHatiliForm.Shutdown();
            }
        }

        private static bool TryValidate(out string why)
        {
            return TryValidateDims(
                TemelHatiliSettings.TemelYuksekligiCm,
                TemelHatiliSettings.HatilKalinligiCm,
                TemelHatiliSettings.HatilYuksekligiCm,
                out why);
        }

        private static bool TryValidateDims(double temelH, double hatilW, double hatilH, out string why)
        {
            why = null;
            if (temelH < 10)
            {
                why = "temel yüksekliği en az 10 cm.";
                return false;
            }
            if (hatilW < 10)
            {
                why = "hatıl kalınlığı en az 10 cm.";
                return false;
            }
            if (hatilH < 20)
            {
                why = "hatıl yüksekliği en az 20 cm.";
                return false;
            }
            double doseme = TemelHatiliSettings.DosemeKalinligiCm;
            if (doseme < 0) doseme = 0;
            if (doseme >= hatilH)
            {
                why = "döşeme kalınlığı hatıl yüksekliğinden küçük olmalı.";
                return false;
            }
            double pas = TemelHatiliSettings.PaspayiCm;
            if (hatilW <= 2.0 * pas + 0.5)
            {
                why = "hatıl kalınlığı 2×paspayından büyük olmalı.";
                return false;
            }
            if (temelH <= TemelAltPaspayiCm + pas)
            {
                why = "temel yüksekliği alt paspayı + kenar paspayından büyük olmalı.";
                return false;
            }
            if (TemelHatiliSettings.DikeyCapMm < 6)
            {
                why = "dikey donatı çapı en az 6 mm.";
                return false;
            }
            return true;
        }

        /// <summary>Üst gönye (cm) = hatıl kalınlığı − 2×paspayı.</summary>
        private static double UstGonyeCm(double hatilKalinlikCm, double paspayiCm)
        {
            double g = hatilKalinlikCm - 2.0 * paspayiCm;
            return g > 0.5 ? g : 0.5;
        }

        /// <summary>
        /// Alt gönye (cm): ≥12ø (yuvarlamadan); toplam boy (gövde+üst+alt) 5 cm katına tamamlanır.
        /// Örn. gövde 152 + üst 14 + 12ø(12) = 178 → L=180 → alt = 14.
        /// </summary>
        private static double AltGonyeCm(double capMm, double stemCm, double ustGonyeCm)
        {
            double minAlt = 12.0 * (capMm / 10.0); // 12ø (cm) — 5’e yuvarlama yok
            if (minAlt < 0.5) minAlt = 0.5;

            double baseL = stemCm + ustGonyeCm + minAlt;
            double targetL = Math.Ceiling(baseL / 5.0 - 1e-9) * 5.0;
            double alt = targetL - stemCm - ustGonyeCm;
            if (alt < minAlt - 1e-9) alt = minAlt;
            return alt;
        }

        private sealed class Drawer
        {
            private readonly Transaction _tr;
            private readonly Database _db;
            private readonly BlockTableRecord _btr;
            private readonly ObjectId _style;

            public Drawer(Transaction tr, Database db)
            {
                _tr = tr;
                _db = db;
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                _btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                _style = GetOrCreateYaziStyle(tr, db);
                EnsureLayers(tr, db);
            }

            private static double S(double realCm) => realCm * Scale;

            public string DrawKalip(Point3d origin,
                out double rightEdgeX,
                out string yatayEtiketOnEk,
                out double detailTopY,
                double? temelHOverride = null,
                double? hatilWOverride = null,
                double? hatilHOverride = null,
                string detailTitle = null,
                int? duseyAdetOverride = null,
                int? yatayAdetOverride = null)
            {
                rightEdgeX = origin.X;
                yatayEtiketOnEk = null;
                detailTopY = origin.Y;
                double temelH = temelHOverride ?? TemelHatiliSettings.TemelYuksekligiCm;
                double hatilH = hatilHOverride ?? TemelHatiliSettings.HatilYuksekligiCm;
                double hatilW = hatilWOverride ?? TemelHatiliSettings.HatilKalinligiCm;
                double dosemeH = TemelHatiliSettings.DosemeKalinligiCm;
                if (dosemeH < 0) dosemeH = 0;
                double kotUstCm = TemelHatiliSettings.TemelUstKotuCm;
                double stemH = hatilH - dosemeH;
                if (stemH < 1) stemH = 1;

                double span = YanCikintiCm + hatilW + YanCikintiCm;
                double x0 = origin.X;
                double y0 = origin.Y;
                double x1 = x0 + S(span);
                double yGb1 = y0 + S(GrobetonCm);
                double yFund1 = yGb1 + S(temelH);
                double ySlabBot = yFund1 + S(stemH);
                double yTop = yFund1 + S(hatilH);
                if (dosemeH <= 0.5)
                    yTop = ySlabBot;
                detailTopY = yTop;

                double xMid = (x0 + x1) * 0.5;
                double xHL = xMid - S(hatilW) * 0.5;
                double xHR = xMid + S(hatilW) * 0.5;

                // ZEMIN: grobeton alt çizgi
                Line(x0, y0, x1, y0, LayerZemin);

                // TEMEL: alt + üst (hatıl altında boşluk)
                Line(x0, yGb1, x1, yGb1, LayerTemel);
                Line(x0, yFund1, xHL, yFund1, LayerTemel);
                Line(xHR, yFund1, x1, yFund1, LayerTemel);

                // TEMEL HATILI (üst yatay yok — döşeme ile birleşik)
                Line(xHL, yFund1, xHL, ySlabBot, LayerHatil);
                Line(xHR, yFund1, xHR, ySlabBot, LayerHatil);

                // DOSEME
                if (dosemeH > 0.5)
                {
                    Line(x0, ySlabBot, xHL, ySlabBot, LayerDoseme);
                    Line(xHR, ySlabBot, x1, ySlabBot, LayerDoseme);
                    Line(x0, yTop, x1, yTop, LayerDoseme);
                }

                // Sol/sağ çıkıntı finişi — komple KESIT SINIRI
                Line(x0, y0, x0, yTop, LayerKesitSiniri);
                Line(x1, y0, x1, yTop, LayerKesitSiniri);

                // Tarama: hatıl sol/sağ boşluk (döşeme bandı hariç) BLOKAJ; grobeton TARAMA
                HatchRect(x0, yFund1, xHL, ySlabBot, HatchArConc, BlokajHatchScale, LayerBlokaj);
                HatchRect(xHR, yFund1, x1, ySlabBot, HatchArConc, BlokajHatchScale, LayerBlokaj);
                HatchRect(x0, y0, x1, yGb1, HatchArConc, GrobetonHatchScale, LayerTarama);

                // Düşey donatı (iki yüz, gönyeler birbirine bakar)
                double pas = TemelHatiliSettings.PaspayiCm;
                double ustG = UstGonyeCm(hatilW, pas);
                double yBarTop = yTop - S(pas);
                double yBarBot = yGb1 + S(TemelAltPaspayiCm);
                double stemCm = (yBarTop - yBarBot) / Scale;
                double altG = AltGonyeCm(TemelHatiliSettings.DikeyCapMm, stemCm, ustG);
                double xBarL = xHL + S(pas);
                double xBarR = xHR - S(pas);
                // Sol: gönye +X (sağa); sağ: gönye −X (sola)
                DrawDuseyBar(xBarL, yBarTop, yBarBot, +1.0, ustG, altG);
                DrawDuseyBar(xBarR, yBarTop, yBarBot, -1.0, ustG, altG);

                // Birleşim kutusu üstü = temel üst donatı izinin altı (döşeme altı değil)
                double rDot = S(DonatiRadiusCm);
                double yTemelUstDonati = yFund1 - S(TemelDonatiIziPasCm);
                double yKutuTop = yTemelUstDonati;
                double yKutuUstKesit = yKutuTop - rDot;
                double yUstGonyeKesit = yBarTop - rDot;
                double xCL = xBarL + rDot;
                double xCR = xBarR - rDot;
                var yatayYs = new List<double>();
                double yatayAralikCm = TemelHatiliSettings.YatayAralikCm;
                if (yKutuTop - yBarBot > 2.0 * rDot + 1.0)
                {
                    yatayYs.Add(yKutuUstKesit);
                    yatayYs.Add(yBarBot + rDot);
                }
                if (yBarTop - yKutuTop > 2.0 * rDot + 0.5)
                {
                    yatayYs.Add(yUstGonyeKesit);
                    if (yatayAdetOverride.HasValue && yatayAdetOverride.Value >= 1)
                    {
                        // Plan ID / STA4CAD: hatıl içinde yüz başına adet verilmiş; üst gönyeden kutu üstüne eşit aralık.
                        int nYatay = yatayAdetOverride.Value;
                        double spanYatay = yUstGonyeKesit - yKutuUstKesit;
                        for (int i = 1; i < nYatay; i++) yatayYs.Add(yUstGonyeKesit - i * spanYatay / nYatay);
                        yatayAralikCm = spanYatay / nYatay / Scale;
                    }
                    else
                        AddYatayDonatiFillYs(yatayYs, yUstGonyeKesit, yKutuUstKesit, TemelHatiliSettings.YatayAralikCm);
                }
                double stepXTemel = S(YatayKesitKopyaAralikCm);
                double stepXSub = S(SubasmanKesitKopyaAralikCm);
                int hatilUstAdet = 0;
                double hatilUstYMin = double.MaxValue;
                double hatilUstYMax = double.MinValue;
                foreach (double y in DistinctYs(yatayYs))
                {
                    bool isTemelAlti = y < yFund1 - 1e-6;

                    if (isTemelAlti)
                    {
                        // Temel içi: 20 cm kopya, ø1.5, width 6, DONATI IZI
                        DrawYatayDonatiKesitKopya(xCL, xCR, y, x0, x1, stepXTemel,
                            LayerDonatiIzi, YatayKesitCapCm, YatayKesitGlobalWidth);
                    }
                    else
                    {
                        // Kutu içi — temel üst kotunun üstünde (hatıl)
                        hatilUstAdet++;
                        if (y < hatilUstYMin) hatilUstYMin = y;
                        if (y > hatilUstYMax) hatilUstYMax = y;
                        DrawYatayDonatiKesit(xCL, y, LayerDonati, YatayKesitCapCm, YatayKesitGlobalWidth);
                        DrawYatayDonatiKesit(xCR, y, LayerDonati, YatayKesitCapCm, YatayKesitGlobalWidth);
                    }
                }

                if (hatilUstAdet > 0)
                {
                    int dia = (int)Math.Round(TemelHatiliSettings.YatayCapMm, MidpointRounding.AwayFromZero);
                    int sCode = (int)Math.Round(yatayAralikCm, MidpointRounding.AwayFromZero);
                    if (dia < 1) dia = 1;
                    if (sCode < 1) sCode = 1;
                    yatayEtiketOnEk = string.Format(CultureInfo.InvariantCulture,
                        "2x{0}\u00F8{1}/{2}", hatilUstAdet, dia, sCode);
                    string lab = yatayEtiketOnEk + " (YATAY)";
                    DrawHatilYatayEtiket(xCL, hatilUstYMin, hatilUstYMax, lab);
                }
                else
                {
                    // Liste için yine form çap/aralık; adet yoksa 2x? yerine yalnız ø
                    int dia = (int)Math.Round(TemelHatiliSettings.YatayCapMm, MidpointRounding.AwayFromZero);
                    int sCode = (int)Math.Round(TemelHatiliSettings.YatayAralikCm, MidpointRounding.AwayFromZero);
                    if (dia < 1) dia = 1;
                    if (sCode < 1) sCode = 1;
                    yatayEtiketOnEk = string.Format(CultureInfo.InvariantCulture,
                        "2x?\u00F8{0}/{1}", dia, sCode);
                }

                // Temel / subasman donatı izleri — kesit sınırından sınıra
                DrawDonatiIzi(x0, x1, yFund1 - S(TemelDonatiIziPasCm));
                DrawDonatiIzi(x0, x1, yGb1 + S(TemelDonatiIziPasCm));

                // Subasman: iz = üst − 2×pas; kopya merkez = üst − (2×pas − 1 cm)
                if (dosemeH > 0.5)
                {
                    double ySubIzi = yTop - S(2.0 * pas);
                    double depthCm = 2.0 * pas - SubasmanKesitIziUstuCm;
                    if (depthCm < 0.5) depthCm = 0.5;
                    double ySubCopy = yTop - S(depthCm);
                    DrawDonatiIzi(x0, x1, ySubIzi);
                    for (double x = xCL - stepXSub; x >= x0 - 0.05; x -= stepXSub)
                        DrawYatayDonatiKesit(x, ySubCopy, LayerDonatiIzi, SubasmanKesitCapCm, SubasmanKesitGlobalWidth);
                    for (double x = xCR + stepXSub; x <= x1 + 0.05; x += stepXSub)
                        DrawYatayDonatiKesit(x, ySubCopy, LayerDonatiIzi, SubasmanKesitCapCm, SubasmanKesitGlobalWidth);
                }

                // Ölçü: üstte hatıl genişliği (+10); sağda detay (döşeme/hatıl/temel/zemin) + toplam (döşemesiz)
                ObjectId dim25 = ResolveOrCreatePlanOlcuDonati25();
                OtoOlcuDimFactory.PlaceRotated(
                    _tr, _btr,
                    new Point3d(xHL, yTop, 0),
                    new Point3d(xHR, yTop, 0),
                    new Point3d(0.5 * (xHL + xHR), yTop + S(HatilOlcuUstOfsetCm), 0),
                    dim25, LayerOlcu);

                double xDimDet = xHR + S(HatilOlcuDikeyOfsetCm);
                void DimVSeg(double yA, double yB, double xDimLine)
                {
                    if (Math.Abs(yB - yA) < 0.5) return;
                    double lo = Math.Min(yA, yB);
                    double hi = Math.Max(yA, yB);
                    OtoOlcuDimFactory.PlaceRotated(
                        _tr, _btr,
                        new Point3d(xHR, lo, 0),
                        new Point3d(xHR, hi, 0),
                        new Point3d(xDimLine, 0.5 * (lo + hi), 0),
                        dim25, LayerOlcu);
                }
                // Detay: döşeme / hatıl gövde / temel / grobeton (ZEMIN)
                if (dosemeH > 0.5)
                    DimVSeg(ySlabBot, yTop, xDimDet);
                DimVSeg(yFund1, ySlabBot, xDimDet);
                DimVSeg(yGb1, yFund1, xDimDet);
                DimVSeg(y0, yGb1, xDimDet);
                // Toplam (+10 cm sağ): döşeme altı yok; temel çizgileri ayrık (hatıl+döşeme / temel / zemin)
                double xDimTop = xDimDet + S(HatilOlcuToplamEkCm);
                DimVSeg(yFund1, yTop, xDimTop);
                DimVSeg(yGb1, yFund1, xDimTop);
                DimVSeg(y0, yGb1, xDimTop);

                // Kotlar — kesitin 15 cm sağı
                double apexX = x1 + S(KesitKotBoslukCm);
                var kots = new List<(double y, double zCm)>();
                if (dosemeH > 0.5)
                    kots.Add((yTop, kotUstCm + hatilH));
                kots.Add((yFund1, kotUstCm));
                kots.Add((yGb1, kotUstCm - temelH));
                DrawKotsAt(apexX, kots);

                // Düşey açılımlar — kotların sağı; en yakın noktalar 5 cm
                double hookSpan = S(Math.Max(ustG, altG));
                double xAfterKot = apexX + KesitKotExtTopRightCm + S(KotSonrasiAcilimPayCm);
                double xStem1 = xAfterKot;
                double xStem2 = xStem1 + hookSpan + S(DuseyAcilimArasiCm) + hookSpan;
                DrawDuseyBar(xStem1, yBarTop, yBarBot, +1.0, ustG, altG);
                DrawDuseyBar(xStem2, yBarTop, yBarBot, -1.0, ustG, altG);
                DrawDuseyAcilimGonyeYazilari(xStem1, yBarTop, yBarBot, +1.0, ustG, altG);
                DrawDuseyAcilimGonyeYazilari(xStem2, yBarTop, yBarBot, -1.0, ustG, altG);
                double Lcm = Math.Round(stemCm + ustG + altG, MidpointRounding.AwayFromZero);
                int diaDusey = (int)Math.Round(TemelHatiliSettings.DikeyCapMm, MidpointRounding.AwayFromZero);
                int aralikDusey = (int)Math.Round(TemelHatiliSettings.DikeyAralikCm, MidpointRounding.AwayFromZero);
                if (diaDusey < 1) diaDusey = 1;
                if (aralikDusey < 1) aralikDusey = 1;
                string duseyLab;
                if (duseyAdetOverride.HasValue && duseyAdetOverride.Value >= 1)
                {
                    duseyLab = string.Format(CultureInfo.InvariantCulture,
                        "{0}\u00F8{1}/{2} L={3:0} (DIKEY)",
                        duseyAdetOverride.Value, diaDusey, aralikDusey, Lcm);
                }
                else
                {
                    duseyLab = string.Format(CultureInfo.InvariantCulture,
                        "\u00F8{0}/{1} L={2:0} (DIKEY)", diaDusey, aralikDusey, Lcm);
                }
                DrawDuseyAcilimEtiket(xStem1, yBarTop, yBarBot, duseyLab);
                DrawDuseyAcilimEtiket(xStem2, yBarTop, yBarBot, duseyLab);

                // Detay kutu sağ kenarı = açılımların en sağı (+ gönye yazı payı)
                rightEdgeX = Math.Max(xStem2, xStem1 + hookSpan) + S(12.0);

                if (!string.IsNullOrWhiteSpace(detailTitle))
                    DrawDetailTitle(x0, x1, y0, detailTitle);

                return "1/25  "
                    + Cm(YanCikintiCm) + "/" + Cm(hatilW) + "/" + Cm(YanCikintiCm)
                    + "  temel " + Cm(temelH)
                    + "  hatıl " + Cm(hatilH)
                    + (dosemeH > 0.5 ? "  döşeme " + Cm(dosemeH) : "")
                    + "  üst gönye " + Cm(ustG)
                    + "  alt gönye " + Cm(altG);
            }

            /// <summary>Kalıp altına ortalanmış 20 punto etiket (KESIT ISMI; alt çizgi yok).</summary>
            private void DrawDetailTitle(double x0, double x1, double yBottom, string title)
            {
                if (string.IsNullOrWhiteSpace(title)) return;
                double cx = 0.5 * (x0 + x1);
                double yTop = yBottom - S(DetailEtiketAltBoslukCm);
                var t = Text(title, cx, yTop, DetailEtiketPunto, 0, LayerKesitIsmi,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextTop);
                if (!TryBox(t, out Extents3d e)) return;
                double midX = 0.5 * (e.MinPoint.X + e.MaxPoint.X);
                double dx = cx - midX;
                if (Math.Abs(dx) > 0.01)
                    t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
            }

            /// <summary>
            /// Detay sağına: "N ADET TH-xx" (ANTET BASLIK 1, 12) + boy tablosu
            /// (IC ANTET; sol 25 cm boş; sağ yazıya göre). L = ölçü + 60 cm bindirme
            /// (düşey adet hesabına girmez). Dönüş: tablo sağ kenarı X.
            /// </summary>
            public double DrawAdetVeBoylar(
                double detailRightX, double yDetailBot, double yDetailTop,
                int thNo, IList<double> lengthsCm, string yatayEtiketOnEk, string thAdi = null)
            {
                int adet = lengthsCm != null ? lengthsCm.Count : 0;
                // thAdi: Plan ID temel planı etiketi (ör. "TH (20/125)"); yoksa TH-nn.
                string adetLine = !string.IsNullOrWhiteSpace(thAdi)
                    ? string.Format(CultureInfo.InvariantCulture, "{0} ADET {1}", adet, thAdi.Trim())
                    : string.Format(CultureInfo.InvariantCulture,
                    "{0} ADET TH-{1}",
                    adet,
                    thNo.ToString("D2", CultureInfo.InvariantCulture));

                var boyLines = new List<string>();
                if (lengthsCm != null && lengthsCm.Count > 0)
                {
                    string onEk = string.IsNullOrWhiteSpace(yatayEtiketOnEk) ? "" : yatayEtiketOnEk.Trim();
                    // Etiket L = ölçü + bindirme, 5 cm katına yukarı; aynı etiket birleşir
                    var groups = new List<(double L, int Count)>();
                    for (int i = 0; i < lengthsCm.Count; i++)
                    {
                        double Lmeas = lengthsCm[i] >= 1.0
                            ? Math.Round(lengthsCm[i], MidpointRounding.AwayFromZero) : 0;
                        double Letiket = Lmeas >= 1.0 ? CeilTo5Cm(Lmeas + YatayBindirmeCm) : 0;
                        if (groups.Count > 0 && Math.Abs(groups[groups.Count - 1].L - Letiket) < 0.5)
                        {
                            var g = groups[groups.Count - 1];
                            groups[groups.Count - 1] = (g.L, g.Count + 1);
                        }
                        else
                            groups.Add((Letiket, 1));
                    }

                    for (int i = 0; i < groups.Count; i++)
                    {
                        double Letiket = groups[i].L;
                        int n = groups[i].Count;
                        string boyCore = Letiket >= 1.0
                            ? string.Format(CultureInfo.InvariantCulture, "{0} L={1:0} (YATAY)", onEk, Letiket).TrimStart()
                            : string.Format(CultureInfo.InvariantCulture, "{0} L=? (YATAY)", onEk).TrimStart();
                        boyLines.Add(n > 1
                            ? string.Format(CultureInfo.InvariantCulture, "{0}x{1}", n, boyCore)
                            : boyCore);
                    }
                }

                double tableLeft = detailRightX + S(AdetTabloDetayArasiCm);

                if (boyLines.Count == 0)
                {
                    double yMid = 0.5 * (yDetailBot + yDetailTop);
                    Text(adetLine, tableLeft, yMid, AdetBaslikPunto, 0, LayerAntetBaslik1,
                        TextHorizontalMode.TextLeft, TextVerticalMode.TextVerticalMid);
                    return tableLeft;
                }

                double maxTextW = 0;
                for (int i = 0; i < boyLines.Count; i++)
                {
                    double w = MeasureTextWidth(boyLines[i], AdetBoyPunto);
                    if (w > maxTextW) maxTextW = w;
                }

                double leftColW = AdetTabloSolBosCm;
                double rightColW = maxTextW + AdetTabloYaziSolPadCm + AdetTabloYaziSagPadCm;
                if (rightColW < AdetTabloSolBosCm) rightColW = AdetTabloSolBosCm;
                double tableW = leftColW + rightColW;
                double rowH = AdetBoySatirAralikCm;
                int nRow = boyLines.Count;
                double tableH = nRow * rowH;

                // Üst çizgi = döşeme üst kotu (detailTopY)
                double ty1 = yDetailTop;
                double ty0 = ty1 - tableH;
                double tx0 = tableLeft;
                double tx1 = tx0 + tableW;
                double xDiv = tx0 + leftColW;

                Line(tx0, ty0, tx1, ty0, LayerIcAntet);
                Line(tx0, ty1, tx1, ty1, LayerIcAntet);
                Line(tx0, ty0, tx0, ty1, LayerIcAntet);
                Line(tx1, ty0, tx1, ty1, LayerIcAntet);
                Line(xDiv, ty0, xDiv, ty1, LayerIcAntet);
                for (int r = 1; r < nRow; r++)
                {
                    double y = ty1 - r * rowH;
                    Line(tx0, y, tx1, y, LayerIcAntet);
                }

                double textX = xDiv + AdetTabloYaziSolPadCm;
                for (int i = 0; i < nRow; i++)
                {
                    double yMid = ty1 - (i + 0.5) * rowH;
                    Text(boyLines[i], textX, yMid, AdetBoyPunto, 0, LayerDonatiYazi,
                        TextHorizontalMode.TextLeft, TextVerticalMode.TextVerticalMid);
                }

                double yTitle = ty1 + AdetBaslikTabloBoslukCm;
                double cx = 0.5 * (tx0 + tx1);
                Text(adetLine, cx, yTitle, AdetBaslikPunto, 0, LayerAntetBaslik1,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextBottom);

                return tx1;
            }

            /// <summary>Geçici DBText ile yazı kutusu genişliği (çizime bırakılmaz).</summary>
            private double MeasureTextWidth(string s, double h)
            {
                if (string.IsNullOrEmpty(s)) return 0;
                // Left+Base modunda AlignmentPoint set edilmez → eNotApplicable
                var t = new DBText();
                t.SetDatabaseDefaults(_db);
                t.Height = h;
                t.TextString = s;
                t.TextStyleId = _style;
                t.Position = new Point3d(0, 0, 0);
                Append(t, LayerDonatiYazi);
                t.TextStyleId = _style;
                double w = 0;
                if (TryBox(t, out Extents3d e))
                    w = e.MaxPoint.X - e.MinPoint.X;
                try
                {
                    if (!t.IsErased)
                    {
                        if (!t.IsWriteEnabled) t.UpgradeOpen();
                        t.Erase();
                    }
                }
                catch { }
                return w > 0.5 ? w : Math.Max(40.0, s.Length * h * 0.55);
            }

            /// <summary>
            /// Düşey çubuk: üst/alt gönye <paramref name="inDir"/> yönünde (+1 sağa, −1 sola).
            /// Köşe R=1.5 cm; width 0. Üst radius merkezine yatay donatı kesiti (ø1.5, 2yay+2vtx).
            /// </summary>
            private void DrawDuseyBar(double x, double yTop, double yBot, double inDir, double ustGonyeCm, double altGonyeCm)
            {
                double r = S(DonatiRadiusCm);
                double ust = S(ustGonyeCm);
                double alt = S(altGonyeCm);
                if (ust < r + 0.5) ust = r + 0.5;
                if (alt < r + 0.5) alt = r + 0.5;
                if (yTop - yBot < 2.0 * r + 1.0)
                    return;

                double xTipUst = x + inDir * ust;
                double xTipAlt = x + inDir * alt;
                double xFillet = x + inDir * r;

                double bulgeTop = inDir > 0 ? DonatiBulge90 : -DonatiBulge90;
                double bulgeBot = inDir > 0 ? DonatiBulge90 : -DonatiBulge90;

                var pl = new Polyline();
                int i = 0;
                pl.AddVertexAt(i++, new Point2d(xTipUst, yTop), 0, 0, 0);
                pl.AddVertexAt(i++, new Point2d(xFillet, yTop), bulgeTop, 0, 0);
                pl.AddVertexAt(i++, new Point2d(x, yTop - r), 0, 0, 0);
                pl.AddVertexAt(i++, new Point2d(x, yBot + r), bulgeBot, 0, 0);
                pl.AddVertexAt(i++, new Point2d(xFillet, yBot), 0, 0, 0);
                pl.AddVertexAt(i, new Point2d(xTipAlt, yBot), 0, 0, 0);
                pl.Closed = false;
                pl.ConstantWidth = 0;
                Append(pl, LayerDonati);
            }

            /// <summary>Açılım gönye boyları — üst/alt kancanın dışına yazı kutusu ile.</summary>
            private void DrawDuseyAcilimGonyeYazilari(
                double x, double yTop, double yBot, double inDir, double ustGonyeCm, double altGonyeCm)
            {
                double ust = S(ustGonyeCm);
                double alt = S(altGonyeCm);
                double xMidUst = x + inDir * ust * 0.5;
                double xMidAlt = x + inDir * alt * 0.5;

                var tU = Text(Cm(ustGonyeCm), xMidUst, yTop + S(4), DonatiYaziH, 0, LayerDonatiYazi,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextBottom);
                if (TryBox(tU, out Extents3d eU))
                {
                    double dy = (yTop + S(2)) - eU.MinPoint.Y;
                    double dx = xMidUst - 0.5 * (eU.MinPoint.X + eU.MaxPoint.X);
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                        tU.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }

                var tA = Text(Cm(altGonyeCm), xMidAlt, yBot - S(4), DonatiYaziH, 0, LayerDonatiYazi,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextTop);
                if (TryBox(tA, out Extents3d eA))
                {
                    double dy = (yBot - S(2)) - eA.MaxPoint.Y;
                    double dx = xMidAlt - 0.5 * (eA.MinPoint.X + eA.MaxPoint.X);
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                        tA.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                }
            }

            /// <summary>Düşey açılım etiketi — ana kolun soluna; yazı kutusu gövdeden 2 cm sol.</summary>
            private void DrawDuseyAcilimEtiket(double xStem, double yTop, double yBot, string lab)
            {
                if (string.IsNullOrEmpty(lab)) return;
                double yMid = 0.5 * (yTop + yBot);
                double xTargetEdge = xStem - S(2.0);
                var t = Text(lab, xTargetEdge, yMid, DonatiYaziH, Math.PI / 2.0, LayerDonatiYazi,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid);
                if (TryBox(t, out Extents3d e))
                {
                    double facing = Math.Abs(e.MaxPoint.X - xStem) <= Math.Abs(e.MinPoint.X - xStem)
                        ? e.MaxPoint.X
                        : e.MinPoint.X;
                    double dx = xTargetEdge - facing;
                    if (Math.Abs(dx) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                }
            }

            /// <summary>
            /// Hatıl üstü yatay: en üst + temel üstü hemen üstü çemberlerden sola 10 cm bit polyline
            /// (ETIKET CIZGISI); yazı kutusu dikeyden 2 cm sola; metin (YATAY).
            /// </summary>
            private void DrawHatilYatayEtiket(double xBarL, double yBarMin, double yBarMax, string lab)
            {
                if (string.IsNullOrEmpty(lab)) return;
                double yLo = Math.Min(yBarMin, yBarMax);
                double yHi = Math.Max(yBarMin, yBarMax);
                double xLine = xBarL - S(HatilYatayEtiketCizgiCm);

                var pl = new Polyline();
                pl.SetDatabaseDefaults(_db);
                pl.AddVertexAt(0, new Point2d(xBarL, yHi), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(xLine, yHi), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(xLine, yLo), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(xBarL, yLo), 0, 0, 0);
                pl.Layer = LayerEtiketCizgisi;
                pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                pl.LineWeight = LineWeight.ByLayer;
                _btr.AppendEntity(pl);
                _tr.AddNewlyCreatedDBObject(pl, true);

                double yMid = 0.5 * (yLo + yHi);
                // Çizgiye bakan yazı kutusu kenarı = dikey − 2 cm (yatay yazı gibi “üstünde”)
                double xTargetEdge = xLine - S(HatilYatayEtiketYaziBoslukCm);
                var t = Text(lab, xTargetEdge, yMid, DonatiYaziH, Math.PI / 2.0, LayerDonatiYazi,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid);
                if (TryBox(t, out Extents3d e))
                {
                    double facing = Math.Abs(e.MaxPoint.X - xLine) <= Math.Abs(e.MinPoint.X - xLine)
                        ? e.MaxPoint.X
                        : e.MinPoint.X;
                    double dx = xTargetEdge - facing;
                    if (Math.Abs(dx) > 0.01)
                        t.TransformBy(Matrix3d.Displacement(new Vector3d(dx, 0, 0)));
                }
            }

            /// <summary>
            /// İki yatay kesit sırası arasına form aralığından büyük boşluk kalmayacak ara Y değerleri.
            /// Uçlar listeye eklenmez (çağıran ekler).
            /// </summary>
            private static void AddYatayDonatiFillYs(List<double> ys, double yUst, double yAlt, double maxAralikCm)
            {
                if (maxAralikCm < 1.0) maxAralikCm = 1.0;
                double maxGap = S(maxAralikCm);
                double yHi = Math.Max(yUst, yAlt);
                double yLo = Math.Min(yUst, yAlt);
                double span = yHi - yLo;
                if (span <= maxGap + 0.05) return;

                int nGaps = (int)Math.Ceiling(span / maxGap - 1e-9);
                if (nGaps < 2) return;
                double step = span / nGaps;
                for (int i = 1; i < nGaps; i++)
                    ys.Add(yHi - i * step);
            }

            private static List<double> DistinctYs(List<double> ys)
            {
                var list = new List<double>();
                foreach (double y in ys.OrderByDescending(v => v))
                {
                    bool dup = false;
                    foreach (double e in list)
                    {
                        if (Math.Abs(e - y) < 0.05) { dup = true; break; }
                    }
                    if (!dup) list.Add(y);
                }
                return list;
            }

            /// <summary>
            /// Kutu sol/sağ çemberini verilen adımla kesit sınırına kadar kopyalar (orijinaller dahil).
            /// </summary>
            private void DrawYatayDonatiKesitKopya(
                double xCL, double xCR, double y,
                double xMin, double xMax, double stepX,
                string layer, double capCm, double width)
            {
                for (double x = xCL; x >= xMin - 0.05; x -= stepX)
                    DrawYatayDonatiKesit(x, y, layer, capCm, width);
                for (double x = xCR; x <= xMax + 0.05; x += stepX)
                    DrawYatayDonatiKesit(x, y, layer, capCm, width);
            }

            /// <summary>DONATI IZI: kesit sol–sağ arası yatay çizgi (renk/kalınlık ByLayer).</summary>
            private void DrawDonatiIzi(double xL, double xR, double y)
            {
                var ln = new Line(new Point3d(xL, y, 0), new Point3d(xR, y, 0));
                ln.SetDatabaseDefaults(_db);
                ln.Layer = LayerDonatiIzi;
                ln.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                ln.LineWeight = LineWeight.ByLayer;
                _btr.AppendEntity(ln);
                _tr.AddNewlyCreatedDBObject(ln, true);
            }

            /// <summary>2 vertex + 2 yay çember; çap/width parametreli, ByLayer.</summary>
            private void DrawYatayDonatiKesit(double cx, double cy, string layer, double capCm, double width)
            {
                double rr = S(capCm) * 0.5;
                var pl = new Polyline();
                pl.SetDatabaseDefaults(_db);
                pl.AddVertexAt(0, new Point2d(cx - rr, cy), 1.0, 0, 0);
                pl.AddVertexAt(1, new Point2d(cx + rr, cy), 1.0, 0, 0);
                pl.Closed = true;
                pl.ConstantWidth = width;
                pl.Layer = layer;
                pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                pl.LineWeight = LineWeight.ByLayer;
                _btr.AppendEntity(pl);
                _tr.AddNewlyCreatedDBObject(pl, true);
            }

            private void DrawKotsAt(double apexX, IList<(double y, double zCm)> items)
            {
                var ordered = new List<(double y, double zCm)>();
                foreach (var it in items)
                {
                    bool dup = false;
                    foreach (var o in ordered)
                    {
                        if (Math.Abs(o.zCm - it.zCm) < 2.0) { dup = true; break; }
                    }
                    if (!dup) ordered.Add(it);
                }
                ordered.Sort((a, b) => a.zCm.CompareTo(b.zCm));

                foreach (var it in ordered)
                {
                    DrawKesitKotClassicSymbol(apexX, it.y);
                    double textX = apexX + KesitKotTriHalfWidthCm;
                    double textY = it.y + KesitKotTriHeightCm + KesitKotTextAboveExtensionCm;
                    ObjectId idTxt = AppendKesitKotText(FormatKesitKot(it.zCm), textX, textY);
                    if (!idTxt.IsNull)
                        SnapTextBoxBottomAbove(idTxt, textY, 5.7);
                }
            }

            private void DrawKesitKotClassicSymbol(double apexX, double apexY)
            {
                double h = KesitKotTriHeightCm;
                double w = KesitKotTriHalfWidthCm;
                double eSec = KesitKotExtTowardSectionCm;
                double eR = KesitKotExtTopRightCm;

                KotLine(apexX - eSec, apexY, apexX, apexY);
                var pA = new Point3d(apexX, apexY, 0);
                var pTL = new Point3d(apexX - w, apexY + h, 0);
                var pTM = new Point3d(apexX, apexY + h, 0);
                var pTR = new Point3d(apexX + w, apexY + h, 0);
                AppendLeftWedge(pA, pTL, pTM);
                KotLine(pTL.X, pTL.Y, pTR.X, pTR.Y);
                KotLine(pA.X, pA.Y, pTR.X, pTR.Y);
                KotLine(pTR.X, pTR.Y, pTM.X, pTM.Y);
                KotLine(pTR.X, pTR.Y, pTR.X + eR, pTR.Y);
            }

            private void AppendLeftWedge(Point3d pA, Point3d pTL, Point3d pTM)
            {
                var pl = new Polyline(3);
                pl.AddVertexAt(0, new Point2d(pA.X, pA.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(pTL.X, pTL.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(pTM.X, pTM.Y), 0, 0, 0);
                pl.Closed = true;
                Append(pl, LayerKotCizgi);

                try
                {
                    var hatch = new Hatch();
                    hatch.SetDatabaseDefaults();
                    hatch.Layer = LayerKotCizgi;
                    hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, KesitKotWedgeSolidAci);
                    _btr.AppendEntity(hatch);
                    _tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { pl.ObjectId });
                    try { hatch.EvaluateHatch(true); } catch { try { hatch.EvaluateHatch(false); } catch { } }
                }
                catch { }
            }

            private ObjectId AppendKesitKotText(string text, double x, double y)
            {
                var txt = new DBText();
                txt.SetDatabaseDefaults();
                txt.Layer = LayerKotYazi;
                txt.Height = KesitKotTextHeightCm;
                txt.TextStyleId = _style;
                txt.TextString = text ?? "";
                txt.HorizontalMode = TextHorizontalMode.TextLeft;
                txt.VerticalMode = TextVerticalMode.TextBottom;
                txt.Position = new Point3d(x, y, 0);
                txt.AlignmentPoint = new Point3d(x, y, 0);
                try { txt.AdjustAlignment(_db); } catch { }
                _btr.AppendEntity(txt);
                _tr.AddNewlyCreatedDBObject(txt, true);
                return txt.ObjectId;
            }

            private void SnapTextBoxBottomAbove(ObjectId textId, double refY, double gapCm)
            {
                if (textId.IsNull) return;
                try
                {
                    var ent = _tr.GetObject(textId, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || !ent.Bounds.HasValue) return;
                    var e = ent.GeometricExtents;
                    double dy = (refY + gapCm) - e.MinPoint.Y;
                    if (Math.Abs(dy) > 1e-6)
                        ent.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
                }
                catch { }
            }

            private static string FormatKesitKot(double zCm)
            {
                double m = Math.Round(zCm / 100.0, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(m) < 0.005)
                    return "\u00B10.00";
                return string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00}", m);
            }

            private void DimV(double dimX, double y0, double y1, double fromX, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (y1 < y0) { double s = y0; y0 = y1; y1 = s; }
                if (y1 - y0 < 0.4) return;
                double dir = Math.Sign(dimX - fromX);
                if (Math.Abs(dir) < 0.5) dir = -1;
                double xEnd = dimX + dir * 2.5;
                Line(fromX, y0, xEnd, y0, LayerOlcu);
                Line(fromX, y1, xEnd, y1, LayerOlcu);
                Tick(dimX, y0);
                Tick(dimX, y1);

                var t = Text(text, dimX, (y0 + y1) * 0.5, DimTextH, Math.PI / 2.0, LayerOlcu,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid);
                double a, b;
                if (TryBox(t, out Extents3d e))
                {
                    a = e.MinPoint.Y - 1.2;
                    b = e.MaxPoint.Y + 1.2;
                }
                else
                {
                    double half = Math.Max(4, text.Length * DimTextH * 0.28);
                    a = (y0 + y1) * 0.5 - half;
                    b = (y0 + y1) * 0.5 + half;
                }
                if (a > y0 + 1.2) Line(dimX, y0, dimX, Math.Min(a, y1), LayerOlcu);
                if (b < y1 - 1.2) Line(dimX, Math.Max(b, y0), dimX, y1, LayerOlcu);
            }

            private void DimH(double dimY, double x0, double x1, double fromY, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (x1 < x0) { double s = x0; x0 = x1; x1 = s; }
                if (x1 - x0 < 0.4) return;
                double dir = Math.Sign(dimY - fromY);
                if (Math.Abs(dir) < 0.5) dir = 1;
                double yEnd = dimY + dir * 2.5;
                Line(x0, fromY, x0, yEnd, LayerOlcu);
                Line(x1, fromY, x1, yEnd, LayerOlcu);
                Tick(x0, dimY);
                Tick(x1, dimY);

                var t = Text(text, (x0 + x1) * 0.5, dimY, DimTextH, 0, LayerOlcu,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid);
                double a, b;
                if (TryBox(t, out Extents3d e))
                {
                    a = e.MinPoint.X - 1.2;
                    b = e.MaxPoint.X + 1.2;
                }
                else
                {
                    double half = Math.Max(4, text.Length * DimTextH * 0.45);
                    a = (x0 + x1) * 0.5 - half;
                    b = (x0 + x1) * 0.5 + half;
                }
                if (a > x0 + 1.2) Line(x0, dimY, Math.Min(a, x1), dimY, LayerOlcu);
                if (b < x1 - 1.2) Line(Math.Max(b, x0), dimY, x1, dimY, LayerOlcu);
            }

            private void Tick(double x, double y)
            {
                const double s = 1.7;
                Line(x - s, y - s, x + s, y + s, LayerOlcu);
            }

            private void KotLine(double x0, double y0, double x1, double y1)
            {
                var ln = new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
                ln.SetDatabaseDefaults();
                ln.Layer = LayerKotCizgi;
                _btr.AppendEntity(ln);
                _tr.AddNewlyCreatedDBObject(ln, true);
            }

            private void Line(double x0, double y0, double x1, double y1, string layer)
            {
                Append(new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)), layer);
            }

            private DBText Text(string s, double x, double y, double h, double rot, string layer,
                TextHorizontalMode hm, TextVerticalMode vm)
            {
                var t = new DBText
                {
                    TextString = s ?? "",
                    Height = h,
                    Rotation = rot,
                    TextStyleId = _style,
                    HorizontalMode = hm,
                    VerticalMode = vm,
                    Position = new Point3d(x, y, 0),
                    AlignmentPoint = new Point3d(x, y, 0)
                };
                Append(t, layer);
                t.TextStyleId = _style;
                try { t.AdjustAlignment(_db); } catch { }
                return t;
            }

            private void Append(Entity ent, string layer)
            {
                ent.SetDatabaseDefaults(_db);
                ent.Layer = layer;
                _btr.AppendEntity(ent);
                _tr.AddNewlyCreatedDBObject(ent, true);
            }

            /// <summary>
            /// Kapalı dikdörtgen AR-CONC tarama. Geçici polyline sınır + ölçek yeniden uygula
            /// (Point2d AppendLoop / PatternScale sırası yoğunluk kaymasına yol açıyordu).
            /// </summary>
            private void HatchRect(double xa, double ya, double xb, double yb,
                string pattern, double patternScale, string layer)
            {
                double xmin = Math.Min(xa, xb);
                double xmax = Math.Max(xa, xb);
                double ymin = Math.Min(ya, yb);
                double ymax = Math.Max(ya, yb);
                if (xmax - xmin < 0.5 || ymax - ymin < 0.5) return;

                Polyline pl = null;
                try
                {
                    pl = new Polyline(4);
                    pl.AddVertexAt(0, new Point2d(xmin, ymin), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(xmax, ymin), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(xmax, ymax), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(xmin, ymax), 0, 0, 0);
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
                    hatch.Origin = new Point2d(xmin, ymin);

                    hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                    hatch.PatternScale = patternScale;
                    hatch.PatternAngle = 0;
                    // PatternScale değişince deseni yeniden yükle (aksi halde ölçek tutmaz / yoğunluk kayar)
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

            private static bool TryBox(DBText t, out Extents3d e)
            {
                e = new Extents3d();
                try
                {
                    e = t.GeometricExtents;
                    return true;
                }
                catch { return false; }
            }

            private static string Cm(double v)
            {
                double r = Math.Round(v, 1, MidpointRounding.AwayFromZero);
                if (Math.Abs(r - Math.Round(r)) < 0.05)
                    return Math.Round(r, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
                return r.ToString("0.0", CultureInfo.InvariantCulture);
            }

            private ObjectId ResolveOrCreatePlanOlcuDonati25()
            {
                return BaDimensionStyles.Ensure(_tr, _db, DimStylePlanOlcuDonati25);
            }
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            EnsureLayer(tr, db, LayerTemel, 2, LineWeight.LineWeight040, dashed: false);
            EnsureLayer(tr, db, LayerHatil, 230, LineWeight.LineWeight030, dashed: false);
            EnsureLayer(tr, db, LayerTemelHatiliIsmi, 243, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerKesitIsmi, 6, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerIcAntet, 152, LineWeight.LineWeight030, dashed: false);
            EnsureLayer(tr, db, LayerAntetBaslik1, 1, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerKolon, 3, LineWeight.LineWeight030, dashed: false);
            EnsureLayer(tr, db, LayerDoseme, 2, LineWeight.LineWeight030, dashed: false);
            EnsureLayer(tr, db, LayerZemin, 43, LineWeight.LineWeight050, dashed: false);
            EnsureLayer(tr, db, LayerKesitSiniri, 241, LineWeight.LineWeight020, dashed: true);
            EnsureLayer(tr, db, LayerOlcu, 14, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerKotCizgi, 7, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerKotYazi, 7, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerDonati, 4, LineWeight.LineWeight040, dashed: false);
            EnsureLayer(tr, db, LayerDonatiIzi, 8, LineWeight.LineWeight025, dashed: false);
            EnsureLayer(tr, db, LayerDonatiYazi, 3, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerEtiketCizgisi, 64, LineWeight.LineWeight015, dashed: false);
            EnsureLayer(tr, db, LayerTarama, 8, LineWeight.LineWeight020, dashed: false);
            EnsureLayer(tr, db, LayerBlokaj, 8, LineWeight.LineWeight015, dashed: false,
                transparencyPercent: LayerBlokajTransparencyPercent);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, short aci, LineWeight lw,
            bool dashed, int? transparencyPercent = null)
        {
            BeykentKatmanYukle.Ensure(tr, db, name, aci, lw, dashed);
        }

        private static ObjectId EnsureDashedLinetype(Transaction tr, Database db)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("DASHED")) return ltt["DASHED"];
            if (ltt.Has("Dashed")) return ltt["Dashed"];
            try { db.LoadLineTypeFile("DASHED", "acad.lin"); }
            catch
            {
                try { db.LoadLineTypeFile("DASHED", "acadiso.lin"); }
                catch
                {
                    try { db.LoadLineTypeFile("Dashed", "acad.lin"); } catch { }
                }
            }
            ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("DASHED")) return ltt["DASHED"];
            if (ltt.Has("Dashed")) return ltt["Dashed"];
            return ObjectId.Null;
        }

        private static ObjectId GetOrCreateYaziStyle(Transaction tr, Database db)
        {
            return BaDimensionStyles.EnsureYazi(tr, db);
        }
    }
}
