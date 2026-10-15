# BETONARME TOOL kurulum — yalniz ofis bilgisayari (BEYKENT-2).
# AutoCAD kapatilir, eski bundle / NETLOAD / APPLOAD tamamen silinir, tek bundle kurulur.
# Iki kopya (eski NETLOAD + yeni bundle) ofiste AutoCAD'i dusurur. COM toolbar silinmez.
$ErrorActionPreference = "Continue"

$AllowedUuid = "4C4C4544-0037-5110-8052-C4C04F354A34"
$AllowedName = "BEYKENT-2"

function Say($text) { Write-Host $text }

function Force-Unblock($path) {
    try { Unblock-File -LiteralPath $path -ErrorAction SilentlyContinue } catch { }
    try {
        $zone = $path + ":Zone.Identifier"
        if (Test-Path -LiteralPath $zone) { Remove-Item -LiteralPath $zone -Force -ErrorAction SilentlyContinue }
    } catch { }
}

function Stop-AutoCadProcesses {
    # Lisans ajanini kapatma: sonraki AutoCAD acilisi bozulabiliyor.
    $names = @("acad", "acadlt", "accoreconsole")
    $had = $false
    foreach ($n in $names) {
        $procs = @(Get-Process -Name $n -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) { continue }
        $had = $true
        foreach ($p in $procs) {
            try {
                Say ("Kapatiliyor: {0} (pid {1})" -f $p.ProcessName, $p.Id)
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
            } catch {
                Say ("Kapatilamadi: {0}" -f $p.ProcessName)
            }
        }
    }
    if ($had) { Start-Sleep -Seconds 4 }
}

function Clear-ReadOnlyTree($path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    try {
        Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.Attributes = 'Normal' } catch { }
        }
        try { (Get-Item -LiteralPath $path -Force).Attributes = 'Normal' } catch { }
    } catch { }
}

function Force-RemovePath($path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $true }
    if (-not (Test-Path -LiteralPath $path)) { return $true }

    Clear-ReadOnlyTree $path
    try {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        if (-not (Test-Path -LiteralPath $path)) {
            Say "Silindi: $path"
            return $true
        }
    } catch { }

    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $parent = Split-Path -Parent $path
    $leaf = Split-Path -Leaf $path
    $renamedLeaf = $leaf + ".__eski_" + $stamp
    $renamed = Join-Path $parent $renamedLeaf
    try {
        Rename-Item -LiteralPath $path -NewName $renamedLeaf -ErrorAction Stop
        Say "Kilitli klasor tasindi: $renamed"
        Start-Sleep -Milliseconds 500
        try { Remove-Item -LiteralPath $renamed -Recurse -Force -ErrorAction SilentlyContinue } catch { }
        if (-not (Test-Path -LiteralPath $path)) { return $true }
    } catch { }

    $arg = '/c rd /s /q "' + $path + '"'
    try { Start-Process -FilePath "cmd.exe" -ArgumentList $arg -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue } catch { }
    if (-not (Test-Path -LiteralPath $path)) {
        Say "Silindi (rd): $path"
        return $true
    }

    Say "SILINEMEDI (kilitli): $path"
    return $false
}

