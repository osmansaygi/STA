# STA4CAD STATIK klasörü — dosya sözlüğü

Örnek proje: `d:\projeler\projeler2026\ahmet_kustepe\adem_tafli\STATIK`  
Taban ad: **A_TFL** · STA4CAD **14.1** · müellif OSMAN SAYGI · proje ADEM_TAFLI  
Model kayıt: **22.09.2026 21:08** (`A_TFL.st4`, `A_TFL.GPR` ve analiz çıktılarının çoğu).  
Döşeme sonlu eleman dosyaları (**`.FEA` / `.RAD` / `.MAT*`**) **24.08.2026** tarihli; Eylül modelinden eski kalmış olabilir.

Bu klasörde alt klasör yok. Hepsi aynı STA4CAD projesinin parçası. Geometri **`.st4`**, donatı seçimi **`.SMX` `.KMX` `.DMX` `.TMX`**, aynı sonuçların metin dökümü **`.GPR`**.

---

## Donatı dosyaları (çözüldü)

GPR’deki cümleler bu dört ikili dosyadan basılıyor. Kayıtlar little-endian `int16` çiftleri: **adet, çap** veya **çap, aralık**. Aynı sayı GPR tablosunda da var.

| Eleman | Dosya | Kayıt | Donatı nerede |
|---|---|---|---|
| Kolon ve perde | **`.SMX`** | 32 × 300 bayt | ofset **161**’den 9 adet int16 |
| Kiriş | **`.KMX`** | 61 × 300 bayt | ofset **108**’den adet/çap çiftleri, etriye **146** |
| Döşeme | **`.DMX`** | 49 × 250 bayt | ofset **96**’dan çap/aralık çiftleri |
| Sürekli temel | **`.TMX`** | 57 × 250 bayt | ofset **100**’den adet/çap, etriye **132** |
| Panel (ayrı) | **`.PMX`** | 0 bayt | Bu projede üretilmemiş. Perde donatısı `.SMX` içinde (SZ-08, S1-08) |

`.COL` / `.BEA` / `.FLO` kuvvet ve geometri kaydıdır. Seçilen donatı `.SMX` `.KMX` `.DMX` `.TMX` içindedir. `.BAR` yalnız çap menüsü (8, 10, 12, 14).

### Kolon — `.SMX` ofset 161

Dokuz `int16`: etriye çapı, kritik aralık, üçüncü etriye sayısı, bayrak, yüz adedi, yüzde çubuk, çap, gövde adedi, gövde çapı.

| Kayıt | Baytlar | Anlam | GPR |
|---|---|---|---|
| 0 (SZ-01) | 10, 15, 8, 1, **2, 3, 14, 2, 14** | 2 yüzde 3Ø14 + 2 gövde Ø14, etriye Ø10/15/8 | SZ-01 aynı |
| 1 (SZ-02) | 10, **12**, 8, 1, **2, 4, 14, 3, 14** | 2×4Ø14 + 2×3Ø14, etriye Ø10/12/8 | SZ-02 aynı |
| 4 (SZ-05) | 10, 15, 8, 1, **2, 5, 14, 3, 14** | 2×5Ø14 + gövde 3Ø14 | SZ-05 |
| 7 (perde SZ-08) | **8, 16, 8**, 0, 0, **9, 14, 7, 12** | 9Ø14 + 7Ø12, etriye Ø8/16/8 | SZ-08 |

Kayıt 16 = S1-01, kayıt 0 ile aynı donatı (3+2 Ø14, Ø10/15/8); kat yüksekliği farklı.

### Kiriş — `.KMX` ofset 108

Çiftler `(adet, çap)`. Kayıt 0 = KZ-01, GPR ile aynı momentler (6.15, −2.06, −6.48 tm) ve aynı donatı.

| Ofset | KZ-01 | KZ-03 (kayıt 2) | Alan |
|---|---|---|---|
| 108 | 2, 12 | 2, 12 | montaj (sürekli üst) |
| 112 | 3, 12 | 3, 14 | düz (açıklık altı) |
| 116, 120 | 0 | 0 | pilye — bu projede 61 kaydın hepsi 0 |
| 124 | 0, 12 | 2, 12 | gövde |
| 128 | 2, 12 | 0, 0 | sol üst ilave |
| 132 | | | sol alt ilave |
| 136 | 2, 12 | 1, 12 | sağ üst ilave |
| 140 | | | sağ alt ilave |
| 146 | 8 | 8 | etriye çapı mm |
| 148 | 20 | 20 | etriye orta aralık cm |
| 150 | 9 | 9 | sarılma aralığı cm. 0 ise tek aralık (`Ø8/20`). Bu projede 59 kayıtta sarılma var, 2 kayıtta 150=0 |

