# AutoCAD 2027 / net10 Yardımcı izole derleme: ortak bin/obj ve NETLOAD'a yazmaz.
param([switch]$Yayin)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$obj=Join-Path $PSScriptRoot $(if($Yayin){"yobj_yayin/"}else{"yobj/"})
$bin=Join-Path $PSScriptRoot $(if($Yayin){"ybuild_yayin/"}else{"ybuild/"})
$targets=Join-Path $PSScriptRoot $(if($Yayin){"Yayin.targets"}else{"Isolated.targets"})
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
& dotnet build "$root/ST4_Yardimci/ST4_Yardimci.csproj" -c Debug -v minimal -p:AutoCAD2027Build=true -p:UseSharedCompilation=false "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$targets"
if($LASTEXITCODE -ne 0){throw 'Yardimci 2027 build failed'}
Get-ChildItem "$bin\ST4_Yardimci_2027_*.dll" | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object FullName
