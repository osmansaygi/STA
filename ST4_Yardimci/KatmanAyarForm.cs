using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using BeykentOrtak;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace ST4Yardimci
{
    /// <summary>
    /// KATMANYUKLE: katman / ölçü stili / yazı stili ayarları (Yardımcı ve Plan ID ortak dosyası).
    /// Kaydet ayar dosyasına yazar; Yükle kaydedip açık çizime uygular. Katman ve stil adları değiştirilemez.
    /// </summary>
    internal sealed class KatmanAyarForm : Beykent.Monolit.MonolitForm
    {
        private static readonly string[] CizgiTipleri = { "Continuous", "DASHED", "HIDDEN", "CENTER", "DASHDOT", "PHANTOM", "DOT", "DIVIDE", "BORDER" };

        private BeykentStilDosya _ayar;
        private readonly DataGridView _katman, _olcu, _yazi;
        private readonly Label _durum, _uyari;
        private readonly BeykentStilDosya _varsayilan = BeykentStilAyar.Varsayilan();

        internal KatmanAyarForm()
        {
            Text = "Katman / Ölçü / Yazı Stili Ayarları";
            ClientSize = new Size(1040, 620);
            MinimumSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg; ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, Padding = new Point(14, 5) };
            tabs.DrawItem += (s, e) =>
            {
                bool sel = e.Index == tabs.SelectedIndex;
                using (var b = new SolidBrush(sel ? Beykent.Monolit.StandardFormTheme.Bg : Beykent.Monolit.StandardFormTheme.BgAlt))
                    e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, e.Bounds,
                    sel ? Beykent.Monolit.StandardFormTheme.Accent : Beykent.Monolit.StandardFormTheme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            _katman = Grid(); _olcu = Grid(); _yazi = Grid();
            tabs.TabPages.Add(Sayfa("Katmanlar", _katman));
            tabs.TabPages.Add(Sayfa("Ölçü stilleri", _olcu));
            tabs.TabPages.Add(Sayfa("Yazı stilleri", _yazi));

            // Genişlik baştan form kadar: sağa sabitli düğmelerin uzaklığı eklenirken bu genişliğe göre alınır
            // (varsayılan dar panelde alınırsa panel büyüyünce düğmeler pencere dışına kayıyordu).
            var alt = new Panel { Dock = DockStyle.Bottom, Height = 78, Width = ClientSize.Width, BackColor = Beykent.Monolit.StandardFormTheme.BgAlt };
            _uyari = new Label { Location = new Point(12, 6), Size = new Size(1010, 20), ForeColor = Beykent.Monolit.StandardFormTheme.Accent, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            _durum = new Label { Location = new Point(12, 28), Size = new Size(520, 40), ForeColor = Beykent.Monolit.StandardFormTheme.TextMuted, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            var varsayilan = Dugme("Varsayılana dön", 540, 130);
            var kaydet = Dugme("Kaydet", 678, 100);
            var yukle = Dugme("Kaydet ve Yükle", 786, 130);
            var kapat = Dugme("Kapat", 924, 100);
            kapat.DialogResult = DialogResult.Cancel;
            CancelButton = kapat;
            varsayilan.Click += (s, e) => VarsayilanaDon();
            kaydet.Click += (s, e) => Kaydet();
            yukle.Click += (s, e) => { if (Kaydet()) _durum.Text = BeykentKatmanYukle.YukleAktifCizim(); };
            foreach (Control c in new Control[] { _uyari, _durum, varsayilan, kaydet, yukle, kapat }) alt.Controls.Add(c);

            Controls.Add(tabs);
            Controls.Add(alt);

            KolonlariKur();
            _ayar = BeykentStilAyar.Aktif();
            Doldur();
            string yol = BeykentStilAyar.AktifYol;
            _durum.Text = yol == null
                ? "Ayar dosyası yok; bugünkü varsayılanlar gösteriliyor. Kaydet: " + BeykentStilAyar.KullaniciYolu
                : "Ayar dosyası: " + yol;
            _durum.Text += "\nKomutlar var olan katmana dokunmaz (Renk zorla hariç); Yükle var olanları da günceller.";

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(940);
            ui.Tip(_durum, _durum.Text);
            _durum.Text = "Kaydet ayarları saklar. Kaydet ve Yükle açık çizime uygular.";
            var filter = new TextBox();
            ui.Section(null).Field("Ad / özellik ara", filter).Control(tabs, 300);
            filter.TextChanged += (s, e) =>
            {
                foreach (var grid in new[] { _katman, _olcu, _yazi })
                {
                    if (!grid.EndEdit()) continue;
                    grid.CurrentCell = null;
                    foreach (DataGridViewRow row in grid.Rows)
                        if (!row.IsNewRow) row.Visible = string.IsNullOrWhiteSpace(filter.Text) || row.Cells.Cast<DataGridViewCell>()
                            .Any(c => Convert.ToString(c.Value).IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0);
                }
            };
            ui.Section(null).Control(_uyari).Control(_durum);
            ui.Footer(yukle, kapat, kaydet, varsayilan);
            ui.Finish();
            MinimumSize = new Size(720, 360);
        }

        // ------------------------------------------------------------------ arayüz

        private static TabPage Sayfa(string baslik, Control icerik)
        {
            var p = new TabPage(baslik) { BackColor = Beykent.Monolit.StandardFormTheme.Bg, ForeColor = Beykent.Monolit.StandardFormTheme.Text, Padding = new Padding(4) };
            p.Controls.Add(icerik);
            return p;
        }

        private static Button Dugme(string text, int x, int w)
        {
            return new Button
            {
                Text = text, Location = new Point(x, 30), Size = new Size(w, 32), FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.Bg, ForeColor = Beykent.Monolit.StandardFormTheme.Text,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                FlatAppearance = { BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder }
            };
        }

        private static DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Beykent.Monolit.StandardFormTheme.Bg, BorderStyle = BorderStyle.None,
                GridColor = Beykent.Monolit.StandardFormTheme.InputBorder, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false, EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Beykent.Monolit.StandardFormTheme.BgAlt;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Beykent.Monolit.StandardFormTheme.BgAlt;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            g.DefaultCellStyle.BackColor = Beykent.Monolit.StandardFormTheme.InputBg;
            g.DefaultCellStyle.ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            g.DefaultCellStyle.SelectionBackColor = Beykent.Monolit.StandardFormTheme.BgPressed;
            g.DefaultCellStyle.SelectionForeColor = Beykent.Monolit.StandardFormTheme.Text;
            g.DataError += (s, e) => { e.ThrowException = false; };
            g.EditingControlShowing += (s, e) =>
            {
                e.Control.BackColor = Beykent.Monolit.StandardFormTheme.InputBg;
                e.Control.ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            };
            // Onay kutuları tıklanınca hemen yazılsın.
            g.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewCheckBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            return g;
        }

        private static DataGridViewTextBoxColumn Metin(string ad, string baslik, int w, bool salt = false) =>
            new DataGridViewTextBoxColumn { Name = ad, HeaderText = baslik, Width = w, ReadOnly = salt, SortMode = DataGridViewColumnSortMode.NotSortable };

        private static DataGridViewCheckBoxColumn Onay(string ad, string baslik, int w) =>
            new DataGridViewCheckBoxColumn { Name = ad, HeaderText = baslik, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable };

        private static DataGridViewComboBoxColumn Liste(string ad, string baslik, int w, IEnumerable<string> items)
        {
            var c = new DataGridViewComboBoxColumn
            {
                Name = ad, HeaderText = baslik, Width = w, FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, SortMode = DataGridViewColumnSortMode.NotSortable
            };
            foreach (string i in items) c.Items.Add(i);
            return c;
        }

        private void KolonlariKur()
        {
            _katman.Columns.Add(Metin("Ad", "Katman", 270, true));
            _katman.Columns.Add(Metin("Ornek", "", 28, true));
            _katman.Columns.Add(Metin("Renk", "Renk (ACI ya da R,G,B)", 110));
            _katman.Columns.Add(Liste("Kalinlik", "Kalınlık (mm)", 90,
                new[] { "Varsayılan" }.Concat(BeykentStilUygula.GecerliKalinliklar.Select(KalinlikMetni))));
            _katman.Columns.Add(Liste("CizgiTipi", "Çizgi tipi", 110, CizgiTipleri));
            _katman.Columns.Add(Metin("Saydamlik", "Saydamlık %", 80));
            _katman.Columns.Add(Onay("Cizdir", "Çizdir", 55));
            _katman.Columns.Add(Onay("Yukle", "Yükle'de oluştur", 75));
            _katman.Columns.Add(Onay("RenkZorla", "Renk zorla", 65));
            _katman.CellDoubleClick += KatmanRenkSec;
            _katman.CellEndEdit += (s, e) => { if (e.RowIndex >= 0) RenkOrnek(_katman.Rows[e.RowIndex]); };

            _olcu.Columns.Add(Metin("Ad", "Ölçü stili", 170, true));
            _olcu.Columns.Add(Metin("YaziYuksekligi", "Yazı yük.", 65));
            _olcu.Columns.Add(Metin("Olcek", "Ölçek (DIMLFAC)", 70));
            _olcu.Columns.Add(Metin("OkBoyu", "Ok boyu", 60));
            _olcu.Columns.Add(Onay("Tik", "Eğik tik", 50));
            _olcu.Columns.Add(Metin("Ofset", "Uzatma ofseti", 70));
            _olcu.Columns.Add(Metin("Uzanti", "Uzatma taşması", 70));
            _olcu.Columns.Add(Onay("SabitUzanti", "Sabit uzatma", 60));
            _olcu.Columns.Add(Metin("Bosluk", "Yazı boşluğu", 65));
            _olcu.Columns.Add(Metin("Yuvarlama", "Yuvarlama", 70));
            _olcu.Columns.Add(Metin("Ondalik", "Ondalık", 55));
            _olcu.Columns.Add(Metin("SifirBastir", "DIMZIN", 55));
            _olcu.Columns.Add(Onay("YaziUstte", "Yazı üstte", 55));
            _olcu.Columns.Add(Onay("UzatmaGizle", "Uzatma gizli", 60));
            _olcu.Columns.Add(Liste("YaziStili", "Yazı stili", 140, new string[0]));
            _olcu.CellEndEdit += (s, e) => YukseklikUyarisi();

            _yazi.Columns.Add(Metin("Ad", "Yazı stili", 200, true));
            var fontlar = new List<string>();
            try
            {
                using (var ifc = new InstalledFontCollection())
                    fontlar.AddRange(ifc.Families.Select(f => f.Name));
            }
            catch { }
            _yazi.Columns.Add(Liste("Font", "Yazı tipi", 260, fontlar));
            _yazi.Columns.Add(Metin("Genislik", "Genişlik faktörü", 90));
            _yazi.Columns.Add(Metin("EgikAci", "Eğik açı (°)", 80));
            _yazi.Columns.Add(Metin("Yukseklik", "Sabit yükseklik (0 = komut)", 110));
        }

        // ------------------------------------------------------------------ doldur / oku

        private void Doldur()
        {
            _katman.Rows.Clear(); _olcu.Rows.Clear(); _yazi.Rows.Clear();

            var cizgi = (DataGridViewComboBoxColumn)_katman.Columns["CizgiTipi"];
            foreach (KatmanAyar k in _ayar.Katmanlar)
            {
                string lt = string.IsNullOrWhiteSpace(k.CizgiTipi) ? "Continuous" : k.CizgiTipi.Trim();
                if (!cizgi.Items.Contains(lt)) cizgi.Items.Add(lt);
                int i = _katman.Rows.Add(k.Ad, "", RenkMetni(k), KalinlikMetni(k.Kalinlik), lt,
                    k.Saydamlik.ToString(CultureInfo.InvariantCulture), k.Cizdir, k.Yukle, k.RenkZorla);
                RenkOrnek(_katman.Rows[i]);
            }

            var stil = (DataGridViewComboBoxColumn)_olcu.Columns["YaziStili"];
            stil.Items.Clear();
            foreach (YaziAyar y in _ayar.YaziStilleri) if (!stil.Items.Contains(y.Ad)) stil.Items.Add(y.Ad);
            foreach (OlcuAyar o in _ayar.OlcuStilleri)
            {
                string ys = string.IsNullOrWhiteSpace(o.YaziStili) ? BeykentStilAyar.YaziBeykent : o.YaziStili;
                if (!stil.Items.Contains(ys)) stil.Items.Add(ys);
                _olcu.Rows.Add(o.Ad, S(o.YaziYuksekligi), S(o.Olcek), S(o.OkBoyu), o.Tik, S(o.Ofset), S(o.Uzanti), o.SabitUzanti,
                    S(o.Bosluk), S(o.Yuvarlama), o.Ondalik.ToString(CultureInfo.InvariantCulture),
                    o.SifirBastir.ToString(CultureInfo.InvariantCulture), o.YaziUstte, o.UzatmaGizle, ys);
            }

            var font = (DataGridViewComboBoxColumn)_yazi.Columns["Font"];
            foreach (YaziAyar y in _ayar.YaziStilleri)
            {
                string f = string.IsNullOrWhiteSpace(y.Font) ? "Bahnschrift Light Condensed" : y.Font.Trim();
                if (!font.Items.Contains(f)) font.Items.Add(f);
                _yazi.Rows.Add(y.Ad, f, S(y.Genislik), S(y.EgikAci), S(y.Yukseklik));
            }
            YukseklikUyarisi();
        }

        /// <summary>Tablolardan yeni ayar nesnesi üretir; hatalı hücrede null ve açıklama döner.</summary>
        private BeykentStilDosya Oku(out string hata)
        {
            hata = null;
            _katman.EndEdit(); _olcu.EndEdit(); _yazi.EndEdit();
            var d = new BeykentStilDosya();
            try
            {
                foreach (DataGridViewRow r in _katman.Rows)
                {
                    var k = new KatmanAyar { Ad = Str(r, "Ad") };
                    RenkCoz(Str(r, "Renk"), k, k.Ad);
                    k.Kalinlik = KalinlikCoz(Str(r, "Kalinlik"));
                    k.CizgiTipi = string.IsNullOrWhiteSpace(Str(r, "CizgiTipi")) ? "Continuous" : Str(r, "CizgiTipi");
                    k.Saydamlik = (int)Sayi(r, "Saydamlik", 0, 90, k.Ad);
                    k.Cizdir = Bool(r, "Cizdir"); k.Yukle = Bool(r, "Yukle"); k.RenkZorla = Bool(r, "RenkZorla");
                    d.Katmanlar.Add(k);
                }
                foreach (DataGridViewRow r in _olcu.Rows)
                {
                    string ad = Str(r, "Ad");
                    d.OlcuStilleri.Add(new OlcuAyar
                    {
                        Ad = ad,
                        YaziYuksekligi = Sayi(r, "YaziYuksekligi", 0.1, 1000, ad),
                        Olcek = Sayi(r, "Olcek", 0.001, 1000, ad),
                        OkBoyu = Sayi(r, "OkBoyu", 0, 1000, ad),
                        Tik = Bool(r, "Tik"),
                        Ofset = Sayi(r, "Ofset", 0, 1000, ad),
                        Uzanti = Sayi(r, "Uzanti", 0, 1000, ad),
                        SabitUzanti = Bool(r, "SabitUzanti"),
                        Bosluk = Sayi(r, "Bosluk", 0, 1000, ad),
                        Yuvarlama = Sayi(r, "Yuvarlama", 0, 1000, ad),
                        Ondalik = (short)Sayi(r, "Ondalik", 0, 8, ad),
                        SifirBastir = (short)Sayi(r, "SifirBastir", 0, 15, ad),
                        YaziUstte = Bool(r, "YaziUstte"),
                        UzatmaGizle = Bool(r, "UzatmaGizle"),
                        YaziStili = string.IsNullOrWhiteSpace(Str(r, "YaziStili")) ? BeykentStilAyar.YaziBeykent : Str(r, "YaziStili")
                    });
                }
                foreach (DataGridViewRow r in _yazi.Rows)
                {
                    string ad = Str(r, "Ad");
                    d.YaziStilleri.Add(new YaziAyar
                    {
                        Ad = ad,
                        Font = string.IsNullOrWhiteSpace(Str(r, "Font")) ? "Bahnschrift Light Condensed" : Str(r, "Font"),
                        Genislik = Sayi(r, "Genislik", 0.01, 100, ad),
                        EgikAci = Sayi(r, "EgikAci", -85, 85, ad),
                        Yukseklik = Sayi(r, "Yukseklik", 0, 10000, ad)
                    });
                }
            }
            catch (FormatException ex)
            {
                hata = ex.Message;
                return null;
            }
            return d;
        }

        private bool Kaydet()
        {
            BeykentStilDosya d = Oku(out string hata);
            if (d == null)
            {
                _uyari.Text = "Kaydedilmedi: " + hata;
                return false;
            }
            try
            {
                BeykentStilAyar.Kaydet(d);
            }
            catch (Exception ex)
            {
                _uyari.Text = "Kaydedilmedi: " + ex.Message;
                return false;
            }
            _ayar = BeykentStilAyar.Aktif();
            _durum.Text = "Kaydedildi: " + BeykentStilAyar.KullaniciYolu + "\nYardımcı ve Plan ID komutları bu ayarları kullanır.";
            YukseklikUyarisi();
            return true;
        }

        private void VarsayilanaDon()
        {
            if (MessageBox.Show(this, "Bütün değerler bugünkü varsayılanlara dönsün mü? (Kaydet'e basılana kadar dosya değişmez.)",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _ayar = BeykentStilAyar.Varsayilan();
            Doldur();
            _durum.Text = "Varsayılanlar yüklendi; kalıcı olması için Kaydet.";
        }

        private void YukseklikUyarisi()
        {
            var degisen = new List<string>();
            foreach (DataGridViewRow r in _olcu.Rows)
            {
                string ad = Str(r, "Ad");
                OlcuAyar v = _varsayilan.OlcuStilleri.FirstOrDefault(o => string.Equals(o.Ad, ad, StringComparison.OrdinalIgnoreCase));
                if (v == null) continue;
                if (TrySayi(Str(r, "YaziYuksekligi"), out double h) && Math.Abs(h - v.YaziYuksekligi) > 1e-9) degisen.Add(ad);
            }
            _uyari.Text = degisen.Count == 0 ? "" :
                "Uyarı: yazı yüksekliği değişti (" + string.Join(", ", degisen) +
                "). Komut yerleşimleri 10–12 cm yazıya göre ayarlı; yazılar çakışabilir.";
        }

        // ------------------------------------------------------------------ renk

        private void KatmanRenkSec(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = _katman.Columns[e.ColumnIndex].Name;
            if (col != "Renk" && col != "Ornek") return;
            DataGridViewRow r = _katman.Rows[e.RowIndex];
            var k = new KatmanAyar();
            try { RenkCoz(Str(r, "Renk"), k, ""); } catch (FormatException) { k.Aci = 7; k.Rgb = null; }
            _katman.EndEdit();
            var dlg = new Autodesk.AutoCAD.Windows.ColorDialog
            {
                Color = BeykentStilUygula.Renk(k), IncludeByBlockByLayer = false
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            AcColor c = dlg.Color;
            r.Cells["Renk"].Value = c.ColorMethod == AcColorMethod.ByAci
                ? c.ColorIndex.ToString(CultureInfo.InvariantCulture)
                : c.Red + "," + c.Green + "," + c.Blue;
            RenkOrnek(r);
        }

        private static void RenkOrnek(DataGridViewRow r)
        {
            var k = new KatmanAyar();
            Color renk = Beykent.Monolit.StandardFormTheme.InputBg;
            try
            {
                RenkCoz(Str(r, "Renk"), k, "");
                renk = BeykentStilUygula.Renk(k).ColorValue;
            }
            catch { }
            DataGridViewCell cell = r.Cells["Ornek"];
            cell.Style.BackColor = renk;
            cell.Style.SelectionBackColor = renk;
        }

        private static string RenkMetni(KatmanAyar k) =>
            k.GercekRenk ? k.Rgb.Replace(" ", "") : k.Aci.ToString(CultureInfo.InvariantCulture);

        private static void RenkCoz(string s, KatmanAyar k, string ad)
        {
            s = (s ?? "").Trim();
            if (s.Contains(","))
            {
                var t = new KatmanAyar { Rgb = s };
                if (!t.TryRgb(out byte r, out byte g, out byte b)) throw new FormatException(ad + ": renk R,G,B (0-255) olmalı.");
                k.Rgb = r + "," + g + "," + b;
                k.Aci = 7;
                return;
            }
            if (!short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out short aci) || aci < 1 || aci > 255)
                throw new FormatException(ad + ": renk 1-255 arası ACI ya da R,G,B olmalı.");
            k.Aci = aci;
            k.Rgb = null;
        }

        // ------------------------------------------------------------------ yardımcılar

        private static string KalinlikMetni(int lw) =>
            lw < 0 ? "Varsayılan" : (lw / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

        private static int KalinlikCoz(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "Varsayılan") return -3;
            return TrySayi(s, out double mm) ? (int)Math.Round(mm * 100) : 20;
        }

        private static string S(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Str(DataGridViewRow r, string col) => Convert.ToString(r.Cells[col].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";

        private static bool Bool(DataGridViewRow r, string col) => r.Cells[col].Value is bool b && b;

        private static bool TrySayi(string s, out double v) =>
            double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static double Sayi(DataGridViewRow r, string col, double min, double max, string ad)
        {
            string s = Str(r, col);
            if (!TrySayi(s, out double v) || v < min || v > max)
                throw new FormatException(ad + " / " + r.DataGridView.Columns[col].HeaderText + ": '" + s + "' geçersiz (" +
                    S(min) + " – " + S(max) + ").");
            return v;
        }
    }
}
