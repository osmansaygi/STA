using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace BeykentOrtak
{
    /// <summary>
    /// 1/25 detayların pafta ölçeği. 1/50 paftada detay gerçek cm ×2 çizilir; 1/100 paftaya konacaksa
    /// aynı çizim (yazı, ölçü, antet dahil) bir kez daha ×2 büyütülür (gerçek cm ×4). Ölçü değerleri DIMLFAC ile korunur.
    /// </summary>
    internal static class PaftaOlcek
    {
        internal const double Yuz = 2.0;

        /// <summary>Form seçeneği: 1/50 pafta / 1/100 pafta. Döndürülen ilk düğme 1/50'dir.</summary>
        internal static RadioButton[] Secenekler(bool yuz)
        {
            return new[]
            {
                new RadioButton { Text = "1/50 pafta", AutoSize = true, Checked = !yuz },
                new RadioButton { Text = "1/100 pafta (×2)", AutoSize = true, Checked = yuz }
            };
        }

        /// <summary>Model alanında <paramref name="baslangicHandle"/> sonrası oluşan nesneleri büyütür (komut başında db.Handseed.Value).</summary>
        internal static int BuyutYeni(Database db, long baslangicHandle, Point3d baz, double carpan = Yuz, Editor ed = null)
        {
            if (db == null) return 0;
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsNull && !id.IsErased && id.Handle.Value >= baslangicHandle) ids.Add(id);
                tr.Commit();
            }
            return Buyut(db, ids, baz, carpan, ed);
        }

        /// <summary>
        /// Yerleşim noktası bilinmeyen detay komutları: yeni nesnelerin kutusunun sol-alt (ust: sol-üst) köşesi sabit kalarak büyütür.
        /// </summary>
        internal static int BuyutYeniKose(Database db, long baslangicHandle, bool ust, double carpan = Yuz, Editor ed = null)
        {
            if (db == null) return 0;
            var ids = new List<ObjectId>();
            Extents3d? kutu = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased || id.Handle.Value < baslangicHandle) continue;
                    ids.Add(id);
                    try
                    {
                        var e = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                        if (kutu.HasValue) { var k = kutu.Value; k.AddExtents(e); kutu = k; } else kutu = e;
                    }
                    catch { }
                }
                tr.Commit();
            }
            if (ids.Count == 0 || !kutu.HasValue) return 0;
            var baz = new Point3d(kutu.Value.MinPoint.X, ust ? kutu.Value.MaxPoint.Y : kutu.Value.MinPoint.Y, 0);
            return Buyut(db, ids, baz, carpan, ed);
        }

        /// <summary>Verilen nesneleri <paramref name="baz"/> etrafında büyütür; ed verilirse görünüm yeni çizime alınır.</summary>
        internal static int Buyut(Database db, IEnumerable<ObjectId> ids, Point3d baz, double carpan = Yuz, Editor ed = null)
        {
            if (db == null || ids == null || carpan <= 0 || Math.Abs(carpan - 1.0) < 1e-9) return 0;
            var xf = Matrix3d.Scaling(carpan, baz);
            int n = 0;
            Extents3d? kutu = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // 1/100 pafta: ölçü, adı ölçeğini gösteren karşılık stile geçer (OLCU X 50-25 → OLCU X 100-25, OLCU X 50 → OLCU X 100-50).
                // Stil tablosu entity ForWrite açılmadan doldurulur.
                var stilMap = new Dictionary<ObjectId, ObjectId>();
                if (Math.Abs(carpan - Yuz) < 1e-9)
                {
                    var dimClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Dimension));
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsNull || id.IsErased || !id.ObjectClass.IsDerivedFrom(dimClass)) continue;
                        try
                        {
                            ObjectId st = ((Dimension)tr.GetObject(id, OpenMode.ForRead, false)).DimensionStyle;
                            if (st.IsNull || stilMap.ContainsKey(st)) continue;
                            stilMap[st] = OlcuStilAdlari.Pafta100Stili(tr, db, st, carpan);
                        }
                        catch { }
                    }
                }
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent;
                    try { ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity; }
                    catch { continue; }
                    if (ent == null) continue;
                    try
                    {
                        var dim = ent as Dimension;
                        double lf = 1.0, sc = 1.0;
                        if (dim != null)
                        {
                            try { lf = dim.Dimlfac; } catch { }
                            try { sc = dim.Dimscale; } catch { }
                        }
                        ent.TransformBy(xf);
                        if (dim != null)
                        {
                            if (stilMap.TryGetValue(dim.DimensionStyle, out ObjectId yeniStil) && !yeniStil.IsNull)
                                dim.DimensionStyle = yeniStil;
                            // Geometri büyüdü: ölçü değeri aynı kalsın, ok/yazı/uzatma çizgileri de büyüsün.
                            dim.Dimlfac = (Math.Abs(lf) < 1e-12 ? 1.0 : lf) / carpan;
                            dim.Dimscale = (sc <= 0 ? 1.0 : sc) * carpan;
                            try { dim.RecomputeDimensionBlock(true); } catch { }
                        }
                        try { ent.LinetypeScale *= carpan; } catch { }
                        try
                        {
                            var e = ent.GeometricExtents;
                            if (kutu.HasValue) { var k = kutu.Value; k.AddExtents(e); kutu = k; } else kutu = e;
                        }
                        catch { }
                        n++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            if (ed != null && kutu.HasValue) Gorunume(ed, kutu.Value);
            return n;
        }

        private static void Gorunume(Editor ed, Extents3d e)
        {
            try
            {
                using (var view = ed.GetCurrentView())
                {
                    double w = Math.Max(1.0, e.MaxPoint.X - e.MinPoint.X), hgt = Math.Max(1.0, e.MaxPoint.Y - e.MinPoint.Y);
                    view.CenterPoint = new Point2d((e.MinPoint.X + e.MaxPoint.X) / 2.0, (e.MinPoint.Y + e.MaxPoint.Y) / 2.0);
                    view.Width = w * 1.05;
                    view.Height = hgt * 1.05;
                    ed.SetCurrentView(view);
                }
            }
            catch { }
        }
    }
}
