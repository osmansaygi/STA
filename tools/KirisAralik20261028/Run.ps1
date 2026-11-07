param([Parameter(Mandatory)][string]$PlanDll, [Parameter(Mandatory)][string]$Name)
$ErrorActionPreference='Stop'
$probeDir=$PSScriptRoot
$probeDll=Join-Path $probeDir 'Native/bin/Debug/net10.0-windows/Native.dll'
$probeScript=Join-Path $probeDir ($Name+'.scr')
@('SECURELOAD','0','_.NETLOAD',('"'+(Resolve-Path $PlanDll).Path+'"'),'_.NETLOAD',('"'+$probeDll+'"'),'KA_TEST','_.QUIT','_Y') | Set-Content -LiteralPath $probeScript -Encoding ascii
$env:KA_DIR=$probeDir
$env:KA_NAME=$Name
[IO.File]::WriteAllText((Join-Path $probeDir ($Name+'.txt')), '')
$probeProcess=Start-Process -FilePath 'C:/Program Files/Autodesk/AutoCAD 2027/accoreconsole.exe' -ArgumentList @('/s',('"'+$probeScript+'"'),'/l','en-US') -WorkingDirectory $probeDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $probeDir ($Name+'.console.log')) -RedirectStandardError (Join-Path $probeDir ($Name+'.error.log')) -Wait -PassThru
"CoreExit=$($probeProcess.ExitCode)"
$result=Get-Content -LiteralPath (Join-Path $probeDir ($Name+'.txt')) -Raw
$result
if ($result -notmatch 'COMPLETE' -or $result -match 'FAIL') {throw 'Native verification failed'}
