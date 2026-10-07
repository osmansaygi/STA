using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Formdaki eski/yeni kot cm'dir. Fark metreye çevrilir; çoklu seçilen kot yazıları
    /// (çizimde m: +3.50, ±0.00) bu fark kadar kaydırılır.
    /// </summary>
    internal static class KotDegistirRunner
    {
        private static readonly Regex KotTokenRx = new Regex(
            @"(?<![\d.,])(?<sign>%%[pP]|\\U\+00[Bb]1|[+\-\u2212\u00B1])?(?<body>\d+(?:[.,]\d+)?)(?!\d)",
            RegexOptions.Compiled);

        public static void Run()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            KotDegistirSettings.EnsureLoaded();

            string eskiRaw = (KotDegistirSettings.EskiKot ?? "").Trim();
            string yeniRaw = (KotDegistirSettings.YeniKot ?? "").Trim();
            if (eskiRaw.Length == 0 || yeniRaw.Length == 0)
            {
                ed.WriteMessage("\nKOTDEGISTIR: Eski kot ve yeni kot gerekli.");
                return;
            }
            if (KotDegistirSettings.Katmanlar.Count == 0)
            {
                ed.WriteMessage("\nKOTDEGISTIR: Kot yazısı katmanı ekleyin.");
                return;
            }

            if (!TryParseKot(eskiRaw, out double eskiCm) || !TryParseKot(yeniRaw, out double yeniCm))
            {
                ed.WriteMessage("\nKOTDEGISTIR: Eski kot ve yeni kot cm cinsinden sayı olmalı.");
                return;
            }
            double deltaCm = yeniCm - eskiCm;
            if (Math.Abs(deltaCm) < 0.05)
            {
                ed.WriteMessage("\nKOTDEGISTIR: Eski kot ile yeni kot aynı.");
                return;
            }
            double deltaM = deltaCm / 100.0;

            var tvs = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.Operator, "<OR")
            };
            foreach (string lyr in KotDegistirSettings.Katmanlar)
            {
                if (!string.IsNullOrWhiteSpace(lyr))
                    tvs.Add(new TypedValue((int)DxfCode.LayerName, lyr.Trim()));
            }
            tvs.Add(new TypedValue((int)DxfCode.Operator, "OR>"));
            tvs.Add(new TypedValue((int)DxfCode.Operator, "AND>"));

            var peo = new PromptSelectionOptions
            {
                MessageForAdding = "\nKot yazılarını seçin [Enter/sağ tık=bitir]: ",
                AllowDuplicates = false,
                RejectObjectsOnLockedLayers = false
            };
            PromptSelectionResult psr = ed.GetSelection(peo, new SelectionFilter(tvs.ToArray()));
            if (psr.Status == PromptStatus.Cancel)
            {
                ed.WriteMessage("\nKOTDEGISTIR: Seçim iptal.");
                return;
            }
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nKOTDEGISTIR: Seçim yok.");
                return;
            }

            var layers = new HashSet<string>(KotDegistirSettings.Katmanlar, StringComparer.OrdinalIgnoreCase);
            int updated = 0;
            int skipped = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var relock = new List<LayerTableRecord>();
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead, false) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    if (ent is AttributeReference || ent is AttributeDefinition) continue;
                    if (!(ent is DBText) && !(ent is MText)) continue;
                    if (!layers.Contains(ent.Layer)) continue;

                    string raw = ent is DBText tx ? (tx.TextString ?? "") : ((MText)ent).Contents ?? "";
                    string next = ShiftKots(raw, deltaM);
                    if (next == null || next == raw)
                    {
                        skipped++;
                        continue;
                    }

                    if (!TryUpgrade(tr, ent, relock)) continue;
                    if (ent is DBText dbt)
                        dbt.TextString = next;
                    else
                        ((MText)ent).Contents = next;
                    updated++;
                }

                foreach (LayerTableRecord ltr in relock)
                {
                    try { ltr.IsLocked = true; } catch { }
                }
                tr.Commit();
            }

            string farkCm = deltaCm.ToString("+0.##;-0.##", CultureInfo.InvariantCulture);
            if (updated == 0)
                ed.WriteMessage("\nKOTDEGISTIR: Fark {0} cm. Kot yazısı güncellenmedi ({1} yazıda kot yok).", farkCm, skipped);
            else if (skipped > 0)
                ed.WriteMessage("\nKOTDEGISTIR: Fark {0} cm ({1} m), {2} kot yazısı güncellendi, {3} yazıda kot yok.",
                    farkCm, FormatKot(deltaM, '.'), updated, skipped);
            else
                ed.WriteMessage("\nKOTDEGISTIR: Fark {0} cm ({1} m), {2} kot yazısı güncellendi.",
                    farkCm, FormatKot(deltaM, '.'), updated);

            try { ed.Regen(); } catch { }
        }

        private static bool TryUpgrade(Transaction tr, Entity ent, List<LayerTableRecord> relock)
        {
            try
            {
                var ltr = tr.GetObject(ent.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (ltr != null && ltr.IsLocked)
                {
                    ltr.UpgradeOpen();
                    ltr.IsLocked = false;
                    relock.Add(ltr);
                }
                if (!ent.IsWriteEnabled)
                    ent.UpgradeOpen();
                return ent.IsWriteEnabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Çizimdeki kot metredir. deltaM her kot belirtecine eklenir.</summary>
        private static string ShiftKots(string raw, double deltaM)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            bool hit = false;
            string next = KotTokenRx.Replace(raw, m =>
            {
                string sign = m.Groups["sign"].Success ? m.Groups["sign"].Value : "";
                string body = m.Groups["body"].Value;
                int sepAt = body.IndexOfAny(new[] { '.', ',' });
                int decLen = sepAt < 0 ? 0 : body.Length - sepAt - 1;
                if (sign.Length == 0 && decLen != 2) return m.Value;
                if (!TryParseBody(sign, body, out double v)) return m.Value;
                hit = true;
                char sep = body.IndexOf(',') >= 0 ? ',' : '.';
                return FormatKot(v + deltaM, sep);
            });
            return hit ? next : raw;
        }

        private static bool TryParseBody(string sign, string body, out double m)
        {
            m = 0;
            if (string.IsNullOrEmpty(body)) return false;
            string n = body.Replace(',', '.');
            if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double mag))
                return false;
            if (sign == "-" || sign == "\u2212")
                m = -Math.Abs(mag);
            else
                m = Math.Abs(mag);
            return true;
        }

        internal static bool TryParseKot(string raw, out double m)
        {
            m = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().Replace(" ", "").Replace("\u00A0", "");
            s = s.Replace("%%p", "").Replace("%%P", "");
            s = s.Replace("\\U+00B1", "").Replace("\\U+00b1", "");
            s = s.Replace("\u00B1", "");
            s = s.Replace("\u2212", "-");
            bool neg = s.StartsWith("-", StringComparison.Ordinal);
            s = s.TrimStart('+', '-');
            s = s.Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double mag))
                return false;
            m = neg ? -Math.Abs(mag) : Math.Abs(mag);
            return true;
        }

        internal static string FormatKot(double m, char sep)
        {
            double r = Math.Round(m, 2, MidpointRounding.AwayFromZero);
            string s;
            if (Math.Abs(r) < 0.005)
                s = "\u00B10.00";
            else
                s = r.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
            if (sep == ',') s = s.Replace('.', ',');
            return s;
        }
    }
}
