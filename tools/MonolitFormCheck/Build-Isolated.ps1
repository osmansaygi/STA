param([ValidatePattern('^$|^\d{8}_\d{6}$')][string]$DeliveryStamp = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
foreach ($module in @('ST4_Yardimci', 'ST4_Plan_ID_Ciz')) {
    $source = Join-Path $root $module
    $target = Join-Path $PSScriptRoot $module
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $original = [IO.File]::ReadAllText((Join-Path $source "$module.csproj"))
    [xml]$project = $original.Replace('$(MSBuildProjectDirectory)', $source)
    # Kaynak projelerin NETLOAD/bundle/temizlik hedefleri bu projeye taşınmaz.
    foreach ($node in @($project.Project.Target)) { [void]$project.Project.RemoveChild($node) }
    foreach ($node in @($project.SelectNodes('//Compile|//EmbeddedResource'))) {
        if ($node.HasAttribute('Include')) {
            $parts = $node.GetAttribute('Include').Split(';') | ForEach-Object {
                if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $source $_ }
            }
            $node.SetAttribute('Include', ($parts -join ';'))
        }
        if ($node.HasAttribute('Exclude')) {
            $parts = $node.GetAttribute('Exclude').Split(';') | ForEach-Object { Join-Path $source $_ }
            $node.SetAttribute('Exclude', ($parts -join ';'))
        }
    }
    $properties = $project.CreateElement('PropertyGroup')
    foreach ($entry in @{
        EnableDefaultCompileItems='false'; EnableDefaultEmbeddedResourceItems='false';
        OutputPath='build/'; AppendTargetFrameworkToOutputPath='false';
        AssemblyName=$(if ($DeliveryStamp) { "${module}_$DeliveryStamp" } else { $module }); NuGetAudit='false'
    }.GetEnumerator()) {
        $node = $project.CreateElement($entry.Key); $node.InnerText = $entry.Value
        [void]$properties.AppendChild($node)
    }
    [void]$project.Project.AppendChild($properties)
    $items = $project.CreateElement('ItemGroup')
    $compile = $project.CreateElement('Compile')
    $compile.SetAttribute('Include', (Join-Path $source '**/*.cs'))
    $compile.SetAttribute('Exclude', "$(Join-Path $source 'bin/**');$(Join-Path $source 'obj/**')")
    [void]$items.AppendChild($compile); [void]$project.Project.AppendChild($items)
    $projectPath = Join-Path $target "$module.csproj"
    $project.Save($projectPath)
    & dotnet build $projectPath -c Debug -v minimal --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "$module izole derleme başarısız." }
}
