$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$python = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
if (!(Test-Path -LiteralPath $python)) { $python = (Get-Command python -ErrorAction Stop).Source }
& $python (Join-Path $PSScriptRoot 'Prepare-AllForms.py')
if ($LASTEXITCODE -ne 0) { throw 'Gerçek form test kopyaları hazırlanamadı.' }
& dotnet build (Join-Path $PSScriptRoot 'Smoke.csproj') -c Debug -v quiet --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Form testi derlenemedi.' }
& (Join-Path $PSScriptRoot 'bin/Debug/net48/Smoke.exe') $root | Tee-Object -FilePath (Join-Path $PSScriptRoot 'compact-test-result.txt')
if ($LASTEXITCODE -ne 0) { throw 'Form testi başarısız.' }