Bu projedeki doluluk (61 kayıt): montaj 61, düz 61, pilye 0, gövde 17, sol üst 35, sağ üst 36, sol alt 1, sağ alt 0.

Aynı kayıtta `float`: +8…+28 momentler, +40…+56 fcd=166.67, +60…+76 As (cm²), +100 Asw.

### Döşeme — `.DMX` ofset 96

Çiftler `(çap, aralık cm)`. İlk üç kayıt boş (su basman panelleri). Kayıt 3 = DZ-01:

`8/28, 8/28, 8/30, 8/30` sonra `8/24, 8/24, 8/30`

GPR DZ-01: X `Ø8/28(düz)+Ø8/28(pil)+Ø8/30(sol)+Ø8/30(sağ)`, Y `Ø8/24(düz)+Ø8/24(pil)+Ø8/30(sol ilave)`.

Kayıt 5 = DZ-03, yalnız `Ø8/20`. Kayıt 6 = DZ-04, X’te `Ø10/21` ve `Ø10/29` ilaveleri.

### Temel — `.TMX` ofset 100

Kayıt adımı 250 bayt. İlk dolu grup: **4, 12** montaj, **7, 12** düz, **3, 12** ilave, **2, 8, 20** etriye. GPR T1: `4Ø12(mon.)` + `7Ø12(düz)` + `3Ø12(sağ alt ila.)` + `2Ø8/20`.

### Panel

`.PMX` boş. Bu modelde perde, kolon kaydı olarak `.SMX`’te (25×250, SZ-08 / S1-08). Ayrı panel betonarme bölümü GPR’de de yok; perde için kesme güvenlik tablosu var, düşey donatı `.SMX`’te.

---

## Kısa cevap: kolon donatısı nerede?

Seçilmiş kolon donatısı **`.SMX`** içindedir (yukarıdaki tablo). Aynı cümle düz metin olarak **`A_TFL.GPR`** bölümünde de basılır: **KOLON BETONARME HESAP SONUÇLARI** (satır ~8052).

Örnek (IBM857; ekranda `Ø` yerine DOS karakteri gelir):

| GPR adı | Düğüm | Kesit | Boyuna | Etriye |
|---|---|---|---|---|
| S1-01 | I:16 J:5 | 30×50, Hk=3.30 m | 2Ø3Ø14 + 2Ø2Ø14 (gövde) | 2Ø10/15/8 |
| SZ-01 | I:5 J:1 | 30×50, Hk=4.55 m | aynı | 2Ø10/15/8 |
| S1-02 | I:13 J:4 | 30×50 | 2Ø4Ø14 + 2Ø3Ø14 (gövde) | 2Ø10/15/8 |
| SZ-02 | I:4 J:2 | 30×50 | aynı | 2Ø10/12/8 |

Aynı kimlik `.COL` kaydında da var (I, J, koordinat). Örnek: S1-01 kaydı koordinatı **5.45 , 0.25** ve kat yüksekliği **3.30 m**. SZ-01 aynı X,Y ve **4.55 m**. Yani `.COL` “hangi kolon” sorusunun analiz kaydı; “kaç tane, hangi çap” cümlesinin tam metni GPR’de.

Bu klasörde çubukların kesit içi X,Y koordinatlarını listeleyen ayrı bir dosya **yok**. STA4CAD kesiti `2Ø3Ø14+2Ø2Ø14(gövde)` kuralından çizer. Çubuk noktası istenirse bu metinden türetilir.

`.BAR` kolon donatısı değildir. Projede kullanılabilecek çap menüsüdür: **8, 10, 12, 14** mm (ilk 4 satır). Çap büyütme kuralımız bunu okur.

---

## İki ana dosya

### `.st4` — model (girdi)

