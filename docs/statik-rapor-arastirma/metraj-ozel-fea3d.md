# GPR'siz üretim araştırması — Metraj (A), Özel tablolar (B), FEA3D (C)

Proje: N_ULK_02 (GPR 25.10.2026, 1518 s.). Araç: `ara_metraj\tool` (Ara.exe; komutlar `temel`, `kiriskalip2` [KV=1], `kolonkalip`, `kfoot`, `doskalip`, `kirisdon2`, `etrfit`, `kolondon`, `flo`, `duvar`, `temelbeton`).
Doğrulama çıktısı: `ara_metraj\dogrulama_A.txt`. Alt raporlar: `B\RAPOR_B.md`, `C\RAPOR_C.md`, `D\RAPOR_D.md` (döşeme donatısı).

**İkinci proje:** D:\projeler altındaki 1131 GPR'den metraj tabloları yalnız N_ULK_02'de var → A bölümü ikinci projede doğrulanamadı.
KSF / `@.TXT` dosyaları (CP_03, FHM_*, CP_OTP_*) yalnız özet tabloları içerir ve **çizim/keşif modülünden** yazılır; GPR metrajıyla birebir değildir
(N_ULK_02.KSF beton 394.08 / GPR 396.10; kiriş ø14 886.6 / 888.3). KSF ayrıca GPR'deki "KEŞİF ÖZETİ" (birim fiyat) tablosunu içerir.

Genel sayı biçimi (metraj): değer float32 aritmetiğiyle hesaplanır.
- Kolon kalıp/beton: float32 değerin kesin ikili değeri yuvarlanır (6.6949997 → 6.69; G7→yarım-yukarı kuralı burada YANLIŞ verir).
- Kiriş kalıp: geometrik mesnet yüzleriyle float32 → G7 → yarım-yukarı (KV=1) satırları ve 3/4 alt toplamı tutar.
- Döşeme: float32 aks farkları; 5 hücre .xx5 sınırında ±0.01.

---

## A. METRAJ (s.30–40)

### A1. TEMEL DONATI KEŞİF METRAJI — 140/140 satır (420/420 hücre)
- Kaynak: **`<proje>.MATBQF`** (radye analizinde yazılır). Kayıt 8 bayt, int16 ×4: `adet, çap, k, boy(cm)`; dosya sırasıyla, satır başına 3 kayıt; tüm satırlar ilk radye plağının adıyla (PL1), etiket "Mat Duz".
- `k` (33/20/8) basılmaz (aralık/tip kodu olası).
- Kat özetindeki "MAT TEMEL" kg değerleri MATBQF'den tutmuyor (ø8 9.7 ↔ 9.22; ø12 7304.5 ↔ 6698): başka temel elemanları da katılıyor; **çözülmedi**.

### A2. KİRİŞ KALIP VE BETON — 512/512 hücre (64 kiriş satırı, 2 kat)
- Sıra: ST4 /Beams Data/ sırası; aynı numaralı ardışık kayıtlar (KZ-28, KZ-21) tek satır.
- Ad K + kat kodu + no%100; bw, H = ST4 1. satır 2./3. alan.
- d1/d2 = H − (o yandaki döşeme kalınlığı), döşeme yoksa H. d1 = küçük koordinat yanı (X kirişinde güney, Y kirişinde batı). Yan döşeme: aynı kat, kenarı kiriş aksında, kiriş açıklığıyla örtüşen; çoksa en kalını.
- Ln = |aks(son) − aks(baş)| − ta − tb (float32). ta/tb = uçtaki mesnetin açıklığa taşan kısmı: aynı kat kolon dikdörtgeni (Column axis data satır no = kolon yerel no; kod −1 → aks..aks+b, +1 → aks−b..aks, 0 → ortalı, diğer negatif → aks+kod/1000 merkez kaydırma) veya dik kiriş bandı (kod ±1/0 aynı kural). ST4 2. satır rijit uç boyları (.299 vb.) kullanılmaz.
- Kalıp = Ln·(bw+d1+d2)/100 − Σ(düğümsüz uç noktalarında dik kiriş kayıtlarının B·H). Düğümsüz nokta = kiriş kaydının I veya J düğümü 0 olan ucu; o noktaya ucu değen dik kiriş kayıtlarının hepsi (birleşik kirişin iki parçası iki kez) düşülür (KZ-13/14 −0.30, KZ-21/28 −0.125).
- Beton = bw·H·Ln.
- Alt toplam = satır değerlerinin kesin toplamı: 115.875 → 115.88 ✓, 18.92/16.15 ✓; zemin kalıp 139.4625 ↔ GPR 139.47 (0.0075 fark; düğümsüz uç düşümünün kesin değeri 0.29375–0.29875 aralığında olabilir).

