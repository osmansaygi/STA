using System;
using System.Drawing;
using System.Windows.Forms;
using Beykent.Monolit.Girdi;
using SdFont = System.Drawing.Font;

namespace ST4Yardimci
{
    /// <summary>Konsol boyu → çap/aralık ayar formu (4 satır, kalıcı).</summary>
    internal sealed class DosemeKonsolAyarForm : Beykent.Monolit.MonolitForm
    {
        private readonly OlcuKutusu[] _numBoy = new OlcuKutusu[DosemeKonsolSettings.BandCount];
        private readonly DonatiKutusu[] _donati = new DonatiKutusu[DosemeKonsolSettings.BandCount];
        private readonly GirdiDenetci Denetci = new GirdiDenetci();
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
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Color.White;
            Font = new SdFont("Segoe UI", 9f);
            KeyPreview = true;

            DosemeKonsolSettings.EnsureLoaded();
            var bands = DosemeKonsolSettings.GetBandsCopy();

            for (int i = 0; i < DosemeKonsolSettings.BandCount; i++)
            {
                int no = i + 1;
                // Sınırlar eski NumericUpDown ile aynı: boy 1–20000 cm, çap ø8–ø20, aralık 5–50 cm (tam sayı).
                _numBoy[i] = Denetci.Ekle(new OlcuKutusu(no + ". kademe azami boy", "cm", 1, 20000,
                    Math.Max(1, Math.Min(20000, Math.Round(bands[i].MaxBoyCm)))));
                int cap = Array.IndexOf(CapChoices, bands[i].CapMm) >= 0 ? bands[i].CapMm : CapChoices[0];
                _donati[i] = Denetci.Ekle(new DonatiKutusu(no + ". kademe donatı", cap,
                    Math.Max(5, Math.Min(50, bands[i].AralikCm)), CapChoices[0], CapChoices[CapChoices.Length - 1], 5, 50));
                var d = _donati[i];
                Denetci.HataKurali(() => Math.Abs(d.AralikCm - Math.Round(d.AralikCm)) > 1e-9
                    ? d.Ad + ": aralık tam sayı cm olmalı" : null, d);
            }

            var btnOk = MakeButton("Kaydet");
            btnOk.Click += (s, e) => { SaveAndClose(); };
            var btnCancel = MakeButton("İptal");
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = btnCancel;

            var ui = BeginCompact(350);
            var sec = ui.Section("Konsol donatı kademeleri  (azami boy → ø çap / aralık)");
            for (int i = 0; i < DosemeKonsolSettings.BandCount; i++)
                sec.Field((i + 1) + ". kademe", _numBoy[i], _donati[i]);
            sec.Hint("Kayıt otomatik hatırlanır.");
            ui.Section(null).Control(Denetci.Serit, 44);
            ui.Footer(btnOk, btnCancel);
            ui.Finish();
            Denetci.Guncelle();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if ((keyData == Keys.Enter || keyData == Keys.Return) && !(ActiveControl is Button)) { SaveAndClose(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveAndClose()
        {
            if (!Denetci.Onayla()) return;
            var bands = new DosemeKonsolSettings.Band[DosemeKonsolSettings.BandCount];
            for (int i = 0; i < DosemeKonsolSettings.BandCount; i++)
            {
                bands[i] = new DosemeKonsolSettings.Band
                {
                    MaxBoyCm = _numBoy[i].Deger,
                    CapMm = _donati[i].CapMm,
                    AralikCm = (int)Math.Round(_donati[i].AralikCm)
                };
            }
            DosemeKonsolSettings.SetBands(bands);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }
    }
}
