using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

// Yalnız bu önizleme exe'sinde: AutoCAD ve disk yazımı taklit edilir. Kullanıcı ayarlarına yazılmaz.
namespace Autodesk.AutoCAD.ApplicationServices
{
    public static class Application
    {
        public static object GetSystemVariable(string name) => 1;
        public static DocumentCollection DocumentManager = new DocumentCollection();
        public static void ShowModelessDialog(Form f) => f.Show();
    }
    public class DocumentCollection { public Document MdiActiveDocument = new Document(); }
    public class Document
    {
        public static Action<string> Gonderildi = k => MessageBox.Show("Önizleme: '" + k.Trim() + "' çizim komutu çalıştırılmaz.", "Önizleme");
        public void SendStringToExecute(string command, bool a, bool b, bool c) => Gonderildi(command);
    }
}

namespace ST4Yardimci
{
    static class BaIni
    {
        internal static readonly Dictionary<string, string[]> Yazilan = new Dictionary<string, string[]>();
        public static Dictionary<string, string> Read(string file) => new Dictionary<string, string>();
        public static double GetD(Dictionary<string, string> m, string k, double d) => d;
        public static string N(double d) => d.ToString(CultureInfo.InvariantCulture);
        public static void Write(string file, params string[] rows) => Yazilan[file] = rows;
    }
    static class BaFormSession
    {
        public static bool TryEnter(string key) => true;
        public static void Attach(string key, Form f) { }
        public static void EndPick(string key) { }
        public static void BeginPick(string key) { }
        public static void Release(string key) { }
    }
    static class BaPaftaOlcek
    {
        static bool _yuz;
        internal static bool Yuz(string anahtar) => _yuz;
        internal static void Kaydet(string anahtar, bool yuz) => _yuz = yuz;
        internal static RadioButton[] Secenekler(string anahtar) => new[]
        {
            new RadioButton { Text = "1/50 pafta", AutoSize = true, Checked = !_yuz },
            new RadioButton { Text = "1/100 pafta (×2)", AutoSize = true, Checked = _yuz }
        };
    }
}
