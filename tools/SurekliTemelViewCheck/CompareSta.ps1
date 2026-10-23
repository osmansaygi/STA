param([string]$Support,[string]$Sta)
# Mesnet ilave karşılaştırması: bizim (alt+üst, adet x çap) ile STA4CAD "ila." etiketleri (alt/üst belirtilmez).
$biz=@{}; $staMap=@{}
foreach($l in Get-Content $Support){
 if($l -notmatch '^SUP T(\d+) (\S+) alt=(\d+)x(\d+) ust=(\d+)x(\d+)'){continue}
 $k="T$($matches[1]) $($matches[2])"; if(-not $biz.ContainsKey($k)){$biz[$k]=New-Object System.Collections.Generic.List[string]}
 if([int]$matches[3] -gt 0){$biz[$k].Add("$($matches[3])x$($matches[4])")}
 if([int]$matches[5] -gt 0){$biz[$k].Add("$($matches[5])x$($matches[6])")}
}
foreach($l in Get-Content $Sta){
 if($l -notmatch '^STA T(\d+) (\S+) (\d+x\d+)'){continue}
 $k="T$($matches[1]) $($matches[2])"; if(-not $staMap.ContainsKey($k)){$staMap[$k]=New-Object System.Collections.Generic.List[string]}
 $staMap[$k].Add($matches[3])
}
$ok=0;$bad=0
foreach($k in ($biz.Keys + $staMap.Keys | Sort-Object -Unique {[int](($_ -split ' ')[0].Substring(1))},{$_})){
 $b=($biz[$k] | Sort-Object) -join '+'; $s=($staMap[$k] | Sort-Object) -join '+'
 if($b -eq $s){$ok++}else{$bad++; "{0,-14} biz={1,-16} sta={2}" -f $k,$b,$s}
}
"ESLESEN $ok, FARKLI $bad"
