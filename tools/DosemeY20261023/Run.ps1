param([string]$Cmd='DY_DRAW',[string]$Name='draw',[string]$OpenDwg='',[string]$YardimciDll='',[string]$Line='',[string]$Yon='Y',[string]$Out='',[string]$PlanDll='')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$root=Resolve-Path "$d\..\.."
$plan="$root\NETLOAD\arsiv\GUNCEL_2027_20261023_204821_053\ST4_Plan_ID_Ciz_2027_20261023_190204.dll"
if($PlanDll){$plan=$PlanDll}
if(-not $YardimciDll){$YardimciDll="$root\NETLOAD\GUNCEL_2027\ST4_Yardimci_2027_20261023_201748.dll"}
$scr="$d\$Name.scr"
$lines=@('SECURELOAD','0')
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$lines+=@('_.NETLOAD',(F "$plan"),'_.NETLOAD',(F $YardimciDll),'_.NETLOAD',(F "$d\Native\bin\Debug\net10.0-windows\Native.dll"),$Cmd,'_.QUIT','_Y')
$lines | Set-Content -Encoding ascii $scr
$env:DY_RESULT="$d\$Name.txt"; $env:DY_ST4="D:\projeler\projeler2026\k$([char]0x131)z$([char]0x131)lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"
$env:DY_DWG="$d\ck.dwg"; $env:DY_LINE=$Line; $env:DY_YON=$Yon; $env:DY_OUT=$Out
Remove-Item $env:DY_RESULT -ErrorAction SilentlyContinue
$ia=@(); if($OpenDwg){$ia=@('/i',$OpenDwg)}
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' @ia /s $scr /l en-US *> "$d\$Name.console.log"
"exit=$LASTEXITCODE"
if(Test-Path $env:DY_RESULT){Get-Content $env:DY_RESULT}else{'sonuc yok'}
