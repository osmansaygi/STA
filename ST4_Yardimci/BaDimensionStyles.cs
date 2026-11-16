using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using BeykentOrtak;

namespace ST4Yardimci
{
    /// <summary>
    /// ST4 ID ölçü aileleri; her komut hem stili hem ölçü üzerindeki ayarları buradan alır.
    /// Değerler KATMANYUKLE formundaki ölçü stili ayarlarından (Ortak\BeykentStil) gelir; var olan stil her çağrıda ayara çekilir.
    /// </summary>
    internal static class BaDimensionStyles
    {
        private static readonly ConditionalWeakTable<Transaction, Dictionary<string, ObjectId>> Cache
            = new ConditionalWeakTable<Transaction, Dictionary<string, ObjectId>>();

        /// <summary>Eski adları (PLAN_OLCU, PLAN_OLCU_25 …) yeni şemaya çevirir; bkz. <see cref="OlcuStilAdlari"/>.</summary>
        private static string Key(string name) => OlcuStilAdlari.Yeni(name).ToUpperInvariant();

        internal static ObjectId Ensure(Transaction tr, Database db, string name)
        {
            string key = Key(name);
            if (key.Length == 0) key = OlcuStilAdlari.Plan50;
            OlcuAyar ayar = BeykentStilAyar.Olcu(key);
            var cache = Cache.GetOrCreateValue(tr);
            if (cache.TryGetValue(key, out ObjectId cached) && cached.IsValid && !cached.IsErased) return cached;
            OlcuStilAdlari.CizimiTasi(tr, db);
            var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            // Formdan seçilen özel stiller kendi ayarlarıyla kullanılır.
            if (ayar == null) return table.Has(key) ? table[key] : ObjectId.Null;
            EnsureYazi(tr, db);
            ObjectId id = BeykentStilUygula.OlcuStiliYaz(tr, db, ayar);
            cache[key] = id;
            return id;
        }

        internal static void Apply(Transaction tr, Database db, Dimension dim, ObjectId styleId, string fallback = OlcuStilAdlari.Plan50)
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

        internal static void EnsureStandards(Transaction tr, Database db)
        {
            EnsureYazi(tr, db);
            foreach (string name in new[]
            {
                OlcuStilAdlari.Plan50, OlcuStilAdlari.Ilave50, OlcuStilAdlari.Aks50, OlcuStilAdlari.Plan100, OlcuStilAdlari.Aks100,
                OlcuStilAdlari.Donati50, OlcuStilAdlari.Detay50_25, OlcuStilAdlari.Donati50_25,
                OlcuStilAdlari.DonatiOku50
            })
                Ensure(tr, db, name);
        }

        /// <summary>YAZI (BEYKENT): ayardaki font (varsayılan Bahnschrift Light Condensed). Var olan stil de ayara çekilir.</summary>
        internal static ObjectId EnsureYazi(Transaction tr, Database db)
            => BeykentStilUygula.YaziStili(tr, db, BeykentStilAyar.YaziBeykent, true);
    }
}
