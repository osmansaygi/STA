using System;
using System.Collections.Generic;
using System.Text;

namespace ST4Yardimci
{
    internal enum DosemeKatmanListe
    {
        Sinir,
        Mesnet,
        Bosluk,
        KatSinir
    }

    /// <summary>
    /// Döşeme donatı katman listeleri — %LocalAppData%\ST4_Yardimci\doseme_donati.ini
    /// Boş liste: çizimde yerleşik KIRIS/KOLON/PERDE ve DOSEME SINIRI kabulü.
    /// Boşluk / kat sınırı listeleri çizgiselde kullanılır (kat sınırı doluysa dışarı donatı yok).
    /// </summary>
    internal static class DosemeDonatiSettings
    {
        public static readonly string[] DefaultSinir =
        {
            "DOSEME SINIRI (BEYKENT)"
        };

        public static readonly string[] DefaultMesnet =
        {
            "KIRIS (BEYKENT)",
            "KOLON (BEYKENT)",
            "PERDE (BEYKENT)",
            "KAPAMA PERDESI (BEYKENT)"
        };

        public static readonly string[] DefaultBosluk =
        {
            "KALIP BOSLUK (BEYKENT)",
            "MERDIVEN (BEYKENT)"
        };

        public static List<string> SinirKatmanlar { get; } = new List<string>();
        public static List<string> MesnetKatmanlar { get; } = new List<string>();
        public static List<string> BoslukKatmanlar { get; } = new List<string>();
        /// <summary>Kat planı dış sınırı (kapalı poligon). Doluysa yalnızca içine donatı.</summary>
        public static List<string> KatSinirKatmanlar { get; } = new List<string>();

        /// <summary>Katman seçimi hangi listeye yazılacak.</summary>
        public static DosemeKatmanListe PickList = DosemeKatmanListe.Sinir;

        static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var m = BaIni.Read("doseme_donati.ini");
            if (m != null && m.ContainsKey("SinirKatmanlar"))
                Replace(SinirKatmanlar, Split(m["SinirKatmanlar"]));
            else
                Replace(SinirKatmanlar, DefaultSinir);

            if (m != null && m.ContainsKey("MesnetKatmanlar"))
                Replace(MesnetKatmanlar, Split(m["MesnetKatmanlar"]));
            else
                Replace(MesnetKatmanlar, DefaultMesnet);

            if (m != null && m.ContainsKey("BoslukKatmanlar"))
                Replace(BoslukKatmanlar, Split(m["BoslukKatmanlar"]));
            else
                Replace(BoslukKatmanlar, DefaultBosluk);

            if (m != null && m.ContainsKey("KatSinirKatmanlar"))
                Replace(KatSinirKatmanlar, Split(m["KatSinirKatmanlar"]));
            else
                KatSinirKatmanlar.Clear();
        }

        public static void Save()
        {
            EnsureLoaded();
            DosemeDonatiRunner.EnsureYonLoaded();
            BaIni.Write("doseme_donati.ini",
                "Yon=" + DosemeDonatiRunner.Yon,
                "SinirKatmanlar=" + Join(SinirKatmanlar),
                "MesnetKatmanlar=" + Join(MesnetKatmanlar),
                "BoslukKatmanlar=" + Join(BoslukKatmanlar),
                "KatSinirKatmanlar=" + Join(KatSinirKatmanlar));
        }

        public static List<string> ListOf(DosemeKatmanListe which)
        {
            switch (which)
            {
                case DosemeKatmanListe.Mesnet: return MesnetKatmanlar;
                case DosemeKatmanListe.Bosluk: return BoslukKatmanlar;
                case DosemeKatmanListe.KatSinir: return KatSinirKatmanlar;
                default: return SinirKatmanlar;
            }
        }

        public static bool AddKatman(DosemeKatmanListe which, string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            var list = ListOf(which);
            foreach (string e in list)
                if (string.Equals(e, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            list.Add(t);
            Save();
            return true;
        }

        public static bool RemoveKatman(DosemeKatmanListe which, string name)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(name)) return false;
            var list = ListOf(which);
            bool removed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (string.Equals(list[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) Save();
            return removed;
        }

        public static bool ListHas(IList<string> list, string lyr)
        {
            if (list == null || string.IsNullOrEmpty(lyr)) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], lyr, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static void Replace(List<string> dest, IEnumerable<string> names)
        {
            dest.Clear();
            if (names == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                string t = n.Trim();
                if (seen.Add(t)) dest.Add(t);
            }
        }

        static IEnumerable<string> Split(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            foreach (string p in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        static string Join(IEnumerable<string> names)
        {
            var sb = new StringBuilder();
            if (names == null) return "";
            foreach (string n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(n.Trim());
            }
            return sb.ToString();
        }
    }
}
