# Core Console 2027: izole Yardımcı DLL ile -BETONKALIP; çıktı <Ad>.png / <Ad>.txt (çizici: SehimTablosu20261026\Native)
param([Parameter(Mandatory)][string]$St4,[string]$Kirilim='Eleman',[string]$Ad='betonkalip',[string]$YardimciDll='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$nat="$d\..\SehimTablosu20261026\Native\bin\Debug\net10.0-windows\Native.dll"
if(-not $YardimciDll){$YardimciDll=(Get-ChildItem "$d\..\SehimTablosu20261026\ybuild\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$d\$Ad.scr"
@('SECURELOAD','0','_.NETLOAD',(F $YardimciDll),'_.NETLOAD',(F $nat),'-BETONKALIP',$St4,$Kirilim,'0,0','ST_RENDER','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:ST_PNG="$d\$Ad.png"; $env:ST_TXT="$d\$Ad.txt"
Remove-Item $env:ST_TXT -ErrorAction SilentlyContinue
Push-Location $d
try { & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Ad.console.log" } finally { Pop-Location }
"exit=$LASTEXITCODE"
if(Test-Path $env:ST_TXT){Get-Content $env:ST_TXT -TotalCount 1}else{'sonuc yok'}
