param([string]$PlanDll,[string]$Prefix,[int]$Count=10,[string]$Antet='0')
# Aynı CK çizimini verilen Plan DLL'si ile yalnız-çizim komutuyla tekrarlar (kararlılık A/B).
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$out="$d\Native\ab-$Prefix\"
& dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$PlanDll" "-p:OutputPath=$out" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
$scr="$d\ab-$Prefix.scr"
"SECURELOAD`r`n0`r`n_.NETLOAD`r`n$($out.Replace('\','/'))Native.dll`r`nFOUNDATION_DRAW_ONLY`r`n_.QUIT`r`n_Y`r`n" | Set-Content $scr -Encoding ASCII -NoNewline
$fail=0
foreach($i in 1..$Count){
 $base="$d\ab-$Prefix-$i"
 Remove-Item "$base.*" -ErrorAction SilentlyContinue
 $env:ST4_FOUNDATION_SOURCE='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
 $env:ST4_FOUNDATION_RESULT="$base.txt";$env:ST4_FOUNDATION_ANTET=$Antet
 $p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/s `"$scr`" /l en-US" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$base.console.log"
 $ok=(Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet)
 if(!$ok){$fail++}
 "$Prefix $i exit=0x{0:x8} complete=$ok" -f $p.ExitCode
}
"$Prefix FAIL $fail / $Count"
