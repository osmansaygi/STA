param([string]$Cmd='KOLONDUSEY_PROBE',[string]$Name='probe',[switch]$NoBuild,[string]$BuildDir='build2027')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
if(-not $NoBuild){
 Remove-Item "$d\build2027\ST4_Plan_ID_Ciz_*" -ErrorAction SilentlyContinue
 & "$d\Build.ps1" -Plan2027 | Out-Null
}
$plan=(Get-ChildItem "$d\$BuildDir\ST4_Plan_ID_Ciz_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
Remove-Item "$d\Native\bin\Debug\net10.0-windows\ST4_Plan_ID_Ciz_*" -ErrorAction SilentlyContinue
& dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$plan" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
$scr="$d\$Name.scr"
@"
SECURELOAD
0
_.NETLOAD
$($d -replace '\\','/')/Native/bin/Debug/net10.0-windows/Native.dll
$Cmd
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
$i=[char]0x131
if(-not $env:KD_SOURCE){$env:KD_SOURCE="D:\projeler\projeler2026\k${i}z${i}lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"}
$env:KD_RESULT="$d\$Name.txt"; $env:KD_DWG="$d\$Name.dwg"
Remove-Item $env:KD_RESULT,$env:KD_DWG -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Name.console.log"
Get-Content $env:KD_RESULT
"Plan DLL: $plan"
