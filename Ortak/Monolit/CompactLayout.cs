using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Beykent.Monolit
{
    /// <summary>Mevcut editörleri/olayları yeniden kullanan, içerik ölçüsüne göre büyüyen form düzeni.</summary>
    internal sealed class CompactLayout
    {
        private readonly MonolitForm _form;
        private readonly int _width;
        private readonly Control[] _original;
        private readonly List<Control> _interactive;
        private readonly Dictionary<Control, Rectangle> _bounds;
        private readonly List<Tuple<Control, Func<bool>>> _conditions = new List<Tuple<Control, Func<bool>>>();
        private readonly Panel _viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly TableLayoutPanel _root = Stack();
        private readonly TableLayoutPanel _footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 42,
            Padding = new Padding(10, 5, 10, 5), RowCount = 1, Margin = Padding.Empty };
        private readonly ToolTip _tips = new ToolTip();
        private bool _installed, _updating;
        internal Size PreviousSize { get; }
        internal int UnplacedCount { get; private set; }

        internal CompactLayout(MonolitForm form, int width)
        {
            _form = form; _width = width; PreviousSize = form.ClientSize;
            _original = form.Controls.Cast<Control>().ToArray();
            _interactive = _original.SelectMany(Inputs).ToList();
            _bounds = _interactive.ToDictionary(c => c, c => c.Bounds);
            _root.Dock = DockStyle.Top;
            _root.Padding = new Padding(10, 4, 10, 6);
            _viewport.Controls.Add(_root);
        }

        internal CompactSection Section(string title)
        {
            var section = new CompactSection(this, title);
            Add(_root, section.View);
            return section;
        }

        internal CompactPage Page(string title, Action<CompactSection> build) => new CompactPage(title, build);

        internal void Tabs(int height, params CompactPage[] pages)
        {
            // A düzeninde ayarlar sekme arkasında saklanmaz.
            foreach (var page in pages) page.Build(Section(page.Title));
        }

        internal void Columns(params CompactSection[] sections)
        {
            foreach (var section in sections) Add(_root, section.View);
        }

        internal CompactSection Column(string title) => new CompactSection(this, title);
        internal Button Button(string text) => _interactive.OfType<Button>().First(b => b.Text == text);
        internal Button[] LayerButtons(ListBox list) => _interactive.OfType<Button>()
            .Where(b => _bounds[b].Top >= _bounds[list].Bottom - 2)
            .OrderBy(b => _bounds[b].Top).ThenBy(b => _bounds[b].Left).Take(3).ToArray();

        internal void Footer(Button primary, Button close, params Button[] secondary)
        {
            if (secondary.Length > 1) Section(null).Actions(secondary);
            _form.MarkPrimary(primary);
            _footer.ColumnCount = 3;
            _footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, secondary.Length == 1 ? 50 : 100));
            _footer.ColumnStyles.Add(secondary.Length == 1
                ? new ColumnStyle(SizeType.Percent, 50)
                : new ColumnStyle(SizeType.Absolute, Math.Max(110, Math.Min(150, primary.PreferredSize.Width + 20))));
            _footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            Prepare(primary); Prepare(close);
            primary.Margin = new Padding(0, 0, 8, 0); close.Margin = Padding.Empty;
            primary.Height = close.Height = 32;
            primary.AutoEllipsis = close.AutoEllipsis = true;
            int column = secondary.Length == 1 ? 0 : 1;
            if (secondary.Length == 1)
            {
                Prepare(secondary[0]); secondary[0].AutoEllipsis = true;
                secondary[0].Margin = new Padding(0, 0, 6, 0);
                _footer.Controls.Add(secondary[0], column++, 0);
            }
            _footer.Controls.Add(primary, column++, 0); _footer.Controls.Add(close, column, 0);
        }

        internal void Finish()
        {
            // Her eski etkileşimli kontrol yeni ağaçta bulunmalı. Unutulmuş seçenek kaybolmaz.
            var placed = new HashSet<Control>(Inputs(_root).Concat(Inputs(_footer)));
            var remaining = _interactive.Where(c => !placed.Contains(c) && !c.IsDisposed).ToArray();
            UnplacedCount = remaining.Length;
            if (remaining.Length > 0)
            {
                var extra = Section("Diğer seçenekler");
                foreach (var c in remaining) extra.Control(c);
            }
            var reserve = new Panel { Visible = false, TabStop = false, Size = Size.Empty };
            // Referans verilen eski açıklama etiketlerini de form ömründe tut.
            foreach (var c in _original)
                if (c.Parent == _form) reserve.Controls.Add(c);
            _form.Controls.Add(reserve);
            _form.Controls.Add(_viewport);
            _form.Controls.Add(_footer);
            _form.AutoScaleDimensions = new SizeF(96, 96);
            _form.AutoScaleMode = AutoScaleMode.Dpi;
            _form.FormBorderStyle = FormBorderStyle.FixedDialog;
            _form.MinimumSize = Size.Empty;
            _form.Font = new Font("Segoe UI", 9f);
            _form.OwnLayoutResource(_form.Font);
            _form.OwnLayoutResource(_tips);
            _installed = true;
            _form.SetCompactLayout(this);
            int order = 0;
            SetTabOrder(_root, ref order);
            SetTabOrder(_footer, ref order);
            Refresh();
        }

        internal void Refresh()
        {
            if (!_installed || _updating || _form.IsDisposed) return;
            _updating = true;
            _form.SuspendLayout();
            try
            {
                foreach (var condition in _conditions) condition.Item1.Visible = condition.Item2();
                float scale = _form.IsHandleCreated ? _form.DeviceDpi / 96f : 1f;
                int width = (int)Math.Round(_width * scale);
                _form.ClientSize = new Size(width, _form.ClientSize.Height);
                _root.Width = Math.Max(100, width - _viewport.Padding.Horizontal);
                _root.PerformLayout();
                int height = _root.GetPreferredSize(new Size(_root.Width, 0)).Height + _footer.Height;
                int limit = Math.Max(180, Screen.FromControl(_form).WorkingArea.Height - 80);
                _form.ClientSize = new Size(width, Math.Min(limit, Math.Max(96, height)));
                _viewport.AutoScrollMinSize = new Size(0, height - _footer.Height);
            }
            finally { _form.ResumeLayout(true); _updating = false; }
        }

        internal void When(Control c, Func<bool> condition) => _conditions.Add(Tuple.Create(c, condition));
        internal void Tip(Control c, string text) => _tips.SetToolTip(c, text);
        internal void Watch(Control c)
        {
            if (c is CheckBox check) check.CheckedChanged += (s, e) => Refresh();
            else if (c is RadioButton radio) radio.CheckedChanged += (s, e) => Refresh();
            else if (c is ComboBox combo) combo.SelectedIndexChanged += (s, e) => Refresh();
        }
        internal static TableLayoutPanel Stack() => new TableLayoutPanel { ColumnCount = 1,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            Margin = Padding.Empty, ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) } };
        internal static void Add(TableLayoutPanel table, Control c)
        {
            int row = table.RowCount++;
            // Native ListBox/TabControl PreferredSize değeri eski mutlak yerleşimden etkilenebilir.
            // Sınırlandırılmış içerik alanlarının satırı gerçek hedef yüksekliği kullanır.
            bool fixedHeight = c is ListBox || c is TabControl || (!c.AutoSize && c.MinimumSize.Height > 0);
            table.RowStyles.Add(fixedHeight
                ? new RowStyle(SizeType.Absolute, c.MinimumSize.Height + c.Margin.Vertical)
                : new RowStyle(SizeType.AutoSize));
            table.Controls.Add(c, 0, row);
        }
        internal static void Prepare(Control c)
        {
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right; c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 2, 0, 2);
            if (c is Button button) { button.AutoSize = false; button.Height = 26; }
            else if (c is TextBoxBase || c is ComboBox || c is NumericUpDown)
                c.MinimumSize = new Size(0, 24);
        }
        private static IEnumerable<Control> Inputs(Control c)
        {
            if (c is Button || c is CheckBox || c is RadioButton || c is TextBoxBase
                || c is ListControl || c is NumericUpDown || c is DataGridView)
            { yield return c; yield break; }
            foreach (Control child in c.Controls) foreach (var input in Inputs(child)) yield return input;
        }
        private static void SetTabOrder(Control c, ref int index)
        {
            foreach (Control child in c.Controls) { child.TabIndex = index++; SetTabOrder(child, ref index); }
        }
    }

    /// <summary>Yerel GroupBox, içerik yüksekliğini mevcut satır genişliğinde ölçer.</summary>
    internal sealed class StandardChoices : FlowLayoutPanel
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 80 ? proposedSize.Width : Math.Max(80, Width);
            return base.GetPreferredSize(new Size(width, 0));
        }
    }

    internal sealed class StandardGroup : GroupBox
    {
        private readonly Control _content;
        internal StandardGroup(Control content) { _content = content; }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > Padding.Horizontal + 80 ? proposedSize.Width : Width;
            int innerWidth = Math.Max(80, width - Padding.Horizontal);
            Size content = _content.GetPreferredSize(new Size(innerWidth, 0));
            return new Size(width, content.Height + DisplayRectangle.Top + Padding.Bottom);
        }
    }

    internal sealed class CompactPage
    {
        internal readonly string Title;
        internal readonly Action<CompactSection> Build;
        internal CompactPage(string title, Action<CompactSection> build) { Title = title; Build = build; }
    }

    internal sealed class CompactSection
    {
        private readonly CompactLayout _owner;
        internal TableLayoutPanel View { get; } = CompactLayout.Stack();
        private readonly TableLayoutPanel _body = CompactLayout.Stack();
        internal CompactSection(CompactLayout owner, string title)
        {
            _owner = owner;
            View.Margin = new Padding(0, 3, 0, 5);
            if (!string.IsNullOrEmpty(title))
            {
                var group = new StandardGroup(_body) { Text = title, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                    Padding = new Padding(10, 8, 10, 6), Margin = Padding.Empty };
                _body.Dock = DockStyle.Top;
                group.Controls.Add(_body);
                CompactLayout.Add(View, group);
            }
            else CompactLayout.Add(View, _body);
        }

        internal CompactSection Field(string caption, params Control[] editors)
            => Field(new Label { Text = caption, AutoSize = true }, editors);

        internal CompactSection FieldWhen(Func<bool> condition, Control caption, params Control[] editors)
        {
            Field(caption, editors);
            _owner.When(_body.Controls[_body.Controls.Count - 1], condition);
            return this;
        }

        internal CompactSection Field(Control caption, params Control[] editors)
        {
            var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                ColumnCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            caption.Anchor = AnchorStyles.Left | AnchorStyles.Right; caption.Dock = DockStyle.Fill;
            caption.Margin = new Padding(0, 4, 8, 4);
            if (caption is Label label) { label.AutoSize = true; label.TextAlign = ContentAlignment.MiddleLeft; label.MaximumSize = new Size(190, 0); }
            row.Controls.Add(caption, 0, 0);
            var inner = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                ColumnCount = editors.Length, Margin = Padding.Empty };
            foreach (var editor in editors)
            {
                inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / editors.Length));
                CompactLayout.Prepare(editor);
                if (inner.Controls.Count > 0) editor.Margin = new Padding(5, 2, 0, 2);
                inner.Controls.Add(editor, inner.Controls.Count, 0);
                _owner.Watch(editor);
            }
            row.Controls.Add(inner, 1, 0);
            CompactLayout.Add(_body, row);
            return this;
        }

        internal CompactSection Choices(params Control[] choices)
        {
            var row = new StandardChoices { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 4) };
            foreach (var choice in choices)
            {
                choice.Dock = DockStyle.None; choice.AutoSize = true;
                choice.MaximumSize = new Size(380, 0);
                choice.Margin = new Padding(0, 3, 10, 3);
                row.Controls.Add(choice); _owner.Watch(choice);
            }
            CompactLayout.Add(_body, row);
            return this;
        }

        internal CompactSection Options(params Control[] choices)
        {
            foreach (var c in choices)
            {
                if (c is CheckBox cb) cb.Appearance = Appearance.Normal;
                if (c is RadioButton rb) rb.Appearance = Appearance.Normal;
            }
            return Choices(choices);
        }

        internal CompactSection Actions(params Control[] actions)
        {
            var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                ColumnCount = actions.Length, Margin = new Padding(0, 1, 0, 3) };
            for (int i = 0; i < actions.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actions.Length));
                CompactLayout.Prepare(actions[i]); actions[i].Height = 26;
                actions[i].Margin = new Padding(i == 0 ? 0 : 5, 2, 0, 2);
                row.Controls.Add(actions[i], i, 0); _owner.Watch(actions[i]);
            }
            CompactLayout.Add(_body, row);
            return this;
        }

        internal CompactSection Template(ComboBox combo, Button add, Button remove)
        {
            var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            _owner.Tip(add, add.Text); _owner.Tip(remove, remove.Text);
            add.Text = "Ekle"; remove.Text = "Sil";
            foreach (var c in new Control[] { combo, add, remove })
            {
                CompactLayout.Prepare(c); c.Margin = new Padding(0, 2, 5, 2);
                row.Controls.Add(c, row.Controls.Count, 0); _owner.Watch(c);
            }
            CompactLayout.Add(_body, row);
            return this;
        }

        internal CompactSection Layers(ListBox list, params Button[] actions)
        {
            list.IntegralHeight = false; list.Height = 52; list.MinimumSize = new Size(0, 52); list.MaximumSize = new Size(10000, 52);
            Control(list);
            if (actions.Length > 0)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    string oldText = actions[i].Text;
                    _owner.Tip(actions[i], oldText + " — çizimden seçim / liste düzenleme");
                    actions[i].Text = i == 0 ? "Ekle" : i == 1 ? "Listeden sil" : "Çıkar";
                }
                Actions(actions);
            }
            return this;
        }

        internal CompactSection Control(Control control, int height = 0)
        {
            CompactLayout.Prepare(control);
            if (height > 0) { control.Height = height; control.MinimumSize = new Size(0, height); control.MaximumSize = new Size(10000, height); }
            if (control is Label label) { label.AutoSize = true; label.MaximumSize = new Size(600, 0); }
            CompactLayout.Add(_body, control);
            return this;
        }
        internal CompactSection Hint(string text) => Control(new Label { Text = text, AutoSize = true, ForeColor = StandardFormTheme.Muted });
        internal CompactSection When(Func<bool> condition) { _owner.When(View, condition); return this; }
        internal CompactSection Columns(params CompactSection[] sections)
        {
            foreach (var section in sections) CompactLayout.Add(_body, section.View);
            return this;
        }

        internal CompactSection Grid(string[] headers, params Control[][] rows)
        {
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                ColumnCount = headers.Length, Margin = Padding.Empty };
            for (int i = 0; i < headers.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / headers.Length));
                table.Controls.Add(new Label { Text = headers[i], AutoSize = true, Margin = new Padding(0, 2, 4, 4) }, i, 0);
            }
            for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                {
                    var editor = rows[row][column]; CompactLayout.Prepare(editor);
                    editor.Margin = new Padding(0, 2, column == headers.Length - 1 ? 0 : 6, 2);
                    table.Controls.Add(editor, column, row + 1); _owner.Watch(editor);
                }
            CompactLayout.Add(_body, table);
            return this;
        }
    }
}
