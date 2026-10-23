using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal sealed class RadyeOtomatikForm : Beykent.Monolit.MonolitForm
    {
        private readonly TextBox project;
        private readonly Label details;
        private readonly Button draw;
        private readonly CheckBox reference;
        internal string ProjectPath => project.Text.Trim();
        internal bool IncludeReference => reference.Checked;

        internal RadyeOtomatikForm()
        {
            Text = "Radye Temel Planı Otomatik";
            ClientSize = new Size(610, 360); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg; ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            Controls.Add(new Label { Text = "Mevcut hesap dosyalarından As haritası ve otomatik ilave karşılaştırması.\nIsı alanının sınır kutusuna mevcut radye bant kuralları uygulanır.", Location = new Point(16, 15), Size = new Size(578, 48) });
            project = new TextBox { Location = new Point(16, 77), Width = 475, ReadOnly = true, BackColor = Beykent.Monolit.StandardFormTheme.InputBg, ForeColor = Beykent.Monolit.StandardFormTheme.Text };
            Controls.Add(project);
            var browse = new Button { Text = "ST4 seç…", Location = new Point(500, 75), Size = new Size(94, 28) };
            browse.Click += Browse; Controls.Add(browse);
            details = new Label { Location = new Point(16, 119), Size = new Size(578, 130), Text = "ST4 projesini seçin. Aynı klasörde GPR, RAD, SO1, MATBQF ve _MatTem.dwg bulunmalı. PDF/DXF gerekmez.\nBu karşılaştırma sürümü: 40 cm radye, sıfır donatı açısı." };
            Controls.Add(details);
            reference = new CheckBox { Location = new Point(16, 265), Size = new Size(578, 25), Text = "STA referansını da yanına yerleştir", Checked = true };
            Controls.Add(reference);
            draw = new Button { Text = "Planları yerleştir", Location = new Point(330, 308), Size = new Size(150, 32), Enabled = false };
            draw.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); }; Controls.Add(draw);
            var cancel = new Button { Text = "Kapat", Location = new Point(495, 308), Size = new Size(99, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(cancel); CancelButton = cancel; AcceptButton = draw;
            foreach (Button button in new[] { browse, draw, cancel })
            {
                button.FlatStyle = FlatStyle.Flat; button.BackColor = Beykent.Monolit.StandardFormTheme.BgAlt;
                button.ForeColor = Beykent.Monolit.StandardFormTheme.Text; button.FlatAppearance.BorderColor = Beykent.Monolit.StandardFormTheme.InputBorder;
            }

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(460);
            ui.Section("STA4CAD projesi").Field("Proje dosyası", project).Actions(browse);
            ui.Section("Kaynak durumu").Control(details).Choices(reference);
            ui.Footer(draw, cancel);
            ui.Finish();
        }
        private void Browse(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog { Title = "Radye STA4CAD projesi", Filter = "STA4CAD proje (*.st4)|*.st4", CheckFileExists = true })
            {
                string docPath = AcApp.DocumentManager.MdiActiveDocument?.Name;
                if (!string.IsNullOrEmpty(docPath) && Path.IsPathRooted(docPath)) dialog.InitialDirectory = Path.GetDirectoryName(docPath);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                project.Text = dialog.FileName; draw.Enabled = false;
                try
                {
                    using (var source = RadyeOtomatikSource.Open(dialog.FileName))
                    {
                        var heat = RadyeRecordedHeatmap.Read(source);
                        details.Text = source.PlateCount + " radye plak, " + source.NodeCount + " düğüm, " + source.ElementCount + " eleman.\n" +
                            heat.Jobs.Count + " As alanı, STA çap/aralık seçimiyle eşleşti. GPR/SO1 tepe As kontrolü geçti.\n\n" + string.Join("\n", source.Bars);
                        draw.Enabled = true;
                    }
                }
                catch (System.Exception ex) { details.Text = ex.Message; }
            }
        }
    }
}
