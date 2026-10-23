using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;

namespace BeykentOrtak
{
    /// <summary>
    /// <see cref="BeykentStilAyar"/> değerlerini çizime uygular. Yardımcı ve Plan ID aynı kaynağı link'li derler.
    /// Komutlar: katman yoksa ayarla oluşturulur, varsa yalnız açılır (RenkZorla işaretliyse rengi de çekilir).
    /// Yükle (KATMANYUKLE formu): var olan katman / ölçü / yazı stilleri de ayar değerine çekilir.
    /// </summary>
    internal static class BeykentStilUygula
    {
        internal static readonly int[] GecerliKalinliklar =
            { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };

        internal static Color Renk(KatmanAyar a)
        {
            if (a.TryRgb(out byte r, out byte g, out byte b)) return Color.FromRgb(r, g, b);
            short aci = a.Aci < 1 || a.Aci > 255 ? (short)7 : a.Aci;
            return Color.FromColorIndex(ColorMethod.ByAci, aci);
        }

        internal static LineWeight Kalinlik(int lw)
        {
            if (lw == -3 || lw == -2 || lw == -1) return (LineWeight)lw;
            int best = 20, fark = int.MaxValue;
            foreach (int v in GecerliKalinliklar)
            {
                int f = Math.Abs(v - lw);
                if (f < fark) { fark = f; best = v; }
            }
            return (LineWeight)best;
        }

        /// <summary>Yüzde (0..90) → AutoCAD alfa (255 opak).</summary>
        internal static Transparency Saydamlik(int yuzde)
        {
            if (yuzde < 0) yuzde = 0;
            if (yuzde > 90) yuzde = 90;
            return new Transparency((byte)Math.Round(255.0 * (100 - yuzde) / 100.0));
        }

