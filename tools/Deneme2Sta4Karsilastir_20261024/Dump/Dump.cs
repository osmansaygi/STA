using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

// Açık DWG'nin model uzayını (blok içleri dünya koordinatında) DD_OUT dosyasına döker.
public class DwgDump
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static string R(double v) => v.ToString("0.#####", CI);
    static string P(Point3d p) => R(p.X) + "," + R(p.Y);

    [CommandMethod("DD_DUMP")]
    public static void Run()
    {
        string outp = Environment.GetEnvironmentVariable("DD_OUT");
        var lines = new List<string>();
        try
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                Walk(tr, ms, Matrix3d.Identity, "", lines, 0);
                tr.Commit();
            }
        }
        catch (System.Exception ex) { lines.Add("FAIL " + ex); }
        lines.Add("COMPLETE");
        File.WriteAllLines(outp, lines);
    }

    static void Walk(Transaction tr, BlockTableRecord btr, Matrix3d m, string blk, List<string> o, int depth)
    {
        foreach (ObjectId id in btr)
        {
            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (e == null) continue;
            string h = e.GetType().Name + "|" + e.Layer + "|" + e.ColorIndex + "|" + blk + "|";
            switch (e)
            {
                case BlockReference br:
                    var bb = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    o.Add(h + bb.Name + " @" + P(br.Position.TransformBy(m)));
                    if (depth < 4) Walk(tr, bb, br.BlockTransform.PreMultiplyBy(m), blk + "/" + bb.Name, o, depth + 1);
                    foreach (ObjectId aid in br.AttributeCollection)
                        if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                            o.Add("Attr|" + ar.Layer + "|" + ar.ColorIndex + "|" + blk + "|" + ar.TextString + " @" + P(ar.Position.TransformBy(m)));
                    continue;
                case Line l: h += P(l.StartPoint.TransformBy(m)) + " " + P(l.EndPoint.TransformBy(m)); break;
                case Polyline pl:
                    h += (pl.Closed ? "C " : "O ") + "w" + R(pl.ConstantWidth) + " ";
                    for (int v = 0; v < pl.NumberOfVertices; v++) h += P(pl.GetPoint3dAt(v).TransformBy(m)) + "/" + Math.Round(pl.GetBulgeAt(v), 3).ToString(CI) + " ";
                    break;
                case Polyline2d p2:
                    h += "2D ";
                    foreach (ObjectId vid in p2) if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d vx) h += P(vx.Position.TransformBy(m)) + " ";
                    break;
                case Circle c: h += P(c.Center.TransformBy(m)) + " r" + R(c.Radius); break;
                case Arc a: h += P(a.Center.TransformBy(m)) + " r" + R(a.Radius) + " " + Math.Round(a.StartAngle, 3).ToString(CI) + "-" + Math.Round(a.EndAngle, 3).ToString(CI); break;
                case DBText t: h += t.TextString + " @" + P(t.Position.TransformBy(m)) + " h" + R(t.Height) + " r" + Math.Round(t.Rotation * 180 / Math.PI).ToString(CI); break;
                case MText mt: h += mt.Contents + " @" + P(mt.Location.TransformBy(m)) + " r" + Math.Round(mt.Rotation * 180 / Math.PI).ToString(CI); break;
                case Dimension d: double me = 0; try { me = d.Measurement; } catch { } h += Math.Round(me, 1).ToString(CI) + " @" + P(d.TextPosition.TransformBy(m)); break;
                default: try { var ex = e.GeometricExtents; h += P(ex.MinPoint.TransformBy(m)) + " " + P(ex.MaxPoint.TransformBy(m)); } catch { } break;
            }
            o.Add(h);
        }
    }
}
