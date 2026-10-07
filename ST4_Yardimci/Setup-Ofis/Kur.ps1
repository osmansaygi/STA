# BETONARME TOOL kurulum — yalniz ofis bilgisayari (BEYKENT-2).
# AutoCAD kapatilir, eski bundle/APPLOAD silinir (kilitliyse yeniden adlandirilir), yeni payload kopyalanir.
$ErrorActionPreference = "Continue"

$AllowedUuid = "4C4C4544-0037-5110-8052-C4C04F354A34"
$AllowedName = "BEYKENT-2"

function Say($text) { Write-Host $text }

function Force-Unblock($path) {
    try { Unblock-File -LiteralPath $path -ErrorAction SilentlyContinue } catch { }
}

function Stop-AutoCadProcesses {
    $names = @("acad", "acadlt", "accoreconsole", "AcEventMgr", "AdskLicensingAgent")
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
    if ($had) { Start-Sleep -Seconds 3 }
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
    $renamed = Join-Path $parent ($leaf + ".__eski_" + $stamp)
    try {
        Rename-Item -LiteralPath $path -NewName (Split-Path -Leaf $renamed) -ErrorAction Stop
        Say "Kilitli klasor tasindi: $renamed"
        Start-Sleep -Milliseconds 400
        try { Remove-Item -LiteralPath $renamed -Recurse -Force -ErrorAction SilentlyContinue } catch { }
        if (-not (Test-Path -LiteralPath $path)) { return $true }
    } catch { }

    $cmd = "cmd.exe"
    $arg = '/c rd /s /q "' + $path + '"'
    try { Start-Process -FilePath $cmd -ArgumentList $arg -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue } catch { }
    if (-not (Test-Path -LiteralPath $path)) {
        Say "Silindi (rd): $path"
        return $true
    }

    Say "SILINEMEDI (kilitli): $path"
    return $false
}

function Remove-MatchingDlls($root) {
    if (-not (Test-Path -LiteralPath $root)) { return }
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "ST4_Yardimci*.dll" -or
            $_.Name -like "ST4_Yardimci*.pdb" -or
            $_.Name -eq "ST4Yardimci.dll"
        } |
        ForEach-Object {
            try {
                $_.Attributes = 'Normal'
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
                Say "Eski DLL silindi: $($_.FullName)"
            } catch {
                $stamp = Get-Date -Format "yyyyMMddHHmmss"
                $alt = $_.FullName + ".__eski_" + $stamp
                try {
                    Rename-Item -LiteralPath $_.FullName -NewName (Split-Path -Leaf $alt) -ErrorAction Stop
                    Say "Kilitli DLL tasindi: $alt"
                } catch {
                    Say "DLL kilitli, atlandi: $($_.FullName)"
                }
            }
        }
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
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
Say "AutoCAD kapatiliyor (eski DLL kilitli kalmasin)..."
Stop-AutoCadProcesses

if (Get-Process -Name "acad" -ErrorAction SilentlyContinue) {
    Say "AutoCAD hala acik; eski surum silinemez. Gorev Yoneticisinden acad.exe kapatip Kur.cmd tekrar calistirin."
    exit 1
}

Say "Eski Yardimci / bundle / APPLOAD temizleniyor..."

$pluginRoots = @(
    (Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"),
    (Join-Path $env:LOCALAPPDATA "Autodesk\ApplicationPlugins"),
    (Join-Path $env:ProgramData "Autodesk\ApplicationPlugins"),
    (Join-Path $env:ProgramFiles "Autodesk\ApplicationPlugins"),
    (Join-Path ${env:ProgramFiles(x86)} "Autodesk\ApplicationPlugins")
)
Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles "Autodesk") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "AutoCAD*" } |
    ForEach-Object {
        $pluginRoots += (Join-Path $_.FullName "ApplicationPlugins")
    }

$failedBundle = $false
foreach ($root in $pluginRoots) {
    if ([string]::IsNullOrWhiteSpace($root)) { continue }
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "*Yardimci*" -or
            $_.Name -like "*ST4Yard*" -or
            $_.Name -like "*BETONARME*"
        } |
        ForEach-Object {
            if (-not (Force-RemovePath $_.FullName)) { $failedBundle = $true }
        }
}

