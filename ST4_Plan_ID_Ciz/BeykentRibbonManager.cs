#if AUTOCAD2027
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

namespace ST4PlanIdCiz
{
    internal static class BeykentRibbonManager
    {
        private static bool _initialized, _enabled, _pending, _queued, _installing, _refresh;
        private static readonly Dictionary<string, ImageSource> Images = new Dictionary<string, ImageSource>();
        private static readonly ICommand Handler = new CommandHandler();

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = _enabled = true;
            ComponentManager.ItemInitialized += OnItemInitialized;
            AcApp.SystemVariableChanged += OnSystemVariableChanged;
            StaMonolitIcons.Changed += OnIconsChanged;
            RequestInstall();
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        { if (!_installing) RequestInstall(); }
        private static void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        { if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) RequestInstall(); }
        private static void OnIconsChanged() { _refresh = true; RequestInstall(); }
        private static void RequestInstall()
        {
            if (!_enabled || _pending || _queued) return;
            _pending = true;
            AcApp.Idle += OnIdle;
        }
        private static void CancelPending() { AcApp.Idle -= OnIdle; _pending = false; }
        private static RibbonTab FindTab(RibbonControl ribbon)
        {
            if (ribbon != null)
                foreach (RibbonTab tab in ribbon.Tabs)
                    if (tab.Id == BeykentRibbonCatalog.TabId) return tab;
            return null;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !doc.Editor.IsQuiescent) return;
                CancelPending();
                var ribbon = ComponentManager.Ribbon;
                if (ribbon != null && (_refresh || FindTab(ribbon) == null))
                {
                    _queued = true;
                    doc.SendStringToExecute("BEYKENTAUTO ", true, false, false);
                }
            }
            catch (System.Exception) { CancelPending(); _queued = false; }
        }

        // Auto-install preserves the current tab. Only the explicit BEYKENT command selects it.
        internal static void Show(bool activate)
        {
            if (!_initialized) Initialize();
            _enabled = true;
            _queued = false;
            CancelPending();
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nŞerit kapalı. RIBBON komutuyla açın, ardından BEYKENT yazın.");
                return;
            }
            _installing = true;
            try
            {
                var tab = FindTab(ribbon);
                if (tab == null || _refresh)
                {
                    bool wasActive = tab != null && tab.IsActive;
                    Images.Clear();
                    // Build completely before touching the attached tab; leave a working tab on failure.
                    var replacement = new RibbonTab { Id = BeykentRibbonCatalog.TabId, Title = "BEYKENT" };
                    foreach (var group in BeykentRibbonCatalog.Groups)
                    {
                        var source = new RibbonPanelSource { Id = group.Id, Title = group.Title };
                        foreach (var tool in group.Tools) source.Items.Add(CreateButton(tool));
                        replacement.Panels.Add(new RibbonPanel { Source = source });
                    }
                    int index = tab == null ? ribbon.Tabs.Count : ribbon.Tabs.IndexOf(tab);
                    if (tab != null) ribbon.Tabs.Remove(tab);
                    ribbon.Tabs.Insert(index, replacement);
                    tab = replacement;
                    _refresh = false;
                    if (wasActive) tab.IsActive = true;
                }
                if (activate) tab.IsActive = true;
            }
            finally { _installing = false; }
        }

        internal static void InstallPending() { _queued = false; if (_enabled) Show(false); }
        private static RibbonButton CreateButton(BeykentRibbonCatalog.Tool tool)
        {
            var image = GetImage(tool.Command);
            return new RibbonButton
            {
                Id = "BEYKENT_PLAN_BUTTON_" + tool.Command, Text = tool.Label,
                ShowText = true, ShowImage = true, Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical, Image = image, LargeImage = image,
                CommandParameter = tool.Command, CommandHandler = Handler
            };
        }
        private static ImageSource GetImage(string command)
        {
            if (Images.TryGetValue(command, out var cached)) return cached;
            // Same personal override / Monolit / classic priority as the existing Plan palette.
            using (var bitmap = StaToolIcons.ForCommand(command))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 32;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                Images[command] = image;
                return image;
            }
        }
        internal static void Hide()
        {
            _enabled = false;
            CancelPending();
            var ribbon = ComponentManager.Ribbon;
            var tab = FindTab(ribbon);
            if (tab != null) ribbon.Tabs.Remove(tab);
        }
        internal static void Shutdown()
        {
            _enabled = false;
            CancelPending();
            if (_initialized)
            {
                ComponentManager.ItemInitialized -= OnItemInitialized;
                AcApp.SystemVariableChanged -= OnSystemVariableChanged;
                StaMonolitIcons.Changed -= OnIconsChanged;
            }
            _initialized = _queued = _refresh = false;
            Images.Clear();
            // Native ribbon/window objects must not be touched while AutoCAD is terminating.
        }
        private sealed class CommandHandler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => AcApp.DocumentManager.MdiActiveDocument != null;
            public void Execute(object parameter)
            {
                if (!(parameter is RibbonButton button) || !(button.CommandParameter is string command)
                    || !BeykentRibbonCatalog.Contains(command)) return;
                AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(command + " ", true, false, false);
            }
        }
    }
}
#endif
