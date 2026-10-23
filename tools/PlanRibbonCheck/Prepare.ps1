$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $root 'ST4_Plan_ID_Ciz/CommandEntry.cs'
$source = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$old = @'
            if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                "accoreconsole", StringComparison.OrdinalIgnoreCase))
                Application.Idle += OnIdleShowStaPanel;
'@
$new = @'
#if AUTOCAD2027
            if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                "accoreconsole", StringComparison.OrdinalIgnoreCase))
                BeykentRibbonManager.Initialize();
#else
            if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                "accoreconsole", StringComparison.OrdinalIgnoreCase))
                Application.Idle += OnIdleShowStaPanel;
#endif
'@
$old = $old.Replace("`r`n", "`n"); $new = $new.Replace("`r`n", "`n")
if (!$source.Contains($old)) { throw 'Plan startup changed: review integration before preparing again.' }
$prepared = if ($source.Contains('BeykentRibbonManager.Initialize();')) { $source } else { $source.Replace($old, $new) }
$shutdown = '            CommandPaletteManager.Shutdown();'
if (!$prepared.Contains($shutdown)) { throw 'Plan shutdown anchor missing' }
if (!$prepared.Contains('BeykentRibbonManager.Shutdown();')) {
    $prepared = $prepared.Replace($shutdown, "#if AUTOCAD2027`n            BeykentRibbonManager.Shutdown();`n#endif`n" + $shutdown)
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'prepared') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'prepared/CommandEntry.cs'), $prepared, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'prepared/source-sha256.txt'), (Get-FileHash -LiteralPath $path).Hash)
'Prepared the probe icon provider and a lifecycle snapshot; production sources are unchanged by this script.'

# Compile the actual production icon provider in the probe, without the native palette class.
$icons = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/ToolPaletteUi.cs'))
$start = $icons.IndexOf('    internal static class StaToolIcons')
if ($start -lt 0) { throw 'Icon provider missing' }
$usings = ([regex]::Matches($icons, '(?m)^using [^\r\n]+;') | ForEach-Object Value) -join "`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'prepared/StaToolIcons.cs'), $usings + "`nnamespace ST4PlanIdCiz {`n" + $icons.Substring($start), [Text.UTF8Encoding]::new($false))
