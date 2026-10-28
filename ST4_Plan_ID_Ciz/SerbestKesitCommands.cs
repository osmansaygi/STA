using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using ST4AksCizCSharp;
using AcRxException = Autodesk.AutoCAD.Runtime.Exception;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ST4PlanIdCiz.SerbestKesitCommands))]

namespace ST4PlanIdCiz
{
    /// <summary>
    /// SERBESTKESIT — çizili herhangi bir planda (ST4PLANID, KALIP50/100, TEMEL…) iki nokta veya bir çizgi ile
    /// kesit hattı, bir nokta ile bakış yönü; o hattan 1/200 şematik bina kesiti ve plana kesit işareti.
    /// </summary>
    public sealed class SerbestKesitCommands
    {
        private static string _sonHarf = "A";

        [CommandMethod("SERBESTKESIT")]
        public void SerbestKesit()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                Application.SetSystemVariable("GRIDMODE", (short)0);
                Application.SetSystemVariable("LTSCALE", 10.0);
                BeykentCizgiLayer.Ensure(doc.Database);
            }
            catch { /* sürüm / bağlam */ }

            var ed = doc.Editor;
            var db = doc.Database;

            if (!RememberedFilePrompt.TryPrompt(ed, "\nSERBESTKESIT icin ST4 Dosyasi Secin (plan bu projeden cizilmis olmali)",
                "ST4 Dosyalari (*.st4)|*.st4|Tum Dosyalar (*.*)|*.*", RememberedFilePrompt.KindSt4, out string st4Path))
                return;

            ed.WriteMessage("\nSERBESTKESIT: Cizili planin uzerinde kesit hattini binanin disindan disina gosterin (OSNAP/ORTHO kullanilabilir).");
            if (!TryHatAl(ed, out Point3d a, out Point3d b)) return;

