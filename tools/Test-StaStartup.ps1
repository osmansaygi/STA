param([switch]$Beykent2027)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/CommandEntry.cs'))
function Extract-Method([string]$signature) {
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
$methods = (Extract-Method 'public void Initialize()') + "`n" + (Extract-Method 'private static void OnIdleShowStaPanel(')
$fixture = @'
using System;
using System.Collections.Generic;
public class Document {
 public FakeEditor Editor = new FakeEditor();
 public List<string> Commands = new List<string>();
 public void SendStringToExecute(string command,bool a,bool b,bool c) { Commands.Add(command); }
}
public class FakeEditor { public bool IsQuiescent; public void WriteMessage(string s,params object[] args) {} }
public class Documents { public Document MdiActiveDocument; }
public static class Application {
 public static Documents DocumentManager = new Documents();
 public static event EventHandler Idle;
 public static void Tick() { Idle?.Invoke(null,EventArgs.Empty); }
}
public static class WindowsAnsiEncodings { public static void EnsureRegistered() {} }
public static class BeykentRibbonManager { public static int Initialized; public static void Initialize() { Initialized++; } }
public class StaStartupProbe {
 static int loaded = 1;
 static int CountLoadedPlanIdAssemblies() { return loaded; }
 public static string Run() {
  var probe = new StaStartupProbe();
  probe.Initialize();
  Application.Tick(); // No document: must defer without losing the subscription.
  var doc = new Document();
  Application.DocumentManager.MdiActiveDocument = doc;
  Application.Tick(); // A command is still active.
  if(doc.Commands.Count != 0) throw new Exception("Queued panel before the document was idle");
  doc.Editor.IsQuiescent = true;
  Application.Tick(); Application.Tick();
#if AUTOCAD2027
  if(doc.Commands.Count != 0 || BeykentRibbonManager.Initialized != 1) throw new Exception("Expected Ribbon initialization without native palette startup");
#else
  if(doc.Commands.Count != 1 || doc.Commands[0] != "STAPANEL ") throw new Exception("Expected exactly one deferred STAPANEL");
#endif
  loaded = 2;
  new StaStartupProbe().Initialize(); Application.Tick();
#if AUTOCAD2027
  if(doc.Commands.Count != 0 || BeykentRibbonManager.Initialized != 1) throw new Exception("Duplicate assembly initialized another ribbon");
  return "PASS: STA 2027 initializes BEYKENT once, queues no legacy palette, and rejects duplicate assembly startup.";
#else
  if(doc.Commands.Count != 1) throw new Exception("Duplicate assembly queued a second panel");
  return "PASS: STA startup defers with no document or busy editor; queues once; duplicate assembly does not queue.";
#endif
 }
'@
if ($Beykent2027) {
    Add-Type -TypeDefinition ($fixture + "`n" + $methods + "`n}") -CompilerOptions '/define:AUTOCAD2027'
} else {
    Add-Type -TypeDefinition ($fixture + "`n" + $methods + "`n}")
}
[StaStartupProbe]::Run()
