param([Parameter(Mandatory=$true)][string]$YardimciDll,[string]$Name='probe')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$ini=Join-Path $env:LOCALAPPDATA 'ST4_Yardimci\merdiven.ini'
$bak="$d\merdiven.ini.bak"
if(Test-Path $ini){Copy-Item $ini $bak -Force}
$scr="$d\$Name.scr"
@"
SECURELOAD
0
_.NETLOAD
$($YardimciDll -replace '\\','/')
_.NETLOAD
$($d -replace '\\','/')/Native/bin/Debug/net10.0-windows/Native.dll
MERKIRIS_PROBE
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
$env:MK_RESULT="$d\$Name.txt"
Remove-Item $env:MK_RESULT -ErrorAction SilentlyContinue
try {
 & 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Name.console.log"
 "exit=$LASTEXITCODE"
} finally {
 if(Test-Path $bak){Copy-Item $bak $ini -Force; Remove-Item $bak}
}
if(Test-Path $env:MK_RESULT){Get-Content $env:MK_RESULT}else{'sonuc yok'}
