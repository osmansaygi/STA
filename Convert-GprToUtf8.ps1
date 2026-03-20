# GPR ve PRN dosyalarini Windows-1254 (Turkce)'den UTF-8'e cevirir.
# Boylece Cursor/VS Code editorunde Turkce karakterler dogru gorunur.
# Kullanim: .\Convert-GprToUtf8.ps1   veya   .\Convert-GprToUtf8.ps1 -Path "C:\dosya\yolu"

param(
    [string]$Path = "."
)

$ErrorActionPreference = "Stop"
$turkey = [System.Text.Encoding]::GetEncoding(1254)
$utf8 = [System.Text.Encoding]::UTF8

$dir = [System.IO.Path]::GetFullPath($Path)
if (-not [System.IO.Directory]::Exists($dir)) {
    Write-Error "Klasor bulunamadi: $dir"
    exit 1
}

$count = 0
Get-ChildItem -Path $dir -File | Where-Object { $_.Extension -match '^\.(gpr|prn)$' } | ForEach-Object {
    $fullName = $_.FullName
    try {
        $lines = [System.IO.File]::ReadAllLines($fullName, $turkey)
        [System.IO.File]::WriteAllLines($fullName, $lines, $utf8)
        $count++
        Write-Host "UTF-8'e cevrildi: $($_.Name)"
    } catch {
        Write-Warning "Hata ($($_.Name)): $_"
    }
}

if ($count -eq 0) {
    Write-Host "Bu klasorde .gpr veya .prn dosyasi bulunamadi: $dir"
} else {
    Write-Host "Toplam $count dosya UTF-8 olarak kaydedildi."
}
