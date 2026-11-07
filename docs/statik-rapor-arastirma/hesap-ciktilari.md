# GPR'siz "Hesap çıktıları" — ara hesap bölümleri (N_ULK_02 s.48–107, 1417–1418)

Doğrulama projeleri: N_ULK_02 (GPR analizden yeni — birincil), K_BLG_03 (GPR BEA'dan yeni — ikincil).
CP_03, FHM_GO_A, VKF_01 GPR'leri BEA'dan eski; farkların bir kısmı bundan olabilir.

Araçlar (hepsi `scratchpad\ara_hesap\` altında; StatikRaporClaude kaynağına dokunulmadı, DLL `lib\` kopyasından):
- `tool\` — bölüm 1–3 (bu rapor). `Ara.exe hepsi|duvar|doseme|kiris`, `ST4=<st4>` ortam değişkeni; `kos.sh` 5 projeyi koşar, çıktılar `out_<proje>.txt`.
  Yardımcı modlar: `dump p1 p2` (GPR sayfa metni), `bas <metin>` (başlık arama), `rec EXT boy no [no2]`, `find tol v…`, `tani`, `zin`, `dug n…`.
- `sehim\` (bölüm 4), `burkulma\` (5), `kolonkesme\` (6), `kiriskesme\` (7), `temel\` (8–9): ayrıntılı raporlar `sehim\RAPOR.md`, `burkulma\RAPOR.md`,
  `kiriskesme\RAPOR.md`, `temel\RAPOR_temel.md`; kolonkesme için kurallar aşağıda.

Genel STA4 baskı tuhaflıkları (bu çalışmada bulundu):
- DÖŞEME ŞERİT tablosu sayfada 84 satır basar ve **her sayfa sonunda bir ST4 satırını atlar** (0 tabanlı 84, 169, 254… indisli kayıt yok). 4 projede birebir.
- DUVAR YÜK tablosu **tek sayfada (70 satır) kesilir** (CP_03: 137 duvar → 70 satır; sonrası basılmaz).
- Kiriş adı: ST4 /Beams Data/ 15. alan (indis 14) = 1 → **panel**, ad öneki "P" (PB-066). (Bölüm 4 raporundaki "15. alan = 1 → konsol" yorumu panelle karışmış olabilir; K_BLG'de sehim tablosunda olmayan 36 satır bunlarla ilişkili olabilir — kontrol edilmeli.)
- Kiriş listesi sırası (KİRİŞ VE PANEL, KİRİŞ/PANEL DUVAR ve büyük olasılıkla diğer kiriş tabloları): **sürekli kiriş zincirleri** (aşağıda `Zincir2`).

---

## 1. s.48 KİRİŞ/PANEL DUVAR YÜK BİLGİLERİ ve DUVAR YÜK BİLGİLERİ

### 1a. KİRİŞ/PANEL DUVAR YÜK BİLGİLERİ
**Kaynak:** ST4 /Beams Data/ (1. satır) + proje `.USE` duvar kütüphanesi.
- Satır: kiriş 1. satırında 17. alan (indis 16) B > 0 ve 18. alan (indis 17) kod > 0 olan kirişler.
- H = kod % 10000 (cm); tip = max(kod/10000 − 1, 0) (kod 10270 → tip 0, kod 100 → tip 0).
- g (t/m²): `.USE` dosyasında "ad" satırını izleyen 10 alanlı satır = duvar kalemi `tip, kalınlık, g, H, pencere g, pencere H, …`;
  kalınlık = B ve tip eşleşen ilk kalem (yoksa yalnız kalınlık). N_ULK: 19 cm → 0.320.
- G = g·H/100. Biçim: g, G F3; H, B tamsayı. Duvar tipi adı: tip 0 → "Tuğla" (diğer tipler — Gazbeton, Cam, Panel… — projelerde örnek yok, doğrulanmadı).
- Ad: panel ise "P" öneki. Sıra: kiriş zinciri sırası (`Zincir2`). İki sütunlu basım: satır satır sol, sağ.

### 1b. DUVAR YÜK BİLGİLERİ
**Kaynak:** ST4 /Walls/ (satır başına 18 alan) + /Axis data/ + /Floors Data/ + /Slab foundations/.
- Alanlar: [0] kat, [2] G×100 (t/m, doğrudan), [3] B (cm), [4] kod (H = kod % 10000, tip = kod/10000 − 1), [5] aks, [6] sol aks, [7] sağ aks, [8] hizalama.
- Duvar no = sıra (1…). kat: 0 → "Zemin", diğer → sayı. Aks adı: 1000+n → "nx", 2000+n → "ny" (küçük harf).
- Aks koordinatları: ST4 5. satır 1./2. alan = X/Y aks sayısı (N_ULK 23/23); /Axis data/ satırlarının 2. alanı; önce X aksları sonra Y.
- L = |aks(sağ) − aks(sol)|, **float32 farkı** → G7 → yarım yukarı F2 (0.725 → 0.73).
- Yüklenen eleman: aynı kattaki döşemeler (döşeme no / 100 = kat) içinden, duvar doğrusu üzerindeki **1/4 veya 3/4 noktasını** (kenar dahil) içerenler,
  artan no ile virgülle ("D210,D211"); ad "D"+no. Kat 0'da önce radye plakları (/Slab foundations/ ad satırı + "kalınlık, x1, x2, y1, y2" satırı).
- En çok 70 satır (STA4 kesmesi).

**Doğrulama**
| Proje | KİRİŞ/PANEL DUVAR | DUVAR YÜK |
|---|---|---|
| N_ULK_02 | 174/174 | 242/242 |
| K_BLG_03 | 1878/1878 | (tablo yok) |
| VKF_01 | 1710/1710 | (tablo yok) |
| FHM_GO_A | 2672/2784 (sırasız 2784/2784) | 363/363 |
| CP_03 | 2456/4056 (sırasız 4056/4056) | 767/770 |
Kalan: FHM/CP'de yalnız satır sırası (zincir kuralının istisnaları, aşağıda); CP'de 0.73 m'lik 3 kısa duvarda iki döşeme yerine biri.

---

## 2. s.49 DÖŞEME ŞERİT BİLGİLERİ, s.50 DÖŞEME BİLGİLERİ

### 2a. DÖŞEME ŞERİT
**Kaynak:** ST4 /Floors Continuous/: satır çiftleri (kod, metin); kod 100 → "X", 200 → "Y", 0 → son. Metin aynen (sağ boşluk kırpılır).
Sayfa sonu atlaması: (yazılan + atlanan + 1) % 85 == 0 olan kayıt basılmaz.
**Doğrulama:** N_ULK 48/48, K_BLG 254/254, CP 544/544, FHM 394/394, VKF 280/280.

### 2b. DÖŞEME BİLGİLERİ
**Kaynak:** ST4 /Floors Data/ (2 satır/döşeme) + FLO (200 bayt, ST4 sırası).
- Satırlar: döşeme no ≥ taban (100; kat 0'ın 1, 2, 3 nolu döşemeleri basılmaz), **no'ya göre artan** sıralı.
- Ad (DZ-01…), Sol/Sağ/Üst/Alt aks = alan [8],[9],[10],[11] → "7X", "2Y" (büyük harf).
- yön = alan [4]: 0 "PLAK", 1 "MERDIVEN-X", 2 "MERDIVEN-Y".
- "d, dmin" = `$"{d,3}È{dmin,3}"` (È = STA ≥), d = alan [1].
- bo, bt boş (asmolen/nervür yok).
- g, q = alan [2], [3] F3.
- **gx = g·k, gy = g·(1−k), qx = q·k, qy = q·(1−k); k = FLO +0 (float)**. 54/54 her sütun; 5 projede bu dört sütun birebir.
- dmin (TS500 11.2; yaklaşık): net açıklıklar nx = FLO+116 − FLO+88 − FLO+92, ny = FLO+120 − FLO+104 − FLO+108
  (FLO +84..+112: dört kenarda kenar kirişinin aks solu/sağı parçaları); ls = min, m = max/min;
  αs = sürekli kenar sayısı/4 (FLO +76..+82 int16 komşu döşeme ≠ 0); m ≤ 2 ise h = ls/(15+20/m)·(1−αs/4)·100, yarım yukarı; en az 8.
  Merdiven (yön ≠ 0) ve kotlu döşeme (alan [5] ≠ 0) → 8. Tek doğrultu (m > 2) → 8 (çözülmedi).
**Doğrulama (tüm tablo hücreleri):** N_ULK 806/810 (dmin 50/54), K_BLG 4085/4110, CP 6870/6930, FHM 5345/5370, VKF 3266/3315 —
kalan farkların tamamı dmin. Açık: tek doğrultulu döşemelerde STA4 değeri (GPR 9–13) l/25, l/30, l/35 ile tutarlı değil; N_ULK DZ-11/12 (merdiven
boşluğu komşusu) 10; birkaç sınırda ±1 (9.49 → 10).

---

## 3. s.55 KİRİŞ VE PANEL BİLGİLERİ
**Kaynak:** ST4 /Beams Data/ + BEA + KMX + COL.
- Kiriş no (panel → "P"), aks [4], sol aks [5], sağ aks [6] ("2Y", "1X"), D = alan [2], B = alan [1].
- **G = alan [3]/100** (t/m, ST4'te hazır: 31 → 0.310), F3.
- I/J nokta = **BEA +32 / +34 int16** (analiz düğümü; ST4 2. satırdaki düğüm değil).
- L = I ve J düğüm koordinatlarının **kiriş doğrultusundaki farkı** (aks 2xxx: Δx, 1xxx: Δy), float32 farkı, G7 yarım yukarı F2.
  Düğüm koordinatı (`Dugum.Koordinat`): kolon düğümü COL +0 (üst düğüm) → (COL +18, +22). Kolon olmayan düğüm (kirişe oturan kiriş/konsol ucu):
  1) düğümü uç olarak paylaşan kirişin diğer ucu biliniyorsa dik bileşen oradan; 2) yoksa düğümden geçen kiriş çizgisi = aks − f7·B/200
  (f7 = ST4 kiriş 8. alan ±1 hizalama); 3) yoksa uç aksı. Aynı adlı (bölünmüş) kirişin ara düğümünde boyuna koordinat bölünme aksından.
- Rh = 0.00; Tabla b/d = **KMX +0 + KMX +4 (kenar tabla genişlikleri, cm) / max(KMX +2, +6)** ("54/15" = 25+29); sol/sağ Hg/Lg "0/0"; malzeme "E1".
- Sıra: `Zincir2` — ST4 sırasında gezilir; aynı aks/kattaki kirişler J düğümü = I düğümü ile zincirlenir (ardıl seçiminde aynı B/H/G önce);
  öncülü olan (panel olmayan, öncülü panel olmayan) kiriş bekletilir; yazılan her kirişin ardından ardıl zinciri yazılır; panel bekletilmez, yalnız ardılını çeker.

**Doğrulama**
| Proje | sıralı | sırasız (içerik) | L sütunu |
|---|---|---|---|
| N_ULK_02 | 924/924 | 924/924 | 66/66 |
| K_BLG_03 | 6437/6468 | 6468/6468 | 462/462 |
| VKF_01 | 4347/4550 | 4347/4550 | 122/325 |
| FHM_GO_A | 7312/8428 | 8254/8428 | 428/602 |
| CP_03 | 5793/10976 | 10690/10976 | 498/784 |
L dışındaki 13 sütun 5 projede birebir. Açık: VKF/CP/FHM'de L (poligon kolon düğümleri, konsol uçları, eğik/kaçık akslar);
satır sırasında istisnalar (K_BLG'de üst üste iki kiriş K2-002/003; CP'de farklı akstaki doğrultudaş kirişler zincirleniyor, paralel
çift kirişler) — K_BLG 6 satır, CP/FHM'de kayma.

---

## 4. s.84 KİRİŞ SEHİM ve ÇATLAK KONTROLU — (ayrıntı: `sehim\RAPOR.md`)
- δi+δig·λ: **BEA +760 ×1000, mutlak**, F3 — N_ULK 64/64, K_BLG 426/426, CP 623/624.
- Elastik = Ani (δi) = δt/(1+λ·r); λ = 2/(1+50ρ'), ρ' = As_montaj(KMX +108 adet, +110 çap)/(b·(h − 5.5)), paspayı OPS 10. satır 1. alan;
  r = ΣG/Σ(G+Q) LOA yük satırlarının uzunluk integrali (öz ağırlık, ST4 G hariç). N_ULK 62/64, K_BLG 382/426, CP 470/624.
- Wk (sehim tablosu) = KirisWk × 0.97 → F2: N_ULK 64/64, K_BLG 386/426, CP 616/624 (fark: konsollar).
- fmax: "L/360=" / duvarlı (ST4 18. alan kod%10000 ≥ 200) "L/240= …w"; 360/240 OPS 10. satır 2./3. alan; Ln = net açıklık
  (rijit uç ST4 2. satır 7./8. alan veya BEA yüz koordinatları +684/+700). N_ULK 63/64, K_BLG 318/426.
- Durum ò: 64/64, 426/426, 624/624.
- sol/sağ T ve M, M ac: dosyada yok; ankastre tek açıklık + LOA servis yükleri (G+Q) ile hesap, ±0.01–0.03 (N_ULK T 47/64, M 46/64, M ac 36/64).
- Toplam hücre: N_ULK 597/704, K_BLG 3459/4686, CP 4070/6864. Çözülemeyen: kirişe oturan kirişin tekil yükü, konsol fmax, M ac tanımı.

## 5. s.85–91 KAT BURKULMA ve KOLON BURKULMA — (ayrıntı: `burkulma\RAPOR.md`)
- Kolon blokları (44 kolon × X/Y): ST4 kolon/aks/malzeme + COL + BEA + KMX. Hk, I, Ac, (I/L)c, ψ, Rm, Nd (=1.4Nz[0]+1.6Nz[1], COL +26), metin 80/80;
  (I/L)b 154/160 (0.35·I_T/ln, tabla KMX +0/+4, +2/+6), EI 75/80, Cm 71/80, β 71/80, k 47/80 (±0.002), λ 65/80, Lkr 15/80 (±%0.4), Nkr 0/80 (±%0.4).
  Toplam düğüm-40 tuhaflığı hariç 1378/1600 birebir, 1504/1600 ±%0.4.
- Kat tablosu: "ΣNg" aslında ΣNd (4/4), ΣNkr = kolon Nkr toplamı (4/4), βs = 1/(1−1.3ΣNd/ΣNkr) eğer é > 0.05 (formül 4/4).
  **Σ(Δi·Nd/L), Vfi, é üretilemedi (0/4)**: göreli kat ötelemesi Δi hiçbir dosyada bulunamadı (DIS çözülemedi); Vfi deprem raporundaki kat kesmesi değil.
- İkinci proje yok (diğer GPR'lerde bölüm basılmamış).

## 6. s.92–101 KOLON KESME / SARILMA / KESME GÜVENLİK / PERDE KESME — (araç: `kolonkesme\tool`, modlar kd, sar, perde, guv, kapf, ve)
- Ortak: fck/fyk ST4 /Materials/ 1. satır; fctd = 0.35·√fck·3.16/1.5 (11.66); fyd = fywd = fyk/1.15; As = Σ adet·3.142185·ø²/400 (SMX int16 +161+2i);
  etriye alanı 0.785ø²/100; kol sayısı SMX bayt[193]/[195] + a4 bit; satır sırası: perde olmayan kolonlar, yerel no, kat azalan.
- KESME DAYANIM: Ac, As, Asw/s, Vw (d = Bx/By, paspayı düşülmez), Vcr = 0.65fctd·Ac, Vr = 0.8Vcr+Vw, Vfr = As·fyd; Vd x: max(|G|+max|Qi|+max|EX±|, 1.4|G|+1.6max|Qi|) (COL Tx +466/Ty +554).
  N_ULK 632/640, K_BLG 2761/2880 — kalan y yönü Vd (kural projeler arası tutarsız).
- SARILMA (metin): Nd = max(SMX +40, +100); bk = By−5/Bx−5; s' = s/2 (R ≤ 4) / s; Asw1/Asw2 TBDY 7.3.4; ≤1 ise 0.66·max. N_ULK 120/120 satır, K_BLG 450/450.
- PERDE KESME GÜVENLİK: N, Vfd, Vd (güçlü yönde V(G+Q)+1.2·D·(E+0.3E')), Vr, Vrh, Vfr — N_ULK 87/96 (β 4, üst kat Y Vd 4, N 1 eksik); K_BLG kalibre edilmedi.
- KOLON KESME GÜVENLİK (s.96–99): kısmen — Ve = (Mu+Ma)/Ln yalnız 38/160 birebir; kiriş Mp elle 4 değer tuttu; kolon Mr ±%1 (25–33/80 birebir);
  Ve tavanı ve Mu/Ma dağıtımı (EX−/EY+ seçimi) çözülemedi.

## 7. s.101–102 KİRİŞ KESME/BURULMA ve ASKI — (ayrıntı: `kiriskesme\RAPOR.md`)
- Yeni ofset: **BEA +214 sol, +302 sağ burulma T** (22 float). V +390/+478. Kombinasyonlar 1.4G+1.6Qi, G+Q1±E (8..11 ikisi de).
  Vd = |V_uç| − |q|·x0 (x0 = s/2 + min(d, Ln/3), s = BEA +16+20 / +24+28); Tr'yi büyük yapan uç/kombinasyon; d = H−5.5; Tcr = 1.35fctd·Σx²y/3 (tabla 0.06Ln).
  N_ULK 700/768 (Vcr, Vw, Vr, Vd, Td, Aov, Aot, Asw, ò 64/64; Tcr 26/64 ±1–7 ×10⁻⁴, Tr 35/64), K_BLG 4248/5112. Satır sırası üretilemedi (bölüm 3 zincir kuralı denenmeli).
- ASKI: satır seçimi (≥3 kiriş ucu, kolon olmayan düğüm; aynı adlı geçen çift ana kiriş), "í{ø}/9", Asw, Vc, Vw, Vr: N_ULK 18/18 (Vd dahil);
  FHM 824/824, CP 1136/1136 Vd hariç. **Askı Vd üretilemedi** (N_ULK'de |V_G,uç| − q_G·x0 2/2, diğer projelerde tutmuyor).

## 8. s.103–106 TEMELLERE GELEN KOLON YÜKLERİ, SÜNEK MOMENTLER — (ayrıntı: `temel\RAPOR_temel.md`)
- KOLON YÜKLERİ: COL N +26, Alt Mx +202, Alt My +378; N satırı G[0], Q azaltmalı ("Nq = Q[1]×f"), Zemin[7]; Mx/My indis 0..7; deprem 8..13 (+14).
  Kolon seçimi: grubun en alt parçası ve COL +2 alt düğümü ne kolon üst düğümü ne kiriş düğümü. Sıra kat, yerel no.
  **N_ULK 1032/1032**; K_BLG 2643/2806, FHM 2079/2193, CP 1286/1376, VKF 883/946 — farkların tümü azaltma katsayısı f (3 kattan fazlada kolona göre 0.899–0.951; çözülmedi).
- SÜNEK MOMENTLER: COL Alt Mx 8/9, Alt My 10/11 × (bodrumsuz: kolon 0.6·D, perde D; bodrumlu: 1.5); D = ST4 14. satır 1. alan.
  **Me N_ULK 96/96, K_BLG 244/244, FHM 204/204, CP 128/128, VKF 88/88**; koşul sütunu 24/24, 61/61… (CP 31/32).
  Perde Mpx/Mpy dosyada yok: kesit hesabı ±%0.6 ort. (güçlü eksende en kötü %4; tam 13/140).

## 9. s.107 TEMEL ZIMBALAMA, s.1417–1418 PERDE-DİYAFRAM KESME — (ayrıntı: `temel\RAPOR_temel.md`)
- ZIMBALAMA: N_ULK'te yalnız başlık (radye, /Single footings/ boş) — kural: tekil temel yoksa bölüm boş; formül örnek olmadığı için kalibre edilemedi.
- PERDE-DİYAFRAM: Vx = D·|Vxalt − Vxüst| (COL Tx +466 indis 8), Vy = D·|Vyalt − Vyüst| (Ty +554 indis 10), üst = aynı x,y'deki üst kat perdesi — 24/24.
  Asb kirişte (perde ucundaki üst donatı fazlası, KMX) / döşemede (DMX düz donatı) 22–23/25; Vxr/Vyr bağıntısı GPR geometrisiyle 48/50.
  **Üretilmeyen:** kenar geometrisi (L, Rx, Ry, X1..Y2, şekil etiketleri) — kural tarif edildi, kodlanmadı; ikinci proje yok.
