namespace StatikRaporClaude.Veri
{
    /// <summary>
    /// STA4CAD ikili sonuç dosyaları (float32 / int16, little-endian, sabit kayıt boyu).
    /// Ofsetler N_ULK_02 GPR'si ile tüm kayıtlarda karşılaştırılarak çıkarıldı (bkz. modül durum dosyası).
    /// Yük durumu dizileri 22 elemanlıdır: 0..6 düşey yük düzenleri (GGGGGG, QQQQQQ, Q_Q_Q_, _Q_Q_Q, QQ_QQ_, _QQ_QQ, Q_QQ_Q),
    /// 7 zemin, 8..13 Deprem+X, Deprem-X, Deprem+Y, Deprem-Y, Rüzgar X, Rüzgar Y.
    /// </summary>
    public sealed class IkiliKayit
    {
        private readonly byte[] _b;
        public readonly int Bas;
        public IkiliKayit(byte[] b, int bas) { _b = b; Bas = bas; }
        public float F(int ofset) => Bas + ofset + 4 <= _b.Length ? BitConverter.ToSingle(_b, Bas + ofset) : 0f;
        public short S(int ofset) => Bas + ofset + 2 <= _b.Length ? BitConverter.ToInt16(_b, Bas + ofset) : (short)0;
        public float[] Dizi(int ofset, int n = 22) => Enumerable.Range(0, n).Select(i => F(ofset + 4 * i)).ToArray();
    }

    public sealed class IkiliDosya
    {
        public readonly string Yol;
        public readonly int KayitBoyu;
        private readonly byte[] _b;
        public int Adet => _b.Length / KayitBoyu;
        public DateTime Tarih => File.GetLastWriteTime(Yol);

        public IkiliDosya(string yol, int kayitBoyu)
        {
            Yol = yol; KayitBoyu = kayitBoyu;
            _b = File.Exists(yol) ? PaylasimliDosya.Bytes(yol) : Array.Empty<byte>();
        }

        public IkiliKayit this[int i] => new IkiliKayit(_b, i * KayitBoyu);
        public bool Var => _b.Length >= KayitBoyu;
    }

    /// <summary>Klasördeki ST4 ve sonuç dosyaları (aynı taban ad).</summary>
    public sealed class StaVeri
    {
        public St4Model Model;
        public string St4Yolu;
        public IkiliDosya Bea, Kmx, Col, Smx, Flo, Dmx, Loa;
        public readonly Dictionary<St4Kiris, int> KirisKaydi = new Dictionary<St4Kiris, int>();

        public static StaVeri Ac(string st4)
        {
            string taban = Path.Combine(Path.GetDirectoryName(st4), Path.GetFileNameWithoutExtension(st4));
            var v = new StaVeri
            {
                Model = St4Model.Oku(st4), St4Yolu = st4,
                Bea = new IkiliDosya(taban + ".BEA", 900), Kmx = new IkiliDosya(taban + ".KMX", 300),
                Col = new IkiliDosya(taban + ".COL", 900), Smx = new IkiliDosya(taban + ".SMX", 300),
                Flo = new IkiliDosya(taban + ".FLO", 200), Dmx = new IkiliDosya(taban + ".DMX", 250),
                Loa = new IkiliDosya(taban + ".LOA", 32),
            };
            v.KirisleriEsle();
            return v;
        }

        /// <summary>
        /// BEA/KMX kayıt sırası ST4 sırasından farklı olabilir: kiriş, BEA +0/+8 (int16) düğüm çifti ile ST4 ikinci satırdaki
        /// I/J düğümleri üzerinden eşlenir. Eşleşmeyen kalırsa sırayla boştaki kayıt verilir.
        /// </summary>
        private void KirisleriEsle()
        {
            if (!Bea.Var) return;
            // Analizden sonra kaydedilen ST4'te düğüm satırı sıfırlanabilir (CP_03): o zaman BEA sırası = ST4 sırası
            if (Model.Kirisler.All(k => k.I == 0 && k.J == 0))
            {
                foreach (var k in Model.Kirisler.Where(k => k.Sira < Bea.Adet)) KirisKaydi[k] = k.Sira;
                return;
            }
            var bos = new HashSet<int>(Enumerable.Range(0, Bea.Adet));
            foreach (var k in Model.Kirisler)
            {
                int bulunan = bos.Where(r => Bea[r].S(0) == k.I && Bea[r].S(8) == k.J).DefaultIfEmpty(-1).First();
                if (bulunan >= 0) { KirisKaydi[k] = bulunan; bos.Remove(bulunan); }
            }
            foreach (var k in Model.Kirisler.Where(k => !KirisKaydi.ContainsKey(k)))
            {
                int r = bos.Count > 0 ? bos.Min() : -1;
                if (r >= 0) { KirisKaydi[k] = r; bos.Remove(r); }
            }
        }

        /// <summary>Isı yükü durumu (dizi indisi 14) analizde var mı: GPR statik tablolarına "ISI" sütunu eklenir (K_BLG_03).</summary>
        public bool IsiVar => _isi ??= Enumerable.Range(0, Col.Adet).Any(r => new[] { 114, 202, 290, 378, 466, 554, 26 }.Any(o => Col[r].F(o + 56) != 0))
                                    || Enumerable.Range(0, Bea.Adet).Any(r => new[] { 38, 126, 390, 478 }.Any(o => Bea[r].F(o + 56) != 0));
        private bool? _isi;

        public int KolonKaydi(St4Kolon k) => k.Sira;    // COL/SMX kayıt sırası = ST4 /Columns Data/ sırası
        public int DosemeKaydi(St4Doseme d) => d.Sira;   // FLO/DMX kayıt sırası = ST4 /Floors Data/ sırası
    }
}
