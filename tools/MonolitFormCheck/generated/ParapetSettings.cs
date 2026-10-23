using System;
using System.Collections.Generic;
using System.Linq;

namespace ST4Yardimci
{
    internal enum ParapetKonumu { DosemeKenari, DosemeOrtasi, KirisUzeri }

    /// <summary>Tüm uzunluklar cm, donatı çapları mm. Sakal döşeme alt yüzünden ölçülür.</summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class ParapetSettings
    {
        public double DosemeKalinligi = 15;
        public double DosemeUstKotu = 300;
        public double ParapetYuksekligi = 50;
        public double ParapetKalinligi = 20;
        public double KirisGenisligi = 30;
        public double KirisYuksekligi = 60;
        public double Sakal = 0;
        public double Paspayi = 2.5;
        public double DikeyCap = 12;
        public double DikeyAralik = 15;
        public double YatayCap = 10;
        public double YatayAralik = 15;
        public double ToplamUzunluk = 0;
        public ParapetKonumu Konum = ParapetKonumu.DosemeKenari;

        public static ParapetSettings Load()
        {
            var s = new ParapetSettings();
            var m = BaIni.Read("parapet.ini");
            foreach (var f in typeof(ParapetSettings).GetFields())
                if (f.FieldType == typeof(double) && !f.IsStatic)
                {
                    double v = BaIni.GetD(m, f.Name, (double)f.GetValue(s));
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) f.SetValue(s, v);
                }
            int k = BaIni.GetI(m, "Konum", 0);
            if (k >= 0 && k <= 2) s.Konum = (ParapetKonumu)k;
            s.Normalize();
            return s;
        }

        public void Save()
        {
            Normalize();
            var rows = new List<string> { "Konum=" + (int)Konum };
            rows.AddRange(typeof(ParapetSettings).GetFields().Where(f => f.FieldType == typeof(double) && !f.IsStatic)
                .Select(f => f.Name + "=" + BaIni.N((double)f.GetValue(this))));
            BaIni.Write("parapet.ini", rows.ToArray());
        }

        public void Normalize() { if (Konum == ParapetKonumu.KirisUzeri) Sakal = 0; }
        public double AltDerinlik => Konum == ParapetKonumu.KirisUzeri ? KirisYuksekligi : DosemeKalinligi + Sakal;
        public double KirisSol => ParapetKalinligi - KirisGenisligi;
        public double KirisSag => ParapetKalinligi;

        public double GovdeBoyu => ParapetYuksekligi + AltDerinlik - 2 * Paspayi;
        public double UstGonye => ParapetKalinligi - 2 * Paspayi;
        // Temel hatılı ile aynı: en az 12ø; toplam boy 5 cm katına tamamlanır.
        public double AltGonye => Math.Ceiling((GovdeBoyu + UstGonye + 12 * DikeyCap / 10) / 5 - 1e-9) * 5 - GovdeBoyu - UstGonye;
        public double DonatiBoyu => GovdeBoyu + UstGonye + AltGonye;

        public double AltGonyeYatay(bool inner)
        {
            if (Konum == ParapetKonumu.DosemeOrtasi) return AltGonye;
            double capacity = inner || (Konum != ParapetKonumu.KirisUzeri && Sakal > 0) ? UstGonye
                : Konum == ParapetKonumu.KirisUzeri ? KirisGenisligi - 2 * Paspayi
                : ParapetKalinligi + 80 - 2 * Paspayi;
            if (AltGonye <= capacity + 1e-6) return AltGonye;
            return capacity;
        }
        public double EkGonye(bool inner)
        {
            double horizontal = AltGonyeYatay(inner);
            if (horizontal >= AltGonye - 1e-6) return 0;
            // İkinci gönye kendi başına en az 12ø; yuvarlama payı bu kola eklenir.
            double minTail = 12 * DikeyCap / 10;
            return Math.Ceiling((GovdeBoyu + UstGonye + horizontal + minTail) / 5 - 1e-9) * 5
                - GovdeBoyu - UstGonye - horizontal;
        }
        public double DonatiBoyuYuz(bool inner) => GovdeBoyu + UstGonye + AltGonyeYatay(inner) + EkGonye(inner);

