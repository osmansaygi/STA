# Bölüm C — FEA3D (N_ULK_02.GPR s.108–1313) GPR'siz üretim

Araç: `ara_metraj\C\tool` (C.exe: `layout`, `head`, `coordv2`, `meshv`, `modv`, `nod`, `elem`, `elab`).
Doğrulama dökümü: `ara_metraj\C\dogrulama.txt`.
Doğrulama projeleri: N_ULK_02 (güncel STA4 14.1, Update 16.10.2026; tüm FEA3D bölümleri basılı),
H_DVR_te_03 (D:\projeler\projeler2026\oncan_mimarlik\hasan_duvarci\statik; 20 kombinasyon, Ocak 2026),
sd_01/sd_02/sd_03 (D:\projeler\projeler2023\hulusi_uzbas\statik_duvar; 2023 sürümü).
VKF_01, CP_03, K_BLG_03, FHM_GO_A GPR'lerinde FEA3D yapı bölümü yok (yalnız radye "SONLU ELEMANLAR"), ama Fea3D dosyalarında yerleşim formülü tutuyor.

**Sonuç: Bölüm C'nin tamamı `.Fea3D` + `.FeaMod` + `.FeaVec` + `.ST4` dosyalarından birebir üretilebiliyor.**
`.FeaNode`, `.FeaBlk`, `.MAT0/1/2`, `-K1/-K2.FEA` gerekmiyor.

## 1. Fea3D dosya yerleşimi (tümü little-endian int32 / float32)

| Bölüm | Ofset | Kayıt |
|---|---|---|
| Başlık | 0 | int: [0] mesh sayısı nm, [1] nokta nn, [2] çubuk nç, [3] link nl, [4] eleman sonuç kaydı ne, [5] kombinasyon nk; 60 bayt |
| Mesh | 60 + 40·(m−1) | 40 B: [0] bayrak, [1] son köşe, [2..5] I1..I4 (I4=0 → üçgen), [6],[7] ?, f[8] kalınlık d (m), [9] ? |
| M | 60 + 40·nm | int (1/2), ardından 28 B boş "nokta 0" |
| Nokta | N1 = M + 32 + 28·(n−1) | 28 B: f x, f y, f z (m), int [3] bağlı yapı düğümü (≠0 ise "constraint"), [4] kat?, [5],[6] |
| Çubuk geometrisi | M + 4 + 28·(nn+1) + 40·k (k=1..nç) | 40 B: I, J, B (cm), D (cm), (kiriş no \| seg<<16), 4 float, int |
| Link | L0 = M + 4 + 28·(nn+1) + 40·(nç+1); kayıt L0 + 24·k | 24 B: [?] [65536…] I J 0 0 (k=0 boş) |
| Nokta sonuçları | R1 = L0 + 24·(nl+1) + 1512 + 4; nokta n: R1 + 1512·(n−1) | 18 blok × 21 float (84 B); blok içinde kombinasyon c (0 tabanlı) +4c |
| Eleman sonuçları | E0 = R1 + 1512·nn + 948; kayıt r: E0 + 972·r (r=0..ne−1); dosya sonunda 20 B | 48 B başlık + 11 blok × 84 B |

Yerleşim N_ULK_02, H_DVR_te_03, K_BLG_03, VKF_01, CP_03, FHM_GO_A, sd_01..03 Fea3D'lerinde tutarlı (her birinde E0 + 972·ne + 20 = dosya boyu).

Nokta sonuç blokları: 0..5 = ux, uy, uz, rx, ry, rz (m, rad); 6 = Q12, 7 = N11, 8 = N22, 9 = M11, 10 = M22, 11 = M12; 12..17 basılmıyor.
Blok içi 21 float: 0..6 düşey düzenler (G+G+G+G, Q+Q+Q+Q, o+Q+o+Q, Q+o+Q+o, Q+Q+o+Q, o+Q+Q+o, o+o+Q+Q), 7 Zemin, 8 +X, 9 −X, 10 +Y, 11 −Y deprem, 12 X-rüzgar, 13 Y-rüzgar, 14 Isı; nk=20'de 15..19 da dolu ama GPR yalnız ilk 15'i basar.

