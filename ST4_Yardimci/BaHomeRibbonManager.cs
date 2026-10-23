#if BA_HOME_RIBBON
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>Yalnız Home'a eklenen geçici Yardımcı panelleri; CUI/çalışma alanı dosyası yazılmaz.</summary>
    internal static class BaHomeRibbonManager
    {
        private static bool _initialized, _enabled, _pending, _installing, _commandQueued;
        private static readonly Dictionary<string, ImageSource> Images = new Dictionary<string, ImageSource>();
        private static readonly ICommand Handler = new RibbonCommandHandler();

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _enabled = true;
            ComponentManager.ItemInitialized += OnItemInitialized;
            AcApp.SystemVariableChanged += OnSystemVariableChanged;
            RequestInstall();
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (!_installing) RequestInstall();
        }

        private static void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) RequestInstall();
        }

        private static void RequestInstall()
        {
            if (!_enabled || _pending || _commandQueued) return;
            _pending = true;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !doc.Editor.IsQuiescent) return;
                CancelPending();
                // No perpetual idle polling when RIBBON is closed or Home is absent.
                // ItemInitialized/WSCURRENT will request a retry when the UI returns.
                var home = FindHome(ComponentManager.Ribbon);
                if (home != null && NeedsInstall(home))
                {
                    _commandQueued = true;
                    doc.SendStringToExecute("BARIBBONAUTO ", true, false, false);
                }
            }
            catch (System.Exception ex)
            {
                CancelPending();
                _commandQueued = false;
                PluginLifecycle.LogLoadStage("Home ribbon queue: " + ex.Message);
            }
        }

        private static void CancelPending()
        {
            AcApp.Idle -= OnIdle;
            _pending = false;
        }

        private static RibbonTab FindHome(RibbonControl ribbon)
        {
            if (ribbon == null) return null;
            foreach (RibbonTab tab in ribbon.Tabs)
                if (string.Equals(tab.Id, "ID_TabHome", StringComparison.OrdinalIgnoreCase)) return tab;
            foreach (RibbonTab tab in ribbon.Tabs)
                if (string.Equals(tab.Id, "ID_TabHome3D", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tab.Title, "Home", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tab.Title, "Giriş", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tab.Title, "Başlangıç", StringComparison.OrdinalIgnoreCase)) return tab;
            return null;
        }

        private static bool NeedsInstall(RibbonTab home)
        {
            foreach (var group in BaHomeRibbonCatalog.Groups)
            {
                bool found = false;
                foreach (RibbonPanel panel in home.Panels)
                    if (panel.Source?.Id == group.Id) { found = true; break; }
                if (!found) return true;
            }
            return false;
        }

        // Invoked only in the BARIBBON command context, never during NETLOAD/Idle.
        internal static void Show()
        {
            if (!_initialized) Initialize();
            _enabled = true;
            _commandQueued = false;
            CancelPending();
            var home = FindHome(ComponentManager.Ribbon);
            if (home == null)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nHome sekmesi bulunamadı. RIBBON ile şeridi açın; sonra BARIBBON yazın.");
                return;
            }
            _installing = true;
            try
            {
                foreach (var group in BaHomeRibbonCatalog.Groups)
                {
                    bool exists = false;
                    foreach (RibbonPanel panel in home.Panels)
                        if (panel.Source?.Id == group.Id) { exists = true; break; }
                    if (exists) continue;

                    // Build the full panel before attaching; existing Home panels stay in place.
                    var source = new RibbonPanelSource { Id = group.Id, Title = group.Title };
                    for (int column = 0; column < group.Commands.Length; column += 3)
                    {
                        var rows = new RibbonRowPanel();
                        for (int row = 0; row < 3 && column + row < group.Commands.Length; row++)
                        {
                            if (row > 0) rows.Items.Add(new RibbonRowBreak());
                            rows.Items.Add(CreateButton(group.Commands[column + row]));
                        }
                        source.Items.Add(rows);
                    }
                    // AutoCAD draws the normal vertical separator between native ribbon panels.
                    home.Panels.Add(new RibbonPanel { Source = source });
                }
            }
            finally { _installing = false; }
        }

        internal static void InstallPending()
        {
            _commandQueued = false;
            if (_enabled) Show();
        }

        private static RibbonButton CreateButton(string command)
        {
            foreach (var tool in BaCommandIcons.Tools)
            {
                if (tool.Cmd != command) continue;
                return new RibbonButton
                {
                    Id = "BEYKENT_YARDIMCI_" + command,
                    Text = tool.Label,
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Standard,
                    Orientation = Orientation.Horizontal,
                    Image = GetImage(tool),
                    CommandParameter = command,
                    CommandHandler = Handler
                };
            }
            throw new InvalidOperationException("Yardımcı komutu bulunamadı: " + command);
        }

        private static ImageSource GetImage(BaTool tool)
        {
            if (Images.TryGetValue(tool.Cmd, out var cached)) return cached;
            using (var bitmap = BaCommandIcons.Load(tool.Cmd, tool.Resource))
            {
                if (bitmap == null) return null;
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 16;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    Images[tool.Cmd] = image;
                    return image;
                }
            }
        }

        internal static void Hide()
        {
            _enabled = false;
            CancelPending();
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            foreach (RibbonTab tab in ribbon.Tabs)
                for (int i = tab.Panels.Count - 1; i >= 0; i--)
                    foreach (var group in BaHomeRibbonCatalog.Groups)
                        if (tab.Panels[i].Source?.Id == group.Id) { tab.Panels.RemoveAt(i); break; }
        }

        internal static void Shutdown()
        {
            _enabled = false;
            CancelPending();
            if (_initialized)
            {
                ComponentManager.ItemInitialized -= OnItemInitialized;
                AcApp.SystemVariableChanged -= OnSystemVariableChanged;
            }
            _initialized = false;
            _commandQueued = false;
            // Do not access native ribbon/window objects while AutoCAD is terminating.
            Images.Clear();
        }

        private sealed class RibbonCommandHandler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => AcApp.DocumentManager.MdiActiveDocument != null;
            public void Execute(object parameter)
            {
                if (!(parameter is RibbonButton button) || !(button.CommandParameter is string command)) return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                foreach (var tool in BaCommandIcons.Tools)
                    if (tool.Cmd == command)
                    {
                        // Use the same command entry/gates as the existing palette.
                        doc.SendStringToExecute(command + " ", true, false, false);
                        return;
                    }
            }
        }
    }
}
#endif