            var yonOpt = new PromptPointOptions("\nBakis yonu (kesitin bakacagi tarafa tiklayin) <hattin solu>: ")
            {
                AllowNone = true,
                UseBasePoint = true,
                BasePoint = new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0)
            };
            var yonRes = ed.GetPoint(yonOpt);
            if (yonRes.Status == PromptStatus.OK)
            {
                // Bakış hattın sol normali: taraf sağdaysa uçları değiştir.
                double cross = (b.X - a.X) * (yonRes.Value.Y - a.Y) - (b.Y - a.Y) * (yonRes.Value.X - a.X);
                if (cross < 0) { var t = a; a = b; b = t; }
            }
            else if (yonRes.Status != PromptStatus.None) return;

            var harfOpt = new PromptStringOptions("\nKesit harfi <" + _sonHarf + ">: ")
            {
                AllowSpaces = false,
                DefaultValue = _sonHarf,
                UseDefaultValue = true
            };
            var harfRes = ed.GetString(harfOpt);
            if (harfRes.Status != PromptStatus.OK && harfRes.Status != PromptStatus.None) return;
            string harf = string.IsNullOrWhiteSpace(harfRes.StringResult) ? _sonHarf : harfRes.StringResult.Trim().ToUpperInvariant();

            try
            {
                NtsGeometryServices.Instance = new NtsGeometryServices(
                    CoordinateArraySequenceFactory.Instance,
                    new PrecisionModel(),
                    0,
                    GeometryOverlay.NG,
                    new CoordinateEqualityComparer());
                var model = new St4Parser().Parse(st4Path);
                GprYapiAksLabels.TryMergeFromGprBesideSt4(st4Path, model);
                var manager = new PlanIdDrawingManager(model);

                if (manager.TryResolveSerbestKesitPlanOffset(db, a, b, out Vector2d offset, out int nx, out int ny))
                {
                    ed.WriteMessage("\nSERBESTKESIT: Plan akslardan taniniyor ({0} X + {1} Y aksi eslesti).", nx, ny);
                }
                else
                {
                    if (!manager.TryGetSerbestKesitReferansAks(out string xAd, out string yAd, out Point2d refModel))
                    {
                        ed.WriteMessage("\nSERBESTKESIT: Plan taninamadi ve referans aks bulunamadi.");
                        return;
                    }
                    var refRes = ed.GetPoint(new PromptPointOptions(string.Format(
                        "\nPlan akslardan taninamadi. Ayni planda {0}-{1} aks kesisimini gosterin: ", xAd, yAd)) { AllowNone = false });
                    if (refRes.Status != PromptStatus.OK) return;
                    offset = new Vector2d(refRes.Value.X - refModel.X, refRes.Value.Y - refModel.Y);
                }

                var insRes = ed.GetPoint(new PromptPointOptions("\nKesitin yerlestirme noktasi (sol-alt): ") { AllowNone = false });
                if (insRes.Status != PromptStatus.OK) return;

                if (manager.DrawSerbestKesit(db, ed, a, b, offset, insRes.Value, harf))
                    _sonHarf = SonrakiHarf(harf);
            }
            catch (System.Exception ex)
            {
                if (ex is AcRxException aex)
                    ed.WriteMessage("\nSERBESTKESIT hata: {0} ({1})", aex.Message, aex.ErrorStatus);
                else
                    ed.WriteMessage("\nSERBESTKESIT hata: {0}", ex.Message);
            }
        }

        /// <summary>İki nokta, ya da [Nesne] ile çizilmiş bir çizgi/polyline (ilk ve son köşe).</summary>
        private static bool TryHatAl(Editor ed, out Point3d a, out Point3d b)
        {
            a = b = Point3d.Origin;
            var p1Opt = new PromptPointOptions("\nKesit hatti 1. nokta veya ")
            {
                AllowNone = false,
                AppendKeywordsToMessage = true
            };
            p1Opt.Keywords.Add("Nesne");
            var p1 = ed.GetPoint(p1Opt);
            if (p1.Status == PromptStatus.Keyword)
            {
                var eo = new PromptEntityOptions("\nKesit hatti olarak cizgi veya polyline secin: ");
                eo.SetRejectMessage("\nYalniz Line veya Polyline.");
                eo.AddAllowedClass(typeof(Line), true);
                eo.AddAllowedClass(typeof(Polyline), true);
                var er = ed.GetEntity(eo);
                if (er.Status != PromptStatus.OK) return false;
                using (var tr = er.ObjectId.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(er.ObjectId, OpenMode.ForRead);
                    if (ent is Line ln) { a = ln.StartPoint; b = ln.EndPoint; }
                    else if (ent is Polyline pl && pl.NumberOfVertices >= 2)
                    {
                        a = pl.GetPoint3dAt(0);
                        b = pl.GetPoint3dAt(pl.NumberOfVertices - 1);
                        if (pl.NumberOfVertices > 2)
                            ed.WriteMessage("\nSERBESTKESIT: Kirik kesit desteklenmiyor; polyline'in ilk ve son noktasi kullanildi.");
                    }
                    tr.Commit();
                }
            }
            else
            {
                if (p1.Status != PromptStatus.OK) return false;
                a = p1.Value;
                var p2 = ed.GetPoint(new PromptPointOptions("\nKesit hatti 2. nokta: ") { AllowNone = false, UseBasePoint = true, BasePoint = a });
                if (p2.Status != PromptStatus.OK) return false;
                b = p2.Value;
            }
            a = new Point3d(a.X, a.Y, 0);
            b = new Point3d(b.X, b.Y, 0);
            if (a.DistanceTo(b) < 1.0)
            {
                ed.WriteMessage("\nSERBESTKESIT: Iki farkli nokta gerekli.");
                return false;
            }
            return true;
        }

        private static string SonrakiHarf(string harf)
        {
            if (harf.Length == 1 && harf[0] >= 'A' && harf[0] < 'Z') return ((char)(harf[0] + 1)).ToString();
            return harf;
        }
    }
}
