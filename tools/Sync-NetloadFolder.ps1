# NETLOAD klasörünü günceller: eklenti DLL'leri + Plan_ID bağımlılıkları.
# Kullanım: powershell -File tools\Sync-NetloadFolder.ps1

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dest = Join-Path $root "NETLOAD"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$planOut = Join-Path $root "ST4_Plan_ID_Ciz\bin\Debug\net48"
$yardOut = Join-Path $root "ST4_Yardimci\bin\Debug\net48"

Get-ChildItem $dest -Filter "ST4_Plan_ID_Ciz_*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $dest -Filter "ST4_Plan_ID_Ciz_*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $dest -Filter "ST4_Yardimci*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $dest -Filter "ST4_Yardimci*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

# Yardımcı: en son tarihli ST4_Yardimci_*.dll
if (Test-Path $yardOut) {
    $latestY = Get-ChildItem $yardOut -Filter "ST4_Yardimci_*.dll" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latestY -and (Test-Path (Join-Path $yardOut "ST4_Yardimci.dll"))) {
        $latestY = Get-Item (Join-Path $yardOut "ST4_Yardimci.dll")
    }
    if ($latestY) {
        Copy-Item $latestY.FullName $dest -Force
        $pdb = [IO.Path]::ChangeExtension($latestY.FullName, ".pdb")
        if (Test-Path $pdb) { Copy-Item $pdb $dest -Force }
        Write-Host "OK  $($latestY.Name)"
    } else {
        Write-Host "YOK ST4_Yardimci_*.dll (once derleyin)"
    }
}

if (Test-Path $planOut) {
    $latest = Get-ChildItem $planOut -Filter "ST4_Plan_ID_Ciz_*.dll" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) {
        Copy-Item $latest.FullName $dest -Force
        $pdb = [IO.Path]::ChangeExtension($latest.FullName, ".pdb")
        if (Test-Path $pdb) { Copy-Item $pdb $dest -Force }
        Write-Host "OK  $($latest.Name)"

        $deps = @(
            "NetTopologySuite.dll",
            "Microsoft.Bcl.HashCode.dll",
            "System.Buffers.dll",
            "System.Memory.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Text.Encoding.CodePages.dll",
            "UglyToad.PdfPig.dll",
            "UglyToad.PdfPig.Core.dll",
            "UglyToad.PdfPig.DocumentLayoutAnalysis.dll",
            "UglyToad.PdfPig.Fonts.dll",
            "UglyToad.PdfPig.Package.dll",
            "UglyToad.PdfPig.Tokenization.dll",
            "UglyToad.PdfPig.Tokens.dll"
        )
        foreach ($d in $deps) {
            $src = Join-Path $planOut $d
            if (Test-Path $src) { Copy-Item $src $dest -Force }
        }
        Get-ChildItem $planOut -Filter "ST4_Aks_Ciz_CSharp*.dll" -ErrorAction SilentlyContinue |
            ForEach-Object { Copy-Item $_.FullName $dest -Force }
    } else {
        Write-Host "YOK ST4_Plan_ID_Ciz_*.dll (once derleyin)"
    }
}

Write-Host ""
Write-Host "NETLOAD: $dest"
