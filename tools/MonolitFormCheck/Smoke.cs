using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Beykent.Monolit;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

internal static class Smoke
{
    internal static string Root;
    private static string _output;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Root = Path.GetFullPath(args[0]); _output = Path.Combine(Root, "tools/MonolitFormCheck/preview");
            Directory.CreateDirectory(_output);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            CheckKotConversions();
            foreach (int theme in new[] { 1, 0 })
            {
                AcApp.SetTheme(theme);
                if (args.Length == 1)
                {
                using (var olcu = NewForm(typeof(ST4Yardimci.OtoOlcuForm)))
                using (var kolon = NewForm(typeof(ST4PlanIdCiz.KolonDuseyForm)))
                {
                    Load(olcu); Load(kolon);
                    Check(AcApp.Subscribers == 0, "standart formlar AutoCAD tema olayına abone olmaz");
                    Check(olcu.BackColor == StandardFormTheme.Bg, "daima açık başlangıç tema");
                    var primary = Field<Button>(olcu, "_btnRun");
                    Check(Layout(olcu).UnplacedCount == 0 && Layout(kolon).UnplacedCount == 0, "tüm mevcut kontroller yeni düzende");
                    Console.WriteLine("SIZE Oto Ölçü: " + Layout(olcu).PreviousSize + " -> " + olcu.ClientSize);
                    Console.WriteLine("SIZE Kolon Düşey: " + Layout(kolon).PreviousSize + " -> " + kolon.ClientSize);
                    Check(Descendants(olcu).OfType<GroupBox>().Any(), "A standart çerçeveli gruplar");
                    Check(primary.FlatStyle == FlatStyle.Standard && primary.UseVisualStyleBackColor, "yerel standart düğme");
                    var type = Field<ComboBox>(olcu, "_cmbOlcuTipi");
                    int oneHeight = olcu.ClientSize.Height;
                    type.SelectedIndex = 1;
                    Check(olcu.ClientSize.Height > oneHeight, "çift ölçü alanı açılır");
                    Check(Field<Button>(olcu, "_btnRun").Bottom < olcu.ClientSize.Height, "çift ölçü düğmesi içeride");
                    type.SelectedIndex = 0;
                    Check(olcu.ClientSize.Height == oneHeight, "tek ölçü boyutu geri gelir");
                    Snapshot(olcu, "oto-olcu-" + theme); Snapshot(kolon, "kolon-dusey-" + theme);
                    Field<CheckBox>(kolon, "_chkAyri").Checked = true;
                    Check(!Field<CheckBox>(kolon, "_chkTum").Checked && Field<CheckBox>(kolon, "_chkAyri").Checked,
                        "kat seçimi olayları korunur");
                    ST4Yardimci.OtoOlcuSettings.ActivePresetName = "AKS OLCU";
                    ((ST4Yardimci.OtoOlcuForm)olcu).RefreshFromDrawing();
                    Check(!Field<ComboBox>(olcu, "_cmbDimStyle").Enabled, "aks stil kilidi korunur");
                    Check(Field<Button>(olcu, "_btnRun").Bottom < olcu.ClientSize.Height, "aks düğmesi içeride");
                    Snapshot(olcu, "oto-aks-" + theme);
                    ST4Yardimci.OtoOlcuSettings.ActivePresetName = "Standart";
                    AcApp.SetTheme(1 - theme);
                    Check(olcu.BackColor == StandardFormTheme.Bg && kolon.BackColor == StandardFormTheme.Bg, "AutoCAD tema değişiminde formlar açık kalır");
                    Check(primary.ForeColor == SystemColors.ControlText, "standart düğme metni");
                    MonolitTheme.Refresh();
                    AcApp.FailRead = true;
                    MonolitTheme.Refresh();
                    Check(MonolitTheme.IsLight == (theme == 0), "host yoksa son tema korunur");
                    AcApp.FailRead = false;
                }
                Check(AcApp.Subscribers == 0, "Dispose olay aboneliklerini temizler");
                }
                AcApp.SetTheme(theme);
                foreach (var t in typeof(Smoke).Assembly.GetTypes().Where(t => t.BaseType == typeof(MonolitForm)
                    && t != typeof(ST4Yardimci.OtoOlcuForm) && t != typeof(ST4PlanIdCiz.KolonDuseyForm)
                    && (args.Length == 1 || t.Name == args[1])))
                {
                    Console.WriteLine("START " + t.Name + " host-theme=" + theme);
                    using (var form = NewForm(t))
                    {
                        Load(form);
                        Check(form.BackColor == StandardFormTheme.Bg, t.Name + " açık tema");
                        foreach (var button in Descendants(form).OfType<Button>().Where(b => b.Visible))
                            Check(button.FlatStyle == FlatStyle.Standard && button.UseVisualStyleBackColor, t.Name + " native düğme");
                        Check(Layout(form) != null, t.Name + " yeni düzen eksik");
                        Check(Layout(form).UnplacedCount == 0, t.Name + " eşlenmemiş kontrol: " + Layout(form).UnplacedCount);
                        Console.WriteLine("SIZE " + t.Name + ": " + Layout(form).PreviousSize + " -> " + form.ClientSize);
                        var tabs = Descendants(form).OfType<TabControl>().ToArray();
                        if (tabs.Length == 0) Snapshot(form, t.Name + "-" + theme);
                        else foreach (var tab in tabs)
                            for (int page = 0; page < tab.TabPages.Count; page++)
                            {
                                tab.SelectedIndex = page;
                                System.Windows.Forms.Application.DoEvents();
                                Snapshot(form, t.Name + "-" + theme + "-tab" + page);
                            }
                        if (form is ST4Yardimci.ParapetForm)
                        {
                            Field<ComboBox>(form, "_konum").SelectedIndex = 2;
                            Check(!Field<System.Collections.Generic.Dictionary<string, NumericUpDown>>(form, "_numbers")["Sakal"].Enabled,
                                "kiriş üzerinde sakal kilidi korunur");
                            Snapshot(form, t.Name + "-" + theme + "-kiris");
                        }
                        if (form is ST4Yardimci.KatmanAyarForm)
                        {
                            var grid = Field<DataGridView>(form, "_katman");
                            int count = grid.Rows.Count;
                            var filter = Descendants(form).OfType<TextBox>().First(c => c.Parent is TableLayoutPanel);
                            filter.Text = "KOLON";
                            Check(grid.Rows.Cast<DataGridViewRow>().Any(r => r.Visible), "arama eşleşmeleri");
                            Check(grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).All(r => r.Cells.Cast<DataGridViewCell>()
                                .Any(c => Convert.ToString(c.Value).IndexOf("KOLON", StringComparison.OrdinalIgnoreCase) >= 0)), "arama yalnız eşleşen satırları gösterir");
                            filter.Text = "";
                            Check(grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) == count, "arama temizlenince tüm satırlar döner");
                        }
                    }
                    Check(AcApp.Subscribers == 0, t.Name + " olay aboneliği sızıntısı");
                }
            }
            Console.WriteLine("PASS: 26 gerçek form sınıfı; standart A, tüm kontroller, AutoCAD iki temada da açık form, Tek/Çift/AKS, kat seçimi, kiriş/sakal, Dispose. AutoCAD ve disk erişimi taklit edildi.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static Form NewForm(Type type)
    {
        if (type == typeof(ST4PlanIdCiz.KirisAcilimForm))
        {
            var model = new ST4AksCizCSharp.St4Model();
            model.Floors.Add(new ST4AksCizCSharp.FloorInfo { Name = "Zemin", FloorNo = 0 });
            model.Floors.Add(new ST4AksCizCSharp.FloorInfo { Name = "1. Kat", FloorNo = 1, ElevationM = 3 });
            return (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { model }, null);
        }
        if (type == typeof(ST4PlanIdCiz.KolonDuseyKatSinirForm))
            return new ST4PlanIdCiz.KolonDuseyKatSinirForm(new System.Collections.Generic.List<(int, string, double)> { (0, "Zemin", 0), (1, "1. Kat", 3) });
        return (Form)Activator.CreateInstance(type, true);
    }
    private static object Invoke(Form form, string method, params object[] args) => form.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    private static void CheckKotConversions()
    {
        ST4Yardimci.OtoDonatiSettings.EnsureLoaded();
        ST4Yardimci.SehpaDonatiSettings.EnsureLoaded();
        ST4Yardimci.OtoDonatiSettings.Tip = ST4Yardimci.OtoDonatiTip.RadyeTemel;
        ST4Yardimci.OtoDonatiSettings.UstKotM = -2.705;
        using (var form = NewForm(typeof(ST4Yardimci.OtoDonatiForm)))
        {
            var kot = Field<TextBox>(form, "_txtUstKot");
            Check(Math.Abs(ParseNumber(kot.Text) + 270.5) < 1e-8,
                "Oto Donatı eski metre ayarı cm gösterilir");
            kot.Text = "-125.5";
            Check((bool)Invoke(form, "TryPushSettings"), "Oto Donatı geçerli cm giriş");
            Check(Math.Abs(ST4Yardimci.OtoDonatiSettings.UstKotM + 1.255) < 1e-8, "Oto normal kaydet cm -> m");
            kot.Text = "300.25";
            Invoke(form, "PushFieldsQuietTo", ST4Yardimci.OtoDonatiTip.RadyeTemel);
            Check(Math.Abs(ST4Yardimci.OtoDonatiSettings.UstKotM - 3.0025) < 1e-8, "Oto sessiz kaydet cm -> m");
            Field<RadioButton>(form, "_rbMantar").Checked = true;
            Field<RadioButton>(form, "_rbRadye").Checked = true;
            Check(Math.Abs(ParseNumber(kot.Text) - 300.25) < 1e-8, "Oto profil değişiminde cm gidiş dönüş");
        }
        ST4Yardimci.SehpaDonatiSettings.Tip = ST4Yardimci.SehpaDonatiTip.RadyeTemel;
        ST4Yardimci.SehpaDonatiSettings.UstKotM = -0.805;
        using (var form = NewForm(typeof(ST4Yardimci.SehpaDonatiForm)))
        {
            var kot = Field<NumericUpDown>(form, "_numUstKot");
            Check(kot.Value == -80.5m, "Sehpa eski metre ayarı cm gösterilir");
            kot.Value = -125.5m;
            Check((bool)Invoke(form, "TryPush"), "Sehpa geçerli cm giriş");
            Check(Math.Abs(ST4Yardimci.SehpaDonatiSettings.UstKotM + 1.255) < 1e-8, "Sehpa normal kaydet cm -> m");
            kot.Value = 300.25m;
            Invoke(form, "PushQuietTo", ST4Yardimci.SehpaDonatiTip.RadyeTemel);
            Check(Math.Abs(ST4Yardimci.SehpaDonatiSettings.UstKotM - 3.0025) < 1e-8, "Sehpa sessiz kaydet cm -> m");
            Field<RadioButton>(form, "_rbMantar").Checked = true;
            Field<RadioButton>(form, "_rbRadye").Checked = true;
            Check(kot.Value == 300.25m, "Sehpa profil değişiminde cm gidiş dönüş");
        }
        Console.WriteLine("PASS kot dönüşümleri: eski ayar, normal/sessiz kayıt ve profil geçişi.");
    }
    private static double ParseNumber(string text) => double.Parse(text.Replace(',', '.'),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var c in Descendants(child)) yield return c; }
    }
    private static void Load(Form form)
    {
        form.Opacity = 0;
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        form.PerformLayout();
    }
    private static T Field<T>(Form form, string name) => (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    private static CompactLayout Layout(Form form) => (CompactLayout)typeof(MonolitForm).GetField("_compact", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    private static void Check(bool success, string name) { if (!success) throw new Exception(name); }
    private static void Snapshot(Form form, string name)
    {
        foreach (var c in Descendants(form).Where(c => c.Visible && (c is Button || c is TextBox
            || c is ComboBox || c is NumericUpDown || c is CheckBox || c is RadioButton || c is ListBox)))
        {
            if (!(c.Parent is TableLayoutPanel) && !(c.Parent is FlowLayoutPanel)) continue;
            Check(c.Left >= -1 && c.Right <= c.Parent.ClientSize.Width + 2,
                name + " yatay taşma: " + c.GetType().Name + " " + c.Text + " " + c.Bounds + " parents=" + Parents(c));
        }
        using (var image = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(_output, name + ".png")); }
    }
    private static string Parents(Control c)
    {
        string result = "";
        for (var p = c.Parent; p != null; p = p.Parent)
            result += " / " + p.GetType().Name + " " + p.Bounds + " client=" + p.ClientSize + " min=" + p.MinimumSize + " dock=" + p.Dock;
        return result;
    }
    internal static Image Icon(string file, int x0, int y0, int x1, int y1, int w, int h)
    {
        using (var atlas = new Bitmap(Path.Combine(Root, file)))
        { return atlas.Clone(Rectangle.FromLTRB(x0 * atlas.Width / w, y0 * atlas.Height / h, x1 * atlas.Width / w, y1 * atlas.Height / h), System.Drawing.Imaging.PixelFormat.Format32bppArgb); }
    }
}
