using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DwgVersion = Autodesk.AutoCAD.DatabaseServices.DwgVersion;
using Beykent.Monolit;
using static ST4Yardimci.MimariStaAnaliz;

namespace ST4Yardimci
{
    /// <summary>MIMARISTA: patlatma sonrası katmanların STA4 sınıfı (Aks/Kolon/Duvar/Merdiven/Sil), çizim birimi ve seçenekler.</summary>
    internal sealed class MimariStaForm : MonolitForm
    {
        private static readonly string[] SinifAd = { "Diğer (kalsın)", "Aks", "Kolon", "Duvar", "Merdiven", "Kapı", "Pencere", "Ölçü (kalsın)", "Sil" };
        private static readonly Sinif[] SinifSira = { Sinif.Diger, Sinif.Aks, Sinif.Kolon, Sinif.Duvar, Sinif.Merdiven, Sinif.Kapi, Sinif.Pencere, Sinif.Olcu, Sinif.Sil };
        private static readonly double[] BirimDeger = { 1, 10, 0.01 };
        // STA4 DWG okuyucusu eski (VectorDraw 2004); STA örnekleri AutoCAD 2000. Yeni sürümleri STA açamayabilir.
        private static readonly DwgVersion[] SurumDeger = { DwgVersion.AC1015, DwgVersion.AC1024, DwgVersion.AC1027, DwgVersion.AC1032 };

        private readonly DataGridView tablo;
        private readonly ComboBox birim, surum;
        private readonly CheckBox balon, basamak, olcuPatlat, olcuEkle, tamamla, olcuYok, beykent, kapama;
        private readonly TextBox hedef;
        private readonly Label ozet;
        private readonly MimariStaRunner.Ayar ayar;

        internal string Hedef => hedef.Text.Trim();

