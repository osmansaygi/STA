using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace ST4Yardimci
{
    internal static partial class TemelCekuruRunner
    {
        internal static void SmokeDraw(string output)
        {
            TemelCekuruSettings.PaspayiCm = 5;
            TemelCekuruSettings.TemelUstKotuCm = 0;
            TemelCekuruSettings.HatilYuksekligiCm = 0;
            TemelCekuruSettings.SubasmanYuksekligiCm = 0;
            TemelCekuruSettings.UstTemelYuksekligiCm = 50;
            TemelCekuruSettings.AltTemelYuksekligiCm = 50;
            TemelCekuruSettings.CukurDerinligiCm = 100;
            TemelCekuruSettings.UstCapMm = TemelCekuruSettings.AltCapMm = 12;
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(Path.GetFullPath("ST4_Yardimci/TEMEL_CUKURU.dwg"), FileOpenMode.OpenForReadAndAllShare, false, null);
                db.CloseInput(true);
                var previous = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = db;
                try
                {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    Fit.FromSettings(100, 1, 15, 21, 10, 1).Apply(tr, ms);
                    tr.Commit();
                }
                db.SaveAs(output + ".dwg", DwgVersion.Current);
                db.DxfOut(output + ".dxf", 16, DwgVersion.Current);
                }
                finally { HostApplicationServices.WorkingDatabase = previous; }
            }
        }
    }
    public class Smoke
    {
        [CommandMethod("CUKURJITCHECK")]
        public void CheckJit()
        {
            string output = Path.GetFullPath("tools/CukurSmoke/jit-check.txt");
            try
            {
                var assembly = Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
                    a => a.GetName().Name == "ST4_Yardimci_2027");
                if (assembly == null) throw new System.Exception("AutoCAD 2027 production assembly is not loaded");
                int count = 0;
                File.WriteAllText(output, "Starting JIT check" + Environment.NewLine);
                foreach (string typeName in new[] { "ST4Yardimci.PluginLifecycle", "ST4Yardimci.CommandEntry", "ST4Yardimci.TemelCekuruRunner", "ST4Yardimci.TemelCekuruRunner+Fit" })
                {
                    var type = assembly.GetType(typeName, true);
                    File.AppendAllText(output, "TYPE " + type.FullName + Environment.NewLine);
                    if (type.ContainsGenericParameters) continue;
                    foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        File.AppendAllText(output, "METHOD " + method.Name + Environment.NewLine);
                        if (method.ContainsGenericParameters || method.IsAbstract || method.GetMethodBody() == null) continue;
                        File.AppendAllText(output, type.FullName + "." + method.Name + Environment.NewLine);
                        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
                        count++;
                    }
                }
                File.WriteAllText(output, "PASS: .NET 10 production methods JIT-compiled: " + count);
            }
            catch (System.Exception ex) { File.WriteAllText(output, ex.ToString()); }
        }

        [CommandMethod("CUKURSMOKE")]
        public void Run()
        {
            string output = Path.GetFullPath("tools/CukurSmoke/result");
            try { TemelCekuruRunner.SmokeDraw(output); File.WriteAllText(output + ".txt", "PASS"); }
            catch (System.Exception ex) { File.WriteAllText(output + ".txt", ex.ToString()); }
        }
    }
}
