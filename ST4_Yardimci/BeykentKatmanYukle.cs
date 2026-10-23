using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using BeykentOrtak;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// KATMANYUKLE: katman / ölçü stili / yazı stili ayar formu (<see cref="KatmanAyarForm"/>).
    /// Değerler Ortak\BeykentStil (Plan ID ile ortak) ayar dosyasından gelir.
    /// Komutlar çalışırken var olan katmanın renk / çizgi tipi / kalınlığı değişmez; yalnız ayarda
    /// "Renk zorla" işaretli katmanların (varsayılan ETRIYE, DONATI OKU: ACI 150) rengi çekilir.
    /// Formdaki Yükle var olan katman ve stilleri de ayar değerine çeker.
    /// </summary>
    internal static class BeykentKatmanYukle
    {
        public static void Run()
        {
            if (!MachineLicense.EnsureOrWarn()) return;
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (var form = new KatmanAyarForm())
                AcApp.ShowModalDialog(form);
        }

        /// <summary>Ayar dosyasındaki bütün katman / ölçü / yazı stillerini açık çizime uygular.</summary>
        internal static string YukleAktifCizim()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "Açık çizim yok.";
            Editor ed = null;
            try { ed = doc.Editor; } catch { }
            Database db;
            try { db = doc.Database; }
            catch { return "Açık çizim yok. Önce bir DWG açın."; }
            if (db == null) return "Açık çizim yok. Önce bir DWG açın.";

            Database previous = HostApplicationServices.WorkingDatabase;
            BeykentStilUygula.YukleSonuc s;
            try
            {
                HostApplicationServices.WorkingDatabase = db;
                using (doc.LockDocument())
                    s = BeykentStilUygula.Yukle(db);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                return "Yüklenemedi: " + ex.ErrorStatus;
            }
            catch (Exception ex)
            {
                return "Yüklenemedi: " + ex.Message;
            }
            finally
            {
                try { HostApplicationServices.WorkingDatabase = previous; } catch { }
            }

            try { ed?.Regen(); } catch { }
            string msg = string.Format(
                "{0} katman güncellendi, {1} yeni; {2} ölçü stili, {3} yazı stili yüklendi; {4} ölçü yenilendi.",
                s.KatmanGuncel, s.KatmanYeni, s.OlcuStili, s.YaziStili, s.OlcuYenilenen);
            ed?.WriteMessage("\nKATMANYUKLE: " + msg);
            return msg;
        }

        /// <summary>
        /// Komut katmanı: ayarda varsa ayar değeriyle, yoksa verilen yedek değerlerle oluşturulur.
        /// Var olan katman yalnız açılır (Renk zorla işaretliyse rengi ayara çekilir).
        /// </summary>
        internal static void Ensure(Transaction tr, Database db, string name, short fallbackAci = 7,
            LineWeight fallbackWeight = LineWeight.LineWeight020, bool fallbackDashed = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            BeykentStilUygula.KatmanHazirla(tr, db, new KatmanAyar
            {
                Ad = name.Trim(),
                Aci = fallbackAci,
                Kalinlik = (int)fallbackWeight,
                CizgiTipi = fallbackDashed ? "DASHED" : "Continuous"
            });
        }
    }
}
