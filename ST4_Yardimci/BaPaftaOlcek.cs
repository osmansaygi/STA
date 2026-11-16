using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// 1/25 detay komutlarının pafta ölçeği (1/50 veya 1/100) — %LocalAppData%\ST4_Yardimci\pafta_olcek.ini, komut başına.
    /// 1/100 seçiliyse komutun çizdiği detay (yazı/ölçü dahil) ×2 büyütülür.
    /// </summary>
    internal static class BaPaftaOlcek
    {
        private const string Ini = "pafta_olcek.ini";

        internal static bool Yuz(string anahtar) => BaIni.GetB(BaIni.Read(Ini), anahtar, false);

        internal static void Kaydet(string anahtar, bool yuz)
        {
            var map = BaIni.Read(Ini);
            map[anahtar] = BaIni.B(yuz);
            BaIni.Write(Ini, map.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        }

        /// <summary>Form seçeneği; değişince hemen kaydedilir (palet formları açık kalır).</summary>
        internal static RadioButton[] Secenekler(string anahtar)
        {
            RadioButton[] rb = BeykentOrtak.PaftaOlcek.Secenekler(Yuz(anahtar));
            rb[1].CheckedChanged += (s, e) => Kaydet(anahtar, rb[1].Checked);
            return rb;
        }

        /// <summary>Komut sonunda: 1/100 seçiliyse <paramref name="ilkHandle"/> sonrası çizilenleri köşesi sabit ×2 büyütür.</summary>
        internal static void Uygula(string anahtar, Document doc, long ilkHandle, bool ustKose)
        {
            if (doc == null || !Yuz(anahtar)) return;
            try
            {
                using (doc.LockDocument())
                {
                    int n = BeykentOrtak.PaftaOlcek.BuyutYeniKose(doc.Database, ilkHandle, ustKose);
                    if (n > 0) doc.Editor.WriteMessage("\n1/100 pafta: detay ×2 büyütüldü ({0} nesne).", n);
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n1/100 pafta büyütme yapılamadı: {0}", ex.Message); }
        }

        /// <summary>
        /// <see cref="Uygula(string, Document, long, bool)"/> gibi; <paramref name="haric"/> nesneleri (ör. plana konan kesit
        /// sembolleri) büyütülmez ve büyütme köşesinin hesabına girmez.
        /// </summary>
        internal static void Uygula(string anahtar, Document doc, long ilkHandle, bool ustKose, System.Collections.Generic.ICollection<Autodesk.AutoCAD.DatabaseServices.ObjectId> haric)
        {
            if (haric == null || haric.Count == 0) { Uygula(anahtar, doc, ilkHandle, ustKose); return; }
            if (doc == null || !Yuz(anahtar)) return;
            try
            {
                var db = doc.Database;
                var dislanan = new System.Collections.Generic.HashSet<Autodesk.AutoCAD.DatabaseServices.ObjectId>(haric);
                var ids = new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
                Autodesk.AutoCAD.DatabaseServices.Extents3d? kutu = null;
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(
                            Autodesk.AutoCAD.DatabaseServices.SymbolUtilityServices.GetBlockModelSpaceId(db),
                            Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)
                        {
                            if (id.IsNull || id.IsErased || id.Handle.Value < ilkHandle || dislanan.Contains(id)) continue;
                            ids.Add(id);
                            try
                            {
                                var e = ((Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead)).GeometricExtents;
                                if (kutu.HasValue) { var k = kutu.Value; k.AddExtents(e); kutu = k; } else kutu = e;
                            }
                            catch { }
                        }
                        tr.Commit();
                    }
                    if (ids.Count == 0 || !kutu.HasValue) return;
                    var baz = new Autodesk.AutoCAD.Geometry.Point3d(kutu.Value.MinPoint.X, ustKose ? kutu.Value.MaxPoint.Y : kutu.Value.MinPoint.Y, 0);
                    int n = BeykentOrtak.PaftaOlcek.Buyut(db, ids, baz);
                    if (n > 0) doc.Editor.WriteMessage("\n1/100 pafta: detay ×2 büyütüldü ({0} nesne; plandaki {1} sembol nesnesi yerinde).", n, dislanan.Count);
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n1/100 pafta büyütme yapılamadı: {0}", ex.Message); }
        }

        /// <summary>1/100 seçiliyse <paramref name="ilkHandle"/> sonrası çizilenleri verilen nokta sabit ×2 büyütür (tablo komutları).</summary>
        internal static void UygulaNokta(string anahtar, Document doc, long ilkHandle, Autodesk.AutoCAD.Geometry.Point3d baz)
        {
            if (doc == null || !Yuz(anahtar)) return;
            try
            {
                var db = doc.Database;
                var ids = new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tr.GetObject(
                            Autodesk.AutoCAD.DatabaseServices.SymbolUtilityServices.GetBlockModelSpaceId(db),
                            Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in ms)
                            if (!id.IsNull && !id.IsErased && id.Handle.Value >= ilkHandle) ids.Add(id);
                        tr.Commit();
                    }
                    int n = BeykentOrtak.PaftaOlcek.Buyut(db, ids, baz);
                    if (n > 0) doc.Editor.WriteMessage("\n1/100 pafta: tablo ×2 büyütüldü ({0} nesne).", n);
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n1/100 pafta büyütme yapılamadı: {0}", ex.Message); }
        }

        internal static long Baslangic() => AcApp.DocumentManager.MdiActiveDocument?.Database.Handseed.Value ?? long.MaxValue;
    }
}
