using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Beykent.Monolit
{
    /// <summary>COLORTHEME: 0 koyu, 1 açık. AutoCAD erişimi yalnız UI bağlamında.</summary>
    internal static class MonolitTheme
    {
        internal static bool IsLight { get; private set; }

        internal static void Refresh()
        {
            try { IsLight = Convert.ToInt32(AcApp.GetSystemVariable("COLORTHEME")) == 1; }
            catch (System.Exception) { /* Host kapanırken son geçerli tema korunur. */ }
        }

        internal static Color Bg => IsLight ? Color.FromArgb(248, 246, 241) : Color.FromArgb(39, 41, 44);
        internal static Color Surface => IsLight ? Color.FromArgb(239, 236, 230) : Color.FromArgb(49, 52, 56);
        internal static Color Hover => IsLight ? Color.FromArgb(228, 234, 245) : Color.FromArgb(61, 68, 80);
        internal static Color Pressed => IsLight ? Color.FromArgb(214, 225, 245) : Color.FromArgb(66, 79, 101);
        internal static Color Border => IsLight ? Color.FromArgb(216, 212, 204) : Color.FromArgb(76, 79, 85);
        internal static Color Text => IsLight ? Color.FromArgb(40, 42, 46) : Color.FromArgb(239, 237, 232);
        internal static Color Muted => IsLight ? Color.FromArgb(103, 102, 98) : Color.FromArgb(176, 179, 185);
        internal static Color Accent => IsLight ? Color.FromArgb(32, 88, 190) : Color.FromArgb(131, 176, 255);
        internal static Color Primary => Color.FromArgb(32, 88, 190);
        internal static Color Orange => IsLight ? Color.FromArgb(163, 81, 18) : Color.FromArgb(242, 170, 93);
        internal static Color Input => IsLight ? Color.FromArgb(255, 254, 251) : Color.FromArgb(31, 33, 36);
    }

    /// <summary>Standart A: daima açık, yerel Windows kontrolleri ve çerçeveli gruplar.</summary>
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

        internal MonolitForm() { MonolitTheme.Refresh(); DoubleBuffered = true; }

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
