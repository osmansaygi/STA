using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;

public class PlanFormMemoryProbe
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly Plan;
    static string Dir => Environment.GetEnvironmentVariable("PF_DIR");
    static string Phase => Environment.GetEnvironmentVariable("PF_PHASE");
    static string Store => Path.Combine(Dir, "settings-test");
    static void Log(string value) => File.AppendAllText(Path.Combine(Dir, Phase + ".txt"), value + Environment.NewLine);
    static void Check(bool ok, string text) { if (!ok) throw new System.Exception(text); Log("PASS " + text); }
    static Type T(string name) => Plan.GetType("ST4PlanIdCiz." + name, true);
    static Form Form(string name, params object[] args) => (Form)Activator.CreateInstance(T(name), All & ~BindingFlags.Static, null, args, null);
    static C Field<C>(object o, string name) => (C)o.GetType().GetField(name, All).GetValue(o);
    static IEnumerable<Control> Children(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Children(x)));
    static void Close(Form form, string button)
    {
        form.Opacity = 0; form.Show(); Application.DoEvents();
        Children(form).OfType<Button>().Single(b => b.Text == button).PerformClick();
        Check(form.DialogResult == (button == "İptal" ? DialogResult.Cancel : DialogResult.OK), form.GetType().Name + " " + button);
    }
    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Store, file + ".ini"))));

    [CommandMethod("PF_TEST")]
    public static void Run()
    {
        try
        {
            Plan = AppDomain.CurrentDomain.GetAssemblies().Last(a => a.GetName().Name.StartsWith("ST4_Plan_ID_Ciz"));
            T("PlanFormSettings").GetField("_directory", All).SetValue(null, Store);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(Phase == "write" ? "en-US" : "tr-TR");
            Log("Plan=" + Plan.Location + "; culture=" + CultureInfo.CurrentCulture.Name);
            var parser = Activator.CreateInstance(Plan.GetType("ST4AksCizCSharp.St4Parser"));
            var model = parser.GetType().GetMethod("Parse", new[] { typeof(string) }).Invoke(parser,
                new object[] { Path.GetFullPath(Path.Combine(Dir, "../KirisAralik20261028/input/CK_20827_2_01.st4")) });
            if (Phase == "write") Write(model);
            else if (Phase == "read") Read(model);
            else Corrupt(model);
            Log("COMPLETE");
        }
        catch (System.Exception ex) { Log("FAIL " + ex); }
    }

    static void Write(object model)
    {
        Directory.CreateDirectory(Store);
        // Önceki sürümün ham INI dosyalarını yeni kayıt kodu okuyabilmeli.
        File.WriteAllText(Path.Combine(Store, "kolondusey.ini"), "Olcek25=true\nKatModu=2\nKatSinirla=1\nSonKatAdi=2. KAT\n");
        File.WriteAllText(Path.Combine(Store, "kapamaperde.ini"), "TemelHatili=false\n");
        using (var f = Form("KirisAcilimForm", model))
        {
            Check(Field<NumericUpDown>(f, "_yatayNet").Value == 4 && Field<NumericUpDown>(f, "_duseyNet").Value == 3 && Field<NumericUpDown>(f, "_paspayi").Value == 4, "fresh beam defaults 4/3/4");
            Children(f).OfType<RadioButton>().Single(c => c.Text == "Antetsiz").Checked = true;
            Children(f).OfType<RadioButton>().Single(c => c.Text == "Seçili kat").Checked = true;
            Field<ComboBox>(f, "_kat").SelectedIndex = 1;
            Field<CheckBox>(f, "_ikinciGonye").Checked = true;
            Field<NumericUpDown>(f, "_yatayNet").Value = 6.5m;
            Field<NumericUpDown>(f, "_duseyNet").Value = 4.5m;
            Field<NumericUpDown>(f, "_paspayi").Value = 5.5m;
            Close(f, "Çiz");
        }
        using (var f = Form("SurekliTemelAcilimForm"))
        {
            Children(f).OfType<RadioButton>().Single(c => c.Text == "Antetsiz").Checked = true;
            Children(f).OfType<RadioButton>().Single(c => c.Text == "Her temel ayrı").Checked = true;
            Close(f, "Çiz");
        }
        using (var f = Form("KolonApplicationForm"))
        {
            foreach (var c in Children(f).OfType<CheckBox>()) c.Checked = c.Text.Contains("açılımı ekle");
            Close(f, "Çiz");
        }
        using (var f = Form("TemelIlaveDenemeForm"))
        {
            Field<NumericUpDown>(f, "_genislik").Value = 240;
            Field<NumericUpDown>(f, "_paspay").Value = 6.5m;
            Field<NumericUpDown>(f, "_hiza").Value = 150;
            Close(f, "Çiz");
        }
        using (var f = Form("KolonDuseyForm"))
        {
            Check(Field<ComboBox>(f, "_cmbOlcek").SelectedIndex == 1 && Field<CheckBox>(f, "_chkBenzer").Checked, "legacy column INI retained");
            Field<CheckBox>(f, "_chkAyri").Checked = true;
            Close(f, "Çiz");
        }
        using (var f = RangeForm())
        {
            Check(Field<ComboBox>(f, "_cmbSon").SelectedIndex == 2, "legacy upper floor name retained");
            Field<ComboBox>(f, "_cmbSon").SelectedIndex = 1;
            Close(f, "Tamam");
        }
        using (var f = Form("KapamaPerdeForm"))
        {
            Check(Field<RadioButton>(f, "_rbHatiliYok").Checked, "legacy wall INI retained");
            Close(f, "Çiz");
        }
    }

    static Form RangeForm() => Form("KolonDuseyKatSinirForm", new List<(int, string, double)>
    { (0, "ZEMİN", 0), (1, "1. KAT", 3), (2, "2. KAT", 6) });

    static void Read(object model)
    {
        using (var f = Form("KirisAcilimForm", model))
        {
            Check(!Field<RadioButton>(f, "_antetli").Checked && !Field<RadioButton>(f, "_tum").Checked && Field<ComboBox>(f, "_kat").SelectedIndex == 1 && Field<CheckBox>(f, "_ikinciGonye").Checked, "beam choices restored in fresh process");
            Check(Field<NumericUpDown>(f, "_yatayNet").Value == 6.5m && Field<NumericUpDown>(f, "_duseyNet").Value == 4.5m && Field<NumericUpDown>(f, "_paspayi").Value == 5.5m, "beam decimals restored across culture");
            string hash = Hash("kirisacilim");
            Field<NumericUpDown>(f, "_yatayNet").Value = 2;
            Close(f, "İptal"); Check(Hash("kirisacilim") == hash, "cancel preserves confirmed beam choices");
        }
        using (var f = Form("SurekliTemelAcilimForm"))
        {
            Check(!Field<RadioButton>(f, "_antetli").Checked && !Field<RadioButton>(f, "_birlestir").Checked, "foundation choices restored");
            string hash = Hash("sureklitemelacilim"); Close(f, "İptal"); Check(hash == Hash("sureklitemelacilim"), "foundation cancel unchanged");
        }
        using (var f = Form("KolonApplicationForm"))
        {
            Check(Children(f).OfType<CheckBox>().All(c => c.Checked == c.Text.Contains("açılımı ekle")), "all four application choices restored");
            string hash = Hash("kolonaplikasyon"); Close(f, "İptal"); Check(hash == Hash("kolonaplikasyon"), "application cancel unchanged");
        }
        using (var f = Form("TemelIlaveDenemeForm"))
        {
            Check(Field<NumericUpDown>(f, "_genislik").Value == 240 && Field<NumericUpDown>(f, "_paspay").Value == 6.5m && Field<NumericUpDown>(f, "_hiza").Value == 150, "foundation supplement choices restored");
            string hash = Hash("temelilave"); Close(f, "İptal"); Check(hash == Hash("temelilave"), "supplement cancel unchanged");
        }
        using (var f = Form("KolonDuseyForm"))
        {
            Check(Field<ComboBox>(f, "_cmbOlcek").SelectedIndex == 1 && Field<CheckBox>(f, "_chkAyri").Checked && Field<CheckBox>(f, "_chkSinirla").Checked, "column scale/mode/limit restored");
            string hash = Hash("kolondusey"); Close(f, "İptal"); Check(hash == Hash("kolondusey"), "column cancel unchanged");
        }
        using (var f = RangeForm())
        {
            Check(Field<ComboBox>(f, "_cmbSon").SelectedIndex == 1, "upper floor restored");
            string hash = Hash("kolondusey"); Field<ComboBox>(f, "_cmbSon").SelectedIndex = 0; Close(f, "İptal"); Check(hash == Hash("kolondusey"), "upper floor cancel unchanged");
        }
        using (var f = Form("KapamaPerdeForm"))
        {
            Check(Field<RadioButton>(f, "_rbHatiliYok").Checked, "wall choice restored");
            string hash = Hash("kapamaperde"); Close(f, "İptal"); Check(hash == Hash("kapamaperde"), "wall cancel unchanged");
        }
        var floors = (IList)model.GetType().GetProperty("Floors").GetValue(model);
        var first = floors[0]; floors.Clear(); floors.Add(first);
        using (var f = Form("KirisAcilimForm", model)) Check(Field<ComboBox>(f, "_kat").SelectedIndex == 0, "missing saved floor falls back to a valid current floor");
    }

    static void Corrupt(object model)
    {
        File.WriteAllText(Path.Combine(Store, "kirisacilim.ini"), "YatayNetBoslukCm=NaN\nDuseyNetBoslukCm=999\nPaspayiCm=-2\nAntetli=invalid\n");
        using (var f = Form("KirisAcilimForm", model))
            Check(Field<NumericUpDown>(f, "_yatayNet").Value == 4 && Field<NumericUpDown>(f, "_duseyNet").Value == 3 && Field<NumericUpDown>(f, "_paspayi").Value == 4, "invalid/out-of-range numbers safely fall back");
        // Bütünü bozulmuş dosyada son atomik kayıt yedeği okunur.
        File.WriteAllText(Path.Combine(Store, "sureklitemelacilim.ini.bak"), "Antetli=0\nBirlestir=0\n");
        File.WriteAllBytes(Path.Combine(Store, "sureklitemelacilim.ini"), new byte[30]);
        using (var f = Form("SurekliTemelAcilimForm")) Check(!Field<RadioButton>(f, "_antetli").Checked && !Field<RadioButton>(f, "_birlestir").Checked, "corrupt file recovers backup");
    }
}
