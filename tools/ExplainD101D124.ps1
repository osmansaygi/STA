$path = Join-Path $PSScriptRoot "..\dosyalar\YP_OTP_08.st4"
$raw = Get-Content -LiteralPath $path -Encoding UTF8
function Split-CsvLine([string]$s) { return ($s -split ',') | ForEach-Object { $_.Trim() } }
function Parse-Double([string]$t) {
    return [double]::Parse(($t -replace 'E', 'e'), [System.Globalization.CultureInfo]::InvariantCulture)
}
function LineAngleRad([string]$kind, [double]$slope) {
    $angleRad = if ($kind -eq 'X') { [Math]::Atan2(1.0, $slope) } else { [Math]::Atan2($slope, -1.0) }
    while ($angleRad -gt [Math]::PI / 2) { $angleRad -= [Math]::PI }
    while ($angleRad -le -[Math]::PI / 2) { $angleRad += [Math]::PI }
    return $angleRad
}

$allValues = New-Object System.Collections.Generic.List[double]
$allSlopes = New-Object System.Collections.Generic.List[double]
$headerNx = $null
$headerNy = $null
$inAxis = $false
$i = 0
foreach ($line in $raw) {
    $u = $line.Trim().ToLowerInvariant()
    if ($i -eq 4 -or $i -eq 5) {
        if ($line.Length -gt 0) {
            $p = Split-CsvLine $line
            if ($p.Count -ge 3) {
                try {
                    $ks = [int][double]$p[2]
                    if ($ks -eq 100 -or $ks -eq 1000) {
                        $headerNx = [int][double]$p[0]
                        $headerNy = [int][double]$p[1]
                    }
                } catch {}
            }
        }
    }
    if ($u.StartsWith('/axis data/')) { $inAxis = $true; $i++; continue }
    if ($inAxis -and $u.StartsWith('/circle axis/')) { $inAxis = $false; $i++; continue }
    if ($inAxis -and $line.Trim().Length -gt 0) {
        $p = Split-CsvLine $line
        if ($p.Count -ge 2) {
            try {
                $slope = Parse-Double $p[0]
                $val = Parse-Double $p[1]
                if ([Math]::Abs($val) -lt 100.0) {
                    if ([Math]::Abs($slope) -le 1e-9) { $val = [Math]::Round($val, 4) }
                    $val *= 100.0
                    $allValues.Add($val) | Out-Null
                    $allSlopes.Add($slope) | Out-Null
                }
            } catch {}
        }
    }
    $i++
}

$nx = $headerNx
$ny = $headerNy
$axisById = @{}
for ($j = 0; $j -lt $allValues.Count; $j++) {
    if ($j -lt $nx) {
        $sl = if ($j -lt $allSlopes.Count) { -$allSlopes[$j] } else { 0.0 }
        $axisById[[int](1001 + $j)] = @{ K = 'X'; S = $sl }
    }
    elseif ($j -lt $nx + $ny) {
        $sl = if ($j -lt $allSlopes.Count) { $allSlopes[$j] } else { 0.0 }
        $axisById[[int](2001 + ($j - $nx))] = @{ K = 'Y'; S = $sl }
    }
}

function Map-ForSlab($ids) {
    $d = @{}
    foreach ($aid in $ids) {
        if (-not $axisById.ContainsKey($aid)) { continue }
        $ax = $axisById[$aid]
        $a = LineAngleRad $ax.K $ax.S
        $k = [Math]::Round($a, 5).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        if (-not $d.ContainsKey($k)) { $d[$k] = 0 }
        $d[$k]++
    }
    return $d
}

function Show-Slab($sid, $ids) {
    Write-Host ""
    Write-Host "D$sid  Floors Data 9-12: $($ids -join ', ')"
    foreach ($aid in $ids) {
        $ax = $axisById[$aid]
        $a = LineAngleRad $ax.K $ax.S
        Write-Host "  Aks $aid ($($ax.K))  LineAngleRad (5 dec) = $([Math]::Round($a, 5))"
    }
    $m = Map-ForSlab $ids
    Write-Host "  Yuvarlanmis dogrultu sayimi (EKSEN gruplama multiset):"
    foreach ($kv in ($m.GetEnumerator() | Sort-Object Name)) {
        Write-Host "    $($kv.Key) rad  ->  $($kv.Value) aks"
    }
    $ok = $false
    foreach ($v in $m.Values) { if ($v -ge 2) { $ok = $true } }
    Write-Host "  Ic sart (en az 2 ayni dogrultu): $ok"
}

Show-Slab 101 @(1032, 1033, 2005, 2006)
Show-Slab 124 @(1044, 1004, 2003, 2004)

