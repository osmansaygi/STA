# PS 5.1: Publish-CurrentNetload2027 adımları. Plan yeni izole derleme, Yardımcı mevcut GUNCEL_2027'den korunur.
param([Parameter(Mandatory)][string]$PlanDll)
$ErrorActionPreference='Stop'
$r=Resolve-Path "$PSScriptRoot\..\.."
$netload="$r\NETLOAD"; $current="$netload\GUNCEL_2027"
$build=Split-Path $PlanDll
$yar=(Get-ChildItem "$current\ST4_Yardimci_2027_*.dll" | Sort-Object Name | Select-Object -Last 1).FullName
$files=[ordered]@{}
function Add([string]$p){ $files[(Split-Path $p -Leaf)]=$p }
Add $PlanDll; Add ([IO.Path]::ChangeExtension($PlanDll,'.pdb'))
Add $yar; Add ([IO.Path]::ChangeExtension($yar,'.pdb'))
Add "$build\NetTopologySuite.dll"
Get-ChildItem "$build\UglyToad.PdfPig*.dll" | ForEach-Object { Add $_.FullName }
$plugins=@()
foreach($p in @($PlanDll,$yar)){
  $an=[Reflection.AssemblyName]::GetAssemblyName($p)
  $txt=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($p))
  if($txt -notmatch '\.NETCoreApp,Version=v10\.0'){ throw "net10 değil: $p" }
  $plugins+=[ordered]@{Module=$an.Name;Name=(Split-Path $p -Leaf);Framework='.NETCoreApp,Version=v10.0';Source=$p;SHA256=(Get-FileHash $p -Algorithm SHA256).Hash}
}
$stage=Join-Path $netload ('.GUNCEL_2027-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$man=[ordered]@{}
foreach($n in $files.Keys){
  $src=$files[$n]; if(-not (Test-Path $src)){ throw "yok: $src" }
  Copy-Item -LiteralPath $src -Destination "$stage\$n"
  $h1=(Get-FileHash $src -Algorithm SHA256).Hash; $h2=(Get-FileHash "$stage\$n" -Algorithm SHA256).Hash
  if($h1 -ne $h2){ throw "hash: $n" }
  $man[$n]=[ordered]@{Hash=$h1;Source=$src}
}
Copy-Item "$current\OKU.txt" "$stage\OKU.txt"
[ordered]@{Updated=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss');AutoCAD=2027;Plugins=$plugins;Files=$man} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$stage\surumler.json" -Encoding utf8
$backup=Join-Path $netload ('arsiv\GUNCEL_2027_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
Move-Item -LiteralPath $current -Destination $backup
try { Move-Item -LiteralPath $stage -Destination $current } catch { Move-Item -LiteralPath $backup -Destination $current; throw }
foreach($n in $files.Keys){ if((Get-FileHash "$current\$n" -Algorithm SHA256).Hash -ne $man[$n].Hash){ throw "son hash: $n" } }
"PASS backup=$(Split-Path $backup -Leaf)"
Get-ChildItem "$current\ST4_*.dll" | ForEach-Object Name
