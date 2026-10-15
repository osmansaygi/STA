using System;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    /// <summary>
    /// Aynı anda tek BETONARME form oturumu.
    /// Form açıkken (veya seçim için gizlenmişken) başka aile komutları engellenir.
    /// Formun kendi CIZ / katman seçimi serbesttir.
    /// TopMost kullanılmaz — iç diyaloglar (şablon adı vb.) arkada kalmasın.
    /// </summary>
    internal static class BaFormSession
    {
        private static string _owner;
        private static Form _form;
        private static bool _picking;

        public static string Owner => _owner;
        public static bool IsBusy =>
            !string.IsNullOrEmpty(_owner) && _form != null && !_form.IsDisposed;

        public static bool IsPicking => IsBusy && _picking;

        /// <summary>
        /// Komut giriş kapısı. family null → herhangi bir form varken engelle (DONATI ADET vb.).
        /// </summary>
        public static bool EnsureCanEnter(string family)
        {
            PruneIfDead();
            if (string.IsNullOrEmpty(_owner))
                return true;
            if (!string.IsNullOrEmpty(family) &&
                string.Equals(_owner, family, StringComparison.OrdinalIgnoreCase))
                return true;

            WarnAndFocus();
            return false;
        }

        /// <summary>Form ShowOrActivate başında: başka form varsa false.</summary>
        public static bool TryEnter(string family)
        {
            return EnsureCanEnter(family);
        }

        public static void Attach(string family, Form form)
        {
            if (string.IsNullOrWhiteSpace(family) || form == null || form.IsDisposed)
                return;
            _owner = family.Trim().ToUpperInvariant();
            _form = form;
            _picking = false;
            try
            {
                // Eski oturumdan kalmış TopMost varsa kaldır (iç diyaloglar için)
                if (form.TopMost)
                    form.TopMost = false;
            }
            catch { }
        }

        public static void BeginPick(string family)
        {
            if (!string.IsNullOrEmpty(family) &&
                string.Equals(_owner, family, StringComparison.OrdinalIgnoreCase))
                _picking = true;
            else if (string.IsNullOrEmpty(_owner) && !string.IsNullOrEmpty(family))
            {
                _owner = family.Trim().ToUpperInvariant();
                _picking = true;
            }
            else if (IsBusy)
                _picking = true;
        }

        public static void EndPick(string family)
        {
            if (string.IsNullOrEmpty(family) ||
                string.Equals(_owner, family, StringComparison.OrdinalIgnoreCase))
                _picking = false;
        }

        public static void Release(string family)
        {
            if (string.IsNullOrEmpty(family))
            {
                Clear();
                return;
            }
            if (string.Equals(_owner, family, StringComparison.OrdinalIgnoreCase))
                Clear();
        }

        public static void Clear()
        {
            _owner = null;
            _form = null;
            _picking = false;
        }

        /// <summary>
        /// Ana form üstünde güvenli modal çocuk. Parent TopMost ise geçici kapatır.
        /// </summary>
        public static DialogResult ShowChildDialog(Form parent, Form child)
        {
            if (child == null) return DialogResult.Cancel;
            bool restoredTopMost = false;
            try
            {
                if (parent != null && !parent.IsDisposed && parent.TopMost)
                {
                    parent.TopMost = false;
                    restoredTopMost = true;
                }
                child.ShowInTaskbar = false;
                if (parent != null && !parent.IsDisposed)
                    return child.ShowDialog(parent);
                return child.ShowDialog();
            }
            catch
            {
                try { return child.ShowDialog(); }
                catch { return DialogResult.Cancel; }
            }
            finally
            {
                if (restoredTopMost && parent != null && !parent.IsDisposed)
                {
                    try { parent.TopMost = false; } catch { } // TopMost bilerek açık bırakılmaz
                }
            }
        }

        private static void PruneIfDead()
        {
            if (_form != null && _form.IsDisposed)
                Clear();
        }

        private static void WarnAndFocus()
        {
            string title = DisplayName(_owner);
            try
            {
                Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage(
                    "\nBETONARME TOOL: Önce açık formu kapatın ({0}).",
                    title);
            }
            catch { }

            try
            {
                if (_form != null && !_form.IsDisposed)
                {
                    if (_picking)
                    {
                        _picking = false;
                        try { _form.Visible = true; } catch { }
                    }
                    try { _form.TopMost = false; } catch { }
                    _form.Visible = true;
                    _form.BringToFront();
                    _form.Activate();
                }
            }
            catch { }
        }

        private static string DisplayName(string family)
        {
            if (string.IsNullOrEmpty(family)) return "?";
            switch (family.ToUpperInvariant())
            {
                case "OTOOLCU": return "Oto Ölçü";
                case "PLANOLCU": return "Plan Ölçü";
                case "DOSEMEDONATI": return "Döşeme Donatı";
                case "OTODONATI": return "Oto Donatı";
                case "SEHPADONATI": return "Sehpa Donatısı";
                case "RADYEILAVE": return "Radye İlave";
                case "TEMELHATILI": return "Temel Hatılı";
                case "TEMELCEKURU": return "Temel Çukuru";
                case "KURANGLEZ": return "Kuranglez";
                case "DONATIBOYU": return "Donatı Boyu";
                case "DONATIMETRAJ": return "Donatı Metraj";
                case "KOTDEGISTIR": return "Kot Değiştir";
                case "MERDIVEN": return "Merdiven";
                default: return family;
            }
        }
    }
}
