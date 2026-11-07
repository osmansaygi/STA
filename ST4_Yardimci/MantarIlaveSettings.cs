using System.Collections.Generic;
using System.Linq;

namespace ST4Yardimci
{
    /// <summary>
    /// Mantar döşeme kolon üstü ilave donatısı. Uzunluklar cm, çaplar mm.
    /// X donatısı XBoy uzunluğunda, YBoy genişliğinde dağıtılır (Y tersi); ikisi de kolona ortalanır.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class MantarIlaveSettings
    {
        public double XBoy = 300;
        public double YBoy = 300;
        public double XCap = 14;
        public double XAralik = 20;
        public double YCap = 14;
        public double YAralik = 20;
        public double DosemeKalinligi = 30;
        public double Paspayi = 2.5;
        /// <summary>Kalınlığı kolona en yakın DOSEME ISMI "d=..cm" yazısından al; bulunmazsa DosemeKalinligi.</summary>
        public bool KalinlikCizimden = true;

        private const string IniName = "mantar_ilave.ini";

        public static MantarIlaveSettings Load()
        {
            var s = new MantarIlaveSettings();
            var m = BaIni.Read(IniName);
            foreach (var f in typeof(MantarIlaveSettings).GetFields())
                if (f.FieldType == typeof(double) && !f.IsStatic)
                {
                    double v = BaIni.GetD(m, f.Name, (double)f.GetValue(s));
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) f.SetValue(s, v);
                }
            s.KalinlikCizimden = BaIni.GetB(m, "KalinlikCizimden", s.KalinlikCizimden);
            return s;
        }

        public void Save()
        {
            var rows = new List<string> { "KalinlikCizimden=" + BaIni.B(KalinlikCizimden) };
            rows.AddRange(typeof(MantarIlaveSettings).GetFields().Where(f => f.FieldType == typeof(double) && !f.IsStatic)
                .Select(f => f.Name + "=" + BaIni.N((double)f.GetValue(this))));
            BaIni.Write(IniName, rows.ToArray());
        }

        public string Validate()
        {
            if (XBoy < 50 || YBoy < 50) return "Donatı boyu en az 50 cm olmalı.";
            if (XAralik < 5 || YAralik < 5) return "Aralık en az 5 cm olmalı.";
            if (XCap < 6 || YCap < 6) return "Çap en az ø6 olmalı.";
            if (Paspayi < 0 || Paspayi > 10) return "Paspayı 0–10 cm aralığında olmalı.";
            if (DosemeKalinligi < 2 * Paspayi + 5) return "Döşeme kalınlığı paspayına göre çok küçük.";
            return null;
        }
    }
}
