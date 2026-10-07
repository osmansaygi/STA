# GPR + DMX doseme donati ayiklama (KALIP50 parser ile ayni ofset/turler)
param(
  [Parameter(Mandatory = $true)][string]$GprPath,
  [Parameter(Mandatory = $true)][string]$DmxPath,
  [Parameter(Mandatory = $true)][string]$OutJson
)

$ErrorActionPreference = "Stop"

function Read-DmxBars([string]$path) {
  $b = [IO.File]::ReadAllBytes($path)
  $rec = 250
  $off = 96
  $n = [Math]::Floor($b.Length / $rec)
  $list = New-Object System.Collections.Generic.List[object]
  for ($r = 0; $r -lt $n; $r++) {
    $o = $r * $rec + $off
    $x = New-Object System.Collections.Generic.List[object]
    $y = New-Object System.Collections.Generic.List[object]
    function Local-Add([byte[]]$bytes, [int]$oo, [string]$kind, $dst) {
      if ($oo + 4 -gt $bytes.Length) { return }
      $dia = [BitConverter]::ToInt16($bytes, $oo)
      $sp = [BitConverter]::ToInt16($bytes, $oo + 2)
      if ($dia -lt 6 -or $dia -gt 40 -or $sp -lt 1 -or $sp -gt 200) { return }
      $dst.Add([pscustomobject]@{ dia = [int]$dia; spacing = [int]$sp; kind = $kind })
    }
    Local-Add $b $o "duz" $x
    Local-Add $b ($o + 6) "pilye" $x
    Local-Add $b ($o + 10) "sol ilave" $x
    Local-Add $b ($o + 14) "sag ilave" $x
    Local-Add $b ($o + 18) "duz" $y
    Local-Add $b ($o + 24) "pilye" $y
    Local-Add $b ($o + 32) "sol ilave" $y
    Local-Add $b ($o + 28) "sag ilave" $y
    $list.Add([pscustomobject]@{
      index = $r + 1
      x = @($x)
      y = @($y)
      xText = (($x | ForEach-Object { "o$($_.dia)/$($_.spacing)($($_.kind))" }) -join "+")
      yText = (($y | ForEach-Object { "o$($_.dia)/$($_.spacing)($($_.kind))" }) -join "+")
    })
  }
  return @($list)
}

function Kind-FromParen([string]$inner) {
  if ([string]::IsNullOrWhiteSpace($inner)) { return "bilinmiyor" }
  $n = $inner.ToLowerInvariant()
  $n = $n.Replace([string][char]0x0131, "i").Replace([string][char]0x011F, "g").Replace([string][char]0x00FC, "u")
  $n = $n.Replace([string][char]0x015F, "s").Replace([string][char]0x00F6, "o").Replace([string][char]0x00E7, "c")
  if ($n -match "sol" -and ($n -match "ila" -or $n -eq "sol")) { return "sol ilave" }
  if ($n -eq "sol") { return "sol ilave" }
  if (($n -match "sag") -and $n -match "ila") { return "sag ilave" }
  if ($n -eq "s" -or $n -eq "sa" -or $n -eq "sag") { return "sag ilave" }
  if ($n -match "mon" -or $n -eq "m" -or $n -eq "mo") { return "montaj" }
  if ($n -match "pil") { return "pilye" }
  if ($n -match "duz" -or $n -eq "dz") { return "duz" }
  return "bilinmiyor"
}

function Parse-GprCell([string]$cell) {
  if ([string]::IsNullOrWhiteSpace($cell)) { return @() }
  $collapsed = ($cell -replace "[\r\n]+", "")
  $parts = New-Object System.Collections.Generic.List[string]
  $depth = 0
  $sb = New-Object System.Text.StringBuilder
  foreach ($ch in $collapsed.ToCharArray()) {
    if ($ch -eq "(") { $depth++ }
    elseif ($ch -eq ")") { $depth = [Math]::Max(0, $depth - 1) }
    if ($ch -eq "+" -and $depth -eq 0) {
      if ($sb.Length -gt 0) { $parts.Add($sb.ToString()); [void]$sb.Clear() }
      continue
    }
    [void]$sb.Append($ch)
  }
  if ($sb.Length -gt 0) { $parts.Add($sb.ToString()) }
  $out = New-Object System.Collections.Generic.List[object]
  foreach ($seg0 in $parts) {
    $seg = $seg0.Trim()
    if (-not $seg) { continue }
    $kind = "bilinmiyor"
    if ($seg -match "\(([^)]*)\)\s*$") { $kind = Kind-FromParen $Matches[1] }
    elseif ($seg -match "\(\s*sa?\s*$" -or $seg -match "\(\s*sag\s*$") { $kind = "sag ilave" }
    elseif ($seg -match "\(\s*(?:Mon\.?|Mo|M)\s*$") { $kind = "montaj" }
    $dia = $null; $sp = $null
    if ($seg -match "(\d+)\s*/\s*(\d+)") {
      $dia = [int]$Matches[1]; $sp = [int]$Matches[2]
    }
    $txt = if ($dia) { "o$dia/$sp($kind)" } else { $seg }
    $out.Add([pscustomobject]@{ dia = $dia; spacing = $sp; kind = $kind; text = $txt })
  }
  return @($out)
}

