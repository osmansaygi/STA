# OR_OTP_<harf>.st4 için DENEME2'yi Core Console 2027'de çizer (KalipDeneme2 Native probu); çıktı biz\<harf>.txt(.donati.txt)
param([string[]]$Harf=@('A','B','C'),[string]$PlanDll='')
$d=$PSScriptRoot
$kd=Join-Path $d '..\KalipDeneme2_20261024' | Resolve-Path
if(-not $PlanDll){$PlanDll=(Get-ChildItem "$d\..\..\NETLOAD\GUNCEL_2027\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
New-Item -ItemType Directory -Force "$d\biz" | Out-Null
$scr="$d\biz\deneme2.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F "$kd\Native\bin\Debug\net10.0-windows\Native.dll"),'KD_KALIP','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
foreach($h in $Harf){
  $env:KD_RESULT="$d\biz\$h.txt"; $env:KD_MODE='DENEME2'; $env:KD_PNG="$d\biz\$h.png"
  $env:KD_ST4="D:\projeler\projeler2026\void\oruc_reis\oruc_reis_yeni_ada_47309_2\statik_otp\OR_OTP_$h.st4"
  Remove-Item $env:KD_RESULT -ErrorAction SilentlyContinue
  Push-Location "$d\biz"
  try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\biz\$h.console.log" } finally { Pop-Location }
  "$h exit=$LASTEXITCODE " + $(if(Test-Path $env:KD_RESULT){(Get-Content $env:KD_RESULT | Select-String 'COMPLETE|FAIL|donati ms').Line -join ' '}else{'sonuc yok'})
}
