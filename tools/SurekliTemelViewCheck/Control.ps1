param([int]$Count=24,[string]$Command='FONT_PROBE',[string]$Prefix='ctl')
# Kontrol: Plan çizimi çağırmayan komutla Core Console kararlılığı.
$d=$PSScriptRoot
$scr="$d\$Prefix.scr"
"SECURELOAD`r`n0`r`n_.NETLOAD`r`n$($d.Replace('\','/'))/Native/bin/Debug/net10.0-windows/Native.dll`r`n$Command`r`n_.QUIT`r`n_Y`r`n" | Set-Content $scr -Encoding ASCII -NoNewline
$fail=0
foreach($i in 1..$Count){
 $base="$d\$Prefix-$i"
 Remove-Item "$base.*" -ErrorAction SilentlyContinue
 $env:ST4_FOUNDATION_RESULT="$base.txt"
 $p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/s `"$scr`" /l en-US" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$base.console.log"
 $ok=(Test-Path "$base.txt") -and (Select-String "$base.txt" -Pattern '^COMPLETE$' -Quiet)
 if(!$ok){$fail++; "$Prefix $i exit=0x{0:x8}" -f $p.ExitCode}
}
"$Prefix FAIL $fail / $Count"
