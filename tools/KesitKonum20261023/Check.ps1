# KESIT_ISTASYON satırlarından önce/sonra karşılaştırma: kiriş üstü kolon (K), ilave (I), bindirme (B) içine düşen kesitler.
# Yeni konum ihlalliyse arama aralığında (uçlardan max(5, %10)) daha az ağırlıklı (kolon 4, ilave 2, bindirme 1) yer yok mu, doğrular.
param([string[]]$Names=@('surekli-antetsiz','surekli-antetli','kiris-antetsiz','kiris-antetli'))
$ErrorActionPreference='Stop'
$inv=[Globalization.CultureInfo]::InvariantCulture
function Zones([string]$t){ $l=New-Object System.Collections.Generic.List[double[]]; if($t){ foreach($q in $t.Split(',')){ $p=$q.Split('~'); $l.Add([double[]]@([double]::Parse($p[0],$inv),[double]::Parse($p[1],$inv))) } }; ,$l }
function In($z,[double]$s){ foreach($r in $z){ if($s -gt $r[0] -and $s -lt $r[1]){return $true} }; $false }
function W($Zi,$Zb,$Zk,[double]$s){ (4*[int](In $Zk $s)) + (2*[int](In $Zi $s)) + [int](In $Zb $s) }
$out=@(); $fail=0
foreach($n in $Names){
  $f="$PSScriptRoot\$n.kesit.txt"; if(-not (Test-Path $f)){ $out+="$n : kesit.txt yok"; $fail++; continue }
  $c=@{eI=0;eB=0;eK=0;yI=0;yB=0;yK=0;n=0;moved=0;forced=0;bad=0}
  foreach($line in Get-Content $f){
    if($line -notmatch 'a=(\S+) b=(\S+) eski=(\S+)\[(..)\] yeni=(\S+)\[(..)\] I=(\S*) B=(\S*)( K=(\S*))?$'){continue}
    $a=[double]::Parse($Matches[1],$inv); $b=[double]::Parse($Matches[2],$inv)
    $e=[double]::Parse($Matches[3],$inv); $y=[double]::Parse($Matches[5],$inv)
    $Zi=Zones $Matches[7]; $Zb=Zones $Matches[8]; $Zk=Zones $Matches[10]
    $c.n++; if([math]::Abs($e-$y) -gt 0.05){$c.moved++}
    if(In $Zi $e){$c.eI++}; if(In $Zb $e){$c.eB++}; if(In $Zk $e){$c.eK++}
    if(In $Zi $y){$c.yI++}; if(In $Zb $y){$c.yB++}; if(In $Zk $y){$c.yK++}
    $wy=W $Zi $Zb $Zk $y
    if($wy -gt 0 -and ($b-$a) -ge 20){
      $k=[math]::Max(5.0,0.1*($b-$a)); $min=99
      for($s=$a+$k; $s -le $b-$k+1e-6; $s+=1.0){ $w=W $Zi $Zb $Zk $s; if($w -lt $min){$min=$w} }
      if($min -lt $wy){ $c.bad++; $out+="  DAHA IYI YER VARDI: $line" } else { $c.forced++ }
    }
  }
  if($c.bad -gt 0){$fail++}
  $out+=("{0}: kesit={1} tasinan={2} | ONCE kolon={9} ilave={3} bindirme={4} | SONRA kolon={10} ilave={5} bindirme={6} | sonra ihlal zorunlu={7} kacinilabilir={8}" -f $n,$c.n,$c.moved,$c.eI,$c.eB,$c.yI,$c.yB,$c.forced,$c.bad,$c.eK,$c.yK)
}
$out | Set-Content -Encoding utf8 "$PSScriptRoot\ozet.txt"; $out
if($fail){ throw "Check FAIL ($fail)" } else { 'PASS' }