$scanRoots = @(
    (Join-Path $env:APPDATA "Autodesk"),
    (Join-Path $env:LOCALAPPDATA "Autodesk"),
    (Join-Path $env:USERPROFILE "Documents\NETLOAD"),
    (Join-Path $env:USERPROFILE "Desktop"),
    (Join-Path $env:USERPROFILE "Downloads")
)
if (Test-Path -LiteralPath (Join-Path $env:ProgramData "Autodesk")) {
    $scanRoots += (Join-Path $env:ProgramData "Autodesk")
}
foreach ($root in $scanRoots) {
    Remove-MatchingDlls $root
}

# APPLOAD / Applications / TRUSTEDPATHS
try {
    $acadKey = "HKCU:\Software\Autodesk\AutoCAD"
    if (Test-Path $acadKey) {
        Get-ChildItem $acadKey -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $key = $_
            if ($key.PSChildName -eq "Startup" -and $key.Name -match "Appload") {
                $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
                if ($props) {
                    foreach ($p in $props.PSObject.Properties) {
                        if ($p.Name -match "^PS") { continue }
                        $val = [string]$p.Value
                        if ($val -match "ST4_Yardimci|ST4Yardimci|BETONARME|NETLOAD") {
                            Remove-ItemProperty -LiteralPath $key.PSPath -Name $p.Name -ErrorAction SilentlyContinue
                            Say "APPLOAD kaydi silindi: $val"
                        }
                    }
                }
            }
            if ($key.PSChildName -eq "Applications") {
                Get-ChildItem -LiteralPath $key.PSPath -ErrorAction SilentlyContinue |
                    Where-Object { $_.PSChildName -match "ST4_Yardimci|ST4Yardimci|BETONARME" } |
                    ForEach-Object {
                        Remove-Item -LiteralPath $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                        Say "Registry uygulamasi silindi: $($_.PSChildName)"
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
} catch {
    Say "Kayit defteri tam temizlenemedi; APPLOAD icinden eski satiri elle silin."
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
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destWin $_.Name) -Force
    Force-Unblock (Join-Path $destWin $_.Name)
}
Force-Unblock (Join-Path $dest "PackageContents.xml")

$bundlePath = $dest.TrimEnd("\") + "\"
try {
    $acadKey = "HKCU:\Software\Autodesk\AutoCAD"
    if (Test-Path $acadKey) {
        Get-ChildItem $acadKey -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -eq "Variables" } |
            ForEach-Object {
                $prop = Get-ItemProperty -LiteralPath $_.PSPath -Name "TRUSTEDPATHS" -ErrorAction SilentlyContinue
                $cur = ""
                if ($prop) { $cur = [string]$prop.TRUSTEDPATHS }
                if ($cur -match [regex]::Escape($dest.TrimEnd("\"))) { return }
                $next = $cur
                if ([string]::IsNullOrWhiteSpace($next)) { $next = $bundlePath }
                else { $next = $next.TrimEnd(";") + ";" + $bundlePath }
                Set-ItemProperty -LiteralPath $_.PSPath -Name "TRUSTEDPATHS" -Value $next
            }
    }
} catch { }

$dllInfo = Get-Item -LiteralPath (Join-Path $destWin "ST4_Yardimci.dll")
Say ""
Say "Kurulum bitti."
Say $dest
Say ("ST4_Yardimci.dll  {0:yyyy-MM-dd HH:mm:ss}  {1} bayt" -f $dllInfo.LastWriteTime, $dllInfo.Length)
Say "Icerik:"
Get-ChildItem -LiteralPath $destWin -File | ForEach-Object { Say ("  " + $_.Name) }
if ($failedBundle) {
    Say ""
    Say "Uyari: bazi eski bundle klasorleri tasindi (__eski_...) veya kilitli kaldi. AutoCAD acmadan once silin."
}
Say ""
Say "AutoCAD'i acin. Komut satirinda BETONARME TOOL yuklendi yazmali."
Say "APPLOAD Startup Suite'te ST4_Yardimci / NETLOAD olmamali."
exit 0
