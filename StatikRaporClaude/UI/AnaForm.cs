using System.Drawing;
using StatikRaporClaude.Gpr;
using StatikRaporClaude.Cizim;
using StatikRaporClaude.Proje;
using StatikRaporClaude.Rapor;

namespace StatikRaporClaude.UI
{
    public sealed class AnaForm : Form
    {
        private static readonly Color Lacivert = Color.FromArgb(24, 42, 74);
        private static readonly Color Kobalt = Color.FromArgb(38, 99, 184);
        private static readonly Color Fildisi = Color.FromArgb(247, 244, 238);
        private static readonly Color Turuncu = Color.FromArgb(226, 122, 36);

        private static readonly string AyarYolu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatikRaporClaude", "son-proje.txt");

        private readonly TextBox _st4 = new TextBox();
        private readonly Label _projeBilgi = new Label();
        private readonly ListView _dosyalar = new ListView();
        private readonly FlowLayoutPanel _katPanel = new FlowLayoutPanel();
        // Rapor içeriği = GPR'de ne varsa (seçim arayüzü kaldırıldı); GPR yoksa dosyalardan üretilebilen varsayılan kalemler
        private List<RaporKalemi> _kalemler = new List<RaporKalemi>();
        private readonly CheckedListBox _ekler = new CheckedListBox();
        private readonly OnizlemePaneli _onizleme;
        private readonly Label _sayfaEtiket = new Label();
        private readonly Label _durum = new Label();
        private readonly CheckBox _modern = new CheckBox();
        private readonly ComboBox _tema = new ComboBox();
        private static readonly string TemaYolu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatikRaporClaude", "resim-tema.txt");
        private readonly CheckBox _dosyadan = new CheckBox { Text = "Tablolar sonuç dosyalarından", Checked = true, AutoSize = true };
        /// <summary>Kapaktan sonra yapı teknik raporu ve yapı yorumu (A statik, B ekonomi, C temel ve zemin + alan oranı).</summary>
        private readonly CheckBox _teknik = new CheckBox { Text = "Kapaktan sonra yapı teknik raporu ve yapı yorumu", AutoSize = true, Margin = new Padding(2, 0, 2, 2) };
        private readonly CheckBox _geo = new CheckBox { Text = "Yalnız geoteknik bilgi raporu (zemin etüdü için)", AutoSize = true, Margin = new Padding(2, 0, 2, 2) };
        private Button _pdfDugme, _yanYanaDugme;

        private ProjeKlasoru _proje;
        private GprBelgesi _gpr;
        private int _sayfa;

        public AnaForm(string st4)
        {
            using (var simgeAkisi = typeof(AnaForm).Assembly.GetManifestResourceStream("StatikRaporClaude.UygulamaSimgesi"))
            using (var simge = new Icon(simgeAkisi))
                Icon = (Icon)simge.Clone();
            var surum = typeof(AnaForm).Assembly.GetName().Version;
            Text = $"Statik Rapor — Claude sürümü {surum.Major}.{surum.Minor}   ·   derleme {File.GetLastWriteTime(typeof(AnaForm).Assembly.Location):dd.MM.yyyy HH:mm}";
            Width = 1500; Height = 950;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Fildisi;
            Font = new Font("Segoe UI", 9.5f);
            _onizleme = new OnizlemePaneli { Dock = DockStyle.Fill };

            Controls.Add(GovdeKur());
            Controls.Add(UstKur());
            Controls.Add(AltKur());

            st4 ??= SonProje();
            if (st4 != null && File.Exists(st4)) Shown += (s, e) => ProjeAc(st4);
        }

        // ---------------- yerleşim ----------------

