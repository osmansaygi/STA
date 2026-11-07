param([string]$Name='sik0',[string]$Sik='0')
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$plan=(Get-ChildItem "$d\build2027\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
if(-not (Test-Path "$d\Native\bin\Debug\net10.0-windows\Native.dll")){
 & dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$plan" | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Native build failed'}
}
$scr="$d\$Name.scr"
@"
SECURELOAD
0
_.NETLOAD
$($d.Replace('\','/'))/Native/bin/Debug/net10.0-windows/Native.dll
KKE_DRAW
_.QUIT
_Y
"@ | Set-Content -Encoding ascii $scr
$i=[char]0x131
if(-not $env:KD_SOURCE){$env:KD_SOURCE="D:\projeler\projeler2026\k${i}z${i}lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4"}
$env:KD_RESULT="$d\$Name.txt"; $env:KD_SIK=$Sik
Remove-Item $env:KD_RESULT -ErrorAction SilentlyContinue
& 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' /s $scr /l en-US *> "$d\$Name.console.log"
"Plan DLL: $plan"
