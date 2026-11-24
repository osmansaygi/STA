namespace GesRaporu
{
    /// <summary>DWG okuma ve blok (A BLOK, B BLOK …) seçimi.</summary>
    public sealed partial class AnaForm
    {
        private readonly ComboBox _blok = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly List<Yazi> _planListesi = new();

        private async Task DwgOku()
        {
            var yol = _dwg.Text.Trim().Trim('"');
            if (!File.Exists(yol)) { MessageBox.Show(this, "DWG dosyası bulunamadı.", Text); return; }
            _durum.Text = "DWG okunuyor...";
            UseWaitCursor = true;
            try
            {
                _bilgi = await Task.Run(() => DwgBilgi.Oku(yol));
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                _durum.Text = "DWG okunamadı: " + ex.Message;
                return;
            }
            UseWaitCursor = false;

            _yukleniyor = true;
            _blok.Items.Clear();
            foreach (var bk in _bilgi.Bloklar) _blok.Items.Add(bk);
            _blok.Enabled = _bilgi.Bloklar.Count > 1;
            _yukleniyor = false;

            DwgBilgi.Blok secilen = _bilgi.Bloklar[0];
            if (_bilgi.Bloklar.Count > 1)
            {
                secilen = BlokSor(_bilgi.Bloklar, Path.GetFileName(yol));
                if (secilen == null) { _durum.Text = "Blok seçilmedi."; return; }
            }
            _yukleniyor = true;
            _blok.SelectedItem = secilen;
            _yukleniyor = false;
            BlokUygula(secilen);
        }

        /// <summary>Çoklu blok çiziminde raporu hazırlanacak bloğu sorar.</summary>
        private DwgBilgi.Blok BlokSor(List<DwgBilgi.Blok> bloklar, string dosya)
        {
            if (_otomatikBlok != null)
                return bloklar.FirstOrDefault(b => string.Equals(b.Ad, _otomatikBlok, StringComparison.OrdinalIgnoreCase)) ?? bloklar[0];
            using var f = new Form
            {
                Text = "Blok seçimi", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(440, 300), Font = Font, Icon = Icon,
            };
            var l = new Label
            {
                Text = $"\"{dosya}\" çiziminde {bloklar.Count} ayrı kapak (blok) bulundu.\nHangi bloğun GES raporu hazırlanacak?",
                Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 10, 10, 0),
            };
            var lb = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f, FontStyle.Bold), IntegralHeight = false };
            foreach (var b in bloklar)
            {
                bool cati = b.Planlar.Any(p => p.Metin.ToUpper(new System.Globalization.CultureInfo("tr-TR")).Contains("ÇATI"));
                lb.Items.Add(new BlokSecenek(b, $"{b.Gorunen}   —   {(cati ? "çatı planı var" : "çatı planı yok")}, {b.Planlar.Count} pafta"));
            }
            var onceki = bloklar.FindIndex(b => string.Equals(b.Ad, _g.Blok, StringComparison.OrdinalIgnoreCase));
            lb.SelectedIndex = onceki >= 0 ? onceki : 0;
            var tamam = new Button { Text = "Seç", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 110 };
            var iptal = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 110 };
            var alt = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) };
            alt.Controls.Add(tamam); alt.Controls.Add(iptal);
            var orta = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
            orta.Controls.Add(lb);
            f.Controls.Add(orta); f.Controls.Add(alt); f.Controls.Add(l);
            f.AcceptButton = tamam; f.CancelButton = iptal;
            lb.DoubleClick += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
            return f.ShowDialog(this) == DialogResult.OK && lb.SelectedItem is BlokSecenek bs ? bs.Blok : null;
        }

        private sealed record BlokSecenek(DwgBilgi.Blok Blok, string Metin)
        {
            public override string ToString() => Metin;
        }

        /// <summary>Seçilen bloğun kapak bilgilerini, çatı planını ve yüksekliğini forma uygular.</summary>
        private void BlokUygula(DwgBilgi.Blok blok)
        {
            if (_bilgi == null || blok == null) return;
            var b = _bilgi;
            var yol = _dwg.Text.Trim().Trim('"');
            b.BlokSec(blok);
            foreach (var ad in _dwgAlanlari) _alanlar[ad].BackColor = SystemColors.Window;

            var kayitli = Depo.ProjeGirdisiniOku(yol, blok.Ad);
            if (kayitli != null) { _g = kayitli; Doldur(_g); }

            void K(string ad, string deger) { if (kayitli == null || string.IsNullOrWhiteSpace(typeof(Girdi).GetProperty(ad).GetValue(kayitli)?.ToString())) Yaz(ad, deger); }
            string Kp(string a) => b.Kapak.TryGetValue(a, out var v) ? v : null;
            if (kayitli == null)
                foreach (var ad in new[] { nameof(Girdi.Mahalle), nameof(Girdi.Pafta), nameof(Girdi.Ada), nameof(Girdi.Parsel), nameof(Girdi.Blok), nameof(Girdi.YapiCinsi), nameof(Girdi.KatAdedi), nameof(Girdi.KullanimAmaci), nameof(Girdi.Ilce) })
                    _alanlar[ad].Text = "";
            K(nameof(Girdi.Il), b.Il);
            K(nameof(Girdi.Mahalle), Kp("MAHALLE"));
            K(nameof(Girdi.Pafta), Kp("PAFTA"));
            K(nameof(Girdi.Ada), Kp("ADA"));
            K(nameof(Girdi.Parsel), Kp("PARSEL"));
            K(nameof(Girdi.Blok), string.IsNullOrWhiteSpace(Kp("BLOK")) ? blok.Ad : Kp("BLOK"));
            K(nameof(Girdi.YapiCinsi), Kp("CİNSİ"));
            K(nameof(Girdi.KatAdedi), Kp("KAT ADEDİ"));
            K(nameof(Girdi.KullanimAmaci), Kp("KULLANIM AMACI"));
            K(nameof(Girdi.YapiSahibi), Kp("YAPI SAHİBİ"));
            K(nameof(Girdi.Ilce), b.Ilce);
            if (kayitli == null && b.SifirKotu != null) Yaz(nameof(Girdi.Rakim), Math.Round(b.SifirKotu.Value).ToString(Tr));
            BolgeGuncelle();

            // Bu bloğun paftaları
            _yukleniyor = true;
            _planListesi.Clear();
            _planListesi.AddRange(blok.Planlar.Count > 0 ? blok.Planlar : b.PlanBasliklari);
            _planBaslik.Items.Clear();
            foreach (var y in _planListesi) _planBaslik.Items.Add(y.Metin);
            var vars = b.VarsayilanCatiBasligi(_planListesi);
            _yukleniyor = false;
            _plan = null;
            if (vars != null) _planBaslik.SelectedIndex = _planListesi.IndexOf(vars);
            if (kayitli != null && !string.IsNullOrEmpty(kayitli.CatiPlaniBasligi) && _planBaslik.Items.Contains(kayitli.CatiPlaniBasligi))
                _planBaslik.SelectedItem = kayitli.CatiPlaniBasligi;
            if (kayitli == null)
            {
                var hb = b.BinaYuksekligi(_plan, out var hKaynak);
                if (hb != null) Yaz(nameof(Girdi.BinaYuksekligi), hb.Value.ToString("0.00", Tr));
                var hNot = hKaynak != null && hKaynak.StartsWith("kat adedinden") ? "Bina yüksekliği " + hKaynak + "; kontrol edin." : null;
                if (hNot != null && !b.Notlar.Contains(hNot)) b.Notlar.Add(hNot);
                var egim = b.EgimTahmini(_plan);
                if (egim != null) Yaz(nameof(Girdi.CatiEgimi), egim.Value.ToString("0.0", Tr));
                if (_plan?.PanelAdedi > 0) Yaz(nameof(Girdi.PanelAdedi), _plan.PanelAdedi.ToString(Tr));
            }

            var eksik = new List<string>();
            if (string.IsNullOrWhiteSpace(_alanlar[nameof(Girdi.YapiSahibi)].Text)) eksik.Add("yapı sahibi");
            if (string.IsNullOrWhiteSpace(_alanlar[nameof(Girdi.Ilce)].Text)) eksik.Add("ilçe");
            if (_plan == null) eksik.Add("çatı planı (bu blokta bulunamadı)");
            _durum.Text = $"{blok.Gorunen} okundu" + (b.Bloklar.Count > 1 ? $" ({b.Bloklar.Count} bloktan biri)" : "") + ". " +
                          (kayitli != null ? "Kayıtlı girdiler yüklendi. " : "") +
                          (eksik.Count > 0 ? "Eksik: " + string.Join(", ", eksik) + ". " : "") +
                          (b.GesNotuVar ? "(Çizimde \"çatıda GES\" notu var.) " : "") + string.Join(" ", b.Notlar);
            OzetGuncelle();
        }

        private void PlaniYenile()
        {
            if (_yukleniyor || _bilgi == null || _planBaslik.SelectedIndex < 0 || _planBaslik.SelectedIndex >= _planListesi.Count) return;
            var g = Topla(out _);
            try { _plan = _bilgi.PlanCiz(_planListesi[_planBaslik.SelectedIndex], g.PanelA, g.PanelB); }
            catch { _plan = null; }
        }
    }
}
