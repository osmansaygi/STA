# ST4_Yardimci — durum

Güncelle: 2026-10-16

## Son DLL

Debug damga: `ST4_Yardimci_20261016_012852` (`NETLOAD\`).

## Son düzeltme

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
