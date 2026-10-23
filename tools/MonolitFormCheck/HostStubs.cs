using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public class SystemVariableChangedEventArgs : EventArgs { public string Name; }
    public static class Application
    {
        private static EventHandler<SystemVariableChangedEventArgs> _changed;
        public static int Subscribers;
        public static int Theme;
        public static bool FailRead;
        public static event EventHandler<SystemVariableChangedEventArgs> SystemVariableChanged
        { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public static object GetSystemVariable(string name) { if (FailRead) throw new Exception("host kapanışı"); return Theme; }
        public static void SetTheme(int value) { Theme = value; _changed?.Invoke(null, new SystemVariableChangedEventArgs { Name = "COLORTHEME" }); }
        public static DocumentCollection DocumentManager = new DocumentCollection();
        public static DialogResult ShowModalDialog(Form f) => DialogResult.Cancel;
        public static void ShowModelessDialog(Form f) { }
    }
    public class DocumentCollection { public Document MdiActiveDocument = new Document(); }
    public class Document
    {
        public string Name = "detached-test.dwg";
        public Editor Editor = new Editor();
        public Autodesk.AutoCAD.DatabaseServices.Database Database = new Autodesk.AutoCAD.DatabaseServices.Database();
        public IDisposable LockDocument() => new EmptyScope();
        public void SendStringToExecute(string command, bool a, bool b, bool c) { throw new Exception("Çizim komutu testte çalıştırılmamalı."); }
    }
    public class Editor { public void WriteMessage(string message) { } }
    public class EmptyScope : IDisposable { public void Dispose() { } }
}
namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum OpenMode { ForRead }
    public struct ObjectId { }
    public class Database { public TransactionManager TransactionManager = new TransactionManager(); public ObjectId DimStyleTableId; }
    public class TransactionManager { public Transaction StartTransaction() => new Transaction(); }
    public class Transaction : IDisposable
    {
        int _reads;
        public object GetObject(ObjectId id, OpenMode mode) => _reads++ == 0 ? (object)new DimStyleTable { new ObjectId() } : new DimStyleTableRecord();
        public void Commit() { } public void Dispose() { }
    }
    public class DimStyleTable : List<ObjectId> { }
    public class DimStyleTableRecord { public string Name = "PLAN_OLCU_50"; }
}
namespace ST4AksCizCSharp
{
    public class St4Model { public List<FloorInfo> Floors = new List<FloorInfo>(); }
    public class FloorInfo { public string Name, ShortName; public double ElevationM; public int FloorNo; }
}
namespace ST4PlanIdCiz
{
    internal static class TemelIlaveKutuBol { internal static double MinDilimGenislikCm = 120; }
    internal static class TemelIlaveDenemeCommands { internal static double TemelPaspayCm, DuseyHizaFarkCm; }
    internal static class StaMonolitIcons
    {
        internal static Image TryLoad(string command) => Smoke.Icon("ST4_Plan_ID_Ciz/Icons/sta_monolit_atlas.png", 573, 10, 739, 281, 1024, 1536);
    }
}

internal static class TestStore
{
    internal static DateTime GetLastWriteTimeUtc(string path) => DateTime.MinValue;
    internal static System.IO.Stream OpenRead(string path) => new System.IO.MemoryStream();
    internal static System.IO.Stream Create(string path) => new System.IO.MemoryStream();
    internal static void Delete(string path) { }
    internal static void Move(string source, string destination) { }
    internal static bool Exists(string path) => false;
    internal static string[] ReadAllLines(string path, params object[] options) => new string[0];
    internal static string ReadAllText(string path, params object[] options) => "";
    internal static byte[] ReadAllBytes(string path) => new byte[0];
    internal static void WriteAllText(string path, string text, params object[] options) { }
    internal static void WriteAllLines(string path, IEnumerable<string> lines, params object[] options) { }
    internal static System.IO.DirectoryInfo CreateDirectory(string path) => new System.IO.DirectoryInfo(path);
}

