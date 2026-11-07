using System;

namespace Beykent.Monolit.Girdi
{
    /// <summary>
    /// Birimi kutunun içinde gösteren, sınır denetimli sayı kutusu. Ok tuşları ±Adim;
    /// virgül ve nokta kabul edilir. Sınır dışı değer kırmızı gösterilir, yuvarlanıp sessizce kırpılmaz.
    /// </summary>
    internal sealed class OlcuKutusu : GirdiKutusu
    {
        private double _deger;
        public double Min { get; }
        public double Max { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double AdimDegeri { get; set; }
        public int Ondalik { get; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double Varsayilan { get; set; }
        public string Birim { get; }

        public OlcuKutusu(string ad, string birim, double min, double max, double varsayilan, int ondalik = 0, double adim = 1)
        {
            Ad = ad; Birim = birim; Min = min; Max = max; Ondalik = ondalik; AdimDegeri = adim; Varsayilan = varsayilan;
            Bilgi.Text = birim;
            Deger = varsayilan;
        }

        /// <summary>Son geçerli değer. Geçersiz metinde önceki geçerli değer korunur.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double Deger
        {
            get => _deger;
            set { _deger = value; Metin.Text = GirdiTema.Yaz(value, Ondalik); }
        }

        protected override string Coz(string text)
        {
            if (!GirdiTema.SayiOku(text, out double v))
                return string.IsNullOrWhiteSpace(text) ? Ad + " boş" : Ad + ": sayı girin (ör. " + GirdiTema.Yaz(Varsayilan, Ondalik) + ")";
            double kare = Math.Round(v, Ondalik, MidpointRounding.AwayFromZero);
            if (Math.Abs(kare - v) > 1e-9)
                return Ad + ": en fazla " + Ondalik + " ondalık";
            if (v < Min - 1e-9 || v > Max + 1e-9)
                return Ad + ": " + GirdiTema.Yaz(Min, Ondalik) + "–" + GirdiTema.Yaz(Max, Ondalik) + " " + Birim + " arası olmalı";
            _deger = v;
            return null;
        }

        protected override void Bicimle()
        {
            string t = GirdiTema.Yaz(_deger, Ondalik);
            if (Metin.Text != t) Metin.Text = t;
        }

        protected override void Adim(int yon)
        {
            double baz = GirdiTema.SayiOku(Metin.Text, out double v) ? v : _deger;
            double yeni = Math.Max(Min, Math.Min(Max, baz + yon * AdimDegeri));
            Deger = yeni;
            Metin.SelectAll();
        }

        public override void VarsayilanaDon() => Deger = Varsayilan;
    }
}
