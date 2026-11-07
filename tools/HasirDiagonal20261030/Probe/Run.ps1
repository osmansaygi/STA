param([Parameter(Mandatory=$true)][string]$YardimciDll,[string]$Name='run')
$d=$PSScriptRoot
$probe="$d\bin\Release\net10.0-windows\Probe.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$d\$Name.scr"
@('SECURELOAD','0','_.NETLOAD',(F $YardimciDll),'_.NETLOAD',(F $probe),'HDPROBE','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:HD_RESULT="$d\$Name.txt"; Remove-Item $env:HD_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Name.console.log"
if(Test-Path $env:HD_RESULT){Get-Content $env:HD_RESULT}else{'sonuc yok'}