$m101 = Map-ForSlab @(1032, 1033, 2005, 2006)
$m124 = Map-ForSlab @(1044, 1004, 2003, 2004)
$sum = 0
foreach ($k in $m101.Keys) {
    if ($m124.ContainsKey($k)) { $sum += [Math]::Min($m101[$k], $m124[$k]) }
}
Write-Host ""
Write-Host "Iki dosemenin DOGRUDAN multiset kesisimi (101 ile 124 arasi): $sum"
Write-Host "(0 ise dogrudan ortak yuvarlanmis dogrultu yok; yine de zincirleme grupta bulunabilirler.)"
Write-Host ""

# Tam greedy simulasyon (C# BuildDeneme1EksenGrupNoBySlabId)
$inFloors = $false
$slabRows = @()
foreach ($line in $raw) {
    $u = $line.Trim().ToLowerInvariant()
    if ($u -like '*floors data*') { $inFloors = $true; continue }
    if ($inFloors -and $u.StartsWith('/')) { $inFloors = $false; continue }
    if ($inFloors -and $line.Length -gt 2) {
        $p = Split-CsvLine $line
        if ($p.Count -ge 12) {
            try {
                $sid = [int][double]$p[0]
                if ($sid -le 0) { continue }
                $a1 = [int][double]$p[8]; $a2 = [int][double]$p[9]; $a3 = [int][double]$p[10]; $a4 = [int][double]$p[11]
                $slabRows += ,@($sid, $a1, $a2, $a3, $a4)
            } catch {}
        }
    }
}

function Clone-Map($src) {
    $d = @{}
    foreach ($k in $src.Keys) { $d[$k] = $src[$k] }
    return $d
}
function Add-MapInPlace($target, $add) {
    foreach ($k in $add.Keys) {
        if (-not $target.ContainsKey($k)) { $target[$k] = 0 }
        $target[$k] += $add[$k]
    }
}
function Intersection-Sum2($slabMap, $groupUnion) {
    $s = 0
    foreach ($k in $slabMap.Keys) {
        if ($groupUnion.ContainsKey($k)) { $s += [Math]::Min($slabMap[$k], $groupUnion[$k]) }
    }
    return $s
}
function Has-AtLeast-Two-Same($m) {
    foreach ($v in $m.Values) { if ($v -ge 2) { return $true } }
    return $false
}

$angleMaps2 = @{}
foreach ($s in $slabRows) {
    $angleMaps2[$s[0]] = Map-ForSlab @($s[1], $s[2], $s[3], $s[4])
}

$slabIdsSorted = ($slabRows | ForEach-Object { $_[0] } | Sort-Object -Unique)
$groups = New-Object System.Collections.ArrayList
foreach ($sid in $slabIdsSorted) {
    $map = $angleMaps2[$sid]
    if (-not (Has-AtLeast-Two-Same $map)) {
        $m0 = New-Object System.Collections.Generic.List[int]
        $m0.Add($sid) | Out-Null
        [void]$groups.Add(@{ Union = (Clone-Map $map); Members = $m0 })
        continue
    }
    $bestIdx = -1
    $bestScore = -1
    for ($gi = 0; $gi -lt $groups.Count; $gi++) {
        $sc = Intersection-Sum2 $map $groups[$gi].Union
        if ($sc -ge 2 -and $sc -gt $bestScore) { $bestScore = $sc; $bestIdx = $gi }
    }
    if ($bestIdx -ge 0) {
        Add-MapInPlace $groups[$bestIdx].Union $map
        [void]$groups[$bestIdx].Members.Add($sid)
    } else {
        $m1 = New-Object System.Collections.Generic.List[int]
        $m1.Add($sid) | Out-Null
        [void]$groups.Add(@{ Union = (Clone-Map $map); Members = $m1 })
    }
}

$g101 = $null
$g124 = $null
foreach ($g in $groups) {
    if ($g.Members -contains 101) { $g101 = $g }
    if ($g.Members -contains 124) { $g124 = $g }
}
Write-Host "=== Greedy grup (tum ST4 Floors Data, slabId artan sira) ==="
if ($g101 -eq $g124 -and $g101 -ne $null) {
    Write-Host "101 ve 124 AYNI grupta."
    Write-Host "Grup uyeleri: $($g101.Members -join ', ')"
} elseif ($g101 -ne $null -and $g124 -ne $null) {
    Write-Host "101 ve 124 FARKLI gruplarda."
    Write-Host "101 grubu: $($g101.Members -join ', ')"
    Write-Host "124 grubu: $($g124.Members -join ', ')"
}
Write-Host ""
