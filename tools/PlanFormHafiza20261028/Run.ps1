param([Parameter(Mandatory)][string]$PlanDll, [ValidateSet('write','read','corrupt')][string]$Phase)
$ErrorActionPreference='Stop'
$probeScript=Join-Path $PSScriptRoot ($Phase+'.scr')
$probeDll=Join-Path $PSScriptRoot 'Native/bin/Debug/net10.0-windows/Native.dll'
@('SECURELOAD','0','_.NETLOAD',('"'+(Resolve-Path $PlanDll).Path+'"'),'_.NETLOAD',('"'+$probeDll+'"'),'PF_TEST','_.QUIT','_Y') | Set-Content -LiteralPath $probeScript -Encoding ascii
$env:PF_DIR=$PSScriptRoot
$env:PF_PHASE=$Phase
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ($Phase+'.txt')), '')
$probeProcess=Start-Process -FilePath 'C:/Program Files/Autodesk/AutoCAD 2027/accoreconsole.exe' -ArgumentList @('/s',('"'+$probeScript+'"'),'/l','en-US') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot ($Phase+'.console.log')) -RedirectStandardError (Join-Path $PSScriptRoot ($Phase+'.error.log')) -Wait -PassThru
"CoreExit=$($probeProcess.ExitCode)"
$result=Get-Content -LiteralPath (Join-Path $PSScriptRoot ($Phase+'.txt')) -Raw
$result
if($result -notmatch 'COMPLETE' -or $result -match 'FAIL') { throw 'Form persistence verification failed' }