function Test-UnderSetup([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    try {
        $full = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
        $root = $script:SetupRoot.TrimEnd('\')
        return $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

function Remove-PluginFile($file) {
    if ($null -eq $file) { return $true }
    $full = $file.FullName
    if (Test-UnderSetup $full) { return $true }
    try {
        $file.Attributes = 'Normal'
        Remove-Item -LiteralPath $full -Force -ErrorAction Stop
        Say "Eski dosya silindi: $full"
        return $true
    } catch {
        $stamp = Get-Date -Format "yyyyMMddHHmmss"
        $altLeaf = $file.Name + ".__eski_" + $stamp
        try {
            Rename-Item -LiteralPath $full -NewName $altLeaf -ErrorAction Stop
            Say "Kilitli dosya tasindi: $full"
            $moved = Join-Path $file.DirectoryName $altLeaf
            try { Remove-Item -LiteralPath $moved -Force -ErrorAction SilentlyContinue } catch { }
            if (-not (Test-Path -LiteralPath $full)) { return $true }
        } catch { }
        Say "DOSYA KILITLI, silinemedi: $full"
        return $false
    }
}

function Remove-MatchingDlls($root) {
    if ([string]::IsNullOrWhiteSpace($root)) { return $true }
    if (Test-UnderSetup $root) { return $true }
    if (-not (Test-Path -LiteralPath $root)) { return $true }
    $ok = $true
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            -not (Test-UnderSetup $_.FullName) -and (
                $_.Name -like "ST4_Yardimci*.dll" -or
                $_.Name -like "ST4_Yardimci*.pdb" -or
                $_.Name -eq "ST4Yardimci.dll" -or
                $_.Name -eq "ST4Yardimci.pdb"
            )
        } |
        ForEach-Object {
            if (-not (Remove-PluginFile $_)) { $script:LeftoverLocked = $true; $ok = $false }
        }
    return $ok
}

function Get-PluginRoots {
    $roots = @(
        (Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"),
        (Join-Path $env:LOCALAPPDATA "Autodesk\ApplicationPlugins"),
        (Join-Path $env:ProgramData "Autodesk\ApplicationPlugins"),
        (Join-Path $env:ProgramFiles "Autodesk\ApplicationPlugins")
    )
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $roots += (Join-Path ${env:ProgramFiles(x86)} "Autodesk\ApplicationPlugins")
    }
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($base)) { continue }
        $autodesk = Join-Path $base "Autodesk"
        if (-not (Test-Path -LiteralPath $autodesk)) { continue }
        Get-ChildItem -LiteralPath $autodesk -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "AutoCAD*" } |
            ForEach-Object { $roots += (Join-Path $_.FullName "ApplicationPlugins") }
    }
    return @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Remove-OldBundles {
    $failed = $false
    foreach ($root in (Get-PluginRoots)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -like "*Yardimci*" -or
                $_.Name -like "*ST4Yard*" -or
                $_.Name -like "*BETONARME*" -or
                $_.Name -like "*__eski_*"
            } |
            ForEach-Object {
                if (-not (Force-RemovePath $_.FullName)) { $failed = $true }
            }
    }
    return $failed
}

function Remove-StartupFileRefs($root) {
    if (-not (Test-Path -LiteralPath $root)) { return }
    $names = @("acad.rx", "acad.lsp", "acaddoc.lsp")
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $names -contains $_.Name } |
        ForEach-Object {
            $lines = @(Get-Content -LiteralPath $_.FullName -ErrorAction SilentlyContinue)
            if ($lines.Count -eq 0) { return }
            $kept = @($lines | Where-Object { $_ -notmatch "ST4_Yardimci|ST4Yardimci|BETONARME TOOL" })
            if ($kept.Count -eq $lines.Count) { return }
            $bak = $_.FullName + ".st4bak"
            try {
                Copy-Item -LiteralPath $_.FullName -Destination $bak -Force
                $enc = [System.Text.Encoding]::Default
                $body = ""
                if ($kept.Count -gt 0) { $body = ($kept -join "`r`n") + "`r`n" }
                [System.IO.File]::WriteAllText($_.FullName, $body, $enc)
                Say "Acilis dosyasindan eski satir silindi: $($_.FullName)"
            } catch {
                Say "Acilis dosyasi yazilamadi: $($_.FullName)"
            }
        }
}

function Clear-PluginRegistry {
    $acadKey = "HKCU:\Software\Autodesk\AutoCAD"
    if (-not (Test-Path $acadKey)) { return }
    Get-ChildItem $acadKey -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        $key = $_
        if ($key.PSChildName -eq "Startup" -and $key.Name -match "Appload") {
            $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($props) {
                foreach ($p in $props.PSObject.Properties) {
                    if ($p.Name -match "^PS") { continue }
                    $val = [string]$p.Value
                    if ($val -match "ST4_Yardimci|ST4Yardimci|BETONARME|\\NETLOAD\\") {
                        Remove-ItemProperty -LiteralPath $key.PSPath -Name $p.Name -ErrorAction SilentlyContinue
                        Say "APPLOAD kaydi silindi: $val"
                    }
                }
            }
        }
        if ($key.PSChildName -eq "Applications") {
            Get-ChildItem -LiteralPath $key.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
                $child = $_
                $hit = $child.PSChildName -match "ST4_Yardimci|ST4Yardimci|BETONARME"
                if (-not $hit) {
                    $cp = Get-ItemProperty -LiteralPath $child.PSPath -ErrorAction SilentlyContinue
                    if ($cp) {
                        foreach ($p in $cp.PSObject.Properties) {
                            if ($p.Name -match "^PS") { continue }
                            if (([string]$p.Value) -match "ST4_Yardimci|ST4Yardimci|BETONARME") { $hit = $true }
                        }
                    }
                }
                if ($hit) {
                    Remove-Item -LiteralPath $child.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                    Say "Registry uygulamasi silindi: $($child.PSChildName)"
                }
            }
        }
        if ($key.PSChildName -eq "Variables") {
            $prop = Get-ItemProperty -LiteralPath $key.PSPath -Name "TRUSTEDPATHS" -ErrorAction SilentlyContinue
            if ($prop) {
                $cur = [string]$prop.TRUSTEDPATHS
                if (-not [string]::IsNullOrWhiteSpace($cur)) {
                    $parts = @($cur -split ";" | ForEach-Object { $_.Trim() } | Where-Object {
                        $_ -and $_ -notmatch "ST4_Yardimci|ST4Yardimci|\\NETLOAD\\"
                    })
                    Set-ItemProperty -LiteralPath $key.PSPath -Name "TRUSTEDPATHS" -Value (($parts -join ";").Trim(";"))
                }
            }
        }
    }
}

