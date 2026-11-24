// KAPAK formunun görsel denetimi (Core Console): yüklü Yardımcı derlemesindeki KapakForm'u oluşturur, isteğe bağlı olarak
// bir mimari projeyi okutur ve formu PNG'ye çizer. Ortam: KAPAK_FORM_PNG (çıktı), KAPAK_FORM_DWG (isteğe bağlı mimari DWG).
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;

public class KapakFormProbe
{
    /// <summary>KAPAK_SAHIP_GIRDI dosyasındaki her DWG adı için dosya adından proje sahibi → KAPAK_SAHIP_CIKTI.</summary>
    [CommandMethod("KAPAKSAHIPTEST")]
    public void SahipTest()
    {
        var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
        Assembly asm = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Yardimci", StringComparison.Ordinal));
        MethodInfo m = asm.GetType("ST4Yardimci.MimariKapakOku", true).GetMethod("SahipDosyaAdindan", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var satirlar = System.IO.File.ReadAllLines(Environment.GetEnvironmentVariable("KAPAK_SAHIP_GIRDI"))
            .Where(s => s.Trim().Length > 0)
            .Select(s => s + "\t=> " + ((string)m.Invoke(null, new object[] { s.Trim() }) ?? "(yok)"));
        System.IO.File.WriteAllLines(Environment.GetEnvironmentVariable("KAPAK_SAHIP_CIKTI"), satirlar, System.Text.Encoding.UTF8);
        ed.WriteMessage("\nKAPAKSAHIPTEST tamam");
    }

    [CommandMethod("KAPAKFORMPNG")]
    public void Calistir()
    {
        string png = Environment.GetEnvironmentVariable("KAPAK_FORM_PNG");
        string dwg = Environment.GetEnvironmentVariable("KAPAK_FORM_DWG");
        var ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            Assembly asm = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Yardimci", StringComparison.Ordinal));
            Type tForm = asm.GetType("ST4Yardimci.KapakForm", true), tRunner = asm.GetType("ST4Yardimci.KapakRunner", true);
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            object[] arg = { null };
            object veri = tRunner.GetMethod("SonVeri", bf).Invoke(null, arg);
            bool hatirla = Environment.GetEnvironmentVariable("KAPAK_FORM_HATIRLA") == "1";
            if (hatirla)
                foreach (var kv in new[] { ("Mahalle", "HOCACİHAN"), ("Sahip", "ELLE DÜZELTİLEN SAHİP"), ("Kat", "B+Z+7"), ("Tadilat", "Çatı katı ilavesi") })
                    veri.GetType().GetField(kv.Item1).SetValue(veri, kv.Item2);
            using (var form = (Form)Activator.CreateInstance(tForm, bf, null, new object[] { veri, "", null, null, hatirla }, null))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                Application.DoEvents();
                // Çok kapaklı projede açılan "Kapak seçimi" penceresi: PNG'ye çizilir, ikinci satır seçilip onaylanır.
                var zaman = new Timer { Interval = 400 };
                zaman.Tick += (s, e) =>
                {
                    Form secim = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Kapak seçimi");
                    if (secim == null) return;
                    zaman.Stop();
                    using (var b = new Bitmap(secim.Width, secim.Height))
                    {
                        secim.DrawToBitmap(b, new Rectangle(0, 0, secim.Width, secim.Height));
                        b.Save(System.IO.Path.ChangeExtension(png, ".secim.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var lb = secim.Controls.OfType<ListBox>().First();
                    if (lb.Items.Count > 1) lb.SelectedIndex = 1;
                    ed.WriteMessage("\nKAPAKFORMPNG secim: {0} kapak, secilen {1}", lb.Items.Count, lb.SelectedItem);
                    secim.DialogResult = DialogResult.OK;
                };
                zaman.Start();
                if (!string.IsNullOrEmpty(dwg)) { tForm.GetMethod("DwgOku", bf).Invoke(form, new object[] { dwg }); Application.DoEvents(); }
                zaman.Stop(); zaman.Dispose();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                }
                ed.WriteMessage("\nKAPAKFORMPNG: {0}x{1} istemci {2}", form.Width, form.Height, form.ClientSize);
                form.Close();
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nKAPAKFORMPNG HATA: {0}", ex.InnerException ?? ex);
        }
    }
}
