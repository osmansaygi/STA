# ST4_Plan_ID_Ciz — durum

Güncelle: 2026-10-10

**ST4PLANID:** yerleşim noktası = en soldaki antet SheetViewOut sol-altının 50 cm solu. Her plan (temel + katlar) kalıp/temel antetinde; antetler arası 50 cm.

Son DLL: `ST4_Plan_ID_Ciz_20261010_145946.dll`

## Son DLL

`ST4_Plan_ID_Ciz\bin\Debug\net48\ST4_Plan_ID_Ciz_20261010_145946.dll`

## Son düzeltme

- **Kiriş açılımı basit uç (KB-067):** Code 1 kaydında iki düğüm de 0. İki komşu kiriş aynı mesnet izini paylaşıyordu; bir uç “basit değil” deyince izdüşüm siliniyordu. Kirişin kendi ucundaki izden (aynı numaranın devamı değilse) aşağı izdüşüm ve 1-2 / 3-4 iner. Ortadan saplanan ara kesit aynı.
- **Kiriş açılımı mesnet köşe numaraları (1-2 / 3-4):** saplama/konsol kesitine artık `SolUc`/`SagUc` bayrağı işleniyor; `chainMid` kıyası kaldırıldı. Aynı kesite iki kiriş (biri soldan biri sağdan) saplanırsa iki yüz bağımsız numaralanır (sol uç 1-2, sağ uç 3-4). Kolon ucu numarası da BEA düğümü 0 olan (saplama/konsol) uçta tekrar yazılmaz — çift numara önlendi.
- **Kesit köşe kayması:** gövde çizgisi NTS ölçeği 2 (0,5 cm ızgara) yüzünden grobetondan ayrılıyordu. Gövde ve grobeton artık ölçek 100 (0,01 cm).
- **Temel kesiti yan detayları:** Drawing2 grobeton (AR-CONC) köşesi çapa. A-A: sağ detayın grobeton **sol-üstü** kesit grobetonunun sağ-alt altına, sol detayın grobeton **sağ-üstü** sol-alt altına. B-B: aynı iki detay 90° CCW; sağ detay grobeton dış kenarının üst ucuna, sol detay alt ucuna.
- **Kesit temel arası basamaklı dolgu tek hatch:** ampatman üst hizasında geniş+dik iki parça tek kapalı poligon + tek hatch.
- **Ampatmanlı sürekli temel:** ST4 3. satırından **Da** (`AmpatmanHeightCm`); plan `TEMEL AMPATMAN` katmanında; hizalama `align 0` gövde merkezine; kesitte `Ba × Da` basamak.
- KIRISACILIMLARI ilave boyu: φ<20'de **net açıklık**; `destekCm` düşme kaldırıldı. İlave donatı sol/sağ `Cross0 == StartAxisId`.
- BINAKESITI şematik kesit (MS_B_01): önce en çok kat döşemesi.

## Not

**Döşeme etiket:** Yeni gruplama (kot+kalınlık komşu, çoklu yük, DB renumber) **yalnız MANTARDOSEME**. KALIP50/100 + ST4PLANID → eski `ComputeSlabIdsToLabel`.

**MANTARDOSEME:** 3 kopya; mantar kat sınırı/boşluk/kesim. **KOLONDUSEY:** mantar kiriş + poligon başlık denge; kesit-içi isim değişimi.

- STAPANEL: her komutun simgesi ayrı. Değiştirmek için `%LocalAppData%\ST4Icons\KOMUT.png`.

## Açık

- [ ] Mantar: GPR donatı ince ayar
- [ ] Kiriş açılımı: donatı, etriye bölgeleri, ölçü zinciri, kesitler

## Referans

Kiriş donatı çizimi kuralları/sınır değerler: [../kiris-donati-cizim-referansi.md](../kiris-donati-cizim-referansi.md) (TOPÇU Betonarme I sf.154–179 özeti).
