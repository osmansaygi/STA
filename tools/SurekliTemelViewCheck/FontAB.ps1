param([int]$Count=36)
# YAZI (BEYKENT) fontu Arial / Bahnschrift Light Condensed: aynı CK çizimi sırayla, çökme oranı.
$ErrorActionPreference='Stop'
$d=$PSScriptRoot
$plan=(Get-ChildItem "$d\build\ST4_Plan_ID_Ciz_*.dll").FullName
$out="$d\Native\ab-font\"
& dotnet build "$d\Native\Native.csproj" -c Debug -v quiet "-p:PlanDll=$plan" "-p:OutputPath=$out" | Out-Null
$scr="$d\ab-font.scr"
"SECURELOAD`r`n0`r`n_.NETLOAD`r`n$($out.Replace('\','/'))Native.dll`r`nFOUNDATION_DRAW_ONLY`r`n_.QUIT`r`n_Y`r`n" | Set-Content $scr -Encoding ASCII -NoNewline
$fail=@{Arial=0;Bahn=0}
foreach($i in 1..$Count){
 foreach($k in 'Arial','Bahn'){
  $base="$d\font-$k-$i"
  Remove-Item "$base.*" -ErrorAction SilentlyContinue
  $env:ST4_FOUNDATION_SOURCE='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
  $env:ST4_FOUNDATION_RESULT="$base.txt";$env:ST4_FOUNDATION_ANTET='0'
  $env:ST4_PRE_STYLE_FONT=$(if($k -eq 'Arial'){'Arial'}else{'Bahnschrift Light Condensed'})
  $p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/s `"$scr`" /l en-US" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$base.console.log"
  $ok=(Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet)
  if(!$ok){$fail[$k]++; "$k $i exit=0x{0:x8}" -f $p.ExitCode}
 }
}
$env:ST4_PRE_STYLE_FONT=''
"Arial FAIL $($fail.Arial) / $Count ; Bahnschrift FAIL $($fail.Bahn) / $Count"
