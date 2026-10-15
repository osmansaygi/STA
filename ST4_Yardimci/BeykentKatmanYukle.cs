using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// KATMANYUKLE: kalıp/temel ana seti + Yardımcı ekleri.
    /// Üstü çizilip dışarıda bırakılanlar: BOS, BIRLESIK KATMAN, döşeme GPR,
    /// donatı yazısı kalıp GPR, kiriş uzatma işaret, ALT DUZ ALAN, iskele,
    /// görünüş, metraj ve kolon düşey özel katmanları.
    /// Var olan katmanın renk / çizgi tipi / kalınlığı değişmez.
    /// </summary>
    internal static class BeykentKatmanYukle
    {
        private readonly struct Spec
        {
            public readonly string Name;
            public readonly short Aci;
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;
            public readonly bool TrueColor;
            public readonly LineWeight Weight;
            public readonly bool Dashed;
            public readonly byte? Transparency;

            public Spec(string name, short aci, LineWeight weight, bool dashed = false, byte? transparency = null)
            {
                Name = name;
                Aci = aci;
                R = G = B = 0;
                TrueColor = false;
                Weight = weight;
                Dashed = dashed;
                Transparency = transparency;
            }

            public Spec(string name, byte r, byte g, byte b, LineWeight weight)
            {
                Name = name;
                Aci = 0;
                R = r;
                G = g;
                B = b;
                TrueColor = true;
                Weight = weight;
                Dashed = false;
                Transparency = null;
            }
        }

        private static readonly Spec[] Layers =
        {
            new Spec("CIZGI (BEYKENT)", 1, LineWeight.LineWeight020),
            new Spec("AKS CIZGISI (BEYKENT)", 252, LineWeight.LineWeight020, dashed: true),
            new Spec("AKS BALONU (BEYKENT)", 7, LineWeight.LineWeight030),
            new Spec("AKS YAZISI (BEYKENT)", 3, LineWeight.LineWeight020),
            new Spec("AKS OLCU (BEYKENT)", 6, LineWeight.LineWeight018),
            new Spec("KIRIS (BEYKENT)", 2, LineWeight.LineWeight030),
            new Spec("KIRIS ISMI (BEYKENT)", 40, LineWeight.LineWeight020),
            new Spec("BANT (BEYKENT)", 23, LineWeight.LineWeight025, dashed: true),
            new Spec("MANTAR KIRIS ISMI (BEYKENT)", 30, LineWeight.LineWeight020),
            new Spec("KOLON (BEYKENT)", 3, LineWeight.LineWeight040),
            new Spec("KOLON ISMI (BEYKENT)", 91, LineWeight.LineWeight020),
            new Spec("PERDE (BEYKENT)", 6, LineWeight.LineWeight040),
            new Spec("PERDE ISMI (BEYKENT)", 240, LineWeight.LineWeight020),
            new Spec("TARAMA (BEYKENT)", 8, LineWeight.LineWeight015),
            new Spec("DOSEME SINIRI (BEYKENT)", 71, LineWeight.LineWeight030),
            new Spec("DOSEME ISMI (BEYKENT)", 9, LineWeight.LineWeight020),
            new Spec("DOSEME PAFTA (BEYKENT)", 3, LineWeight.LineWeight020),
            new Spec("DOSEME (BEYKENT)", 2, LineWeight.LineWeight030),
            new Spec("MERDIVEN (BEYKENT)", 5, LineWeight.LineWeight030),
            new Spec("MERDIVEN YAZI (BEYKENT)", 2, LineWeight.LineWeight020),
            new Spec("YAZI (BEYKENT)", 4, LineWeight.LineWeight020),
            new Spec("TEMEL (BEYKENT)", 2, LineWeight.LineWeight040),
            new Spec("TEMEL AMPATMAN (BEYKENT)", 21, LineWeight.LineWeight040),
            new Spec("TEMEL HATILI (BEYKENT)", 230, LineWeight.LineWeight030),
            new Spec("TEMEL HATILI ISMI (BEYKENT)", 243, LineWeight.LineWeight020),
            new Spec("TEMEL ISMI (BEYKENT)", 40, LineWeight.LineWeight020),
            new Spec("KAT SINIRI (BEYKENT)", 91, LineWeight.LineWeight025),
            new Spec("KALIP BOSLUK (BEYKENT)", 30, LineWeight.LineWeight025),
            new Spec("OLCU (BEYKENT)", 14, LineWeight.LineWeight020),
            new Spec("DONATI (BEYKENT)", 4, LineWeight.LineWeight040),
            new Spec("DONATI YAZISI (BEYKENT)", 3, LineWeight.LineWeight020),
            new Spec("DONATI HALKASI (BEYKENT)", 2, LineWeight.LineWeight030),
            new Spec("DONATI IZI (BEYKENT)", 8, LineWeight.LineWeight025),
            new Spec("DONATI OKU (BEYKENT)", 242, LineWeight.LineWeight020),
            new Spec("HASIR (BEYKENT)", 4, LineWeight.LineWeight035),
            new Spec("HASIR CIZGI (BEYKENT)", 1, LineWeight.LineWeight020),
            new Spec("HASIR YAZI (BEYKENT)", 3, LineWeight.LineWeight020),
            new Spec("HASIR YONU (BEYKENT)", 5, LineWeight.LineWeight025),
            new Spec("CIROZ (BEYKENT)", 140, LineWeight.LineWeight020),
            new Spec("YUK YAZISI (BEYKENT)", 140, LineWeight.LineWeight020),
            new Spec("KOT YAZI (BEYKENT)", 7, LineWeight.LineWeight020),
            new Spec("KOT CIZGISI (BEYKENT)", 7, LineWeight.LineWeight020),
            new Spec("KESIT (BEYKENT)", 151, LineWeight.LineWeight060),
            new Spec("KESIT CIZGISI (BEYKENT)", 3, LineWeight.LineWeight050),
            new Spec("KESIT ISMI (BEYKENT)", 6, LineWeight.LineWeight020),
            new Spec("KESIT SINIRI (BEYKENT)", 241, LineWeight.LineWeight020, dashed: true),
            new Spec("KESIT GORUNUS (BEYKENT)", 253, LineWeight.LineWeight020, dashed: true),
            new Spec("D.GORUNUS (BEYKENT)", 8, LineWeight.LineWeight020, dashed: true),
            new Spec("GROBETON (BEYKENT)", 70, LineWeight.LineWeight050),
            new Spec("ZEMIN (BEYKENT)", 43, LineWeight.LineWeight050),
            new Spec("BLOKAJ (BEYKENT)", 8, LineWeight.LineWeight015, transparency: 90),
            new Spec("ETIKET CIZGISI (BEYKENT)", 64, LineWeight.LineWeight015),
            new Spec("IC ANTET (BEYKENT)", 152, LineWeight.LineWeight030),
            new Spec("ANTET BASLIK 1 (BEYKENT)", 1, LineWeight.LineWeight020),
            new Spec("KAPAMA PERDESI (BEYKENT)", 5, LineWeight.LineWeight030),
            new Spec("KAPAMA PERDESI ISMI (BEYKENT)", 5, LineWeight.LineWeight020),
            new Spec("POZ NO (BEYKENT)", 2, LineWeight.LineWeight020),
            new Spec("POZ BALON (BEYKENT)", 4, LineWeight.LineWeight020),
            new Spec("TEMEL ILAVE X (BEYKENT)", 196, 72, 72, LineWeight.LineWeight020),
            new Spec("TEMEL ILAVE Y (BEYKENT)", 64, 112, 176, LineWeight.LineWeight020),
            new Spec("TEMEL ILAVE KUTU X (BEYKENT)", 255, 176, 176, LineWeight.LineWeight020),
            new Spec("TEMEL ILAVE KUTU Y (BEYKENT)", 176, 204, 236, LineWeight.LineWeight020),
        };

        public static void Run()
        {
            Editor ed = null;
            try
            {
                if (!MachineLicense.EnsureOrWarn()) return;
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return;
                try { ed = doc.Editor; } catch { }

                Database db;
                try { db = doc.Database; }
                catch
                {
                    ed?.WriteMessage("\nKATMANYUKLE: acik cizim yok. Once bir DWG acin.");
                    return;
                }
                if (db == null)
                {
                    ed?.WriteMessage("\nKATMANYUKLE: acik cizim yok. Once bir DWG acin.");
                    return;
                }

                Database previous = HostApplicationServices.WorkingDatabase;
                int created = 0;
                try
                {
                    HostApplicationServices.WorkingDatabase = db;
                    ObjectId dashedId = TryLoadDashed(db);
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        foreach (Spec spec in Layers)
                        {
                            if (lt.Has(spec.Name))
                            {
                                var existing = (LayerTableRecord)tr.GetObject(lt[spec.Name], OpenMode.ForRead);
                                if (existing.IsOff)
                                {
                                    try
                                    {
                                        existing.UpgradeOpen();
                                        existing.IsOff = false;
                                    }
                                    catch { }
                                }
                                continue;
                            }

                            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
                            var rec = new LayerTableRecord
                            {
                                Name = spec.Name,
                                LineWeight = spec.Weight,
                                Color = spec.TrueColor
                                    ? Color.FromRgb(spec.R, spec.G, spec.B)
                                    : Color.FromColorIndex(ColorMethod.ByAci, spec.Aci)
                            };
                            if (spec.Dashed && !dashedId.IsNull)
                                rec.LinetypeObjectId = dashedId;
                            lt.Add(rec);
                            tr.AddNewlyCreatedDBObject(rec, true);
                            if (spec.Transparency.HasValue)
                                rec.Transparency = new Transparency(spec.Transparency.Value);
                            try { rec.IsOff = false; } catch { }
                            created++;
                        }
                        tr.Commit();
                    }
                }
                finally
                {
                    try { HostApplicationServices.WorkingDatabase = previous; } catch { }
                }

                ed?.WriteMessage(
                    "\nKATMANYUKLE: {0} katman hazir, {1} yeni.",
                    Layers.Length, created);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed?.WriteMessage("\nKATMANYUKLE: {0}", ex.ErrorStatus);
            }
            catch (Exception ex)
            {
                ed?.WriteMessage("\nKATMANYUKLE: {0}", ex.Message);
            }
        }

        /// <summary>Çizgi tipi dosyası işlem açıkken yüklenirse AutoCAD eNoDatabase verir.</summary>
        private static ObjectId TryLoadDashed(Database db)
        {
            ObjectId existing = FindDashed(db);
            if (!existing.IsNull) return existing;

            string[] names = { "DASHED", "Dashed" };
            string[] files = { "acad.lin", "acadiso.lin" };
            foreach (string name in names)
            {
                foreach (string file in files)
                {
                    try
                    {
                        db.LoadLineTypeFile(name, file);
                        ObjectId id = FindDashed(db);
                        if (!id.IsNull) return id;
                    }
                    catch { }
                }
            }
            return ObjectId.Null;
        }

        private static ObjectId FindDashed(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                ObjectId id = ObjectId.Null;
                if (ltt.Has("DASHED")) id = ltt["DASHED"];
                else if (ltt.Has("Dashed")) id = ltt["Dashed"];
                tr.Commit();
                return id;
            }
        }
    }
}