### A3. KOLON KALIP VE BETON — 352/352 hücre, alt toplamlar 4/4
- Tip "Dikdortgen"; bx, by; Uk = 2(bx+by) cm; Hk = ST4 kolon 2. satır üst−alt kotu.
- Kalıp = Uk/100·Hk − Σ B·H (kolona değen aynı kat kirişleri; bant kolonu kesiyor ve boyuna temas var; birleşik kirişin 2.+ parçaları sayılmaz). Float32, kesin ikili yuvarlama.
- Beton = bx·by·Hk.
- Alt toplam = Σ satır kesin değerleri (187.679 → 187.68; 115.57); beton alt toplamı 3 hane (19.326, 12.352).
- Satır sırası: kat 2'de no sırası; zeminde 103,102,101,104,105… (ST4 sırası 104,101,102,103,105…). Uyan varsayım: "no son elemandan küçükse başa ekle, değilse sona". Tek örnek.

### A4. DÖŞEME KALIP VE BETON — 508/513 hücre, alt toplamlar 6/6
- Satırlar döşeme no sırasıyla. Zemin tablosu S katı döşemelerini (DS-) de içerir.
- d = ST4 2. alan.
- Lx/Ly (Lx1=Lx2, Ly1=Ly2) = aks açıklığı − kenar kirişinin döşeme içine taşan genişliği (her kenarda en büyük). Döşeme 6. alanı f5 ise: |f5| > d·10 olan merdiven sahanlığında kırpma yok.
- Kalıp = Lx·Ly + d/100·Σserbest. FLO kaydında (200 bayt, ST4 sırası) kenar k=0..3 için +60+4k kenar kirişi ve +76+2k komşu döşeme (int16) ikisi de 0 ise kenar serbesttir; k=0,1 için **Lx**, k=2,3 için **Ly** eklenir. Ayna konsollardaki asimetri buradan gelir (D1-08 1.3 ↔ D1-11 17.5).
- Beton = Lx·Ly·d. Plak dolgu 0.00 (asmolen yok).
- Kalıp alt toplamı yalnız katın kendi döşemeleri (S hariç): 299.8725 / 340.6175. Beton alt toplamı S döşemeleri dahil: 90.90; özetteki zemin döşeme betonu 43.39 S hariç.
- Kalan 5 hücre .xx5 sınırında ±0.01 (DZ-16/19 11.025, DZ-24 1.375, D1-01 21.315, D1-07 9.425); STA4'ün float işlem sırası bilinmiyor.

### A5. KİRİŞ DONATI KEŞİF — etiket/adet/çap 291/291 satır; etriye boyu 64/64; çubuk boyu çözülmedi
- Kaynak: KMX (300 bayt; BEA I/J düğüm eşlemesi), int16 dizisi +108: `[montaj n,ø, düz n,ø, pilye n,ø, pilye2 n,ø, gövde n,ø, solÜst n,ø, solAlt n,ø, sağÜst n,ø, sağAlt n,ø, ?, etriye ø, s_orta, s_uç]`.
- Satır sırası: Montaj, Düz, (Pilye), Gövde, SolUstIla, (SolAltIla), SagUstIla, (SagAltIla), Etriye. n>0 olanlar basılır; birleşik kirişte sol değerler ilk kayıttan, sağ değerler son kayıttan.
- Etriye adedi: L = round(Ln·100, 0.1) + 5 cm, Lc = min(2h+5, L/2).
  - s_uç = 0 ise n = ⌈L/s_orta⌉ + 1.
  - Diğer durumda n = 2⌈Lc/s_uç⌉ + ⌈(L−2Lc)/s_orta⌉ + 1.
  - Ln, A2'deki geometrik net açıklık. 64/64.
