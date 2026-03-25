using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    internal static class CommandPaletteManager
    {
        private static PaletteSet _palette;

        /// <summary>
        /// Yansıma başarısız olsa da panelde görünmesi gereken STA komutları (NETLOAD sonrası yenileme ile güncellenir).
        /// </summary>
        private static readonly string[] StaCommandsFallback =
        {
            "ST4PLANID", "KOLONDATA", "ST4KESIT", "TEMEL50ST4", "TEMEL100ST4",
            "KOLON50ST4", "KOLON100ST4", "KALIP50ST4", "KALIP100ST4", "ISKELECIZ"
        };

        /// <summary>
        /// Her çağrıda paneli güncel komut listesiyle yeniden kurar.
        /// Aksi halde ilk NETLOAD'daki eski butonlar kalır; yeni DLL yüklendiğinde ISKELECIZ görünmez.
        /// </summary>
        public static void Show()
        {
            try
            {
                if (_palette != null)
                {
                    try { _palette.Dispose(); } catch { /* sürüm / durum */ }
                    _palette = null;
                }

                _palette = new PaletteSet("STA Komut Paneli")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(170, 220),
                    Size = new Size(190, 310),
                    KeepFocus = false,
                    DockEnabled = DockSides.Left | DockSides.Right
                };

                _palette.Add("Komutlar", new CommandPaletteControl(GetAllCommandNames()));
                _palette.Visible = true;
            }
            catch
            {
                // AutoCAD yükleme bağlamı farklı ise sessiz geç.
            }
        }

        private static List<string> GetAllCommandNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in StaCommandsFallback)
                set.Add(s);

            try
            {
                var methods = typeof(CommandEntry)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .OrderBy(m => m.MetadataToken);
                foreach (var m in methods)
                {
                    foreach (CommandMethodAttribute a in m.GetCustomAttributes(typeof(CommandMethodAttribute), inherit: false))
                    {
                        if (!string.IsNullOrWhiteSpace(a.GlobalName))
                            set.Add(a.GlobalName.Trim());
                    }
                }
            }
            catch { /* yansıma yoksa StaCommandsFallback yeterli */ }

            return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    internal sealed class CommandPaletteControl : UserControl
    {
        public CommandPaletteControl(List<string> commands)
        {
            BackColor = Color.FromArgb(245, 245, 245);
            Dock = DockStyle.Fill;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = "STA Komutlari",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0)
            };
            Controls.Add(header);

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8, 8, 8, 8)
            };
            Controls.Add(panel);
            panel.BringToFront();

            foreach (string cmd in commands)
            {
                var button = new Button
                {
                    Width = 135,
                    Height = 17,
                    Text = cmd,
                    Tag = cmd,
                    FlatStyle = FlatStyle.Standard
                };
                button.Click += OnCommandClick;
                panel.Controls.Add(button);
            }
        }

        private static void OnCommandClick(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is Control c) || !(c.Tag is string cmd) || string.IsNullOrWhiteSpace(cmd))
                    return;

                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute(cmd + " ", true, false, false);
            }
            catch
            {
                // Komut çağrısı başarısız olsa da panel açık kalmalı.
            }
        }
    }
}
