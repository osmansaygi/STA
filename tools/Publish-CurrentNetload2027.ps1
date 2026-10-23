[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$YardimciDll,
    [Parameter(Mandatory=$true)][string]$PlanDll
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$netload = Join-Path $root 'NETLOAD'
$current = Join-Path $netload 'GUNCEL_2027'

# Read PE metadata without loading either plugin into PowerShell/AutoCAD.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
public static class Netload2027Metadata {
 public static string[] Read(string path) {
  using(var stream=File.OpenRead(path)) using(var pe=new PEReader(stream)) {
   var md=pe.GetMetadataReader(); var asm=md.GetAssemblyDefinition(); string framework="";
   foreach(var handle in asm.GetCustomAttributes()) {
    var attr=md.GetCustomAttribute(handle);
    if(attr.Constructor.Kind!=HandleKind.MemberReference) continue;
    var member=md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
    if(member.Parent.Kind!=HandleKind.TypeReference) continue;
    var type=md.GetTypeReference((TypeReferenceHandle)member.Parent);
    if(md.GetString(type.Namespace)!="System.Runtime.Versioning" || md.GetString(type.Name)!="TargetFrameworkAttribute") continue;
    var blob=md.GetBlobReader(attr.Value); blob.ReadUInt16(); framework=blob.ReadSerializedString();
   }
   return new[]{md.GetString(asm.Name), framework};
  }
 }
}
'@

$sources = @((Resolve-Path -LiteralPath $YardimciDll).Path, (Resolve-Path -LiteralPath $PlanDll).Path)
$prefixes = @('ST4_Yardimci_2027','ST4_Plan_ID_Ciz_2027')
$files = @{}
$plugins = @()
for ($i=0; $i -lt 2; $i++) {
    $source=$sources[$i]
    $info=[Netload2027Metadata]::Read($source)
    if (!$info[0].StartsWith($prefixes[$i]) -or $info[1] -ne '.NETCoreApp,Version=v10.0') {
        throw "Wrong module/framework: $source ($($info -join ', '))"
    }
    $candidates=@(Get-Item -LiteralPath $source)
    $pdb=[IO.Path]::ChangeExtension($source,'.pdb')
    if (Test-Path -LiteralPath $pdb) { $candidates += Get-Item -LiteralPath $pdb }
    $candidates += Get-ChildItem -LiteralPath (Split-Path $source) -File -Filter '*.dll' | Where-Object {
        $_.Name -notmatch '^ST4_' -and $_.Name -notmatch '^(Ac|Ad)' -and $_.Name -notmatch '^System\.'
    }
    foreach ($file in $candidates) {
        $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
        if ($files.ContainsKey($file.Name) -and $files[$file.Name].Hash -ne $hash) { throw "Dependency conflict: $($file.Name)" }
        $files[$file.Name]=@{Source=$file.FullName;Hash=$hash}
    }
    $plugins += [pscustomobject]@{Module=$prefixes[$i];Name=[IO.Path]::GetFileName($source);Framework=$info[1];Source=$source;SHA256=(Get-FileHash -LiteralPath $source).Hash}
}

$stage=Join-Path $netload ('.GUNCEL_2027-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in $files.Keys) {
    $dest=Join-Path $stage $name
    Copy-Item -LiteralPath $files[$name].Source -Destination $dest
    if ((Get-FileHash -LiteralPath $dest).Hash -ne $files[$name].Hash) { throw "Copy verification failed: $name" }
}
$manifest=[pscustomobject]@{Updated=[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow,'Turkey Standard Time').ToString('yyyy-MM-dd HH:mm:ss');AutoCAD=2027;Plugins=$plugins;Files=$files}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'surumler.json') -Encoding utf8
@"
AutoCAD 2027 — GÜNCEL DLL SEÇİM KLASÖRÜ

AutoCAD'i kapatıp açın. NETLOAD ile bu klasörden sırayla:
1. $($plugins[0].Name)
2. $($plugins[1].Name)

Yalnız ST4_ ile başlayan bu iki DLL yüklenir. Diğer DLL'ler bağımlılıktır.
Dosya adı filtresine ST4*.dll yazarak yalnız bu iki dosyayı görebilirsiniz.
Aynı oturumda başka klasörden eski sürüm yüklemeyin.
"@ | Set-Content -LiteralPath (Join-Path $stage 'OKU.txt') -Encoding utf8

# Keep the previous package recoverable; check all move destinations are inside NETLOAD.
$backup=Join-Path $netload ('arsiv/GUNCEL_2027_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
foreach ($path in @($current,$stage,$backup)) {
    if (![IO.Path]::GetFullPath($path).StartsWith($netload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment path outside NETLOAD' }
}
$hadCurrent=Test-Path -LiteralPath $current
if ($hadCurrent) {
    New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
    Move-Item -LiteralPath $current -Destination $backup
}
try { Move-Item -LiteralPath $stage -Destination $current }
catch {
    if ($hadCurrent -and !(Test-Path -LiteralPath $current)) { Move-Item -LiteralPath $backup -Destination $current }
    throw
}
Write-Output "PASS: two net10/2027 modules, matching dependencies, verified SHA256 copies: $current"
$plugins | ForEach-Object { Write-Output "$($_.Module): $($_.Name)" }
