using System;
using System.Management;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Makine UUID kilidi: yalnızca izinli bilgisayarlarda çalışır.
    /// DLL başka PC'ye kopyalanınca komutlar reddedilir.
    /// </summary>
    internal static class MachineLicense
    {
        /// <summary>İzinli anakart UUID listesi (büyük/küçük harf duyarsız).</summary>
        private static readonly string[] AllowedUuids =
        {
            // Ofis — DESKTOP-I49ATPJ / BEYKENT-2
            "4C4C4544-0037-5110-8052-C4C04F354A34",
            // Geliştirme — DESKTOP-MHC1B2K / BEYKENT
            "4C4C4544-0052-4E10-8057-C2C04F583634",
        };

        private static bool? _cachedOk;
        private static string _cachedUuid;

        public static bool IsAllowed()
        {
            if (_cachedOk.HasValue)
                return _cachedOk.Value;

            string uuid = TryReadMachineUuid();
            _cachedUuid = uuid ?? "";
            bool ok = false;
            if (!string.IsNullOrWhiteSpace(uuid))
            {
                foreach (string allowed in AllowedUuids)
                {
                    if (string.Equals(allowed, uuid, StringComparison.OrdinalIgnoreCase))
                    {
                        ok = true;
                        break;
                    }
                }
            }
            _cachedOk = ok;
            return ok;
        }

        /// <summary>İzin yoksa komut mesajı yazar ve false döner.</summary>
        public static bool EnsureOrWarn()
        {
            if (IsAllowed())
                return true;

            try
            {
                Document doc = AcApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage(
                    "\nBETONARME TOOL: Bu bilgisayar için lisans yok. Program kopyalanmış veya yetkisiz makine.");
            }
            catch { }
            return false;
        }

        private static string TryReadMachineUuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT UUID FROM Win32_ComputerSystemProduct"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject mo in results)
                    {
                        object v = mo["UUID"];
                        if (v == null) continue;
                        string s = v.ToString().Trim();
                        if (s.Length > 0 &&
                            !string.Equals(s, "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase))
                            return s;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
