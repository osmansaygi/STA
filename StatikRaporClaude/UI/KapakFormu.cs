using System.Drawing;
using StatikRaporClaude.Rapor;

namespace StatikRaporClaude.UI
{
    /// <summary>Kapak bilgileri penceresi: alanlar gruplu, değerler KapakBilgisi'ne yazılır.</summary>
    public sealed class KapakFormu : Form
    {
        private readonly KapakBilgisi _k;
        private readonly Dictionary<string, TextBox> _kutular = new Dictionary<string, TextBox>();

        public KapakFormu(KapakBilgisi k)
        {
            _k = k;
            Text = "Kapak bilgileri";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 244, 238);
            ClientSize = new Size(560, 640);

            var tablo = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(16, 12, 16, 8)
            };
            tablo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            tablo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            string grup = null;
            foreach (var (g, etiket, alan) in KapakBilgisi.Alanlar)
            {
                if (g != grup)
                {
                    grup = g;
                    var gl = new Label
                    {
                        Text = g.ToUpper(new System.Globalization.CultureInfo("tr-TR")), AutoSize = true,
                        ForeColor = Color.FromArgb(38, 99, 184), Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 10, 0, 2)
                    };
                    tablo.Controls.Add(gl);
                    tablo.SetColumnSpan(gl, 2);
                }
                tablo.Controls.Add(new Label { Text = etiket, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) });
                var t = new TextBox { Text = k.Al(alan), Dock = DockStyle.Fill };
                _kutular[alan] = t;
                tablo.Controls.Add(t);
            }

            var alt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            var tamam = new Button { Text = "Kaydet", Width = 100, Height = 30, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(38, 99, 184), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            tamam.FlatAppearance.BorderSize = 0;
            var iptal = new Button { Text = "Vazgeç", Width = 90, Height = 30, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            alt.Controls.AddRange(new Control[] { tamam, iptal });
            AcceptButton = tamam;
            CancelButton = iptal;
            Controls.Add(tablo);
            Controls.Add(alt);
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK) foreach (var (alan, t) in _kutular) _k.Ver(alan, t.Text.Trim());
            };
        }
    }
}
