# Core Console 2027: izole Yardımcı DLL ile -SEHIMTABLOSU; çıktı <Ad>.png / <Ad>.txt
param([Parameter(Mandatory)][string]$St4,[Parameter(Mandatory)][string]$Kat,[string]$Ad='sehim',[string]$YardimciDll='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $YardimciDll){$YardimciDll=(Get-ChildItem "$d\ybuild\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$d\$Ad.scr"
@('SECURELOAD','0','_.NETLOAD',(F $YardimciDll),'_.NETLOAD',(F "$d\Native\bin\Debug\net10.0-windows\Native.dll"),'-SEHIMTABLOSU',$St4,$Kat,'0,0','ST_RENDER','_.SAVEAS','2018',(F "$d\$Ad.dwg"),'_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:ST_PNG="$d\$Ad.png"; $env:ST_TXT="$d\$Ad.txt"
Remove-Item $env:ST_TXT,"$d\$Ad.dwg" -ErrorAction SilentlyContinue
Push-Location $d
try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Ad.console.log" } finally { Pop-Location }
"exit=$LASTEXITCODE"
if(Test-Path $env:ST_TXT){Get-Content $env:ST_TXT -TotalCount 3}else{'sonuc yok'}
