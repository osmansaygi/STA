$enc=[Text.Encoding]::GetEncoding(1254); $ok=0; $bad=0; $ex=@()
foreach($line in Get-Content scan4.txt){ $p=$line.Split('|'); $a=$p[3]; if(-not $a){continue}; $st4=$p[$p.Length-1]
 $g=[IO.Path]::ChangeExtension($st4,'GPR'); if(-not (Test-Path -LiteralPath $g)){ $g=[IO.Path]::ChangeExtension($st4,'gpr') }
 $first=''; $n=0; foreach($x in [IO.File]::ReadLines($g,$enc)){ $n++; if($n -gt 20000){break}; $m=[regex]::Match($x,'\bA\d+\b'); if($m.Success){$first=$m.Value;break} }
 if($first -eq $a){$ok++}else{$bad++; if($ex.Count -lt 5){$ex+="$a -> $first : $st4"}} }
"dogru=$ok yanlis=$bad"; $ex
