using System.Diagnostics;

namespace RaporBirlestir
{
    /// <summary>
    /// Statik / İskele / Kalıp iskele bölümleri tek sayfada alt alta. Tek "BİRLEŞTİR" ile seçilen klasöre
    /// statik raporun adından türetilen dosyalar yazılır: MS_B_RPR → MS_B_RPR, MS_B_ISK_RPR, MS_B_KAL_RPR.
    /// </summary>
    internal sealed class AnaForm : Form
    {
        public static readonly Color Kobalt = Color.FromArgb(32, 72, 168);
        public static readonly Color Turuncu = Color.FromArgb(232, 124, 32);
        public static readonly Color Gri = Color.FromArgb(90, 98, 112);
        public static readonly Color Fildisi = Color.FromArgb(246, 241, 228);

        private readonly List<BolumPaneli> _bolumler = new();
        private readonly TextBox _klasor = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 4, 0) };
        private readonly CheckBox _bitinceAc = new() { Text = "Bitince klasörü aç", AutoSize = true, Checked = true, Margin = new Padding(12, 8, 3, 3) };
        private readonly Label _adlar = new() { AutoSize = true, ForeColor = Color.FromArgb(70, 76, 88), Margin = new Padding(0, 4, 0, 0) };
        private readonly Label _durum = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 0) };

        public AnaForm(string[] args)
        {
            Text = "Rapor Birleştir — Statik / İskele / Kalıp İskele";
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 900);
            MinimumSize = new Size(900, 700);
            BackColor = Fildisi;
            try
            {
                using var s = typeof(AnaForm).Assembly.GetManifestResourceStream("RaporBirlestir.Simge");
                if (s != null) Icon = new Icon(s);
            }
            catch { }

            // --- üst bant
            var ust = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Kobalt };
            ust.Controls.Add(new Label
            {
                Text = "RAPOR BİRLEŞTİR",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15f),
                AutoSize = true,
                Location = new Point(14, 6)
            });
            ust.Controls.Add(new Label
            {
                Text = "Her bölüme kapak, rapor ve ek PDF'lerini ekleyin (sürükle-bırak da olur). Sıra yukarıdan aşağı. " +
                       "\"Alınacak sayfalar\" boşsa tümü; örn. 2-  (ilk sayfayı at), 1,3-5, -4, son.",
                ForeColor = Color.FromArgb(220, 228, 250),
                AutoSize = true,
                Location = new Point(16, 36)
            });
            var serit = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Turuncu };

            // --- üç bölüm alt alta
            var govde = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 8, 10, 0) };
            govde.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (ad, ek, renk) in new[]
            {
                ("STATİK HESAP RAPORU", "", Kobalt),
                ("İSKELE HESAP RAPORU", "_ISK_RPR", Turuncu),
                ("KALIP İSKELESİ RAPORU", "_KAL_RPR", Gri),
            })
            {
                var p = new BolumPaneli(ad, ek, renk, this, paspayKontrolu: _bolumler.Count == 0);
                _bolumler.Add(p);
                govde.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
                govde.Controls.Add(p, 0, _bolumler.Count - 1);
            }

            // --- alt: klasör + birleştir
            var alt = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 104,
                ColumnCount = 5,
                RowCount = 3,
                Padding = new Padding(12, 8, 12, 6),
                BackColor = Color.FromArgb(236, 229, 212)
            };
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            alt.Controls.Add(new Label { Text = "Kayıt klasörü:", AutoSize = true, Margin = new Padding(0, 8, 6, 0), Font = new Font("Segoe UI Semibold", 9.5f) }, 0, 0);
            alt.Controls.Add(_klasor, 1, 0);
            var gozat = new Button { Text = "…", Width = 34, Height = 27, Margin = new Padding(0, 3, 0, 0) };
            gozat.Click += (_, _) => KlasorSec();
            alt.Controls.Add(gozat, 2, 0);
            alt.Controls.Add(_bitinceAc, 3, 0);
            var birlestir = new Button
            {
                Text = "BİRLEŞTİR",
                Width = 220,
                Height = 44,
                BackColor = Turuncu,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 12f),
                Margin = new Padding(14, 0, 0, 0)
            };
            birlestir.FlatAppearance.BorderSize = 0;
            birlestir.Click += (_, _) => Birlestir();
            alt.Controls.Add(birlestir, 4, 0);
            alt.SetRowSpan(birlestir, 2);
            alt.Controls.Add(_adlar, 1, 1);
            alt.SetColumnSpan(_adlar, 3);
            alt.Controls.Add(_durum, 1, 2);
            alt.SetColumnSpan(_durum, 4);
            _klasor.TextChanged += (_, _) => { if (!_klasorAyarlaniyor) _klasorElle = _klasor.Text.Trim().Length > 0; AdlariGoster(); };

            Controls.Add(govde);
            Controls.Add(alt);
            Controls.Add(serit);
            Controls.Add(ust);

            AdlariGoster();

            // İskele bölümü: son bırakılan liste her açılışta yüklenir (kullanıcı değiştirene kadar).
            _bolumler[1].KaliciYap("iskele");

            // Exe'ye/kısayola bırakılan dosyalar Statik bölümüne eklenir ("isk=" / "kal=" önekiyle diğer bölümlere).
            var gelen = new List<(int Bolum, string Yol)>();
            foreach (var a in args)
            {
                int b = a.StartsWith("isk=", StringComparison.OrdinalIgnoreCase) ? 1 : a.StartsWith("kal=", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                var yol = b == 0 ? a : a[4..];
                if (File.Exists(yol) && yol.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) gelen.Add((b, yol));
            }
            if (gelen.Count > 0)
                Shown += (_, _) => { foreach (var g in gelen.GroupBy(x => x.Bolum)) _bolumler[g.Key].DosyaEkle(g.Select(x => x.Yol), null); };
        }

        /// <summary>Doğrulama (--ekran) kipinde kalıcı liste/klasör yazılmaz.</summary>
        public static bool KayitKapali;

        public void Durum(string metin) => _durum.Text = metin;

        // ---------------------------------------------------------------- adlandırma

        /// <summary>
        /// Statik rapor adı: kök + "_RPR" + sonek. "MS_B_RPR" → ("MS_B", ""), "MS_B_RPR_20261121" → ("MS_B", "_20261121").
        /// Adda "_RPR" yoksa (ad, "") ve diğer bölümler ad + "_ISK_RPR" olur.
        /// Adlar yalnız statik rapordan türetilir (iskele şablonu her açılışta yüklü olduğundan ondan ad türetilmez).
        /// </summary>
        private bool AdParcalari(out string anaAd, out string kok, out string sonek)
        {
            anaAd = kok = sonek = null;
            var statik = _bolumler[0].Parcalar.FirstOrDefault(p => p.Tur == "Rapor");
            if (statik == null) return false;
            anaAd = statik.Ad;
            Bol(statik.Ad, out kok, out sonek);
            return true;
        }

        private static void Bol(string ad, out string kok, out string sonek)
        {
            int i = ad.LastIndexOf("_RPR", StringComparison.OrdinalIgnoreCase);
            if (i < 0) { kok = ad; sonek = ""; return; }
            kok = ad[..i];
            sonek = ad[(i + 4)..];
        }

        /// <summary>Bölümün çıktı dosya adı (uzantılı). Statik: raporun kendi adı; diğerleri: kök + _ISK_RPR / _KAL_RPR + sonek.</summary>
        private string CiktiAdi(BolumPaneli b)
        {
            if (!AdParcalari(out var anaAd, out var kok, out var sonek)) return null;
            return (b.Ek.Length == 0 ? anaAd : kok + b.Ek + sonek) + ".pdf";
        }

        private bool _klasorElle;        // kullanıcı klasörü kendisi yazdı/seçti: artık otomatik değiştirme
        private bool _klasorAyarlaniyor;

        public void ListeDegisti()
        {
            // Kayıt klasörü yalnız STATİK raporun klasöründen önerilir (iskele şablonu kalıcı yüklendiği için
            // onun klasörü kullanılmaz). Kullanıcı klasörü elle değiştirdiyse dokunulmaz.
            if (!_klasorElle && _bolumler.Count > 0)
            {
                var statik = _bolumler[0].Parcalar.FirstOrDefault(p => p.Tur == "Rapor");
                if (statik != null)
                {
                    _klasorAyarlaniyor = true;
                    _klasor.Text = Path.GetDirectoryName(statik.Yol);
                    _klasorAyarlaniyor = false;
                }
            }
            AdlariGoster();
        }

        private void AdlariGoster()
        {
            var parcalar = new List<string>();
            foreach (var b in _bolumler)
            {
                var ad = b.Parcalar.Count > 0 ? CiktiAdi(b) : null;
                b.CiktiGoster(ad);
                if (ad != null) parcalar.Add(ad);
            }
            _adlar.Text = parcalar.Count == 0
                ? "Kaydedilecek dosyalar: — (statik raporu ekleyince adlar ondan türetilir)"
                : "Kaydedilecek dosyalar:  " + string.Join("   ·   ", parcalar);
        }

        // ---------------------------------------------------------------- yardımcılar

        /// <summary>Bölüme özel kalıcı klasör (ör. iskele raporlarının durduğu yer). Kullanıcı başka klasör seçene kadar korunur.</summary>
        public static string BolumKlasoru(string anahtar)
        {
            try
            {
                var f = AyarDosyasi($"klasor-{anahtar}.txt");
                if (File.Exists(f)) { var k = File.ReadAllText(f).Trim(); if (Directory.Exists(k)) return k; }
            }
            catch { }
            return null;
        }

        public static void BolumKlasoruKaydet(string anahtar, string klasor)
        {
            try { if (!string.IsNullOrEmpty(klasor)) File.WriteAllText(AyarDosyasi($"klasor-{anahtar}.txt"), klasor); } catch { }
        }

        private static string AyarDosyasi(string ad)
        {
            var k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaporBirlestir");
            Directory.CreateDirectory(k);
            return Path.Combine(k, ad);
        }

        public static string SonKlasor
        {
            get
            {
                try
                {
                    var f = AyarDosyasi("son-klasor.txt");
                    if (File.Exists(f)) { var k = File.ReadAllText(f).Trim(); if (Directory.Exists(k)) return k; }
                }
                catch { }
                return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            set
            {
                try { if (!string.IsNullOrEmpty(value)) File.WriteAllText(AyarDosyasi("son-klasor.txt"), value); } catch { }
            }
        }

        private void KlasorSec()
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Birleşik raporların kaydedileceği klasör",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(_klasor.Text) ? _klasor.Text : SonKlasor
            };
            if (d.ShowDialog() == DialogResult.OK) _klasor.Text = d.SelectedPath;
        }

        public static void Ac(string yol)
        {
            try { Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Açılamadı:\n" + ex.Message, "Rapor Birleştir", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // ---------------------------------------------------------------- birleştirme

        private void Birlestir()
        {
            foreach (var b in _bolumler) b.DuzenlemeyiBitir();
            var dolu = _bolumler.Where(b => b.Parcalar.Count > 0).ToList();
            if (dolu.Count == 0)
            {
                MessageBox.Show("Hiçbir bölümde PDF yok.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!AdParcalari(out _, out _, out _))
            {
                MessageBox.Show("Dosya adları statik raporun adından türetilir: STATİK HESAP RAPORU bölümüne \"Rapor\" türünde PDF ekleyin.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var hatali = dolu.FirstOrDefault(b => b.SayfaHatasiVar);
            if (hatali != null)
            {
                MessageBox.Show($"\"{hatali.Ad}\" bölümünde geçersiz sayfa aralığı var.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var klasor = _klasor.Text.Trim();
            if (klasor.Length == 0) { KlasorSec(); klasor = _klasor.Text.Trim(); if (klasor.Length == 0) return; }
            try { Directory.CreateDirectory(klasor); }
            catch (Exception ex) { MessageBox.Show("Klasör oluşturulamadı:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var isler = dolu.Select(b => (Bolum: b, Yol: Path.Combine(klasor, CiktiAdi(b)))).ToList();

            // Aynı isimli dosya varsa sor.
            var varolan = isler.Where(x => File.Exists(x.Yol)).ToList();
            if (varolan.Count > 0)
            {
                var kaynakIle = varolan.Where(x => _bolumler.SelectMany(b => b.Parcalar)
                    .Any(p => string.Equals(Path.GetFullPath(p.Yol), Path.GetFullPath(x.Yol), StringComparison.OrdinalIgnoreCase))).ToList();
                var mesaj = "Klasörde aynı isimli dosya var:\n\n" + string.Join("\n", varolan.Select(x => "   " + Path.GetFileName(x.Yol))) +
                            (kaynakIle.Count > 0 ? "\n\nDikkat: " + string.Join(", ", kaynakIle.Select(x => Path.GetFileName(x.Yol))) +
                                                   " aynı zamanda eklediğiniz kaynak dosya; üzerine yazılırsa orijinali birleşik raporla değişir." : "") +
                            "\n\nÜzerine yazılsın mı?";
                if (MessageBox.Show(mesaj, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            var sonuc = new List<string>();
            var notlar = new List<string>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var (b, yol) in isler)
                {
                    int n = Birlestirici.Birlestir(new[] { (b.Ad, b.Hazirla()) }, yol, notlar);
                    sonuc.Add($"{Path.GetFileName(yol)}   ({n} sayfa)");
                }
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                var yazilan = sonuc.Count > 0 ? "\n\nYazılan: " + string.Join(", ", sonuc) : "";
                MessageBox.Show("Birleştirme yapılamadı:\n\n" + ex.Message + yazilan, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Cursor = Cursors.Default;
            SonKlasor = klasor;
            Durum($"Tamamlandı {DateTime.Now:HH:mm} → {klasor}");

            if (notlar.Count > 0) sonuc.AddRange(new[] { "", "Kolon paspayı kontrolü:" }.Concat(notlar));
            MessageBox.Show(klasor + "\n\n" + string.Join("\n", sonuc), "Birleştirme tamamlandı", MessageBoxButtons.OK,
                notlar.Any(x => x.StartsWith("UYARI")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (_bitinceAc.Checked) Ac(klasor);
        }
    }

    /// <summary>Bir bölümün başlığı, düğme şeridi ve PDF listesi.</summary>
    internal sealed class BolumPaneli : UserControl
    {
        public readonly string Ad;
        public readonly string Ek;   // çıktı adı eki: "" (statik), "_ISK_RPR", "_KAL_RPR"
        private readonly AnaForm _ana;
        public readonly List<Parca> Parcalar = new();

        private readonly DataGridView _tablo = new();
        private readonly Label _ciktiAdi = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f), Margin = new Padding(16, 5, 0, 0) };
        private readonly Label _ozet = new() { AutoSize = true, ForeColor = Color.FromArgb(225, 230, 240), Margin = new Padding(16, 5, 0, 0) };
        private readonly CheckBox _paspay;   // yalnız Statik bölümünde
        private bool _yukleniyor;

        public BolumPaneli(string ad, string ek, Color renk, AnaForm ana, bool paspayKontrolu = false)
        {
            Ad = ad; Ek = ek; _ana = ana;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 0, 8);

            // --- başlık şeridi
            var baslik = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, BackColor = renk, WrapContents = false, Padding = new Padding(8, 0, 0, 0) };
            baslik.Controls.Add(new Label { Text = ad, AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 4, 0, 0) });
            baslik.Controls.Add(_ciktiAdi);
            baslik.Controls.Add(_ozet);

            // --- düğme şeridi
            var dugmeler = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            Button Dugme(string metin, Action is_, Color? r = null, int en = 0)
            {
                var b = new Button { Text = metin, AutoSize = en == 0, Width = en == 0 ? 60 : en, Height = 29, Margin = new Padding(0, 0, 6, 0) };
                if (r.HasValue) { b.BackColor = r.Value; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; }
                b.Click += (_, _) => is_();
                dugmeler.Controls.Add(b);
                return b;
            }
            Dugme("+ Kapak", () => DosyaSecEkle("Kapak"), AnaForm.Turuncu, 92);
            Dugme("+ Rapor", () => DosyaSecEkle("Rapor"), AnaForm.Kobalt, 92);
            Dugme("+ Ek PDF", () => DosyaSecEkle("Ek"), AnaForm.Gri, 92);
            dugmeler.Controls.Add(new Label { Width = 14 });
            Dugme("▲", () => Tasi(-1), null, 36);
            Dugme("▼", () => Tasi(+1), null, 36);
            Dugme("✕ Kaldır", Kaldir);
            Dugme("Temizle", () =>
            {
                if (Parcalar.Count > 0 && MessageBox.Show($"{Ad} listesi temizlensin mi?", Ad, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { Parcalar.Clear(); Yenile(); }
            });
            Dugme("PDF'i aç", () => { var i = Secili; if (i >= 0) AnaForm.Ac(Parcalar[i].Yol); });
            if (paspayKontrolu)
            {
                _paspay = new CheckBox
                {
                    Text = $"Kolon paspayını {PaspayDuzelt.Hedef} yap (STA4CAD)",
                    Checked = true,
                    AutoSize = true,
                    Margin = new Padding(18, 5, 0, 0),
                    ForeColor = AnaForm.Kobalt
                };
                new ToolTip().SetToolTip(_paspay,
                    "\"KOLON ve PERDELERIN betonarme opsiyonlari\" altındaki\n" +
                    "\"Etriye paspayı / Boyuna donatı paspayı\" değeri farklıysa\n" +
                    $"aynı yazı tipiyle {PaspayDuzelt.Hedef} yapılır. Kiriş ve temel satırlarına dokunulmaz.\n" +
                    "Yalnız \"Rapor\" türündeki dosyalara uygulanır; kaynak PDF değişmez.");
                dugmeler.Controls.Add(_paspay);
            }

            // --- tablo
            _tablo.Dock = DockStyle.Fill;
            _tablo.AllowUserToAddRows = false;
            _tablo.AllowUserToDeleteRows = false;
            _tablo.AllowUserToResizeRows = false;
            _tablo.RowHeadersVisible = false;
            _tablo.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _tablo.MultiSelect = false;
            _tablo.BackgroundColor = Color.White;
            _tablo.BorderStyle = BorderStyle.FixedSingle;
            _tablo.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _tablo.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _tablo.ColumnHeadersHeight = 27;
            _tablo.RowTemplate.Height = 26;
            _tablo.EnableHeadersVisualStyles = false;
            _tablo.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 231, 245);
            _tablo.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            _tablo.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 218, 245);
            _tablo.DefaultCellStyle.SelectionForeColor = Color.Black;
            _tablo.AllowDrop = true;

            _tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", ReadOnly = true, FillWeight = 4 });
            var tur = new DataGridViewComboBoxColumn { HeaderText = "Tür", FillWeight = 10, FlatStyle = FlatStyle.Flat };
            tur.Items.AddRange(Birlestirici.Turler);
            _tablo.Columns.Add(tur);
            _tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dosya", ReadOnly = true, FillWeight = 56 });
            _tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sayfa", ReadOnly = true, FillWeight = 7 });
            _tablo.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Alınacak sayfalar",
                FillWeight = 15,
                ToolTipText = "Boş = tümü.  2-  : ilk sayfa hariç.  1,3-5  : seçili sayfalar.  -4 : ilk dört.  son : son sayfa."
            });
            _tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Alınan", ReadOnly = true, FillWeight = 7 });
            _tablo.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _tablo.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _tablo.Columns[5].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            foreach (DataGridViewColumn c in _tablo.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            _tablo.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_tablo.IsCurrentCellDirty && _tablo.CurrentCell is DataGridViewComboBoxCell)
                    _tablo.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _tablo.CellValueChanged += TabloDegisti;
            _tablo.CellPainting += HucreCiz;
            _tablo.DataError += (_, e) => e.ThrowException = false;
            _tablo.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 2) AnaForm.Ac(Parcalar[e.RowIndex].Yol); };
            _tablo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && !_tablo.IsCurrentCellInEditMode) Kaldir(); };
            _tablo.DragEnter += SurukleGir;
            _tablo.DragDrop += SurukleBirak;
            AllowDrop = true;
            DragEnter += SurukleGir;
            DragDrop += SurukleBirak;

            Controls.Add(_tablo);
            Controls.Add(dugmeler);
            Controls.Add(baslik);
            Yenile();
        }

        private int Secili => _tablo.CurrentRow?.Index ?? -1;

        /// <summary>Kalıcı klasör anahtarı: statik / iskele / kalip.</summary>
        private string Anahtar => Ek switch { "_ISK_RPR" => "iskele", "_KAL_RPR" => "kalip", _ => "statik" };

        public void DuzenlemeyiBitir() => _tablo.EndEdit();

        public void CiktiGoster(string ad) => _ciktiAdi.Text = ad != null ? "→  " + ad : "";

        public bool SayfaHatasiVar => Parcalar.Any(p => Alinan(p) == "HATA");

        /// <summary>Birleştirme öncesi: paspay kontrolü yalnız Statik bölümünün "Rapor" türündeki dosyalarına uygulanır.</summary>
        public IList<Parca> Hazirla()
        {
            KapakDurumu();
            foreach (var p in Parcalar)
                p.PaspayKontrol = _paspay != null && _paspay.Checked && p.Tur == "Rapor";
            return Parcalar;
        }

        private void DosyaSecEkle(string tur)
        {
            // Her bölüm kendi son klasöründe açılır (iskele raporları hep aynı yerden seçilir); yoksa genel son klasör.
            using var d = new OpenFileDialog
            {
                Filter = "PDF dosyası (*.pdf)|*.pdf",
                Multiselect = true,
                InitialDirectory = AnaForm.BolumKlasoru(Anahtar) ?? AnaForm.SonKlasor,
                RestoreDirectory = true,
                Title = $"{Ad} — {tur} PDF seç"
            };
            if (d.ShowDialog() != DialogResult.OK) return;
            AnaForm.BolumKlasoruKaydet(Anahtar, Path.GetDirectoryName(d.FileNames[0]));
            AnaForm.SonKlasor = Path.GetDirectoryName(d.FileNames[0]);
            DosyaEkle(d.FileNames, tur);
        }

        private static string TurTahmin(string yol, bool raporVar)
        {
            var ad = Path.GetFileNameWithoutExtension(yol).ToLowerInvariant();
            if (ad.Contains("kapak") || ad.Contains("cover")) return "Kapak";
            if (ad.StartsWith("ek") || ad.Contains(" ek") || ad.Contains("_ek") || ad.Contains("-ek")) return "Ek";
            return raporVar ? "Ek" : "Rapor";
        }

        /// <summary>tur null ise dosya adından tahmin edilir. Kapaklar başa, raporlar ortaya, ekler sona yerleşir.</summary>
        public void DosyaEkle(IEnumerable<string> yollar, string tur)
        {
            var hatalar = new List<string>();
            foreach (var yol in yollar)
            {
                Parca p;
                try { p = Parca.Oku(yol, tur ?? TurTahmin(yol, Parcalar.Any(x => x.Tur == "Rapor"))); }
                catch (Exception ex) { hatalar.Add($"{Path.GetFileName(yol)}: {ex.Message}"); continue; }
                int sira = Array.IndexOf(Birlestirici.Turler, p.Tur);
                int konum = Parcalar.FindLastIndex(x => Array.IndexOf(Birlestirici.Turler, x.Tur) <= sira) + 1;
                Parcalar.Insert(konum, p);
            }
            Yenile();
            if (hatalar.Count > 0)
                MessageBox.Show("Şu dosyalar okunamadı (şifreli veya bozuk olabilir):\n\n" + string.Join("\n", hatalar),
                    Ad, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Tasi(int yon)
        {
            int i = Secili, j = i + yon;
            if (i < 0 || j < 0 || j >= Parcalar.Count) return;
            (Parcalar[i], Parcalar[j]) = (Parcalar[j], Parcalar[i]);
            Yenile(j);
        }

        private void Kaldir()
        {
            int i = Secili;
            if (i < 0) return;
            Parcalar.RemoveAt(i);
            Yenile(Math.Min(i, Parcalar.Count - 1));
        }

        /// <summary>Bölümde yeni kapak varsa rapor/eklerin baştaki eski kapakları atlanır.</summary>
        private void KapakDurumu()
        {
            bool yeniKapak = Parcalar.Any(x => x.Tur == "Kapak");
            foreach (var p in Parcalar) p.KapakAtla = yeniKapak && p.Tur != "Kapak";
        }

        private void Yenile(int secilecek = -1)
        {
            KapakDurumu();
            _yukleniyor = true;
            _tablo.Rows.Clear();
            for (int i = 0; i < Parcalar.Count; i++)
            {
                var p = Parcalar[i];
                int r = _tablo.Rows.Add(i + 1, p.Tur, Path.GetFileName(p.Yol), p.SayfaSayisi, p.Sayfalar, Alinan(p));
                var satir = _tablo.Rows[r];
                satir.Cells[2].ToolTipText = p.Yol;
                satir.Cells[1].Style.ForeColor = p.Tur switch
                {
                    "Kapak" => Color.FromArgb(200, 100, 20),
                    "Rapor" => AnaForm.Kobalt,
                    _ => Color.FromArgb(70, 76, 88)
                };
            }
            if (secilecek >= 0 && secilecek < _tablo.Rows.Count)
                _tablo.CurrentCell = _tablo.Rows[secilecek].Cells[2];
            _yukleniyor = false;
            Ozet();
            KaliciKaydet();
            _ana?.ListeDegisti();
        }

        // ---------------------------------------------------------------- kalıcı liste (iskele)

        private string _kaliciDosya;   // null: liste kalıcı değil

        /// <summary>Listeyi her değişiklikte kaydeder ve şimdi son kayıtlı hali yükler.</summary>
        public void KaliciYap(string anahtar)
        {
            _kaliciDosya = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaporBirlestir", $"liste-{anahtar}.txt");
            if (!File.Exists(_kaliciDosya)) return;
            var eksik = new List<string>();
            try
            {
                foreach (var satir in File.ReadAllLines(_kaliciDosya))
                {
                    var s = satir.Split('|');
                    if (s.Length < 3) continue;
                    if (!File.Exists(s[1])) { eksik.Add(s[1]); continue; }
                    Parca p;
                    try { p = Parca.Oku(s[1], s[0]); } catch { eksik.Add(s[1]); continue; }
                    p.Sayfalar = s[2];
                    Parcalar.Add(p);
                }
            }
            catch { }
            var dosya = _kaliciDosya;
            if (eksik.Count > 0) _kaliciDosya = null;   // eksik dosya varken kaydı ezme; kullanıcı görsün
            Yenile();
            _kaliciDosya = dosya;
            if (eksik.Count > 0)
                BeginInvoke(() => MessageBox.Show($"{Ad}: kayıtlı listedeki şu dosyalar bulunamadı, listeye alınmadı:\n\n" + string.Join("\n", eksik),
                    Ad, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        private void KaliciKaydet()
        {
            if (_kaliciDosya == null || AnaForm.KayitKapali) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_kaliciDosya));
                File.WriteAllLines(_kaliciDosya, Parcalar.Select(p => $"{p.Tur}|{p.Yol}|{p.Sayfalar}"));
            }
            catch { }
        }

        private static string Alinan(Parca p)
        {
            try { return p.EtkinSayfalar(p.SayfaSayisi).Count.ToString(); }
            catch { return "HATA"; }
        }

        private void Ozet()
        {
            int toplam = 0; bool hata = false;
            foreach (var p in Parcalar)
            {
                try { toplam += p.EtkinSayfalar(p.SayfaSayisi).Count; } catch { hata = true; }
            }
            _ozet.Text = Parcalar.Count == 0
                ? "(boş — PDF'leri buraya sürükleyin)"
                : $"({Parcalar.Count} dosya, {toplam} sayfa{(hata ? ", HATALI SAYFA ARALIĞI" : "")}" +
                  (Parcalar.Any(p => p.EskiKapakAtlaniyor) ? " — eski kapak çıkarılıp yeni kapak konacak" : "") + ")";
        }

        /// <summary>Boş "Alınacak sayfalar" hücresinde eski kapak atlanıyorsa soluk yazıyla gösterir (değer değişmez).</summary>
        private void HucreCiz(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= Parcalar.Count || e.ColumnIndex != 4) return;
            var p = Parcalar[e.RowIndex];
            if (!p.EskiKapakAtlaniyor || _tablo.IsCurrentCellInEditMode && _tablo.CurrentCell?.RowIndex == e.RowIndex && _tablo.CurrentCell?.ColumnIndex == 4) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            var yazi = p.MevcutKapak == 1 ? "eski kapak atlanır (2-)" : $"eski kapak atlanır ({p.MevcutKapak + 1}-)";
            TextRenderer.DrawText(e.Graphics, yazi, new Font(_tablo.Font, FontStyle.Italic), e.CellBounds,
                Color.FromArgb(200, 100, 20), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }

        private void TabloDegisti(object sender, DataGridViewCellEventArgs e)
        {
            if (_yukleniyor || e.RowIndex < 0 || e.RowIndex >= Parcalar.Count) return;
            var p = Parcalar[e.RowIndex];
            var deger = Convert.ToString(_tablo.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
            if (e.ColumnIndex == 1) { p.Tur = deger; BeginInvoke(() => Yenile(e.RowIndex)); }
            else if (e.ColumnIndex == 4)
            {
                p.Sayfalar = deger.Trim();
                KaliciKaydet();
                _yukleniyor = true;
                var hucre = _tablo.Rows[e.RowIndex].Cells[5];
                hucre.Value = Alinan(p);
                hucre.Style.ForeColor = (string)hucre.Value == "HATA" ? Color.Firebrick : Color.Black;
                _yukleniyor = false;
                Ozet();
            }
        }

        private static void SurukleGir(object sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void SurukleBirak(object sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] dosyalar) return;
            var pdfler = new List<string>();
            foreach (var d in dosyalar)
            {
                if (Directory.Exists(d)) pdfler.AddRange(Directory.GetFiles(d, "*.pdf").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
                else if (d.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) pdfler.Add(d);
            }
            if (pdfler.Count == 0) return;
            AnaForm.BolumKlasoruKaydet(Anahtar, Path.GetDirectoryName(pdfler[0]));
            AnaForm.SonKlasor = Path.GetDirectoryName(pdfler[0]);
            DosyaEkle(pdfler, null);
        }
    }
}