function Extract-DonatiCell([string]$line) {
  $parts = $line -split [char]0x00A1
  if ($parts.Count -lt 2) { $parts = $line -split [char]0xA1 }
  for ($i = $parts.Count - 1; $i -ge 0; $i--) {
    $t = $parts[$i].Trim()
    if (-not $t) { continue }
    if ($t -match "\d+\s*/\s*\d+") { return $t }
    if ($t.IndexOf([char]0x00F8) -ge 0 -or $t.IndexOf([char]0x00D8) -ge 0) { return $t }
    if ($t -match "\(d" -and $t.Contains(")")) { return $t }
  }
  return ""
}

function Read-GprDoseme([string]$path) {
  $bytes = [IO.File]::ReadAllBytes($path)
  $enc = [System.Text.Encoding]::GetEncoding(1254)
  $text = $enc.GetString($bytes)
  $lines = $text -split "`r`n|`n|`r"
  $start = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -notmatch "(?i)BETONARME") { continue }
    if ($l -notmatch "(?i)HESAP") { continue }
    if ($l -notmatch "(?i)SONU") { continue }
    if ($l -match "(?i)KOLON") { continue }
    if ($l -match "(?i)DOSEME|DSEME|EME BETONARME|DEME|D.?EME") { $start = $i; break }
  }
  if ($start -lt 0) { throw "GPR: DOSEME BETONARME bolumu bulunamadi: $path" }
  $end = -1
  for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -notmatch "(?i)PANEL") { continue }
    if ($l -notmatch "(?i) VE ") { continue }
    if ($l -match "(?i)DUVAR") { continue }
    if ($l -match "(?i)BILG") { $end = $i; break }
  }
  if ($end -lt 0) { throw "GPR: KIRIS VE PANEL bitisi bulunamadi: $path" }

  $dict = [ordered]@{}
  $pendingKey = $null
  $pendingX = $null
  $rxX = [regex]'(?i)(D(?:\d+[A-Z]{1,2}|[A-Z]{1,2}\d?|\d+)-\d+)\s+X'
  $rxY = [regex]'(?i)d\s*=\s*\d+\s*cm[\s\xA1\uFFFD]{0,12}Y\b'

  for ($i = $start; $i -lt $end; $i++) {
    $raw = $lines[$i]
    if ([string]::IsNullOrWhiteSpace($raw)) { continue }
    if ($raw.Length -gt 4 -and [char]::IsDigit($raw[0]) -and $raw.StartsWith("130,")) {
      if ($i + 1 -lt $end) { $raw = $lines[$i + 1]; $i++ } else { continue }
    }
    $lineForMatch = $raw.TrimStart([char]0x00A1, [char]0xFFFD, [char]0xFEFF, [char]0x20, [char]0x09)
    while ($lineForMatch.StartsWith("?") -and $lineForMatch.Length -gt 1 -and $lineForMatch[1] -eq [char]"D") {
      $lineForMatch = $lineForMatch.Substring(1)
    }
    $mX = $rxX.Match($lineForMatch)
    if ($mX.Success) {
      $key = $mX.Groups[1].Value.ToUpperInvariant()
      $don = Extract-DonatiCell $raw
      if ($pendingKey) {
        $xp = Parse-GprCell $pendingX
        $dict[$pendingKey] = [pscustomobject]@{
          id = $pendingKey; xText = $pendingX; yText = ""; x = @($xp); y = @()
        }
      }
      $pendingKey = $key
      $pendingX = $don
      continue
    }
    $yRow = $rxY.IsMatch($lineForMatch) -or ($pendingKey -and $lineForMatch -match "(?i)d\s*=" -and $lineForMatch -match "(?i)\bY\b")
    if ($yRow -and $pendingKey) {
      $yDon = Extract-DonatiCell $raw
      $xp = Parse-GprCell $pendingX
      $yp = Parse-GprCell $yDon
      $dict[$pendingKey] = [pscustomobject]@{
        id = $pendingKey; xText = $pendingX; yText = $yDon; x = @($xp); y = @($yp)
      }
      $pendingKey = $null
      $pendingX = $null
    }
  }
  if ($pendingKey) {
    $xp = Parse-GprCell $pendingX
    $dict[$pendingKey] = [pscustomobject]@{
      id = $pendingKey; xText = $pendingX; yText = ""; x = @($xp); y = @()
    }
  }
  return @($dict.Values)
}

