using System;
using System.Drawing;
using System.Windows.Forms;

namespace Beykent.Monolit.Girdi
{
    /// <summary>Denetçinin izlediği girdi.</summary>
    internal interface IGirdi
    {
        string Ad { get; }
        bool Gecerli { get; }
        string Hata { get; }
        Control Kontrol { get; }
        event EventHandler DegerDegisti;
        void VarsayilanaDon();
        void DisHata(string mesaj);
    }

    /// <summary>
    /// Kendi çerçevesini çizen tek satır metin kutusu + sağda bilgi etiketi (birim / hesap).
    /// MonolitForm.ApplyStandard renk ve kenarlık değiştirse de görünüm korunur.
    /// </summary>
    internal abstract class GirdiKutusu : UserControl, IGirdi
    {
        protected readonly TextBox Metin;
        protected readonly Label Bilgi;
        private string _icHata, _disHata;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Ad { get; set; }
        public bool Gecerli => _icHata == null && _disHata == null;
        public string Hata => _icHata ?? _disHata;
        public Control Kontrol => this;
        public event EventHandler DegerDegisti;

        protected GirdiKutusu()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Padding = new Padding(5, 4, 4, 2);
            Height = 24;
            MinimumSize = new Size(0, 24);
            Metin = new TextBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = GirdiTema.Zemin };
            Bilgi = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = GirdiTema.Soluk,
                BackColor = GirdiTema.Zemin, TextAlign = ContentAlignment.TopRight, Padding = new Padding(2, 0, 0, 0) };
            Metin.BorderStyleChanged += (s, e) => { if (Metin.BorderStyle != BorderStyle.None) Metin.BorderStyle = BorderStyle.None; };
            Metin.BackColorChanged += (s, e) => { if (Metin.BackColor != ArkaRenk) Metin.BackColor = ArkaRenk; };
            Bilgi.BackColorChanged += (s, e) => { if (Bilgi.BackColor != ArkaRenk) Bilgi.BackColor = ArkaRenk; };
            Bilgi.ForeColorChanged += (s, e) => { if (Bilgi.ForeColor != GirdiTema.Soluk) Bilgi.ForeColor = GirdiTema.Soluk; };
            Metin.TextChanged += (s, e) => { Dogrula(); DegerDegisti?.Invoke(this, EventArgs.Empty); };
            Metin.GotFocus += (s, e) => { Invalidate(); BeginInvoke(new Action(Metin.SelectAll)); };
            Metin.LostFocus += (s, e) => { if (Gecerli) Bicimle(); Invalidate(); };
            Metin.KeyDown += OnTus;
            Controls.Add(Metin);
            Controls.Add(Bilgi);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Metin.Focus();
        }

        /// <summary>Metnin solunda düzenlenemeyen önek (ör. çap işareti); kullanıcı yazmak zorunda kalmaz.</summary>
        protected Label OnekEkle(string onek)
        {
            var l = new Label { Text = onek, Dock = DockStyle.Left, AutoSize = true, BackColor = GirdiTema.Zemin,
                ForeColor = SystemColors.ControlText, Padding = new Padding(0, 0, 1, 0), Margin = Padding.Empty };
            l.BackColorChanged += (s, e) => { if (l.BackColor != ArkaRenk) l.BackColor = ArkaRenk; };
            l.Click += (s, e) => Metin.Focus();
            Controls.Add(l);
            _onek = l;
            return l;
        }

        private Label _onek;
        private Color ArkaRenk => _icHata != null || _disHata != null ? GirdiTema.HataZemin : GirdiTema.Zemin;

        public override string Text { get => Metin.Text; set => Metin.Text = value; }

        protected abstract string Coz(string text);
        protected abstract void Bicimle();
        public abstract void VarsayilanaDon();
        protected virtual void Adim(int yon) { }

        private void OnTus(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                Adim(e.KeyCode == Keys.Up ? 1 : -1);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected void Dogrula()
        {
            _icHata = Coz(Metin.Text);
            Boya();
        }

        public void DisHata(string mesaj)
        {
            if (_disHata == mesaj) return;
            _disHata = mesaj;
            Boya();
        }

        private void Boya()
        {
            Metin.BackColor = ArkaRenk; Bilgi.BackColor = ArkaRenk;
            if (_onek != null) _onek.BackColor = ArkaRenk;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var r = ClientRectangle; r.Width--; r.Height--;
            using (var b = new SolidBrush(ArkaRenk)) e.Graphics.FillRectangle(b, ClientRectangle);
            Color c = !Gecerli ? GirdiTema.Hata : Metin.Focused ? GirdiTema.Odak : GirdiTema.Cerceve;
            using (var p = new Pen(c)) e.Graphics.DrawRectangle(p, r);
            if (Metin.Focused || !Gecerli)
                using (var p = new Pen(c)) e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Metin.Enabled = Enabled;
            Invalidate();
        }
    }
}
