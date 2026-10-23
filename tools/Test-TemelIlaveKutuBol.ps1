$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
& $dotnet run --project (Join-Path $root 'tools/TemelIlaveKutuBolCheck/TemelIlaveKutuBolCheck.csproj') -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