Metin, bölüm başlıkları `/Story/`, `/Axis data/`, `/Beams Data/`, `/Floors Data/`, `/Columns Data/`, `/Walls/`, `/Continuous foundations/` …

Bu projede:

- Katlar: Su basman (kot 1.25), Zemin (4.55), 1. Normal (7.85). Kolonlar zemin ve 1. normalde; 16 aks düğümü × 2 kat = **32 kolon kaydı**.
- Kolon ebatı ST4’te var (ör. `101,30,50,...`), donatı yok.
- Kiriş, döşeme poligonu, sürekli temel (T1…T13), perde/duvar burada.
- Aks geometrisi `/Axis data/` satırında; aks **isimleri** `.AKS` dosyasında (1…5 ve A…G).

`.BAK` aynı biçimin **24.08.2026 yedeği** (güncel `.st4`’ten birkaç yüz bayt farklı). Çizim için güncel olan `.st4`.

### `.GPR` — grafik hesap raporu (çıktı metni)

~808 KB, **13706 satır**, kod sayfası **IBM857** (Türkçe DOS). AutoCAD’de tablo gibi görünen şey aslında bir çizim listesi:

- Çizgi: `x1,y1,x2,y2,...`
- Yazı: `x,y,0,0,-10,1` satırının **hemen altındaki** metin
- Tablo satırı: `x,y,0,0,8,1` satırının altındaki kutu çizgili metin

İçindekiler `.IDX` dosyasında (sabit genişlikli başlıklar). Bu projedeki bölümler:

| GPR bölümü | Ne işe yarar | ST4 karşılığı |
|---|---|---|
| GENEL BETONARME ÇİZİM OPSİYONLARI | paspayı, min çap, etriye aralığı | `.OPS` |
| DEPREM RAPORU | TBDY2018, modal, kat kuvvetleri | `.EAR` `.CQC` `.FeaSeismic` `.SSM` |
| DÖŞEME STATİK / BETONARME | moment, As, `Ø8/28(düz)+Ø8/28(pil)` | döşeme poligonu `.st4` `/Floors Data/` |
| KİRİŞ STATİK / BETONARME | M, As, `3Ø12(düz)`, `Ø8/20/9(etriye)` | `/Beams Data/` |
| KOLON STATİK | N, M, eksenel | `/Columns Data/` + `.SMX` |
| KOLON BETONARME | seçilen donatının metin kopyası | asıl kayıt `.SMX` |
| KOLON / PERDE KESME GÜVENLİK | kesme kontrolü | — |
| TEMEL STATİK / BETONARME | sürekli temel donatısı `7Ø12(düz)`, `2Ø8/20(etriye)` | `/Continuous foundations/` |

Döşeme örneği (DZ-01, d=15 cm): X `Ø8/28(düz)+Ø8/28(pil)+Ø8/30(sol)+Ø8/30(sağ)`.  
Kiriş örneği (KZ-01, 25×50): `2Ø12(mon.)` + `3Ø12(düz)` + `Ø8/20/9(etriye)`. Momentler **6.15 / −2.06 / −6.48 / 1.14 tm** — bunlar `.KMX` ilk kaydı ile aynı sayılar.  
Temel örneği (T1, Bo/D=70/50): `4Ø12(mon.)` + `7Ø12(düz)` + `2Ø8/20(etriye)`.

---

## Hangi uzantı ne işe yarar

Gruplar, “çizim için oku” / “kuvvet arşivi” / “çözücü artığı” diye ayrıldı.

### Çizim ve donatı için okunacaklar

| Dosya | Boyut | Biçim | İş |
|---|---|---|---|
| `A_TFL.st4` | 22 KB | metin | Geometri, kat, malzeme, yük tanımı. Donatı yok. |
| `A_TFL.GPR` | 808 KB | IBM857 metin | Betonarme sonuç **metinleri** (kolon, kiriş, döşeme, temel, perde kesme). |
| `A_TFL.IDX` | 6 KB | sabit kayıt + metin | GPR içindekiler. |
| `A_TFL.BAR` | 659 B | metin | Çap listesi 8 10 12 14. Üretici satırı ÇESAN. |
| `A_TFL.AKS` | 123 B | metin | Aks etiketleri: düşey 1–5, yatay A–G. |
| `A_TFL.OPS` | 942 B | metin `Ver14.02` | Betonarme seçenekleri (min oran, etriye, font ROMANSTA). 28.02.2026; Eylül hesabından eski olabilir. |
| `A_TFL.USE` | 1.4 KB | metin | Duvar ve kaplama yük kütüphanesi (tuğla, gazbeton, fayans, şap). 28.11.2025. |
| `AKS.not` | 235 B | metin | Hangi ST4 açık, ekran orijini. |
| `LASTREAD` / `WORKING` | 11 B | metin | Son açılan dosya adı: `A_TFL.ST4`. |

