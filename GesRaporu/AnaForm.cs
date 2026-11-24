using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace GesRaporu
{
    public sealed partial class AnaForm : Form
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private readonly Dictionary<string, Control> _alanlar = new();
        private readonly HashSet<string> _dwgAlanlari = new();
        private readonly TextBox _dwg = new() { Dock = DockStyle.Fill };
        private readonly ComboBox _planBaslik = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _durum = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f) };
        private readonly Label _ozet = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly ListBox _ekler = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        private readonly Button _rapor = new() { Text = "PDF RAPOR OLUŞTUR", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f, FontStyle.Bold), BackColor = Color.FromArgb(22, 52, 110), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        private DwgBilgi _bilgi;
        private DwgBilgi.PlanCizimi _plan;
        private Girdi _g;
        private bool _yukleniyor;

        public AnaForm()
        {
            Text = "GES Çatı Statik Hesap Raporu";
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1080, 860);
            MinimumSize = new Size(900, 650);
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GesRaporu.Simge");
                if (s != null) Icon = new Icon(s);
            }
            catch { }

            _g = Depo.SonGirdiyiOku();
            // Eski sürümün varsayılan katalog ekleri artık kullanılmaz (EK-1, EK-2 sabit)
            _g.EkPdfler = _g.EkPdfler.Where(e => File.Exists(e) && !e.Replace('/', '\\').Contains(@"\Varliklar\ekler\", StringComparison.OrdinalIgnoreCase)).ToList();

            var kok = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            kok.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            kok.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            kok.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            kok.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(kok);

            // --- DWG satırı
            var ust = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            ust.Controls.Add(Etiket("Mimari DWG:"), 0, 0);
            ust.Controls.Add(_dwg, 1, 0);
            var gozat = new Button { Text = "Gözat...", Dock = DockStyle.Fill };
            gozat.Click += (s, e) => DwgSec();
            ust.Controls.Add(gozat, 2, 0);
            var oku = new Button { Text = "DWG'yi Oku", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            oku.Click += async (s, e) => await DwgOku();
            ust.Controls.Add(oku, 3, 0);
            ust.Controls.Add(Etiket("Blok:"), 0, 1);
            ust.Controls.Add(_blok, 1, 1);
            ust.SetColumnSpan(_blok, 3);
            _blok.Enabled = false;
            _blok.SelectedIndexChanged += (s, e) => { if (!_yukleniyor) BlokUygula(_blok.SelectedItem as DwgBilgi.Blok); };
            ust.Controls.Add(Etiket("Çatı planı:"), 0, 2);
            ust.Controls.Add(_planBaslik, 1, 2);
            ust.SetColumnSpan(_planBaslik, 3);
            _planBaslik.SelectedIndexChanged += (s, e) => PlaniYenile();
            kok.Controls.Add(ust, 0, 0);

            // --- Gruplar
            var akis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            akis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            akis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            kok.Controls.Add(akis, 0, 1);

            var proje = Grup("Proje / Kapak (DWG'den okunur — mavi alanlar)", akis, 0, 0, 2);
            Satir(proje, "Yapı sahibi", nameof(Girdi.YapiSahibi), true);
            Satir(proje, "İl", nameof(Girdi.Il), true, combo: BolgeDeposu.Iller);
            Satir(proje, "İlçe", nameof(Girdi.Ilce), true, combo: BolgeDeposu.KonyaIlceleri);
            Satir(proje, "Mahalle", nameof(Girdi.Mahalle), true);
            Satir(proje, "Pafta / Ada / Parsel", nameof(Girdi.Pafta), true, ek: new[] { nameof(Girdi.Ada), nameof(Girdi.Parsel) });
            Satir(proje, "Blok", nameof(Girdi.Blok), true);
            Satir(proje, "Yapı cinsi / Kat adedi", nameof(Girdi.YapiCinsi), true, ek: new[] { nameof(Girdi.KatAdedi) });
            Satir(proje, "Kullanım amacı", nameof(Girdi.KullanimAmaci), true);
            Satir(proje, "Hazırlayan firma", nameof(Girdi.Hazirlayan));
            Satir(proje, "Hazırlayan mühendis", nameof(Girdi.HazirlayanMuhendis));

            var yuk = Grup("Yükler (TS 498:2021)", akis, 0, 1);
            Satir(yuk, "Rakım (m)", nameof(Girdi.Rakim), true);
            Satir(yuk, "Kar bölgesi (1–9)", nameof(Girdi.KarBolgesi), combo: Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray(), liste: true);
            Satir(yuk, "Bina yüksekliği H (m)", nameof(Girdi.BinaYuksekligi), true);
            Satir(yuk, "Rüzgar hızı (km/sa)", nameof(Girdi.RuzgarHiziKmSa));
            Satir(yuk, "Çatı eğimi α (°)", nameof(Girdi.CatiEgimi), true);

            var panel = Grup("Güneş paneli", akis, 1, 1);
            Satir(panel, "Model", nameof(Girdi.PanelModeli), combo: PanelKatalogu.Modeller.Select(m => m.Ad).Append(PanelKatalogu.Kullanici).ToArray());
            Satir(panel, "Ebat a x b (m)", nameof(Girdi.PanelA), ek: new[] { nameof(Girdi.PanelB) });
            Satir(panel, "Ağırlık (kg) / Kalınlık (mm)", nameof(Girdi.PanelAgirlik), ek: new[] { nameof(Girdi.PanelT) });
            Satir(panel, "Güç (Wp) / Adet", nameof(Girdi.PanelGucWp), ek: new[] { nameof(Girdi.PanelAdedi) });

            var kap = Grup("Mevcut çatı kaplaması", akis, 0, 2);
            Satir(kap, "Kaplama tipi", nameof(Girdi.KaplamaTipi), combo: TrapezTablosu.Tipler.ToArray(), liste: true);
            Satir(kap, "Sac kalınlığı (mm)", nameof(Girdi.SacKalinligi), combo: new[] { "0,50", "0,60", "0,70" });
            Satir(kap, "Aşık aralığı (m)", nameof(Girdi.AsikAraligi), combo: new[] { "1,00", "1,50", "2,00", "2,50" });
            Satir(kap, "Ağırlık (kg/m²)", nameof(Girdi.KaplamaAgirligi));
            Satir(kap, "Kapasite (kg/m², 0=tablo)", nameof(Girdi.KaplamaKapasitesi));

            var tas = Grup("Taşıyıcı ve bağlantı", akis, 1, 2);
            Satir(tas, "Profil mesnet aralığı (m)", nameof(Girdi.ProfilMesnetAraligi));
            Satir(tas, "Bağlantı ağırlığı (kg/m)", nameof(Girdi.BaglantiAgirligi));
            Satir(tas, "Bulon Ø (mm) / adet", nameof(Girdi.BulonCapi), ek: new[] { nameof(Girdi.MesnetBulonAdedi) });
            Satir(tas, "Bağlantı profili t (mm)", nameof(Girdi.BaglantiProfilKalinligi));
            Satir(tas, "Vida Ø (mm) / adet", nameof(Girdi.VidaCapi), ek: new[] { nameof(Girdi.ProfilVidaAdedi) });

            var ek = Grup("İlave ekler — EK-1 güneş paneli ve EK-2 trapez sac her rapora sabit eklenir; buradakiler EK-3, EK-4 … olur", akis, 0, 3, 2);
            var ekTablo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 90 };
            ekTablo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ekTablo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            ekTablo.Controls.Add(_ekler, 0, 0);
            ekTablo.SetRowSpan(_ekler, 2);
            var ekle = new Button { Text = "Ekle...", Dock = DockStyle.Fill };
            var cikar = new Button { Text = "Çıkar", Dock = DockStyle.Fill };
            ekle.Click += (s, e) => { using var d = new OpenFileDialog { Filter = "PDF|*.pdf", Multiselect = true }; if (d.ShowDialog(this) == DialogResult.OK) { _ekler.Items.AddRange(d.FileNames); } };
            cikar.Click += (s, e) => { if (_ekler.SelectedIndex >= 0) _ekler.Items.RemoveAt(_ekler.SelectedIndex); };
            ekTablo.Controls.Add(ekle, 1, 0);
            ekTablo.Controls.Add(cikar, 1, 1);
            ek.Controls.Add(ekTablo);
            ek.Height = 130;

            kok.Controls.Add(_ozet, 0, 2);
            var alt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            alt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            alt.Controls.Add(_durum, 0, 0);
            alt.Controls.Add(_rapor, 1, 0);
            _rapor.Click += (s, e) => RaporOlustur();
            kok.Controls.Add(alt, 0, 3);

            Doldur(_g);
            _dwg.Text = _g.DwgYolu;
            _durum.Text = "Mimari DWG dosyasını seçip \"DWG'yi Oku\"ya basın. Mavi alanlar DWG'den doldurulur; gerekirse düzeltin.";

            Bagla(nameof(Girdi.Il), (s, e) => BolgeGuncelle());
            Bagla(nameof(Girdi.Ilce), (s, e) => BolgeGuncelle());
            Bagla(nameof(Girdi.PanelModeli), (s, e) => PanelSec());
            foreach (var c in _alanlar.Values) c.TextChanged += (s, e) => OzetGuncelle();
            OzetGuncelle();
        }

        // ------------------------------------------------------------------ yerleşim yardımcıları
        private static Label Etiket(string m) => new() { Text = m, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };

        private static TableLayoutPanel Grup(string baslik, TableLayoutPanel ana, int sutun, int satir, int yayilma = 1)
        {
            var gb = new GroupBox { Text = baslik, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Font = new Font("Segoe UI", 9.5f) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gb.Controls.Add(t);
            ana.Controls.Add(gb, sutun, satir);
            if (yayilma > 1) ana.SetColumnSpan(gb, yayilma);
            return t;
        }

        private void Satir(TableLayoutPanel t, string etiket, string ozellik, bool dwg = false, string[] combo = null, bool liste = false, string[] ek = null)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.Controls.Add(Etiket(etiket), 0, r);
            var adlar = new[] { ozellik }.Concat(ek ?? Array.Empty<string>()).ToArray();
            var ic = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = adlar.Length, Margin = new Padding(0) };
            for (int i = 0; i < adlar.Length; i++)
            {
                ic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / adlar.Length));
                Control c = combo != null && i == 0
                    ? new ComboBox { Dock = DockStyle.Fill, DropDownStyle = liste ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown }
                    : new TextBox { Dock = DockStyle.Fill };
                if (c is ComboBox cb) { cb.Items.AddRange(combo); if (!liste) { cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend; cb.AutoCompleteSource = AutoCompleteSource.ListItems; } }
                _alanlar[adlar[i]] = c;
                if (dwg) _dwgAlanlari.Add(adlar[i]);
                ic.Controls.Add(c, i, 0);
            }
            t.Controls.Add(ic, 1, r);
        }

        private void Bagla(string ad, EventHandler h)
        {
            var c = _alanlar[ad];
            c.TextChanged += h;
            if (c is ComboBox cb) cb.SelectedIndexChanged += h;
        }

        // ------------------------------------------------------------------ girdi ↔ form
        private void Doldur(Girdi g)
        {
            _yukleniyor = true;
            foreach (var (ad, c) in _alanlar)
            {
                var v = typeof(Girdi).GetProperty(ad).GetValue(g);
                string s = v switch
                {
                    double d => d.ToString("0.###", Tr),
                    int i => i.ToString(Tr),
                    _ => v?.ToString() ?? "",
                };
                if (c is ComboBox cb && cb.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    int idx = cb.Items.IndexOf(s);
                    cb.SelectedIndex = idx >= 0 ? idx : (cb.Items.Count > 0 ? 0 : -1);
                }
                else c.Text = s;
            }
            _ekler.Items.Clear();
            _ekler.Items.AddRange(g.EkPdfler.Cast<object>().ToArray());
            _yukleniyor = false;
        }

        private Girdi Topla(out string hata)
        {
            hata = null;
            var g = _g.Kopya();
            foreach (var (ad, c) in _alanlar)
            {
                var p = typeof(Girdi).GetProperty(ad);
                var s = c.Text.Trim();
                try
                {
                    if (p.PropertyType == typeof(double)) p.SetValue(g, double.Parse(s.Replace('.', ','), Tr));
                    else if (p.PropertyType == typeof(int)) p.SetValue(g, string.IsNullOrEmpty(s) ? 0 : int.Parse(s, Tr));
                    else p.SetValue(g, s);
                }
                catch { hata ??= $"Geçersiz sayı: {ad} = \"{s}\""; }
            }
            g.DwgYolu = _dwg.Text.Trim();
            g.CatiPlaniBasligi = _planBaslik.SelectedItem?.ToString() ?? "";
            g.EkPdfler = _ekler.Items.Cast<string>().ToList();
            return g;
        }

        private void Yaz(string ad, string deger)
        {
            if (string.IsNullOrWhiteSpace(deger)) return;
            var c = _alanlar[ad];
            if (c is ComboBox cb && cb.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                int i = cb.Items.IndexOf(deger);
                if (i >= 0) cb.SelectedIndex = i;
            }
            else c.Text = deger;
            c.BackColor = Color.FromArgb(222, 235, 255);
        }

        private void BolgeGuncelle()
        {
            if (_yukleniyor) return;
            var b = BolgeDeposu.Bolge(_alanlar[nameof(Girdi.Il)].Text, _alanlar[nameof(Girdi.Ilce)].Text);
            if (b != null) Yaz(nameof(Girdi.KarBolgesi), b.Value.ToString());
        }

        private void PanelSec()
        {
            if (_yukleniyor) return;
            var m = PanelKatalogu.Modeller.FirstOrDefault(x => x.Ad == _alanlar[nameof(Girdi.PanelModeli)].Text);
            if (m == null) return;
            _alanlar[nameof(Girdi.PanelA)].Text = m.A.ToString("0.###", Tr);
            _alanlar[nameof(Girdi.PanelB)].Text = m.B.ToString("0.###", Tr);
            _alanlar[nameof(Girdi.PanelT)].Text = m.T.ToString("0.###", Tr);
            _alanlar[nameof(Girdi.PanelAgirlik)].Text = m.Kg.ToString("0.###", Tr);
            _alanlar[nameof(Girdi.PanelGucWp)].Text = m.Wp.ToString("0.###", Tr);
        }

        private void OzetGuncelle()
        {
            if (_yukleniyor) return;
            var g = Topla(out var hata);
            if (hata != null) { _ozet.ForeColor = Color.DarkRed; _ozet.Text = hata; return; }
            try
            {
                var h = new Hesap(g);
                string T(bool b) => b ? "✔" : "✘";
                _ozet.ForeColor = h.HepsiYeterli ? Color.FromArgb(20, 110, 50) : Color.FromArgb(180, 30, 30);
                _ozet.Text = $"Kar Sk = {h.SkKn:0.00} kN/m² ({h.KarKg:0.0} kg/m²)   |   Rüzgar q = {h.QKn:0.00} kN/m²   |   " +
                             $"Kaplama {T(h.KaplamaYeterli)} {h.PKaplama:0}/{h.Ga:0}   Bulon {T(h.BulonYeterli)}   Vida {T(h.VidaYeterli)}   Profil {T(h.ProfilYeterli)}";
                if (!h.HepsiYeterli)
                {
                    bool cozulur = new Hesap(Hesap.Kurtar(g, out _)).HepsiYeterli;
                    _ozet.ForeColor = cozulur ? Color.FromArgb(190, 110, 0) : Color.FromArgb(180, 30, 30);
                    _ozet.Text += cozulur ? "   → raporda otomatik kurtarılacak" : "   → otomatik kurtarılamıyor!";
                }
            }
            catch (Exception ex) { _ozet.Text = ex.Message; }
        }

        // ------------------------------------------------------------------ DWG
        private void DwgSec()
        {
            using var d = new OpenFileDialog { Filter = "AutoCAD çizimi (*.dwg)|*.dwg", Title = "Mimari DWG seç" };
            try { if (File.Exists(_dwg.Text)) d.InitialDirectory = Path.GetDirectoryName(_dwg.Text); } catch { }
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                _dwg.Text = d.FileName;
                _ = DwgOku();
            }
        }


        // ------------------------------------------------------------------ rapor
        private void RaporOlustur()
        {
            var g = Topla(out var hata);
            if (hata != null) { MessageBox.Show(this, hata, Text); return; }

            // Yetersiz eleman raporda yer almaz: kademeli büyütülür, form güncellenir
            g = Hesap.Kurtar(g, out var degisiklik);
            if (degisiklik.Count > 0)
            {
                KurtarmayiUygula(g);
                MessageBox.Show(this, "Yetersiz çıkan elemanlar yeterli olacak şekilde değiştirildi:\n\n• " + string.Join("\n• ", degisiklik) +
                    "\n\nRapor bu değerlerle hazırlanacak.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (!new Hesap(g).HepsiYeterli)
            {
                MessageBox.Show(this, "Bazı tahkikler otomatik düzeltmeyle de sağlanamadı (ör. kullanıcı tanımlı kaplama kapasitesi yetersiz veya profil 10 cm mesnet aralığında bile yetmiyor).\n" +
                    "Girdileri kontrol edin; yetersiz eleman içeren rapor oluşturulmaz.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(g.YapiSahibi) &&
                MessageBox.Show(this, "Yapı sahibi boş. Yine de devam edilsin mi?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (_bilgi == null && File.Exists(g.DwgYolu) &&
                MessageBox.Show(this, "DWG henüz okunmadı; rapora çatı planı konmayacak. Devam edilsin mi?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            string klasor = File.Exists(g.DwgYolu) ? Path.GetDirectoryName(g.DwgYolu) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string ad = "GES_STATIK_HESAP_RAPORU" + (string.IsNullOrWhiteSpace(g.Blok) ? "" : "_" + DwgBilgi.Etiketle(g.Blok).Replace(' ', '_').Replace(".", "")) + ".pdf";
            using var d = new SaveFileDialog { Filter = "PDF|*.pdf", InitialDirectory = klasor, FileName = ad, Title = "Raporu kaydet" };
            if (d.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                UseWaitCursor = true;
                Rapor.Uret(g, _plan, d.FileName);
                UseWaitCursor = false;
                _g = g;
                Depo.SonGirdiyiYaz(g);
                Depo.ProjeGirdisiniYaz(g);
                BolgeDeposu.Ogren(g.Il, g.Ilce, g.Mahalle, g.KarBolgesi);
                _durum.Text = "Rapor oluşturuldu: " + d.FileName;
                Process.Start(new ProcessStartInfo(d.FileName) { UseShellExecute = true });
            }
            catch (IOException ex)
            {
                UseWaitCursor = false;
                MessageBox.Show(this, "PDF yazılamadı (dosya açık olabilir):\n" + ex.Message, Text);
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                MessageBox.Show(this, "Rapor oluşturulamadı:\n" + ex, Text);
            }
        }

        /// <summary>Kurtarmada değişen alanları forma yazar ve turuncu işaretler.</summary>
        private void KurtarmayiUygula(Girdi g)
        {
            var eski = Topla(out _);
            foreach (var (ad, c) in _alanlar)
            {
                var p = typeof(Girdi).GetProperty(ad);
                if (Equals(p.GetValue(eski), p.GetValue(g))) continue;
                var v = p.GetValue(g);
                c.Text = v is double dd ? dd.ToString("0.###", Tr) : Convert.ToString(v, Tr);
                c.BackColor = Color.FromArgb(255, 226, 170);
            }
        }

        /// <summary>Doğrulama: DWG'yi arayüz akışıyla okuyup alan değerlerini döker.</summary>
        private string _otomatikBlok;

        internal async Task<string> FormTesti(string dwg, string blok = null)
        {
            _otomatikBlok = blok;
            _dwg.Text = dwg;
            await DwgOku();
            var g = Topla(out var hata);
            return $"hata={hata}\nplan={_planBaslik.SelectedItem} cizgi={_plan?.Cizgiler.Count}\n" +
                   string.Join("\n", _alanlar.Select(a => $"{a.Key}={a.Value.Text}{(a.Value.BackColor == Color.FromArgb(222, 235, 255) ? " [DWG]" : "")}")) +
                   $"\nozet={_ozet.Text}\ndurum={_durum.Text}";
        }

        private static IEnumerable<string> VarsayilanEkler()
        {
            var k = Path.Combine(AppContext.BaseDirectory, "Varliklar", "ekler");
            return Directory.Exists(k) ? Directory.GetFiles(k, "*.pdf").OrderBy(x => x) : Enumerable.Empty<string>();
        }
    }
}
