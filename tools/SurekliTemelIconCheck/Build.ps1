$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$isolatedObj = Join-Path $PSScriptRoot 'obj/'
$isolatedBin = Join-Path $PSScriptRoot 'build/'
New-Item -ItemType Directory -Force $isolatedObj, $isolatedBin | Out-Null
# Reuse the existing restore cache read-only; all generated files stay isolated.
Get-ChildItem (Join-Path $root 'ST4_Plan_ID_Ciz/obj') -File |
    Where-Object { $_.Name -match '\.(json|props|targets|cache)$' } |
    Copy-Item -Destination $isolatedObj
$argsBuild = @(
    'build', (Join-Path $root 'ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj'),
    '-c', 'Debug', '--no-restore', '-v', 'minimal',
    "-p:BaseIntermediateOutputPath=$isolatedObj",
    "-p:MSBuildProjectExtensionsPath=$isolatedObj",
    "-p:OutputPath=$isolatedBin",
    '-p:DefaultItemExcludes=bin/**%3Bobj/**',
    "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'Isolated.targets')"
)
& dotnet @argsBuild
if ($LASTEXITCODE -ne 0) { throw 'Isolated plugin build failed' }
$checkExe = Join-Path $PSScriptRoot 'Check.exe'
$checkSource = Join-Path $PSScriptRoot 'Check.cs'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /r:System.Drawing.dll "/out:$checkExe" $checkSource
if ($LASTEXITCODE -ne 0) { throw 'Icon check compilation failed' }
$dll = Get-ChildItem $isolatedBin -Filter 'ST4_Plan_ID_Ciz_*.dll' | Select-Object -First 1
& $checkExe $dll.FullName $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Icon check failed' }
