using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using ST4Yardimci;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Application;
class Program {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static RibbonTab Setup(){var ribbon=new RibbonControl();typeof(ComponentManager).GetProperty("Ribbon").SetValue(null,ribbon);var home=new RibbonTab{Id="ID_TabHome",Title="Home"};home.Panels.Add(new RibbonPanel{Source=new RibbonPanelSource{Id="ORIGINAL",Title="AutoCAD"}});ribbon.Tabs.Add(home);ribbon.Tabs.Add(new RibbonTab{Id="OTHER",Title="Insert"});home.IsActive=true;return home;}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string[] args){
  var app=new System.Windows.Application();var home=Setup();
  BaHomeRibbonManager.Initialize();AcApp.Tick();
  Check(AcApp.IdleCount==1,"no document defers install");
  var doc=new Autodesk.AutoCAD.ApplicationServices.Document();AcApp.DocumentManager.MdiActiveDocument=doc;AcApp.Tick();
  Check(doc.Commands.Count==0,"busy command does not queue ribbon UI");
  doc.Editor.IsQuiescent=true;AcApp.Tick();AcApp.Tick();BaHomeRibbonManager.Initialize();AcApp.Tick();
  Check(doc.Commands.SequenceEqual(new[]{"BARIBBONAUTO "}),"startup queues exactly one command; no UI created in NETLOAD/Idle");
  Check(home.Panels.Count==1,"original Home unchanged before command runs");
  BaHomeRibbonManager.InstallPending();BaHomeRibbonManager.Show();
  Check(ComponentManager.Ribbon.Tabs.Count==2,"no new tab; Insert preserved");
  Check(home.Panels.Count==5&&home.Panels[0].Source.Id=="ORIGINAL","four panels appended; repeated install does not duplicate or replace Home panels");
  var buttons=home.Panels.Skip(1).SelectMany(p=>p.Source.Items.OfType<RibbonRowPanel>()).SelectMany(r=>r.Items.OfType<RibbonButton>()).ToArray();
  Check(buttons.Length==20&&buttons.Select(b=>b.CommandParameter).Distinct().Count()==20,"all 20 tools exactly once");
  Check(buttons.Select(b=>(string)b.CommandParameter).OrderBy(s=>s).SequenceEqual(BaCommandIcons.Tools.Select(t=>t.Cmd).OrderBy(s=>s)),"ribbon matches all main palette commands");
  Check(home.Panels.Skip(1).Select(p=>p.Source.Title).SequenceEqual(new[]{"Ölçü","Detay","Donatı","Genel Düzenleme"}),"user group order preserved");
  foreach(var button in buttons){Check(button.Size==RibbonItemSize.Standard&&button.ShowText&&button.Image is BitmapImage image&&image.PixelWidth==16&&image.IsFrozen,"16px frozen icon + label: "+button.CommandParameter);doc.Commands.Clear();button.CommandHandler.Execute(button);Check(doc.Commands.SequenceEqual(new[]{button.CommandParameter+" "}),"click routes existing command: "+button.CommandParameter);}
  doc.Commands.Clear();AcApp.Workspace();AcApp.Tick();Check(doc.Commands.Count==0,"workspace notification does not duplicate existing UI");
  var replacement=Setup();AcApp.Workspace();AcApp.Tick();Check(doc.Commands.SequenceEqual(new[]{"BARIBBONAUTO "}),"workspace rebuilt: reinstallation queued once");
  BaHomeRibbonManager.Hide();BaHomeRibbonManager.InstallPending();Check(replacement.Panels.Count==1,"hide cancels queued auto-install and preserves original panel");
  BaHomeRibbonManager.Show();Check(replacement.Panels.Count==5,"manual restore works");
  if(args.Length>0){var ribbon=ComponentManager.Ribbon;var window=new Window{Content=ribbon,Width=1800,Height=240,Left=-20000,Top=-20000,ShowInTaskbar=false,WindowStyle=WindowStyle.None};window.Show();ribbon.ApplyTemplate();ribbon.UpdateLayout();app.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Render);var bmp=new RenderTargetBitmap(1800,240,96,96,PixelFormats.Pbgra32);bmp.Render(ribbon);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(args[0]))png.Save(stream);Console.WriteLine("Rendered template="+(ribbon.Template!=null));window.Close();}
  BaHomeRibbonManager.Shutdown();Check(AcApp.IdleCount==0,"shutdown detaches Idle without touching native ribbon");doc.Commands.Clear();AcApp.Workspace();AcApp.Tick();Check(doc.Commands.Count==0,"shutdown detaches workspace listener");
  Console.WriteLine("COMPLETE");
 }
}