- Etriye boyu = 2(b+h) − 12.
- Çubuk boyları (Montaj/Düz/Gövde/ila) hiçbir dosyada yok; STA4 kiriş çizim algoritması (süreklilik, ek yeri, ø'ye bağlı bindirme) üretir.
  - Örnek: Düz ø14 653 ↔ ø12 643 (Δ=5ø).
  - Örnek: aynı geometrili KZ-07 576/636 ↔ K1-06 543/663.
  - Çözülmedi.

### A6. KOLON DONATI KEŞİF — etiket/adet/çap kısmen; boylar çözülmedi
- Kaynak: SMX +161 int16 ×9 `[etriye ø, s_orta, s_uç, kol bayrağı, ?, n1, ø1, n2, ø2]`.
- Gövde satırları: 2·n1 øø1 ve 2·n2 øø2 (44/44 kolonda görsel olarak tutuyor).
- Etriye boyu = 2(bx+by) (kolon). Perdede "Etriye" boyu = uzun kenar − 5, ikinci "Etriye" 246 (sabit).
- Çiroz boyu = b + 24 (ø10) / b + 18 (ø8).
- 2.Etriye (bayrak 1): 105 (30×50), 147 (30×110); kural ≈ 2(bx + 0.35·by + 5), yalnız iki örnek.
- Etriye adedi (zemin, s 15/8): n = 2⌈Lc/8⌉ + ⌈(H−2Lc)/15⌉ + 1, Lc = max(2·hmax+5, ~100), H = kat kolon boyu (30×40 → 44, 30×50 → 45, 30×50 normal → 37, 30×110 tümü sıklaştırılmış → 57).
  - S1-01 (s 8/0 → 38), perde ve çiroz adetleri tutmuyor.
- Boyuna boylar (459/338/524/255/345) çözülmedi: üstte kolon devam ediyor mu, perde mi, bindirme.

### A7. DÖŞEME DONATI KEŞİF (ayrıntı `D\RAPOR_D.md`)
- Satır yapısı, etiket ve çap: DMX +96 (X) / +114 (Y) dizileri `[ø, s, bayrak, pø, ps, solø, sols, sağø, sağs]` → 207/207. Y ilavesi metrajda "Y sag ila".
- Adet: n = ⌈L_dağ/s⌉ (float32), L_dağ = A4 net açıklığı; 145/207. Kalan: x 10.375–15.8 döşemeleri, konsollar, merdivenler.
- Boylar çözülmedi; ayna döşemeler farklı boy alıyor (FLO birleşik konsol açıklıkları +116/+120 ve /Floors Continuous/ şeritleri).
- Ağırlık: kg = Σ adet·boy·(π/4)·ø²·7.85e-3 (0.0061654·ø² kg/m), yuvarlanmamış boyla; GPR boylarıyla 4/4 kat-çap ±0.2 kg. 0.00617·ø² değil.

### A8. YAPI BETON/KALIP KEŞİF ÖZETİ
- Kat satırları: döşeme / kiriş / kolon tablolarının alt toplamları.
  - Döşeme betonu S katı hariç.
  - Kat toplamı ve genel TOPLAM bunların toplamı.
  - Zemin ve normal kat satırları A2–A4 ile tutuyor (kiriş zemin kalıp 139.47'de 0.01 belirsizlik).
- TEMEL 237.67 m³ / 531.36 m² çözülmedi.
  - Radye alanı 347.1 m² (ST4 /Slab foundations/ aks dikdörtgenleri; PL1 −0.4..4.8 × 0.45..15.3 vb.).
  - Kotlar −1.2/−0.8 (t = 0.40).
  - Bağ kirişleri /Tie beams/ (B, H=165, aks, baş, son, kod).
  - Adaylar: kalıp ≈ 30.96 (çevre 77.4·0.4) + 2·1.65·ΣLn_BK (523.6, sapma −1.5 %); beton ≈ 138.84 + ΣB·1.65·Ln + S döşeme betonu (236.6, −0.4 %).

### A9. DUVAR METRAJI — çözülmedi
- Kaynak adayları: kiriş 1. satır 17./18. alan (duvar kalınlığı 19, yük kodu) ve /Walls/ (kat, ?, 85/96, 19, yük, aks, baş, son, kod).
- GPR: zemin 200.34, normal 318.04, sıva 1027.41 / 1246.66.
- Kat ataması kayık görünüyor: zemin kirişleri üzerindeki duvar alanı ΣLn·(3.2−0.5) = 295.9, ΣL·2.7 = 340.2; 318.04 bu ikisinin arasında.
- Sıva − 2·duvar ≈ katın toplam kalıbı (626.73 ↔ 627.02).

### A10. DONATI KEŞİF ÖZETİ (kg)
- Biçim: π formülü, satır = kat × eleman, sütun = çap.
- Boylara bağlı; boylar (A5–A7) çözülmeden üretilemez.
- Not: GPR'de "DONATI KEŞİF METRAJI, ÇİZİMLERDEKİ KESİN METRAJLA %10 FARKLI OLABİLİR."

### A11. KOLON VE PERDE ALAN ORANI — 12/12 sayısal hücre
- Ak = `<proje>.MASS` satır k 1. alan (331.105, 358.120).
- Perde = uzun kenar ≥ 6 × kısa kenar (25×150 dahil); Ac = Σ bx·by (kolon), Ap = Σ (perde).
- % = A/Ak·100, 3 hane; Toplam satırı Σ.
- "(k)" diyafram no = kat no.
- Aynı tablo `.YORUM` dosyasında X/Y perde oranı sütunlarıyla da var (B5).

---

## B. ÖZEL TABLOLAR (s.1318–1338) — ayrıntı `B\RAPOR_B.md`
Sayı biçimi: float32 → G7 → decimal yarım-yukarı (double ile .xx5'te 2 hata).

### B1. KOLON SÜNEKLİLİK ALAN KONTROLÜ — 352/352
- COL Nz +26: Ng=[0], Nq=[1] (Cqa=1).
- Ne = max|[8..11]| + 0.3·(2/3)·Sds·Ng.
- fck = 1.5·SMX+72.
- Perde (uzun ≥ 6·kısa) sınırları 0.35/0.25, kolon 0.40/0.35.
- Sıra: yerel no artan, aynı no'da kat azalan.

### B2. TEMELLERE KOLONLARDAN GELEN SÜNEK TASARIM DEPREM TESİRLERİ (Dust=2.5) — kolon satırları 792/792
- İkinci proje (A_02, F_TIPI, 112_ACIL) %100.
- COL alt uç dizileri ×10 (kN). Fx=+466, Fy=+554, Fz=+26, Mx=+202, My=+378.
- e1 asıl yön, e2 dik, Ezd = 0.3·(2/3)·Sds; g3 = 1.2 (yeni sürüm).
- Kombinasyon kuralları B raporunda. Xg/Yg ST4 aks + kolon kodlarından.
- Bodrumlu projelerde kombinasyon 3'teki Q azaltması kalibre edilmedi.
- Başlık satırlarından toplam temel taban alanı (663.88), Gf+Qf ve ß/ic seçimi çözülmedi.
- Zemin gerilmesi taşıma gücü kontrolü 2592/2601 (E sütunu hariç; E sütunu çözülmedi).

### B3. KİRİŞLERDE DENGELİ DONATI ORANI ve SÜNEK TASARIM — 1761/1792
- Sıra: sürekli kiriş zinciri (64/64).
- d = H − 5.5; KMX As değerleri; ρb = 0.85·k1·fcd/fyd·600/(600+fyd), ρmax = 0.6ρb.
- 30 hücrede ρ−ρ' ±1 son hane, 1 hücre K1-05.

### B4. İMAR YÖNETMELİĞİ KUŞATILMIŞ KOLON KONTROLÜ
- Sabit not + başlık + boş tablo; N_ULK_02'de birebir.
- Satırlı örnek hiçbir GPR'de yok.

### B5. STA4-CAD YAPI YORUMU — 8 projede birebir
- Kaynak `<proje>.YORUM` (1254 metin, analizde yazılır).
- N_ULK_02 37/37, CP_02 50/50, 5176_14 39/39, SB_EMN 40/40, KRK_02 39/39, A_02 37/37, F_TIPI 38/38, 112_ACIL 37/37.
- Kısıt: GPR'den sonra yeniden analiz yapılırsa ayrışır.

N_ULK_02_GEO.GPR yalnız zemin gerilmesi taşıma gücü tablosunu içerir (ana GPR'dekiyle aynı).

---

## C. FEA3D (s.108–1313) — ayrıntı `C\RAPOR_C.md`
- Kaynak: `.Fea3D` + `.FeaMod` + `.FeaVec` + `.ST4`. `.FeaNode`, `.FeaBlk`, `.MAT0/1/2`, `-K1/K2.FEA` gerekmez.
- **Fea3D yerleşimi:**
  - Başlık int[0..5] = mesh, nokta, çubuk, link, eleman sonuç kaydı, kombinasyon; başlık 60 bayt.
  - Mesh 40 bayt (int[2..5] I1..I4, float[8] d m).
  - M = 60 + 40·nm. Nokta x, y, z: M + 32 + 28(n−1); int[3] ≠ 0 ise constraint.
  - L0 = M + 4 + 28(nn+1) + 40(nç+1); link 24 bayt.
  - R1 = L0 + 24(nl+1) + 1516. Nokta sonucu 1512 bayt = 18 blok × 21 float, kombinasyon c → +4c.
  - E0 = R1 + 1512·nn + 948. Eleman kaydı 972 bayt = 48 bayt başlık + 11 blok × 84; dosya sonunda 20 bayt kuyruk.
  - 9 dosyada tutarlı.
- **Sayı biçimi:** float32 → 7 anlamlı hane → ondalığa yarım-yukarı; sütuna sığmayan değer kesilir.
- **İkinci projeler:** VKF_01, CP_03, K_BLG_03 ve FHM_GO_A GPR'lerinde FEA3D bölümü yok. FEA3D bölümü olanlar: H_DVR_te_03 (oncan_mimarlik\hasan_duvarci\statik) ve sd_01/02/03 (projeler2023\hulusi_uzbas\statik_duvar).

| Tablo | Kural | Doğrulama |
|---|---|---|
| C1. Genel bilgiler | Fea3D başlığı; denklem sayısı = 6·nokta·komb.; constraint sayısı; mod sayısı FeaMod int[1] | 5 projede 40/40 |
| C2. Koordinat | STA4 hatalı basıyor: konum c için X = x(n+c+1), Y = Z = x(n) | N_ULK_02 9159/9159; sd_01/02/03 174, 153, 195 |
| C3. Mesh | d = round(f[8]·100); 0 olanlar boş | N_ULK_02 8890/8890, H_DVR 22860/22860, sd_* tam |
| C4. Mod vektörleri | FeaVec [(n−1)·24·nmod + bileşen·4·nmod + mod·4]; ΣM/Σm = max(Mxr, Myr); 9 mod/grup, 82 satır/sayfa | başlık 3564/3564, vektör 82476/82476 |
| C5. Noktaların statik sonuçları | bloklar M11=9, M22=10, M12=11, Q12=6, N11=7, N22=8; 15 kombinasyon satırı | N_ULK_02 206190/206190, H_DVR 260100/260100 |
| C6. Kiriş / bağ kiriş / kolon | h5 = kat \| tip<<16, h6 = ST4 sıra; sütun blokları 10,0,2,8,4,6,1,3,9,5,7 | kiriş 58410, bağ kiriş 29040, kolon 7260; sol sütun 8610/8610; H_DVR tam |
| C7. Nokta deplasmanları | bloklar 0..5 ×1000 (dönmeler mrad) | 206190/206190 |

C2 için öneri: raporda gerçek x, y, z basılsın.

Çözülmeyen:
- Koordinat ve deplasmanda "tüm noktalar / yalnız constraint noktaları" seçimi (sürüm farkı; N_ULK_02 tüm noktalar).
- Nokta blokları 12–17.
- Tip 4 (temel kirişi) tablo başlığı.

---

## Özet tablo

| Bölüm | Kaynak | Doğrulama | Durum |
|---|---|---|---|
| A1 Temel donatı listesi | MATBQF | 420/420 | tam |
| A2 Kiriş kalıp/beton | ST4 geometri | 512/512 (+3/4 alt toplam) | tam |
| A3 Kolon kalıp/beton | ST4 geometri | 352/352, alt toplam 4/4 | tam |
| A4 Döşeme kalıp/beton | ST4 + FLO | 508/513, alt toplam 6/6 | ±0.01 (5 hücre) |
| A5 Kiriş donatı | KMX | etiket/adet/çap 291/291, etriye boyu 64/64 | boylar yok |
| A6 Kolon donatı | SMX | etiket/çap tam, adet kısmen | boylar yok |
| A7 Döşeme donatı | DMX + FLO | etiket/çap 207/207, adet 145/207 | boylar yok |
| A8 Beton/kalıp özeti | A2–A4 | kat satırları tam | TEMEL yok |
| A9 Duvar | — | — | yok |
| A10 Donatı kg özeti | π formülü | — | boylara bağlı |
| A11 Kolon/perde alan oranı | MASS + ST4 | 12/12 | tam |
| B1 Kolon süneklilik | COL + SMX + ST4 | 352/352 | tam |
| B2 Temel sünek tesirleri | COL + ST4 | 792/792 (+3 proje) | başlık kısmen |
| B3 Dengeli donatı | KMX + OPS | 1761/1792 | ±1 son hane |
| B4 Kuşatılmış kolon | sabit metin | sayfa birebir | tam |
| B5 Yapı yorumu | .YORUM | 8 projede tam | tam (analizle eşzamanlı) |
| C1–C7 FEA3D | Fea3D/FeaMod/FeaVec | tüm tablolar %100 (5 proje) | tam |
