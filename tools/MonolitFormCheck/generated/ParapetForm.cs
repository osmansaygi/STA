using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal sealed class ParapetForm : Beykent.Monolit.MonolitForm
    {
        static ParapetForm _instance;
        readonly Dictionary<string, NumericUpDown> _numbers = new Dictionary<string, NumericUpDown>();
        readonly ComboBox _konum, _dikeyCap, _yatayCap;
        readonly Panel _beam, _footer;
        readonly ToolTip _tips = new ToolTip { AutoPopDelay = 12000 };
        bool _starting;
        internal static ParapetSettings Current;

        public static void ShowOrActivate()
        {
            if (!BaFormSession.TryEnter("PARAPET")) return;
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new ParapetForm();
                AcApp.ShowModelessDialog(_instance);
            }
            else { _instance.Show(); _instance.BringToFront(); }
            BaFormSession.Attach("PARAPET", _instance);
            BaFormSession.EndPick("PARAPET");
        }

        public static void Shutdown()
        {
            BaFormSession.Release("PARAPET");
            if (_instance != null) { _instance.Close(); _instance = null; }
        }

        ParapetForm()
        {
            Current = ParapetSettings.Load();
            SuspendLayout();
            Text = "Parapet Detayı";
            AutoScaleDimensions = new SizeF(96,96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420,298);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            BackColor = Beykent.Monolit.StandardFormTheme.Bg;
            ForeColor = Beykent.Monolit.StandardFormTheme.Text;
            Font = new Font("Segoe UI",8.5f);
            KeyPreview = true;

            LabelAt(this,"Yerleşim",10,15,76,20,true);
            _konum=Combo(this,96,10,314);
            _konum.Items.AddRange(new object[]{"Döşeme kenarı","Döşeme ortası","Kiriş üzeri"});
            _konum.SelectedIndex=(int)Current.Konum;
            Rule(this,10,44,400);

            Section(this,"Kesit (cm)",10,54);
            Row(this,"DosemeKalinligi","Döşeme kalınlığı",10,83,8,150);
            Row(this,"DosemeUstKotu","Döşeme üst kotu",10,109,-100000,100000);
            Row(this,"ParapetYuksekligi","Parapet yüksekliği",10,135,15,600);
            Row(this,"ParapetKalinligi","Parapet kalınlığı",10,161,10,150);
            Row(this,"Sakal","Sakal",10,187,0,300);
            Row(this,"Paspayi","Paspayı",10,213,2,20);
            _tips.SetToolTip(_numbers["Sakal"],"Döşeme alt yüzünden aşağı sarkan mesafe. 0: sakal yok. Kiriş üzerinde kullanılamaz.");
            _tips.SetToolTip(_numbers["ParapetYuksekligi"],"Döşeme üst yüzünden parapet üstüne kadar ölçülür.");
            _tips.SetToolTip(_numbers["DosemeUstKotu"],"Üst kotu cm olarak girin. Örnek: +3.00 m için 300.");

            Section(this,"Donatı",224,54);
            LabelAt(this,"Ø mm",268,78,56,18,true);
            LabelAt(this,"Ara / cm",342,78,68,18,true);
            _dikeyCap=RebarRow("Düşey","DikeyAralik",99,Current.DikeyCap);
            _yatayCap=RebarRow("Yatay","YatayAralik",127,Current.YatayCap);
            _tips.SetToolTip(_dikeyCap,"Alt ve ikinci gönye en az 12Ø; toplam boy 5 cm katına tamamlanır.");
            _tips.SetToolTip(_yatayCap,"Sakal sıraları yatay donatı adedine dahildir.");

            Section(this,"Metraj",224,169);
            LabelAt(this,"Toplam boy (cm)",224,194,186,18,true);
            Number(this,"ToplamUzunluk",224,215,186,0,10000000,0,10);
            _tips.SetToolTip(_numbers["ToplamUzunluk"],"Stok boyu 1200 cm, bindirme 60 cm. 0: adet ve metraj tablosu yok.");

            _beam=new Panel {Location=new Point(10,246),Size=new Size(194,74),BackColor=BackColor};
            Controls.Add(_beam);
            Section(_beam,"Kiriş (cm)",0,0);
            Row(_beam,"KirisGenisligi","Genişlik",0,22,10,300);
            Row(_beam,"KirisYuksekligi","Yükseklik",0,48,8,300);
            _tips.SetToolTip(_beam,"Kiriş ve parapetin dış yüzleri hizalıdır. Kiriş üst kotu döşeme üst kotudur.");

            _footer=new Panel {Location=new Point(0,254),Size=new Size(420,44),BackColor=BackColor};
            Controls.Add(_footer);
            Rule(_footer,10,0,400);
            LabelAt(_footer,"Boy 0: metraj yok",10,16,150,20,true);
            var close=ButtonAt(_footer,"Kapat",230,10,66,false);
            close.Click+=(s,e)=>Close();
            var draw=ButtonAt(_footer,"Detayı çiz",306,10,104,true);
            draw.Click+=(s,e)=>Draw();
            _konum.SelectedIndexChanged+=(s,e)=>EnableBeam();
            EnableBeam();
            FormClosed+=(s,e)=>{ReadSettings().Save(); _instance=null; BaFormSession.Release("PARAPET");};
            ResumeLayout(false);

            // Kompakt, içerik odaklı form düzeni.
            var ui = BeginCompact(360);
            ui.Section(null).Field("Yerleşim", _konum);
            ui.Tabs(220,
                ui.Page("Kesit", p => p.Field("Döşeme kalınlığı · cm", _numbers["DosemeKalinligi"]).Field("Döşeme üst kotu · cm", _numbers["DosemeUstKotu"])
                    .Field("Parapet yük. / en · cm", _numbers["ParapetYuksekligi"], _numbers["ParapetKalinligi"])
                    .Field("Sakal / paspayı · cm", _numbers["Sakal"], _numbers["Paspayi"])
                    .Columns(ui.Column("Kiriş · cm").Field("En / yükseklik", _numbers["KirisGenisligi"], _numbers["KirisYuksekligi"]).When(() => _konum.SelectedIndex == 2))),
                ui.Page("Donatı ve metraj", p => p.Field("Düşey · Ø mm / ara cm", _dikeyCap, _numbers["DikeyAralik"])
                    .Field("Yatay · Ø mm / ara cm", _yatayCap, _numbers["YatayAralik"]).Field("Toplam boy · cm", _numbers["ToplamUzunluk"])
                    .Hint("Toplam boy 0 ise metraj tablosu çizilmez.")));
            ui.Footer(draw, close);
            ui.Finish();
        }

        static Label LabelAt(Control parent,string text,int x,int y,int width,int height,bool muted)
        {
            var label=new Label {Text=text,Location=new Point(x,y),Size=new Size(width,height),
                ForeColor=muted?Beykent.Monolit.StandardFormTheme.TextMuted:Beykent.Monolit.StandardFormTheme.Text,BackColor=Color.Transparent};
            parent.Controls.Add(label);
            return label;
        }

        static void Section(Control parent,string text,int x,int y)
        {
            var label=LabelAt(parent,text,x,y,190,23,false);
            label.Font=new Font("Segoe UI Semibold",9);
        }

        static void Rule(Control parent,int x,int y,int width)
            => parent.Controls.Add(new Panel {Location=new Point(x,y),Size=new Size(width,1),BackColor=Beykent.Monolit.StandardFormTheme.InputBorder});

        static ComboBox Combo(Control parent,int x,int y,int width)
        {
            var combo=new ComboBox {Location=new Point(x,y),Width=width,DropDownStyle=ComboBoxStyle.DropDownList,
                FlatStyle=FlatStyle.Flat,BackColor=Beykent.Monolit.StandardFormTheme.InputBg,ForeColor=Beykent.Monolit.StandardFormTheme.Text,
                IntegralHeight=true,DropDownHeight=220,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=18};
            combo.DrawItem+=(s,e)=>
            {
                bool selected=(e.State&DrawItemState.Selected)!=0;
                using(var brush=new SolidBrush(selected?Beykent.Monolit.StandardFormTheme.BgHover:Beykent.Monolit.StandardFormTheme.InputBg))
                    e.Graphics.FillRectangle(brush,e.Bounds);
                object item=e.Index>=0?combo.Items[e.Index]:combo.SelectedItem;
                if(item!=null)
                    TextRenderer.DrawText(e.Graphics,combo.GetItemText(item),combo.Font,
                        new Rectangle(e.Bounds.X+3,e.Bounds.Y,e.Bounds.Width-5,e.Bounds.Height),Beykent.Monolit.StandardFormTheme.Text,
                        TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            parent.Controls.Add(combo);
            return combo;
        }

        ComboBox RebarRow(string name,string spacingKey,int y,double selected)
        {
            LabelAt(this,name,224,y+3,44,22,false);
            var combo=Combo(this,268,y,64);
            var sizes=new List<double>{6,8,10,12,14,16,18,20,22,24,25,26,28,30,32,36,40};
            // Eski kayıttaki özel çap kaybolmasın.
            if(selected>=6 && selected<=40 && !sizes.Contains(selected)) {sizes.Add(selected); sizes.Sort();}
            foreach(double diameter in sizes) combo.Items.Add(diameter);
            combo.Format+=(s,e)=>e.Value="Ø"+Convert.ToDouble(e.ListItem,CultureInfo.InvariantCulture).ToString("0.#",CultureInfo.InvariantCulture);
            combo.FormattingEnabled=true;
            combo.SelectedItem=selected;
            if(combo.SelectedIndex<0) combo.SelectedItem=12d;
            Number(this,spacingKey,342,y,68,5,50);
            return combo;
        }

        void Number(Control parent,string key,int x,int y,int width,decimal min,decimal max,int decimals=1,decimal step=0.5m)
        {
            var n=new NumericUpDown {Location=new Point(x,y),Width=width,Minimum=min,Maximum=max,
                DecimalPlaces=decimals,Increment=step,BackColor=Beykent.Monolit.StandardFormTheme.InputBg,ForeColor=Beykent.Monolit.StandardFormTheme.Text,
                BorderStyle=BorderStyle.FixedSingle,TextAlign=HorizontalAlignment.Right};
            double value=(double)typeof(ParapetSettings).GetField(key).GetValue(Current);
            n.Value=(decimal)Math.Max((double)min,Math.Min((double)max,value));
            YardimciUi.StripUpDownButtons(n);
            _numbers.Add(key,n);
            parent.Controls.Add(n);
        }

        void Row(Control parent,string key,string text,int x,int y,decimal min,decimal max)
        {
            LabelAt(parent,text,x,y+3,126,22,true);
            Number(parent,key,x+132,y,62,min,max);
        }

        void EnableBeam()
        {
            bool beam=_konum.SelectedIndex==2;
            _beam.Visible=beam;
            _numbers["KirisGenisligi"].Enabled=_numbers["KirisYuksekligi"].Enabled=beam;
            _numbers["Sakal"].Enabled=!beam;
            if(beam) _numbers["Sakal"].Value=0;
            if(CompactLayoutReady) { RefreshCompactLayout(); return; }
            float scale=IsHandleCreated?DeviceDpi/96f:1f;
            _footer.Top=(int)Math.Round((beam?330:254)*scale);
            ClientSize=new Size((int)Math.Round(420*scale),(int)Math.Round((beam?374:298)*scale));
        }

        ParapetSettings ReadSettings()
        {
            var s=new ParapetSettings {Konum=(ParapetKonumu)_konum.SelectedIndex};
            foreach(var kv in _numbers) typeof(ParapetSettings).GetField(kv.Key).SetValue(s,(double)kv.Value.Value);
            s.DikeyCap=Convert.ToDouble(_dikeyCap.SelectedItem,CultureInfo.InvariantCulture);
            s.YatayCap=Convert.ToDouble(_yatayCap.SelectedItem,CultureInfo.InvariantCulture);
            s.Normalize();
            return s;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            EnableBeam();
        }

        void Draw()
        {
            if(_starting) return;
            var settings=ReadSettings();
            string error=settings.Validate();
            if(error!=null) {MessageBox.Show(this,error,"Parapet Detayı"); return;}
            var doc=AcApp.DocumentManager.MdiActiveDocument;
            if(doc==null) return;
            Current=settings;
            Current.Save();
            _starting=true;
            BaFormSession.BeginPick("PARAPET");
            Hide();
            try {doc.SendStringToExecute("PARAPETCIZ ",true,false,false);}
            catch {_starting=false; Show(); BaFormSession.EndPick("PARAPET"); throw;}
        }

        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.Escape) {Close(); return true;}
            if(keyData==Keys.Enter)
            {
                if(_konum.DroppedDown || _dikeyCap.DroppedDown || _yatayCap.DroppedDown) return base.ProcessCmdKey(ref msg,keyData);
                Draw(); return true;
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }

        static Button ButtonAt(Control parent,string text,int x,int y,int width,bool primary)
        {
            var button=new Button {Text=text,Location=new Point(x,y),Size=new Size(width,30),FlatStyle=FlatStyle.Flat,
                BackColor=primary?Beykent.Monolit.StandardFormTheme.Accent:Beykent.Monolit.StandardFormTheme.BgAlt,
                ForeColor=primary?Beykent.Monolit.StandardFormTheme.InputBg:Beykent.Monolit.StandardFormTheme.Text,Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
            button.FlatAppearance.BorderSize=primary?0:1;
            button.FlatAppearance.BorderColor=Beykent.Monolit.StandardFormTheme.InputBorder;
            parent.Controls.Add(button);
            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing) _tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
