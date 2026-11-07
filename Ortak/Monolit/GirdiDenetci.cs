using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Beykent.Monolit.Girdi
{
    /// <summary>
    /// Formun girdi durumunu tek yerde toplar:
    /// hata (kırmızı, çizimi engeller), mühendislik uyarısı (sarı, engellemez), her şey uygunsa yeşil.
    /// MessageBox yerine formun altındaki <see cref="Serit"/> kullanılır.
    /// </summary>
    internal sealed class GirdiDenetci
    {
        private sealed class Kural
        {
            internal Func<string> Mesaj;
            internal IGirdi[] Alanlar;
        }

        private readonly List<IGirdi> _alanlar = new List<IGirdi>();
        private readonly List<Kural> _hatalar = new List<Kural>();
        private readonly List<Func<string>> _uyarilar = new List<Func<string>>();
        private readonly Label _mesaj;
        public TableLayoutPanel Serit { get; }
        public Button VarsayilanDugmesi { get; }

        public GirdiDenetci()
        {
            // Sabit iki satırlık alan: mesaj değişince form boyu zıplamaz.
            _mesaj = new Label { AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(6, 0, 4, 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            VarsayilanDugmesi = new Button { Text = "Varsayılana dön", AutoSize = true, Margin = new Padding(0, 2, 0, 2),
                Anchor = AnchorStyles.Right };
            VarsayilanDugmesi.Click += (s, e) => { foreach (var a in _alanlar) a.VarsayilanaDon(); Guncelle(); };
            Serit = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            Serit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Serit.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Serit.Controls.Add(_mesaj, 0, 0);
            Serit.Controls.Add(VarsayilanDugmesi, 1, 0);
            Serit.Paint += SeritCiz;
            _mesaj.BackColorChanged += (s, e) => { if (_mesaj.BackColor != Color.Transparent) _mesaj.BackColor = Color.Transparent; };
        }

        public T Ekle<T>(T alan) where T : IGirdi
        {
            _alanlar.Add(alan);
            alan.DegerDegisti += (s, e) => Guncelle();
            return alan;
        }

        public void SegmentIzle(SegmentSecim s) => s.SeciliDegisti += (o, e) => Guncelle();

        /// <summary>Alanlar arası zorunlu kural; mesaj null ise sağlanmış. İlgili alanlar kırmızı çerçevelenir.</summary>
        public void HataKurali(Func<string> mesaj, params IGirdi[] alanlar) => _hatalar.Add(new Kural { Mesaj = mesaj, Alanlar = alanlar });

        /// <summary>Engellemeyen hatırlatma (yönetmelik/alışkanlık). Mesaj null ise sessiz.</summary>
        public void UyariKurali(Func<string> mesaj) => _uyarilar.Add(mesaj);

        public IReadOnlyList<string> Hatalar { get; private set; } = new string[0];
        public IReadOnlyList<string> Uyarilar { get; private set; } = new string[0];
        public bool Uygun => Hatalar.Count == 0;

        public void Guncelle()
        {
            foreach (var a in _alanlar) a.DisHata(null);
            var hatalar = new List<string>();
            foreach (var a in _alanlar) if (a.Hata != null) hatalar.Add(a.Hata);
            if (hatalar.Count == 0)
            {
                foreach (var k in _hatalar)
                {
                    string m = k.Mesaj();
                    if (m == null) continue;
                    hatalar.Add(m);
                    foreach (var a in k.Alanlar) a.DisHata(m);
                }
            }
            var uyarilar = hatalar.Count == 0 ? _uyarilar.Select(u => u()).Where(m => m != null).ToList() : new List<string>();
            Hatalar = hatalar; Uyarilar = uyarilar;

            if (hatalar.Count > 0)
            {
                _mesaj.ForeColor = GirdiTema.Hata;
                _mesaj.Text = "✕ " + string.Join("\n✕ ", hatalar.Take(2)) + (hatalar.Count > 2 ? "  (+" + (hatalar.Count - 2) + ")" : "");
            }
            else if (uyarilar.Count > 0)
            {
                _mesaj.ForeColor = GirdiTema.Uyari;
                _mesaj.Text = "⚠ " + string.Join("\n⚠ ", uyarilar.Take(2));
            }
            else
            {
                _mesaj.ForeColor = GirdiTema.Tamam;
                _mesaj.Text = "✓ Girdiler uygun";
            }
            Serit.Invalidate();
        }

        /// <summary>Çiz/Tamam öncesi: hata varsa ilk hatalı alana odaklanır ve false döner.</summary>
        public bool Onayla()
        {
            Guncelle();
            if (Uygun) return true;
            var ilk = _alanlar.FirstOrDefault(a => !a.Gecerli);
            ilk?.Kontrol.Focus();
            System.Media.SystemSounds.Beep.Play();
            return false;
        }

        private void SeritCiz(object sender, PaintEventArgs e)
        {
            Color zemin = Hatalar.Count > 0 ? GirdiTema.HataZemin : Uyarilar.Count > 0 ? GirdiTema.UyariZemin : Color.FromArgb(232, 245, 233);
            var r = new Rectangle(0, 0, _mesaj.Right + 4, Serit.Height - 1);
            using (var b = new SolidBrush(zemin)) e.Graphics.FillRectangle(b, r);
        }
    }
}
