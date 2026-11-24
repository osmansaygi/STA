using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ST4Yardimci
{
    /// <summary>KAPAK formu: mimari proje seçimi (kapak bilgileri okunur), düzeltilebilir alanlar, başlık ve pafta ölçeği.</summary>
    internal sealed class KapakForm : Beykent.Monolit.MonolitForm
    {
        private readonly string _cizimKlasoru;
        private readonly TextBox _txtDwg;
        private readonly ComboBox _cmbKapak, _cmbBaslik, _cmbOlcek;
        private readonly Label _lblBilgi;
        private readonly Dictionary<string, TextBox> _alan = new Dictionary<string, TextBox>();
        private List<MimariKapak> _kapaklar = new List<MimariKapak>();
        private bool _yukleniyor;

        public KapakVeri Veri { get; private set; }
        public string MimariDwg => _txtDwg.Text.Trim();
        private string _pdfKlasoru;
        /// <summary>"Rapor kapaklarını PDF kaydet" ile seçilen klasör (DialogResult.Yes).</summary>
        public string PdfKlasoru => _pdfKlasoru;

        private readonly bool _hatirlandi;
        private bool _gosteriliyor;
        private Color _normalRenk = SystemColors.WindowText;
        private static readonly Color HatirlananRenk = Color.FromArgb(200, 0, 0);
        private static readonly Color DosyaAdiRenk = Color.FromArgb(0, 140, 40);

        /// <param name="hatirlandi">Değerler bu çizimin kaydından geldi: dolu proje alanları kırmızı gösterilir (mimari proje yeniden okununca normale döner).</param>
        public KapakForm(KapakVeri veri, string sonDwg, string cizimKlasoru, string pdfKlasoru = null, bool hatirlandi = false)
        {
            Veri = veri ?? new KapakVeri();
            _cizimKlasoru = cizimKlasoru;
            _pdfKlasoru = pdfKlasoru;
            _hatirlandi = hatirlandi;
            Text = "Statik Proje Kapağı";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);

            const int sol = 12, genislik = 760, kolon = 374, sag = sol + kolon + 12;
            int y = 10;

            // mimari proje
            Controls.Add(new Label { Text = "Mimari proje (DWG) — kapak bilgileri buradan okunur", Location = new Point(sol, y), AutoSize = true });
            y += 20;
            _txtDwg = new TextBox { Location = new Point(sol, y), Size = new Size(genislik - 150, 24), ReadOnly = true };
            var btnSec = new Button { Text = "Seç ve oku…", Location = new Point(sol + genislik - 144, y - 1), Size = new Size(144, 26) };
            btnSec.Click += (s, e) => DwgSec();
            Controls.Add(_txtDwg);
            Controls.Add(btnSec);
            y += 30;
            Controls.Add(new Label { Text = "Kapak (blok)", Location = new Point(sol, y + 4), AutoSize = true });
            _cmbKapak = new ComboBox { Location = new Point(sol + 110, y), Size = new Size(264, 24), DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            _cmbKapak.SelectedIndexChanged += (s, e) => { if (!_yukleniyor) KapakUygula(); };
            Controls.Add(_cmbKapak);
            _lblBilgi = new Label { Location = new Point(sag, y + 4), Size = new Size(kolon, 20), ForeColor = Color.DimGray };
            Controls.Add(_lblBilgi);
            y += 34;

            // başlık / ölçek
            Controls.Add(new Label { Text = "Kapak başlığı", Location = new Point(sol, y + 4), AutoSize = true });
            _cmbBaslik = new ComboBox { Location = new Point(sol + 110, y), Size = new Size(264, 24), DropDownStyle = ComboBoxStyle.DropDown };
            _cmbBaslik.Items.AddRange(KapakRunner.Basliklar);
            Controls.Add(_cmbBaslik);
            Controls.Add(new Label { Text = "Pafta ölçeği", Location = new Point(sag, y + 4), AutoSize = true });
            _cmbOlcek = new ComboBox { Location = new Point(sag + 110, y), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (int o in KapakRunner.Olcekler) _cmbOlcek.Items.Add("1/" + o);
            Controls.Add(_cmbOlcek);
            y += 36;

            // arsa (sol) / bina (sağ)
            int ySol = y, ySag = y;
            Baslik("ARSA VE GENEL", sol, ref ySol);
            Satir("Nitelik", "Niteliği", sol, ref ySol);
            Satir("Blok", "Blok adı", sol, ref ySol);
            Satir("Mahalle", "Mahalle", sol, ref ySol);
            Satir("Pap", "Pafta / ada / parsel", sol, ref ySol);
            Satir("Yuzolcumu", "Yüzölçümü (m²)", sol, ref ySol);
            Satir("Ihtiva", "İhtiva ettiği yer", sol, ref ySol);
            Satir("Aciklama", "Açıklama", sol, ref ySol);
            Satir("Sahip", "Proje sahibi", sol, ref ySol);

            Baslik("BİNA", sag, ref ySag);
            Satir("Cinsi", "Cinsi", sag, ref ySag);
            Satir("Kat", "Kat adedi", sag, ref ySag);
            Satir("InsaatAlani", "Bina inşaat alanı (m²)", sag, ref ySag);
            Satir("Kullanim", "Kullanım amacı", sag, ref ySag);
            Satir("M2", "m² maliyeti (₺)", sag, ref ySag);
            Satir("Toplam", "Toplam maliyeti (₺)", sag, ref ySag);
            Satir("Sinif", "Mim. hiz. sın. oranı", sag, ref ySag);
            var btnToplam = new Button { Text = "Toplamı hesapla (alan × m² maliyeti)", Location = new Point(sag + 150, ySag), Size = new Size(kolon - 150, 26) };
            btnToplam.Click += (s, e) =>
            {
                string t = KapakRunner.ToplamHesapla(_alan["InsaatAlani"].Text, _alan["M2"].Text);
                if (t.Length > 0) _alan["Toplam"].Text = t;
                else _lblBilgi.Text = "Toplam için inşaat alanı ve m² maliyeti sayı olmalı.";
            };
            Controls.Add(btnToplam);
            ySag += 30;
            y = Math.Max(ySol, ySag) + 8;

            // imza kutuları
            const int ucKolon = 246, ara = 11;
            int y1 = y, y2 = y, y3 = y;
            Baslik("PROJE MÜELLİFİ", sol, ref y1);
            Satir("MuellifAd", "Adı soyadı", sol, ref y1, ucKolon, 86);
            Satir("MuellifUnvan", "Unvanı", sol, ref y1, ucKolon, 86);
            Satir("MuellifSicil", "Oda sicil no", sol, ref y1, ucKolon, 86);
            Satir("MuellifBuro", "Büro tescil no", sol, ref y1, ucKolon, 86);
            int x2 = sol + ucKolon + ara;
            Baslik("DENETÇİ / FENNİ MESUL (İNŞ. MÜH.)", x2, ref y2);
            Satir("FenniAd", "Adı soyadı", x2, ref y2, ucKolon, 86);
            Satir("FenniUnvan", "Unvanı", x2, ref y2, ucKolon, 86);
            Satir("FenniSicil", "Oda sicil no", x2, ref y2, ucKolon, 86);
            int x3 = x2 + ucKolon + ara;
            Baslik("YAPI DENETİM FİRMASI", x3, ref y3);
            Satir("DenetimFirma", "Firma", x3, ref y3, ucKolon, 86);
            Satir("DenetimBelge", "İzin belge no", x3, ref y3, ucKolon, 86);
            y = Math.Max(y1, Math.Max(y2, y3)) + 6;

            // tadilat konuları (kapağın altındaki boşluğa numaralı yazılır)
            Baslik("TADİLAT KONULARI — her satıra bir madde (boşsa yazılmaz)", sol, ref y);
            var tadilat = new TextBox
            {
                Location = new Point(sol, y), Size = new Size(genislik, 72), Multiline = true, AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true
            };
            Controls.Add(tadilat);
            _alan["Tadilat"] = tadilat;
            y += 80;

            var btnCiz = new Button { Text = "Kapağı çiz", Location = new Point(sol + genislik - 196, y), Size = new Size(110, 28) };
            btnCiz.Click += (s, e) => { Topla(); DialogResult = DialogResult.OK; Close(); };
            var btnIptal = new Button { Text = "İptal", Location = new Point(sol + genislik - 80, y), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            // Rapor kapakları: STATİK / KALIP İSKELE / İSKELE HESAP RAPORLARI ayrı ayrı A4 PDF (seçilen klasöre)
            var btnPdf = new Button { Text = "Rapor kapaklarını PDF kaydet…", Location = new Point(sol, y), Size = new Size(220, 28) };
            btnPdf.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog
                {
                    Description = "Rapor kapaklarının (Statik, Kalıp İskele, İskele hesap raporları) kaydedileceği klasör",
                    ShowNewFolderButton = true
                })
                {
#if AUTOCAD2027
                    d.UseDescriptionForTitle = true;
#endif
                    string ilk = !string.IsNullOrEmpty(_pdfKlasoru) && Directory.Exists(_pdfKlasoru) ? _pdfKlasoru
                        : MimariDwg.Length > 0 ? Path.GetDirectoryName(MimariDwg) : _cizimKlasoru;
                    if (!string.IsNullOrEmpty(ilk) && Directory.Exists(ilk)) d.SelectedPath = ilk;
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    _pdfKlasoru = d.SelectedPath;
                }
                Topla();
                DialogResult = DialogResult.Yes;
                Close();
            };
            Controls.Add(btnPdf);
            Controls.Add(btnCiz);
            Controls.Add(btnIptal);
            AcceptButton = btnCiz;
            CancelButton = btnIptal;
            ClientSize = new Size(sol * 2 + genislik, y + 40);

            Goster();
            _txtDwg.Text = !string.IsNullOrEmpty(sonDwg) && File.Exists(sonDwg) ? sonDwg : "";
            _lblBilgi.Text = "Mimari projeyi seçin; okunamayan alanları elle doldurun.";

            // Elle değiştirilen alan normal renge döner (artık kullanıcının girdisi).
            foreach (TextBox t in _alan.Values)
            {
                TextBox tb = t;
                tb.TextChanged += (s, e) => { if (!_gosteriliyor) tb.ForeColor = _normalRenk; };
            }
            // Renkler MonolitForm.OnLoad temasından sonra verilir.
            Shown += (s, e) =>
            {
                _normalRenk = _alan["Mahalle"].ForeColor;
                if (!_hatirlandi) return;
                int n = 0;
                foreach (string ad in KapakRunner.HatirlananAlanlar)
                    if (_alan.TryGetValue(ad, out TextBox tb) && tb.Text.Trim().Length > 0 && !(ad == "FenniUnvan" && _alan["FenniAd"].Text.Trim().Length == 0))
                    { tb.ForeColor = HatirlananRenk; n++; }
                _lblBilgi.ForeColor = HatirlananRenk;
                _lblBilgi.Text = n > 0 ? "Kırmızı: önceden girilen değerler (okutunca yenilenir)."
                                       : "Bu çizim için kayıt var; mimari projeyi okutabilirsiniz.";
            };
        }

        /// <summary>Mimari proje yeniden okununca hatırlanan (kırmızı) gösterim kalkar.</summary>
        private void RenkleriSifirla()
        {
            foreach (TextBox tb in _alan.Values) tb.ForeColor = _normalRenk;
            _lblBilgi.ForeColor = Color.DimGray;
        }

        private void Baslik(string metin, int x, ref int y)
        {
            Controls.Add(new Label { Text = metin, Location = new Point(x, y), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            y += 22;
        }

        private void Satir(string ad, string etiket, int x, ref int y, int genislik = 374, int etiketW = 150)
        {
            Controls.Add(new Label { Text = etiket, Location = new Point(x, y + 4), Size = new Size(etiketW - 4, 20) });
            var t = new TextBox { Location = new Point(x + etiketW, y), Size = new Size(genislik - etiketW, 24) };
            Controls.Add(t);
            _alan[ad] = t;
            y += 28;
        }

        private static string Oku(KapakVeri v, string ad) => (string)typeof(KapakVeri).GetField(ad).GetValue(v) ?? "";

        /// <summary>Veri → form.</summary>
        private void Goster()
        {
            _gosteriliyor = true;
            foreach (var kv in _alan) kv.Value.Text = Oku(Veri, kv.Key);
            _gosteriliyor = false;
            _cmbBaslik.Text = string.IsNullOrWhiteSpace(Veri.Baslik) ? KapakRunner.Basliklar[0] : Veri.Baslik;
            int i = Array.IndexOf(KapakRunner.Olcekler, Veri.Olcek);
            _cmbOlcek.SelectedIndex = i >= 0 ? i : 0;
        }

        /// <summary>Form → veri.</summary>
        internal void Topla()
        {
            foreach (var kv in _alan) typeof(KapakVeri).GetField(kv.Key).SetValue(Veri, kv.Value.Text.Trim());
            Veri.Baslik = string.IsNullOrWhiteSpace(_cmbBaslik.Text) ? KapakRunner.Basliklar[0] : _cmbBaslik.Text.Trim().ToUpper(MimariKapakOku.Tr);
            Veri.Olcek = KapakRunner.Olcekler[Math.Max(0, _cmbOlcek.SelectedIndex)];
            if (string.IsNullOrWhiteSpace(Veri.Toplam)) Veri.Toplam = KapakRunner.ToplamHesapla(Veri.InsaatAlani, Veri.M2);
        }

        private void DwgSec()
        {
            using (var d = new OpenFileDialog { Title = "Mimari proje", Filter = "AutoCAD çizimi (*.dwg)|*.dwg", CheckFileExists = true })
            {
                string klasor = MimariDwg.Length > 0 ? Path.GetDirectoryName(MimariDwg) : _cizimKlasoru;
                if (!string.IsNullOrEmpty(klasor) && Directory.Exists(klasor)) d.InitialDirectory = klasor;
                if (d.ShowDialog(this) == DialogResult.OK) DwgOku(d.FileName);
            }
        }

        private void DwgOku(string yol)
        {
            _txtDwg.Text = yol;
            _kapaklar = new List<MimariKapak>();
            try
            {
                Cursor = Cursors.WaitCursor;
                _kapaklar = MimariKapakOku.Oku(yol);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Autodesk.AutoCAD.Runtime.Exception)
            {
                _lblBilgi.Text = "Okunamadı: " + ex.Message;
            }
            finally { Cursor = Cursors.Default; }

            // Çok kapaklı projede (A/B blok, otopark …) hangi bloğun kapağı çizileceği sorulur.
            int sec = _kapaklar.Count == 1 ? 0 : _kapaklar.Count > 1 ? BlokSor(_kapaklar) : -1;

            _yukleniyor = true;
            _cmbKapak.Items.Clear();
            foreach (MimariKapak k in _kapaklar) _cmbKapak.Items.Add(k);
            _cmbKapak.Enabled = _kapaklar.Count > 1;
            _cmbKapak.SelectedIndex = sec;
            _yukleniyor = false;
            if (sec >= 0) KapakUygula();
            else if (_kapaklar.Count > 1) _lblBilgi.Text = _kapaklar.Count + " kapak bulundu; listeden bloğu seçin.";
        }

        /// <summary>Kapak (blok) seçim penceresi. Vazgeçilirse -1 (alanlar değişmez, listeden sonra seçilebilir).</summary>
        private int BlokSor(List<MimariKapak> kapaklar)
        {
            using (var f = new Beykent.Monolit.MonolitForm())
            {
                f.Text = "Kapak seçimi";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ShowInTaskbar = false; f.MinimizeBox = false; f.MaximizeBox = false;
                f.Font = new Font("Segoe UI", 9f);
                f.Controls.Add(new Label
                {
                    Text = "Bu mimari projede " + kapaklar.Count + " kapak var. Hangi bloğun statik kapağını hazırlayacaksınız?",
                    Location = new Point(12, 12), Size = new Size(396, 36)
                });
                var liste = new ListBox { Location = new Point(12, 50), Size = new Size(396, 170), IntegralHeight = false };
                foreach (MimariKapak k in kapaklar)
                {
                    string ayrinti = string.Join(" · ", new[] { k.Al(MimariKapakOku.AKat), k.Al(MimariKapakOku.AKullanim), k.Al(MimariKapakOku.ASinif) }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                    liste.Items.Add(k.Gorunen + (ayrinti.Length > 0 ? "   —   " + ayrinti : ""));
                }
                liste.SelectedIndex = 0;
                var tamam = new Button { Text = "Seç", Location = new Point(246, 230), Size = new Size(80, 28), DialogResult = DialogResult.OK };
                var iptal = new Button { Text = "Vazgeç", Location = new Point(332, 230), Size = new Size(76, 28), DialogResult = DialogResult.Cancel };
                liste.DoubleClick += (s, e) => { if (liste.SelectedIndex >= 0) { f.DialogResult = DialogResult.OK; f.Close(); } };
                f.Controls.Add(liste); f.Controls.Add(tamam); f.Controls.Add(iptal);
                f.AcceptButton = tamam; f.CancelButton = iptal;
                f.ClientSize = new Size(420, 270);
                return f.ShowDialog(this) == DialogResult.OK ? liste.SelectedIndex : -1;
            }
        }

        private void KapakUygula()
        {
            if (!(_cmbKapak.SelectedItem is MimariKapak k)) return;
            Topla();
            KapakRunner.ProjeyiTemizle(Veri); // okunamayan alan boş kalsın (önceki projeden değer taşınmaz)
            bool sahipDosyadan = KapakRunner.Uygula(Veri, k, MimariDwg);
            Goster();
            RenkleriSifirla();
            int dolu = k.Alan.Count;
            _lblBilgi.Text = dolu == 0 ? "Kapak bilgisi bulunamadı; alanları elle doldurun."
                : (_kapaklar.Count > 1 ? _kapaklar.Count + " kapak bulundu. " : "") + dolu + " alan mimari kapaktan okundu.";
            if (sahipDosyadan)
            {
                // Proje sahibi kapakta yoktu, mimari DWG dosya adından alındı: yeşil (kontrol edin; elle değişince normal renk)
                _alan["Sahip"].ForeColor = DosyaAdiRenk;
                _lblBilgi.Text += " Yeşil: sahip dosya adından.";
            }
        }
    }
}
