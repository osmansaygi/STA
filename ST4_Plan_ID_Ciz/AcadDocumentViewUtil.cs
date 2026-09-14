using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Komut bitiminde <c>SendStringToExecute("ZOOM"/"REGEN")</c> komut kuyruğunu bozar;
    /// sonraki MOVE/COPY gibi büyük işlemlerde AutoCAD 2023 fatal error üretebiliyordu.
    /// </summary>
    internal static class AcadDocumentViewUtil
    {
        public static void ZoomExtentsWithoutNestedCommand(Document doc)
        {
            if (doc == null) return;
            try
            {
                Editor ed = doc.Editor;
                Database db = doc.Database;
                db.UpdateExt(true);
                Point3d min = db.Extmin;
                Point3d max = db.Extmax;
                if (min.X > max.X || min.Y > max.Y) return;
                double w = max.X - min.X;
                double h = max.Y - min.Y;
                if (w < 1e-6) w = 1.0;
                if (h < 1e-6) h = 1.0;
                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    view.CenterPoint = new Point2d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5);
                    view.Width = w * 1.08;
                    view.Height = h * 1.08;
                    ed.SetCurrentView(view);
                }
            }
            catch
            {
                /* Regen/ZOOM komutu kuyrugu MOVE/COPY fatal error uretebiliyor; sessizce gec. */
            }
        }

        public static void RegenWithoutNestedCommand(Document doc)
        {
            if (doc == null) return;
            try { doc.Editor.UpdateScreen(); } catch { }
        }
    }
}
