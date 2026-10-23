using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using ST4PlanIdCiz;

class Program
{
 [STAThread]
 static void Main()
 {
  var output=Path.GetFullPath("docs/icon-alternatives/st4-monolit");Directory.CreateDirectory(output);
  var flag=typeof(StaMonolitIcons).GetField("<Enabled>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
  flag.SetValue(null,true);
  string[] names={"Kalıp Planı · 1:50","Kolon Aplikasyon · 1:50","Kolon Düşey","Temel Planı · 1:50","Rd. Temel Planı","Kalıp Planı · 1:100","Kolon Aplikasyon · 1:100","Temel Planı · 1:100","İskele Planı","İskele Kesiti","ST4 Plan ID","Kolon Data","ST4 Kesit","Katman temizle","Deneme1","Mantar Döşeme","Kapama Perde","Kolon Revize","Bina Görünüşü","Bina Kesiti","Kiriş Açılımları","Kiriş düzelt","Temel kiriş düzelt","Metraj"};
  var hashes=new HashSet<string>();
  using(var board=new Bitmap(1200,1010))using(var g=Graphics.FromImage(board))
  using(var title=new Font("Segoe UI",22,FontStyle.Bold))using(var text=new Font("Segoe UI",11))
  using(var white=new SolidBrush(Color.FromArgb(235,239,243)))using(var muted=new SolidBrush(Color.FromArgb(172,183,195)))
  {
   g.Clear(AcadPaletteTheme.Bg);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
   g.DrawString("ST4 ID / MONOLİT",title,white,28,22);
   g.DrawString("24 komut · gerçek simge yükleyicisinden 32 px ve 64 px çıktısı",text,muted,30,68);
   for(int i=0;i<StaMonolitIcons.Commands.Length;i++)
   {
    string cmd=StaMonolitIcons.Commands[i];
    using(var icon=(Bitmap)StaToolIcons.ForCommand(cmd))
    {
     if(icon==null||icon.Width!=32||icon.Height!=32)throw new Exception("Icon missing: "+cmd);
     bool alpha=false,visible=false;
     for(int y=0;y<32;y++)for(int x=0;x<32;x++){int a=icon.GetPixel(x,y).A;alpha|=a==0;visible|=a>128;}
     if(!alpha||!visible)throw new Exception("Alpha/content invalid: "+cmd);
     using(var ms=new MemoryStream()){icon.Save(ms,ImageFormat.Png);hashes.Add(Convert.ToBase64String(SHA256.HashData(ms.ToArray())));}
     icon.Save(Path.Combine(output,cmd+".png"),ImageFormat.Png);
     int col=i/12,row=i%12,x0=30+col*590,y0=119+row*71;
     g.DrawImage(icon,new Rectangle(x0,y0+10,32,32));g.DrawImage(icon,new Rectangle(x0+48,y0-5,64,64));
     g.DrawString(names[i],text,white,x0+126,y0+17);
    }
   }
   board.Save(Path.Combine(output,"panel-preview.png"),ImageFormat.Png);
  }
  if(hashes.Count!=24)throw new Exception("Duplicate icons: "+hashes.Count);
  using(var alias=StaMonolitIcons.TryLoad("kolondusey25"))if(alias==null)throw new Exception("Alias missing");
  if(StaMonolitIcons.TryLoad("NONEXISTENT")!=null)throw new Exception("Unknown command did not fall back");
  flag.SetValue(null,false);
  foreach(string cmd in StaMonolitIcons.Commands)using(var classic=StaToolIcons.ForCommand(cmd))if(classic==null)throw new Exception("Classic missing: "+cmd);
  flag.SetValue(null,true);
  // Verify both tabs refresh via the same event, without touching saved settings or running commands.
  using(var a=new Panel())using(var b=new Panel())
  {
   ToolPaletteUi.AddToolRow(a,0,"Kalıp",StaToolIcons.ForCommand("KALIP50ST4"),"KALIP50ST4");
   ToolPaletteUi.AddToolRow(b,0,"Metraj",StaToolIcons.ForCommand("METRAJ"),"METRAJ");
   ToolPaletteUi.AttachIconMenu(a);ToolPaletteUi.AttachIconMenu(b);
   flag.SetValue(null,false);
   var changed=(Action)typeof(StaMonolitIcons).GetField("Changed",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   changed();
   foreach(Panel panel in new[]{a,b})foreach(Control row in panel.Controls)
    if(row.Tag is string command&&row.Controls[0] is PictureBox pic)
     using(var expected=StaToolIcons.ForCommand(command))
      if(((Bitmap)pic.Image).GetPixel(16,16)!=((Bitmap)expected).GetPixel(16,16))throw new Exception("Tab refresh failed");
   foreach(Panel panel in new[]{a,b})foreach(Control row in panel.Controls)foreach(Control child in row.Controls)if(child is PictureBox pic)pic.Image?.Dispose();
  }
  File.WriteAllText(Path.Combine(output,"validation.txt"),"PASS: 24 distinct 32x32 Monolit icons, transparent background and visible content.\nPASS: 24 classic fallback icons.\nPASS: vertical aliases and unknown command fallback.\nPASS: both tab controls refresh without executing drawing commands.\nNo saved icon preference modified.\n");
  Console.WriteLine("PASS: all ST4 Monolit icon checks; preview "+output);
 }
}
