# Radye düğüm As — GPR'siz hesap araştırması (28.10.2026)

Araç: `dotnet build -c Release -o bin` → `bin\T.exe <veri klasörü> N_ULK_02 [eski GPR statik dökümü]`.
Girdi yalnız RAD + SO1 (GPR yalnız karşılaştırma için). Proje dosyalarına yazmaz; kopya üzerinde çalışır.

## Zincir (N_ULK_02, 516 mesh düğümü × 4 = 2064 değer)

1. **Statik Mx/My (17 yük):** SO1 eleman blokları 21×33 float32; satır 1–17 = yük, sütun `1 + köşe·8 + bileşen`; bileşen 3 = Mx, 4 = My. Düğüm değeri = bağlı eleman köşelerinin aritmetik ortalaması. GPR statik tablosuyla 7156/7740 (Mx), 7084/7740 (My) ±0.01; farklar üçgen komşuluğunda.
2. **Düğüm tasarım momenti:** TS500T.COD 10 kombinasyon × q1..q6 × (±Ex+, ±Ex−, ±Ey+, ±Ey−; 0.3 birleşimi YOK) × (±Wx, ±Wy); alt = max(0,·), üst = min(0,·). Mxy (Wood–Armer) kullanılmıyor — eklenince uyum düşüyor.
3. **"Bağlı düğüm ortalaması":** düğümün kendisi + kenar komşuları (tekil). GPR statik girdisiyle 1413/2064 tam (±0.01); düzenli iç düğüm (4 dörtgen) 711/788. 3 ve 5 elemanlı (üçgen) düğümlerde kural farklı — çözülmedi.
4. **As:** dikdörtgen blok, k = 0.85·fcd ≈ 158 kg/cm², fyd = 2800 kg/cm² (fyk/1.5), d = h − 7 cm; M > 0 ise As ≥ ρmin·b·d, ρmin = RAD 4. satır 1. alan (0.0015). GPR tasarım momentinden 76/77 tam (As > min olanlar).

## Uçtan uca (yalnız RAD + SO1) – GPR As ile

| Yön | ort. abs(ΔAs) | abs(ΔAs) ≤ 0.25 | max GPR / biz | İlave düğüm: ikisi / yalnız biz / yalnız GPR |
|---|---|---|---|---|
| X alt | 0.135 | 468/516 | 10.84 / 15.18 | 7 / 3 / 2 |
| X üst | 0.125 | 503/516 | 4.95 / 4.95 | 0 / 0 / 0 |
| Y alt | 0.084 | 485/516 | 9.15 / 8.23 | 0 / 2 / 1 |
| Y üst | 0.040 | 511/516 | 6.23 / 6.41 | 3 / 0 / 0 |

## Üçgen turu (28.10.2026, ikinci aşama)

Çalıştırma: `TRIZERO=1 SETK=6 bin\T.exe ..\data N_ULK_02 <eski gpr_radye.txt>` (eski döküm yalnız statik tablo karşılaştırması için).

