# ST4_Yardimci — durum

Güncelle: 2026-09-30

## Son DLL

`NETLOAD\ST4_Yardimci_20260930_193516.dll`

## Ne çalışıyor (özet)

- Açılım: etiket `nøcap/aralik L=boy`; kol boyu ByLayer; etiket çakışınca boy alt/üst soluna.
- Basamaklı kesit: 1. ve 3. bölge kalınlığı (temelde 1. bölge yok); basamak alt ile sanal üst arası bel ölçüsü. 1/25 basamaksız sahanlıkta kolon/perde/kiriş kalınlığı ayrı (25+260).
- Hız: kesit sınırı bir kez toplanır, sembol dwg bir kez açılır. Basamak kutusu tüm çizimi değil, bu komutta çizilenleri ve yakındaki eski elemanları tarar.
- Kesitte tevzi ve ana donatı numarası yok. Kesit başlığı kol harfi (A, B, …).
- Kesit kotu (KOLONDUSEY2 sembol): 1. bölge üst, 3. bölge üst, basamaksız üst; alt kottan rıht birikimi.
- Temel üst kotu = form alt kotu. Dış çıkıntı ilk rıhttan 80 cm, iç çıkıntı ikinci rıhttan 50 cm. Soffit/sahanlık altı temel içinde silinir.
- Temelde 1. açılım 70 cm dışa, 2. açılım 50 cm dışa. İkisinin tepesi temel alt çizgisinin 10 cm altında; diğer açılımlar aynı kadar iner ve kesit kutusunun içinde kalır.
- Temelde 3 ve 4. donatı, temel üst kotunun altında kalan kısımdan kesilir.
- Temelde 1. tevzi, basamak altına paralel, temel üstünün 4.5 cm üstünde; 28 ve 1–29 arası ona göre. Temelde 4. tevzi yok.

## Açık / test et

- [ ] Basamak kesit detayı görsel (120×15, adet/gönye/açılım)
- [ ] 1/25 plan rıht no / etiket 12 / ölçü zinciri görsel

## Ana dosyalar

- `ST4_Yardimci/MerdivenRunner.cs`, `MerdivenForm.cs`
- `.cursor/rules/merdiven-kesit-donati.mdc`
