from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs');s=p.read_text(encoding='utf-8-sig');s=s.replace('        private const double DuPerCm = 2.0;','        private const double DuPerCm = 2.0;\n        private const double AcilimTextGapDu = 2 * DuPerCm;')
s=s.replace('mx2, my2, nx, ny, 8.0)', 'mx2, my2, nx, ny, AcilimTextGapDu)')
s=s.replace('vx - 8.0, vmy, edgeIsRight: true)', 'vx - AcilimTextGapDu, vmy, edgeIsRight: true)')
s=s.replace('at.X - 14, at.Y, 1, 0, 8)', 'at.X - 14, at.Y, 1, 0, AcilimTextGapDu)')
s=s.replace('at.X, at.Y + 8)', 'at.X, at.Y + AcilimTextGapDu)').replace('at.X, at.Y - 8)', 'at.X, at.Y - AcilimTextGapDu)')
s=s.replace('midX, hy + 8.0)', 'midX, hy + AcilimTextGapDu)').replace('midX, hy - 8)', 'midX, hy - AcilimTextGapDu)')
s=s.replace('hx, hy, inward, 0, 8.0)', 'hx, hy, inward, 0, AcilimTextGapDu)')
s=s.replace('mx, my, nx, ny, 8.0)', 'mx, my, nx, ny, AcilimTextGapDu)')
p.write_text(s,encoding='utf-8-sig')
p=Path('ST4_Yardimci/CommandEntry.cs');s=p.read_text(encoding='utf-8-sig');s=s.replace('        private static Mutex _loadMutex;','        private static Mutex _loadMutex;\n        private bool _ownsInitialization;')
a=s.index('                AppDomain.CurrentDomain.AssemblyResolve -=');b=s.index('                if (!MachineLicense.IsAllowed())',a)
s=s[:a]+'''                if (_loadMutex != null) return;
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                WarnIfDuplicateAssemblies(ed);
                // A process-specific named handle is shared by timestamped DLL copies.
                // Keep the first handle alive; a second copy must not create another palette.
                bool created;
                var registration = new Mutex(false, @"Local\\ST4_Yardimci_ACAD_" +
                    System.Diagnostics.Process.GetCurrentProcess().Id, out created);
                if (!created)
                {
                    registration.Dispose();
                    ed?.WriteMessage("\\nBETONARME TOOL: Bu oturumda Yardimci zaten yuklu. Ikinci kopyanin panel acilisi engellendi. Yeni surum icin AutoCAD'i yeniden baslatin.");
                    return;
                }
                _loadMutex = registration;
                _ownsInitialization = true;
                LogLoadStage("Initialize: " + Assembly.GetExecutingAssembly().Location);
                AppDomain.CurrentDomain.AssemblyResolve -= ResolvePluginDependency;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;

'''+s[b:]
a=s.index('        private static void OnIdleShowBaPanel(');b=s.index('        /// <summary>Bundle klasöründeki',a)
s=s[:a]+'''        private static void OnIdleShowBaPanel(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdleShowBaPanel;
            try
            {
                if (!MachineLicense.IsAllowed()) return;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null || !doc.Editor.IsQuiescent)
                {
                    AcApp.Idle += OnIdleShowBaPanel;
                    return;
                }
                // Queue a normal command after NETLOAD/startup completes. Never create
                // native palette windows inside an Idle callback or assembly initialization.
                LogLoadStage("BAPANEL queued");
                doc.SendStringToExecute("BAPANEL ", true, false, false);
            }
            catch (System.Exception ex) { LogLoadStage("Panel queue failed: " + ex); }
        }

        internal static void LogLoadStage(string stage)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "ST4_Yardimci-load.log"),
                    DateTime.UtcNow.ToString("o") + " PID=" + System.Diagnostics.Process.GetCurrentProcess().Id
                    + " " + stage + Environment.NewLine);
            }
            catch { }
        }

'''+s[b:]
s=s.replace('        public void Terminate()\n        {','        public void Terminate()\n        {\n            if (!_ownsInitialization) return;')
s=s.replace('            try { BaTopToolbar.Shutdown(); } catch { }','            try { BaTopToolbar.Shutdown(); } catch { }\n            try { _loadMutex?.Dispose(); _loadMutex = null; } catch { }')
s=s.replace('            St4LispPaletteManager.Show();','            PluginLifecycle.LogLoadStage("BAPANEL creating/showing palette");\n            St4LispPaletteManager.Show();\n            PluginLifecycle.LogLoadStage("BAPANEL completed");',1)
p.write_text(s,encoding='utf-8-sig')
