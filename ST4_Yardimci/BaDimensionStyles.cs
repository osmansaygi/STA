using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;

namespace ST4Yardimci
{
    /// <summary>ST4 ID ölçü aileleri; her komut hem stili hem ölçü üzerindeki ayarları buradan alır.</summary>
    internal static class BaDimensionStyles
    {
        internal sealed class Profile
        {
            public string Name;
            public double TextHeight, Factor = 1, Arrow = 3, Offset = 3, Gap = 2;
            public bool FixedExtension, RebarArrow;
            public double Extension => RebarArrow ? 5 : FixedExtension ? 5 : TextHeight / 2;
        }

        private static readonly ConditionalWeakTable<Transaction, Dictionary<string, ObjectId>> Cache
            = new ConditionalWeakTable<Transaction, Dictionary<string, ObjectId>>();

        internal static Profile GetProfile(string name)
        {
            string key = (name ?? "").Trim().ToUpperInvariant();
            if (key == "PLAN_OLCU_25") key = "PLAN_OLCU 25";
            if (key == "PLAN_OLCU_DONATI_25") key = "PLAN_OLCU_DONATI 25";
            switch (key)
            {
                case "PLAN_OLCU": case "AKS_OLCU":
                    return new Profile { Name = key, TextHeight = 12 };
                case "PLAN_OLCU_100": case "AKS_OLCU_100":
                    return new Profile { Name = key, TextHeight = 18, Arrow = 4.5, Offset = 4.5, Gap = 3 };
                case "PLAN_OLCU_DONATI":
                    return new Profile { Name = key, TextHeight = 10 };
                case "PLAN_OLCU 25":
                    return new Profile { Name = key, TextHeight = 12, Factor = 0.5, FixedExtension = true };
                case "PLAN_OLCU_DONATI 25":
                    return new Profile { Name = key, TextHeight = 10, Factor = 0.5 };
                case "DONATI OKU (BEYKENT)":
                    return new Profile { Name = key, TextHeight = 10, Arrow = 10, Offset = 5, RebarArrow = true };
                default: return null; // Formdan seçilen özel stiller kendi ayarlarıyla kullanılır.
            }
        }

        internal static ObjectId Ensure(Transaction tr, Database db, string name)
        {
            Profile profile = GetProfile(name);
            string key = profile?.Name ?? (name ?? "").Trim();
            if (key.Length == 0) key = "PLAN_OLCU";
            if (profile == null && key == "PLAN_OLCU") profile = GetProfile(key);
            var cache = Cache.GetOrCreateValue(tr);
            if (cache.TryGetValue(key, out ObjectId cached) && cached.IsValid && !cached.IsErased) return cached;
            var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (profile == null) return table.Has(key) ? table[key] : ObjectId.Null;
            ObjectId textStyle = EnsureTextStyle(tr, db);
            DimStyleTableRecord record;
            ObjectId id;
            if (table.Has(key))
            {
                id = table[key];
                record = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
            }
            else
            {
                if (!table.IsWriteEnabled) table.UpgradeOpen();
                record = new DimStyleTableRecord { Name = key };
                id = table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            Configure(record, profile, textStyle, db.ContinuousLinetype);
            cache[key] = id;
            return id;
        }

        internal static void Configure(DimStyleTableRecord record, Profile profile, ObjectId textStyle, ObjectId continuous)
        {
            record.Dimtxsty = textStyle;
            record.Dimscale = 1;
            record.Dimlfac = profile.Factor;
            record.Dimtxt = profile.TextHeight;
            record.Dimblk = record.Dimblk1 = record.Dimblk2 = record.Dimldrblk = ObjectId.Null;
            record.Dimsah = false;
            record.Dimasz = profile.Arrow;
            record.Dimtsz = profile.RebarArrow ? 0 : profile.Arrow;
            record.Dimexo = profile.Offset;
            record.Dimexe = profile.Extension;
            record.Dimfxlen = profile.Extension;
            record.DimfxlenOn = profile.FixedExtension;
            record.Dimse1 = record.Dimse2 = profile.RebarArrow;
            record.Dimsd1 = record.Dimsd2 = false;
            record.Dimdle = 0;
            record.Dimgap = profile.Gap;
            record.Dimtad = (short)(profile.RebarArrow ? 0 : 1);
            record.Dimjust = 0;
            record.Dimtih = record.Dimtoh = false;
            record.Dimtofl = record.Dimtix = true;
            record.Dimsoxd = false;
            record.Dimatfit = 3;
            record.Dimtmove = 0;
            record.Dimrnd = profile.RebarArrow ? 0 : 0.5;
            record.Dimdec = record.Dimtdec = (short)(profile.RebarArrow ? 0 : 1);
            record.Dimzin = 12;
            record.Dimlunit = 2;
            record.Dimaunit = record.Dimadec = 0;
            record.Dimalt = record.Dimtol = record.Dimlim = false;
            record.Dimpost = record.Dimapost = "";
            record.Dimcen = 0;
            record.Dimclrd = record.Dimclre = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            record.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, 7);
            record.Dimlwd = record.Dimlwe = LineWeight.ByLayer;
            record.Dimltype = record.Dimltex1 = record.Dimltex2 = continuous;
            record.Dimtfill = 0;
            record.Annotative = AnnotativeStates.False;
        }

        internal static void Apply(Transaction tr, Database db, Dimension dim, ObjectId styleId, string fallback = "PLAN_OLCU")
        {
            if (dim == null) return;
            DimStyleTableRecord record = null;
            if (!styleId.IsNull && styleId.IsValid)
                record = tr.GetObject(styleId, OpenMode.ForRead) as DimStyleTableRecord;
            styleId = record == null ? Ensure(tr, db, fallback) : Ensure(tr, db, record.Name);
            if (styleId.IsNull) styleId = Ensure(tr, db, fallback);
            record = (DimStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead);
            dim.DimensionStyle = styleId;
            // DWG şablonundaki / aktif DIM ayarlarındaki eski entity override'larını da temizler.
            dim.SetDimstyleData(record);
            dim.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            dim.LineWeight = LineWeight.ByLayer;
            dim.Annotative = record.Annotative;
            if (!dim.ObjectId.IsNull) dim.RecomputeDimensionBlock(true);
        }

        private static ObjectId EnsureTextStyle(Transaction tr, Database db)
        {
            const string name = "YAZI (BEYKENT)";
            var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            TextStyleTableRecord record;
            ObjectId id;
            if (table.Has(name))
            {
                id = table[name];
                record = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
            }
            else
            {
                record = new TextStyleTableRecord { Name = name };
                if (!table.IsWriteEnabled) table.UpgradeOpen();
                id = table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            record.TextSize = 0;
            record.XScale = 1;
            try { record.Font = new FontDescriptor("Bahnschrift Light Condensed", false, false, 0, 0); }
            catch { record.Font = new FontDescriptor("Arial", false, false, 0, 0); }
            return id;
        }
    }
}
