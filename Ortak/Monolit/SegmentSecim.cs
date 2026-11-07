using System;
using System.Drawing;
using System.Windows.Forms;

namespace Beykent.Monolit.Girdi
{
    /// <summary>
    /// Birbirini dışlayan 2–5 seçenek için tek satır segmentli düğme (radyo grubunun kompakt hali).
    /// Sol/sağ ok tuşu ve tıklama ile seçilir; kendi renklerini çizer.
    /// </summary>
    internal sealed class SegmentSecim : Control
    {
        private readonly string[] _secenekler;
        private int _secili, _uzerinde = -1;
        public event EventHandler SeciliDegisti;

        public SegmentSecim(params string[] secenekler)
        {
            _secenekler = secenekler;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            Height = 26;
            MinimumSize = new Size(0, 26);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Secili
        {
            get => _secili;
            set
            {
                int v = Math.Max(0, Math.Min(_secenekler.Length - 1, value));
                if (v == _secili) return;
                _secili = v; Invalidate();
                SeciliDegisti?.Invoke(this, EventArgs.Empty);
            }
        }

        public string SeciliMetin => _secenekler[_secili];

        private Rectangle Dilim(int i)
        {
            int w = (Width - 1) / _secenekler.Length;
            int x = i * w;
            int r = i == _secenekler.Length - 1 ? Width - 1 : x + w;
            return new Rectangle(x, 0, r - x, Height - 1);
        }

        private int Bul(Point p)
        {
            for (int i = 0; i < _secenekler.Length; i++) if (Dilim(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            for (int i = 0; i < _secenekler.Length; i++)
            {
                var r = Dilim(i);
                bool sec = i == _secili;
                Color zemin = sec ? GirdiTema.Secili : i == _uzerinde ? Color.FromArgb(229, 241, 251) : GirdiTema.Zemin;
                using (var b = new SolidBrush(zemin)) g.FillRectangle(b, r);
                using (var p = new Pen(GirdiTema.Cerceve)) g.DrawRectangle(p, r);
                TextRenderer.DrawText(g, _secenekler[i], Font, r, sec ? Color.FromArgb(0, 70, 140) : SystemColors.ControlText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            var s = Dilim(_secili);
            using (var p = new Pen(GirdiTema.SeciliCerceve)) g.DrawRectangle(p, s);
            if (Focused)
            {
                var f = s; f.Inflate(-3, -3);
                ControlPaint.DrawFocusRectangle(g, f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); int i = Bul(e.Location); if (i >= 0) Secili = i; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = Bul(e.Location); if (i != _uzerinde) { _uzerinde = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Secili--;
            else if (e.KeyCode == Keys.Right) Secili++;
        }
    }
}
