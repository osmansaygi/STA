# YP_OTP_08.st4: Floors Data kose akslari icin GetLineAngleRad-benzeri analiz (5 ondalik)
$path = Join-Path $PSScriptRoot "..\dosyalar\YP_OTP_08.st4"
$raw = Get-Content -LiteralPath $path -Encoding UTF8

function Split-CsvLine([string]$s) {
    return ($s -split ',') | ForEach-Object { $_.Trim() }
}

function Parse-Double([string]$t) {
    $t2 = $t -replace 'E','e'
    return [double]::Parse($t2, [System.Globalization.CultureInfo]::InvariantCulture)
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
$inCol = $false
$cols = @()

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
    if ($u -like '*column axis data*') { $inCol = $true; $i++; continue }
    if ($inCol -and $u.StartsWith('/')) { $inCol = $false; $i++; continue }
    if ($inCol -and $line.Length -gt 2) {
        $p = Split-CsvLine $line
        if ($p.Count -ge 3) {
            try {
                $ax = [int][double]$p[1]; $ay = [int][double]$p[2]
                if ($ax -ge 1001 -and $ax -le 1999 -and $ay -ge 2001 -and $ay -le 2999) {
                    $cols += ,@($ax, $ay)
                }
            } catch {}
        }
    }
    $i++
}

$nx = $headerNx
$ny = $headerNy
if (-not ($nx -gt 0 -and $ny -gt 0 -and $allValues.Count -ge $nx)) {
    $maxX = 1000; $maxY = 2000
    foreach ($c in $cols) {
        if ($c[0] -gt $maxX) { $maxX = $c[0] }
        if ($c[1] -gt $maxY) { $maxY = $c[1] }
    }
    $nx = [Math]::Max(0, $maxX - 1000)
    $ny = [Math]::Max(0, $maxY - 2000)
}

$axisById = @{}
for ($j = 0; $j -lt $allValues.Count; $j++) {
    if ($j -lt $nx) {
        $sl = if ($j -lt $allSlopes.Count) { -$allSlopes[$j] } else { 0.0 }
        $axisById[[int](1001 + $j)] = @{ Kind = 'X'; ValueCm = $allValues[$j]; Slope = $sl }
    }
    elseif ($j -lt $nx + $ny) {
        $sl = if ($j -lt $allSlopes.Count) { $allSlopes[$j] } else { 0.0 }
        $axisById[[int](2001 + ($j - $nx))] = @{ Kind = 'Y'; ValueCm = $allValues[$j]; Slope = $sl }
    }
}

$inFloors = $false
$slabs = @()
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
                $slabs += ,@($sid, $a1, $a2, $a3, $a4)
            } catch {}
        }
    }
}

Write-Host "nx=$nx ny=$ny axis_data_lines=$($allValues.Count) axes_built=$($axisById.Count)"
Write-Host ""
Write-Host "Kriter: 4 koseden en az 3unun dogrultu acisi (rad, 5 ondalik) ayni - ST4Parser + GetLineAngleRad"
Write-Host ""

function Max-SameRounded([double[]]$vals) {
    if ($vals.Count -eq 0) { return 0 }
    $keys = $vals | ForEach-Object { [Math]::Round($_, 5) }
    return ($keys | Group-Object | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
}

$qualAngle = @()
$qualSlope = @()
foreach ($s in $slabs) {
    $sid = $s[0]
    $ids = @($s[1], $s[2], $s[3], $s[4])
    $angleKeys = @()
    $slopeVals = @()
    foreach ($aid in $ids) {
        if ($aid -eq 0 -or -not $axisById.ContainsKey($aid)) { continue }
        $ax = $axisById[$aid]
        $ang = LineAngleRad $ax.Kind $ax.Slope
        $angleKeys += [Math]::Round($ang, 5)
        $slopeVals += $ax.Slope
    }
    if ($angleKeys.Count -lt 4) { continue }
    $maxAng = ($angleKeys | Group-Object | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
    $maxSl = Max-SameRounded $slopeVals
    $detail = @()
    for ($k = 0; $k -lt 4; $k++) {
        $aid = $ids[$k]
        if ($aid -eq 0 -or -not $axisById.ContainsKey($aid)) {
            $detail += "A$($k+1)=$aid?"
        } else {
            $ax = $axisById[$aid]
            $ang = LineAngleRad $ax.Kind $ax.Slope
            $detail += "A$($k+1)=$aid $($ax.Kind) s=$([Math]::Round($ax.Slope,6)) a=$([Math]::Round($ang,5))"
        }
    }
    $dstr = $detail -join ' | '
    if ($maxAng -ge 3) { $qualAngle += [PSCustomObject]@{ SlabId = $sid; MaxSame = $maxAng; Detail = $dstr } }
    if ($maxSl -ge 3) { $qualSlope += [PSCustomObject]@{ SlabId = $sid; MaxSame = $maxSl; Detail = $dstr } }
}

Write-Host "--- Ayni dogrultu acisi (5 dec) en az 3 ---"
$qualAngle = $qualAngle | Sort-Object SlabId
Write-Host "Sayi: $($qualAngle.Count)"
$qualAngle | ForEach-Object { Write-Host ('D' + $_.SlabId + '  maxSame=' + $_.MaxSame + '  ' + $_.Detail) }
Write-Host ""
Write-Host "--- Ayni STA4 slope (cizimdeki isaret, 5 dec) en az 3 ---"
$qualSlope = $qualSlope | Sort-Object SlabId
Write-Host "Sayi: $($qualSlope.Count)"
$qualSlope | ForEach-Object { Write-Host ('D' + $_.SlabId + '  maxSame=' + $_.MaxSame + '  ' + $_.Detail) }
Write-Host ""
Write-Host "--- Hepsi: max 2 ayni aci (ozet, ilk 15) ---"
$allSumm = @()
foreach ($s in $slabs) {
    $sid = $s[0]; $ids = @($s[1], $s[2], $s[3], $s[4])
    $angleKeys = @()
    foreach ($aid in $ids) {
        if ($aid -eq 0 -or -not $axisById.ContainsKey($aid)) { continue }
        $ax = $axisById[$aid]
        $angleKeys += [Math]::Round((LineAngleRad $ax.Kind $ax.Slope), 5)
    }
    if ($angleKeys.Count -lt 4) { continue }
    $maxAng = ($angleKeys | Group-Object | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
    $allSumm += [PSCustomObject]@{ SlabId = $sid; MaxSameAngle = $maxAng }
}
$allSumm | Sort-Object SlabId | Select-Object -First 15 | ForEach-Object { Write-Host ('D' + $_.SlabId + '  maxSameAngle=' + $_.MaxSameAngle) }
