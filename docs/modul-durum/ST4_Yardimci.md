# ST4_Yardimci — durum

Güncelle: 2026-10-06

## Son DLL

`NETLOAD\ST4_Yardimci_20261006_163823.dll`

## Son düzeltme

- Döşeme donatısı ofis göçme: AutoCAD native IntersectWith yok (C# doğru kesişimi); NTS tüm kat yerine 800 cm karo. Pilye/düz/ilave aynı. Setup 2026.10.06.6.

## Önceki

- Döşeme donatısı: katman kapat + çift Regen kalktı. Açıklık poligonu bellekteki mesnet hücresinden; ekran gelip gitmez.

## Önceki

- `DONATI OKU`: iki nokta mesafe, üçüncü nokta ölçü çizgisi. DONATI OKU stili; `DONATI (BEYKENT)` kesişimine halka. Sonra etiket adedi (DONATI ADET ile aynı).

## Önceki düzeltme

- Ofis kurulum klasörü: `ST4_Yardimci\Setup-Ofis\Kur.cmd`. Yalnız BEYKENT-2 UUID’sinde kurulur; eski bundle/NETLOAD kaydını siler.
- `KATMANYUKLE`: palet/üst şeritte **Katman Yükle**. Çizgi tipi işlem dışında yüklenir.
- Kalıp boşluk içindeki döşeme sınırına donatı uzamıyor. Boundary poligonu hafızada kalır, çizilmez.
- TEMELCEKURU: 60 cm kalınlıkta 1 ve 3'ün dibinde kalan noktalar da siliniyor.
- Açıklık poligonu ve donatı altındaki döşeme adı çizilmez; ikisi de hafızada kalır, donatı yerleşimi aynı.
- Y ve hat 90° üstü: yalnız GPR bakışı solu okur. Donatı çizimi ve yazı açısı eskisi gibi.
- Mesnet ilavesi: demirin kestiği kiriş/perde/kolon çizgisinin üstündeki ilave yazısı kopyalanır.
- Poligon: açıklığın mesnet hücresinden, bellekte. TraceBoundary yok.
- Aynı döşeme adı (DB-23) ikinci kez taşkın poligona yazılmıyor; asıl hücrenin poligonu çiziliyor.
- Uç döşeme (hat yalnız kenardan girer, iki yüzü geçmez) kendi donatısını almıyor; boydan kesilenin ilavesi duruyor.
- Kiriş içindeki alt düz yazısı ek donatı ve poligon üretmiyor.
- Döşeme poligonu kiriş/kolon gövdesinin dışına taşmıyor.
- Palet açılışı: başlıktaki boşluk komut satırına düşmüyor (BAPANEL / BAUST).
- Döşeme poligonu: bayı kesen tek çapraz kenar dik köşeye çevriliyor; paralel duvar çifti kalır.
- Y mesnet: kiriş üstündeki uç GPR yazısı da ilave okunur (poligon dışı).

## Açık / test et

- [ ] X/Y düz-pilye doğru; ilave komşu mesnetinden; çalma yok
- [ ] Y üst ilave (DB-15/21) — mesnet bandı
- [ ] DONATI ADET görsel
- [ ] DONATI OKU: ölçü, halka, adet

## Ana dosyalar

- `ST4_Yardimci/DosemeDonatiRunner.cs`
- `ST4_Yardimci/DosemeKonsolSettings.cs`
