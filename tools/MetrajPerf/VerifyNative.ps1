param([switch]$CompareOnly, [string]$LargeRetryResult)
$ErrorActionPreference = 'Stop'
$core = 'C:/Program Files/Autodesk/AutoCAD 2027/accoreconsole.exe'
foreach ($count in @(1,31,32,33,1000)) {
    $results = @{}
    foreach ($version in @('before','after')) {
        $env:METRAJ_COUNT = [string]$count
        $env:METRAJ_PLUGIN = (Resolve-Path "$PSScriptRoot/build/$version/ST4_Yardimci.dll").Path
        $env:METRAJ_RESULT = Join-Path $PSScriptRoot "case-$count-$version.txt"
        if ($CompareOnly -and $count -eq 1000 -and $version -eq 'after' -and $LargeRetryResult) {
            $env:METRAJ_RESULT = (Resolve-Path $LargeRetryResult).Path
        }
        $results[$version] = $env:METRAJ_RESULT
        if (!$CompareOnly) {
            & $core /s "$PSScriptRoot/run.scr" /l en-US *> "$PSScriptRoot/case-$count-$version-console.log"
        }
        if (!(Test-Path $env:METRAJ_RESULT) -or !(Select-String $env:METRAJ_RESULT -Pattern '^COMPLETE$' -Quiet)) {
            throw "Native test incomplete: $count $version"
        }
    }
    foreach ($color in @('False','True')) {
        foreach ($kind in @('content','order')) {
            $old = (Get-FileHash "$($results['before']).$color.$kind.txt").Hash
            $new = (Get-FileHash "$($results['after']).$color.$kind.txt").Hash
            if ($old -ne $new) { throw "Output differs: $count $color $kind" }
        }
    }
    $oldTotals = Get-Content $results['before'] | Where-Object { $_ -match '^TABLE' } | ForEach-Object { $_ -replace ' ms=\d+', '' }
    $newTotals = Get-Content $results['after'] | Where-Object { $_ -match '^TABLE' } | ForEach-Object { $_ -replace ' ms=\d+', '' }
    if (Compare-Object $oldTotals $newTotals) { throw "Totals differ: $count" }
    Write-Output "PASS: $count positions, color on/off; quantities, geometry, colors, links and full draw order identical."
}