Eleman sonuç başlığı (int): h5 = kat | tip<<16 (tip 1 kiriş, 2 kolon, 5 bağ kirişi, 4 temel kirişi (FHM_GO_A; GPR örneği yok), 0 nokta kaydı — basılmaz),
h6 = ST4 sıra no (1 tabanlı: kiriş /Beams Data/, kolon /Columns Data/, bağ kirişi /Tie beams/; 0 = ST4'te olmayan eleman),
h7 = global segment no (kiriş), h8 = I, h9 = J (perde: 0), h10 = uç bayrağı (1 ilk, 0 ara, 2 son).
Bloklar: 0 Mxi, 1 Mxj, 2 Myi, 3 Myj, 4 Txi, 5 Txj, 6 Tyi, 7 Tyj, 8 Mbi, 9 Mbj, 10 N (t, tm; ölçek 1, işaret aynen).

## 2. Sayı biçimi (tüm FEA3D tabloları)
float32 → 7 anlamlı haneye **yarım-yukarı (sıfırdan uzağa)** → istenen ondalığa yarım-yukarı → "−0" yok → sütun genişliğini aşarsa **sağdan kesilir** (yuvarlanmaz; sd_03: −2393.50171 → "−2393.5", −6627.27 → "−6627.2", −249.8175 mm → "−249.8175000").
.NET "G7" ara metni tek bir eşit-uç durumda yanlış (0.072265625 → …62, STA4 …63); 7 hane adımında da yarım-yukarı şart (N_ULK deplasman 206189 → 206190/206190).

## 3. Bölüm bölüm

### 3.1 YAPININ 3D SONLU ELEMANLARLA ANALİZİ (genel bilgiler)
| Satır | Kaynak |
|---|---|
| Yapı nokta sayısı | Fea3D int[1] |
| Yapı constraint sayısı | nokta kaydı int[3] ≠ 0 olan nokta sayısı |
| Yapı mesh sayısı | Fea3D int[0] |
| Çubuk sonlu eleman sayısı | Fea3D int[2] (= kiriş segment sayısı) |
| Link eleman sayısı | Fea3D int[3] |
| Yük kombinasyon sayısı | Fea3D int[5] |
| Denklem sayısı | 6 · nokta · kombinasyon (STA4 böyle basıyor) |
| Mod sayısı | FeaMod int[1] |
| Beton poisson oranı | her zaman boş |
| Plak birim mesh genişliği | her zaman "1.00" (5 GPR'de sabit) |
0 olan sayılar boş basılır. Biçim: etiket + ":" + sağa dayalı 9 hane.
Doğrulama: 5 proje × 8 değer = 40/40 (N_ULK_02, H_DVR_te_03, sd_01, sd_02, sd_03).

### 3.2 KOORDİNAT BİLGİLERİ
Kaynak: Fea3D nokta tablosu (N1). Satırda 3 nokta, hücre: Nokta, X, Y, Z (F3, genişlik 8).
**STA4 bu tabloyu hatalı basıyor** ve GPR'de görülen kural kesin olarak şu: basılı sıradaki konum c (0,1,2) için
`X = x(n + c + 1)`, `Y = Z = x(n)` (gerçek y, z hiç basılmıyor). `n + c + 1 > nokta sayısı` ise hücre boş kalır (N_ULK: 2289 ve 2291 boş).
Doğrulama: N_ULK_02 9159/9159 (2289 nokta × 3 değer + 2292 hücre konumu), sd_01 174/174, sd_02 153/153, sd_03 195/195.
Öneri: Raporumuzda gerçek (x, y, z) basılmalı (N1+0/+4/+8); STA4 ile birebir istenirse yukarıdaki hata kuralı uygulanır.
Hangi noktalar: N_ULK_02 (Update 16.10.2026) tüm noktalar; 2023 sürümü (sd_*) ve H_DVR (Ocak 2026) yalnız constraint noktaları (H_DVR'de 0 → tablo boş).
Bu seçim dosyalarda bulunamadı (IDX yalnız içindekiler); sürüm farkı olduğu düşünülüyor.

### 3.3 MESH BİLGİLERİ
Kaynak: Fea3D mesh kayıtları. Satırda 3 mesh: no, d (cm) = round(f[8]·100) (0 → boş), I1..I4 = int[2..5] (0 → boş).
Doğrulama: N_ULK_02 8890/8890 (1778 × 5), H_DVR_te_03 22860/22860, sd_01 685/685, sd_02 600/600, sd_03 925/925.

### 3.4 MOD VEKTÖRLERİ
Kaynak: FeaVec f32 `[(n−1)·24·nmod + bileşen·4·nmod + mod·4]` (nmod = FeaMod int[1]; N_ULK'ta 144 = 24·6; H_DVR 1387200 = 2890·6·20·4).
Başlık: T = FeaMod double [20+8i] (F3), ω = double [20+8(nmod+1)+8i] (F3), "ΣM/Σm" = **max(Mxr, Myr)** (FeaMod son 8·nmod bayt float çiftleri; toplam değil — H_DVR mod 1: 0.399/0.172 → 0.399).
Tablo: 9 mod/sütun grubu, başlık "a-b MOD VEKTÖRLERİ" (N_ULK 6 mod: "1-6"; H_DVR 20: "1-9", "10-18", "19-20"); her sayfada başlık (Mod no, T, ω, ΣM/Σm) tekrar. Satır: "n ëx|ëy|ëz|éx|éy|éz", F7 genişlik 10; düğüm blokları sayfa arasında bölünebilir; sayfada 82 satır (veri + ayraç).
Doğrulama: N_ULK_02 başlık 3564/3564, vektör 82476/82476 (2291 düğüm); H_DVR başlık 60/60.
Not: eski sürümler vektörü basamıyor (sd_03 GPR'de değerler 0.0000000, dosyada ~1; H_DVR'de tek "0" düğüm bloğu, sıfırlar) — güncel sürüm dosyayla birebir.

### 3.5 NOKTALARIN STATİK SONUÇLARI
Kaynak: nokta sonuç kaydı; M11 = blok 9, M22 = 10, M12 = 11, Q12 = 6, N11 = 7, N22 = 8 (ölçek 1; tm/m, t/m). F3, genişlik 10.
Satır: her nokta için sabit 15 etiket (G+G+G+G … Isi etkisi) = blok içi kombinasyon 0..14; nokta blokları arasında ayraç; tüm noktalar (1..nn); 84 satır/sayfa (78 veri + 6 ayraç), başlık her sayfada.
Doğrulama: N_ULK_02 206190/206190, H_DVR_te_03 260100/260100 (nk=20, yalnız ilk 15 basılı), sd_01 17100/17100, sd_02 15840/15840, sd_03 21510/21510.

### 3.6 KİRİŞ / BAĞ KİRİŞ / KOLON STATİK SONUÇLARI (t, tm)
Kaynak: Fea3D eleman sonuç kayıtları (E0). Tablolar: tip 1 → "KİRİŞ STATİK SONUÇLARI", tip 5 → "BAĞ KIRIS STATİK SONUÇLARI", tip 2 → "KOLON STATİK SONUÇLARI"; her tabloda dosya sırası (kolonda perdeler dosyada sonda, GPR'de de sonda).
Her segment 15 satır (Kom. 1..15 = blok içi kombinasyon 0..14), sütunlar N, Mxi, Myi, Mbi, Txi, Tyi, Mxj, Myj, Mbj, Txj, Tyj = blok 10, 0, 2, 8, 4, 6, 1, 3, 9, 5, 7; F2, sütun genişlikleri 8,8,8,7,7,7,8,8,7,7,7 (taşarsa kesme).
Sol sütun: satır 1 ad, satır 4 "I=%7d", 5 "J=%7d" (0 → boş), 8 "B=%7d", 9 "D=%7d", 11 "G=%7.3f" (yalnız kiriş).
- Kiriş: ad/B/D = ST4 /Beams Data/ (h6−1) adı, b, h; G = aynı satırın 4. alanı / 100 (KZ-03: 31 → 0.310, KZ-01: 70 → 0.700). h6=0 → "K000", B/D boş, G=0.000.
- Bağ kirişi: ad ve B, D = ST4 /Tie beams/ (ad satırı + "B,D,…" satırı), G yok.
- Kolon: ad/B/D = ST4 /Columns Data/ (h6−1) Bx, By; perde I/J boş; h6=0 → "S000", B/D boş.
Doğrulama (değer): N_ULK_02 kiriş 58410/58410 (354 segment), bağ kirişi 29040/29040 (176), kolon 7260/7260 (44); H_DVR_te_03 kiriş 81675/81675 (495), kolon 17490/17490 (106); sd_01 1815/1815, sd_02 1980/1980, sd_03 3465/3465.
Doğrulama (sol sütun 15 satır × segment): N_ULK_02 8610/8610, H_DVR_te_03 9015/9015 (sd_* 2023 sürümü ad biçimi "K101", güncel "K1-01").

### 3.7 NOKTA DEPLASMANLARI
Kaynak: nokta sonuç blokları 0..5 × 1000 (ëx, ëy, ëz "mm"; éx, éy, éz başlıkta "radyan" ama değer de ×1000, yani mrad). F8, genişlik 12 (taşarsa kesme). Satır düzeni NOKTALARIN STATİK ile aynı.
Doğrulama: N_ULK_02 206190/206190 (2291 nokta), sd_01 3960/3960, sd_02 3510/3510, sd_03 4500/4500.
Nokta seçimi koordinat tablosundaki gibi: N_ULK_02 tüm noktalar; sd_* yalnız constraint noktaları (44/39/50 = constraint sayısı); H_DVR boş (constraint 0).

## 4. Üretilemeyen / belirsiz
- Koordinat ve deplasman tablosunda "tüm noktalar mı, constraint noktaları mı" seçimi dosyalarda bulunamadı; güncel sürüm (N_ULK_02) tüm noktaları basıyor.
- Nokta sonuç blokları 12..17 ve eleman/geometri kayıtlarındaki bazı float alanları (ör. çubuk kaydındaki 4 float) GPR'de basılmıyor, çözülmedi (gerek yok).
- Tip 4 (temel kirişi, FHM_GO_A) eleman kayıtları var; GPR örneği olmadığından tablo başlığı doğrulanamadı.
- FeaNode: yapı düğümü kimlik/koordinat listesi (29 B paketli kayıt, id ≥ 67…); nokta kaydı int[3] buraya bağlanıyor; GPR için gerekli değil.
