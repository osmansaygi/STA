param([switch]$HomeOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/CommandEntry.cs'))
function Extract-StartupMethod([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Method missing: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$methods = (Extract-StartupMethod 'public void Initialize()') + "`n" + (Extract-StartupMethod 'private static void OnIdleShowBaPanel(')
$code = @'
using System;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
public class FakeEditor { public bool IsQuiescent=true; public void WriteMessage(string s) {} }
public class FakeDocument {
 public FakeEditor Editor=new FakeEditor();
 public List<string> Commands=new List<string>();
 public void SendStringToExecute(string text,bool a,bool b,bool c) {Commands.Add(text);}
}
public class FakeDocuments {public FakeDocument MdiActiveDocument;}
public static class AcApp {
 public static FakeDocuments DocumentManager=new FakeDocuments();
 public static event EventHandler Idle;
 public static void Tick() { Idle?.Invoke(null,EventArgs.Empty); }
}
public static class MachineLicense {public static bool IsAllowed() {return true;} }
public static class BaHomeRibbonCatalog {
 public static bool LegacyPaletteEnabled {
  get {
#if AUTOCAD2027
   return false;
#else
   return true;
#endif
  }
 }
}
public static class BaHomeRibbonManager {public static int Count;public static void Initialize(){Count++;}}
public class StartupProbe {
 private static Mutex _loadMutex;
 private bool _ownsInitialization;
 static void WarnIfDuplicateAssemblies(FakeEditor ed) {}
 static Assembly ResolvePluginDependency(object s,ResolveEventArgs e) {return null;}
 static void LogLoadStage(string message) {}
 public static void Run() {
  var first=new StartupProbe(); first.Initialize();
  var doc=new FakeDocument(); AcApp.DocumentManager.MdiActiveDocument=doc;
  doc.Editor.IsQuiescent=false;
  AcApp.Tick();
  if(doc.Commands.Count!=0) throw new Exception("Panel queued while NETLOAD/command is active");
  doc.Editor.IsQuiescent=true;
  AcApp.Tick(); AcApp.Tick();
  int expected=BaHomeRibbonCatalog.LegacyPaletteEnabled?1:0;
  if(doc.Commands.Count!=expected || (expected==1&&doc.Commands[0]!="BAPANEL ")) throw new Exception("Unexpected legacy palette command count");
#if AUTOCAD2027
  if(BaHomeRibbonManager.Count!=1)throw new Exception("Home ribbon initialization lost");
#endif
  new StartupProbe().Initialize(); AcApp.Tick();
  if(doc.Commands.Count!=expected) throw new Exception("Repeated initialization queued another panel");
  _loadMutex.Dispose(); _loadMutex=null;
  bool created;
  using(var other=new Mutex(false,@"Local\ST4_Yardimci_ACAD_"+System.Diagnostics.Process.GetCurrentProcess().Id,out created)) {
   var duplicate=new StartupProbe(); duplicate.Initialize();
   if(duplicate._ownsInitialization || _loadMutex!=null) throw new Exception("Duplicate DLL initialized");
   AcApp.Tick();
   if(doc.Commands.Count!=expected) throw new Exception("Duplicate DLL queued another panel");
  }
  Console.WriteLine("PASS: legacy palette commands="+expected+"; active-command deferral, repeated Initialize, duplicate DLL registration; Home ribbon preserved.");
 }
'@
$code += "`n" + $methods + "`n}"
if ($HomeOnly) {
    Add-Type -TypeDefinition $code -CompilerOptions '/define:AUTOCAD2027' -WarningAction SilentlyContinue
} else {
    Add-Type -TypeDefinition $code -WarningAction SilentlyContinue
}
[StartupProbe]::Run()
