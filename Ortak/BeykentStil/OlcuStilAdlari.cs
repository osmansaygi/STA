using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace BeykentOrtak
{
    /// <summary>
    /// Ölçü stili adları: <c>OLCU &lt;AMAÇ&gt; &lt;PAFTA&gt;[-&lt;DETAY&gt;] (BEYKENT)</c>.
    /// PAFTA = paftanın çizim ölçeği (50/100), DETAY = detayın ölçeği; DIMLFAC = DETAY / PAFTA.
    /// Eski adlar (PLAN_OLCU, AKS_OLCU, PLAN_OLCU_25 …) <see cref="Yeni"/> ile yeni ada çevrilir; çizimdeki eski stiller
    /// <see cref="CizimiTasi"/> ile (yeni ad yoksa) yeniden adlandırılır, ölçüler aynı stil kaydında kalır.
    /// </summary>
    internal static class OlcuStilAdlari
    {
        internal const string Ek = " (BEYKENT)";

        // 1/50 plan ve detaylar (×1)
        internal const string Aks50 = "OLCU AKS 50" + Ek;
        internal const string Plan50 = "OLCU PLAN 50" + Ek;
        internal const string Donati50 = "OLCU DONATI 50" + Ek;
        internal const string Etriye50 = "OLCU ETRIYE 50" + Ek;
        internal const string Ilave50 = "OLCU ILAVE 50" + Ek;
        internal const string DonatiOku50 = "OLCU DONATI OKU 50" + Ek;

        // 1/100 plan (yazı/ok ×1.5)
        internal const string Aks100 = "OLCU AKS 100" + Ek;
        internal const string Plan100 = "OLCU PLAN 100" + Ek;
        internal const string Ilave100 = "OLCU ILAVE 100" + Ek;
        internal const string DonatiOku100 = "OLCU DONATI OKU 100" + Ek;

        // 1/50 pafta, 1/25 detay (×2, DIMLFAC 0.5)
        internal const string Acilim50_25 = "OLCU ACILIM 50-25" + Ek;
        internal const string Detay50_25 = "OLCU DETAY 50-25" + Ek;
        internal const string Donati50_25 = "OLCU DONATI 50-25" + Ek;
        internal const string Etriye50_25 = "OLCU ETRIYE 50-25" + Ek;

        // 1/100 pafta, 1/50 detay (×2, DIMLFAC 0.5, DIMSCALE 2)
        internal const string Donati100_50 = "OLCU DONATI 100-50" + Ek;
        internal const string Etriye100_50 = "OLCU ETRIYE 100-50" + Ek;

        // 1/100 pafta, 1/25 detay (×4, DIMLFAC 0.25, DIMSCALE 2)
        internal const string Acilim100_25 = "OLCU ACILIM 100-25" + Ek;
        internal const string Detay100_25 = "OLCU DETAY 100-25" + Ek;
        internal const string Donati100_25 = "OLCU DONATI 100-25" + Ek;
        internal const string Etriye100_25 = "OLCU ETRIYE 100-25" + Ek;

        // 1/50 pafta, 1/100 detay (×0.5, DIMLFAC 2): iskele dikme/yatay detayları
        internal const string Iskele50_100 = "OLCU ISKELE 50-100" + Ek;

        /// <summary>Etriye ölçüsü yazı stili (genişlik 0.7).</summary>
        internal const string YaziEtriye = "YAZI ETRIYE" + Ek;
        internal const string EskiYaziEtriye = "ETRIYE_OLCU_YAZI";

        /// <summary>Eski ölçü stili adı → yeni ad (büyük harf anahtar).</summary>
        internal static readonly Dictionary<string, string> EskiAdlar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AKS_OLCU", Aks50 },
            { "AKS_OLCU_100", Aks100 },
            { "PLAN_OLCU", Plan50 },
            { "PLAN_OLCU_100", Plan100 },
            { "PLAN_OLCU_DONATI", Donati50 },
            { "ETRIYE_OLCU", Etriye50 },
            { "ILAVE_OLCU", Ilave50 },
            { "DONATI OKU (BEYKENT)", DonatiOku50 },
            { "PLAN_OLCU_25", Acilim50_25 },
            { "PLAN_OLCU 25", Detay50_25 },
            { "PLAN_OLCU_DONATI 25", Donati50_25 },
            { "PLAN_OLCU_DONATI_25", Donati50_25 },
            { "ETRIYE_OLCU 25", Etriye50_25 },
            { "AKS_OLCU 25", "OLCU AKS 50-25" + Ek },
            { "PLAN_OLCU_DETAY", Iskele50_100 },
        };

        /// <summary>Eski adı yeni ada çevirir; bilinmeyen ad olduğu gibi (kırpılmış) döner.</summary>
        internal static string Yeni(string ad)
        {
            string a = (ad ?? "").Trim();
            if (a.Length == 0) return a;
            return EskiAdlar.TryGetValue(a, out string y) ? y : a;
        }

        /// <summary>Eski yazı stili adını yeni ada çevirir.</summary>
        internal static string YeniYazi(string ad)
        {
            string a = (ad ?? "").Trim();
            return string.Equals(a, EskiYaziEtriye, StringComparison.OrdinalIgnoreCase) ? YaziEtriye : a;
        }

        /// <summary>
        /// "OLCU X 50 (BEYKENT)" → "OLCU X 50-25 (BEYKENT)". Yeni şemada olmayan adlara eskisi gibi " 25" eklenir.
        /// </summary>
        internal static string Detay25(string ad)
        {
            string a = Yeni(ad);
            if (Coz(a, out string amac, out string pafta, out string detay))
            {
                if (detay != null) return a;
                return "OLCU " + amac + " " + pafta + "-25" + Ek;
            }
            if (a.EndsWith(" 25", StringComparison.Ordinal) || a.EndsWith("_25", StringComparison.Ordinal)) return a;
            return a + " 25";
        }

        /// <summary>1/50 paftadaki 1/25 detay stili mi (DIMLFAC 0.5, DIMSCALE 1)? Eski " 25"/"_25" adlar da sayılır.</summary>
        internal static bool Pafta50Detay25(string ad)
        {
            string a = (ad ?? "").Trim();
            if (Coz(a, out _, out string pafta, out string detay)) return pafta == "50" && detay == "25";
            return a.EndsWith(" 25", StringComparison.Ordinal) || a.EndsWith("_25", StringComparison.Ordinal);
        }

        /// <summary>
        /// 1/100 paftaya ×2 büyütülen detayın stili: "OLCU X 50-25" → "OLCU X 100-25", "OLCU X 50" → "OLCU X 100-50".
        /// Şemaya uymayan ya da zaten 1/100 olan adlarda null.
        /// </summary>
        internal static string Pafta100(string ad)
        {
            string a = Yeni(ad);
            if (!Coz(a, out string amac, out string pafta, out string detay) || pafta != "50") return null;
            if (detay == null) return "OLCU " + amac + " 100-50" + Ek;
            if (detay == "100") return null;
            return "OLCU " + amac + " 100-" + detay + Ek;
        }

        /// <summary>"OLCU &lt;AMAÇ&gt; &lt;PAFTA&gt;[-&lt;DETAY&gt;] (BEYKENT)" ayrıştırır.</summary>
        private static bool Coz(string ad, out string amac, out string pafta, out string detay)
        {
            amac = pafta = detay = null;
            if (string.IsNullOrEmpty(ad) || !ad.StartsWith("OLCU ", StringComparison.OrdinalIgnoreCase)
                || !ad.EndsWith(Ek, StringComparison.OrdinalIgnoreCase)) return false;
            string ic = ad.Substring(5, ad.Length - 5 - Ek.Length).Trim();
            int sp = ic.LastIndexOf(' ');
            if (sp <= 0) return false;
            string olcek = ic.Substring(sp + 1);
            amac = ic.Substring(0, sp).Trim();
            int tire = olcek.IndexOf('-');
            pafta = tire < 0 ? olcek : olcek.Substring(0, tire);
            detay = tire < 0 ? null : olcek.Substring(tire + 1);
            return amac.Length > 0 && Sayi(pafta) && (detay == null || Sayi(detay));
        }

        private static bool Sayi(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// Çizimdeki eski adlı ölçü/yazı stillerini yeni ada çevirir (yeni ad zaten varsa eskisine dokunmaz).
        /// Stil kaydı aynı kaldığından ölçüler stilini korur.
        /// </summary>
        internal static void CizimiTasi(Transaction tr, Database db)
        {
            if (tr == null || db == null) return;
            try
            {
                var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                foreach (var kv in EskiAdlar)
                {
                    if (!dst.Has(kv.Key) || dst.Has(kv.Value)) continue;
                    ObjectId id = dst[kv.Key];
                    if (id.IsNull || id.IsErased) continue;
                    var rec = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (rec.IsDependent) continue;
                    rec.Name = kv.Value;
                }
            }
            catch { }
            try
            {
                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has(EskiYaziEtriye) && !tst.Has(YaziEtriye))
                {
                    var rec = (TextStyleTableRecord)tr.GetObject(tst[EskiYaziEtriye], OpenMode.ForWrite);
                    if (!rec.IsDependent) rec.Name = YaziEtriye;
                }
            }
            catch { }
        }

        /// <summary>
        /// 1/100 pafta büyütmesi için hedef stili hazırlar: çizimdeki kaynak stilin kopyası, DIMLFAC / <paramref name="carpan"/>,
        /// DIMSCALE × <paramref name="carpan"/>; ardından KATMANYUKLE'de farklılaştırılan alanlar uygulanır. Var olan stile dokunulmaz.
        /// </summary>
        internal static ObjectId Pafta100Stili(Transaction tr, Database db, ObjectId kaynakId, double carpan)
        {
            if (tr == null || db == null || kaynakId.IsNull || kaynakId.IsErased) return ObjectId.Null;
            DimStyleTableRecord src;
            try { src = tr.GetObject(kaynakId, OpenMode.ForRead, false) as DimStyleTableRecord; }
            catch { return ObjectId.Null; }
            if (src == null) return ObjectId.Null;
            string hedef = Pafta100(src.Name);
            if (hedef == null) return ObjectId.Null;
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(hedef)) return dst[hedef];
            var rec = new DimStyleTableRecord();
            rec.CopyFrom(src);
            rec.Name = hedef;
            double lf = Math.Abs(src.Dimlfac) < 1e-12 ? 1.0 : src.Dimlfac;
            double sc = src.Dimscale <= 0 ? 1.0 : src.Dimscale;
            rec.Dimlfac = lf / carpan;
            rec.Dimscale = sc * carpan;
            if (!dst.IsWriteEnabled) dst.UpgradeOpen();
            ObjectId id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            BeykentStilUygula.OlcuFarkiniUygula(tr, db, rec);
            return id;
        }
    }
}