function Add-TrustedBundle([string]$bundlePath) {
    $acadKey = "HKCU:\Software\Autodesk\AutoCAD"
    if (-not (Test-Path $acadKey)) { return }
    $needle = $bundlePath.TrimEnd("\")
    Get-ChildItem $acadKey -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -eq "Variables" } |
        ForEach-Object {
            $prop = Get-ItemProperty -LiteralPath $_.PSPath -Name "TRUSTEDPATHS" -ErrorAction SilentlyContinue
            $cur = ""
            if ($prop) { $cur = [string]$prop.TRUSTEDPATHS }
            if ($cur -match [regex]::Escape($needle)) { return }
            $next = $cur
            if ([string]::IsNullOrWhiteSpace($next)) { $next = $bundlePath }
            else { $next = $next.TrimEnd(";") + ";" + $bundlePath }
            Set-ItemProperty -LiteralPath $_.PSPath -Name "TRUSTEDPATHS" -Value $next
        }
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:SetupRoot = [System.IO.Path]::GetFullPath($here)
$script:LeftoverLocked = $false

$payloadDll = Join-Path $here "payload\ST4_Yardimci.dll"
$payloadXml = Join-Path $here "PackageContents.xml"
if (-not (Test-Path -LiteralPath $payloadDll) -or -not (Test-Path -LiteralPath $payloadXml)) {
    Say "Kurulum dosyalari eksik (payload\ST4_Yardimci.dll)."
    exit 1
}

$requiredDeps = @(
    "NetTopologySuite.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)
foreach ($dep in $requiredDeps) {
    $p = Join-Path $here ("payload\" + $dep)
    if (-not (Test-Path -LiteralPath $p)) {
        Say "Eksik bagimlilik: payload\$dep"
        Say "NTS eksikse AutoCAD acilista duser. Kurulum yapilmadi."
        exit 1
    }
}

$uuid = ""
try {
    $uuid = (Get-CimInstance -ClassName Win32_ComputerSystemProduct).UUID
} catch { }
if ([string]::IsNullOrWhiteSpace($uuid)) {
    Say "Bu bilgisayarin kimligi okunamadi. Kurulum yapilmadi."
    exit 1
}
$uuid = $uuid.Trim().Trim("{}")
if ($uuid -notmatch ('^' + [regex]::Escape($AllowedUuid) + '$')) {
    Say "Bu kurulum yalnizca $AllowedName bilgisayarinda calisir."
    Say "Bu makine: $uuid"
    exit 1
}

Say "Makine dogrulandi: $AllowedName"
Say "AutoCAD kapatiliyor (eski DLL kilitli kalmasin, cift yukleme olmasin)..."
Stop-AutoCadProcesses

$still = @(Get-Process -Name "acad","acadlt","accoreconsole" -ErrorAction SilentlyContinue)
if ($still.Count -gt 0) {
    Say "AutoCAD hala acik. Gorev Yoneticisinden acad.exe kapatip Kur.cmd tekrar calistirin."
    Say "AutoCAD acikken kurulum yapilirsa eski ve yeni DLL birlikte yuklenir ve program duser."
    exit 1
}

Say "Eski Yardimci / bundle / APPLOAD temizleniyor..."
$failedBundle = Remove-OldBundles

$scanRoots = @(
    (Join-Path $env:APPDATA "Autodesk"),
    (Join-Path $env:LOCALAPPDATA "Autodesk"),
    (Join-Path $env:USERPROFILE "Documents\NETLOAD"),
    (Join-Path $env:USERPROFILE "Desktop"),
    (Join-Path $env:USERPROFILE "Downloads"),
    (Join-Path $env:USERPROFILE "Desktop\NETLOAD")
)
if (Test-Path -LiteralPath (Join-Path $env:ProgramData "Autodesk")) {
    $scanRoots += (Join-Path $env:ProgramData "Autodesk")
}
foreach ($pluginRoot in (Get-PluginRoots)) {
    $scanRoots += $pluginRoot
}
foreach ($root in $scanRoots) {
    Remove-MatchingDlls $root | Out-Null
}

Remove-StartupFileRefs (Join-Path $env:APPDATA "Autodesk")
try { Clear-PluginRegistry } catch {
    Say "Kayit defteri tam temizlenemedi; APPLOAD icinden eski satiri elle silin."
}

$iconBar = Join-Path $env:LOCALAPPDATA "ST4_Yardimci\iconbar_layout.txt"
if (Test-Path -LiteralPath $iconBar) {
    try {
        Remove-Item -LiteralPath $iconBar -Force -ErrorAction Stop
        Say "Eski ust serit konumu silindi (BAUST acilista kurulmaz)."
    } catch { }
}

$destRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
if (-not (Test-Path -LiteralPath $destRoot)) {
    New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
}
$dest = Join-Path $destRoot "ST4_Yardimci.bundle"
$destWin = Join-Path $dest "Contents\Windows"

if (-not (Force-RemovePath $dest)) {
    Say "Eski bundle silinemedi. AutoCAD kapali mi kontrol edin, sonra Kur.cmd tekrar."
    exit 1
}

New-Item -ItemType Directory -Force -Path $destWin | Out-Null
Copy-Item -LiteralPath $payloadXml -Destination (Join-Path $dest "PackageContents.xml") -Force
Get-ChildItem -LiteralPath (Join-Path $here "payload") -File | ForEach-Object {
    if ($_.Extension -match '^\.(pdb|xml)$') { return }
    $target = Join-Path $destWin $_.Name
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    Force-Unblock $target
}
Force-Unblock (Join-Path $dest "PackageContents.xml")

$bundlePath = $dest.TrimEnd("\") + "\"
try { Add-TrustedBundle $bundlePath } catch { }

$dllPath = Join-Path $destWin "ST4_Yardimci.dll"
foreach ($dep in $requiredDeps) {
    if (-not (Test-Path -LiteralPath (Join-Path $destWin $dep))) {
        Say "Kurulum eksik kopyaladi: $dep"
        exit 1
    }
}

$others = @()
foreach ($root in $scanRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "ST4_Yardimci*.dll" -and
            -not (Test-UnderSetup $_.FullName) -and
            ($_.FullName -ne $dllPath)
        } |
        ForEach-Object { $others += $_.FullName }
}

$dllInfo = Get-Item -LiteralPath $dllPath
Say ""
Say "Kurulum bitti. Tek kopya:"
Say $dest
Say ("ST4_Yardimci.dll  {0:yyyy-MM-dd HH:mm:ss}  {1} bayt" -f $dllInfo.LastWriteTime, $dllInfo.Length)
Say "Icerik:"
Get-ChildItem -LiteralPath $destWin -File | ForEach-Object { Say ("  " + $_.Name) }

if ($failedBundle -or $script:LeftoverLocked -or $others.Count -gt 0) {
    Say ""
    Say "UYARI: Baska bir ST4_Yardimci.dll duruyor. AutoCAD'i acmayin; cift yukleme programi dusurur."
    foreach ($o in ($others | Select-Object -Unique)) { Say ("  " + $o) }
    Say "Bu dosyalari silip Kur.cmd tekrar calistirin."
    exit 1
}

Say ""
Say "AutoCAD'i simdi acin. Komut satirinda BETONARME TOOL yuklendi yazmali."
Say "APPLOAD Startup Suite'te ST4_Yardimci / NETLOAD olmamali. Ayrica NETLOAD yapmayin."
exit 0
