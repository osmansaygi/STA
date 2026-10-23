$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dir=Join-Path ([IO.Path]::GetTempPath()) ('ST4-dependency-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/CommandEntry.cs'))
$start=$source.IndexOf('private static Assembly ResolvePluginDependency(')
if($start -lt 0){throw 'Resolver missing'}
$open=$source.IndexOf('{',$start);$depth=1;$end=$open+1
while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
$method=$source.Substring($start,$end-$start)
$candidate=Join-Path $dir 'LocalDependency.dll'
Add-Type -TypeDefinition 'using System.Reflection; [assembly:AssemblyVersion("1.0.0.0")] public class LocalDependencyMarker {}' -OutputAssembly $candidate
$identity=[Reflection.AssemblyName]::GetAssemblyName($candidate)
$actualPath=Join-Path $dir ($identity.Name+'.dll')
if($actualPath -ne $candidate){Copy-Item -LiteralPath $candidate -Destination $actualPath}
$probe='using System; using System.IO; using System.Reflection; public class DependencyResolverProbe {'+$method+' public static Assembly Resolve(string request){return ResolvePluginDependency(null,new ResolveEventArgs(request));}}'
$probePath=Join-Path $dir 'DependencyResolverProbe.dll'
Add-Type -TypeDefinition $probe -OutputAssembly $probePath
$type=[Reflection.Assembly]::LoadFrom($probePath).GetType('DependencyResolverProbe')
$resolve=$type.GetMethod('Resolve')
function Assert-Rejected([string]$request,[string]$label){if($null -ne $resolve.Invoke($null,@($request))){throw "Resolver accepted incompatible DLL: $label"};"PASS: rejected $label"}
$wrongVersion=New-Object Reflection.AssemblyName($identity.FullName);$wrongVersion.Version=[version]'9.9.9.9'
Assert-Rejected $wrongVersion.FullName 'wrong version'
$wrongToken=New-Object Reflection.AssemblyName($identity.FullName);$wrongToken.SetPublicKeyToken([byte[]](1,2,3,4,5,6,7,8))
Assert-Rejected $wrongToken.FullName 'wrong public key'
$wrongCulture=New-Object Reflection.AssemblyName($identity.FullName);$wrongCulture.CultureName='fr'
Assert-Rejected $wrongCulture.FullName 'wrong culture'
Assert-Rejected 'MissingDependency, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null' 'missing dependency'
Assert-Rejected ($identity.Name+'.resources') 'satellite dependency'
$loaded=$resolve.Invoke($null,@($identity.FullName))
if($null -eq $loaded -or $loaded.GetName().FullName -ne $identity.FullName){throw 'Matching dependency did not load'}
'PASS: matching local dependency still loads; production resolver exercised without AutoCAD.'
