using System;
using System.IO;
using System.Reflection;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// ISKELE kesit şablon DXF dosyaları derlemeye gömülür; diskte kopya yoksa geçici dosyaya yazılıp kullanılır.
    /// </summary>
    internal static class IskeleKesitEmbeddedTemplates
    {
        private const string ResourcePrefix = "ST4PlanIdCiz.IskeleKesitTemplates.";

        /// <summary>Önce disk (DLL yanı / üst dizin / dosyalar\), yoksa gömülü kaynak.</summary>
        public static string ResolveDxf(string fileName, Func<string> findOnDisk)
        {
            string disk = findOnDisk?.Invoke();
            if (!string.IsNullOrEmpty(disk) && File.Exists(disk)) return disk;
            return TryExtractEmbeddedDxf(fileName);
        }

        public static string TryExtractEmbeddedDxf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var asm = typeof(IskeleKesitEmbeddedTemplates).Assembly;
            string resName = ResourcePrefix + fileName;
            using (Stream s = asm.GetManifestResourceStream(resName))
            {
                if (s == null) return null;
                string dir = Path.Combine(Path.GetTempPath(), "ST4PlanIdCiz_IskeleKesit");
                try { Directory.CreateDirectory(dir); } catch { /* yok */ }
                string path = Path.Combine(dir, fileName);
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                        s.CopyTo(fs);
                }
                catch
                {
                    return null;
                }
                return path;
            }
        }
    }
}