- **Bulundu — üçgen köşe sıfırlama (SO1'de de geçerli):** üçgen elemanda 3. köşenin (K) Mx katkısı ve 2. köşenin (J) My katkısı 0 alınır, bölen eleman sayısı değişmez. Örnek n147 G Mx: (7.121 + 6.062 + 0)/3 = 4.39 = GPR. Statik eşleşme Mx 7156 → 7391/7740, My 7084 → 7444/7740. Kalan statik farklar, eski araştırmanın "kural uygulanmayan üçgen" listesindeki elemanlarda (1, 21, 36, 56, 338–341, …).
- **Kısmen — üçgende komşuluk:** üçgende yalnız I–J ve J–K kenarları komşu sayılır (K–I atlanır). n1 = {1, 2, 5} ile dört değer birebir. Toplamda küçük kazanç (1413 → 1421).
- **Çözülmedi — ortalama kuralı:** sabit komşu kümesinde düz ortalama modeli, düğüm değerleri serbest bırakılarak tersine çözülse bile GPR'yi vermiyor (`TERS=6`). Yani STA bazı düğümlerde bileşene göre farklı küme kullanıyor ya da ortalama başka bir ara değer üzerinden. Örnekler: n29 dört değer de {29, 35} ile tutuyor (sınır kenarındaki 20 dışarıda); n3 My yalnız {3, 8} ile tutarken Mx {3, 2, 8, 9} ile tutuyor. Yön/sınır kenarı istatistiği (`STAT=1`) temiz bir kural vermedi.

Son uçtan uca (TRIZERO=1, SETK=6):

| Yön | ort. abs(ΔAs) | abs(ΔAs) ≤ 0.25 | max GPR / biz | İlave: ikisi / yalnız biz / yalnız GPR |
|---|---|---|---|---|
| X alt | 0.107 | 483/516 | 10.84 / 12.91 | 7 / 1 / 2 |
| X üst | 0.125 | 503/516 | 4.95 / 4.95 | 0 / 0 / 0 |
| Y alt | 0.058 | 486/516 | 9.15 / 7.43 | 0 / 0 / 1 |
| Y üst | 0.030 | 511/516 | 6.23 / 6.23 | 2 / 0 / 1 |

İlk turdaki not: üçgen elemana bağlı düğümler (ör. n147 X alt: biz 15.18, GPR 10.84; n29 Y alt: biz 4.95, GPR 9.15). GPR'de n147 için M = 11.58 yazıp As = 10.84 vermesi, As'ın yazılan ortalama momentten değil başka bir ara değerden çıktığını gösteriyor.

## Üçüncü tur — sınır düğümleri (28.10.2026)

Kaynak taraması: yerel STA4 yardımı (STAHELP4 s.12–13) yalnız "ortalamayı aşan bölgelere ilave" diyor; internette STA4CAD'e özgü bir açıklama yok. Genel literatür (DKMQ/DKMT, STAAD) yalnız eleman köşe değerlerinin düğümde ortalamasını anlatıyor; bunu zaten uyguluyoruz.

Kategori başarısı (`KAT=1 TRIZERO=1 SETK=6`, tasarım momenti ±0.01):

| Düğüm | Tam eşleşme |
|---|---|
| İç, üçgen yok | 426/448 (%95) |
| İç komşusu sınırda, üçgen yok | 276/292 (%95) |
| Sınırda, üçgen yok | 88/128 (%69) |
| Sınırda, üçgen komşu | 218/600 (%36) |
| İki plağın ortak sınır düğümü | 1/72 |

Denenip elenenler: deprem 0.3 birleşimi (`EQM` 1–4, her kategoride kötüleşiyor), Wood–Armer, eleman merkezi tasarım momenti (`ELMERK`), yerinde sıralı ortalama (`SIRALI`), düğümü tek plağa sınırlama (`PLAKFILT`), sınır düğümlerini serbest bırakıp tersine çözme (`STERS`, tutmuyor), köşe-rol kural taraması (`KURAL`, en iyi 1380/2064), güvenli taraf (sınırda max: Y alt'ta 14–92 fazla ilave düğümü).

Tam bulunan tekil kümeler: n29 = {29, 35}, n463 = {463, 431, 496, 499}, n1 = {1, 2, 5} (dört bileşen de birebir). Bu kümeler ne köşe sırasıyla ne mesafe/yönle tek kurala bağlanabildi; STA'nın komşu listesini mesh değişikliklerinden (kolon düğümü/üçgen ekleme) önceki ızgaradan tuttuğu şüphesi var, kanıtlanmadı.

İlave kararı ayrışan düğümler (GPR / biz, cm²/m): Y alt n29 9.15 / 5.50; X alt n463 8.97 / 7.03; X alt n148 6.34 / 8.15; X alt n160 7.77 / 6.69; Y üst n181 5.96 / 5.64.

## Dördüncü tur — dört proje (28.10.2026)

Projeler (salt okunur kopya): N_ULK_02 (C25, h40, 3 plak), CP_03 (C30, h80, 1 plak), K_BLG_03 (C30, h70, 1 plak), VKF_01 (C30, h60, 3 plak, 28.10 18:31 çözümü). Yalnız N_ULK_02 GPR'sinde statik tablo var; diğerlerinde statik SO1'den alınır.

**As formülü — çözüldü (dört projede):**
- k = 0.85·fck/1.5 (C30: 170), d = h − 6.8 cm, fy = 2800 kg/cm² (fyk/1.5); dikdörtgen blok.
- Sınır: As(2800) > 0.0032858·b·(h−7) ise As = max(sınır, As(fyd = fyk/1.15)). İki projede aynı oran; N_ULK n147'deki "M 11.58 → As 10.84" ve CP_03'teki 23.99 tavanı bundan.
- Minimum: M > 0 ise As ≥ max(ρmin_RAD, 0.001)·b·(h−7).
- C30 regresyonu: iç kol z = d_eff − 0.083·As, d_eff = h − 6.75…6.82 (3 proje). Doğrulama (`ASN`): ±0.25 içinde 9/9, 277/277, 247/247, 69/69; ±0.05 içinde 9/9, 255/277, 238/247, 69/69.

**Tasarım momenti ortalaması:** kendi + kenar komşusu kuralı iç bölgede doğrulandı (üçgensiz iç düğüm: CP_03 1304/1308, K_BLG_03 2847/2912, VKF_01 587/648). Sınır ve üçgen komşuluğunda kural projeye göre değişiyor; köşe-rol taraması projeler arasında tutarlı kural vermedi. CP_03 n15'te Mx {15,8,23,16,24}, My {15,8,16,24} ile birebir (bileşene göre farklı küme).

Uçtan uca (`E2E=<fck> TRIZERO=1`, ±0.25 cm²/m; ilave: GPR'de olup bizde olmayan düğüm / en büyük kaçan ilave):

| Proje | X alt | X üst | Y alt | Y üst |
|---|---|---|---|---|
| N_ULK_02 | 488/516; 2 / 1.27 | 507/516; 0 | 486/516; 1 / 1.45 | 511/516; 1 / 0.30 |
| CP_03 | 578/785; 2 / 3.12 | 706/785; 1 / 0.35 | 547/785; 1 / 1.92 | 604/785; 4 / 2.54 |
| K_BLG_03 | 1546/1632; 8 / 3.21 | 1602/1632; 3 / 3.82 | 1534/1632; 5 / 1.14 | 1597/1632; 4 / 4.99 |
| VKF_01 | 421/516; 1 / 2.70 | 509/516; 0 | 430/516; 0 | 480/516; 0 |

## Beşinci tur — MS_A_01 / MS_B_01 ve önerilen çözüm (28.10.2026)

- MS_B_01 (C30, 1 plak, çok üçgenli): As formülü 205/205 (±0.25). Üçgen komşuluğundaki iç düğümler de yarı yarıya tutmuyor; sorun yalnız sınırda değil.
- MS_A_01: GPR yok, SO1 başlığındaki en büyük As değerleri 0 (STA donatı hesabı yapılmamış, yalnız analiz). Yöntem burada da çalışıyor (X alt 37, X üst 119, Y alt 6, Y üst 281 ilave düğümü, mod 0); doğrulayacak STA çıktısı yok.

**Önerilen yöntem (STA kuralını taklit etmek yerine olası kuralların zarfı):** düğüm tasarım momenti = alt için en büyük, üst için en küçük değer:
1. komşu kümesi N3 (tüm kenarlar), N6 (üçgende I–J, J–K), N7 (üçgende yalnız K–I) × SO1 üçgen sıfırlamalı/ham ortalama,
2. tümü üçgen olan (kolon yelpazesi) düğümde: (kendi + N6 ortalaması)/2,
3. (mod 4) sınırda ve üçgene bağlı düğümde: sınır kenarı komşuları çıkarılmış ortalama.
As = yukarıdaki nihai formül; SO1 başlığındaki yön tavanı (sıfır değilse) üst sınır.

Kaçan ilave düğümü (toplam / yakınında bizim ilave düğümü olmayan, >1.1 m) ve fazla ilave (GPR ilavesinden >1.1 m uzak ve ≥0.5 cm²/m):

| Proje | Mod 0 (tek kural) | Mod 2 (zarf) | Mod 4 (zarf + sınır) |
|---|---|---|---|
| N_ULK_02 | 4 / 1 (n29) | 1 / 1 (n29); fazla 8 | 0 / 0; fazla 17 |
| CP_03 | 8 / 2 | 2 / 1 (n549, 0.48); fazla 0 | 2 / 0; fazla 0 |
| K_BLG_03 | 20 / 8 | 4 / 0; fazla 13 | 4 / 0; fazla 13 |
| VKF_01 | 1 / 0 | 0 / 0; fazla 0 | 0 / 0; fazla 0 |
| MS_B_01 | 15 / 0 | 6 / 0; fazla 2 | 6 / 0; fazla 3 |

Fazla ilavelerin büyük kısmı kenarda kolon bulunan üçgenli sınır düğümleri (N_ULK x = −0.4 ve 21.4 kenarları, 2–3 cm²/m). Çalıştırma: `KMODE=<0|2|4> KACAN=<fck> bin\T.exe <klasör> <proje>`.