### Kolon / kiriş / döşeme / perde / temel analiz kayıtları (ikili)

Bunlar GPR tablosunun **sayısal ham hali**. Donatı cümlesi burada düzgün bir tablo olarak durmuyor; kuvvet, düğüm ve koordinat duruyor.

| Dosya | Kayıt | Bu projedeki anlam |
|---|---|---|
| `.COL` | 32 × 900 bayt | Her kolon×kat. Başta düğüm I,J (GPR’deki I: J: ile aynı), sonra X,Y ve yükleme zarfları. 16 kolon × 2 kat. |
| `.SMX` | 32 × 300 bayt | **Kolon ve perde donatısı** (ofset 161) ve kuvvetler. İlk kayıtta N=**35.889 t** = GPR SZ-01. |
| `.CAP` | 7808 bayt, kayıt başı ~244 bayt | Kolon kapasite / etkileşim eğrisi noktaları (ilk değer 1.0, ardından büyük kuvvetler). Çubuk yerleşimi değil. |
| `.BEA` | 61 × 900 bayt | Kiriş analiz kayıtları (61 kiriş parçası). |
| `.KMX` | 61 × 300 bayt | **Kiriş donatısı** (ofset 108) ve moment/As. İlk kayıt KZ-01. |
| `.FLO` | 49 × 200 bayt | 49 döşeme paneli (ST4 `/Floors Data/` adedi ile aynı). İçerik katsayı/sonuç; donatı metni GPR’de. |
| `.PDE` | 17 × 300 bayt | Perde/panel analiz kayıtları. ST4’te duvar satırı biraz daha fazla; bir kısmı birleşik. |
| `.PMX` | 0 | Ayrı panel donatısı üretilmemiş. Perde donatısı `.SMX` kaydında. |
| `.THE` | 133 × 300 bayt | Sürekli temel (temel kirişi) analiz blokları. Donatı metni GPR “TEMEL BETONARME”. |
| `.TMX` | 57 × 250 bayt | **Sürekli temel donatısı** (ofset 100). T1: 4Ø12 + 7Ø12 + etriye 2Ø8/20. |
| `.SGL` | 57 KB | Tek satırlık, okunur ama anlamsız görünen sıkıştırılmış/özel metin. ST4’te tekil temel yok (`/Single footings/` boş). Tekil temel donatısı burada çözülmüş bir tablo değil. |
| `.HCR` | 256 bayt = 16 kolon × 16 bayt | Yalnız 8. kolon (perde 25×250) dolu: yaklaşık 7,85 m, 3,30 m, 0, 4,51 m. GPR’de de Hcr satırı yalnız SZ-08 altında. |
| `.TBI` | 1584 bayt | Küçük ikili tablo (bayrak + 1.0 / 0.15 gibi katsayılar). Ayrı bir donatı listesi değil. |
| `.DWL` | 4554 bayt | İkili; içinde koordinat şamandırası (ör. 14.2 m aks). Filiz/detay çizgisi artığı. Donatı adet tablosu değil. |

`.COL` kuyruğunda (ofset ~716) `01 21 08 02` gibi baytlar, etriyesi `2Ø10/15/8` olan kolonlarda tekrarlanıyor; perde kolonda (S1-08, 25×250) bu dizi bozuluyor. Bu, tasarım bayrağıdır. Güvenilir donatı metni yine GPR satırıdır.

### Deprem, kat rijitliği, deplasman

