$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$stamp = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'standard-delivery-stamp.txt') -Raw).Trim()
if ($stamp -notmatch '^\d{8}_\d{6}$') { throw 'Geçersiz teslim damgası.' }
$netload = (Resolve-Path (Join-Path $root 'NETLOAD')).Path
$backup = Join-Path $PSScriptRoot "onceki-netload-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$previous = @('ST4_Yardimci_20261022_013049', 'ST4_Plan_ID_Ciz_20261022_142128')
$oldNames = @($previous | ForEach-Object { "${_}.dll"; "${_}.pdb" })
$newNames = @('ST4_Yardimci', 'ST4_Plan_ID_Ciz') | ForEach-Object { "${_}_${stamp}.dll"; "${_}_${stamp}.pdb" }
$unchanged = @{}
Get-ChildItem -LiteralPath $netload -File | Where-Object { $_.Name -notin $oldNames -and $_.Name -notin $newNames -and $_.Name -ne 'OKU.txt' } | ForEach-Object {
    $unchanged[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
foreach ($name in $oldNames) {
    $path = Join-Path $netload $name
    if (!(Test-Path -LiteralPath $path)) { throw "Beklenen önceki sürüm yok: $name" }
    $saved = Join-Path $backup $name
    Copy-Item -LiteralPath $path -Destination $saved
    if ((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath $saved).Hash) { throw "Yedek hash farklı: $name" }
}
if (!(Test-Path -LiteralPath (Join-Path $backup 'OKU.txt'))) {
    Copy-Item -LiteralPath (Join-Path $netload 'OKU.txt') -Destination (Join-Path $backup 'OKU.txt')
}
$delivered = @()
foreach ($module in @('ST4_Yardimci', 'ST4_Plan_ID_Ciz')) {
    foreach ($extension in @('dll', 'pdb')) {
        $name = "${module}_${stamp}.$extension"
        $source = Join-Path $PSScriptRoot "$module/build/$name"
        $target = Join-Path $netload $name
        if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $source -Destination $target }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ($hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw "Teslim hash farklı: $name" }
        $delivered += [pscustomobject]@{Path=$target; SHA256=$hash}
    }
}
# Önceki dosya silmeye kapalı olabilir; sürüm seçimi OKU'da açıkça belirtilir.
foreach ($name in $oldNames) {
    $path = (Resolve-Path -LiteralPath (Join-Path $netload $name)).Path
    if ([IO.Path]::GetDirectoryName($path) -ne $netload) { throw 'Beklenmeyen kaynak yolu.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup $name)).Hash) { throw 'Önceki dosya değişti.' }
}
$notes = @"
NETLOAD — STANDART A AÇIK FORMLAR / 22.10.2026

YARDIMCI: ST4_Yardimci_$stamp.dll
PLAN ID: ST4_Plan_ID_Ciz_$stamp.dll
Hedef: iki modül de Debug/net48 (legacy); net10/2027 teslimi değildir.

Temiz AutoCAD oturumunda NETLOAD ile yukarıdaki iki DLL'yi yükleyin.
Önceki sürümler de klasörde kalır; bu teslim için 150054 tarihli çifti seçin.
26 form: daima açık gri zemin, beyaz girişler, yerel Windows düğmeleri,
standart çerçeveli gruplar. Genel ayarlar alt alta görünür.
Oto Donatı ve Sehpa Donatı kot girişleri cm; eski metre kayıtları korunur.
AutoCAD'in koyu/açık seçimi formları değiştirmez.

Önceki DLL/PDB ve OKU yedeği: tools/MonolitFormCheck/onceki-netload-$stamp/
26 gerçek form testi ve kot gidiş-dönüş testleri PASS.
İki derleme 0 hata/0 uyarı. DLL/PDB kaynak-kopya SHA256 eşit.
AutoCAD'e otomatik yüklenmedi; canlı AutoCAD ve yüksek DPI denenmedi.
Genel Verify-Project mevcut BaDimensionStyles/BeykentOrtak başvurusunda durur.
Ayrıntı: docs/modul-durum/ST4_FormTasarimi.md
"@
Set-Content -LiteralPath (Join-Path $netload 'OKU.txt') -Value $notes -Encoding utf8
foreach ($name in $unchanged.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $netload $name) -Algorithm SHA256).Hash -ne $unchanged[$name]) { throw "Diğer NETLOAD dosyası değişti: $name" }
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'Ortak/Monolit') -Filter '*.cs' -File)
foreach ($module in @('ST4_Yardimci','ST4_Plan_ID_Ciz')) {
    $sources += Get-ChildItem -LiteralPath (Join-Path $root $module) -Filter '*Form.cs' -File
}
$sourceHashes = @($sources | ForEach-Object { [pscustomobject]@{Path=$_.FullName; SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
[pscustomobject]@{Stamp=$stamp; Framework='net48'; Configuration='Debug'; Forms=26; AutoCADLoaded=$false;
    Backup=$backup; RetainedPrevious=$oldNames; OtherFilesUnchanged=$unchanged.Count; Delivered=$delivered; Sources=$sourceHashes} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'standard-delivery.json') -Encoding utf8
$delivered | Format-Table -AutoSize
Write-Output "Diğer $($unchanged.Count) NETLOAD dosyası hash eşit."
