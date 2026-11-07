param([string]$Dwg)
$d=$PSScriptRoot; $out="$d\out"
$PlanDll=(Get-ChildItem "$d\buildPlan\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName
$probe="$d\Probe\bin\Debug\net10.0-windows\Probe.dll"
function F([string]$p){ '"' + $p.Replace([string][char]92,'/') + '"' }
$scr="$out\dok.scr"
@('SECURELOAD','0','_.NETLOAD',(F $PlanDll),'_.NETLOAD',(F $probe),'KP_DOK','_.QUIT','_Y') | Set-Content -Encoding ascii $scr
$env:KP_RESULT="$out\dok.txt"; Remove-Item $env:KP_RESULT -ErrorAction SilentlyContinue
$p=Start-Process 'C:\Program Files\Autodesk\AutoCAD 2027\accoreconsole.exe' -ArgumentList "/i `"$Dwg`" /s `"$scr`" /l en-US" -PassThru -NoNewWindow -RedirectStandardOutput "$out\dok.console.log"
if(-not $p.WaitForExit(200000)){ $p.Kill(); 'ZAMAN ASIMI' }
if(Test-Path $env:KP_RESULT){Get-Content $env:KP_RESULT -Encoding UTF8}else{'sonuc yok'}
