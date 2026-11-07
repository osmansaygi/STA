using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISCIZ "Kalıp planından al": kullanıcı kiriş boyunca yüzleri sırayla tıklar (mesnet yüzü, açıklık sonu, …);
    /// aralıklar mesnet eni / Ln olur. Açıklık üzerindeki "MC-01 (25/55)" yazısından ad ve b/h, mesnet aralığına
    /// oturan tarama/kapalı poliçizgiden mesnet tipi (Kolon; yoksa Kiriş) okunur. Çizim birimi cm kabul edilir.
    /// Yalnız okur; çizime nesne yazmaz (geçici vektörler hariç).
    /// </summary>
    internal static class KirisManuelPlanSecim
    {
        /// <summary>Modal formu gizleyip seçim yaptırır; iptal/hata → null.</summary>
        internal static KirisPlanOkuma Sec(Form owner)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            if (owner == null) return SecEditor(doc);
            using (doc.Editor.StartUserInteraction(owner))
                return SecEditor(doc);
        }

        private static bool MesnetSirasi(int noktaSayisi, bool basSerbest)
            => ((noktaSayisi - 1) + (basSerbest ? 1 : 0)) % 2 == 0;

        internal static KirisPlanOkuma SecEditor(Document doc)
        {
            var ed = doc.Editor;
            ed.WriteMessage("\nKalıp planından kiriş: yüzleri soldan sağa sırayla tıklayın — ilk mesnetin dış yüzü, mesnet öbür yüzü (açıklık başı), açıklık sonu (sonraki mesnet yüzü), … son mesnetin dış yüzü, Enter.");
            var pts = new List<Point3d>(); // UCS
            bool basSerbest = false;
            Vector3d? yon = null; // UCS, ilk iki noktadan (yalnız ilerleme denetimi)
            while (true)
            {
                PromptPointOptions po;
                int aciklikSay = 0;
                for (int j = 0; j + 1 < pts.Count; j++) if (!MesnetSirasi(j + 1, basSerbest)) aciklikSay++;
                if (pts.Count == 0)
                {
                    po = new PromptPointOptions(basSerbest ? "\nKonsol serbest ucu (kiriş başı): " : "\nİlk mesnetin dış yüzü [Konsol]: ");
                    if (!basSerbest) po.Keywords.Add("Konsol");
                }
                else
                {
                    bool mesnet = MesnetSirasi(pts.Count, basSerbest);
                    int mesnetNo = (pts.Count - 1 + (basSerbest ? 1 : 0)) / 2 + 1;
                    bool enter = aciklikSay > 0 && (!mesnet || !basSerbest);
                    string ek = enter ? (mesnet ? " [Geri] <Enter: konsol ucu, bitir>" : " [Geri] <Enter: bitir>") : " [Geri]";
                    po = new PromptPointOptions(mesnet
                        ? string.Format("\nMesnet {0} öbür yüzü (açıklık başı){1}: ", mesnetNo, ek)
                        : string.Format("\nAçıklık {0} sonu (mesnet {1} yüzü){2}: ", aciklikSay + 1, mesnetNo + 1, ek));
                    po.Keywords.Add("Geri");
                    po.AllowNone = enter;
                    po.UseBasePoint = true;
                    po.BasePoint = pts[pts.Count - 1];
                    po.UseDashedLine = true;
                }
                var pr = ed.GetPoint(po);
                if (pr.Status == PromptStatus.Keyword)
                {
                    if (pr.StringResult == "Konsol") { basSerbest = true; continue; }
                    if (pts.Count > 0) pts.RemoveAt(pts.Count - 1);
                    else basSerbest = false;
                    if (pts.Count < 2) yon = null;
                    try { ed.Regen(); } catch { }
                    for (int j = 1; j < pts.Count; j++) Vektor(ed, pts[j - 1], pts[j], MesnetSirasi(j, basSerbest));
                    continue;
                }
                if (pr.Status == PromptStatus.None) break;
                if (pr.Status != PromptStatus.OK) { try { ed.Regen(); } catch { } return null; }
                var p = pr.Value;
                if (pts.Count >= 1)
                {
                    var d = p - pts[0];
                    if (yon == null)
                    {
                        if (d.Length < 1e-6) { ed.WriteMessage("\nNokta öncekiyle aynı; yeniden seçin."); continue; }
                        yon = d.GetNormal();
                    }
                    else if (d.DotProduct(yon.Value) <= (pts[pts.Count - 1] - pts[0]).DotProduct(yon.Value) + 1e-6)
                    { ed.WriteMessage("\nNokta kiriş boyunca ilerlemiyor; yeniden seçin."); continue; }
                    Vektor(ed, pts[pts.Count - 1], p, MesnetSirasi(pts.Count, basSerbest));
                }
                pts.Add(p);
            }
            try { ed.Regen(); } catch { }

            // WCS'de eksen: ilk → son nokta (uzun taban daha doğru); eksene 0,5° yakınsa tam X/Y.
            var ucs = ed.CurrentUserCoordinateSystem;
            var w = new List<Point3d>();
            foreach (var p in pts) { var q = p.TransformBy(ucs); w.Add(new Point3d(q.X, q.Y, 0)); }
            var ex = w[w.Count - 1] - w[0];
            if (ex.Length < 1e-6) { ed.WriteMessage("\nKiriş boyu çok kısa."); return null; }
            var dir = ex.GetNormal();
            double aci = Math.Atan2(dir.Y, dir.X);
            for (int k = -4; k <= 4; k++)
                if (Math.Abs(aci - k * Math.PI / 2) < Math.PI / 360) { dir = new Vector3d(Math.Round(Math.Cos(k * Math.PI / 2)), Math.Round(Math.Sin(k * Math.PI / 2)), 0); break; }
            var t = new List<double>();
            foreach (var q in w) t.Add((q - w[0]).DotProduct(dir));
            double f = Birim(ed, t[t.Count - 1] - t[0]);
            if (f <= 0) return null;
            for (int i = 0; i < t.Count; i++) t[i] *= f;
            var o = KirisPlanOkuma.Araliklardan(t, basSerbest, out string hata);
            if (o == null) { ed.WriteMessage("\nKalıp planından okunamadı: {0}", hata); return null; }
            try { Analiz(doc.Database, w, dir, t, f, o); }
            catch (System.Exception exc) { ed.WriteMessage("\nYazı/mesnet tipi okunamadı ({0}); yalnız ölçüler alındı.", exc.Message); }
            ed.WriteMessage("\nKalıp planından: {0}", o.Ozet());
            return o;
        }

        /// <summary>
        /// cm / çizim birimi. Plan ID kalıp planı cm'dir; kiriş boyu 50–20000 birim ise sorulmaz. Daha küçükse
        /// (STA4CAD DWG'si 1/100 pafta metre, ya da metre) veya daha büyükse (mm) birim sorulur. İptal → 0.
        /// </summary>
        private static double Birim(Editor ed, double boy)
        {
            if (boy >= 50 && boy <= 20000) return 1;
            var po = new PromptKeywordOptions(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "\nSeçilen boy {0:0.####} birim; çizim cm değil gibi. Çizim birimi [Cm/Metre/Milimetre/Pafta100/Pafta50]: ", boy));
            foreach (var k in new[] { "Cm", "Metre", "Milimetre", "Pafta100", "Pafta50" }) po.Keywords.Add(k);
            po.Keywords.Default = boy > 20000 ? "Milimetre" : boy < 1.5 ? "Pafta100" : "Metre";
            po.AllowNone = true;
            var pr = ed.GetKeywords(po);
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return 0;
            switch (pr.Status == PromptStatus.None ? po.Keywords.Default : pr.StringResult)
            {
                case "Metre": return 100;
                case "Milimetre": return 0.1;
                case "Pafta100": return 10000; // 1/100 ölçekli pafta, metre (STA4CAD kalıp DWG'si)
                case "Pafta50": return 5000;
                default: return 1;
            }
        }

        private static void Vektor(Editor ed, Point3d a, Point3d b, bool mesnet)
        {
            try { ed.DrawVector(a, b, mesnet ? 1 : 3, true); } catch { }
        }

        private sealed class Sekil { public double AMin = double.MaxValue, AMax = double.MinValue, PMin = double.MaxValue, PMax = double.MinValue; }
        private sealed class Yazi { public string Ad; public double B, H, A, P; }

        /// <summary>Açıklık yazıları ve mesnet tipleri: geçerli boşluktaki yazı, tarama ve kapalı poliçizgiler (blok içleri hariç).</summary>
        private static void Analiz(Database db, List<Point3d> w, Vector3d dir, List<double> t, double f, KirisPlanOkuma o)
        {
            var n = new Vector3d(-dir.Y, dir.X, 0);
            var dirF = dir * f; var nF = n * f; // izdüşümler cm
            var w0 = w[0];
            double pOrt = 0;
            foreach (var q in w) pOrt += (q - w0).DotProduct(nF);
            pOrt /= w.Count;
            const double YanAra = 150; // eksenden yana arama bandı (cm)
            double aMin = t[0] - 60, aMax = t[t.Count - 1] + 60;
            Point3d K(double a, double p) => w0 + dir * (a / f) + n * (p / f);
            var kutu = new Extents3d();
            foreach (var c in new[] { K(aMin, pOrt - YanAra), K(aMin, pOrt + YanAra), K(aMax, pOrt - YanAra), K(aMax, pOrt + YanAra) }) kutu.AddPoint(c);
            bool Kesisir(Extents3d e) => e.MinPoint.X <= kutu.MaxPoint.X && e.MaxPoint.X >= kutu.MinPoint.X && e.MinPoint.Y <= kutu.MaxPoint.Y && e.MaxPoint.Y >= kutu.MinPoint.Y;

            var sekiller = new List<Sekil>();
            var yazilar = new List<Yazi>();
            var cText = RXObject.GetClass(typeof(DBText));
            var cMText = RXObject.GetClass(typeof(MText));
            var cHatch = RXObject.GetClass(typeof(Hatch));
            var cPline = RXObject.GetClass(typeof(Polyline));
            double eksenAci = Math.Atan2(dir.Y, dir.X);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var cls = id.ObjectClass;
                    bool text = cls.IsDerivedFrom(cText), mtext = cls.IsDerivedFrom(cMText), hatch = cls.IsDerivedFrom(cHatch), pline = cls.IsDerivedFrom(cPline);
                    if (!text && !mtext && !hatch && !pline) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    var b = ent.Bounds;
                    if (!b.HasValue || !Kesisir(b.Value)) continue;
                    if (text || mtext)
                    {
                        string s; double rot;
                        if (ent is DBText dt) { s = dt.TextString; rot = dt.Rotation; }
                        else { var mt = (MText)ent; s = mt.Text; rot = mt.Rotation; }
                        if (Math.Abs(Math.Sin(rot - eksenAci)) > 0.26) continue; // kirişe paralel yazı
                        if (!KirisPlanOkuma.TryEtiket(s, out string ad, out double bb, out double hh)) continue;
                        var c = b.Value.MinPoint + (b.Value.MaxPoint - b.Value.MinPoint) * 0.5;
                        var v = new Point3d(c.X, c.Y, 0) - w0;
                        yazilar.Add(new Yazi { Ad = ad, B = bb, H = hh, A = v.DotProduct(dirF), P = v.DotProduct(nF) });
                    }
                    else if (ent is Polyline pl)
                    {
                        if (!pl.Closed || pl.NumberOfVertices < 3) continue;
                        var sk = new Sekil();
                        for (int i = 0; i < pl.NumberOfVertices; i++) Ekle(sk, pl.GetPoint3dAt(i), w0, dirF, nF);
                        sekiller.Add(sk);
                    }
                    else if (ent is Hatch h)
                    {
                        if (!h.Normal.IsParallelTo(Vector3d.ZAxis)) continue;
                        int loops = h.NumberOfLoops;
                        for (int i = 0; i < loops; i++)
                        {
                            HatchLoop loop;
                            try { loop = h.GetLoopAt(i); } catch { continue; }
                            var sk = new Sekil();
                            if (loop.IsPolyline)
                            {
                                foreach (BulgeVertex bv in loop.Polyline) Ekle(sk, new Point3d(bv.Vertex.X, bv.Vertex.Y, 0), w0, dirF, nF);
                            }
                            else
                            {
                                foreach (Curve2d cv in loop.Curves)
                                {
                                    if (cv is LineSegment2d ls) { Ekle(sk, new Point3d(ls.StartPoint.X, ls.StartPoint.Y, 0), w0, dirF, nF); Ekle(sk, new Point3d(ls.EndPoint.X, ls.EndPoint.Y, 0), w0, dirF, nF); }
                                    else if (cv is CircularArc2d ca) { Ekle(sk, new Point3d(ca.StartPoint.X, ca.StartPoint.Y, 0), w0, dirF, nF); Ekle(sk, new Point3d(ca.EndPoint.X, ca.EndPoint.Y, 0), w0, dirF, nF); }
                                }
                            }
                            if (sk.AMax > sk.AMin) sekiller.Add(sk);
                        }
                    }
                }
                tr.Commit();
            }

            // Aralık konumları: k. mesnet / i. açıklık başlangıç-bitiş t indeksleri.
            int ti = 0;
            int nAc = o.Acikliklar.Count;
            for (int k = 0; k <= nAc; k++)
            {
                var m = o.Mesnetler[k];
                if (m.Tip != KirisManuelMesnetTip.Serbest)
                {
                    double ts = t[ti], te = t[ti + 1];
                    ti++;
                    double tol = Math.Max(3.0, 0.12 * (te - ts));
                    bool kolon = false;
                    foreach (var sk in sekiller)
                    {
                        if (Math.Abs(sk.AMin - ts) <= tol && Math.Abs(sk.AMax - te) <= tol && sk.PMin <= pOrt + 60 && sk.PMax >= pOrt - 60) { kolon = true; break; }
                    }
                    m.Tip = kolon ? KirisManuelMesnetTip.Kolon : KirisManuelMesnetTip.Kiris;
                }
                if (k == nAc) break;
                {
                    double ts = t[ti], te = t[ti + 1];
                    ti++;
                    Yazi en = null;
                    double enD = double.MaxValue;
                    foreach (var y in yazilar)
                    {
                        if (y.A < ts - 10 || y.A > te + 10) continue;
                        double d = Math.Abs(y.P - pOrt);
                        if (d > 100) continue;
                        if (d < enD) { enD = d; en = y; }
                    }
                    if (en != null)
                    {
                        var a = o.Acikliklar[k];
                        a.Ad = en.Ad; a.BCm = en.B; a.HCm = en.H;
                    }
                }
            }
        }

        private static void Ekle(Sekil s, Point3d p, Point3d w0, Vector3d dir, Vector3d n)
        {
            var v = new Point3d(p.X, p.Y, 0) - w0;
            double a = v.DotProduct(dir), q = v.DotProduct(n);
            if (a < s.AMin) s.AMin = a;
            if (a > s.AMax) s.AMax = a;
            if (q < s.PMin) s.PMin = q;
            if (q > s.PMax) s.PMax = q;
        }
    }
}
