using System;
using System.Collections.Generic;
namespace Autodesk.AutoCAD.ApplicationServices {
 public class SystemVariableChangedEventArgs:EventArgs {public string Name;}
 public class Editor {public bool IsQuiescent; public void WriteMessage(string text) {}}
 public class Document {public Editor Editor=new Editor();public List<string> Commands=new List<string>(); public void SendStringToExecute(string s,bool a,bool b,bool c){Commands.Add(s);}}
 public class Documents {public Document MdiActiveDocument;}
 public static class Application {
  public static Documents DocumentManager=new Documents();
  public static event EventHandler Idle;
  public static event EventHandler<SystemVariableChangedEventArgs> SystemVariableChanged;
  public static void Tick(){Idle?.Invoke(null,EventArgs.Empty);}
  public static void Workspace(){SystemVariableChanged?.Invoke(null,new SystemVariableChangedEventArgs{Name="WSCURRENT"});}
  public static int IdleCount=>Idle?.GetInvocationList().Length??0;
 }
}
namespace ST4Yardimci {
 internal static class PluginLifecycle {internal static void LogLoadStage(string s){Console.WriteLine(s);}}
 internal static class BaIni {
  public static Dictionary<string,string> Read(string f)=>new Dictionary<string,string>();
  public static bool GetB(Dictionary<string,string> m,string key,bool fallback)=>fallback;
  public static string B(bool value)=>value?"1":"0";
  public static void Write(string f,params string[] lines){}
 }
}
