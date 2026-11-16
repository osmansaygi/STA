param([ValidateSet('Plan','Yardimci')][string]$Mod)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$proj=if($Mod -eq 'Plan'){"$root/ST4_Plan_ID_Ciz/ST4_Plan_ID_Ciz.csproj"}else{"$root/ST4_Yardimci/ST4_Yardimci.csproj"}
$t=if($Mod -eq 'Plan'){'p27o'}else{'y27o'}
$obj=Join-Path $PSScriptRoot "${t}obj/"; $bin=Join-Path $PSScriptRoot "$t/"
New-Item -ItemType Directory -Force $obj,$bin | Out-Null
& dotnet build $proj -c Release -v minimal "-p:BaseIntermediateOutputPath=$obj" "-p:MSBuildProjectExtensionsPath=$obj" "-p:OutputPath=$bin" "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Isolated.targets" -p:AutoCAD2027Build=true -p:OfisPaketi=true *> (Join-Path $PSScriptRoot "$t.log")
if($LASTEXITCODE -ne 0){throw "$Mod build failed"}
