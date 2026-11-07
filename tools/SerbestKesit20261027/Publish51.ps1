# tools/Publish-CurrentNetload2027.ps1 adımlarının PS 5.1 karşılığı (pwsh yok; PEReader Add-Type edilemiyor).
param([Parameter(Mandatory=$true)][string]$YardimciDll,[Parameter(Mandatory=$true)][string]$PlanDll)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$netload=Join-Path $root 'NETLOAD'; $current=Join-Path $netload 'GUNCEL_2027'
$sources=@((Resolve-Path -LiteralPath $YardimciDll).Path,(Resolve-Path -LiteralPath $PlanDll).Path)
$prefixes=@('ST4_Yardimci_2027','ST4_Plan_ID_Ciz_2027')
$files=@{}; $plugins=@()
for($i=0;$i -lt 2;$i++){
  $source=$sources[$i]
  $name=[Reflection.AssemblyName]::GetAssemblyName($source).Name
  $bytes=[IO.File]::ReadAllBytes($source)
  $fw=if([Text.Encoding]::ASCII.GetString($bytes).Contains('.NETCoreApp,Version=v10.0')){'.NETCoreApp,Version=v10.0'}else{''}
  if(!$name.StartsWith($prefixes[$i]) -or $fw -ne '.NETCoreApp,Version=v10.0'){ throw "Wrong module/framework: $source ($name, $fw)" }
  $candidates=@(Get-Item -LiteralPath $source)
  $pdb=[IO.Path]::ChangeExtension($source,'.pdb'); if(Test-Path -LiteralPath $pdb){$candidates+=Get-Item -LiteralPath $pdb}
  $candidates+=Get-ChildItem -LiteralPath (Split-Path $source) -File -Filter '*.dll' | Where-Object { $_.Name -notmatch '^ST4_' -and $_.Name -notmatch '^(Ac|Ad)' -and $_.Name -notmatch '^System\.' }
  foreach($file in $candidates){
    $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
    if($files.ContainsKey($file.Name) -and $files[$file.Name].Hash -ne $hash){ throw "Dependency conflict: $($file.Name)" }
    $files[$file.Name]=@{Source=$file.FullName;Hash=$hash}
  }
  $plugins+=[pscustomobject]@{Module=$prefixes[$i];Name=[IO.Path]::GetFileName($source);Framework=$fw;Source=$source;SHA256=(Get-FileHash -LiteralPath $source).Hash}
}
$stage=Join-Path $netload ('.GUNCEL_2027-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach($n in $files.Keys){ $dest=Join-Path $stage $n; Copy-Item -LiteralPath $files[$n].Source -Destination $dest; if((Get-FileHash -LiteralPath $dest).Hash -ne $files[$n].Hash){ throw "Copy verification failed: $n" } }
$manifest=[pscustomobject]@{Updated=[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow,'Turkey Standard Time').ToString('yyyy-MM-dd HH:mm:ss');AutoCAD=2027;Plugins=$plugins;Files=$files}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'surumler.json') -Encoding utf8
$oku = (Get-Content -LiteralPath (Join-Path $current 'OKU.txt') -Encoding utf8 -Raw)
$oku = [regex]::Replace($oku,'ST4_Yardimci_2027_\d{8}_\d{6}\.dll',$plugins[0].Name)
$oku = [regex]::Replace($oku,'ST4_Plan_ID_Ciz_2027_\d{8}_\d{6}\.dll',$plugins[1].Name)
Set-Content -LiteralPath (Join-Path $stage 'OKU.txt') -Value $oku -Encoding utf8 -NoNewline
$backup=Join-Path $netload ('arsiv\GUNCEL_2027_'+(Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
foreach($p in @($current,$stage,$backup)){ if(![IO.Path]::GetFullPath($p).StartsWith($netload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){ throw 'Deployment path outside NETLOAD' } }
$had=Test-Path -LiteralPath $current
if($had){ New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Move-Item -LiteralPath $current -Destination $backup }
try { Move-Item -LiteralPath $stage -Destination $current } catch { if($had -and !(Test-Path -LiteralPath $current)){ Move-Item -LiteralPath $backup -Destination $current }; throw }
foreach($n in $files.Keys){ if((Get-FileHash -LiteralPath (Join-Path $current $n)).Hash -ne $files[$n].Hash){ throw "Final hash mismatch: $n" } }
"PASS: two net10/2027 modules, matching dependencies, verified SHA256 copies: $current"
"backup: $backup"
$plugins | ForEach-Object { "$($_.Module): $($_.Name) $($_.SHA256)" }
