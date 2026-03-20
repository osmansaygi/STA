# ST4_Plan_ID_Ciz her derlemede tarihli DLL uretir; bin klasorunu sisirir.
# En yeni $Keep adet ST4_Plan_ID_Ciz_*.dll haricindekileri siler (pdb dahil).
param([int]$Keep = 8)
$bin = Join-Path $PSScriptRoot "..\ST4_Plan_ID_Ciz\bin\Debug\net48"
if (-not (Test-Path $bin)) { Write-Host "Klasor yok: $bin"; exit 0 }
$dlls = Get-ChildItem -Path $bin -Filter "ST4_Plan_ID_Ciz_*.dll" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
if ($dlls.Count -le $Keep) { Write-Host "Temizlenecek eski DLL yok (toplam $($dlls.Count))."; exit 0 }
$remove = $dlls | Select-Object -Skip $Keep
foreach ($f in $remove) {
    Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
    $pdb = [System.IO.Path]::ChangeExtension($f.FullName, "pdb")
    if (Test-Path $pdb) { Remove-Item -LiteralPath $pdb -Force -ErrorAction SilentlyContinue }
}
Write-Host "Silindi: $($remove.Count) eski DLL (son $Keep tutuldu)."
