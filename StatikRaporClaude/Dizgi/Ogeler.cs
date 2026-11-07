using System.Drawing;
using StatikRaporClaude.Gpr;

namespace StatikRaporClaude.Dizgi
{
    /// <summary>STA4 sayfasından ayrıştırılan içerik öğesi. Y = kaynak sayfadaki konum (sıralama için).</summary>
    public abstract class Oge
    {
        public GprSayfa Sayfa;
        public float Y;
    }

    /// <summary>Başlık: seviye 1 (-10/-11/-12), seviye 2 (-8/-9).</summary>
    public sealed class BaslikOgesi : Oge
    {
        public string Metin;
        public int Seviye;
    }

    /// <summary>Düz metin satırı. Satır içi çok boşluklu hizalama sütun konumu (karakter indeksi) ile korunur.</summary>
    public sealed class MetinSatiri
    {
        public List<(int Sutun, string Metin)> Parcalar = new List<(int, string)>();
        public bool Kalin;
    }

    public sealed class MetinOgesi : Oge
    {
        public readonly List<MetinSatiri> Satirlar = new List<MetinSatiri>();
    }

    /// <summary>"Anahtar ........: değer" satırları.</summary>
    public sealed class AnahtarDegerOgesi : Oge
    {
        public readonly List<(string Anahtar, string Deger)> Satirlar = new List<(string, string)>();
    }

    /// <summary>Kutu çizgili STA tablosundan çözülmüş tablo.</summary>
    public sealed class TabloOgesi : Oge
    {
        /// <summary>Sütun genişlikleri (karakter sayısı).</summary>
        public List<int> Sutunlar = new List<int>();
        public readonly List<TabloSatir> Baslik = new List<TabloSatir>();
        public readonly List<TabloSatir> Govde = new List<TabloSatir>();
        public readonly List<string> Notlar = new List<string>();
        public string Imza;   // sütun yapısı (sayfa geçişinde aynı tabloyu birleştirmek için)
        /// <summary>Doluysa bu sütunlar sayı olsa da sola hizalanır (parametre / değer tabloları).</summary>
        public HashSet<int> SolSutunlar;
    }

    public sealed class TabloSatir
    {
        public readonly List<TabloHucre> Hucreler = new List<TabloHucre>();
        public bool GrupBasi;   // önünde ├──┤ ayırıcı vardı
        public bool AraBaslik;  // blok içi başlık satırı (yük düzeni adları vb.): vurgulu çizilir
        public bool Vurgu;      // toplam satırı: kalın
        /// <summary>Kiriş statik şeması (BEA +36); doluysa ilk hücredeki adın yanına simge çizilir.</summary>
        public int? UcTipi;
    }

    /// <summary>STA4 kütüphane resmi (GPR "-3" kaydı); BEYKENT çizimi varsa yerine o basılır.</summary>
    public sealed class ResimOgesi : Oge
    {
        public int No;
    }

    /// <summary>KAT DİYAFRAMLARI şeması: üstten alta katlar ve her katın diyafram numaraları.</summary>
    public sealed class DiyaframOgesi : Oge
    {
        public readonly List<(string Kat, List<string> Diyaframlar)> Katlar = new List<(string, List<string>)>();
    }

    public sealed class TabloHucre
    {
        public int Ilk, Son;     // kapsadığı sütun aralığı (dahil)
        public string Metin;
    }

    /// <summary>Çizim bölgesi (plan, grafik, şema): kaynak sayfanın dikdörtgeni vektör olarak, bizim yazı tipiyle çizilir.</summary>
    public sealed class SekilOgesi : Oge
    {
        public RectangleF Alan;
        public HashSet<GprYazi> Yazilar = new HashSet<GprYazi>();
    }
}
