# ST4_Plan_ID_Ciz — durum

Güncelle: 2026-10-13

**KALIP50 döşeme donatısı:** GPR tablo sütunu 3 haneli döşeme no’yu kesiyor (`D3B-001` → `D3B-00`). Aynı anahtar ezilince çoğu döşeme boş kalıyor, DMX de sahte uyuşmazlıkla eleniyordu. Kesik anahtarda donatı ST4 sırasındaki `.DMX` kaydından yazılıyor (ARD_A_02: 1104 döşeme).

## Son DLL

`ST4_Plan_ID_Ciz\bin\Debug\net48\ST4_Plan_ID_Ciz_20261013_120746.dll`

## Son düzeltme

- **KALIP50/100 şematik kesit:** X+Y bina kesiti antetin içinden çıktı. Alt başlığın en alt kenarı antet üstünün 500 cm üstünde; sol hiza aynı (antet solundan 150 cm).
- **KALIP50 çatı kolonları (ARD_A_02):** 1000’lik projede kesit yoksa `kat*100+kolonNo` yedeği, kolon no ≥ 100 iken 1. kat kesidine düşüyordu (çatı 104 → 1004, 80×30). Çatıda 40 sahte kolon. Yedek numara 1000 ve üstündeyse artık reddediliyor. Gerçek çatı kesitleri 9052–9093 duruyor.
- **KALIP50 çökme (ARD_A_02):** bina kesitinde `Buffer(0)` / büyüyen siluet birleşimi yığın taşırıyordu. Geçerli geometri aynı; bozuksa 0,01 cm + GeometryFixer.


- **KOLONDUSEY benzer grup + temel hatılı:** temel/temel hatılı durumu artık benzerlik imzasına girer (`KolonDuseyTemelHatilImza`: F=temel, H=radye temel temel hatılı yüksekliği). Temel hatılı olan/olmayan kolonlar (ve perde başlıkları) ayrı grup çizilir. KOLONDUSEY (`BuildKolonDuseyGroupSignature`) kolon bazında; KOLONDUSEY2 (`KolonDuseyKatBenzerImza`) yalnız en alt kat görünüşünde.
- **Kapama perde + kolon görünüşü temel hatılı filtresi:** görünüşte TEMEL HATILI artık yalnız "radye temel temel hatılı" (sürekli temel üstü hatıl şeridi + sürekli temel/radye içindeki Tie beams) için çizilir; dışarıdaki Tie beams = "bağ kirişi" hatıl sayılmaz. `EnsureTemelFootprintCaches` + yeni `GetColumnRadyeTemelHatiliHeights` (KOLONDUSEY fallback'i de buna geçti).
- **KAPAMADETAY form:** komut artık form açıyor → "Temel hatılı olsun / olmasın" (iki radyo seçenek). `KapamaPerdeForm` + `KapamaPerdeSettings` (`%LocalAppData%\ST4_Plan_ID_Ciz\kapamaperde.ini`). `DrawKapamaDetayFromSt4(temelHatili:)` görünüşte hatıl toplamayı bu seçime göre açar/kapatır.
- **Kiriş açılımı:** en alttaki kolon kesiminden ilave 30, montaj 45, gövde 60 cm aşağıda tek çubuk. Yan yana olanlar 5 cm şaşar. Açılımda `L=` var; görünüş içi ilave yazısında boy yok.
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
- [ ] Sehim tablosu (henüz kod yok): istendiğinde `VKF_01_ka1.dwg` düzeni — kat kirişleri sonra döşemeler, `No | Sehim`, mm 1 ondalık. Kaynak sözlükte: [../sta4cad-statik-dosyalari.md](../sta4cad-statik-dosyalari.md) «Sehim tablosu».

## Referans

Kiriş donatı çizimi kuralları/sınır değerler: [../kiris-donati-cizim-referansi.md](../kiris-donati-cizim-referansi.md) (TOPÇU Betonarme I sf.154–179 özeti).
