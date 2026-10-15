using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace ST4Yardimci
{
    internal readonly struct BaTool
    {
        public readonly string Label;
        public readonly string Cmd;
        public readonly string Resource;

        public BaTool(string label, string cmd, string resource)
        {
            Label = label;
            Cmd = cmd;
            Resource = resource;
        }
    }

    /// <summary>BAPANEL ve üst simge şeridinin ortak komut listesi ve simgeleri.</summary>
    internal static class BaCommandIcons
    {
        public static readonly BaTool[] Tools =
        {
            new BaTool("Oto Ölçü", "OTOOLCU", "ST4Yardimci.Icons.ba_otoolcu.png"),
            new BaTool("Plan Ölçü", "PLANOLCU", "ST4Yardimci.Icons.ba_planolcu.png"),
            new BaTool("Merdiven", "MERDIVEN", "ST4Yardimci.Icons.ba_merdiven.png"),
            new BaTool("Döşeme Donatı", "DOSEMEDONATI", "ST4Yardimci.Icons.ba_doseme.png"),
            new BaTool("Oto Donatı", "OTODONATI", "ST4Yardimci.Icons.ba_otodonati.png"),
            new BaTool("Sehpa Donatısı", "SEHPADONATI", "ST4Yardimci.Icons.ba_sehpa.png"),
            new BaTool("Hasır Donatı", "HASIRDONATI", "ST4Yardimci.Icons.ba_hasirdonati.png"),
            new BaTool("Radye İlave Donatı", "RADYEILAVE", "ST4Yardimci.Icons.ba_radyeilave.png"),
            new BaTool("Temel Hatılı", "TEMELHATILI", "ST4Yardimci.Icons.ba_temelhatili.png"),
            new BaTool("Temel Çukuru", "TEMELCEKURU", "ST4Yardimci.Icons.ba_temelcekuru.png"),
            new BaTool("Kuranglez", "KURANGLEZ", "ST4Yardimci.Icons.ba_kuranglez.png"),
            new BaTool("Donatı Boyu", "DONATIBOYU", "ST4Yardimci.Icons.ba_donatiboyu.png"),
            new BaTool("Donatı Metraj", "DONATIMETRAJ", "ST4Yardimci.Icons.ba_donatimetraj.png"),
            new BaTool("Donatı Adet", "DONATIADET", "ST4Yardimci.Icons.ba_donatiadet.png"),
            new BaTool("Donatı Oku", "DONATIOKU", "ST4Yardimci.Icons.ba_donatioku.png"),
            new BaTool("Kot Değiştir", "KOTDEGISTIR", "ST4Yardimci.Icons.ba_kotdegistir.png"),
            new BaTool("Katman Yükle", "KATMANYUKLE", "ST4Yardimci.Icons.ba_katmanyukle.png"),
        };

        public static Image Load(string cmd, string resourceName)
        {
            Image user = TryUserOverride(cmd);
            if (user != null) return user;
            return LoadIcon(resourceName);
        }

        /// <summary>%LocalAppData%\ST4Icons\KOMUT.png — AutoCAD PNG aktarımı.</summary>
        static Image TryUserOverride(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ST4Icons",
                    cmd.Trim().ToUpperInvariant() + ".png");
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var src = Image.FromStream(ms))
                {
                    var dst = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, new Rectangle(0, 0, 32, 32));
                    }
                    return dst;
                }
            }
            catch
            {
                return null;
            }
        }

        static Image LoadIcon(string resourceName)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (var src = Image.FromStream(stream))
                        return new Bitmap(src);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
