using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using ST4PlanIdCiz;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

class Program
{
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static RibbonTab Setup()
    {
        var ribbon = new RibbonControl();
        typeof(ComponentManager).GetProperty("Ribbon").SetValue(null, ribbon);
        var home = new RibbonTab { Id = "ID_TabHome", Title = "Home" };
        home.Panels.Add(new RibbonPanel { Source = new RibbonPanelSource { Id = "EXISTING_YARDIMCI", Title = "Yardımcı" } });
        ribbon.Tabs.Add(home);
        ribbon.Tabs.Add(new RibbonTab { Id = "INSERT", Title = "Insert" });
        ribbon.Tabs.Add(new RibbonTab { Id = "FOREIGN", Title = "BEYKENT" });
        home.IsActive = true;
        return home;
    }
    static RibbonTab OwnTab() => ComponentManager.Ribbon.Tabs.FirstOrDefault(t => t.Id == BeykentRibbonCatalog.TabId);
    [STAThread] static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
    static void Run(string[] args)
    {
        var app = new System.Windows.Application();
        var home = Setup();
        BeykentRibbonManager.Initialize(); AcApp.Tick();
        Check(AcApp.IdleCount == 1, "no document defers installation");
        var doc = new Autodesk.AutoCAD.ApplicationServices.Document();
        AcApp.DocumentManager.MdiActiveDocument = doc; AcApp.Tick();
        Check(doc.Commands.Count == 0, "busy document does not queue UI");
        doc.Editor.IsQuiescent = true;
        AcApp.Tick(); AcApp.Tick(); BeykentRibbonManager.Initialize(); AcApp.Tick();
        Check(doc.Commands.SequenceEqual(new[] { "BEYKENTAUTO " }) && OwnTab() == null,
            "one queued command; NETLOAD and Idle do not create the ribbon");
        BeykentRibbonManager.InstallPending(); BeykentRibbonManager.Show(false);
        var tab = OwnTab();
        Check(ComponentManager.Ribbon.Tabs.Count == 4 && home.IsActive && home.Panels.Count == 1,
            "one new tab, existing Home/Yardimci and active tab preserved");
        Check(tab.Panels.Select(p => p.Source.Title).SequenceEqual(new[] { "Temel", "Kolon", "Kiriş", "Yapı", "Detay / İskele", "Metraj" }),
            "six groups follow the approved option 4 order");
        var buttons = tab.Panels.SelectMany(p => p.Source.Items.OfType<RibbonButton>()).ToArray();
        Check(buttons.Length == 19 && buttons.Select(b => b.CommandParameter).Distinct().Count() == 19,
            "19 distinct existing commands");
        foreach (var button in buttons)
        {
            Check(button.Size == RibbonItemSize.Large && button.ShowText && button.LargeImage is BitmapImage image
                && image.PixelWidth == 32 && image.IsFrozen, "32px cached icon and label: " + button.CommandParameter);
            doc.Commands.Clear(); button.CommandHandler.Execute(button);
            Check(doc.Commands.SequenceEqual(new[] { button.CommandParameter + " " }), "existing command routing: " + button.CommandParameter);
        }
        doc.Commands.Clear();
        var originalCommand = buttons[0].CommandParameter;
        buttons[0].CommandParameter = "_ERASE"; buttons[0].CommandHandler.Execute(buttons[0]);
        Check(doc.Commands.Count == 0, "unknown command rejected by handler");
        buttons[0].CommandParameter = originalCommand;
        AcApp.DocumentManager.MdiActiveDocument = null;
        Check(!buttons[0].CommandHandler.CanExecute(buttons[0]), "no document disables command execution");
        AcApp.DocumentManager.MdiActiveDocument = doc;
        AcApp.Workspace(); AcApp.Tick();
        Check(doc.Commands.Count == 0, "workspace event does not duplicate an existing tab");

        // Trigger the actual notification without writing the user's icon preference file.
        var changed = (Action)typeof(StaMonolitIcons).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        changed?.Invoke(); AcApp.Tick(); BeykentRibbonManager.InstallPending();
        Check(!ReferenceEquals(tab, OwnTab()) && home.IsActive && ComponentManager.Ribbon.Tabs.Count == 4,
            "icon refresh replaces only the owned tab and preserves selection");
        doc.Commands.Clear();
        var replacementHome = Setup(); AcApp.Workspace(); AcApp.Tick();
        Check(doc.Commands.SequenceEqual(new[] { "BEYKENTAUTO " }), "workspace rebuild requests a new tab once");
        BeykentRibbonManager.Hide(); BeykentRibbonManager.InstallPending();
        Check(OwnTab() == null && ComponentManager.Ribbon.Tabs.Count == 3,
            "hide cancels queued installation and keeps foreign tabs even with the same title");
        BeykentRibbonManager.Show(true);
        Check(OwnTab().IsActive, "manual BEYKENT selects the tab");
        BeykentRibbonManager.Hide(); BeykentRibbonManager.Show(false);
        Check(ComponentManager.Ribbon.Tabs.Count == 4 && replacementHome.Panels.Count == 1, "hide/show remains idempotent");
        if (args.Length > 0)
        {
            ComponentManager.Ribbon.Tabs.Remove(ComponentManager.Ribbon.Tabs.First(t => t.Id == "FOREIGN"));
            OwnTab().IsActive = true;
            var ribbon = ComponentManager.Ribbon;
            var window = new Window { Content = ribbon, Width = 1800, Height = 230, Left = -20000, Top = -20000,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show(); ribbon.ApplyTemplate(); ribbon.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap(1800, 230, 96, 96, PixelFormats.Pbgra32); bitmap.Render(ribbon);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(args[0])) encoder.Save(stream);
            window.Close();
        }
        BeykentRibbonManager.Shutdown();
        Check(AcApp.IdleCount == 0, "shutdown removes idle subscription");
        Setup(); AcApp.Workspace(); AcApp.Tick(); BeykentRibbonManager.InstallPending();
        Check(OwnTab() == null, "shutdown removes workspace subscription and prevents late install");
        BeykentRibbonManager.Initialize();
        typeof(ComponentManager).GetProperty("Ribbon").SetValue(null, null);
        AcApp.Tick();
        Check(AcApp.IdleCount == 0, "closed ribbon does not leave perpetual idle polling");
        doc.Commands.Clear(); Setup(); AcApp.Workspace(); AcApp.Tick(); BeykentRibbonManager.InstallPending();
        Check(OwnTab() != null, "ribbon reopening recovers through workspace event");
        BeykentRibbonManager.Shutdown();
        Console.WriteLine("COMPLETE: actual AdWindows classes, production Plan icon provider, independent host fixture.");
    }
}