function Get-CapSeq([object[]]$bars) {
  if (-not $bars) { return @() }
  @($bars | Where-Object { $null -ne $_.dia -and $_.dia -ne "" } | ForEach-Object { "$($_.dia)/$($_.spacing)" })
}

function Get-AxisStatus([object[]]$gprBars, [object[]]$dmxBars, [bool]$bothPresent) {
  if (-not $bothPresent) { return "-" }
  $cg = @(Get-CapSeq $gprBars)
  $cd = @(Get-CapSeq $dmxBars)
  if ($cg.Count -eq 0 -and $cd.Count -eq 0) { return "bos" }
  if ($cg.Count -eq 0) { return "sadece DMX" }
  if ($cd.Count -eq 0) { return "sadece GPR" }
  $ok = $true
  for ($k = 0; $k -lt $cg.Count; $k++) {
    if ($k -ge $cd.Count -or $cg[$k] -ne $cd[$k]) { $ok = $false; break }
  }
  if ($ok -and $cg.Count -lt $cd.Count) { return "GPR kesik / DMX tamamlar" }
  if ($ok -and $cg.Count -eq $cd.Count) { return "ayni" }
  return "farkli"
}

$dmx = @(Read-DmxBars $DmxPath)
$gpr = @(Read-GprDoseme $GprPath)

$rows = New-Object System.Collections.Generic.List[object]
$max = [Math]::Max($dmx.Count, $gpr.Count)
for ($i = 0; $i -lt $max; $i++) {
  $d = if ($i -lt $dmx.Count) { $dmx[$i] } else { $null }
  $g = if ($i -lt $gpr.Count) { $gpr[$i] } else { $null }
  $gx = if ($g) { [string]$g.xText } else { "" }
  $gy = if ($g) { [string]$g.yText } else { "" }
  $dx = if ($d) { [string]$d.xText } else { "" }
  $dy = if ($d) { [string]$d.yText } else { "" }
  $both = ($null -ne $g -and $null -ne $d)
  $gxParts = if ($g) { @($g.x) } else { @() }
  $gyParts = if ($g) { @($g.y) } else { @() }
  $dxParts = if ($d) { @($d.x) } else { @() }
  $dyParts = if ($d) { @($d.y) } else { @() }
  $rows.Add([pscustomobject]@{
    i = $i + 1
    id = if ($g) { [string]$g.id } else { "DMX#$($i+1)" }
    gprX = $gx
    gprY = $gy
    dmxX = $dx
    dmxY = $dy
    xStatus = Get-AxisStatus $gxParts $dxParts $both
    yStatus = Get-AxisStatus $gyParts $dyParts $both
  })
}

$rowArr = @($rows)
$sameX = @($rowArr | Where-Object { $_.xStatus -eq "ayni" }).Count
$diffX = @($rowArr | Where-Object { $_.xStatus -eq "farkli" }).Count
$cutX = @($rowArr | Where-Object { $_.xStatus -eq "GPR kesik / DMX tamamlar" }).Count
$sameY = @($rowArr | Where-Object { $_.yStatus -eq "ayni" }).Count
$diffY = @($rowArr | Where-Object { $_.yStatus -eq "farkli" }).Count
$cutY = @($rowArr | Where-Object { $_.yStatus -eq "GPR kesik / DMX tamamlar" }).Count

$result = [pscustomobject]@{
  project = [IO.Path]::GetFileNameWithoutExtension($GprPath)
  gprPath = $GprPath
  dmxPath = $DmxPath
  gprCount = $gpr.Count
  dmxCount = $dmx.Count
  summary = [pscustomobject]@{
    sameX = $sameX; diffX = $diffX; cutX = $cutX
    sameY = $sameY; diffY = $diffY; cutY = $cutY
  }
  rows = $rowArr
}

$dir = Split-Path $OutJson -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[IO.File]::WriteAllText($OutJson, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Host ("Wrote {0} GPR={1} DMX={2} sameX={3} diffX={4} cutX={5} sameY={6} diffY={7} cutY={8}" -f $OutJson,$gpr.Count,$dmx.Count,$sameX,$diffX,$cutX,$sameY,$diffY,$cutY)