        /// <summary>Çizgi tipini bulur; yoksa acad.lin / acadiso.lin'den yüklemeyi dener. Bulunamazsa Null.</summary>
        internal static ObjectId CizgiTipi(Transaction tr, Database db, string ad)
        {
            if (string.IsNullOrWhiteSpace(ad)) return db.ContinuousLinetype;
            ad = ad.Trim();
            if (string.Equals(ad, "Continuous", StringComparison.OrdinalIgnoreCase)) return db.ContinuousLinetype;
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(ad)) return ltt[ad];
            foreach (string file in new[] { "acad.lin", "acadiso.lin" })
            {
                try { db.LoadLineTypeFile(ad, file); } catch { }
                ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(ad)) return ltt[ad];
            }
            return ObjectId.Null;
        }

        /// <summary>İşlem açılmadan önce ayardaki çizgi tiplerini yükler (WorkingDatabase farklıyken işlem içinde yükleme eNoDatabase verebilir).</summary>
        internal static void CizgiTipleriniOnYukle(Database db)
        {
            var adlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KatmanAyar k in BeykentStilAyar.Aktif().Katmanlar)
                if (k.Kesikli) adlar.Add(k.CizgiTipi.Trim());
            foreach (string ad in adlar)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try { CizgiTipi(tr, db, ad); } catch { }
                    tr.Commit();
                }
            }
        }

        internal static void KatmanOzellikYaz(Transaction tr, Database db, LayerTableRecord rec, KatmanAyar a)
        {
            if (!rec.IsWriteEnabled) rec.UpgradeOpen();
            rec.Color = Renk(a);
            rec.LineWeight = Kalinlik(a.Kalinlik);
            ObjectId lt = CizgiTipi(tr, db, a.CizgiTipi);
            if (!lt.IsNull) rec.LinetypeObjectId = lt;
            try { rec.IsPlottable = a.Cizdir; } catch { }
            try { rec.Transparency = Saydamlik(a.Saydamlik); } catch { }
        }

        /// <summary>
        /// Komut katmanı: ayarda varsa ayar değeriyle, yoksa <paramref name="yedek"/> ile oluşturur.
        /// Var olan katman yalnız açılır; ayarda RenkZorla işaretliyse rengi ayardaki renge çekilir.
        /// </summary>
        internal static void KatmanHazirla(Transaction tr, Database db, KatmanAyar yedek)
        {
            if (tr == null || db == null || yedek == null || string.IsNullOrWhiteSpace(yedek.Ad)) return;
            string ad = yedek.Ad.Trim();
            if (ad == "0") return;
            KatmanAyar a = BeykentStilAyar.Katman(ad) ?? yedek;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ad))
            {
                try
                {
                    var existing = (LayerTableRecord)tr.GetObject(lt[ad], OpenMode.ForRead);
                    if (existing.IsOff)
                    {
                        existing.UpgradeOpen();
                        existing.IsOff = false;
                    }
                    if (a.RenkZorla)
                    {
                        Color c = Renk(a);
                        if (!RenkEsit(existing.Color, c))
                        {
                            if (!existing.IsWriteEnabled) existing.UpgradeOpen();
                            existing.Color = c;
                        }
                    }
                }
                catch { }
                return;
            }

            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = a == yedek ? ad : a.Ad };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            KatmanOzellikYaz(tr, db, rec, a);
            try { rec.IsOff = false; } catch { }
        }

        private static bool RenkEsit(Color x, Color y)
        {
            if (x == null || y == null) return false;
            if (x.ColorMethod != y.ColorMethod) return false;
            if (y.ColorMethod == ColorMethod.ByAci) return x.ColorIndex == y.ColorIndex;
            return x.Red == y.Red && x.Green == y.Green && x.Blue == y.Blue;
        }

        // ------------------------------------------------------------------ yazı stilleri

        internal static void YaziOzellikYaz(TextStyleTableRecord rec, YaziAyar a)
        {
            if (!rec.IsWriteEnabled) rec.UpgradeOpen();
            string font = string.IsNullOrWhiteSpace(a.Font) ? "Bahnschrift Light Condensed" : a.Font.Trim();
            if (font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
            {
                try { rec.Font = new FontDescriptor("", false, false, 0, 0); } catch { }
                rec.FileName = font;
            }
            else
            {
                try { rec.Font = new FontDescriptor(font, false, false, 0, 0); }
                catch { rec.Font = new FontDescriptor("Arial", false, false, 0, 0); }
            }
            rec.TextSize = a.Yukseklik > 0 ? a.Yukseklik : 0;
            rec.XScale = a.Genislik > 0.01 ? a.Genislik : 1;
            try { rec.ObliquingAngle = a.EgikAci * Math.PI / 180.0; } catch { }
        }

        /// <summary>Yazı stilini ayardan oluşturur; <paramref name="varsaGuncelle"/> ise var olanı da ayara çeker.</summary>
        internal static ObjectId YaziStili(Transaction tr, Database db, string ad, bool varsaGuncelle)
        {
            if (string.IsNullOrWhiteSpace(ad)) ad = BeykentStilAyar.YaziBeykent;
            ad = ad.Trim();
            YaziAyar a = BeykentStilAyar.Yazi(ad) ?? new YaziAyar { Ad = ad };
            var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(ad))
            {
                ObjectId id = table[ad];
                if (varsaGuncelle) YaziOzellikYaz((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite), a);
                return id;
            }
            var rec = new TextStyleTableRecord { Name = ad };
            YaziOzellikYaz(rec, a);
            if (!table.IsWriteEnabled) table.UpgradeOpen();
            ObjectId yeni = table.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return yeni;
        }

        // ------------------------------------------------------------------ ölçü stilleri

        /// <summary>Ölçü stilinin bütün BEYKENT değerleri (Yardımcı'nın eski Configure kuralları).</summary>
        internal static void OlcuStiliYapilandir(DimStyleTableRecord record, OlcuAyar a, ObjectId textStyle, ObjectId continuous)
        {
            record.Dimtxsty = textStyle;
            record.Dimscale = 1;
            record.Dimlfac = a.Olcek;
            record.Dimtxt = a.YaziYuksekligi;
            record.Dimblk = record.Dimblk1 = record.Dimblk2 = record.Dimldrblk = ObjectId.Null;
            record.Dimsah = false;
            record.Dimasz = a.OkBoyu;
            record.Dimtsz = a.Tik ? a.OkBoyu : 0;
            record.Dimexo = a.Ofset;
            record.Dimexe = a.Uzanti;
            record.Dimfxlen = a.Uzanti;
            record.DimfxlenOn = a.SabitUzanti;
            record.Dimse1 = record.Dimse2 = a.UzatmaGizle;
            record.Dimsd1 = record.Dimsd2 = false;
            record.Dimdle = 0;
            record.Dimgap = a.Bosluk;
            record.Dimtad = (short)(a.YaziUstte ? 1 : 0);
            record.Dimjust = 0;
            record.Dimtih = record.Dimtoh = false;
            record.Dimtofl = record.Dimtix = true;
            record.Dimsoxd = false;
            record.Dimatfit = 3;
            record.Dimtmove = 0;
            record.Dimrnd = a.Yuvarlama;
            record.Dimdec = record.Dimtdec = a.Ondalik;
            record.Dimzin = a.SifirBastir;
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

        /// <summary>Ölçü stilini ayardan oluşturur ya da var olanın üzerine yazar.</summary>
        internal static ObjectId OlcuStiliYaz(Transaction tr, Database db, OlcuAyar a)
        {
            ObjectId text = YaziStili(tr, db, a.YaziStili, false);
            var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            DimStyleTableRecord record;
            ObjectId id;
            if (table.Has(a.Ad))
            {
                id = table[a.Ad];
                record = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
            }
            else
            {
                if (!table.IsWriteEnabled) table.UpgradeOpen();
                record = new DimStyleTableRecord { Name = a.Ad };
                id = table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            OlcuStiliYapilandir(record, a, text, db.ContinuousLinetype);
            return id;
        }

        /// <summary>
        /// Plan ID ölçü stilleri kendi kurallarıyla kurulur; bu metot yalnız kullanıcının formda
        /// varsayılandan farklılaştırdığı alanları kayda yazar. Ayar dosyası yoksa hiçbir şey değişmez.
        /// </summary>
        internal static void OlcuFarkiniUygula(Transaction tr, Database db, DimStyleTableRecord rec)
        {
            if (tr == null || db == null || rec == null || string.IsNullOrEmpty(rec.Name)) return;
            OlcuAyar a = BeykentStilAyar.Olcu(rec.Name);
            OlcuAyar v = BeykentStilAyar.OlcuVarsayilan(rec.Name);
            if (a == null || v == null || ReferenceEquals(a, v)) return;
            try
            {
                if (!rec.IsWriteEnabled) rec.UpgradeOpen();
                if (Fark(a.YaziYuksekligi, v.YaziYuksekligi)) rec.Dimtxt = a.YaziYuksekligi;
                if (Fark(a.Olcek, v.Olcek)) rec.Dimlfac = a.Olcek;
                if (Fark(a.OkBoyu, v.OkBoyu) || a.Tik != v.Tik)
                {
                    rec.Dimasz = a.OkBoyu;
                    rec.Dimtsz = a.Tik ? a.OkBoyu : 0;
                }
                if (Fark(a.Ofset, v.Ofset)) rec.Dimexo = a.Ofset;
                if (Fark(a.Uzanti, v.Uzanti) || a.SabitUzanti != v.SabitUzanti)
                {
                    rec.Dimexe = a.Uzanti;
                    rec.Dimfxlen = a.Uzanti;
                    rec.DimfxlenOn = a.SabitUzanti;
                }
                if (Fark(a.Bosluk, v.Bosluk)) rec.Dimgap = a.Bosluk;
                if (Fark(a.Yuvarlama, v.Yuvarlama)) rec.Dimrnd = a.Yuvarlama;
                if (a.Ondalik != v.Ondalik) rec.Dimdec = rec.Dimtdec = a.Ondalik;
                if (a.SifirBastir != v.SifirBastir) rec.Dimzin = a.SifirBastir;
                if (a.YaziUstte != v.YaziUstte) rec.Dimtad = (short)(a.YaziUstte ? 1 : 0);
                if (a.UzatmaGizle != v.UzatmaGizle) rec.Dimse1 = rec.Dimse2 = a.UzatmaGizle;
                if (!string.Equals(a.YaziStili, v.YaziStili, StringComparison.OrdinalIgnoreCase))
                {
                    ObjectId ts = YaziStili(tr, db, a.YaziStili, false);
                    if (!ts.IsNull) rec.Dimtxsty = ts;
                }
            }
            catch { }
        }

        private static bool Fark(double x, double y) => Math.Abs(x - y) > 1e-9;

        // ------------------------------------------------------------------ Yükle

        internal sealed class YukleSonuc
        {
            public int KatmanYeni, KatmanGuncel, OlcuStili, YaziStili, OlcuYenilenen;
        }

        /// <summary>
        /// Bütün ayarları açık çizime uygular. Çağıran belge kilidini almalı ve WorkingDatabase'i <paramref name="db"/> yapmalı.
        /// </summary>
        internal static YukleSonuc Yukle(Database db)
        {
            var s = new YukleSonuc();
            BeykentStilDosya ayar = BeykentStilAyar.Aktif();
            CizgiTipleriniOnYukle(db);
            var stiller = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (YaziAyar y in ayar.YaziStilleri)
                {
                    if (string.IsNullOrWhiteSpace(y.Ad)) continue;
                    YaziStili(tr, db, y.Ad, true);
                    s.YaziStili++;
                }

                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (KatmanAyar k in ayar.Katmanlar)
                {
                    if (string.IsNullOrWhiteSpace(k.Ad)) continue;
                    if (lt.Has(k.Ad))
                    {
                        var rec = (LayerTableRecord)tr.GetObject(lt[k.Ad], OpenMode.ForWrite);
                        if (rec.IsDependent) continue;
                        KatmanOzellikYaz(tr, db, rec, k);
                        try { rec.IsOff = false; } catch { }
                        s.KatmanGuncel++;
                    }
                    else if (k.Yukle)
                    {
                        if (!lt.IsWriteEnabled) lt.UpgradeOpen();
                        var rec = new LayerTableRecord { Name = k.Ad };
                        lt.Add(rec);
                        tr.AddNewlyCreatedDBObject(rec, true);
                        KatmanOzellikYaz(tr, db, rec, k);
                        s.KatmanYeni++;
                    }
                }

                foreach (OlcuAyar o in ayar.OlcuStilleri)
                {
                    if (string.IsNullOrWhiteSpace(o.Ad)) continue;
                    stiller.Add(OlcuStiliYaz(tr, db, o));
                    s.OlcuStili++;
                }

                s.OlcuYenilenen = OlculeriYenile(tr, db, stiller);
                tr.Commit();
            }
            return s;
        }

        /// <summary>Verilen stildeki yerleşim (model/kağıt) ölçülerinin bloğunu yeniden hesaplar.</summary>
        private static int OlculeriYenile(Transaction tr, Database db, HashSet<ObjectId> stiller)
        {
            int n = 0;
            if (stiller.Count == 0) return 0;
            var dimClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Dimension));
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (!btr.IsLayout) continue;
                foreach (ObjectId id in btr)
                {
                    if (!id.ObjectClass.IsDerivedFrom(dimClass)) continue;
                    try
                    {
                        var dim = (Dimension)tr.GetObject(id, OpenMode.ForRead);
                        if (!stiller.Contains(dim.DimensionStyle)) continue;
                        var layer = (LayerTableRecord)tr.GetObject(dim.LayerId, OpenMode.ForRead);
                        if (layer.IsLocked) continue;
                        dim.UpgradeOpen();
                        dim.RecomputeDimensionBlock(true);
                        n++;
                    }
                    catch { }
                }
            }
            return n;
        }
    }
}
