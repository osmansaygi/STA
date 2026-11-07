param([string]$Dump,[string]$Png,[double]$X0,[double]$Y0,[double]$X1,[double]$Y1,[int]$W=1400)
Add-Type -AssemblyName System.Drawing
$inv=[Globalization.CultureInfo]::InvariantCulture
$s=$W/($X1-$X0); $H=[int](($Y1-$Y0)*$s)
$bmp=New-Object Drawing.Bitmap $W,$H; $g=[Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::FromArgb(33,40,48)); $g.SmoothingMode='AntiAlias'
function P([double]$x,[double]$y){ New-Object Drawing.PointF ([float](($x-$X0)*$s)),([float](($Y1-$y)*$s)) }
function Col($layer){ if($layer -match 'DONATI YAZ'){'Lime'} elseif($layer -match 'ETR'){'DeepSkyBlue'} elseif($layer -match 'DONATI'){'Cyan'} elseif($layer -match 'OLCU|ÖLÇÜ'){'Orange'} elseif($layer -match 'YAZI'){'White'} else {'LightGreen'} }
$font=New-Object Drawing.Font 'Arial',9
foreach($line in Get-Content $Dump -Encoding utf8){
 $f=$line.Split('|'); $c=[Drawing.Color]::FromName((Col $f[-1])); $pen=New-Object Drawing.Pen $c,1
 switch($f[0]){
  'L'{ $g.DrawLine($pen,(P ([double]::Parse($f[1],$inv)) ([double]::Parse($f[2],$inv))),(P ([double]::Parse($f[3],$inv)) ([double]::Parse($f[4],$inv)))) }
  'P'{ $pts=@(); foreach($q in $f[1].Split(';')){ $xy=$q.Split(','); $pts+=(P ([double]::Parse($xy[0],$inv)) ([double]::Parse($xy[1],$inv))) }; if($pts.Count -le 4 -and $f[-1] -match '^DONATI .BEY' -and [Math]::Abs($pts[0].X-$pts[1].X)+[Math]::Abs($pts[0].Y-$pts[1].Y) -lt 8*$s){ $cx=($pts[0].X+$pts[1].X)/2; $cy=($pts[0].Y+$pts[1].Y)/2; $g.FillEllipse((New-Object Drawing.SolidBrush $c),$cx-5,$cy-5,10,10) } elseif($pts.Count -ge 2){ $g.DrawLines($pen,[Drawing.PointF[]]$pts) } }
  'T'{ $g.DrawString($f[4],$font,(New-Object Drawing.SolidBrush $c),(P ([double]::Parse($f[1],$inv)) (([double]::Parse($f[2],$inv))+[double]::Parse($f[3],$inv)))) }
  'D'{ $a=(P ([double]::Parse($f[1],$inv)) ([double]::Parse($f[2],$inv))); $b=(P ([double]::Parse($f[3],$inv)) ([double]::Parse($f[4],$inv))); $g.DrawLine($pen,$a,$b); $g.DrawString($f[7],$font,(New-Object Drawing.SolidBrush $c),(P ([double]::Parse($f[5],$inv)) ([double]::Parse($f[6],$inv)))) }
 }
}
$bmp.Save($Png,[Drawing.Imaging.ImageFormat]::Png)
