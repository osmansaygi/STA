"""One-time standard A layout migration. Does not change drawing/settings units."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
def read(p): return p.read_text(encoding='utf-8-sig')
def write(p, t):
    with p.open('w', encoding='utf-8', newline='\r\n') as f: f.write(t)

p = root/'Ortak/Monolit/MonolitForm.cs'
t = read(p)
t = t[:t.index('    /// <summary>Mevcut komut olayları')]+'''    /// <summary>Standart A: daima açık, yerel Windows kontrolleri ve çerçeveli gruplar.</summary>
    internal class MonolitForm : Form
    {
        private readonly List<IDisposable> _ownedResources = new List<IDisposable>();
        private CompactLayout _compact;
        protected bool CompactLayoutReady => _compact != null;
        protected CompactLayout BeginCompact(int width) => new CompactLayout(this, width);
        protected void RefreshCompactLayout() => _compact?.Refresh();
        internal void SetCompactLayout(CompactLayout layout) { _compact = layout; }
        internal void MarkPrimary(Button button) { }
        internal void OwnLayoutResource(IDisposable resource) { _ownedResources.Add(resource); }

        internal MonolitForm() { DoubleBuffered = true; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyStandard(this);
            RefreshCompactLayout();
        }

        private static void ApplyStandard(Control c)
        {
            c.ForeColor = StandardFormTheme.Text;
            c.BackColor = StandardFormTheme.Bg;
            if (c is Button b)
            {
                b.FlatStyle = FlatStyle.Standard;
                b.UseVisualStyleBackColor = true;
                b.ForeColor = SystemColors.ControlText;
            }
            else if (c is TextBoxBase || c is ListControl || c is NumericUpDown)
            {
                c.BackColor = StandardFormTheme.Input;
                if (c is TextBox box) box.BorderStyle = BorderStyle.Fixed3D;
                if (c is ListBox list) list.BorderStyle = BorderStyle.Fixed3D;
                if (c is NumericUpDown number) number.BorderStyle = BorderStyle.Fixed3D;
                if (c is ComboBox combo) { combo.FlatStyle = FlatStyle.Standard; combo.DrawMode = DrawMode.Normal; }
            }
            else if (c is DataGridView grid)
            {
                grid.BackgroundColor = StandardFormTheme.Input;
                grid.GridColor = StandardFormTheme.Border;
                grid.EnableHeadersVisualStyles = true;
                grid.DefaultCellStyle.BackColor = StandardFormTheme.Input;
                grid.DefaultCellStyle.ForeColor = StandardFormTheme.Text;
                grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            }
            else if (c is TabControl tabs) tabs.DrawMode = TabDrawMode.Normal;
            else if (c is CheckBox check) { check.FlatStyle = FlatStyle.Standard; check.UseVisualStyleBackColor = true; }
            else if (c is RadioButton radio) { radio.FlatStyle = FlatStyle.Standard; radio.UseVisualStyleBackColor = true; }
            else if (c is Panel && c.Height <= 2) c.BackColor = StandardFormTheme.Border;
            foreach (Control child in c.Controls) ApplyStandard(child);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IDisposable resource in _ownedResources) resource.Dispose();
                _ownedResources.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
'''
write(p,t)

for module in ('ST4_Yardimci','ST4_Plan_ID_Ciz'):
    for p in (root/module).glob('*Form.cs'):
        t=read(p)
        if ': Beykent.Monolit.MonolitForm' in t:
            write(p,t.replace('AcadPaletteTheme.', 'Beykent.Monolit.StandardFormTheme.'))

p=root/'ST4_Yardimci/OtoDonatiForm.cs'; t=read(p)
t=t.replace('Üst kot (m)', 'Üst kot (cm)').replace('Üst kot · m','Üst kot · cm')
t=t.replace('FormatNum(OtoDonatiSettings.UstKotM, 2)', 'FormatNum(OtoDonatiSettings.UstKotM * 100.0, 2)')
t=t.replace('kot < -100 || kot > 300', 'kot < -10000 || kot > 30000').replace('kot >= -100 && kot <= 300','kot >= -10000 && kot <= 30000')
t=t.replace('Üst kot geçersiz (m).','Üst kot geçersiz (cm).')
t=t.replace('OtoDonatiSettings.UstKotM = kot;', 'OtoDonatiSettings.UstKotM = kot / 100.0;').replace('p.UstKotM = kot;', 'p.UstKotM = kot / 100.0;')
write(p,t)
p=root/'ST4_Yardimci/SehpaDonatiForm.cs'; t=read(p)
t=t.replace('70, -80, 200, 0.05m, 2, (decimal)SehpaDonatiSettings.UstKotM','70, -8000, 20000, 5m, 2, (decimal)(SehpaDonatiSettings.UstKotM * 100.0)')
t=t.replace('MakeUnit("m", 226', 'MakeUnit("cm", 226').replace('üst kot · m','üst kot · cm')
t=t.replace('Clamp(_numUstKot, (decimal)SehpaDonatiSettings.UstKotM)', 'Clamp(_numUstKot, (decimal)(SehpaDonatiSettings.UstKotM * 100.0))')
t=t.replace('UstKotM = (double)_numUstKot.Value;', 'UstKotM = (double)_numUstKot.Value / 100.0;')
write(p,t)
p=root/'ST4_Plan_ID_Ciz/KirisAcilimForm.cs'; t=read(p)
t=t.replace('f.ElevationM.ToString("0.00") + " m)"', '(f.ElevationM * 100.0).ToString("0.##") + " cm)"')
write(p,t)

p=root/'Ortak/Monolit/CompactLayout.cs'; t=read(p)
start=t.index('            var tabs = new TabControl',t.index('internal void Tabs('))
end=t.index('\n        internal CompactSection Column(',start)
t=t[:start]+'''            // A düzeninde ayarlar sekme arkasında saklanmaz.
            foreach (var page in pages) page.Build(Section(page.Title));
        }

        internal void Columns(params CompactSection[] sections)
        {
            foreach (var section in sections) Add(_root, section.View);
        }
''' + t[end:]
t=t.replace('internal TableLayoutPanel View { get; } = CompactLayout.Stack();', '''internal TableLayoutPanel View { get; } = CompactLayout.Stack();
        private readonly TableLayoutPanel _body = CompactLayout.Stack();''')
start=t.index('            View.Margin =', t.index('internal CompactSection(CompactLayout'))
end=t.index('\n        internal CompactSection Field(',start)
t=t[:start]+'''            View.Margin = new Padding(0, 3, 0, 5);
            if (!string.IsNullOrEmpty(title))
            {
                var group = new GroupBox { Text = title, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                    Padding = new Padding(10, 20, 10, 8), Margin = Padding.Empty };
                group.Controls.Add(_body);
                CompactLayout.Add(View, group);
            }
            else CompactLayout.Add(View, _body);
        }
''' + t[end:]
t=t.replace('CompactLayout.Add(View, row)', 'CompactLayout.Add(_body, row)').replace('CompactLayout.Add(View, control)', 'CompactLayout.Add(_body, control)').replace('CompactLayout.Add(View, table)', 'CompactLayout.Add(_body, table)')
start=t.index('            var table = new TableLayoutPanel', t.index('internal CompactSection Columns('))
end=t.index('\n        internal CompactSection Grid(',start)
t=t[:start]+'''            foreach (var section in sections) CompactLayout.Add(_body, section.View);
            return this;
        }
''' + t[end:]
# Standard checkbox/radio appearance, rather than flat segmented tiles.
start=t.index('            foreach (var c in choices)',t.index('internal CompactSection Options('))
end=t.index('\n        internal CompactSection Actions(',start)
t=t[:start]+'''            foreach (var c in choices)
            {
                if (c is CheckBox cb) cb.Appearance = Appearance.Normal;
                if (c is RadioButton rb) rb.Appearance = Appearance.Normal;
            }
            return Choices(choices);
        }
''' + t[end:]
t=t.replace('MonolitTheme.', 'StandardFormTheme.')
t=t.replace('list.Height = 52; list.MinimumSize = new Size(0, 52); list.MaximumSize = new Size(10000, 52)', 'list.Height = 76; list.MinimumSize = new Size(0, 76); list.MaximumSize = new Size(10000, 76)')
write(p,t)
print('Standard A and centimeter UI migration written.')
