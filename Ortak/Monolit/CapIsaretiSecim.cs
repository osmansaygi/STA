using System;
using System.Windows.Forms;

namespace Beykent.Monolit.Girdi
{
    /// <summary>
    /// Çizime yazılacak çap işareti için serbest metin kutusu yerine liste (Donatı Boyu / Metraj "Fi işareti").
    /// Kayıtlı ama listede olmayan eski değer kaybolmaz, listeye eklenir.
    /// </summary>
    internal sealed class CapIsaretiSecim : ComboBox
    {
        private sealed class Oge
        {
            internal readonly string Deger, Gorunen;
            internal Oge(string deger, string gorunen) { Deger = deger; Gorunen = gorunen; }
            public override string ToString() => Gorunen;
        }

        internal static readonly string[,] Secenekler =
        {
            { "ø", "ø   küçük (varsayılan)" },
            { "Ø", "Ø   büyük" },
            { "%%c", "⌀   AutoCAD %%c" },
            { "Φ", "Φ   Yunan fi" },
        };

        public CapIsaretiSecim(string kayitli)
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 0; i < Secenekler.GetLength(0); i++) Items.Add(new Oge(Secenekler[i, 0], Secenekler[i, 1]));
            Deger = kayitli;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Deger
        {
            get => (SelectedItem as Oge)?.Deger ?? "ø";
            set
            {
                string v = string.IsNullOrWhiteSpace(value) ? "ø" : value.Trim();
                foreach (Oge o in Items) if (o.Deger == v) { SelectedItem = o; return; }
                var yeni = new Oge(v, v + "   (kayıtlı)");
                Items.Add(yeni); SelectedItem = yeni;
            }
        }
    }
}
