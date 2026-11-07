# KIRISCIZ formunu .kiris verisiyle açıp ekran görüntüsünü out\form_<ad>.png olarak kaydeder (AutoCAD'siz önizleme).
param([string[]]$Names=@('t1','t2'))
$d=$PSScriptRoot
& dotnet build "$d\FormOnizleme\FormOnizleme.csproj" -c Release -v q | Out-Null
if($LASTEXITCODE -ne 0){throw 'FormOnizleme build failed'}
$exe=(Get-ChildItem "$d\FormOnizleme\bin\Release" -Recurse -Filter FormOnizleme.exe | Select-Object -First 1).FullName
foreach($n in $Names){
  $png="$d\out\form_$n.png"; Remove-Item $png -ErrorAction SilentlyContinue
  $p=Start-Process $exe -ArgumentList "`"$png`"","`"$d\$n.kiris`"" -PassThru
  $p.WaitForExit(20000) | Out-Null
  "$n : $(Test-Path $png)"
}
