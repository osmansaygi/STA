# ST4_Yardimci — durum

Güncelle: 2026-10-16

## Son DLL

Debug damga: `ST4_Yardimci_20261016_190640` (`NETLOAD\`).

## Son düzeltme

- **TEMELDETAY / TEMELCEKURU ölçü düzeni**: ortak ölçülendirme; yatay iki sıra üstte 10 cm aralıklı. Grobeton 10 cm taşma ölçüleri altta; üst toplam yalnız beton genişliğini içerir (40+200+40=280). İç taraftaki ikinci düşey sıra hatıl üstünden grobeton altına kesintisiz devam eder. Donatı numaraları geçici olarak kapalı; noktalar ve hesap kimlikleri korunur. Geometri/ölçü ve ortak stil kontrolleri geçti; AutoCAD görsel kontrolü bekliyor.

- **Ortak ölçü ayarları**: `BaDimensionStyles.cs`; OTOOLCU / AKS / PLANOLCU, MERDIVEN, TEMELCEKURU / TEMELDETAY, TEMELHATILI, KURANGLEZ, OTODONATI, HASIRDONATI, SEHPADONATI, DONATI OKU / RADYEILAVE aynı stil kaynağını kullanır. Standart stil mevcutsa ayarları güncellenir; yeni ölçülerde DWG/aktif DIM override değerleri temizlenir. PLAN/AKS yazı 12; donatı yazı 10; tik 3; yuvarlama 0.5, 1 ondalık; YAZI (BEYKENT). 1/25 faktör 0.5, PLAN_OLCU 25 sabit uzatma 5. Donatı oku: dolu ok 10, uzatma kapalı, yazı ortalı. Formdan seçilen özel stiller korunur. Kontrol: `tools/Test-BaDimensionStyles.ps1`; gerçek AutoCAD görsel kontrolü bekliyor.

- **TEMELDETAY**: güncel temel çukuru çekirdeğiyle tek kesit; çukur eni yok. Sol/sağ kademe ayrı seçilir; yükseklik ve üst temel kalınlıkları bağımsız, alt temel tek kalınlık ve ortak kotta. Hatıl yalnız ek detaydır. Üst kot yüksek kademeye aittir. Kademe uzunluğu ile açılım adetleri; tek taraflı detayda alt temel donatısı/açılımı kaldırılır ve kesme işaretiyle kısaltılır. Yatay ölçüler çizimin üstünde 10 cm aralıklı çift sıra; düşey ölçüler kademelerin alt temele bakan iç yüzünde 10 cm aralıklı çift sıra. Kot tekrarları ayıklanır. Sayısal kontrol: `tools/Test-TemelDetayGeometry.ps1`; AutoCAD görsel kontrolü bekliyor.
- **OTODONATI**: formda «Alt donatı çizilsin» / «Üst donatı çizilsin». Kapalı katman çizilmez; şema konumu aynı kalır (ayrı atınca üst üste binmez).
- **KURANGLEZ**: form Adet (metraj); L’li etiketlere çarpan önek (`3x2x14ø… L=…`).
- **HASIRDONATI**: form + runner + gömülü HASIR/HASIRMETRAJ.
- **BAPANEL simgeler** (onaylı): 64 px, kalem 0.45 / 0.50 / 0.40.

## Önceki

- DONATIMETRAJ renkli balon: pastel, ardışık çaplar ayrı aile. Balon = tarama rengi. Rakam dolguya göre açık veya koyu. Tarama çıktıda kapalı.
- DONATIBOYU: metraj poz balonu (`POZ BALON` / `HPOZ BALON` / `POZ TARAMA` veya `ST4METRAJPOZ`) boy adayı değil.
- Ofis `Kur.cmd`: yalnız BEYKENT-2. Eski bundle, NETLOAD, APPLOAD ve acad.rx/lsp satırı silinir.
- Dilatasyon: kat sınırı dışına çıkan yüzde L/4 yok → gönye. GPR: uç yazı ilave; orta bant düz/pilye/montaj.

## Açık / test et

- [ ] KURANGLEZ: plan/kesit etiket, bindirme ölçü, adet çarpanı
- [ ] HASIRDONATI: alan seçimi, kenar kesim, bindirme
- [ ] X/Y düz-pilye doğru; ilave komşu mesnetinden; çalma yok
- [ ] DONATI ADET / DONATI OKU görsel

## Ana dosyalar

- `ST4_Yardimci/KuranglezForm.cs` / `KuranglezRunner.cs`
- `ST4_Yardimci/HasirDonatiForm.cs` / `HasirDonatiRunner.cs` / `HasirDonatiSettings.cs`
- `ST4_Yardimci/DosemeDonatiRunner.cs`
- `ST4_Yardimci/DosemeKonsolSettings.cs`