| Dosya | Biçim | İş |
|---|---|---|
| `.EAR` | metin | Kat deprem kuvvetleri, periyoda bağlı sayılar, sonda `STA4-CAD`. |
| `.CQC` | metin | Modal katılım (6 mod). Periyotlar ~0.70, 0.57, 0.51 s. |
| `.TP1` `.TP2` | metin | Periyot çifti. TP2 içinde `//ALFA//`. TP2: Tx=0.447 Ty=0.400 (GPR deprem raporu ile aynı). |
| `.SSM` | metin | Kat rijitlik matrisi 3D (S1/S2, dx dy rz) ve indirgenmiş X/Y/Rz. |
| `.CST` | ikili | Kat sabiti. İlk şamandıralar **1, 4.55, −4.30, 7.43** — kat kotları. |
| `.MASS` | metin, 2 satır | Kat kütleleri ve kütle merkezleri (iki kat). |
| `.DIS` | ikili | Deplasman vektörleri (çok küçük sayılar, mm/m mertebesi). |
| `.DMX` | 49 × 250 bayt | **Döşeme donatısı** (çap/aralık). Deplasman dosyası değil. |
| `.DRP` | metin (Türkçe, DOS) | Düzensizlik özeti: “Dinamik kütle oranı yeterli”, R=4, eksantrisite %5, A1/B2, göreli öteleme kontrolleri Ok. |
| `.LOA` | ikili | Yük aktarımı. İlk şamandıralar **0.15, 2.00, 0.081, 1.17** — döşeme yükü / kiriş çizgisel yük mertebesi. |
| `.SEE` | metin | 3B düğüm listesi `//NOKTA KOORDINATLARI//` (X Y Z, metre). |
| `.KNT` | metin | Aks düğüm bağlantısı: `1001,0,2001,2006,açı`. Geometri grafı. |
| `.ZEM` | ikili | Zemin yay / temel deplasman artığı. |
| `.Wdrc` `.Wmem` | ikili | Rüzgâr yönü ve rüzgâr yük belleği. |
| `.GIF` | GIF87a | Ekran tamponu. Rapor değil; donatı yok. |

### Sonlu eleman artıkları (büyük, çoğu 24.08.2026)

| Dosya | Boyut | İş |
|---|---|---|
| `A_TFL-K1.FEA` | 57 KB metin | 1. kat döşeme FEA ağı. Başlık `V14.01`, ST4 yolu, beton 250/4200, çaplar 8 10 12 14, sonra panel koordinatları (`.545,.5,5.3,0,...`). |
| `A_TFL-K2.FEA` | 44 KB metin | 2. kat (1. normal) aynı biçimde. |
| `A_TFL#.RAD` | 161 KB metin | Radye/plak FEA ağı (`V14.01`, ST4 yolu, dörtgen köşe koordinatları, z=−0.20). |
| `.MAT0` `.MAT1` `.MAT2` | 20 MB + 14 MB + 8 MB | Kat rijitlik matrisleri (seyrek). Çizim verisi değil. |
| `.Fea3D` | 4.1 MB | 3B çözüm vektörü. Baştaki tamsayılar 1879, 2243, 321… serbestlik/düğüm sayımı. |
| `.FeaNode` | 73 KB | FEA düğüm tablosu. |
| `.FeaVec` | 323 KB | Mod vektörleri. |
| `.FeaMod` | 26 KB | Modal sonuç (ilk periyot şamandırası ~0.98 s civarı ham değer; rapor periyodu GPR/TP2’de 0.447 ve 0.400). |
| `.FeaBlk` | 1 KB | GPR’deki “YAPI DEPREM YÜKLERİ” bloğunun ham kopyası: W=572.36, Fdeprem X=57.05 Y=63.72. |
| `.FeaSeismic` | 412 B metin | Aynı deprem özeti, sayılarla: Tx=0.4467 Ty=0.3999 R=4. |
| `.PanFea` | 4 bayt 0 | Panel FEA çalışmamış. |
| `.3dBackup` | 137 KB ikili | 22.09.2026 3B model yedeği. |
| `WORKFEA` | metin | Son FEA hedefi `A_TFL-K2` (Ağustos tarihiyle uyumlu). |
| `.ERR` | 0 | Hata günlüğü boş. |
| `.KenPer` | 0 | Kenar perde listesi boş. |
| `StaDwg1.sta` | 2730 B ikili | Çizim oturumu (kalem/katman sırası gibi tamsayılar). Geometri kaynağı değil. |

---

## ST4 ↔ GPR ↔ ikili dosya eşlemesi

