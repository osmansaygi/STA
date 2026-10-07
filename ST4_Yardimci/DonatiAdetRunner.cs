using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Mesafe seç → bir veya daha çok donatı etiketi seç.
    /// Her etikette kendi aralığına göre adet = Ceil(mesafe / aralık) yazar.
    /// Örn. 225 cm / 15 → 15ø12/15 L=…
    /// </summary>
    internal static class DonatiAdetRunner
    {
        /// <summary>
        /// carpan + adet + øcap/aralik. Çarpanlar (2x, 2x3x, 2×) korunur;
        /// yalnız ø öncesindeki son sayı yeni adetle değişir: 2x25ø8/20 → 2x23ø8/20.
        /// Phi: ø Ø ⌀ %%C Φ φ
        /// </summary>
        private static readonly Regex LabelRx = new Regex(
            @"(?<carpan>(?:\d+\s*[xX×*]\s*)*)" +
            @"(?<adet>\d+)?\s*" +
            @"(?<phi>%%[Cc]|\\U\+00[Dd]8|\\U\+00[Ff]8|\\U\+2205|[\u00D8\u00F8Øø⌀Φφ])\s*" +
            @"(?<cap>\d{1,2})\s*/\s*(?<aralik>\d+(?:[.,]\d+)?)",
            RegexOptions.Compiled);

        public static void RunInteractive()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\nDONATI ADET: Mesafe seçin, sonra donatı etiketlerini (çoklu). Esc = bitir.");

            while (true)
            {
                var distOpt = new PromptDistanceOptions("\nDonatı mesafesi (bitir: Esc): ")
                {
                    AllowNone = true,
                    AllowNegative = false,
                    AllowZero = false
                };
                PromptDoubleResult distRes = ed.GetDistance(distOpt);
                if (distRes.Status != PromptStatus.OK)
                    break;

                double mesafe = distRes.Value;
                if (mesafe <= 1e-9)
                {
                    ed.WriteMessage("\nDONATI ADET: Mesafe geçersiz.");
                    continue;
                }

                var peo = new PromptSelectionOptions
                {
                    MessageForAdding = "\nDonatı etiketlerini seçin: ",
                    MessageForRemoval = "\nÇıkarılacak etiket: ",
                    AllowDuplicates = false
                };
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult psr = ed.GetSelection(peo, filter);
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    if (psr.Status == PromptStatus.Cancel)
                        break;
                    ed.WriteMessage("\nDONATI ADET: Seçim yok.");
                    continue;
                }

                int updated = 0, skipped = 0;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null) continue;
                        var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased)
                        {
                            skipped++;
                            continue;
                        }

                        string raw;
                        if (ent is DBText dbt)
                            raw = dbt.TextString ?? "";
                        else if (ent is MText mt)
                            raw = mt.Contents ?? "";
                        else
                        {
                            skipped++;
                            continue;
                        }

                        string plain = StripMText(raw);
                        if (!TryComputeAdet(plain, mesafe, out int newAdet, out double aralik, out string err))
                        {
                            skipped++;
                            ed.WriteMessage("\n  Atlandı: {0}", err);
                            continue;
                        }

                        if (!TryReplaceAdet(raw, plain, newAdet, out string newRaw, out string newPlain, out string oldAdetDisp))
                        {
                            skipped++;
                            ed.WriteMessage("\n  Atlandı: Adet yazılamadı.");
                            continue;
                        }

                        ent.UpgradeOpen();
                        if (ent is DBText tx)
                            tx.TextString = newPlain;
                        else if (ent is MText mtx)
                            mtx.Contents = newRaw;

                        updated++;
                        ed.WriteMessage(
                            "\n  {0:0.##}/{1:0.##} → adet {2} → {3}  ({4})",
                            mesafe, aralik, oldAdetDisp, newAdet, newPlain);
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nDONATI ADET: mesafe={0:0.##}  guncellenen={1}  atlanan={2}",
                    mesafe, updated, skipped);
            }
        }

        internal static bool TryComputeAdet(
            string plain, double mesafe,
            out int newAdet, out double aralik, out string err)
        {
            newAdet = 0;
            aralik = 0;
            err = null;
            if (string.IsNullOrWhiteSpace(plain))
            {
                err = "Boş etiket.";
                return false;
            }

            Match m = LabelRx.Match(plain);
            if (!m.Success)
            {
                err = "Etikette çap/aralık yok (örn. 12ø12/15).";
                return false;
            }

            string arRaw = m.Groups["aralik"].Value.Replace(',', '.');
            if (!double.TryParse(arRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out aralik) ||
                aralik <= 1e-9)
            {
                err = "Aralık okunamadı.";
                return false;
            }

            newAdet = (int)Math.Ceiling(mesafe / aralik);
            if (newAdet < 1) newAdet = 1;
            return true;
        }

        internal static bool TryReplaceAdet(
            string raw, string plain, int newAdet,
            out string newRaw, out string newPlain, out string oldAdetDisp)
        {
            newRaw = raw;
            newPlain = plain;
            oldAdetDisp = "?";

            Match mp = LabelRx.Match(plain);
            if (!mp.Success) return false;

            string carpan = mp.Groups["carpan"].Value;
            string adetTok = mp.Groups["adet"].Value;
            oldAdetDisp = string.IsNullOrEmpty(adetTok)
                ? (string.IsNullOrWhiteSpace(carpan) ? "(yok)" : carpan.Trim())
                : (carpan + adetTok).Trim();
            string adetStr = newAdet.ToString(CultureInfo.InvariantCulture);

            newPlain = ReplaceAdetInMatch(plain, mp, adetStr);

            if (raw.IndexOf('\\') < 0 && raw.IndexOf('{') < 0)
                newRaw = newPlain;
            else
            {
                Match mr = LabelRx.Match(raw);
                newRaw = mr.Success ? ReplaceAdetInMatch(raw, mr, adetStr) : newPlain;
            }

            return true;
        }

        private static string ReplaceAdetInMatch(string text, Match m, string adetStr)
        {
            System.Text.RegularExpressions.Group gAdet = m.Groups["adet"];
            if (gAdet.Success && gAdet.Length > 0)
            {
                return text.Substring(0, gAdet.Index)
                     + adetStr
                     + text.Substring(gAdet.Index + gAdet.Length);
            }

            System.Text.RegularExpressions.Group gPhi = m.Groups["phi"];
            return text.Substring(0, gPhi.Index)
                 + adetStr
                 + text.Substring(gPhi.Index);
        }

        internal static string StripMText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\\[PpLlOoKk]", " ");
            s = Regex.Replace(s, @"\{[^;]*;", "");
            s = s.Replace("{", "").Replace("}", "");
            return s;
        }
    }
}
