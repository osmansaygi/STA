param([int]$Count=6,[string]$Prefix='stab',[string]$Antet='0')
# Aynı CK çizimini ayrı Core Console süreçlerinde tekrarlar; kararsız çökme oranı için.
$d=$PSScriptRoot
foreach($i in 1..$Count){
 $base="$d\$Prefix-$i"
 Remove-Item "$base.*" -ErrorAction SilentlyContinue
 $env:ST4_FOUNDATION_SOURCE='D:\projeler\projeler2026\kızılkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
 $env:ST4_FOUNDATION_RESULT="$base.txt";$env:ST4_FOUNDATION_DWG='';$env:ST4_FOUNDATION_ANTET=$Antet;$env:ST4_FOUNDATION_REPEAT=''
 $p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/s `"$d\run.scr`" /l en-US" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$base.console.log"
 "$i exit=$($p.ExitCode) complete=$((Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet))"
}
