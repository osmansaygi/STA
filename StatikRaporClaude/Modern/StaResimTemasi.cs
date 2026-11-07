using System.Drawing;

namespace StatikRaporClaude.Modern
{
    /// <summary>STA4 resim çizimlerinin renk ve çizgi düzeni. Çizimler renkleri yalnız buradan alır; tema değişince hepsi değişir.</summary>
    public sealed class StaResimTemasi
    {
        public string Kod, Ad, Tarif;
        public Color Beton, BetonYan, BetonUst, Kenar;      // beton yüzleri ve kenar çizgisi
        public Color Eksen, Vurgu, Yuk, Donati;             // eksen/ölçü, vurgu (moment, rijit bölge), yük okları, donatı
        public Color Panel, Ikincil, Tugla;                 // açık zemin (ağ, şema), soluk yardımcı çizgi, tuğla
        public Color KartZemin, KartSerit, KartCerceve;     // resim kartı
        public Color Yazi, Kilavuz, Baslik, Aciklama;       // etiket, kılavuz çizgisi, kart başlığı, açıklama
        public float Kalinlik = 1f;                          // çizgi kalınlığı çarpanı

        private static Color R(int r, int g, int b) => Color.FromArgb(r, g, b);

        public static readonly StaResimTemasi[] Hepsi =
        {
            new StaResimTemasi
            {
                Kod = "fildisi", Ad = "Fildişi beton", Tarif = "Sıcak fildişi beton, kobalt eksen, turuncu vurgu. Raporun genel renkleriyle aynı.",
                Beton = R(238, 233, 224), BetonYan = R(222, 215, 202), BetonUst = R(246, 243, 236), Kenar = R(118, 110, 98),
                Eksen = ModernSayfa.Kobalt, Vurgu = ModernSayfa.Turuncu, Yuk = ModernSayfa.Lacivert, Donati = R(196, 60, 40),
                Panel = R(244, 247, 243), Ikincil = R(175, 185, 200), Tugla = R(214, 150, 120),
                KartZemin = R(250, 249, 246), KartSerit = ModernSayfa.Turuncu, KartCerceve = Color.Empty,
                Yazi = ModernSayfa.Koyu, Kilavuz = ModernSayfa.Gri, Baslik = ModernSayfa.Lacivert, Aciklama = ModernSayfa.Gri,
            },
            new StaResimTemasi
            {
                Kod = "teknik", Ad = "Teknik çizim", Tarif = "Beyaz zemin, siyah çizgi, gri yüzler. Siyah-beyaz yazıcıda kayıpsız.",
                Beton = R(255, 255, 255), BetonYan = R(232, 232, 232), BetonUst = R(246, 246, 246), Kenar = R(20, 20, 20),
                Eksen = R(70, 70, 70), Vurgu = R(20, 20, 20), Yuk = R(20, 20, 20), Donati = R(20, 20, 20),
                Panel = R(255, 255, 255), Ikincil = R(170, 170, 170), Tugla = R(205, 205, 205),
                KartZemin = R(255, 255, 255), KartSerit = Color.Empty, KartCerceve = R(60, 60, 60),
                Yazi = R(20, 20, 20), Kilavuz = R(90, 90, 90), Baslik = R(20, 20, 20), Aciklama = R(70, 70, 70),
                Kalinlik = 1.15f,
            },
            new StaResimTemasi
            {
                Kod = "mavikopya", Ad = "Mavi kopya", Tarif = "Koyu mavi zemin, açık çizgiler; proje paftası havası. Yazıcıda mürekkep harcar.",
                Beton = R(38, 74, 128), BetonYan = R(30, 60, 108), BetonUst = R(50, 90, 148), Kenar = R(214, 230, 250),
                Eksen = R(140, 200, 255), Vurgu = R(255, 196, 92), Yuk = R(255, 255, 255), Donati = R(255, 140, 120),
                Panel = R(30, 64, 116), Ikincil = R(110, 150, 200), Tugla = R(150, 100, 90),
                KartZemin = R(22, 52, 98), KartSerit = R(255, 196, 92), KartCerceve = Color.Empty,
                Yazi = R(235, 242, 252), Kilavuz = R(150, 180, 220), Baslik = R(255, 255, 255), Aciklama = R(190, 210, 236),
            },
            new StaResimTemasi
            {
                Kod = "gri", Ad = "Gri ton + kırmızı", Tarif = "Gri tonlu yüzler, yükler ve vurgular kırmızı. Sade, yazıcıda okunaklı.",
                Beton = R(230, 230, 228), BetonYan = R(208, 208, 205), BetonUst = R(242, 242, 240), Kenar = R(70, 70, 70),
                Eksen = R(110, 110, 110), Vurgu = R(200, 35, 35), Yuk = R(200, 35, 35), Donati = R(200, 35, 35),
                Panel = R(246, 246, 245), Ikincil = R(185, 185, 185), Tugla = R(196, 160, 150),
                KartZemin = R(255, 255, 255), KartSerit = R(200, 35, 35), KartCerceve = Color.Empty,
                Yazi = R(30, 30, 30), Kilavuz = R(110, 110, 110), Baslik = R(30, 30, 30), Aciklama = R(100, 100, 100),
            },
            new StaResimTemasi
            {
                Kod = "duz", Ad = "Düz renkli", Tarif = "Açık mavi beton, yeşil eksen, mercan vurgu; sunum görünümü.",
                Beton = R(212, 227, 242), BetonYan = R(186, 207, 230), BetonUst = R(230, 240, 250), Kenar = R(54, 86, 128),
                Eksen = R(28, 140, 96), Vurgu = R(232, 92, 70), Yuk = R(24, 42, 80), Donati = R(232, 92, 70),
                Panel = R(240, 247, 252), Ikincil = R(160, 188, 214), Tugla = R(236, 170, 140),
                KartZemin = R(255, 255, 255), KartSerit = R(38, 99, 184), KartCerceve = R(212, 222, 236),
                Yazi = R(24, 42, 80), Kilavuz = R(110, 130, 160), Baslik = R(24, 42, 80), Aciklama = R(90, 104, 126),
            },
        };

        public static StaResimTemasi Bul(string kod) => Hepsi.FirstOrDefault(t => t.Kod == kod) ?? Hepsi[0];
    }
}