namespace Autodesk.AutoCAD.Colors
{
    public enum ColorMethod { ByAci, ByColor }
    public class Color
    {
        public ColorMethod ColorMethod; public short ColorIndex;
        public byte Red, Green, Blue;
        public System.Drawing.Color ColorValue => System.Drawing.Color.FromArgb(Red, Green, Blue);
    }
}
namespace Autodesk.AutoCAD.Windows
{
    public class ColorDialog
    {
        public Autodesk.AutoCAD.Colors.Color Color;
        public bool IncludeByBlockByLayer;
        public DialogResult ShowDialog() => DialogResult.Cancel;
    }
}
namespace BeykentOrtak
{
    internal static class BeykentStilUygula
    {
        internal static readonly int[] GecerliKalinliklar = { 0, 13, 18, 25, 30, 35, 40, 50 };
        internal static Autodesk.AutoCAD.Colors.Color Renk(KatmanAyar k) => new Autodesk.AutoCAD.Colors.Color();
    }
}
namespace ST4Yardimci
{
    internal static class YardimciUi { internal static void StripUpDownButtons(NumericUpDown number) { } }
    internal static class KotDegistirRunner { internal static bool TryParseKot(string text, out double result) => double.TryParse(text, out result); }
    internal static class DosemeDonatiRunner
    {
        internal enum DonatiYonu { Serbest, X, Y }
        internal static DonatiYonu Yon;
        internal static void EnsureYonLoaded() { }
        internal static void SaveYon() { }
    }
    internal static class BeykentKatmanYukle { internal static string YukleAktifCizim() => "detached"; }
    internal sealed class RadyeOtomatikSource : IDisposable
    {
        internal int PlateCount = 0, NodeCount = 0, ElementCount = 0;
        internal List<string> Bars = new List<string>();
        internal static RadyeOtomatikSource Open(string file) => new RadyeOtomatikSource();
        public void Dispose() { }
    }
    internal sealed class RadyeRecordedHeatmap
    {
        internal List<int> Jobs = new List<int>();
        internal static RadyeRecordedHeatmap Read(RadyeOtomatikSource source) => new RadyeRecordedHeatmap();
    }
}
namespace ST4Yardimci
{
    internal enum OtoOlcuLayerList { Birinci, Ikinci }
    internal enum OtoOlcuSablon { TekOlcu, CiftOlcu }
    internal static class OtoOlcuSettings
    {
        internal static string ActivePresetName = "Standart", DimStyleName = "PLAN_OLCU_50";
        internal const string DefaultDimStyleName = "PLAN_OLCU_50";
        internal static OtoOlcuSablon Sablon;
        internal static bool IkinciOlcuToplam;
        internal static bool IsAksOlcuPreset => ActivePresetName == "AKS OLCU";
        internal static OtoOlcuLayerList LayerPickTarget;
        internal static void EnsureLoaded() { } internal static void Save() { }
        internal static List<string> GetLayers(OtoOlcuLayerList which) => new List<string> { "AKS (BEYKENT)", "KOLON (BEYKENT)" };
        internal static List<string> GetPresetNames() => new List<string> { "Standart", "AKS OLCU" };
        internal static void SetLayers(OtoOlcuLayerList which, IEnumerable<string> layers) { }
        internal static bool SelectPreset(string name) { ActivePresetName = name; return true; }
        internal static bool IsLockedPreset(string name) => name == "AKS OLCU";
        internal static bool AddPreset(string name) => true;
        internal static bool RemovePreset(string name) => true;
        internal static void RemoveLayer(OtoOlcuLayerList which, string name) { }
    }
    internal static class OtoOlcuDrawingBootstrap { internal static void EnsureForActiveDocument() { } }
    internal static class BaFormSession
    {
        internal static bool TryEnter(string command) => true;
        internal static void Attach(string command, Form form) { }
        internal static void BeginPick(string command) { }
        internal static void EndPick(string command) { }
        internal static void Release(string command) { }
        internal static DialogResult ShowChildDialog(Form parent, Form child) => DialogResult.Cancel;
    }
    internal static class BaCommandIcons
    {
        internal static Image Load(string command, string resource) => Smoke.Icon("ST4_Yardimci/Icons/monolit_atlas.png", 15, 25, 270, 280, 1122, 1402);
    }
}
