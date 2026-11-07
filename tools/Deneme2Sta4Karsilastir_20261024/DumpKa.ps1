# STA4CAD OR_OTP_*_ka1.dwg kopyalarını Core Console 2027'de açıp ka\<harf>.txt'ye döker.
param([string[]]$Harf=@('A','B','C'))
$d=$PSScriptRoot
$src='D:\projeler\projeler2026\void\oruc_reis\oruc_reis_yeni_ada_47309_2\statik_otp'
New-Item -ItemType Directory -Force "$d\ka" | Out-Null
foreach($h in $Harf){
  Copy-Item "$src\OR_OTP_${h}_ka1.dwg" "$d\ka\" -Force
  $env:DD_OUT="$d\ka\$h.txt"; Remove-Item $env:DD_OUT -ErrorAction SilentlyContinue
  Push-Location "$d\ka"
  try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /i "$d\ka\OR_OTP_${h}_ka1.dwg" /s "$d\dump.scr" /l en-US *> "$d\ka\$h.console.log" } finally { Pop-Location }
  "$h exit=$LASTEXITCODE " + $(if(Test-Path $env:DD_OUT){(Get-Content $env:DD_OUT).Count}else{'sonuc yok'})
}
