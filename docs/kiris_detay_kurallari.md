# Betonarme Kiriş Donatı Detaylandırma Kuralları (TS 500:2000 + TBDY 2018)

**Otomatik kiriş detayı çizen program için kurallar belgesi.** Boyuna açılımı ve enkesitleri kapsar.

*Hazırlanma tarihi: 26.09.2026. Hazırlayan: araştırma notu (Osman Saygı için). Bu belge mühendislik muhakemesinin yerini tutmaz. Proje müellifi her kuralı kendi projesine göre kontrol etmelidir.*

---

## 0. Belgenin kullanımı

### 0.1 Etiketler

| Etiket | Anlamı |
|---|---|
| **[YÖNETMELİK]** | Kaynak metinden **birebir doğrulandı**: TS 500:2000 veya TBDY 2018 metni ya da şekli okundu. Zorunlu kural. |
| **[YÖNETMELİK-TÜRETİLMİŞ]** | Yönetmelikteki bir kuraldan veya varsayımdan **doğrudan matematiksel/geometrik olarak türetildi**. Formül metinde açıkça yazmaz. |
| **[UYGULAMA]** | Ders notu, yazılım kılavuzu veya yaygın pratikten alındı. **Zorunlu değildir**, programda "varsayılan/ayarlanabilir" parametre olmalıdır. |
| **DOĞRULANAMADI** | Birincil metinde bulunamadı veya metinler çelişiyor. Açıklama ilgili maddede verilmiştir. |

### 0.2 Kullanılan birincil kaynaklar (metinleri indirilip okundu)

| Kısaltma | Belge | Erişilen kopya |
|---|---|---|
| **TS500** | TS 500/Şubat 2000 *Betonarme Yapıların Tasarım ve Yapım Kuralları* (75 sayfa, TSE) | https://web.itu.edu.tr/mdaskiran/wp-content/uploads/2014/09/TS500.pdf (TSE metninin PDF kopyası; resmi TSE satış kopyası değildir) |
| **TS500-T3** | TS 500:2000/T3 Kasım 2014 tadili. **Yalnızca Madde 3.4'ü değiştirir** (beton kalite denetimi → TS 13515 Ek B1). Detaylandırma kurallarını etkilemez. | https://www.resmigazete.gov.tr/eskiler/2015/01/20150118-9.htm |
| **TBDY** | Türkiye Bina Deprem Yönetmeliği 2018 (R.G. 18.03.2018, sayı 30364 mük.), Bölüm 7 | https://webdosya.csb.gov.tr/db/yapiisleri/icerikler/tbdy_2018-20210506174126.pdf (ÇŞB kopyası, 416 s.); R.G. bağlantısı: https://www.resmigazete.gov.tr/eskiler/2018/03/20180318M1-2-1.pdf |

> **Not (TS 500 tadilleri):** "TS 500 2018 tadili" diye bir metin bulunamadı (**DOĞRULANAMADI**). Bulunan tek tadil T3:2014'tür ve yalnızca 3.4'ü değiştirir. T1/T2 tadillerinin içeriği incelenemedi.
>
> **Öncelik kuralı:** TBDY 7.2.2: *"İlgili standartlarda verilen kuralların farklı olduğu özel durumlarda, bu bölümdeki kurallar esas alınacaktır."* **[YÖNETMELİK]** Yani TS 500 ile TBDY çelişirse TBDY geçerlidir.

### 0.3 İkincil kaynaklar (yalnızca [UYGULAMA] ve çapraz kontrol için)

