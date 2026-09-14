using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// AutoCAD 2025+ (.NET 8) varsayılan olarak Windows-1254 / 1252 içermez.
    /// ST4/GPR Türkçe ANSI dosyaları için CodePagesEncodingProvider kaydı gerekir;
    /// NETLOAD bağımlılığı yüklemezse yerleşik 1254 çözümleyici kullanılır.
    /// </summary>
    internal static class WindowsAnsiEncodings
    {
        private static volatile bool _registered;
        private static readonly object RegisterLock = new object();

        /// <summary>Windows-1254, 0x80..0xFF (kaynak dosya ASCII kalsın diye \u kaçışları).</summary>
        private static readonly char[] Windows1254From80 =
        {
            '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
            '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u008E', '\u008F',
            '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
            '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u009E', '\u0178',
            '\u00A0', '\u00A1', '\u00A2', '\u00A3', '\u00A4', '\u00A5', '\u00A6', '\u00A7',
            '\u00A8', '\u00A9', '\u00AA', '\u00AB', '\u00AC', '\u00AD', '\u00AE', '\u00AF',
            '\u00B0', '\u00B1', '\u00B2', '\u00B3', '\u00B4', '\u00B5', '\u00B6', '\u00B7',
            '\u00B8', '\u00B9', '\u00BA', '\u00BB', '\u00BC', '\u00BD', '\u00BE', '\u00BF',
            '\u00C0', '\u00C1', '\u00C2', '\u00C3', '\u00C4', '\u00C5', '\u00C6', '\u00C7',
            '\u00C8', '\u00C9', '\u00CA', '\u00CB', '\u00CC', '\u00CD', '\u00CE', '\u00CF',
            '\u011E', '\u00D1', '\u00D2', '\u00D3', '\u00D4', '\u00D5', '\u00D6', '\u00D7',
            '\u00D8', '\u00D9', '\u00DA', '\u00DB', '\u00DC', '\u0130', '\u015E', '\u00DF',
            '\u00E0', '\u00E1', '\u00E2', '\u00E3', '\u00E4', '\u00E5', '\u00E6', '\u00E7',
            '\u00E8', '\u00E9', '\u00EA', '\u00EB', '\u00EC', '\u00ED', '\u00EE', '\u00EF',
            '\u011F', '\u00F1', '\u00F2', '\u00F3', '\u00F4', '\u00F5', '\u00F6', '\u00F7',
            '\u00F8', '\u00F9', '\u00FA', '\u00FB', '\u00FC', '\u0131', '\u015F', '\u00FF'
        };

        public static void EnsureRegistered()
        {
            if (_registered) return;
            lock (RegisterLock)
            {
                if (_registered) return;
                TryLoadCodePagesAssembly();
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                }
                catch
                {
                    /* AutoCAD gölge kopya / paket yoksa yerleşik 1254 kullanılır */
                }
                _registered = true;
            }
        }

        public static bool TryGet(int codePage, out Encoding encoding)
        {
            encoding = null;
            EnsureRegistered();
            try
            {
                encoding = Encoding.GetEncoding(codePage);
                return encoding != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGet(string name, out Encoding encoding)
        {
            encoding = null;
            EnsureRegistered();
            try
            {
                encoding = Encoding.GetEncoding(name);
                return encoding != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>ST4/GPR: UTF-8 (BOM veya geçerli metin) yoksa Windows-1254. <see cref="File.ReadAllLines(string)"/> kullanmayın — AutoCAD 2025+ 1254 fırlatır.</summary>
        public static string[] ReadAllLines(string path)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
            byte[] bytes = File.ReadAllBytes(path);
            string text = DecodeFileBytes(bytes);
            return SplitLines(text);
        }

        public static string DecodeFileBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                return utf8Strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                /* ANSI Türkçe ST4 */
            }
            catch (ArgumentException)
            {
            }
            EnsureRegistered();
            if (TryGet(1254, out Encoding enc1254))
                return enc1254.GetString(bytes);
            return DecodeWindows1254Bytes(bytes);
        }

        public static string DecodeWindows1254Bytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                chars[i] = b < 128 ? (char)b : Windows1254From80[b - 128];
            }
            return new string(chars);
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var lines = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                int start = i;
                while (i < text.Length && text[i] != '\r' && text[i] != '\n')
                    i++;
                lines.Add(text.Substring(start, i - start));
                if (i < text.Length && text[i] == '\r') i++;
                if (i < text.Length && text[i] == '\n') i++;
            }
            return lines.ToArray();
        }

        private static void TryLoadCodePagesAssembly()
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "System.Text.Encoding.CodePages", StringComparison.OrdinalIgnoreCase))
                        return;
                }
                string loc = typeof(WindowsAnsiEncodings).Assembly.Location;
                if (string.IsNullOrEmpty(loc)) return;
                string dir = Path.GetDirectoryName(loc);
                if (string.IsNullOrEmpty(dir)) return;
                string dll = Path.Combine(dir, "System.Text.Encoding.CodePages.dll");
                if (File.Exists(dll))
                    Assembly.LoadFrom(dll);
            }
            catch
            {
            }
        }
    }
}