        public int DikeyAdet => ToplamUzunluk > 0 ? Math.Max(1,(int)Math.Ceiling(ToplamUzunluk/DikeyAralik-1e-9)) : 0;
        public const double StokBoyu = 1200, Bindirme = 60;
        public sealed class StokParca
        {
            public int Adet;
            public double Boy;
        }
        public List<StokParca> YatayParcalar()
        {
            var result = new List<StokParca>();
            if (ToplamUzunluk <= 0) return result;
            int pieces=Math.Max(1,(int)Math.Ceiling((ToplamUzunluk-Bindirme)/(StokBoyu-Bindirme)-1e-9));
            int full=pieces-1;
            double last=Math.Ceiling((ToplamUzunluk-full*(StokBoyu-Bindirme))/5-1e-9)*5;
            if (Math.Abs(last-StokBoyu)<1e-6) { full++; last=0; }
            if (full>0) result.Add(new StokParca { Adet=full, Boy=StokBoyu });
            if (last>0) result.Add(new StokParca { Adet=1, Boy=last });
            return result;
        }

        public List<double> YatayKesitKotlari()
        {
            var rows=new List<double>();
            double upper=ParapetYuksekligi-Paspayi-1.5, lower=-Paspayi-1.5;
            int gaps=Math.Max(1,(int)Math.Ceiling((upper-lower)/YatayAralik-1e-9));
            double step=(upper-lower)/gaps;
            for(int i=0;i<=gaps;i++)
            {
                double y=upper-i*step;
                if(y>=0) rows.Add(y);
            }
            if(Konum!=ParapetKonumu.KirisUzeri && Sakal>0)
            {
                double low=-AltDerinlik+Paspayi+1.5;
                double high=-DosemeKalinligi-Paspayi-1.5;
                // Üstteki gerçek sıra adımı korunur; uç sıraya sıkıştırma yapılmaz.
                for(double y=low;y<=high+1e-6;y+=step) rows.Add(y);
            }
            return rows;
        }

        public string Validate()
        {
            foreach (var f in typeof(ParapetSettings).GetFields())
                if (f.FieldType == typeof(double) && !f.IsStatic)
                {
                    double v = (double)f.GetValue(this);
                    if (double.IsNaN(v) || double.IsInfinity(v)) return "Geçersiz sayısal değer.";
                }
            if (!Enum.IsDefined(typeof(ParapetKonumu), Konum)) return "Geçersiz oturma konumu.";
            if (ToplamUzunluk < 0 || ToplamUzunluk > 10000000) return "Toplam parapet boyu 0–10000000 cm arasında olmalı.";
            if (DosemeKalinligi < 8 || ParapetYuksekligi < 15 || ParapetKalinligi < 10 || Sakal < 0)
                return "Döşeme en az 8, parapet yüksekliği en az 15, kalınlığı en az 10 cm; sakal en az 0 cm olmalı.";
            if (DikeyCap < 6 || YatayCap < 6 || DikeyAralik < 5 || YatayAralik < 5)
                return "Donatı çapları en az 6 mm, aralıkları en az 5 cm olmalı.";
            double pay = Paspayi + Math.Max(DikeyCap, YatayCap) / 20;
            if (Paspayi < 2 || ParapetKalinligi <= 2 * pay + 3 || DosemeKalinligi <= 2 * pay + 3
                || ParapetYuksekligi <= 2 * pay)
                return "Kesit, paspayı ve donatı bükümleri için yeterli iç boşluk sağlamıyor.";
            if (Konum == ParapetKonumu.KirisUzeri && (KirisGenisligi < ParapetKalinligi || KirisYuksekligi < DosemeKalinligi))
                return "Kiriş genişliği parapet kalınlığından, yüksekliği döşeme kalınlığından küçük olamaz.";
            if (Konum == ParapetKonumu.KirisUzeri && Sakal != 0) return "Kiriş üzeri parapette sakal olamaz.";
            if (Math.Max(EkGonye(true), EkGonye(false)) > GovdeBoyu - 3)
                return "Ek gönye için düşey donatı gövdesinde yeterli mesafe yok.";
            return null;
        }
    }
}