        internal MimariStaForm(List<MimariStaRunner.KatmanOzet> katmanlar, MimariStaRunner.Ayar ayar, string hedefYol)
        {
            this.ayar = ayar;
            Text = "Mimari → STA4CAD hazırlığı";
            ClientSize = new Size(640, 688);
            FormBorderStyle = FormBorderStyle.Sizable; MinimumSize = new Size(560, 520);
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = StandardFormTheme.Bg; ForeColor = StandardFormTheme.Text; Font = new Font("Segoe UI", 9f);

            var aciklama = new Label
            {
                Location = new Point(14, 10), Size = new Size(612, 50), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "Bloklar patlatıldı. Her katmanın STA4'teki karşılığını seçin: seçilenler AKS, KOLON, DUVAR, MERDIVEN, KAPI ve PENCERE katmanlarına toplanır. " +
                       "\"Sil\" seçilen katmanlar yeni dosyaya alınmaz. Açık çiziminiz değişmez."
            };
            Controls.Add(aciklama);

            tablo = new DataGridView
            {
                Location = new Point(14, 64), Size = new Size(612, 300), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EditMode = DataGridViewEditMode.EditOnEnter,
                RowTemplate = { Height = 22 },                 BackgroundColor = StandardFormTheme.InputBg, BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Katman", ReadOnly = true, FillWeight = 55 });
            tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nesne", ReadOnly = true, FillWeight = 15 });
            var sinifKolon = new DataGridViewComboBoxColumn { HeaderText = "STA4 karşılığı", FillWeight = 30, FlatStyle = FlatStyle.Flat };
            sinifKolon.Items.AddRange(SinifAd);
            tablo.Columns.Add(sinifKolon);
            // Tanınan katmanlar üstte, sonra nesne sayısına göre.
            foreach (var k in katmanlar.OrderBy(k => k.Oneri == Sinif.Diger ? 2 : k.Oneri == Sinif.Sil ? 1 : 0).ThenByDescending(k => k.Adet))
            {
                int i = tablo.Rows.Add(k.Ad, k.Adet, SinifAd[Array.IndexOf(SinifSira, ayar.Katman[k.Ad])]);
                tablo.Rows[i].Tag = k.Ad;
            }
            tablo.CurrentCellDirtyStateChanged += (s, e) => { if (tablo.IsCurrentCellDirty) tablo.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            tablo.CellValueChanged += (s, e) => OzetYaz();
            Controls.Add(tablo);

            // Toplu seçim: tanınmayan (Diğer) katmanlar tek tıkla silinir ya da korunur; seçili satırlara sınıf atanır.
            var digerSil = new Button { Text = "Diğerlerini sil", Location = new Point(14, 368), Size = new Size(110, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var digerKoru = new Button { Text = "Silinenleri koru", Location = new Point(130, 368), Size = new Size(110, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            digerSil.Click += (s, e) => TopluDegistir(Sinif.Diger, Sinif.Sil);
            digerKoru.Click += (s, e) => TopluDegistir(Sinif.Sil, Sinif.Diger);
            Controls.Add(digerSil); Controls.Add(digerKoru);
            Controls.Add(new Label { Text = "Seçili satırlar:", Location = new Point(300, 373), Size = new Size(90, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Right });
            var secili = new ComboBox { Location = new Point(392, 369), Width = 234, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            secili.Items.AddRange(SinifAd);
            secili.SelectionChangeCommitted += (s, e) =>
            {
                tablo.EndEdit();
                foreach (DataGridViewRow r in tablo.SelectedRows) r.Cells[2].Value = SinifAd[secili.SelectedIndex];
                OzetYaz();
            };
            Controls.Add(secili);

            ozet = new Label { Location = new Point(14, 398), Size = new Size(612, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(ozet);

            int y = 424;
            Controls.Add(new Label { Text = "Çizim birimi", Location = new Point(14, y + 4), Size = new Size(90, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
            birim = new ComboBox { Location = new Point(108, y), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            birim.Items.AddRange(new object[] { "cm", "mm", "m" });
            birim.SelectedIndex = ayar.Birim >= 5 ? 1 : ayar.Birim <= 0.05 ? 2 : 0;
            Controls.Add(birim);
            Controls.Add(new Label { Text = "DWG sürümü", Location = new Point(214, y + 4), Size = new Size(84, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
            surum = new ComboBox { Location = new Point(300, y), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            surum.Items.AddRange(new object[] { "2000 (STA)", "2010", "2013", "2018" });
            surum.SelectedIndex = Math.Max(0, Array.IndexOf(SurumDeger, ayar.Surum));
            Controls.Add(surum);

            balon = Onay("Balonu olmayan aks ucuna balon ekle", 14, y + 32, ayar.BalonEkle);
            basamak = Onay("Numarasız merdivene basamak numarası yaz", 320, y + 32, ayar.BasamakNo);
            olcuPatlat = Onay("Ölçüleri çizgi + yazıya patlat (STA örneği gibi)", 14, y + 56, ayar.OlcuPatlat);
            olcuEkle = Onay("Hiç ölçü yoksa aks arası ölçü ekle", 320, y + 56, ayar.OlcuEkle);
            tamamla = Onay("Kolondan geçmeyen akslara aks ekle (2')", 14, y + 80, ayar.AksTamamla);
            tamamla.Width = 300;
            olcuYok = Onay("Hiç ölçü olmasın (STA ölçek bulamayabilir)", 320, y + 80, ayar.OlcuYok);
            beykent = Onay("BEYKENT katman adları", 420, y + 2, !ayar.StaKatman);
            beykent.Width = 206;
            kapama = Onay("Kapama perdelerini ayıkla (ince uzun perde: kolon sayma, aks atma)", 14, y + 104, ayar.KapamaAyikla);
            kapama.Width = 560;
            olcuYok.CheckedChanged += (s, e) => { olcuPatlat.Enabled = olcuEkle.Enabled = !olcuYok.Checked; };
            olcuPatlat.Enabled = olcuEkle.Enabled = !ayar.OlcuYok;

            Controls.Add(new Label { Text = "Yeni dosya", Location = new Point(14, y + 140), Size = new Size(90, 20), Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
            hedef = new TextBox
            {
                Location = new Point(108, y + 137), Width = 424, Text = hedefYol, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = StandardFormTheme.InputBg, ForeColor = StandardFormTheme.Text
            };
            Controls.Add(hedef);
            var gozat = new Button { Text = "Seç…", Location = new Point(540, y + 135), Size = new Size(86, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            gozat.Click += (s, e) =>
            {
                using (var d = new SaveFileDialog { Title = "STA4CAD için hazırlanan DWG", Filter = "AutoCAD çizimi (*.dwg)|*.dwg", FileName = hedef.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) hedef.Text = d.FileName;
            };
            Controls.Add(gozat);

            var tamam = new Button { Text = "Hazırla", Location = new Point(424, 648), Size = new Size(110, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var iptal = new Button { Text = "İptal", Location = new Point(540, 648), Size = new Size(86, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            var aksKolon = new Button { Text = "Yalnız aks ve kolon çiz", Location = new Point(14, 648), Size = new Size(170, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            new ToolTip().SetToolTip(aksKolon, "Yeni dosyada yalnız akslar (balon ve adlarıyla) ve kolonlar kalır; duvar, merdiven, kapı/pencere ve ölçü alınmaz.");
            tamam.Click += (s, e) => Tamam(false);
            aksKolon.Click += (s, e) => Tamam(true);
            Controls.Add(tamam); Controls.Add(iptal); Controls.Add(aksKolon);
            AcceptButton = tamam; CancelButton = iptal;
            OzetYaz();
        }

        private CheckBox Onay(string metin, int x, int y, bool deger)
        {
            var c = new CheckBox { Text = metin, Location = new Point(x, y), Size = new Size(300, 22), Checked = deger, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Controls.Add(c);
            return c;
        }

        private Sinif SatirSinifi(DataGridViewRow r) => SinifSira[Math.Max(0, Array.IndexOf(SinifAd, Convert.ToString(r.Cells[2].Value)))];

        private void TopluDegistir(Sinif eski, Sinif yeni)
        {
            tablo.EndEdit();
            foreach (DataGridViewRow r in tablo.Rows)
                if (SatirSinifi(r) == eski) r.Cells[2].Value = SinifAd[Array.IndexOf(SinifSira, yeni)];
            OzetYaz();
        }

        private void OzetYaz()
        {
            var say = new Dictionary<Sinif, int>();
            foreach (DataGridViewRow r in tablo.Rows) { Sinif s = SatirSinifi(r); say[s] = say.TryGetValue(s, out int c) ? c + 1 : 1; }
            Func<Sinif, int> n = s => say.TryGetValue(s, out int c) ? c : 0;
            var eksik = new List<string>();
            if (n(Sinif.Aks) == 0) eksik.Add("aks");
            if (n(Sinif.Kolon) == 0) eksik.Add("kolon");
            if (n(Sinif.Duvar) == 0) eksik.Add("duvar");
            if (n(Sinif.Merdiven) == 0) eksik.Add("merdiven");
            ozet.Text = string.Format("Aks {0} · Kolon {1} · Duvar {2} · Merdiven {3} · Sil {4} katman", n(Sinif.Aks), n(Sinif.Kolon), n(Sinif.Duvar), n(Sinif.Merdiven), n(Sinif.Sil))
                + (eksik.Count > 0 ? "   —   seçilmedi: " + string.Join(", ", eksik) : "");
            ozet.ForeColor = eksik.Count > 0 ? Color.FromArgb(176, 96, 0) : StandardFormTheme.Text;
        }

        private void Tamam(bool yalnizAksKolon)
        {
            ayar.YalnizAksKolon = yalnizAksKolon;
            tablo.EndEdit();
            string yol = Hedef;
            if (yol.Length == 0 || !System.IO.Path.IsPathRooted(yol) || !System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(yol)))
            {
                MessageBox.Show(this, "Yeni dosya için geçerli bir klasör ve dosya adı girin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!yol.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) hedef.Text = yol + ".dwg";
            foreach (DataGridViewRow r in tablo.Rows) ayar.Katman[(string)r.Tag] = SatirSinifi(r);
            ayar.Birim = BirimDeger[birim.SelectedIndex];
            ayar.Surum = SurumDeger[surum.SelectedIndex];
            ayar.BalonEkle = balon.Checked; ayar.BasamakNo = basamak.Checked; ayar.OlcuPatlat = olcuPatlat.Checked; ayar.OlcuEkle = olcuEkle.Checked; ayar.AksTamamla = tamamla.Checked; ayar.OlcuYok = olcuYok.Checked; ayar.StaKatman = !beykent.Checked; ayar.KapamaAyikla = kapama.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
