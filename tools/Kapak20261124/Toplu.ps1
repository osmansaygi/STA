# Birden çok mimari projede kapak okumasını dener (çizer, yazdırmaz); okunan alanları cikti\toplu.txt'ye yazar.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Kapak20261124\Toplu.ps1
param([string[]]$Dwg = @(
    'D:\projeler\projeler2026\hakan_yorgun\mehmet_sevic\mimari_20260825\ms_a_blk.dwg',
    'D:\projeler\projeler2026\ahmet_kustepe\adem_tafli\mimari_20261012\17.08.2026 - ADEM TAFLI MİMARİ.dwg',
    'D:\projeler\projeler2026\ali_babaoglu\vakiflar\mimari_202611116\000_Mimari_Proje_Belediye_Teslim.dwg',
    'D:\projeler\projeler2026\furkan_mimar\celalettin_pulluk\mimari_20261001\CELALETTINPULUKMIMARIPROJE22.09.dwg',
    'D:\projeler\projeler2026\hakan_yorgun\mehmet_sevic\mimari_20261123\MEHMET SEVİÇ VE HİSS.- 16555 ADA - 1 PARSEL - MİMARİ PROJE-REVİZE 4 - 23112026.dwg',
    'D:\projeler\projeler2026\ahmet_kustepe\hasan_huseyin_ozgul\mimari_20261028\20.10.2026 HASAN HÜSEYİN ÖZGÜL MİMARİ.dwg',
    'D:\projeler\projeler2026\hulusi_uzbas\villa\mimari_20260806\260624_selmankon_ruhsat.dwg',
    'D:\projeler\projeler2026\musa_karacan\arya\mimari_20261001\Musa KARACAN - Mimari Proje - 27.09.2026.dwg'))
$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$sonuc = New-Object System.Collections.Generic.List[string]
$i = 0
foreach ($yol in $Dwg) {
    $i++
    $ad = "toplu$i"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & "$d\Run.ps1" -Dwg $yol -Name $ad -Sira 1 -Plotsuz | Out-Null
    $sonuc.Add("# $([IO.Path]::GetFileName($yol))  ($([int]$sw.Elapsed.TotalSeconds) sn, dwg=$(Test-Path "$d\cikti\$ad.dwg"))")
    Get-Content "$d\cikti\$ad.console.log" -Encoding Unicode | Where-Object { $_ -match '^\s*KAPAK: \[|^\s+\S.* = |okunamad|HATA|rror' } | ForEach-Object { $sonuc.Add('   ' + $_.Trim()) }
}
$sonuc | Set-Content -Encoding UTF8 "$d\cikti\toplu.txt"
$sonuc
