using System;
using System.Drawing;
using System.Windows.Forms;

namespace ST4Yardimci
{
    /// <summary>ST4_Yardimci form ortak UI yardımcıları.</summary>
    internal static class YardimciUi
    {
        /// <summary>
        /// NumericUpDown yükselt/düşür oklarını tamamen kaldırır;
        /// metin alanını kutunun tamamına yayar (beyaz dikdörtgen kalmaz).
        /// </summary>
        public static void StripUpDownButtons(NumericUpDown n)
        {
            if (n == null) return;
            try
            {
                Control buttons = null;
                Control edit = null;
                foreach (Control c in n.Controls)
                {
                    string name = c.GetType().Name;
                    if (name == "UpDownButtons")
                        buttons = c;
                    else if (name == "UpDownEdit" || c is TextBox)
                        edit = c;
                }
                if (buttons == null && n.Controls.Count > 0)
                    buttons = n.Controls[0];
                if (edit == null && n.Controls.Count > 1)
                    edit = n.Controls[1];

                if (buttons != null)
                {
                    buttons.Visible = false;
                    buttons.Enabled = false;
                    buttons.Size = Size.Empty;
                }

                void FitEdit()
                {
                    if (edit == null || edit.IsDisposed) return;
                    edit.Dock = DockStyle.Fill;
                    edit.Bounds = new Rectangle(0, 0, n.ClientSize.Width, n.ClientSize.Height);
                }

                FitEdit();
                n.Resize -= OnNumResize;
                n.Resize += OnNumResize;

                void OnNumResize(object sender, EventArgs e) => FitEdit();
            }
            catch { }
        }
    }
}