```
.st4 geometri  ──hesap──►  .COL .BEA .FLO .PDE .THE .SMX .KMX .TMX .CAP
                      └──►  .GPR metin tabloları  (donatı cümlesi burada)
.BAR .OPS .USE .AKS   girdi yardımcıları (çap, seçenek, yük kütüphanesi, aks adı)
.EAR .CQC .SSM .MASS .TP2 .FeaSeismic   deprem raporu sayıları
.FEA .RAD .MAT* .Fea3D    sonlu eleman; bu projede modelden 1 ay eski
```

Kesişen bir sayı üç yerde durabiliyor. Örnek kiriş KZ-01 momenti 6.15 tm:

- GPR kiriş betonarme tablosunda yazılır
- `.KMX` ilk kaydının şamandırasıdır
- kesit ve aksı `.st4` `/Beams Data/` içindedir (`25,50` gibi)

Örnek kolon SZ-01 eksenel 35.889 t:

- GPR kolon statik / betonarme tablosunda N
- `.SMX` ilk kaydı
- donatı adedi ve çapı `.SMX` ofset 161’de; aynı cümle GPR’de; ebatı `.st4` kolon satırında 30×50

---

## Bizim çizim için pratik kaynak

| İhtiyaç | Dosya | Not |
|---|---|---|
| Aks, kolon/kiriş/perde/döşeme geometrisi | `.st4` | Zaten okunuyor |
| Aks adı | `.AKS` | 1–5, A–G |
| Kolon boyuna + etriye | **`.SMX`** ofset 161, metin kopyası `.GPR` | KOLONDATA bugün GPR metnini okuyor |
| Kiriş donatısı | **`.KMX`** ofset 108, sıra = `.st4` `/Beams Data/` | GPR kiriş betonarme metin ikizidir; satır sırası kayabilir |
| Döşeme donatısı | `.GPR` → DÖŞEME BETONARME | donatı planı (sağ kopya) |
| Sürekli temel donatısı | `.GPR` → TEMEL BETONARME | |
| Çap büyütme menüsü | `.BAR` | 8, 10, 12, 14 |
| Min çap / paspayı kuralları | `.OPS` veya GPR opsiyon sayfaları | |
| Duvar yükü isimleri | `.USE` | |
| Kolon kapasite eğrisi | `.CAP` | metin değil; etkileşim noktaları |
| Çubuk kesit koordinatı | yok | GPR metninden üretilir |
| Güncel FEA ağı | `*-K1.FEA` `*-K2.FEA` `.RAD` | tarih 24.08; 22.09 modeliyle birebir sayma |

Okunmayan görünen dosyaların çoğu bozuk değil: STA4CAD’in tek duyarlıklı (float32) analiz çöplüğü. Metin kapısı `.st4` + `.GPR` + `.BAR` + `.AKS` + `.OPS`. Seçilen donatının kaydı `.KMX` `.SMX` `.DMX` `.TMX`; GPR bunların metin kopyasıdır.

## Kiriş dosyaları (ayrı)

Çerçeve kirişi. Eşleştirme isimle değil: **ST4 kiriş satır sırası = KMX kayıt numarası = BEA kayıt numarası**. GPR’de aynı kiriş `KZ-` (zemin) veya `K1-` (1. normal). 61 geometri = 61 KMX = 61 GPR `Mduz` satırı. 54 farklı isim; aynı numara iki açıklıkta iki kayıt olur.

| Dosya | Kirişte ne işe yarar |
|---|---|
| `.st4` `/Beams Data/` | Kesit, aks, kaçıklık. Zemin 37 (101–137), 1. normal 24. Perde bayrağı yok. Donatı yok. |
| `.KMX` | Seçilen donatı ve moment. Ofset tablosu yukarıda. Pilye yok. |
| `.BEA` | 61 × 900 bayt kuvvet kaydı. Donatı cümlesi değil. Ofset **0** ve **8** (`int16`): sol / sağ düğüm no; **0** = o uç kolon düğümü değil. Ofset **36** (`int16`): uç tipi. Açıklık ortasına saplanan kirişin adı veya “var/yok” bayrağı yok. |
| `.GPR` | KİRİŞ YÜK, STATİK, BETONARME, SEHİM, KESME GÜVENLİĞİ, ASKI DONATISI, KİRİŞ BETONARME OPSİYONLARI |
| `.OPS` | Paspayı 5,5 cm, min çap 12/12/12/8, sehim L/360 ve L/240. Çap yeniden seçilmez. |
| `.BAR` | Kullanılabilecek çaplar 8, 10, 12, 14 |
| `.LOA` | Kirişe aktarılan çizgisel yükün ham hali |

