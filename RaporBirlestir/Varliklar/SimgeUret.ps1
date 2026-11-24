# Rapor Birleştir simgesi: üç renkli rapor sayfası → tek ciltli rapor.
# Monolit görsel dili: fildişi zemin, kobalt, turuncu vurgu. Her boyut ayrı çizilir (16–256).
param([string]$Klasor = $PSScriptRoot)
Add-Type -AssemblyName System.Drawing

function C([int]$r,[int]$g,[int]$b,[int]$a=255){ [System.Drawing.Color]::FromArgb($a,$r,$g,$b) }
function YuvarlakDortgen([float]$x,[float]$y,[float]$w,[float]$h,[float]$r){
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2*$r
  $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90)
  $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90)
  $p.CloseFigure(); return $p
}

function Ciz([int]$n){
  $bmp = New-Object System.Drawing.Bitmap $n,$n,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $s = $n / 256.0
  $g.ScaleTransform($s,$s)
  $kucuk = $n -le 32

  # Zemin karosu (GES simgesiyle aynı aile)
  $zemin = YuvarlakDortgen 8 8 240 240 30
  $g.FillPath((New-Object System.Drawing.SolidBrush (C 245 238 222)), $zemin)
  $g.DrawPath((New-Object System.Drawing.Pen (C 196 180 150), $(if($kucuk){14}else{8})), $zemin)

  $kobalt = C 32 72 168; $koyu = C 20 44 110; $turuncu = C 232 124 32; $gri = C 96 104 118
  $kagit = C 255 253 247; $cizgi = C 186 194 212

  # Sol: üç sayfa (statik=kobalt, iskele=turuncu, kalıp=gri başlık bandı)
  $renkler = @($gri, $turuncu, $kobalt)
  for($i=0; $i -lt 3; $i++){
    $x = 30 + $i*16; $y = 40 + $i*18
    $sayfa = YuvarlakDortgen $x $y 82 104 8
    $g.FillPath((New-Object System.Drawing.SolidBrush $kagit), $sayfa)
    $g.DrawPath((New-Object System.Drawing.Pen $koyu, $(if($kucuk){8}else{5})), $sayfa)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $renkler[$i]), $x+10, $y+12, 62, $(if($kucuk){22}else{16}))
  }
  if(-not $kucuk){
    $kalem = New-Object System.Drawing.Pen $cizgi, 6
    foreach($yy in 118,134,150){ $g.DrawLine($kalem, 72, $yy, 140, $yy) }
  }

  # Ok
  $ok = New-Object System.Drawing.Drawing2D.GraphicsPath
  $ok.AddPolygon([System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 140,122), (New-Object System.Drawing.PointF 162,122),
    (New-Object System.Drawing.PointF 162,104), (New-Object System.Drawing.PointF 190,136),
    (New-Object System.Drawing.PointF 162,168), (New-Object System.Drawing.PointF 162,150),
    (New-Object System.Drawing.PointF 140,150)))
  $g.FillPath((New-Object System.Drawing.SolidBrush $turuncu), $ok)

  # Sağ: tek ciltli rapor (kalın, kobalt sırt + üç renkli bölüm sekmesi)
  $kalin = YuvarlakDortgen 176 66 56 148 8
  $g.FillPath((New-Object System.Drawing.SolidBrush $kagit), $kalin)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $kobalt), 176, 66, 18, 148)
  $g.DrawPath((New-Object System.Drawing.Pen $koyu, $(if($kucuk){8}else{5})), $kalin)
  $sekme = @($kobalt, $turuncu, $gri)
  for($i=0; $i -lt 3; $i++){
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $sekme[$i]), 226, 88 + $i*36, 14, 26)
  }
  if(-not $kucuk){
    $kalem2 = New-Object System.Drawing.Pen $cizgi, 5
    foreach($yy in 98,116,134,152,170,188){ $g.DrawLine($kalem2, 202, $yy, 220, $yy) }
  }
  $g.Dispose()
  return $bmp
}

$boyutlar = 16,24,32,48,64,128,256
$pngler = @()
foreach($b in $boyutlar){
  $bmp = Ciz $b
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  if($b -eq 256){ $bmp.Save((Join-Path $Klasor 'birlestir.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
  $bmp.Dispose()
  $pngler += ,@($b, $ms.ToArray())
}

# ICO (PNG gömülü girişler)
$fs = [System.IO.File]::Create((Join-Path $Klasor 'birlestir.ico'))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngler.Count)
$ofs = 6 + 16*$pngler.Count
foreach($p in $pngler){
  $b = $p[0]; $veri = $p[1]
  $w.Write([byte]($(if($b -ge 256){0}else{$b}))); $w.Write([byte]($(if($b -ge 256){0}else{$b})))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
  $w.Write([UInt32]$veri.Length); $w.Write([UInt32]$ofs); $ofs += $veri.Length
}
foreach($p in $pngler){ $w.Write([byte[]]$p[1]) }
$w.Close()
"tamam"