        private Control UstKur()
        {
            var ust = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Lacivert };
            var baslik = new Label
            {
                Text = "STATİK RAPOR", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f),
                AutoSize = true, Location = new Point(18, 10)
            };
            var alt = new Label
            {
                Text = "STA4CAD projesinden seçenekli hesap raporu  ·  Claude sürümü (deneme)",
                ForeColor = Color.FromArgb(190, 205, 230), AutoSize = true, Location = new Point(20, 40)
            };
            var etiket = new Label { Text = "ST4 dosyası", ForeColor = Color.White, AutoSize = true, Location = new Point(560, 16) };
            _st4.SetBounds(560, 38, 640, 26);
            _st4.ReadOnly = true;
            var gozat = Dugme("Gözat…", 1210, 36, 90, Kobalt);
            gozat.Click += (s, e) => Gozat();
            var yenile = Dugme("Yenile", 1306, 36, 80, Color.FromArgb(70, 84, 110));
            yenile.Click += (s, e) => { if (_proje != null) ProjeAc(_proje.St4Yolu); };
            ust.Controls.AddRange(new Control[] { baslik, alt, etiket, _st4, gozat, yenile });
            return ust;
        }

        private Control GovdeKur()
        {
            var bol = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = Color.FromArgb(220, 214, 202) };
            bol.Panel1.BackColor = Fildisi;
            bol.Panel2.BackColor = Color.FromArgb(232, 228, 220);
            Load += (s, e) => bol.SplitterDistance = 540;

            // Sol: proje kartı, dosya durumu, kat seçimi, ekler
            var sol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(12, 10, 8, 10) };
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // proje
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // dosya durumu başlık
            sol.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));     // dosyalar
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // rapor başı başlık
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // teknik rapor seçeneği
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // kat başlık
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // katlar
            sol.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // ekler başlık
            sol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // ekler

            _projeBilgi.AutoSize = true;
            _projeBilgi.MaximumSize = new Size(440, 0);
            _projeBilgi.Text = "Proje seçilmedi.";
            _projeBilgi.Padding = new Padding(0, 2, 0, 6);

            _dosyalar.View = View.Details;
            _dosyalar.Dock = DockStyle.Fill;
            _dosyalar.FullRowSelect = true;
            _dosyalar.Scrollable = false;
            _dosyalar.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _dosyalar.Columns.Add("Dosya", 130);
            _dosyalar.Columns.Add("Tarih", 110);
            _dosyalar.Columns.Add("Durum", 400);
            _dosyalar.ShowItemToolTips = true;

            _katPanel.Dock = DockStyle.Fill;
            _katPanel.AutoSize = true;
            _katPanel.Margin = new Padding(0, 0, 0, 2);

            _ekler.Dock = DockStyle.Fill;
            _ekler.CheckOnClick = true;
            _ekler.BorderStyle = BorderStyle.FixedSingle;

            sol.Controls.Add(_projeBilgi, 0, 0);
            sol.Controls.Add(Baslik("Dosya durumu"), 0, 1);
            sol.Controls.Add(_dosyalar, 0, 2);
            sol.Controls.Add(Baslik("Rapor başı"), 0, 3);
            var basSecenek = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            basSecenek.Controls.Add(_teknik);
            basSecenek.Controls.Add(_geo);
            sol.Controls.Add(basSecenek, 0, 4);
            sol.Controls.Add(Baslik("Kat yazdırma seçimi"), 0, 5);
            sol.Controls.Add(_katPanel, 0, 6);
            sol.Controls.Add(Baslik("Rapora eklenecek PDF'ler (sona)"), 0, 7);
            sol.Controls.Add(_ekler, 0, 8);
            bol.Panel1.Controls.Add(sol);

            // Sağ: önizleme
            var gezinti = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(232, 228, 220) };
            var geri = Dugme("◀", 12, 8, 44, Kobalt);
            geri.Click += (s, e) => SayfayaGit(_sayfa - 1);
            var ileri = Dugme("▶", 60, 8, 44, Kobalt);
            ileri.Click += (s, e) => SayfayaGit(_sayfa + 1);
            _sayfaEtiket.SetBounds(116, 13, 900, 24);
            _sayfaEtiket.Font = new Font("Segoe UI Semibold", 10f);
            gezinti.Controls.AddRange(new Control[] { geri, ileri, _sayfaEtiket });
            bol.Panel2.Controls.Add(_onizleme);
            bol.Panel2.Controls.Add(gezinti);
            _onizleme.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown) SayfayaGit(_sayfa + 1);
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp) SayfayaGit(_sayfa - 1);
            };
            return bol;
        }

        private Control AltKur()
        {
            var alt = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.White };
            _durum.SetBounds(18, 18, 360, 24);
            // STA4 resimlerinin çizim teması (kalıcı)
            _tema.DropDownStyle = ComboBoxStyle.DropDownList;
            _tema.Width = 170;
            foreach (var t in Modern.StaResimTemasi.Hepsi) _tema.Items.Add("Resim: " + t.Ad);
            Modern.StaResimleri.Tema = Modern.StaResimTemasi.Bul(TemaOku());
            _tema.SelectedIndex = Array.IndexOf(Modern.StaResimTemasi.Hepsi, Modern.StaResimleri.Tema);
            _tema.SelectedIndexChanged += (s, e) => { Modern.StaResimleri.Tema = Modern.StaResimTemasi.Hepsi[_tema.SelectedIndex]; TemaYaz(Modern.StaResimleri.Tema.Kod); };
            _teknik.Checked = TeknikOku();
            _teknik.CheckedChanged += (s, e) => TeknikYaz(_teknik.Checked);
            // jeoteknik rapor ayrı bir rapor türü: teknik rapor seçeneği bu durumda kullanılmaz
            _geo.CheckedChanged += (s, e) => { _teknik.Enabled = !_geo.Checked; _pdfDugme.Text = _geo.Checked ? "Geoteknik PDF" : "PDF oluştur"; };
            new ToolTip().SetToolTip(_tema, "STA4 kütüphane resimlerinin rapordaki çizim teması");
            var kapak = Dugme("Kapak bilgileri…", 0, 12, 150, Color.FromArgb(70, 84, 110));
            kapak.Click += (s, e) => KapakDuzenle();
            _yanYanaDugme = Dugme("GPR ile yan yana", 0, 10, 168, Kobalt);
            _yanYanaDugme.Height = 38;
            _yanYanaDugme.Font = new Font("Segoe UI Semibold", 10f);
            _yanYanaDugme.Click += async (s, e) => await YanYanaOlustur();
            _pdfDugme = Dugme("PDF oluştur", 0, 10, 170, Turuncu);
            _pdfDugme.Height = 38;
            _pdfDugme.Font = new Font("Segoe UI Semibold", 11f);
            _pdfDugme.Click += async (s, e) => await PdfOlustur();
            _modern.Text = "BEYKENT tasarımı";
            _modern.Checked = true;
            _modern.AutoSize = true;
            var ipucu = new ToolTip();
            ipucu.SetToolTip(_modern, "Deprem raporu, kiriş ve kolon betonarme sonuçları BEYKENT tablo tasarımıyla; diğer bölümler STA4 görünümünde.");
            ipucu.SetToolTip(kapak, "Arsa, bina, müellif, yapı sahibi ve yapı denetim bilgileri (proje bazında saklanır).");
            ipucu.SetToolTip(_yanYanaDugme, "Seçili bölümler A3 yatay: sol STA4 GPR sayfası, sağ bu programın aynı bölümü.");
            alt.Resize += (s, e) =>
            {
                _pdfDugme.Left = alt.Width - 188;
                _yanYanaDugme.Left = alt.Width - 366;
                kapak.Left = alt.Width - 526;
                _modern.Left = alt.Width - 700; _modern.Top = 19;
                _tema.Left = alt.Width - 1115; _tema.Top = 17;

                _dosyadan.Left = alt.Width - 930; _dosyadan.Top = 19;
            };
            ipucu.SetToolTip(_dosyadan, "Kiriş/kolon/döşeme statik ve betonarme, kiriş ve kolon yük tabloları GPR yerine BEA, KMX, COL, SMX, FLO, DMX, LOA dosyalarından üretilir; GPR varsa hücre hücre karşılaştırılır.");
            alt.Controls.AddRange(new Control[] { _durum, _tema, _dosyadan, _modern, kapak, _yanYanaDugme, _pdfDugme });
            return alt;
        }

        private static Label Baslik(string metin) => new Label
        {
            Text = metin.ToUpper(new System.Globalization.CultureInfo("tr-TR")), AutoSize = true, ForeColor = Kobalt,
            Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 8, 0, 4)
        };

        private static Button Dugme(string metin, int x, int y, int w, Color renk)
        {
            var b = new Button
            {
                Text = metin, Left = x, Top = y, Width = w, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = renk, ForeColor = Color.White, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // ---------------- davranış ----------------

        private void Gozat()
        {
            using var d = new OpenFileDialog { Filter = "STA4CAD proje (*.st4)|*.st4", Title = "ST4 dosyasını seçin" };
            if (_proje != null) d.InitialDirectory = _proje.Klasor;
            if (d.ShowDialog(this) == DialogResult.OK) ProjeAc(d.FileName);
        }

        private void ProjeAc(string st4)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                _proje = ProjeKlasoru.Ac(st4);
                _st4.Text = st4;
                SonProjeKaydet(st4);
                _gpr = _proje.GprYolu != null ? GprBelgesi.Oku(_proje.GprYolu) : null;

                string katlar = string.Join("  ·  ", _proje.Katlar.Select(k => $"{k.Ad} ({k.Kot:0.00} m)"));
                _projeBilgi.Text =
                    $"{_proje.ProjeAdi}   ({_proje.TabanAd}.ST4)\n" +
                    $"Firma: {_proje.Firma}   ·   {_proje.Surum}\n" +
                    $"Katlar: {katlar}" +
                    (_gpr != null ? $"\nGPR: {_gpr.Sayfalar.Count} sayfa, rapor tarihi {_gpr.RaporTarihi}" : "");
                _projeBilgi.Font = new Font("Segoe UI", 9.5f);

                _dosyalar.Items.Clear();
                foreach (var d in _proje.Dosyalar)
                {
                    var it = new ListViewItem(new[] { d.Ad, d.Tarih?.ToString("dd.MM.yyyy HH:mm") ?? "—", d.Uyari ?? "Tamam · " + d.Aciklama });
                    it.ForeColor = d.Uyari != null ? Color.FromArgb(190, 80, 0) : Color.FromArgb(30, 110, 60);
                    it.ToolTipText = d.Uyari ?? d.Aciklama;
                    _dosyalar.Items.Add(it);
                }

                SekmeleriDoldur();
                _ekler.Items.Clear();
                foreach (var p in _proje.EkPdfler) _ekler.Items.Add(Path.GetFileName(Path.GetDirectoryName(p)) + "\\" + Path.GetFileName(p));

                _sayfa = 0;
                SayfayaGit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Proje açılamadı:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void SekmeleriDoldur()
        {
            // GPR varsa içeriği aynen (tekrar baskılar Dagit'te ayıklanır); GPR yoksa dosyalardan üretilebilen varsayılan kalemler
            if (_gpr != null) _kalemler = RaporKatalogu.GprIcerigi(_gpr, _proje);
            else
            {
                _kalemler = RaporKatalogu.Olustur();
                RaporKatalogu.Zorla(_kalemler, _proje);
                _kalemler = _kalemler.Where(k => k.Bolumler.Count > 0 && k.Varsayilan).ToList();
            }

            _katPanel.Controls.Clear();
            if (_proje != null)
                foreach (var kat in _proje.Katlar)
                {
                    var kod = kat.Kod.TrimEnd('-');
                    // STA4 varsayılanı: su basman kapalı
                    var kk = new CheckBox { Text = Baslikla(kat.Ad), Tag = kod, AutoSize = true, Checked = kod != "S", Margin = new Padding(2, 2, 14, 2) };
                    kk.CheckedChanged += (s, e) => DurumYaz();
                    _katPanel.Controls.Add(kk);
                }
            DurumYaz();
        }

        private static string Baslikla(string s) => Modern.ModernBolumler.Tr(s);

        private HashSet<int> SeciliBolumler() => _kalemler.SelectMany(k => k.Bolumler).Select(b => b.No).ToHashSet();

        private ISet<string> SeciliKatlar()
        {
            var kutular = _katPanel.Controls.OfType<CheckBox>().ToList();
            if (kutular.Count == 0 || kutular.All(k => k.Checked)) return null;
            return kutular.Where(k => k.Checked).Select(k => (string)k.Tag).ToHashSet();
        }

        private static bool Secili(GprSayfa s, HashSet<int> secili) => s.Bolumler.Any(b => secili.Contains(b.No));

        private void DurumYaz()
        {
            if (_proje == null) { _durum.Text = "ST4 dosyası seçin."; return; }
            var secili = SeciliBolumler();
            int sanal = _kalemler.Where(k => k.Dosyadan).Sum(k => k.Bolumler.Count);
            if (_gpr == null) { _durum.Text = $"GPR yok — sonuç dosyalarından {sanal} bölüm üretilecek"; return; }
            int n = _gpr.Sayfalar.Count(s => Secili(s, secili));
            _durum.Text = $"Rapora girecek: {n} / {_gpr.Sayfalar.Count} GPR sayfası   ·   {secili.Count - sanal} / {_gpr.Bolumler.Count} bölüm" +
                          (sanal > 0 ? $"   ·   +{sanal} bölüm dosyalardan" : "");
        }

        private void SayfayaGit(int sira)
        {
            if (_gpr == null || _gpr.Sayfalar.Count == 0) { _onizleme.Sayfa = null; _sayfaEtiket.Text = ""; return; }
            _sayfa = Math.Max(0, Math.Min(_gpr.Sayfalar.Count - 1, sira));
            _onizleme.Sayfa = _gpr.Sayfalar[_sayfa];
            SayfaEtiketiYaz();
            _onizleme.Focus();
        }

        private void SayfaEtiketiYaz()
        {
            if (_gpr == null || _onizleme.Sayfa == null) return;
            var s = _onizleme.Sayfa;
            bool secili = Secili(s, SeciliBolumler());
            string bolum = string.Join("  +  ", s.Bolumler.Select(b => Baslikla(b.Baslik) + (b.Grafik ? " (grafik)" : "")));
            _sayfaEtiket.Text = $"GPR sayfa {s.No}  ({_sayfa + 1}/{_gpr.Sayfalar.Count})   ·   {bolum}" + (secili ? "" : "   ·   rapora girmiyor");
            _sayfaEtiket.ForeColor = secili ? Lacivert : Color.FromArgb(150, 150, 150);
            _onizleme.Soluk = !secili;
            _onizleme.Invalidate();
        }

        private static string SonProje()
        {
            try { if (File.Exists(AyarYolu)) return File.ReadAllText(AyarYolu).Trim(); } catch { }
            const string ornek = @"D:\projeler\projeler2026\ahmet_kustepe\nevzat_ulker\STATIK\N_ULK_02.ST4";
            return File.Exists(ornek) ? ornek : null;
        }

        private static readonly string TeknikYolu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatikRaporClaude", "teknik-rapor.txt");
        private static bool TeknikOku() { try { return !File.Exists(TeknikYolu) || File.ReadAllText(TeknikYolu).Trim() != "0"; } catch { return true; } }
        private static void TeknikYaz(bool acik) { if (AyarKaydetme) return; try { Directory.CreateDirectory(Path.GetDirectoryName(TeknikYolu)); File.WriteAllText(TeknikYolu, acik ? "1" : "0"); } catch { } }

        private static string TemaOku() { try { return File.Exists(TemaYolu) ? File.ReadAllText(TemaYolu).Trim() : null; } catch { return null; } }
        private static void TemaYaz(string kod) { if (AyarKaydetme) return; try { Directory.CreateDirectory(Path.GetDirectoryName(TemaYolu)); File.WriteAllText(TemaYolu, kod); } catch { } }

        /// <summary>Doğrulama kipinde (--ekran) kullanıcının "son proje" ayarı değiştirilmez.</summary>
        public static bool AyarKaydetme;

        private static void SonProjeKaydet(string st4)
        {
            if (AyarKaydetme) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(AyarYolu)); File.WriteAllText(AyarYolu, st4); } catch { }
        }


        // ---------------- kapak ve PDF ----------------

        private void KapakDuzenle()
        {
            if (_proje == null) { MessageBox.Show(this, "Önce ST4 dosyası seçin.", Text); return; }
            var k = KapakBilgisi.Yukle(_proje.St4Yolu);
            if (string.IsNullOrWhiteSpace(k.KatAdedi) && _proje.Katlar.Count > 0)
                k.KatAdedi = string.Join("+", _proje.Katlar.Where(x => x.Kod != "S-").Select(x => x.Kod.TrimEnd('-')));
            using var f = new KapakFormu(k);
            if (f.ShowDialog(this) == DialogResult.OK) k.Kaydet(_proje.St4Yolu);
        }

        /// <summary>Sonuç dosyalarını açar; okunamazsa (ör. STA4 dosyayı kilitlemiş) uyarır, çökmez.</summary>
        private bool VeriAc(bool gerekli, out Veri.StaVeri veri)
        {
            veri = null;
            if (!gerekli) return true;
            try { veri = Veri.StaVeri.Ac(_proje.St4Yolu); return true; }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Sonuç dosyaları okunamadı:\n" + ex.Message +
                    "\n\nSTA4 analiz yapıyorsa bitmesini bekleyip tekrar deneyin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private async Task YanYanaOlustur()
        {
            if (_proje == null) { MessageBox.Show(this, "Önce ST4 dosyası seçin.", Text); return; }
            if (_gpr == null) { MessageBox.Show(this, "Bu projenin GPR dosyası yok. Yan yana görünüm GPR sayfası ister.", Text); return; }
            var secili = SeciliBolumler();
            if (secili.Count == 0) { MessageBox.Show(this, "Karşılaştırılacak bölüm seçilmedi.", Text); return; }

            string kok = Directory.GetParent(_proje.Klasor)?.FullName ?? _proje.Klasor;
            string varsayilanKlasor = Directory.Exists(Path.Combine(kok, "CZM_RPR")) ? Path.Combine(kok, "CZM_RPR") : _proje.Klasor;
            using var d = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf", InitialDirectory = varsayilanKlasor,
                FileName = _proje.TabanAd + "_KARSILASTIRMA.pdf", Title = "GPR ile yan yana kaydet"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (!VeriAc(_dosyadan.Checked, out var veri)) return;

            var secenek = new RaporSecenekleri
            {
                Proje = _proje, Gpr = _gpr, SeciliBolumler = secili, Kapak = KapakBilgisi.Yukle(_proje.St4Yolu),
                ModernTablolar = _modern.Checked, TeknikRapor = _teknik.Checked, Katlar = SeciliKatlar(), CiktiYolu = d.FileName,
                Veri = veri,
                Bolumler = RaporKatalogu.Sirala(_kalemler.SelectMany(k => k.Bolumler))
            };
            _pdfDugme.Enabled = _yanYanaDugme.Enabled = false;
            Cursor = Cursors.WaitCursor;
            var ilerleme = new Progress<string>(m => _durum.Text = m);
            try
            {
                int n = await Task.Run(() => YanYanaRapor.Olustur(secenek, ilerleme));
                _durum.Text = $"Karşılaştırma hazır: {n} sayfa  ·  {Path.GetFileName(d.FileName)}";
                if (MessageBox.Show(this,
                        $"Karşılaştırma hazır: {n} sayfa (A3 yatay)\n\nSol: STA4 GPR sayfası\nSağ: bu programın aynı bölümü\n\nSayfa sayıları bölüm içinde eşit olmayabilir; kısa kalan tarafta not yazar.\n\n{d.FileName}\n\nŞimdi açılsın mı?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.FileName) { UseShellExecute = true });
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "PDF yazılamadı (dosya başka programda açık olabilir):\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DurumYaz();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Karşılaştırma oluşturulamadı:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DurumYaz();
            }
            finally
            {
                _pdfDugme.Enabled = _yanYanaDugme.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private async Task PdfOlustur()
        {
            if (_proje == null) { MessageBox.Show(this, "Önce ST4 dosyası seçin.", Text); return; }
            bool geo = _geo.Checked;
            // jeoteknik raporun bölümleri ayrı: temel yükleri, zemin gerilmeleri, temel aplikasyonu (GPR'de yoksa dosyalardan)
            var bolumler = geo ? RaporKatalogu.Geoteknik(_gpr, _proje) : RaporKatalogu.Sirala(_kalemler.SelectMany(k => k.Bolumler));
            var secili = geo ? bolumler.Select(b => b.No).ToHashSet() : SeciliBolumler();
            if (secili.Count == 0 && !geo) { MessageBox.Show(this, "Rapora girecek bölüm seçilmedi.", Text); return; }

            string kok = Directory.GetParent(_proje.Klasor)?.FullName ?? _proje.Klasor;
            string varsayilanKlasor = Directory.Exists(Path.Combine(kok, "CZM_RPR")) ? Path.Combine(kok, "CZM_RPR") : _proje.Klasor;
            using var d = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf", InitialDirectory = varsayilanKlasor,
                FileName = _proje.TabanAd + (geo ? "_GEOTEKNIK_RAPOR.pdf" : "_STATIK_RAPOR.pdf"), Title = "Raporu kaydet"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;

            var ekler = _ekler.CheckedIndices.Cast<int>().Select(i => _proje.EkPdfler[i])
                .Where(y => !string.Equals(Path.GetFullPath(y), Path.GetFullPath(d.FileName), StringComparison.OrdinalIgnoreCase)).ToList();
            // GPR'de olmayan (sanal) bölüm seçiliyse sonuç dosyaları zorunlu
            if (!VeriAc(geo || _dosyadan.Checked || _gpr == null || _kalemler.Any(k => k.Dosyadan), out var veri)) return;
            var secenek = new RaporSecenekleri
            {
                Proje = _proje, Gpr = _gpr, SeciliBolumler = secili, Kapak = KapakBilgisi.Yukle(_proje.St4Yolu),
                EkPdfler = ekler, ModernTablolar = _modern.Checked, TeknikRapor = _teknik.Checked, Geoteknik = geo, Katlar = SeciliKatlar(), CiktiYolu = d.FileName,
                Veri = veri,
                Bolumler = bolumler
            };
            _pdfDugme.Enabled = _yanYanaDugme.Enabled = false;
            Cursor = Cursors.WaitCursor;
            var ilerleme = new Progress<string>(m => _durum.Text = m);
            try
            {
                var sonuc = await Task.Run(() => RaporDerleyici.Olustur(secenek, ilerleme));
                _durum.Text = $"Hazır: {sonuc.ToplamSayfa} sayfa  ·  {Path.GetFileName(sonuc.Yol)}";
                if (MessageBox.Show(this,
                        $"Rapor oluşturuldu: {sonuc.ToplamSayfa} sayfa\n\nKapak + içindekiler: {sonuc.KapakVeIcindekiler}\nModern tablo: {sonuc.ModernSayfa}\nSTA4 sayfası: {sonuc.Sta4Sayfa}\nEk: {sonuc.EkSayfa}\n\n{sonuc.Yol}\n\nŞimdi açılsın mı?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sonuc.Yol) { UseShellExecute = true });
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "PDF yazılamadı (dosya başka programda açık olabilir):\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DurumYaz();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "PDF oluşturulamadı:\n" + ex, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DurumYaz();
            }
            finally
            {
                _pdfDugme.Enabled = _yanYanaDugme.Enabled = true;
                Cursor = Cursors.Default;
            }
        }
    }

    internal sealed class OnizlemePaneli : Panel
    {
        private GprSayfa _sayfa;
        public bool Soluk;

        public OnizlemePaneli()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            ResizeRedraw = true;
            MouseDown += (s, e) => Focus();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public GprSayfa Sayfa { get => _sayfa; set { _sayfa = value; Invalidate(); } }

        protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown || base.IsInputKey(k);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_sayfa == null)
            {
                TextRenderer.DrawText(e.Graphics, "Önizleme için ST4 seçin", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            var alan = new RectangleF(20, 14, ClientSize.Width - 40, ClientSize.Height - 28);
            float olcek = Math.Min(alan.Width / GprCizici.SayfaGenislik, alan.Height / GprCizici.SayfaYukseklik);
            float w = GprCizici.SayfaGenislik * olcek, h = GprCizici.SayfaYukseklik * olcek;
            float x = alan.X + (alan.Width - w) / 2, y = alan.Y + (alan.Height - h) / 2;
            using (var golge = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) e.Graphics.FillRectangle(golge, x + 5, y + 5, w, h);
            GprCizici.Ciz(new GdiYuzey(e.Graphics), _sayfa, new RectangleF(x, y, w, h));
            if (Soluk)
                using (var sis = new SolidBrush(Color.FromArgb(150, 240, 238, 232))) e.Graphics.FillRectangle(sis, x, y, w, h);
        }
    }
}