Çerçeve kirişi olmayanlar: `.THE` + `.TMX` sürekli temel kirişi; `/Walls/` + `.PDE` perde (donatı `.SMX`); `/Tie beams/` boş; `.FEA` döşeme ağı, kiriş donatısı tutmaz, tarihi 24.08.

### Uç tipi (konsol / basit / sabit)

GPR kiriş statik tablosunda, deprem satırının sol hücresindeki kutu sembolü. Sayısal karşılığı yalnız **`.BEA` ofset 36** (`int16`). `.st4` ve `.KMX` içinde bu alan yok. Kayıt sırası ST4 kiriş sırasıdır.

Sembol iki ucu ayrı çizer: `├`/`┤` sabit, `┌`/`┐` basit, çizginin açık kalması konsol (serbest uç).

| Kod | Sembol | Anlam | Bu projede |
|---|---|---|---|
| 0 | `├────┤` | iki uç sabit | 51 kiriş (KZ-01 dahil) |
| 5 | `┌────┐` | iki uç basit | KZ-03, KZ-04, KZ-13, KZ-14 |
| 1 | `┌────┤` | sol basit, sağ sabit | KZ-07 |
| 2 | `├────┐` | sol sabit, sağ basit | KZ-08 |
| 4 | `├─────` | sağ uç konsol | KZ-17, KZ-29 |
| 3 | `─────┤` | sol uç konsol | KZ-19, KZ-31 |

Açıklık ortasına kiriş saplanması bu ofsette yok. Tali kirişin kendi kaydında ilgili uç düğümü 0’dır (KZ-03, KZ-04, KZ-13, KZ-14 iki uç da 0). Ana kiriş saplama noktasında iki kayda bölünür; ortak uç yine düğüm 0’dır (KZ-22, KZ-20, KZ-21, KZ-26, KZ-27, KZ-28, K1-19). Konsol uç da düğüm 0 kullanır; ayrım ofset 36’dadır (3 veya 4). “Kim kime saplandı” listesi GPR **KİRİŞ ASKI DONATISI** tablosundadır (ör. KZ-22 ← KZ-03). Yeri santimetre olarak `.st4` kesişiminden çıkar.

## Sehim tablosu (istendiğinde bu çizilecek)

Kod henüz yok. Referans: `d:\projeler\projeler2026\ali_babaoglu\vakiflar\statik\VKF_01_ka1.dwg` — başlık **BODRUM KAT UZUN SÜRELİ TERS SEHİM TABLOSU mm**. Düzen: 4 sütun çifti `No | Sehim`. Önce o katın kirişleri, sonra döşemeleri. Değer mm, 1 ondalık.

| Eleman | Dosya | Hangi sayı |
|---|---|---|
| Döşeme | `.GPR` → DÖŞEME STATİK HESAP SONUÇLARI | X ve Y sehiminden **büyük olan** (VKF bodrumda hep Y). X satırındaki `sehim < fmax` hücresi değil. 26/26 uydu. Örnek DB-01: çizim 0.8 = Y 0.80; X 0.39 < 9.58. |
| Kiriş | `.BEA` | Kayıt 900 bayt, ofset **760**, float32, **metre × 1000**, 1 ondalık. Kayıt sırası = GPR kiriş listesi. Çok açıklıkta çizim tek sayı basar (KB-17: 11.135 / 11.135 / −0.155 → **11.1**). |

VKF bodrum kirişinde 15/23 uydu (KB-01…11, KB-14, KB-17, KB-20, KB-22). Uymayanlar: KB-12 (çizim 8.6, BEA 1.035), KB-13, KB-15, KB-16, KB-18, KB-19, KB-21, KB-23. KB-12’nin 8.6 değeri bodrum BEA kayıtlarında yok. `.IDX` kiriş sehim başlığını listeler; bu GPR gövdesinde o sayfa yok. `.OPS` yalnız L/360 ve L/240 sınırıdır.
