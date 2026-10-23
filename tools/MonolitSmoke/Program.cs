using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using ST4Yardimci;

class Program {
 static void Main() {
  string dir=Path.GetFullPath("docs/icon-alternatives/monolit-trial");Directory.CreateDirectory(dir);
  // Exercise both designs without writing a user's saved preference.
  var selected=typeof(BaCommandIcons).GetField("<UseMonolit>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
  selected.SetValue(null,true);
  var hashes=new HashSet<string>();
  using(var board=new Bitmap(960,850))using(var g=Graphics.FromImage(board))
  using(var title=new Font("Segoe UI",20,FontStyle.Bold))using(var text=new Font("Segoe UI",10))
  using(var white=new SolidBrush(Color.FromArgb(233,238,244)))using(var muted=new SolidBrush(Color.FromArgb(167,181,195))) {
   g.Clear(Color.FromArgb(32,40,50));g.InterpolationMode=InterpolationMode.HighQualityBicubic;
   g.DrawString("MONOLİT / 20 KOMUT",title,white,25,20);
   g.DrawString("Gerçek simge yükleyicisinden çıktı · 28 px / 32 px / 64 px",text,muted,27,62);
   for(int i=0;i<BaCommandIcons.Tools.Length;i++) {
    var tool=BaCommandIcons.Tools[i];
    using(var icon=(Bitmap)BaCommandIcons.Load(tool.Cmd,tool.Resource)) {
     if(icon==null||icon.Width!=32||icon.Height!=32)throw new Exception("Missing icon: "+tool.Cmd);
     bool visible=false,transparent=false;
     for(int y=0;y<32;y++)for(int x=0;x<32;x++){int a=icon.GetPixel(x,y).A;visible|=a>128;transparent|=a==0;}
     if(!visible||!transparent)throw new Exception("Invalid alpha: "+tool.Cmd);
     using(var ms=new MemoryStream()){icon.Save(ms,ImageFormat.Png);hashes.Add(Convert.ToBase64String(SHA256.HashData(ms.ToArray())));}
     icon.Save(Path.Combine(dir,tool.Cmd+".png"),ImageFormat.Png);
     int col=i/10,row=i%10,x0=25+col*470,y0=102+row*72;
     g.DrawImage(icon,new Rectangle(x0,y0+12,28,28));
     g.DrawImage(icon,new Rectangle(x0+40,y0+10,32,32));
     g.DrawImage(icon,new Rectangle(x0+85,y0-6,64,64));
     g.DrawString(tool.Label,text,white,x0+160,y0+16);
    }
   }
   board.Save(Path.Combine(dir,"panel-preview.png"),ImageFormat.Png);
  }
  if(hashes.Count!=20)throw new Exception("Duplicate atlas cells");
  selected.SetValue(null,false);
  foreach(var tool in BaCommandIcons.Tools)using(var icon=BaCommandIcons.Load(tool.Cmd,tool.Resource))if(icon==null)throw new Exception("Classic fallback missing "+tool.Cmd);
  File.WriteAllText(Path.Combine(dir,"validation.txt"),"PASS: 20 distinct Monolit icons, 32x32 RGBA, visible content and transparency.\nPASS: 20 classic fallbacks load.\nNo drawing commands invoked; no icon preference written.\n");
  Console.WriteLine("PASS: all 20 Monolit icons and all classic fallbacks; preview: "+dir);
 }
}
