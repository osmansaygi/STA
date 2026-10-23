[CmdletBinding()]
param(
    [ValidateSet('None', 'Legacy', 'Yardimci2027')]
    [string]$BuildTarget = 'None',
    [switch]$Analyze
)

$ErrorActionPreference = 'Stop'
if ($Analyze -and $BuildTarget -eq 'None') {
    throw '-Analyze için -BuildTarget Legacy veya Yardimci2027 gerekli.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$tests = @(
    'Test-YardimciStartup.ps1',
    'Test-StaStartup.ps1',
    'Test-YardimciDependencies.ps1',
    'Test-BaDimensionStyles.ps1',
    'Test-TemelDetayGeometry.ps1',
    'Test-TemelIlaveKutuBol.ps1'
)

Push-Location $projectRoot
try {
    foreach ($test in $tests) {
        Write-Output "Kontrol: $test"
        # Add-Type fixtures must run in separate processes to avoid type collisions.
        & $pwshPath -NoProfile -File (Join-Path $PSScriptRoot $test)
        if ($LASTEXITCODE -ne 0) { throw "Test basarisiz: $test (exit $LASTEXITCODE)" }
    }

    if ($BuildTarget -ne 'None') {
        $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
        $projects = @('ST4_Yardimci/ST4_Yardimci.csproj')
        if ($BuildTarget -eq 'Legacy') { $projects += 'ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj' }
        foreach ($project in $projects) {
            $buildArgs = @('build', $project, '-c', 'Debug', '--no-restore', '-v', 'minimal')
            if ($BuildTarget -eq 'Yardimci2027') { $buildArgs += '-p:AutoCAD2027Build=true' }
            if ($Analyze) {
                $buildArgs += @('-p:EnableNETAnalyzers=true', '-p:AnalysisLevel=latest', '-p:RunAnalyzersDuringBuild=true')
            }
            Write-Output "Derleme: $project / $BuildTarget"
            & $dotnetPath @buildArgs
            if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz: $project (exit $LASTEXITCODE)" }
        }
    }
    Write-Output "PASS: mevcut regresyonlar; derleme hedefi=$BuildTarget; analiz=$Analyze. Gercek AutoCAD testi yapilmadi."
}
finally {
    Pop-Location
}
