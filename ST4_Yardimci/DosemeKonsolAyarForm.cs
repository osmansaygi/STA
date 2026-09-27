using System;
using System.Drawing;
using System.Windows.Forms;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Konsol boyu → çap/aralık ayar formu (4 satır, kalıcı).</summary>
    internal sealed class DosemeKonsolAyarForm : Form
    {
        private readonly NumericUpDown[] _numBoy = new NumericUpDown[DosemeKonsolSettings.BandCount];
        private readonly ComboBox[] _cmbCap = new ComboBox[DosemeKonsolSettings.BandCount];
        private readonly NumericUpDown[] _numAralik = new NumericUpDown[DosemeKonsolSettings.BandCount];
        private static readonly int[] CapChoices = { 8, 10, 12, 14, 16, 18, 20 };

        public DosemeKonsolAyarForm()
        {
            Text = "Konsol donatı ayarları";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, 250);
            BackColor = AcadPaletteTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);

            DosemeKonsolSettings.EnsureLoaded();
            var bands = DosemeKonsolSettings.GetBandsCopy();

            int y = 12;
            Controls.Add(MakeLabel("Konsol boyu (cm, azami) → çap / aralık. Kayıt otomatik hatırlanır.", 12, y));
            y += 28;

            Controls.Add(MakeLabel("Azami boy", 12, y));
            Controls.Add(MakeLabel("Çap ø", 130, y));
            Controls.Add(MakeLabel("Aralık cm", 250, y));
            y += 22;

            for (int i = 0; i < DosemeKonsolSettings.BandCount; i++)
            {
                _numBoy[i] = MakeNum(12, y, 100, 1, 20000, 1, (decimal)bands[i].MaxBoyCm);
                _cmbCap[i] = MakeCapCombo(130, y, bands[i].CapMm);
                _numAralik[i] = MakeNum(250, y, 80, 5, 50, 1, bands[i].AralikCm);
                Controls.Add(_numBoy[i]);
                Controls.Add(_cmbCap[i]);
                Controls.Add(_numAralik[i]);
                y += 32;
            }

            var btnOk = MakeButton("Kaydet", 200, y + 8, 100);
            btnOk.Click += (s, e) => { SaveAndClose(); };
            var btnCancel = MakeButton("İptal", 310, y + 8, 90);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void SaveAndClose()
        {
            var bands = new DosemeKonsolSettings.Band[DosemeKonsolSettings.BandCount];
            for (int i = 0; i < DosemeKonsolSettings.BandCount; i++)
            {
                int cap = CapChoices[0];
                if (_cmbCap[i].SelectedItem is int ci) cap = ci;
                else if (_cmbCap[i].SelectedItem != null)
                    int.TryParse(_cmbCap[i].SelectedItem.ToString(), out cap);
                bands[i] = new DosemeKonsolSettings.Band
                {
                    MaxBoyCm = (double)_numBoy[i].Value,
                    CapMm = cap,
                    AralikCm = (int)_numAralik[i].Value
                };
            }
            DosemeKonsolSettings.SetBands(bands);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };

        private static NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal inc, decimal val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 24),
                Minimum = min,
                Maximum = max,
                Increment = inc,
                DecimalPlaces = inc < 1 ? 2 : 0,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White
            };
            if (val < min) val = min;
            if (val > max) val = max;
            n.Value = val;
            YardimciUi.StripUpDownButtons(n);
            return n;
        }

        private static ComboBox MakeCapCombo(int x, int y, int selected)
        {
            var c = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(90, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int v in CapChoices)
                c.Items.Add(v);
            int ix = Array.IndexOf(CapChoices, selected);
            c.SelectedIndex = ix >= 0 ? ix : 0;
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = AcadPaletteTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }
    }
}
