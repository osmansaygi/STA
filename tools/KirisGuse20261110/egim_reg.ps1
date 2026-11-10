# Eğimli kiriş değişikliği regresyonu: yayındaki (eski) Plan DLL ile yeni izole derlemenin entity dökümü karşılaştırması.
param([string]$Old='')
$d=$PSScriptRoot
if(-not $Old){$Old=(Get-ChildItem "$d\..\..\NETLOAD\GUNCEL_2027\ST4_Plan_ID_Ciz_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName}
$ck='D:\projeler\projeler2026\k'+[char]0x131+'z'+[char]0x131+'lkaya\20827_2_TICARI\STATIK\CK_20827_2_01.st4'
$list=@(
 @('ck',$ck),
 @('hho','D:\projeler\projeler2026\ahmet_kustepe\hasan_huseyin_ozgul\statik\HHO_01.st4'),
 @('nulk','D:\projeler\projeler2026\ahmet_kustepe\nevzat_ulker\STATIK\N_ULK_02.ST4'),
 @('bys','D:\projeler\projeler2026\oncan_mimarlik\beysehir_ticari\SON_STATIK\BYS_T9.ST4'),
 @('bzk','D:\projeler\projeler2026\oncan_mimarlik\bozkir_ds\statik\BZK_ODS_02.ST4'),
 @('atfl','D:\projeler\projeler2026\ahmet_kustepe\adem_tafli\STATIK\A_TFL.st4'))
$env:KK_PNG=''
foreach($p in $list){
  $a=(& "$d\RunKiris.ps1" -Name ($p[0]+'_e_eski') -PlanDll $Old -St4 $p[1] | Select-String 'ok=') -join ''
  $b=(& "$d\RunKiris.ps1" -Name ($p[0]+'_e_yeni') -St4 $p[1] | Select-String 'ok=') -join ''
  $fa="$d\reg\$($p[0])_e_eski.ent.txt"; $fb="$d\reg\$($p[0])_e_yeni.ent.txt"
  $same = (Test-Path $fa) -and (Test-Path $fb) -and ((Get-FileHash $fa).Hash -eq (Get-FileHash $fb).Hash)
  "{0}: eski[{1}] yeni[{2}] birebir={3}" -f $p[0],$a,$b,$same
}