- **TS 708:2016 Çizelge 3** (B420C/B500C mekanik özellikleri). İki ikincil kaynaktan alındı, TS 708'in kendisi indirilmedi: ideCAD yardım sayfası https://help.idecad.com.tr/ideCAD/malzeme-ts-500-3 ve TS 708 PDF kopyası https://insapedia.com/wp-content/uploads/2018/05/TS_708.pdf (arama önizlemesi).
- **A. Topçu**, *Betonarme I*, Eskişehir Osmangazi Üniv., 2019, "Kirişlerde sınır değerler" tablosu (s.154–156): https://atopcu.osmanmuratkaya.com/index_dosyalar/Dersler/Betonarme1/Sunular/Betonarme_1_5.pdf
- **ideCAD** yardım sayfaları: kenetlenme (https://help.idecad.com.tr/ideCAD/donatnn-kenetlenmesi-ts-500), kiriş boyuna donatı (https://help.idecad.com.tr/ideCAD/kirislerin-boyuna-donati-kosullari), enine donatı (https://help.idecad.com.tr/ideCAD/kirislerin-enine-donati-kontrolleri), kiriş betonarme tasarım (https://help.idecad.com.tr/ideCAD/kiris-betonarme-tasarim).
- **ProtaStructure** kiriş tasarım ayarları: https://protasoftware.com/tr/destek/teknik-kilavuzlar/kiris-tasarim-ayarlari-ps2022/

### 0.4 Değişken adları (kodda kullanılacak)

| Değişken | Anlamı | Birim |
|---|---|---|
| `bw`, `h` | kiriş gövde genişliği, toplam yükseklik (TBDY'de `hk`) | mm |
| `d` | faydalı yükseklik = `h - y_s` (`y_s`: çekme donatısı ağırlık merkezinin çekme yüzüne uzaklığı) | mm |
| `t` | döşeme kalınlığı | mm |
| `ln` | net (serbest) açıklık, kolon yüzünden kolon yüzüne | mm |
| `cc` | net beton örtüsü, en dış donatının (etriyenin) dış yüzünden | mm |
| `phi`, `phi_w` | boyuna donatı çapı, etriye çapı | mm |
| `Dmax` | en büyük agrega tane çapı | mm |
| `fck`, `fcd`, `fctk`, `fctd` | beton karakteristik/tasarım basınç ve eksenel çekme dayanımı | MPa |
| `fyk`, `fyd`, `fywd` | boyuna ve enine donatının karakteristik/tasarım akma dayanımı | MPa |
| `lb`, `lbk`, `l0` | düz kenetlenme, kancalı kenetlenme, bindirme boyu | mm |
| `DY` | süneklik düzeyi: `"yuksek"` (TBDY 7.4) veya `"sinirli"` (TBDY 7.8) | – |
| `DTS` | Deprem Tasarım Sınıfı: 1, 1a, 2, 2a, 3, 3a, 4, 4a | – |

---

## 1. Malzeme tasarım değerleri

### R-MAT-01 Malzeme katsayıları [YÖNETMELİK]

- `fcd = fck / gamma_mc`, `fctd = fctk / gamma_mc`, `fyd = fyk / gamma_ms`
- `gamma_mc = 1.5` (yerinde dökme). Öndökümlü betonda 1.4 alınabilir. Nitelik denetimi yeterli yapılamıyorsa tasarımcı kararıyla 1.7.
- `gamma_ms = 1.15` (tüm donatı sınıfları).
- Kaynak: TS500 Madde 6.2.5, Denk. (6.2).
- Not: TBDY 7.2.5.2 tüm binalarda nitelik denetimli, vibratörle yerleştirilmiş beton kullanılmasını şart koşar. Bu yüzden TBDY kapsamındaki binalarda pratikte `gamma_mc = 1.5` alınır.

### R-MAT-02 Beton sınıfları [YÖNETMELİK]

- Değerler TS500 Çizelge 3.2'den (`fck`, `fctk`, `Ec`) ve Çizelge 7.1'den (`k1`) alındı.
- `fctk = 0.35*sqrt(fck)` (TS500 Denk. 3.1). Çizelge 3.2'deki değerler bu formülün yuvarlatılmış halidir.
- **TBDY 7.2.5.1:** C25'ten düşük beton kullanılamaz. 7.2.5.3(a): deprem etkisini karşılayan elemanlarda C25–C80.

| Sınıf | fck | fcd=fck/1.5 | fctk (Ç.3.2) | fctd (Ç.3.2/1.5) | fctd (0.35√fck/1.5) | Ec | k1 |
|---|---|---|---|---|---|---|---|
| C20* | 20 | 13.33 | 1.6 | 1.067 | 1.043 | 28000 | 0.85 |
| C25 | 25 | 16.67 | 1.8 | 1.200 | 1.167 | 30000 | 0.85 |
| C30 | 30 | 20.00 | 1.9 | 1.267 | 1.278 | 32000 | 0.82 |
| C35 | 35 | 23.33 | 2.1 | 1.400 | 1.380 | 33000 | 0.79 |
| C40 | 40 | 26.67 | 2.2 | 1.467 | 1.476 | 34000 | 0.76 |
| C45 | 45 | 30.00 | 2.3 | 1.533 | 1.565 | 36000 | 0.73 |
| C50 | 50 | 33.33 | 2.5 | 1.667 | 1.650 | 37000 | 0.70 |

\*C20: TBDY kapsamındaki binalarda **kullanılamaz** (TBDY 7.2.5.1).

> **Program seçimi:** `fctd` için "tablo" ya da "formül" seçilebilir yapın. Bu belgedeki örneklerde **tablo** değerleri kullanıldı. Aradaki fark en çok %2.3'tür (C20).

### R-MAT-03 Donatı çeliği

- **[YÖNETMELİK]** TBDY 7.2.5.3(b): deprem etkisini karşılayan elemanlarda TS 708'deki **B420C ve B500C** nervürlü çelikler kullanılır. S420 ancak `Rm/Re < 1.35` ve karbon eşdeğeri ≤ %0.55 koşuluyla kullanılabilir.
- **[YÖNETMELİK]** `Es = 2e5 MPa` (TS500 3.2 ve 7.1).
- **[YÖNETMELİK]** `fyd = fyk/1.15`, dolayısıyla B420C için **365.22 MPa**, B500C için **434.78 MPa**.
- TS 708:2016 Çizelge 3 değerleri ikincil kaynaklardan alındı (ideCAD, insapedia TS 708 kopyası). **TS 708 metni doğrudan okunmadı.**

| Sınıf | fyk=Re,min | Rm/Re | Re,act/Re,nom max | A5 min | Agt min | fyd |
|---|---|---|---|---|---|---|
| B420C | 420 | ≥1.15 ve <1.35 | 1.30 | %12 | %7.5 | 365.22 |
| B500C | 500 | ≥1.15 ve <1.35 | 1.30 | %12 | %7.5 | 434.78 |

- Etriye için de aynı sınıflar kullanılır: `fywd = fyk_w/1.15`.
- **[UYGULAMA]** Metraj için birim ağırlık `G = phi^2/162` kg/m (φ mm). 7850 kg/m³ yoğunluktan türetilmiştir.

---

## 2. Paspayı (net beton örtüsü) ve donatı ağırlık merkezleri

### R-CC-01 Net beton örtüsü [YÖNETMELİK]

- TS500 **7.3**: kirişlerde net beton örtüsü **dış elemanlarda ≥ 25 mm, iç elemanlarda ≥ 20 mm**. Elverişsiz çevre koşullarında ve yangın güvenliğinde artırılır.
- TS500 **9.5.1, Çizelge 9.3**. Örtü **en dış donatının dış yüzünden** ölçülür; kirişte en dış donatı etriyedir.

| Durum | `cc_min` |
|---|---|
| Zeminle doğrudan temas eden elemanlar | 50 mm |
| Hava koşullarına açık kolon ve kirişler | 25 mm |
| Yapı içinde, dış etkilere açık olmayan kolon ve kirişler | 20 mm |
| Perde duvar ve döşemeler | 15 mm |

- TBDY paspayı değeri vermez. TBDY 7.13.1.1, TS EN 206 çevresel etki sınıfının **tüm paftalarda** yazılmasını ister **[YÖNETMELİK]**. Çevresel etki sınıfına göre daha büyük örtü gerekebilir. Bu, mühendisin gireceği bir değerdir.
- **[UYGULAMA]** Topçu (2019), iç ve dış tüm kirişlerde `cc ≥ 30 mm` önerir. Yangın dayanımı 2–4 saat istenirse ≥ 40 mm, deniz kıyısında ≥ 50 mm önerilir.
- Tanım farkına dikkat: TS500 0.2.2'de **"beton örtüsü"** boyuna donatı **ağırlık merkezi** ile en dış lif arasındaki uzaklıktır (`d'`). **"Net beton örtüsü"** ise `cc`'dir.

### R-CC-02 Çubuk ağırlık merkezi konumları [YÖNETMELİK-TÜRETİLMİŞ]
```
# 1. sıra (etriyeye yaslanan sıra), alt veya üst yüzden:
y1 = cc + phi_w + phi1/2
# 2. sıra: sıralar arası net açıklık s_v (bkz. R-SP-03)
y2 = cc + phi_w + phi1 + s_v + phi2/2
# Yatayda, köşe çubuğunun ekseni (etriyenin iç yüzüne teğet, yan yüzden):
x_kose = cc + phi_w + phi/2
# Grup ağırlık merkezi ve faydalı yükseklik:
y_s = sum(A_i*y_i)/sum(A_i) ;  d = h - y_s
```
- **[UYGULAMA / geometri, isteğe bağlı düzeltme]** Etriye köşesinde bükme iç çapı `D_b` olduğunda (TBDY 135° kanca için `D_b ≥ 5*phi_w`, TS500 etriye için `≥ 4*phi_w`), köşe çubuğu köşeye tam oturmaz ve kıvrıma yaslanır. `R = D_b/2 > phi/2` ise köşe çubuğunun merkezi, etriye bacaklarının iç yüzlerinden `x = R - (R - phi/2)/sqrt(2)` kadar içeridedir. Örnek: φ10 etriye, D_b=50, φ16 → x = 12.98 mm (düz hesapta 8 mm). Kesit çiziminde gerçekçi görünüm için kullanılabilir.

---

## 3. Donatının kesitte yerleşimi

### R-SP-01 Aynı sıradaki çubuklar arası minimum net aralık [YÖNETMELİK]
İki madde farklı değer verir:

- TS500 **7.3** (kirişler): *"sıra içinde veya sıralar arasında ... net aralık, 20 mm'den ve donatı çapından ve en büyük agrega boyutunun 4/3'ünden az olmamalıdır"*.
- TS500 **9.5.2** (genel): *"Aynı sıradaki donatı çubukları arasındaki net aralık donatı çapından, maksimum agrega çapının 4/3'ünden ve 25 mm'den az olamaz. Bu sınırlar bindirmeli eklerin bulunduğu yerlerde de geçerlidir."*
- **Program kuralı (güvenli taraf):** `s_min = max(phi_max, 25, 4/3*Dmax)` (mm). `phi_max` komşu iki çubuğun büyüğüdür. 20 mm ile 25 mm arasındaki çelişki bölüm 13'te listelenmiştir.
- Demet donatıda `phi` yerine eşdeğer çap `phi_e` kullanılır (TS500 7.3 ve 9.5.3).
- **[UYGULAMA]** Topçu (2019) net aralık için 50 mm önerir (beton yerleşimi ve vibratör girişi için).

### R-SP-02 Bir sıraya sığan en fazla çubuk sayısı [YÖNETMELİK-TÜRETİLMİŞ]
```
b_av  = bw - 2*cc - 2*phi_w            # etriye içi net genişlik
n_max = floor((b_av + s_min) / (phi + s_min))   # tek çaplı sıra
# Karışık çaplar (n_i adet phi_i) için kontrol:
sum(n_i*phi_i) + (N-1)*s_min <= b_av   # N = toplam çubuk
# Gerçek net aralık (düzgün dağıtılmış):
s_net = (b_av - sum(phi_i)) / (N-1)
```
- Bindirmeli ek bulunan kesitlerde ekli çubuk çiftleri de bu aralığa uymalıdır (TS500 9.5.2, son cümle) **[YÖNETMELİK]**.

### R-SP-03 İkinci sıra ve sıralar arası açıklık [YÖNETMELİK]

- TS500 **7.3**: *"Birden fazla sıra oluşturulduğunda, üstüste çubuklar aynı hizaya getirilmelidir."*
- TS500 **9.5.2**: *"üst sıradaki çubuklar alt sıradakilerle aynı düşey eksen üzerinde sıralanmalı ve iki sıra arasındaki net açıklık en az 25 mm veya çap kadar olmalıdır."*
- Program: `s_v = max(25, phi_max, 4/3*Dmax)` (4/3·Dmax terimi TS500 7.3'teki "sıralar arasında" ifadesinden gelir).
- **2. sıradaki çubukların x konumları, 1. sıradaki çubuk x konumlarının bir alt kümesi olmalıdır.**
- TS500 **9.5.4**: *"iki sıra donatı arasına çelik çubuk parçaları konmalıdır"*. Plastik ara elemanlar da kullanılabilir. Detayda "ara çubuk" gösterilebilir.
- **İkinci sıra ne zaman gerekir? [YÖNETMELİK-TÜRETİLMİŞ]** Bir yüzdeki toplam çubuk sayısı `n_max`'ı aşarsa. Birinci sıra doldurulur, kalanlar 2. sıraya geçer. 2. sıra `d`'yi azaltır. Program `d`'yi yeniden hesaplayıp kullanıcıyı uyarmalıdır (donatı hesabı ona ait olduğundan).

### R-SP-04 Köşe çubukları ve sürekli çubuklar [YÖNETMELİK-TÜRETİLMİŞ]

- TBDY **7.2.8.2**: *"Özel deprem etriyeleri boyuna donatıyı dıştan kavrayacak ve kancaları aynı boyuna donatı etrafında kapanacaktır."* Sarılma bölgelerinde bu etriyeler zorunludur (TBDY 7.4.4, 7.8.4).
- TS500 **9.5.4**: asal çekme ve basınç çubukları etriyelerle iyice bağlanmalıdır.
- Sonuç: etriyenin **dört köşesinde kirişin tüm boyunca bir çubuk bulunmalıdır**. İlave çubuklar açıklıkta kesildiği için köşe çubukları **sürekli çubuklar** olmalıdır:
  - **Üst köşeler = montaj donatısı** (üst sürekli / "üst düz" çubuklar).
  - **Alt köşeler = alt düz donatı.**
- TBDY 7.4.2.2 zaten en az 2 üst ve 2 alt sürekli çubuk istediği için bu koşul her zaman sağlanabilir.

### R-SP-05 İlave çubukların düz ve montaj çubuklarına göre yeri [UYGULAMA]
Yönetmelik ilave çubukların kesitteki sırasını belirlemez. Yaygın pratik ve yazılım davranışı (ideCAD, Prota) şöyledir:

1. Üst 1. sıra: iki köşeye montaj. Montaj çubuğu 2'den fazlaysa aralara simetrik dağıtılır.
2. Üst sol/sağ ilave: önce **aynı 1. sırada montajların arasındaki boş yerlere** simetrik yerleşir. Sığmazsa 2. sıraya, 1. sıradaki çubuklarla aynı düşey eksenlere (R-SP-03).
3. Alt 1. sıra: köşelere ve aralara alt düz. Alt sol/sağ ilave önce 1. sıradaki boşluklara, sığmazsa 2. sıraya.
4. Simetri: çift sayılı gruplar eksene göre simetrik olmalı. Tek sayılı ilave grubunda bir çubuk ortaya gelir.
5. Aynı sırada farklı çaplar varsa büyük çaplar köşeye yakın konur. Köşede yine sürekli çubuk bulunur.
6. Sol ve sağ ilave çubuklar farklı enkesitlerde bulunur (sol mesnet kesiti ve sağ mesnet kesiti). Bu yüzden aynı yuvaları kullanabilirler. Kısa kirişte sol ve sağ ilave çakışıyorsa (bkz. R-CUT-05) iki grup aynı kesitte bulunur ve yuvaların toplamı yeterli olmalıdır.
- Program her mesnet kesiti ve açıklık kesiti için yuva sayısını ayrı ayrı kontrol etmelidir.

### R-SP-06 Demet donatı [YÖNETMELİK]

- TS500 **9.5.3**: yalnız nervürlü çubuklarla, **demette en çok 3 çubuk**. Eşdeğer çap `phi_e = 1.2*phi*sqrt(n)` (Denk. 9.3). Kenetlenme, bindirme ve aralık hesaplarında `phi_e` kullanılır.
- TS500 9.2.2: demette tüm çubuklar aynı kesitte eklenmez. Demetteki çubuğun bindirme boyu %20 artırılır.
- **[UYGULAMA]** ideCAD demet donatı uygulamaz. Programda isteğe bağlı bırakılabilir.

---

## 4. Minimum ve maksimum donatı, sürekli donatılar

### R-RHO-01 Minimum çekme donatısı [YÖNETMELİK]

- `rho = As/(bw*d) >= rho_min = 0.8*fctd/fyd`. Kaynak: TS500 7.3 Denk. (7.3). TBDY 7.4.2.1 Denk. (7.8) mesnetler için aynı koşulu verir. TBDY 7.8.2 bunu sınırlı süneklik için de geçerli kılar.

| rho_min | C25 | C30 | C35 | C40 | C45 | C50 |
|---|---|---|---|---|---|---|
| B420C | 0.00263 | 0.00277 | 0.00307 | 0.00321 | 0.00336 | 0.00365 |
| B500C | 0.00221 | 0.00233 | 0.00258 | 0.00270 | 0.00282 | 0.00307 |

(Tablo `fctd` değerleriyle hesaplandı.)

### R-RHO-02 Maksimum donatı [YÖNETMELİK]

- TS500 7.3: `rho - rho' <= 0.85*rho_b` (Denk. 7.4) **ve** `rho <= 0.02` (Denk. 7.5).
- TBDY 7.4.2.4: açıklık ve mesnetlerde çekme donatısı oranı TS 500'deki maksimumdan ve **%2**'den fazla olamaz.
- `rho_b` formülü TS 500'de açıkça **yazılmaz**. TS500 7.1 varsayımlarından (εcu=0.003, Es=2e5, 0.85fcd blok, k1) türetilir **[YÖNETMELİK-TÜRETİLMİŞ]**:
  `rho_b = 0.85*k1*(fcd/fyd)*600/(600+fyd)`
  (Çapraz kontrol: Topçu 2019'daki C25/B420 örneğinde ρb=0.0205 bulunur, bu formül de aynı sonucu verir.)

| rho_b | C25 | C30 | C35 | C40 | C45 | C50 |
|---|---|---|---|---|---|---|
| B420C | 0.0205 | 0.0237 | 0.0267 | 0.0293 | 0.0317 | 0.0338 |
| B500C | 0.0161 | 0.0186 | 0.0209 | 0.0230 | 0.0248 | 0.0264 |

### R-RHO-03 Çubuk çapı ve sürekli çubuklar [YÖNETMELİK]

- Boyuna çubuk çapı **≥ 12 mm**: TS500 7.3 ve TBDY 7.4.2.2. Montaj ve gövde donatısı dahil tüm boyuna çubuklar için geçerlidir. Gövde için bkz. R-WEB.
- TBDY 7.4.2.2: *"Kirişin alt ve üstünde en az iki donatı çubuğu, kiriş açıklığı boyunca sürekli olarak bulunacaktır."* Yani **montaj ≥ 2 adet** ve **alt düz ≥ 2 adet**.
- TBDY **7.4.3.1(a)**: *"Kirişin iki ucundaki mesnet üst donatılarının büyük olanının en az 1/4'ü tüm kiriş boyunca sürekli olarak devam ettirilecektir."* Program koşulu:
  `As_montaj >= 0.25 * max(As_ust_sol_toplam, As_ust_sag_toplam)`
  Burada `As_ust_*_toplam = As_montaj + As_ust_ilave_*` alınır (montaj mesnette kenetlendiği için mesnet donatısına katılır).
- TS500 7.3: *"Açıklıktaki çekme donatısının, en az üçte birinin mesnete kadar uzatılıp kenetlenmesi gereklidir."* Program koşulu: `As_alt_duz >= As_alt_aciklik_toplam/3`. Kullanıcı açıklıkta ilave alt çubuk verirse bu kontrol edilir.
- **"Üst düz" hakkında:** TBDY'nin istediği üst sürekli donatıyı pratikte montaj donatısı sağlar. Montaj yukarıdaki 1/4 koşulunu sağlamazsa çapı veya adedi artırılır, ya da ayrı "üst düz" sürekli çubuk eklenir **[UYGULAMA]**.
- **[UYGULAMA]** Topçu 2019: en az 2 montaj çubuğu olmalı. Çekme donatısı için en az 3 çubuk önerilir, TBDY en az 2 ister.

### R-RHO-04 Mesnetteki alt donatının üst donatıya oranı [YÖNETMELİK]

- TBDY **7.4.2.3**: *"DTS = 1, 1a ve DTS = 2, 2a olan taşıyıcı sistemlerde, kiriş mesnedindeki alt donatı, aynı mesnetteki üst donatının %50'sinden daha az olamaz. Ancak, diğer durumlarda bu oran %30'a indirilebilir."* 7.8.2 bu koşulu sınırlı süneklik için de geçerli kılar.
```
oran = 0.50 if DTS in {"1","1a","2","2a"} else 0.30
As_alt_mesnet(i) = As_alt_duz(mesnette kenetli) + As_alt_ilave(i)
kontrol: As_alt_mesnet(i) >= oran * As_ust_mesnet(i)   # i = sol, sag
```

---

## 5. Kenetlenme boyu

### R-LB-01 Temel düz kenetlenme boyu (nervürlü) [YÖNETMELİK]
TS500 **9.1.2.1, Denk. (9.1)**:
```
lb0 = max(0.12 * (fyd/fctd) * phi , 20*phi)          # mm
```
- Düz yüzeyli çubukta 2 katı alınır. Düz kenetlenme yalnız nervürlü çubukta kullanılabilir.
- TBDY 7.2.6: aksi belirtilmedikçe kenetlenme boyları TS 500'e göre hesaplanır.

### R-LB-02 Düzeltme katsayıları [YÖNETMELİK]
| Katsayı | Koşul | Kaynak |
|---|---|---|
| `k_konum = 1.0` | **Konum II**: betonlamada 45°–90° eğimli çubuklar; veya yatay olup kesitin **alt yarısında** ya da serbest üst yüzden **300 mm'den daha uzakta** olan çubuklar | TS500 9.1.1, 9.1.2.1 |
| `k_konum = 1.4` | **Konum I**: diğer tüm çubuklar. Kirişte **üst çubukların** çoğu buraya girer (üst yarıda ve üst yüzden ≤ 300 mm) | TS500 9.1.1, 9.1.2.1 |
| `k_32_40 = 100/(132-phi)` | 32 < φ ≤ 40 mm | TS500 9.1.2.1 |
| `k_ortu = 1.2` | `cc < phi` **veya** aynı sırada net aralık `< 1.5*phi` | TS500 9.1.2.1 |
| `k_As = As_gerekli/As_mevcut` | Azaltma. Sonuç ≥ `0.5*lb0` ve ≥ `20*phi` olmalı. TS500'e göre **süneklik düzeyi yüksek çerçeve elemanlarında azaltma yapılamaz** | TS500 9.1.2.1 |
| `k_kanca = 0.75` | TS500 Şekil 9.1'deki standart kanca varsa `lbk = 0.75*lb` | TS500 9.1.2.2 |
| `k_basinc = 0.75` | Çubuk tüm yüklemelerde basınçta ise. Basınç donatısına kanca yapılamaz | TS500 9.1.3 |

```
lb = lb0 * k_konum * k_32_40 * k_ortu        # (k_As yalnız DY="sinirli" ve kullanıcı isterse)
```
- Konum sınıflaması: kesitin serbest üst yüzünden ölçülür. `y_ust_yuzden > 300` **veya** çubuk alt yarıda ise Konum II, değilse Konum I **[YÖNETMELİK]**. ideCAD bu seçimi kullanıcıya bırakır **[UYGULAMA]**. Programda varsayılan: üst çubuklar Konum I (×1.4), alt çubuklar Konum II (×1.0).

**lb/φ tablosu** (Konum II / Konum I, `k_ortu=1`, tablo `fctd` ile):

| Çelik | C25 | C30 | C35 | C40 | C45 | C50 |
|---|---|---|---|---|---|---|
| B420C | 36.5 / 51.1 | 34.6 / 48.4 | 31.3 / 43.8 | 29.9 / 41.8 | 28.6 / 40.0 | 26.3 / 36.8 |
| B500C | 43.5 / 60.9 | 41.2 / 57.7 | 37.3 / 52.2 | 35.6 / 49.8 | 34.0 / 47.6 | 31.3 / 43.8 |

### R-LB-03 Standart kancalar (boyuna donatı) [YÖNETMELİK]
TS500 **9.3.1** ve Şekil 9.1, 9.4:

- **90° kanca:** serbest uçta düz kısım `≥ 12*phi`, iç çap `dm ≥ 6*phi`.
- **180° kanca:** metne göre serbest uçta düz kısım `≥ 4*phi` ve `≥ 60 mm`, `dm ≥ 6*phi`. **Çelişki:** Şekil 9.1(a)'da "≥6φ, ≥60 mm" yazılıdır. Güvenli tarafta kalmak için `max(6*phi, 60)` kullanılabilir. Bölüm 13'e bakınız.
- **Fiyong:** `dm ≥ 12*phi`.
- TS500 9.4: boyuna donatı en az `6*phi` çaplı merdane etrafında, ısıtılmadan bükülür.

### R-LB-04 Kenar mesnet (kiriş kolonun öbür yüzünde devam etmiyor) [YÖNETMELİK]
TBDY **7.4.3.1(b)** ve **Şekil 7.7** (7.8.3 ile sınırlı süneklik için de geçerli):

- Alt ve üst donatı, kolonun **etriyelerle sarılmış çekirdeğinin karşı yüzüne kadar** uzatılır ve **etriyelerin iç tarafından 90° bükülür**.
- Kontrol: `a + b >= lb`, `a >= 0.4*lb`, `b >= 12*phi`
  - `a`: kolon içindeki yatay kısım
  - `b`: 90° kıvrılan düşey kısım. Üst çubuk aşağı, alt çubuk yukarı kıvrılır (Şekil 7.7).
- **Perdelerde** ve `a` ölçüsü `lb`'den **ve** `50*phi`'den büyük olan kolonlarda kancasız düz kenetlenme yapılabilir.
- Kullanılabilir yatay uzunluk **[UYGULAMA / geometri]**: `a_mevcut = hc - cc_kolon - phi_w_kolon - phi/2`
  - `hc`: kolonun kiriş doğrultusundaki boyutu
  - Kolon boyuna çubuklarının iç tarafından geçiş için ayrıca `phi_kolon` düşülebilir.
  - `a_mevcut < 0.4*lb` ise **kesit/kolon uygun değildir**. Kullanıcı uyarılmalıdır.
  - Düşey kısım: `b = max(12*phi, lb - a)`.
- `lb` burada TS500'deki düz kenetlenme boyudur. Konum katsayısı dahildir (bkz. bölüm 13).

### R-LB-05 Ara mesnet (her iki taraftan kiriş bağlanıyor) [YÖNETMELİK]

- TBDY **7.4.3.1(c)**: *"kiriş alt donatıları, açıklığa komşu olan kolon yüzünden itibaren, 50φ'den az olmamak üzere, en az TS 500'de verilen kenetlenme boyu ℓb kadar uzatılacaktır."* Şekil 7.7'de "komşu açıklık alt donatısı" kolonu geçer ve bu açıklığa **≥ ℓb, ≥ 50φ** uzanır.
  - `L_alt_uzatma (kolon yüzünden, komşu açıklığa) = max(lb_konumII, 50*phi)`
- Kirişlerin yükseklik farkı gibi nedenlerle bu yapılamıyorsa kenetlenme R-LB-04'teki gibi yapılır (90° kıvrım).
- Üst sürekli çubuklar (montaj) ve üst ilaveler ara mesnetten geçerek devam eder. Üst ilaveler her iki açıklığa R-CUT'a göre uzanır.
- Kolon kesiti değişiyorsa ya da kiriş eksenleri kaçıksa kenetlenme ayrıca kontrol edilir **[UYGULAMA]**.

---

## 6. Bindirmeli ekler

### R-LAP-01 Bindirme boyu (çekme) [YÖNETMELİK]
TS500 **9.2.5.1, Denk. (9.2)**:
```
alpha1 = 1 + 0.5*r        # r: aynı kesitte eklenen donatı / toplam donatı (0..1)
alpha1 = 1.8              # bütün kesiti çekme taşıyan elemanlarda
l0 = alpha1 * lb          # Konum I çubuklarda l0 ayrıca 1.4 ile artırılır (bkz. bölüm 13)
l0_kanca = 0.75 * l0      # eklenen çubuk uçları kancalı ise
```
- **Şaşırtma:** iki ekin merkezleri arası `≥ 1.5*l0` ise ekler şaşırtılmış sayılır. Birden fazla çubuk ekleniyorsa ekler şaşırtılmalıdır.
- Ekli çubuklar bitişik olmalıdır. Aralık gerekiyorsa `≤ l0/6` ve `≤ 100 mm` olmalıdır (TS500 9.2.2).
- **Sargı donatısı** (TS500 9.2.5.1): bindirme boyunca en az **6 adet** sargı. Çapı `≥ phi/3` ve `≥ φ8`. Aralığı `≤ h/4` ve `≤ 200 mm`.

### R-LAP-02 Basınç bindirmesi [YÖNETMELİK]

- TS500 **9.2.6.1**: `l0 ≥ lb` ve `≥ 300 mm`. Kanca yapılmaz. Sargı aralığı `< d/4`.
- *"Çapı 30 mm'den büyük olan donatı çubuklarına bindirmeli ek yapılamaz"*: bu cümle basınç eki maddesinde geçer. Genel uygulanıp uygulanmadığı için bölüm 13'e bakınız.

### R-LAP-03 Ek yeri kısıtları (deprem) [YÖNETMELİK]
TBDY **7.4.3.2** (7.8.3 ile sınırlı süneklik için de geçerli):

- (a) **Bindirmeli ek yapılamayan bölgeler:** kiriş **sarılma bölgeleri** (kolon yüzünden 2h), **kolon-kiriş birleşim bölgeleri** ve **açıklık ortasında alt donatı bölgeleri**. Bunlar donatının akma olasılığı bulunan kritik bölgelerdir.
- Bu bölgeler dışındaki eklerde ek boyunca **özel deprem etriyesi** kullanılır. Aralık `≤ h/4` ve `≤ 100 mm`.
- *"Üst montaj donatısının açıklıkta sarılma bölgelerinin dışında kalan eklerinde özel deprem etriyeleri kullanılmasına gerek yoktur."*
- (b) Manşonlu ve bindirmeli kaynaklı ekler bir kesitte ancak **birer donatı atlayarak** yapılır. Komşu iki ekin merkezleri arası `≥ 600 mm`.
- TBDY 7.2.7.3: enine donatı boyuna donatıya kaynakla bağlanamaz.
- **"Açıklık ortasında alt donatı bölgesi"nin uzunluğu TBDY'de sayısal olarak tanımlanmamıştır (DOĞRULANAMADI).** [UYGULAMA] önerisi: alt donatı ekleri ya mesnetten geçirilip komşu açıklığa uzatılarak (R-LB-05) çözülür, ya da ek bölgesi 2h ile orta ln/3 bölgesi arasında seçilir. Mühendis parametresi olmalıdır.
- **[UYGULAMA]** Montaj ekleri genellikle açıklık ortasında yapılır (sarılma bölgesi dışında). Prota'daki "üst donatıları açıklık ortasında bindir" seçeneği buna örnektir.

### R-LAP-04 Çubuk boyu sınırı [UYGULAMA]

- Piyasada nervürlü çubuk **12 m** boyda satılır. TS 708 boyu siparişe bırakır (ikincil kaynak).
- Program: `L_cubuk > L_stok` (varsayılan 12000 mm, ayarlanabilir) ise R-LAP-03'e uygun bir bölgede ek yapılır. Ek boyu R-LAP-01'e göre hesaplanır.

---

## 7. İlave (ek) donatıların kesim boyları

### R-CUT-01 Kesim noktası ilkesi (moment diyagramı varsa) [YÖNETMELİK]
TS500 **7.3**:

- *"Gerekli olmayan çubukların kesilme noktaları ile kuramsal kesim noktası arasındaki uzaklık ise faydalı yükseklikten ve nervürlü çubuklarda donatı çapının 20 katından, düz yüzeyli çubuklarda ise donatı çapının 40 katından az olmamalıdır."*
  - `L_asim >= max(d, 20*phi)` (nervürlü), `max(d, 40*phi)` (düz)
- Pilye büküm noktası: kuramsal kesim noktasından `≥ d/3` ve `≥ 8*phi` ileride olmalıdır. Pilye TBDY'de kesmeye katkı sağlamaz (TBDY 7.4.5.3).
- TS500 9.1.2.1: *"Kenetlenme, donatının gereksinme duyulmayan noktadan düz olarak ℓb kadar uzatılması ile sağlanabilir."* Bu cümlenin 7.3 ile birlikte nasıl okunacağı bölüm 13'te tartışılmıştır.
- TBDY **7.4.3.1(a)**: *"Mesnet üst donatısının geri kalan kısmı, kiriş boyunca karşılanmamış moment bırakılmamak üzere TS 500'e göre düzenlenecektir."*

### R-CUT-02 Üst sol/sağ ilave, moment diyagramı yoksa [UYGULAMA]
Kullanıcı yalnız çubuk sayı ve çapını veriyorsa, programın yönetmelik dışı bir varsayılana ihtiyacı vardır:
```
L_ust_ilave_aciklik (kolon yüzünden) = max( k_ln * ln_ref , lb_konumI , L_min_kullanici )
k_ln = 0.25   (varsayılan; Topçu 2019 "ek öneri: c ≥ Ln/4")
ln_ref = ara mesnette komşu iki açıklığın büyüğü (güvenli taraf; Prota "simetrik uzat" seçeneği),
         kenar mesnette ilgili açıklığın ln'i
```
- İkinci sıradaki üst ilaveler için bazı uluslararası pratikler 1. sıra ln/3, 2. sıra ln/4 kullanır. Bu **Türk yönetmeliğinde yoktur**. Seçenek olarak verilebilir.
- Mühendis moment diyagramını girerse R-CUT-01 kullanılmalıdır: kuramsal kesim noktası + `max(d, 20*phi)`, ayrıca `≥ lb`.
- Mesnet tarafında kenar mesnetteki kenetlenme R-LB-04'e göre yapılır. Ara mesnette çubuk kolonu geçerek komşu açıklığa uzanır (sürekli tek çubuk).
- **[UYGULAMA] Uzunluk yuvarlama:** Kesim boyları genellikle 50 mm veya 10 mm katlarına yukarı yuvarlanır. Ayar parametresi olmalıdır.

### R-CUT-03 Alt sol/sağ ilave (mesnet alt ek donatısı) [UYGULAMA + YÖNETMELİK sınırları]

- Amaç R-RHO-04'ü (mesnette alt ≥ %50/%30 üst) ve mesnette pozitif moment ihtiyacını karşılamaktır **[YÖNETMELİK]**.
- Kolon içindeki kenetlenme R-LB-04 (kenar) veya R-LB-05 (ara: kolonu geçip komşu açıklığa `max(lb, 50φ)`) ile yapılır **[YÖNETMELİK]**.
- Açıklık içine uzanma boyu için yönetmelikte sayısal bir kural bulunamadı (**DOĞRULANAMADI**). Önerilen varsayılan:
  `L_alt_ilave_aciklik (kolon yüzünden) = max(lb_konumII, 50*phi)` (7.4.3.1(c) ile tutarlı), moment diyagramı varsa R-CUT-01.
- Kesim noktası sarılma bölgesi içinde kalabilir. Sarılma bölgesinde yasak olan **ek (bindirme)** yapmaktır, çubuğu kesmek değildir (TBDY 7.4.3.2(a)).

### R-CUT-04 Düz (alt sürekli) ve montaj çubukları [YÖNETMELİK]

- **Alt düz:** mesnetten mesnede sürekli. Kenar mesnette R-LB-04, ara mesnette R-LB-05.
- **Montaj:** kiriş boyunca sürekli. Uçlarda mesnet üst donatısının parçası olarak R-LB-04 ile kenetlenir. 12 m'yi aşarsa açıklık ortasında (sarılma bölgesi dışında) eklenir.

### R-CUT-05 Kısa açıklıklar [UYGULAMA]

- `L_sol + L_sag >= ln` ise (sol ve sağ ilaveler çakışıyor veya aradaki boşluk çok kısa) iki ilave kiriş boyunca sürekli tek çubuğa dönüştürülür. Prota'daki "kısa açıklıkta yalnız mesnet donatısı kullan" seçeneği buna örnektir.
- Eşik: aradaki boşluk `< 2*l0` veya `< 500 mm` olabilir. Değer ayarlanabilir olmalıdır.

---

## 8. Gövde donatısı

| Kural | TS 500 7.3 [YÖNETMELİK] | TBDY 7.4.1.1(c) [YÖNETMELİK] |
|---|---|---|
| Ne zaman? | **Gövde yüksekliği > 600 mm** | **h > ln/4** (kiriş yüksekliği serbest açıklığın 1/4'ünden büyük) |
| Toplam alan | `As_govde >= 0.001*bw*d` (Denk. 7.6), iki yüze eşit | `As_govde_toplam >= 0.30 * max_i(As_ust_i + As_alt_i)` (i = sol/sağ mesnet kesiti) |
| Çap | ≥ 10 mm | ≥ 12 mm |
| Aralık | ≤ 300 mm | ≤ 300 mm |
| Diğer | – | Yatay **gövde çirozları**: kiriş yüksekliği boyunca ≤ 600 mm, kiriş ekseni boyunca ≤ 400 mm aralıkla |
| Kenetlenme | – | 7.4.3.1(b) ve (c) boyuna donatı gibi (kenar mesnette 90° kıvrım, ara mesnette `max(lb, 50φ)`) |

- TBDY koşulu (d) maddesine göre kolonlara mafsallı bağlanan kirişlerde, bağ kirişlerinde ve kolon-kiriş düğümü dışında saplanan ikincil kirişlerde zorunlu değildir.
- Program: **iki koşulun ikisi de ayrı ayrı kontrol edilir.** Tetiklenen koşullar için en elverişsiz alan, çap (DY'den bağımsız TBDY ≥12) ve aralık alınır.
- Yüz başına çubuk sayısı **[YÖNETMELİK-TÜRETİLMİŞ]**:
  `n_yuz = max(ceil(As_gerekli/2 / A_phi), ceil(h_serbest/300) - 1)`
  `h_serbest`: üst ve alt boyuna çubuk sıraları arasındaki net düşey mesafe. Çubuklar bu mesafeye eşit aralıkla yerleştirilir.
- "Gövde yüksekliği" tablalı kirişte `h - t` olarak yorumlanabilir. Metin tanımlamaz (**DOĞRULANAMADI**). Güvenli taraf `h` almaktır.
- **[UYGULAMA]** Prota'da yan donatı "h boyunca" veya "alt 2/3h boyunca" seçilebilir. Topçu, h>600 kirişlerde büzülme çatlağı için bu donatıyı gösterir.

---

## 9. Etriye ve çiroz

### R-ST-01 Sarılma bölgesi [YÖNETMELİK]
TBDY **7.4.4** (DY yüksek) ve **7.8.4** (DY sınırlı), Şekil 7.8:

- Sarılma bölgesi uzunluğu: **kolon yüzünden itibaren `L_sar = 2*h`** (her iki uçta). Bölge boyunca **özel deprem etriyesi** kullanılır.
- Etriye çapı **≥ φ8**. **İlk etriye kolon yüzünden ≤ 50 mm.**
- Aralık üst sınırı (kesme hesabı daha küçük bir değer vermiyorsa):

| DY | `s_sar_max` | Kaynak |
|---|---|---|
| **Yüksek** | `min(d/4, 8*phi_boyuna_min, 150)` (metin: "etkili yüksekliğin 1/4'ü") | TBDY 7.4.4 |
| **Sınırlı** | `min(h/4, 8*phi_boyuna_min, 200)` (metin: "kiriş yüksekliğinin 1/4'ü") | TBDY 7.8.4 |

- `phi_boyuna_min`: kirişteki **en küçük boyuna donatı çapı**. Programda gövde donatısının bu minimuma dahil edilip edilmeyeceği bölüm 13'te tartışılmıştır.
- TS500 **8.1.6** de çerçeve kirişi uçlarında, kiriş derinliğinin iki katı bölgede `s ≤ d/4`, `s ≤ 8φ`, `s ≤ 150 mm` ister **[YÖNETMELİK]**.
- **Çelişki (DY yüksek):** TBDY 7.4.4 metninde `d/4` yazar, Şekil 7.8'de `sk ≤ hk/4`. `d/4` daha küçük olduğu için program `d/4` kullanmalıdır. TS500 8.1.6 da `d/4` der.

### R-ST-02 Orta bölge (sarılma dışı) [YÖNETMELİK]

- TBDY: *"Sarılma bölgesi dışında, TS 500'de verilen enine donatı koşullarına uyulacaktır."*
- TS500 **8.1.6**: `s ≤ d/2`. `Vd > 3*Vcr` ise `s ≤ d/4`. Burada `Vcr = 0.65*fctd*bw*d*(1+gamma*Nd/Ac)` (TS500 Denk. 8.1).
- TS500 **8.1.5.1** minimum etriye (açıklık boyunca etriye zorunlu): `Asw/s >= 0.3*(fctd/fywd)*bw` (Denk. 8.6)
- Burulma etkisi ihmal edilemiyorsa (TS500 8.2.6): 90° kanca ve bindirmeli etriye yasaktır, 135° kapalı etriye kullanılır. `s ≤ d/2`, `s ≤ ue/8`, `s ≤ 300 mm`. Köşelerde `≥ φ12` çubuk, boyuna çubuklar arası `≤ 300 mm`.
- Orta bölgede etriye çapı alt sınırı: TS500 kirişler için açık bir değer **vermez (DOĞRULANAMADI)**. **[UYGULAMA]** Pratikte orta bölgede de φ8 kullanılır (Topçu 2019 tablosu da "min φw 8 mm" verir).
- **[UYGULAMA]** Topçu: orta bölgede `s ≤ 200 mm`, sarılma bölgesinde `s ≥ 50 mm` ve orta bölgede `≥ 100 mm` önerir (beton yerleşimi için).

### R-ST-03 Etriye kolu aralığı (enine doğrultuda) [YÖNETMELİK]

- TBDY 7.4.4 ve 7.8.4: *"Kiriş eksenine dik doğrultuda etriye kolları aralığı 350 mm'yi aşmayacaktır."*
```
e = bw - 2*cc - phi_w                  # dış etriyenin iki düşey kolu arasındaki eksen mesafesi
n_kol = max(2, ceil(e/350) + 1)        # gerekli düşey kol sayısı
# n_kol > 2 ise: iç etriye(ler) veya düşey çiroz ile kol eklenir; kollar boyuna çubuklara oturmalı
```
- İç kollar bir boyuna çubuk yuvasına oturmalıdır. Program kol konumunu mevcut çubuk yuvalarından seçmelidir **[UYGULAMA]**.

### R-ST-04 Kanca detayları [YÖNETMELİK]
| Konu | Değer | Kaynak |
|---|---|---|
| Özel deprem etriyesi kancası | **her iki uçta 135°** | TBDY 7.2.8.1 |
| 135° kanca iç büküm çapı | `≥ 5*phi_w` | TBDY 7.2.8.1 |
| 135° kanca uç düz boyu (nervürlü) | **`≥ max(6*phi_w, 80 mm)`**, kıvrımdaki son teğet noktasından | TBDY 7.2.8.1, Şekil 7.1 |
| Kancaların kapanışı | boyuna donatıyı dıştan kavrar, kancalar **aynı boyuna çubuk etrafında** kapanır | TBDY 7.2.8.2 |
| TS500 135° etriye kancası (deprem dışı) | `≥ 6*phi_w`, `≥ 50 mm` | TS500 Şekil 9.2a |
| TS500 90° etriye kancası | `≥ 10*phi_w`, `≥ 70 mm`, **yalnız dişli döşeme kirişlerinde**, kanca tabla içinde kalmak koşuluyla | TS500 9.1.4.1, Şekil 9.2b |
| Bindirmeli (90°) etriye | deprem veya burulma etkisindeki elemanlarda **kullanılamaz** | TS500 9.1.4.2, 9.3.2(c) |
| Etriye kancası iç çapı (TS500) | metin `≥ 4*phi_w` (9.3.2), Şekil 9.1 notu "etriye çiroz ve kancalarında dm ≥ 6φ" | TS500 9.3.2, Şekil 9.1 |

- TBDY 7.13.1.3: özel deprem etriyesi ve çirozlarının kanca kıvrım detayları **her kiriş detay paftasında gösterilir** **[YÖNETMELİK]**. Programın paftaya kanca detayı eklemesi gerekir.
- **Program uygulaması:** Tüm kiriş etriyeleri 135° kancalı özel deprem etriyesi olarak çizilir. Orta bölgede de aynı etriye kullanılır; bu yaygın pratiktir ve güvenli taraftadır **[UYGULAMA]**. Kanca uç boyu `max(6*phi_w, 80)`, büküm iç çapı `5*phi_w`.
- **[UYGULAMA]** Topçu 135° kanca için `10*phi_w` ve `100 mm` önerir.

### R-ST-05 Çiroz [YÖNETMELİK]

- TBDY **7.2.8.1**: özel deprem çirozlarının bir ucunda 90° kanca yapılabilir. Bu durumda 90° ve 135° uçlar şaşırtmalı düzenlenir. Metin bunu kolon ve perde yüzü için yazar.
- TBDY **7.2.8.2**: çirozun **çap ve aralığı etriye ile aynıdır**. Çiroz her iki ucunda **boyuna donatıyı ve dış etriyeyi sarar**.
- Kirişte çiroz kullanılan yerler: (i) R-ST-03'e göre ek düşey kol gerektiğinde, (ii) TBDY 7.4.1.1(c) gövde donatısı varsa yatay gövde çirozu (düşeyde ≤ 600 mm, eksen boyunca ≤ 400 mm).

### R-ST-06 Kesme hesabı ile ilişki [YÖNETMELİK, bilgi]

- DY yüksek: `Ve = Vdy ± (Mpi + Mpj)/ln`, `Mp ≈ 1.4*Mr` (TBDY 7.4.5.1). `Ve ≤ 0.85*bw*d*sqrt(fck)` (Denk. 7.10). Deprem kesmesi toplamın yarısından büyükse sarılma bölgesinde `Vc = 0` alınır (7.4.5.3).
- DY sınırlı: D ile büyütülmüş deprem etkisiyle `Vd` kullanılır (7.8.5).
- Program donatı hesabı yapmıyorsa kesmeden gelen aralığı **kullanıcı girdisi** olarak almalı ve konstrüktif sınırla karşılaştırmalıdır: `s = min(s_kullanici, s_max_konstruktif)`.

### R-ST-07 Etriye sayısı ve yerleşimi [YÖNETMELİK-TÜRETİLMİŞ + UYGULAMA]
```
# her uç için:
x0 = 50                                      # ilk etriye kolon yüzünden (≤50, TBDY)
n_sar = floor((L_sar - x0)/s_sar) + 1        # sarılma bölgesi etriye sayısı
# orta bölge: kalan uzunluk L_orta = ln - 2*L_sar, aralık s_orta; uçtaki son sarılma etriyesinden devam
# [UYGULAMA] aralıklar 10 veya 25 mm katlarına aşağı yuvarlanır; L_orta < s_orta ise tüm kiriş sıklaştırılır
```
- **Bindirme bölgesi**: sarılma dışındaki eklerde ek boyunca özel deprem etriyesi, `s ≤ min(h/4, 100)` (TBDY 7.4.3.2(a)). Montaj eklerinde bu gerekmez. TS500 9.2.5.1'e göre ek boyunca en az 6 sargı, `s ≤ min(h/4, 200)`.
- Kolon-kiriş birleşim bölgesindeki (kolon içi) etriyeler kolon detayına aittir (TBDY 7.5.2.3). Kiriş detayında gösterilmez **[UYGULAMA]**.
- TBDY 7.13.3: kiriş detaylarında her kiriş için sarılma bölgesi uzunlukları ile bu bölgelerdeki ve orta bölgedeki etriyelerin çap, sayı, aralık ve açılımları **gösterilecektir** **[YÖNETMELİK]**.

---

## 10. Geometri ve diğer kontroller

| Kod | Kural | Formül | Etiket / Kaynak |
|---|---|---|---|
| R-G-01 | Gövde genişliği | TBDY: `bw ≥ 250`; TS500: `bw ≥ 200` | [YÖNETMELİK] TBDY 7.4.1.1(a) (7.8.1 ile sınırlı için de); TS500 7.3 |
| R-G-02 | Gövde genişliği üst sınırı | `bw ≤ h + b_kolon_dik` (kolon/perdenin kirişe dik genişliği) | [YÖNETMELİK] TBDY 7.4.1.1(a); TS500 7.3 (`bw ≤ a + h`) |
| R-G-03 | Yükseklik alt sınırı | `h ≥ max(300, 3*t)`. Sağlanmazsa çerçeve kirişi sayılmaz | [YÖNETMELİK] TBDY 7.4.1.1(b); TS500 7.3 |
| R-G-04 | Yükseklik üst sınırı | `h ≤ 3.5*bw` | [YÖNETMELİK] TBDY 7.4.1.1(b) |
| R-G-05 | Yüksek kiriş | sürekli: `ln < 2.5*h`, basit: `ln < 1.5*h` ise yüksek kiriş olarak tasarlanır (TS500 8.5); bu programın kapsamı dışında bırakılabilir | [YÖNETMELİK] TS500 7.3 |
| R-G-06 | Eksenel kuvvet | `Nd ≤ 0.10*Ac*fck`. Aksi halde kolon olarak boyutlandırılır | [YÖNETMELİK] TBDY 7.4.1.2; TS500 Denk. 7.2 |
| R-G-07 | İstisnalar | (a)–(c) sınırları mafsallı kirişlerde, bağ kirişlerinde ve düğüm dışında saplanan ikincil kirişlerde zorunlu değildir | [YÖNETMELİK] TBDY 7.4.1.1(d) |
| R-G-08 | Askı donatısı | Dolaylı mesnette (kirişe oturan kiriş) askı donatısı düzenlenir. TS500 miktar formülü vermez (**DOĞRULANAMADI**) | [YÖNETMELİK] TS500 8.1.6 |
| R-G-09 | Bükme | Boyuna donatı merdane çapı `≥ 6*phi`, ısıtılmadan. Beton döküldükten sonra açıp doğrultmak sakıncalıdır | [YÖNETMELİK] TS500 9.4 |
| R-G-10 | Kaynak | Enine donatı boyuna donatıya kaynaklanmaz. Tesisat vb. donatıya kaynaklanmaz | [YÖNETMELİK] TBDY 7.2.7.3–7.2.7.4 |
| R-G-11 | Pafta bilgileri | Beton ve donatı sınıfı ile TS EN 206 çevresel etki sınıfı tüm paftalarda yazılır | [YÖNETMELİK] TBDY 7.13.1.1 |
| R-G-12 | Süneklik düzeyi seçimi | DY sınırlı çerçeve sistemleri (A31/B31/C31) yalnız DTS=3 ve 4'te kullanılabilir. DTS=1a, 2a, 3a, 4a'da DY sınırlı sistem kullanılamaz | [YÖNETMELİK] TBDY 4.3.4.1, 4.3.4.3 |
| R-G-13 | Stok boyu | `L_stok = 12000 mm` (ayarlanabilir) | [UYGULAMA] |

---

## 11. Sayısal örnekler

**Veri:** C30, B420C, φ16 boyuna, φ10 etriye, `bw = 300`, `h = 500`, `cc = 25` mm, `Dmax = 16` mm (ikinci varyant `Dmax = 22`), DY yüksek.

### 11.1 Malzeme

- `fcd = 30/1.5 = 20.00 MPa`
- `fctd = 1.9/1.5 = 1.267 MPa` (tablo); formülle 1.278
- `fyd = 420/1.15 = 365.22 MPa`

### 11.2 Kenetlenme ve bindirme (φ16)
| Büyüklük | Hesap | Sonuç |
|---|---|---|
| `lb0` | 0.12·(365.22/1.267)·16 = 553.6 ≥ 20·16 = 320 | **554 mm** (alt çubuk, Konum II) |
| `lb` üst çubuk (Konum I) | 1.4·553.6 | **775 mm** |
| Kancalı `lbk` (alt) | 0.75·553.6 | 415 mm |
| Ara mesnette alt çubuğun komşu açıklığa uzaması | max(554, 50·16 = 800) | **800 mm** (kolon yüzünden) |
| Kenar mesnet, alt çubuk | a ≥ 0.4·554 = 222; b ≥ 12·16 = 192; a+b ≥ 554 | örn. hc = 400 → a_mevcut = 400−25−10−8 = 357 ≥ 222, **b = max(192, 554−357 = 197) = 197 → 200 mm** |
| Kenar mesnet, üst çubuk (lb = 775) | a ≥ 310; b ≥ 192; a+b ≥ 775 | hc = 400 → a = 357, **b = max(192, 418) = 418 → 420 mm** |
| Bindirme alt, r = 0.5 | 1.25·553.6 | 692 mm |
| Bindirme alt, r = 1.0 | 1.5·553.6 | 830 mm |
| Bindirme üst (montaj), r = 1.0 | 1.5·553.6·1.4 | 1163 mm (1.4 bir kez uygulandı) |
| B500C ile `lb0` (karşılaştırma) | 0.12·(434.78/1.267)·16 | 659 mm |

### 11.3 Bir sıraya sığan çubuk sayısı

- `b_av = 300 − 2·25 − 2·10 = 230 mm`
- `Dmax = 16` için: `s_min = max(16, 25, 21.3) = 25` → `n_max = floor((230+25)/(16+25)) = floor(6.22) = 6`. Gerçek net aralık = (230 − 6·16)/5 = **26.8 mm ≥ 25 ✓**
- `Dmax = 22` için: `s_min = max(16, 25, 29.3) = 29.3` → `n_max = floor(259.3/45.3) = 5`. Net aralık = 37.5 mm.
- Örnek yerleşim: 3φ16 alt düz + 4φ16 alt sol ilave = sol mesnet kesitinde 7 çubuk > 6. 1. sıraya 6 çubuk (köşelerde 2 düz, aradaki 1 düz ve 3 ilave simetrik), **1 ilave 2. sıraya** 1. sıradaki bir çubukla aynı eksene gelir. `s_v = max(25, 16, 21.3) = 25` → `y2 = 25+10+16+25+8 = 84 mm`.
- **[UYGULAMA / geometri]** Köşe çubuğu etriye kıvrımına (D_b = 5·10 = 50) yaslanırsa merkezi bacak iç yüzünden 13.0 mm içeride olur (8 yerine). Bu durumda b_av fiilen yaklaşık 10 mm azalır. 6 çubuklu seçenek 25 mm sınırına çok yaklaşır. Program bu düzeltmeyi seçenek olarak sunmalıdır.

### 11.4 Faydalı yükseklik ve etriye

- 1. sıra: `y1 = 25 + 10 + 8 = 43 mm` → `d = 457 mm` (tek sıra)
- Sarılma bölgesi: `L_sar = 2·500 = 1000 mm`, ilk etriye ≤ 50 mm
- DY yüksek: `s ≤ min(457/4 = 114, 8·16 = 128, 150) = 114` → **[UYGULAMA] 100 mm**. `n_sar = floor((1000 − 50)/100) + 1 = 10` adet/uç
- DY sınırlı: `s ≤ min(500/4 = 125, 128, 200) = 125` → 125 mm (veya 120)
- Orta bölge: `s ≤ d/2 = 228` → **[UYGULAMA] 200 mm**
- Minimum etriye: `Asw/s ≥ 0.3·1.267/365.22·300 = 0.312 mm²/mm`. 2 kollu φ8 (100.5 mm²) ile s ≤ 322 mm, yani d/2 belirleyicidir.
- Kol aralığı: `e = 300 − 50 − 10 = 240 ≤ 350` → 2 kol yeterli
- Kanca: 135°, uç boyu `max(6·10, 80) = 80 mm`, büküm iç çapı ≥ 50 mm
- Gövde donatısı: TS500'e göre h = 500 → gövde ≤ 600, gerekmez. TBDY'ye göre ln = 4000 ise h = 500 ≤ 1000, gerekmez.

---

## 12. Programın hesap sırası (sözde kod)

```python
def kiris_detay(geom, malzeme, donati, proje):
    # --- 0. Girdi ---
    # geom: bw, h, t, ln, kolon_sol{hc, b_dik, cc, phi_w, phi_l}, kolon_sag{...}, mesnet_turu_sol/sag ("kenar"|"ara"|"perde")
    # donati: montaj{n,phi}, alt_duz{n,phi}, ust_sol_ilave{n,phi}, ust_sag_ilave{n,phi},
    #         alt_sol_ilave{n,phi}, alt_sag_ilave{n,phi}, govde{n_yuz,phi}|None, etriye{phi_w, s_sar?, s_orta?}
    # proje: DY ("yuksek"|"sinirli"), DTS, cc, Dmax, fctd_modu ("tablo"|"formul"), L_stok=12000,
    #        k_ln=0.25, konum_ust="I", moment_diyagrami=None

    # --- 1. Malzeme ---
    fcd, fctd = beton(malzeme.sinif, fctd_modu); fyd = fyk/1.15; fywd = fyk_w/1.15
    assert malzeme.sinif >= "C25"                                   # TBDY 7.2.5.1

    # --- 2. Geometri kontrolleri (R-G-01..07) ---
    check(bw >= 250); check(bw <= h + b_dik); check(h >= max(300, 3*t)); check(h <= 3.5*bw)
    warn_if(ln < 2.5*h, "yüksek kiriş")

    # --- 3. Çubuk çapı ve adet kontrolleri (R-RHO-03) ---
    for g in tum_boyuna_gruplar: check(g.phi >= 12)
    check(montaj.n >= 2 and alt_duz.n >= 2)

    # --- 4. Kesit yerleşimi (R-SP-01..05) ---
    for kesit in ["sol_mesnet", "aciklik", "sag_mesnet"]:
        ust = [montaj(köşeler)] + ilgili_ust_ilave(kesit)
        alt = [alt_duz(köşeler)] + ilgili_alt_ilave(kesit)
        for yuz in (ust, alt):
            s_min = max(phi_max, 25, 4/3*Dmax)
            n_max = floor((bw - 2*cc - 2*phi_w + s_min) / (phi + s_min))
            sira1, sira2 = doldur(yuz, n_max, kose_once_surekli=True, simetrik=True)
            sira2 = hizala(sira2, sira1)                         # aynı düşey eksen
            y = konumlar(cc, phi_w, sira1, sira2, s_v=max(25, phi_max, 4/3*Dmax))
        d[kesit] = h - agirlik_merkezi(cekme_yuzu)

    # --- 5. Oran kontrolleri (R-RHO-01, 02, 04 ve 7.4.3.1(a)) ---
    for kesit: check(rho >= 0.8*fctd/fyd); check(rho <= min(0.02, 0.85*rho_b + rho_basinc))
    check(As_montaj >= 0.25*max(As_ust_sol, As_ust_sag))
    oran = 0.5 if DTS in {"1","1a","2","2a"} else 0.3
    check(As_alt_sol >= oran*As_ust_sol); check(As_alt_sag >= oran*As_ust_sag)

    # --- 6. Kenetlenme boyları (R-LB-01..05) ---
    def lb(phi, konum):
        v = max(0.12*fyd/fctd*phi, 20*phi)
        if 32 < phi <= 40: v *= 100/(132-phi)
        if cc < phi or s_net < 1.5*phi: v *= 1.2
        return v * (1.4 if konum == "I" else 1.0)
    for uc in ("sol", "sag"):
        if mesnet_turu[uc] == "kenar":
            a = hc - cc_k - phi_w_k - phi/2
            check(a >= 0.4*lb_)                                   # aksi halde HATA
            b = max(12*phi, lb_ - a)
            if a >= max(lb_, 50*phi): kanca = None                # düz kenetlenme
        elif mesnet_turu[uc] == "ara":
            alt_uzatma = max(lb(phi_alt, "II"), 50*phi_alt)       # komşu açıklığa, kolon yüzünden
        elif mesnet_turu[uc] == "perde":
            duz_kenetlenme(lb_)

    # --- 7. İlave boyları (R-CUT-01..05) ---
    if moment_diyagrami:
        L = kuramsal_kesim + max(d, 20*phi);  L = max(L, lb_)
    else:
        L_ust_ilave = max(k_ln*ln_ref, lb(phi, "I"))
        L_alt_ilave = max(lb(phi, "II"), 50*phi)
    if L_sol + L_sag >= ln - bosluk_min: birlestir_surekli()

    # --- 8. Çubuk boyu ve ekler (R-LAP-01..04) ---
    for cubuk: if cubuk.L > L_stok:
        yer = izinli_ek_bolgesi(cubuk)       # sarılma (2h), düğüm, açıklık ortası alt bölgesi hariç
        l0 = (1 + 0.5*r) * lb(phi, konum)    # 1.4 yalnız bir kez
        ek_etriyesi = min(h/4, 100) if not cubuk.montaj else None

    # --- 9. Etriye (R-ST-01..07) ---
    L_sar = 2*h; x0 = 50
    s_sar_max = min(d/4, 8*phi_min, 150) if DY == "yuksek" else min(h/4, 8*phi_min, 200)
    s_orta_max = d/2 if not (Vd > 3*Vcr) else d/4
    s_sar = min(kullanici_s_sar or inf, s_sar_max); s_orta = min(kullanici_s_orta or inf, s_orta_max)
    check(Asw/s_orta >= 0.3*fctd/fywd*bw); check(phi_w >= 8)
    n_kol = max(2, ceil((bw - 2*cc - phi_w)/350) + 1)
    kanca = dict(aci=135, uc=max(6*phi_w, 80), ic_cap=5*phi_w)

    # --- 10. Gövde (R-WEB) ---
    if (h - t_gövde_yorumu) > 600 or h > ln/4: govde_hesapla_ve_yerlestir()

    # --- 11. Çizim çıktısı ---
    # boyuna açılım: montaj, alt düz, ilaveler (boylarıyla), kancalar, ek yerleri ve boyları
    # enkesitler: sol mesnet, açıklık, sağ mesnet (+ ek kesiti); etriye/çiroz kanca detayı (TBDY 7.13.1.3)
    # sarılma bölgesi uzunlukları + etriye çap/sayı/aralık (TBDY 7.13.3); malzeme + çevresel etki sınıfı (7.13.1.1)
```

---

## 13. Açık konular, çelişkiler, mühendisin seçmesi gerekenler

1. **Süneklik düzeyi (DY) ve DTS** kullanıcı girdisidir. DY yalnızca sarılma bölgesi etriye aralığını değiştirir: yüksek için `d/4`, 150 mm; sınırlı için `h/4`, 200 mm. Kesit, boyuna donatı ve kenetlenme kuralları iki düzeyde aynıdır (TBDY 7.8.1–7.8.3). DTS, mesnette alt/üst oranını belirler (%50 veya %30).
2. **TBDY 7.4.4 metni ile Şekil 7.8 çelişiyor:** metin "etkili yüksekliğin 1/4'ü" (d/4) der, şekil `hk/4` der. Önerilen değer d/4'tür (güvenli taraf, TS500 8.1.6 ile uyumlu).
3. **TS500 7.3 ile 9.5.2 arasında minimum net aralık farkı:** 7.3 kirişler için 20 mm, 9.5.2 genel için 25 mm der. Önerilen değer 25 mm'dir.
4. **180° kanca uç boyu:** TS500 9.3.1(a) metni `4φ` ve `60 mm`, Şekil 9.1(a) `6φ` ve `60 mm` der. **Etriye kancası iç çapı:** 9.3.2 metni `4φ`, Şekil 9.1 notu `6φ` der. TBDY 135° özel deprem kancası için `5φ` ister. Programda ayarlanabilir olmalıdır.
5. **Konum I katsayısı (1.4):** Kiriş üst çubukları metne göre Konum I'dir, yani ×1.4. TBDY 7.4.3.1'deki "TS 500'de öngörülen düz kenetlenme boyu ℓb"ye bu katsayının dahil olup olmadığını metin açıkça söylemez. Önerilen: dahil edilmesi (güvenli taraf). ideCAD bu seçimi kullanıcıya bırakır.
6. **Bindirmede 1.4 çift sayılmamalı:** TS500 9.2.5.1 "Konum I'e giren çubuklarda ℓ0 1.4 çarpanıyla artırılır" der. Denk. 9.2'deki ℓb Konum düzeltmesi yapılmamış temel değer (Denk. 9.1) olarak yorumlanırsa 1.4 bir kez uygulanır. Bu belgedeki örnekler böyle hesaplandı.
7. **TS500 9.1.2.1 azaltma yasağı** 1997 yönetmeliğindeki "süneklik düzeyi yüksek" elemanlara atıf yapar. TBDY'deki DY sınırlı kirişlerde `As_gerekli/As_mevcut` azaltmasına izin verilip verilmediği açık değildir. Önerilen: azaltma hiç uygulanmamalı (ideCAD da TBDY'yi esas alır).
8. **İlave kesim boyu:** Moment diyagramı yoksa `ln/4` bir **pratiktir, yönetmelik değildir**. TS500 7.3 "kuramsal kesim + max(d, 20φ)" der. TS500 9.1.2.1 ise "gereksinme duyulmayan noktadan ℓb uzatma" der. İki ifadenin birlikte okunuşu yoruma açıktır. Güvenli seçenek `max(d, 20φ, ℓb)`'dir.
9. **Alt ilavenin açıklığa uzama boyu** ve **"açıklık ortası alt donatı bölgesi"nin uzunluğu** TBDY'de sayısal olarak tanımlanmaz. Mühendis parametresi olmalıdır.
10. **TBDY 7.4.3.1(c)'deki "açıklığa komşu olan kolon yüzü" ifadesi:** Şekil 7.7'ye göre komşu açıklığın alt donatısı kolonu geçer ve bu açıklığa kolon yüzünden ≥ max(ℓb, 50φ) uzanır. Bu uzanma, komşu kirişin sarılma bölgesi içinde bindirmeye benzer bir durum oluşturur. Bu yönetmeliğin kendi istediği detaydır ve 7.4.3.2(a)'daki ek yasağıyla çelişki gibi görünebilir. Uygulamada ideCAD bu maddeyi otomatik uygular. ProtaStructure'da bunun için "Bindirme ile Bitişik Açıklığa Uzat" seçeneği bulunur [UYGULAMA].
11. **Sarılma aralığındaki `8φ` için "en küçük boyuna donatı çapı":** Gövde donatısının bu minimuma dahil olup olmadığı yazılmamıştır. Önerilen: yalnız üst ve alt boyuna çubuklar (montaj dahil) esas alınmalı. Gövde dahil edilirse sonuç daha güvenlidir.
12. **"Gövde yüksekliği > 600 mm"** (TS500): `h` mi yoksa `h − t` mi olduğu tanımlanmamıştır. Önerilen: `h` (güvenli).
13. **Paspayı:** TS500 en az 20/25 mm verir. Dayanıklılık için TS EN 206 ve TS EN 1992-1-1 çevresel etki sınıfına göre daha büyük değer gerekebilir. Proje girdisidir.
14. **φ > 30 mm bindirme yasağı** (TS500 9.2.6.1) basınç eki başlığı altındadır. Çekme eklerine genel uygulanıp uygulanmadığı açık değildir. Önerilen: φ > 30 mm için bindirme yerine manşon kullanılmalı (TBDY 7.4.3.2(b) koşullarıyla).
15. **TS 708:2016 değerleri** ikincil kaynaktan alındı. TS 708'in kendisi doğrudan okunmadı.

---

## 14. Kaynak listesi

1. TSE, **TS 500 Betonarme Yapıların Tasarım ve Yapım Kuralları**, Şubat 2000. Maddeler: 0.2.2, 3.2, 3.3 (Ç.3.2), 6.2.5, 7.1 (Ç.7.1), 7.3, 8.1.3–8.1.6, 8.2.6, 9.1–9.5. Kopya: https://web.itu.edu.tr/mdaskiran/wp-content/uploads/2014/09/TS500.pdf
2. TS 500:2000/T3 (Kasım 2014) tadili, R.G. 18.01.2015: https://www.resmigazete.gov.tr/eskiler/2015/01/20150118-9.htm
3. **Türkiye Bina Deprem Yönetmeliği** (TBDY 2018), R.G. 18.03.2018/30364 (mük.). Maddeler: 4.3.4, 7.2.2, 7.2.5, 7.2.6, 7.2.7, 7.2.8 (Şekil 7.1), 7.4.1–7.4.5 (Şekil 7.7, 7.8, 7.9), 7.5.1–7.5.2, 7.8.1–7.8.5, 7.13. ÇŞB PDF: https://webdosya.csb.gov.tr/db/yapiisleri/icerikler/tbdy_2018-20210506174126.pdf — R.G.: https://www.resmigazete.gov.tr/eskiler/2018/03/20180318M1-2-1.pdf
4. TS 708:2016 Çizelge 3 (ikincil): https://help.idecad.com.tr/ideCAD/malzeme-ts-500-3 ; https://insapedia.com/wp-content/uploads/2018/05/TS_708.pdf
5. A. Topçu, *Betonarme I*, ESOGÜ 2019, "Kirişlerde sınır değerler": https://atopcu.osmanmuratkaya.com/index_dosyalar/Dersler/Betonarme1/Sunular/Betonarme_1_5.pdf
6. ideCAD yardım: https://help.idecad.com.tr/ideCAD/donatnn-kenetlenmesi-ts-500 ; https://help.idecad.com.tr/ideCAD/kirislerin-boyuna-donati-kosullari ; https://help.idecad.com.tr/ideCAD/kirislerin-enine-donati-kontrolleri ; https://help.idecad.com.tr/ideCAD/kiris-betonarme-tasarim
7. ProtaStructure kiriş tasarım ayarları: https://protasoftware.com/tr/destek/teknik-kilavuzlar/kiris-tasarim-ayarlari-ps2022/
