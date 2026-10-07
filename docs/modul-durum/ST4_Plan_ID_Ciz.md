# ST4_Plan_ID_Ciz — durum

Güncelle: 2026-10-07

**ST4PLANID:** yerleşim noktası = en soldaki antet SheetViewOut sol-altının 50 cm solu. Her plan (temel + katlar) kalıp/temel antetinde; antetler arası 50 cm.

Son DLL: `ST4_Plan_ID_Ciz_20261007_134950.dll`

## Son DLL

`ST4_Plan_ID_Ciz\bin\Debug\net48\ST4_Plan_ID_Ciz_20261007_134950.dll`

## Son düzeltme

- BINAGORUNUS Hepsi: antet min. yüksekliği SheetViewOut 4500 cm (önce iç 4500 + üst/alt 50 pay yüzünden 4600 çıkıyordu). İçerik taşarsa 500 cm adımlarla büyür.

## Not

**Döşeme etiket:** Yeni gruplama (kot+kalınlık komşu, çoklu yük, DB renumber) **yalnız MANTARDOSEME**. KALIP50/100 + ST4PLANID → eski `ComputeSlabIdsToLabel`.

**MANTARDOSEME:** 3 kopya; mantar kat sınırı/boşluk/kesim. **KOLONDUSEY:** mantar kiriş + poligon başlık denge; kesit-içi isim değişimi.

- STAPANEL: her komutun simgesi ayrı. Değiştirmek için `%LocalAppData%\ST4Icons\KOMUT.png`.

## Açık

- [ ] Mantar: GPR donatı ince ayar
- [ ] Kiriş açılımı: donatı, etriye bölgeleri, ölçü zinciri, kesitler
