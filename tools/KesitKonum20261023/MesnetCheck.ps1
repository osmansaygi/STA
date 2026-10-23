# MESNET_BINDIRME satırları: temel iç mesnetlerinde montaj/düz bindirmesi ≥ ℓb mi? Önce/sonra sayım.
param([string[]]$Names=@('m0','b-surekli-antetsiz','b-surekli-antetli'),[string[]]$Zorunlu=@('b-surekli-antetsiz','b-surekli-antetli'))
$ErrorActionPreference='Stop'
$inv=[Globalization.CultureInfo]::InvariantCulture
$fail=0; $out=@()
foreach($n in $Names){
  $f="$PSScriptRoot\$n.mesnet.txt"; if(-not (Test-Path $f)){ $out+="$n : mesnet.txt yok"; $fail++; continue }
  $c=@{gecer=0;yeter=0;kisa=0}
  foreach($line in Get-Content $f){
    if($line -match ' (mon|duz) gecer'){ $c.gecer++; continue }
    if($line -match ' (mon|duz) bindirme=(\S+) lb=(\S+)'){
      $b=[double]::Parse($Matches[2],$inv); $lb=[double]::Parse($Matches[3],$inv)
      if($b -ge $lb-0.5){$c.yeter++} else {$c.kisa++; if($Zorunlu -contains $n){ $out+="  KISA: $line" }}
    }
  }
  if($Zorunlu -contains $n -and $c.kisa -gt 0){$fail++}
  $out+=("{0}: tek cubuk gecer={1} | bindirme>=lb={2} | bindirme<lb veya ayrik={3}" -f $n,$c.gecer,$c.yeter,$c.kisa)
}
$out | Set-Content -Encoding utf8 "$PSScriptRoot\mesnet-ozet.txt"; $out
if($fail){ throw "MesnetCheck FAIL ($fail)" } else { 'PASS' }
