param([int]$Count=60,[string[]]$Modes=@('full','concrete','donationly','nodraw'))
# Deney kopyası (_e2_plan) E_MODE ile; modlar sırayla dönüşümlü koşar.
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$plan=(Get-ChildItem "$d\e2build\ST4_Plan_ID_Ciz_*.dll").FullName
$out="$d\Native\ab-modes\"
& dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$plan" "-p:OutputPath=$out" | Out-Null
$scr="$d\ab-modes.scr"
"SECURELOAD`r`n0`r`n_.NETLOAD`r`n$($out.Replace('\','/'))Native.dll`r`nFOUNDATION_DRAW_ONLY`r`n_.QUIT`r`n_Y`r`n" | Set-Content $scr -Encoding ASCII -NoNewline
$fail=@{}; foreach($m in $Modes){$fail[$m]=0}
foreach($i in 1..$Count){
 foreach($m in $Modes){
  $base="$d\mode-$m-$i"
  Remove-Item "$base.*" -ErrorAction SilentlyContinue
  $env:ST4_FOUNDATION_SOURCE='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
  $env:ST4_FOUNDATION_RESULT="$base.txt";$env:ST4_FOUNDATION_ANTET='0';$env:ST4_PRE_STYLE_FONT=''
  $env:E_MODE=$(if($m -eq 'full'){''}else{$m})
  $p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/s `"$scr`" /l en-US" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$base.console.log"
  $ok=(Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet)
  if($ok){Remove-Item "$base.*" -ErrorAction SilentlyContinue}else{$fail[$m]++; "$m $i exit=0x{0:x8}" -f $p.ExitCode}
 }
}
$env:E_MODE=''
foreach($m in $Modes){"$m FAIL $($fail[$m]) / $Count"}
