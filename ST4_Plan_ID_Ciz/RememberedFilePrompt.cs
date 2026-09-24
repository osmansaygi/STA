using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Dosya seçim diyaloglarında son kullanılan yolu hatırlar (klasör + dosya adı).
    /// Oturum ve NETLOAD sonrası için diske yazar.
    /// </summary>
    internal static class RememberedFilePrompt
    {
        public const string KindSt4 = "st4";
        public const string KindKsf = "ksf";
        public const string KindPdf = "pdf";
        public const string KindGpr = "gpr";
        public const string KindDwg = "dwg";

        private static readonly object Sync = new object();
        private static Dictionary<string, string> _cache;
        private static string _storePath;

        private static string StorePath
        {
            get
            {
                if (_storePath != null) return _storePath;
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4_Plan_ID_Ciz");
                try { Directory.CreateDirectory(dir); }
                catch { /* okuma yine denenir */ }
                _storePath = Path.Combine(dir, "last_open_files.txt");
                return _storePath;
            }
        }

        public static string GetLast(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return null;
            EnsureLoaded();
            lock (Sync)
            {
                return _cache.TryGetValue(kind.Trim().ToLowerInvariant(), out string p) ? p : null;
            }
        }

        public static void Remember(string kind, string path)
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(path)) return;
            EnsureLoaded();
            lock (Sync)
            {
                _cache[kind.Trim().ToLowerInvariant()] = path;
                TrySaveUnlocked();
            }
        }

        /// <summary>
        /// Dosya seçtirir. İptalde false. Başarıda <paramref name="path"/> dolu ve kind hatırlanır.
        /// </summary>
        public static bool TryPrompt(
            Editor ed,
            string message,
            string filter,
            string kind,
            out string path,
            string fallbackInitialDirectory = null)
        {
            path = null;
            if (ed == null) return false;

            string title = (message ?? "Dosya secin").Trim();
            if (title.StartsWith("\n", StringComparison.Ordinal))
                title = title.Substring(1).TrimStart();

            string last = GetLast(kind);
            string initialDir = null;
            string initialFile = null;
            if (!string.IsNullOrEmpty(last))
            {
                try
                {
                    string dir = Path.GetDirectoryName(last);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        initialDir = dir;
                    if (File.Exists(last))
                        initialFile = Path.GetFileName(last);
                }
                catch { }
            }
            if (initialDir == null && !string.IsNullOrEmpty(fallbackInitialDirectory)
                && Directory.Exists(fallbackInitialDirectory))
                initialDir = fallbackInitialDirectory;

            // WinForms: FileName ile son dosya diyalogda görünür (GetFileNameForOpen bunu vermez).
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = string.IsNullOrEmpty(title) ? "Dosya secin" : title;
                ofd.Filter = string.IsNullOrEmpty(filter)
                    ? "Tum Dosyalar (*.*)|*.*"
                    : filter;
                ofd.CheckFileExists = true;
                ofd.Multiselect = false;
                ofd.RestoreDirectory = true;
                if (!string.IsNullOrEmpty(initialDir))
                    ofd.InitialDirectory = initialDir;
                if (!string.IsNullOrEmpty(initialFile))
                    ofd.FileName = initialFile;

                DialogResult dr;
                try
                {
                    IntPtr hwnd = IntPtr.Zero;
                    try { hwnd = AcadApp.MainWindow.Handle; }
                    catch { }
                    if (hwnd != IntPtr.Zero)
                        dr = ofd.ShowDialog(new Win32Window(hwnd));
                    else
                        dr = ofd.ShowDialog();
                }
                catch
                {
                    // WinForms başarısızsa AutoCAD diyaloguna düş.
                    return TryPromptAcad(ed, message, filter, kind, initialDir, out path);
                }

                if (dr != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
                    return false;
                path = ofd.FileName;
                Remember(kind, path);
                return true;
            }
        }

        private static bool TryPromptAcad(
            Editor ed,
            string message,
            string filter,
            string kind,
            string initialDir,
            out string path)
        {
            path = null;
            var opts = new PromptOpenFileOptions(message ?? "\nDosya secin")
            {
                Filter = string.IsNullOrEmpty(filter)
                    ? "Tum Dosyalar (*.*)|*.*"
                    : filter
            };
            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                opts.InitialDirectory = initialDir;
            var res = ed.GetFileNameForOpen(opts);
            if (res.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(res.StringResult))
                return false;
            path = res.StringResult;
            Remember(kind, path);
            return true;
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_cache != null) return;
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string p = StorePath;
                    if (!File.Exists(p)) return;
                    foreach (string line in File.ReadAllLines(p))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                            continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k.Length > 0 && v.Length > 0)
                            _cache[k] = v;
                    }
                }
                catch { }
            }
        }

        private static void TrySaveUnlocked()
        {
            try
            {
                var lines = new List<string> { "# ST4_Plan_ID_Ciz last open files" };
                foreach (var kv in _cache)
                    lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(StorePath, lines);
            }
            catch { }
        }

        private sealed class Win32Window : IWin32Window
        {
            public Win32Window(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }
}
