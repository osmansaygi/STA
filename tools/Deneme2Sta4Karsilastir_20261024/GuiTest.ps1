# Gerçek AutoCAD 2027 (GUI): yükleyici GUNCEL_2027 DLL'ini alır, Native probe DENEME2 çizer; CizimIzleyici günlüğü ve pencere görünürlüğü izlenir.
param([string]$Harf='B',[int]$BekleSn=240)
$d=$PSScriptRoot
$kd=Resolve-Path "$d\..\KalipDeneme2_20261024"
$log=Join-Path $env:TEMP 'ST4_CizimIzleyici.log'
Remove-Item $log -ErrorAction SilentlyContinue
$scr="$d\gui.scr"
@('SECURELOAD','0','_.NETLOAD',('"' + "$kd\Native\bin\Debug\net10.0-windows\Native.dll".Replace('\','/') + '"'),'KD_KALIP','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:KD_RESULT="$d\gui_$Harf.txt"; $env:KD_MODE='DENEME2'; $env:KD_PNG=''
$env:KD_ST4="D:\projeler\projeler2026\void\oruc_reis\oruc_reis_yeni_ada_47309_2\statik_otp\OR_OTP_$Harf.st4"
Remove-Item $env:KD_RESULT -ErrorAction SilentlyContinue
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Son;
  public static void Yakala(string yol) {
    SetProcessDPIAware(); RECT r; if (Son == IntPtr.Zero || !GetWindowRect(Son, out r)) return;
    var vs = System.Windows.Forms.SystemInformation.VirtualScreen; int x = vs.Left, y = vs.Top, w = vs.Width, hh = vs.Height;
    using (var b = new System.Drawing.Bitmap(w, hh)) { using (var g = System.Drawing.Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, b.Size); b.Save(yol); }
  }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  public static string Ust() {
    SetProcessDPIAware(); RECT r; if (Son == IntPtr.Zero || !GetWindowRect(Son, out r)) return "rect yok";
    var p = new POINT { X = (r.L + r.R) / 2, Y = (r.T + r.B) / 2 }; IntPtr h = GetAncestor(WindowFromPoint(p), 2);
    var c = new StringBuilder(128); GetClassName(h, c, 128); var t = new StringBuilder(128); GetWindowText(h, t, 128); uint pid; GetWindowThreadProcessId(h, out pid);
    return "form=" + r.L + "," + r.T + "-" + r.R + "," + r.B + " exstyle=0x" + GetWindowLong(Son, -20).ToString("X") + " noktada=" + c + " [" + t + "] pid=" + pid + " ayni=" + (h == Son);
  }
  public static string Bul(uint pid) {
    var sb = new StringBuilder();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { var c = new StringBuilder(128); GetClassName(h, c, 128); if (c.ToString().StartsWith("WindowsForms")) { sb.Append(c).Append(' '); Son = h; } }
      return true; }, IntPtr.Zero);
    return sb.ToString();
  }
}
'@ -ReferencedAssemblies System.Drawing,System.Windows.Forms
$p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\acad.exe' -ArgumentList '/nologo','/b',"`"$scr`"" -PassThru
$gorundu=$false
for($i=0;$i -lt $BekleSn*2;$i++){
  Start-Sleep -Milliseconds 500
  if($p.HasExited){break}
  $w=[W]::Bul([uint32]$p.Id)
  if($w -and $gorundu -and -not $yakalandi){ Start-Sleep -Milliseconds 400; [W]::Yakala((Join-Path $d 'gui_pencere.png')); $yakalandi=$true }
  if($w){ "  ust: " + [W]::Ust() }
  if($w){ if(-not $gorundu){"$(Get-Date -Format HH:mm:ss.fff) FORM GORUNUR: $w"}; $gorundu=$true }
}
if(-not $p.HasExited){ "zaman asimi; AutoCAD kapatiliyor"; Stop-Process -Id $p.Id -Force }
"gorundu=$gorundu"
if(Test-Path $log){ Get-Content $log } else { 'gunluk yok' }
if(Test-Path $env:KD_RESULT){ Get-Content $env:KD_RESULT | Select-String 'COMPLETE|FAIL|ms=|Plan=' }
