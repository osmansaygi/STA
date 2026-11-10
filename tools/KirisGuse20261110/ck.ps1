# CK (Türkçe karakterli yol) eski/yeni karşılaştırması.
$s='D:\projeler\projeler2026\k'+[char]0x131+'z'+[char]0x131+'lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
$old=Join-Path $PSScriptRoot '..\..\NETLOAD\arsiv\GUNCEL_2027_20261110_160808_940\ST4_Plan_ID_Ciz_2027_20261109_205238.dll'
$old=(Resolve-Path $old).Path
& "$PSScriptRoot\RunKiris.ps1" -Name ck_eski -PlanDll $old -St4 $s | Select-String 'ok=|FAIL'
& "$PSScriptRoot\RunKiris.ps1" -Name ck_yeni -St4 $s | Select-String 'ok=|FAIL'
