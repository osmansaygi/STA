# Exercise the actual MSBuild copy target without compiling or touching shared outputs.
param([string]$TestRoot = (Join-Path $PSScriptRoot 'CrashFix20261023/deployment-test'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj'
$source = Join-Path $TestRoot 'source'
$deploy = Join-Path $TestRoot 'NETLOAD'
New-Item -ItemType Directory -Force $source,$deploy | Out-Null
$source = (Resolve-Path $source).Path + [IO.Path]::DirectorySeparatorChar
$deploy = (Resolve-Path $deploy).Path
$assembly = 'ST4_Plan_ID_Ciz_deployment_fixture'
foreach ($name in @("$assembly.dll","$assembly.pdb",'NetTopologySuite.dll','Microsoft.Bcl.HashCode.dll','System.Buffers.dll','System.Memory.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Text.Encoding.CodePages.dll')) {
    Set-Content -LiteralPath (Join-Path $source $name) -Value "deployment fixture $name"
}
$sentinel = Join-Path $deploy 'root-must-stay.txt'
Set-Content -LiteralPath $sentinel -Value 'Existing root contents stay intact'
$before = (Get-FileHash -LiteralPath $sentinel).Hash
$argsBase = @('msbuild',$project,'-t:CopyToNetloadFolder','-v:quiet',"-p:OutputPath=$source","-p:NetloadRootDir=$deploy","-p:AssemblyName=$assembly")
& dotnet @argsBase '-p:AutoCAD2027Build=false'
if ($LASTEXITCODE -ne 0) { throw 'Legacy copy target failed' }
foreach ($name in @("$assembly.dll","$assembly.pdb",'NetTopologySuite.dll')) {
    $dest = Join-Path $deploy "Legacy-net48/$name"
    if (!(Test-Path -LiteralPath $dest)) { throw "Missing legacy output: $name" }
    if ((Get-FileHash -LiteralPath $dest).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $name)).Hash) { throw "Copy changed bytes: $name" }
}
if (Get-ChildItem -LiteralPath $deploy -File -Filter '*.dll') { throw 'Legacy DLL leaked into NETLOAD root' }
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $before) { throw 'Root contents changed' }
Write-Output 'PASS: legacy DLL/PDB and dependencies go to Legacy-net48; root untouched; hashes equal.'
$newDeploy = Join-Path $TestRoot '2027-no-auto-copy'
& dotnet @argsBase '-p:AutoCAD2027Build=true' "-p:NetloadRootDir=$newDeploy"
if ($LASTEXITCODE -ne 0) { throw '2027 target evaluation failed' }
if (Test-Path -LiteralPath $newDeploy) { throw '2027 unexpectedly copied to legacy/root deployment' }
Write-Output 'PASS: AutoCAD 2027 build does not run the legacy copy target.'
