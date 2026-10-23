using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ST4Yardimci
{
    // Offscreen UI preview: AutoCAD host and settings disk writes are replaced only in this executable.
    static class BaIni
    {
        public static Dictionary<string,string> Read(string file) => new Dictionary<string,string>();
        public static double GetD(Dictionary<string,string> m,string k,double d)=>d;
        public static int GetI(Dictionary<string,string> m,string k,int d)=>d;
        public static string N(double d)=>d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static void Write(string file,params string[] rows) { }
    }
    static class BaFormSession
    {
        public static bool TryEnter(string key)=>true;
        public static void Attach(string key,Form f) { }
        public static void EndPick(string key) { }
        public static void BeginPick(string key) { }
        public static void Release(string key) { }
    }
    static class Program
    {
        static T Field<T>(Form f,string name)=>(T)typeof(ParapetForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../../.."));
            using(var form=(Form)Activator.CreateInstance(typeof(ParapetForm),true))
            {
                form.StartPosition=FormStartPosition.Manual;
                form.Location=new Point(-32000,-32000);
                form.Show();
                Application.DoEvents();
                form.PerformLayout();
                foreach(int mode in new[]{0,2})
                {
                    Field<ComboBox>(form,"_konum").SelectedIndex=mode;
                    form.PerformLayout();
                    using(var bitmap=new Bitmap(form.Width,form.Height))
                    {
                        form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                        bitmap.Save(Path.Combine(root,mode==0?"parapet-form.png":"parapet-form-kiris.png"),ImageFormat.Png);
                    }
                }
                Field<ComboBox>(form,"_dikeyCap").SelectedItem=16d;
                Field<ComboBox>(form,"_yatayCap").SelectedItem=8d;
                var result=(ParapetSettings)typeof(ParapetForm).GetMethod("ReadSettings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                if(result.DikeyCap!=16 || result.YatayCap!=8 || result.Sakal!=0) throw new Exception("Diameter selection round trip failed.");
                Console.WriteLine("Offscreen slab/beam previews and diameter selections OK.");
            }
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    static class Application
    {
        public static void ShowModelessDialog(Form form) { }
        public static Manager DocumentManager {get;}=new Manager();
    }
    sealed class Manager { public Document MdiActiveDocument=>null; }
    sealed class Document { public void SendStringToExecute(string text,bool a,bool b,bool c) { } }
}
