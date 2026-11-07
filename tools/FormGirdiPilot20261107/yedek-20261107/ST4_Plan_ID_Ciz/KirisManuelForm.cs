using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// KIRISCIZ formu: solda mesnetler (tip + en), sağda açıklıklar (bir satır = bir açıklık, donatı "3ø16" yazımı),
    /// altta canlı şematik görünüş ve seçili açıklığın As özeti. Son girdi hatırlanır; .kiris dosyasına kaydedilir/açılır.
    /// </summary>
    internal sealed class KirisManuelForm : Beykent.Monolit.MonolitForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] TipAdlari = { "Kolon", "Kiriş (saplama)", "Serbest uç" };
        private static readonly int[] Betonlar = { 20, 25, 30, 35, 40, 45, 50 };
        private static readonly int[] Celikler = { 420, 500 };

        private readonly ComboBox _beton, _celik;
        private readonly NumericUpDown _paspayi, _katKotu;
        private readonly CheckBox _sonKat, _ikinciGonye, _tekEtriye, _pafta100;
        /// <summary>Son onaylanan form: 1/100 paftaya konacak (açılım ×2).</summary>
        internal static bool Pafta100 { get; private set; }
        private readonly DataGridView _mesnet, _aciklik;
        private readonly Panel _onizleme;
        private readonly Label _bilgi;
        private bool _yukleniyor;

        public KirisManuelVeri Veri { get; private set; }
        /// <summary>"Kalıp planından al": formu gizleyip çizimden okur (AutoCAD tarafı komut sınıfında verilir).</summary>
        private Func<Form, KirisPlanOkuma> _plandanSec;

        internal static bool TryShow(out KirisManuelVeri veri, Func<Form, KirisPlanOkuma> plandanSec = null)
        {
            veri = null;
            using (var form = new KirisManuelForm())
            {
                form._plandanSec = plandanSec;
                if (AcApp.ShowModalDialog(form) != DialogResult.OK) return false;
                veri = form.Veri;
                return veri != null;
            }
        }

        private KirisManuelForm()
        {
            Text = "Kiriş Çiz — manuel kiriş açılımı (1/25)";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1240, 660);
            MinimumSize = new Size(980, 560);

            // Genel
            var genel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
            _beton = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            foreach (int c in Betonlar) _beton.Items.Add("C" + c);
            _celik = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
            foreach (int c in Celikler) _celik.Items.Add("B" + c + "C");
            _paspayi = new NumericUpDown { DecimalPlaces = 1, Increment = 0.5m, Minimum = 0.5m, Maximum = 15m, Width = 60 };
            _katKotu = new NumericUpDown { DecimalPlaces = 0, Minimum = -10000m, Maximum = 100000m, Width = 80 };
            _sonKat = new CheckBox { Text = "Son kat (kolon üstte devam etmez)", AutoSize = true, Margin = new Padding(16, 5, 3, 3) };
            _ikinciGonye = new CheckBox { Text = "İkinci gönyeyi kaldır (alt)", AutoSize = true, Margin = new Padding(12, 5, 3, 3) };
            _tekEtriye = new CheckBox { Text = "Tek etriye", AutoSize = true, Margin = new Padding(12, 5, 3, 3) };
            _pafta100 = new CheckBox { Text = "1/100 pafta (×2)", AutoSize = true, Margin = new Padding(12, 5, 3, 3),
                Checked = PlanFormSettings.Load("kirisciz").Bool("Pafta100", false) };
            Label Etiket(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(10, 7, 3, 3) };
            genel.Controls.AddRange(new Control[] { Etiket("Beton"), _beton, Etiket("Çelik"), _celik, Etiket("Paspayı (etriye dışı) cm"), _paspayi,
                Etiket("Kat kotu cm"), _katKotu, _sonKat, _ikinciGonye, _tekEtriye, _pafta100 });

            // Tablolar
            _mesnet = Tablo();
            _mesnet.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mesnet", ReadOnly = true, Width = 58 });
            var tip = new DataGridViewComboBoxColumn { HeaderText = "Tip", Width = 120, FlatStyle = FlatStyle.Flat };
            tip.Items.AddRange(TipAdlari);
            _mesnet.Columns.Add(tip);
            _mesnet.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Eni cm", Width = 58 });

            _aciklik = Tablo();
            foreach (var (baslik, en, ipucu) in new[]
            {
                ("Ad", 56, "Kiriş adı (K01 …)"), ("b", 42, "Genişlik cm"), ("h", 42, "Yükseklik cm"), ("Ln", 50, "Net açıklık (mesnet yüzleri arası) cm"),
                ("Δkot", 44, "Kiriş üstünün kat kotuna göre farkı cm (düşük kiriş: -20)"), ("Döşeme", 58, "Döşeme kalınlığı cm (0 = döşeme yok)"),
                ("Üst", 56, "Üst montaj (sürekli) donatı. ø gerekmez: 3 16 · 3/16 · 316 → 3ø16"), ("Alt", 56, "Alt düz (sürekli) donatı. 3 16 · 316 → 3ø16"),
                ("Gövde", 56, "Gövde donatısı toplam adet. 2 12 · 212 → 2ø12"),
                ("Sol üst ilave", 70, "Sol mesnet üst ilave. 2 14 · 214 → 2ø14 (boş = yok)"), ("Sağ üst ilave", 70, "Sağ mesnet üst ilave. 2 14 · 214 → 2ø14 (boş = yok)"),
                ("Sol alt ilave", 70, "Sol mesnet alt ilave (boş = yok)"), ("Sağ alt ilave", 70, "Sağ mesnet alt ilave (boş = yok)"),
                ("Etriye", 82, "Çap sık orta: 10 10 20 · 10/10/20 → ø10/10/20 (tek aralık: 8 15)")
            })
                _aciklik.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = baslik, Width = en, ToolTipText = ipucu, SortMode = DataGridViewColumnSortMode.NotSortable });
            _aciklik.Columns[0].Frozen = true;

            var mesnetKutu = new GroupBox { Text = "Mesnetler (soldan sağa)", Dock = DockStyle.Fill, Padding = new Padding(6) };
            mesnetKutu.Controls.Add(_mesnet);
            var aciklikKutu = new GroupBox { Text = "Açıklıklar · ø yazmanız gerekmez: donatı 3 16 / 316 → 3ø16 · etriye 10 10 20 → ø10/10/20", Dock = DockStyle.Fill, Padding = new Padding(6) };
            aciklikKutu.Controls.Add(_aciklik);
            var bolme = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
            bolme.Panel1.Controls.Add(mesnetKutu);
            bolme.Panel2.Controls.Add(aciklikKutu);

            // Satır düğmeleri
            var satir = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(6, 4, 6, 0) };
            Button Dugme(string t, int w, EventHandler h) { var b = new Button { Text = t, Width = w, Height = 27 }; b.Click += h; return b; }
            var plandan = Dugme("Kalıp planından al…", 140, (s, e) => PlandanAl());
            new ToolTip().SetToolTip(plandan, "Kalıp planında kiriş boyunca yüzleri sırayla tıklayın: ilk mesnet dış yüzü, açıklık başı, açıklık sonu, … son mesnet dış yüzü, Enter.\n" +
                "Mesnet enleri ve Ln ölçülür; açıklık üzerindeki \"MC-01 (25/55)\" yazısından ad ve b/h, mesnetteki taramadan Kolon/Kiriş tipi alınır.\n" +
                "Donatı, etriye, döşeme ve Δkot mevcut satırlardan korunur. Konsol: ilk istemde K; son konsolda açıklık sonundan sonra Enter.");
            satir.Controls.AddRange(new Control[]
            {
                plandan,
                Dugme("+ Açıklık ekle", 110, (s, e) => AciklikEkle()),
                Dugme("− Son açıklığı sil", 120, (s, e) => AciklikSil()),
                Dugme("Seçiliyi tüm açıklıklara uygula", 200, (s, e) => SeciliyiUygula()),
                Dugme("Varsayılan", 90, (s, e) => Yukle(KirisManuelVeri.Varsayilan())),
                Dugme("Aç…", 70, (s, e) => DosyaAc()),
                Dugme("Kaydet…", 80, (s, e) => DosyaKaydet())
            });

            var orta = new Panel { Dock = DockStyle.Fill };
            orta.Controls.Add(bolme);
            orta.Controls.Add(satir);

            // Önizleme + bilgi + alt düğmeler
            _onizleme = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_onizleme, true);
            _onizleme.Paint += OnizlemeCiz;
            _onizleme.Resize += (s, e) => _onizleme.Invalidate();
            _bilgi = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), AutoEllipsis = true };
            var ciz = new Button { Text = "Çiz", Width = 96, Height = 30, Anchor = AnchorStyles.Right };
            var iptal = new Button { Text = "İptal", Width = 96, Height = 30, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
            ciz.Click += (s, e) => Ciz();
            var alt = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 46, ColumnCount = 3, Padding = new Padding(4, 6, 8, 6) };
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            alt.Controls.Add(_bilgi, 0, 0);
            alt.Controls.Add(ciz, 1, 0);
            alt.Controls.Add(iptal, 2, 0);

            Controls.Add(orta);
            Controls.Add(_onizleme);
            Controls.Add(alt);
            Controls.Add(genel);
            AcceptButton = null; // Enter tabloda alt satıra geçsin.
            CancelButton = iptal;

            Load += (s, e) => { bolme.SplitterDistance = 270; };
            foreach (var g in new[] { _mesnet, _aciklik })
            {
                g.CellValueChanged += (s, e) => Degisti();
                g.CurrentCellDirtyStateChanged += (s, e) => { if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                g.DataError += (s, e) => e.ThrowException = false;
            }
            _aciklik.SelectionChanged += (s, e) => BilgiYaz();
            // ø'süz yazımı hücreden çıkınca standart biçime çevir (3 16 → 3ø16, 10 10 20 → ø10/10/20).
            _aciklik.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 6) return;
                var hucre = _aciklik.Rows[e.RowIndex].Cells[e.ColumnIndex];
                string eski = Convert.ToString(hucre.Value, Inv) ?? "";
                string yeni = e.ColumnIndex == 13 ? KirisManuelVeri.EtriyeBicim(eski) : KirisManuelVeri.DonatiBicim(eski);
                if (yeni != eski) hucre.Value = yeni;
            };
            _katKotu.ValueChanged += (s, e) => Degisti();
            _paspayi.ValueChanged += (s, e) => Degisti();
            _sonKat.CheckedChanged += (s, e) => Degisti();

            KirisManuelVeri son = null;
            try { son = KirisManuelVeri.Oku(PlanFormSettings.Load("kirisciz").Text("Veri", null)); } catch { }
            Yukle(son ?? KirisManuelVeri.Varsayilan());
        }

        private static DataGridView Tablo() => new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ShowCellToolTips = true
        };

        // ---------------------------------------------------------------- veri ↔ tablo

        private void Yukle(KirisManuelVeri v)
        {
            _yukleniyor = true;
            try
            {
                int bi = Array.IndexOf(Betonlar, v.Fck);
                _beton.SelectedIndex = bi >= 0 ? bi : 2;
                int ci = Array.IndexOf(Celikler, v.Fyk);
                _celik.SelectedIndex = ci >= 0 ? ci : 0;
                _paspayi.Value = Math.Max(_paspayi.Minimum, Math.Min(_paspayi.Maximum, (decimal)v.PaspayiCm));
                _katKotu.Value = Math.Max(_katKotu.Minimum, Math.Min(_katKotu.Maximum, (decimal)Math.Round(v.KatKotuCm)));
                _sonKat.Checked = v.SonKat;
                _ikinciGonye.Checked = v.IkinciGonyeKaldir;
                _tekEtriye.Checked = v.TekEtriye;
                _mesnet.Rows.Clear();
                foreach (var m in v.Mesnetler) MesnetSatiri(m);
                _aciklik.Rows.Clear();
                foreach (var a in v.Acikliklar) AciklikSatiri(a);
                Numarala();
            }
            finally { _yukleniyor = false; }
            Degisti();
        }

        private void MesnetSatiri(KirisManuelMesnet m)
            => _mesnet.Rows.Add("", TipAdlari[(int)m.Tip], S(m.EniCm));

        private void AciklikSatiri(KirisManuelAciklik a)
            => _aciklik.Rows.Add(a.Ad, S(a.BCm), S(a.HCm), S(a.LnCm), S(a.DkotCm), S(a.DosemeCm),
                B(a.Ust), B(a.Alt), B(a.Govde), B(a.SolUst), B(a.SagUst), B(a.SolAlt), B(a.SagAlt), KirisManuelVeri.EtriyeBicim(a.Etriye));

        private static string B(string donati) => KirisManuelVeri.DonatiBicim(donati);

        private static string S(double v) => v.ToString("0.##", Inv);

        private void Numarala()
        {
            for (int i = 0; i < _mesnet.Rows.Count; i++) _mesnet.Rows[i].Cells[0].Value = (i + 1).ToString(Inv);
        }

        private static string Hucre(DataGridViewRow r, int i) => Convert.ToString(r.Cells[i].Value, Inv)?.Trim() ?? "";

        private static double Sayi(string s, double varsayilan)
            => double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, Inv, out double d) ? d : varsayilan;

        /// <summary>Tablolardan veri; sayı okunamazsa NaN (doğrulama yakalar).</summary>
        private KirisManuelVeri Topla()
        {
            var v = new KirisManuelVeri
            {
                Fck = Betonlar[Math.Max(0, _beton.SelectedIndex)],
                Fyk = Celikler[Math.Max(0, _celik.SelectedIndex)],
                PaspayiCm = (double)_paspayi.Value,
                KatKotuCm = (double)_katKotu.Value,
                SonKat = _sonKat.Checked,
                IkinciGonyeKaldir = _ikinciGonye.Checked,
                TekEtriye = _tekEtriye.Checked
            };
            foreach (DataGridViewRow r in _mesnet.Rows)
            {
                int t = Array.IndexOf(TipAdlari, Hucre(r, 1));
                v.Mesnetler.Add(new KirisManuelMesnet { Tip = t > 0 ? (KirisManuelMesnetTip)t : KirisManuelMesnetTip.Kolon, EniCm = Sayi(Hucre(r, 2), double.NaN) });
            }
            foreach (DataGridViewRow r in _aciklik.Rows)
            {
                v.Acikliklar.Add(new KirisManuelAciklik
                {
                    Ad = Hucre(r, 0), BCm = Sayi(Hucre(r, 1), double.NaN), HCm = Sayi(Hucre(r, 2), double.NaN), LnCm = Sayi(Hucre(r, 3), double.NaN),
                    DkotCm = Sayi(Hucre(r, 4), 0), DosemeCm = Sayi(Hucre(r, 5), 0),
                    Ust = Hucre(r, 6), Alt = Hucre(r, 7), Govde = Hucre(r, 8), SolUst = Hucre(r, 9), SagUst = Hucre(r, 10),
                    SolAlt = Hucre(r, 11), SagAlt = Hucre(r, 12), Etriye = Hucre(r, 13)
                });
            }
            return v;
        }

        // ---------------------------------------------------------------- satır işlemleri

        private static string SonrakiAd(string ad)
        {
            var m = Regex.Match(ad ?? "", @"^(.*?)(\d+)$");
            if (!m.Success) return (ad ?? "K") + "2";
            string sayi = (int.Parse(m.Groups[2].Value, Inv) + 1).ToString(Inv).PadLeft(m.Groups[2].Value.Length, '0');
            return m.Groups[1].Value + sayi;
        }

        private void AciklikEkle()
        {
            var v = Topla();
            if (v.Acikliklar.Count >= 30) return;
            var son = v.Acikliklar.Count > 0 ? v.Acikliklar[v.Acikliklar.Count - 1].Kopya() : new KirisManuelAciklik();
            son.Ad = SonrakiAd(son.Ad);
            if (double.IsNaN(son.BCm)) son.BCm = 30;
            if (double.IsNaN(son.HCm)) son.HCm = 60;
            if (double.IsNaN(son.LnCm)) son.LnCm = 450;
            var sonMesnet = v.Mesnetler.Count > 0 ? v.Mesnetler[v.Mesnetler.Count - 1] : new KirisManuelMesnet();
            if (sonMesnet.Tip == KirisManuelMesnetTip.Serbest || double.IsNaN(sonMesnet.EniCm))
            {
                // Serbest uç artık ara mesnet olur: kolona çevir, yeni uç serbest kalsın.
                if (_mesnet.Rows.Count > 0)
                {
                    var r = _mesnet.Rows[_mesnet.Rows.Count - 1];
                    r.Cells[1].Value = TipAdlari[0];
                    if (double.IsNaN(Sayi(Hucre(r, 2), double.NaN)) || Sayi(Hucre(r, 2), 0) < 10) r.Cells[2].Value = "50";
                }
            }
            _yukleniyor = true;
            AciklikSatiri(son);
            MesnetSatiri(new KirisManuelMesnet { Tip = sonMesnet.Tip, EniCm = double.IsNaN(sonMesnet.EniCm) || sonMesnet.EniCm < 10 ? 50 : sonMesnet.EniCm });
            Numarala();
            _yukleniyor = false;
            Degisti();
        }

        private void AciklikSil()
        {
            if (_aciklik.Rows.Count <= 1) return;
            _yukleniyor = true;
            _aciklik.Rows.RemoveAt(_aciklik.Rows.Count - 1);
            _mesnet.Rows.RemoveAt(_mesnet.Rows.Count - 1);
            _yukleniyor = false;
            Degisti();
        }

        /// <summary>Seçili hücrenin sütun değerini (ad hariç) bütün açıklıklara yazar.</summary>
        private void SeciliyiUygula()
        {
            var c = _aciklik.CurrentCell;
            if (c == null || c.ColumnIndex == 0)
            {
                MessageBox.Show(this, "Önce açıklık tablosunda kopyalanacak hücreyi seçin (ad sütunu hariç).", Text);
                return;
            }
            _aciklik.EndEdit();
            object deger = c.Value;
            _yukleniyor = true;
            foreach (DataGridViewRow r in _aciklik.Rows) r.Cells[c.ColumnIndex].Value = deger;
            _yukleniyor = false;
            Degisti();
        }

        private void PlandanAl()
        {
            if (_plandanSec == null) { MessageBox.Show(this, "Kalıp planından seçim bu ortamda kullanılamıyor.", Text); return; }
            _aciklik.EndEdit(); _mesnet.EndEdit();
            KirisPlanOkuma o;
            try { o = _plandanSec(this); }
            catch (Exception ex) { MessageBox.Show(this, "Kalıp planından okunamadı: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (o == null) return;
            Yukle(KirisPlanOkuma.Uygula(Topla(), o));
        }

        private void DosyaAc()
        {
            using (var dlg = new OpenFileDialog { Filter = "Kiriş verisi (*.kiris)|*.kiris|Tüm dosyalar (*.*)|*.*", Title = "Kiriş verisi aç" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                KirisManuelVeri v = null;
                try { v = KirisManuelVeri.Oku(File.ReadAllText(dlg.FileName, Encoding.UTF8)); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
                if (v == null) { MessageBox.Show(this, "Dosyada kiriş verisi yok.", Text); return; }
                Yukle(v);
            }
        }

        private void DosyaKaydet()
        {
            _aciklik.EndEdit(); _mesnet.EndEdit();
            using (var dlg = new SaveFileDialog { Filter = "Kiriş verisi (*.kiris)|*.kiris", Title = "Kiriş verisini kaydet", FileName = "kiris.kiris" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, Topla().Yaz().Replace("¦", Environment.NewLine), Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            }
        }

        // ---------------------------------------------------------------- doğrulama, bilgi, önizleme

        private void Degisti()
        {
            if (_yukleniyor) return;
            Boya();
            BilgiYaz();
            _onizleme.Invalidate();
        }

        /// <summary>Okunamayan donatı/sayı hücrelerini açık kırmızı gösterir.</summary>
        private void Boya()
        {
            Color hata = Color.FromArgb(255, 222, 222);
            foreach (DataGridViewRow r in _aciklik.Rows)
            {
                for (int i = 1; i < r.Cells.Count; i++)
                {
                    string s = Hucre(r, i);
                    bool ok = i <= 5 ? (i >= 4 && s.Length == 0) || !double.IsNaN(Sayi(s, double.NaN))
                        : i == 13 ? KirisManuelVeri.TryEtriye(s, out _, out _, out _)
                        : KirisManuelVeri.TryDonati(s, out _, out _);
                    r.Cells[i].Style.BackColor = ok ? Color.Empty : hata;
                }
            }
            foreach (DataGridViewRow r in _mesnet.Rows)
            {
                bool serbest = Hucre(r, 1) == TipAdlari[2];
                double eni = Sayi(Hucre(r, 2), double.NaN);
                r.Cells[2].Style.BackColor = serbest || (eni >= 10 && eni <= 500) ? Color.Empty : hata;
                r.Cells[2].Style.ForeColor = serbest ? SystemColors.GrayText : Color.Empty;
            }
        }

        private void BilgiYaz()
        {
            if (_yukleniyor) return;
            var v = Topla();
            if (!v.Dogrula(out string hata))
            {
                _bilgi.ForeColor = Color.FromArgb(176, 32, 32);
                _bilgi.Text = "⚠ " + hata;
                return;
            }
            _bilgi.ForeColor = SystemColors.ControlText;
            int i = _aciklik.CurrentCell?.RowIndex ?? 0;
            if (i < 0 || i >= v.Acikliklar.Count) i = 0;
            var a = v.Acikliklar[i];
            double As(string d) { KirisManuelVeri.TryDonati(d, out int n, out int c); return KirisManuelVeri.AsCm2(n, c); }
            double toplam = v.Acikliklar.Sum(x => x.LnCm) + v.Mesnetler.Sum(m => m.Tip == KirisManuelMesnetTip.Serbest ? 0 : m.EniCm);
            _bilgi.Text = string.Format(Inv,
                "{0} ({1:0}/{2:0}) · Üst As {3:0.00} cm² · Alt As {4:0.00} cm² · Sol mesnet üst {5:0.00} · Sağ mesnet üst {6:0.00} cm² · Toplam boy {7:0} cm · {8} açıklık",
                a.Ad, a.BCm, a.HCm, As(a.Ust), As(a.Alt), As(a.Ust) + As(a.SolUst), As(a.Ust) + As(a.SagUst), toplam, v.Acikliklar.Count);
        }

        private void OnizlemeCiz(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            KirisManuelVeri v;
            try { v = Topla(); } catch { return; }
            int n = v.Acikliklar.Count;
            if (n == 0 || v.Mesnetler.Count != n + 1) return;
            double Eni(int k) { var m = v.Mesnetler[k]; return m.Tip == KirisManuelMesnetTip.Serbest || double.IsNaN(m.EniCm) ? 0 : Math.Max(0, m.EniCm); }
            double Ln(int i) { double l = v.Acikliklar[i].LnCm; return double.IsNaN(l) || l < 1 ? 100 : l; }
            double H(int i) { double h = v.Acikliklar[i].HCm; return double.IsNaN(h) || h < 1 ? 50 : h; }
            double toplam = 0;
            for (int k = 0; k <= n; k++) toplam += Eni(k);
            for (int i = 0; i < n; i++) toplam += Ln(i);
            double Kot(double k) => double.IsNaN(k) ? 0 : k;
            double maxUst = Enumerable.Range(0, n).Max(i => Kot(v.Acikliklar[i].DkotCm));
            double minAlt = Enumerable.Range(0, n).Min(i => Kot(v.Acikliklar[i].DkotCm) - H(i));
            float sol = 24, sag = _onizleme.ClientSize.Width - 24, ust = 8, altSinir = _onizleme.ClientSize.Height - 22;
            // Şematik: yatay ölçek genişliğe sığar, düşey en çok 3 kat abartılır; görünüş dikeyde ortalanır.
            double olcek = (sag - sol) / Math.Max(toplam, 1);
            double olcekY = Math.Min(olcek * 3.0, (altSinir - ust - 50) / Math.Max(maxUst - minAlt, 1));
            if (olcek <= 0 || olcekY <= 0) return;
            float kirisUst = (float)(ust + (altSinir - ust - (maxUst - minAlt) * olcekY) / 2.0);
            float Y(double kotCm) => (float)(kirisUst + (maxUst - Kot(kotCm)) * olcekY);

            using (var beton = new SolidBrush(Color.FromArgb(243, 230, 205)))
            using (var kolon = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(150, 130, 98), Color.FromArgb(226, 208, 174)))
            using (var cizgi = new Pen(Color.FromArgb(110, 94, 70), 1.2f))
            using (var donati = new Pen(Color.FromArgb(30, 78, 214), 1.6f))
            using (var vurgu = new Pen(Color.FromArgb(244, 122, 24), 1.4f))
            using (var yazi = new SolidBrush(Color.FromArgb(40, 42, 46)))
            using (var soluk = new SolidBrush(Color.FromArgb(103, 102, 98)))
            using (var kucuk = new Font("Segoe UI", 8f))
            using (var kalin = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                var orta = new StringFormat { Alignment = StringAlignment.Center };
                double x = sol;
                for (int k = 0; k <= n; k++)
                {
                    var m = v.Mesnetler[k];
                    float w = (float)(Eni(k) * olcek);
                    double kotSol = k > 0 ? v.Acikliklar[k - 1].DkotCm : v.Acikliklar[0].DkotCm;
                    double kotSag = k < n ? v.Acikliklar[k].DkotCm : kotSol;
                    double hSol = k > 0 ? H(k - 1) : H(0), hSag = k < n ? H(k) : hSol;
                    float yTop = Y(Math.Max(kotSol, kotSag)), yBot = Y(Math.Min(kotSol - hSol, kotSag - hSag));
                    if (m.Tip == KirisManuelMesnetTip.Kolon)
                    {
                        float y0 = v.SonKat ? yTop : ust, y1 = Math.Min(altSinir - 4, yBot + 30);
                        g.FillRectangle(kolon, (float)x, y0, w, y1 - y0);
                        g.DrawRectangle(cizgi, (float)x, y0, w, y1 - y0);
                    }
                    else if (m.Tip == KirisManuelMesnetTip.Kiris)
                    {
                        g.FillRectangle(beton, (float)x, yTop, w, yBot - yTop);
                        g.DrawRectangle(cizgi, (float)x, yTop, w, yBot - yTop);
                        g.DrawLine(cizgi, (float)x, yTop, (float)x + w, yBot);
                        g.DrawLine(cizgi, (float)x + w, yTop, (float)x, yBot);
                    }
                    g.DrawString(m.Tip == KirisManuelMesnetTip.Serbest ? (k + 1).ToString(Inv) + " (serbest)" : (k + 1).ToString(Inv),
                        kucuk, soluk, (float)x + w / 2 + (m.Tip != KirisManuelMesnetTip.Serbest ? 0 : k == 0 ? 26 : -26), altSinir + 4, orta);
                    x += w;
                    if (k == n) break;

                    var a = v.Acikliklar[k];
                    float lw = (float)(Ln(k) * olcek);
                    float y0b = Y(a.DkotCm), y1b = Y(a.DkotCm - H(k));
                    g.FillRectangle(beton, (float)x, y0b, lw, y1b - y0b);
                    g.DrawRectangle(cizgi, (float)x, y0b, lw, y1b - y0b);
                    if (a.DosemeCm > 0.5 && !double.IsNaN(a.DosemeCm))
                    {
                        float yd = Y(a.DkotCm - a.DosemeCm);
                        using (var kesik = new Pen(Color.FromArgb(150, 130, 98), 1f) { DashStyle = DashStyle.Dash })
                            g.DrawLine(kesik, (float)x, yd, (float)x + lw, yd);
                    }
                    // Sürekli üst/alt ve mesnet ilaveleri (şematik).
                    float pp = Math.Max(2f, (float)(v.PaspayiCm * olcekY) + 1f);
                    g.DrawLine(donati, (float)x - w / 2, y0b + pp, (float)x + lw + (float)(Eni(k + 1) * olcek) / 2, y0b + pp);
                    g.DrawLine(donati, (float)x - w / 2, y1b - pp, (float)x + lw + (float)(Eni(k + 1) * olcek) / 2, y1b - pp);
                    float ila = lw * 0.28f;
                    if (KirisManuelVeri.TryDonati(a.SolUst, out int su, out _) && su > 0) g.DrawLine(vurgu, (float)x - w / 2, y0b + pp + 3, (float)x + ila, y0b + pp + 3);
                    if (KirisManuelVeri.TryDonati(a.SagUst, out int gu, out _) && gu > 0) g.DrawLine(vurgu, (float)x + lw - ila, y0b + pp + 3, (float)x + lw + (float)(Eni(k + 1) * olcek) / 2, y0b + pp + 3);
                    if (KirisManuelVeri.TryDonati(a.SolAlt, out int sa, out _) && sa > 0) g.DrawLine(vurgu, (float)x - w / 2, y1b - pp - 3, (float)x + ila, y1b - pp - 3);
                    if (KirisManuelVeri.TryDonati(a.SagAlt, out int ga, out _) && ga > 0) g.DrawLine(vurgu, (float)x + lw - ila, y1b - pp - 3, (float)x + lw + (float)(Eni(k + 1) * olcek) / 2, y1b - pp - 3);

                    var kutu = new RectangleF((float)x, y0b - 18, lw, 16);
                    g.DrawString(string.Format(Inv, "{0} ({1:0}/{2:0})", a.Ad, a.BCm, a.HCm), kalin, yazi, kutu, orta);
                    g.DrawString(string.Format(Inv, "Ln={0:0}", Ln(k)), kucuk, soluk, new RectangleF((float)x, y1b + 4, lw, 14), orta);
                    x += lw;
                }
            }
        }

        // ---------------------------------------------------------------- çiz

        private void Ciz()
        {
            _aciklik.EndEdit(); _mesnet.EndEdit();
            var v = Topla();
            if (!v.Dogrula(out string hata))
            {
                MessageBox.Show(this, hata, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Pafta100 = _pafta100.Checked;
            try { PlanFormSettings.Load("kirisciz").Set("Veri", v.Yaz()).Set("Pafta100", Pafta100).Save(); } catch { }
            Veri = v;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
